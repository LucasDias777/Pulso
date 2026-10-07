using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Pulso
{
    // Quanto do uso do Codex veio de cada projeto: os tokens de cada resposta (evento token_count) pela pasta da
    // sessão (session_meta, a primeira linha do arquivo). Índice dos últimos 8 dias em
    // %APPDATA%\Pulso\codex-projetos.json; o atrasado é lido uma vez, depois só o que foi acrescentado.
    class CodexProjetos : IDisposable
    {
        static readonly TimeSpan Guardar = TimeSpan.FromDays(8);
        Seguidor seguidor;
        Timer salvar;
        bool mudou;
        readonly object trava = new object();
        readonly Dictionary<string, string> pastas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // arquivo → pasta
        readonly Dictionary<string, double> totais = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);    // arquivo → total_tokens já contado
        Dictionary<string, long> posicoesSalvas = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        static string Arquivo { get { return Path.Combine(Caminhos.Dados, "codex-projetos.json"); } }

        public void Iniciar()
        {
            Carregar();
            seguidor = new Seguidor(Caminhos.CodexSessoes, AoLinha, TimeSpan.FromMinutes(30), PosicaoInicial);
            seguidor.EmDia += delegate { Salvar(); Estado.Avisar(); };
            seguidor.Iniciar();
            salvar = new Timer(delegate { if (mudou) Salvar(); }, null, 60000, 60000);
        }

        long? PosicaoInicial(string arq, DateTime modificado, long tamanho)
        {
            long p;
            if (posicoesSalvas.TryGetValue(arq, out p) && p <= tamanho) return p;
            return modificado > DateTime.UtcNow - Guardar ? 0 : (long?)null;
        }

        void AoLinha(string arq, string linha)
        {
            if (linha.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0)
            {
                string cwd = Json.Str(Json.Parse(linha), "payload", "cwd");
                if (cwd != null) lock (trava) pastas[arq] = cwd;
                return;
            }
            if (linha.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0) return;
            var o = Json.Parse(linha);
            var info = Json.Obj(o, "payload", "info");
            double? total = Json.Num(info, "total_token_usage", "total_tokens");
            var u = Json.Obj(info, "last_token_usage");
            if (!total.HasValue || u == null) return;
            DateTime quando = Json.Data(o, "timestamp") ?? DateTime.UtcNow;
            if (quando < DateTime.UtcNow - Guardar) return;
            lock (trava)
            {
                // O mesmo total repetido é o mesmo evento gravado de novo, não uma resposta nova
                double antes;
                if (totais.TryGetValue(arq, out antes) && antes == total.Value) return;
                totais[arq] = total.Value;
            }
            double entrada = Json.Num(u, "input_tokens") ?? 0, cache = Json.Num(u, "cached_input_tokens") ?? 0, saida = Json.Num(u, "output_tokens") ?? 0;
            // Peso pela proporção de preço dos modelos do Codex: entrada em cache sai a 1/10, saída a 8×
            double peso = Math.Max(0, entrada - cache) + cache * 0.1 + saida * 8;
            Projetos.Codex.Somar(quando, PastaDe(arq), peso);
            mudou = true;
        }

        string PastaDe(string arq)
        {
            lock (trava) { string p; if (pastas.TryGetValue(arq, out p)) return p; }
            try
            {
                using (var fs = new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string cwd = Json.Str(Json.Parse(sr.ReadLine()), "payload", "cwd");
                    if (cwd != null) lock (trava) pastas[arq] = cwd;
                    return cwd;
                }
            }
            catch { return null; }
        }

        void Carregar()
        {
            try
            {
                if (!File.Exists(Arquivo)) return;
                var o = Json.Parse(File.ReadAllText(Arquivo));
                Projetos.Codex.DeJson(Json.Obj(o, "porProjeto"), DateTime.UtcNow - Guardar);
                var p = Json.Obj(o, "posicoes");
                if (p != null) foreach (var kv in p) { double? v = Json.Num(p, kv.Key); if (v.HasValue) posicoesSalvas[kv.Key] = (long)v.Value; }
                var t = Json.Obj(o, "totais");
                if (t != null) foreach (var kv in t) { double? v = Json.Num(t, kv.Key); if (v.HasValue) totais[kv.Key] = v.Value; }
            }
            catch (Exception e) { Log.Erro("ler índice de projetos do Codex", e); }
        }

        void Salvar()
        {
            if (seguidor == null || !seguidor.EstaEmDia) return;
            try
            {
                mudou = false;
                var p = new Dictionary<string, object>();
                var t = new Dictionary<string, object>();
                foreach (var kv in seguidor.Posicoes())
                {
                    if (!File.Exists(kv.Key)) continue;
                    p[kv.Key] = kv.Value;
                    double tot;
                    lock (trava) if (totais.TryGetValue(kv.Key, out tot)) t[kv.Key] = tot;
                }
                Caminhos.GravarAtomico(Arquivo, Json.Write(new Dictionary<string, object>
                {
                    { "porProjeto", Projetos.Codex.ParaJson(DateTime.UtcNow - Guardar) }, { "posicoes", p }, { "totais", t },
                }));
            }
            catch (Exception e) { Log.Erro("salvar índice de projetos do Codex", e); }
        }

        public void Dispose()
        {
            if (salvar != null) salvar.Dispose();
            Salvar();
            if (seguidor != null) seguidor.Dispose();
        }
    }
}
