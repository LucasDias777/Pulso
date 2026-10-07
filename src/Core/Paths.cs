using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Pulso
{
    static class Caminhos
    {
        public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly string Dados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulso");

        public static string Config { get { return Path.Combine(Dados, "config.json"); } }
        public static string Estado { get { return Path.Combine(Dados, "estado.json"); } }
        public static string LogArquivo { get { return Path.Combine(Dados, "pulso.log"); } }

        // Respeita CLAUDE_CONFIG_DIR / CODEX_HOME, como o próprio Claude Code e o Codex
        public static string ClaudeDir
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return string.IsNullOrEmpty(v) ? Path.Combine(Home, ".claude") : v;
            }
        }
        public static string ClaudeProjetos { get { return Path.Combine(ClaudeDir, "projects"); } }
        public static string ClaudeCredenciais { get { return Path.Combine(ClaudeDir, ".credentials.json"); } }
        public static string ClaudeSettings { get { return Path.Combine(ClaudeDir, "settings.json"); } }

        public static string CodexDir
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("CODEX_HOME");
                return string.IsNullOrEmpty(v) ? Path.Combine(Home, ".codex") : v;
            }
        }
        public static string CodexSessoes { get { return Path.Combine(CodexDir, "sessions"); } }
        public static string CodexAuth { get { return Path.Combine(CodexDir, "auth.json"); } }

        public static void Garantir() { Directory.CreateDirectory(Dados); }

        // Escrita atômica: grava ao lado e troca, para um leitor nunca ver o arquivo pela metade.
        public static void GravarAtomico(string arq, string conteudo)
        {
            string tmp = arq + ".tmp";
            File.WriteAllText(tmp, conteudo, new UTF8Encoding(false));
            if (File.Exists(arq)) File.Replace(tmp, arq, null);
            else File.Move(tmp, arq);
        }

        // Lê arquivo que outro processo mantém aberto para escrita
        public static string LerCompartilhado(string arq)
        {
            using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }

    static class Log
    {
        static readonly object Trava = new object();
        const long Limite = 512 * 1024;

        public static void Info(string msg)
        {
            try
            {
                lock (Trava)
                {
                    string arq = Caminhos.LogArquivo;
                    var fi = new FileInfo(arq);
                    if (fi.Exists && fi.Length > Limite)
                    {
                        string velho = arq + ".1";
                        if (File.Exists(velho)) File.Delete(velho);
                        File.Move(arq, velho);
                    }
                    File.AppendAllText(arq, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" +
                        Thread.CurrentThread.ManagedThreadId + "] " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Erro(string onde, Exception e)
        {
            Info("ERRO " + onde + ": " + e.GetType().Name + ": " + e.Message);
        }
    }
}
