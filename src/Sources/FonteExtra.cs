using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Pulso
{
    class Leitura
    {
        public readonly List<Janela> Janelas = new List<Janela>();
        public string Plano, Detalhe, Fonte;
    }

    // Recusa do servidor com tempo de espera (429) ou credencial que precisa de ação
    class SemLeitura : Exception
    {
        public readonly TimeSpan? Espera;
        public readonly bool Credencial;
        public SemLeitura(string msg, TimeSpan? espera = null, bool credencial = false) : base(msg) { Espera = espera; Credencial = credencial; }
    }

    // Base dos provedores além de Claude e Codex (z.ai, OpenCode, Cursor, Grok, Copilot, Antigravity).
    // Detectar() é barato (arquivos/pastas) e roda sempre, para a aba Contas saber quem está conectado;
    // Consultar() (rede) só roda para quem tem anel no notch, a cada 5 min ou no clique em "atualizar".
    abstract class FonteExtra : IDisposable
    {
        protected readonly string Id;
        Timer relogio;
        int ocupado;
        DateTime proxima = DateTime.MinValue;
        int falhas;

        protected static readonly HttpClient Http = NovoHttp();

        static HttpClient NovoHttp()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Pulso/1.0 (Windows)");
            return h;
        }

        protected FonteExtra(string id) { Id = id; }

        protected virtual TimeSpan Intervalo { get { return TimeSpan.FromMinutes(5); } }

        // Nulo = presente; senão, o motivo ("não instalado", "sem login"…)
        protected abstract string Detectar();
        protected abstract Leitura Consultar();

        public void Iniciar() { relogio = new Timer(delegate { Tique(); }, null, 1500, 15000); }

        public void Atualizar()
        {
            proxima = DateTime.MinValue;
            ThreadPool.QueueUserWorkItem(delegate { Tique(); });
        }

        void Tique()
        {
            if (Interlocked.Exchange(ref ocupado, 1) == 1) return;
            try
            {
                string ausencia;
                try { ausencia = Detectar(); }
                catch (Exception e) { ausencia = "não foi possível verificar"; Log.Erro(Id + ": detectar", e); }
                bool mudou;
                lock (Estado.Trava)
                {
                    var p = Estado.Pegar(Id);
                    mudou = p.Presente != (ausencia == null) || p.Ausencia != ausencia;
                    p.Presente = ausencia == null;
                    p.Ausencia = ausencia;
                }
                if (mudou) Estado.Avisar();
                if (ausencia != null || !Config.Atual.Ativo(Id) || DateTime.UtcNow < proxima) return;

                try
                {
                    var l = Consultar();
                    falhas = 0;
                    proxima = DateTime.UtcNow + Intervalo;
                    lock (Estado.Trava)
                    {
                        var p = Estado.Pegar(Id);
                        p.Janelas.Clear();
                        p.Janelas.AddRange(l.Janelas);
                        p.Plano = l.Plano;
                        p.Nota = null;
                        p.Detalhe = l.Detalhe;
                        p.Fonte = l.Fonte;
                        p.Erro = null;
                        p.Confirmado = DateTime.UtcNow;
                        var principal = p.Principal;
                        p.RitmoHora = principal != null && !principal.Contagem.HasValue ? Ritmo.Registrar(Id, DateTime.UtcNow, principal.Usado) : null;
                    }
                    Estado.Avisar();
                    EstadoSalvo.Gravar();
                }
                catch (Exception e)
                {
                    var sl = e as SemLeitura;
                    falhas++;
                    var espera = sl != null && sl.Espera.HasValue ? sl.Espera.Value : TimeSpan.FromSeconds(Math.Min(900, 60 * Math.Pow(2, Math.Min(falhas - 1, 4))));
                    proxima = DateTime.UtcNow + espera;
                    string msg = sl != null ? sl.Message : Raiz(e).Message;
                    if (sl == null) Log.Info(Id + ": " + msg);
                    lock (Estado.Trava)
                    {
                        var p = Estado.Pegar(Id);
                        p.Nota = msg;
                        if (p.Janelas.Count == 0 || (sl != null && sl.Credencial)) p.Erro = msg;
                    }
                    Estado.Avisar();
                }
            }
            finally { ocupado = 0; }
        }

        protected static Exception Raiz(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }

        // Envia e trata os códigos comuns; devolve o corpo como texto
        protected static string Enviar(HttpRequestMessage req)
        {
            using (var resp = Http.SendAsync(req).Result)
            {
                int c = (int)resp.StatusCode;
                if (c == 401 || c == 403) throw new SemLeitura("Credencial recusada — entre de novo na ferramenta", TimeSpan.FromMinutes(10), true);
                if (c == 429)
                {
                    var ra = resp.Headers.RetryAfter;
                    var t = ra != null && ra.Delta.HasValue ? ra.Delta.Value : ra != null && ra.Date.HasValue ? ra.Date.Value.UtcDateTime - DateTime.UtcNow : TimeSpan.FromMinutes(5);
                    throw new SemLeitura("Servidor pediu pausa; nova leitura em " + Tempo.Duracao(t), t < TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : t);
                }
                if (!resp.IsSuccessStatusCode) throw new SemLeitura("Servidor respondeu " + c);
                return resp.Content.ReadAsStringAsync().Result;
            }
        }

        public void Dispose() { if (relogio != null) relogio.Dispose(); }
    }
}
