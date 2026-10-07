using System.Collections.Generic;
using System.Linq;

namespace Pulso
{
    class InfoProvedor
    {
        public string Id, Nome, UrlUso;
    }

    // Os provedores que o Pulso conhece, na ordem da aba Contas
    static class Catalogo
    {
        public static readonly InfoProvedor[] Todos =
        {
            new InfoProvedor { Id = "claude", Nome = "Claude", UrlUso = "https://claude.ai/settings/usage" },
            new InfoProvedor { Id = "codex", Nome = "Codex", UrlUso = "https://chatgpt.com/codex/settings/usage" },
            new InfoProvedor { Id = "glm", Nome = "z.ai", UrlUso = "https://z.ai/manage-apikey/subscription" },
            new InfoProvedor { Id = "opencode", Nome = "OpenCode", UrlUso = "https://opencode.ai" },
            new InfoProvedor { Id = "cursor", Nome = "Cursor", UrlUso = "https://cursor.com/dashboard?tab=usage" },
            new InfoProvedor { Id = "grok", Nome = "Grok", UrlUso = "https://grok.com" },
            new InfoProvedor { Id = "copilot", Nome = "GitHub Copilot", UrlUso = "https://github.com/settings/copilot" },
            new InfoProvedor { Id = "gemini", Nome = "Antigravity", UrlUso = "https://antigravity.google" },
        };

        public static InfoProvedor Info(string id) { return Todos.FirstOrDefault(i => i.Id == id); }

        public static bool Existe(string id) { return Info(id) != null; }

        public static IEnumerable<string> Ids { get { return Todos.Select(i => i.Id); } }
    }
}
