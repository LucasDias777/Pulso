using System;
using System.Collections.Generic;
using System.IO;

namespace Pulso
{
    // %APPDATA%\Pulso\estado.json: últimas leituras (o notch abre já com números), calibração da estimativa
    // e a espera pedida pelo servidor (sobrevive a reinício). Só números e datas; nenhuma credencial.
    static class EstadoSalvo
    {
        public static DateTime ClaudeEsperaAte = DateTime.MinValue;
        public static readonly Dictionary<string, double> Calibracao = new Dictionary<string, double>();
        // 2: calibração entre leituras exatas. A de antes (desde o início da janela) pode ter sido inflada por uso
        // feito em outro computador, então é descartada e refeita nas próximas leituras
        const int VersaoCalibracao = 2;
        static readonly object trava = new object();
        static DateTime ultimaGravacao = DateTime.MinValue;

        public static Func<DateTime> EsperaClaude; // fornecido pela fonte do Claude

        public static void Carregar()
        {
            try
            {
                if (!File.Exists(Caminhos.Estado)) return;
                var o = Json.Parse(File.ReadAllText(Caminhos.Estado));
                ClaudeEsperaAte = Json.Data(o, "claudeEsperaAte") ?? DateTime.MinValue;
                var cal = Json.Obj(o, "calibracao");
                if (cal != null && (Json.Num(o, "calibracaoVersao") ?? 1) >= VersaoCalibracao)
                    foreach (var kv in cal)
                    {
                        double? v = Json.Num(cal, kv.Key);
                        if (v.HasValue && v.Value > 0) Calibracao[kv.Key] = v.Value;
                    }
                lock (Estado.Trava)
                {
                    foreach (var p in Estado.Todos) Restaurar(p, Json.Obj(o, p.Id));
                }
            }
            catch (Exception e) { Log.Erro("ler estado", e); }
        }

        static void Restaurar(Provedor p, IDictionary<string, object> o)
        {
            if (o == null) return;
            p.Confirmado = Json.Data(o, "confirmado");
            p.Fonte = Json.Str(o, "fonte");
            p.Plano = Json.Str(o, "plano");
            var js = Json.Arr(o, "janelas");
            if (js == null) return;
            p.Janelas.Clear();
            foreach (var j in js)
            {
                p.Janelas.Add(new Janela
                {
                    Id = Json.Str(j, "id"),
                    Rotulo = Json.Str(j, "rotulo"),
                    Usado = Json.Num(j, "usado") ?? 0,
                    ResetaEm = Json.Data(j, "resetaEm"),
                    Minutos = (int)(Json.Num(j, "minutos") ?? 0),
                    Semanal = Json.Bool(j, "semanal") == true,
                });
            }
        }

        static Dictionary<string, object> Serializar(Provedor p)
        {
            var js = new List<object>();
            foreach (var j in p.Janelas)
                js.Add(new Dictionary<string, object>
                {
                    { "id", j.Id }, { "rotulo", j.Rotulo }, { "usado", j.Usado },
                    { "resetaEm", j.ResetaEm.HasValue ? (object)Tempo.UnixMs(j.ResetaEm.Value) : null },
                    { "minutos", j.Minutos }, { "semanal", j.Semanal },
                });
            return new Dictionary<string, object>
            {
                { "confirmado", p.Confirmado.HasValue ? (object)Tempo.UnixMs(p.Confirmado.Value) : null },
                { "fonte", p.Fonte }, { "plano", p.Plano }, { "janelas", js },
            };
        }

        // Grava no máximo a cada 5 s (as leituras do Codex podem chegar várias vezes por minuto)
        public static void Gravar(bool forcar = false)
        {
            lock (trava)
            {
                if (!forcar && (DateTime.UtcNow - ultimaGravacao).TotalSeconds < 5) return;
                ultimaGravacao = DateTime.UtcNow;
            }
            try
            {
                var espera = EsperaClaude != null ? EsperaClaude() : ClaudeEsperaAte;
                var cal = new Dictionary<string, object>();
                lock (Calibracao) foreach (var kv in Calibracao) cal[kv.Key] = kv.Value;
                var o = new Dictionary<string, object>
                {
                    { "claudeEsperaAte", espera > DateTime.UtcNow ? (object)Tempo.UnixMs(espera) : null },
                    { "calibracao", cal },
                    { "calibracaoVersao", VersaoCalibracao },
                };
                lock (Estado.Trava)
                    foreach (var p in Estado.Todos)
                        if (p.Janelas.Count > 0 || p.Confirmado.HasValue) o[p.Id] = Serializar(p);
                Caminhos.GravarAtomico(Caminhos.Estado, Json.Write(o));
            }
            catch (Exception e) { Log.Erro("gravar estado", e); }
        }
    }
}
