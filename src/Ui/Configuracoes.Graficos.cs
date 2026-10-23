using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace Pulso
{
    // Aba Relatórios, parte de 23/10 (pedido do dono): as abas "Visão geral" e "Gráficos", o calendário do período e os
    // gráficos — destaques, participação, uso por dia agrupado por provedor/modelo/projeto, progressão contra o período
    // anterior, semanas do mês, dia da semana × hora, tendência e o dia a dia. Cores e marcas pela skill dataviz: cor
    // segue a entidade (não a posição), um tom só para intensidade, legenda com nome sempre que há mais de uma série
    partial class Configuracoes
    {
        // ---------- séries e cores ----------
        class Serie { public string Chave, Nome; public Color Cor; }

        // Paleta categórica validada (validate_palette.js) contra os grupos #ffffff e #48484b, nesta ordem
        static readonly string[] CategoriasClaro = { "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#6250d6" };
        static readonly string[] CategoriasEscuro = { "#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#9085e9" };
        static Color Categoria(int i, bool escuro) { return Paleta.Hex((escuro ? CategoriasEscuro : CategoriasClaro)[i % 6]); }
        static Color CorOutros(bool escuro) { return Paleta.Hex(escuro ? "#8e8e93" : "#a1a1a6"); }

        Serie SerieDoProvedor(string p) { return new Serie { Chave = p, Nome = Relatorio.NomeProvedor(p), Cor = CorProvedor(p, Escuro) }; }

        static Func<Relatorio.Registro, string> ChaveDoGrupo(int grupo)
        {
            if (grupo == 0) return r => r.Provedor;
            if (grupo == 1) return r => r.Modelo;
            return r => r.Projeto;
        }

        static Func<string, string> NomeDoGrupo(int grupo)
        {
            if (grupo == 0) return Relatorio.NomeProvedor;
            if (grupo == 1) return Relatorio.NomeModelo;
            return Relatorio.NomeProjeto;
        }

        // Os 6 modelos (ou projetos) mais usados em todo o livro ganham as 6 cores: assim a cor de cada um é a mesma em
        // qualquer período. Refeito a cada minuto
        Dictionary<string, int>[] coresGlobais = new Dictionary<string, int>[3];
        DateTime coresEm;

        Dictionary<string, int> CoresGlobais(int grupo)
        {
            if ((DateTime.UtcNow - coresEm).TotalSeconds > 60) { coresGlobais = new Dictionary<string, int>[3]; coresEm = DateTime.UtcNow; }
            if (coresGlobais[grupo] == null)
            {
                var chave = ChaveDoGrupo(grupo);
                coresGlobais[grupo] = Relatorio.Registros(PrimeiroDia, DateTime.Today, false)
                    .GroupBy(chave, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Sum(r => r.L.Total)).Take(6)
                    .Select((g, i) => new { g.Key, i }).ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
            }
            return coresGlobais[grupo];
        }

        // As séries do agrupamento nos registros: o provedor com a cor dele; modelo e projeto com a cor fixa, e quem não
        // está entre os 6 do livro vai para "Outros". 'indice' diz a série de cada registro
        List<Serie> Series(List<Relatorio.Registro> regs, int grupo, out Func<Relatorio.Registro, int> indice)
        {
            bool escuro = Escuro;
            var chave = ChaveDoGrupo(grupo);
            var lista = new List<Serie>();
            var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int outros = -1;
            if (grupo == 0)
            {
                foreach (var p in Consumo.Provedores)
                    if (regs.Any(r => r.Provedor == p)) { mapa[p] = lista.Count; lista.Add(SerieDoProvedor(p)); }
            }
            else
            {
                var cores = CoresGlobais(grupo);
                var presentes = regs.Select(chave).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var k in presentes.Where(cores.ContainsKey).OrderBy(k => cores[k]))
                {
                    mapa[k] = lista.Count;
                    lista.Add(new Serie { Chave = k, Nome = NomeDoGrupo(grupo)(k), Cor = Categoria(cores[k], escuro) });
                }
                if (presentes.Any(k => !cores.ContainsKey(k)))
                {
                    outros = lista.Count;
                    lista.Add(new Serie { Nome = "Outros".T(), Cor = CorOutros(escuro) });
                }
            }
            indice = r => { int i; return mapa.TryGetValue(chave(r), out i) ? i : outros; };
            return lista;
        }

        // Intensidade de uso na cor de destaque: um tom só, do fundo até 70% do destaque (o escolhido, sólido, continua
        // diferente do dia de mais uso); a raiz deixa os dias fracos visíveis
        Color Calor(long v, long max, Color fundo)
        {
            double t = Math.Sqrt(Math.Max(0, Math.Min(1, (double)v / Math.Max(1, max))));
            return Sobre(Paleta.Alfa(c.Accent, 0.12 + 0.58 * t), fundo);
        }

        // Texto legível sobre uma cor qualquer (células com intensidade)
        static Color TextoSobre(Color f)
        {
            double lum = (0.2126 * f.R + 0.7152 * f.G + 0.0722 * f.B) / 255;
            return lum > 0.55 ? Paleta.Hex("#1d1d1f") : Paleta.Hex("#f2f2f4");
        }

        static string Pct(long v, long total)
        {
            double p = total > 0 ? 100.0 * v / total : 0;
            return p.ToString(p > 0 && p < 0.1 ? "0.##" : "0.0", Idioma.Cultura) + "%";
        }

        static string PctInteiro(long v, long total) { return (total > 0 ? (int)Math.Round(100.0 * v / total) : 0) + "%"; }

        // Quadradinho de cor + nome, lado a lado, quebrando a linha quando não cabe; g nulo = só mede a altura
        float Chips(Graphics g, float x, float y, float w, List<KeyValuePair<string, Color>> itens, Color fundo)
        {
            float xx = x, yy = y;
            foreach (var it in itens)
            {
                float lw = E(16) + Tx.Largura(peq, it.Key);
                if (xx > x && xx + lw > x + w) { xx = x; yy += peq.Linha + E(4); }
                if (g != null)
                {
                    Preencher(g, new RectangleF(xx, yy + (peq.Linha - E(10)) / 2, E(10), E(10)), E(2.5f), it.Value);
                    Tx.Linha(g, peq, it.Key, Sobre(c.Text2, fundo), fundo, xx + E(16), yy, Tx.Alinhar.Esquerda, x + w - xx - E(16));
                }
                xx += lw + E(16);
            }
            return itens.Count == 0 ? 0 : yy - y + peq.Linha;
        }

        // ---------- cache e navegação ----------
        string chaveRelatorios;
        List<Bloco> blocosRelatorios;

        // O conteúdo é refeito quando muda o que se vê ou a cada 5 s (respostas novas); passar o cursor só redesenha,
        // porque os gráficos leem o cursor na hora de desenhar
        List<Bloco> RelatoriosEmCache()
        {
            string chave = string.Join("|", relVista, (int)relPeriodo, relDesloc, relProvedor, relGrupo, relCalendario, relCalMes.Ticks,
                Escuro, Idioma.Indice, DateTime.Today.Ticks, DateTime.UtcNow.Ticks / (5 * TimeSpan.TicksPerSecond));
            if (chave != chaveRelatorios || blocosRelatorios == null) { blocosRelatorios = Relatorios(); chaveRelatorios = chave; }
            return blocosRelatorios;
        }

        static DateTime PrimeiroDia { get { return Consumo.InicioUtc.ToLocalTime().Date; } }

        void IrPara(int desloc)
        {
            relDesloc = Math.Min(0, desloc);
            relCalMes = InicioDoCalendario(relPeriodo, Relatorio.Referencia(relPeriodo, relDesloc));
            rolagem = 0;
            Invalidate();
        }

        // Abre o período que contém a data (o clique num dia, numa coluna da tendência ou no calendário)
        void Abrir(Relatorio.Periodo p, DateTime data)
        {
            relPeriodo = p;
            relCalendario = false;
            IrPara(DeslocDe(p, data));
        }

        static DateTime InicioDoCalendario(Relatorio.Periodo p, DateTime refe)
        {
            return p == Relatorio.Periodo.Mes ? new DateTime(refe.Year, 1, 1) : new DateTime(refe.Year, refe.Month, 1);
        }

        // "Hoje", "Ontem", "Qua 21" · "Esta semana", "Semana passada", "6 a 12 out" · "Este mês", "Mês passado", "Agosto"
        static string NomeCurto(Relatorio.Periodo p, int desloc)
        {
            var cult = Idioma.Cultura;
            DateTime de, ate;
            Relatorio.Intervalo(p, Relatorio.Referencia(p, desloc), out de, out ate);
            if (p == Relatorio.Periodo.Dia) return desloc == 0 ? "Hoje".T() : desloc == -1 ? "Ontem".T() : Util.Maiuscula(de.ToString("ddd d", cult));
            if (p == Relatorio.Periodo.Semana) return desloc == 0 ? "Esta semana".T() : desloc == -1 ? "Semana passada".T() : "{0} a {1}".T(de.Day, ate.ToString("d MMM", cult));
            return desloc == 0 ? "Este mês".T() : desloc == -1 ? "Mês passado".T() : Util.Maiuscula(de.ToString("MMMM", cult));
        }

        // As horas do dia (do provedor filtrado); depois do início do registro por hora, dia sem hora é dia sem uso
        long[] HorasDe(DateTime d, DateTime? inicioHoras)
        {
            var h = Relatorio.HorasDoDia(d, relProvedor);
            if (h == null && inicioHoras.HasValue && d >= inicioHoras.Value && d <= DateTime.Today) h = new long[24];
            return h;
        }

        // ---------- calendário ----------
        Linha NovoCalendario(DateTime de, DateTime ate)
        {
            bool ano = relPeriodo == Relatorio.Periodo.Mes;
            DateTime ini = ano ? relCalMes : Calendario.InicioDaGrade(relCalMes), fim = ano ? relCalMes.AddYears(1).AddDays(-1) : ini.AddDays(41);
            return new Calendario
            {
                P = relPeriodo, Mes = relCalMes, SelDe = de, Min = PrimeiroDia,
                Totais = Relatorio.TotaisPorDia(ini, fim, relProvedor),
                Navegar = n => { relCalMes = ano ? relCalMes.AddYears(n) : relCalMes.AddMonths(n); Invalidate(); },
                Escolher = d => Abrir(relPeriodo, d),
            };
        }

        // Calendário dentro da janela: no Dia, os dias do mês; na Semana, a linha inteira é a semana (com o total dela);
        // no Mês, os 12 meses do ano. A cor de fundo é o uso; o escolhido fica na cor de destaque e hoje, contornado
        class Calendario : Linha
        {
            public Relatorio.Periodo P; public DateTime Mes, SelDe, Min;
            public Dictionary<DateTime, long> Totais;
            public Action<int> Navegar; public Action<DateTime> Escolher;

            public static DateTime InicioDaGrade(DateTime mes) { return mes.AddDays(-(((int)mes.DayOfWeek + 6) % 7)); }   // a segunda-feira da 1ª semana
            bool Ano { get { return P == Relatorio.Periodo.Mes; } }
            float Cab(Configuracoes j) { return j.E(44); }
            float Celula(Configuracoes j) { return Ano ? j.E(56) : j.E(34); }
            float Rodape(Configuracoes j) { return j.E(10) + j.peq.Linha + j.E(12); }

            public override float Altura(Configuracoes j, float w)
            {
                return Cab(j) + (Ano ? 3 * Celula(j) : j.peq.Linha + j.E(6) + 6 * Celula(j)) + Rodape(j);
            }

            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                var hoje = DateTime.Today;
                var cult = Idioma.Cultura;
                float xi = x + j.E(12), wi = w - j.E(24);
                // ‹ Outubro de 2026 ›  (no Mês, o ano)
                bool antes = Ano ? Mes.Year > Min.Year : Mes > new DateTime(Min.Year, Min.Month, 1);
                bool depois = Ano ? Mes.Year < hoje.Year : Mes < new DateTime(hoje.Year, hoje.Month, 1);
                var bAnt = new Botao { Id = "rel-cal-ant", Texto = "‹", Desativado = !antes, Clique = delegate { Navegar(-1); } };
                var bProx = new Botao { Id = "rel-cal-prox", Texto = "›", Desativado = !depois, Clique = delegate { Navegar(1); } };
                var tb = bAnt.Tamanho(j);
                float cy = y + j.E(10);
                bAnt.Desenhar(j, g, new RectangleF(xi, cy, tb.Width, tb.Height), j.c.Group);
                bProx.Desenhar(j, g, new RectangleF(xi + wi - tb.Width, cy, tb.Width, tb.Height), j.c.Group);
                string titulo = Ano ? Mes.Year.ToString(cult) : Util.Maiuscula(Mes.ToString("Y", cult));
                Tx.Linha(g, j.forte, titulo, j.c.Text, j.c.Group, xi + wi / 2, cy + (tb.Height - j.forte.Linha) / 2, Tx.Alinhar.Centro, 0);

                string legenda = Ano ? Meses(j, g, xi, y + Cab(j), wi) : Dias(j, g, xi, y + Cab(j), wi);

                // Rodapé: o que está sob o cursor, ou o convite e a escala de cores
                var fraco = Sobre(j.c.Text2, j.c.Group);
                float yr = y + h - Rodape(j) + j.E(10);
                float xs = xi + wi;
                if (legenda == null)
                {
                    string mais = "Mais".T(), menos = "Menos".T();
                    Tx.Linha(g, j.peq, mais, fraco, j.c.Group, xs, yr, Tx.Alinhar.Direita, 0);
                    xs -= Tx.Largura(j.peq, mais) + j.E(6);
                    for (int i = 4; i >= 0; i--)
                    {
                        xs -= j.E(12);
                        j.Preencher(g, new RectangleF(xs, yr + (j.peq.Linha - j.E(12)) / 2, j.E(12), j.E(12)), j.E(3), j.Calor(i * 25 + 1, 101, j.c.Group));
                        xs -= j.E(3);
                    }
                    Tx.Linha(g, j.peq, menos, fraco, j.c.Group, xs - j.E(3), yr, Tx.Alinhar.Direita, 0);
                    xs -= Tx.Largura(j.peq, menos) + j.E(16);
                    legenda = P == Relatorio.Periodo.Dia ? "Escolha um dia.".T() : P == Relatorio.Periodo.Semana ? "Escolha uma semana.".T() : "Escolha um mês.".T();
                    Tx.Linha(g, j.peq, legenda, fraco, j.c.Group, xi, yr, Tx.Alinhar.Esquerda, xs - xi);
                }
                else Tx.Linha(g, j.peq, legenda, j.c.Text, j.c.Group, xi, yr, Tx.Alinhar.Esquerda, wi);
            }

            long Total(DateTime de, DateTime ate)
            {
                long t = 0;
                foreach (var kv in Totais) if (kv.Key >= de && kv.Key <= ate) t += kv.Value;
                return t;
            }

            string Dias(Configuracoes j, Graphics g, float xi, float yg, float wi)
            {
                var cult = Idioma.Cultura;
                var hoje = DateTime.Today;
                bool semana = P == Relatorio.Periodo.Semana;
                float cw = (wi - (semana ? j.E(76) : 0)) / 7, ch = Celula(j);
                var ini = InicioDaGrade(Mes);
                var apagado = Sobre(Paleta.Alfa(j.c.Text2, 0.45), j.c.Group);
                for (int col = 0; col < 7; col++)
                    Tx.Linha(g, j.peq, Util.Maiuscula(ini.AddDays(col).ToString("ddd", cult)).TrimEnd('.'), Sobre(j.c.Text2, j.c.Group), j.c.Group,
                        xi + cw * col + cw / 2, yg, Tx.Alinhar.Centro, 0);
                yg += j.peq.Linha + j.E(6);
                long max = 1;
                for (int i = 0; i < 42; i++) { long v; if (Totais.TryGetValue(ini.AddDays(i), out v)) max = Math.Max(max, v); }
                string legenda = null;
                for (int lin = 0; lin < 6; lin++)
                {
                    var seg = ini.AddDays(7 * lin);
                    var faixa = new RectangleF(xi - j.E(4), yg + ch * lin + j.E(1), wi + j.E(8), ch - j.E(2));
                    Color fundo = j.c.Group;
                    if (semana)
                    {
                        string idSem = "rel-cal-sem:" + lin;
                        bool ativa = seg <= hoje && seg.AddDays(6) >= Min;
                        if (seg == SelDe) fundo = Sobre(Paleta.Alfa(j.c.Accent, j.Escuro ? 0.32 : 0.2), j.c.Group);
                        else if (ativa && j.Sobre(idSem)) fundo = Sobre(j.c.Hover, j.c.Group);
                        if (fundo != j.c.Group) j.Preencher(g, faixa, j.E(9), fundo);
                        long tot = Total(seg, seg.AddDays(6));
                        Tx.Linha(g, j.peq, ativa ? (tot > 0 ? Relatorio.Compacto(tot) : "–") : "", Sobre(j.c.Text2, fundo), fundo, xi + wi - j.E(6), faixa.Y + (faixa.Height - j.peq.Linha) / 2, Tx.Alinhar.Direita, 0);
                        if (ativa)
                        {
                            var s0 = seg;
                            j.Registrar(faixa, idSem, delegate { Escolher(s0); }, true);
                            if (j.Sobre(idSem))
                                legenda = Relatorio.Titulo(Relatorio.Periodo.Semana, seg, seg.AddDays(6), int.MinValue) + ": " + (tot > 0 ? "{0} tokens".T(Relatorio.Compacto(tot)) : "sem uso".T());
                        }
                    }
                    for (int col = 0; col < 7; col++)
                    {
                        var d = seg.AddDays(col);
                        bool ativo = d <= hoje && d >= Min;
                        long v;
                        Totais.TryGetValue(d, out v);
                        var cel = new RectangleF(xi + cw * col + j.E(3), faixa.Y + j.E(3), cw - j.E(6), faixa.Height - j.E(6));
                        string id = "rel-cal-dia:" + d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                        bool sel = !semana && d == SelDe, sob = !semana && ativo && j.Sobre(id);
                        Color f = fundo;
                        if (sel) f = j.c.Accent;
                        else if (ativo && v > 0) f = j.Calor(v, max, fundo);
                        if (sob && !sel) f = Sobre(j.c.Hover, f);
                        if (f != fundo) j.Preencher(g, cel, j.E(7), f);
                        if (d == hoje && !sel) j.Contornar(g, cel, j.E(7), j.c.Accent, Math.Max(1, j.E(1.5f)));
                        Color t = sel ? j.c.OnAccent : !ativo ? apagado : d.Month != Mes.Month ? Sobre(j.c.Text2, f) : f == fundo ? j.c.Text : TextoSobre(f);
                        Tx.Linha(g, j.ctl, d.Day.ToString(cult), t, f, cel.X + cel.Width / 2, cel.Y + (cel.Height - j.ctl.Linha) / 2, Tx.Alinhar.Centro, 0);
                        if (semana || !ativo) continue;
                        var d0 = d;
                        j.Registrar(cel, id, delegate { Escolher(d0); }, true);
                        if (sob) legenda = Util.Maiuscula(d.ToString("D", cult)) + ": " + (v > 0 ? "{0} tokens".T(Relatorio.Compacto(v)) : "sem uso".T());
                    }
                }
                return legenda;
            }

            string Meses(Configuracoes j, Graphics g, float xi, float yg, float wi)
            {
                var cult = Idioma.Cultura;
                var hoje = DateTime.Today;
                float cw = wi / 4, ch = Celula(j);
                var totais = new long[12];
                foreach (var kv in Totais) if (kv.Key.Year == Mes.Year) totais[kv.Key.Month - 1] += kv.Value;
                long max = Math.Max(1, totais.Max());
                var minMes = new DateTime(Min.Year, Min.Month, 1);
                var apagado = Sobre(Paleta.Alfa(j.c.Text2, 0.45), j.c.Group);
                string legenda = null;
                for (int m = 0; m < 12; m++)
                {
                    var inicio = new DateTime(Mes.Year, m + 1, 1);
                    bool ativo = inicio <= hoje && inicio >= minMes;
                    var cel = new RectangleF(xi + cw * (m % 4) + j.E(3), yg + ch * (m / 4) + j.E(3), cw - j.E(6), ch - j.E(6));
                    string id = "rel-cal-mes:" + m;
                    bool sel = inicio == SelDe, sob = ativo && j.Sobre(id);
                    Color f = j.c.Group;
                    if (sel) f = j.c.Accent;
                    else if (ativo && totais[m] > 0) f = j.Calor(totais[m], max, j.c.Group);
                    if (sob && !sel) f = Sobre(j.c.Hover, f);
                    if (f != j.c.Group) j.Preencher(g, cel, j.E(9), f);
                    if (inicio.Year == hoje.Year && inicio.Month == hoje.Month && !sel) j.Contornar(g, cel, j.E(9), j.c.Accent, Math.Max(1, j.E(1.5f)));
                    Color t = sel ? j.c.OnAccent : !ativo ? apagado : f == j.c.Group ? j.c.Text : TextoSobre(f);
                    Color t2 = sel ? j.c.OnAccent : !ativo ? apagado : f == j.c.Group ? Sobre(j.c.Text2, f) : TextoSobre(f);
                    float ty = cel.Y + (cel.Height - j.corpo.Linha - j.peq.Linha) / 2;
                    Tx.Linha(g, j.corpo, Util.Maiuscula(inicio.ToString("MMM", cult)).TrimEnd('.'), t, f, cel.X + cel.Width / 2, ty, Tx.Alinhar.Centro, 0);
                    if (ativo) Tx.Linha(g, j.peq, totais[m] > 0 ? Relatorio.Compacto(totais[m]) : "–", t2, f, cel.X + cel.Width / 2, ty + j.corpo.Linha, Tx.Alinhar.Centro, 0);
                    if (!ativo) continue;
                    j.Registrar(cel, id, delegate { Escolher(inicio); }, true);
                    if (sob) legenda = Util.Maiuscula(inicio.ToString("Y", cult)) + ": " + (totais[m] > 0 ? "{0} tokens".T(Relatorio.Compacto(totais[m])) : "sem uso".T());
                }
                return legenda;
            }
        }

        // Abas dentro de uma página (o SelectorBar do Windows 11): o texto e um traço na cor de destaque sob a escolhida
        class Abas : Linha
        {
            public string Id; public string[] Opcoes; public int Sel; public Action<int> Mudou;
            public override float Altura(Configuracoes j, float w) { return j.E(40); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                float xx = x;
                for (int i = 0; i < Opcoes.Length; i++)
                {
                    int idx = i;
                    var estilo = i == Sel ? j.forte : j.corpo;
                    var r = new RectangleF(xx, y + j.E(2), Tx.Largura(estilo, Opcoes[i]) + j.E(24), h - j.E(6));
                    string id = Id + ":" + i;
                    bool sob = i != Sel && j.Sobre(id);
                    Color fundo = j.c.Pane;
                    if (sob) { fundo = Sobre(j.c.Hover, j.c.Pane); j.Preencher(g, r, j.E(6), fundo); }
                    Tx.Linha(g, estilo, Opcoes[i], i == Sel || sob ? j.c.Text : Sobre(j.c.Text2, j.c.Pane), fundo, r.X + r.Width / 2, r.Y + (r.Height - estilo.Linha) / 2 - j.E(2), Tx.Alinhar.Centro, 0);
                    if (i == Sel) j.Preencher(g, new RectangleF(r.X + r.Width / 2 - j.E(9), r.Bottom - j.E(3), j.E(18), j.E(3)), j.E(1.5f), j.c.Accent);
                    else j.Registrar(r, id, delegate { Mudou(idx); j.Invalidate(); }, true);
                    xx += r.Width + j.E(4);
                }
            }
        }

        // ---------- aba Gráficos ----------
        List<Bloco> Graficos(DateTime de, DateTime ate, List<Relatorio.Registro> regs)
        {
            var blocos = new List<Bloco>();
            var dados = new List<object[]>();   // os números de cada gráfico, para a exportação: { gráfico, item, série, valor }
            bool dia = relPeriodo == Relatorio.Periodo.Dia, mes = relPeriodo == Relatorio.Periodo.Mes;
            var total = Relatorio.Soma(regs);
            var inicioHoras = Relatorio.InicioDasHoras;
            Func<Relatorio.Registro, int> indice;
            var series = Series(regs, relGrupo, out indice);

            blocos.Add(Destaques(de, ate, regs, total, inicioHoras, dados));

            var somas = new long[series.Count];
            foreach (var r in regs) somas[indice(r)] += r.L.Total;
            string tPart = "Participação".T();
            foreach (int i in Enumerable.Range(0, series.Count).OrderBy(i => series[i].Chave == null ? 1 : 0).ThenByDescending(i => somas[i]))
            {
                dados.Add(new object[] { tPart, series[i].Nome, "Tokens".T(), somas[i] });
                dados.Add(new object[] { tPart, series[i].Nome, "%", Pct(somas[i], total.Total) });
            }
            blocos.Add(series.Count > 1 ? B(tPart, new Rosca { Series = series, Valores = somas }) : B(tPart, L("{0}: 100% dos tokens deste período.".T(series[0].Nome))));

            blocos.Add(ContraAnterior(de, regs, dados));
            blocos.Add(Progressao(de, ate, inicioHoras, dados));
            if (mes) blocos.Add(SemanasDoMes(de, ate, dados));
            if (!dia)
            {
                var mapa = MapaDeUso(de, ate, regs, inicioHoras, dados);
                if (mapa != null) blocos.Add(mapa);
            }
            blocos.Add(Tendencia(ate, dados));
            if (!dia) blocos.Add(DiaADia(de, ate, regs, series, indice, dados));

            // Exportar como na Visão geral, mas com os números dos gráficos; no navegador, os gráficos desenhados como aqui
            var graficos = blocos.ToList();
            var cult = Idioma.Cultura;
            string agrupado = new[] { "Provedor", "Modelo", "Projeto" }[relGrupo].T().ToLower(cult);
            string titulo = Relatorio.Titulo(relPeriodo, de, ate, relDesloc) + (relProvedor != null ? " · " + Relatorio.NomeProvedor(relProvedor) : "") + " · " + "Agrupado por {0}".T(agrupado);
            string nome = "graficos" + Relatorio.NomeArquivo(relPeriodo, de).Substring("consumo".Length) + "-" + new[] { "provedor", "modelo", "projeto" }[relGrupo] + (relProvedor != null ? "-" + relProvedor : "");
            blocos.Add(B("Exportar".T(),
                I("", new Botoes
                {
                    Itens = new List<Botao>
                    {
                        NovoBotao("rel-g-csv", "Salvar CSV…".T(), delegate { SalvarCsv(nome, () => Relatorio.CsvGraficos(dados)); }),
                        NovoBotao("rel-g-html", "Abrir no navegador".T(), delegate
                        {
                            int largura;
                            string png = FotoDosGraficos(graficos, out largura);
                            AbrirHtml(nome, () => Relatorio.HtmlGraficos(dados, titulo, png, largura));
                        }),
                    },
                }),
                L(LegendaRelatorios())));
            return blocos;
        }

        // Os gráficos desenhados como na janela (sem o cursor), em PNG base64, para a página do navegador; 'largura' em px
        // de tela (a imagem tem a densidade do monitor)
        string FotoDosGraficos(List<Bloco> blocos, out int largura)
        {
            var guardados = alvos.ToList();
            string cursor = sobre;
            sobre = null;
            try
            {
                float w = Corpo.Width - E(40);
                float alt;
                using (var b = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(b)) alt = DesenharBlocos(g, blocos, 0, 0, w);   // mede
                using (var b = new Bitmap((int)Math.Ceiling(w + E(40)), (int)Math.Ceiling(alt + E(24))))
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(c.Pane);
                    DesenharBlocos(g, blocos, E(20), E(4), w);
                    largura = (int)Math.Round(b.Width / k);
                    using (var ms = new System.IO.MemoryStream())
                    {
                        b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception e) { Log.Erro("desenhar gráficos para o navegador", e); largura = 0; return null; }
            finally { alvos.Clear(); alvos.AddRange(guardados); sobre = cursor; }
        }

        // Os pontos de cada linha, como linhas da exportação
        static void DadosDasCurvas(List<object[]> dados, string grafico, Curvas cv)
        {
            for (int i = 0; i < cv.Nomes.Length; i++)
                foreach (var s in cv.Series)
                    if (s.V[i].HasValue) dados.Add(new object[] { grafico, cv.Nomes[i], s.Nome, (long)Math.Round(s.V[i].Value) });
        }

        // Números do período: média, pico, comparação com o anterior, dias com uso e a fatia lida do cache
        Bloco Destaques(DateTime de, DateTime ate, List<Relatorio.Registro> regs, Consumo.Linha total, DateTime? inicioHoras, List<object[]> dados)
        {
            Action<Numeros> exportar = n => { foreach (var it in n.Itens) dados.Add(new object[] { "Destaques".T(), it.Key, "", it.Value }); };
            var cult = Idioma.Cultura;
            var hoje = DateTime.Today;
            var ultimo = ate < hoje ? ate : hoje;
            bool dia = relPeriodo == Relatorio.Periodo.Dia;

            var horas = new long[24];
            for (var d = de; d <= ultimo; d = d.AddDays(1))
            {
                var h = Relatorio.HorasDoDia(d, relProvedor);
                if (h != null) for (int i = 0; i < 24; i++) horas[i] += h[i];
            }
            long maxHora = horas.Max();
            string pico = maxHora > 0 ? Array.IndexOf(horas, maxHora).ToString("00") + ":00" : "—";

            bool parcial;
            long anterior = Anterior(de, out parcial);
            string delta = anterior > 0 ? Variacao(total.Total, anterior) : "—";
            string contra = relPeriodo == Relatorio.Periodo.Dia ? (relDesloc == 0 ? "Contra ontem" : "Contra o dia anterior")
                : relPeriodo == Relatorio.Periodo.Semana ? (relDesloc == 0 ? "Contra a semana passada" : "Contra a semana anterior")
                : (relDesloc == 0 ? "Contra o mês passado" : "Contra o mês anterior");
            string cache = total.Total > 0 ? PctInteiro(total.Tokens[1], total.Total) : "—";
            var nota = L(parcial ? "No período em curso, a comparação vai até o mesmo ponto do anterior.".T() : null);

            var linha1 = new Numeros();
            if (dia)
            {
                linha1.Itens.Add(new KeyValuePair<string, string>("Hora de pico".T(), pico));
                linha1.Itens.Add(new KeyValuePair<string, string>(contra.T(), delta));
                linha1.Itens.Add(new KeyValuePair<string, string>("Lidos do cache".T(), cache));
                exportar(linha1);
                return B("Destaques".T(), linha1, nota);
            }
            var porDia = regs.GroupBy(r => r.Dia).Select(g => new { Dia = g.Key, T = g.Sum(r => r.L.Total) }).Where(x => x.T > 0).ToList();
            var maior = porDia.OrderByDescending(x => x.T).FirstOrDefault();
            int diasNoPeriodo = Math.Max(1, (ultimo - de).Days + 1);
            linha1.Itens.Add(new KeyValuePair<string, string>("Média por dia com uso".T(), porDia.Count > 0 ? Relatorio.Compacto(total.Total / porDia.Count) : "—"));
            linha1.Itens.Add(new KeyValuePair<string, string>("Dia de maior uso".T(),
                maior != null ? Util.Maiuscula(DateTime.ParseExact(maior.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("ddd d", cult)) : "—"));
            linha1.Itens.Add(new KeyValuePair<string, string>("Hora de pico".T(), pico));
            var linha2 = new Numeros();
            linha2.Itens.Add(new KeyValuePair<string, string>(contra.T(), delta));
            linha2.Itens.Add(new KeyValuePair<string, string>("Dias com uso".T(), "{0} de {1}".T(porDia.Count, diasNoPeriodo)));
            linha2.Itens.Add(new KeyValuePair<string, string>("Lidos do cache".T(), cache));
            exportar(linha1);
            exportar(linha2);
            return B("Destaques".T(), linha1, linha2, nota);
        }

        // Tokens do período anterior ao mostrado; no período em curso, só até o mesmo ponto (até esta hora, ou até o
        // mesmo dia da semana ou do mês)
        long Anterior(DateTime de, out bool parcial)
        {
            DateTime ade, aate;
            Relatorio.Intervalo(relPeriodo, Relatorio.Referencia(relPeriodo, relDesloc - 1), out ade, out aate);
            parcial = relDesloc == 0;
            if (relPeriodo == Relatorio.Periodo.Dia && parcial)
            {
                var h = Relatorio.HorasDoDia(ade, relProvedor);
                if (h != null) return h.Take(DateTime.Now.Hour + 1).Sum();
                parcial = false;
            }
            else if (parcial) aate = ade.AddDays(Math.Min((DateTime.Today - de).Days, (aate - ade).Days));
            return Relatorio.TotaisPorDia(ade, aate, relProvedor).Values.Sum();
        }

        // "+30%", "−12%"; com um anterior quase vazio a porcentagem explode (+67511%): acima de 1000%, o múltiplo ("677×")
        static string Variacao(long agora, long antes)
        {
            if (antes <= 0) return agora > 0 ? "novo".T() : "—";
            double p = 100.0 * (agora - antes) / antes;
            if (p >= 1000) return ((double)agora / antes).ToString("0", Idioma.Cultura) + "×";
            return (p >= 0 ? "+" : "−") + Math.Abs(p).ToString(Math.Abs(p) < 10 ? "0.#" : "0", Idioma.Cultura) + "%";
        }

        // Cada provedor, modelo ou projeto contra o período anterior (no período em curso, até o mesmo ponto; no Dia,
        // ontem inteiro, porque a hora não guarda modelo nem projeto)
        Bloco ContraAnterior(DateTime de, List<Relatorio.Registro> regs, List<object[]> dados)
        {
            DateTime ade, aate;
            Relatorio.Intervalo(relPeriodo, Relatorio.Referencia(relPeriodo, relDesloc - 1), out ade, out aate);
            bool hoje = relPeriodo == Relatorio.Periodo.Dia && relDesloc == 0, parcial = relDesloc == 0 && !hoje;
            if (parcial) aate = ade.AddDays(Math.Min((DateTime.Today - de).Days, (aate - ade).Days));
            var antes = Relatorio.Registros(ade, aate, false).Where(r => relProvedor == null || r.Provedor == relProvedor).ToList();
            Func<Relatorio.Registro, int> indice;
            var series = Series(regs.Concat(antes).ToList(), relGrupo, out indice);
            var cmp = new Comparacao { Series = series, Agora = new long[series.Count], Antes = new long[series.Count], NomeAgora = NomeCurto(relPeriodo, relDesloc), NomeAntes = NomeCurto(relPeriodo, relDesloc - 1) };
            foreach (var r in regs) cmp.Agora[indice(r)] += r.L.Total;
            foreach (var r in antes) cmp.Antes[indice(r)] += r.L.Total;
            string tCmp = "Contra o período anterior".T();
            foreach (int i in cmp.Ordem())
            {
                dados.Add(new object[] { tCmp, series[i].Nome, cmp.NomeAgora, cmp.Agora[i] });
                dados.Add(new object[] { tCmp, series[i].Nome, cmp.NomeAntes, cmp.Antes[i] });
                dados.Add(new object[] { tCmp, series[i].Nome, "Variação".T(), Variacao(cmp.Agora[i], cmp.Antes[i]) });
            }
            string nota = hoje ? "Colorida = hoje até agora · cinza = ontem inteiro.".T()
                : parcial ? "Colorida = {0} · cinza = {1}, até o mesmo ponto.".T(cmp.NomeAgora, cmp.NomeAntes)
                : "Colorida = {0} · cinza = {1}.".T(cmp.NomeAgora, cmp.NomeAntes);
            return B("Contra o período anterior".T(), cmp, L(nota));
        }

        static double?[] Acumulado(long[] v, int ultimo)
        {
            var r = new double?[v.Length];
            long s = 0;
            for (int i = 0; i < v.Length; i++) { s += v[i]; if (i <= ultimo) r[i] = s; }
            return r;
        }

        // Tokens acumulados ao longo do período escolhido, contra o anterior e a média dos anteriores
        Bloco Progressao(DateTime de, DateTime ate, DateTime? inicioHoras, List<object[]> dados)
        {
            var cult = Idioma.Cultura;
            var hoje = DateTime.Today;
            bool escuro = Escuro;
            var cv = new Curvas { Id = "rel-prog" };
            Action<string, double?[], int> serie = (nome, v, tipo) =>
                cv.Series.Add(new SerieCurva { Nome = nome, Curto = tipo == 2 ? "Média".T() : null, V = v, Fraca = tipo == 2, Cor = tipo == 2 ? CorOutros(escuro) : Categoria(tipo, escuro) });
            string nota;
            if (relPeriodo == Relatorio.Periodo.Dia)
            {
                var h = HorasDe(de, inicioHoras);
                if (h == null) return B("Progressão".T(), L("Este dia é de antes do registro por hora: só o total do dia ficou guardado.".T()));
                serie(NomeCurto(relPeriodo, relDesloc), Acumulado(h, de == hoje ? DateTime.Now.Hour : 23), 0);
                var ant = HorasDe(de.AddDays(-1), inicioHoras);
                if (ant != null) serie(NomeCurto(relPeriodo, relDesloc - 1), Acumulado(ant, 23), 1);
                var media = new double[24];
                int n = 0;
                for (int k = 1; k <= 7; k++)
                {
                    var hk = HorasDe(de.AddDays(-k), inicioHoras);
                    if (hk == null) continue;
                    n++;
                    long s = 0;
                    for (int i = 0; i < 24; i++) { s += hk[i]; media[i] += s; }
                }
                if (n >= 2) serie("Média dos 7 dias anteriores".T(), media.Select(v => (double?)(v / n)).ToArray(), 2);
                cv.Rotulos = Enumerable.Range(0, 24).Select(i => i % 6 == 0 ? i.ToString("00") + ":00" : null).ToArray();
                cv.Nomes = Enumerable.Range(0, 24).Select(i => "Até as {0}".T((i + 1).ToString("00") + ":00")).ToArray();
                nota = "Tokens acumulados ao longo do dia, contra o dia anterior e a média dos 7 dias antes dele.";
            }
            else if (relPeriodo == Relatorio.Periodo.Semana)
            {
                var tot = Relatorio.TotaisPorDia(de.AddDays(-28), ate, relProvedor);
                Func<DateTime, int, double?[]> semana = (ini, ultimoDia) =>
                {
                    var v = new long[7];
                    for (int i = 0; i < 7; i++) { long t; tot.TryGetValue(ini.AddDays(i), out t); v[i] = t; }
                    return Acumulado(v, ultimoDia);
                };
                serie(NomeCurto(relPeriodo, relDesloc), semana(de, ate <= hoje ? 6 : (hoje - de).Days), 0);
                if (de.AddDays(-7) >= PrimeiroDia) serie(NomeCurto(relPeriodo, relDesloc - 1), semana(de.AddDays(-7), 6), 1);
                var anteriores = Enumerable.Range(1, 4).Select(k => de.AddDays(-7 * k)).Where(d => d >= PrimeiroDia).Select(d => semana(d, 6)).ToList();
                if (anteriores.Count >= 2) serie("Média das 4 semanas anteriores".T(), Enumerable.Range(0, 7).Select(i => (double?)anteriores.Average(a => a[i].Value)).ToArray(), 2);
                cv.Rotulos = Enumerable.Range(0, 7).Select(i => de.AddDays(i).ToString("ddd", cult)).ToArray();
                cv.Nomes = Enumerable.Range(0, 7).Select(i => "Até {0}".T(de.AddDays(i).ToString("dddd", cult))).ToArray();
                nota = "Tokens acumulados ao longo da semana, contra a semana anterior e a média das 4 anteriores.";
            }
            else
            {
                var tot = Relatorio.TotaisPorDia(de.AddMonths(-3), ate, relProvedor);
                Func<DateTime, double?[]> mes = ini =>
                {
                    int dm = DateTime.DaysInMonth(ini.Year, ini.Month);
                    var v = new long[31];
                    for (int i = 0; i < dm; i++) { long t; tot.TryGetValue(ini.AddDays(i), out t); v[i] = t; }
                    var a = Acumulado(v, Math.Min(dm - 1, ini.Year == hoje.Year && ini.Month == hoje.Month ? hoje.Day - 1 : 30));
                    for (int i = dm; i < 31; i++) a[i] = null;
                    return a;
                };
                serie(NomeCurto(relPeriodo, relDesloc), mes(de), 0);
                if (de.AddMonths(-1) >= new DateTime(PrimeiroDia.Year, PrimeiroDia.Month, 1)) serie(NomeCurto(relPeriodo, relDesloc - 1), mes(de.AddMonths(-1)), 1);
                var anteriores = Enumerable.Range(1, 3).Select(k => de.AddMonths(-k)).Where(d => d >= new DateTime(PrimeiroDia.Year, PrimeiroDia.Month, 1)).Select(mes).ToList();
                if (anteriores.Count >= 2)
                    serie("Média dos 3 meses anteriores".T(), Enumerable.Range(0, 31).Select(i =>
                    {
                        var vs = anteriores.Where(a => a[i].HasValue).Select(a => a[i].Value).ToList();
                        return vs.Count > 0 ? (double?)vs.Average() : null;
                    }).ToArray(), 2);
                cv.Rotulos = Enumerable.Range(1, 31).Select(d => d == 1 || d % 5 == 0 ? d.ToString(cult) : null).ToArray();
                cv.Nomes = Enumerable.Range(1, 31).Select(d => "Até o dia {0}".T(d)).ToArray();
                nota = "Tokens acumulados ao longo do mês, contra o mês anterior e a média dos 3 anteriores.";
            }
            DadosDasCurvas(dados, "Progressão".T(), cv);
            return B("Progressão".T(), cv, L(nota.T()));
        }

        // No Mês: os dias de cada semana (segunda a domingo), uma linha por semana
        Bloco SemanasDoMes(DateTime de, DateTime ate, List<object[]> dados)
        {
            var cult = Idioma.Cultura;
            var hoje = DateTime.Today;
            var tot = Relatorio.TotaisPorDia(de, ate, relProvedor);
            var cv = new Curvas { Id = "rel-sem" };
            var seg0 = Calendario.InicioDaGrade(de);
            string maiorNome = null;
            long maior = 0;
            int k = 0;
            for (var seg = seg0; seg <= ate; seg = seg.AddDays(7), k++)
            {
                DateTime prim = seg < de ? de : seg, ult = seg.AddDays(6) > ate ? ate : seg.AddDays(6);
                if (prim > hoje) break;   // semanas que ainda não começaram
                var v = new double?[7];
                long soma = 0;
                for (int i = 0; i < 7; i++)
                {
                    var d = seg.AddDays(i);
                    if (d < de || d > ate || d > hoje) continue;
                    long t;
                    tot.TryGetValue(d, out t);
                    v[i] = t;
                    soma += t;
                }
                string nome = prim.Day == ult.Day ? prim.Day.ToString(cult) : "{0} a {1}".T(prim.Day, ult.Day);
                if (soma > maior) { maior = soma; maiorNome = nome; }
                cv.Series.Add(new SerieCurva { Nome = nome, Cor = Categoria(k, Escuro), V = v });
            }
            cv.Rotulos = Enumerable.Range(0, 7).Select(i => seg0.AddDays(i).ToString("ddd", cult)).ToArray();
            cv.Nomes = Enumerable.Range(0, 7).Select(i => Util.Maiuscula(seg0.AddDays(i).ToString("dddd", cult))).ToArray();
            cv.Resumo = maiorNome != null ? "Semana de maior uso: {0}, {1} tokens".T(maiorNome, Relatorio.Compacto(maior)) : null;
            DadosDasCurvas(dados, "Semanas do mês".T(), cv);
            return B("Semanas do mês".T(), cv, L("Tokens de cada dia, uma linha por semana do mês (segunda a domingo).".T()));
        }

        // Dia da semana × hora, somados no período; nulo se nenhum dia do período tem hora
        Bloco MapaDeUso(DateTime de, DateTime ate, List<Relatorio.Registro> regs, DateTime? inicioHoras, List<object[]> dados)
        {
            var cult = Idioma.Cultura;
            var hoje = DateTime.Today;
            var ultimo = ate < hoje ? ate : hoje;
            var mapa = new Mapa { V = new long[7, 24], Dias = new int[7] };
            var comUso = new HashSet<string>(regs.Select(r => r.Dia));
            bool falta = false, algum = false;
            for (var d = de; d <= ultimo; d = d.AddDays(1))
            {
                var h = HorasDe(d, inicioHoras);
                if (h == null) { if (comUso.Contains(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) falta = true; continue; }
                int dw = ((int)d.DayOfWeek + 6) % 7;
                mapa.Dias[dw]++;
                for (int i = 0; i < 24; i++) { mapa.V[dw, i] += h[i]; if (h[i] > 0) algum = true; }
            }
            if (!algum) return null;
            var seg0 = Calendario.InicioDaGrade(de);
            mapa.NomesCurtos = Enumerable.Range(0, 7).Select(i => Util.Maiuscula(seg0.AddDays(i).ToString("ddd", cult)).TrimEnd('.')).ToArray();
            mapa.NomesLongos = Enumerable.Range(0, 7).Select(i => Util.Maiuscula(seg0.AddDays(i).ToString("dddd", cult))).ToArray();
            for (int d = 0; d < 7; d++)
                for (int hr = 0; hr < 24; hr++) dados.Add(new object[] { "Quando você usa".T(), mapa.NomesLongos[d], hr.ToString("00") + ":00", mapa.V[d, hr] });
            return B("Quando você usa".T(), mapa, L(falta ? "Tokens de cada hora, somados por dia da semana. Os dias de antes do registro por hora ficam fora do mapa.".T()
                : "Tokens de cada hora, somados por dia da semana no período.".T()));
        }

        // Os últimos 14 dias, 12 semanas ou 12 meses até o período escolhido; o clique abre a coluna
        Bloco Tendencia(DateTime ate, List<object[]> dados)
        {
            var cult = Idioma.Cultura;
            int n = relPeriodo == Relatorio.Periodo.Dia ? 14 : 12;
            var limites = new DateTime[n, 2];
            for (int k = 0; k < n; k++)
            {
                DateTime pde, pate;
                Relatorio.Intervalo(relPeriodo, Relatorio.Referencia(relPeriodo, relDesloc - (n - 1 - k)), out pde, out pate);
                limites[k, 0] = pde; limites[k, 1] = pate;
            }
            var regs = Relatorio.Registros(limites[0, 0], ate, false).Where(r => relProvedor == null || r.Provedor == relProvedor).ToList();
            Func<Relatorio.Registro, int> indice;
            var series = Series(regs, relGrupo, out indice);
            var grafico = new Grafico { Id = "rel-g-tend", Ocupacao = 0.6f, Series = series, LegendaNoTopo = series.Count <= 3, Destaque = n - 1 };
            var porDia = regs.GroupBy(r => r.Dia).ToDictionary(g => Relatorio.Data(g.First()), g => g.ToList());
            string diaMes = cult.TwoLetterISOLanguageName == "en" ? "M/d" : "d/M";
            for (int k = 0; k < n; k++)
            {
                var v = new long[series.Count];
                foreach (var kv in porDia)
                    if (kv.Key >= limites[k, 0] && kv.Key <= limites[k, 1]) foreach (var r in kv.Value) v[indice(r)] += r.L.Total;
                grafico.Valores.Add(v);
                var pde = limites[k, 0];
                grafico.Rotulos.Add(relPeriodo == Relatorio.Periodo.Dia ? pde.Day.ToString(cult)
                    : relPeriodo == Relatorio.Periodo.Semana ? (k % 2 == (n - 1) % 2 ? pde.ToString(diaMes, cult) : null)
                    : Util.Maiuscula(pde.ToString("MMM", cult)).TrimEnd('.'));
                grafico.Nomes.Add(NomeCurto(relPeriodo, relDesloc - (n - 1 - k)));
                for (int p = 0; p < series.Count; p++)
                    if (v[p] > 0) dados.Add(new object[] { "Tendência".T(), grafico.Nomes[k], series[p].Nome, v[p] });
                dados.Add(new object[] { "Tendência".T(), grafico.Nomes[k], "Total".T(), v.Sum() });
            }
            grafico.Clicavel = i => limites[i, 1] >= PrimeiroDia;
            grafico.Abrir = i => IrPara(relDesloc - (n - 1 - i));
            string quantos = relPeriodo == Relatorio.Periodo.Dia ? "Últimos 14 dias".T() : relPeriodo == Relatorio.Periodo.Semana ? "Últimas 12 semanas".T() : "Últimos 12 meses".T();
            return B("Tendência".T(), grafico, series.Count > 3 ? new LegendaCores { Series = series } : null,
                L("{0} até o período escolhido. Clique numa coluna para abrir o período.".T(quantos)));
        }

        // A tabela dos gráficos: cada dia com os tokens, a barra pelo agrupamento e o modelo e o projeto principais
        Bloco DiaADia(DateTime de, DateTime ate, List<Relatorio.Registro> regs, List<Serie> series, Func<Relatorio.Registro, int> indice, List<object[]> dados)
        {
            string tDia = "Dia a dia".T();
            var cult = Idioma.Cultura;
            var hoje = DateTime.Today;
            var ultimo = ate < hoje ? ate : hoje;
            bool mes = relPeriodo == Relatorio.Periodo.Mes;
            var porDia = regs.GroupBy(r => r.Dia).ToDictionary(g => g.Key, g => g.ToList());
            long max = porDia.Values.Select(l => l.Sum(r => r.L.Total)).DefaultIfEmpty(0).Max();
            var linhas = new List<Linha>();
            for (var d = de; d <= ultimo; d = d.AddDays(1))
            {
                string chave = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                List<Relatorio.Registro> rs;
                if (!porDia.TryGetValue(chave, out rs))
                {
                    if (mes) continue;   // no mês, só os dias com uso
                    rs = new List<Relatorio.Registro>();
                }
                var soma = Relatorio.Soma(rs);
                var partes = new long[series.Count];
                foreach (var r in rs) partes[indice(r)] += r.L.Total;
                var data = d;
                string nomeDia = Util.Maiuscula(d.ToString("dddd, d", cult));
                dados.Add(new object[] { tDia, nomeDia, "Total".T(), soma.Total });
                for (int i = 0; i < series.Count; i++) if (partes[i] > 0) dados.Add(new object[] { tDia, nomeDia, series[i].Nome, partes[i] });
                dados.Add(new object[] { tDia, nomeDia, "Respostas".T(), soma.Respostas });
                if (soma.Custo > 0) dados.Add(new object[] { tDia, nomeDia, "US$", Math.Round(soma.Custo, 2) });
                if (soma.Total > 0)
                {
                    dados.Add(new object[] { tDia, nomeDia, "Modelo principal".T(), Principal(rs, r => r.Modelo, Relatorio.NomeModelo, soma.Total) });
                    dados.Add(new object[] { tDia, nomeDia, "Projeto principal".T(), Principal(rs, r => r.Projeto, Relatorio.NomeProjeto, soma.Total) });
                }
                linhas.Add(new Fatia
                {
                    Id = "rel-dd:" + chave,
                    Nome = nomeDia,
                    Valor = soma.Total > 0 ? "{0} tokens".T(Relatorio.Compacto(soma.Total)) : "sem uso".T(),
                    Parte = max > 0 ? (double)soma.Total / max : 0,
                    Segmentos = Enumerable.Range(0, series.Count).Where(i => partes[i] > 0).Select(i => new KeyValuePair<Color, double>(series[i].Cor, (double)partes[i] / max)).ToList(),
                    Detalhe = soma.Total > 0 ? DetalheDoDia(rs, soma) : "",
                    Abrir = delegate { Abrir(Relatorio.Periodo.Dia, data); },
                });
            }
            linhas.Add(L("Clique num dia para abrir.".T()));
            return new Bloco { Secao = "Dia a dia".T(), Linhas = linhas };
        }

        // "Opus 5.5 94% · Guelgi 80% · 1.234 respostas · ≈ US$ 300,12"
        static string DetalheDoDia(List<Relatorio.Registro> rs, Consumo.Linha soma)
        {
            var cult = Idioma.Cultura;
            var partes = new List<string> { Principal(rs, r => r.Modelo, Relatorio.NomeModelo, soma.Total), Principal(rs, r => r.Projeto, Relatorio.NomeProjeto, soma.Total) };
            partes.Add(soma.Respostas == 1 ? "1 resposta".T() : "{0} respostas".T(soma.Respostas.ToString("N0", cult)));
            if (soma.Custo > 0) partes.Add("≈ " + Relatorio.Dolares(soma.Custo));
            return string.Join(" · ", partes);
        }

        // O modelo (ou projeto) com mais tokens e a fatia dele: "Opus 5.5 94%"
        static string Principal(List<Relatorio.Registro> rs, Func<Relatorio.Registro, string> chave, Func<string, string> nome, long total)
        {
            var g = rs.GroupBy(chave, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Sum(r => r.L.Total)).First();
            return nome(g.Key) + " " + PctInteiro(g.Sum(r => r.L.Total), total);
        }

        // ---------- gráficos ----------
        // Rosca da participação (as 6 cores e "Outros") com a lista ao lado: nome, porcentagem e tokens. O cursor numa
        // linha da lista destaca a fatia e mostra a porcentagem dela no meio
        class Rosca : Linha
        {
            public List<Serie> Series; public long[] Valores;
            float D(Configuracoes j) { return j.E(136); }
            float AltLinha(Configuracoes j) { return j.E(28); }
            public override float Altura(Configuracoes j, float w) { return j.E(16) + Math.Max(D(j), Series.Count * AltLinha(j)) + j.E(16); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                long total = Valores.Sum();
                if (total <= 0) return;
                float d = D(j), cx = x + j.E(16) + d / 2, cy = y + h / 2;
                int sob = -1;
                for (int i = 0; i < Series.Count; i++) if (j.Sobre("rel-rosca:" + i)) sob = i;
                // Da maior fatia para a menor, "Outros" (sem chave) no fim; a cor continua a de cada um
                var ordem = Enumerable.Range(0, Series.Count).OrderBy(i => Series[i].Chave == null ? 1 : 0).ThenByDescending(i => Valores[i]).ToList();
                float ang = -90;
                var bordas = new List<float>();
                foreach (int i in ordem)
                {
                    if (Valores[i] <= 0) continue;
                    float varre = 360f * Valores[i] / total;
                    var cor = sob >= 0 && sob != i ? Sobre(Paleta.Alfa(Series[i].Cor, 0.3), j.c.Group) : Series[i].Cor;
                    using (var b = new SolidBrush(cor)) g.FillPie(b, cx - d / 2, cy - d / 2, d, d, ang, varre + 0.5f);
                    bordas.Add(ang);
                    ang += varre;
                }
                // Frestas de 2 px entre as fatias (na cor do fundo, sem contorno) e o furo
                if (bordas.Count > 1)
                    using (var pen = new Pen(j.c.Group, j.E(2)))
                        foreach (var a in bordas)
                        {
                            double rad = a * Math.PI / 180;
                            g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(rad) * d * 0.52f, cy + (float)Math.Sin(rad) * d * 0.52f);
                        }
                float furo = d - 2 * j.E(24);
                using (var b = new SolidBrush(j.c.Group)) g.FillEllipse(b, cx - furo / 2, cy - furo / 2, furo, furo);
                string grande = sob >= 0 ? Pct(Valores[sob], total) : Relatorio.Compacto(total);
                string pequeno = sob >= 0 ? Series[sob].Nome : "tokens".T();
                Tx.Linha(g, j.forte, grande, j.c.Text, j.c.Group, cx, cy - j.forte.Linha + j.E(3), Tx.Alinhar.Centro, 0);
                Tx.Linha(g, j.peq, pequeno, Sobre(j.c.Text2, j.c.Group), j.c.Group, cx, cy + j.E(3), Tx.Alinhar.Centro, furo - j.E(10));

                float lx = x + j.E(16) + d + j.E(28), lw = x + w - j.E(12) - lx;
                float ly = y + (h - Series.Count * AltLinha(j)) / 2;
                foreach (int i in ordem)
                {
                    var r = new RectangleF(lx - j.E(8), ly, lw + j.E(14), AltLinha(j));
                    Color fundo = j.c.Group;
                    if (i == sob) { fundo = Sobre(j.c.Hover, j.c.Group); j.Preencher(g, r, j.E(6), fundo); }
                    j.Registrar(r, "rel-rosca:" + i, null, true);
                    float ty = ly + (AltLinha(j) - j.corpo.Linha) / 2;
                    j.Preencher(g, new RectangleF(lx, ly + (AltLinha(j) - j.E(10)) / 2, j.E(10), j.E(10)), j.E(2.5f), Series[i].Cor);
                    string direita = Pct(Valores[i], total) + " · " + Relatorio.Compacto(Valores[i]);
                    float ld = Tx.Largura(j.corpo, direita);
                    Tx.Linha(g, j.corpo, direita, Sobre(j.c.Text2, fundo), fundo, lx + lw, ty, Tx.Alinhar.Direita, 0);
                    Tx.Linha(g, j.corpo, Series[i].Nome, j.c.Text, fundo, lx + j.E(18), ty, Tx.Alinhar.Esquerda, lw - j.E(18) - ld - j.E(12));
                    ly += AltLinha(j);
                }
            }
        }

        // Duas barras por provedor, modelo ou projeto, na mesma escala: a colorida é o período escolhido e a cinza, o
        // anterior; à direita, o total e a variação. O cursor numa linha troca a direita pelos dois números com os nomes
        class Comparacao : Linha
        {
            public List<Serie> Series; public long[] Agora, Antes; public string NomeAgora, NomeAntes;
            float AltLinha(Configuracoes j) { return j.E(9) + j.corpo.Linha + j.E(7) + j.E(6) + j.E(3) + j.E(3) + j.E(9); }
            public List<int> Ordem()
            {
                return Enumerable.Range(0, Series.Count).Where(i => Agora[i] > 0 || Antes[i] > 0)
                    .OrderBy(i => Series[i].Chave == null ? 1 : 0).ThenByDescending(i => Agora[i]).ThenByDescending(i => Antes[i]).ToList();
            }
            public override float Altura(Configuracoes j, float w) { return Ordem().Count * AltLinha(j); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                long max = Math.Max(1, Math.Max(Agora.DefaultIfEmpty(0).Max(), Antes.DefaultIfEmpty(0).Max()));
                var cinza = CorOutros(j.Escuro);
                float yy = y;
                foreach (int i in Ordem())
                {
                    string id = "rel-cmp:" + i;
                    var r = new RectangleF(x + j.E(1), yy, w - j.E(2), AltLinha(j));
                    bool sob = j.Sobre(id);
                    Color fundo = j.c.Group;
                    if (sob) { fundo = Sobre(j.c.Hover, j.c.Group); j.Preencher(g, r, j.E(9), fundo); }
                    j.Registrar(r, id, null, true);
                    float xi = x + j.E(12), xf = x + w - j.E(12), ty = yy + j.E(9);
                    j.Preencher(g, new RectangleF(xi, ty + (j.corpo.Linha - j.E(10)) / 2, j.E(10), j.E(10)), j.E(2.5f), Series[i].Cor);
                    string direita = sob ? NomeAgora + " " + Relatorio.Compacto(Agora[i]) + " · " + NomeAntes + " " + Relatorio.Compacto(Antes[i])
                        : Relatorio.Compacto(Agora[i]) + " · " + Variacao(Agora[i], Antes[i]);
                    float ld = Tx.Largura(j.corpo, direita);
                    Tx.Linha(g, j.corpo, direita, sob ? j.c.Text : Sobre(j.c.Text2, fundo), fundo, xf, ty, Tx.Alinhar.Direita, 0);
                    Tx.Linha(g, j.corpo, Series[i].Nome, j.c.Text, fundo, xi + j.E(18), ty, Tx.Alinhar.Esquerda, xf - ld - j.E(12) - xi - j.E(18));
                    float yb = ty + j.corpo.Linha + j.E(7), wb = xf - xi;
                    if (Agora[i] > 0) j.Preencher(g, new RectangleF(xi, yb, Math.Max(j.E(4), wb * Agora[i] / max), j.E(6)), j.E(3), Series[i].Cor);
                    if (Antes[i] > 0) j.Preencher(g, new RectangleF(xi, yb + j.E(9), Math.Max(j.E(3), wb * Antes[i] / max), j.E(3)), j.E(1.5f), cinza);
                    yy += AltLinha(j);
                }
            }
        }

        // As cores de cada série, quando são muitas para caber ao lado da legenda do gráfico
        class LegendaCores : Linha
        {
            public List<Serie> Series;
            List<KeyValuePair<string, Color>> Itens() { return Series.Select(s => new KeyValuePair<string, Color>(s.Nome, s.Cor)).ToList(); }
            public override float Altura(Configuracoes j, float w) { return j.E(10) + j.Chips(null, 0, 0, w - j.E(24), Itens(), j.c.Group) + j.E(10); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                j.Chips(g, x + j.E(12), y + j.E(10), w - j.E(24), Itens(), j.c.Group);
            }
        }

        class SerieCurva { public string Nome, Curto; public Color Cor; public double?[] V; public bool Fraca; }   // Curto: na legenda de cima

        // Linhas sobre um eixo só de tokens: a progressão acumulada ou as semanas do mês. Fio de 2 px (a média, mais
        // fina e cinza); o cursor mostra a linha vertical, os pontos e os valores de cada linha naquela posição
        class Curvas : Linha
        {
            public string Id;
            public List<SerieCurva> Series = new List<SerieCurva>();
            public string[] Rotulos, Nomes;
            public string Resumo;   // legenda de cima sem o cursor; nulo = o último ponto da primeira linha
            float Plot(Configuracoes j) { return j.E(130); }
            List<KeyValuePair<string, Color>> Itens() { return Series.Select(s => new KeyValuePair<string, Color>(s.Nome, s.Cor)).ToList(); }

            public override float Altura(Configuracoes j, float w)
            {
                return j.E(12) + j.peq.Linha + j.E(14) + Plot(j) + j.E(6) + j.peq.Linha + j.E(12) + j.Chips(null, 0, 0, w - j.E(24), Itens(), j.c.Group) + j.E(12);
            }

            static double Teto(double max)
            {
                if (max <= 0) return 1;
                double e = Math.Pow(10, Math.Floor(Math.Log10(max))), m = max / e;
                return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * e;
            }

            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                int n = Rotulos.Length;
                var fraco = Sobre(j.c.Text2, j.c.Group);
                double max = 0;
                foreach (var s in Series) foreach (var v in s.V) if (v.HasValue && v.Value > max) max = v.Value;
                double teto = Teto(max);
                float x0 = x + j.E(12) + j.E(44), x1 = x + w - j.E(18), topo = y + j.E(12) + j.peq.Linha + j.E(14), plot = Plot(j), base_ = topo + plot;
                Func<int, float> px = i => n <= 1 ? (x0 + x1) / 2 : x0 + (x1 - x0) * i / (n - 1);
                Func<double, float> py = v => base_ - (float)(v / teto) * plot;

                // Grade em fio: zero, metade e o teto
                using (var pen = new Pen(Sobre(j.c.Sep, j.c.Group), Math.Max(1, j.E(1))))
                    for (int k = 0; k <= 2; k++)
                    {
                        float gy = base_ - plot * k / 2f;
                        g.DrawLine(pen, x0, gy, x1, gy);
                        Tx.Linha(g, j.peq, Relatorio.Compacto((long)(teto * k / 2)), fraco, j.c.Group, x0 - j.E(8), gy - j.peq.Linha / 2, Tx.Alinhar.Direita, 0);
                    }

                int sob = -1;
                for (int i = 0; i < n; i++) if (j.Sobre(Id + ":" + i)) sob = i;
                if (sob >= 0) using (var pen = new Pen(Sobre(Paleta.Alfa(j.c.Text2, 0.6), j.c.Group), Math.Max(1, j.E(1)))) g.DrawLine(pen, px(sob), topo, px(sob), base_);

                // As fracas (médias) embaixo; a primeira linha (o período escolhido) por cima de todas
                foreach (var s in Series.Where(o => o.Fraca).Concat(Series.Where(o => !o.Fraca).Reverse()))
                {
                    using (var pen = new Pen(s.Cor, s.Fraca ? j.E(1.5f) : j.E(2)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        var pts = new List<PointF>();
                        for (int i = 0; i <= n; i++)
                        {
                            if (i < n && s.V[i].HasValue) { pts.Add(new PointF(px(i), py(s.V[i].Value))); continue; }
                            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
                            else if (pts.Count == 1) using (var b = new SolidBrush(s.Cor)) g.FillEllipse(b, pts[0].X - j.E(2.5f), pts[0].Y - j.E(2.5f), j.E(5), j.E(5));
                            pts.Clear();
                        }
                    }
                }

                // Pontos sob o cursor: 8 px com anel de 2 px na cor do fundo
                if (sob >= 0)
                    foreach (var s in Series)
                    {
                        if (!s.V[sob].HasValue) continue;
                        float mx = px(sob), my = py(s.V[sob].Value), r = j.E(4);
                        using (var b = new SolidBrush(j.c.Group)) g.FillEllipse(b, mx - r - j.E(2), my - r - j.E(2), 2 * r + j.E(4), 2 * r + j.E(4));
                        using (var b = new SolidBrush(s.Cor)) g.FillEllipse(b, mx - r, my - r, 2 * r, 2 * r);
                    }

                // Faixa de cursor de cada ponto (a vaga inteira, não só o ponto) e o eixo de baixo
                float vaga = n <= 1 ? x1 - x0 : (x1 - x0) / (n - 1);
                for (int i = 0; i < n; i++)
                {
                    j.Registrar(new RectangleF(px(i) - vaga / 2, topo - j.E(6), vaga, plot + j.E(12)), Id + ":" + i, null, true);
                    if (Rotulos[i] != null) Tx.Linha(g, j.peq, Rotulos[i], i == sob ? j.c.Text : fraco, j.c.Group, px(i), base_ + j.E(6), Tx.Alinhar.Centro, 0);
                }

                // Legenda de cima: o ponto sob o cursor; sem cursor, o resumo ou o último ponto da primeira linha
                string legenda = null;
                int idx = sob;
                if (idx < 0 && Resumo != null) legenda = Resumo;
                else
                {
                    if (idx < 0 && Series.Count > 0) for (int i = n - 1; i >= 0; i--) if (Series[0].V[i].HasValue) { idx = i; break; }
                    if (idx >= 0)
                        legenda = Nomes[idx] + ": " + string.Join(" · ", Series.Where(s => s.V[idx].HasValue).Select(s => (s.Curto ?? s.Nome) + " " + Relatorio.Compacto((long)Math.Round(s.V[idx].Value))).ToArray());
                }
                if (legenda != null) Tx.Linha(g, j.peq, legenda, sob >= 0 ? j.c.Text : fraco, j.c.Group, x + j.E(12), y + j.E(12), Tx.Alinhar.Esquerda, w - j.E(24));
                j.Chips(g, x + j.E(12), base_ + j.E(6) + j.peq.Linha + j.E(12), w - j.E(24), Itens(), j.c.Group);
            }
        }

        // Dia da semana × hora em tons da cor de destaque (um tom só, com a escala embaixo); o cursor mostra a célula
        class Mapa : Linha
        {
            public long[,] V; public int[] Dias;   // Dias: quantos dias de cada dia da semana entraram
            public string[] NomesCurtos, NomesLongos;
            float Cel(Configuracoes j) { return j.E(16); }
            float Passo(Configuracoes j) { return Cel(j) + j.E(3); }
            public override float Altura(Configuracoes j, float w) { return j.E(12) + j.peq.Linha + j.E(10) + 7 * Passo(j) + j.E(4) + j.peq.Linha + j.E(12); }
            public override void Desenhar(Configuracoes j, Graphics g, float x, float y, float w, float h)
            {
                var fraco = Sobre(j.c.Text2, j.c.Group);
                float x0 = x + j.E(12) + j.E(40), wc = (x + w - j.E(12) - x0) / 24f, topo = y + j.E(12) + j.peq.Linha + j.E(10);
                long max = 1;
                foreach (var v in V) max = Math.Max(max, v);
                int sobD = -1, sobH = -1;
                for (int d = 0; d < 7; d++) for (int hr = 0; hr < 24; hr++) if (j.Sobre("rel-mapa:" + d + ":" + hr)) { sobD = d; sobH = hr; }
                var vazio = Sobre(j.c.SegBg, j.c.Group);
                for (int d = 0; d < 7; d++)
                {
                    float cy = topo + d * Passo(j);
                    Tx.Linha(g, j.peq, NomesCurtos[d], d == sobD ? j.c.Text : fraco, j.c.Group, x0 - j.E(8), cy + (Cel(j) - j.peq.Linha) / 2, Tx.Alinhar.Direita, 0);
                    for (int hr = 0; hr < 24; hr++)
                    {
                        var r = new RectangleF(x0 + wc * hr + j.E(1), cy, wc - j.E(2), Cel(j));
                        long v = V[d, hr];
                        j.Preencher(g, r, j.E(3), v > 0 ? j.Calor(v, max, j.c.Group) : vazio);
                        if (d == sobD && hr == sobH) j.Contornar(g, RectangleF.Inflate(r, j.E(1.5f), j.E(1.5f)), j.E(4), j.c.Text, Math.Max(1, j.E(1.5f)));
                        j.Registrar(RectangleF.Inflate(r, j.E(1), j.E(1.5f)), "rel-mapa:" + d + ":" + hr, null, true);
                    }
                }
                float yb = topo + 7 * Passo(j) + j.E(4);
                for (int hr = 0; hr < 24; hr += 6) Tx.Linha(g, j.peq, hr.ToString("00") + ":00", hr == sobH ? j.c.Text : fraco, j.c.Group, x0 + wc * hr + j.E(1), yb, Tx.Alinhar.Esquerda, 0);

                // Escala no canto de cima: menos → mais (embaixo ela cobria o "18:00")
                float xs = x + w - j.E(12), yt = y + j.E(12);
                string mais = "Mais".T(), menos = "Menos".T();
                Tx.Linha(g, j.peq, mais, fraco, j.c.Group, xs, yt, Tx.Alinhar.Direita, 0);
                xs -= Tx.Largura(j.peq, mais) + j.E(6);
                for (int i = 4; i >= 0; i--)
                {
                    xs -= j.E(11);
                    j.Preencher(g, new RectangleF(xs, yt + (j.peq.Linha - j.E(11)) / 2, j.E(11), j.E(11)), j.E(3), j.Calor(i * 25 + 1, 101, j.c.Group));
                    xs -= j.E(3);
                }
                Tx.Linha(g, j.peq, menos, fraco, j.c.Group, xs - j.E(3), yt, Tx.Alinhar.Direita, 0);
                xs -= Tx.Largura(j.peq, menos) + j.E(16);

                string legenda;
                if (sobD >= 0)
                {
                    long v = V[sobD, sobH];
                    legenda = NomesLongos[sobD] + ", " + sobH.ToString("00") + ":00–" + ((sobH + 1) % 24).ToString("00") + ":00: "
                        + (v > 0 ? "{0} tokens".T(Relatorio.Compacto(v)) + (Dias[sobD] > 1 ? " " + "em {0} dias".T(Dias[sobD]) : "") : "sem uso".T());
                }
                else
                {
                    int pd = 0, ph = 0;
                    for (int d = 0; d < 7; d++) for (int hr = 0; hr < 24; hr++) if (V[d, hr] > V[pd, ph]) { pd = d; ph = hr; }
                    legenda = "Pico: {0}, {1} tokens".T(NomesLongos[pd] + ", " + ph.ToString("00") + ":00", Relatorio.Compacto(V[pd, ph]));
                }
                Tx.Linha(g, j.peq, legenda, sobD >= 0 ? j.c.Text : fraco, j.c.Group, x + j.E(12), y + j.E(12), Tx.Alinhar.Esquerda, xs - x - j.E(12));
            }
        }
    }
}
