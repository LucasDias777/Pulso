using System.Collections.Generic;

namespace Pulso
{
    // Fábrica dos leitores dos provedores além de Claude e Codex
    static class Extras
    {
        public static Dictionary<string, FonteExtra> Criar()
        {
            return new Dictionary<string, FonteExtra>
            {
                { "glm", new Glm() }, { "opencode", new OpenCode() }, { "cursor", new Cursor() },
                { "grok", new Grok() }, { "copilot", new Copilot() }, { "gemini", new Antigravity() },
            };
        }
    }
}
