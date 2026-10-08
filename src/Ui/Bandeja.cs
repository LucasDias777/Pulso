using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Pulso
{
    // Ícone da bandeja: um mini anel com o consumo da janela principal mais cheia (dá para ver o número
    // mesmo com o notch oculto). O menu é montado na hora de abrir, sempre com valores frescos.
    class Bandeja : IDisposable
    {
        readonly NotifyIcon icone = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly App app;
        IntPtr hIcone = IntPtr.Zero;
        string ultimaAssinatura;

        public Bandeja(App app)
        {
            this.app = app;
            Estilizar(menu);
            menu.Opening += delegate { Montar(menu, null); };
            icone.ContextMenuStrip = menu;
            icone.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                // Clique esquerdo também abre o menu
                var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(icone, null);
            };
            Estado.Mudou += Atualizar;
            Atualizar();
        }

        public bool Visivel
        {
            get { return icone.Visible; }
            set { icone.Visible = value; }
        }


        void Atualizar()
        {
            var provs = Config.Atual.Provedores.Select(Estado.Copia).ToList();
            string dica = "Pulso";
            double max = -1;
            foreach (var p in provs)
            {
                var j = p.Principal;
                if (j == null) continue;
                dica += " · " + p.Nome + " " + Texto.Pct(j.Atual, false);
                if (j.Atual > max) max = j.Atual;
            }
            if (dica.Length > 63) dica = dica.Substring(0, 63);
            icone.Text = dica;

            string assinatura = (max < 0 ? "-" : ((int)Math.Round(max * 100)).ToString()) + "|" + Config.Atual.Tema + Config.Atual.CorGradual + Config.Atual.LimiteAtencao + Config.Atual.LimiteCritico;
            if (assinatura == ultimaAssinatura) return;
            ultimaAssinatura = assinatura;
            int tam = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(tam, tam))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var pal = Paleta.Escura;
                    float m = tam * 0.06f, d = tam - 2 * m;
                    using (var b = new SolidBrush(Color.Black)) g.FillEllipse(b, m, m, d, d);
                    float esp = tam * 0.16f, r0 = m + esp / 2 + tam * 0.04f, dr = tam - 2 * r0;
                    using (var pen = new Pen(pal.Trilho, esp)) g.DrawEllipse(pen, r0, r0, dr, dr);
                    if (max > 0)
                        using (var pen = new Pen(pal.Tom(max), esp) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(pen, r0, r0, dr, dr, -90, (float)Math.Max(8, 360 * Math.Min(1, max)));
                    float c = tam * 0.14f;
                    using (var b = new SolidBrush(pal.Tinta2)) g.FillEllipse(b, tam / 2f - c, tam / 2f - c, 2 * c, 2 * c);
                }
                IntPtr novo = bmp.GetHicon();
                icone.Icon = Icon.FromHandle(novo);
                if (hIcone != IntPtr.Zero) Nativo.DestroyIcon(hIcone);
                hIcone = novo;
            }
        }

        // Menu da bandeja (provedorSobMouse == null) ou do clique direito no notch
        public void Montar(ContextMenuStrip m, string provedorSobMouse)
        {
            m.Items.Clear();
            var cfg = Config.Atual;
            if (provedorSobMouse == null)
            {
                foreach (var id in cfg.Provedores)
                {
                    var p = Estado.Copia(id);
                    var j = p.Principal;
                    string cab = j != null ? p.Nome + " — " + Texto.Pct(j.Atual, j.Estimado.HasValue && j.Estimado > j.Usado + 0.004) : "{0} — sem leitura".T(p.Nome);
                    if (p.Confirmado.HasValue && (DateTime.UtcNow - p.Confirmado.Value).TotalMinutes > 15) cab += " · " + Tempo.Ha(p.Confirmado.Value);
                    string idLocal = id;
                    if (negrito == null) negrito = new Font(m.Font, FontStyle.Bold);
                    var item = new ToolStripMenuItem(cab, null, delegate { app.Atualizar(idLocal); }) { Font = negrito, ToolTipText = "Clique para atualizar".T() };
                    m.Items.Add(item);
                    foreach (var w in p.Janelas)
                    {
                        string rn = Texto.RenovaEm(w.ResetaEm);
                        m.Items.Add(new ToolStripMenuItem("    " + w.Rotulo.T() + ": " + Texto.UsadoRestante(w.Atual, false) + (rn != null ? " · " + rn : "")) { Enabled = false });
                    }
                    if (p.Janelas.Count == 0) m.Items.Add(new ToolStripMenuItem("    " + (p.Erro.T() ?? "Aguardando a primeira leitura…".T())) { Enabled = false });
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem((app.Notch.OcultoPeloAtalho ? "Mostrar a cápsula" : "Ocultar a cápsula").T(), null, delegate { app.Notch.AlternarVisivel(); }) { ShortcutKeyDisplayString = cfg.Atalho ? cfg.AtalhoTexto : null });
                m.Items.Add(new ToolStripMenuItem("Atualizar tudo".T(), null, delegate { app.Atualizar(null); }));
            }
            else
            {
                string idLocal = provedorSobMouse;
                m.Items.Add(new ToolStripMenuItem("Atualizar agora".T(), null, delegate { app.Atualizar(null); }));
                var info = Catalogo.Info(provedorSobMouse) ?? Catalogo.Todos[0];
                string url = info.UrlUso;
                m.Items.Add(new ToolStripMenuItem("Abrir a página de uso — {0}".T(info.Nome), null, delegate { Abrir(url); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Manter aberto".T(), null, delegate
                {
                    cfg.Mostrar = cfg.Mostrar == Mostrar.Sempre ? Mostrar.AoPassar : Mostrar.Sempre;
                    cfg.Salvar();
                    app.Notch.Reposicionar();
                }) { Checked = cfg.Mostrar == Mostrar.Sempre });
                var mover = new ToolStripMenuItem("Mover para a borda".T());
                foreach (Borda b in Enum.GetValues(typeof(Borda)))
                {
                    var bl = b;
                    mover.DropDownItems.Add(new ToolStripMenuItem(Nome(b), null, delegate { cfg.Borda = bl; cfg.Salvar(); app.Notch.Reposicionar(); }) { Checked = cfg.Borda == b });
                }
                mover.DropDownItems.Add(new ToolStripSeparator());
                mover.DropDownItems.Add(new ToolStripMenuItem("Centralizar".T(), null, delegate { cfg.PosicaoNaBorda = 0.5; cfg.Salvar(); app.Notch.Reposicionar(); }));
                Estilizar((ToolStripDropDownMenu)mover.DropDown);
                m.Items.Add(mover);
                m.Items.Add(new ToolStripMenuItem("Ocultar a cápsula".T(), null, delegate { app.Notch.AlternarVisivel(); }) { ShortcutKeyDisplayString = cfg.Atalho ? cfg.AtalhoTexto : null });
            }
            if (Atualizacao.Estado == Atualizacao.Situacao.Disponivel)
                m.Items.Add(new ToolStripMenuItem("Atualizar o Pulso (versão nova)".T(), null, delegate { Atualizacao.Aplicar(); }));
            m.Items.Add(new ToolStripMenuItem("Configurações…".T(), null, delegate { app.AbrirConfig(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Sair do Pulso".T(), null, delegate { app.Sair(); }));
        }

        static Font negrito;

        public static void Estilizar(ToolStripDropDownMenu m)
        {
            m.Renderer = new MenuEscuro();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
        }

        public static string Nome(Borda b)
        {
            switch (b) { case Borda.Direita: return "Direita".T(); case Borda.Esquerda: return "Esquerda".T(); case Borda.Cima: return "Em cima".T(); default: return "Embaixo".T(); }
        }

        static void Abrir(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Log.Erro("abrir " + url, e); }
        }

        public void Dispose()
        {
            Estado.Mudou -= Atualizar;
            icone.Visible = false;
            icone.Dispose();
            menu.Dispose();
            if (hIcone != IntPtr.Zero) Nativo.DestroyIcon(hIcone);
        }
    }

    // Menu escuro, no tom do notch
    class MenuEscuro : ToolStripProfessionalRenderer
    {
        public MenuEscuro() : base(new Cores()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(232, 232, 234) : Color.FromArgb(150, 150, 150);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(200, 200, 200);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var pen = new Pen(Color.FromArgb(0, 255, 136), 2))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + 3, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2.5f, r.Bottom - 4), new PointF(r.Right - 3, r.Top + 3) });
            }
        }

        class Cores : ProfessionalColorTable
        {
            static readonly Color Fundo = Color.FromArgb(24, 24, 24), Sel = Color.FromArgb(56, 56, 56), Linha = Color.FromArgb(60, 60, 60);
            public override Color ToolStripDropDownBackground { get { return Fundo; } }
            public override Color MenuBorder { get { return Linha; } }
            public override Color MenuItemBorder { get { return Sel; } }
            public override Color MenuItemSelected { get { return Sel; } }
            public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
            public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
            public override Color ImageMarginGradientBegin { get { return Fundo; } }
            public override Color ImageMarginGradientMiddle { get { return Fundo; } }
            public override Color ImageMarginGradientEnd { get { return Fundo; } }
            public override Color SeparatorDark { get { return Linha; } }
            public override Color SeparatorLight { get { return Fundo; } }
            public override Color CheckBackground { get { return Fundo; } }
            public override Color CheckSelectedBackground { get { return Sel; } }
            public override Color CheckPressedBackground { get { return Sel; } }
        }
    }
}
