using System;

namespace Pulso
{
    static class Texto
    {
        // Percentual do anel: "61%", "0,6%", "<0,1%"; "~" quando inclui estimativa local
        public static string Pct(double v, bool estimado)
        {
            var c = Idioma.Cultura;
            string s;
            if (v <= 0) s = "0%";
            else if (v < 0.001) s = "<" + 0.1.ToString("0.0", c) + "%";
            else if (v < 0.01) s = (v * 100).ToString("0.0", c) + "%";
            else s = Math.Min(100, Math.Round(v * 100)).ToString("0", c) + "%";
            return (estimado && v > 0 ? "~" : "") + s;
        }

        public static string UsadoRestante(double v, bool estimado)
        {
            int u = (int)Math.Round(Math.Min(1, v) * 100);
            return (estimado ? "~" : "") + "{0}% usado · {1}% restante".T(u, 100 - u);
        }

        // "renova em 23 min" / "renova às 16:59" / "renova qui 16:59" / "renova 12 out"
        public static string Renova(DateTime? utc)
        {
            if (!utc.HasValue) return null;
            var c = Idioma.Cultura;
            var falta = utc.Value - DateTime.UtcNow;
            var local = utc.Value.ToLocalTime();
            if (falta.TotalSeconds <= 0) return "renovando…".T();
            if (falta.TotalMinutes < 60) return "renova em {0} min".T(Math.Max(1, (int)Math.Ceiling(falta.TotalMinutes)));
            string hora = local.ToString("HH:mm", c);
            if (falta.TotalHours < 24) return "renova às {0}".T(hora);
            // Três letras do nome inteiro: o abreviado do es-ES é "ma.", e o que se quer é "mar"
            if (falta.TotalDays < 7) return "renova {0} {1}".T(c.DateTimeFormat.GetDayName(local.DayOfWeek).Substring(0, 3), hora);
            // Mês primeiro nos argumentos para o inglês poder trocar a ordem ("resets Oct 12") sem colidir com o formato de cima
            return "renova {1} {0}".T(c.DateTimeFormat.GetAbbreviatedMonthName(local.Month).TrimEnd('.'), local.Day);
        }

        // Para o menu da bandeja: "renova em 2h 15min"
        public static string RenovaEm(DateTime? utc)
        {
            if (!utc.HasValue) return null;
            var falta = utc.Value - DateTime.UtcNow;
            return falta.TotalSeconds <= 0 ? "renovando…".T() : "renova em {0}".T(Tempo.Duracao(falta));
        }

        // Projeção pelo ritmo atual: só diz algo útil se esgota antes de renovar
        public static string Ritmo(double? porHora, Janela j)
        {
            if (!porHora.HasValue || porHora.Value < 0.002 || j == null) return null;
            string ritmo = "Ritmo: +{0} pts/h".T(Math.Max(1, (int)Math.Round(porHora.Value * 100)));
            double falta = 1 - j.Atual;
            if (falta <= 0) return ritmo;
            if (j.ResetaEm.HasValue && DateTime.UtcNow.AddHours(falta / porHora.Value) >= j.ResetaEm.Value) return "{0} · não esgota antes de renovar".T(ritmo);
            string esgota = Esgota(porHora, j);
            return esgota != null ? ritmo + " · " + esgota : ritmo;
        }

        // "esgota por volta de 16:40": só quando esgota antes de renovar e nas próximas 48h
        public static string Esgota(double? porHora, Janela j)
        {
            if (!porHora.HasValue || porHora.Value < 0.002 || j == null) return null;
            double falta = 1 - j.Atual;
            if (falta <= 0) return null;
            var esgota = DateTime.UtcNow.AddHours(falta / porHora.Value);
            if (j.ResetaEm.HasValue && esgota >= j.ResetaEm.Value) return null;
            if ((esgota - DateTime.UtcNow).TotalHours > 48) return null;
            return "esgota por volta de {0}".T(esgota.ToLocalTime().ToString("HH:mm", Idioma.Cultura));
        }

        public static string Frescor(Provedor p)
        {
            if (!p.Confirmado.HasValue) return null;
            string s = "Exato {0}".T(Tempo.Ha(p.Confirmado.Value)) + (p.Fonte != null ? " · " + p.Fonte.T() : "");
            foreach (var j in p.Janelas) if (j.Estimado.HasValue) return s + " · " + "estimativa ao vivo".T();
            return s;
        }

        public static string Estado(Atividade a)
        {
            switch (a)
            {
                case Atividade.Trabalhando: return "trabalhando".T();
                case Atividade.Aguardando: return "aguardando você".T();
                case Atividade.Concluida: return "concluída".T();
                default: return "ociosa".T();
            }
        }

        public static string Plural(int n, string um, string varios) { return n + " " + (n == 1 ? um : varios); }
    }
}
