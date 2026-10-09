using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Pulso
{
    // Atualização. Instalado pelo projeto: compara o commit desta compilação com a branch main do GitHub (git fetch);
    // atualizar = atualizar.cmd, que baixa, compila e reinstala. Instalado pelo download: compara com a última Release
    // do repositório, que é público (sem login); atualizar = baixar o Pulso.exe dela, trocar pelo instalado e reabrir.
    // A procura automática (2 min depois de abrir e a cada 12 h) nunca abre janela de login; o botão pode abrir.
    static class Atualizacao
    {
        public enum Situacao { Desconhecida, Procurando, EmDia, Disponivel, Baixando, Erro }

        const string Repo = "LucasDias777/Pulso";

        public static Situacao Estado { get; private set; }
        // Montado a cada leitura, no idioma da vez
        public static string Detalhe { get { var d = detalhe; return d == null ? null : d(); } }
        public static event Action Mudou;
        public static event Action NovaVersao;   // achada pela procura automática, uma vez por versão: a notificação do Windows
        public static bool PeloDownload { get { return Instalacao.Origem == null; } }

        static SynchronizationContext ui;
        static Action sair;
        static Timer relogio;
        static int ocupado;
        static Func<string> detalhe;
        static object exeNovo;                  // o Pulso.exe da última Release, quando ela é mais nova
        static string avisada;                  // a versão nova já anunciada (ou já vista pelo botão)
        static HttpClient http;

        public static string Commit { get { return Compilacao.Commit; } }

        public static void Iniciar(SynchronizationContext contexto, Action sairDoApp)
        {
            ui = contexto;
            sair = sairDoApp;
            relogio = new Timer(delegate { Procurar(false); }, null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(12));
        }

        public static void Procurar(bool interativo)
        {
            string origem = Instalacao.Origem;
            if (Interlocked.Exchange(ref ocupado, 1) == 1) return;
            Definir(Situacao.Procurando, null);
            Task.Factory.StartNew(delegate
            {
                try
                {
                    if (origem != null) PeloGit(origem, interativo);
                    else PelaRelease(interativo);
                }
                catch (Exception e) { Log.Erro("procurar atualização", e); Definir(Situacao.Erro, () => e.Message); }
                finally { ocupado = 0; }
            });
        }

        static void PeloGit(string origem, bool interativo)
        {
            string erro;
            if (Git(origem, "fetch --quiet origin main", interativo, out erro) == null) { Falhou(erro); return; }
            string base_ = string.IsNullOrEmpty(Commit) ? "HEAD" : Commit;
            string n = Git(origem, "rev-list --count " + base_ + "..origin/main", false, out erro);
            int novas;
            if (n == null || !int.TryParse(n.Trim(), out novas)) { Falhou(erro); return; }
            Encontrou(novas, "main+" + novas, interativo);
        }

        static void PelaRelease(bool interativo)
        {
            int status;
            var rel = Api("releases/latest", out status);
            string tag = Json.Str(rel, "tag_name");
            var exe = (Json.Arr(rel, "assets") ?? new object[0]).FirstOrDefault(a => Json.Str(a, "name") == "Pulso.exe");
            if (tag == null || exe == null) { FalhouApi(status); return; }
            int novas = 0;
            if (tag != Instalacao.Versao + "-" + Commit)
            {
                if (string.IsNullOrEmpty(Commit)) novas = 1;
                else
                {
                    // A Release à frente desta compilação ("ahead") é mais nova; "identical" ou "behind", em dia
                    var c = Api("compare/" + Commit + "..." + Uri.EscapeDataString(tag), out status);
                    string s = Json.Str(c, "status");
                    if (s == null) { FalhouApi(status); return; }
                    novas = s == "ahead" || s == "diverged" ? Math.Max(1, (int)(Json.Num(c, "ahead_by") ?? 1)) : 0;
                }
            }
            exeNovo = novas > 0 ? exe : null;
            Encontrou(novas, tag, interativo);
        }

        // versao: o que identifica a versão nova (a tag da Release, ou quantos commits a main está à frente)
        static void Encontrou(int novas, string versao, bool interativo)
        {
            if (novas == 0) Definir(Situacao.EmDia, null);
            else Definir(Situacao.Disponivel, () => novas == 1 ? "1 mudança nova".T() : "{0} mudanças novas".T(novas));
            Log.Info("atualização: " + (novas == 0 ? "em dia" : novas + " commit(s) novos"));
            // Notificação só na procura automática (quem clicou no botão já está vendo) e uma vez por versão
            if (novas == 0 || versao == avisada) return;
            avisada = versao;
            if (interativo) return;
            Log.Info("atualização: avisada pela notificação do Windows");
            var c = ui;
            Action avisar = delegate { var h = NovaVersao; if (h != null) h(); };
            if (c != null) c.Post(delegate { avisar(); }, null); else avisar();
        }

        // Instalado pelo projeto: abre o atualizar.cmd numa janela de console (dá para acompanhar); ele fecha este Pulso
        // e abre o novo. Pelo download: baixa a Release, troca o exe e reabre.
        public static void Aplicar()
        {
            string origem = Instalacao.Origem;
            if (origem == null) { Baixar(); return; }
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/c \"\"" + Path.Combine(origem, "atualizar.cmd") + "\"\"") { UseShellExecute = false, WorkingDirectory = origem });
            }
            catch (Exception e) { Log.Erro("abrir atualizar.cmd", e); }
        }

        static void Baixar()
        {
            var exe = exeNovo;
            if (exe == null || Interlocked.Exchange(ref ocupado, 1) == 1) return;
            Definir(Situacao.Baixando, null);
            Task.Factory.StartNew(delegate
            {
                try
                {
                    Instalacao.TrocarPor(BaixarExe(exe));
                    Log.Info("atualização: versão nova instalada, reabrindo");
                    ui.Post(delegate { sair(); }, null);
                }
                catch (Exception e)
                {
                    Log.Erro("instalar atualização", e);
                    string motivo = e.GetBaseException().Message;
                    Definir(Situacao.Erro, () => "Não foi possível instalar a atualização: {0}".T(motivo));
                }
                finally { ocupado = 0; }
            });
        }

        // Grava o Pulso.exe da Release ao lado do instalado, só se o tamanho e o SHA-256 publicados pelo GitHub conferirem
        // e se ele for mesmo o Pulso
        static string BaixarExe(object exe)
        {
            byte[] dados;
            using (var r = Http.GetAsync(Json.Str(exe, "browser_download_url")).Result)
            {
                r.EnsureSuccessStatusCode();
                dados = r.Content.ReadAsByteArrayAsync().Result;
            }
            string digest = Json.Str(exe, "digest");
            bool confere = dados.LongLength == (long)(Json.Num(exe, "size") ?? -1);
            if (confere && digest != null && digest.StartsWith("sha256:"))
                using (var sha = SHA256.Create())
                    confere = BitConverter.ToString(sha.ComputeHash(dados)).Replace("-", "").Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase);
            string arq = Path.Combine(Instalacao.Pasta, "Pulso.exe.novo");
            if (confere)
            {
                File.WriteAllBytes(arq, dados);
                try { confere = AssemblyName.GetAssemblyName(arq).Name == "Pulso"; }
                catch (BadImageFormatException) { confere = false; }
                if (!confere) File.Delete(arq);
            }
            if (!confere) throw new InvalidDataException("O arquivo baixado não confere com a versão publicada.".T());
            return arq;
        }

        static HttpClient Http
        {
            get
            {
                if (http == null)
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // + TLS 1.3
                    var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                    h.DefaultRequestHeaders.UserAgent.ParseAdd("Pulso/" + Instalacao.Versao + " (Windows)");
                    http = h;
                }
                return http;
            }
        }

        // JSON da API pública do GitHub, ou nulo (status = código HTTP; 0 = sem resposta)
        static object Api(string caminho, out int status)
        {
            status = 0;
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repo + "/" + caminho);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            try
            {
                using (var r = Http.SendAsync(req).Result)
                {
                    status = (int)r.StatusCode;
                    return r.IsSuccessStatusCode ? Json.Parse(r.Content.ReadAsStringAsync().Result) : null;
                }
            }
            catch (AggregateException e)
            {
                status = e.InnerException is TaskCanceledException ? -1 : 0;
                return null;
            }
        }

        static void FalhouApi(int status)
        {
            Log.Info("atualização: não consultou as Releases (HTTP " + status + ")");
            Definir(Situacao.Erro, () =>
                status == 0 ? "Sem conexão com o GitHub.".T() :
                status == -1 ? "O GitHub demorou demais para responder. Tente novamente mais tarde.".T() :
                status == 403 || status == 429 ? "O GitHub limitou as consultas. Tente de novo mais tarde.".T() :
                status == 404 ? "Nenhuma versão publicada no GitHub.".T() :
                "Não foi possível consultar o GitHub.".T());
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
                    if (!p.WaitForExit(interativo ? 180000 : 60000)) { try { p.Kill(); } catch { } erro = "O GitHub demorou demais para responder. Tente novamente mais tarde."; return null; }
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
