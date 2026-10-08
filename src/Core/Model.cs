using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pulso
{
    enum Atividade { Ociosa, Trabalhando, Aguardando, Concluida }

    // Uma janela de limite: "5 horas", "semana", "semana (Opus)"...
    class Janela
    {
        public string Id;
        public string Rotulo;
        public double Usado;          // 0..1, último valor exato (servidor, statusline ou sessão do Codex)
        public double? Estimado;      // 0..1, valor exato + consumo local desde a leitura (só Claude)
        public DateTime? ResetaEm;    // UTC
        public int Minutos;           // duração da janela (300 = 5h, 10080 = semana)
        public bool Semanal;
        public int? Contagem;         // janela só de contagem (ex.: requisições hoje, sem limite publicado)
        public string Grupo;          // título que agrupa janelas no cartão (ex.: "Modelos Gemini")

        public double Atual { get { return Estimado.HasValue ? Math.Max(Usado, Estimado.Value) : Usado; } }

        public Janela Copia() { return (Janela)MemberwiseClone(); }
    }

    class Sessao
    {
        public string Id;
        public string Pasta;
        public string Nome;            // nome que o Claude Code dá à sessão ("website-36")
        public string Origem;          // claude-vscode, cli, claude-desktop...
        public Atividade Estado;
        public DateTime Ultima;        // UTC do último sinal
        public bool PorHook;           // estado veio do registro do Claude Code (mais confiável que o arquivo de conversa)
        public Sessao Copia() { return (Sessao)MemberwiseClone(); }

        public string Titulo
        {
            get
            {
                string pasta = string.IsNullOrEmpty(Pasta) ? null : System.IO.Path.GetFileName(Pasta.TrimEnd('\\', '/'));
                string curto = Id != null && Id.Length >= 4 ? Id.Substring(0, 4) : Id;
                return (pasta ?? Nome ?? "sessão".T()) + " · " + curto;
            }
        }
    }

    class Provedor
    {
        public string Id;
        public string Nome;
        public readonly List<Janela> Janelas = new List<Janela>();
        public readonly Dictionary<string, Sessao> Sessoes = new Dictionary<string, Sessao>();
        public DateTime? Confirmado;   // UTC da última leitura exata
        public string Fonte;
        public string Plano;
        public string Nota;
        public string Erro;
        public double? RitmoHora;      // pontos percentuais/hora na janela principal (projeção)
        public bool Presente = true;   // ferramenta instalada/conectada neste computador
        public string Ausencia;        // por que não está conectado ("não instalado", "sem login"…)
        public string Detalhe;         // linha extra no cartão (ex.: conta pessoal sem cota publicada)

        public Janela PorId(string id)
        {
            foreach (var j in Janelas) if (j.Id == id) return j;
            return null;
        }

        // Janela do anel de cada provedor. Nula com janelas = "—".
        public Janela Principal
        {
            get
            {
                if (Janelas.Count == 0) return null;
                switch (Id)
                {
                    case "cursor": return PorId("included") ?? PorId("api");
                    case "grok": return PorId("credits") ?? Janelas[0];
                    case "copilot": return PorId("premium_interactions") ?? Janelas[0];
                    case "glm": return PorId("session");
                    case "opencode": return PorId("rolling");
                    case "gemini": return Antigravity.Principal(Janelas);
                    default: return Janelas[0];
                }
            }
        }

        public Janela PrimeiraSemanal
        {
            get
            {
                switch (Id)
                {
                    case "cursor": case "grok": case "copilot": return null;
                    case "glm": case "opencode": return PorId("weekly");
                    case "gemini": return Antigravity.Semanal(Janelas);
                    default: foreach (var j in Janelas) if (j.Semanal) return j; return null;
                }
            }
        }

        public Atividade Atividade
        {
            get
            {
                var r = Atividade.Ociosa;
                foreach (var s in Sessoes.Values)
                {
                    if (s.Estado == Atividade.Aguardando) return Atividade.Aguardando;
                    if (s.Estado == Atividade.Trabalhando) r = Atividade.Trabalhando;
                    else if (s.Estado == Atividade.Concluida && r == Atividade.Ociosa) r = Atividade.Concluida;
                }
                return r;
            }
        }

        public Provedor Copia()
        {
            var p = new Provedor { Id = Id, Nome = Nome, Confirmado = Confirmado, Fonte = Fonte, Plano = Plano, Nota = Nota, Erro = Erro, RitmoHora = RitmoHora, Presente = Presente, Ausencia = Ausencia, Detalhe = Detalhe };
            foreach (var j in Janelas) p.Janelas.Add(j.Copia());
            foreach (var kv in Sessoes) p.Sessoes[kv.Key] = kv.Value.Copia();
            return p;
        }
    }

    // Estado central. As fontes alteram sob a trava e chamam Avisar(); a interface lê cópias.
    static class Estado
    {
        public static readonly object Trava = new object();
        static readonly Dictionary<string, Provedor> todos = Catalogo.Todos.ToDictionary(i => i.Id, i => new Provedor
        {
            Id = i.Id, Nome = i.Nome,
            // Claude e Codex são conferidos na partida; os demais até o leitor deles responder
            Presente = i.Id == "claude" || i.Id == "codex", Ausencia = i.Id == "claude" || i.Id == "codex" ? null : "verificando…",
        });
        public static Provedor Claude { get { return todos["claude"]; } }
        public static Provedor Codex { get { return todos["codex"]; } }
        public static IEnumerable<Provedor> Todos { get { return todos.Values; } }

        public static event Action Mudou;
        static SynchronizationContext ui;
        static int pendente;

        public static void Iniciar(SynchronizationContext contexto) { ui = contexto; }

        public static Provedor Pegar(string id) { Provedor p; return todos.TryGetValue(id, out p) ? p : Claude; }

        public static Provedor Copia(string id)
        {
            lock (Trava) return Pegar(id).Copia();
        }

        // Junta vários avisos seguidos num só repaint na thread da interface
        public static void Avisar()
        {
            if (Interlocked.Exchange(ref pendente, 1) == 1) return;
            var c = ui;
            if (c == null) { pendente = 0; return; }
            c.Post(delegate
            {
                pendente = 0;
                var h = Mudou;
                if (h != null) h();
            }, null);
        }
    }
}
