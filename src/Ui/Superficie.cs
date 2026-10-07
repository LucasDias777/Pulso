using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Pulso
{
    // Bitmap 32 bits com alfa pré-multiplicado sobre uma DIB section: o GDI+ desenha direto na memória que
    // o UpdateLayeredWindow lê, sem cópia por quadro. Recriado só quando o tamanho muda.
    sealed class Superficie : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        public int Largura, Altura;
        IntPtr dib, bits, memDc, antigo;
        Bitmap bmp;

        public Graphics Abrir(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (w != Largura || h != Altura || bmp == null) Recriar(w, h);
            var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            return g;
        }

        void Recriar(int w, int h)
        {
            Liberar();
            Largura = w; Altura = h;
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr tela = Nativo.GetDC(IntPtr.Zero);
            dib = CreateDIBSection(tela, ref bi, 0, out bits, IntPtr.Zero, 0);
            memDc = Nativo.CreateCompatibleDC(tela);
            Nativo.ReleaseDC(IntPtr.Zero, tela);
            antigo = Nativo.SelectObject(memDc, dib);
            bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public void Mostrar(IntPtr hwnd, int x, int y, byte opacidade = 255)
        {
            var dst = new Nativo.PONTO(x, y);
            var tam = new Nativo.TAMANHO(Largura, Altura);
            var src = new Nativo.PONTO(0, 0);
            var blend = new Nativo.BLENDFUNCTION { BlendOp = Nativo.AC_SRC_OVER, SourceConstantAlpha = opacidade, AlphaFormat = Nativo.AC_SRC_ALPHA };
            IntPtr tela = Nativo.GetDC(IntPtr.Zero);
            Nativo.UpdateLayeredWindow(hwnd, tela, ref dst, ref tam, memDc, ref src, 0, ref blend, Nativo.ULW_ALPHA);
            Nativo.ReleaseDC(IntPtr.Zero, tela);
        }

        public void Salvar(string arquivo)
        {
            if (bmp != null) bmp.Save(arquivo, ImageFormat.Png);
        }

        void Liberar()
        {
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (memDc != IntPtr.Zero) { Nativo.SelectObject(memDc, antigo); Nativo.DeleteDC(memDc); memDc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { Nativo.DeleteObject(dib); dib = IntPtr.Zero; }
        }

        public void Dispose() { Liberar(); }
    }
}
