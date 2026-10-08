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
    // Daí o "--registrar" cria o atalho no Menu Iniciar e a entrada em Configurações › Aplicativos (Desinstalar chama o desinstalar.cmd).
    static class Instalacao
    {
        public const string Versao = "1.1";
        const string ChaveApp = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pulso";

        public static string Pasta
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pulso"); }
        }
        static string ExeInstalado { get { return Path.Combine(Pasta, "Pulso.exe"); } }
        static string Desinstalador { get { return Path.Combine(Pasta, "desinstalar.cmd"); } }
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

        // Pulso.exe baixado: pergunta (na primeira vez, com os atalhos a criar), fecha o Pulso aberto, copia para a pasta
        // de programas do usuário, registra pelo próprio instalado (como o instalar.cmd) e o abre. Recusar não instala nada.
        public static int InstalarDaqui()
        {
            string registrar = "--registrar";
            if (File.Exists(ExeInstalado))
            {
                if (MessageBox.Show("Atualizar o Pulso instalado com esta versão?".T(), "Pulso", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            }
            else
            {
                using (var j = new OpcoesInstalacao())
                {
                    if (j.ShowDialog() != DialogResult.OK) return 0;
                    registrar += " --menu-iniciar=" + (j.MenuIniciar ? 1 : 0) + " --area-de-trabalho=" + (j.AreaDeTrabalho ? 1 : 0);
                }
            }
            try
            {
                FecharAberto();
                Directory.CreateDirectory(Pasta);
                Copiar(Application.ExecutablePath, ExeInstalado);
                using (var p = Process.Start(new ProcessStartInfo(ExeInstalado, registrar) { UseShellExecute = false }))
                    if (!p.WaitForExit(60000) || p.ExitCode != 0) return 1;
                Process.Start(new ProcessStartInfo(ExeInstalado) { UseShellExecute = false, WorkingDirectory = Pasta });
                return 0;
            }
            catch (Exception e)
            {
                Log.Erro("instalar pelo download", e);
                MessageBox.Show("Não foi possível concluir a instalação do Pulso:\n\n{0}".T(e.Message), "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
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

        // --registrar [origem] [--menu-iniciar=0|1] [--area-de-trabalho=0|1]: roda já da pasta instalada, logo depois da cópia.
        // Primeira instalação sem as opções (instalar.cmd) mostra a mesma janela do exe baixado; cancelar devolve 2.
        // Ao atualizar, só renova os atalhos que existem: o que a pessoa apagou continua apagado.
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
                using (var j = new OpcoesInstalacao())
                {
                    if (j.ShowDialog() != DialogResult.OK) return 2;
                    menu = j.MenuIniciar; area = j.AreaDeTrabalho;
                }
            try
            {
                Caminhos.Garantir();
                File.WriteAllText(Desinstalador,
                    "@echo off\r\n" +
                    "rem Desinstala o Pulso (usado por Configuracoes > Aplicativos do Windows)\r\n" +
                    "\"%~dp0Pulso.exe\" --desinstalar\r\n" +
                    "if errorlevel 1 exit /b 0\r\n" +
                    "cd /d \"%TEMP%\"\r\n" +
                    "(goto) 2>nul & rd /s /q \"%~dp0\"\r\n");
                AjustarAtalho(Atalho, menu ?? (primeira || File.Exists(Atalho)));
                AjustarAtalho(AtalhoAreaDeTrabalho, area ?? File.Exists(AtalhoAreaDeTrabalho));
                using (var k = Registry.CurrentUser.CreateSubKey(ChaveApp))
                {
                    k.SetValue("DisplayName", "Pulso");
                    k.SetValue("DisplayVersion", Versao);
                    k.SetValue("Publisher", "Lucas Dias");
                    k.SetValue("DisplayIcon", ExeInstalado + ",0");
                    k.SetValue("InstallLocation", Pasta);
                    k.SetValue("UninstallString", "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /c \"\"" + Desinstalador + "\"\"");
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
                return 0;
            }
            catch (Exception e)
            {
                Log.Erro("registrar instalação", e);
                MessageBox.Show("Não foi possível concluir a instalação do Pulso:\n\n{0}".T(e.Message), "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
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

        // --desinstalar (chamado pelo desinstalar.cmd, que apaga a pasta depois). 1 = cancelado.
        public static int Desinstalar()
        {
            if (MessageBox.Show("Desinstalar o Pulso deste computador?\n\nA cápsula, o ícone da bandeja e o início com o Windows serão removidos.".T(),
                    "Desinstalar o Pulso".T(), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
            FecharAberto();
            Integracao.IniciarComWindows(false);
            try { if (Integracao.BarraInstalada()) Integracao.RemoverBarra(); } catch { }
            AjustarAtalho(Atalho, false);
            AjustarAtalho(AtalhoAreaDeTrabalho, false);
            try { Registry.CurrentUser.DeleteSubKeyTree(ChaveApp, false); } catch { }
            if (Directory.Exists(Caminhos.Dados) &&
                MessageBox.Show("Apagar também as suas configurações e o histórico do Pulso?\n\n{0}\n\nEscolha Não para mantê-los, caso pretenda instalar de novo.".T(Caminhos.Dados),
                    "Desinstalar o Pulso".T(), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Directory.Delete(Caminhos.Dados, true); } catch { }
            }
            MessageBox.Show("O Pulso foi desinstalado.".T(), "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        public static void AbrirDesinstalador()
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c \"\"" + Desinstalador + "\"\"") { UseShellExecute = false }); }
            catch (Exception e) { Log.Erro("abrir desinstalador", e); }
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
