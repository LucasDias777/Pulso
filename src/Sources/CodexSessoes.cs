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

        public void Iniciar()
        {
            CarregarUltimo();
            seguidor = new Seguidor(Caminhos.CodexSessoes, AoLinha, TimeSpan.FromMinutes(30));
            seguidor.Iniciar();
        }

        // Na partida, o evento de limite mais recente entre os arquivos dos últimos dias
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
                foreach (var f in recentes)
                {
                    var linhas = Seguidor.Cauda(f.FullName, 768 * 1024);
                    for (int i = linhas.Count - 1; i >= 0; i--)
                    {
                        if (linhas[i].IndexOf("\"rate_limits\"", StringComparison.Ordinal) < 0) continue;
                        if (Limites(Json.Parse(linhas[i]))) break;
                    }
                }
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

            bool mudou = false;
            if (linha.IndexOf("\"rate_limits\"", StringComparison.Ordinal) >= 0)
                mudou = Limites(Json.Parse(linha));

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

        // Aplica o rate_limits de um token_count; ignora evento mais velho que o já mostrado.
        bool Limites(object o)
        {
            var rl = Json.Obj(o, "payload", "rate_limits");
            if (rl == null) return false;
            DateTime quando = Json.Data(o, "timestamp") ?? DateTime.UtcNow;
            lock (Estado.Trava)
            {
                if (quando < ultimoEvento) return false;
                ultimoEvento = quando;
                var p = Estado.Codex;
                p.Janelas.Clear();
                AdicionarJanela(p, rl, "primary");
                AdicionarJanela(p, rl, "secondary");
                string plano = Json.Str(rl, "plan_type");
                if (plano != null) p.Plano = Rotulos.Plano(plano);
                bool creditos = Json.Bool(rl, "credits", "has_credits") == true;
                string saldo = Json.Str(rl, "credits", "balance");
                p.Nota = creditos && saldo != null && saldo != "0" ? "Créditos: {0}".T(saldo) : null;
                p.Confirmado = quando;
                var principal = p.PorId("primary");
                if (principal != null) p.RitmoHora = Ritmo.Registrar("codex", quando, principal.Usado);
                p.Fonte = "sessão do Codex";
                p.Erro = null;
            }
            return true;
        }

        static void AdicionarJanela(Provedor p, IDictionary<string, object> rl, string chave)
        {
            var w = Json.Obj(rl, chave);
            if (w == null) return;
            int minutos = (int)(Json.Num(w, "window_minutes") ?? 0);
            var j = new Janela
            {
                Id = chave,
                Minutos = minutos,
                Semanal = minutos >= 7 * 24 * 60 || chave == "secondary",
                Usado = Math.Max(0, Math.Min(1, (Json.Num(w, "used_percent") ?? 0) / 100.0)),
                ResetaEm = Json.Data(w, "resets_at"),
            };
            j.Rotulo = Rotulos.Janela(minutos, j.Semanal);
            p.Janelas.Add(j);
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
                default: return char.ToUpperInvariant(p[0]) + p.Substring(1);
            }
        }
    }
}
