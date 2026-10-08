namespace Pulso
{
    // Traduções dos textos que vêm das leituras de cada provedor (janelas, erros, detalhes, notas):
    // { português (como está no código), inglês, espanhol }
    static partial class Idioma
    {
        static readonly string[,] tFontes =
        {
            // Janelas (Rotulo) e grupos (Grupo)
            { "Sessão (5h)", "Session (5h)", "Sesión (5h)" },
            { "Semana", "Week", "Semana" },
            { "Semana (Opus)", "Week (Opus)", "Semana (Opus)" },
            { "Semana (Sonnet)", "Week (Sonnet)", "Semana (Sonnet)" },
            { "Semana (por modelo)", "Week (per model)", "Semana (por modelo)" },
            { "Limite", "Limit", "Límite" },
            { "Uso", "Usage", "Uso" },
            { "Limite de 5 horas", "5-hour limit", "Límite de 5 horas" },
            { "Limite semanal", "Weekly limit", "Límite semanal" },
            { "Limite mensal", "Monthly limit", "Límite mensual" },
            { "Requisições hoje · sem limite publicado", "Requests today · no published limit", "Solicitudes hoy · sin límite publicado" },
            { "Requisições premium", "Premium requests", "Solicitudes premium" },
            { "Requisições de chat", "Chat requests", "Solicitudes de chat" },
            { "Uso incluído", "Included usage", "Uso incluido" },
            { "Uso de API", "API usage", "Uso de API" },
            { "Sob demanda", "On-demand", "Bajo demanda" },
            { "MCP (1 mês)", "MCP (1 month)", "MCP (1 mes)" },
            { "Modelos Gemini", "Gemini models", "Modelos Gemini" },
            { "Modelos Claude e GPT", "Claude and GPT models", "Modelos Claude y GPT" },

            // Origem da leitura (Fonte) e plano (Plano)
            { "servidor", "server", "servidor" },
            { "barra de status", "status line", "barra de estado" },
            { "servidor do Codex", "Codex server", "servidor de Codex" },
            { "sessão do Codex", "Codex session", "sesión de Codex" },
            { "registros do Antigravity", "Antigravity logs", "registros de Antigravity" },
            { "Pessoal", "Personal", "Personal" },

            // Por que o provedor não está conectado (Ausencia; o cartão põe a inicial em maiúscula)
            { "não instalado", "not installed", "no instalado" },
            { "não instalado (GitHub CLI)", "not installed (GitHub CLI)", "no instalado (GitHub CLI)" },
            { "não foi possível verificar", "couldn't check", "no se pudo verificar" },
            { "Claude Code não instalado", "Claude Code not installed", "Claude Code no instalado" },
            { "Codex não instalado", "Codex not installed", "Codex no instalado" },

            // Erros, notas e detalhes
            { "Entre no Claude Code para ver o consumo exato", "Sign in to Claude Code to see exact usage", "Inicia sesión en Claude Code para ver el consumo exacto" },
            { "Credencial vencida — o Claude Code a renova no próximo uso", "Credential expired — Claude Code renews it on next use", "Credencial vencida — Claude Code la renueva en el próximo uso" },
            { "Credencial recusada — entre de novo no Claude Code", "Credential rejected — sign in to Claude Code again", "Credencial rechazada — vuelve a iniciar sesión en Claude Code" },
            { "Resposta sem janelas de limite", "Response has no limit windows", "Respuesta sin ventanas de límite" },
            { "Credencial recusada — entre de novo na ferramenta", "Credential rejected — sign in to the tool again", "Credencial rechazada — vuelve a iniciar sesión en la herramienta" },
            { "O Antigravity está fechado — última leitura mantida", "Antigravity is closed — last reading kept", "Antigravity está cerrado — se mantiene la última lectura" },
            { "Abra o Antigravity para ler a cota", "Open Antigravity to read the quota", "Abre Antigravity para leer la cuota" },
            { "A sessão Google do Antigravity foi recusada — entre de novo no Antigravity", "Antigravity's Google session was rejected — sign in to Antigravity again", "La sesión de Google de Antigravity fue rechazada — vuelve a iniciar sesión en Antigravity" },
            { "Rode gh auth login — o Pulso usa a sessão do GitHub CLI", "Run gh auth login — Pulso uses the GitHub CLI session", "Ejecuta gh auth login — Pulso usa la sesión de GitHub CLI" },
            { "O GitHub Copilot não informou cotas nesta conta", "GitHub Copilot reported no quotas for this account", "GitHub Copilot no informó cuotas en esta cuenta" },
            { "O GitHub Copilot não mede cota nesta conta", "GitHub Copilot doesn't track quota for this account", "GitHub Copilot no mide cuota en esta cuenta" },
            { "Rode grok login para ver o consumo", "Run grok login to see usage", "Ejecuta grok login para ver el consumo" },
            { "O Grok não informou a cobrança", "Grok didn't report billing", "Grok no informó la facturación" },
            { "Nada medido nesta conta do Grok ainda", "Nothing measured on this Grok account yet", "Aún no hay nada medido en esta cuenta de Grok" },
            { "Não consegui ler a sessão do Cursor", "Couldn't read the Cursor session", "No pude leer la sesión de Cursor" },
            { "Entre no Cursor (o editor) para ver o consumo", "Sign in to Cursor (the editor) to see usage", "Inicia sesión en Cursor (el editor) para ver el consumo" },
            { "atual", "current", "actual" },
            { "Nenhuma chave do z.ai encontrada (ZCode, OpenCode ou glm.json na pasta do Pulso)", "No z.ai key found (ZCode, OpenCode or glm.json in the Pulso folder)", "No se encontró clave de z.ai (ZCode, OpenCode o glm.json en la carpeta de Pulso)" },
            { "O z.ai recusou a chave — renove-a na ferramenta que a guarda", "z.ai rejected the key — renew it in the tool that stores it", "z.ai rechazó la clave — renuévala en la herramienta que la guarda" },
            { "O plano não informou janelas de uso", "The plan reported no usage windows", "El plan no informó ventanas de uso" },
            { "Rode opencode auth login para entrar no OpenCode", "Run opencode auth login to sign in to OpenCode", "Ejecuta opencode auth login para iniciar sesión en OpenCode" },
            { "O login do OpenCode expirou — abra o OpenCode para renovar", "OpenCode sign-in expired — open OpenCode to renew it", "La sesión de OpenCode expiró — abre OpenCode para renovarla" },
            { "Sem assinatura Go nesta conta, ou o login foi recusado — rode opencode auth login", "No Go subscription on this account, or sign-in was rejected — run opencode auth login", "Sin suscripción Go en esta cuenta, o se rechazó el inicio de sesión — ejecuta opencode auth login" },
            { "A chave opencode-go foi recusada ou não tem plano Go", "The opencode-go key was rejected or has no Go plan", "La clave opencode-go fue rechazada o no tiene plan Go" },
            { "O plano Go não informou janelas de uso", "The Go plan reported no usage windows", "El plan Go no informó ventanas de uso" },

            // Formatos (partes variáveis em {0}; traduzidos na leitura)
            { "Semana ({0})", "Week ({0})", "Semana ({0})" },
            { "{0} dias", "{0} days", "{0} días" },
            { "Uso ({0} h)", "Usage ({0} h)", "Uso ({0} h)" },
            { "Uso ({0} sem)", "Usage ({0} wk)", "Uso ({0} sem)" },
            { "Créditos: {0}", "Credits: {0}", "Créditos: {0}" },
            { "Sem resposta do servidor ({0})", "No response from server ({0})", "Sin respuesta del servidor ({0})" },
            { "Servidor respondeu {0}", "Server responded {0}", "El servidor respondió {0}" },
            { "Servidor pediu pausa; leitura exata em {0}", "Server asked for a pause; exact reading in {0}", "El servidor pidió una pausa; lectura exacta en {0}" },
            { "Servidor pediu pausa; nova leitura em {0}", "Server asked for a pause; next reading in {0}", "El servidor pidió una pausa; nueva lectura en {0}" },
            { "{0} · o Google não publica cota para esta conta", "{0} · Google doesn't publish a quota for this account", "{0} · Google no publica cuota para esta cuenta" },
            { "Ilimitado no plano {0} — nada para medir", "Unlimited on the {0} plan — nothing to measure", "Ilimitado en el plan {0} — nada que medir" },
            { "O plano {0} ainda não tem nada para medir", "The {0} plan has nothing to measure yet", "El plan {0} aún no tiene nada que medir" },
            { "O monitor do z.ai recusou o pedido ({0})", "The z.ai monitor rejected the request ({0})", "El monitor de z.ai rechazó la solicitud ({0})" },
        };
    }
}
