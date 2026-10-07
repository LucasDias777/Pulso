using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pulso
{
    // Junta as fontes do Claude:
    //  - leitura exata (servidor, ou barra de status do Claude Code no terminal) = âncora;
    //  - calibração por janela: k = % da janela ÷ custo local gasto desde o início dela (UMA leitura basta);
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

        readonly ClaudeSessoes transcritos = new ClaudeSessoes();
        readonly ClaudeRegistro registro = new ClaudeRegistro();
        ClaudeServidor servidor;
        readonly Dictionary<string, Ancora> ancoras = new Dictionary<string, Ancora>();
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
                    if (transcritos.Pronto) { a.CustoJanela = transcritos.CustoDesde(a.Inicio); Calibrar(kv.Key, a); }
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

        // Índice completo: as âncoras de antes ganham o custo da janela e calibram
        void IndiceEmDia()
        {
            lock (travaAncoras)
                foreach (var kv in ancoras)
                {
                    if (!double.IsNaN(kv.Value.CustoJanela)) continue;
                    kv.Value.CustoJanela = transcritos.CustoEntre(kv.Value.Inicio, kv.Value.Quando);
                    // Leitura velha calibraria com dado vencido; só a recente (de até 30 min) ensina
                    if ((DateTime.UtcNow - kv.Value.Quando).TotalMinutes <= 30) Calibrar(kv.Key, kv.Value);
                }
            Recalcular();
        }

        static int Ordem(string id)
        {
            switch (id) { case "session": return 0; case "weekly_all": return 1; case "weekly_opus": return 2; case "weekly_sonnet": return 3; default: return 9; }
        }

        // k = % da janela ÷ custo gasto nela. Com uso pequeno o ruído domina (o servidor dá % inteiro): exige mínimo.
        void Calibrar(string id, Ancora a)
        {
            if (a.Usado < 0.03 || double.IsNaN(a.CustoJanela) || a.CustoJanela < 0.5) return;
            double kNovo = a.Usado / a.CustoJanela;
            double k0;
            lock (EstadoSalvo.Calibracao) EstadoSalvo.Calibracao.TryGetValue(id, out k0);
            double k = k0 > 0 ? k0 * 0.4 + kNovo * 0.6 : kNovo;
            lock (EstadoSalvo.Calibracao) EstadoSalvo.Calibracao[id] = k;
            Log.Info("claude: calibração " + id + " = " + k.ToString("0.00000") + " (" + Math.Round(a.Usado * 100) + "% / US$" + a.CustoJanela.ToString("0.00") + " desde " + a.Inicio.ToLocalTime().ToString("dd/MM HH:mm") + ")");
        }

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
