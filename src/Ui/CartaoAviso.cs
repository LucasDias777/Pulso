using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulso
{
    class Aviso
    {
        public string Provedor, Titulo, Subtitulo, Status, Proxima;
        public Color? CorStatus;     // nulo = verde (cota disponível)
        public Som Som;
        public Action Clique;        // clicar no cartão (ex.: ir para a janela da sessão)
    }

    // Cartão de aviso ao lado do notch: 246×128,
    // borda de 1 px, cantos de 16, cauda de 34×32 apontando para o anel do provedor, entra em 0,18 s
    // subindo 5 px e fica 6 s (o mouse em cima segura). Avisos chegam em fila, um de cada vez.
    class CartaoAviso : Form
    {
        readonly NotchJanela notch;
        readonly Superficie sup = new Superficie();
        readonly Queue<Aviso> fila = new Queue<Aviso>();
        readonly Timer relogio = new Timer { Interval = 16 };
        Aviso atual;
        DateTime inicio;
        bool sobre, sobreFechar;
        Rectangle janela;
        RectangleF cartaoLocal, fecharLocal;
        static readonly TimeSpan Duracao = TimeSpan.FromSeconds(6);

        public CartaoAviso(NotchJanela notch)
        {
            this.notch = notch;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Pulso — aviso";
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);
            relogio.Tick += delegate { Tique(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = Nativo.WS_POPUP;
                cp.ExStyle |= Nativo.WS_EX_LAYERED | Nativo.WS_EX_TOOLWINDOW | Nativo.WS_EX_TOPMOST | Nativo.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Nativo.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Nativo.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        public void Mostrar(Aviso a)
        {
            if (notch.TelaCheia) return; // jogo, vídeo, apresentação: não interrompe
            fila.Enqueue(a);
            if (atual == null) Proximo();
        }

        void Proximo()
        {
            if (fila.Count == 0)
            {
                atual = null;
                relogio.Stop();
                if (Visible) Hide();
                return;
            }
            atual = fila.Dequeue();
            inicio = DateTime.UtcNow;
            sobre = sobreFechar = false;
            Sons.Tocar(atual.Som);
            relogio.Interval = 16;
            relogio.Start();
            Renderizar();
        }

        void Tique()
        {
            if (atual == null) return;
            var passou = DateTime.UtcNow - inicio;
            if (sobre) inicio = DateTime.UtcNow - TimeSpan.FromMilliseconds(Math.Min(passou.TotalMilliseconds, 1000));
            else if (passou > Duracao) { Proximo(); return; }
            if (passou.TotalMilliseconds <= 200) Renderizar();
            else if (relogio.Interval != 200) { relogio.Interval = 200; Renderizar(); }
        }

        // ---------- desenho ----------
        void Renderizar()
        {
            if (atual == null || IsDisposed) return;
            if (!IsHandleCreated) CreateHandle();
            float s = notch.EscalaCartao; // como o cartão de consumo: não encolhe com o notch Pequeno
            var borda = notch.BordaAtual;
            bool preso = notch.Visible;
            float W = 280 * s, H = 150 * s;
            var corpo = notch.CorpoTela;
            var area = notch.AreaTrabalho;
            PointF alvo = notch.CentroDoAnel(atual.Provedor) ?? new PointF(corpo.X + corpo.Width / 2, corpo.Y + corpo.Height / 2);
            float x, y;
            if (!preso)
            {
                // Notch oculto: cartão solto, encostado na borda de sempre
                switch (borda)
                {
                    case Borda.Esquerda: x = area.Left + 8 * s; y = area.Top + (area.Height - H) / 2; break;
                    case Borda.Cima: x = area.Left + (area.Width - W) / 2; y = area.Top + 8 * s; break;
                    case Borda.Baixo: x = area.Left + (area.Width - W) / 2; y = area.Bottom - H - 8 * s; break;
                    default: x = area.Right - W - 8 * s; y = area.Top + (area.Height - H) / 2; break;
                }
            }
            else
            {
                switch (borda)
                {
                    case Borda.Esquerda: x = corpo.Right; y = alvo.Y - H / 2; break;
                    case Borda.Cima: x = alvo.X - W / 2; y = corpo.Bottom; break;
                    case Borda.Baixo: x = alvo.X - W / 2; y = corpo.Top - H; break;
                    default: x = corpo.Left - W; y = alvo.Y - H / 2; break;
                }
            }
            x = Math.Max(area.Left, Math.Min(area.Right - W, x));
            y = Math.Max(area.Top, Math.Min(area.Bottom - H, y));

            double t = Math.Min(1, (DateTime.UtcNow - inicio).TotalMilliseconds / 180);
            double e = 1 - Math.Pow(1 - t, 3);
            float sobe = (float)(5 * s * (1 - e));
            janela = new Rectangle((int)Math.Round(x), (int)Math.Round(y + sobe), (int)Math.Ceiling(W), (int)Math.Ceiling(H));

            var pal = Paleta.Atual;
            bool escuro = pal == Paleta.Escura;
            Color card = escuro ? Paleta.Hex("#0a0a0a") : Color.White, linha = escuro ? Paleta.Hex("#242424") : Paleta.Hex("#e4e4e8");
            Color tinta = escuro ? Color.White : Paleta.Hex("#1d1d1f"), suave = escuro ? Paleta.Hex("#c8c8c8") : Paleta.Hex("#3c3c43");
            Color verde = escuro ? Paleta.Hex("#00ff88") : Paleta.Hex("#007d42"), botaoHover = escuro ? Paleta.Hex("#383838") : Paleta.Hex("#e6e6e9");

            using (var g = sup.Abrir(janela.Width, janela.Height))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Posição do cartão e da cauda dentro da janela (unidades lógicas × s)
                float cx, cy; string cauda = null; float tx = 0, ty = 0;
                if (!preso) { cx = 17; cy = 11; }
                else switch (borda)
                {
                    case Borda.Esquerda: cx = 34; cy = 11; cauda = "M34 0C34 8 15 12 0 16C15 20 34 24 34 32Z"; tx = 1; ty = 59; break;
                    case Borda.Cima: cx = 17; cy = 22; cauda = "M0 23C8 23 12 9 17 0C22 9 26 23 34 23Z"; tx = 123; ty = 0; break;
                    case Borda.Baixo: cx = 17; cy = 0; cauda = "M0 0C8 0 12 14 17 23C22 14 26 0 34 0Z"; tx = 123; ty = 127; break;
                    default: cx = 0; cy = 11; cauda = "M0 0C0 8 19 12 34 16C19 20 0 24 0 32Z"; tx = 245; ty = 59; break;
                }
                cartaoLocal = new RectangleF(cx * s, cy * s, 246 * s, 128 * s);
                using (var b = new SolidBrush(card))
                {
                    if (cauda != null)
                        using (var p = Svg.Interpretar(cauda))
                        using (var m = new Matrix())
                        {
                            m.Translate(tx * s, ty * s); m.Scale(s, s);
                            p.Transform(m);
                            g.FillPath(b, p);
                        }
                    using (var p = Arred(cartaoLocal, 16 * s)) g.FillPath(b, p);
                }
                using (var p = Arred(RectangleF.Inflate(cartaoLocal, -0.5f * s, -0.5f * s), 16 * s))
                using (var pen = new Pen(linha, Math.Max(1, s))) g.DrawPath(pen, p);

                float px = cartaoLocal.X + 15 * s, py = cartaoLocal.Y + 13 * s;
                var titulo = Tx.Novo("Segoe UI", 700, 14 * s, 14 * 1.35f * s);
                var sub = Tx.Novo("Segoe UI", 400, 11 * s, 11 * 1.35f * s);
                var st = Tx.Novo("Segoe UI", 400, 12 * s, 12 * 1.35f * s);
                float hCab = titulo.Linha + (string.IsNullOrEmpty(atual.Subtitulo) ? 0 : sub.Linha);
                using (var ic = Glifos.Caminho(atual.Provedor, px + 10.5f * s, py + hCab / 2, 21 * s))
                using (var b = new SolidBrush(tinta)) g.FillPath(b, ic);
                float hx = px + 30 * s, larg = cartaoLocal.Right - 15 * s - 30 * s + 6 * s - hx - 4 * s;
                Tx.Linha(g, titulo, atual.Titulo, tinta, card, hx, py, Tx.Alinhar.Esquerda, larg);
                if (!string.IsNullOrEmpty(atual.Subtitulo)) Tx.Linha(g, sub, atual.Subtitulo, suave, card, hx, py + titulo.Linha, Tx.Alinhar.Esquerda, larg);

                // Botão de dispensar: 30×30, raio 7, X de 13 px
                fecharLocal = new RectangleF(cartaoLocal.Right - 15 * s - 30 * s + 6 * s, py - 4 * s, 30 * s, 30 * s);
                if (sobreFechar) using (var p = Arred(fecharLocal, 7 * s)) using (var b = new SolidBrush(botaoHover)) g.FillPath(b, p);
                float fx = fecharLocal.X + fecharLocal.Width / 2, fy = fecharLocal.Y + fecharLocal.Height / 2, d = 5 * s;
                using (var pen = new Pen(sobreFechar ? tinta : suave, 1.8f * s * 13 / 16) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, fx - d, fy - d, fx + d, fy + d);
                    g.DrawLine(pen, fx + d, fy - d, fx - d, fy + d);
                }

                float sy = py + hCab + 13 * s;
                Color cs = atual.CorStatus ?? verde;
                using (var b = new SolidBrush(cs)) g.FillEllipse(b, px, sy + (st.Linha - 8 * s) / 2, 8 * s, 8 * s);
                Tx.Linha(g, st, atual.Status, cs, card, px + 16 * s, sy, Tx.Alinhar.Esquerda, cartaoLocal.Right - 15 * s - px - 16 * s);
                if (!string.IsNullOrEmpty(atual.Proxima))
                    Tx.Linha(g, sub, atual.Proxima, suave, card, px + 16 * s, sy + st.Linha + 8 * s, Tx.Alinhar.Esquerda, cartaoLocal.Right - 15 * s - px - 16 * s);
            }
            sup.Mostrar(Handle, janela.X, janela.Y, (byte)Math.Round(255 * e));
            if (!Visible) { Show(); Nativo.SetWindowPos(Handle, Nativo.HWND_TOPMOST, 0, 0, 0, 0, Nativo.SWP_NOMOVE | Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE); }
        }

        static GraphicsPath Arred(RectangleF r, float raio)
        {
            float d = raio * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- mouse ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool noCartao = cartaoLocal.Contains(e.Location), noFechar = fecharLocal.Contains(e.Location);
            Cursor = noCartao && (noFechar || (atual != null && atual.Clique != null)) ? Cursors.Hand : Cursors.Default;
            if (noCartao != sobre || noFechar != sobreFechar) { sobre = noCartao; sobreFechar = noFechar; Renderizar(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sobre || sobreFechar) { sobre = sobreFechar = false; Renderizar(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (atual == null || e.Button != MouseButtons.Left || !cartaoLocal.Contains(e.Location)) return;
            var a = atual;
            if (!fecharLocal.Contains(e.Location) && a.Clique != null)
                try { a.Clique(); } catch (Exception ex) { Log.Erro("clique no aviso", ex); }
            Proximo();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { relogio.Dispose(); sup.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
