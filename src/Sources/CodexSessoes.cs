using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pulso
{
    // Consumo e atividade do Codex pelos arquivos de sessão (~/.codex/sessions/AAAA/MM/DD/rollout-*.jsonl).
    // A cada resposta do modelo o Codex grava um evento token_count com os limites da conta: é o valor exato,
    // chega sem rede e sem atraso.
    class CodexSessoes : IDisposable
    {
        Seguidor seguidor;
        DateTime ultimoEvento = DateTime.MinValue;
        readonly Dictionary<string, string> pastaDaSessao = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex IdNoNome = new Regex(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.jsonl$", RegexOptions.IgnoreCase);

        public event Action LimiteAtingido;     // a conta bateu o limite: a fonte pede a leitura do servidor na hora

        public void Iniciar()
        {
            CarregarUltimo();
            seguidor = new Seguidor(Caminhos.CodexSessoes, AoLinha, TimeSpan.FromMinutes(30));
            seguidor.Iniciar();
        }

        // Na partida, o evento de limite mais recente com números entre os arquivos dos últimos dias; e, se depois dele a
        // conta bateu o limite (429 sem números ou tarefa encerrada no limite), já abre esgotado
        void CarregarUltimo()
        {
            if (!Directory.Exists(Caminhos.CodexSessoes)) return;
            try
            {
                var recentes = new DirectoryInfo(Caminhos.CodexSessoes)
                    .EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-8))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(12);
                DateTime? atingidoEm = null;
                foreach (var f in recentes)
                {
                    var linhas = Seguidor.Cauda(f.FullName, 768 * 1024);
                    for (int i = linhas.Count - 1; i >= 0; i--)
                    {
                        bool noLimite = linhas[i].IndexOf("\"usage_limit_exceeded\"", StringComparison.Ordinal) >= 0;
                        if (!noLimite && linhas[i].IndexOf("\"rate_limits\"", StringComparison.Ordinal) < 0) continue;
                        var o = Json.Parse(linhas[i]);
                        bool comJanelas = false;
                        if (!noLimite) Limites(o, out comJanelas, true);
                        if (noLimite || Json.Str(o, "payload", "rate_limits", "rate_limit_reached_type") != null)
                        {
                            var q = Json.Data(o, "timestamp");
                            if (q.HasValue && (!atingidoEm.HasValue || q.Value > atingidoEm.Value)) atingidoEm = q;
                        }
                        if (comJanelas) break;
                    }
                }
                if (atingidoEm.HasValue && atingidoEm.Value >= ultimoEvento) Atingido();
            }
            catch (Exception e) { Log.Erro("codex: carregar último", e); }
        }

        void AoLinha(string arq, string linha)
        {
            string id = IdSessao(arq);
            if (linha.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0)
            {
                var o = Json.Parse(linha);
                string cwd = Json.Str(o, "payload", "cwd");
                if (cwd != null) lock (pastaDaSessao) pastaDaSessao[id] = cwd;
            }

            Atividade? estado = null;
            if (linha.IndexOf("\"task_complete\"", StringComparison.Ordinal) >= 0 ||
                linha.IndexOf("\"turn_aborted\"", StringComparison.Ordinal) >= 0) estado = Atividade.Concluida;
            else if (linha.IndexOf("\"event_msg\"", StringComparison.Ordinal) >= 0 ||
                     linha.IndexOf("\"response_item\"", StringComparison.Ordinal) >= 0) estado = Atividade.Trabalhando;

            bool mudou = false, comJanelas;
            if (linha.IndexOf("\"rate_limits\"", StringComparison.Ordinal) >= 0)
                mudou = Limites(Json.Parse(linha), out comJanelas);
            // A tarefa que parou no limite grava o erro no task_complete (o evento de erro em si não vai para o arquivo)
            if (estado == Atividade.Concluida && linha.IndexOf("\"usage_limit_exceeded\"", StringComparison.Ordinal) >= 0)
                mudou |= Atingido();

            if (estado.HasValue)
            {
                lock (Estado.Trava)
                {
                    Sessao s;
                    if (!Estado.Codex.Sessoes.TryGetValue(id, out s))
                    {
                        s = new Sessao { Id = id, Pasta = PastaDe(arq, id) };
                        Estado.Codex.Sessoes[id] = s;
                    }
                    if (s.Estado != estado.Value) mudou = true;
                    s.Estado = estado.Value;
                    s.Ultima = DateTime.UtcNow;
                }
            }
            if (mudou) Estado.Avisar();
        }

        string IdSessao(string arq)
        {
            var m = IdNoNome.Match(arq);
            return m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(arq);
        }

        string PastaDe(string arq, string id)
        {
            lock (pastaDaSessao)
            {
                string p;
                if (pastaDaSessao.TryGetValue(id, out p)) return p;
            }
            // Primeira linha do arquivo é o session_meta, com a pasta do projeto
            try
            {
                using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string primeira = sr.ReadLine();
                    string cwd = Json.Str(Json.Parse(primeira), "payload", "cwd");
                    if (cwd != null) { lock (pastaDaSessao) pastaDaSessao[id] = cwd; return cwd; }
                }
            }
            catch { }
            return null;
        }

        // Aplica o rate_limits de um token_count; ignora evento mais velho que o já mostrado. Devolve se a tela mudou;
        // comJanelas diz se o evento trouxe números (sem eles, a partida continua procurando um evento mais antigo).
        // O Codex grava a janela nula quando a resposta não trouxe os cabeçalhos dela, como no estouro do limite (429, com
        // rate_limit_reached_type preenchido). Esvaziar a lista deixava a cápsula sem número até a consulta ao servidor,
        // minutos depois, trazer os 100%: no estouro, janela nula mantém a última conhecida (fora dele, é janela que o plano
        // não tem). O mesmo token_count pode trazer o limite de um modelo com cota própria (limit_id diferente de "codex"):
        // não é o da conta e não substitui as janelas
        bool Limites(object o, out bool comJanelas, bool partida = false)
        {
            comJanelas = false;
            var rl = Json.Obj(o, "payload", "rate_limits");
            if (rl == null) return false;
            string limite = Json.Str(rl, "limit_id");
            if (limite != null && limite != "codex") return false;
            var primaria = LerJanela(rl, "primary");
            var secundaria = LerJanela(rl, "secondary");
            bool atingido = Json.Str(rl, "rate_limit_reached_type") != null;
            // Na partida, o limite atingido é decidido no fim da leitura (CarregarUltimo), com as janelas já carregadas
            if (primaria == null && secundaria == null) return atingido && !partida && Atingido();
            DateTime quando = Json.Data(o, "timestamp") ?? DateTime.UtcNow;
            lock (Estado.Trava)
            {
                if (quando < ultimoEvento) return false;
                ultimoEvento = quando;
                var p = Estado.Codex;
                // Evento comum é a foto inteira: janela que não veio não existe no plano (o Pro do Codex ficou sem a de 5h
                // em 09/2026). No estouro, a que faltou é cabeçalho que não chegou: fica a última conhecida
                if (!atingido) p.Janelas.RemoveAll(j => (j.Id == "primary" && primaria == null) || (j.Id == "secondary" && secundaria == null));
                Trocar(p, primaria);
                Trocar(p, secundaria);
                string plano = Json.Str(rl, "plan_type");
                if (plano != null) p.Plano = Rotulos.PlanoCodex(plano);
                if (Json.Obj(rl, "credits") != null)
                {
                    bool creditos = Json.Bool(rl, "credits", "has_credits") == true;
                    string saldo = Json.Str(rl, "credits", "balance");
                    p.Nota = creditos && saldo != null && saldo != "0" ? "Créditos: {0}".T(saldo) : null;
                }
                p.Confirmado = quando;
                if (primaria != null) p.RitmoHora = Ritmo.Registrar("codex", quando, primaria.Usado);
                p.Fonte = "sessão do Codex";
                p.Erro = null;
            }
            comJanelas = true;
            if (atingido && !partida) Atingido();
            return true;
        }

        // Limite atingido (token_count com rate_limit_reached_type, ou tarefa encerrada com usage_limit_exceeded): se os
        // números não chegaram a 100%, a janela mais cheia é a que estourou; e pede a leitura exata do servidor na hora.
        // Só marca a partir de 90%: abaixo disso o número conhecido está velho (uso em outro PC ou na web) e a marca podia
        // cair na janela errada; quando o servidor a corrigisse, a queda de 10 pontos virava um aviso falso de "renovado".
        // Na mesma janela o uso só sobe, então de 90% a correção nunca cai 10 pontos
        bool Atingido()
        {
            bool mudou = false;
            lock (Estado.Trava)
            {
                Janela cheia = null;
                foreach (var j in Estado.Codex.Janelas) if (cheia == null || j.Usado > cheia.Usado) cheia = j;
                if (cheia != null && cheia.Usado >= 0.9 && cheia.Usado < 1) { cheia.Usado = 1; mudou = true; }
            }
            Log.Info("codex: limite atingido" + (mudou ? "; janela mais cheia marcada como esgotada" : "") + ", pedindo a leitura do servidor");
            var h = LimiteAtingido;
            if (h != null) h();
            return mudou;
        }

        // Troca a janela de mesmo id; "primary" fica antes de "secondary" (a primeira é a do anel)
        static void Trocar(Provedor p, Janela nova)
        {
            if (nova == null) return;
            int i = p.Janelas.FindIndex(j => j.Id == nova.Id);
            if (i >= 0) p.Janelas[i] = nova;
            else p.Janelas.Add(nova);
            p.Janelas.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        static Janela LerJanela(IDictionary<string, object> rl, string chave)
        {
            var w = Json.Obj(rl, chave);
            double? pct = Json.Num(w, "used_percent");
            if (w == null || !pct.HasValue) return null;
            int minutos = (int)(Json.Num(w, "window_minutes") ?? 0);
            var j = new Janela
            {
                Id = chave,
                Minutos = minutos,
                Semanal = minutos >= 7 * 24 * 60 || chave == "secondary",
                Usado = Math.Max(0, Math.Min(1, pct.Value / 100.0)),
                ResetaEm = Json.Data(w, "resets_at"),
            };
            j.Rotulo = Rotulos.Janela(minutos, j.Semanal);
            return j;
        }

        public void Dispose() { if (seguidor != null) seguidor.Dispose(); }
    }

    static class Rotulos
    {
        public static string Janela(int minutos, bool semanal)
        {
            if (minutos == 300) return "Sessão (5h)";
            if (minutos == 10080 || (semanal && minutos == 0)) return "Semana";
            if (minutos > 0 && minutos % 1440 == 0) return "{0} dias".T(minutos / 1440);
            if (minutos > 0 && minutos % 60 == 0) return (minutos / 60) + "h";
            return minutos > 0 ? minutos + " min" : "Limite";
        }

        // Plano do Claude pelo tipo de assinatura e o nível de limite. Desde o fim de 10/2026 o Max 20x se chama Ultra (10x a
        // semana do Pro, sem limite de 5h na conta verificada) e o Max 5x é só "Max"; os códigos não mudaram: o Ultra continua
        // "max" + default_claude_max_20x (Claude Code 2.1.319). "ultra" no código fica aceito, se um dia vier
        public static string PlanoClaude(string assinatura, string nivel)
        {
            if (string.IsNullOrEmpty(assinatura)) return null;
            string a = assinatura.ToLowerInvariant(), n = (nivel ?? "").ToLowerInvariant();
            if (a == "ultra" || n.Contains("ultra") || (a == "max" && n.EndsWith("max_20x"))) return "Ultra";
            return Plano(a);
        }

        // Plano do ChatGPT com o nome que o próprio Codex mostra (KnownPlan.display_name em openai/codex, 07/11/2026). O
        // "prolite" é o Pro de entrada e o "pro" antigo virou "Pro (More)". Os três de empresa com nome comprido perdem o
        // "Self Serve"/"CBP" (canal de venda, não plano): "Self Serve Business Usage Based" cobria o "Codex" no cartão
        public static string PlanoCodex(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            switch (p.ToLowerInvariant())
            {
                case "free": return "Free";
                case "go": return "Go";
                case "plus": return "Plus";
                case "pro": return "Pro (More)";
                case "prolite": return "Pro";
                case "promax": return "Pro (Max)";
                case "team": return "Team";
                case "self_serve_business_prolite": return "Business ProLite";
                case "self_serve_business_usage_based": return "Business Usage Based";
                case "business": return "Business";
                case "ent26": case "enterprise": case "hc": return "Enterprise";
                case "enterprise_cbp_automation": return "Enterprise (Automation)";
                case "enterprise_cbp_usage_based": return "Enterprise Usage Based";
                case "edu": case "education": return "Edu";
                case "edu_plus": return "Edu Plus";
                case "edu_pro": return "Edu Pro";
                case "sci": return "Academic Researchers";
                case "sci_plus": return "Academic Researchers Plus";
                default: return Plano(p);
            }
        }

        // Nomes comuns; código desconhecido vira palavras com inicial maiúscula ("novo_plano" → "Novo Plano")
        public static string Plano(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            switch (p.ToLowerInvariant())
            {
                case "plus": return "Plus";
                case "pro": return "Pro";
                case "team": return "Team";
                case "business": return "Business";
                case "enterprise": return "Enterprise";
                case "free": return "Free";
                case "max": return "Max";
                case "ultra": return "Ultra";
                default:
                    var palavras = p.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < palavras.Length; i++) palavras[i] = char.ToUpperInvariant(palavras[i][0]) + palavras[i].Substring(1);
                    return string.Join(" ", palavras);
            }
        }
    }
}
