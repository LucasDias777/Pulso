using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulso
{
    // Texto nítido: o GDI+ (DrawString) desenha letra fina e borrada; aqui o texto é desenhado pelo GDI
    // (ClearType, com hinting — o mesmo dos menus do Windows) sobre um bitmap opaco com a cor do fundo
    // real e colado alinhado ao pixel. Só serve onde o fundo é sólido (pílula e cartão) — e é onde há texto.
    // Medida e desenho usam o mesmo tipo de contexto (bitmap), senão a largura medida difere da desenhada.
    static class TextoGdi
    {
        const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        static readonly Bitmap medidaBmp = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
        static readonly Graphics medida = Graphics.FromImage(medidaBmp);

        class Pronto { public Bitmap Bmp; }
        static readonly Dictionary<string, Pronto> cache = new Dictionary<string, Pronto>();
        static readonly Queue<string> ordem = new Queue<string>();

        public static Size Medir(string texto, Font f)
        {
            return TextRenderer.MeasureText(medida, texto ?? "", f, Size.Empty, Base | TextFormatFlags.SingleLine);
        }

        public static int AlturaParagrafo(string texto, Font f, int largura)
        {
            return TextRenderer.MeasureText(medida, texto ?? "", f, new Size(largura, int.MaxValue), Base | TextFormatFlags.WordBreak).Height;
        }

        // Percentual do anel: dígitos tabulares (todos com a largura do "0") e centrado
        // pela tinta real — alinhado ao centro do ícone, sem depender do espaço lateral de cada glifo.
        public static void Centro(Graphics g, string texto, Font f, Color cor, Color fundo, PointF c, float alfa)
        {
            if (string.IsNullOrEmpty(texto)) return;
            fundo = Color.FromArgb(255, fundo); cor = Color.FromArgb(255, cor);
            string chave = "C\u0001" + texto + "\u0001" + f.Name + f.Size + (int)f.Style + "|" + cor.ToArgb() + "|" + fundo.ToArgb();
            Pronto p;
            if (!cache.TryGetValue(chave, out p))
            {
                // DirectWrite (Segoe UI 600, tabular); se falhar, GDI
                using (var tmp = DWrite.Desenhar(texto, "Segoe UI", 600, f.Size, cor, fundo) ?? GdiTabular(texto, f, cor, fundo))
                {
                    // Recorta na horizontal pela tinta; a altura fica a da linha (baseline igual entre "21%" e "0%")
                    int min = tmp.Width, max = -1;
                    var dados = tmp.LockBits(new Rectangle(0, 0, tmp.Width, tmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        var linha = new byte[dados.Stride];
                        for (int y = 0; y < tmp.Height; y++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(dados.Scan0 + y * dados.Stride, linha, 0, dados.Stride);
                            for (int x = 0; x < tmp.Width; x++)
                            {
                                int o = x * 3;
                                if (Math.Abs(linha[o] - fundo.B) + Math.Abs(linha[o + 1] - fundo.G) + Math.Abs(linha[o + 2] - fundo.R) > 24)
                                {
                                    if (x < min) min = x;
                                    if (x > max) max = x;
                                }
                            }
                        }
                    }
                    finally { tmp.UnlockBits(dados); }
                    if (max < min) { min = 0; max = tmp.Width - 1; }
                    p = new Pronto { Bmp = tmp.Clone(new Rectangle(min, 0, max - min + 1, tmp.Height), PixelFormat.Format24bppRgb) };
                }
                Guardar(chave, p);
            }
            var r = new Rectangle((int)Math.Round(c.X - p.Bmp.Width / 2f), (int)Math.Round(c.Y - p.Bmp.Height / 2f), p.Bmp.Width, p.Bmp.Height);
            Desenhar(g, p.Bmp, r, alfa);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class LOGFONT
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }
        [StructLayout(LayoutKind.Sequential)] struct TAM { public int cx, cy; }
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirect(LOGFONT lf);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int modo);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int cor);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOut(IntPtr hdc, int x, int y, string s, int n);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32(IntPtr hdc, string s, int n, out TAM t);

        static Bitmap GdiTabular(string texto, Font f, Color cor, Color fundo)
        {
            var tmp = new Bitmap(Medir(texto, f).Width * 2 + 16, Medir("0%", f).Height, PixelFormat.Format24bppRgb);
            using (var gt = Graphics.FromImage(tmp))
            {
                gt.Clear(fundo);
                DesenharNatural(gt, texto, f, cor);
            }
            return tmp;
        }

        // Reserva sem DirectWrite: GDI ClearType natural, dígitos numa célula da largura do maior dígito (tabular-nums)
        static void DesenharNatural(Graphics gt, string texto, Font f, Color cor)
        {
            var lf = new LOGFONT();
            f.ToLogFont(lf, gt);
            lf.lfQuality = 6; // CLEARTYPE_NATURAL_QUALITY
            IntPtr hdc = gt.GetHdc();
            IntPtr hf = CreateFontIndirect(lf), antiga = SelectObject(hdc, hf);
            try
            {
                SetBkMode(hdc, 1); // TRANSPARENT
                SetTextColor(hdc, ColorTranslator.ToWin32(cor));
                TAM t;
                int celula = 0;
                for (char d = '0'; d <= '9'; d++) { GetTextExtentPoint32(hdc, d.ToString(), 1, out t); celula = Math.Max(celula, t.cx); }
                int x = 8;
                foreach (char ch in texto)
                {
                    GetTextExtentPoint32(hdc, ch.ToString(), 1, out t);
                    int avanco = char.IsDigit(ch) ? celula : t.cx;
                    TextOut(hdc, x + (avanco - t.cx) / 2, 0, ch.ToString(), 1);
                    x += avanco;
                }
            }
            finally
            {
                SelectObject(hdc, antiga);
                DeleteObject(hf);
                gt.ReleaseHdc(hdc);
            }
        }

        // Uma linha dentro de um retângulo, à esquerda ou à direita, centrada na vertical, com reticências se faltar espaço
        public static void Linha(Graphics g, string texto, Font f, Color cor, Color fundo, RectangleF ret, bool direita)
        {
            if (string.IsNullOrEmpty(texto) || ret.Width < 2) return;
            var t = Medir(texto, f);
            int w = Math.Min(t.Width + 1, (int)Math.Floor(ret.Width));
            int x = direita ? (int)Math.Floor(ret.Right) - w : (int)Math.Round(ret.Left);
            int y = (int)Math.Round(ret.Top + (ret.Height - t.Height) / 2);
            var flags = Base | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | (direita ? TextFormatFlags.Right : TextFormatFlags.Left);
            Colar(g, texto, f, cor, fundo, new Rectangle(x, y, w, t.Height), flags);
        }

        public static int Paragrafo(Graphics g, string texto, Font f, Color cor, Color fundo, float x, float y, float largura)
        {
            int w = (int)Math.Floor(largura);
            int h = AlturaParagrafo(texto, f, w);
            if (g != null) Colar(g, texto, f, cor, fundo, new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, h), Base | TextFormatFlags.WordBreak);
            return h;
        }

        static void Colar(Graphics g, string texto, Font f, Color cor, Color fundo, Rectangle r, TextFormatFlags flags)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            fundo = Color.FromArgb(255, fundo); cor = Color.FromArgb(255, cor);
            string chave = texto + "\u0001" + f.Name + f.Size + (int)f.Style + "\u0001" + cor.ToArgb() + "|" + fundo.ToArgb() + "|" + r.Width + "x" + r.Height + "|" + (int)flags;
            Pronto p;
            if (!cache.TryGetValue(chave, out p))
            {
                var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
                using (var gt = Graphics.FromImage(bmp))
                {
                    gt.Clear(fundo);
                    TextRenderer.DrawText(gt, texto, f, new Rectangle(0, 0, r.Width, r.Height), cor, fundo, flags);
                }
                p = new Pronto { Bmp = bmp };
                Guardar(chave, p);
            }
            Desenhar(g, p.Bmp, r, 1);
        }

        static void Guardar(string chave, Pronto p)
        {
            cache[chave] = p;
            ordem.Enqueue(chave);
            if (ordem.Count <= 240) return;
            string velha = ordem.Dequeue();
            Pronto v;
            if (cache.TryGetValue(velha, out v)) { v.Bmp.Dispose(); cache.Remove(velha); }
        }

        static void Desenhar(Graphics g, Bitmap bmp, Rectangle r, float alfa)
        {
            if (alfa <= 0.01f) return;
            var pom = g.PixelOffsetMode;
            var im = g.InterpolationMode;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (alfa >= 0.99f) g.DrawImage(bmp, r.X, r.Y, r.Width, r.Height);
            else
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = alfa });
                    g.DrawImage(bmp, r, 0, 0, r.Width, r.Height, GraphicsUnit.Pixel, ia);
                }
            g.PixelOffsetMode = pom;
            g.InterpolationMode = im;
        }
    }
}
