using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Pulso
{
    static class Nativo
    {
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TRANSPARENT = 0x20;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_HOTKEY = 0x312, WM_COPYDATA = 0x4A,
            WM_DPICHANGED = 0x2E0, WM_DISPLAYCHANGE = 0x7E, WM_SETTINGCHANGE = 0x1A, WM_NCHITTEST = 0x84, WM_MOUSELEAVE = 0x2A3;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public const uint TME_LEAVE = 2;

        [StructLayout(LayoutKind.Sequential)] public struct PONTO { public int X, Y; public PONTO(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct TAMANHO { public int W, H; public TAMANHO(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PONTO pptDst, ref TAMANHO psize, IntPtr hdcSrc, ref PONTO pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(PONTO pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint x, out uint y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out PONTO p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        public static float EscalaDoMonitor(IntPtr hmon)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(hmon, 0, out x, out y) == 0 && x > 0) return x / 96f;
            }
            catch { }
            return 1f;
        }

        public static Rectangle Ret(RECT r) { return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); }
    }
}
