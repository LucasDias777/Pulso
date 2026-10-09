using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Pulso
{
    // Quanto do uso do Codex veio de cada projeto: os tokens de cada resposta (evento token_count) pela pasta da
    // sessão (session_meta, a primeira linha do arquivo). Índice dos últimos 8 dias em
    // %APPDATA%\Pulso\codex-projetos.json; o atrasado é lido uma vez, depois só o que foi acrescentado.
    // Cada resposta também entra no livro de consumo (Consumo.Codex), com o modelo do turno (turn_context).
    class CodexProjetos : IDisposable
    {
        static readonly TimeSpan Guardar = TimeSpan.FromDays(8);
        const int Versao = 2;                   // 2: com o livro de consumo
        Seguidor seguidor;
        Timer salvar;
        bool mudou;
        readonly object trava = new object();
        readonly Dictionary<string, string> pastas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // arquivo → pasta
        readonly Dictionary<string, double> totais = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);    // arquivo → total_tokens já contado
        readonly Dictionary<string, string> modelos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // arquivo → modelo do turno atual
        Dictionary<string, long> posicoesSalvas = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        static string Arquivo { get { return Path.Combine(Caminhos.Dados, "codex-projetos.json"); } }

        public void Iniciar()
        {
            Consumo.Codex.Carregar();
            Carregar();
            seguidor = new Seguidor(Caminhos.CodexSessoes, AoLinha, TimeSpan.FromMinutes(30), PosicaoInicial);
            seguidor.EmDia += delegate
            {
                int relidos = Consumo.Codex.ConcluirReconstrucao();
                if (relidos > 0) Log.Info("codex: " + relidos + " dias relidos no livro de consumo");
                Salvar();
                Estado.Avisar();
            };
            seguidor.Iniciar();
            salvar = new Timer(delegate { if (mudou) Salvar(); }, null, 60000, 60000);
        }

        // Arquivo já indexado: continua de onde parou. Novo e dos meses que o livro guarda: do começo. Antigo: do fim.
        long? PosicaoInicial(string arq, DateTime modificado, long tamanho)
        {
            long p;
            if (posicoesSalvas.TryGetValue(arq, out p) && p <= tamanho) return p;
            return modificado > Consumo.InicioUtc ? 0 : (long?)null;
        }

        void AoLinha(string arq, string linha)
        {
            if (linha.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0)
            {
                string cwd = Json.Str(Json.Parse(linha), "payload", "cwd");
                if (cwd != null) lock (trava) pastas[arq] = cwd;
                return;
            }
            if (linha.IndexOf("\"turn_context\"", StringComparison.Ordinal) >= 0)
            {
                var t = Json.Parse(linha);
                string modelo = Json.Str(t, "type") == "turn_context" ? Json.Str(t, "payload", "model") : null;
                if (modelo != null) lock (trava) modelos[arq] = modelo;
                return;
            }
            if (linha.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0) return;
            var o = Json.Parse(linha);
            var info = Json.Obj(o, "payload", "info");
            double? total = Json.Num(info, "total_token_usage", "total_tokens");
            var u = Json.Obj(info, "last_token_usage");
            if (!total.HasValue || u == null) return;
            DateTime quando = Json.Data(o, "timestamp") ?? DateTime.UtcNow;
            if (quando < Consumo.InicioUtc) return;
            string modeloDoTurno;
            lock (trava)
            {
                // O mesmo total repetido é o mesmo evento gravado de novo, não uma resposta nova
                double antes;
                if (totais.TryGetValue(arq, out antes) && antes == total.Value) return;
                totais[arq] = total.Value;
                modelos.TryGetValue(arq, out modeloDoTurno);
            }
            double entrada = Json.Num(u, "input_tokens") ?? 0, cache = Json.Num(u, "cached_input_tokens") ?? 0, saida = Json.Num(u, "output_tokens") ?? 0;
            // Peso pela proporção de preço dos modelos do Codex: entrada em cache sai a 1/10, saída a 8×
            double peso = Math.Max(0, entrada - cache) + cache * 0.1 + saida * 8;
            // A entrada do Codex inclui a lida e a gravada em cache; o raciocínio vem somado na saída
            double gravada = Json.Num(u, "cache_write_input_tokens") ?? 0;
            var tokens = new[] { (long)Math.Max(0, entrada - cache - gravada), (long)cache, (long)gravada, (long)saida, (long)(Json.Num(u, "reasoning_output_tokens") ?? 0) };
            string pasta = PastaDe(arq);
            Consumo.Codex.Registrar(quando, modeloDoTurno, pasta, tokens, peso, 0, true);
            mudou = true;
            if (quando >= DateTime.UtcNow - Guardar) Projetos.Codex.Somar(quando, pasta, peso);
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
                var o = File.Exists(Arquivo) ? Json.Parse(File.ReadAllText(Arquivo)) : null;
                // Sem índice ou de versão anterior: refaz do zero, uma vez (relê as sessões dos meses que o livro guarda)
                if ((Json.Num(o, "versao") ?? 1) < Versao) { Log.Info("codex: refazendo o índice de projetos e o livro de consumo"); Consumo.Codex.IniciarReconstrucao(); return; }
                Projetos.Codex.DeJson(Json.Obj(o, "porProjeto"), DateTime.UtcNow - Guardar);
                var p = Json.Obj(o, "posicoes");
                if (p != null) foreach (var kv in p) { double? v = Json.Num(p, kv.Key); if (v.HasValue) posicoesSalvas[kv.Key] = (long)v.Value; }
                var t = Json.Obj(o, "totais");
                if (t != null) foreach (var kv in t) { double? v = Json.Num(t, kv.Key); if (v.HasValue) totais[kv.Key] = v.Value; }
                var m = Json.Obj(o, "modelos");
                if (m != null) foreach (var kv in m) { string v = Json.Str(m, kv.Key); if (v != null) modelos[kv.Key] = v; }
            }
            catch (Exception e) { Log.Erro("ler índice de projetos do Codex", e); }
        }

        void Salvar()
        {
            if (seguidor == null || !seguidor.EstaEmDia) return;
            var livro = Consumo.Codex.Preparar();               // antes das posições (ver Consumo.Preparar)
            try
            {
                mudou = false;
                var p = new Dictionary<string, object>();
                var t = new Dictionary<string, object>();
                var m = new Dictionary<string, object>();
                foreach (var kv in seguidor.Posicoes())
                {
                    if (!File.Exists(kv.Key)) continue;
                    p[kv.Key] = kv.Value;
                    double tot; string modelo;
                    lock (trava)
                    {
                        if (totais.TryGetValue(kv.Key, out tot)) t[kv.Key] = tot;
                        if (modelos.TryGetValue(kv.Key, out modelo)) m[kv.Key] = modelo;
                    }
                }
                Caminhos.GravarAtomico(Arquivo, Json.Write(new Dictionary<string, object>
                {
                    { "versao", Versao }, { "porProjeto", Projetos.Codex.ParaJson(DateTime.UtcNow - Guardar) }, { "posicoes", p }, { "totais", t }, { "modelos", m },
                }));
                livro.Gravar();
            }
            catch (Exception e) { livro.Desistir(); Log.Erro("salvar índice de projetos do Codex", e); }
        }

        public void Dispose()
        {
            if (salvar != null) salvar.Dispose();
            Salvar();
            if (seguidor != null) seguidor.Dispose();
        }
    }
}
