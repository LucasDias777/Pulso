using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
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
        readonly Tx.Estilo corpo, forte, titulo, peq, ctl, marca;
        readonly Timer relogio = new Timer { Interval = 250 };
        DateTime salvoEm = DateTime.MinValue;
        public bool SemAtivar;

        class Alvo { public RectangleF R; public string Id; public Action Clique; public Action<float> Arrastar; public bool NoCorpo; }
        readonly List<Alvo> alvos = new List<Alvo>();
        string sobre;
        Alvo segurando;
        bool gravandoAtalho;
        Func<string> avisoAtalho; // montado na hora de desenhar, para seguir o idioma

        static readonly string[] NomesAbas = { "Contas", "Aparência", "Geral", "Relatórios" };

        // Aba Relatórios: período mostrado (0 = o atual, -1 = o anterior...) e provedor filtrado (nulo = todos)
        Relatorio.Periodo relPeriodo = Relatorio.Periodo.Semana;
        int relDesloc;
        string relProvedor;

        public Configuracoes(App app)
        {
            this.app = app;
            k = DeviceDpi / 96f;
            Text = "Pulso — Configurações".T();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size((int)Math.Round(E(760)), (int)Math.Round(E(600)));
            ShowInTaskbar = true;
            KeyPreview = true;
            Icon = IconeApp();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            const string fam = "Segoe UI Variable Text";
            corpo = Tx.Novo(fam, 400, E(14.5f), E(14.5f * 1.35f));
            forte = Tx.Novo(fam, 600, E(14.5f), E(14.5f * 1.35f));
            titulo = Tx.Novo(fam, 600, E(24), E(54));
            peq = Tx.Novo(fam, 400, E(13), E(13 * 1.4f));
            ctl = Tx.Novo(fam, 400, E(14), E(14 * 1.35f));
            marca = Tx.Novo(fam, 600, E(20), E(20 * 1.35f));

            relogio.Tick += delegate
            {
                // Toast some sozinho; na aba Contas o "exato há X s" anda; nos Relatórios, as respostas novas entram a cada 5 s
                var agora = DateTime.UtcNow;
                if (gravandoAtalho || (agora - salvoEm).TotalSeconds < 2 || (aba == 0 && agora.Millisecond < 260) || (aba == 3 && agora.Second % 5 == 0 && agora.Millisecond < 260)) Invalidate();
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
        // A seção Atualizações fica no fim da aba Geral: rola até o fim (a pintura limita à rolagem máxima)
        public void MostrarAtualizacoes() { aba = 2; rolagem = float.MaxValue; Invalidate(); }

        void Salvou()
        {
            Config.Atual.Salvar();
            app.AplicarConfig();
            Text = "Pulso — Configurações".T();
            salvoEm = DateTime.UtcNow;
            Invalidate();
        }

        // ---------- geometria da janela ----------
        RectangleF Lateral { get { return new RectangleF(E(4), E(4), E(206), ClientSize.Height - E(8)); } }
        float PaneX { get { return E(214); } }
        RectangleF Fechar { get { return new RectangleF(ClientSize.Width - E(46), 0, E(46), E(34)); } }
        RectangleF Corpo { get { return new RectangleF(PaneX, E(54), ClientSize.Width - PaneX, ClientSize.Height - E(54)); } }

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
            // O conteúdo encolheu (outro período nos Relatórios): a rolagem volta para dentro dele
            float maxRolagem = Math.Max(0, alturaConteudo - corpoR.Height);
            if (rolagem > maxRolagem) { rolagem = maxRolagem; Invalidate(); }

            DesenharToast(g);
        }

        void Registrar(RectangleF r, string id, Action clique, bool noCorpo, Action<float> arrastar = null)
        {
            alvos.Add(new Alvo { R = r, Id = id, Clique = clique, NoCorpo = noCorpo, Arrastar = arrastar });
        }

        bool Sobre(string id) { return sobre == id; }

        // Bandeira de cada idioma (Idioma.Codigos: Brasil, Estados Unidos, Espanha) em vetor: o Windows não desenha
        // emoji de bandeira. Simplificadas para o tamanho de um ícone (sem estrelas nem brasão).
        static void DesenharBandeira(Graphics g, int idioma, RectangleF r)
        {
            var est = g.Save();
            using (var forma = Arred(r, r.Height * 0.16f))
            {
                g.SetClip(forma, CombineMode.Intersect);
                float w = r.Width, h = r.Height, cx = r.X + w / 2, cy = r.Y + h / 2;
                switch (idioma)
                {
                    case 0: // Brasil: verde, losango amarelo, círculo azul com a faixa branca
                        using (var b = new SolidBrush(Paleta.Hex("#009c3b"))) g.FillRectangle(b, r);
                        using (var b = new SolidBrush(Paleta.Hex("#ffdf00")))
                            g.FillPolygon(b, new[] { new PointF(r.X + w * 0.085f, cy), new PointF(cx, r.Y + h * 0.12f), new PointF(r.Right - w * 0.085f, cy), new PointF(cx, r.Bottom - h * 0.12f) });
                        float rc = h * 0.25f;
                        using (var circulo = new GraphicsPath())
                        {
                            circulo.AddEllipse(cx - rc, cy - rc, 2 * rc, 2 * rc);
                            using (var b = new SolidBrush(Paleta.Hex("#002776"))) g.FillPath(b, circulo);
                            g.SetClip(circulo, CombineMode.Intersect);
                            using (var pen = new Pen(Color.White, Math.Max(1, h * 0.07f)))
                                g.DrawArc(pen, cx - rc * 2.6f, cy - rc * 0.55f, rc * 4.4f, rc * 4.4f, 200, 100);
                        }
                        break;
                    case 1: // Estados Unidos: 13 listras e o cantão azul
                        using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, r);
                        using (var b = new SolidBrush(Paleta.Hex("#b22234")))
                            for (int i = 0; i < 13; i += 2) g.FillRectangle(b, r.X, r.Y + h * i / 13f, w, h / 13f);
                        using (var b = new SolidBrush(Paleta.Hex("#3c3b6e"))) g.FillRectangle(b, r.X, r.Y, w * 0.42f, h * 7 / 13f);
                        break;
                    default: // Espanha: vermelho, amarelo (o dobro), vermelho
                        using (var b = new SolidBrush(Paleta.Hex("#aa151b"))) g.FillRectangle(b, r);
                        using (var b = new SolidBrush(Paleta.Hex("#f1bf00"))) g.FillRectangle(b, r.X, r.Y + h / 4, w, h / 2);
                        break;
                }
                g.Restore(est);
                using (var pen = new Pen(Color.FromArgb(60, 128, 128, 128), 1)) g.DrawPath(pen, forma);
            }
        }

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

        // Barra lateral: cartão recuado 4 px, marca do Pulso no topo (na altura do título da página), linhas com selo
        void DesenharLateral(Graphics g)
        {
            var r = Lateral;
            Preencher(g, r, E(14), c.Side);
            Contornar(g, r, E(14), Sobre(c.Line, c.Side), Math.Max(1, E(1)));
            // Logo e nome na altura do título da página, com folga até as abas
            float t = E(28), meio = E(29);
            DesenharLogo(g, r.X + E(14), meio - t / 2, t);
            Tx.Linha(g, marca, "Pulso", c.Text, c.Side, r.X + E(14) + t + E(11), meio - marca.Linha / 2, Tx.Alinhar.Esquerda, 0);
            float x = r.X + E(8), w = r.Width - E(16), y = r.Y + E(66);
            for (int i = 0; i < NomesAbas.Length; i++)
            {
                int idx = i;
                LinhaLateral(g, new RectangleF(x, y, w, E(36)), "aba" + i, NomesAbas[i].T(), i, aba == i, delegate { MostrarAba(idx); });
                y += E(38);
            }
            LinhaLateral(g, new RectangleF(x, r.Bottom - E(8) - E(36), w, E(36)), "sair", "Encerrar o Pulso".T(), 4, false, delegate { Close(); app.Sair(); });
        }

        // O ícone do app (tools\gerar-icone.ps1: disco, trilho, arco de consumo e miolo) em vetor, nítido em qualquer DPI
        static void DesenharLogo(Graphics g, float x, float y, float t)
        {
            float m = t * 0.04f, d = t - 2 * m;
            using (var b = new SolidBrush(Color.Black)) g.FillEllipse(b, x + m, y + m, d, d);
            float esp = t * 0.15f, r0 = m + esp / 2 + t * 0.05f, dr = t - 2 * r0;
            using (var p = new Pen(Color.FromArgb(48, 48, 48), esp)) g.DrawEllipse(p, x + r0, y + r0, dr, dr);
            using (var p = new Pen(Color.FromArgb(0, 255, 136), esp) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p, x + r0, y + r0, dr, dr, -90, 250);
            float c = t * 0.13f;
            using (var b = new SolidBrush(Color.FromArgb(232, 232, 234))) g.FillEllipse(b, x + t / 2 - c, y + t / 2 - c, 2 * c, 2 * c);
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
            { "#4fa6ff", "#0a6cff" }, { "#8784ff", "#5856d6" }, { "#a6a6ab", "#727277" }, { "#3ddc84", "#12a150" }, { "#ff6d62", "#dd3328" },
        };
        static readonly GraphicsPath[] Icones = new GraphicsPath[5];

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
                case 3: // gráfico de barras (viewBox 12)
                    p = new GraphicsPath();
                    foreach (var barra in new[] { new RectangleF(1.3f, 6.6f, 2.6f, 4.4f), new RectangleF(4.7f, 3.6f, 2.6f, 7.4f), new RectangleF(8.1f, 1f, 2.6f, 10f) })
                        using (var b = Arred(barra, 0.7f)) p.AddPath(b, false);
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
                if (i == 4) using (var pen = new Pen(Color.White, E(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(pen, ic);
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
            Tx.Linha(g, titulo, NomesAbas[aba].T(), c.Text, c.Pane, PaneX + E(24), 0, Tx.Alinhar.Esquerda, 0);
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
            string txt = "Salvo".T();
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
            var blocos = aba == 0 ? Contas() : aba == 1 ? Aparencia() : aba == 2 ? Geral() : Relatorios();
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
            public bool SoTela;                                // muda só o que a janela mostra (Relatórios): sem salvar nem "Salvo"
            public Action<Graphics, int, RectangleF> Icone;   // desenhado antes do texto de cada opção (bandeiras do idioma)
            float IconeL(Configuracoes j) { return Icone == null ? 0 : j.E(20) + j.E(7); }
            float Largura(Configuracoes j, int i) { return Tx.Largura(j.ctl, Opcoes[i]) + j.E(28) + IconeL(j); }
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
                    float ty = rb.Y + (rb.Height - j.ctl.Linha) / 2;
                    if (Icone == null) Tx.Linha(g, j.ctl, Opcoes[i], t, f, rb.X + rb.Width / 2, ty, Tx.Alinhar.Centro, 0);
                    else
                    {
                        // Ícone + texto centrados juntos na opção
                        float x0 = (float)Math.Round(rb.X + (rb.Width - IconeL(j) - Tx.Largura(j.ctl, Opcoes[i])) / 2);
                        float ih = (float)Math.Round(j.E(14));
                        Icone(g, i, new RectangleF(x0, (float)Math.Round(rb.Y + (rb.Height - ih) / 2), (float)Math.Round(j.E(20)), ih));
                        Tx.Linha(g, j.ctl, Opcoes[i], t, f, x0 + IconeL(j), ty, Tx.Alinhar.Esquerda, 0);
                    }
                    if (i != Sel) j.Registrar(rb, id, delegate { Mudou(idx); if (SoTela) j.Invalidate(); else j.Salvou(); }, true);
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
                l.Add(new Botao { Id = "atalho-cancelar", Texto = "Cancelar".T(), Clique = Atalhos.Cancelar });
                return l;
            }
            l.Add(new Botao
            {
                Id = "atalho-gravar", Texto = "Gravar novo atalho".T(),
                Clique = delegate
                {
                    gravandoAtalho = true; avisoAtalho = null; Invalidate();
                    Atalhos.Gravar((mods, tecla) =>
                    {
                        gravandoAtalho = false;
                        bool trocou = app.TrocarAtalho(mods, (int)tecla);
                        avisoAtalho = delegate
                        {
                            string texto = Atalhos.Texto(mods, (int)tecla);
                            return trocou ? "Pronto: {0} mostra e oculta a cápsula.".T(texto)
                                : "{0} já é usado pelo Windows ou por outro programa — o atalho anterior continua valendo.".T(texto);
                        };
                        salvoEm = DateTime.UtcNow;
                        Invalidate();
                    }, delegate { gravandoAtalho = false; avisoAtalho = null; Invalidate(); });
                },
            });
            if (cfg.AtalhoMods != Atalhos.PadraoMods || cfg.AtalhoTecla != Atalhos.PadraoTecla)
                l.Add(new Botao
                {
                    Id = "atalho-padrao", Texto = "Restaurar {0}".T(Atalhos.Texto(Atalhos.PadraoMods, Atalhos.PadraoTecla)),
                    Clique = delegate
                    {
                        avisoAtalho = app.TrocarAtalho(Atalhos.PadraoMods, Atalhos.PadraoTecla) ? null
                            : (Func<string>)(() => "{0} está em uso por outro programa — o atalho anterior continua valendo.".T(Atalhos.Texto(Atalhos.PadraoMods, Atalhos.PadraoTecla)));
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
                if (Partes == null) return new SizeF(Tx.Largura(j.ctl, "Pressione as teclas…".T()) + j.E(24), h);
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
                    Tx.Linha(g, j.ctl, "Pressione as teclas…".T(), j.c.Text, f, r.X + r.Width / 2, r.Y + (r.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
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
            if (p.Janelas.Count == 0) return p.Erro.T() ?? "Sem leitura ainda.".T();
            string s = (p.Plano != null ? p.Plano.T() + " · " : "") + (p.Confirmado.HasValue ? "exato {0}".T(Tempo.Ha(p.Confirmado.Value)) + (p.Fonte != null ? " (" + p.Fonte.T() + ")" : "") : "sem leitura exata".T());
            if (p.Nota != null) s += " · " + p.Nota.T();
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
                string detalhe = !p.Presente ? Util.Maiuscula(p.Ausencia.T())
                    : !ligado && p.Janelas.Count == 0 ? "Instalado neste computador — ative para ler o consumo".T() : Status(p);
                if (p.Presente && p.Detalhe != null) detalhe = p.Detalhe.T() + (detalhe != null ? " · " + detalhe : "");
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
                            if (erro != null) MessageBox.Show(this, erro.T(), "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        },
                    };
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Estimativa ao vivo".T(), NovaChave("estimativa", cfg.Estimativa, v => cfg.Estimativa = v)));
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Barra de status do Claude Code".T(), barra));
                    lista.Add(L("Estimativa ao vivo: o anel anda a cada resposta, sem esperar o servidor (o número aparece com “~”).".T() + " " +
                        (deOutro ? "Barra de status: o Claude Code já usa outra barra, e o Pulso não a substitui.".T() :
                         "Barra de status: quando você usa o Claude Code no terminal, o número exato chega a cada resposta.".T())));
                }
                else if (info.Id == "gemini" && ligado)
                {
                    string[] leit = { "automatico", "5h", "semana" }, mod = { "gemini", "claude-gpt" };
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Leitura da cápsula".T(), NovoSeg("ag-leitura", new[] { "Automático".T(), "Limite de 5 horas".T(), "Limite semanal".T() }, Math.Max(0, Array.IndexOf(leit, cfg.AntigravityLeitura)), i => cfg.AntigravityLeitura = leit[i])));
                    conta.Subs.Add(new KeyValuePair<string, Ctl>("Dados do modelo".T(), NovoSeg("ag-modelos", new[] { "Modelos Gemini".T(), "Modelos Claude e GPT".T() }, Math.Max(0, Array.IndexOf(mod, cfg.AntigravityModelos)), i => cfg.AntigravityModelos = mod[i])));
                }
            }
            conectados.Add(L("Pelo menos um provedor permanece selecionado para que a cápsula nunca fique vazia.".T()));
            var blocos = new List<Bloco> { B("Conectado".T(), conectados.ToArray()) };
            if (desconectados.Count > 0)
            {
                desconectados.Add(L("Estes não têm anel. Ative um e ele entra no fim da lista acima; um provedor não instalado neste computador não aparece na cápsula.".T()));
                blocos.Add(B("Não conectado".T(), desconectados.ToArray()));
            }
            return blocos;
        }

        List<Bloco> Aparencia()
        {
            var cfg = Config.Atual;
            string[] capMostrar = {
                "A cápsula fica aberta com todas as leituras visíveis.".T(),
                "Uma pequena cápsula na borda da tela que abre quando você chega nela.".T(),
                "A cápsula some; o Pulso continua contando seu uso e o ícone da bandeja fica visível.".T() };
            int tam = cfg.Tamanho < 0.9 ? 0 : cfg.Tamanho < 1.1 ? 1 : 2;
            string[] capTam = {
                "Cápsula e anéis menores, para ocupar o mínimo da borda. O texto dos cartões continua do tamanho do Médio.".T(),
                "O tamanho com que a cápsula foi desenhada.".T(),
                "Mais fácil de ler de relance, e mais difícil de ignorar.".T() };
            int tema = cfg.Tema == Tema.Sistema ? 0 : cfg.Tema == Tema.Claro ? 1 : 2;
            string[] capTema = {
                "A cápsula e esta janela seguem a cor dos aplicativos do Windows.".T(),
                "Cápsula e cartão claros, independentemente da configuração do Windows.".T(),
                "A cápsula escura, independentemente da configuração do Windows.".T() };
            string[] capAnel = {
                "Um anel por provedor, mostrando o limite principal. A cota semanal permanece no cartão ao passar o cursor.".T(),
                "Um anel mais fino para o limite semanal, desenhado dentro do principal. Ele compartilha o espaço com o indicador de atividade.".T(),
                "Um anel mais fino para o limite semanal, desenhado ao redor do principal, no espaço entre o anel e a borda da cápsula.".T() };
            // Ordem das bordas: Esquerda, Direita, Superior, Inferior
            var bordas = new[] { Borda.Esquerda, Borda.Direita, Borda.Cima, Borda.Baixo };
            var linhasNotch = new List<Linha>
            {
                I("Exibição".T(), NovoSeg("mostrar", new[] { "Sempre exibir".T(), "Exibir ao passar o cursor".T(), "Ocultar".T() }, (int)cfg.Mostrar, i => { cfg.Mostrar = (Mostrar)i; if (cfg.Mostrar == Mostrar.Oculto) cfg.IconeBandeja = true; })),
                L(capMostrar[(int)cfg.Mostrar]),
                cfg.Mostrar == Mostrar.AoPassar ? I("Cápsula adaptável".T(), NovaChave("adaptavel", cfg.CapsulaAdaptavel, v => cfg.CapsulaAdaptavel = v)) : null,
                cfg.Mostrar == Mostrar.AoPassar ? L("Enquanto está recolhida, a cápsula acompanha o que está atrás dela: clara sobre fundo escuro, preta sobre fundo claro, para continuar fácil de achar. Para isso, o Pulso lê uma faixa fina da tela ao lado da cápsula duas vezes por segundo e guarda só o brilho médio.".T()) : null,
                I("Tamanho".T(), NovoSeg("tamanho", new[] { "Pequeno".T(), "Médio".T(), "Grande".T() }, tam, i => cfg.Tamanho = new[] { 0.8, 1.0, 1.25 }[i])),
                L(capTam[tam]),
                I("Tema".T(), NovoSeg("tema", new[] { "Do sistema".T(), "Clara".T(), "Escura".T() }, tema, i => cfg.Tema = new[] { Tema.Sistema, Tema.Claro, Tema.Escuro }[i])),
                L(capTema[tema]),
                I("Anel semanal".T(), NovoSeg("anel", new[] { "Desativado".T(), "Dentro".T(), "Fora".T() }, (int)cfg.AnelSemanal, i => cfg.AnelSemanal = (AnelSemanal)i)),
                L(capAnel[(int)cfg.AnelSemanal]),
                cfg.AnelSemanal != AnelSemanal.Desligado ? I("Anel semanal tracejado".T(), NovaChave("tracejado", cfg.AnelTracejado, v => cfg.AnelTracejado = v)) : null,
                I("Ritmo do dia no Claude".T(), NovaChave("ritmodia", cfg.RitmoDiario, v => cfg.RitmoDiario = v)),
                L("O anel do Claude passa a mostrar o uso da semana contra a parte liberada até hoje: 1/7 da cota por dia, contado a partir da abertura da semana. Gastando no ritmo, o anel só chega a 100% no fim de cada dia. A sessão de 5 horas continua no cartão.".T()),
                I("Consumo por projeto no cartão".T(), NovaChave("projetos", cfg.ProjetosNoCartao, v => cfg.ProjetosNoCartao = v)),
                L("O cartão do Claude e do Codex mostra quanto da sessão de 5 horas veio de cada pasta de projeto. Conta só o uso deste computador.".T()),
                I("Borda".T(), NovoSeg("borda", new[] { "Esquerda".T(), "Direita".T(), "Superior".T(), "Inferior".T() }, Array.IndexOf(bordas, cfg.Borda), i => cfg.Borda = bordas[i])),
            };
            var telas = Screen.AllScreens;
            if (telas.Length > 1)
            {
                int sel = 0;
                var nomes = new string[telas.Length];
                for (int i = 0; i < telas.Length; i++)
                {
                    nomes[i] = telas[i].Primary ? "{0} · principal".T(i + 1) : (i + 1).ToString();
                    if ((cfg.Monitor == null && telas[i].Primary) || telas[i].DeviceName == cfg.Monitor) sel = i;
                }
                linhasNotch.Add(I("Tela".T(), NovoSeg("tela", nomes, sel, i => cfg.Monitor = telas[i].Primary ? null : telas[i].DeviceName)));
                linhasNotch.Add(L(string.Join("   ", telas.Select((t, i) => (i + 1) + ": " + t.Bounds.Width + " × " + t.Bounds.Height).ToArray())));
            }
            linhasNotch.Add(I("Arrastável".T(), NovaChave("arrastavel", cfg.Arrastavel, v => cfg.Arrastavel = v)));
            if (cfg.Arrastavel)
                linhasNotch.Add(new Item
                {
                    Dica = true,
                    Rotulo = "Segure Alt e arraste a cápsula, ou arraste a engrenagem ou os pontinhos ao lado dela, para levá-la pelas bordas da tela. Cada borda lembra onde você a deixou. Recentralizar a põe de volta no meio da borda em que está.".T(),
                    C = NovoBotao("recentralizar", "Recentralizar".T(), delegate { cfg.PosicaoNaBorda = 0.5; Salvou(); }),
                });
            else
                linhasNotch.Add(L("A cápsula fica travada onde você a deixou (no meio da borda, se nunca foi arrastada). Ao passar o cursor na orelha de baixo aparece só a engrenagem das configurações, sem os pontinhos de arrastar.".T()));

            var transicao = NovoSeg("transicao", new[] { "Degrau seco".T(), "Rampa de cor".T() }, cfg.CorGradual ? 1 : 0, i => cfg.CorGradual = i == 1);
            float largDesl = transicao.Tamanho(this).Width;
            var atencao = new Deslizante { Id = "atencao", Min = 1, Max = 99, Atual = (int)Math.Round(cfg.LimiteAtencao * 100), Largura = largDesl };
            var critico = new Deslizante { Id = "critico", Min = 1, Max = 100, Atual = (int)Math.Round(cfg.LimiteCritico * 100), Largura = largDesl };
            atencao.Mudou = v => { cfg.LimiteAtencao = v / 100.0; if (cfg.LimiteCritico <= cfg.LimiteAtencao) cfg.LimiteCritico = Math.Min(1, cfg.LimiteAtencao + 0.01); };
            critico.Mudou = v => { cfg.LimiteCritico = Math.Max(0.02, v / 100.0); if (cfg.LimiteAtencao >= cfg.LimiteCritico) cfg.LimiteAtencao = cfg.LimiteCritico - 0.01; };

            return new List<Bloco>
            {
                B("Cápsula".T(), linhasNotch.ToArray()),
                B("Limites de uso".T(),
                    I("Transição de cor".T(), transicao),
                    L(cfg.CorGradual ? "A cor muda continuamente de verde para vermelho em toda a faixa e fica amarela no limite de atenção abaixo.".T()
                                     : "Verde, amarelo e vermelho permanecem cores sólidas e mudam de uma para outra nos limites abaixo.".T()),
                    I("Limite de atenção".T(), atencao),
                    I("Limite crítico".T(), critico),
                    cfg.CorGradual ? L("Com a rampa de cor ativada, o anel só fica vermelho em 100%, então o limite crítico vale no modo Degrau seco.".T()) : null,
                    I("", NovoBotao("restaurar", "Restaurar padrões".T(), delegate { cfg.CorGradual = false; cfg.LimiteAtencao = 0.5; cfg.LimiteCritico = 0.7; Salvou(); }))),
                B("App".T(),
                    I("Ícone do app".T(), NovoSeg("bandeja", new[] { "Bandeja do sistema".T(), "Nenhum".T() }, cfg.IconeBandeja ? 0 : 1, i => cfg.IconeBandeja = i == 0 || cfg.Mostrar == Mostrar.Oculto)),
                    L(cfg.Mostrar == Mostrar.Oculto ? "Com a cápsula oculta, o ícone na bandeja fica, para você não perder o acesso.".T()
                        : cfg.IconeBandeja ? "O ícone na bandeja mostra um mini anel com o consumo atual.".T() : "Sem ícone na bandeja; a cápsula é o único acesso.".T())),
            };
        }

        List<Bloco> Geral()
        {
            var cfg = Config.Atual;
            var idioma = NovoSeg("idioma", Idioma.Nomes, Idioma.Indice, i => cfg.Idioma = Idioma.Codigos[i]);
            idioma.Icone = DesenharBandeira;
            return new List<Bloco>
            {
                B(null,
                    I("Idioma".T(), idioma),
                    I("Abrir o Pulso ao entrar no Windows".T(), NovaChave("iniciar", cfg.IniciarComWindows, v => { cfg.IniciarComWindows = v; Integracao.IniciarComWindows(v); })),
                    L("O Pulso abre sozinho toda vez que você entra neste computador e fica quieto em segundo plano. Desative e você precisará abri-lo manualmente.".T()),
                    I("Atalho para mostrar e ocultar a cápsula".T(), NovaChave("atalho", cfg.Atalho, v => { cfg.Atalho = v; app.AplicarAtalho(); })),
                    I("Teclas".T(), new Teclas { Partes = gravandoAtalho ? null : Atalhos.Partes(cfg.AtalhoMods, cfg.AtalhoTecla) }),
                    I("", new Botoes { Itens = BotoesAtalho(cfg) }),
                    L(gravandoAtalho ? "Pressione a combinação: Ctrl, Alt, Shift ou Win com outra tecla, ou uma tecla de F1 a F24. Esc cancela.".T()
                        : avisoAtalho != null ? avisoAtalho() : !cfg.Atalho ? "Desativado.".T() : app.AtalhoAtivo ? "Mostra e oculta a cápsula de qualquer lugar.".T()
                        : "{0} está em uso por outro programa — grave outra combinação.".T(cfg.AtalhoTexto))),
                B("Notificações".T(),
                    I("Avisos de renovação".T(), NovaChave("renovacao", cfg.AvisoRenovacao, v => cfg.AvisoRenovacao = v)),
                    L("Mostra um cartão ao lado da cápsula quando o Claude ou o Codex renova uma janela depois de pelo menos 10% de uso. O cartão aparece mesmo com a cápsula oculta.".T()),
                    I("Aviso de limite".T(), NovaChave("limite", cfg.AvisoLimite, v => cfg.AvisoLimite = v)),
                    L("Avisa quando uma janela chega a 80%, a 95% e ao limite, uma vez por janela — sempre pelo valor exato, nunca pela estimativa.".T()),
                    I("Aviso de ritmo".T(), NovaChave("ritmo", cfg.AvisoRitmo, v => cfg.AvisoRitmo = v)),
                    L("Avisa quando, no ritmo dos últimos 30 minutos, a sessão de 5 horas vai acabar antes de renovar — com tempo de desacelerar antes dos avisos de 80% e 95%. Uma vez por sessão.".T()),
                    I("Sessão terminou".T(), NovaChave("fim", cfg.AvisoSessaoFim, v => cfg.AvisoSessaoFim = v)),
                    L("Avisa quando o Claude ou o Codex termina uma resposta que levou pelo menos 10 segundos — a não ser que você já esteja na janela dela. Clicar no cartão leva até a janela.".T()),
                    I("Sessão esperando você".T(), NovaChave("espera", cfg.AvisoSessaoEspera, v => cfg.AvisoSessaoEspera = v)),
                    L("Avisa quando uma sessão para e pede a sua resposta (permissão, pergunta).".T()),
                    I("Som dos avisos".T(), NovaChave("som", cfg.SomAvisos, v => cfg.SomAvisos = v)),
                    I("Pré-visualizar o cartão".T(), NovoBotao("previa", "Pré-visualizar".T(), delegate { app.Cartao.Mostrar(Avisos.Exemplo()); }))),
                B("Atualizações".T(), LinhasAtualizacao().ToArray()),
                B(null,
                    I("Pasta de dados".T(), NovoBotao("pasta", "Abrir pasta".T(), delegate { try { Process.Start(new ProcessStartInfo(Caminhos.Dados) { UseShellExecute = true }); } catch { } })),
                    L("Tudo o que o Pulso guarda fica numa pasta: configurações, estado, índice de custo e log. Nenhuma credencial.".T()),
                    Instalacao.Instalado ? I("Desinstalar o Pulso".T(), NovoBotao("desinstalar", "Desinstalar…".T(), Instalacao.AbrirDesinstalador)) : null,
                    Instalacao.Instalado ? L("Também dá para desinstalar em Configurações › Aplicativos do Windows. Você escolhe se as suas configurações ficam.".T()) : null),
            };
        }

        List<Linha> LinhasAtualizacao()
        {
            var l = new List<Linha>();
            l.Add(I("Versão".T(), new Valor { Texto = Instalacao.Versao }));
            var st = Atualizacao.Estado;
            var botoes = new List<Botao>();
            if (st == Atualizacao.Situacao.Disponivel) botoes.Add(NovoBotao("atualizar", "Instalar atualização".T(), Atualizacao.Aplicar));
            bool ocupado = st == Atualizacao.Situacao.Procurando || st == Atualizacao.Situacao.Baixando;
            botoes.Add(new Botao
            {
                Id = "procurar", Texto = st == Atualizacao.Situacao.Procurando ? "Procurando…".T() : st == Atualizacao.Situacao.Baixando ? "Instalando…".T() : "Procurar atualização".T(),
                Desativado = ocupado, Clique = delegate { Atualizacao.Procurar(true); },
            });
            l.Add(I("", new Botoes { Itens = botoes }));
            string texto;
            switch (st)
            {
                case Atualizacao.Situacao.Disponivel:
                    texto = (Atualizacao.PeloDownload ? "Versão nova no GitHub ({0}). Instalar atualização baixa e reabre o Pulso em alguns segundos."
                        : "Versão nova no GitHub ({0}). Instalar atualização baixa, compila e reabre o Pulso em alguns segundos.").T(Atualizacao.Detalhe.T());
                    break;
                case Atualizacao.Situacao.Baixando: texto = "Baixando a versão nova…".T(); break;
                case Atualizacao.Situacao.EmDia: texto = "Você está com a versão mais recente.".T(); break;
                case Atualizacao.Situacao.Erro: texto = Atualizacao.Detalhe.T(); break;
                case Atualizacao.Situacao.Procurando: texto = "Consultando o GitHub…".T(); break;
                default: texto = "O Pulso procura sozinho 2 minutos depois de abrir e a cada 12 horas, sem interromper você.".T(); break;
            }
            l.Add(L(texto));
            return l;
        }

        // ---------- relatórios ----------
        static Color CorProvedor(string id, bool escuro)
        {
            if (id == "claude") return Paleta.Hex(escuro ? "#e08a6d" : "#c96442");
            return Paleta.Hex(escuro ? "#7aa2ff" : "#3d6fe0");
        }

        List<Bloco> Relatorios()
        {
            DateTime de, ate;
            Relatorio.Intervalo(relPeriodo, Relatorio.Referencia(relPeriodo, relDesloc), out de, out ate);
            var todos = Relatorio.Registros(de, ate, false);
            var regs = relProvedor == null ? todos : todos.Where(r => r.Provedor == relProvedor).ToList();

            var periodo = NovoSeg("rel-periodo", new[] { "Dia".T(), "Semana".T(), "Mês".T() }, (int)relPeriodo, i => { relPeriodo = (Relatorio.Periodo)i; relDesloc = 0; });
            periodo.SoTela = true;
            var provedor = NovoSeg("rel-provedor", new[] { "Todos".T() }.Concat(Consumo.Provedores.Select(Relatorio.NomeProvedor)).ToArray(),
                relProvedor == null ? 0 : Array.IndexOf(Consumo.Provedores, relProvedor) + 1, i => relProvedor = i == 0 ? null : Consumo.Provedores[i - 1]);
            provedor.SoTela = true;
            var navegar = new Botoes
            {
                Itens = new List<Botao>
                {
                    new Botao { Id = "rel-anterior", Texto = "‹", Clique = delegate { relDesloc--; Invalidate(); } },
                    new Botao { Id = "rel-proximo", Texto = "›", Desativado = relDesloc >= 0, Clique = delegate { relDesloc++; Invalidate(); } },
                },
            };
            var blocos = new List<Bloco>
            {
                B(null, I("Período".T(), periodo), I(Relatorio.Titulo(relPeriodo, de, ate, relDesloc), navegar), I("Provedor".T(), provedor)),
            };
            if (regs.Count == 0)
            {
                blocos.Add(B(null, L("Nada registrado neste período.".T()), L(LegendaRelatorios())));
                return blocos;
            }

            var total = Relatorio.Soma(regs);
            var cult = Idioma.Cultura;
            var numeros = new Numeros();
            numeros.Itens.Add(new KeyValuePair<string, string>("Tokens".T(), Relatorio.Compacto(total.Total)));
            numeros.Itens.Add(new KeyValuePair<string, string>("Respostas".T(), total.Respostas.ToString("N0", cult)));
            if (total.Custo > 0) numeros.Itens.Add(new KeyValuePair<string, string>("Custo equivalente".T(), Relatorio.Dolares(total.Custo)));
            blocos.Add(B("Resumo".T(), numeros,
                L("Entrada {0} · cache lido {1} · cache gravado {2} · saída {3}, com {4} de raciocínio".T(Relatorio.Compacto(total.Tokens[0]),
                    Relatorio.Compacto(total.Tokens[1]), Relatorio.Compacto(total.Tokens[2]), Relatorio.Compacto(total.Tokens[3]), Relatorio.Compacto(total.Tokens[4])))));

            if (relPeriodo != Relatorio.Periodo.Dia)
            {
                var grafico = new Grafico { Mes = relPeriodo == Relatorio.Periodo.Mes, Provedores = Consumo.Provedores.Where(p => regs.Any(r => r.Provedor == p)).ToList() };
                for (var d = de; d <= ate; d = d.AddDays(1))
                {
                    string chave = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    grafico.Dias.Add(d);
                    grafico.Valores.Add(grafico.Provedores.Select(p => regs.Where(r => r.Dia == chave && r.Provedor == p).Sum(r => r.L.Total)).ToArray());
                }
                grafico.Abrir = d => { relPeriodo = Relatorio.Periodo.Dia; relDesloc = (int)(d - DateTime.Today).TotalDays; rolagem = 0; Invalidate(); };
                blocos.Add(B("Por dia".T(), grafico, L("Clique num dia para ver só ele.".T())));
            }
            if (relProvedor == null && regs.Select(r => r.Provedor).Distinct().Count() > 1)
                blocos.Add(B("Por provedor".T(), Fatias(regs, r => r.Provedor, Relatorio.NomeProvedor, total.Total, true).ToArray()));
            blocos.Add(B("Por modelo".T(), Fatias(regs, r => r.Modelo, Relatorio.NomeModelo, total.Total, false).ToArray()));
            blocos.Add(B("Por projeto".T(), Fatias(regs, r => r.Projeto, Relatorio.NomeProjeto, total.Total, false).ToArray()));

            string nome = Relatorio.NomeArquivo(relPeriodo, de) + (relProvedor != null ? "-" + relProvedor : "");
            string titulo = Relatorio.Titulo(relPeriodo, de, ate, relDesloc) + (relProvedor != null ? " · " + Relatorio.NomeProvedor(relProvedor) : "");
            bool porDia = relPeriodo != Relatorio.Periodo.Dia;
            blocos.Add(B("Exportar".T(),
                I("", new Botoes
                {
                    Itens = new List<Botao>
                    {
                        NovoBotao("rel-csv", "Salvar CSV…".T(), delegate { SalvarCsv(nome, regs); }),
                        NovoBotao("rel-html", "Abrir no navegador".T(), delegate { AbrirHtml(nome, regs, titulo, porDia); }),
                    },
                }),
                L(LegendaRelatorios())));
            return blocos;
        }

        static string LegendaRelatorios()
        {
            return "Conta o uso deste computador, lido dos registros do Claude Code e do Codex e guardado por 13 meses. A porcentagem é a fatia dos tokens do período (entrada, cache e saída); US$ é o preço de API equivalente, não uma cobrança.".T();
        }

        // Uma linha por grupo, da maior fatia para a menor; depois de 8, o resto vira "Outros"
        List<Linha> Fatias(List<Relatorio.Registro> regs, Func<Relatorio.Registro, string> chave, Func<string, string> nome, long total, bool porProvedor)
        {
            var grupos = regs.GroupBy(chave, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Chave = g.Key, Soma = Relatorio.Soma(g), Itens = g.ToList() })
                .OrderByDescending(g => g.Soma.Total).ToList();
            var linhas = new List<Linha>();
            bool escuro = Escuro;
            var cult = Idioma.Cultura;
            Func<Consumo.Linha, string> detalhe = s => (s.Respostas == 1 ? "1 resposta · {0} tokens".T(Relatorio.Compacto(s.Total)) : "{0} respostas · {1} tokens".T(s.Respostas.ToString("N0", cult), Relatorio.Compacto(s.Total)))
                + (s.Custo > 0 ? " · ≈ " + Relatorio.Dolares(s.Custo) : "");
            // A barra de cada linha vem dividida pela cor de cada provedor (um projeto pode somar Claude Code e Codex)
            Func<IEnumerable<Relatorio.Registro>, List<KeyValuePair<Color, double>>> segmentos = rs => Consumo.Provedores
                .Select(p => new KeyValuePair<Color, double>(CorProvedor(p, escuro), total > 0 ? (double)rs.Where(r => r.Provedor == p).Sum(r => r.L.Total) / total : 0))
                .Where(s => s.Value > 0).ToList();
            int mostrar = grupos.Count > 9 ? 8 : grupos.Count;
            foreach (var g in grupos.Take(mostrar))
                linhas.Add(new Fatia
                {
                    Nome = nome(g.Chave), Glifo = porProvedor ? g.Chave : null, Detalhe = detalhe(g.Soma),
                    Parte = total > 0 ? (double)g.Soma.Total / total : 0, Segmentos = segmentos(g.Itens),
                });
            if (grupos.Count > mostrar)
            {
                var resto = grupos.Skip(mostrar).SelectMany(g => g.Itens).ToList();
                var soma = Relatorio.Soma(resto);
                linhas.Add(new Fatia { Nome = "Outros".T() + " (" + (grupos.Count - mostrar) + ")", Detalhe = detalhe(soma), Parte = total > 0 ? (double)soma.Total / total : 0, Segmentos = segmentos(resto) });
            }
            return linhas;
        }

        void SalvarCsv(string nome, List<Relatorio.Registro> regs)
        {
            using (var d = new SaveFileDialog { FileName = nome + ".csv", Filter = "CSV (*.csv)|*.csv", DefaultExt = "csv", OverwritePrompt = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { System.IO.File.WriteAllText(d.FileName, Relatorio.Csv(regs), new System.Text.UTF8Encoding(true)); }
                catch (Exception e) { Log.Erro("salvar CSV de consumo", e); MessageBox.Show(this, e.Message, "Pulso", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // Grava em %APPDATA%\Pulso\relatorios e abre no navegador padrão
        void AbrirHtml(string nome, List<Relatorio.Registro> regs, string titulo, bool porDia)
        {
            try
            {
                string pasta = System.IO.Path.Combine(Caminhos.Dados, "relatorios");
                System.IO.Directory.CreateDirectory(pasta);
                string arq = System.IO.Path.Combine(pasta, nome + ".html");
                System.IO.File.WriteAllText(arq, Relatorio.Html(regs, titulo, porDia), new System.Text.UTF8Encoding(false));
                Process.Start(new ProcessStartInfo(arq) { UseShellExecute = true });
            }
            catch (Exception e) { Log.Erro("abrir relatório de consumo", e); }
        }

        // Números grandes lado a lado: rótulo pequeno em cima, valor embaixo
        class Numeros : Linha
        {
            public List<KeyValuePair<string, string>> Itens = new List<KeyValuePair<string, string>>();
            public override float Altura(Configuracoes j, float w) { return j.E(12) + j.peq.Linha + j.E(2) + j.marca.Linha + j.E(10); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                float col = (w - j.E(24)) / Math.Max(1, Itens.Count);
                for (int i = 0; i < Itens.Count; i++)
                {
                    float xi = x + j.E(12) + col * i;
                    Tx.Linha(g, j.peq, Itens[i].Key, Sobre(j.c.Text2, j.c.Group), j.c.Group, xi, y + j.E(12), Tx.Alinhar.Esquerda, col - j.E(8));
                    Tx.Linha(g, j.marca, Itens[i].Value, j.c.Text, j.c.Group, xi, y + j.E(12) + j.peq.Linha + j.E(2), Tx.Alinhar.Esquerda, col - j.E(8));
                }
            }
        }

        // Nome e porcentagem, a barra da fatia e o detalhe embaixo (como o Armazenamento do Windows)
        class Fatia : Linha
        {
            public string Nome, Detalhe, Glifo; public double Parte;
            public List<KeyValuePair<Color, double>> Segmentos;   // cor e fração do total de cada pedaço da barra
            public override float Altura(Configuracoes j, float w) { return j.E(10) + j.corpo.Linha + j.E(6) + j.E(4) + j.E(5) + j.peq.Linha + j.E(10); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                float xi = x + j.E(12), yy = y + j.E(10);
                if (Glifo != null)
                {
                    using (var ic = Glifos.Caminho(Glifo, xi + j.E(8), yy + j.corpo.Linha / 2, j.E(16)))
                    using (var b = new SolidBrush(j.c.Text)) g.FillPath(b, ic);
                    xi += j.E(26);
                }
                string pct = (Parte * 100).ToString(Parte < 0.001 && Parte > 0 ? "0.##" : "0.0", Idioma.Cultura) + "%";
                float lp = Tx.Largura(j.corpo, pct);
                Tx.Linha(g, j.corpo, pct, j.c.Text, j.c.Group, x + w - j.E(12), yy, Tx.Alinhar.Direita, 0);
                Tx.Linha(g, j.corpo, Nome, j.c.Text, j.c.Group, xi, yy, Tx.Alinhar.Esquerda, x + w - j.E(12) - lp - j.E(12) - xi);
                yy += j.corpo.Linha + j.E(6);
                var trilho = new RectangleF(xi, yy, x + w - j.E(12) - xi, j.E(4));
                j.Preencher(g, trilho, j.E(2), Sobre(j.c.BtnHover, j.c.Group));
                if (Parte > 0)
                {
                    var cheio = new RectangleF(trilho.X, trilho.Y, Math.Max(j.E(4), trilho.Width * (float)Math.Min(1, Parte)), trilho.Height);
                    var est = g.Save();
                    using (var forma = Arred(cheio, j.E(2))) g.SetClip(forma, CombineMode.Intersect);
                    float xs = cheio.X;
                    for (int i = 0; i < Segmentos.Count; i++)
                    {
                        // O último pedaço vai até o fim da barra (sem fresta de arredondamento)
                        float ws = i == Segmentos.Count - 1 ? cheio.Right - xs : cheio.Width * (float)(Segmentos[i].Value / Parte);
                        using (var b = new SolidBrush(Segmentos[i].Key)) g.FillRectangle(b, xs, cheio.Y, ws + 0.5f, cheio.Height);
                        xs += ws;
                    }
                    g.Restore(est);
                }
                yy += j.E(4) + j.E(5);
                Tx.Linha(g, j.peq, Detalhe, Sobre(j.c.Text2, j.c.Group), j.c.Group, xi, yy, Tx.Alinhar.Esquerda, x + w - j.E(12) - xi);
            }
        }

        // Colunas por dia, empilhadas por provedor; o cursor sobre uma coluna mostra os números e o clique abre o dia
        class Grafico : Linha
        {
            public bool Mes; public List<string> Provedores;
            public List<DateTime> Dias = new List<DateTime>();
            public List<long[]> Valores = new List<long[]>();
            public Action<DateTime> Abrir;
            float AlturaBarras(Configuracoes j) { return j.E(110); }
            public override float Altura(Configuracoes j, float w) { return j.E(12) + j.peq.Linha + j.E(10) + AlturaBarras(j) + j.E(6) + j.peq.Linha + j.E(10); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                var cult = Idioma.Cultura;
                bool escuro = j.Escuro;
                var fraco = Sobre(j.c.Text2, j.c.Group);
                float xi = x + j.E(12), wi = w - j.E(24);
                float topo = y + j.E(12) + j.peq.Linha + j.E(10), hb = AlturaBarras(j), base_ = topo + hb;
                long max = Math.Max(1, Valores.Max(v => v.Sum()));
                float vaga = wi / Dias.Count, larg = Math.Min(vaga * (Mes ? 0.66f : 0.5f), j.E(36));

                int sob = -1;
                for (int i = 0; i < Dias.Count; i++) if (j.Sobre("rel-dia:" + i)) sob = i;

                // Linha de base e, para cada dia com uso, a coluna empilhada
                using (var pen = new Pen(Sobre(j.c.Sep, j.c.Group), Math.Max(1, j.E(1)))) g.DrawLine(pen, xi, base_, xi + wi, base_);
                for (int i = 0; i < Dias.Count; i++)
                {
                    var vagaR = new RectangleF(xi + vaga * i, topo, vaga, hb);
                    if (i == sob) j.Preencher(g, RectangleF.Inflate(vagaR, -j.E(1), 0), j.E(5), Sobre(j.c.Hover, j.c.Group));
                    float cx = vagaR.X + vaga / 2, yb = base_;
                    long soma = Valores[i].Sum();
                    if (soma > 0)
                    {
                        float alt = Math.Max(j.E(2), hb * 0.94f * soma / max);
                        var est = g.Save();
                        using (var forma = Arred(new RectangleF(cx - larg / 2, base_ - alt, larg, alt + j.E(3)), Math.Min(j.E(3), larg / 2)))
                        {
                            g.SetClip(new RectangleF(cx - larg / 2, base_ - alt, larg, alt), CombineMode.Intersect);
                            g.SetClip(forma, CombineMode.Intersect);
                            for (int p = 0; p < Provedores.Count; p++)
                            {
                                if (Valores[i][p] <= 0) continue;
                                float hp = alt * Valores[i][p] / soma;
                                using (var b = new SolidBrush(CorProvedor(Provedores[p], escuro))) g.FillRectangle(b, cx - larg / 2, yb - hp, larg, hp + 0.5f);
                                yb -= hp;
                            }
                        }
                        g.Restore(est);
                    }
                    var dia = Dias[i];
                    if (dia <= DateTime.Today) j.Registrar(vagaR, "rel-dia:" + i, delegate { Abrir(dia); }, true);
                    // Eixo: o dia da semana na semana; no mês, 1, 5, 10, 15... e o último
                    bool rotular = !Mes || dia.Day == 1 || dia.Day % 5 == 0 || i == Dias.Count - 1 && dia.Day % 5 > 1;
                    if (rotular)
                        Tx.Linha(g, j.peq, Mes ? dia.Day.ToString(cult) : dia.ToString("ddd", cult), dia == DateTime.Today ? j.c.Text : fraco, j.c.Group, cx, base_ + j.E(6), Tx.Alinhar.Centro, 0);
                }

                // Legenda: o dia sob o cursor ou o pico do período; à direita, a cor de cada provedor
                string legenda;
                if (sob >= 0)
                {
                    var partes = new List<string>();
                    for (int p = 0; p < Provedores.Count; p++) if (Valores[sob][p] > 0) partes.Add(Relatorio.NomeProvedor(Provedores[p]) + " " + Relatorio.Compacto(Valores[sob][p]));
                    if (partes.Count < 2) partes.Clear(); // um provedor só no dia: o total já diz tudo
                    legenda = Util.Maiuscula(Dias[sob].ToString("ddd " + cult.DateTimeFormat.MonthDayPattern, cult)) + ": " + "{0} tokens".T(Relatorio.Compacto(Valores[sob].Sum())) + (partes.Count > 0 ? " (" + string.Join(" · ", partes) + ")" : "");
                }
                else
                {
                    int pico = Valores.FindIndex(v => v.Sum() == max);
                    legenda = "Pico: {0}, {1} tokens".T(Dias[pico].ToString(cult.DateTimeFormat.MonthDayPattern, cult), Relatorio.Compacto(max));
                }
                float xl = xi + wi;
                if (Provedores.Count > 1)
                    for (int p = Provedores.Count - 1; p >= 0; p--)
                    {
                        string n = Relatorio.NomeProvedor(Provedores[p]);
                        float ln = Tx.Largura(j.peq, n);
                        Tx.Linha(g, j.peq, n, fraco, j.c.Group, xl, y + j.E(12), Tx.Alinhar.Direita, 0);
                        xl -= ln + j.E(6) + j.E(8);
                        j.Preencher(g, new RectangleF(xl, y + j.E(12) + (j.peq.Linha - j.E(8)) / 2, j.E(8), j.E(8)), j.E(2), CorProvedor(Provedores[p], escuro));
                        xl -= j.E(14);
                    }
                Tx.Linha(g, j.peq, legenda, sob >= 0 ? j.c.Text : fraco, j.c.Group, xi, y + j.E(12), Tx.Alinhar.Esquerda, xl - xi - j.E(8));
            }
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
                for (int i = 0; i < NomesAbas.Length; i++)
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
