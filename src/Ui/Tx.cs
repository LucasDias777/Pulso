using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulso
{
    // Texto pelo DirectWrite colado num Graphics, com cache de bitmaps (a tela redesenha a cada hover).
    // O bitmap é opaco com a cor do fundo real, então o texto só vai onde o fundo é sólido e conhecido.
    // Sem DirectWrite, cai para o GDI com a mesma família.
    static class Tx
    {
        public enum Alinhar { Esquerda, Centro, Direita }

        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        static readonly Queue<string> ordem = new Queue<string>();

        public class Estilo
        {
            public string Familia; public int Peso; public float Em; public float Linha;
            public DWrite.Fonte Fonte { get { return DWrite.Obter(Familia, Peso); } }
            Font gdi;
            public Font Gdi { get { return gdi ?? (gdi = new Font(Familia, Em, Peso >= 600 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel)); } }
        }

        public static Estilo Novo(string familia, int peso, float em, float alturaLinha)
        {
            return new Estilo { Familia = familia, Peso = peso, Em = em, Linha = alturaLinha };
        }

        public static float Largura(Estilo e, string texto)
        {
            var f = e.Fonte;
            if (f != null) return DWrite.Largura(f, e.Em, texto);
            return TextRenderer.MeasureText(texto ?? "", e.Gdi, Size.Empty, TextFormatFlags.NoPadding).Width;
        }

        public static List<string> Quebrar(Estilo e, string texto, float largura)
        {
            var f = e.Fonte;
            if (f != null) return DWrite.Quebrar(f, e.Em, texto, largura);
            var l = new List<string>();
            string atual = "";
            foreach (var p in (texto ?? "").Split(' '))
            {
                string t = atual.Length == 0 ? p : atual + " " + p;
                if (atual.Length > 0 && Largura(e, t) > largura) { l.Add(atual); atual = p; } else atual = t;
            }
            l.Add(atual);
            return l;
        }

        // Uma linha; com larguraMax > 0, corta com reticências se não couber
        public static void Linha(Graphics g, Estilo e, string texto, Color cor, Color fundo, float x, float y, Alinhar al, float larguraMax)
        {
            if (string.IsNullOrEmpty(texto)) return;
            if (larguraMax > 0 && Largura(e, texto) > larguraMax)
            {
                while (texto.Length > 1 && Largura(e, texto + "…") > larguraMax) texto = texto.Substring(0, texto.Length - 1);
                texto = texto.TrimEnd() + "…";
            }
            fundo = Color.FromArgb(255, fundo); cor = Color.FromArgb(255, cor);
            string chave = e.Familia + e.Peso + "|" + e.Em + "|" + e.Linha + "|" + cor.ToArgb() + "|" + fundo.ToArgb() + "|" + DWrite.Assinatura + "|" + texto;
            Bitmap b;
            if (!cache.TryGetValue(chave, out b))
            {
                b = DWrite.Desenhar(e.Fonte, e.Em, texto, cor, fundo, e.Linha);
                if (b == null)
                {
                    var t = TextRenderer.MeasureText(texto, e.Gdi, Size.Empty, TextFormatFlags.NoPadding);
                    b = new Bitmap(Math.Max(1, t.Width + 2), Math.Max(1, (int)Math.Ceiling(e.Linha)));
                    using (var gb = Graphics.FromImage(b))
                    {
                        gb.Clear(fundo);
                        TextRenderer.DrawText(gb, texto, e.Gdi, new Rectangle(1, 0, b.Width, b.Height), cor, fundo, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                    }
                }
                cache[chave] = b;
                ordem.Enqueue(chave);
                if (ordem.Count > 600)
                {
                    string velha = ordem.Dequeue();
                    Bitmap v;
                    if (cache.TryGetValue(velha, out v)) { v.Dispose(); cache.Remove(velha); }
                }
            }
            float w = b.Width - 4; // o bitmap tem 1 px de folga à esquerda e 3 à direita
            float px = al == Alinhar.Esquerda ? x - 1 : al == Alinhar.Centro ? x - w / 2 - 1 : x - w - 1;
            var pom = g.PixelOffsetMode; var im = g.InterpolationMode;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(b, (float)Math.Round(px), (float)Math.Round(y), b.Width, b.Height);
            g.PixelOffsetMode = pom; g.InterpolationMode = im;
        }

        // Parágrafo com quebra de linha; g nulo = só mede. Devolve a altura.
        public static float Paragrafo(Graphics g, Estilo e, string texto, Color cor, Color fundo, float x, float y, float largura)
        {
            var linhas = Quebrar(e, texto, largura);
            if (g != null)
                for (int i = 0; i < linhas.Count; i++) Linha(g, e, linhas[i], cor, fundo, x, y + i * e.Linha, Alinhar.Esquerda, 0);
            return linhas.Count * e.Linha;
        }
    }
}
