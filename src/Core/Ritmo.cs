using System;
using System.Collections.Generic;

namespace Pulso
{
    // Ritmo de uso: compara o quanto da cota já foi usado com o quanto do tempo
    // da janela já passou. Positivo = acima do ritmo (vai faltar); negativo = folga.
    static class Ritmo
    {
        public static double? Decorrido(Janela j)
        {
            if (j == null || !j.ResetaEm.HasValue || j.Minutos <= 0) return null;
            double resta = (j.ResetaEm.Value - DateTime.UtcNow).TotalMinutes;
            if (resta <= 0) return null;
            return 1 - Math.Min(1, resta / j.Minutos);
        }

        // Pontos percentuais: uso − tempo decorrido. Janelas que começam no primeiro uso (5h do Claude e
        // do Codex, semana do Codex) acabaram de abrir com pouco tempo passado: qualquer uso pareceria
        // "acima do ritmo". Antes de 15% da janela a comparação não diz nada — fica de fora.
        public static double? Pontos(Janela j)
        {
            var d = Decorrido(j);
            if (!d.HasValue || d.Value < 0.15) return null;
            return (Math.Min(1, j.Atual) - d.Value) * 100;
        }

        public static string Texto(Janela j)
        {
            var p = Pontos(j);
            if (!p.HasValue) return null;
            double v = Math.Abs(p.Value);
            string n = v < 0.5 ? "<1" : Math.Round(v).ToString("0");
            return (p.Value > 0 ? "{0}% acima do ritmo" : "{0}% de folga").T(n);
        }

        // Velocidade real de consumo pelas leituras exatas recentes (Codex e demais; o Claude usa o
        // índice de custo, que é mais fino). Pontos (fração) por hora nos últimos 30 min.
        static readonly Dictionary<string, List<KeyValuePair<DateTime, double>>> historico = new Dictionary<string, List<KeyValuePair<DateTime, double>>>();

        public static double? Registrar(string chave, DateTime quando, double usado)
        {
            lock (historico)
            {
                List<KeyValuePair<DateTime, double>> h;
                if (!historico.TryGetValue(chave, out h)) historico[chave] = h = new List<KeyValuePair<DateTime, double>>();
                // Caiu: a janela renovou; o histórico anterior não vale mais
                if (h.Count > 0 && usado < h[h.Count - 1].Value - 0.005) h.Clear();
                if (h.Count == 0 || quando > h[h.Count - 1].Key) h.Add(new KeyValuePair<DateTime, double>(quando, usado));
                h.RemoveAll(kv => kv.Key < quando.AddMinutes(-30));
                if (h.Count < 2) return null;
                double horas = (h[h.Count - 1].Key - h[0].Key).TotalHours;
                if (horas < 5 / 60.0) return null; // menos de 5 min de leituras: cedo demais
                double v = (h[h.Count - 1].Value - h[0].Value) / horas;
                return v > 0 ? v : (double?)null;
            }
        }

        // Ritmo do dia: a cota semanal dividida em 7 partes iguais, contadas a partir da abertura da semana.
        // No 1º dia vale 1/7, no 2º 2/7… o anel mostra o uso contra a parte liberada até hoje.
        public static Janela Diario(Janela semanal)
        {
            if (semanal == null || !semanal.ResetaEm.HasValue) return null;
            const double dia = 1440, semana = 7 * 1440;
            var inicio = semanal.ResetaEm.Value.AddMinutes(-semana);
            double passado = Math.Max(0, Math.Min(semana, (DateTime.UtcNow - inicio).TotalMinutes));
            int indice = Math.Min(6, (int)(passado / dia));
            double liberado = (indice + 1) / 7.0;
            var fim = indice == 6 ? semanal.ResetaEm.Value : inicio.AddMinutes((indice + 1) * dia);
            return new Janela
            {
                Id = "daily_pace", Rotulo = "Ritmo do dia ({0}º de 7)".T(indice + 1), Minutos = (int)dia,
                Usado = semanal.Usado / liberado,
                Estimado = semanal.Estimado.HasValue ? semanal.Estimado.Value / liberado : (double?)null,
                ResetaEm = fim,
            };
        }

        // Cópia do Claude com o ritmo do dia na frente (vira o anel principal); a semana segue no cartão
        public static Provedor ComRitmoDiario(Provedor p)
        {
            if (p.Id != "claude" || !Config.Atual.RitmoDiario) return p;
            var d = Diario(p.PorId("weekly_all"));
            if (d == null) return p;
            p.Janelas.Insert(0, d);
            return p;
        }
    }
}
