using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulso
{
    // Avisos no cartão ao lado do notch:
    //  - limite: 80%, 95% e 100% (uma vez por janela), só com valor exato — nunca pela estimativa;
    //  - renovação: a janela voltou depois de pelo menos 10% de uso;
    //  - ritmo: entre 30% e 80% da sessão de 5h, se no ritmo dos últimos 30 min ela acaba 20 min ou mais
    //    antes de renovar (uma vez por sessão);
    //  - sessão terminou / está esperando você: só transições vistas ao vivo,
    //    turnos de pelo menos 10 s, e nunca de uma sessão cuja janela você já está olhando.
    class Avisos
    {
        class Visto { public double Uso; public DateTime? Reset; }

        readonly Action<Aviso> mostrar;
        readonly Dictionary<string, Visto> visto = new Dictionary<string, Visto>();
        readonly HashSet<string> avisados = new HashSet<string>();
        readonly Dictionary<string, DateTime> ultimaRenovacao = new Dictionary<string, DateTime>();
        readonly Dictionary<string, Atividade> estadoSessao = new Dictionary<string, Atividade>();
        readonly Dictionary<string, DateTime> trabalhandoDesde = new Dictionary<string, DateTime>();
        readonly Dictionary<string, DateTime> ritmoAvisado = new Dictionary<string, DateTime>();   // janela → renovação já avisada
        static readonly double[] Limiares = { 0.80, 0.95, 1.0 };
        // Leitura restaurada do disco ao abrir não serve de "antes": comparar com ela inventa renovação
        readonly DateTime abertura = DateTime.UtcNow;

        public Avisos(Action<Aviso> mostrar)
        {
            this.mostrar = mostrar;
            Estado.Mudou += Conferir;
        }

        void Conferir()
        {
            var cfg = Config.Atual;
            var pal = Paleta.Atual;
            foreach (var id in cfg.Provedores)
            {
                var p = Estado.Copia(id);
                if (p.Confirmado.HasValue && p.Confirmado.Value >= abertura) Janelas(p, cfg, pal);
                if (cfg.AvisoRitmo) RitmoAlto(p, pal);
                Sessoes(p, cfg, pal);
            }
        }

        void Janelas(Provedor p, Config cfg, Paleta pal)
        {
            // O ritmo (RitmoHora) é o da janela principal: projetá-lo na semana dizia que ela esgotava em horas
            var principal = p.Principal;
            foreach (var j in p.Janelas)
            {
                string chave = p.Id + "|" + j.Id;
                Visto antes;
                visto.TryGetValue(chave, out antes);
                double uso = j.Usado;
                if (antes != null)
                {
                    if (cfg.AvisoLimite)
                        foreach (var lim in Limiares)
                        {
                            string marca = chave + "|" + (j.ResetaEm.HasValue ? j.ResetaEm.Value.ToString("yyyyMMddHH") : "-") + "|" + lim;
                            if (uso < lim || antes.Uso >= lim || !avisados.Add(marca)) continue;
                            int pct = (int)Math.Round(Math.Min(1, uso) * 100);
                            mostrar(new Aviso
                            {
                                Provedor = p.Id,
                                Titulo = lim >= 1 ? "Limite do {0} atingido".T(p.Nome) : "{0} em {1}%".T(p.Nome, pct),
                                Subtitulo = j.Rotulo.T(),
                                Status = lim >= 1 ? "Sem cota até renovar".T() : "{0}% restante".T(100 - pct),
                                CorStatus = lim >= 0.95 ? pal.Critico : pal.Atencao,
                                Proxima = Juntar(Texto.Renova(j.ResetaEm), lim < 1 && j == principal ? Texto.Esgota(p.RitmoHora, j) : null),
                                Som = Som.Limite,
                            });
                        }
                    if (cfg.AvisoRenovacao && antes.Uso >= 0.10 && uso <= antes.Uso - 0.10)
                    {
                        DateTime ult;
                        if (!ultimaRenovacao.TryGetValue(chave, out ult) || (DateTime.UtcNow - ult).TotalMinutes > 15)
                        {
                            ultimaRenovacao[chave] = DateTime.UtcNow;
                            mostrar(new Aviso
                            {
                                Provedor = p.Id,
                                Titulo = "{0} renovado".T(p.Nome),
                                Subtitulo = "{0} renovada".T(j.Rotulo.T()),
                                Status = "Cota disponível · {0}%".T((int)Math.Round(uso * 100)),
                                Proxima = j.ResetaEm.HasValue ? Texto.Renova(j.ResetaEm) : null,
                                Som = Som.Renovacao,
                            });
                        }
                    }
                }
                visto[chave] = new Visto { Uso = uso, Reset = j.ResetaEm };
            }
        }

        // Projeção pela velocidade recente (pontos por hora): dá tempo de desacelerar antes dos avisos de 80%/95%
        void RitmoAlto(Provedor p, Paleta pal)
        {
            var j = p.Janelas.FirstOrDefault(x => x.Minutos == 300 && !x.Semanal);
            if (j == null || !j.ResetaEm.HasValue || !p.RitmoHora.HasValue || p.RitmoHora.Value <= 0 || !p.Confirmado.HasValue) return;
            var agora = DateTime.UtcNow;
            if ((agora - p.Confirmado.Value).TotalMinutes > 30) return; // leitura velha não projeta nada
            double atual = Math.Min(1, j.Atual);
            if (atual < 0.30 || atual >= 0.80) return;                  // cedo demais / o aviso de 80% já cobre
            var esgota = agora.AddHours((1 - atual) / p.RitmoHora.Value);
            var sobra = j.ResetaEm.Value - esgota;
            if (sobra.TotalMinutes < 20) return;
            string chave = p.Id + "|" + j.Id;
            DateTime avisada;
            // A renovação do Claude oscila alguns segundos entre leituras: mesma sessão se perto da já avisada
            if (ritmoAvisado.TryGetValue(chave, out avisada) && Math.Abs((avisada - j.ResetaEm.Value).TotalMinutes) < 60) return;
            ritmoAvisado[chave] = j.ResetaEm.Value;
            bool estimado = j.Estimado.HasValue && j.Estimado.Value > j.Usado + 0.004;
            mostrar(new Aviso
            {
                Provedor = p.Id,
                Titulo = "{0} em ritmo alto".T(p.Nome),
                Subtitulo = "{0} · {1} usado".T(j.Rotulo.T(), Texto.Pct(atual, estimado)),
                Status = "No ritmo atual, acaba às {0}".T(esgota.ToLocalTime().ToString("HH:mm")),
                CorStatus = pal.Atencao,
                Proxima = "{0} antes de renovar · +{1} pts/h".T(Tempo.Duracao(sobra), Math.Max(1, (int)Math.Round(p.RitmoHora.Value * 100))),
                Som = Som.Limite,
            });
        }

        void Sessoes(Provedor p, Config cfg, Paleta pal)
        {
            var agora = DateTime.UtcNow;
            var vivas = new HashSet<string>();
            foreach (var s in p.Sessoes.Values)
            {
                string chave = p.Id + "|" + s.Id;
                vivas.Add(chave);
                Atividade antes;
                bool conhecida = estadoSessao.TryGetValue(chave, out antes);
                estadoSessao[chave] = s.Estado;
                if (s.Estado == Atividade.Trabalhando && (!conhecida || antes != Atividade.Trabalhando)) trabalhandoDesde[chave] = agora;
                // Primeira vez que a sessão aparece (inclusive ao abrir o Pulso): só registra
                if (!conhecida || antes == s.Estado) continue;
                var sessao = s;
                Action irPara = delegate { Foco.Trazer(sessao.Pasta); };

                if (antes == Atividade.Trabalhando && s.Estado == Atividade.Concluida && cfg.AvisoSessaoFim)
                {
                    DateTime desde;
                    var dur = trabalhandoDesde.TryGetValue(chave, out desde) ? agora - desde : TimeSpan.Zero;
                    if (dur.TotalSeconds < 10 || EmFoco(s.Pasta)) continue;
                    mostrar(new Aviso
                    {
                        Provedor = p.Id, Titulo = "{0} terminou".T(p.Nome), Subtitulo = s.Titulo,
                        Status = "Concluída em {0}".T(Tempo.Duracao(dur)),
                        Proxima = "Clique para abrir a janela".T(), Som = Som.Concluida, Clique = irPara,
                    });
                }
                else if (s.Estado == Atividade.Aguardando && cfg.AvisoSessaoEspera && !EmFoco(s.Pasta))
                {
                    mostrar(new Aviso
                    {
                        Provedor = p.Id, Titulo = "{0} está esperando você".T(p.Nome), Subtitulo = s.Titulo,
                        Status = "Precisa da sua resposta".T(), CorStatus = pal.Atencao,
                        Proxima = "Clique para abrir a janela".T(), Som = Som.Esperando, Clique = irPara,
                    });
                }
            }
            foreach (var k in estadoSessao.Keys.Where(k => k.StartsWith(p.Id + "|") && !vivas.Contains(k)).ToList())
            {
                estadoSessao.Remove(k);
                trabalhandoDesde.Remove(k);
            }
        }

        static bool EmFoco(string pasta)
        {
            return Foco.EDaPasta(Nativo.GetForegroundWindow(), pasta);
        }

        static string Juntar(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + " · " + b;
        }

        // Pré-visualização (botão nas Configurações)
        public static Aviso Exemplo()
        {
            return new Aviso
            {
                Provedor = "codex", Titulo = "{0} renovado".T("Codex"), Subtitulo = "{0} renovada".T("Sessão (5h)".T()),
                Status = "Cota disponível · {0}%".T(0), Proxima = "renova às {0}".T(DateTime.Now.AddHours(5).ToString("HH:mm")), Som = Som.Renovacao,
            };
        }
    }
}
