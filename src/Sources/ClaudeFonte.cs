using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pulso
{
    // Junta as fontes do Claude:
    //  - leitura exata (servidor, ou barra de status do Claude Code no terminal) = âncora;
    //  - calibração por janela: k = pontos que a janela subiu ÷ custo gasto NESTE computador entre duas leituras exatas;
    //  - estimativa = % exato + k × custo gasto depois da leitura — anda a cada resposta, sem esperar o servidor.
    class ClaudeFonte : IDisposable
    {
        class Ancora
        {
            public double Usado;
            public DateTime Inicio;         // início da janela (renova − duração) ou o momento da leitura
            public DateTime Quando;
            public double CustoJanela = double.NaN; // custo local de Inicio até a leitura (NaN = índice ainda não pronto)
        }

        // Ponto de partida da calibração de cada janela: a leitura exata e o custo ao vivo deste computador até ela
        class Referencia { public double Usado, Custo; public DateTime? Reset; }

        readonly ClaudeSessoes transcritos = new ClaudeSessoes();
        readonly ClaudeRegistro registro = new ClaudeRegistro();
        ClaudeServidor servidor;
        readonly Dictionary<string, Ancora> ancoras = new Dictionary<string, Ancora>();
        readonly Dictionary<string, Referencia> referencias = new Dictionary<string, Referencia>(); // sob travaAncoras
        readonly object travaAncoras = new object();
        Timer relogio;

        public void Iniciar()
        {
            servidor = new ClaudeServidor(HaSessaoAtiva);
            EstadoSalvo.EsperaClaude = () => servidor.EsperaAte;
            servidor.LeituraExata += janelas => AplicarExata(janelas, "servidor", false);

            transcritos.AtividadePeloArquivo = !registro.Disponivel;
            transcritos.NovoCusto += Recalcular;
            // Fim de turno: o consumo acabou de mudar; pede a leitura exata logo (o servidor leva alguns segundos)
            transcritos.FimDeTurno += delegate { servidor.Pedir(TimeSpan.FromSeconds(4)); };
            registro.FimDeTurno += delegate { servidor.Pedir(TimeSpan.FromSeconds(4)); };
            transcritos.EmDia += IndiceEmDia;

            // Âncoras iniciais = últimas leituras salvas (o custo delas é calculado quando o índice ficar pronto)
            lock (Estado.Trava)
                foreach (var j in Estado.Claude.Janelas)
                    ancoras[j.Id] = NovaAncora(j, Estado.Claude.Confirmado ?? DateTime.UtcNow);

            registro.Iniciar();
            transcritos.Iniciar();
            DateTime? ultima;
            lock (Estado.Trava) ultima = Estado.Claude.Fonte == "servidor" ? Estado.Claude.Confirmado : null;
            servidor.Iniciar(EstadoSalvo.ClaudeEsperaAte, ultima);
            relogio = new Timer(delegate { Virada(); }, null, 2000, 2000);
        }

        static Ancora NovaAncora(Janela j, DateTime quando)
        {
            var inicio = j.ResetaEm.HasValue && j.Minutos > 0 ? j.ResetaEm.Value.AddMinutes(-j.Minutos) : quando;
            if (inicio > quando) inicio = quando;
            return new Ancora { Usado = j.Usado, Inicio = inicio, Quando = quando };
        }

        bool HaSessaoAtiva()
        {
            lock (Estado.Trava)
                return Estado.Claude.Sessoes.Values.Any(s => s.Estado == Atividade.Trabalhando || s.Estado == Atividade.Aguardando
                    || (DateTime.UtcNow - s.Ultima).TotalMinutes < 10);
        }

        public void Atualizar() { servidor.Pedir(TimeSpan.Zero); }

        // Barra de status do Claude Code (terminal): valores exatos a cada resposta, só 5h e semana
        public void DaBarraDeStatus(object rl)
        {
            var r = new Dictionary<string, Janela>();
            Adicionar(r, Json.Obj(rl, "five_hour"), "session", "Sessão (5h)", 300, false);
            Adicionar(r, Json.Obj(rl, "seven_day"), "weekly_all", "Semana", 10080, true);
            if (r.Count > 0) AplicarExata(r, "barra de status", true);
        }

        static void Adicionar(Dictionary<string, Janela> r, IDictionary<string, object> w, string id, string rotulo, int min, bool semanal)
        {
            double? pct = Json.Num(w, "used_percentage");
            if (w == null || !pct.HasValue) return;
            r[id] = new Janela { Id = id, Rotulo = rotulo, Minutos = min, Semanal = semanal, Usado = Math.Max(0, Math.Min(1, pct.Value / 100)), ResetaEm = Json.Data(w, "resets_at") };
        }

        void AplicarExata(Dictionary<string, Janela> janelas, string fonte, bool mesclar)
        {
            var agora = DateTime.UtcNow;
            lock (travaAncoras)
                foreach (var kv in janelas)
                {
                    var a = NovaAncora(kv.Value, agora);
                    if (transcritos.Pronto) { a.CustoJanela = transcritos.CustoDesde(a.Inicio); Calibrar(kv.Key, kv.Value); }
                    ancoras[kv.Key] = a;
                }
            lock (Estado.Trava)
            {
                var p = Estado.Claude;
                if (mesclar)
                {
                    foreach (var kv in janelas)
                    {
                        var j = p.PorId(kv.Key);
                        if (j == null) p.Janelas.Add(kv.Value);
                        else { j.Usado = kv.Value.Usado; j.ResetaEm = kv.Value.ResetaEm ?? j.ResetaEm; j.Estimado = null; }
                    }
                }
                else
                {
                    p.Janelas.Clear();
                    p.Janelas.AddRange(janelas.Values);
                }
                p.Janelas.Sort((x, y) => Ordem(x.Id).CompareTo(Ordem(y.Id)));
                p.Confirmado = agora;
                p.Fonte = fonte;
                p.Erro = null;
                p.Nota = null;
            }
            Recalcular();
            EstadoSalvo.Gravar();
        }

        // Índice completo: as âncoras de antes ganham o custo da janela (a estimativa volta a andar com o k salvo);
        // a calibração começa na próxima leitura exata, que vira o ponto de partida
        void IndiceEmDia()
        {
            lock (travaAncoras)
                foreach (var kv in ancoras)
                    if (double.IsNaN(kv.Value.CustoJanela)) kv.Value.CustoJanela = transcritos.CustoEntre(kv.Value.Inicio, kv.Value.Quando);
            Recalcular();
        }

        static int Ordem(string id)
        {
            switch (id) { case "session": return 0; case "weekly_all": return 1; case "weekly_opus": return 2; case "weekly_sonnet": return 3; default: return 9; }
        }

        // k = pontos que a janela subiu ÷ custo gasto neste computador entre duas leituras exatas (chamado sob travaAncoras).
        // O mesmo login usado em outro computador sobe o % sem custo aqui: medir desde o início da janela punha tudo na
        // conta deste PC e inflava k (de 0,008 para 0,45; o anel ia a ~89% com 60% exatos). Entre duas leituras quem usa
        // é um computador por vez, então o salto feito no outro fica dentro da leitura exata e não ensina nada.
        void Calibrar(string id, Janela j)
        {
            double custo = transcritos.CustoAoVivo;
            Referencia r;
            referencias.TryGetValue(id, out r);
            // Primeira leitura, janela renovada ou outra janela: só marca o ponto de partida
            if (r == null || j.Usado < r.Usado - 0.005 || (j.ResetaEm.HasValue && r.Reset.HasValue && Math.Abs((j.ResetaEm.Value - r.Reset.Value).TotalMinutes) > 60))
            {
                referencias[id] = Ref(j, custo);
                return;
            }
            double sobe = j.Usado - r.Usado, gasto = custo - r.Custo;
            double k0;
            lock (EstadoSalvo.Calibracao) EstadoSalvo.Calibracao.TryGetValue(id, out k0);
            // Subiu mais do que o gasto daqui explica: uso de outro computador; recomeça desta leitura
            if (gasto < 0.05 ? sobe >= 0.02 : k0 > 0 && sobe > 3 * k0 * gasto + 0.02)
            {
                Log.Info("claude: " + id + " subiu " + Math.Round(sobe * 100) + " pontos com US$" + gasto.ToString("0.00") + " gastos aqui (uso de outro computador); calibração mantida");
                referencias[id] = Ref(j, custo);
                return;
            }
            // O servidor dá % inteiro: espera 3 pontos e um gasto que não seja ruído (sem k anterior, ao menos US$ 0,50)
            if (sobe < 0.03 || gasto < (k0 > 0 ? 0.1 : 0.5)) return;
            referencias[id] = Ref(j, custo);
            double kNovo = sobe / gasto, k;
            if (k0 <= 0 || kNovo < k0 / 3) k = kNovo;   // primeira, ou a anterior estava inflada: troca de vez
            else if (kNovo > k0 * 3) return;             // salto que o uso de fora não explicou: não ensina
            else k = k0 * 0.4 + kNovo * 0.6;
            lock (EstadoSalvo.Calibracao) EstadoSalvo.Calibracao[id] = k;
            Log.Info("claude: calibração " + id + " = " + k.ToString("0.00000") + " (+" + Math.Round(sobe * 100) + " pontos / US$" + gasto.ToString("0.00") + " gastos aqui)");
        }

        static Referencia Ref(Janela j, double custo) { return new Referencia { Usado = j.Usado, Custo = custo, Reset = j.ResetaEm }; }

        // Estimativa = % exato + k × custo gasto depois da leitura, na mesma janela
        void Recalcular()
        {
            bool usar = Config.Atual.Estimativa;
            var extras = new Dictionary<string, double>();
            lock (travaAncoras)
                foreach (var kv in ancoras)
                {
                    double k;
                    lock (EstadoSalvo.Calibracao) EstadoSalvo.Calibracao.TryGetValue(kv.Key, out k);
                    if (!usar || k <= 0 || double.IsNaN(kv.Value.CustoJanela)) continue;
                    extras[kv.Key] = k * Math.Max(0, transcritos.CustoDesde(kv.Value.Inicio) - kv.Value.CustoJanela);
                }
            lock (Estado.Trava)
            {
                foreach (var j in Estado.Claude.Janelas)
                {
                    double extra;
                    // Teto: a estimativa nunca anda mais que 30 pontos sem confirmação
                    j.Estimado = extras.TryGetValue(j.Id, out extra) && extra > 0.0005 ? Math.Min(1, j.Usado + Math.Min(0.30, extra)) : (double?)null;
                }
                Estado.Claude.RitmoHora = Ritmo();
            }
            Estado.Avisar();
        }

        // Pontos por hora na janela de 5h, pelo custo dos últimos 30 min
        double? Ritmo()
        {
            double k;
            lock (EstadoSalvo.Calibracao) if (!EstadoSalvo.Calibracao.TryGetValue("session", out k)) return null;
            double c = transcritos.CustoDesde(DateTime.UtcNow.AddMinutes(-30));
            return c > 0 ? k * c * 2 : (double?)null;
        }

        // Janela que passou do horário de renovação volta a zero na hora, sem esperar o servidor
        void Virada()
        {
            bool mudou = false;
            var agora = DateTime.UtcNow;
            lock (Estado.Trava)
            {
                foreach (var j in Estado.Claude.Janelas)
                {
                    if (!j.ResetaEm.HasValue || j.ResetaEm.Value > agora) continue;
                    j.Usado = 0; j.Estimado = null; j.ResetaEm = null;
                    lock (travaAncoras)
                        ancoras[j.Id] = new Ancora { Usado = 0, Inicio = agora, Quando = agora, CustoJanela = transcritos.Pronto ? transcritos.CustoDesde(agora) : double.NaN };
                    mudou = true;
                }
                // Sessão concluída some da lista depois de 30 min
                foreach (var s in Estado.Claude.Sessoes.Values)
                    if (s.Estado == Atividade.Concluida && (agora - s.Ultima).TotalMinutes > 30) { s.Estado = Atividade.Ociosa; mudou = true; }
            }
            if (mudou)
            {
                servidor.Pedir(TimeSpan.FromSeconds(20));
                Estado.Avisar();
            }
        }

        public void Dispose()
        {
            if (relogio != null) relogio.Dispose();
            transcritos.Dispose();
            registro.Dispose();
            if (servidor != null) servidor.Dispose();
        }
    }
}
