namespace Pulso
{
    // Traduções de instalação, atualização, integrações e mensagens do sistema: { português (como está no código), inglês, espanhol }
    static partial class Idioma
    {
        static readonly string[,] tSistema =
        {
            { "Pulso", "Pulso", "Pulso" },

            // Instalação e desinstalação
            { "Não foi possível concluir a instalação do Pulso:\n\n{0}", "Couldn't finish installing Pulso:\n\n{0}", "No se pudo completar la instalación de Pulso:\n\n{0}" },
            { "Consumo do Claude Code e do Codex ao vivo", "Live Claude Code and Codex usage", "Consumo de Claude Code y Codex en vivo" },
            { "Desinstalar o Pulso deste computador?\n\nA cápsula, o ícone da bandeja e o início com o Windows serão removidos.",
              "Uninstall Pulso from this computer?\n\nThe capsule, the system tray icon and starting with Windows will be removed.",
              "¿Desinstalar Pulso de este equipo?\n\nSe quitarán la cápsula, el icono de la bandeja del sistema y el inicio con Windows." },
            { "Desinstalar o Pulso", "Uninstall Pulso", "Desinstalar Pulso" },
            { "Apagar também as suas configurações e o histórico do Pulso?\n\n{0}\n\nEscolha Não para mantê-los, caso pretenda instalar de novo.",
              "Also delete your Pulso settings and history?\n\n{0}\n\nChoose No to keep them if you plan to install it again.",
              "¿Borrar también tu configuración y el historial de Pulso?\n\n{0}\n\nElige No para conservarlos si piensas volver a instalarlo." },
            { "O Pulso foi desinstalado.", "Pulso has been uninstalled.", "Pulso se ha desinstalado." },

            // Procura de atualização (Configurações)
            { "1 mudança nova", "1 new change", "1 cambio nuevo" },
            { "{0} mudanças novas", "{0} new changes", "{0} cambios nuevos" },
            { "Não foi possível consultar o GitHub.", "Couldn't check GitHub.", "No se pudo consultar GitHub." },
            { "O GitHub não liberou o acesso ao repositório. Clique em Procurar atualização para entrar com a sua conta.",
              "GitHub didn't grant access to the repository. Click Check for updates to sign in with your account.",
              "GitHub no dio acceso al repositorio. Haz clic en Buscar actualizaciones para iniciar sesión con tu cuenta." },
            { "Sem conexão com o GitHub.", "No connection to GitHub.", "Sin conexión con GitHub." },
            { "O GitHub demorou demais para responder.", "GitHub took too long to respond.", "GitHub tardó demasiado en responder." },
            { "Git não encontrado neste computador.", "Git wasn't found on this computer.", "No se encontró Git en este equipo." },

            // Barra de status do Claude Code
            { "Já existe uma barra de status configurada ({0}). Nada foi alterado.", "A status line is already configured ({0}). Nothing was changed.", "Ya hay una barra de estado configurada ({0}). No se cambió nada." },
            { "O settings.json do Claude Code não é um JSON válido. Nada foi alterado.", "Claude Code's settings.json isn't valid JSON. Nothing was changed.", "El settings.json de Claude Code no es un JSON válido. No se cambió nada." },
            { "Formato inesperado no settings.json. Nada foi alterado.", "Unexpected format in settings.json. Nothing was changed.", "Formato inesperado en settings.json. No se cambió nada." },
            { "Não consegui montar um JSON válido. Nada foi alterado.", "Couldn't build valid JSON. Nothing was changed.", "No se pudo generar un JSON válido. No se cambió nada." },
            { "Não consegui remover sem quebrar o JSON. Nada foi alterado.", "Couldn't remove it without breaking the JSON. Nothing was changed.", "No se pudo quitar sin romper el JSON. No se cambió nada." },
            { "semana {0}%", "week {0}%", "semana {0}%" },

            // Estado.*.Ausencia (App.cs): guardado em português, traduzido por quem mostra
            { "Claude Code não instalado", "Claude Code not installed", "Claude Code no instalado" },
            { "Codex não instalado", "Codex not installed", "Codex no instalado" },
        };
    }
}
