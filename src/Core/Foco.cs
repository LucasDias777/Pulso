using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulso
{
    // Acha a janela onde uma sessão roda (VS Code, Cursor, terminal) pelo nome da pasta no título.
    // Serve para: clicar na sessão e ir para ela; não avisar de uma sessão que você já está olhando;
    // e marcar "concluída" como vista quando você volta para a janela dela.
    static class Foco
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

        static readonly string[] Editores = { "code", "code - insiders", "cursor", "windsurf", "antigravity", "windowsterminal", "powershell", "pwsh", "cmd", "wezterm-gui", "alacritty" };

        static string Processo(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            try { using (var p = Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant(); }
            catch { return ""; }
        }

        static string Titulo(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string NomePasta(string pasta)
        {
            if (string.IsNullOrEmpty(pasta)) return null;
            return Path.GetFileName(pasta.TrimEnd('\\', '/'));
        }

        // A janela (de editor ou terminal) é da pasta desta sessão?
        public static bool EDaPasta(IntPtr h, string pasta)
        {
            string nome = NomePasta(pasta);
            if (string.IsNullOrEmpty(nome) || h == IntPtr.Zero) return false;
            if (Titulo(h).IndexOf(nome, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return Array.IndexOf(Editores, Processo(h)) >= 0;
        }

        public static IntPtr Achar(string pasta)
        {
            IntPtr achada = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true; // só janelas principais
                if (EDaPasta(h, pasta)) { achada = h; return false; }
                return true;
            }, IntPtr.Zero);
            return achada;
        }

        // Clique veio do Pulso, então o Windows deixa trazer outra janela para a frente
        public static bool Trazer(string pasta)
        {
            var h = Achar(pasta);
            if (h == IntPtr.Zero) return false;
            if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
            return SetForegroundWindow(h);
        }
    }
}
