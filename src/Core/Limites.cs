using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pulso
{
    // Limites de uso (aba Limites): o Claude Code e o Codex param sozinhos quando a cota chega no % que a pessoa definiu,
    // em todas as pastas ou numa pasta escolhida, e recusam pedidos novos ali até ela renovar.
    //  - Os dois chamam "Pulso.exe --limite <provedor>" (hook) a cada pedido e a cada ferramenta. O hook não calcula nada:
    //    lê o limites.json que o Pulso aberto mantém (regras + o % de agora, o mesmo do anel) e decide.
    //  - Conta o % TOTAL da cota (decisão do dono, 2026-10-13), não o quanto a pasta gastou.
    //  - Pulso fechado (o arquivo some) ou travado (arquivo velho): o hook não bloqueia nada — um limite nunca pode
    //    prender o trabalho por falta de dado.
    // Uma regra é de UM provedor (a aba mostra as regras dentro do bloco de cada um)
    class RegraLimite
    {
        public string Provedor = "claude";
        public string Pasta;              // nula = todas as pastas
        public int Sessao = 80;           // % da sessão de 5 horas; 0 = sem limite
        public int Semana;                // % da semana; 0 = sem limite

        public bool Vale(string provedor) { return Provedor == provedor; }

        public Dictionary<string, object> ParaJson()
        {
            return new Dictionary<string, object> { { "provedor", Provedor }, { "pasta", Pasta }, { "sessao", Sessao }, { "semana", Semana } };
        }

        // O formato de antes (13/10, só em testes) tinha "claude"/"codex" na mesma regra: vira uma regra para cada
        public static IEnumerable<RegraLimite> Ler(object o)
        {
            if (!(o is IDictionary<string, object>)) yield break;
            string prov = Json.Str(o, "provedor");
            var provs = prov != null ? new[] { prov }
                : new[] { "claude", "codex" }.Where(p => Json.Bool(o, p) ?? true).ToArray();
            foreach (var p in provs)
                yield return new RegraLimite { Provedor = p, Pasta = Json.Str(o, "pasta"), Sessao = Pct(Json.Num(o, "sessao")), Semana = Pct(Json.Num(o, "semana")) };
        }

        static int Pct(double? v) { return v.HasValue ? Math.Max(0, Math.Min(100, (int)Math.Round(v.Value))) : 0; }
    }

    static class Limites
    {
        public static readonly string[] Provedores = { "claude", "codex" };
        static string Arquivo { get { return Path.Combine(Caminhos.Dados, "limites.json"); } }
        // O Pulso aberto regrava a cada 30 s mesmo sem mudança: mais velho que isto, ele fechou sem apagar (queda)
        static readonly TimeSpan Validade = TimeSpan.FromMinutes(3);
        public static DateTime LiberadoAte = DateTime.MinValue;      // UTC: "Liberar por 1 hora"
        public static event Action<Aviso> Avisar;

        public class Uso { public double? Sessao, Semana; public DateTime? RenovaSessao, RenovaSemana; }

        public class Bloqueio
        {
            public RegraLimite Regra; public bool Semanal; public double Uso; public DateTime? Renova;
            public int Limite { get { return Semanal ? Regra.Semana : Regra.Sessao; } }
        }

        public static bool Liberado { get { return LiberadoAte > DateTime.UtcNow; } }

        // ---------- regra ----------

        // Primeira janela estourada entre as regras que valem para o provedor naquela pasta (qualquer uma basta para parar)
        public static Bloqueio Avaliar(IEnumerable<RegraLimite> regras, string provedor, string pasta, Uso uso, DateTime agora)
        {
            if (uso == null) return null;
            foreach (var r in regras)
            {
                if (r == null || !r.Vale(provedor) || !Contem(r.Pasta, pasta)) continue;
                var b = Checar(r, false, uso.Sessao, uso.RenovaSessao, agora) ?? Checar(r, true, uso.Semana, uso.RenovaSemana, agora);
                if (b != null) return b;
            }
            return null;
        }

        static Bloqueio Checar(RegraLimite r, bool semanal, double? uso, DateTime? renova, DateTime agora)
        {
            int limite = semanal ? r.Semana : r.Sessao;
            if (limite <= 0 || !uso.HasValue) return null;
            if (renova.HasValue && renova.Value <= agora) return null;   // já renovou e o Pulso ainda não leu o número novo
            if (uso.Value * 100 < limite - 1e-9) return null;
            return new Bloqueio { Regra = r, Semanal = semanal, Uso = uso.Value, Renova = renova };
        }

        // A pasta da regra e as subpastas. Maiúsculas e barras não importam; "/c/Users/..." (Git Bash) vira "C:\Users\..."
        public static bool Contem(string regra, string pasta)
        {
            if (regra == null) return true;
            if (string.IsNullOrEmpty(pasta)) return false;
            string a = Normalizar(regra), b = Normalizar(pasta);
            return b.Equals(a, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static string Normalizar(string p)
        {
            p = p.Trim().Replace('/', '\\');
            if (p.Length >= 3 && p[0] == '\\' && char.IsLetter(p[1]) && p[2] == '\\') p = p[1] + ":" + p.Substring(2);
            if (p.Length == 2 && p[1] == ':') p += "\\";   // "C:" sozinho seria a pasta atual do drive, não a raiz
            try { p = Path.GetFullPath(p); } catch { }
            return p.TrimEnd('\\');
        }

        // O % de agora, o mesmo do anel (com a estimativa do Claude quando ligada)
        public static Uso DoEstado(string id)
        {
            var p = Estado.Copia(id);
            if (p.Janelas.Count == 0) return null;
            var u = new Uso();
            var s = p.Janelas.FirstOrDefault(j => j.Minutos == 300 && !j.Semanal);
            var w = p.PrimeiraSemanal;
            if (s != null) { u.Sessao = s.Atual; u.RenovaSessao = s.ResetaEm; }
            if (w != null) { u.Semana = w.Atual; u.RenovaSemana = w.ResetaEm; }
            return u;
        }

        // ---------- o arquivo que o hook lê ----------
        static string ultimoConteudo;
        static DateTime ultimaGravacao = DateTime.MinValue;

        public static void Publicar(bool forcar)
        {
            try
            {
                var cfg = Config.Atual;
                if (cfg.Limites.Count == 0) { Apagar(); return; }
                var provs = new Dictionary<string, object>();
                foreach (var id in Provedores)
                {
                    var u = DoEstado(id);
                    if (u != null) provs[id] = new Dictionary<string, object>
                    {
                        { "sessao", u.Sessao }, { "renovaSessao", Iso(u.RenovaSessao) }, { "semana", u.Semana }, { "renovaSemana", Iso(u.RenovaSemana) },
                    };
                }
                var corpo = new Dictionary<string, object>
                {
                    { "versao", 1 },
                    { "idioma", cfg.Idioma },
                    { "liberadoAte", Liberado ? Iso(LiberadoAte) : null },
                    { "regras", cfg.Limites.Select(r => (object)r.ParaJson()).ToArray() },
                    { "provedores", provs },
                };
                string conteudo = Json.Write(corpo);
                var agora = DateTime.UtcNow;
                if (!forcar && conteudo == ultimoConteudo && agora - ultimaGravacao < TimeSpan.FromSeconds(30)) return;
                corpo["atualizado"] = Iso(agora);
                Caminhos.GravarAtomico(Arquivo, Json.Write(corpo));
                ultimoConteudo = conteudo;
                ultimaGravacao = agora;
            }
            catch (Exception e) { Log.Erro("publicar limites", e); }
        }

        // Pulso fechando ou sem regras: sem arquivo, o hook deixa tudo passar
        public static void Apagar()
        {
            ultimoConteudo = null;
            try { if (File.Exists(Arquivo)) File.Delete(Arquivo); } catch (Exception e) { Log.Erro("apagar limites", e); }
        }

        static string Iso(DateTime? d) { return d.HasValue ? d.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") : null; }

        public static void Liberar(bool ligar)
        {
            LiberadoAte = ligar ? DateTime.UtcNow.AddHours(1) : DateTime.MinValue;
            Log.Info(ligar ? "limites liberados por 1 hora" : "limites valendo de novo");
            Publicar(true);
        }

        // ---------- avisos: parou e pode retomar ----------
        class Parada { public string Provedor, Pasta; public bool Semanal; public DateTime? Renova; }
        static readonly List<Parada> paradas = new List<Parada>();

        // Mensagem do hook (WM_COPYDATA, tipo 7): um cartão por pasta e janela — o hook roda a cada ferramenta e pedido
        public static void Parou(string json)
        {
            var o = Json.Parse(json);
            string provedor = Json.Str(o, "provedor"), pasta = Json.Str(o, "pasta");
            if (provedor == null || pasta == null) return;
            bool semanal = Json.Bool(o, "semanal") == true;
            var renova = Json.Data(o, "renova");
            if (paradas.Any(p => p.Provedor == provedor && p.Semanal == semanal && string.Equals(p.Pasta, pasta, StringComparison.OrdinalIgnoreCase))) return;
            paradas.Add(new Parada { Provedor = provedor, Pasta = pasta, Semanal = semanal, Renova = renova });
            int uso = (int)Math.Round((Json.Num(o, "uso") ?? 0) * 100), limite = (int)(Json.Num(o, "limite") ?? 0);
            Log.Info("limite: " + provedor + " parado em " + pasta + " (" + (semanal ? "semana" : "5h") + " " + uso + "%, limite " + limite + "%)");
            Disparar(new Aviso
            {
                Provedor = provedor,
                Titulo = "{0} parado pelo limite".T(NomeDe(provedor)),
                Subtitulo = NomePasta(pasta),
                Status = "{0} em {1}% · limite {2}%".T((semanal ? "Semana" : "Sessão (5h)").T(), uso, limite),
                CorStatus = Paleta.Atual.Critico,
                Proxima = renova.HasValue ? Texto.Renova(renova) : null,
                Som = Som.Limite,
                Clique = delegate { Foco.Trazer(pasta); },
            });
        }

        // A cota voltou para baixo do limite (renovou, ou a regra mudou): avisa uma vez para retomar
        public static void Conferir()
        {
            if (paradas.Count == 0) return;
            var agora = DateTime.UtcNow;
            foreach (var p in paradas.ToList())
            {
                if (Avaliar(Config.Atual.Limites, p.Provedor, p.Pasta, DoEstado(p.Provedor), agora) != null) continue;
                paradas.Remove(p);
                string pasta = p.Pasta;
                Disparar(new Aviso
                {
                    Provedor = p.Provedor,
                    Titulo = "{0} pode retomar".T(NomeDe(p.Provedor)),
                    Subtitulo = NomePasta(pasta),
                    Status = "A cota voltou para baixo do limite".T(),
                    Proxima = "Clique para abrir a janela".T(),
                    Som = Som.Renovacao,
                    Clique = delegate { Foco.Trazer(pasta); },
                });
            }
        }

        static void Disparar(Aviso a)
        {
            var h = Avisar;
            if (h != null) h(a);
        }

        static string NomeDe(string id) { var i = Catalogo.Info(id); return i != null ? i.Nome : id; }

        public static string NomePasta(string pasta)
        {
            if (pasta == null) return "Todas as pastas".T();
            string n = Path.GetFileName(pasta.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? pasta : n;
        }

        // ---------- o hook ("Pulso.exe --limite claude|codex"), processo curto ----------
        public static void Executar(string provedor)
        {
            string entrada;
            using (var sr = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) entrada = sr.ReadToEnd();
            var o = Json.Parse(entrada);
            string evento = Json.Str(o, "hook_event_name") ?? "";
            string pasta = Json.Str(o, "cwd") ?? Environment.CurrentDirectory;

            var dados = Json.Parse(LerArquivo());
            var agora = DateTime.UtcNow;
            var atualizado = Json.Data(dados, "atualizado");
            if (dados == null || !atualizado.HasValue || agora - atualizado.Value > Validade) return;
            var liberado = Json.Data(dados, "liberadoAte");
            if (liberado.HasValue && liberado.Value > agora) return;
            Config.Atual.Idioma = Json.Str(dados, "idioma") ?? "pt";

            var regras = (Json.Arr(dados, "regras") ?? new object[0]).SelectMany(RegraLimite.Ler).ToList();
            var u = Json.Obj(dados, "provedores", provedor);
            var uso = u == null ? null : new Uso
            {
                Sessao = Json.Num(u, "sessao"), RenovaSessao = Json.Data(u, "renovaSessao"),
                Semana = Json.Num(u, "semana"), RenovaSemana = Json.Data(u, "renovaSemana"),
            };
            var b = Avaliar(regras, provedor, pasta, uso, agora);
            if (b == null) return;

            Mensagens.Enviar(7, Json.Write(new Dictionary<string, object>
            {
                { "provedor", provedor }, { "pasta", pasta }, { "semanal", b.Semanal }, { "uso", b.Uso }, { "limite", b.Limite }, { "renova", Iso(b.Renova) },
            }));
            using (var sw = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                sw.Write(Saida(provedor, evento, b));
        }

        static string LerArquivo()
        {
            // O Pulso troca o arquivo por inteiro (GravarAtomico); pego no meio da troca, tenta de novo
            for (int i = 0; i < 3; i++)
            {
                try { return File.Exists(Arquivo) ? Caminhos.LerCompartilhado(Arquivo) : null; }
                catch (IOException) { System.Threading.Thread.Sleep(40); }
            }
            return null;
        }

        // Resposta no formato de cada um. Claude: "continue: false" para na hora, sem gastar token, e o motivo fica na
        // conversa. Codex: não tem parada seca no meio do turno — o motivo vira o resultado da ferramenta e o modelo, avisado,
        // anota onde parou e encerra; as ferramentas seguintes são negadas. Pedido novo é recusado nos dois.
        public static string Saida(string provedor, string evento, Bloqueio b)
        {
            string motivo = Motivo(b);
            string liberar = "Para liberar antes, use o menu do Pulso na bandeja.".T();
            string parar = "Pare aqui: não use mais ferramentas. Diga em poucas linhas onde parou e o que falta, para continuar depois.".T();
            var r = new Dictionary<string, object>();
            if (evento == "UserPromptSubmit")
            {
                r["decision"] = "block";
                r["reason"] = motivo + " " + liberar;
            }
            else if (evento == "PreToolUse")
            {
                r["hookSpecificOutput"] = new Dictionary<string, object>
                {
                    { "hookEventName", "PreToolUse" }, { "permissionDecision", "deny" }, { "permissionDecisionReason", motivo + " " + parar },
                };
                // No Codex o PreToolUse não aceita "continue" (o hook falharia)
                if (provedor == "claude") { r["continue"] = false; r["stopReason"] = motivo + " " + liberar; }
            }
            else
            {
                r["continue"] = false;
                r["stopReason"] = motivo + " " + (provedor == "codex" ? parar : liberar);
            }
            return Json.Write(r);
        }

        public static string Motivo(Bloqueio b)
        {
            string onde = b.Regra.Pasta == null ? "" : " " + "para {0}".T(NomePasta(b.Regra.Pasta));
            string renova = b.Renova.HasValue ? " " + Util.Maiuscula(Texto.Renova(b.Renova)) + "." : "";
            return (b.Semanal ? "Limite do Pulso: a cota semanal chegou a {0}% (limite de {1}%{2})." : "Limite do Pulso: a sessão de 5 horas chegou a {0}% (limite de {1}%{2}).")
                .T((int)Math.Round(b.Uso * 100), b.Limite, onde) + renova;
        }
    }
}
