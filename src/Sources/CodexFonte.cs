using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Pulso
{
    // Codex: a fonte principal são os arquivos de sessão (exatos, a cada resposta, sem rede).
    // O servidor (chatgpt.com/backend-api/wham/usage) só é consultado quando o Codex está parado há um tempo —
    // pega consumo feito em outro lugar (web, outro PC) e confirma a renovação das janelas.
    class CodexFonte : IDisposable
    {
        const string Url = "https://chatgpt.com/backend-api/wham/usage";
        readonly CodexSessoes sessoes = new CodexSessoes();
        HttpClient http;
        Timer relogio;
        DateTime ultimaConsulta = DateTime.MinValue, esperaAte = DateTime.MinValue;
        int consultando, falhas;

        public void Iniciar()
        {
            sessoes.Iniciar();
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulso/1.0 (Windows)");
            relogio = new Timer(delegate { Tique(); }, null, 3000, 3000);
        }

        public void Atualizar() { ultimaConsulta = DateTime.MinValue; esperaAte = DateTime.MinValue; Tique(); }

        void Tique()
        {
            Virada();
            var agora = DateTime.UtcNow;
            if (agora < esperaAte) return;
            DateTime? confirmado;
            lock (Estado.Trava) confirmado = Estado.Codex.Confirmado;
            // Dado da sessão com menos de 5 min é melhor que o do servidor; só consulta quando ficou velho
            if (confirmado.HasValue && (agora - confirmado.Value).TotalMinutes < 5) return;
            if ((agora - ultimaConsulta).TotalMinutes < 5) return;
            if (!File.Exists(Caminhos.CodexAuth)) return;
            if (Interlocked.Exchange(ref consultando, 1) == 1) return;
            ultimaConsulta = agora;
            Task.Factory.StartNew(delegate
            {
                try { Consultar(); falhas = 0; }
                catch (Exception e)
                {
                    falhas++;
                    esperaAte = DateTime.UtcNow.AddMinutes(Math.Min(30, 5 * falhas));
                    Log.Info("codex: servidor indisponível (" + Raiz(e).Message + "); seguindo pelas sessões");
                }
                finally { consultando = 0; }
            });
        }

        static Exception Raiz(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }

        void Consultar()
        {
            var auth = Json.Parse(Caminhos.LerCompartilhado(Caminhos.CodexAuth));
            string token = Json.Str(auth, "tokens", "access_token");
            string conta = Json.Str(auth, "tokens", "account_id");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(conta)) return;
            var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("ChatGPT-Account-Id", conta);
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            token = null;
            using (var resp = http.SendAsync(req).Result)
            {
                int codigo = (int)resp.StatusCode;
                if (codigo == 429)
                {
                    var ra = resp.Headers.RetryAfter;
                    var espera = ra != null && ra.Delta.HasValue ? ra.Delta.Value : TimeSpan.FromMinutes(5);
                    esperaAte = DateTime.UtcNow + (espera.TotalSeconds < 60 ? TimeSpan.FromSeconds(60) : espera);
                    return;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    esperaAte = DateTime.UtcNow.AddMinutes(15);
                    Log.Info("codex: servidor respondeu " + codigo);
                    return;
                }
                var o = Json.Parse(resp.Content.ReadAsStringAsync().Result);
                var janelas = new List<Janela>();
                LerJanela(janelas, Json.Obj(o, "rate_limit", "primary_window"), "primary", false);
                LerJanela(janelas, Json.Obj(o, "rate_limit", "secondary_window"), "secondary", true);
                if (janelas.Count == 0) return;
                lock (Estado.Trava)
                {
                    var p = Estado.Codex;
                    // Um evento de sessão mais novo pode ter chegado enquanto a consulta corria
                    if (p.Confirmado.HasValue && (DateTime.UtcNow - p.Confirmado.Value).TotalSeconds < 30) return;
                    p.Janelas.Clear();
                    p.Janelas.AddRange(janelas);
                    string plano = Json.Str(o, "plan_type");
                    if (plano != null) p.Plano = Rotulos.Plano(plano);
                    p.Confirmado = DateTime.UtcNow;
                    p.Fonte = "servidor do Codex";
                    var principal = p.PorId("primary");
                    if (principal != null) p.RitmoHora = Ritmo.Registrar("codex", DateTime.UtcNow, principal.Usado);
                    p.Erro = null;
                }
                Estado.Avisar();
                EstadoSalvo.Gravar();
            }
        }

        static void LerJanela(List<Janela> l, IDictionary<string, object> w, string id, bool semanalPadrao)
        {
            if (w == null) return;
            double? pct = Json.Num(w, "used_percent");
            if (!pct.HasValue) return;
            int segundos = (int)(Json.Num(w, "limit_window_seconds") ?? 0);
            DateTime? reset = Json.Data(w, "reset_at");
            double? depois = Json.Num(w, "reset_after_seconds");
            if (!reset.HasValue && depois.HasValue) reset = DateTime.UtcNow.AddSeconds(depois.Value);
            int min = segundos / 60;
            var j = new Janela { Id = id, Minutos = min, Semanal = min >= 7 * 1440 || (min == 0 && semanalPadrao), Usado = Math.Max(0, Math.Min(1, pct.Value / 100)), ResetaEm = reset };
            j.Rotulo = Rotulos.Janela(min, j.Semanal);
            l.Add(j);
        }

        // Janela vencida volta a zero na hora (o Codex só grava o novo valor na próxima resposta)
        void Virada()
        {
            bool mudou = false;
            var agora = DateTime.UtcNow;
            lock (Estado.Trava)
            {
                // Sem leitura há 30 min: a velocidade de consumo de antes não vale mais
                var c = Estado.Codex;
                if (c.RitmoHora.HasValue && (!c.Confirmado.HasValue || (agora - c.Confirmado.Value).TotalMinutes > 30)) { c.RitmoHora = null; mudou = true; }
                foreach (var j in Estado.Codex.Janelas)
                {
                    if (!j.ResetaEm.HasValue || j.ResetaEm.Value > agora) continue;
                    j.Usado = 0; j.ResetaEm = null;
                    mudou = true;
                }
                foreach (var s in Estado.Codex.Sessoes.Values)
                {
                    // Tarefa aberta sem linha nova há 10 min: parada (o Codex não grava aprovação pendente no arquivo)
                    if (s.Estado == Atividade.Trabalhando && (agora - s.Ultima).TotalMinutes > 10) { s.Estado = Atividade.Ociosa; mudou = true; }
                    else if (s.Estado == Atividade.Concluida && (agora - s.Ultima).TotalMinutes > 30) { s.Estado = Atividade.Ociosa; mudou = true; }
                }
            }
            if (mudou) Estado.Avisar();
        }

        public void Dispose()
        {
            if (relogio != null) relogio.Dispose();
            sessoes.Dispose();
            if (http != null) http.Dispose();
        }
    }
}
