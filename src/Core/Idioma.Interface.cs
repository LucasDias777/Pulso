namespace Pulso
{
    // Traduções da cápsula, dos cartões, dos avisos e da bandeja: { português (como está no código), inglês, espanhol }
    static partial class Idioma
    {
        static readonly string[,] tInterface =
        {
            // Cartão de consumo (Texto, Tempo, Ritmo)
            { "{0}% usado · {1}% restante", "{0}% used · {1}% left", "{0}% usado · {1}% libre" },
            { "{0} usado", "{0} used", "{0} usado" },
            { "renovando…", "resetting…", "renovando…" },
            { "renova em {0} min", "resets in {0} min", "renueva en {0} min" },
            { "renova em {0}", "resets in {0}", "renueva en {0}" },
            { "renova às {0}", "resets at {0}", "renueva a las {0}" },
            { "renova {0} {1}", "resets {0} {1}", "renueva {0} {1}" },          // dia da semana, hora
            { "renova {1} {0}", "resets {0} {1}", "renueva {1} {0}" },          // mês, dia
            { "Ritmo: +{0} pts/h", "Pace: +{0} pts/h", "Ritmo: +{0} pts/h" },
            { "{0} · não esgota antes de renovar", "{0} · won't run out before reset", "{0} · no se agota antes de renovar" },
            { "esgota por volta de {0}", "runs out ~{0}", "se agota ~{0}" },
            { "Exato {0}", "Exact {0}", "Exacto {0}" },
            { "estimativa ao vivo", "live estimate", "estimación en vivo" },
            { "agora", "now", "ahora" },
            { "há {0}", "{0} ago", "hace {0}" },
            { "{0} dia", "{0} day", "{0} día" },
            { "{0} dias", "{0} days", "{0} días" },
            { "{0}% acima do ritmo", "{0}% over pace", "{0}% por encima del ritmo" },
            { "{0}% de folga", "{0}% headroom", "{0}% de margen" },
            { "Ritmo do dia ({0}º de 7)", "Daily pace (day {0} of 7)", "Ritmo del día ({0}.º de 7)" },
            { "Use o Codex uma vez para aparecer o consumo.", "Use Codex once for your usage to show up.", "Usa Codex una vez para que aparezca el consumo." },
            { "Aguardando a primeira leitura…", "Waiting for the first reading…", "Esperando la primera lectura…" },
            { "verificando…", "checking…", "verificando…" },
            { "nenhuma requisição hoje", "no requests today", "ninguna solicitud hoy" },
            { "~{0} requisição hoje", "~{0} request today", "~{0} solicitud hoy" },
            { "~{0} requisições hoje", "~{0} requests today", "~{0} solicitudes hoy" },
            { "Por projeto nesta sessão", "By project this session", "Por proyecto en esta sesión" },
            { "Outros", "Others", "Otros" },
            { "Sem pasta", "No folder", "Sin carpeta" },

            // Sessões
            { "trabalhando", "working", "trabajando" },
            { "aguardando você", "waiting for you", "esperándote" },
            { "concluída", "done", "terminada" },
            { "ociosa", "idle", "inactiva" },
            { "sessão", "session", "sesión" },
            { "e mais {0}", "and {0} more", "y {0} más" },

            // Cartões de aviso
            { "Limite do {0} atingido", "{0} limit reached", "{0} llegó al límite" },
            { "{0} em {1}%", "{0} at {1}%", "{0} al {1}%" },
            { "Sem cota até renovar", "No quota until reset", "Sin cuota hasta renovar" },
            { "{0}% restante", "{0}% left", "{0}% libre" },
            { "{0} renovado", "{0} reset", "{0} renovado" },
            { "{0} renovada", "{0} reset", "{0} renovada" },
            { "Sessão (5h)", "Session (5h)", "Sesión (5h)" },
            { "Cota disponível · {0}%", "Quota available · {0}%", "Cuota disponible · {0}%" },
            { "{0} em ritmo alto", "{0} at a high pace", "{0} a ritmo alto" },
            { "{0} · {1} usado", "{0} · {1} used", "{0} · {1} usado" },
            { "No ritmo atual, acaba às {0}", "At this pace, runs out at {0}", "A este ritmo, se agota a las {0}" },
            { "{0} antes de renovar · +{1} pts/h", "{0} before reset · +{1} pts/h", "{0} antes de renovar · +{1} pts/h" },
            { "{0} terminou", "{0} finished", "{0} terminó" },
            { "Concluída em {0}", "Done in {0}", "Terminada en {0}" },
            { "{0} está esperando você", "{0} is waiting", "{0} te espera" },
            { "Precisa da sua resposta", "Needs your reply", "Necesita tu respuesta" },
            { "Clique para abrir a janela", "Click to open the window", "Haz clic para abrir la ventana" },

            // Bandeja e menu do clique direito
            { "{0} — sem leitura", "{0} — no reading", "{0} — sin lectura" },
            { "Clique para atualizar", "Click to refresh", "Haz clic para actualizar" },
            { "Mostrar a cápsula", "Show the capsule", "Mostrar la cápsula" },
            { "Ocultar a cápsula", "Hide the capsule", "Ocultar la cápsula" },
            { "Atualizar tudo", "Refresh all", "Actualizar todo" },
            { "Atualizar agora", "Refresh now", "Actualizar ahora" },
            { "Abrir a página de uso — {0}", "Open the usage page — {0}", "Abrir la página de uso — {0}" },
            { "Manter aberto", "Keep open", "Mantener abierto" },
            { "Mover para a borda", "Move to edge", "Mover al borde" },
            { "Direita", "Right", "Derecha" },
            { "Esquerda", "Left", "Izquierda" },
            { "Em cima", "Top", "Arriba" },
            { "Embaixo", "Bottom", "Abajo" },
            { "Centralizar", "Center", "Centrar" },
            { "Atualizar o Pulso (versão nova)", "Update Pulso (new version)", "Actualizar Pulso (versión nueva)" },
            { "Versão nova do Pulso", "New version of Pulso", "Nueva versión de Pulso" },
            { "Há uma atualização disponível. Clique para ver e instalar.", "An update is available. Click to see and install it.", "Hay una actualización disponible. Haz clic para verla e instalarla." },
            { "Configurações…", "Settings…", "Configuración…" },
            { "Sair do Pulso", "Quit Pulso", "Salir de Pulso" },

            // Atalho
            { "Espaço", "Space", "Espacio" },
        };
    }
}
