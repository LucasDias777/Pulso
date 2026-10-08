using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulso
{
    // O notch: janela em camadas com alfa por pixel, desenhada com GDI+ (sem navegador embutido).
    // Pixels transparentes deixam o clique passar sozinhos — não há vigia de cursor ligando e desligando
    // "click-through". Medidas em unidades lógicas × escala do monitor × tamanho escolhido.
    class NotchJanela : Form
    {
        public event Action<string> PedirAtualizacao;
        public event Action AbrirConfig;
        public event Action<Point> AbrirMenu;

        readonly Superficie sup = new Superficie();
        readonly Timer anim = new Timer();
        readonly Timer esconderCartao = new Timer { Interval = 250 };
        readonly Timer recolherTimer = new Timer { Interval = 450 };
        readonly Timer vigia = new Timer { Interval = 1000 };
        readonly Dictionary<string, Font> fontes = new Dictionary<string, Font>();

        // Geometria (pixels físicos da tela)
        float s = 1;
        float sCartao = 1;             // cartões: escala do monitor × tamanho, mas nunca menor que no Médio
        Rectangle area, monitor;
        Borda borda;
        List<string> slots = new List<string>();
        RectangleF corpo;
        float R;
        PointF[] centros = new PointF[0];
        PointF orbe;
        Rectangle janela;
        RectangleF cartaoRet;

        // Interação
        int slotCartao = -1;
        bool dentro, sobreOrbe, sobreGrip, pressionado, arrastando, pressNoOrbe, pressNoGrip, comAlt;
        readonly List<KeyValuePair<RectangleF, Sessao>> linhasSessao = new List<KeyValuePair<RectangleF, Sessao>>();
        bool? fundoEscuro;                                   // cápsula adaptável: o que está atrás da pílula recolhida
        readonly Timer amostra = new Timer { Interval = 500 };
        Point pressTela;
        bool expandido = true;
        float expansao = 1;            // 0 recolhido … 1 aberto
        DateTime animInicio; float animDe, animPara; bool animando;
        readonly Dictionary<string, DateTime> girando = new Dictionary<string, DateTime>();
        public bool OcultoPeloAtalho;
        public float EscalaCartao { get { return sCartao; } }
        public Borda BordaAtual { get { return borda; } }
        public RectangleF CorpoTela { get { return corpo; } }
        public Rectangle AreaTrabalho { get { return area; } }
        public bool TelaCheia { get { return telaCheia; } }
        public event Action<IntPtr> PrimeiroPlano;

        public PointF? CentroDoAnel(string id)
        {
            int i = slots.IndexOf(id);
            return i >= 0 && i < centros.Length ? centros[i] : (PointF?)null;
        }
        bool telaCheia;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int indice);
        const int GWL_STYLE = -16, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000;
        WinEventProc procPrimeiroPlano;
        IntPtr ganchoPrimeiroPlano;

        public NotchJanela()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Pulso";
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);

            anim.Tick += delegate { Renderizar(); };
            esconderCartao.Tick += delegate
            {
                esconderCartao.Stop();
                if (pressionado) return;
                dentro = false; slotCartao = -1; sobreOrbe = false;
                if (Config.Atual.Mostrar == Mostrar.AoPassar) recolherTimer.Start();
                Renderizar();
            };
            recolherTimer.Tick += delegate { recolherTimer.Stop(); if (!dentro && !pressionado) Animar(false); };
            vigia.Tick += delegate { Vigiar(); };
            amostra.Tick += delegate { AmostrarFundo(); };
            Estado.Mudou += Renderizar;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            procPrimeiroPlano = AoTrocarPrimeiroPlano;
            ganchoPrimeiroPlano = SetWinEventHook(3, 3, IntPtr.Zero, procPrimeiroPlano, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND
            vigia.Start();
            Reposicionar();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Nativo.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Nativo.MA_NOACTIVATE;
                    return;
                case Nativo.WM_DPICHANGED:
                    m.Result = IntPtr.Zero;
                    BeginInvoke((Action)Reposicionar);
                    return;
                case Nativo.WM_DISPLAYCHANGE:
                case Nativo.WM_SETTINGCHANGE:
                    BeginInvoke((Action)Reposicionar);
                    break;
            }
            base.WndProc(ref m);
        }

        // Outra janela foi para a frente: volta ao topo; e some se ela está em tela cheia (jogo, vídeo, apresentação)
        void AoTrocarPrimeiroPlano(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time)
        {
            var pp = PrimeiroPlano;
            if (pp != null) pp(hwnd);
            AvaliarTelaCheia(hwnd);
            if (!telaCheia && IsHandleCreated && Visible)
                Nativo.SetWindowPos(Handle, Nativo.HWND_TOPMOST, 0, 0, 0, 0, Nativo.SWP_NOMOVE | Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE);
        }

        // Só redesenha quando o estado muda
        void AvaliarTelaCheia(IntPtr hwnd)
        {
            bool cheia = OcupaTelaInteira(hwnd);
            if (cheia != telaCheia) { telaCheia = cheia; Renderizar(); }
        }

        // Tela cheia: cobre o monitor do notch e não é maximizada com moldura (WS_CAPTION ou WS_THICKFRAME).
        // Maximizada comum também cobre o monitor quando a barra se oculta ou está em outro monitor (sobra a
        // borda invisível de 8 px); F11, vídeo, jogo sem borda e apresentação tiram a moldura.
        bool OcupaTelaInteira(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == Handle) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(hwnd, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Windows.UI.Core.CoreWindow") return false;
            Nativo.RECT r;
            if (!Nativo.GetWindowRect(hwnd, out r)) return false;
            var ret = Nativo.Ret(r);
            if (!ret.Contains(monitor) || monitor.Width <= 0) return false;
            int estilo = GetWindowLong(hwnd, GWL_STYLE);
            bool moldura = (estilo & WS_CAPTION) == WS_CAPTION || (estilo & WS_THICKFRAME) != 0;
            return !(moldura && IsZoomed(hwnd));
        }

        void Vigiar()
        {
            // F11 ou sair do vídeo não trocam de janela: reavalia a da frente a cada segundo
            var frente = Nativo.GetForegroundWindow();
            if (frente != IntPtr.Zero) AvaliarTelaCheia(frente);
            // Área de trabalho mudou (barra de tarefas, resolução): reposiciona
            var alvo = MonitorAlvo();
            if (alvo.WorkingArea != area || alvo.Bounds != monitor) Reposicionar();
            else if (slotCartao >= 0 || (DateTime.UtcNow.Second % 15) == 0) Renderizar(); // "há 12 s" no cartão
            if (Visible && !telaCheia)
                Nativo.SetWindowPos(Handle, Nativo.HWND_TOPMOST, 0, 0, 0, 0, Nativo.SWP_NOMOVE | Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE);
        }

        public void AlternarVisivel()
        {
            OcultoPeloAtalho = !OcultoPeloAtalho;
            Renderizar();
        }

        public void Atualizada(string id)
        {
            girando[id] = DateTime.UtcNow;
            Renderizar();
        }

        // ---------- Geometria ----------

        static Screen MonitorAlvo()
        {
            string nome = Config.Atual.Monitor;
            if (nome != null) foreach (var sc in Screen.AllScreens) if (sc.DeviceName == nome) return sc;
            return Screen.PrimaryScreen;
        }

        static IntPtr HMonitor(Screen sc)
        {
            var c = new Nativo.PONTO(sc.Bounds.Left + sc.Bounds.Width / 2, sc.Bounds.Top + sc.Bounds.Height / 2);
            return Nativo.MonitorFromPoint(c, Nativo.MONITOR_DEFAULTTONEAREST);
        }

        static float EscalaDe(Screen sc) { return Nativo.EscalaDoMonitor(HMonitor(sc)); }

        bool Vertical { get { return borda == Borda.Direita || borda == Borda.Esquerda; } }

        public void Reposicionar()
        {
            var cfg = Config.Atual;
            var sc = MonitorAlvo();
            area = sc.WorkingArea;
            monitor = sc.Bounds;
            DWrite.Monitor = HMonitor(sc); // o ClearType e a gamma do texto são os deste monitor
            float esc = monitorFixo > 0 ? monitorFixo : EscalaDe(sc), tam = tamanhoFixo > 0 ? tamanhoFixo : (float)cfg.Tamanho;
            float novaEscala = esc * tam;
            // O cartão é texto para ler: não encolhe com o notch Pequeno (só pílula e anéis encolhem), mas cresce no Grande
            float novaCartao = esc * Math.Max(1, tam);
            if (Math.Abs(novaEscala - s) > 0.001f || Math.Abs(novaCartao - sCartao) > 0.001f) { foreach (var f in fontes.Values) f.Dispose(); fontes.Clear(); }
            s = novaEscala;
            sCartao = novaCartao;
            borda = cfg.Borda;
            slots = SlotsDesejados();
            int n = Math.Max(1, slots.Count);
            R = 38.7f * s;
            float frac = (float)cfg.PosicaoNaBorda;
            if (Vertical)
            {
                float esp = 70 * s, comp = (36 + n * 70 + (n - 1) * 14 + 2) * s;
                float min = area.Top + R + comp / 2, max = area.Bottom - R - comp / 2;
                float cy = max > min ? min + (max - min) * frac : (area.Top + area.Bottom) / 2f;
                float x = borda == Borda.Direita ? area.Right - esp : area.Left;
                corpo = new RectangleF(x, cy - comp / 2, esp, comp);
                centros = new PointF[slots.Count];
                for (int i = 0; i < slots.Count; i++) centros[i] = new PointF(corpo.Left + esp / 2, corpo.Top + (18 + 22 + i * 84) * s);
                orbe = new PointF(borda == Borda.Direita ? area.Right - R : area.Left + R, corpo.Bottom + R);
            }
            else
            {
                float esp = 95 * s, comp = (36 + n * 44 + (n - 1) * 14 + 2) * s;
                float min = area.Left + R + comp / 2, max = area.Right - R - comp / 2;
                float cx = max > min ? min + (max - min) * frac : (area.Left + area.Right) / 2f;
                float y = borda == Borda.Cima ? area.Top : area.Bottom - esp;
                corpo = new RectangleF(cx - comp / 2, y, comp, esp);
                centros = new PointF[slots.Count];
                for (int i = 0; i < slots.Count; i++) centros[i] = new PointF(corpo.Left + (18 + 22 + i * 58) * s, corpo.Top + (12 + 22) * s);
                orbe = new PointF(corpo.Right + R, borda == Borda.Cima ? area.Top + R : area.Bottom - R);
            }
            if (cfg.Mostrar != Mostrar.AoPassar) { expandido = true; expansao = 1; }
            else if (!dentro) { expandido = false; expansao = 0; }
            Renderizar();
        }

        // Pílula com as "orelhas" côncavas encostadas na borda da tela. fechar=false omite o trecho sobre a borda (para o contorno).
        GraphicsPath Pilula(RectangleF b, float R, float r, bool fechar)
        {
            var p = new GraphicsPath();
            float L = b.Left, T = b.Top, Rt = b.Right, B = b.Bottom;
            switch (borda)
            {
                case Borda.Direita:
                    p.AddArc(Rt - 2 * R, T - 2 * R, 2 * R, 2 * R, 0, 90);
                    p.AddLine(Rt - R, T, L + r, T);
                    p.AddArc(L, T, 2 * r, 2 * r, 270, -90);
                    p.AddLine(L, T + r, L, B - r);
                    p.AddArc(L, B - 2 * r, 2 * r, 2 * r, 180, -90);
                    p.AddLine(L + r, B, Rt - R, B);
                    p.AddArc(Rt - 2 * R, B, 2 * R, 2 * R, 270, 90);
                    if (fechar) p.AddLine(Rt, B + R, Rt, T - R);
                    break;
                case Borda.Esquerda:
                    p.AddArc(L, T - 2 * R, 2 * R, 2 * R, 180, -90);
                    p.AddLine(L + R, T, Rt - r, T);
                    p.AddArc(Rt - 2 * r, T, 2 * r, 2 * r, 270, 90);
                    p.AddLine(Rt, T + r, Rt, B - r);
                    p.AddArc(Rt - 2 * r, B - 2 * r, 2 * r, 2 * r, 0, 90);
                    p.AddLine(Rt - r, B, L + R, B);
                    p.AddArc(L, B, 2 * R, 2 * R, 270, -90);
                    if (fechar) p.AddLine(L, B + R, L, T - R);
                    break;
                case Borda.Cima:
                    p.AddArc(L - 2 * R, T, 2 * R, 2 * R, 270, 90);
                    p.AddLine(L, T + R, L, B - r);
                    p.AddArc(L, B - 2 * r, 2 * r, 2 * r, 180, -90);
                    p.AddLine(L + r, B, Rt - r, B);
                    p.AddArc(Rt - 2 * r, B - 2 * r, 2 * r, 2 * r, 90, -90);
                    p.AddLine(Rt, B - r, Rt, T + R);
                    p.AddArc(Rt, T, 2 * R, 2 * R, 180, 90);
                    if (fechar) p.AddLine(Rt + R, T, L - R, T);
                    break;
                default:
                    p.AddArc(L - 2 * R, B - 2 * R, 2 * R, 2 * R, 90, -90);
                    p.AddLine(L, B - R, L, T + r);
                    p.AddArc(L, T, 2 * r, 2 * r, 180, 90);
                    p.AddLine(L + r, T, Rt - r, T);
                    p.AddArc(Rt - 2 * r, T, 2 * r, 2 * r, 270, 90);
                    p.AddLine(Rt, T + r, Rt, B - R);
                    p.AddArc(Rt, B - 2 * R, 2 * R, 2 * R, 180, -90);
                    if (fechar) p.AddLine(Rt + R, B, L - R, B);
                    break;
            }
            if (fechar) p.CloseFigure();
            return p;
        }

        RectangleF Extensao(RectangleF b, float R)
        {
            return Vertical ? RectangleF.FromLTRB(b.Left, b.Top - R, b.Right, b.Bottom + R)
                            : RectangleF.FromLTRB(b.Left - R, b.Top, b.Right + R, b.Bottom);
        }

        // Pílula recolhida (modo "ao passar o mouse"): 10 × 79, cantos internos de 6
        RectangleF Recolhida()
        {
            float esp = 10 * s, comp = 79 * s;
            var c = new PointF(corpo.Left + corpo.Width / 2, corpo.Top + corpo.Height / 2);
            switch (borda)
            {
                case Borda.Direita: return new RectangleF(area.Right - esp, c.Y - comp / 2, esp, comp);
                case Borda.Esquerda: return new RectangleF(area.Left, c.Y - comp / 2, esp, comp);
                case Borda.Cima: return new RectangleF(c.X - comp / 2, area.Top, comp, esp);
                default: return new RectangleF(c.X - comp / 2, area.Bottom - esp, comp, esp);
            }
        }

        // ---------- Desenho ----------

        // Chave pelo tamanho já em pixels: o notch (s) e o cartão (sCartao) têm escalas próprias
        Font Fonte(float px, float escala, bool forte, bool seminegrito = false)
        {
            px *= escala;
            string chave = px + (forte ? "b" : "") + (seminegrito ? "s" : "");
            Font f;
            if (!fontes.TryGetValue(chave, out f))
            {
                f = seminegrito ? new Font("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel)
                                : new Font("Segoe UI", px, forte ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                fontes[chave] = f;
            }
            return f;
        }

        // Diagnóstico: salva o notch, cada cartão e o estado recolhido em PNG, sem mudar o que está na tela
        public void Capturar(string pasta)
        {
            int slotAntes = slotCartao; bool dentroAntes = dentro; float expAntes = expansao;
            try
            {
                System.IO.Directory.CreateDirectory(pasta);
                dentro = false; slotCartao = -1; expansao = 1;
                Renderizar(false); sup.Salvar(System.IO.Path.Combine(pasta, "notch.png"));
                for (int i = 0; i < slots.Count; i++)
                {
                    dentro = true; slotCartao = i;
                    Renderizar(false); sup.Salvar(System.IO.Path.Combine(pasta, "cartao-" + slots[i] + ".png"));
                }
                dentro = false; slotCartao = -1; expansao = 0;
                Renderizar(false); sup.Salvar(System.IO.Path.Combine(pasta, "recolhido.png"));
            }
            catch (Exception e) { Log.Erro("captura", e); }
            finally { slotCartao = slotAntes; dentro = dentroAntes; expansao = expAntes; Renderizar(); }
        }

        // Bancada (--bancada): as mesmas capturas em combinações de escala do monitor × tamanho do notch
        // ("1.25x0.8"), uma pasta por combinação, sem abrir o app nem mostrar janela
        static bool soCaptura;
        float monitorFixo, tamanhoFixo;

        public static void Bancada(string pasta, string combinacoes)
        {
            soCaptura = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            // Uma sessão de exemplo, para o cartão ter também a linha de sessão
            lock (Estado.Trava) Estado.Pegar("claude").Sessoes["bancada"] = new Sessao { Id = "1a4a", Pasta = "Website", Estado = Atividade.Trabalhando, Ultima = DateTime.UtcNow };
            using (var n = new NotchJanela())
            {
                var h = n.Handle;
                foreach (var comb in combinacoes.Split(','))
                {
                    var partes = comb.Split('x');
                    n.monitorFixo = float.Parse(partes[0], ci);
                    n.tamanhoFixo = float.Parse(partes[1], ci);
                    n.Reposicionar();
                    n.Capturar(System.IO.Path.Combine(pasta, "m" + partes[0] + "-t" + partes[1]));
                }
            }
        }

        public void Renderizar() { Renderizar(true); }

        // Provedores com anel que estão de fato no computador (os "não instalados" não ocupam a cápsula)
        static List<string> SlotsDesejados()
        {
            var l = Config.Atual.Provedores.Where(id => Estado.Pegar(id).Presente || id == "claude" || id == "codex").ToList();
            return l.Count > 0 ? l : new List<string> { Config.Atual.Provedores[0] };
        }

        void Renderizar(bool mostrar)
        {
            if (!IsHandleCreated || IsDisposed) return;
            var cfg = Config.Atual;
            if (!SlotsDesejados().SequenceEqual(slots)) { Reposicionar(); return; }
            bool visivel = cfg.Mostrar != Mostrar.Oculto && !OcultoPeloAtalho && !telaCheia && slots.Count > 0;
            if (!visivel)
            {
                if (Visible) Hide();
                anim.Stop();
                return;
            }
            var pal = Paleta.Atual;
            var provs = new List<Provedor>();
            foreach (var id in slots) provs.Add(Ritmo.ComRitmoDiario(Estado.Copia(id)));

            AvancarAnimacao();
            bool aberto = expansao > 0.001f;
            bool mostrarCartao = aberto && expansao >= 1 && slotCartao >= 0 && slotCartao < provs.Count && !arrastando;

            using (var med = Graphics.FromHwnd(IntPtr.Zero))
            {
                med.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                cartaoRet = mostrarCartao ? PosicionarCartao(med, provs[slotCartao], centros[slotCartao]) : RectangleF.Empty;
            }

            RectangleF lim = aberto ? Extensao(corpo, R) : Recolhida();
            if (aberto)
            {
                lim = RectangleF.Union(lim, new RectangleF(orbe.X - 30 * s, orbe.Y - 30 * s, 60 * s, 60 * s));
                if (cfg.Arrastavel) lim = RectangleF.Union(lim, RetGrip());
            }
            else lim = RectangleF.Union(lim, Faixa());
            if (mostrarCartao) lim = RectangleF.Union(lim, cartaoRet);
            var jan = Rectangle.FromLTRB((int)Math.Floor(lim.Left) - 2, (int)Math.Floor(lim.Top) - 2, (int)Math.Ceiling(lim.Right) + 2, (int)Math.Ceiling(lim.Bottom) + 2);
            jan.Intersect(monitor);

            using (var g = sup.Abrir(jan.Width, jan.Height))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(-jan.X, -jan.Y);
                if (aberto) DesenharAberto(g, pal, provs, mostrarCartao);
                else DesenharRecolhido(g, pal);
            }
            if (!mostrar || soCaptura) return;
            janela = jan;
            sup.Mostrar(Handle, jan.X, jan.Y);
            if (!Visible) { Show(); Nativo.SetWindowPos(Handle, Nativo.HWND_TOPMOST, 0, 0, 0, 0, Nativo.SWP_NOMOVE | Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE); }
            AjustarRelogio(provs);
        }

        RectangleF Faixa()
        {
            // Faixa invisível que acorda o notch recolhido (14 px para dentro da tela)
            var r = Recolhida();
            float f = 14 * s;
            switch (borda)
            {
                case Borda.Direita: return RectangleF.FromLTRB(r.Left - f, r.Top, r.Right, r.Bottom);
                case Borda.Esquerda: return RectangleF.FromLTRB(r.Left, r.Top, r.Right + f, r.Bottom);
                case Borda.Cima: return RectangleF.FromLTRB(r.Left, r.Top, r.Right, r.Bottom + f);
                default: return RectangleF.FromLTRB(r.Left, r.Top - f, r.Right, r.Bottom);
            }
        }

        void DesenharRecolhido(Graphics g, Paleta pal)
        {
            using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, Faixa());
            var r = Recolhida();
            Color cor = pal.Pilula, contorno = pal.BordaRecolhida;
            if (Config.Atual.CapsulaAdaptavel && fundoEscuro.HasValue)
            {
                cor = fundoEscuro.Value ? Paleta.Hex("#f5f5f7") : Color.Black;
                contorno = fundoEscuro.Value ? Paleta.Hex("#86868b") : Paleta.Hex("#5c5c5c");
            }
            using (var p = RetanguloInterno(r, 6 * s))
            using (var b = new SolidBrush(cor))
            using (var pen = new Pen(contorno, 1 * s))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        // Retângulo com cantos arredondados só do lado de dentro da tela
        GraphicsPath RetanguloInterno(RectangleF r, float raio)
        {
            float d = raio * 2;
            var p = new GraphicsPath();
            bool tl = borda == Borda.Direita || borda == Borda.Baixo, tr = borda == Borda.Esquerda || borda == Borda.Baixo;
            bool br = borda == Borda.Esquerda || borda == Borda.Cima, bl = borda == Borda.Direita || borda == Borda.Cima;
            if (tl) p.AddArc(r.Left, r.Top, d, d, 180, 90); else p.AddLine(r.Left, r.Top, r.Left, r.Top);
            if (tr) p.AddArc(r.Right - d, r.Top, d, d, 270, 90); else p.AddLine(r.Right, r.Top, r.Right, r.Top);
            if (br) p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl) p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); else p.AddLine(r.Left, r.Bottom, r.Left, r.Bottom);
            p.CloseFigure();
            return p;
        }

        void DesenharAberto(Graphics g, Paleta pal, List<Provedor> provs, bool mostrarCartao)
        {
            float e = expansao;
            RectangleF b = corpo;
            float Re = R;
            if (e < 1)
            {
                // Abrindo/recolhendo: a pílula cresce a partir da recolhida
                var r0 = Recolhida();
                b = new RectangleF(r0.Left + (corpo.Left - r0.Left) * e, r0.Top + (corpo.Top - r0.Top) * e,
                                   r0.Width + (corpo.Width - r0.Width) * e, r0.Height + (corpo.Height - r0.Height) * e);
                Re = Math.Max(0.5f, R * e);
            }

            // Área invisível entre pílula e cartão (e o orbe), para o mouse não "cair" no vão
            if (dentro && e >= 1)
            {
                using (var inv = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                {
                    var ext = Extensao(b, Re);
                    if (mostrarCartao) g.FillRectangle(inv, RectangleF.Union(ext, Vao(ext)));
                    g.FillEllipse(inv, orbe.X - 28.5f * s, orbe.Y - 28.5f * s, 57 * s, 57 * s);
                    if (Config.Atual.Arrastavel)
                    {
                        var gc = GripCentro();
                        g.FillEllipse(inv, gc.X - 26.3f * s, gc.Y - 26.3f * s, 52.6f * s, 52.6f * s);
                    }
                }
            }

            float rc = Math.Max(0.5f, Math.Min(20 * s, Math.Min(b.Width, b.Height) / 2));
            using (var fundo = Pilula(b, Re, rc, true))
            using (var contorno = Pilula(b, Re, rc, false))
            using (var pb = new SolidBrush(pal.Pilula))
            using (var pen = new Pen(pal.Borda, 1 * s))
            {
                g.FillPath(pb, fundo);
                g.DrawPath(pen, contorno);
            }

            float alfaSlots = e < 1 ? Math.Max(0, (e - 0.4f) / 0.6f) : 1;
            if (alfaSlots > 0)
                for (int i = 0; i < provs.Count && i < centros.Length; i++)
                    DesenharSlot(g, pal, provs[i], centros[i], alfaSlots);

            if (e >= 1) DesenharOrbe(g, pal);
            if (mostrarCartao) DesenharCartao(g, pal, provs[slotCartao], centros[slotCartao]);
        }

        RectangleF Vao(RectangleF ext)
        {
            switch (borda)
            {
                case Borda.Direita: return RectangleF.FromLTRB(cartaoRet.Right, Math.Min(ext.Top, cartaoRet.Top), ext.Left, Math.Max(ext.Bottom, cartaoRet.Bottom));
                case Borda.Esquerda: return RectangleF.FromLTRB(ext.Right, Math.Min(ext.Top, cartaoRet.Top), cartaoRet.Left, Math.Max(ext.Bottom, cartaoRet.Bottom));
                case Borda.Cima: return RectangleF.FromLTRB(Math.Min(ext.Left, cartaoRet.Left), ext.Bottom, Math.Max(ext.Right, cartaoRet.Right), cartaoRet.Top);
                default: return RectangleF.FromLTRB(Math.Min(ext.Left, cartaoRet.Left), cartaoRet.Bottom, Math.Max(ext.Right, cartaoRet.Right), ext.Top);
            }
        }

        void DesenharSlot(Graphics g, Paleta pal, Provedor p, PointF c, float alfa)
        {
            var cfg = Config.Atual;
            var agora = DateTime.UtcNow;
            var principal = p.Principal;
            bool velho = !p.Confirmado.HasValue || (agora - p.Confirmado.Value).TotalMinutes > 15;
            bool comEstimativa = principal != null && principal.Estimado.HasValue && principal.Estimado.Value > principal.Usado + 0.004;
            if (comEstimativa) velho = false;

            // Animação do clique (atualizar): anel encolhe e o progresso dá uma volta
            float escala = 1, giro = 0;
            DateTime t0;
            if (girando.TryGetValue(p.Id, out t0))
            {
                double ms = (agora - t0).TotalMilliseconds;
                if (ms > 950) girando.Remove(p.Id);
                else
                {
                    double t = ms / 950;
                    giro = (float)(360 * (1 - Math.Pow(1 - t, 3)));
                    escala = ms < 380 ? 0.93f : 0.93f + 0.07f * (float)Math.Min(1, (ms - 380) / 300);
                }
            }

            float k = s * 44f / 56f * escala;
            float op = (velho ? 0.55f : 1f) * alfa;
            using (var b = new SolidBrush(Paleta.Alfa(pal.Miolo, alfa))) g.FillEllipse(b, c.X - 22 * k, c.Y - 22 * k, 44 * k, 44 * k);
            using (var pen = new Pen(Paleta.Alfa(pal.Trilho, op), 5 * k)) g.DrawEllipse(pen, c.X - 25 * k, c.Y - 25 * k, 50 * k, 50 * k);

            var at = p.Atividade;
            bool atividade = at == Atividade.Trabalhando || at == Atividade.Aguardando;

            // Anel semanal opcional (dentro ou fora do principal)
            var sem = p.PrimeiraSemanal;
            if (cfg.AnelSemanal != AnelSemanal.Desligado && sem != null && sem != principal && !(cfg.AnelSemanal == AnelSemanal.Dentro && atividade))
            {
                float rs = (cfg.AnelSemanal == AnelSemanal.Dentro ? 16 : 31) * k;
                using (var pen = PenSemanal(Paleta.Alfa(pal.Trilho, op * 0.7), 2.4f * k, false)) g.DrawEllipse(pen, c.X - rs, c.Y - rs, 2 * rs, 2 * rs);
                if (sem.Atual > 0)
                    using (var pen = PenSemanal(Paleta.Alfa(pal.Tom(sem.Atual), op * 0.85), 2.4f * k, true))
                        g.DrawArc(pen, c.X - rs, c.Y - rs, 2 * rs, 2 * rs, -90, (float)(360 * Math.Min(1, sem.Atual)));
            }

            if (principal != null && principal.Atual > 0 && !principal.Contagem.HasValue)
            {
                using (var pen = new Pen(Paleta.Alfa(pal.Tom(principal.Atual), op), 5 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, c.X - 25 * k, c.Y - 25 * k, 50 * k, 50 * k, -90 + giro, (float)Math.Max(0.5, 360 * Math.Min(1, principal.Atual)));
            }

            // Atividade: arco girando (trabalhando) ou círculo pulsando (aguardando você). Em passos, de propósito.
            long ms2 = (long)(agora - DateTime.Today.ToUniversalTime()).TotalMilliseconds;
            float ra = 19 * k;
            if (at == Atividade.Trabalhando)
            {
                float ang = -90 + (ms2 % 1200) / 100 * 30;
                using (var pen = new Pen(Paleta.Alfa(pal.Tinta, alfa), 2.5f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, c.X - ra, c.Y - ra, 2 * ra, 2 * ra, ang, 100.8f);
            }
            else if (at == Atividade.Aguardando)
            {
                double t = (ms2 % 1100) / (1100 / 6) / 6.0;
                double pulso = 0.25 + 0.75 * Math.Abs(1 - 2 * t);
                using (var pen = new Pen(Paleta.Alfa(pal.Atencao, pulso * alfa), 2.5f * k)) g.DrawEllipse(pen, c.X - ra, c.Y - ra, 2 * ra, 2 * ra);
            }

            bool esgotado = principal != null && principal.Atual >= 1;
            using (var icone = Glifos.Caminho(p.Id, c.X, c.Y, 26 * k))
            using (var b = new SolidBrush(Paleta.Alfa(pal.Tinta2, (esgotado || velho ? 0.55 : 1) * alfa)))
                g.FillPath(b, icone);

            string txt;
            if (principal != null) txt = principal.Contagem.HasValue ? "~" + principal.Contagem.Value : Texto.Pct(principal.Atual, comEstimativa);
            else txt = p.Erro != null || p.Janelas.Count > 0 ? "—" : "…";
            // Segoe UI 15 px, peso 600, dígitos tabulares
            TextoGdi.Centro(g, txt, Fonte(15, s, false, true), pal.Tinta, pal.Pilula, new PointF(c.X, c.Y + 38 * s), alfa);
        }

        void DesenharOrbe(Graphics g, Paleta pal)
        {
            if (dentro && Config.Atual.Arrastavel) DesenharGrip(g, pal);
            // A orelha do começo leva o mesmo quarto de arco, espelhado
            float iniComeco;
            switch (borda)
            {
                case Borda.Direita: iniComeco = 0; break;
                case Borda.Esquerda: iniComeco = 90; break;
                case Borda.Cima: iniComeco = 270; break;
                default: iniComeco = 0; break;
            }
            ArcoNaOrelha(g, pal, Vertical ? new PointF(orbe.X, corpo.Top - R) : new PointF(corpo.Left - R, orbe.Y), iniComeco);
            if (sobreOrbe)
            {
                float rd = 23.3f * s;
                using (var b = new SolidBrush(pal.Pilula)) g.FillEllipse(b, orbe.X - rd, orbe.Y - rd, 2 * rd, 2 * rd);
                using (var pen = new Pen(pal.Borda, 1 * s)) g.DrawEllipse(pen, orbe.X - rd, orbe.Y - rd, 2 * rd, 2 * rd);
                // Engrenagem de 21 px no disco de 46,6, cantos arredondados por um traço de 1,4 na mesma cor
                using (var eng = Engrenagem(orbe, 8.93f * s))
                using (var b = new SolidBrush(pal.Tinta2))
                using (var pen = new Pen(pal.Tinta2, 1.4f * s) { LineJoin = LineJoin.Round })
                {
                    g.FillPath(b, eng);
                    g.DrawPath(pen, eng);
                }
                return;
            }
            // Em repouso: um quarto de arco abraçando a orelha, indicando que ali há algo
            float ini;
            switch (borda)
            {
                case Borda.Direita: ini = 270; break;
                case Borda.Esquerda: ini = 180; break;
                case Borda.Cima: ini = 180; break;
                default: ini = 90; break;
            }
            ArcoNaOrelha(g, pal, orbe, ini);
        }

        void ArcoNaOrelha(Graphics g, Paleta pal, PointF c, float ini)
        {
            float r = 28.5f * s;
            using (var pen = new Pen(pal.Borda, 8.8f * s)) g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, ini, 90);
            using (var pen = new Pen(pal.Pilula, 6.8f * s)) g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, ini - 1, 92);
        }

        // Geometria em viewBox 24: 8 dentes com a ponta em arco no raio 10,2 (±10,5°),
        // base no corpo de raio 7,6 (±15°) e furo de 4,1; raio = ponta do dente
        static GraphicsPath Engrenagem(PointF c, float raio)
        {
            float k = raio / 10.2f, rc = 7.6f * k;
            Func<float, float, PointF> P = (r, ang) => new PointF(
                c.X + r * (float)Math.Cos(ang * Math.PI / 180), c.Y + r * (float)Math.Sin(ang * Math.PI / 180));
            var p = new GraphicsPath();
            for (int i = 0; i < 8; i++)
            {
                float meio = -90 + i * 45;
                p.AddLine(P(rc, meio - 15), P(raio, meio - 10.5f));
                p.AddArc(c.X - raio, c.Y - raio, 2 * raio, 2 * raio, meio - 10.5f, 21);
                p.AddLine(P(raio, meio + 10.5f), P(rc, meio + 15));
                p.AddArc(c.X - rc, c.Y - rc, 2 * rc, 2 * rc, meio + 15, 15);
            }
            p.CloseFigure();
            float furo = 4.1f * k;
            p.AddEllipse(c.X - furo, c.Y - furo, 2 * furo, 2 * furo);
            p.FillMode = FillMode.Alternate;
            return p;
        }

        // ---------- Cartão ----------

        const float LarguraCartao = 246, Pad = 16;

        RectangleF PosicionarCartao(Graphics med, Provedor p, PointF c)
        {
            float w = LarguraCartao * sCartao;
            float h = Conteudo(med, null, Paleta.Atual, p, 0, 0, w - 2 * Pad * sCartao) + 2 * Pad * sCartao;
            // A ponta da cauda (28,2, do cartão) para a 10,5 da pílula (do notch)
            float m = 8 * sCartao, folga = 10.5f * s + 28.2f * sCartao;
            float x, y;
            switch (borda)
            {
                case Borda.Direita: x = corpo.Left - folga - w; y = c.Y - h / 2; break;
                case Borda.Esquerda: x = corpo.Right + folga; y = c.Y - h / 2; break;
                case Borda.Cima: x = c.X - w / 2; y = corpo.Bottom + folga; break;
                default: x = c.X - w / 2; y = corpo.Top - folga - h; break;
            }
            x = Math.Max(area.Left + m, Math.Min(area.Right - m - w, x));
            y = Math.Max(area.Top + m, Math.Min(area.Bottom - m - h, y));
            return new RectangleF(x, y, w, h);
        }

        void DesenharCartao(Graphics g, Paleta pal, Provedor p, PointF c)
        {
            linhasSessao.Clear();
            var r = cartaoRet;
            using (var b = new SolidBrush(pal.Cartao))
            {
                using (var forma = Arredondado(r, 16 * sCartao)) g.FillPath(b, forma);
                using (var cauda = Cauda(r, c)) g.FillPath(b, cauda);
            }
            Conteudo(g, g, pal, p, r.Left + Pad * sCartao, r.Top + Pad * sCartao, r.Width - 2 * Pad * sCartao);
        }

        // Cauda do cartão apontando para o anel (base 32,72, comprimento 28,2)
        GraphicsPath Cauda(RectangleF r, PointF alvo)
        {
            float meio = 16.36f * sCartao, lim = 16 * sCartao + meio;
            PointF baseP; PointF dir, perp;
            switch (borda)
            {
                case Borda.Direita: baseP = new PointF(r.Right - 1, Math.Max(r.Top + lim, Math.Min(r.Bottom - lim, alvo.Y))); dir = new PointF(1, 0); perp = new PointF(0, 1); break;
                case Borda.Esquerda: baseP = new PointF(r.Left + 1, Math.Max(r.Top + lim, Math.Min(r.Bottom - lim, alvo.Y))); dir = new PointF(-1, 0); perp = new PointF(0, 1); break;
                case Borda.Cima: baseP = new PointF(Math.Max(r.Left + lim, Math.Min(r.Right - lim, alvo.X)), r.Top + 1); dir = new PointF(0, -1); perp = new PointF(1, 0); break;
                default: baseP = new PointF(Math.Max(r.Left + lim, Math.Min(r.Right - lim, alvo.X)), r.Bottom - 1); dir = new PointF(0, 1); perp = new PointF(1, 0); break;
            }
            Func<float, float, PointF> P = (u, v) => new PointF(
                baseP.X + dir.X * (u * sCartao + (u == 0 ? 0 : 1)) + perp.X * (v - 16.36f) * sCartao,
                baseP.Y + dir.Y * (u * sCartao + (u == 0 ? 0 : 1)) + perp.Y * (v - 16.36f) * sCartao);
            var p = new GraphicsPath();
            p.AddBezier(P(0, 0), P(0, 8.18f), P(16.36f, 12.43f), P(28.2f, 16.36f));
            p.AddBezier(P(28.2f, 16.36f), P(16.36f, 20.29f), P(0, 24.54f), P(0, 32.72f));
            p.CloseFigure();
            return p;
        }

        static GraphicsPath Arredondado(RectangleF r, float raio)
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

        // Mede (g == null) ou desenha o conteúdo do cartão; devolve a altura
        Color fundoTexto;

        float Conteudo(Graphics med, Graphics g, Paleta pal, Provedor p, float x, float y, float w)
        {
            float y0 = y;
            fundoTexto = pal.Cartao;
            var fTitulo = Fonte(14, sCartao, true);
            var fRotulo = Fonte(12, sCartao, false, true);
            var fPeq = Fonte(11, sCartao, false);
            // Como o Codenotch: nota em 12 px com entrelinha de 1,5 (.c-note) e título de grupo em 12 px negrito (.g-head)
            var fNota = Fonte(12, sCartao, false);
            var fGrupo = Fonte(12, sCartao, true);
            float hNota = 18 * sCartao;

            // Cabeçalho: ícone + "Claude" + plano à direita
            float hTit = 20 * sCartao;
            if (g != null)
            {
                using (var ic = Glifos.Caminho(p.Id, x + 8 * sCartao, y + hTit / 2, 16 * sCartao))
                using (var b = new SolidBrush(pal.Tinta2)) g.FillPath(b, ic);
                Escrever(g, p.Nome, fTitulo, pal.Tinta, x + 24 * sCartao, y, w - 24 * sCartao, hTit, StringAlignment.Near);
                if (p.Plano != null) Escrever(g, p.Plano, fPeq, pal.TintaFraca, x, y, w, hTit, StringAlignment.Far, fTitulo);
            }
            y += hTit + 4 * sCartao;

            if (p.Janelas.Count == 0)
            {
                string msg = !p.Presente ? Util.Maiuscula(p.Ausencia) : p.Erro ?? p.Detalhe ?? (p.Id == "codex" ? "Use o Codex uma vez para aparecer o consumo." : "Aguardando a primeira leitura…");
                y += Paragrafo(med, g, msg, fNota, pal.Tinta3, x, y + 6 * sCartao, w, hNota) + 6 * sCartao;
            }

            string grupoAnterior = null;
            foreach (var j in p.Janelas)
            {
                // Grupos (Antigravity: Modelos Gemini / Modelos Claude e GPT)
                if (j.Grupo != null && j.Grupo != grupoAnterior)
                {
                    y += 12 * sCartao;
                    if (g != null) Escrever(g, j.Grupo, fGrupo, pal.Tinta2, x, y, w, 16 * sCartao, StringAlignment.Near);
                    y += 16 * sCartao;
                    grupoAnterior = j.Grupo;
                }
                y += 10 * sCartao;
                float hl = 16 * sCartao;
                if (j.Contagem.HasValue)
                {
                    // Janela só de contagem: sem barra, sem limite publicado
                    if (g != null)
                    {
                        Escrever(g, j.Rotulo, fRotulo, pal.Tinta2, x, y, w, hl, StringAlignment.Near);
                        Escrever(g, j.Contagem.Value == 0 ? "nenhuma requisição hoje" : "~" + j.Contagem.Value + (j.Contagem.Value == 1 ? " requisição hoje" : " requisições hoje"), fPeq, pal.TintaFraca, x, y + hl + 4 * sCartao, w, 15 * sCartao, StringAlignment.Near);
                    }
                    y += hl + 4 * sCartao + 15 * sCartao;
                    continue;
                }
                if (g != null)
                {
                    string rit = j.Id == "daily_pace" ? null : Ritmo.Texto(j);
                    var pts = Ritmo.Pontos(j);
                    float writ = rit != null ? TextoGdi.Medir(rit, fPeq).Width + 1 : 0;
                    if (rit != null && TextoGdi.Medir(j.Rotulo, fRotulo).Width + writ + 10 * sCartao > w) { rit = null; writ = 0; }
                    Escrever(g, j.Rotulo, fRotulo, pal.Tinta2, x, y, w - (writ > 0 ? writ + 10 * sCartao : 0), hl, StringAlignment.Near);
                    if (rit != null) Escrever(g, rit, fPeq, pts > 0 ? pal.Atencao : pal.TintaFraca, x + w - writ, y, writ, hl, StringAlignment.Far, fRotulo);
                }
                y += hl + 6 * sCartao;
                if (g != null)
                {
                    float hb = 4 * sCartao;
                    using (var b = new SolidBrush(pal.Barra))
                    using (var trilho = Arredondado(new RectangleF(x, y, w, hb), 2 * sCartao)) g.FillPath(b, trilho);
                    double conf = Math.Min(1, j.Usado), atual = Math.Min(1, j.Atual);
                    if (atual > conf + 0.002)
                        using (var b = new SolidBrush(Paleta.Alfa(pal.Tom(atual), 0.45)))
                        using (var est = Arredondado(new RectangleF(x, y, (float)Math.Max(hb, w * atual), hb), 2 * sCartao)) g.FillPath(b, est);
                    if (conf > 0)
                        using (var b = new SolidBrush(pal.Tom(atual)))
                        using (var cheio = Arredondado(new RectangleF(x, y, (float)Math.Max(hb, w * conf), hb), 2 * sCartao)) g.FillPath(b, cheio);
                    // Onde você "deveria" estar pelo tempo já passado da janela
                    var dec = j.Id == "daily_pace" ? null : Ritmo.Decorrido(j);
                    if (dec.HasValue)
                        using (var b = new SolidBrush(pal.Tinta3)) g.FillRectangle(b, x + (float)(w * dec.Value) - 1 * sCartao, y - 2 * sCartao, 2 * sCartao, hb + 4 * sCartao);
                }
                y += 4 * sCartao + 4 * sCartao;
                // Embaixo da barra: uso à esquerda, renovação à direita (sem "restante" se não couber)
                float hs = 15 * sCartao;
                bool est2 = j.Estimado.HasValue && j.Estimado.Value > j.Usado + 0.004;
                if (g != null)
                {
                    string rn = Texto.Renova(j.ResetaEm);
                    float wr = rn != null ? TextoGdi.Medir(rn, fPeq).Width + 1 : 0;
                    string uso = Texto.UsadoRestante(j.Atual, est2);
                    if (TextoGdi.Medir(uso, fPeq).Width + wr + 8 * sCartao > w) uso = Texto.Pct(j.Atual, est2) + " usado";
                    Escrever(g, uso, fPeq, pal.TintaFraca, x, y, w - wr - 8 * sCartao, hs, StringAlignment.Near);
                    if (rn != null) Escrever(g, rn, fPeq, pal.TintaFraca, x + w - wr, y, wr, hs, StringAlignment.Far);
                }
                y += hs;
            }

            // Ritmo e projeção
            string ritmo = Texto.Ritmo(p.RitmoHora, p.Principal);
            if (ritmo != null) { y += 8 * sCartao; y += Paragrafo(med, g, ritmo, fPeq, pal.Tinta4, x, y, w); }

            if (p.Detalhe != null && p.Janelas.Count > 0) { y += 8 * sCartao; y += Paragrafo(med, g, p.Detalhe, fNota, pal.Tinta3, x, y, w, hNota); }
            string frescor = Texto.Frescor(p);
            if (frescor != null) { y += 8 * sCartao; y += Paragrafo(med, g, frescor, fPeq, pal.TintaFraca, x, y, w); }
            if (p.Nota != null && p.Janelas.Count > 0) { y += 4 * sCartao; y += Paragrafo(med, g, p.Nota, fNota, pal.Tinta3, x, y, w, hNota); }

            // De onde veio o uso da sessão de 5h (só o deste computador)
            var indice = Projetos.De(p.Id);
            if (indice != null && Config.Atual.ProjetosNoCartao)
            {
                var js = p.Janelas.FirstOrDefault(v => v.Minutos == 300 && !v.Semanal);
                var parcelas = indice.Parcelas(js != null && js.ResetaEm.HasValue ? js.ResetaEm.Value.AddMinutes(-300) : DateTime.UtcNow.AddHours(-5));
                if (parcelas.Count > 4)
                    parcelas = parcelas.Take(3).Concat(new[] { new KeyValuePair<string, double>("Outros", parcelas.Skip(3).Sum(kv => kv.Value)) }).ToList();
                if (parcelas.Count > 0)
                {
                    y += 12 * sCartao;
                    if (g != null) using (var pen = new Pen(pal.CartaoRegra, 1 * sCartao)) g.DrawLine(pen, x, y, x + w, y);
                    y += 8 * sCartao;
                    if (g != null) Escrever(g, "Por projeto nesta sessão", fRotulo, pal.Tinta2, x, y, w, 16 * sCartao, StringAlignment.Near);
                    y += 16 * sCartao + 2 * sCartao;
                    foreach (var kv in parcelas)
                    {
                        float hlin = 16 * sCartao;
                        if (g != null)
                        {
                            string pct = kv.Value < 0.005 ? "<1%" : Math.Round(kv.Value * 100) + "%";
                            float wp = TextoGdi.Medir("100%", fPeq).Width + 2 * sCartao;
                            Escrever(g, kv.Key, fPeq, pal.Tinta4, x, y, w - wp - 8 * sCartao, hlin, StringAlignment.Near);
                            Escrever(g, pct, fPeq, pal.TintaFraca, x + w - wp, y, wp, hlin, StringAlignment.Far);
                            float hb = 3 * sCartao, yb = y + hlin + 1 * sCartao;
                            using (var b = new SolidBrush(pal.Barra))
                            using (var trilho = Arredondado(new RectangleF(x, yb, w, hb), 1.5f * sCartao)) g.FillPath(b, trilho);
                            using (var b = new SolidBrush(pal.Tinta3))
                            using (var cheio = Arredondado(new RectangleF(x, yb, (float)Math.Max(hb, w * kv.Value), hb), 1.5f * sCartao)) g.FillPath(b, cheio);
                        }
                        y += hlin + 1 * sCartao + 3 * sCartao + 5 * sCartao;
                    }
                    y -= 5 * sCartao;
                }
            }

            // Sessões
            var sessoes = p.Sessoes.Values.Where(v => v.Estado != Atividade.Ociosa)
                .OrderBy(v => v.Estado == Atividade.Aguardando ? 0 : v.Estado == Atividade.Trabalhando ? 1 : 2)
                .ThenByDescending(v => v.Ultima).ToList();
            if (sessoes.Count > 0)
            {
                y += 12 * sCartao;
                if (g != null) using (var pen = new Pen(pal.CartaoRegra, 1 * sCartao)) g.DrawLine(pen, x, y, x + w, y);
                y += 8 * sCartao;
                int mostradas = Math.Min(5, sessoes.Count);
                for (int i = 0; i < mostradas; i++)
                {
                    var se = sessoes[i];
                    float hlin = 17 * sCartao;
                    if (g != null)
                    {
                        Color cor = se.Estado == Atividade.Trabalhando ? pal.Tinta : se.Estado == Atividade.Aguardando ? pal.Atencao : pal.Folga;
                        float d = 6 * sCartao;
                        using (var b = new SolidBrush(cor)) g.FillEllipse(b, x, y + (hlin - d) / 2, d, d);
                        string est = Texto.Estado(se.Estado);
                        float we = TextoGdi.Medir(est, fPeq).Width + 8 * sCartao;
                        Escrever(g, se.Titulo, fPeq, pal.Tinta4, x + 12 * sCartao, y, w - 12 * sCartao - we, hlin, StringAlignment.Near);
                        Escrever(g, est, fPeq, pal.TintaFraca, x + w - we, y, we, hlin, StringAlignment.Far);
                        linhasSessao.Add(new KeyValuePair<RectangleF, Sessao>(new RectangleF(x - 4 * sCartao, y, w + 8 * sCartao, hlin), se));
                    }
                    y += hlin;
                }
                if (sessoes.Count > mostradas)
                {
                    float hlin = 16 * sCartao;
                    if (g != null) Escrever(g, "e mais " + (sessoes.Count - mostradas), fPeq, pal.TintaFraca, x + 12 * sCartao, y, w, hlin, StringAlignment.Near);
                    y += hlin;
                }
            }
            return y - y0;
        }

        float Paragrafo(Graphics med, Graphics g, string texto, Font f, Color cor, float x, float y, float w, float entrelinha = 0)
        {
            return TextoGdi.Paragrafo(g, texto, f, cor, fundoTexto, x, y, w, entrelinha);
        }

        void Escrever(Graphics g, string texto, Font f, Color cor, float x, float y, float w, float h, StringAlignment al, Font baseDe = null)
        {
            TextoGdi.Linha(g, texto, f, cor, fundoTexto, new RectangleF(x, y, w, h), al == StringAlignment.Far, baseDe);
        }

        // ---------- Animação ----------

        void Animar(bool abrir)
        {
            if (abrir == expandido && !animando) return;
            expandido = abrir;
            animDe = expansao; animPara = abrir ? 1 : 0;
            animInicio = DateTime.UtcNow;
            animando = true;
            Renderizar();
        }

        void AvancarAnimacao()
        {
            if (!animando) return;
            double t = Math.Min(1, (DateTime.UtcNow - animInicio).TotalMilliseconds / 360);
            double e = 1 - Math.Pow(1 - t, 4);
            expansao = (float)(animDe + (animPara - animDe) * e);
            if (t >= 1) { animando = false; expansao = animPara; }
        }

        // Relógio só roda quando há algo se mexendo: 60 fps na abertura/clique, 10 fps no indicador de atividade
        void AjustarRelogio(List<Provedor> provs)
        {
            var cfg = Config.Atual;
            bool amostrar = cfg.CapsulaAdaptavel && cfg.Mostrar == Mostrar.AoPassar && expansao <= 0 && Visible;
            if (amostrar && !amostra.Enabled) { amostra.Start(); AmostrarFundo(); }
            else if (!amostrar && amostra.Enabled) amostra.Stop();
            int intervalo = 0;
            if (animando || girando.Count > 0) intervalo = 16;
            else if (expansao > 0 && provs.Any(p => p.Atividade == Atividade.Trabalhando)) intervalo = 100;
            else if (expansao > 0 && provs.Any(p => p.Atividade == Atividade.Aguardando)) intervalo = 183;
            if (intervalo == 0) { anim.Stop(); return; }
            if (anim.Interval != intervalo) anim.Interval = intervalo;
            if (!anim.Enabled) anim.Start();
        }

        // ---------- Mouse ----------

        PointF Tela(MouseEventArgs e) { return new PointF(e.X + janela.X, e.Y + janela.Y); }

        int SlotEm(PointF p)
        {
            for (int i = 0; i < centros.Length; i++)
            {
                var c = centros[i];
                if (Vertical ? Math.Abs(p.Y - c.Y) <= 42 * s && p.X >= corpo.Left - 2 && p.X <= corpo.Right + 2
                             : Math.Abs(p.X - c.X) <= 29 * s && p.Y >= corpo.Top - 2 && p.Y <= corpo.Bottom + 2) return i;
            }
            return -1;
        }

        public string ProvedorEm(Point tela)
        {
            int i = SlotEm(tela);
            return i >= 0 && i < slots.Count ? slots[i] : null;
        }

        // Pontinhos de arrastar (2 × 3) ao lado da engrenagem, ao longo da borda, longe da pílula
        PointF GripCentro()
        {
            float d = 43.2f * s;
            return Vertical ? new PointF(orbe.X, orbe.Y + d) : new PointF(orbe.X + d, orbe.Y);
        }

        RectangleF RetGrip()
        {
            var c = GripCentro();
            float r = 27 * s;
            return new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        bool NoGrip(PointF p)
        {
            var c = GripCentro();
            float dx = p.X - c.X, dy = p.Y - c.Y, r = 26.3f * s;
            return Config.Atual.Arrastavel && expansao >= 1 && dentro && dx * dx + dy * dy <= r * r;
        }

        void DesenharGrip(Graphics g, Paleta pal)
        {
            var c = GripCentro();
            float d = 6.1f * s * (sobreGrip ? 1.3f : 1), passo = 9.9f * s;
            using (var b = new SolidBrush(sobreGrip ? pal.Tinta3 : pal.TintaFraca))
                for (int i = -1; i <= 1; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        float ao = i * passo, at = (j - 0.5f) * passo;
                        float px = Vertical ? c.X + at : c.X + ao, py = Vertical ? c.Y + ao : c.Y + at;
                        g.FillEllipse(b, px - d / 2, py - d / 2, d, d);
                    }
        }

        static Pen PenSemanal(Color cor, float largura, bool arco)
        {
            var pen = new Pen(cor, largura);
            if (Config.Atual.AnelTracejado) pen.DashPattern = new[] { 4f / 2.4f, 2f / 2.4f };
            else if (arco) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; }
            return pen;
        }

        // Cápsula adaptável: duas vezes por segundo, só com o notch recolhido, mede o brilho de uma faixa
        // de 12 px ao lado da pílula e escolhe cápsula clara (fundo escuro) ou preta (fundo claro)
        void AmostrarFundo()
        {
            if (expansao > 0 || !Visible) return;
            var r = Recolhida();
            float f = 12 * s, gap = 4 * s;
            RectangleF faixa;
            switch (borda)
            {
                case Borda.Direita: faixa = new RectangleF(r.Left - gap - f, r.Top, f, r.Height); break;
                case Borda.Esquerda: faixa = new RectangleF(r.Right + gap, r.Top, f, r.Height); break;
                case Borda.Cima: faixa = new RectangleF(r.Left, r.Bottom + gap, r.Width, f); break;
                default: faixa = new RectangleF(r.Left, r.Top - gap - f, r.Width, f); break;
            }
            var rr = Rectangle.Round(faixa);
            rr.Intersect(monitor);
            if (rr.Width < 1 || rr.Height < 1) return;
            double soma = 0;
            using (var bmp = new Bitmap(rr.Width, rr.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(rr.Location, Point.Empty, rr.Size);
                for (int y = 0; y < bmp.Height; y += 2)
                    for (int x = 0; x < bmp.Width; x += 2)
                    {
                        var c = bmp.GetPixel(x, y);
                        soma += 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
                    }
                soma /= Math.Max(1, ((bmp.Width + 1) / 2) * ((bmp.Height + 1) / 2));
            }
            const double corte = 0.18;
            bool escuro = fundoEscuro.HasValue
                ? (fundoEscuro.Value ? soma < corte * 1.5 : soma < corte / 1.5)   // histerese de 1,5×
                : soma < corte;
            if (escuro != fundoEscuro) { fundoEscuro = escuro; Renderizar(); }
        }

        static double Linear(byte v)
        {
            double c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        bool NoOrbe(PointF p)
        {
            float dx = p.X - orbe.X, dy = p.Y - orbe.Y;
            return expansao >= 1 && dx * dx + dy * dy <= (28.5f * s) * (28.5f * s);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = Tela(e);
            esconderCartao.Stop();
            recolherTimer.Stop();
            if (pressionado)
            {
                float dx = p.X - pressTela.X, dy = p.Y - pressTela.Y;
                if (!arrastando && Config.Atual.Arrastavel && (pressNoOrbe || pressNoGrip || comAlt) && dx * dx + dy * dy > 16 * s * s) { arrastando = true; Capture = true; slotCartao = -1; }
                if (arrastando) { Arrastar(Point.Round(p)); return; }
            }
            bool estava = dentro;
            dentro = true;
            if (!expandido) { Animar(true); return; }
            bool orbeAgora = NoOrbe(p), gripAgora = !orbeAgora && NoGrip(p);
            int slot = orbeAgora || gripAgora ? -1 : SlotEm(p);
            bool mudou = !estava || orbeAgora != sobreOrbe || gripAgora != sobreGrip;
            sobreOrbe = orbeAgora;
            sobreGrip = gripAgora;
            if (gripAgora && slotCartao != -1) { slotCartao = -1; mudou = true; }
            if (orbeAgora) { if (slotCartao != -1) { slotCartao = -1; mudou = true; } }
            else if (slot >= 0 && slot != slotCartao) { slotCartao = slot; mudou = true; }
            bool naSessao = linhasSessao.Any(kv => kv.Key.Contains(p));
            Cursor = gripAgora ? Cursors.SizeAll : orbeAgora || naSessao ? Cursors.Hand : Cursors.Default;
            if (mudou) Renderizar();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!arrastando) esconderCartao.Start();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var p = Tela(e);
            pressionado = true;
            pressTela = Point.Round(p);
            pressNoOrbe = NoOrbe(p);
            pressNoGrip = !pressNoOrbe && NoGrip(p);
            comAlt = (ModifierKeys & Keys.Alt) == Keys.Alt;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var p = Tela(e);
            if (e.Button == MouseButtons.Right)
            {
                var h = AbrirMenu;
                if (h != null) h(Point.Round(p));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            bool arrastou = arrastando;
            pressionado = false; arrastando = false; Capture = false;
            if (arrastou) { Config.Atual.Salvar(); Reposicionar(); return; }
            if (NoOrbe(p)) { var h = AbrirConfig; if (h != null) h(); return; }
            if (pressNoGrip) return;
            foreach (var kv in linhasSessao)
                if (kv.Key.Contains(p)) { Foco.Trazer(kv.Value.Pasta); return; }
            int slot = SlotEm(p);
            if (slot >= 0 && slot < slots.Count)
            {
                Atualizada(slots[slot]);
                var h = PedirAtualizacao;
                if (h != null) h(slots[slot]);
            }
        }

        // Arrastar: o notch segue o ponteiro pela borda mais próxima, em qualquer monitor
        void Arrastar(Point p)
        {
            var sc = Screen.FromPoint(p);
            var wa = sc.WorkingArea;
            var cfg = Config.Atual;
            float histerese = 40 * s;
            var dist = new Dictionary<Borda, float>
            {
                { Borda.Direita, wa.Right - p.X }, { Borda.Esquerda, p.X - wa.Left },
                { Borda.Cima, p.Y - wa.Top }, { Borda.Baixo, wa.Bottom - p.Y },
            };
            var melhor = dist.OrderBy(kv => kv.Value).First().Key;
            bool mesmoMonitor = sc.DeviceName == MonitorAlvo().DeviceName;
            if (mesmoMonitor && melhor != cfg.Borda && dist[melhor] + histerese > dist[cfg.Borda]) melhor = cfg.Borda;
            cfg.Borda = melhor;
            cfg.Monitor = sc.Primary ? null : sc.DeviceName;
            borda = melhor;
            float esc = EscalaDe(sc) * (float)cfg.Tamanho;
            int n = Math.Max(1, slots.Count);
            float Rr = 38.7f * esc;
            if (Vertical)
            {
                float comp = (36 + n * 70 + (n - 1) * 14 + 2) * esc;
                float min = wa.Top + Rr + comp / 2, max = wa.Bottom - Rr - comp / 2;
                cfg.PosicaoNaBorda = max > min ? (p.Y - min) / (max - min) : 0.5;
            }
            else
            {
                float comp = (36 + n * 44 + (n - 1) * 14 + 2) * esc;
                float min = wa.Left + Rr + comp / 2, max = wa.Right - Rr - comp / 2;
                cfg.PosicaoNaBorda = max > min ? (p.X - min) / (max - min) : 0.5;
            }
            Reposicionar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Estado.Mudou -= Renderizar;
                if (ganchoPrimeiroPlano != IntPtr.Zero) UnhookWinEvent(ganchoPrimeiroPlano);
                anim.Dispose(); esconderCartao.Dispose(); recolherTimer.Dispose(); vigia.Dispose(); amostra.Dispose();
                sup.Dispose();
                foreach (var f in fontes.Values) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
