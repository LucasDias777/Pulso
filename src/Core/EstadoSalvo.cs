using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        // Resumo (SHA-256) da conta e do plano do Claude Code donos das leituras e da calibração
        public static string ClaudeConta;
        public static string ClaudePlano;   // "pro", "max_5x", "max_20x"; nulo fora deles (Team, Enterprise…)
        // O k depende do plano, não da conta: a calibração de cada plano (ou da conta, se o plano é desconhecido) fica
        // guardada e volta quando ele entra de novo
        static readonly Dictionary<string, Dictionary<string, double>> calibracaoGuardada = new Dictionary<string, Dictionary<string, double>>();
        // Janelas cujo k ainda é o ponto de partida do plano, não uma medida (sob a trava de Calibracao)
        public static readonly HashSet<string> Provisoria = new HashSet<string>();
        // Ponto de partida do k até a primeira calibração da janela, medido neste PC pelo log: Max 5x em 14–22/10 (505 pontos
        // com US$907 na sessão, 68 com US$898 na semana; semana do Fable em 15/10) e Pro em 20 e 23/10 (92 pontos com US$39,89
        // na sessão; a semana pela mesma proporção, 4,1x a do Max 5x). O Pro deu 4,1x e não os 5x oficiais porque o medidor
        // da Anthropic pesa a escrita de cache ~2,4x o preço (no Max o k sobe de 0,0048 a 0,0064 com a escrita de 13% a 40%)
        // e as horas do Pro tiveram menos escrita (11% contra 25%); descontado isso, Pro/Max 5x = 4,9x. Max 20x sem medida:
        // Max 5x ÷ 4 (0,00139) e Pro ÷ 20 corrigido para o uso do Max (0,00135) concordam
        static readonly Dictionary<string, Dictionary<string, double>> PartidaPorPlano = new Dictionary<string, Dictionary<string, double>>
        {
            { "pro", new Dictionary<string, double> { { "session", 0.0231 }, { "weekly_all", 0.0031 } } },
            { "max_5x", new Dictionary<string, double> { { "session", 0.0056 }, { "weekly_all", 0.00076 }, { "weekly_scoped", 0.0035 } } },
            { "max_20x", new Dictionary<string, double> { { "session", 0.00137 } } },
        };
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
                ClaudeConta = Json.Str(o, "claudeConta");
                ClaudePlano = Json.Str(o, "claudePlano");
                var guardada = Json.Obj(o, "calibracaoGuardada");
                if (guardada != null)
                    foreach (var plano in guardada)
                    {
                        var k = new Dictionary<string, double>();
                        var c = plano.Value as IDictionary<string, object>;
                        if (c != null)
                            foreach (var kv in c)
                            {
                                double? v = Json.Num(c, kv.Key);
                                if (v.HasValue && v.Value > 0) k[kv.Key] = v.Value;
                            }
                        calibracaoGuardada[plano.Key] = k;
                    }
                foreach (var id in Json.Arr(o, "calibracaoProvisoria") ?? new object[0])
                    if (id is string && Calibracao.ContainsKey((string)id)) Provisoria.Add((string)id);
                lock (Estado.Trava)
                {
                    foreach (var p in Estado.Todos) Restaurar(p, Json.Obj(o, p.Id));
                }
            }
            catch (Exception e) { Log.Erro("ler estado", e); }
        }

        // Anota a conta e o plano do Claude Code; true = a conta mudou. A calibração medida do plano que saiu fica guardada e
        // a do que entrou volta; janela sem medida parte do valor do plano (provisório). Sem conta anotada (versão anterior)
        // não se sabe de quem é a calibração salva: é descartada
        public static bool TrocarConta(string conta, string plano)
        {
            lock (Calibracao)
            {
                bool mudou = conta != ClaudeConta;
                if (mudou)
                {
                    if (ClaudeConta != null)
                        calibracaoGuardada[ClaudePlano ?? ClaudeConta] = Calibracao.Where(kv => !Provisoria.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                    Calibracao.Clear();
                    Provisoria.Clear();
                    Dictionary<string, double> k;
                    if (calibracaoGuardada.TryGetValue(plano ?? conta, out k))
                    {
                        foreach (var kv in k) Calibracao[kv.Key] = kv.Value;
                        calibracaoGuardada.Remove(plano ?? conta);
                    }
                    ClaudeConta = conta;
                }
                ClaudePlano = plano;
                Dictionary<string, double> partida;
                if (plano != null && PartidaPorPlano.TryGetValue(plano, out partida))
                    foreach (var kv in partida)
                        if (!Calibracao.ContainsKey(kv.Key)) { Calibracao[kv.Key] = kv.Value; Provisoria.Add(kv.Key); }
                return mudou;
            }
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
                var guardada = new Dictionary<string, object>();
                List<string> provisoria;
                string conta, plano;
                lock (Calibracao)
                {
                    foreach (var kv in Calibracao) cal[kv.Key] = kv.Value;
                    foreach (var kv in calibracaoGuardada) guardada[kv.Key] = kv.Value.ToDictionary(x => x.Key, x => (object)x.Value);
                    provisoria = Provisoria.ToList();
                    conta = ClaudeConta;
                    plano = ClaudePlano;
                }
                var o = new Dictionary<string, object>
                {
                    { "claudeEsperaAte", espera > DateTime.UtcNow ? (object)Tempo.UnixMs(espera) : null },
                    { "calibracao", cal },
                    { "calibracaoVersao", VersaoCalibracao },
                    { "calibracaoProvisoria", provisoria },
                    { "claudeConta", conta },
                    { "claudePlano", plano },
                    { "calibracaoGuardada", guardada },
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
