using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace Pulso
{
    // JSON sem dependência externa: o JavaScriptSerializer devolve Dictionary/object[]/números/strings.
    static class Json
    {
        static JavaScriptSerializer Novo()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        }

        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Novo().DeserializeObject(s); }
            catch { return null; }
        }

        public static object Get(object o, params string[] caminho)
        {
            foreach (string k in caminho)
            {
                var d = o as IDictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }

        public static IDictionary<string, object> Obj(object o, params string[] caminho)
        {
            return Get(o, caminho) as IDictionary<string, object>;
        }

        public static object[] Arr(object o, params string[] caminho)
        {
            var v = Get(o, caminho);
            var a = v as object[];
            if (a != null) return a;
            var l = v as ArrayList;
            return l != null ? l.ToArray() : null;
        }

        public static string Str(object o, params string[] caminho)
        {
            var v = Get(o, caminho);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double? Num(object o, params string[] caminho)
        {
            var v = Get(o, caminho);
            if (v == null || v is string || v is bool) return null;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        public static bool? Bool(object o, params string[] caminho)
        {
            var v = Get(o, caminho);
            return v is bool ? (bool?)(bool)v : null;
        }

        // Serialização indentada, para os arquivos que a pessoa pode abrir e ler.
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            Escrever(sb, o, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Escrever(StringBuilder sb, object o, int nivel)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { sb.Append(Novo().Serialize(o)); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is double || o is float || o is decimal)
            {
                double d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (o is int || o is long || o is short || o is uint || o is ulong)
            {
                sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
                return;
            }
            string pad = new string(' ', (nivel + 1) * 2), fim = new string(' ', nivel * 2);
            var dic = o as IDictionary<string, object>;
            if (dic != null)
            {
                if (dic.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in dic)
                {
                    sb.Append(pad).Append(Novo().Serialize(kv.Key)).Append(": ");
                    Escrever(sb, kv.Value, nivel + 1);
                    sb.Append(++i < dic.Count ? ",\n" : "\n");
                }
                sb.Append(fim).Append('}');
                return;
            }
            var lista = o as IEnumerable;
            if (lista != null)
            {
                var itens = new List<object>();
                foreach (var x in lista) itens.Add(x);
                if (itens.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < itens.Count; i++)
                {
                    sb.Append(pad);
                    Escrever(sb, itens[i], nivel + 1);
                    sb.Append(i + 1 < itens.Count ? ",\n" : "\n");
                }
                sb.Append(fim).Append(']');
                return;
            }
            sb.Append(Novo().Serialize(o));
        }

        public static DateTime? Data(object o, params string[] caminho)
        {
            var v = Get(o, caminho);
            if (v == null) return null;
            if (v is string)
            {
                DateTime d;
                if (DateTime.TryParse((string)v, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
                return null;
            }
            double? n = Num(o, caminho);
            if (n == null || n.Value <= 0) return null;
            // Segundos ou milissegundos desde a época Unix
            double ms = n.Value < 1e11 ? n.Value * 1000 : n.Value;
            return Tempo.DeUnixMs((long)ms);
        }
    }

    static class Tempo
    {
        static readonly DateTime Epoca = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static DateTime DeUnixMs(long ms) { return Epoca.AddMilliseconds(ms); }
        public static long UnixMs(DateTime d) { return (long)(d.ToUniversalTime() - Epoca).TotalMilliseconds; }
        public static long AgoraMs() { return UnixMs(DateTime.UtcNow); }

        // "2h 13min", "4 dias 3h", "38 s"
        public static string Duracao(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + " s";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min";
            if (t.TotalHours < 24)
            {
                int m = t.Minutes;
                return (int)t.TotalHours + "h" + (m > 0 ? " " + m.ToString("00") + "min" : "");
            }
            int dias = (int)t.TotalDays, h = t.Hours;
            return (dias == 1 ? "{0} dia" : "{0} dias").T(dias) + (h > 0 ? " " + h + "h" : "");
        }

        public static string Ha(DateTime utc)
        {
            var t = DateTime.UtcNow - utc;
            if (t.TotalSeconds < 10) return "agora".T();
            return "há {0}".T(Duracao(t));
        }
    }
}
