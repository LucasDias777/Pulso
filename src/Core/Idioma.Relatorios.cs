namespace Pulso
{
    // Traduções da aba Gráficos e do calendário dos Relatórios: { português (como está no código), inglês, espanhol }
    static partial class Idioma
    {
        static readonly string[,] tRelatorios =
        {
            // abas e filtros
            { "Visão geral", "Overview", "Vista general" },
            { "Gráficos", "Charts", "Gráficos" },
            { "Agrupar por", "Group by", "Agrupar por" },

            // calendário
            { "Escolha um dia.", "Pick a day.", "Elige un día." },
            { "Escolha uma semana.", "Pick a week.", "Elige una semana." },
            { "Escolha um mês.", "Pick a month.", "Elige un mes." },
            { "Menos", "Less", "Menos" },
            { "Mais", "More", "Más" },

            // destaques
            { "Destaques", "Highlights", "Destacados" },
            { "Média por dia com uso", "Average per active day", "Promedio por día con uso" },
            { "Dia de maior uso", "Busiest day", "Día de mayor uso" },
            { "Hora de pico", "Peak hour", "Hora pico" },
            { "Dias com uso", "Days with usage", "Días con uso" },
            { "{0} de {1}", "{0} of {1}", "{0} de {1}" },
            { "Lidos do cache", "Read from cache", "Leídos de la caché" },
            { "Contra ontem", "Vs. yesterday", "Frente a ayer" },
            { "Contra o dia anterior", "Vs. the day before", "Frente al día anterior" },
            { "Contra a semana passada", "Vs. last week", "Frente a la semana pasada" },
            { "Contra a semana anterior", "Vs. the week before", "Frente a la semana anterior" },
            { "Contra o mês passado", "Vs. last month", "Frente al mes pasado" },
            { "Contra o mês anterior", "Vs. the month before", "Frente al mes anterior" },
            { "No período em curso, a comparação vai até o mesmo ponto do anterior.",
              "For the current period, the comparison stops at the same point of the previous one.",
              "En el período en curso, la comparación llega hasta el mismo punto del anterior." },

            // participação e uso
            { "Participação", "Share", "Participación" },
            { "tokens", "tokens", "tokens" },
            { "{0}: 100% dos tokens deste período.", "{0}: 100% of this period's tokens.", "{0}: 100% de los tokens de este período." },
            { "Contra o período anterior", "Vs. the previous period", "Frente al período anterior" },
            { "novo", "new", "nuevo" },
            { "Colorida = hoje até agora · cinza = ontem inteiro.", "Colored = today so far · gray = all of yesterday.", "Color = hoy hasta ahora · gris = ayer completo." },
            { "Colorida = {0} · cinza = {1}, até o mesmo ponto.", "Colored = {0} · gray = {1}, up to the same point.", "Color = {0} · gris = {1}, hasta el mismo punto." },
            { "Colorida = {0} · cinza = {1}.", "Colored = {0} · gray = {1}.", "Color = {0} · gris = {1}." },

            // progressão
            { "Progressão", "Progress", "Progresión" },
            { "Média", "Average", "Promedio" },
            { "Média dos 7 dias anteriores", "Average of the previous 7 days", "Promedio de los 7 días anteriores" },
            { "Média das 4 semanas anteriores", "Average of the previous 4 weeks", "Promedio de las 4 semanas anteriores" },
            { "Média dos 3 meses anteriores", "Average of the previous 3 months", "Promedio de los 3 meses anteriores" },
            { "Até as {0}", "Up to {0}", "Hasta las {0}" },
            { "Até {0}", "Through {0}", "Hasta el {0}" },
            { "Até o dia {0}", "Through day {0}", "Hasta el día {0}" },
            { "Tokens acumulados ao longo do dia, contra o dia anterior e a média dos 7 dias antes dele.",
              "Tokens accumulated through the day, against the day before and the average of the 7 days before it.",
              "Tokens acumulados a lo largo del día, frente al día anterior y al promedio de los 7 días previos." },
            { "Tokens acumulados ao longo da semana, contra a semana anterior e a média das 4 anteriores.",
              "Tokens accumulated through the week, against the week before and the average of the previous 4.",
              "Tokens acumulados a lo largo de la semana, frente a la semana anterior y al promedio de las 4 anteriores." },
            { "Tokens acumulados ao longo do mês, contra o mês anterior e a média dos 3 anteriores.",
              "Tokens accumulated through the month, against the month before and the average of the previous 3.",
              "Tokens acumulados a lo largo del mes, frente al mes anterior y al promedio de los 3 anteriores." },

            // semanas do mês e mapa
            { "Semanas do mês", "Weeks of the month", "Semanas del mes" },
            { "Tokens de cada dia, uma linha por semana do mês (segunda a domingo).", "Tokens per day, one line per week of the month (Monday to Sunday).", "Tokens de cada día, una línea por semana del mes (lunes a domingo)." },
            { "Semana de maior uso: {0}, {1} tokens", "Busiest week: {0}, {1} tokens", "Semana de mayor uso: {0}, {1} tokens" },
            { "Quando você usa", "When you use it", "Cuándo lo usas" },
            { "Tokens de cada hora, somados por dia da semana no período.", "Tokens per hour, added up by weekday over the period.", "Tokens de cada hora, sumados por día de la semana en el período." },
            { "Tokens de cada hora, somados por dia da semana. Os dias de antes do registro por hora ficam fora do mapa.",
              "Tokens per hour, added up by weekday. Days from before the hourly record are left out of the map.",
              "Tokens de cada hora, sumados por día de la semana. Los días anteriores al registro por hora quedan fuera del mapa." },
            { "em {0} dias", "over {0} days", "en {0} días" },

            // tendência e dia a dia
            { "Tendência", "Trend", "Tendencia" },
            { "Últimos 14 dias", "Last 14 days", "Últimos 14 días" },
            { "Últimas 12 semanas", "Last 12 weeks", "Últimas 12 semanas" },
            { "Últimos 12 meses", "Last 12 months", "Últimos 12 meses" },
            { "{0} até o período escolhido. Clique numa coluna para abrir o período.", "{0} up to the chosen period. Click a column to open that period.", "{0} hasta el período elegido. Haz clic en una columna para abrir ese período." },
            { "Dia a dia", "Day by day", "Día a día" },
            { "Clique num dia para abrir.", "Click a day to open it.", "Haz clic en un día para abrirlo." },
            // exportação dos gráficos
            { "Agrupado por {0}", "Grouped by {0}", "Agrupado por {0}" },
            { "Valor", "Value", "Valor" },
            { "Variação", "Change", "Variación" },
            { "Modelo principal", "Main model", "Modelo principal" },
            { "Projeto principal", "Main project", "Proyecto principal" },
            { "1 resposta", "1 response", "1 respuesta" },
            { "{0} respostas", "{0} responses", "{0} respuestas" },
        };
    }
}
