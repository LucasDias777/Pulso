using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulso
{
    // De onde veio o uso: peso por minuto e por pasta de projeto (custo em dólar de API no Claude, tokens
    // ponderados no Codex). Só conta o uso deste computador; o gasto em outro lugar não aparece aqui.
    class Projetos
    {
        public static readonly Projetos Claude = new Projetos(), Codex = new Projetos();
        public static Projetos De(string id) { return id == "claude" ? Claude : id == "codex" ? Codex : null; }

        readonly Dictionary<string, Dictionary<long, double>> porProjeto = new Dictionary<string, Dictionary<long, double>>(StringComparer.OrdinalIgnoreCase);
        readonly object trava = new object();

        public static string Nome(string pasta)
        {
            if (string.IsNullOrEmpty(pasta)) return "Sem pasta";
            string n = Path.GetFileName(pasta.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? pasta : n;
        }

        public void Somar(DateTime quando, string pasta, double peso)
        {
            if (peso <= 0) return;
            string nome = Nome(pasta);
            long min = Tempo.UnixMs(quando) / 60000;
            lock (trava)
            {
                Dictionary<long, double> d;
                if (!porProjeto.TryGetValue(nome, out d)) porProjeto[nome] = d = new Dictionary<long, double>();
                double v;
                d.TryGetValue(min, out v);
                d[min] = v + peso;
            }
        }

        // Fração de cada projeto no uso desde um instante, da maior para a menor
        public List<KeyValuePair<string, double>> Parcelas(DateTime desdeUtc)
        {
            long ini = Tempo.UnixMs(desdeUtc) / 60000;
            var r = new List<KeyValuePair<string, double>>();
            double total = 0;
            lock (trava)
                foreach (var kv in porProjeto)
                {
                    double soma = 0;
                    foreach (var m in kv.Value) if (m.Key >= ini) soma += m.Value;
                    if (soma <= 0) continue;
                    r.Add(new KeyValuePair<string, double>(kv.Key, soma));
                    total += soma;
                }
            return r.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value / total)).OrderByDescending(kv => kv.Value).ToList();
        }

        public void Limpar() { lock (trava) porProjeto.Clear(); }

        public Dictionary<string, object> ParaJson(DateTime corteUtc)
        {
            long corte = Tempo.UnixMs(corteUtc) / 60000;
            var o = new Dictionary<string, object>();
            lock (trava)
            {
                foreach (var nome in porProjeto.Keys.ToList())
                {
                    var d = porProjeto[nome];
                    foreach (var k in d.Keys.Where(k => k < corte).ToList()) d.Remove(k);
                    if (d.Count == 0) { porProjeto.Remove(nome); continue; }
                    var m = new Dictionary<string, object>();
                    foreach (var kv in d) m[kv.Key.ToString()] = Math.Round(kv.Value, 6);
                    o[nome] = m;
                }
            }
            return o;
        }

        public void DeJson(IDictionary<string, object> o, DateTime corteUtc)
        {
            if (o == null) return;
            long corte = Tempo.UnixMs(corteUtc) / 60000;
            lock (trava)
                foreach (var kv in o)
                {
                    var m = kv.Value as IDictionary<string, object>;
                    if (m == null) continue;
                    var d = new Dictionary<long, double>();
                    foreach (var p in m)
                    {
                        long min; double? v = Json.Num(m, p.Key);
                        if (long.TryParse(p.Key, out min) && min >= corte && v.HasValue) d[min] = v.Value;
                    }
                    if (d.Count > 0) porProjeto[kv.Key] = d;
                }
        }
    }
}
