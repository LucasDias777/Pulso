using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pulso
{
    // Lê os registros de sessão do Claude Code (~/.claude/projects/**/*.jsonl, subagentes inclusive).
    // Cada resposta do modelo é gravada na hora com o uso de tokens; daqui sai um índice de custo por minuto
    // (dólares de API equivalentes, pelo preço de cada modelo), dos últimos 8 dias. Com ele:
    //  - a calibração sai de UMA leitura exata: % da janela ÷ custo gasto desde o início da janela;
    //  - o medidor anda a cada resposta entre leituras exatas.
    // O índice e a posição lida de cada arquivo ficam em %APPDATA%\Pulso\custos.json: o atrasado
    // (que pode passar de 1 GB) é lido uma vez só; nas próximas aberturas, só o que foi acrescentado.
    class ClaudeSessoes : IDisposable
    {
        static readonly TimeSpan Guardar = TimeSpan.FromDays(8);
        Seguidor seguidor;
        Timer salvar;
        readonly object trava = new object();
        readonly Dictionary<long, double> porMinuto = new Dictionary<long, double>();   // minuto Unix → custo
        // Cada bloco da mesma resposta repete o "usage"; conta uma vez, pelo maior valor visto
        readonly Dictionary<string, double> custoPorMensagem = new Dictionary<string, double>();
        readonly Queue<string> ordemMensagens = new Queue<string>();
        Dictionary<string, long> posicoesSalvas = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        bool mudouDesdeSalvar;

        public event Action NovoCusto;           // uma resposta recente entrou no índice
        public event Action FimDeTurno;          // uma sessão principal terminou de responder (agora)
        public event Action EmDia;               // índice completo (atrasado lido)
        public bool AtividadePeloArquivo;        // só quando não há o registro ~/.claude/sessions
        public bool Pronto { get { return seguidor != null && seguidor.EstaEmDia; } }

        static string Arquivo { get { return Path.Combine(Caminhos.Dados, "custos.json"); } }

        public void Iniciar()
        {
            Carregar();
            seguidor = new Seguidor(Caminhos.ClaudeProjetos, AoLinha, TimeSpan.FromMinutes(20), PosicaoInicial);
            seguidor.EmDia += delegate
            {
                Log.Info("claude: índice de custo em dia (" + porMinuto.Count + " minutos com uso)");
                Salvar();
                var h = EmDia;
                if (h != null) h();
            };
            seguidor.Iniciar();
            salvar = new Timer(delegate { if (mudouDesdeSalvar) Salvar(); }, null, 30000, 30000);
        }

        // Arquivo já indexado: continua de onde parou. Novo e recente: do começo (entra no índice). Antigo: do fim.
        long? PosicaoInicial(string arq, DateTime modificado, long tamanho)
        {
            long p;
            if (posicoesSalvas.TryGetValue(arq, out p) && p <= tamanho) return p;
            return modificado > DateTime.UtcNow - Guardar ? 0 : (long?)null;
        }

        void AoLinha(string arq, string linha)
        {
            // Filtro barato antes de interpretar: linhas de anexo/ferramenta podem ter centenas de KB
            bool assistente = linha.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) >= 0;
            bool usuario = !assistente && AtividadePeloArquivo && linha.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0;
            if (!assistente && !usuario) return;

            var o = Json.Parse(linha);
            if (o == null) return;
            DateTime quando = Json.Data(o, "timestamp") ?? DateTime.UtcNow;
            bool recente = (DateTime.UtcNow - quando).TotalMinutes < 2;
            string sessao = Json.Str(o, "sessionId") ?? Path.GetFileNameWithoutExtension(arq);
            string cwd = Json.Str(o, "cwd");
            bool lateral = Json.Bool(o, "isSidechain") == true || arq.IndexOf("subagents", StringComparison.OrdinalIgnoreCase) >= 0;

            if (assistente)
            {
                var msg = Json.Obj(o, "message");
                var uso = Json.Obj(msg, "usage");
                if (uso != null) Somar(Json.Str(msg, "id") + "|" + Json.Str(o, "requestId"), Json.Str(msg, "model"), uso, quando, recente);
                string parada = Json.Str(msg, "stop_reason");
                if (!recente || lateral) return;
                if (AtividadePeloArquivo) Marcar(sessao, cwd, parada == "end_turn" ? Atividade.Concluida : Atividade.Trabalhando);
                if (parada == "end_turn")
                {
                    var h = FimDeTurno;
                    if (h != null) h();
                }
            }
            else if (recente && !lateral) Marcar(sessao, cwd, Atividade.Trabalhando);
        }

        void Somar(string chave, string modelo, IDictionary<string, object> uso, DateTime quando, bool recente)
        {
            double custo = Custo(modelo, uso);
            if (custo <= 0 || quando < DateTime.UtcNow - Guardar) return;
            lock (trava)
            {
                double antes, delta;
                if (custoPorMensagem.TryGetValue(chave, out antes))
                {
                    if (custo <= antes) return;
                    delta = custo - antes;
                }
                else
                {
                    delta = custo;
                    ordemMensagens.Enqueue(chave);
                    if (ordemMensagens.Count > 4000) custoPorMensagem.Remove(ordemMensagens.Dequeue());
                }
                custoPorMensagem[chave] = custo;
                long min = Tempo.UnixMs(quando) / 60000;
                double v;
                porMinuto.TryGetValue(min, out v);
                porMinuto[min] = v + delta;
                mudouDesdeSalvar = true;
            }
            if (recente)
            {
                var h = NovoCusto;
                if (h != null) h();
            }
        }

        // Custo gasto a partir de um instante (resolução de 1 minuto)
        public double CustoDesde(DateTime inicioUtc)
        {
            long ini = Tempo.UnixMs(inicioUtc) / 60000;
            double soma = 0;
            lock (trava) foreach (var kv in porMinuto) if (kv.Key >= ini) soma += kv.Value;
            return soma;
        }

        public double CustoEntre(DateTime inicioUtc, DateTime fimUtc)
        {
            long ini = Tempo.UnixMs(inicioUtc) / 60000, fim = Tempo.UnixMs(fimUtc) / 60000;
            double soma = 0;
            lock (trava) foreach (var kv in porMinuto) if (kv.Key >= ini && kv.Key <= fim) soma += kv.Value;
            return soma;
        }

        void Carregar()
        {
            try
            {
                if (!File.Exists(Arquivo)) return;
                var o = Json.Parse(File.ReadAllText(Arquivo));
                long corte = Tempo.UnixMs(DateTime.UtcNow - Guardar) / 60000;
                var m = Json.Obj(o, "porMinuto");
                if (m != null)
                    foreach (var kv in m)
                    {
                        long min; double? v = Json.Num(m, kv.Key);
                        if (long.TryParse(kv.Key, out min) && min >= corte && v.HasValue) porMinuto[min] = v.Value;
                    }
                var p = Json.Obj(o, "posicoes");
                if (p != null)
                    foreach (var kv in p)
                    {
                        double? v = Json.Num(p, kv.Key);
                        if (v.HasValue) posicoesSalvas[kv.Key] = (long)v.Value;
                    }
            }
            catch (Exception e) { Log.Erro("ler índice de custo", e); }
        }

        void Salvar()
        {
            if (seguidor == null || !seguidor.EstaEmDia) return; // índice pela metade não vale salvar como se estivesse completo
            try
            {
                var m = new Dictionary<string, object>();
                long corte = Tempo.UnixMs(DateTime.UtcNow - Guardar) / 60000;
                lock (trava)
                {
                    foreach (var k in porMinuto.Keys.Where(k => k < corte).ToList()) porMinuto.Remove(k);
                    foreach (var kv in porMinuto) m[kv.Key.ToString()] = Math.Round(kv.Value, 6);
                    mudouDesdeSalvar = false;
                }
                var p = new Dictionary<string, object>();
                foreach (var kv in seguidor.Posicoes()) if (File.Exists(kv.Key)) p[kv.Key] = kv.Value;
                Caminhos.GravarAtomico(Arquivo, Json.Write(new Dictionary<string, object> { { "porMinuto", m }, { "posicoes", p } }));
            }
            catch (Exception e) { Log.Erro("salvar índice de custo", e); }
        }

        void Marcar(string sessao, string cwd, Atividade a)
        {
            bool mudou;
            lock (Estado.Trava)
            {
                Sessao s;
                if (!Estado.Claude.Sessoes.TryGetValue(sessao, out s))
                {
                    s = new Sessao { Id = sessao };
                    Estado.Claude.Sessoes[sessao] = s;
                }
                if (cwd != null) s.Pasta = cwd;
                mudou = s.Estado != a;
                s.Estado = a;
                s.Ultima = DateTime.UtcNow;
            }
            if (mudou) Estado.Avisar();
        }

        // Custo em dólares de API equivalentes. Os limites da assinatura não são publicados em dólar,
        // mas a proporção entre modelos segue o preço; a escala é calibrada contra as leituras exatas.
        static double Custo(string modelo, IDictionary<string, object> uso)
        {
            double entrada, saida, leitura;
            Precos(modelo ?? "", out entrada, out saida, out leitura);
            double inp = Json.Num(uso, "input_tokens") ?? 0;
            double outp = Json.Num(uso, "output_tokens") ?? 0;
            double lido = Json.Num(uso, "cache_read_input_tokens") ?? 0;
            double criado5 = Json.Num(uso, "cache_creation", "ephemeral_5m_input_tokens") ?? -1;
            double criado1h = Json.Num(uso, "cache_creation", "ephemeral_1h_input_tokens") ?? 0;
            if (criado5 < 0) criado5 = Json.Num(uso, "cache_creation_input_tokens") ?? 0;
            return (inp * entrada + outp * saida + lido * leitura + criado5 * entrada * 1.25 + criado1h * entrada * 2) / 1e6;
        }

        // US$ por milhão de tokens: entrada, saída, leitura de cache
        static void Precos(string m, out double entrada, out double saida, out double leitura)
        {
            m = m.ToLowerInvariant();
            if (m.Contains("fable") || m.Contains("mythos")) { entrada = 10; saida = 50; leitura = 0.25; }
            else if (m.Contains("opus-5-5")) { entrada = 4; saida = 20; leitura = 0.20; }
            else if (m.Contains("opus-4-1") || m.Contains("opus-4-2025") || m.Contains("opus-4-0") || m == "claude-opus-4") { entrada = 15; saida = 75; leitura = 1.5; }
            else if (m.Contains("opus")) { entrada = 5; saida = 25; leitura = 0.5; }
            else if (m.Contains("sonnet-5")) { entrada = 2; saida = 10; leitura = 0.20; }
            else if (m.Contains("sonnet")) { entrada = 3; saida = 15; leitura = 0.30; }
            else if (m.Contains("haiku")) { entrada = 1; saida = 5; leitura = 0.10; }
            else if (m.Contains("synthetic") || m.Length == 0) { entrada = 0; saida = 0; leitura = 0; }
            else { entrada = 4; saida = 20; leitura = 0.20; }
        }

        public void Dispose()
        {
            if (salvar != null) salvar.Dispose();
            Salvar();
            if (seguidor != null) seguidor.Dispose();
        }
    }
}
