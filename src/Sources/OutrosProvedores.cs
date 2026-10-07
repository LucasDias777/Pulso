using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Pulso
{
    // Leitores dos demais provedores (Copilot, Grok, Cursor, z.ai, OpenCode).
    // Credenciais só lidas, em memória, na hora da consulta.

    static class Util
    {
        public static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }

        // Executável no PATH (só .exe, nunca .cmd)
        public static string NoPath(string exe)
        {
            foreach (var d in (Env("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(d) || !Path.IsPathRooted(d)) continue;
                try { var f = Path.Combine(d.Trim(), exe); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        public static string Rodar(string exe, string args, int ms)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            using (var p = Process.Start(psi))
            {
                p.StandardInput.Close();
                var saida = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 ? saida.Result.Trim() : null;
            }
        }

        public static string Maiuscula(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        public static Janela J(string id, string rotulo, double fracao, DateTime? reset, int minutos = 0, bool semanal = false)
        {
            return new Janela { Id = id, Rotulo = rotulo, Usado = Math.Max(0, Math.Min(1, fracao)), ResetaEm = reset, Minutos = minutos, Semanal = semanal };
        }
    }

    // ---------------- GitHub Copilot ----------------
    class Copilot : FonteExtra
    {
        string token; DateTime tokenAte;
        public Copilot() : base("copilot") { }

        static IEnumerable<string> Hosts()
        {
            var l = new List<string>();
            if (Util.Env("GH_CONFIG_DIR") != null) l.Add(Path.Combine(Util.Env("GH_CONFIG_DIR"), "hosts.yml"));
            if (Util.Env("XDG_CONFIG_HOME") != null) l.Add(Path.Combine(Util.Env("XDG_CONFIG_HOME"), "gh", "hosts.yml"));
            l.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHub CLI", "hosts.yml"));
            l.Add(Path.Combine(Caminhos.Home, ".config", "gh", "hosts.yml"));
            return l.Where(File.Exists);
        }

        static string Gh()
        {
            string[] c =
            {
                Path.Combine(Util.Env("ProgramFiles") ?? "", "GitHub CLI", "gh.exe"),
                Path.Combine(Util.Env("ProgramFiles(x86)") ?? "", "GitHub CLI", "gh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "gh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "gh.exe"),
                Path.Combine(Caminhos.Home, "scoop", "shims", "gh.exe"),
            };
            return c.FirstOrDefault(File.Exists) ?? Util.NoPath("gh.exe");
        }

        protected override string Detectar()
        {
            if (Util.Env("GH_TOKEN") != null || Util.Env("GITHUB_TOKEN") != null || Hosts().Any() || Gh() != null) return null;
            return "não instalado (GitHub CLI)";
        }

        // Bloco github.com: do hosts.yml → user e oauth_token
        static void LerHosts(out string usuario, out string tok)
        {
            usuario = tok = null;
            foreach (var f in Hosts())
            {
                bool dentro = false;
                foreach (var l in File.ReadAllLines(f))
                {
                    if (l.Trim() == "github.com:") { dentro = true; continue; }
                    if (dentro && l.Length > 0 && l[0] != ' ' && l[0] != '\t') break;
                    if (!dentro) continue;
                    string t = l.Trim();
                    if (usuario == null && t.StartsWith("user:")) usuario = t.Substring(5).Trim().Trim('"', '\'');
                    if (tok == null && t.StartsWith("oauth_token:")) tok = t.Substring(12).Trim().Trim('"', '\'');
                }
                if (usuario != null || tok != null) return;
            }
        }

        protected override Leitura Consultar()
        {
            string usuario, doArquivo;
            LerHosts(out usuario, out doArquivo);
            string fonte = "GitHub";
            string tok = Util.Env("GH_TOKEN") ?? Util.Env("GITHUB_TOKEN");
            if (tok == null && doArquivo != null) { tok = doArquivo; fonte = "GitHub CLI"; }
            if (tok == null)
            {
                // O gh guarda o token no cofre do Windows: pede a ele (cache de 30 min para não abrir o gh a cada ciclo)
                if (token == null || DateTime.UtcNow > tokenAte)
                {
                    string gh = Gh();
                    token = gh != null ? Util.Rodar(gh, "auth token --hostname github.com", 10000) : null;
                    tokenAte = DateTime.UtcNow.AddMinutes(30);
                }
                tok = token; fonte = "GitHub CLI";
            }
            if (string.IsNullOrEmpty(tok)) throw new SemLeitura("Rode gh auth login — o Pulso usa a sessão do GitHub CLI", TimeSpan.FromMinutes(10), true);

            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            string corpo;
            try { corpo = Enviar(req); }
            catch (SemLeitura e) { if (e.Credencial) token = null; throw; }
            var o = Json.Parse(corpo);
            var snaps = Json.Obj(o, "quota_snapshots");
            var l = new Leitura { Fonte = fonte };
            l.Plano = Util.Maiuscula(Json.Str(o, "copilot_plan") ?? Json.Str(o, "plan"));
            if (snaps == null) { l.Detalhe = "O GitHub Copilot não informou cotas nesta conta"; return l; }
            var ordem = new[] { "premium_interactions", "chat", "completions" };
            foreach (var k in ordem.Where(snaps.ContainsKey).Concat(snaps.Keys.Where(k => !ordem.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)))
            {
                var q = Json.Obj(snaps, k);
                if (q == null || Json.Bool(q, "unlimited") == true) continue;
                double? ent = Json.Num(q, "entitlement");
                if (!ent.HasValue || ent <= 0) continue;
                double usado = Json.Num(q, "used") ?? Math.Max(ent.Value - (Json.Num(q, "remaining") ?? ent.Value), 0);
                var reset = Json.Data(q, "reset_date") ?? Json.Data(q, "reset_at") ?? Json.Data(q, "resets_at") ?? Json.Data(o, "quota_reset_date");
                string rot = k == "premium_interactions" ? "Requisições premium" : k == "chat" ? "Requisições de chat" : k == "completions" ? "Completions"
                    : string.Join(" ", k.Split('_').Select(Util.Maiuscula));
                l.Janelas.Add(Util.J(k, rot, usado / ent.Value, reset));
            }
            if (l.Janelas.Count == 0) l.Detalhe = "O GitHub Copilot não mede cota nesta conta";
            else if (usuario != null) l.Detalhe = usuario;
            return l;
        }
    }

    // ---------------- Grok ----------------
    class Grok : FonteExtra
    {
        public Grok() : base("grok") { }
        static string Auth { get { return Path.Combine(Caminhos.Home, ".grok", "auth.json"); } }

        protected override string Detectar() { return File.Exists(Auth) ? null : "não instalado"; }

        protected override Leitura Consultar()
        {
            var o = Json.Obj(Json.Parse(Caminhos.LerCompartilhado(Auth)));
            string chave = null, email = null;
            if (o != null)
            {
                // Só entradas do emissor oficial; a primeira ainda válida (ordem alfabética das chaves)
                var confiaveis = o.Keys.OrderBy(k => k, StringComparer.Ordinal)
                    .Where(k => k.Split(new[] { "::" }, StringSplitOptions.None)[0] == "https://auth.x.ai" || Json.Str(o[k], "oidc_issuer") == "https://auth.x.ai").ToList();
                var escolhida = confiaveis.FirstOrDefault(k => { var e = Json.Data(o[k], "expires_at"); return !e.HasValue || e.Value > DateTime.UtcNow; }) ?? confiaveis.FirstOrDefault();
                if (escolhida != null) { chave = Json.Str(o[escolhida], "key"); email = Json.Str(o[escolhida], "email"); }
            }
            if (string.IsNullOrEmpty(chave)) throw new SemLeitura("Rode grok login para ver o consumo", TimeSpan.FromMinutes(10), true);
            var req = new HttpRequestMessage(HttpMethod.Get, "https://cli-chat-proxy.grok.com/v1/billing?format=credits");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chave);
            req.Headers.Add("X-XAI-Token-Auth", "xai-grok-cli");
            req.Headers.Accept.ParseAdd("application/json");
            var r = Json.Parse(Enviar(req));
            var cfg = Json.Obj(r, "config");
            var l = new Leitura { Fonte = "Grok CLI", Detalhe = email };
            if (cfg == null) { l.Detalhe = "O Grok não informou a cobrança"; return l; }
            var reset = Json.Data(cfg, "currentPeriod", "end") ?? Json.Data(cfg, "billingPeriodEnd");
            var produtos = Json.Arr(cfg, "productUsage") ?? new object[0];
            double? pct = Json.Num(cfg, "creditUsagePercent");
            if (pct.HasValue)
            {
                string prod = produtos.Length > 0 ? Json.Str(produtos[0], "product") : null;
                l.Janelas.Add(Util.J("credits", prod != null ? System.Text.RegularExpressions.Regex.Replace(prod, "(?<=[a-z])([A-Z])", " $1") : "Grok Build", pct.Value / 100, reset));
            }
            else
                foreach (var p in produtos)
                {
                    double? u = Json.Num(p, "usagePercent");
                    if (!u.HasValue) continue;
                    l.Janelas.Add(Util.J(l.Janelas.Count == 0 ? "credits" : Json.Str(p, "product") ?? "produto", Json.Str(p, "product") ?? "Uso", u.Value / 100, reset));
                }
            if (l.Janelas.Count == 0 && (Json.Str(cfg, "currentPeriod", "type") ?? "").ToUpperInvariant().Contains("WEEKLY"))
                l.Janelas.Add(Util.J("credits", "Limite semanal", 0, reset, 10080, true));
            if (l.Janelas.Count == 0) l.Detalhe = "Nada medido nesta conta do Grok ainda";
            return l;
        }
    }

    // ---------------- Cursor ----------------
    class Cursor : FonteExtra
    {
        public Cursor() : base("cursor") { }
        static string Db { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb"); } }

        protected override string Detectar() { return File.Exists(Db) ? null : "não instalado"; }

        protected override Leitura Consultar()
        {
            var linhas = Sqlite.Consultar(Db, "SELECT 1 FROM ItemTable LIMIT 1",
                "SELECT key, value FROM ItemTable WHERE key IN ('cursorAuth/accessToken','cursorAuth/stripeMembershipAuthId','cursorAuth/stripeMembershipType')");
            if (linhas == null) throw new SemLeitura("Não consegui ler a sessão do Cursor");
            var v = linhas.Where(r => r[0] != null).ToDictionary(r => r[0], r => r[1]);
            string acesso, id, tipo;
            v.TryGetValue("cursorAuth/accessToken", out acesso);
            v.TryGetValue("cursorAuth/stripeMembershipAuthId", out id);
            v.TryGetValue("cursorAuth/stripeMembershipType", out tipo);
            if (string.IsNullOrEmpty(acesso) || string.IsNullOrEmpty(id)) throw new SemLeitura("Entre no Cursor (o editor) para ver o consumo", TimeSpan.FromMinutes(10), true);
            var req = new HttpRequestMessage(HttpMethod.Get, "https://cursor.com/api/usage-summary");
            req.Headers.Add("Cookie", "WorkosCursorSessionToken=" + id + "::" + acesso);
            req.Headers.Accept.ParseAdd("application/json");
            var o = Json.Parse(Enviar(req));
            var reset = Json.Data(o, "billingCycleEnd");
            string plano = Util.Maiuscula(Json.Str(o, "membershipType") ?? tipo);
            var l = new Leitura { Fonte = "Cursor", Plano = plano };
            double? inc = Json.Num(o, "individualUsage", "plan", "totalPercentUsed");
            if (inc.HasValue) l.Janelas.Add(Util.J("included", "Uso incluído", inc.Value / 100, reset));
            double? api = Json.Num(o, "individualUsage", "plan", "apiPercentUsed");
            if (api.HasValue && api > 0) l.Janelas.Add(Util.J("api", "Uso de API", api.Value / 100, reset));
            var od = Json.Obj(o, "individualUsage", "onDemand");
            double? lim = Json.Num(od, "limit"), us = Json.Num(od, "used");
            if (Json.Bool(od, "enabled") == true && lim > 0 && us.HasValue) l.Janelas.Add(Util.J("on_demand", "Sob demanda", us.Value / lim.Value, reset));
            if (l.Janelas.Count == 0)
                l.Detalhe = Json.Bool(o, "isUnlimited") == true ? "Ilimitado no plano " + (plano ?? "atual") + " — nada para medir" : "O plano " + (plano ?? "atual") + " ainda não tem nada para medir";
            return l;
        }
    }

    // ---------------- z.ai (GLM) ----------------
    class Glm : FonteExtra
    {
        public Glm() : base("glm") { }

        static string GlmJson { get { return Path.Combine(Caminhos.Dados, "glm.json"); } }
        static string ZcodeCfg { get { return Path.Combine(Caminhos.Home, ".zcode", "v2", "config.json"); } }
        static string ZcodeCred { get { return Path.Combine(Caminhos.Home, ".zcode", "v2", "credentials.json"); } }
        static IEnumerable<string> OpenCodeAuth()
        {
            return new[] { Path.Combine(Caminhos.Home, ".local", "share", "opencode", "auth.json"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json") }.Where(File.Exists);
        }

        static bool HostZai(string url)
        {
            Uri u;
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "api.z.ai" || h.EndsWith(".z.ai") || h == "open.bigmodel.cn" || h.EndsWith(".bigmodel.cn");
        }

        static string Console(string url)
        {
            Uri u;
            return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Host.ToLowerInvariant().EndsWith("bigmodel.cn") ? "https://open.bigmodel.cn" : "https://api.z.ai";
        }

        protected override string Detectar()
        {
            if (File.Exists(GlmJson) || File.Exists(ZcodeCfg) || File.Exists(ZcodeCred) || OpenCodeAuth().Any()) return null;
            var s = Json.Parse(File.Exists(Caminhos.ClaudeSettings) ? File.ReadAllText(Caminhos.ClaudeSettings) : null);
            return HostZai(Json.Str(s, "env", "ANTHROPIC_BASE_URL")) ? null : "não instalado";
        }

        // Chave crua + console, por ordem de preferência das fontes
        static bool Chave(out string chave, out string console, out string fonte)
        {
            chave = console = fonte = null;
            if (File.Exists(GlmJson))
            {
                var o = Json.Parse(File.ReadAllText(GlmJson));
                chave = Json.Str(o, "api_key") ?? Json.Str(o, "apiKey");
                if (chave != null) { console = Console(Json.Str(o, "base_url") ?? Json.Str(o, "baseURL")); fonte = "glm.json"; return true; }
            }
            if (File.Exists(Caminhos.ClaudeSettings))
            {
                var s = Json.Parse(File.ReadAllText(Caminhos.ClaudeSettings));
                string url = Json.Str(s, "env", "ANTHROPIC_BASE_URL");
                if (HostZai(url))
                {
                    chave = Json.Str(s, "env", "ANTHROPIC_AUTH_TOKEN") ?? Json.Str(s, "env", "ANTHROPIC_API_KEY");
                    if (chave != null) { console = Console(url); fonte = "Claude Code"; return true; }
                }
            }
            if (File.Exists(ZcodeCfg))
            {
                var prov = Json.Obj(Json.Parse(File.ReadAllText(ZcodeCfg)), "provider");
                if (prov != null)
                    foreach (var k in prov.Keys.Where(k => k.Contains("coding-plan")).OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (Json.Bool(prov[k], "enabled") == false) continue;
                        chave = Json.Str(prov[k], "options", "apiKey");
                        if (chave != null) { console = Console(Json.Str(prov[k], "options", "baseURL")); fonte = "ZCode"; return true; }
                    }
            }
            if (File.Exists(ZcodeCred))
            {
                chave = Json.Str(Json.Parse(File.ReadAllText(ZcodeCred)), "oauth:zai:access_token");
                if (chave != null && !chave.StartsWith("enc:v1:")) { console = "https://api.z.ai"; fonte = "ZCode"; return true; }
            }
            foreach (var f in OpenCodeAuth())
            {
                var o = Json.Obj(Json.Parse(File.ReadAllText(f)));
                if (o == null) continue;
                foreach (var id in new[] { "zai-coding-plan", "zai", "z-ai", "z.ai", "glm", "zhipu", "zhipuai" })
                {
                    object v;
                    if (!o.TryGetValue(id, out v)) continue;
                    chave = v as string ?? new[] { "apiKey", "api_key", "token", "key", "accessToken", "auth_token" }.Select(c => Json.Str(v, c)).FirstOrDefault(c => c != null);
                    if (chave != null) { console = id.StartsWith("zhipu") ? "https://open.bigmodel.cn" : "https://api.z.ai"; fonte = "OpenCode"; return true; }
                }
            }
            chave = null;
            return false;
        }

        protected override Leitura Consultar()
        {
            string chave, console, fonte;
            if (!Chave(out chave, out console, out fonte))
                throw new SemLeitura("Nenhuma chave do z.ai encontrada (ZCode, OpenCode ou glm.json na pasta do Pulso)", TimeSpan.FromMinutes(10), true);
            var req = new HttpRequestMessage(HttpMethod.Get, console + "/api/monitor/usage/quota/limit");
            req.Headers.TryAddWithoutValidation("Authorization", chave); // chave crua, sem "Bearer"
            req.Headers.Accept.ParseAdd("application/json");
            var o = Json.Parse(Enviar(req));
            double codigo = Json.Num(o, "code") ?? 0;
            if (codigo == 401 || codigo == 403) throw new SemLeitura("O z.ai recusou a chave — renove-a na ferramenta que a guarda", TimeSpan.FromMinutes(10), true);
            if (Json.Bool(o, "success") == false && codigo != 200) throw new SemLeitura("O monitor do z.ai recusou o pedido (" + codigo + ")");
            var l = new Leitura { Fonte = fonte, Plano = Util.Maiuscula(Json.Str(o, "data", "level")) };
            foreach (var w in Json.Arr(o, "data", "limits") ?? new object[0])
            {
                double? pct = Json.Num(w, "percentage");
                if (!pct.HasValue) continue;
                string tipo = Json.Str(w, "type");
                int unidade = (int)(Json.Num(w, "unit") ?? 0), n = (int)(Json.Num(w, "number") ?? 0);
                string id, rot;
                int min = 0;
                if (tipo == "TIME_LIMIT") { id = "mcp"; rot = "MCP (1 mês)"; }
                else if (unidade == 3 && n == 5) { id = "session"; rot = "Sessão (5h)"; min = 300; }
                else if (unidade == 6 && n == 1) { id = "weekly"; rot = "Semana"; min = 10080; }
                else if (unidade > 0) { id = "window-" + unidade + "x" + n; rot = unidade == 3 ? "Uso (" + n + " h)" : unidade == 6 ? "Uso (" + n + " sem)" : "Uso"; }
                else { id = (tipo ?? "unknown").ToLowerInvariant(); rot = "Uso"; }
                double? prox = Json.Num(w, "nextResetTime");
                l.Janelas.Add(Util.J(id, rot, pct.Value / 100, prox.HasValue ? Tempo.DeUnixMs((long)prox.Value) : (DateTime?)null, min, id == "weekly"));
            }
            string[] ordem = { "session", "weekly", "mcp" };
            var ordenadas = l.Janelas.OrderBy(j => Array.IndexOf(ordem, j.Id) < 0 ? 9 : Array.IndexOf(ordem, j.Id)).ThenBy(j => j.Id, StringComparer.Ordinal).ToList();
            l.Janelas.Clear(); l.Janelas.AddRange(ordenadas);
            if (l.Janelas.Count == 0) l.Detalhe = "O plano não informou janelas de uso";
            return l;
        }
    }

    // ---------------- OpenCode (plano Go) ----------------
    class OpenCode : FonteExtra
    {
        public OpenCode() : base("opencode") { }

        static IEnumerable<string> Pastas()
        {
            var l = new List<string>();
            if (Util.Env("XDG_DATA_HOME") != null) l.Add(Path.Combine(Util.Env("XDG_DATA_HOME"), "opencode"));
            l.Add(Path.Combine(Caminhos.Home, ".local", "share", "opencode"));
            return l;
        }

        protected override string Detectar()
        {
            return Pastas().Any(p => File.Exists(Path.Combine(p, "auth.json")) || File.Exists(Path.Combine(p, "opencode.db"))) ? null : "não instalado";
        }

        protected override Leitura Consultar()
        {
            string token = null, org = null, servidor = null; bool oauth = false; DateTime? expira = null;
            foreach (var p in Pastas())
            {
                string auth = Path.Combine(p, "auth.json");
                var a = File.Exists(auth) ? Json.Obj(Json.Parse(File.ReadAllText(auth))) : null;
                object go;
                if (a != null && a.TryGetValue("opencode-go", out go))
                    token = go as string ?? new[] { "key", "apiKey", "api_key", "token", "accessToken" }.Select(c => Json.Str(go, c)).FirstOrDefault(c => c != null);
                if (token == null)
                {
                    string db = Path.Combine(p, "opencode.db");
                    var linhas = File.Exists(db) ? Sqlite.Consultar(db, "SELECT 1 FROM credential LIMIT 1",
                        "SELECT integration_id, value FROM credential WHERE integration_id IN ('opencode-go','opencode') AND COALESCE(active,1) != 0 ORDER BY time_updated DESC") : null;
                    var escolhida = linhas == null ? null : linhas.OrderBy(r => r[0] == "opencode-go" ? 0 : 1).FirstOrDefault();
                    if (escolhida != null)
                    {
                        var v = Json.Parse(escolhida[1]);
                        if (Json.Str(v, "type") == "oauth") { token = Json.Str(v, "access"); oauth = true; org = Json.Str(v, "metadata", "orgID"); servidor = Json.Str(v, "metadata", "server"); expira = Json.Data(v, "expires"); }
                        else token = Json.Str(v, "key") ?? escolhida[1];
                    }
                }
                object oc;
                if (token == null && a != null && a.TryGetValue("opencode", out oc) && Json.Str(oc, "type") == "oauth")
                { token = Json.Str(oc, "access"); oauth = true; org = Json.Str(oc, "metadata", "orgID"); servidor = Json.Str(oc, "metadata", "server"); expira = Json.Data(oc, "expires"); }
                if (token != null) break;
            }
            if (token == null) throw new SemLeitura("Rode opencode auth login para entrar no OpenCode", TimeSpan.FromMinutes(10), true);
            var req = new HttpRequestMessage(HttpMethod.Get, oauth ? "https://opencode.ai/inference/go/v1/usage" : "https://opencode.ai/zen/go/v1/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.ParseAdd("application/json");
            if (org != null) req.Headers.Add("x-opencode-org-id", org);
            string corpo;
            try { corpo = Enviar(req); }
            catch (SemLeitura e)
            {
                if (!e.Credencial) throw;
                if (expira.HasValue && expira.Value <= DateTime.UtcNow) throw new SemLeitura("O login do OpenCode expirou — abra o OpenCode para renovar", TimeSpan.FromMinutes(10), true);
                throw new SemLeitura(oauth ? "Sem assinatura Go nesta conta, ou o login foi recusado — rode opencode auth login" : "A chave opencode-go foi recusada ou não tem plano Go", TimeSpan.FromMinutes(10), true);
            }
            var o = Json.Parse(corpo);
            var l = new Leitura { Fonte = "OpenCode", Plano = "Go" };
            foreach (var t in new[] { new[] { "rolling", "Limite de 5 horas", "300" }, new[] { "weekly", "Limite semanal", "10080" }, new[] { "monthly", "Limite mensal", "43200" } })
            {
                double? pct = Json.Num(o, "usage", t[0], "percent");
                if (pct.HasValue) l.Janelas.Add(Util.J(t[0], t[1], pct.Value / 100, Json.Data(o, "usage", t[0], "resetsAt"), int.Parse(t[2]), t[0] == "weekly"));
            }
            if (l.Janelas.Count == 0) l.Detalhe = "O plano Go não informou janelas de uso";
            return l;
        }
    }
}
