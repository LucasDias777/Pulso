using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulso
{
    // Texto nítido do notch e do cartão: o GDI+ (DrawString) desenha letra fina e borrada; aqui o texto vem
    // do DirectWrite (DWrite.cs, em tons de cinza: sem franja colorida no tema escuro) sobre um bitmap opaco
    // com a cor do fundo real e é colado alinhado ao pixel. Só serve onde o fundo é sólido (pílula e cartão) — e é onde há texto.
    // Medida e desenho usam as mesmas métricas do DirectWrite, senão a largura medida difere da desenhada.
    // Sem DirectWrite, ou com caractere que a fonte não tem, vai pelo GDI (TextRenderer), o caminho de reserva.
    static class TextoGdi
    {
        const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        static readonly Bitmap medidaBmp = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
        static readonly Graphics medida = Graphics.FromImage(medidaBmp);

        class Pronto { public Bitmap Bmp; }
        static readonly Dictionary<string, Pronto> cache = new Dictionary<string, Pronto>();
        static readonly Queue<string> ordem = new Queue<string>();

        // Font do GDI → fonte do DirectWrite: "Segoe UI Semibold" é a Segoe UI peso 600; negrito, 700.
        // O tamanho em pixels é o f.Size (as fontes do notch são em GraphicsUnit.Pixel). Nulo = vai pelo GDI.
        static DWrite.Fonte Dw(Font f, string texto)
        {
            if (!DWrite.Disponivel) return null;
            string familia = f.Name;
            int peso = f.Bold ? 700 : 400;
            if (familia.EndsWith(" Semibold")) { familia = familia.Substring(0, familia.Length - " Semibold".Length); peso = 600; }
            var d = DWrite.Obter(familia, peso);
            return d != null && DWrite.Cobre(d, texto) ? d : null;
        }

        static int Largura(DWrite.Fonte d, Font f, string texto)
        {
            return (int)Math.Ceiling(DWrite.Largura(d, f.Size, texto));
        }

        // Altura da linha pelas métricas do DirectWrite (ascendente + descendente, o "line-height: normal" do navegador).
        // A medida do GDI dependia do DPI do monitor principal e, com ele a 100%, cortava cedilha e descendentes.
        static int AlturaLinha(DWrite.Fonte d, Font f)
        {
            return (int)Math.Ceiling((d.Ascent + d.Descent) * f.Size / d.Upm);
        }

        // Onde o DWrite.Desenhar põe a linha de base, a partir do topo da linha
        static int BaseNaLinha(DWrite.Fonte d, Font f)
        {
            float k = f.Size / d.Upm;
            return (int)Math.Round((AlturaLinha(d, f) - (d.Ascent + d.Descent) * k) / 2 + d.Ascent * k);
        }

        public static Size Medir(string texto, Font f)
        {
            if (string.IsNullOrEmpty(texto)) return Size.Empty;
            var d = Dw(f, texto);
            if (d == null) return MedirGdi(texto, f);
            return new Size(Largura(d, f, texto), AlturaLinha(d, f));
        }

        public static int AlturaParagrafo(string texto, Font f, int largura)
        {
            if (string.IsNullOrEmpty(texto)) return 0;
            var d = Dw(f, texto);
            if (d == null) return TextRenderer.MeasureText(medida, texto, f, new Size(largura, int.MaxValue), Base | TextFormatFlags.WordBreak).Height;
            return DWrite.Quebrar(d, f.Size, texto.Replace("\r", ""), largura).Count * AlturaLinha(d, f);
        }

        // Percentual do anel: dígitos tabulares (todos com a largura do "0") e centrado
        // pela tinta real — alinhado ao centro do ícone, sem depender do espaço lateral de cada glifo.
        public static void Centro(Graphics g, string texto, Font f, Color cor, Color fundo, PointF c, float alfa)
        {
            if (string.IsNullOrEmpty(texto)) return;
            fundo = Color.FromArgb(255, fundo); cor = Color.FromArgb(255, cor);
            string chave = "C\u0001" + texto + "\u0001" + f.Name + f.Size + (int)f.Style + "|" + cor.ToArgb() + "|" + fundo.ToArgb() + "|" + DWrite.Assinatura;
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
            var tmp = new Bitmap(MedirGdi(texto, f).Width * 2 + 16, MedirGdi("0%", f).Height, PixelFormat.Format24bppRgb);
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

        // Uma linha dentro de um retângulo, à esquerda ou à direita, centrada na vertical, com reticências se faltar espaço.
        // baseDe: fica na linha de base que essa outra fonte teria no mesmo retângulo (texto menor ao lado de um rótulo)
        public static void Linha(Graphics g, string texto, Font f, Color cor, Color fundo, RectangleF ret, bool direita, Font baseDe = null)
        {
            if (string.IsNullOrEmpty(texto) || ret.Width < 2) return;
            var d = Dw(f, texto);
            if (d == null) { LinhaGdi(g, texto, f, cor, fundo, ret, direita); return; }
            int max = (int)Math.Floor(ret.Width);
            if (Largura(d, f, texto) > max)
            {
                while (texto.Length > 1 && Largura(d, f, texto + "…") > max) texto = texto.Substring(0, texto.Length - 1);
                texto = texto.TrimEnd() + "…";
            }
            int lw = Largura(d, f, texto), h = AlturaLinha(d, f);
            int w = Math.Min(lw + 1, max);
            int x = direita ? (int)Math.Floor(ret.Right) - w : (int)Math.Round(ret.Left);
            int y = (int)Math.Round(ret.Top + (ret.Height - h) / 2);
            var db = baseDe != null ? Dw(baseDe, "A") : null;
            if (db != null) y = (int)Math.Round(ret.Top + (ret.Height - AlturaLinha(db, baseDe)) / 2) + BaseNaLinha(db, baseDe) - BaseNaLinha(d, f);
            // À esquerda o texto começa na borda do retângulo; à direita, termina nela
            Colar(g, d, f, new List<string> { texto }, direita ? w - lw : 0, h, cor, fundo, new Rectangle(x, y, w, h));
        }

        // entrelinha > 0: altura de cada linha em pixels (o line-height do CSS); 0 = a da fonte.
        // O caminho de reserva pelo GDI fica com a entrelinha dele.
        public static int Paragrafo(Graphics g, string texto, Font f, Color cor, Color fundo, float x, float y, float largura, float entrelinha = 0)
        {
            int w = (int)Math.Floor(largura);
            var d = string.IsNullOrEmpty(texto) ? null : Dw(f, texto);
            if (d == null)
            {
                int hg = AlturaParagrafo(texto, f, w);
                if (g != null) ColarGdi(g, texto, f, cor, fundo, new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, hg), Base | TextFormatFlags.WordBreak);
                return hg;
            }
            var linhas = DWrite.Quebrar(d, f.Size, texto.Replace("\r", ""), w);
            int alt = entrelinha > 0 ? Math.Max(AlturaLinha(d, f), (int)Math.Round(entrelinha, MidpointRounding.AwayFromZero)) : AlturaLinha(d, f);
            int h = linhas.Count * alt;
            if (g != null) Colar(g, d, f, linhas, 0, alt, cor, fundo, new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, h));
            return h;
        }

        // Bitmap do tamanho de r, opaco com a cor do fundo, com uma linha do DirectWrite a cada 'alt' pixels
        // (o texto começa em xTexto); fica no cache e é colado alinhado ao pixel
        static void Colar(Graphics g, DWrite.Fonte d, Font f, List<string> linhas, int xTexto, int alt, Color cor, Color fundo, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            fundo = Color.FromArgb(255, fundo); cor = Color.FromArgb(255, cor);
            string chave = "D\u0001" + string.Join("\n", linhas) + "\u0001" + f.Name + f.Size + (int)f.Style + "\u0001" + cor.ToArgb() + "|" + fundo.ToArgb() + "|" + r.Width + "x" + r.Height + "|" + xTexto + "|" + alt + "|" + DWrite.Assinatura;
            Pronto p;
            if (!cache.TryGetValue(chave, out p))
            {
                var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
                using (var gt = Graphics.FromImage(bmp))
                {
                    gt.Clear(fundo);
                    gt.PixelOffsetMode = PixelOffsetMode.None;
                    gt.InterpolationMode = InterpolationMode.NearestNeighbor;
                    for (int i = 0; i < linhas.Count; i++)
                        using (var b = DWrite.Desenhar(d, f.Size, linhas[i], cor, fundo, alt))
                            if (b != null) gt.DrawImage(b, xTexto - 1, i * alt, b.Width, b.Height); // o bitmap do DirectWrite tem 1 px de folga à esquerda
                }
                p = new Pronto { Bmp = bmp };
                Guardar(chave, p);
            }
            Desenhar(g, p.Bmp, r, 1);
        }

        // ---- reserva pelo GDI (sem DirectWrite ou com caractere que a fonte não tem) ----

        static Size MedirGdi(string texto, Font f)
        {
            return TextRenderer.MeasureText(medida, texto ?? "", f, Size.Empty, Base | TextFormatFlags.SingleLine);
        }

        static void LinhaGdi(Graphics g, string texto, Font f, Color cor, Color fundo, RectangleF ret, bool direita)
        {
            var t = MedirGdi(texto, f);
            int w = Math.Min(t.Width + 1, (int)Math.Floor(ret.Width));
            int x = direita ? (int)Math.Floor(ret.Right) - w : (int)Math.Round(ret.Left);
            int y = (int)Math.Round(ret.Top + (ret.Height - t.Height) / 2);
            var flags = Base | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | (direita ? TextFormatFlags.Right : TextFormatFlags.Left);
            ColarGdi(g, texto, f, cor, fundo, new Rectangle(x, y, w, t.Height), flags);
        }

        static void ColarGdi(Graphics g, string texto, Font f, Color cor, Color fundo, Rectangle r, TextFormatFlags flags)
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
