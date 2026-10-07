using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Pulso
{
    // Atalho global configurável (padrão Win + Y): texto para exibir e gravação de uma combinação nova.
    static class Atalhos
    {
        public const uint PadraoMods = Nativo.MOD_WIN;
        public const int PadraoTecla = (int)Keys.Y;

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint codigo, uint tipo);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder s, int n);

        public static string[] Partes(uint mods, int tecla)
        {
            var l = new List<string>();
            if ((mods & Nativo.MOD_CONTROL) != 0) l.Add("Ctrl");
            if ((mods & Nativo.MOD_ALT) != 0) l.Add("Alt");
            if ((mods & Nativo.MOD_SHIFT) != 0) l.Add("Shift");
            if ((mods & Nativo.MOD_WIN) != 0) l.Add("Win");
            l.Add(NomeTecla((Keys)tecla));
            return l.ToArray();
        }

        public static string Texto(uint mods, int tecla) { return string.Join(" + ", Partes(mods, tecla)); }

        static string NomeTecla(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Space: return "Espaço";
                case Keys.Return: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Back: return "Backspace";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Pause: return "Pause";
                case Keys.PrintScreen: return "Print Screen";
            }
            // Demais (pontuação etc.): nome que o próprio Windows dá à tecla, no layout em uso
            uint sc = MapVirtualKey((uint)k, 0);
            var sb = new StringBuilder(32);
            if (sc != 0 && GetKeyNameText((int)(sc << 16), sb, sb.Capacity) > 0) return sb.ToString();
            return k.ToString();
        }

        static bool EModificador(Keys k)
        {
            return k == Keys.LWin || k == Keys.RWin || k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu;
        }

        // Combinação válida: um modificador + outra tecla, ou uma tecla F sozinha
        public static bool Valida(uint mods, Keys k)
        {
            if (EModificador(k)) return false;
            return mods != 0 || (k >= Keys.F1 && k <= Keys.F24);
        }

        // ---- gravação ----
        // Gancho de teclado de baixo nível só durante a gravação: lê a combinação e segura as teclas,
        // para Win + E, Alt + Tab etc. não agirem enquanto você grava. Esc cancela; 10 s sem tecla cancela.
        delegate IntPtr ProcGancho(int codigo, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, ProcGancho proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int codigo, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string nome);

        static IntPtr gancho = IntPtr.Zero;
        static ProcGancho proc;
        static uint modsPressionados;
        static Action<uint, Keys> aoGravar;
        static Action aoCancelar;
        static Timer limite;
        static System.Threading.SynchronizationContext ui;

        public static bool Gravando { get { return gancho != IntPtr.Zero; } }

        public static void Gravar(Action<uint, Keys> gravou, Action cancelou)
        {
            Parar();
            aoGravar = gravou; aoCancelar = cancelou; modsPressionados = 0;
            ui = System.Threading.SynchronizationContext.Current;
            proc = Gancho;
            gancho = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
            if (gancho == IntPtr.Zero) { var c = cancelou; if (c != null) c(); return; }
            limite = new Timer { Interval = 10000 };
            limite.Tick += delegate { Cancelar(); };
            limite.Start();
        }

        public static void Cancelar()
        {
            var c = aoCancelar;
            Parar();
            if (c != null) c();
        }

        static void Parar()
        {
            if (gancho != IntPtr.Zero) { UnhookWindowsHookEx(gancho); gancho = IntPtr.Zero; }
            if (limite != null) { limite.Dispose(); limite = null; }
            aoGravar = null; aoCancelar = null;
        }

        static uint ModDe(Keys k)
        {
            if (k == Keys.LWin || k == Keys.RWin) return Nativo.MOD_WIN;
            if (k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey) return Nativo.MOD_SHIFT;
            if (k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey) return Nativo.MOD_CONTROL;
            if (k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu) return Nativo.MOD_ALT;
            return 0;
        }

        static IntPtr Gancho(int codigo, IntPtr wParam, IntPtr lParam)
        {
            if (codigo < 0 || gancho == IntPtr.Zero) return CallNextHookEx(IntPtr.Zero, codigo, wParam, lParam);
            var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            var k = (Keys)info.vkCode;
            int msg = wParam.ToInt32();
            bool desce = msg == 0x100 || msg == 0x104; // WM_KEYDOWN / WM_SYSKEYDOWN
            uint mod = ModDe(k);
            if (mod != 0)
            {
                if (desce) modsPressionados |= mod; else modsPressionados &= ~mod;
                return (IntPtr)1; // segura: nem o Win abre o Iniciar
            }
            if (!desce) return (IntPtr)1;
            if (k == Keys.Escape && modsPressionados == 0) { Depois(Cancelar); return (IntPtr)1; }
            if (!Valida(modsPressionados, k)) return (IntPtr)1; // espera uma combinação válida
            var g = aoGravar;
            uint mods = modsPressionados;
            Parar();
            if (g != null) Depois(delegate { g(mods, k); });
            return (IntPtr)1;
        }

        // O gancho precisa responder rápido: o resto roda depois, na thread da interface
        static void Depois(Action a)
        {
            if (ui != null) ui.Post(delegate { a(); }, null); else a();
        }
    }
}
