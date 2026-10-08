using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
    // Instalação como um app comum, sem administrador: o instalar.cmd compila e copia o Pulso.exe para
    // %LOCALAPPDATA%\Programs\Pulso; daí o "--registrar" cria o atalho no Menu Iniciar e a entrada em
    // Configurações › Aplicativos (Desinstalar chama o desinstalar.cmd da pasta instalada).
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

        public static bool Instalado
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) return k != null && File.Exists(ExeInstalado);
            }
        }

        // Início com o Windows e barra de status apontam sempre para o Pulso instalado, mesmo se outro for aberto
        public static string ExeOficial { get { return Instalado ? ExeInstalado : Application.ExecutablePath; } }

        // Pasta do repositório (a que tem o build.cmd), de onde vêm as atualizações
        public static string Origem
        {
            get
            {
                string o = null;
                try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) if (k != null) o = k.GetValue("PulsoOrigem") as string; } catch { }
                if (o == null) o = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath)); // rodando de <repo>\bin
                return o != null && File.Exists(Path.Combine(o, "build.cmd")) ? o : null;
            }
        }

        // --registrar <origem>: roda já da pasta instalada, logo depois da cópia
        public static int Registrar(string origem)
        {
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
                CriarAtalho();
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
                MessageBox.Show("Não foi possível concluir a instalação do Pulso:\n\n" + e.Message, "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // Atalho do Menu Iniciar pelo WScript.Shell (sem dependência extra)
        static void CriarAtalho()
        {
            try
            {
                var tipo = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(tipo);
                object lnk = tipo.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Atalho });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { ExeInstalado });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Pasta });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Consumo do Claude Code e do Codex ao vivo" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception e) { Log.Erro("atalho do Menu Iniciar", e); }
        }

        // --desinstalar (chamado pelo desinstalar.cmd, que apaga a pasta depois). 1 = cancelado.
        public static int Desinstalar()
        {
            if (MessageBox.Show("Desinstalar o Pulso deste computador?\n\nA cápsula, o ícone da bandeja e o início com o Windows serão removidos.",
                    "Desinstalar o Pulso", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
            FecharAberto();
            Integracao.IniciarComWindows(false);
            try { if (Integracao.BarraInstalada()) Integracao.RemoverBarra(); } catch { }
            try { if (File.Exists(Atalho)) File.Delete(Atalho); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(ChaveApp, false); } catch { }
            if (Directory.Exists(Caminhos.Dados) &&
                MessageBox.Show("Apagar também as suas configurações e o histórico do Pulso?\n\n" + Caminhos.Dados +
                    "\n\nEscolha Não para mantê-los, caso pretenda instalar de novo.",
                    "Desinstalar o Pulso", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Directory.Delete(Caminhos.Dados, true); } catch { }
            }
            MessageBox.Show("O Pulso foi desinstalado.", "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
