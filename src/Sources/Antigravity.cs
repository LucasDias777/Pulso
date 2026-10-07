using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulso
{
    // Antigravity (id "gemini"). Fontes, em ordem:
    //  1. ponte local do language_server do app aberto (RetrieveUserQuotaSummary) — cota real por modelo;
    //  2. app fechado depois de já ter lido nesta execução: mantém a última leitura;
    //  3. sessão Google do Credential Manager (gemini:antigravity) → cloudcode-pa (loadCodeAssist + cota);
    //  4. contagem de requisições do dia nos transcript.jsonl (conta pessoal: o Google não publica cota).
    class Antigravity : FonteExtra
    {
        bool ponteJaLeu;

        public Antigravity() : base("gemini") { }

        static IEnumerable<string> Raizes()
        {
            string g = Path.Combine(Caminhos.Home, ".gemini");
            if (!Directory.Exists(g)) return new string[0];
            return Directory.GetDirectories(g, "antigravity*").OrderBy(d => d, StringComparer.Ordinal);
        }

        protected override string Detectar()
        {
            return Raizes().Any() ? null : "não instalado";
        }

        protected override Leitura Consultar()
        {
            // 1. Ponte local (só se o app está aberto)
            if (Process.GetProcesses().Any(p => p.ProcessName.IndexOf("language_server", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var l = Ponte();
                if (l != null) { ponteJaLeu = true; return l; }
            }
            else if (ponteJaLeu)
                throw new SemLeitura("O Antigravity está fechado — última leitura mantida", TimeSpan.FromMinutes(5));

            // 3. Sessão Google
            string plano = null;
            var cred = Credencial();
            if (cred != null)
            {
                string token = Json.Str(cred, "token", "access_token");
                var expira = Json.Data(cred, "token", "expiry");
                string metodo = Json.Str(cred, "auth_method");
                if (!string.IsNullOrEmpty(token) && (!expira.HasValue || expira.Value > DateTime.UtcNow))
                {
                    plano = Plano(token);
                    var cota = Cota(token);
                    if (cota.Count > 0)
                    {
                        var l = new Leitura { Plano = plano, Fonte = "Google" };
                        l.Janelas.AddRange(cota);
                        return l;
                    }
                }
                else if (metodo != null) plano = metodo == "consumer" ? "Pessoal" : metodo;
            }

            // 4. Contagem do dia
            var leitura = new Leitura { Plano = plano, Fonte = "registros do Antigravity" };
            leitura.Janelas.Add(new Janela { Id = "requests", Rotulo = "Requisições hoje · sem limite publicado", Contagem = ContarHoje() });
            leitura.Detalhe = plano != null ? plano + " · o Google não publica cota para esta conta" : "Abra o Antigravity para ler a cota";
            return leitura;
        }

        // ---------- 1. ponte local ----------
        Leitura Ponte()
        {
            try
            {
                using (var busca = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%language_server%'"))
                    foreach (ManagementObject mo in busca.Get())
                    {
                        string cmd = mo["CommandLine"] as string;
                        if (cmd == null || cmd.IndexOf("--csrf_token", StringComparison.Ordinal) < 0) continue;
                        var partes = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        int i = Array.IndexOf(partes, "--csrf_token");
                        if (i < 0 || i + 1 >= partes.Length) continue;
                        string csrf = partes[i + 1].Trim('"');
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        foreach (int porta in PortasEmEscuta(pid))
                        {
                            var js = PerguntarPonte(porta, csrf);
                            if (js != null && js.Count > 0)
                            {
                                var l = new Leitura { Fonte = "Antigravity" };
                                l.Janelas.AddRange(js);
                                return l;
                            }
                        }
                    }
            }
            catch (Exception e) { Log.Info("antigravity: ponte local falhou (" + Raiz(e).Message + ")"); }
            return null;
        }

        static List<Janela> PerguntarPonte(int porta, string csrf)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:" + porta + "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["x-codeium-csrf-token"] = csrf;
                req.Timeout = 10000;
                // Certificado autoassinado do próprio app; aceito só aqui, para 127.0.0.1
                req.ServerCertificateValidationCallback = delegate { return true; };
                var corpo = Encoding.UTF8.GetBytes("{\"forceRefresh\":true}");
                using (var s = req.GetRequestStream()) s.Write(corpo, 0, corpo.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    return JanelasDaPonte(Json.Parse(sr.ReadToEnd()));
            }
            catch { return null; }
        }

        static List<Janela> JanelasDaPonte(object o)
        {
            var r = new List<Janela>();
            var grupos = Json.Arr(o, "response", "groups") ?? Json.Arr(o, "groups");
            if (grupos == null) return r;
            foreach (var g in grupos)
            {
                string grupo = Traduzir(Json.Str(g, "displayName"));
                var bs = Json.Arr(g, "buckets");
                if (bs == null) continue;
                var doGrupo = new List<Janela>();
                foreach (var b in bs)
                {
                    double? resta = Json.Num(b, "remainingFraction");
                    if (!resta.HasValue || resta < 0 || resta > 1) continue;
                    string id = Json.Str(b, "bucketId") ?? Json.Str(g, "displayName") ?? "quota";
                    string faixa = Faixa(id);
                    doGrupo.Add(new Janela
                    {
                        Id = id, Grupo = grupo, Usado = 1 - resta.Value, ResetaEm = Json.Data(b, "resetTime"),
                        Rotulo = faixa ?? grupo ?? Json.Str(b, "displayName") ?? "Uso",
                        Minutos = faixa == "Limite semanal" ? 10080 : faixa == "Limite de 5 horas" ? 300 : 0,
                        Semanal = faixa == "Limite semanal",
                    });
                }
                // Dentro do grupo: 5h, depois semana, depois o resto
                r.AddRange(doGrupo.OrderBy(j => j.Minutos == 300 ? 0 : j.Semanal ? 1 : 2));
            }
            return r;
        }

        static string Faixa(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("weekly")) return "Limite semanal";
            if (s.Contains("5h") || s.Contains("five hour") || s.Contains("hourly")) return "Limite de 5 horas";
            return null;
        }

        static string Traduzir(string grupo)
        {
            if (grupo == null) return null;
            if (grupo.Equals("Gemini Models", StringComparison.OrdinalIgnoreCase)) return "Modelos Gemini";
            if (grupo.Equals("Claude and GPT models", StringComparison.OrdinalIgnoreCase)) return "Modelos Claude e GPT";
            return grupo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MIB_TCPROW_OWNER_PID { public uint estado, endLocal, portaLocal, endRemoto, portaRemota, pid; }
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr tabela, ref int tamanho, bool ordenar, int af, int classe, uint reservado);

        // Portas TCP em escuta do processo (o app abre duas aleatórias; só uma responde)
        static List<int> PortasEmEscuta(int pid)
        {
            var r = new List<int>();
            int tam = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref tam, false, 2, 3, 0); // AF_INET, TCP_TABLE_OWNER_PID_LISTENER
            IntPtr buf = Marshal.AllocHGlobal(tam);
            try
            {
                if (GetExtendedTcpTable(buf, ref tam, false, 2, 3, 0) != 0) return r;
                int n = Marshal.ReadInt32(buf);
                int passo = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < n; i++)
                {
                    var row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(buf + 4 + i * passo, typeof(MIB_TCPROW_OWNER_PID));
                    if (row.pid != pid) continue;
                    int porta = (int)(((row.portaLocal & 0xFF) << 8) | ((row.portaLocal >> 8) & 0xFF));
                    if (!r.Contains(porta)) r.Add(porta);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            r.Sort();
            return r;
        }

        // ---------- 3. sessão Google ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public int Flags, Type; public IntPtr TargetName, Comment; public long LastWritten;
            public int CredentialBlobSize; public IntPtr CredentialBlob; public int Persist, AttributeCount;
            public IntPtr Attributes, TargetAlias, UserName;
        }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string alvo, int tipo, int flags, out IntPtr cred);
        [DllImport("advapi32.dll")] static extern void CredFree(IntPtr cred);

        // Só lê, em memória; o próprio Antigravity renova
        static object Credencial()
        {
            IntPtr p;
            if (!CredRead("gemini:antigravity", 1, 0, out p)) return null;
            try
            {
                var c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize <= 0) return null;
                var bytes = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
                string t = Encoding.UTF8.GetString(bytes);
                if (t.IndexOf('\0') >= 0) t = Encoding.Unicode.GetString(bytes);
                t = t.Replace("\0", "").Trim();
                const string prefixo = "go-keyring-base64:";
                if (t.StartsWith(prefixo))
                {
                    string b = t.Substring(prefixo.Length).Replace('-', '+').Replace('_', '/');
                    while (b.Length % 4 != 0) b += "=";
                    t = Encoding.UTF8.GetString(Convert.FromBase64String(b));
                }
                return Json.Parse(t);
            }
            catch { return null; }
            finally { CredFree(p); }
        }

        static string Plano(string token)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")
                { Content = new StringContent("{\"metadata\":{\"pluginType\":\"GEMINI\"}}", Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var o = Json.Parse(Enviar(req));
                string n = Json.Str(o, "currentTier", "name");
                if (n != null) return n;
                var tiers = Json.Arr(o, "allowedTiers");
                if (tiers != null && tiers.Length > 0) return Json.Str(tiers.FirstOrDefault(t => Json.Bool(t, "isDefault") == true) ?? tiers[0], "name") ?? "Gemini";
                return "Gemini";
            }
            catch (SemLeitura e) { if (e.Credencial) throw new SemLeitura("A sessão Google do Antigravity foi recusada — entre de novo no Antigravity", TimeSpan.FromMinutes(10), true); return null; }
            catch (Exception e) { Log.Info("antigravity: loadCodeAssist falhou (" + Raiz(e).Message + ")"); return null; }
        }

        // Conta pessoal recebe 403 aqui; qualquer erro só significa "sem cota publicada"
        static List<Janela> Cota(string token)
        {
            var r = new List<Janela>();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary")
                { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var o = Json.Parse(Enviar(req));
                var todos = new List<object>();
                var grupos = Json.Arr(o, "quotaGroups");
                if (grupos != null) foreach (var g in grupos) { var bs = Json.Arr(g, "buckets"); if (bs != null) todos.AddRange(bs); }
                var soltos = Json.Arr(o, "buckets");
                if (soltos != null) todos.AddRange(soltos);
                foreach (var b in todos)
                {
                    double? lim = Json.Num(b, "limit"), usado = Json.Num(b, "used");
                    if (!lim.HasValue || !usado.HasValue || lim <= 0 || usado < 0 || usado > 1.5 * lim) continue;
                    string rot = Json.Str(b, "displayName") ?? Json.Str(b, "name") ?? "Uso";
                    r.Add(new Janela { Id = Json.Str(b, "name") ?? rot, Rotulo = rot, Usado = Math.Min(1, usado.Value / lim.Value), ResetaEm = Json.Data(b, "resetTime") });
                }
            }
            catch { }
            return r;
        }

        // ---------- 4. contagem do dia ----------
        static int ContarHoje()
        {
            int n = 0;
            var hoje = DateTime.Today;
            foreach (var raiz in Raizes())
            {
                string brain = Path.Combine(raiz, "brain");
                if (!Directory.Exists(brain)) continue;
                foreach (var d in Directory.GetDirectories(brain))
                {
                    string f = Path.Combine(d, ".system_generated", "logs", "transcript.jsonl");
                    // Arquivo não mexido hoje não tem requisição de hoje: nem abre
                    if (!File.Exists(f) || File.GetLastWriteTime(f) < hoje) continue;
                    foreach (var linha in Caminhos.LerCompartilhado(f).Split('\n'))
                    {
                        if (linha.IndexOf("\"MODEL\"", StringComparison.Ordinal) < 0) continue;
                        var o = Json.Parse(linha);
                        if (Json.Str(o, "source") != "MODEL") continue;
                        var quando = Json.Data(o, "created_at");
                        if (quando.HasValue && quando.Value.ToLocalTime().Date == hoje) n++;
                    }
                }
            }
            return n;
        }

        // ---------- janela do anel e janela semanal ----------
        static string Familia(Janela j)
        {
            string id = (j.Id ?? "").ToLowerInvariant();
            if (id.StartsWith("gemini")) return "gemini";
            if (id.StartsWith("3p") || id.StartsWith("claude")) return "3p";
            return "";
        }

        static bool DoTipo(Janela j, bool semanal)
        {
            string t = ((j.Id ?? "") + " " + (j.Rotulo ?? "")).ToLowerInvariant();
            if (semanal) return t.Contains("weekly") || t.Contains("semanal");
            return new[] { "5h", "5-hour", "five hour", "five-hour", "hourly", "session", "5 horas" }.Any(t.Contains);
        }

        static Janela MaisApertada(IEnumerable<Janela> js)
        {
            return js.Where(j => !j.Contagem.HasValue).OrderByDescending(j => j.Usado).ThenBy(j => j.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        static List<Janela> DaFamilia(List<Janela> js)
        {
            string fam = Config.Atual.AntigravityModelos == "claude-gpt" ? "3p" : "gemini";
            var f = js.Where(j => Familia(j) == fam).ToList();
            return f.Count > 0 ? f : js;
        }

        public static Janela Principal(List<Janela> js)
        {
            var f = DaFamilia(js);
            string leitura = Config.Atual.AntigravityLeitura;
            if (leitura == "5h" || leitura == "semana")
            {
                var t = MaisApertada(f.Where(j => DoTipo(j, leitura == "semana")));
                if (t != null) return t;
            }
            return MaisApertada(f.Where(j => j.Usado < 1)) ?? MaisApertada(f) ?? f.FirstOrDefault();
        }

        public static Janela Semanal(List<Janela> js)
        {
            return MaisApertada(DaFamilia(js).Where(j => DoTipo(j, true)));
        }
    }
}
