using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pulso
{
    // Atualização pelo repositório de onde o Pulso foi compilado: compara o commit desta compilação com a branch
    // main do GitHub (git fetch). Atualizar = atualizar.cmd, que baixa, compila e reinstala.
    // A procura automática (2 min depois de abrir e a cada 12 h) nunca abre janela de login; o botão pode abrir.
    static class Atualizacao
    {
        public enum Situacao { Desconhecida, Procurando, EmDia, Disponivel, Erro }

        public static Situacao Estado { get; private set; }
        // Montado a cada leitura, no idioma da vez
        public static string Detalhe { get { var d = detalhe; return d == null ? null : d(); } }
        public static event Action Mudou;

        static SynchronizationContext ui;
        static Timer relogio;
        static int procurando;
        static Func<string> detalhe;

        public static string Commit { get { return Compilacao.Commit; } }

        public static void Iniciar(SynchronizationContext contexto)
        {
            ui = contexto;
            relogio = new Timer(delegate { Procurar(false); }, null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(12));
        }

        public static void Procurar(bool interativo)
        {
            string origem = Instalacao.Origem;
            if (origem == null || Interlocked.Exchange(ref procurando, 1) == 1) return;
            Definir(Situacao.Procurando, null);
            Task.Factory.StartNew(delegate
            {
                try
                {
                    string erro;
                    if (Git(origem, "fetch --quiet origin main", interativo, out erro) == null) { Falhou(erro); return; }
                    string base_ = string.IsNullOrEmpty(Commit) ? "HEAD" : Commit;
                    string n = Git(origem, "rev-list --count " + base_ + "..origin/main", false, out erro);
                    int novas;
                    if (n == null || !int.TryParse(n.Trim(), out novas)) { Falhou(erro); return; }
                    if (novas == 0) Definir(Situacao.EmDia, null);
                    else Definir(Situacao.Disponivel, () => novas == 1 ? "1 mudança nova".T() : "{0} mudanças novas".T(novas));
                    Log.Info("atualização: " + (novas == 0 ? "em dia" : novas + " commit(s) novos"));
                }
                catch (Exception e) { Log.Erro("procurar atualização", e); Definir(Situacao.Erro, () => e.Message); }
                finally { procurando = 0; }
            });
        }

        // Abre o atualizar.cmd numa janela de console (dá para acompanhar); ele fecha este Pulso e abre o novo
        public static void Aplicar()
        {
            string origem = Instalacao.Origem;
            if (origem == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/c \"\"" + Path.Combine(origem, "atualizar.cmd") + "\"\"") { UseShellExecute = false, WorkingDirectory = origem });
            }
            catch (Exception e) { Log.Erro("abrir atualizar.cmd", e); }
        }

        static void Falhou(string erro)
        {
            Log.Info("atualização: não consultou (" + (erro ?? "").Trim().Replace('\n', ' ') + ")");
            Definir(Situacao.Erro, () => Motivo(erro));
        }

        static string Motivo(string erro)
        {
            if (string.IsNullOrEmpty(erro)) return "Não foi possível consultar o GitHub.".T();
            if (erro.Contains("not found") || erro.Contains("Authentication") || erro.Contains("could not read Username") || erro.Contains("terminal prompts disabled"))
                return "O GitHub não liberou o acesso ao repositório. Clique em Procurar atualização para entrar com a sua conta.".T();
            if (erro.Contains("Could not resolve host") || erro.Contains("unable to access")) return "Sem conexão com o GitHub.".T();
            string l = erro.Trim().Split('\n')[0].Trim();
            // Os erros escritos pelo Git() abaixo estão na tabela; o texto do próprio git passa igual
            return (l.Length > 140 ? l.Substring(0, 140) + "…" : l).T();
        }

        // Saída do git, ou nulo se falhou (erro = o que ele escreveu)
        static string Git(string pasta, string args, bool interativo, out string erro)
        {
            erro = null;
            var psi = new ProcessStartInfo("git", "-C \"" + pasta + "\" " + args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            if (!interativo) psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
            try
            {
                using (var p = Process.Start(psi))
                {
                    var saida = p.StandardOutput.ReadToEndAsync();
                    var falha = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(interativo ? 180000 : 60000)) { try { p.Kill(); } catch { } erro = "O GitHub demorou demais para responder."; return null; }
                    erro = falha.Result;
                    return p.ExitCode == 0 ? saida.Result : null;
                }
            }
            catch (System.ComponentModel.Win32Exception) { erro = "Git não encontrado neste computador."; return null; }
        }

        static void Definir(Situacao s, Func<string> d)
        {
            Estado = s;
            detalhe = d;
            var c = ui;
            Action avisar = delegate { var h = Mudou; if (h != null) h(); };
            if (c != null) c.Post(delegate { avisar(); }, null); else avisar();
        }
    }
}
