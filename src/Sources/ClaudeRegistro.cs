using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Pulso
{
    // Atividade das sessões do Claude Code pelo registro que ele mesmo mantém em ~/.claude/sessions/<pid>.json
    // ("status": busy | waiting | idle). Dispensa hooks: nada é instalado no settings.json.
    class ClaudeRegistro : IDisposable
    {
        FileSystemWatcher fsw;
        Timer relogio;
        int lendo;
        readonly Dictionary<string, string> statusAnterior = new Dictionary<string, string>();
        readonly Dictionary<int, DateTime> vivoConferido = new Dictionary<int, DateTime>();

        public event Action FimDeTurno;
        public bool Disponivel { get { return Directory.Exists(Pasta); } }

        static string Pasta { get { return Path.Combine(Caminhos.ClaudeDir, "sessions"); } }

        public void Iniciar()
        {
            Ler();
            relogio = new Timer(delegate { Ler(); Vigiar(); }, null, 1000, 1000);
            Vigiar();
        }

        void Vigiar()
        {
            if (fsw != null || !Directory.Exists(Pasta)) return;
            try
            {
                fsw = new FileSystemWatcher(Pasta, "*.json");
                fsw.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                FileSystemEventHandler h = delegate { ThreadPool.QueueUserWorkItem(delegate { Ler(); }); };
                fsw.Changed += h; fsw.Created += h; fsw.Deleted += h;
                fsw.Renamed += delegate { ThreadPool.QueueUserWorkItem(delegate { Ler(); }); };
                fsw.EnableRaisingEvents = true;
            }
            catch (Exception e) { Log.Erro("vigiar sessões do claude", e); fsw = null; }
        }

        void Ler()
        {
            if (Interlocked.Exchange(ref lendo, 1) == 1) return;
            try
            {
                if (!Directory.Exists(Pasta)) return;
                var vistas = new HashSet<string>();
                bool mudou = false, terminou = false;
                foreach (var arq in Directory.GetFiles(Pasta, "*.json"))
                {
                    object o;
                    try { o = Json.Parse(Caminhos.LerCompartilhado(arq)); }
                    catch (IOException) { continue; }
                    string id = Json.Str(o, "sessionId");
                    int pid = (int)(Json.Num(o, "pid") ?? 0);
                    if (id == null || (pid > 0 && !Vivo(pid))) continue;
                    vistas.Add(id);
                    string status = Json.Str(o, "status") ?? "idle";
                    var quando = Json.Data(o, "statusUpdatedAt") ?? Json.Data(o, "updatedAt") ?? DateTime.UtcNow;

                    string antes;
                    statusAnterior.TryGetValue(id, out antes);
                    if (antes != status)
                    {
                        if ((antes == "busy" || antes == "waiting") && status == "idle") terminou = true;
                        statusAnterior[id] = status;
                    }

                    lock (Estado.Trava)
                    {
                        Sessao s;
                        if (!Estado.Claude.Sessoes.TryGetValue(id, out s))
                        {
                            s = new Sessao { Id = id };
                            Estado.Claude.Sessoes[id] = s;
                            mudou = true;
                        }
                        s.Pasta = Json.Str(o, "cwd") ?? s.Pasta;
                        s.Nome = Json.Str(o, "name");
                        s.Origem = Json.Str(o, "entrypoint");
                        s.PorHook = true;
                        Atividade novo;
                        if (status == "busy") novo = Atividade.Trabalhando;
                        else if (status == "waiting") novo = Atividade.Aguardando;
                        else novo = antes == "busy" || antes == "waiting" || s.Estado == Atividade.Concluida ? Atividade.Concluida : Atividade.Ociosa;
                        if (s.Estado != novo) { s.Estado = novo; mudou = true; }
                        s.Ultima = quando;
                    }
                }
                lock (Estado.Trava)
                {
                    var sair = new List<string>();
                    foreach (var kv in Estado.Claude.Sessoes) if (kv.Value.PorHook && !vistas.Contains(kv.Key)) sair.Add(kv.Key);
                    foreach (var k in sair) { Estado.Claude.Sessoes.Remove(k); statusAnterior.Remove(k); mudou = true; }
                }
                if (mudou) Estado.Avisar();
                if (terminou)
                {
                    var h = FimDeTurno;
                    if (h != null) h();
                }
            }
            catch (Exception e) { Log.Erro("ler sessões do claude", e); }
            finally { lendo = 0; }
        }

        // O arquivo fica para trás quando o Claude Code fecha à força; confere o processo a cada 10 s
        bool Vivo(int pid)
        {
            DateTime visto;
            if (vivoConferido.TryGetValue(pid, out visto) && (DateTime.UtcNow - visto).TotalSeconds < 10) return true;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) { vivoConferido.Remove(pid); return false; }
                }
                vivoConferido[pid] = DateTime.UtcNow;
                return true;
            }
            catch (ArgumentException) { vivoConferido.Remove(pid); return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { vivoConferido[pid] = DateTime.UtcNow; return true; } // sem acesso, mas existe
        }

        public void Dispose()
        {
            if (relogio != null) relogio.Dispose();
            if (fsw != null) fsw.Dispose();
        }
    }
}
