using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pulso")]
[assembly: AssemblyProduct("Pulso")]
[assembly: AssemblyCompany("Lucas Dias")]
[assembly: AssemblyVersion(Pulso.Instalacao.Versao + ".0.0")]
[assembly: AssemblyFileVersion(Pulso.Instalacao.Versao + ".0.0")]

namespace Pulso
{
    // Instalação como um app comum, sem administrador, em %LOCALAPPDATA%\Programs\Pulso: pelo instalar.cmd (que compila
    // o projeto clonado e copia) ou pelo próprio Pulso.exe baixado das Releases do repositório, que se copia para lá.
    // Daí o "--registrar" cria o atalho no Menu Iniciar e a entrada em Configurações › Aplicativos (Desinstalar chama o
    // próprio exe com --desinstalar). Instalar, atualizar e desinstalar passam pelo Assistente: nenhum console à vista.
    static class Instalacao
    {
        public const string Versao = "1.4";
        const string ChaveApp = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pulso";

        public static string Pasta
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pulso"); }
        }
        static string ExeInstalado { get { return Path.Combine(Pasta, "Pulso.exe"); } }
        static string Desinstalador { get { return Path.Combine(Pasta, "desinstalar.cmd"); } } // só das versões antigas, apagado ao registrar
        static string Atalho { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pulso.lnk"); } }
        static string AtalhoAreaDeTrabalho { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pulso.lnk"); } }

        public static bool Instalado
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) return k != null && File.Exists(ExeInstalado);
            }
        }

        // Início com o Windows e barra de status apontam sempre para o Pulso instalado, mesmo se outro for aberto
        public static string ExeOficial { get { return Instalado ? ExeInstalado : Application.ExecutablePath; } }

        // Pasta do repositório (a que tem o build.cmd), de onde vêm as atualizações; nula = instalado pelo download
        public static string Origem
        {
            get
            {
                string o = null;
                try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) if (k != null) o = k.GetValue("PulsoOrigem") as string; } catch { }
                if (o == null) o = PastaDoProjeto;
                return o != null && File.Exists(Path.Combine(o, "build.cmd")) ? o : null;
            }
        }

        // Rodando de <repo>\bin, compilado pelo build.cmd (desenvolvimento)
        static string PastaDoProjeto
        {
            get
            {
                string o = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
                return o != null && File.Exists(Path.Combine(o, "build.cmd")) ? o : null;
            }
        }

        // Pulso.exe baixado e aberto de outra pasta (Downloads…): instala antes de rodar, em vez de rodar dali
        public static bool PrecisaInstalar
        {
            get { return PastaDoProjeto == null && !string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(ExeInstalado), StringComparison.OrdinalIgnoreCase); }
        }

        // Pulso.exe baixado: pelo assistente (sem console) pergunta — na primeira vez, com os atalhos a criar; por cima de
        // um instalado, se atualiza —, fecha o Pulso aberto, copia para a pasta de programas do usuário, registra pelo
        // instalado e, no OK do fim, o abre. Recusar não instala nada.
        // ⚠️ O registro (que grava o início com o Windows) roda no exe JÁ INSTALADO, não neste: um exe fora da pasta de
        // programas gravando a chave Run foi o que o Defender acusou como persistência (07/10/2026).
        public static int InstalarDaqui()
        {
            bool porCima = File.Exists(ExeInstalado);
            var a = new Assistente(porCima ? Assistente.Modo.AtualizarPorCima : Assistente.Modo.Instalar, porCima ? VersaoInstalada() : null, Versao);
            a.Fazer = j =>
            {
                j.Etapa(10, "Fechando o Pulso aberto…".T());
                FecharAberto();
                j.Etapa(35, "Copiando o Pulso para a pasta de programas…".T());
                Directory.CreateDirectory(Pasta);
                Copiar(Application.ExecutablePath, ExeInstalado);
                j.Etapa(65, "Criando os atalhos e a entrada em Aplicativos…".T());
                // Por cima: sem as opções, só renova os atalhos que existem (o que a pessoa apagou continua apagado)
                string registrar = "--registrar" + (porCima ? "" : " --menu-iniciar=" + (j.Opcao1 ? 1 : 0) + " --area-de-trabalho=" + (j.Opcao2 ? 1 : 0));
                using (var p = Process.Start(new ProcessStartInfo(ExeInstalado, registrar) { UseShellExecute = false, WorkingDirectory = Pasta }))
                    if (!p.WaitForExit(60000) || p.ExitCode != 0) return "O registro do Pulso no Windows não terminou.".T();
                j.Etapa(100, "Pronto.".T());
                return null;
            };
            a.AoConcluir = delegate { Process.Start(new ProcessStartInfo(ExeInstalado) { UseShellExecute = false, WorkingDirectory = Pasta }); };
            Application.Run(a);
            return a.Concluiu ? 0 : 1;
        }

        // Só o número: o commit não diz nada a quem usa (pedido do dono)
        static string VersaoInstalada()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) return k == null ? null : k.GetValue("DisplayVersion") as string; }
            catch { return null; }
        }

        // Cópia com algumas tentativas (o Pulso que acabou de fechar pode segurar o arquivo por um instante), sem a marca
        // de "baixado da internet": quem instala já confirmou o aviso do Windows ao abrir o download
        static void Copiar(string de, string para)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(de, para, true); break; }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(250); }
            }
            DeleteFile(para + ":Zone.Identifier");
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool DeleteFile(string nome);

        // --registrar [origem] [--menu-iniciar=0|1] [--area-de-trabalho=0|1]: roda já da pasta instalada, logo depois da cópia
        // (instalar.cmd e a atualização pelo app, que precisa registrar pela versão NOVA). Primeira instalação sem as opções
        // (instalar.cmd) mostra a pergunta do assistente; cancelar devolve 2.
        public static int Registrar(string[] args)
        {
            string origem = null;
            bool? menu = null, area = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--menu-iniciar=")) menu = args[i].EndsWith("1");
                else if (args[i].StartsWith("--area-de-trabalho=")) area = args[i].EndsWith("1");
                else if (!args[i].StartsWith("--")) origem = args[i];
            }
            bool primeira;
            using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) primeira = k == null;
            if (primeira && !menu.HasValue)
                using (var j = new Assistente(Assistente.Modo.Instalar))
                {
                    j.ShowDialog();
                    if (!j.Concluiu) return 2;
                    menu = j.Opcao1; area = j.Opcao2;
                }
            string erro = RegistrarAgora(origem, menu, area);
            if (erro == null) return 0;
            MessageBox.Show("Não foi possível concluir a instalação do Pulso:\n\n{0}".T(erro), "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // Atalhos, a entrada em Configurações › Aplicativos e o início com o Windows. Ao atualizar (menu/area nulos), só
        // renova os atalhos que existem: o que a pessoa apagou continua apagado. Devolve o erro, ou nulo.
        static string RegistrarAgora(string origem, bool? menu, bool? area)
        {
            bool primeira;
            using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) primeira = k == null;
            try
            {
                Caminhos.Garantir();
                // O desinstalar.cmd das versões antigas: a desinstalação agora é o próprio exe, sem console
                if (File.Exists(Desinstalador)) File.Delete(Desinstalador);
                AjustarAtalho(Atalho, menu ?? (primeira || File.Exists(Atalho)));
                AjustarAtalho(AtalhoAreaDeTrabalho, area ?? File.Exists(AtalhoAreaDeTrabalho));
                using (var k = Registry.CurrentUser.CreateSubKey(ChaveApp))
                {
                    k.SetValue("DisplayName", "Pulso");
                    k.SetValue("DisplayVersion", Versao);
                    k.SetValue("Publisher", "Lucas Dias");
                    k.SetValue("DisplayIcon", ExeInstalado + ",0");
                    k.SetValue("InstallLocation", Pasta);
                    k.SetValue("UninstallString", "\"" + ExeInstalado + "\" --desinstalar");
                    k.SetValue("URLInfoAbout", "https://github.com/LucasDias777/Pulso");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(ExeInstalado).Length / 1024), RegistryValueKind.DWord);
                    if (k.GetValue("InstallDate") == null) k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    if (!string.IsNullOrEmpty(origem)) k.SetValue("PulsoOrigem", Path.GetFullPath(origem));
                }
                Config.Carregar();
                Integracao.IniciarComWindows(Config.Atual.IniciarComWindows);
                Log.Info("instalado em " + Pasta + " (versão " + Versao + ")");
                return null;
            }
            catch (Exception e)
            {
                Log.Erro("registrar instalação", e);
                return e.Message;
            }
        }

        // Cria (ou renova, apontando para o instalado) ou apaga um atalho
        static void AjustarAtalho(string arquivo, bool ter)
        {
            if (ter) CriarAtalho(arquivo);
            else try { if (File.Exists(arquivo)) File.Delete(arquivo); } catch { }
        }

        // Atalho pelo WScript.Shell (sem dependência extra)
        static void CriarAtalho(string arquivo)
        {
            try
            {
                var tipo = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(tipo);
                object lnk = tipo.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { arquivo });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { ExeInstalado });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Pasta });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Consumo do Claude Code e do Codex ao vivo".T() });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception e) { Log.Erro("atalho " + arquivo, e); }
        }

        // --desinstalar (Configurações › Aplicativos do Windows e o botão Desinstalar…): pelo assistente, sem console.
        // A pasta do programa sai depois, num cmd ESCONDIDO que espera este processo fechar — um exe não apaga a si mesmo
        // aberto. 1 = cancelado ou falhou.
        public static int Desinstalar()
        {
            var a = new Assistente(Assistente.Modo.Desinstalar);
            a.Fazer = j =>
            {
                bool apagarDados = j.Opcao1;
                j.Etapa(10, "Fechando o Pulso…".T());
                FecharAberto();
                j.Etapa(30, "Desligando o início com o Windows e as integrações…".T());
                Integracao.IniciarComWindows(false);
                try { if (Integracao.BarraInstalada()) Integracao.RemoverBarra(); } catch { }
                foreach (var p in Limites.Provedores)
                    try { if (Integracao.LimiteInstalado(p)) Integracao.RemoverLimite(p); } catch { }
                j.Etapa(55, "Removendo os atalhos…".T());
                AjustarAtalho(Atalho, false);
                AjustarAtalho(AtalhoAreaDeTrabalho, false);
                j.Etapa(75, "Removendo a entrada em Aplicativos…".T());
                try { Registry.CurrentUser.DeleteSubKeyTree(ChaveApp, false); } catch { }
                if (apagarDados && Directory.Exists(Caminhos.Dados))
                {
                    j.Etapa(90, "Apagando as configurações e o histórico…".T());
                    try { Directory.Delete(Caminhos.Dados, true); } catch { }
                }
                j.Etapa(100, "Pronto.".T());
                return null;
            };
            a.AoConcluir = ApagarPastaDepois;
            Application.Run(a);
            return a.Concluiu ? 0 : 1;
        }

        static void ApagarPastaDepois()
        {
            if (!Directory.Exists(Pasta)) return;
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "/d /c ping -n 3 127.0.0.1 >nul & rd /s /q \"" + Pasta + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() });
        }

        public static void AbrirDesinstalador()
        {
            try { Process.Start(new ProcessStartInfo(ExeOficial, "--desinstalar") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() }); }
            catch (Exception e) { Log.Erro("abrir desinstalador", e); }
        }

        // Atualização pelo app: troca o exe instalado pelo novo (o Windows deixa renomear um exe em uso, não apagar) e
        // registra pela versão nova. Se algo falhar, o exe antigo volta para o lugar. Quem abre o novo é ReabrirNovo, no OK.
        public static void Trocar(string novo)
        {
            string velho = ExeInstalado + ".old";
            if (File.Exists(velho)) File.Delete(velho);
            File.Move(ExeInstalado, velho);
            try
            {
                File.Move(novo, ExeInstalado);
                using (var p = Process.Start(new ProcessStartInfo(ExeInstalado, "--registrar") { UseShellExecute = false }))
                    if (!p.WaitForExit(60000) || p.ExitCode != 0) throw new InvalidOperationException("--registrar");
            }
            catch
            {
                try { if (File.Exists(ExeInstalado)) File.Delete(ExeInstalado); File.Move(velho, ExeInstalado); } catch { }
                try { if (File.Exists(novo)) File.Delete(novo); } catch { }
                throw;
            }
        }

        // A versão nova espera este processo fechar e apaga o exe antigo dele
        public static void ReabrirNovo()
        {
            Process.Start(new ProcessStartInfo(ExeInstalado, "--apos-atualizar " + Process.GetCurrentProcess().Id) { UseShellExecute = false, WorkingDirectory = Pasta });
        }

        // --apos-atualizar <pid>: espera o Pulso que se atualizou fechar e apaga o exe antigo dele
        public static void AposAtualizar(string pid)
        {
            int id;
            if (int.TryParse(pid, out id))
                try { using (var p = Process.GetProcessById(id)) p.WaitForExit(20000); }
                catch (ArgumentException) { } // já fechou
            string velho = ExeInstalado + ".old";
            for (int i = 0; i < 20 && File.Exists(velho); i++)
            {
                try { File.Delete(velho); }
                catch (IOException) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { Thread.Sleep(250); }
            }
        }

        // --sair: pede ao Pulso aberto para fechar e espera ele soltar a instância (até 10 s)
        public static void FecharAberto()
        {
            if (!Mensagens.Enviar(6, "sair")) return;
            for (int i = 0; i < 50; i++)
            {
                Mutex m;
                if (!Mutex.TryOpenExisting(@"Local\Pulso", out m)) return;
                m.Dispose();
                Thread.Sleep(200);
            }
        }
    }
}
