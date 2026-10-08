using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pulso
{
    // Janela da primeira instalação (exe baixado ou instalar.cmd): onde criar os atalhos. O ícone da bandeja não é opção.
    class OpcoesInstalacao : Form
    {
        readonly CheckBox menuIniciar, areaDeTrabalho;

        public bool MenuIniciar { get { return menuIniciar.Checked; } }
        public bool AreaDeTrabalho { get { return areaDeTrabalho.Checked; } }

        public OpcoesInstalacao()
        {
            float k = DeviceDpi / 96f;
            Func<float, int> E = v => (int)Math.Round(v * k);
            Text = "Instalar o Pulso".T();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, E(20), E(14));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var grade = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Location = new Point(E(20), E(18)), Margin = new Padding(0) };
            grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var logo = new PictureBox { Size = new Size(E(48), E(48)), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, E(16), 0) };
            try { logo.Image = new Icon(Icon, E(48), E(48)).ToBitmap(); } catch { }
            grade.Controls.Add(logo, 0, 0);
            grade.SetRowSpan(logo, 4);

            int largura = E(340);
            grade.Controls.Add(new Label
            {
                Text = "Instalar o Pulso neste computador?".T(), AutoSize = true, MaximumSize = new Size(largura, 0),
                Font = new Font(Font.FontFamily, Font.Size * 1.25f, FontStyle.Bold), Margin = new Padding(0, 0, 0, E(6)),
            }, 1, 0);
            grade.Controls.Add(new Label
            {
                Text = "Ele abre junto com o Windows, fica na bandeja do sistema e pode ser desinstalado em Configurações › Aplicativos.".T(),
                AutoSize = true, MaximumSize = new Size(largura, 0), Margin = new Padding(0, 0, 0, E(14)),
            }, 1, 1);
            menuIniciar = new CheckBox { Text = "Criar atalho no Menu Iniciar".T(), Checked = true, AutoSize = true, Margin = new Padding(0, 0, 0, E(6)) };
            areaDeTrabalho = new CheckBox { Text = "Criar atalho na área de trabalho".T(), Checked = false, AutoSize = true, Margin = new Padding(0, 0, 0, E(18)) };
            grade.Controls.Add(menuIniciar, 1, 2);
            grade.Controls.Add(areaDeTrabalho, 1, 3);

            var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            var cancelar = new Button { Text = "Cancelar".T(), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(E(88), E(28)), Margin = new Padding(E(8), 0, 0, 0) };
            var instalar = new Button { Text = "Instalar".T(), DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(E(88), E(28)), Margin = new Padding(0) };
            botoes.Controls.Add(cancelar);
            botoes.Controls.Add(instalar);
            grade.Controls.Add(botoes, 0, 4);
            grade.SetColumnSpan(botoes, 2);

            Controls.Add(grade);
            AcceptButton = instalar;
            CancelButton = cancelar;
        }
    }
}
