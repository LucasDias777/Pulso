using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulso
{
    // Integrações que mexem fora da pasta do Pulso. Todas são ligadas por ação da pessoa nas Configurações.
    static class Integracao
    {
        static string Exe { get { return Instalacao.ExeOficial; } }
        static string Comando { get { return "\"" + Exe.Replace("\\", "/") + "\" --statusline"; } }

        // ---- Barra de status do Claude Code (terminal) ----
        // Quando o Claude Code roda no terminal, ele entrega à barra de status o consumo exato a cada resposta
        // (rate_limits). O Pulso vira essa barra: recebe o valor na hora e ainda escreve um resumo no terminal.

        public static bool BarraInstalada()
        {
            try { return File.Exists(Caminhos.ClaudeSettings) && File.ReadAllText(Caminhos.ClaudeSettings).Contains("--statusline\"") && File.ReadAllText(Caminhos.ClaudeSettings).Contains("Pulso.exe"); }
            catch { return false; }
        }

        public static bool BarraDeOutro(out string atual)
        {
            atual = null;
            try
            {
                if (!File.Exists(Caminhos.ClaudeSettings)) return false;
                var o = Json.Parse(File.ReadAllText(Caminhos.ClaudeSettings));
                string cmd = Json.Str(o, "statusLine", "command");
                if (cmd == null) return false;
                atual = cmd;
                return cmd.IndexOf("Pulso.exe", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        // Inserção textual da chave no fim do objeto: o resto do arquivo fica byte a byte igual. Backup antes.
        public static string InstalarBarra()
        {
            string arq = Caminhos.ClaudeSettings;
            string texto = File.Exists(arq) ? File.ReadAllText(arq) : "{}";
            string outro;
            if (BarraDeOutro(out outro)) return "Já existe uma barra de status configurada ({0}). Nada foi alterado.".T(outro);
            if (BarraInstalada()) return null;
            if (Json.Parse(texto) == null) return "O settings.json do Claude Code não é um JSON válido. Nada foi alterado.".T();
            int fim = texto.LastIndexOf('}');
            if (fim < 0) return "Formato inesperado no settings.json. Nada foi alterado.".T();
            string antes = texto.Substring(0, fim).TrimEnd();
            bool vazio = antes.EndsWith("{");
            var cmd = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Comando);
            string bloco = (vazio ? "" : ",") + "\n  \"statusLine\": { \"type\": \"command\", \"command\": " + cmd + ", \"padding\": 0 }\n";
            string novo = antes + bloco + texto.Substring(fim);
            if (Json.Parse(novo) == null) return "Não consegui montar um JSON válido. Nada foi alterado.".T();
            Backup(arq);
            Caminhos.GravarAtomico(arq, novo);
            return null;
        }

        public static string RemoverBarra()
        {
            string arq = Caminhos.ClaudeSettings;
            if (!File.Exists(arq)) return null;
            string texto = File.ReadAllText(arq);
            var re = new Regex(@",?\s*""statusLine""\s*:\s*\{[^{}]*Pulso\.exe[^{}]*\}", RegexOptions.IgnoreCase);
            if (!re.IsMatch(texto)) return null;
            string novo = re.Replace(texto, "", 1);
            novo = Regex.Replace(novo, @"\{\s*,", "{");
            if (Json.Parse(novo) == null) return "Não consegui remover sem quebrar o JSON. Nada foi alterado.".T();
            Backup(arq);
            Caminhos.GravarAtomico(arq, novo);
            return null;
        }

        static void Backup(string arq)
        {
            if (File.Exists(arq)) File.Copy(arq, arq + ".pulso-bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
        }

        // ---- Limites: hooks do Claude Code (settings.json) e do Codex (~/.codex/hooks.json) ----
        // Pedido novo, antes e depois de cada ferramenta: os três pontos em que dá para parar o agente sem cortar uma
        // ferramenta no meio. Só o valor de "hooks" é reescrito; o resto do arquivo fica byte a byte igual. Backup antes.
        static readonly string[] EventosLimite = { "UserPromptSubmit", "PreToolUse", "PostToolUse" };

        static string ArquivoHooks(string provedor) { return provedor == "claude" ? Caminhos.ClaudeSettings : Path.Combine(Caminhos.CodexDir, "hooks.json"); }
        // O Claude Code aceita o caminho entre aspas, como na barra de status. O Codex roda o hook pelo PowerShell no Windows,
        // que só executa um caminho entre aspas com o operador & — testado: aspas sozinhas, cmd /c e commandWindows falham
        static string ComandoLimite(string provedor)
        {
            return provedor == "codex" ? "& \"" + Exe + "\" --limite codex" : "\"" + Exe.Replace("\\", "/") + "\" --limite " + provedor;
        }

        static bool DoPulso(object handler, string provedor)
        {
            string cmd = Json.Str(handler, "command") ?? "";
            return cmd.IndexOf("Pulso.exe", StringComparison.OrdinalIgnoreCase) >= 0 && cmd.Contains("--limite " + provedor);
        }

        public static bool LimiteInstalado(string provedor)
        {
            try
            {
                string arq = ArquivoHooks(provedor);
                if (!File.Exists(arq)) return false;
                var hooks = Json.Obj(Json.Parse(File.ReadAllText(arq)), "hooks");
                return hooks != null && hooks.Values.OfType<object[]>().SelectMany(g => g)
                    .Any(g => (Json.Arr(g, "hooks") ?? new object[0]).Any(h => DoPulso(h, provedor)));
            }
            catch { return false; }
        }

        public static string InstalarLimite(string provedor) { return MexerNosHooks(provedor, true); }
        public static string RemoverLimite(string provedor) { return MexerNosHooks(provedor, false); }

        static string MexerNosHooks(string provedor, bool instalar)
        {
            string arq = ArquivoHooks(provedor);
            if (!instalar && !File.Exists(arq)) return null;
            string texto = File.Exists(arq) ? File.ReadAllText(arq) : "{}";
            if (!(Json.Parse(texto) is IDictionary<string, object>)) return "O arquivo {0} não é um JSON válido. Nada foi alterado.".T(arq);

            // Tira os do Pulso (inclusive de um caminho antigo do exe) e, ao instalar, põe de novo os de agora
            var hooks = new Dictionary<string, object>();
            var atuais = Json.Obj(Json.Parse(texto), "hooks");
            if (atuais != null)
                foreach (var ev in atuais)
                {
                    if (!(ev.Value is object[])) { hooks[ev.Key] = ev.Value; continue; } // formato que não é o nosso: fica como está
                    // O evento fica mesmo vazio: o arquivo pode já ter listas vazias (o do dono tinha, sobra de outro app)
                    var grupos = new List<object>();
                    foreach (var g in (object[])ev.Value)
                    {
                        var d = g as IDictionary<string, object>;
                        var lista = Json.Arr(g, "hooks");
                        if (d == null || lista == null) { grupos.Add(g); continue; }
                        var resto = lista.Where(h => !DoPulso(h, provedor)).ToArray();
                        if (resto.Length == 0 && lista.Length > 0) continue;
                        var copia = new Dictionary<string, object>(d);
                        copia["hooks"] = resto;
                        grupos.Add(copia);
                    }
                    hooks[ev.Key] = grupos.ToArray();
                }
            if (instalar)
                foreach (var ev in EventosLimite)
                {
                    var handler = new Dictionary<string, object> { { "type", "command" }, { "command", ComandoLimite(provedor) }, { "timeout", 10 } };
                    var grupo = new Dictionary<string, object>();
                    if (ev != "UserPromptSubmit") grupo["matcher"] = "*";
                    grupo["hooks"] = new object[] { handler };
                    object antes;
                    var lista = hooks.TryGetValue(ev, out antes) ? ((object[])antes).ToList() : new List<object>();
                    lista.Add(grupo);
                    hooks[ev] = lista.ToArray();
                }

            // Sobrou só o que o Pulso criou, vazio: tira a chave inteira (o arquivo volta a ser como era antes de ligar)
            bool soNossos = hooks.All(kv => EventosLimite.Contains(kv.Key) && kv.Value is object[] && ((object[])kv.Value).Length == 0);
            string novo = TrocarChave(texto, "hooks", soNossos ? null : hooks);
            if (novo == null || !(Json.Parse(novo) is IDictionary<string, object>)) return "Não consegui montar um JSON válido. Nada foi alterado.".T();
            if (novo == texto) return null;
            Backup(arq);
            Directory.CreateDirectory(Path.GetDirectoryName(arq));
            Caminhos.GravarAtomico(arq, novo);
            Log.Info("limites: hooks " + (instalar ? "instalados" : "removidos") + " em " + arq);
            return null;
        }

        // Troca o valor de uma chave do objeto raiz pelo texto do valor novo (nulo = tira a chave); sem a chave, acrescenta
        // no fim. O scanner só precisa achar onde o valor começa e termina (strings e aninhamento)
        static string TrocarChave(string texto, string chave, object valor)
        {
            int i = Pular(texto, 0);
            if (i >= texto.Length || texto[i] != '{') return null;
            i = Pular(texto, i + 1);
            int anteriorFim = -1; // fim do valor anterior, para tirar a vírgula junto
            while (i < texto.Length && texto[i] != '}')
            {
                int inicioPar = i;
                int fimChave = FimString(texto, i);
                if (fimChave < 0) return null;
                string nome = Json.Parse(texto.Substring(i, fimChave - i)) as string;
                i = Pular(texto, fimChave);
                if (i >= texto.Length || texto[i] != ':') return null;
                int inicioValor = Pular(texto, i + 1);
                int fimValor = FimValor(texto, inicioValor);
                if (fimValor < 0) return null;
                int depois = Pular(texto, fimValor);
                if (nome == chave)
                {
                    if (valor != null) return texto.Substring(0, inicioValor) + Escrever(valor, 1) + texto.Substring(fimValor);
                    // Tira o par e uma vírgula: a de depois, ou a de antes se ele for o último
                    if (depois < texto.Length && texto[depois] == ',') return texto.Substring(0, inicioPar) + texto.Substring(Pular(texto, depois + 1));
                    return anteriorFim >= 0 ? texto.Substring(0, anteriorFim) + texto.Substring(fimValor) : texto.Substring(0, inicioPar) + texto.Substring(depois);
                }
                anteriorFim = fimValor;
                i = depois;
                if (i < texto.Length && texto[i] == ',') i = Pular(texto, i + 1);
            }
            if (valor == null) return texto;
            int fim = texto.LastIndexOf('}');
            string antesDoFim = texto.Substring(0, fim).TrimEnd();
            bool vazio = antesDoFim.EndsWith("{");
            return antesDoFim + (vazio ? "" : ",") + "\n  " + Escrever(chave, 1) + ": " + Escrever(valor, 1) + "\n" + texto.Substring(fim);
        }

        static int Pular(string t, int i) { while (i < t.Length && char.IsWhiteSpace(t[i])) i++; return i; }

        static int FimString(string t, int i)
        {
            if (i >= t.Length || t[i] != '"') return -1;
            for (i++; i < t.Length; i++)
            {
                if (t[i] == '\\') { i++; continue; }
                if (t[i] == '"') return i + 1;
            }
            return -1;
        }

        static int FimValor(string t, int i)
        {
            if (i >= t.Length) return -1;
            if (t[i] == '"') return FimString(t, i);
            if (t[i] == '{' || t[i] == '[')
            {
                int nivel = 0;
                for (; i < t.Length; i++)
                {
                    char ch = t[i];
                    if (ch == '"') { i = FimString(t, i) - 1; if (i < 0) return -1; continue; }
                    if (ch == '{' || ch == '[') nivel++;
                    else if ((ch == '}' || ch == ']') && --nivel == 0) return i + 1;
                }
                return -1;
            }
            while (i < t.Length && t[i] != ',' && t[i] != '}' && t[i] != ']' && !char.IsWhiteSpace(t[i])) i++;
            return i;
        }

        // JSON com 2 espaços, como o próprio Claude Code grava, e escape mínimo (o JavaScriptSerializer trocaria & e < por &)
        static string Escrever(object o, int nivel)
        {
            if (o == null) return "null";
            var s = o as string;
            if (s != null)
            {
                var sb = new StringBuilder("\"");
                foreach (char ch in s)
                {
                    if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                    else if (ch == '\n') sb.Append("\\n");
                    else if (ch == '\r') sb.Append("\\r");
                    else if (ch == '\t') sb.Append("\\t");
                    else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                }
                return sb.Append('"').ToString();
            }
            if (o is bool) return (bool)o ? "true" : "false";
            if (o is int || o is long || o is decimal || o is double)
                return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
            string pad = new string(' ', (nivel + 1) * 2), fim = new string(' ', nivel * 2);
            var dic = o as IDictionary<string, object>;
            if (dic != null)
                return dic.Count == 0 ? "{}" : "{\n" + string.Join(",\n", dic.Select(kv => pad + Escrever(kv.Key, nivel + 1) + ": " + Escrever(kv.Value, nivel + 1)).ToArray()) + "\n" + fim + "}";
            var lista = o as System.Collections.IEnumerable;
            if (lista != null)
            {
                var itens = lista.Cast<object>().ToList();
                return itens.Count == 0 ? "[]" : "[\n" + string.Join(",\n", itens.Select(x => pad + Escrever(x, nivel + 1)).ToArray()) + "\n" + fim + "]";
            }
            return Escrever(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture), nivel);
        }

        // ---- Iniciar com o Windows ----
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Só grava quando falta ou aponta para outro lugar: regravar a cada abertura é o que o antivírus
        // reconhece como vírus se fixando no sistema. "--inicio": aberto pelo Windows, fica só na bandeja.
        public static void IniciarComWindows(bool ligar)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Run, true))
                {
                    if (k == null) return;
                    string valor = "\"" + Exe + "\" --inicio";
                    if (ligar) { if (!string.Equals(k.GetValue("Pulso") as string, valor, StringComparison.OrdinalIgnoreCase)) k.SetValue("Pulso", valor); }
                    else if (k.GetValue("Pulso") != null) k.DeleteValue("Pulso");
                }
            }
            catch (Exception e) { Log.Erro("iniciar com o Windows", e); }
        }
    }

    // Modo "--statusline": chamado pelo Claude Code a cada atualização da barra (processo curto).
    static class BarraDeStatus
    {
        public static void Executar()
        {
            string entrada;
            using (var sr = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) entrada = sr.ReadToEnd();
            var o = Json.Parse(entrada);
            var rl = Json.Obj(o, "rate_limits");
            if (rl != null) Mensagens.Enviar(2, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(rl));

            Config.Carregar(); // idioma do texto da barra
            var saida = new StringBuilder();
            string modelo = Json.Str(o, "model", "display_name");
            if (modelo != null) saida.Append(modelo);
            double? h5 = Json.Num(rl, "five_hour", "used_percentage"), sem = Json.Num(rl, "seven_day", "used_percentage");
            if (h5.HasValue) saida.Append(saida.Length > 0 ? " · " : "").Append("5h ").Append(Math.Round(h5.Value)).Append('%');
            if (sem.HasValue) saida.Append(" · ").Append("semana {0}%".T(Math.Round(sem.Value)));
            using (var sw = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                sw.Write(saida.ToString());
        }
    }

    // Janela invisível que recebe mensagens de outras instâncias (barra de status, segunda execução) e o atalho global
    class Mensagens : NativeWindow, IDisposable
    {
        public const string Titulo = "Pulso.Mensagens";
        public event Action<int, string> Recebeu;
        public event Action Atalho;

        public Mensagens()
        {
            CreateHandle(new CreateParams { Caption = Titulo, Parent = (IntPtr)(-3) }); // HWND_MESSAGE
        }

        public bool RegistrarAtalho(uint mods, int tecla)
        {
            return Nativo.RegisterHotKey(Handle, 1, mods | Nativo.MOD_NOREPEAT, (uint)tecla);
        }

        public void SoltarAtalho() { Nativo.UnregisterHotKey(Handle, 1); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Nativo.WM_HOTKEY && (int)m.WParam == 1)
            {
                var h = Atalho;
                if (h != null) h();
                return;
            }
            if (m.Msg == Nativo.WM_COPYDATA)
            {
                var cds = (Nativo.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Nativo.COPYDATASTRUCT));
                string texto = cds.cbData > 0 ? Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2) : "";
                var h = Recebeu;
                if (h != null) h((int)cds.dwData, texto);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        public static bool Enviar(int tipo, string texto)
        {
            IntPtr alvo = FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, Titulo);
            if (alvo == IntPtr.Zero) return false;
            IntPtr buf = Marshal.StringToHGlobalUni(texto);
            try
            {
                var cds = new Nativo.COPYDATASTRUCT { dwData = (IntPtr)tipo, cbData = (texto.Length) * 2, lpData = buf };
                IntPtr r;
                Nativo.SendMessageTimeout(alvo, Nativo.WM_COPYDATA, IntPtr.Zero, ref cds, 2 /* SMTO_ABORTIFHUNG */, 500, out r);
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose() { DestroyHandle(); }
    }
}
