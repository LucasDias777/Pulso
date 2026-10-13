using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulso
{
    // Janela única de instalar, atualizar e desinstalar, no lugar do console (pedido do dono, 2026-10-13): pergunta,
    // mostra o progresso numa barra (o download com a porcentagem de verdade) e termina dizendo o que aconteceu, com OK.
    // O trabalho roda numa thread e avisa a janela a cada passo. Fechar no meio não vale: o trabalho pela metade
    // deixaria o Pulso sem exe ou sem registro.
    class Assistente : Form
    {
        public enum Modo { Instalar, AtualizarPorCima, Atualizar, Desinstalar }

        // O que a janela executa (numa thread): devolve o erro para mostrar, ou nulo se deu certo
        public Func<Assistente, string> Fazer;
        // Depois do OK na tela de sucesso (abrir o Pulso, reabrir a versão nova, apagar a pasta…)
        public Action AoConcluir;
        public bool Concluiu { get; private set; }
        public bool Opcao1 { get; private set; }
        public bool Opcao2 { get; private set; }

        readonly Modo modo;
        readonly string versaoAntes, versaoNova;
        readonly Label titulo, texto, status, porcento;
        readonly ProgressBar barra;
        readonly CheckBox opcao1, opcao2;
        readonly Button principal, secundario;
        readonly TableLayoutPanel linhaStatus;
        readonly System.Windows.Forms.Timer anima = new System.Windows.Forms.Timer { Interval = 120 };
        int animAlvo;
        bool trabalhando, terminou;

        // versaoAntes/versaoNova: só o número ("1.3"), para o texto da atualização — nunca o commit
        public Assistente(Modo modo, string versaoAntes = null, string versaoNova = null)
        {
            this.modo = modo;
            this.versaoAntes = versaoAntes;
            this.versaoNova = versaoNova;
            float k = DeviceDpi / 96f;
            Func<float, int> E = v => (int)Math.Round(v * k);
            Text = Titulo().T();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, E(22), E(16));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int largura = E(380);
            var grade = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Location = new Point(E(22), E(20)), Margin = new Padding(0) };
            grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grade.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, largura));

            var logo = new PictureBox { Size = new Size(E(48), E(48)), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, E(18), 0) };
            try { logo.Image = new Icon(Icon, E(48), E(48)).ToBitmap(); } catch { }
            grade.Controls.Add(logo, 0, 0);
            grade.SetRowSpan(logo, 6);

            titulo = new Label { AutoSize = true, MaximumSize = new Size(largura, 0), Font = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold), Margin = new Padding(0, 0, 0, E(6)) };
            texto = new Label { AutoSize = true, MaximumSize = new Size(largura, 0), Margin = new Padding(0, 0, 0, E(14)) };
            opcao1 = new CheckBox { AutoSize = true, Margin = new Padding(0, 0, 0, E(6)) };
            opcao2 = new CheckBox { AutoSize = true, Margin = new Padding(0, 0, 0, E(6)) };
            barra = new ProgressBar { Width = largura, Height = E(18), Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, E(2), 0, E(6)) };
            status = new Label { AutoSize = true, MaximumSize = new Size(largura - E(56), 0), Margin = new Padding(0) };
            porcento = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            // Largura fixa (a da barra): com AutoSize ela encolhia até o texto e a porcentagem colava nele
            linhaStatus = new TableLayoutPanel { ColumnCount = 2, Size = new Size(largura, E(20)), Margin = new Padding(0, 0, 0, E(6)) };
            linhaStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            linhaStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linhaStatus.Controls.Add(status, 0, 0);
            linhaStatus.Controls.Add(porcento, 1, 0);

            grade.Controls.Add(titulo, 1, 0);
            grade.Controls.Add(texto, 1, 1);
            grade.Controls.Add(opcao1, 1, 2);
            grade.Controls.Add(opcao2, 1, 3);
            grade.Controls.Add(barra, 1, 4);
            grade.Controls.Add(linhaStatus, 1, 5);

            var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, E(12), 0, 0), WrapContents = false };
            secundario = new Button { AutoSize = true, MinimumSize = new Size(E(92), E(28)), Margin = new Padding(E(8), 0, 0, 0) };
            principal = new Button { AutoSize = true, MinimumSize = new Size(E(92), E(28)), Margin = new Padding(0) };
            botoes.Controls.Add(secundario);
            botoes.Controls.Add(principal);
            grade.Controls.Add(botoes, 0, 6);
            grade.SetColumnSpan(botoes, 2);
            Controls.Add(grade);

            principal.Click += delegate { Principal(); };
            secundario.Click += delegate { Close(); };
            AcceptButton = principal;
            anima.Tick += delegate { if (barra.Value < animAlvo) barra.Value++; else anima.Stop(); };

            if (modo == Modo.Atualizar) MostrarProgresso(); // quem pediu já clicou em "Instalar atualização"
            else MostrarPergunta();
        }

        string Titulo()
        {
            switch (modo)
            {
                case Modo.Instalar: return "Instalar o Pulso";
                case Modo.Desinstalar: return "Desinstalar o Pulso";
                default: return "Atualizar o Pulso";
            }
        }

        // ---------- telas ----------
        void MostrarPergunta()
        {
            barra.Visible = linhaStatus.Visible = false;
            secundario.Text = "Cancelar".T();
            switch (modo)
            {
                case Modo.Instalar:
                    titulo.Text = "Instalar o Pulso neste computador?".T();
                    texto.Text = "Ele abre junto com o Windows, fica na bandeja do sistema e pode ser desinstalado em Configurações › Aplicativos.".T();
                    Opcoes("Criar atalho no Menu Iniciar".T(), true, "Criar atalho na área de trabalho".T(), false);
                    principal.Text = "Instalar".T();
                    break;
                case Modo.AtualizarPorCima:
                    titulo.Text = "Atualizar o Pulso instalado?".T();
                    texto.Text = FraseDaVersao() + "Suas configurações e o histórico continuam.".T();
                    Opcoes(null, false, null, false);
                    principal.Text = "Atualizar".T();
                    break;
                case Modo.Desinstalar:
                    titulo.Text = "Desinstalar o Pulso?".T();
                    texto.Text = "A cápsula, o ícone da bandeja, os atalhos e o início com o Windows serão removidos, junto com a barra de status e os limites do Claude Code e do Codex, se estiverem ligados.".T();
                    Opcoes("Apagar também as configurações e o histórico".T(), false, null, false);
                    principal.Text = "Desinstalar".T();
                    break;
            }
        }

        // O exe baixado contra o instalado: versão nova, a mesma, ou uma anterior (quem baixou uma Release antiga)
        string FraseDaVersao()
        {
            Version antes, nova;
            if (versaoAntes == null || versaoNova == null || !Version.TryParse(versaoAntes, out antes) || !Version.TryParse(versaoNova, out nova)) return "";
            // Nesta tela o exe já foi baixado: o botão instala, não baixa
            if (nova > antes) return "A nova versão {0} do Pulso foi lançada. Clique em Atualizar para instalá-la.".T(versaoNova) + " ";
            if (nova == antes) return "Você já está com a versão {0} do Pulso; atualizar instala esta cópia por cima.".T(versaoNova) + " ";
            return "Esta é a versão {0}, anterior à instalada ({1}); atualizar volta para ela.".T(versaoNova, versaoAntes) + " ";
        }

        void Opcoes(string t1, bool v1, string t2, bool v2)
        {
            opcao1.Visible = t1 != null; opcao1.Text = t1 ?? ""; opcao1.Checked = v1;
            opcao2.Visible = t2 != null; opcao2.Text = t2 ?? ""; opcao2.Checked = v2;
        }

        void MostrarProgresso()
        {
            opcao1.Visible = opcao2.Visible = false;
            barra.Visible = linhaStatus.Visible = true;
            barra.Value = 0;
            status.Text = "Preparando…".T();
            porcento.Text = "0%";
            titulo.Text = modo == Modo.Instalar ? "Instalando o Pulso…".T() : modo == Modo.Desinstalar ? "Desinstalando o Pulso…".T() : "Atualizando o Pulso…".T();
            texto.Text = modo == Modo.Atualizar && Atualizacao.PeloDownload ? "Baixando a versão nova do GitHub. Leva alguns segundos.".T()
                : modo == Modo.Atualizar ? "Baixando, compilando e instalando a versão nova. Leva alguns segundos.".T()
                : "Leva só alguns segundos.".T();
            principal.Visible = false;
            secundario.Text = "Cancelar".T();
            secundario.Enabled = false;
        }

        void MostrarFim(string erro)
        {
            anima.Stop();
            trabalhando = false;
            terminou = true;
            Concluiu = erro == null;
            barra.Visible = linhaStatus.Visible = false;
            opcao1.Visible = opcao2.Visible = false;
            secundario.Visible = false;
            principal.Visible = true;
            principal.Text = "OK".T();
            if (erro == null)
            {
                switch (modo)
                {
                    case Modo.Instalar:
                        titulo.Text = "Pulso instalado".T();
                        texto.Text = "Tudo pronto. Clique em OK para abrir: ele fica na bandeja do sistema e abre junto com o Windows.".T();
                        break;
                    case Modo.Desinstalar:
                        titulo.Text = "Pulso desinstalado".T();
                        texto.Text = Opcao1 ? "O Pulso foi removido deste computador, com as configurações e o histórico.".T()
                            : "O Pulso foi removido deste computador. Suas configurações e o histórico ficaram guardados, para o caso de você instalar de novo.".T();
                        break;
                    default:
                        titulo.Text = "Pulso atualizado".T();
                        texto.Text = (versaoNova != null ? "Agora você está com a versão {0}.".T(versaoNova) + " " : "")
                            + "Clique em OK para abrir o Pulso de novo.".T();
                        break;
                }
            }
            else
            {
                titulo.Text = modo == Modo.Instalar ? "Não foi possível instalar o Pulso".T() : modo == Modo.Desinstalar ? "Não foi possível desinstalar o Pulso".T() : "Não foi possível atualizar o Pulso".T();
                texto.Text = erro + (modo == Modo.Atualizar || modo == Modo.AtualizarPorCima ? "\n\n" + "O Pulso continua na versão de antes.".T() : "");
            }
            principal.Focus();
        }

        void Principal()
        {
            if (terminou) { Close(); return; }
            Opcao1 = opcao1.Checked;
            Opcao2 = opcao2.Checked;
            // Só a pergunta (o --registrar do instalar.cmd): quem chamou faz o resto
            if (Fazer == null) { Concluiu = true; DialogResult = DialogResult.OK; Close(); return; }
            MostrarProgresso();
            Comecar();
        }

        void Comecar()
        {
            trabalhando = true;
            var fazer = Fazer;
            Task.Factory.StartNew(delegate
            {
                string erro;
                try { erro = fazer(this); }
                catch (Exception e) { Log.Erro(Text, e); erro = e.GetBaseException().Message; }
                Na(delegate { MostrarFim(erro); });
            });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (modo == Modo.Atualizar && Fazer != null && !trabalhando && !terminou) Comecar();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (trabalhando) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            anima.Dispose();
            if (Concluiu && terminou && AoConcluir != null)
                try { AoConcluir(); } catch (Exception ex) { Log.Erro("depois do " + Text, ex); }
        }

        // ---------- progresso (chamados da thread do trabalho) ----------

        // Um passo: a barra vai até "pct" e o texto muda. Um instante de pausa para dar para ler: os passos locais
        // levam milissegundos e a barra pularia de 0 a 100
        public void Etapa(int pct, string passo)
        {
            Na(delegate { anima.Stop(); Valor(pct); status.Text = passo; });
            Thread.Sleep(280);
        }

        // Progresso contínuo (o download), sem pausa
        public void Progresso(int pct, string passo)
        {
            Na(delegate { anima.Stop(); Valor(pct); status.Text = passo; });
        }

        // Passo sem medida (git, compilação): a barra anda sozinha devagar até "ate", sem chegar, enquanto ele roda
        public void Andando(int de, int ate, string passo)
        {
            Na(delegate { Valor(de); status.Text = passo; animAlvo = Math.Max(de, ate - 1); anima.Start(); });
        }

        void Valor(int pct)
        {
            pct = Math.Max(barra.Value, Math.Min(100, pct));
            // A barra do Windows anima a subida devagar e ficava atrás da porcentagem (85% escrito, metade da barra, visto no
            // teste real); descer não anima, então sobe um a mais e desce para o valor certo
            if (pct < 100) { barra.Value = pct + 1; barra.Value = pct; }
            else barra.Value = pct;
            porcento.Text = pct + "%";
        }


        void Na(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch (InvalidOperationException) { } }
            else a();
        }

        // ---------- prévia (diagnóstico: --previa-instalacao) ----------
        public static void Previa(string pasta, Action<Form, string> fotografar)
        {
            System.IO.Directory.CreateDirectory(pasta);
            // A janela muda de tamanho ao trocar de tela (AutoSize): espera o leiaute e o desenho antes da foto
            Action<Assistente, string> foto = (a, nome) =>
            {
                // Fora da tela o Windows compõe a janela com atraso: sem esta espera a foto sai com a tela anterior
                for (int i = 0; i < 8; i++) { a.PerformLayout(); a.Refresh(); Application.DoEvents(); Thread.Sleep(120); }
                fotografar(a, System.IO.Path.Combine(pasta, nome + ".png"));
            };
            foreach (var m in new[] { Modo.Instalar, Modo.AtualizarPorCima, Modo.Atualizar, Modo.Desinstalar })
            {
                // Na tela, quase transparente: fora dela o Windows 11 não compõe a janela e a foto sai velha ou em branco
                using (var a = new Assistente(m, "1.3", "1.4") { StartPosition = FormStartPosition.Manual, Location = Screen.PrimaryScreen.WorkingArea.Location, ShowInTaskbar = false, Opacity = 0.02 })
                {
                    a.Show();
                    string n = m.ToString().ToLowerInvariant();
                    if (m != Modo.Atualizar) foto(a, n + "-1-pergunta");
                    a.MostrarProgresso();
                    a.Valor(m == Modo.Atualizar ? 42 : 65);
                    a.status.Text = m == Modo.Atualizar ? "Baixando a versão nova… {0} de {1}".T("180 KB", "430 KB")
                        : m == Modo.Desinstalar ? "Removendo os atalhos…".T() : "Criando os atalhos e a entrada em Aplicativos…".T();
                    foto(a, n + "-2-progresso");
                    a.Opcao1 = m == Modo.Desinstalar;
                    a.MostrarFim(null);
                    foto(a, n + "-3-pronto");
                    if (m == Modo.Atualizar)
                    {
                        a.terminou = false;
                        a.MostrarFim("O arquivo baixado não confere com a versão publicada.".T());
                        foto(a, n + "-4-erro");
                    }
                    a.terminou = false; a.Concluiu = false;
                }
            }
        }
    }
}
