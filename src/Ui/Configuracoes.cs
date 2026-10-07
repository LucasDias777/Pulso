using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulso
{
    // Configurações no estilo das Configurações do Windows 11 (cores sólidas):
    // barra lateral em cartão com selos coloridos, seções em grupos arredondados com divisórias,
    // interruptor contornado, segmentados e deslizante na cor de destaque do Windows, aviso "Salvo".
    // Tudo desenhado aqui (sem controles do WinForms) e o texto pelo DirectWrite, como no navegador.
    class Configuracoes : Form
    {
        // ---------- tema (CSS :root / body.solid) ----------
        class Cores
        {
            public Color Pane, Side, Group, Line, Sep, Text, Text2, SegBg, Hover, SelSoft, Btn, BtnHover, Accent, OnAccent;
        }

        static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }

        // Cor com alfa sobre um fundo opaco (o texto e os bitmaps precisam do resultado sólido)
        static Color Sobre(Color cima, Color baixo)
        {
            double t = cima.A / 255.0;
            return Color.FromArgb(255, (int)Math.Round(cima.R * t + baixo.R * (1 - t)), (int)Math.Round(cima.G * t + baixo.G * (1 - t)), (int)Math.Round(cima.B * t + baixo.B * (1 - t)));
        }

        bool Escuro
        {
            get
            {
                var t = Config.Atual.Tema;
                if (t == Tema.Escuro) return true;
                if (t == Tema.Claro) return false;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                        return !(v is int && (int)v == 1);
                    }
                }
                catch { return true; }
            }
        }

        Cores CoresDoTema()
        {
            var c = new Cores();
            if (Escuro)
            {
                c.Pane = Paleta.Hex("#3d3d40"); c.Side = Paleta.Hex("#1f2022"); c.Group = Paleta.Hex("#48484b");
                c.Line = A(20, 255, 255, 255); c.Sep = A(20, 255, 255, 255);
                c.Text = Paleta.Hex("#f2f2f4"); c.Text2 = A(158, 235, 235, 245);
                c.SegBg = A(23, 255, 255, 255); c.Hover = A(15, 255, 255, 255); c.SelSoft = A(20, 255, 255, 255);
                c.Btn = A(26, 255, 255, 255); c.BtnHover = A(41, 255, 255, 255);
            }
            else
            {
                c.Pane = Paleta.Hex("#f0f0f2"); c.Side = Paleta.Hex("#e2e2e6"); c.Group = Paleta.Hex("#ffffff");
                c.Line = A(18, 0, 0, 0); c.Sep = A(18, 0, 0, 0);
                c.Text = Paleta.Hex("#1d1d1f"); c.Text2 = A(179, 60, 60, 67);
                c.SegBg = A(15, 0, 0, 0); c.Hover = A(10, 0, 0, 0); c.SelSoft = A(13, 0, 0, 0);
                c.Btn = A(13, 0, 0, 0); c.BtnHover = A(23, 0, 0, 0);
            }
            // Destaque como nos controles do Windows 11: Accent Light 2 + texto preto no escuro, Accent Dark 1 + branco no claro
            c.Accent = Paleta.Hex("#0a7aff"); c.OnAccent = Color.White;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var p = k != null ? k.GetValue("AccentPalette") as byte[] : null;
                    if (p != null && p.Length >= 32)
                    {
                        int i = Escuro ? 1 : 4;
                        c.Accent = Color.FromArgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
                        c.OnAccent = Escuro ? Color.Black : Color.White;
                    }
                }
            }
            catch { }
            return c;
        }

        // ---------- estado ----------
        readonly App app;
        readonly float k;
        float E(float v) { return v * k; }
        int aba;
        float rolagem, alturaConteudo;
        Cores c;
        readonly Tx.Estilo corpo, forte, titulo, peq, ctl;
        readonly Timer relogio = new Timer { Interval = 250 };
        DateTime salvoEm = DateTime.MinValue;
        public bool SemAtivar;

        class Alvo { public RectangleF R; public string Id; public Action Clique; public Action<float> Arrastar; public bool NoCorpo; }
        readonly List<Alvo> alvos = new List<Alvo>();
        string sobre;
        Alvo segurando;
        bool gravandoAtalho;
        string avisoAtalho;

        static readonly string[] NomesAbas = { "Contas", "Aparência", "Geral" };

        public Configuracoes(App app)
        {
            this.app = app;
            k = DeviceDpi / 96f;
            Text = "Pulso — Configurações";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size((int)Math.Round(E(680)), (int)Math.Round(E(520)));
            ShowInTaskbar = true;
            KeyPreview = true;
            Icon = IconeApp();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            const string fam = "Segoe UI Variable Text";
            corpo = Tx.Novo(fam, 400, E(13.5f), E(13.5f * 1.35f));
            forte = Tx.Novo(fam, 600, E(13.5f), E(13.5f * 1.35f));
            titulo = Tx.Novo(fam, 600, E(22), E(52));
            peq = Tx.Novo(fam, 400, E(12), E(12 * 1.35f));
            ctl = Tx.Novo(fam, 400, E(13), E(13 * 1.35f));

            relogio.Tick += delegate
            {
                // Toast some sozinho; na aba Contas o "exato há X s" anda
                if (gravandoAtalho || (DateTime.UtcNow - salvoEm).TotalSeconds < 2 || (aba == 0 && DateTime.UtcNow.Millisecond < 260)) Invalidate();
            };
            relogio.Start();
            FormClosed += delegate { relogio.Dispose(); if (Atalhos.Gravando) Atalhos.Cancelar(); };
            Estado.Mudou += AoMudarEstado;
            FormClosed += delegate { Estado.Mudou -= AoMudarEstado; };
            Atualizacao.Mudou += AoMudarAtualizacao;
            FormClosed += delegate { Atualizacao.Mudou -= AoMudarAtualizacao; };
        }

        void AoMudarEstado() { if (aba == 0 && !IsDisposed) Invalidate(); }
        void AoMudarAtualizacao() { if (aba == 2 && !IsDisposed) Invalidate(); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return SemAtivar; } }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int atributo, ref int valor, int tamanho);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int sim = Escuro ? 1 : 0, redondo = 2;
                DwmSetWindowAttribute(Handle, 20, ref sim, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(Handle, 33, ref redondo, 4);  // DWMWA_WINDOW_CORNER_PREFERENCE = arredondado
            }
            catch { }
        }

        static Icon IconeApp()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }

        public void MostrarAba(int i) { aba = i; rolagem = 0; Invalidate(); }

        void Salvou()
        {
            Config.Atual.Salvar();
            app.AplicarConfig();
            salvoEm = DateTime.UtcNow;
            Invalidate();
        }

        // ---------- geometria da janela ----------
        RectangleF Lateral { get { return new RectangleF(E(4), E(4), E(196), ClientSize.Height - E(8)); } }
        float PaneX { get { return E(204); } }
        RectangleF Fechar { get { return new RectangleF(ClientSize.Width - E(46), 0, E(46), E(34)); } }
        RectangleF Corpo { get { return new RectangleF(PaneX, E(52), ClientSize.Width - PaneX, ClientSize.Height - E(52)); } }

        // ---------- desenho ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            c = CoresDoTema();
            alvos.Clear();
            g.Clear(c.Pane);

            DesenharLateral(g);
            DesenharCabecalho(g);

            var corpoR = Corpo;
            var est = g.Save();
            g.SetClip(corpoR);
            float x = corpoR.X + E(20), w = corpoR.Width - E(40);
            float y0 = corpoR.Y - rolagem;
            float fim = DesenharConteudo(g, x, y0, w);
            alturaConteudo = fim - y0 + E(20);
            g.Restore(est);

            DesenharToast(g);
        }

        void Registrar(RectangleF r, string id, Action clique, bool noCorpo, Action<float> arrastar = null)
        {
            alvos.Add(new Alvo { R = r, Id = id, Clique = clique, NoCorpo = noCorpo, Arrastar = arrastar });
        }

        bool Sobre(string id) { return sobre == id; }

        static GraphicsPath Arred(RectangleF r, float raio)
        {
            float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Preencher(Graphics g, RectangleF r, float raio, Color cor)
        {
            using (var p = Arred(r, raio)) using (var b = new SolidBrush(cor)) g.FillPath(b, p);
        }

        void Contornar(Graphics g, RectangleF r, float raio, Color cor, float esp)
        {
            var rr = new RectangleF(r.X + esp / 2, r.Y + esp / 2, r.Width - esp, r.Height - esp);
            using (var p = Arred(rr, raio)) using (var pen = new Pen(cor, esp)) g.DrawPath(pen, p);
        }

        // Barra lateral: cartão recuado 4 px, faixa de 48 px no topo, linhas de 32 px com selo
        void DesenharLateral(Graphics g)
        {
            var r = Lateral;
            Preencher(g, r, E(14), c.Side);
            Contornar(g, r, E(14), Sobre(c.Line, c.Side), Math.Max(1, E(1)));
            float x = r.X + E(8), w = r.Width - E(16), y = r.Y + E(48);
            for (int i = 0; i < NomesAbas.Length; i++)
            {
                int idx = i;
                LinhaLateral(g, new RectangleF(x, y, w, E(32)), "aba" + i, NomesAbas[i], i, aba == i, delegate { MostrarAba(idx); });
                y += E(34);
            }
            LinhaLateral(g, new RectangleF(x, r.Bottom - E(8) - E(32), w, E(32)), "sair", "Encerrar o Pulso", 3, false, delegate { Close(); app.Sair(); });
        }

        void LinhaLateral(Graphics g, RectangleF r, string id, string texto, int selo, bool sel, Action clique)
        {
            Color fundo = c.Side;
            if (sel) fundo = Sobre(c.SelSoft, c.Side);
            else if (Sobre(id)) fundo = Sobre(c.Hover, c.Side);
            if (fundo != c.Side) Preencher(g, r, E(8), fundo);
            if (sel) Preencher(g, new RectangleF(r.X, r.Y + E(9), E(3), r.Height - E(18)), E(2), c.Accent);
            var rs = new RectangleF(r.X + E(8), r.Y + (r.Height - E(20)) / 2, E(20), E(20));
            DesenharSelo(g, rs, selo);
            Tx.Linha(g, corpo, texto, c.Text, fundo, rs.Right + E(9), r.Y + (r.Height - corpo.Linha) / 2, Tx.Alinhar.Esquerda, 0);
            Registrar(r, id, clique, false);
        }

        // Selos 20×20 com degradê e ícone branco de 12 px
        static readonly string[,] Selos =
        {
            { "#4fa6ff", "#0a6cff" }, { "#8784ff", "#5856d6" }, { "#a6a6ab", "#727277" }, { "#ff6d62", "#dd3328" },
        };
        static readonly GraphicsPath[] Icones = new GraphicsPath[4];

        static GraphicsPath Icone(int i)
        {
            if (Icones[i] != null) return Icones[i];
            GraphicsPath p;
            switch (i)
            {
                case 0: // pessoa (viewBox 12)
                    p = Svg.Interpretar("M1.5 11.2c.4-2.5 2.3-3.9 4.5-3.9s4.1 1.4 4.5 3.9z");
                    p.AddEllipse(6 - 2.4f, 4 - 2.4f, 4.8f, 4.8f);
                    break;
                case 1: // pincel (viewBox 12)
                    p = Svg.Interpretar("M10 .9a.8.8 0 0 1 1.1 1.1L6.4 6.7 5.3 5.6zM4.6 6.3l1.1 1.1c.2 1.9-1.9 3.6-4.7 3.1.9-.4 1.2-1 1.3-1.8.1-1.3 1.1-2.3 2.3-2.4z");
                    break;
                case 2: // engrenagem (viewBox 24 → 12)
                    p = Svg.Interpretar("M10.03 4.66L10.14 1.97 A10.20 10.20 0 0 1 13.86 1.97 L13.97 4.66A7.60 7.60 0 0 1 15.80 5.42L17.78 3.59 A10.20 10.20 0 0 1 20.41 6.22 L18.58 8.20A7.60 7.60 0 0 1 19.34 10.03L22.03 10.14 A10.20 10.20 0 0 1 22.03 13.86 L19.34 13.97A7.60 7.60 0 0 1 18.58 15.80L20.41 17.78 A10.20 10.20 0 0 1 17.78 20.41 L15.80 18.58A7.60 7.60 0 0 1 13.97 19.34L13.86 22.03 A10.20 10.20 0 0 1 10.14 22.03 L10.03 19.34A7.60 7.60 0 0 1 8.20 18.58L6.22 20.41 A10.20 10.20 0 0 1 3.59 17.78 L5.42 15.80A7.60 7.60 0 0 1 4.66 13.97L1.97 13.86 A10.20 10.20 0 0 1 1.97 10.14 L4.66 10.03A7.60 7.60 0 0 1 5.42 8.20L3.59 6.22 A10.20 10.20 0 0 1 6.22 3.59 L8.20 5.42A7.60 7.60 0 0 1 10.03 4.66 ZM7.90 12a4.10 4.10 0 1 0 8.20 0a4.10 4.10 0 1 0 -8.20 0Z");
                    using (var m = new Matrix()) { m.Scale(0.5f, 0.5f); p.Transform(m); }
                    break;
                default: // liga/desliga (traço)
                    p = Svg.Interpretar("M6 1.2v4.3");
                    p.StartFigure();
                    p.AddArc(6 - 3.9f, 6.2f - 3.9f, 7.8f, 7.8f, -48, 276);
                    break;
            }
            p.FillMode = FillMode.Alternate;
            Icones[i] = p;
            return p;
        }

        void DesenharSelo(Graphics g, RectangleF r, int i)
        {
            using (var p = Arred(r, E(5.5f)))
            using (var b = new LinearGradientBrush(new PointF(r.X, r.Y - 1), new PointF(r.X, r.Bottom + 1), Paleta.Hex(Selos[i, 0]), Paleta.Hex(Selos[i, 1])))
                g.FillPath(b, p);
            using (var ic = (GraphicsPath)Icone(i).Clone())
            using (var m = new Matrix())
            {
                m.Translate(r.X + E(4), r.Y + E(4));
                m.Scale(E(1), E(1));
                ic.Transform(m);
                if (i == 3) using (var pen = new Pen(Color.White, E(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(pen, ic);
                else if (i == 2)
                {
                    using (var b = new SolidBrush(Color.White)) g.FillPath(b, ic);
                    using (var pen = new Pen(Color.White, E(0.8f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, ic);
                }
                else using (var b = new SolidBrush(Color.White)) g.FillPath(b, ic);
            }
        }

        void DesenharCabecalho(Graphics g)
        {
            Tx.Linha(g, titulo, NomesAbas[aba], c.Text, c.Pane, PaneX + E(24), 0, Tx.Alinhar.Esquerda, 0);
            var f = Fechar;
            bool hov = Sobre("fechar");
            if (hov)
                using (var p = new GraphicsPath())
                {
                    // Canto superior direito acompanha o arredondamento da janela
                    float d = E(16);
                    p.AddLine(f.Left, f.Top, f.Right - d / 2, f.Top);
                    p.AddArc(f.Right - d, f.Top, d, d, 270, 90);
                    p.AddLine(f.Right, f.Top + d / 2, f.Right, f.Bottom);
                    p.AddLine(f.Right, f.Bottom, f.Left, f.Bottom);
                    p.CloseFigure();
                    using (var b = new SolidBrush(Paleta.Hex("#c42b1c"))) g.FillPath(b, p);
                }
            float cx = f.X + f.Width / 2, cy = f.Y + f.Height / 2, s = E(5);
            using (var pen = new Pen(hov ? Color.White : Sobre(c.Text2, c.Pane), Math.Max(1, E(1.2f))))
            {
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
            }
            Registrar(f, "fechar", Close, false);
        }

        void DesenharToast(Graphics g)
        {
            double t = (DateTime.UtcNow - salvoEm).TotalSeconds;
            if (t > 1.5) return;
            double op = t < 1.2 ? 1 : 1 - (t - 1.2) / 0.3;
            string txt = "Salvo";
            float w = Tx.Largura(peq, txt) + E(20), h = peq.Linha + E(10);
            var r = new RectangleF(ClientSize.Width - E(18) - w, ClientSize.Height - E(14) - h, w, h);
            var fundo = Sobre(Color.FromArgb((int)(255 * op), c.Side), c.Pane);
            Preencher(g, r, E(8), fundo);
            Contornar(g, r, E(8), Sobre(Color.FromArgb((int)(c.Line.A * op), c.Line), fundo), Math.Max(1, E(1)));
            if (op > 0.5) Tx.Linha(g, peq, txt, Sobre(Color.FromArgb((int)(c.Text2.A * op), c.Text2), fundo), fundo, r.X + E(10), r.Y + E(5), Tx.Alinhar.Esquerda, 0);
        }

        // ---------- conteúdo: seções, grupos e linhas ----------
        abstract class Linha
        {
            public abstract float Altura(Configuracoes j, float w);
            public abstract void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h);
        }

        abstract class Ctl
        {
            public string Id;
            public abstract SizeF Tamanho(Configuracoes j);
            public abstract void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo);
        }

        class Bloco { public string Secao; public List<Linha> Linhas; }

        float DesenharConteudo(Graphics g, float x, float y, float w)
        {
            var blocos = aba == 0 ? Contas() : aba == 1 ? Aparencia() : Geral();
            bool primeiro = true;
            foreach (var b in blocos)
            {
                if (b.Secao != null)
                {
                    y += primeiro ? E(6) : E(18);
                    Tx.Linha(g, forte, b.Secao, c.Text, c.Pane, x + E(12), y, Tx.Alinhar.Esquerda, 0);
                    y += forte.Linha + E(8);
                }
                else if (!primeiro) y += E(14);
                else y += E(6);
                primeiro = false;
                var alturas = b.Linhas.Select(l => l.Altura(this, w)).ToList();
                float total = alturas.Sum() + Math.Max(0, b.Linhas.Count - 1) * E(1);
                var gr = new RectangleF(x, y, w, total + E(2));
                Preencher(g, gr, E(10), c.Group);
                Contornar(g, gr, E(10), Sobre(c.Line, c.Group), Math.Max(1, E(1)));
                float yy = y + E(1);
                for (int i = 0; i < b.Linhas.Count; i++)
                {
                    if (i > 0)
                    {
                        using (var pen = new Pen(Sobre(c.Sep, c.Group), Math.Max(1, E(1)))) g.DrawLine(pen, x + E(1), yy, x + w - E(1), yy);
                        yy += E(1);
                    }
                    b.Linhas[i].Desenhar(this, g, x, yy, w, alturas[i]);
                    yy += alturas[i];
                }
                y += gr.Height;
            }
            return y;
        }

        // Rótulo à esquerda, controle à direita; se não couber, o controle desce (alinhado à direita)
        class Item : Linha
        {
            public string Rotulo; public Ctl C; public bool Dica;
            bool Quebra(Configuracoes j, float w, SizeF t)
            {
                return !Dica && C != null && Tx.Largura(j.corpo, Rotulo) + j.E(12) + t.Width > w - j.E(24);
            }
            float AlturaRotulo(Configuracoes j, float w, SizeF t)
            {
                if (!Dica) return j.corpo.Linha;
                return Tx.Paragrafo(null, j.peq, Rotulo, Color.Empty, Color.Empty, 0, 0, w - j.E(24) - t.Width - j.E(12));
            }
            public override float Altura(Configuracoes j, float w)
            {
                var t = C != null ? C.Tamanho(j) : SizeF.Empty;
                float hr = AlturaRotulo(j, w, t);
                if (Quebra(j, w, t)) return j.E(9) + hr + j.E(8) + t.Height + j.E(9);
                return Math.Max(j.E(40), j.E(18) + Math.Max(hr, t.Height));
            }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                var t = C != null ? C.Tamanho(j) : SizeF.Empty;
                float hr = AlturaRotulo(j, w, t);
                bool q = Quebra(j, w, t);
                float yr = q ? y + j.E(9) : y + (h - hr) / 2;
                if (Dica) Tx.Paragrafo(g, j.peq, Rotulo, Sobre(j.c.Text2, j.c.Group), j.c.Group, x + j.E(12), yr, w - j.E(24) - t.Width - j.E(12));
                else Tx.Linha(g, j.corpo, Rotulo, j.c.Text, j.c.Group, x + j.E(12), yr, Tx.Alinhar.Esquerda, w - j.E(24) - (q ? 0 : t.Width + j.E(12)));
                if (C == null) return;
                float yc = q ? yr + hr + j.E(8) : y + (h - t.Height) / 2;
                C.Desenhar(j, g, new RectangleF(x + w - j.E(12) - t.Width, yc, t.Width, t.Height), j.c.Group);
            }
        }

        class Legenda : Linha
        {
            public string Texto;
            public override float Altura(Configuracoes j, float w) { return j.E(16) + Tx.Paragrafo(null, j.peq, Texto, Color.Empty, Color.Empty, 0, 0, w - j.E(24)); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                Tx.Paragrafo(g, j.peq, Texto, Sobre(j.c.Text2, j.c.Group), j.c.Group, x + j.E(12), y + j.E(8), w - j.E(24));
            }
        }

        // Linha de conta: marca + nome + interruptor; detalhe e sub-opções recuados 26 px
        class Conta : Linha
        {
            public string Provedor, Nome, Detalhe; public bool Ligado; public Ctl Chave;
            public List<KeyValuePair<string, Ctl>> Subs = new List<KeyValuePair<string, Ctl>>();
            float AlturaDetalhe(Configuracoes j, float w) { return Detalhe == null ? 0 : j.E(4) + Tx.Paragrafo(null, j.peq, Detalhe, Color.Empty, Color.Empty, 0, 0, w - j.E(24) - j.E(26)); }
            public override float Altura(Configuracoes j, float w)
            {
                float h = j.E(9) + Math.Max(j.E(24), j.corpo.Linha) + AlturaDetalhe(j, w);
                foreach (var s in Subs) h += j.E(8) + Math.Max(j.ctl.Linha, s.Value.Tamanho(j).Height);
                return h + j.E(9);
            }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                float lh = Math.Max(j.E(24), j.corpo.Linha), yy = y + j.E(9);
                float op = Ligado ? 1 : 0.5f;
                using (var ic = Glifos.Caminho(Provedor, x + j.E(12) + j.E(8), yy + lh / 2, j.E(16)))
                using (var b = new SolidBrush(Paleta.Alfa(j.c.Text, op))) g.FillPath(b, ic);
                Tx.Linha(g, j.corpo, Nome, Ligado ? j.c.Text : Sobre(j.c.Text2, j.c.Group), j.c.Group, x + j.E(12) + j.E(26), yy + (lh - j.corpo.Linha) / 2, Tx.Alinhar.Esquerda, 0);
                var t = Chave.Tamanho(j);
                Chave.Desenhar(j, g, new RectangleF(x + w - j.E(12) - t.Width, yy + (lh - t.Height) / 2, t.Width, t.Height), j.c.Group);
                yy += lh;
                if (Detalhe != null)
                {
                    yy += j.E(4);
                    yy += Tx.Paragrafo(g, j.peq, Detalhe, Sobre(j.c.Text2, j.c.Group), j.c.Group, x + j.E(12) + j.E(26), yy, w - j.E(24) - j.E(26));
                }
                foreach (var s in Subs)
                {
                    yy += j.E(8);
                    var ts = s.Value.Tamanho(j);
                    float hs = Math.Max(j.ctl.Linha, ts.Height);
                    Tx.Linha(g, j.ctl, s.Key, j.c.Text, j.c.Group, x + j.E(12) + j.E(26), yy + (hs - j.ctl.Linha) / 2, Tx.Alinhar.Esquerda, w - j.E(24) - j.E(26) - ts.Width - j.E(12));
                    s.Value.Desenhar(j, g, new RectangleF(x + w - j.E(12) - ts.Width, yy + (hs - ts.Height) / 2, ts.Width, ts.Height), j.c.Group);
                    yy += hs;
                }
            }
        }

        // ---------- controles ----------
        class Chave : Ctl
        {
            public bool Ligado; public Action<bool> Mudou; public bool Travado;
            public override SizeF Tamanho(Configuracoes j) { return new SizeF(j.E(40), j.E(20)); }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                float op = Travado ? 0.5f : 1;
                if (Ligado) j.Preencher(g, r, r.Height / 2, Paleta.Alfa(j.c.Accent, op));
                else j.Contornar(g, r, r.Height / 2, Sobre(Paleta.Alfa(j.c.Text2, op * j.c.Text2.A / 255.0), fundo), Math.Max(1, j.E(1)));
                float d = j.E(12), kx = Ligado ? r.Right - j.E(4) - d : r.X + j.E(4);
                using (var b = new SolidBrush(Ligado ? j.c.OnAccent : Sobre(Paleta.Alfa(j.c.Text2, op * j.c.Text2.A / 255.0), fundo)))
                    g.FillEllipse(b, kx, r.Y + (r.Height - d) / 2, d, d);
                if (!Travado) j.Registrar(r, Id, delegate { Mudou(!Ligado); j.Salvou(); }, true);
            }
        }

        class Segmentado : Ctl
        {
            public string[] Opcoes; public int Sel; public Action<int> Mudou;
            float Largura(Configuracoes j, int i) { return Tx.Largura(j.ctl, Opcoes[i]) + j.E(28); }
            public override SizeF Tamanho(Configuracoes j)
            {
                float w = j.E(4) + j.E(2) * (Opcoes.Length - 1);
                for (int i = 0; i < Opcoes.Length; i++) w += Largura(j, i);
                return new SizeF(w, j.E(4) + j.E(6) + j.ctl.Linha);
            }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                var bg = Sobre(j.c.SegBg, fundo);
                j.Preencher(g, r, j.E(7), bg);
                float x = r.X + j.E(2);
                for (int i = 0; i < Opcoes.Length; i++)
                {
                    int idx = i;
                    var rb = new RectangleF(x, r.Y + j.E(2), Largura(j, i), r.Height - j.E(4));
                    string id = Id + ":" + i;
                    Color f = bg, t = j.c.Text;
                    if (i == Sel) { f = j.c.Accent; t = j.c.OnAccent; }
                    else if (j.Sobre(id)) f = Sobre(j.c.Hover, bg);
                    if (f != bg) j.Preencher(g, rb, j.E(5), f);
                    Tx.Linha(g, j.ctl, Opcoes[i], t, f, rb.X + rb.Width / 2, rb.Y + (rb.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                    if (i != Sel) j.Registrar(rb, id, delegate { Mudou(idx); j.Salvou(); }, true);
                    x += rb.Width + j.E(2);
                }
            }
        }

        class Botao : Ctl
        {
            public string Texto; public Action Clique; public bool Desativado;
            public override SizeF Tamanho(Configuracoes j) { return new SizeF(Tx.Largura(j.ctl, Texto) + j.E(24), j.ctl.Linha + j.E(10)); }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                var f = Sobre(!Desativado && j.Sobre(Id) ? j.c.BtnHover : j.c.Btn, fundo);
                j.Preencher(g, r, j.E(6), f);
                Tx.Linha(g, j.ctl, Texto, Desativado ? Sobre(j.c.Text2, f) : j.c.Text, f, r.X + r.Width / 2, r.Y + (r.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                if (!Desativado) j.Registrar(r, Id, Clique, true);
            }
        }

        List<Botao> BotoesAtalho(Config cfg)
        {
            var l = new List<Botao>();
            if (gravandoAtalho)
            {
                l.Add(new Botao { Id = "atalho-cancelar", Texto = "Cancelar", Clique = Atalhos.Cancelar });
                return l;
            }
            l.Add(new Botao
            {
                Id = "atalho-gravar", Texto = "Gravar novo atalho",
                Clique = delegate
                {
                    gravandoAtalho = true; avisoAtalho = null; Invalidate();
                    Atalhos.Gravar((mods, tecla) =>
                    {
                        gravandoAtalho = false;
                        string texto = Atalhos.Texto(mods, (int)tecla);
                        avisoAtalho = app.TrocarAtalho(mods, (int)tecla) ? "Pronto: " + texto + " mostra e oculta o notch."
                            : texto + " já é usado pelo Windows ou por outro programa — o atalho anterior continua valendo.";
                        salvoEm = DateTime.UtcNow;
                        Invalidate();
                    }, delegate { gravandoAtalho = false; avisoAtalho = null; Invalidate(); });
                },
            });
            if (cfg.AtalhoMods != Atalhos.PadraoMods || cfg.AtalhoTecla != Atalhos.PadraoTecla)
                l.Add(new Botao
                {
                    Id = "atalho-padrao", Texto = "Restaurar " + Atalhos.Texto(Atalhos.PadraoMods, Atalhos.PadraoTecla),
                    Clique = delegate
                    {
                        avisoAtalho = app.TrocarAtalho(Atalhos.PadraoMods, Atalhos.PadraoTecla) ? null
                            : Atalhos.Texto(Atalhos.PadraoMods, Atalhos.PadraoTecla) + " está em uso por outro programa — o atalho anterior continua valendo.";
                        salvoEm = DateTime.UtcNow;
                        Invalidate();
                    },
                });
            return l;
        }

        // Combinação desenhada como teclas ("Win" "Y"); nula = gravando (mostra o convite pulsando)
        class Teclas : Ctl
        {
            public string[] Partes;
            float LarguraTecla(Configuracoes j, string t) { return Math.Max(j.E(26), Tx.Largura(j.ctl, t) + j.E(16)); }
            public override SizeF Tamanho(Configuracoes j)
            {
                float h = j.ctl.Linha + j.E(8);
                if (Partes == null) return new SizeF(Tx.Largura(j.ctl, "Pressione as teclas…") + j.E(24), h);
                float w = 0;
                foreach (var p in Partes) w += LarguraTecla(j, p) + j.E(18);
                return new SizeF(w - j.E(18), h);
            }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                if (Partes == null)
                {
                    double t = (DateTime.UtcNow.Millisecond / 1000.0);
                    var f = Sobre(Paleta.Alfa(j.c.Accent, 0.35 + 0.25 * Math.Sin(t * Math.PI * 2)), fundo);
                    j.Preencher(g, r, j.E(6), f);
                    Tx.Linha(g, j.ctl, "Pressione as teclas…", j.c.Text, f, r.X + r.Width / 2, r.Y + (r.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                    return;
                }
                float x = r.X;
                var tecla = Sobre(j.c.Btn, fundo);
                for (int i = 0; i < Partes.Length; i++)
                {
                    float w = LarguraTecla(j, Partes[i]);
                    var rt = new RectangleF(x, r.Y, w, r.Height);
                    j.Preencher(g, rt, j.E(5), tecla);
                    j.Contornar(g, rt, j.E(5), Sobre(j.c.Line, tecla), Math.Max(1, j.E(1)));
                    using (var pen = new Pen(Sobre(j.c.Line, fundo), Math.Max(1, j.E(2)))) g.DrawLine(pen, rt.X + j.E(4), rt.Bottom, rt.Right - j.E(4), rt.Bottom);
                    Tx.Linha(g, j.ctl, Partes[i], j.c.Text, tecla, rt.X + w / 2, rt.Y + (rt.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                    x += w;
                    if (i < Partes.Length - 1)
                    {
                        Tx.Linha(g, j.ctl, "+", Sobre(j.c.Text2, fundo), fundo, x + j.E(9), r.Y + (r.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                        x += j.E(18);
                    }
                }
            }
        }

        // Vários botões lado a lado
        class Botoes : Ctl
        {
            public List<Botao> Itens;
            public override SizeF Tamanho(Configuracoes j)
            {
                float w = 0, h = 0;
                foreach (var b in Itens) { var t = b.Tamanho(j); w += t.Width + j.E(8); h = Math.Max(h, t.Height); }
                return new SizeF(Math.Max(0, w - j.E(8)), h);
            }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                float x = r.X;
                foreach (var b in Itens)
                {
                    var t = b.Tamanho(j);
                    b.Desenhar(j, g, new RectangleF(x, r.Y, t.Width, t.Height), fundo);
                    x += t.Width + j.E(8);
                }
            }
        }

        class Valor : Ctl
        {
            public string Texto;
            public override SizeF Tamanho(Configuracoes j) { return new SizeF(Tx.Largura(j.corpo, Texto) + j.E(2), j.corpo.Linha); }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                Tx.Linha(g, j.corpo, Texto, Sobre(j.c.Text2, fundo), fundo, r.Right, r.Y, Tx.Alinhar.Direita, 0);
            }
        }

        // Valor à direita + trilho de 4 px com bolinha de 12 px, na largura do controle "Transição de cor"
        class Deslizante : Ctl
        {
            public int Min, Max, Atual; public float Largura; public Action<int> Mudou;
            float LarguraValor(Configuracoes j) { return Tx.Largura(j.corpo, "100%") + j.E(2); }
            public override SizeF Tamanho(Configuracoes j) { return new SizeF(LarguraValor(j) + j.E(12) + Largura, Math.Max(j.corpo.Linha, j.E(16))); }
            public override void Desenhar(Configuracoes j, Graphics g, RectangleF r, Color fundo)
            {
                float lv = LarguraValor(j);
                Tx.Linha(g, j.corpo, Atual + "%", Sobre(j.c.Text2, fundo), fundo, r.X + lv, r.Y + (r.Height - j.corpo.Linha) / 2, Tx.Alinhar.Direita, 0);
                var t = new RectangleF(r.Right - Largura, r.Y + (r.Height - j.E(16)) / 2, Largura, j.E(16));
                float f = (Atual - Min) / (float)(Max - Min);
                float cx = t.X + j.E(6) + (t.Width - j.E(12)) * f, cy = t.Y + t.Height / 2;
                j.Preencher(g, new RectangleF(t.X, cy - j.E(2), t.Width, j.E(4)), j.E(2), Sobre(j.c.BtnHover, fundo));
                j.Preencher(g, new RectangleF(t.X, cy - j.E(2), cx - t.X, j.E(4)), j.E(2), j.c.Accent);
                using (var b = new SolidBrush(j.c.Accent)) g.FillEllipse(b, cx - j.E(6), cy - j.E(6), j.E(12), j.E(12));
                var alvoR = RectangleF.Inflate(t, 0, j.E(4));
                Action<float> mover = px =>
                {
                    float fr = (px - t.X - j.E(6)) / Math.Max(1, t.Width - j.E(12));
                    int v = Min + (int)Math.Round(Math.Max(0, Math.Min(1, fr)) * (Max - Min));
                    if (v != Atual) { Atual = v; Mudou(v); j.Invalidate(); }
                };
                j.Registrar(alvoR, Id, null, true, mover);
            }
        }

        // ---------- abas ----------
        Chave NovaChave(string id, bool v, Action<bool> mudou) { return new Chave { Id = id, Ligado = v, Mudou = mudou }; }
        Segmentado NovoSeg(string id, string[] op, int sel, Action<int> mudou) { return new Segmentado { Id = id, Opcoes = op, Sel = sel, Mudou = mudou }; }
        Botao NovoBotao(string id, string t, Action a) { return new Botao { Id = id, Texto = t, Clique = a }; }
        static Bloco B(string secao, params Linha[] linhas) { return new Bloco { Secao = secao, Linhas = linhas.Where(l => l != null).ToList() }; }
        static Legenda L(string t) { return t == null ? null : new Legenda { Texto = t }; }
        static Item I(string rotulo, Ctl ctl) { return new Item { Rotulo = rotulo, C = ctl }; }

        static string Status(Provedor p)
        {
            if (p.Janelas.Count == 0) return p.Erro ?? "Sem leitura ainda.";
            string s = (p.Plano != null ? p.Plano + " · " : "") + (p.Confirmado.HasValue ? "exato " + Tempo.Ha(p.Confirmado.Value) + (p.Fonte != null ? " (" + p.Fonte + ")" : "") : "sem leitura exata");
            if (p.Nota != null) s += " · " + p.Nota;
            return s;
        }

        List<Bloco> Contas()
        {
            var cfg = Config.Atual;
            string outro;
            bool minha = Integracao.BarraInstalada(), deOutro = Integracao.BarraDeOutro(out outro);
            var conectados = new List<Linha>();
            var desconectados = new List<Linha>();
            foreach (var info in Catalogo.Todos)
            {
                var p = Estado.Copia(info.Id);
                bool ligado = cfg.Ativo(info.Id);
                string detalhe = !p.Presente ? Util.Maiuscula(p.Ausencia)
                    : !ligado && p.Janelas.Count == 0 ? "Instalado neste computador — ative para ler o consumo" : Status(p);
                if (p.Presente && p.Detalhe != null) detalhe = p.Detalhe + (detalhe != null ? " · " + detalhe : "");
                var conta = new Conta
                {
                    Provedor = info.Id, Nome = info.Nome, Ligado = ligado, Detalhe = detalhe,
                    Chave = new Chave { Id = "p-" + info.Id, Ligado = ligado, Travado = ligado && cfg.Ativos.Count == 1, Mudou = v => cfg.Ligar(info.Id, v) },
                };
                // "Conectado" = tem anel no notch; "Não conectado" = desligado aqui
                var lista = ligado ? conectados : desconectados;
                lista.Add(conta);
                if (info.Id == "claude")
                {
                    var barra = new Chave
                    {
                        Id = "barra", Ligado = minha, Travado = deOutro,
                        Mudou = v =>
                        {
                            string erro = v ? Integracao.InstalarBarra() : Integracao.RemoverBarra();
                            if (erro != null) MessageBox.Show(this, erro, "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        },
                    };
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Estimativa ao vivo", NovaChave("estimativa", cfg.Estimativa, v => cfg.Estimativa = v)));
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Barra de status do Claude Code", barra));
                    lista.Add(L("A estimativa ao vivo move o anel a cada resposta, pelos tokens gravados nas sessões, entre as leituras exatas do servidor (aparece com “~”). " +
                        (deOutro ? "Já existe outra barra de status no Claude Code; o Pulso não a substitui." :
                         "A barra de status entrega o consumo exato a cada resposta quando o Claude Code roda no terminal; ligar grava uma linha no ~/.claude/settings.json (com backup).")));
                }
                else if (info.Id == "codex" && ligado)
                    lista.Add(L("O Codex grava o consumo exato a cada resposta nos arquivos de sessão: chega na hora, sem rede. O servidor do ChatGPT só é consultado quando o Codex fica parado."));
                else if (info.Id == "gemini" && ligado)
                {
                    string[] leit = { "automatico", "5h", "semana" }, mod = { "gemini", "claude-gpt" };
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Leitura do notch", NovoSeg("ag-leitura", new[] { "Automático", "Limite de 5 horas", "Limite semanal" }, Math.Max(0, Array.IndexOf(leit, cfg.AntigravityLeitura)), i => cfg.AntigravityLeitura = leit[i])));
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Dados do modelo", NovoSeg("ag-modelos", new[] { "Modelos Gemini", "Modelos Claude e GPT" }, Math.Max(0, Array.IndexOf(mod, cfg.AntigravityModelos)), i => cfg.AntigravityModelos = mod[i])));
                }
            }
            conectados.Add(L("Pelo menos um provedor permanece selecionado para que a cápsula nunca fique vazia."));
            var blocos = new List<Bloco> { B("Conectado", conectados.ToArray()) };
            if (desconectados.Count > 0)
            {
                desconectados.Add(L("Estes não têm anel. Ative um e ele entra no fim da lista acima; um provedor não instalado neste computador não aparece na cápsula."));
                blocos.Add(B("Não conectado", desconectados.ToArray()));
            }
            blocos.Add(B(null, L("O Pulso só lê as credenciais que cada ferramenta já mantém no seu usuário, em memória e na hora da consulta. Nada é gravado, copiado ou enviado a outro lugar além do servidor oficial de cada uma.")));
            return blocos;
        }

        List<Bloco> Aparencia()
        {
            var cfg = Config.Atual;
            string[] capMostrar = {
                "O notch fica aberto com todas as leituras visíveis.",
                "Uma pequena cápsula na borda da tela que abre quando você chega nela.",
                "A cápsula some; o Pulso continua contando seu uso e o ícone da bandeja fica visível." };
            int tam = cfg.Tamanho < 0.9 ? 0 : cfg.Tamanho < 1.1 ? 1 : 2;
            string[] capTam = {
                "Ocupa o mínimo de espaço na borda. Legível, mas não do outro lado da mesa.",
                "O tamanho com que o notch foi desenhado.",
                "Mais fácil de ler de relance, e mais difícil de ignorar." };
            int tema = cfg.Tema == Tema.Sistema ? 0 : cfg.Tema == Tema.Claro ? 1 : 2;
            string[] capTema = {
                "A cápsula e esta janela seguem a cor dos aplicativos do Windows.",
                "Cápsula e cartão claros, independentemente da configuração do Windows.",
                "A cápsula escura, independentemente da configuração do Windows." };
            string[] capAnel = {
                "Um anel por provedor, mostrando o limite principal. A cota semanal permanece no cartão ao passar o cursor.",
                "Um anel mais fino para o limite semanal, desenhado dentro do principal. Ele compartilha o espaço com o indicador de atividade.",
                "Um anel mais fino para o limite semanal, desenhado ao redor do principal, no espaço entre o anel e a borda do notch." };
            // Ordem das bordas: Esquerda, Direita, Superior, Inferior
            var bordas = new[] { Borda.Esquerda, Borda.Direita, Borda.Cima, Borda.Baixo };
            var linhasNotch = new List<Linha>
            {
                I("Exibição", NovoSeg("mostrar", new[] { "Sempre exibir", "Exibir ao passar o cursor", "Ocultar" }, (int)cfg.Mostrar, i => { cfg.Mostrar = (Mostrar)i; if (cfg.Mostrar == Mostrar.Oculto) cfg.IconeBandeja = true; })),
                L(capMostrar[(int)cfg.Mostrar]),
                cfg.Mostrar == Mostrar.AoPassar ? I("Cápsula adaptável", NovaChave("adaptavel", cfg.CapsulaAdaptavel, v => cfg.CapsulaAdaptavel = v)) : null,
                cfg.Mostrar == Mostrar.AoPassar ? L("Enquanto o notch está recolhido, a cápsula acompanha o que está atrás dela: clara sobre fundo escuro, preta sobre fundo claro, para continuar fácil de achar. Para isso, o Pulso lê uma faixa fina da tela ao lado da cápsula duas vezes por segundo e guarda só o brilho médio.") : null,
                I("Tamanho", NovoSeg("tamanho", new[] { "Pequeno", "Médio", "Grande" }, tam, i => cfg.Tamanho = new[] { 0.8, 1.0, 1.25 }[i])),
                L(capTam[tam]),
                I("Tema", NovoSeg("tema", new[] { "Do sistema", "Clara", "Escura" }, tema, i => cfg.Tema = new[] { Tema.Sistema, Tema.Claro, Tema.Escuro }[i])),
                L(capTema[tema]),
                I("Anel semanal", NovoSeg("anel", new[] { "Desativado", "Dentro", "Fora" }, (int)cfg.AnelSemanal, i => cfg.AnelSemanal = (AnelSemanal)i)),
                L(capAnel[(int)cfg.AnelSemanal]),
                cfg.AnelSemanal != AnelSemanal.Desligado ? I("Anel semanal tracejado", NovaChave("tracejado", cfg.AnelTracejado, v => cfg.AnelTracejado = v)) : null,
                I("Ritmo do dia no Claude", NovaChave("ritmodia", cfg.RitmoDiario, v => cfg.RitmoDiario = v)),
                L("O anel do Claude passa a mostrar o uso da semana contra a parte liberada até hoje: 1/7 da cota por dia, contado a partir da abertura da semana. Gastando no ritmo, o anel só chega a 100% no fim de cada dia. A sessão de 5 horas continua no cartão."),
                I("Consumo por projeto no cartão", NovaChave("projetos", cfg.ProjetosNoCartao, v => cfg.ProjetosNoCartao = v)),
                L("O cartão do Claude e do Codex mostra quanto da sessão de 5 horas veio de cada pasta de projeto. Conta só o uso deste computador."),
                I("Borda", NovoSeg("borda", new[] { "Esquerda", "Direita", "Superior", "Inferior" }, Array.IndexOf(bordas, cfg.Borda), i => cfg.Borda = bordas[i])),
            };
            var telas = Screen.AllScreens;
            if (telas.Length > 1)
            {
                int sel = 0;
                var nomes = new string[telas.Length];
                for (int i = 0; i < telas.Length; i++)
                {
                    nomes[i] = (i + 1) + (telas[i].Primary ? " · principal" : "");
                    if ((cfg.Monitor == null && telas[i].Primary) || telas[i].DeviceName == cfg.Monitor) sel = i;
                }
                linhasNotch.Add(I("Tela", NovoSeg("tela", nomes, sel, i => cfg.Monitor = telas[i].Primary ? null : telas[i].DeviceName)));
                linhasNotch.Add(L(string.Join("   ", telas.Select((t, i) => (i + 1) + ": " + t.Bounds.Width + " × " + t.Bounds.Height).ToArray())));
            }
            linhasNotch.Add(I("Arrastável", NovaChave("arrastavel", cfg.Arrastavel, v => cfg.Arrastavel = v)));
            if (cfg.Arrastavel)
                linhasNotch.Add(new Item
                {
                    Dica = true,
                    Rotulo = "Segure Alt e arraste o notch, ou arraste a engrenagem ou os pontinhos ao lado dela, para levá-lo pelas bordas da tela. Cada borda lembra onde você o deixou. Recentralizar o põe de volta no meio da borda em que está.",
                    C = NovoBotao("recentralizar", "Recentralizar", delegate { cfg.PosicaoNaBorda = 0.5; Salvou(); }),
                });
            else
                linhasNotch.Add(L("O notch fica fixo no meio da borda escolhida acima. Ao passar o cursor na orelha de baixo aparece só a engrenagem das configurações, sem os pontinhos de arrastar."));

            var transicao = NovoSeg("transicao", new[] { "Degrau seco", "Rampa de cor" }, cfg.CorGradual ? 1 : 0, i => cfg.CorGradual = i == 1);
            float largDesl = transicao.Tamanho(this).Width;
            var atencao = new Deslizante { Id = "atencao", Min = 1, Max = 99, Atual = (int)Math.Round(cfg.LimiteAtencao * 100), Largura = largDesl };
            var critico = new Deslizante { Id = "critico", Min = 1, Max = 100, Atual = (int)Math.Round(cfg.LimiteCritico * 100), Largura = largDesl };
            atencao.Mudou = v => { cfg.LimiteAtencao = v / 100.0; if (cfg.LimiteCritico <= cfg.LimiteAtencao) cfg.LimiteCritico = Math.Min(1, cfg.LimiteAtencao + 0.01); };
            critico.Mudou = v => { cfg.LimiteCritico = Math.Max(0.02, v / 100.0); if (cfg.LimiteAtencao >= cfg.LimiteCritico) cfg.LimiteAtencao = cfg.LimiteCritico - 0.01; };

            return new List<Bloco>
            {
                B("Notch", linhasNotch.ToArray()),
                B("Limites de uso",
                    I("Transição de cor", transicao),
                    L(cfg.CorGradual ? "A cor muda continuamente de verde para vermelho em toda a faixa e fica amarela no limite de atenção abaixo."
                                     : "Verde, amarelo e vermelho permanecem cores sólidas e mudam de uma para outra nos limites abaixo."),
                    I("Limite de atenção", atencao),
                    I("Limite crítico", critico),
                    cfg.CorGradual ? L("Com a rampa de cor ativada, o anel só fica vermelho em 100%, então o limite crítico vale no modo Degrau seco.") : null,
                    I("", NovoBotao("restaurar", "Restaurar padrões", delegate { cfg.CorGradual = false; cfg.LimiteAtencao = 0.5; cfg.LimiteCritico = 0.7; Salvou(); }))),
                B("App",
                    I("Ícone do app", NovoSeg("bandeja", new[] { "Bandeja do sistema", "Nenhum" }, cfg.IconeBandeja ? 0 : 1, i => cfg.IconeBandeja = i == 0 || cfg.Mostrar == Mostrar.Oculto)),
                    L(cfg.Mostrar == Mostrar.Oculto ? "Com o notch oculto, o ícone na bandeja fica, para você não perder o acesso."
                        : cfg.IconeBandeja ? "O ícone na bandeja mostra um mini anel com o consumo atual." : "Sem ícone na bandeja; o notch é o único acesso.")),
            };
        }

        List<Bloco> Geral()
        {
            var cfg = Config.Atual;
            return new List<Bloco>
            {
                B(null,
                    I("Abrir o Pulso ao entrar no Windows", NovaChave("iniciar", cfg.IniciarComWindows, v => { cfg.IniciarComWindows = v; Integracao.IniciarComWindows(v); })),
                    L("O Pulso abre sozinho toda vez que você entra neste computador e fica quieto em segundo plano. Desative e você precisará abri-lo manualmente."),
                    I("Atalho para mostrar e ocultar o notch", NovaChave("atalho", cfg.Atalho, v => { cfg.Atalho = v; app.AplicarAtalho(); })),
                    I("Teclas", new Teclas { Partes = gravandoAtalho ? null : Atalhos.Partes(cfg.AtalhoMods, cfg.AtalhoTecla) }),
                    I("", new Botoes { Itens = BotoesAtalho(cfg) }),
                    L(gravandoAtalho ? "Pressione a combinação: Ctrl, Alt, Shift ou Win com outra tecla, ou uma tecla de F1 a F24. Esc cancela."
                        : avisoAtalho ?? (!cfg.Atalho ? "Desativado." : app.AtalhoAtivo ? "Mostra e oculta o notch de qualquer lugar."
                        : cfg.AtalhoTexto + " está em uso por outro programa — grave outra combinação."))),
                B("Notificações",
                    I("Avisos de renovação", NovaChave("renovacao", cfg.AvisoRenovacao, v => cfg.AvisoRenovacao = v)),
                    L("Mostra um cartão ao lado do notch quando o Claude ou o Codex renova uma janela depois de pelo menos 10% de uso. O cartão aparece mesmo com o notch oculto."),
                    I("Aviso de limite", NovaChave("limite", cfg.AvisoLimite, v => cfg.AvisoLimite = v)),
                    L("Avisa quando uma janela chega a 80%, a 95% e ao limite, uma vez por janela — sempre pelo valor exato, nunca pela estimativa."),
                    I("Aviso de ritmo", NovaChave("ritmo", cfg.AvisoRitmo, v => cfg.AvisoRitmo = v)),
                    L("Avisa quando, no ritmo dos últimos 30 minutos, a sessão de 5 horas vai acabar antes de renovar — com tempo de desacelerar antes dos avisos de 80% e 95%. Uma vez por sessão."),
                    I("Sessão terminou", NovaChave("fim", cfg.AvisoSessaoFim, v => cfg.AvisoSessaoFim = v)),
                    L("Avisa quando o Claude ou o Codex termina uma resposta que levou pelo menos 10 segundos — a não ser que você já esteja na janela dela. Clicar no cartão leva até a janela."),
                    I("Sessão esperando você", NovaChave("espera", cfg.AvisoSessaoEspera, v => cfg.AvisoSessaoEspera = v)),
                    L("Avisa quando uma sessão para e pede a sua resposta (permissão, pergunta)."),
                    I("Som dos avisos", NovaChave("som", cfg.SomAvisos, v => cfg.SomAvisos = v)),
                    I("Pré-visualizar o cartão", NovoBotao("previa", "Pré-visualizar", delegate { app.Cartao.Mostrar(Avisos.Exemplo()); }))),
                B("Atualizações", LinhasAtualizacao().ToArray()),
                B(null,
                    I("Pasta de dados", NovoBotao("pasta", "Abrir pasta", delegate { try { Process.Start(new ProcessStartInfo(Caminhos.Dados) { UseShellExecute = true }); } catch { } })),
                    L("Tudo o que o Pulso guarda fica numa pasta: configurações, estado, índice de custo e log. Nenhuma credencial."),
                    Instalacao.Instalado ? I("Desinstalar o Pulso", NovoBotao("desinstalar", "Desinstalar…", Instalacao.AbrirDesinstalador)) : null,
                    Instalacao.Instalado ? L("Também dá para desinstalar em Configurações › Aplicativos do Windows. Você escolhe se as suas configurações ficam.") : null),
            };
        }

        List<Linha> LinhasAtualizacao()
        {
            var l = new List<Linha>();
            l.Add(I("Versão", new Valor { Texto = Instalacao.Versao + (string.IsNullOrEmpty(Atualizacao.Commit) ? "" : " · " + Atualizacao.Commit) }));
            if (Instalacao.Origem == null)
            {
                l.Add(L("Para receber atualizações por aqui, instale o Pulso pelo instalar.cmd da pasta do projeto clonada do GitHub."));
                return l;
            }
            var st = Atualizacao.Estado;
            var botoes = new List<Botao>();
            if (st == Atualizacao.Situacao.Disponivel) botoes.Add(NovoBotao("atualizar", "Atualizar agora", Atualizacao.Aplicar));
            botoes.Add(new Botao
            {
                Id = "procurar", Texto = st == Atualizacao.Situacao.Procurando ? "Procurando…" : "Procurar atualização",
                Desativado = st == Atualizacao.Situacao.Procurando, Clique = delegate { Atualizacao.Procurar(true); },
            });
            l.Add(I("", new Botoes { Itens = botoes }));
            string texto;
            switch (st)
            {
                case Atualizacao.Situacao.Disponivel: texto = "Versão nova no GitHub (" + Atualizacao.Detalhe + "). Atualizar agora baixa, compila e reabre o Pulso em alguns segundos."; break;
                case Atualizacao.Situacao.EmDia: texto = "Você está com a versão mais recente."; break;
                case Atualizacao.Situacao.Erro: texto = Atualizacao.Detalhe; break;
                case Atualizacao.Situacao.Procurando: texto = "Consultando o GitHub…"; break;
                default: texto = "O Pulso procura sozinho 2 minutos depois de abrir e a cada 12 horas, sem interromper você."; break;
            }
            l.Add(L(texto));
            return l;
        }

        // ---------- interação ----------
        Alvo AlvoEm(Point p)
        {
            bool noCorpo = Corpo.Contains(p);
            for (int i = alvos.Count - 1; i >= 0; i--)
            {
                var a = alvos[i];
                if (a.NoCorpo && !noCorpo) continue;
                if (a.R.Contains(p)) return a;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (segurando != null && segurando.Arrastar != null) { segurando.Arrastar(e.X); return; }
            var a = AlvoEm(e.Location);
            string id = a != null ? a.Id : null;
            Cursor = a != null ? Cursors.Hand : Cursors.Default;
            if (id != sobre) { sobre = id; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sobre != null) { sobre = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var a = AlvoEm(e.Location);
            if (a != null && a.Arrastar != null) { segurando = a; Capture = true; a.Arrastar(e.X); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (segurando != null)
            {
                segurando = null; Capture = false;
                Salvou();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            var a = AlvoEm(e.Location);
            if (a != null && a.Clique != null) a.Clique();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float max = Math.Max(0, alturaConteudo - Corpo.Height);
            rolagem = Math.Max(0, Math.Min(max, rolagem - e.Delta / 120f * E(48)));
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        // Arrastar a janela pela faixa do topo da barra lateral, pelo vazio dela e pelo cabeçalho
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
            base.WndProc(ref m);
            if (m.Msg != WM_NCHITTEST || (int)m.Result != HTCLIENT) return;
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (AlvoEm(p) != null) return;
            var lat = Lateral;
            bool faixa = lat.Contains(p) && (p.Y < lat.Y + E(48) || p.Y > lat.Y + E(48) + E(34) * NomesAbas.Length);
            bool cabecalho = p.X >= PaneX && p.Y < E(52);
            if (faixa || cabecalho) m.Result = (IntPtr)HTCAPTION;
        }

        // Diagnóstico: desenha cada aba num PNG, fora da tela e sem tomar o foco
        public static void Capturar(App app, string pasta)
        {
            using (var f = new Configuracoes(app) { SemAtivar = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), ShowInTaskbar = false })
            {
                f.Show();
                for (int i = 0; i < 3; i++)
                {
                    f.MostrarAba(i);
                    // Captura o conteúdo inteiro (sem a dobra): cresce a janela até caber tudo
                    f.ClientSize = new Size(f.ClientSize.Width, (int)Math.Round(f.E(520)));
                    using (var rascunho = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                        f.DrawToBitmap(rascunho, new Rectangle(0, 0, rascunho.Width, rascunho.Height)); // pinta uma vez para medir
                    int alto = (int)Math.Ceiling(f.alturaConteudo + f.E(52));
                    if (alto > f.ClientSize.Height) f.ClientSize = new Size(f.ClientSize.Width, alto);
                    using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
                        bmp.Save(System.IO.Path.Combine(pasta, "config-" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                f.Close();
            }
        }
    }
}
