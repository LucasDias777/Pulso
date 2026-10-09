using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Pulso
{
    // Relatórios do livro de consumo: o dia, a semana (segunda a domingo) ou o mês, por provedor, modelo, projeto e
    // dia. Servem a aba Relatórios das Configurações (livro da memória) e a exportação em CSV e HTML. O diagnóstico
    // --relatorio <pasta> [dia|semana|mes] [AAAA-MM-DD] grava os dois arquivos lendo o livro do disco.
    static class Relatorio
    {
        public enum Periodo { Dia, Semana, Mes }

        public class Registro { public string Dia, Provedor, Modelo, Projeto; public Consumo.Linha L; }

        static readonly string[] CodigosPeriodo = { "dia", "semana", "mes" };

        public static int Gerar(string pasta, string periodo, string data)
        {
            int p = Array.IndexOf(CodigosPeriodo, periodo);
            DateTime refe = DateTime.Today, de, ate;
            if (p < 0 || (data != null && !DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out refe))) return 1;
            Intervalo((Periodo)p, refe, out de, out ate);
            var regs = Registros(de, ate, true);
            Directory.CreateDirectory(pasta);
            string nome = NomeArquivo((Periodo)p, de);
            File.WriteAllText(Path.Combine(pasta, nome + ".csv"), Csv(regs), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(pasta, nome + ".html"), Html(regs, Titulo((Periodo)p, de, ate, int.MinValue), (Periodo)p != Periodo.Dia), new UTF8Encoding(false));
            Log.Info("relatório de consumo (" + nome + "): " + regs.Count + " linhas em " + pasta);
            return 0;
        }

        // ---------- período ----------
        public static void Intervalo(Periodo p, DateTime refe, out DateTime de, out DateTime ate)
        {
            refe = refe.Date;
            if (p == Periodo.Dia) { de = ate = refe; return; }
            if (p == Periodo.Semana) { de = refe.AddDays(-(((int)refe.DayOfWeek + 6) % 7)); ate = de.AddDays(6); return; }
            de = new DateTime(refe.Year, refe.Month, 1); ate = de.AddMonths(1).AddDays(-1);
        }

        // Uma data dentro do período 'desloc' períodos antes do atual (0 = o atual, -1 = o anterior)
        public static DateTime Referencia(Periodo p, int desloc)
        {
            var hoje = DateTime.Today;
            return p == Periodo.Dia ? hoje.AddDays(desloc) : p == Periodo.Semana ? hoje.AddDays(7 * desloc) : hoje.AddMonths(desloc);
        }

        // "Hoje", "Ontem", "segunda-feira, 6 de outubro de 2026", "6 de outubro a 12 de outubro", "Outubro de 2026"
        public static string Titulo(Periodo p, DateTime de, DateTime ate, int desloc)
        {
            var cult = Idioma.Cultura;
            if (p == Periodo.Dia) return desloc == 0 ? "Hoje".T() : desloc == -1 ? "Ontem".T() : Util.Maiuscula(de.ToString("D", cult));
            if (p == Periodo.Semana)
            {
                string md = cult.DateTimeFormat.MonthDayPattern;
                return "{0} a {1}".T(de.ToString(md, cult), ate.ToString(md, cult) + (ate.Year != DateTime.Today.Year ? " " + ate.Year : ""));
            }
            return Util.Maiuscula(de.ToString("Y", cult));
        }

        public static string NomeArquivo(Periodo p, DateTime de) { return "consumo-" + CodigosPeriodo[(int)p] + "-" + de.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        // ---------- dados ----------
        // Do disco (diagnóstico, com o Pulso fechado) ou da memória (o Pulso aberto grava o disco a cada minuto)
        public static List<Registro> Registros(DateTime de, DateTime ate, bool doDisco)
        {
            var regs = new List<Registro>();
            foreach (var p in Consumo.Provedores)
                foreach (var d in doDisco ? Consumo.Ler(p, de, ate) : Consumo.De(p).Copia(de, ate))
                    foreach (var m in d.Value)
                        foreach (var pr in m.Value)
                            regs.Add(new Registro { Dia = d.Key, Provedor = p, Modelo = m.Key, Projeto = pr.Key, L = pr.Value });
            return regs.OrderBy(r => r.Dia, StringComparer.Ordinal).ThenBy(r => r.Provedor).ThenBy(r => r.Modelo, StringComparer.Ordinal).ThenBy(r => r.Projeto, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Consumo.Linha Soma(IEnumerable<Registro> rs)
        {
            var t = new Consumo.Linha();
            foreach (var r in rs) t.Somar(r.L);
            return t;
        }

        public static DateTime Data(Registro r) { return DateTime.ParseExact(r.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture); }

        // ---------- nomes e números ----------
        public static string NomeProvedor(string id) { return id == "claude" ? "Claude Code" : id == "codex" ? "Codex" : id; }
        public static string NomeModelo(string m) { return m == "desconhecido" ? "Modelo desconhecido".T() : m; }
        public static string NomeProjeto(string p) { return p.T(); } // "Sem pasta" traduzido; nomes de pasta ficam como estão

        // 950 · 12,3 mil · 4,8 mi · 1,2 bi (no idioma escolhido)
        public static string Compacto(long v)
        {
            var cult = Idioma.Cultura;
            if (v < 1000) return v.ToString("N0", cult);
            if (v < 1000000) return "{0} mil".T((v / 1e3).ToString(v < 10000 ? "0.#" : "0", cult));
            if (v < 1000000000) return "{0} mi".T((v / 1e6).ToString(v < 10000000 ? "0.#" : "0", cult));
            return "{0} bi".T((v / 1e9).ToString("0.#", cult));
        }

        public static string Dolares(double v) { return "US$ " + v.ToString(v < 1 ? "0.00##" : "N2", Idioma.Cultura); }

        // ---------- CSV ----------
        // Ponto e vírgula e vírgula decimal: o Excel em português abre direto, e o BOM preserva os acentos
        public static string Csv(List<Registro> regs)
        {
            var br = new CultureInfo("pt-BR");
            var sb = new StringBuilder("dia;provedor;modelo;projeto;respostas;entrada;cache_lido;cache_gravado;saida;raciocinio;total;peso;custo_usd\r\n");
            foreach (var r in regs)
            {
                var l = r.L;
                sb.Append(string.Join(";", new[]
                {
                    r.Dia, NomeProvedor(r.Provedor), CampoCsv(r.Modelo), CampoCsv(r.Projeto), l.Respostas.ToString(br),
                    l.Tokens[0].ToString(br), l.Tokens[1].ToString(br), l.Tokens[2].ToString(br), l.Tokens[3].ToString(br), l.Tokens[4].ToString(br),
                    l.Total.ToString(br), l.Peso.ToString("0.######", br), l.Custo > 0 ? l.Custo.ToString("0.######", br) : "",
                })).Append("\r\n");
            }
            return sb.ToString();
        }

        static string CampoCsv(string s) { return s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\""; }

        // ---------- HTML ----------
        public static string Html(List<Registro> regs, string titulo, bool porDia)
        {
            var cult = Idioma.Cultura;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"").Append(cult.Name).Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>").Append(E("Consumo".T())).Append(" · ").Append(E(titulo)).Append("</title><style>").Append(Estilo).Append("</style></head><body><main>");
            sb.Append("<h1>").Append(E("Consumo".T())).Append(" <span>").Append(E(titulo)).Append("</span></h1>");
            sb.Append("<p class=\"nota\">").Append(E("Tokens das respostas registradas pelo Claude Code e pelo Codex neste computador. Gerado em {0}.".T(DateTime.Now.ToString("g", cult)))).Append("</p>");
            if (regs.Count == 0) { sb.Append("<p class=\"vazio\">").Append(E("Nada registrado neste período.".T())).Append("</p></main></body></html>"); return sb.ToString(); }

            var provedores = regs.Select(r => r.Provedor).Distinct().ToList();
            if (provedores.Count > 1)
            {
                sb.Append("<h2>").Append(E("Por provedor".T())).Append("</h2>");
                Tabela(sb, "Provedor".T(), Agrupar(regs, r => NomeProvedor(r.Provedor)));
            }
            foreach (var p in provedores)
            {
                var doProvedor = regs.Where(r => r.Provedor == p).ToList();
                sb.Append("<h2>").Append(E(NomeProvedor(p) + " — " + "Por modelo".T())).Append("</h2>");
                Tabela(sb, "Modelo".T(), Agrupar(doProvedor, r => NomeModelo(r.Modelo)));
                sb.Append("<h2>").Append(E(NomeProvedor(p) + " — " + "Por projeto".T())).Append("</h2>");
                Tabela(sb, "Projeto".T(), Agrupar(doProvedor, r => NomeProjeto(r.Projeto)));
            }
            if (provedores.Count > 1)
            {
                sb.Append("<h2>").Append(E("Por projeto".T())).Append("</h2>");
                Cruzada(sb, "Projeto".T(), regs, r => NomeProjeto(r.Projeto), provedores, true);
            }
            if (porDia)
            {
                sb.Append("<h2>").Append(E("Por dia".T())).Append("</h2>");
                Cruzada(sb, "Dia".T(), regs, r => Data(r).ToString("ddd " + cult.DateTimeFormat.ShortDatePattern, cult), provedores, false);
            }
            sb.Append("<p class=\"nota\">").Append(E("Total = entrada + cache lido + cache gravado + saída (o raciocínio já está na saída). A porcentagem é a fatia dos tokens; US$ é o preço de API equivalente, não uma cobrança.".T())).Append("</p>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        static List<KeyValuePair<string, Consumo.Linha>> Agrupar(List<Registro> rs, Func<Registro, string> chave)
        {
            return rs.GroupBy(chave, StringComparer.OrdinalIgnoreCase).Select(g => new KeyValuePair<string, Consumo.Linha>(g.Key, Soma(g))).ToList();
        }

        static void Tabela(StringBuilder sb, string rotulo, List<KeyValuePair<string, Consumo.Linha>> linhas)
        {
            long total = linhas.Sum(l => l.Value.Total);
            bool custo = linhas.Any(l => l.Value.Custo > 0);
            sb.Append("<div class=\"rolagem\"><table><thead><tr><th>").Append(E(rotulo)).Append("</th>");
            foreach (var t in new[] { "Respostas", "Entrada", "Cache lido", "Cache gravado", "Saída", "Raciocínio", "Total", "% dos tokens" }) sb.Append("<th>").Append(E(t.T())).Append("</th>");
            sb.Append(custo ? "<th>US$</th>" : "").Append("</tr></thead><tbody>");
            foreach (var kv in linhas.OrderByDescending(l => l.Value.Total))
            {
                var l = kv.Value;
                sb.Append("<tr><td>").Append(E(kv.Key)).Append("</td>").Append(N(l.Respostas)).Append(N(l.Tokens[0])).Append(N(l.Tokens[1])).Append(N(l.Tokens[2]))
                  .Append(N(l.Tokens[3])).Append(N(l.Tokens[4])).Append(N(l.Total)).Append(Pct(total > 0 ? (double)l.Total / total : 0));
                if (custo) sb.Append("<td>").Append(l.Custo > 0 ? l.Custo.ToString("N2", Idioma.Cultura) : "–").Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table></div>");
        }

        // Total de tokens de cada provedor por linha (projeto ou dia), com a fatia no total
        static void Cruzada(StringBuilder sb, string rotulo, List<Registro> regs, Func<Registro, string> chave, List<string> provedores, bool ordenarPorTotal)
        {
            long totalGeral = Soma(regs).Total;
            sb.Append("<div class=\"rolagem\"><table><thead><tr><th>").Append(E(rotulo)).Append("</th>");
            foreach (var p in provedores) sb.Append("<th>").Append(NomeProvedor(p)).Append("</th>");
            sb.Append("<th>").Append(E("Total".T())).Append("</th><th>").Append(E("% dos tokens".T())).Append("</th></tr></thead><tbody>");
            var grupos = regs.GroupBy(chave, StringComparer.OrdinalIgnoreCase).Select(g => new { Nome = g.Key, Itens = g.ToList(), Total = g.Sum(r => r.L.Total) });
            foreach (var g in ordenarPorTotal ? grupos.OrderByDescending(g => g.Total) : grupos)
            {
                sb.Append("<tr><td>").Append(E(g.Nome)).Append("</td>");
                foreach (var p in provedores) sb.Append(N(g.Itens.Where(r => r.Provedor == p).Sum(r => r.L.Total)));
                sb.Append(N(g.Total)).Append(Pct(totalGeral > 0 ? (double)g.Total / totalGeral : 0)).Append("</tr>");
            }
            sb.Append("</tbody></table></div>");
        }

        static string N(long v) { return "<td>" + (v == 0 ? "–" : v.ToString("N0", Idioma.Cultura)) + "</td>"; }

        static string Pct(double f)
        {
            return "<td class=\"pct\"><span class=\"barra\" style=\"width:" + Math.Round(Math.Min(1, f) * 100, 1).ToString(CultureInfo.InvariantCulture) +
                   "%\"></span><span>" + (f * 100).ToString("0.0", Idioma.Cultura) + "%</span></td>";
        }

        static string E(string s) { return WebUtility.HtmlEncode(s); }

        const string Estilo =
            ":root{--fundo:#f7f7f5;--papel:#fff;--texto:#1d1d1f;--fraco:#6e6e73;--linha:#e6e6e3;--barra:#d9e7ff;color-scheme:light dark}" +
            "@media (prefers-color-scheme:dark){:root{--fundo:#0e0e10;--papel:#17171a;--texto:#ececef;--fraco:#9a9aa2;--linha:#2a2a30;--barra:#23324d}}" +
            "*{box-sizing:border-box}body{margin:0;background:var(--fundo);color:var(--texto);font:14px/1.5 'Segoe UI',system-ui,sans-serif}" +
            "main{max-width:1100px;margin:0 auto;padding:32px 16px 48px}h1{font-size:24px;margin:0 0 4px}h1 span{color:var(--fraco);font-weight:400}" +
            "h2{font-size:16px;margin:28px 0 8px}.nota{color:var(--fraco);font-size:12.5px;max-width:80ch}.vazio{margin-top:24px}" +
            ".rolagem{overflow-x:auto;background:var(--papel);border:1px solid var(--linha);border-radius:10px}" +
            "table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}th,td{padding:7px 12px;text-align:right;white-space:nowrap;border-bottom:1px solid var(--linha)}" +
            "th{font-size:12px;font-weight:600;color:var(--fraco)}th:first-child,td:first-child{text-align:left;white-space:normal}tbody tr:last-child td{border-bottom:0}" +
            ".pct{position:relative;min-width:84px}.barra{position:absolute;right:0;top:6px;bottom:6px;background:var(--barra);border-radius:3px}" +
            ".pct>span:last-child{position:relative}";
    }
}
