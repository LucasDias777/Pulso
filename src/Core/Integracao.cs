using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulso
{
    // Integrações que mexem fora da pasta do Pulso. Todas são ligadas por ação da pessoa nas Configurações.
    static class Integracao
    {
        static string Exe { get { return Instalacao.ExeOficial; } }
        static string Comando { get { return "\"" + Exe.Replace("\\", "/") + "\" --statusline"; } }

        // ---- Barra de status do Claude Code (terminal) ----
        // Quando o Claude Code roda no terminal, ele entrega à barra de status o consumo exato a cada resposta
        // (rate_limits). O Pulso vira essa barra: recebe o valor na hora e ainda escreve um resumo no terminal.

        public static bool BarraInstalada()
        {
            try { return File.Exists(Caminhos.ClaudeSettings) && File.ReadAllText(Caminhos.ClaudeSettings).Contains("--statusline\"") && File.ReadAllText(Caminhos.ClaudeSettings).Contains("Pulso.exe"); }
            catch { return false; }
        }

        public static bool BarraDeOutro(out string atual)
        {
            atual = null;
            try
            {
                if (!File.Exists(Caminhos.ClaudeSettings)) return false;
                var o = Json.Parse(File.ReadAllText(Caminhos.ClaudeSettings));
                string cmd = Json.Str(o, "statusLine", "command");
                if (cmd == null) return false;
                atual = cmd;
                return cmd.IndexOf("Pulso.exe", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        // Inserção textual da chave no fim do objeto: o resto do arquivo fica byte a byte igual. Backup antes.
        public static string InstalarBarra()
        {
            string arq = Caminhos.ClaudeSettings;
            string texto = File.Exists(arq) ? File.ReadAllText(arq) : "{}";
            string outro;
            if (BarraDeOutro(out outro)) return "Já existe uma barra de status configurada ({0}). Nada foi alterado.".T(outro);
            if (BarraInstalada()) return null;
            if (Json.Parse(texto) == null) return "O settings.json do Claude Code não é um JSON válido. Nada foi alterado.".T();
            int fim = texto.LastIndexOf('}');
            if (fim < 0) return "Formato inesperado no settings.json. Nada foi alterado.".T();
            string antes = texto.Substring(0, fim).TrimEnd();
            bool vazio = antes.EndsWith("{");
            var cmd = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Comando);
            string bloco = (vazio ? "" : ",") + "\n  \"statusLine\": { \"type\": \"command\", \"command\": " + cmd + ", \"padding\": 0 }\n";
            string novo = antes + bloco + texto.Substring(fim);
            if (Json.Parse(novo) == null) return "Não consegui montar um JSON válido. Nada foi alterado.".T();
            Backup(arq);
            Caminhos.GravarAtomico(arq, novo);
            return null;
        }

        public static string RemoverBarra()
        {
            string arq = Caminhos.ClaudeSettings;
            if (!File.Exists(arq)) return null;
            string texto = File.ReadAllText(arq);
            var re = new Regex(@",?\s*""statusLine""\s*:\s*\{[^{}]*Pulso\.exe[^{}]*\}", RegexOptions.IgnoreCase);
            if (!re.IsMatch(texto)) return null;
            string novo = re.Replace(texto, "", 1);
            novo = Regex.Replace(novo, @"\{\s*,", "{");
            if (Json.Parse(novo) == null) return "Não consegui remover sem quebrar o JSON. Nada foi alterado.".T();
            Backup(arq);
            Caminhos.GravarAtomico(arq, novo);
            return null;
        }

        static void Backup(string arq)
        {
            if (File.Exists(arq)) File.Copy(arq, arq + ".pulso-bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
        }

        // ---- Iniciar com o Windows ----
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Só grava quando falta ou aponta para outro lugar: regravar a cada abertura é o que o antivírus
        // reconhece como vírus se fixando no sistema. "--inicio": aberto pelo Windows, fica só na bandeja.
        public static void IniciarComWindows(bool ligar)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Run, true))
                {
                    if (k == null) return;
                    string valor = "\"" + Exe + "\" --inicio";
                    if (ligar) { if (!string.Equals(k.GetValue("Pulso") as string, valor, StringComparison.OrdinalIgnoreCase)) k.SetValue("Pulso", valor); }
                    else if (k.GetValue("Pulso") != null) k.DeleteValue("Pulso");
                }
            }
            catch (Exception e) { Log.Erro("iniciar com o Windows", e); }
        }
    }

    // Modo "--statusline": chamado pelo Claude Code a cada atualização da barra (processo curto).
    static class BarraDeStatus
    {
        public static void Executar()
        {
            string entrada;
            using (var sr = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) entrada = sr.ReadToEnd();
            var o = Json.Parse(entrada);
            var rl = Json.Obj(o, "rate_limits");
            if (rl != null) Mensagens.Enviar(2, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(rl));

            Config.Carregar(); // idioma do texto da barra
            var saida = new StringBuilder();
            string modelo = Json.Str(o, "model", "display_name");
            if (modelo != null) saida.Append(modelo);
            double? h5 = Json.Num(rl, "five_hour", "used_percentage"), sem = Json.Num(rl, "seven_day", "used_percentage");
            if (h5.HasValue) saida.Append(saida.Length > 0 ? " · " : "").Append("5h ").Append(Math.Round(h5.Value)).Append('%');
            if (sem.HasValue) saida.Append(" · ").Append("semana {0}%".T(Math.Round(sem.Value)));
            using (var sw = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                sw.Write(saida.ToString());
        }
    }

    // Janela invisível que recebe mensagens de outras instâncias (barra de status, segunda execução) e o atalho global
    class Mensagens : NativeWindow, IDisposable
    {
        public const string Titulo = "Pulso.Mensagens";
        public event Action<int, string> Recebeu;
        public event Action Atalho;

        public Mensagens()
        {
            CreateHandle(new CreateParams { Caption = Titulo, Parent = (IntPtr)(-3) }); // HWND_MESSAGE
        }

        public bool RegistrarAtalho(uint mods, int tecla)
        {
            return Nativo.RegisterHotKey(Handle, 1, mods | Nativo.MOD_NOREPEAT, (uint)tecla);
        }

        public void SoltarAtalho() { Nativo.UnregisterHotKey(Handle, 1); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Nativo.WM_HOTKEY && (int)m.WParam == 1)
            {
                var h = Atalho;
                if (h != null) h();
                return;
            }
            if (m.Msg == Nativo.WM_COPYDATA)
            {
                var cds = (Nativo.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Nativo.COPYDATASTRUCT));
                string texto = cds.cbData > 0 ? Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2) : "";
                var h = Recebeu;
                if (h != null) h((int)cds.dwData, texto);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        public static bool Enviar(int tipo, string texto)
        {
            IntPtr alvo = FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, Titulo);
            if (alvo == IntPtr.Zero) return false;
            IntPtr buf = Marshal.StringToHGlobalUni(texto);
            try
            {
                var cds = new Nativo.COPYDATASTRUCT { dwData = (IntPtr)tipo, cbData = (texto.Length) * 2, lpData = buf };
                IntPtr r;
                Nativo.SendMessageTimeout(alvo, Nativo.WM_COPYDATA, IntPtr.Zero, ref cds, 2 /* SMTO_ABORTIFHUNG */, 500, out r);
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose() { DestroyHandle(); }
    }
}
