using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulso
{
    // Avisos no cartão ao lado do notch:
    //  - limite: 80%, 95% e 100% (uma vez por janela), só com valor exato — nunca pela estimativa;
    //  - renovação: a janela voltou depois de pelo menos 10% de uso;
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
                Sessoes(p, cfg, pal);
            }
        }

        void Janelas(Provedor p, Config cfg, Paleta pal)
        {
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
                            string ritmo = Texto.Ritmo(p.RitmoHora, j);
                            mostrar(new Aviso
                            {
                                Provedor = p.Id,
                                Titulo = lim >= 1 ? "Limite do " + p.Nome + " atingido" : p.Nome + " em " + pct + "%",
                                Subtitulo = j.Rotulo,
                                Status = lim >= 1 ? "Sem cota até renovar" : (100 - pct) + "% restante",
                                CorStatus = lim >= 0.95 ? pal.Critico : pal.Atencao,
                                Proxima = Juntar(Texto.Renova(j.ResetaEm), lim < 1 && ritmo != null && ritmo.Contains("esgota") ? ritmo.Substring(ritmo.IndexOf("esgota")) : null),
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
                                Titulo = p.Nome + " renovado",
                                Subtitulo = j.Rotulo + " renovada",
                                Status = "Cota disponível · " + (int)Math.Round(uso * 100) + "%",
                                Proxima = j.ResetaEm.HasValue ? Texto.Renova(j.ResetaEm) : null,
                                Som = Som.Renovacao,
                            });
                        }
                    }
                }
                visto[chave] = new Visto { Uso = uso, Reset = j.ResetaEm };
            }
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
                        Provedor = p.Id, Titulo = p.Nome + " terminou", Subtitulo = s.Titulo,
                        Status = "Concluída em " + Tempo.Duracao(dur),
                        Proxima = "Clique para abrir a janela", Som = Som.Concluida, Clique = irPara,
                    });
                }
                else if (s.Estado == Atividade.Aguardando && cfg.AvisoSessaoEspera && !EmFoco(s.Pasta))
                {
                    mostrar(new Aviso
                    {
                        Provedor = p.Id, Titulo = p.Nome + " está esperando você", Subtitulo = s.Titulo,
                        Status = "Precisa da sua resposta", CorStatus = pal.Atencao,
                        Proxima = "Clique para abrir a janela", Som = Som.Esperando, Clique = irPara,
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
                Provedor = "codex", Titulo = "Codex renovado", Subtitulo = "Sessão (5h) renovada",
                Status = "Cota disponível · 0%", Proxima = "renova às " + DateTime.Now.AddHours(5).ToString("HH:mm"), Som = Som.Renovacao,
            };
        }
    }
}
