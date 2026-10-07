using System;
using System.Globalization;

namespace Pulso
{
    static class Texto
    {
        static readonly CultureInfo Br = new CultureInfo("pt-BR");

        // Percentual do anel: "61%", "0,6%", "<0,1%"; "~" quando inclui estimativa local
        public static string Pct(double v, bool estimado)
        {
            string s;
            if (v <= 0) s = "0%";
            else if (v < 0.001) s = "<0,1%";
            else if (v < 0.01) s = (v * 100).ToString("0.0", Br) + "%";
            else s = Math.Min(100, Math.Round(v * 100)).ToString("0", Br) + "%";
            return (estimado && v > 0 ? "~" : "") + s;
        }

        public static string UsadoRestante(double v, bool estimado)
        {
            int u = (int)Math.Round(Math.Min(1, v) * 100);
            return (estimado ? "~" : "") + u + "% usado · " + (100 - u) + "% restante";
        }

        // "renova em 23 min" / "renova às 16:59" / "renova qui 16:59" / "renova 12 out"
        public static string Renova(DateTime? utc)
        {
            if (!utc.HasValue) return null;
            var falta = utc.Value - DateTime.UtcNow;
            var local = utc.Value.ToLocalTime();
            if (falta.TotalSeconds <= 0) return "renovando…";
            if (falta.TotalMinutes < 60) return "renova em " + Math.Max(1, (int)Math.Ceiling(falta.TotalMinutes)) + " min";
            if (falta.TotalHours < 24) return "renova às " + local.ToString("HH:mm", Br);
            if (falta.TotalDays < 7) return "renova " + Br.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek).TrimEnd('.') + " " + local.ToString("HH:mm", Br);
            return "renova " + local.Day + " " + Br.DateTimeFormat.GetAbbreviatedMonthName(local.Month).TrimEnd('.');
        }

        // Para o menu da bandeja: "renova em 2h 15min"
        public static string RenovaEm(DateTime? utc)
        {
            if (!utc.HasValue) return null;
            var falta = utc.Value - DateTime.UtcNow;
            return falta.TotalSeconds <= 0 ? "renovando…" : "renova em " + Tempo.Duracao(falta);
        }

        // Projeção pelo ritmo atual: só diz algo útil se esgota antes de renovar
        public static string Ritmo(double? porHora, Janela j)
        {
            if (!porHora.HasValue || porHora.Value < 0.002 || j == null) return null;
            string ritmo = "Ritmo: +" + Math.Max(1, (int)Math.Round(porHora.Value * 100)) + " pts/h";
            double falta = 1 - j.Atual;
            if (falta <= 0) return ritmo;
            var esgota = DateTime.UtcNow.AddHours(falta / porHora.Value);
            if (j.ResetaEm.HasValue && esgota >= j.ResetaEm.Value) return ritmo + " · não esgota antes de renovar";
            if ((esgota - DateTime.UtcNow).TotalHours > 48) return ritmo;
            return ritmo + " · esgota por volta de " + esgota.ToLocalTime().ToString("HH:mm", Br);
        }

        public static string Frescor(Provedor p)
        {
            if (!p.Confirmado.HasValue) return null;
            string s = "Exato " + Tempo.Ha(p.Confirmado.Value) + (p.Fonte != null ? " · " + p.Fonte : "");
            foreach (var j in p.Janelas) if (j.Estimado.HasValue) return s + " · estimativa ao vivo";
            return s;
        }

        public static string Estado(Atividade a)
        {
            switch (a)
            {
                case Atividade.Trabalhando: return "trabalhando";
                case Atividade.Aguardando: return "aguardando você";
                case Atividade.Concluida: return "concluída";
                default: return "ociosa";
            }
        }

        public static string Plural(int n, string um, string varios) { return n + " " + (n == 1 ? um : varios); }
    }
}
