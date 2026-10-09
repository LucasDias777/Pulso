using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Pulso
{
    // Livro de consumo: tokens por dia (data local), modelo e projeto de cada provedor lido neste computador. É a
    // base dos relatórios de dia, semana e mês. Um arquivo por provedor e mês em %APPDATA%\Pulso\consumo
    // (claude-2026-10.json), guardado por 13 meses: o Claude Code apaga os próprios transcritos depois de alguns dias.
    // Quem alimenta o livro grava junto as posições de leitura, para nenhuma resposta entrar duas vezes. Quando o
    // índice é refeito (versão nova ou primeira vez), o que é relido vai para um livro à parte e, ao terminar, cada
    // célula (dia, modelo, projeto) fica com a versão que tem mais respostas: transcrito já apagado não zera o gravado.
    class Consumo
    {
        public static readonly Consumo Claude = new Consumo("claude"), Codex = new Consumo("codex");
        public static readonly string[] Provedores = { "claude", "codex" };
        public static Consumo De(string id) { return id == "claude" ? Claude : id == "codex" ? Codex : null; }

        // Tokens: entrada sem cache, leitura de cache, gravação de cache, saída e raciocínio (já incluído na saída)
        public const int Campos = 5;
        public static readonly string[] NomesCampos = { "entrada", "cacheLeitura", "cacheEscrita", "saida", "raciocinio" };
        const int MesesGuardados = 13;

        public class Linha
        {
            public long Respostas;
            public readonly long[] Tokens = new long[Campos];
            public double Peso;   // base da fatia de cada um no uso: custo em dólar no Claude, tokens ponderados no Codex
            public double Custo;  // dólar equivalente de API (0 = desconhecido)
            public long Total { get { return Tokens[0] + Tokens[1] + Tokens[2] + Tokens[3]; } }
            public void Somar(Linha o) { Respostas += o.Respostas; for (int i = 0; i < Campos; i++) Tokens[i] += o.Tokens[i]; Peso += o.Peso; Custo += o.Custo; }
        }

        // dia → modelo → projeto
        public class Dias : Dictionary<string, Dictionary<string, Dictionary<string, Linha>>> { }

        public readonly string Provedor;
        readonly object trava = new object();
        Dias dados = new Dias();
        Dias relido;                                  // não nulo enquanto o índice é refeito
        readonly HashSet<string> mesesMudados = new HashSet<string>();

        Consumo(string provedor) { Provedor = provedor; }

        static string Pasta { get { return Path.Combine(Caminhos.Dados, "consumo"); } }

        // Primeiro dia do mês mais antigo guardado: o que é mais velho que isso não entra nem é lido dos transcritos
        public static DateTime InicioUtc
        {
            get { var d = DateTime.Now.AddMonths(-(MesesGuardados - 1)); return new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(); }
        }

        // tokens: na ordem de NomesCampos; peso e custo: o que esta resposta (ou o pedaço novo dela) acrescentou
        public void Registrar(DateTime quandoUtc, string modelo, string pasta, long[] tokens, double peso, double custo, bool novaResposta)
        {
            if (quandoUtc < InicioUtc || (tokens.All(t => t <= 0) && peso <= 0)) return;
            string dia = quandoUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (trava)
            {
                var l = Celula(relido ?? dados, dia, string.IsNullOrEmpty(modelo) ? "desconhecido" : modelo, Projetos.Nome(pasta));
                if (novaResposta) l.Respostas++;
                for (int i = 0; i < Campos; i++) l.Tokens[i] += Math.Max(0, tokens[i]);
                l.Peso += Math.Max(0, peso);
                l.Custo += Math.Max(0, custo);
                if (relido == null) mesesMudados.Add(dia.Substring(0, 7));
            }
        }

        static Linha Celula(Dias d, string dia, string modelo, string projeto)
        {
            Dictionary<string, Dictionary<string, Linha>> porModelo; Dictionary<string, Linha> porProjeto; Linha l;
            if (!d.TryGetValue(dia, out porModelo)) d[dia] = porModelo = new Dictionary<string, Dictionary<string, Linha>>();
            if (!porModelo.TryGetValue(modelo, out porProjeto)) porModelo[modelo] = porProjeto = new Dictionary<string, Linha>(StringComparer.OrdinalIgnoreCase);
            if (!porProjeto.TryGetValue(projeto, out l)) porProjeto[projeto] = l = new Linha();
            return l;
        }

        public void Carregar()
        {
            Podar();
            var d = Ler(Provedor, InicioUtc.ToLocalTime(), null);
            lock (trava) dados = d;
        }

        // Os dias de 'de' a 'ate' (datas locais) do que está na memória: com o Pulso aberto, mais novo que o disco
        public Dias Copia(DateTime de, DateTime ate)
        {
            string a = de.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), b = ate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var r = new Dias();
            lock (trava)
                foreach (var d in dados)
                    if (string.CompareOrdinal(d.Key, a) >= 0 && string.CompareOrdinal(d.Key, b) <= 0)
                        foreach (var m in d.Value)
                            foreach (var p in m.Value) Celula(r, d.Key, m.Key, p.Key).Somar(p.Value);
            return r;
        }

        public void IniciarReconstrucao() { lock (trava) relido = new Dias(); }

        // O índice terminou de ser relido: devolve quantos dias o relido trouxe
        public int ConcluirReconstrucao()
        {
            lock (trava)
            {
                if (relido == null) return 0;
                foreach (var dia in relido)
                    foreach (var m in dia.Value)
                        foreach (var p in m.Value)
                        {
                            var atual = Celula(dados, dia.Key, m.Key, p.Key);
                            if (p.Value.Respostas > atual.Respostas || (p.Value.Respostas == atual.Respostas && p.Value.Total > atual.Total))
                                dados[dia.Key][m.Key][p.Key] = p.Value;
                            mesesMudados.Add(dia.Key.Substring(0, 7));
                        }
                int n = relido.Count;
                relido = null;
                return n;
            }
        }

        // Foto dos meses que mudaram, para gravar depois das posições de leitura: uma resposta lida entre as duas fotos
        // fica só na das posições e entra no livro na gravação seguinte, nunca nas duas
        public Gravacao Preparar()
        {
            lock (trava)
            {
                var meses = mesesMudados.OrderBy(m => m).ToList();
                mesesMudados.Clear();
                var arquivos = meses.Select(m => new KeyValuePair<string, string>(Path.Combine(Pasta, Provedor + "-" + m + ".json"), Json.Write(Mes(m)))).ToList();
                return new Gravacao(this, meses, arquivos);
            }
        }

        public class Gravacao
        {
            readonly Consumo dono;
            readonly List<string> meses;
            readonly List<KeyValuePair<string, string>> arquivos;

            public Gravacao(Consumo dono, List<string> meses, List<KeyValuePair<string, string>> arquivos) { this.dono = dono; this.meses = meses; this.arquivos = arquivos; }

            public void Gravar()
            {
                if (arquivos.Count == 0) return;
                try
                {
                    Directory.CreateDirectory(Pasta);
                    foreach (var a in arquivos) Caminhos.GravarAtomico(a.Key, a.Value);
                }
                catch (Exception e) { Log.Erro("salvar consumo do " + dono.Provedor, e); Desistir(); }
            }

            // As posições não foram gravadas: os meses voltam para a próxima vez
            public void Desistir() { lock (dono.trava) foreach (var m in meses) dono.mesesMudados.Add(m); }
        }

        Dictionary<string, object> Mes(string mes)
        {
            var dias = new Dictionary<string, object>();
            foreach (var dia in dados.Keys.Where(d => d.StartsWith(mes, StringComparison.Ordinal)).OrderBy(d => d, StringComparer.Ordinal))
                dias[dia] = dados[dia].OrderBy(m => m.Key, StringComparer.Ordinal).ToDictionary(m => m.Key,
                    m => (object)m.Value.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(p => p.Key, p => (object)ParaJson(p.Value)));
            return new Dictionary<string, object> { { "versao", 1 }, { "provedor", Provedor }, { "dias", dias } };
        }

        static Dictionary<string, object> ParaJson(Linha l)
        {
            var o = new Dictionary<string, object> { { "respostas", l.Respostas } };
            for (int i = 0; i < Campos; i++) o[NomesCampos[i]] = l.Tokens[i];
            o["peso"] = Math.Round(l.Peso, 6);
            if (l.Custo > 0) o["custo"] = Math.Round(l.Custo, 6);
            return o;
        }

        // Dias gravados de um provedor, de 'de' a 'ate' (datas locais, inclusive); usado também pelo relatório
        public static Dias Ler(string provedor, DateTime? de, DateTime? ate)
        {
            var r = new Dias();
            if (!Directory.Exists(Pasta)) return r;
            foreach (var arq in Directory.GetFiles(Pasta, provedor + "-*.json"))
            {
                try
                {
                    var dias = Json.Obj(Json.Parse(File.ReadAllText(arq)), "dias");
                    if (dias == null) continue;
                    foreach (var d in dias)
                    {
                        DateTime data;
                        if (!DateTime.TryParseExact(d.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out data)) continue;
                        if ((de.HasValue && data < de.Value.Date) || (ate.HasValue && data > ate.Value.Date)) continue;
                        var modelos = d.Value as IDictionary<string, object>;
                        if (modelos == null) continue;
                        foreach (var m in modelos)
                        {
                            var projetos = m.Value as IDictionary<string, object>;
                            if (projetos == null) continue;
                            foreach (var p in projetos)
                            {
                                var l = Celula(r, d.Key, m.Key, p.Key);
                                l.Respostas = (long)(Json.Num(p.Value, "respostas") ?? 0);
                                for (int i = 0; i < Campos; i++) l.Tokens[i] = (long)(Json.Num(p.Value, NomesCampos[i]) ?? 0);
                                l.Peso = Json.Num(p.Value, "peso") ?? 0;
                                l.Custo = Json.Num(p.Value, "custo") ?? 0;
                            }
                        }
                    }
                }
                catch (Exception e) { Log.Erro("ler consumo " + Path.GetFileName(arq), e); }
            }
            return r;
        }

        // Meses além dos 13 guardados saem do disco
        void Podar()
        {
            string corte = InicioUtc.ToLocalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
            try
            {
                if (!Directory.Exists(Pasta)) return;
                foreach (var arq in Directory.GetFiles(Pasta, Provedor + "-*.json"))
                {
                    string mes = Path.GetFileNameWithoutExtension(arq).Substring(Provedor.Length + 1);
                    if (mes.Length == 7 && string.CompareOrdinal(mes, corte) < 0) File.Delete(arq);
                }
            }
            catch (Exception e) { Log.Erro("podar consumo do " + Provedor, e); }
        }
    }
}
