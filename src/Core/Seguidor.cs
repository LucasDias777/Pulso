using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Pulso
{
    // Acompanha arquivos .jsonl que crescem (registros de sessão) e entrega cada linha nova assim que é gravada.
    // FileSystemWatcher avisa na hora; como o Windows atrasa a data/tamanho de arquivo mantido aberto por quem
    // escreve, os arquivos "quentes" também são conferidos abrindo o arquivo a cada intervalo curto.
    // A posição inicial de cada arquivo pode vir de fora (índice salvo): assim o atrasado é lido uma vez só.
    class Seguidor : IDisposable
    {
        readonly string raiz;
        readonly Action<string, string> aoLinha;
        readonly Func<string, DateTime, long, long?> posicaoInicial; // (arquivo, modificado UTC, tamanho) → posição; nulo = fim
        readonly object trava = new object();
        readonly Dictionary<string, long> posicao = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> quente = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher fsw;
        Timer conferir, varrer;
        readonly TimeSpan janelaQuente;
        bool ativo;

        public const int IntervaloMs = 350;
        const int Bloco = 8 * 1024 * 1024;

        public event Action EmDia;      // terminou de ler o atrasado da partida
        public bool EstaEmDia { get; private set; }

        public Seguidor(string raiz, Action<string, string> aoLinha, TimeSpan janelaQuente, Func<string, DateTime, long, long?> posicaoInicial = null)
        {
            this.raiz = raiz;
            this.aoLinha = aoLinha;
            this.janelaQuente = janelaQuente;
            this.posicaoInicial = posicaoInicial;
        }

        public void Iniciar()
        {
            ativo = true;
            Varrer(true);
            TentarVigiar();
            conferir = new Timer(delegate { Conferir(); }, null, 50, IntervaloMs);
            varrer = new Timer(delegate { Varrer(false); TentarVigiar(); }, null, 20000, 20000);
        }

        public Dictionary<string, long> Posicoes()
        {
            lock (trava) return new Dictionary<string, long>(posicao, StringComparer.OrdinalIgnoreCase);
        }

        void TentarVigiar()
        {
            if (fsw != null || !Directory.Exists(raiz)) return;
            try
            {
                // Na grafia da varredura (o DirectoryInfo troca o nome curto 8.3 pelo longo; o vigia, não): com grafias
                // diferentes o mesmo arquivo ganhava duas posições e era lido duas vezes
                fsw = new FileSystemWatcher(new DirectoryInfo(raiz).FullName, "*.jsonl");
                fsw.IncludeSubdirectories = true;
                fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                fsw.InternalBufferSize = 64 * 1024;
                FileSystemEventHandler h = delegate(object s, FileSystemEventArgs e) { Esquentar(e.FullPath, e.ChangeType == WatcherChangeTypes.Created); };
                fsw.Changed += h;
                fsw.Created += h;
                fsw.Error += delegate { Log.Info("watcher estourou o buffer em " + raiz + "; varrendo"); Varrer(false); };
                fsw.EnableRaisingEvents = true;
            }
            catch (Exception e) { Log.Erro("vigiar " + raiz, e); fsw = null; }
        }

        void Esquentar(string arq, bool novo)
        {
            lock (trava)
            {
                if (novo && !posicao.ContainsKey(arq)) posicao[arq] = 0;
                quente[arq] = DateTime.UtcNow;
            }
            ThreadPool.QueueUserWorkItem(delegate { Conferir(); });
        }

        // Descobre arquivos (se o watcher perder algum) e marca os recém-alterados como quentes.
        void Varrer(bool inicial)
        {
            if (!Directory.Exists(raiz)) return;
            try
            {
                var limite = DateTime.UtcNow - (inicial ? janelaQuente : TimeSpan.FromHours(2));
                foreach (var f in new DirectoryInfo(raiz).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                {
                    DateTime mod = f.LastWriteTimeUtc;
                    lock (trava) { if (posicao.ContainsKey(f.FullName) && (mod < limite || quente.ContainsKey(f.FullName))) continue; }
                    // Tamanho do diretório pode estar atrasado em arquivo aberto; nos recentes, pergunta ao arquivo
                    long tam = mod >= limite ? TamanhoReal(f.FullName, f.Length) : f.Length;
                    lock (trava)
                    {
                        if (!posicao.ContainsKey(f.FullName))
                        {
                            long? ini = null;
                            if (inicial && posicaoInicial != null) ini = posicaoInicial(f.FullName, mod, tam);
                            // Sem índice: arquivo de antes da partida começa do fim; o que surgiu depois, do começo
                            long pos = ini.HasValue ? Math.Min(ini.Value, tam) : (inicial || mod < limite ? tam : 0);
                            posicao[f.FullName] = pos;
                            if (pos < tam || (!inicial && mod >= limite)) quente[f.FullName] = DateTime.UtcNow;
                        }
                        else if (mod >= limite && !quente.ContainsKey(f.FullName)) quente[f.FullName] = mod;
                    }
                }
            }
            catch (Exception e) { Log.Erro("varrer " + raiz, e); }
        }

        static long TamanhoReal(string arq, long padrao)
        {
            try
            {
                using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return fs.Length;
            }
            catch { return padrao; }
        }

        int conferindo;

        void Conferir()
        {
            if (!ativo || Interlocked.Exchange(ref conferindo, 1) == 1) return;
            try
            {
                List<string> lista;
                lock (trava)
                {
                    var corte = DateTime.UtcNow - janelaQuente;
                    lista = new List<string>();
                    var frios = new List<string>();
                    foreach (var kv in quente)
                    {
                        if (kv.Value < corte) frios.Add(kv.Key);
                        else lista.Add(kv.Key);
                    }
                    foreach (var f in frios) quente.Remove(f);
                }
                foreach (var arq in lista) LerNovo(arq);
                if (!EstaEmDia)
                {
                    EstaEmDia = true;
                    var h = EmDia;
                    if (h != null) h();
                }
            }
            finally { conferindo = 0; }
        }

        // Lê do ponto salvo até o fim, em blocos (o atrasado da primeira vez pode ter centenas de MB)
        void LerNovo(string arq)
        {
            long pos;
            lock (trava) { if (!posicao.TryGetValue(arq, out pos)) pos = 0; }
            try
            {
                using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long tam = fs.Length;
                    if (tam < pos) pos = 0; // arquivo foi reescrito
                    while (pos < tam && ativo)
                    {
                        fs.Seek(pos, SeekOrigin.Begin);
                        int pedir = (int)Math.Min(Bloco, tam - pos);
                        var buf = new byte[pedir];
                        int lidos = 0;
                        while (lidos < pedir)
                        {
                            int n = fs.Read(buf, lidos, pedir - lidos);
                            if (n <= 0) break;
                            lidos += n;
                        }
                        if (lidos == 0) break;
                        int ultimoFim = Array.LastIndexOf(buf, (byte)'\n', lidos - 1);
                        if (ultimoFim < 0)
                        {
                            // Linha maior que o bloco: só avança se o arquivo já tem mais que isso
                            if (pedir < Bloco) break;
                            pos += lidos;
                            lock (trava) posicao[arq] = pos;
                            continue;
                        }
                        string texto = Encoding.UTF8.GetString(buf, 0, ultimoFim);
                        pos += ultimoFim + 1;
                        lock (trava) { posicao[arq] = pos; quente[arq] = DateTime.UtcNow; }
                        foreach (var linha in texto.Split('\n'))
                        {
                            string l = linha.TrimEnd('\r');
                            if (l.Length == 0) continue;
                            try { aoLinha(arq, l); }
                            catch (Exception e) { Log.Erro("linha de " + Path.GetFileName(arq), e); }
                        }
                    }
                }
            }
            catch (FileNotFoundException) { lock (trava) { quente.Remove(arq); posicao.Remove(arq); } }
            catch (DirectoryNotFoundException) { lock (trava) { quente.Remove(arq); posicao.Remove(arq); } }
            catch (IOException) { }
        }

        // Últimas linhas completas de um arquivo (para achar o estado mais recente na partida)
        public static List<string> Cauda(string arq, int maxBytes)
        {
            var r = new List<string>();
            try
            {
                using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long ini = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(ini, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - ini];
                    int lidos = 0;
                    while (lidos < buf.Length)
                    {
                        int n = fs.Read(buf, lidos, buf.Length - lidos);
                        if (n <= 0) break;
                        lidos += n;
                    }
                    string t = Encoding.UTF8.GetString(buf, 0, lidos);
                    var partes = t.Split('\n');
                    for (int i = ini > 0 ? 1 : 0; i < partes.Length; i++)
                    {
                        string l = partes[i].TrimEnd('\r');
                        if (l.Length > 0) r.Add(l);
                    }
                }
            }
            catch { }
            return r;
        }

        public void Dispose()
        {
            ativo = false;
            if (fsw != null) { fsw.EnableRaisingEvents = false; fsw.Dispose(); }
            if (conferir != null) conferir.Dispose();
            if (varrer != null) varrer.Dispose();
        }
    }
}
