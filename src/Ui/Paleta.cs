using System;
using System.Drawing;
using Microsoft.Win32;

namespace Pulso
{
    // Cores do notch
    class Paleta
    {
        public Color Pilula, Borda, BordaRecolhida, Cartao, CartaoLinha, CartaoRegra, Barra, Trilho, Miolo;
        public Color Folga, Atencao, Critico;
        public Color Tinta, Tinta2, Tinta3, Tinta4, TintaFraca;
        public Color Botao, BotaoLinha, Hover;

        public static readonly Paleta Escura = new Paleta
        {
            Pilula = Hex("#000000"), Borda = Hex("#2e2e2e"), BordaRecolhida = Hex("#5c5c5c"),
            Cartao = Hex("#0a0a0a"), CartaoLinha = Hex("#242424"), CartaoRegra = Hex("#1e1e1e"),
            Barra = Hex("#2d2d2d"), Trilho = Hex("#303030"), Miolo = Hex("#2a2a2a"),
            Folga = Hex("#00FF88"), Atencao = Hex("#F2FF00"), Critico = Hex("#FF3F00"),
            Tinta = Hex("#ffffff"), Tinta2 = Hex("#e8e8ea"), Tinta3 = Hex("#c8c8c8"), Tinta4 = Hex("#b0b0b3"), TintaFraca = Hex("#808080"),
            Botao = Hex("#252525"), BotaoLinha = Hex("#555555"), Hover = Hex("#383838"),
        };

        public static readonly Paleta Clara = new Paleta
        {
            Pilula = Hex("#f5f5f7"), Borda = Hex("#d2d2d7"), BordaRecolhida = Hex("#86868b"),
            Cartao = Hex("#ffffff"), CartaoLinha = Hex("#e4e4e8"), CartaoRegra = Hex("#e8e8ec"),
            Barra = Color.FromArgb(38, 0, 0, 0), Trilho = Color.FromArgb(41, 0, 0, 0), Miolo = Hex("#ffffff"),
            Folga = Hex("#00A356"), Atencao = Hex("#B08800"), Critico = Hex("#FF3F00"),
            Tinta = Hex("#1d1d1f"), Tinta2 = Hex("#1d1d1f"), Tinta3 = Hex("#3c3c43"), Tinta4 = Hex("#4a4a4f"), TintaFraca = Hex("#6b6b6b"),
            Botao = Hex("#ffffff"), BotaoLinha = Hex("#c6c6cc"), Hover = Hex("#f0f0f2"),
        };

        public static Paleta Atual
        {
            get
            {
                var t = Config.Atual.Tema;
                if (t == Tema.Claro) return Clara;
                if (t == Tema.Escuro) return Escura;
                return SistemaClaro() ? Clara : Escura;
            }
        }

        static bool SistemaClaro()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        // Cor do consumo: degraus (verde < atenção ≤ amarelo < crítico ≤ vermelho) ou gradual
        public Color Tom(double usado)
        {
            var c = Config.Atual;
            if (!c.CorGradual)
            {
                if (usado >= c.LimiteCritico) return Critico;
                if (usado >= c.LimiteAtencao) return Atencao;
                return Folga;
            }
            if (usado <= c.LimiteAtencao) return Misturar(Folga, Atencao, usado / c.LimiteAtencao);
            return Misturar(Atencao, Critico, (usado - c.LimiteAtencao) / Math.Max(0.01, 1 - c.LimiteAtencao));
        }

        public static Color Misturar(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alfa(Color c, double opacidade)
        {
            return Color.FromArgb((int)Math.Round(c.A * Math.Max(0, Math.Min(1, opacidade))), c.R, c.G, c.B);
        }

        public static Color Hex(string h)
        {
            h = h.TrimStart('#');
            return Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }
    }
}
