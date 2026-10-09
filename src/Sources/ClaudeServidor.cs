using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Pulso
{
    // Leitura exata do Claude: GET api.anthropic.com/api/oauth/usage com o token que o próprio Claude Code mantém.
    // O token só é lido do arquivo, em memória, na hora da consulta — nunca gravado nem enviado a outro lugar.
    // Consulta logo depois que um turno termina (o consumo acabou de mudar), respeita o
    // Retry-After (inclusive em formato de data) e guarda a espera em disco, para não estourar o limite ao reiniciar.
    class ClaudeServidor : IDisposable
    {
        const string Url = "https://api.anthropic.com/api/oauth/usage";
        static readonly TimeSpan EspacoMinimo = TimeSpan.FromSeconds(120);
        static readonly TimeSpan IntervaloAtivo = TimeSpan.FromMinutes(5);
        static readonly TimeSpan IntervaloOcioso = TimeSpan.FromMinutes(15);

        readonly HttpClient http;
        readonly Func<bool> haSessaoAtiva;
        Timer relogio;
        FileSystemWatcher vigiaCredencial;
        int consultando;
        int falhas429;
        DateTime ultimaConsulta = DateTime.MinValue;   // UTC de envio
        DateTime pedidaPara = DateTime.MaxValue;        // consulta agendada (fim de turno, clique)
        DateTime esperaAte = DateTime.MinValue;         // backoff por 429/erro

        public event Action<Dictionary<string, Janela>> LeituraExata; // janelas por id

        public ClaudeServidor(Func<bool> haSessaoAtiva)
        {
            this.haSessaoAtiva = haSessaoAtiva;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // + TLS 1.3
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulso/1.0 (Windows)");
        }

        public void Iniciar(DateTime esperaPersistida, DateTime? ultimaLeituraSalva)
        {
            esperaAte = esperaPersistida;
            // Reabrir o app não deve gerar consulta se a leitura salva é recente (evita 429 a cada reinício)
            if (ultimaLeituraSalva.HasValue && ultimaLeituraSalva.Value <= DateTime.UtcNow) ultimaConsulta = ultimaLeituraSalva.Value;
            relogio = new Timer(delegate { Tique(); }, null, 1500, 5000);
            try
            {
                if (Directory.Exists(Caminhos.ClaudeDir))
                {
                    // O Claude Code renovou o token: boa hora para uma leitura (se a última foi bloqueada por token vencido)
                    vigiaCredencial = new FileSystemWatcher(Caminhos.ClaudeDir, ".credentials.json");
                    vigiaCredencial.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                    FileSystemEventHandler h = delegate { if (tokenVencido) Pedir(TimeSpan.FromSeconds(2)); };
                    vigiaCredencial.Changed += h;
                    vigiaCredencial.Created += h;
                    vigiaCredencial.EnableRaisingEvents = true;
                }
            }
            catch (Exception e) { Log.Erro("vigiar credencial", e); }
        }

        bool tokenVencido;

        public DateTime EsperaAte { get { return esperaAte; } }

        // Pede uma leitura daqui a 'atraso', respeitando o espaço mínimo entre consultas
        public void Pedir(TimeSpan atraso)
        {
            var quando = DateTime.UtcNow + atraso;
            lock (this) if (quando < pedidaPara) pedidaPara = quando;
        }

        void Tique()
        {
            var agora = DateTime.UtcNow;
            if (agora < esperaAte) return;
            bool devida;
            lock (this)
            {
                var proxima = ultimaConsulta + (haSessaoAtiva() ? IntervaloAtivo : IntervaloOcioso);
                if (pedidaPara < proxima) proxima = pedidaPara;
                var minimo = ultimaConsulta + EspacoMinimo;
                if (proxima < minimo) proxima = minimo;
                devida = agora >= proxima;
            }
            if (devida) Consultar();
        }

        void Consultar()
        {
            if (Interlocked.Exchange(ref consultando, 1) == 1) return;
            Task.Factory.StartNew(delegate
            {
                try { ConsultarAgora(); }
                catch (Exception e) { Log.Erro("claude: consulta", e); Nota("Sem resposta do servidor ({0})".T(e.GetType().Name), true); }
                finally { consultando = 0; }
            });
        }

        void ConsultarAgora()
        {
            lock (this) { ultimaConsulta = DateTime.UtcNow; pedidaPara = DateTime.MaxValue; }

            string token; DateTime? expira; string plano;
            if (!LerCredencial(out token, out expira, out plano))
            {
                Nota("Entre no Claude Code para ver o consumo exato", true);
                return;
            }
            if (plano != null) lock (Estado.Trava) Estado.Claude.Plano = Rotulos.Plano(plano);
            // Token vencido recebe 429 com espera de ~1 h; melhor aguardar o Claude Code renová-lo
            if (expira.HasValue && expira.Value <= DateTime.UtcNow.AddSeconds(30))
            {
                tokenVencido = true;
                Nota("Credencial vencida — o Claude Code a renova no próximo uso", true);
                return;
            }
            tokenVencido = false;

            var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.Accept.ParseAdd("application/json");
            token = null;

            using (var resp = http.SendAsync(req).Result)
            {
                int codigo = (int)resp.StatusCode;
                if (codigo == 429)
                {
                    falhas429++;
                    var espera = TimeSpan.FromSeconds(Math.Min(900, 60 * Math.Pow(2, Math.Min(falhas429 - 1, 4))));
                    var ra = RetryAfter(resp);
                    if (ra.HasValue && ra.Value > espera) espera = ra.Value;
                    esperaAte = DateTime.UtcNow + espera;
                    Log.Info("claude: 429, aguardando " + (int)espera.TotalSeconds + " s");
                    Nota("Servidor pediu pausa; leitura exata em {0}".T(Tempo.Duracao(espera)), false);
                    Persistir();
                    return;
                }
                if (codigo == 401)
                {
                    Nota("Credencial recusada — entre de novo no Claude Code", true);
                    esperaAte = DateTime.UtcNow.AddMinutes(2);
                    return;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    esperaAte = DateTime.UtcNow.AddSeconds(90);
                    Nota("Servidor respondeu {0}".T(codigo), false);
                    return;
                }
                falhas429 = 0;
                if (esperaAte != DateTime.MinValue) { esperaAte = DateTime.MinValue; Persistir(); }
                string corpo = resp.Content.ReadAsStringAsync().Result;
                var janelas = Interpretar(Json.Parse(corpo));
                if (janelas.Count == 0) { Nota("Resposta sem janelas de limite", false); return; }
                var h = LeituraExata;
                if (h != null) h(janelas);
            }
        }

        static TimeSpan? RetryAfter(HttpResponseMessage r)
        {
            var ra = r.Headers.RetryAfter;
            if (ra == null) return null;
            if (ra.Delta.HasValue) return ra.Delta.Value;
            if (ra.Date.HasValue) return ra.Date.Value.UtcDateTime - DateTime.UtcNow;
            return null;
        }

        // Formato atual: limits[{kind, percent, resets_at, scope}] + five_hour/seven_day{utilization, resets_at}
        public static Dictionary<string, Janela> Interpretar(object o)
        {
            var r = new Dictionary<string, Janela>();
            var limites = Json.Arr(o, "limits");
            if (limites != null)
            {
                foreach (var l in limites)
                {
                    string tipo = Json.Str(l, "kind");
                    double? pct = Json.Num(l, "percent") ?? Json.Num(l, "utilization");
                    if (tipo == null || !pct.HasValue) continue;
                    string id = Normalizar(tipo);
                    var j = Nova(id, pct.Value, Json.Data(l, "resets_at"));
                    string modelo = Json.Str(l, "scope", "model", "displayName");
                    if (modelo != null && id != "session" && id != "weekly_all") j.Rotulo = "Semana ({0})".T(modelo);
                    if (!r.ContainsKey(id)) r[id] = j;
                }
            }
            Complementar(r, o, "five_hour", "session");
            Complementar(r, o, "seven_day", "weekly_all");
            Complementar(r, o, "seven_day_opus", "weekly_opus");
            Complementar(r, o, "seven_day_sonnet", "weekly_sonnet");
            return r;
        }

        static void Complementar(Dictionary<string, Janela> r, object o, string chave, string id)
        {
            if (r.ContainsKey(id)) return;
            var w = Json.Obj(o, chave);
            double? u = Json.Num(w, "utilization");
            if (w == null || !u.HasValue) return;
            r[id] = Nova(id, u.Value, Json.Data(w, "resets_at"));
        }

        static string Normalizar(string tipo)
        {
            switch (tipo)
            {
                case "five_hour": return "session";
                case "seven_day": case "weekly": return "weekly_all";
                case "seven_day_opus": return "weekly_opus";
                case "seven_day_sonnet": return "weekly_sonnet";
                default: return tipo;
            }
        }

        static Janela Nova(string id, double pct, DateTime? reset)
        {
            var j = new Janela { Id = id, Usado = Math.Max(0, Math.Min(1, pct / 100.0)), ResetaEm = reset };
            switch (id)
            {
                case "session": j.Rotulo = "Sessão (5h)"; j.Minutos = 300; break;
                case "weekly_all": j.Rotulo = "Semana"; j.Minutos = 10080; j.Semanal = true; break;
                case "weekly_opus": j.Rotulo = "Semana (Opus)"; j.Minutos = 10080; j.Semanal = true; break;
                case "weekly_sonnet": j.Rotulo = "Semana (Sonnet)"; j.Minutos = 10080; j.Semanal = true; break;
                case "weekly_scoped": j.Rotulo = "Semana (por modelo)"; j.Minutos = 10080; j.Semanal = true; break;
                default:
                    j.Rotulo = char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' ');
                    j.Semanal = id.StartsWith("week") || id.StartsWith("seven");
                    break;
            }
            return j;
        }

        // Só lê; nunca grava nem renova (renovar trocaria o token de que o Claude Code depende)
        static bool LerCredencial(out string token, out DateTime? expira, out string plano)
        {
            token = null; expira = null; plano = null;
            foreach (var arq in new[] { Caminhos.ClaudeCredenciais, Path.Combine(Caminhos.ClaudeDir, "credentials.json") })
            {
                if (!File.Exists(arq)) continue;
                var o = Json.Parse(Caminhos.LerCompartilhado(arq));
                var c = Json.Obj(o, "claudeAiOauth") ?? (o as IDictionary<string, object>);
                token = Json.Str(c, "accessToken");
                if (string.IsNullOrEmpty(token)) continue;
                expira = Json.Data(c, "expiresAt");
                plano = Json.Str(c, "subscriptionType");
                return true;
            }
            return false;
        }

        void Nota(string texto, bool erro)
        {
            lock (Estado.Trava)
            {
                Estado.Claude.Nota = texto;
                if (erro) Estado.Claude.Erro = texto;
            }
            Estado.Avisar();
        }

        void Persistir() { EstadoSalvo.Gravar(true); }

        public void Dispose()
        {
            if (relogio != null) relogio.Dispose();
            if (vigiaCredencial != null) vigiaCredencial.Dispose();
            http.Dispose();
        }
    }
}
