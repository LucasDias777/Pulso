using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulso
{
    enum Mostrar { Sempre, AoPassar, Oculto }
    enum Borda { Direita, Esquerda, Cima, Baixo }
    enum AnelSemanal { Desligado, Dentro, Fora }
    enum Tema { Sistema, Escuro, Claro }

    // Preferências em %APPDATA%\Pulso\config.json. Nada de credencial aqui.
    class Config
    {
        public Mostrar Mostrar = Mostrar.Sempre;
        public Borda Borda = Borda.Direita;
        public Dictionary<string, double> Posicao = new Dictionary<string, double>(); // fração 0..1 ao longo de cada borda
        public string Monitor;              // \\.\DISPLAY2; nulo = principal
        public double Tamanho = 0.8;        // 0.8 | 1 | 1.25
        public Tema Tema = Tema.Escuro;
        public AnelSemanal AnelSemanal = AnelSemanal.Desligado;
        public bool CorGradual;             // falso = degraus (verde/amarelo/vermelho)
        public double LimiteAtencao = 0.5;
        public double LimiteCritico = 0.7;
        public List<string> Ativos = new List<string> { "claude", "codex" }; // provedores com anel, na ordem do notch
        public string AntigravityLeitura = "automatico";   // automatico | 5h | semana
        public string AntigravityModelos = "gemini";       // gemini | claude-gpt
        public bool IconeBandeja = true;
        public bool IniciarComWindows = true;
        public bool AvisoRenovacao = true;
        public bool AvisoLimite = true;     // aviso ao passar de 80% e 95%
        public bool Atalho = true;          // atalho global mostra/oculta o notch
        public uint AtalhoMods = Atalhos.PadraoMods;   // Ctrl/Alt/Shift/Win (flags do RegisterHotKey)
        public int AtalhoTecla = Atalhos.PadraoTecla;  // tecla virtual (padrão Y)
        public string AtalhoTexto { get { return Atalhos.Texto(AtalhoMods, AtalhoTecla); } }
        public bool Estimativa = true;      // move o medidor do Claude entre leituras exatas
        public bool AvisoSessaoFim = true;     // cartão quando uma sessão termina o turno
        public bool AvisoSessaoEspera = true;  // cartão quando uma sessão para esperando você
        public bool SomAvisos = true;
        public bool RitmoDiario;               // anel do Claude mostra o ritmo do dia (cota semanal ÷ 7)
        public bool AnelTracejado;             // anel semanal tracejado
        public bool CapsulaAdaptavel;          // cápsula recolhida muda de cor conforme o fundo
        public bool Arrastavel;                // pontinhos/engrenagem/Alt movem o notch; desligado, fica fixo no meio da borda

        public static Config Atual = new Config();

        public static void Carregar()
        {
            var c = new Config();
            try
            {
                if (File.Exists(Caminhos.Config))
                {
                    var o = Json.Parse(File.ReadAllText(Caminhos.Config));
                    c.Mostrar = Enum<Mostrar>(Json.Str(o, "mostrar"), c.Mostrar);
                    c.Borda = Enum<Borda>(Json.Str(o, "borda"), c.Borda);
                    var pos = Json.Obj(o, "posicao");
                    if (pos != null)
                        foreach (var kv in pos)
                        {
                            double? v = Json.Num(pos, kv.Key);
                            if (v.HasValue) c.Posicao[kv.Key] = Math.Max(0, Math.Min(1, v.Value));
                        }
                    c.Monitor = Json.Str(o, "monitor");
                    c.Tamanho = Limitar(Json.Num(o, "tamanho") ?? c.Tamanho, 0.6, 1.6);
                    c.Tema = Enum<Tema>(Json.Str(o, "tema"), c.Tema);
                    c.AnelSemanal = Enum<AnelSemanal>(Json.Str(o, "anelSemanal"), c.AnelSemanal);
                    c.CorGradual = Json.Bool(o, "corGradual") ?? c.CorGradual;
                    c.LimiteAtencao = Limitar(Json.Num(o, "limiteAtencao") ?? c.LimiteAtencao, 0.01, 0.99);
                    c.LimiteCritico = Limitar(Json.Num(o, "limiteCritico") ?? c.LimiteCritico, c.LimiteAtencao + 0.01, 1);
                    var ativos = Json.Arr(o, "ativos");
                    if (ativos != null) c.Ativos = ativos.Select(x => x as string).Where(x => x != null && Catalogo.Existe(x)).Distinct().ToList();
                    else
                    {
                        // Configuração antiga (só Claude e Codex)
                        c.Ativos = new List<string>();
                        if (Json.Bool(o, "claude") != false) c.Ativos.Add("claude");
                        if (Json.Bool(o, "codex") != false) c.Ativos.Add("codex");
                    }
                    c.AntigravityLeitura = Json.Str(o, "antigravityLeitura") ?? c.AntigravityLeitura;
                    c.AntigravityModelos = Json.Str(o, "antigravityModelos") ?? c.AntigravityModelos;
                    c.IconeBandeja = Json.Bool(o, "iconeBandeja") ?? c.IconeBandeja;
                    c.IniciarComWindows = Json.Bool(o, "iniciarComWindows") ?? c.IniciarComWindows;
                    c.AvisoRenovacao = Json.Bool(o, "avisoRenovacao") ?? c.AvisoRenovacao;
                    c.AvisoLimite = Json.Bool(o, "avisoLimite") ?? c.AvisoLimite;
                    c.Atalho = Json.Bool(o, "atalho") ?? c.Atalho;
                    double? am = Json.Num(o, "atalhoMods"), at = Json.Num(o, "atalhoTecla");
                    if (am.HasValue && at.HasValue && Atalhos.Valida((uint)am.Value, (System.Windows.Forms.Keys)(int)at.Value)) { c.AtalhoMods = (uint)am.Value; c.AtalhoTecla = (int)at.Value; }
                    c.Estimativa = Json.Bool(o, "estimativa") ?? c.Estimativa;
                    c.AvisoSessaoFim = Json.Bool(o, "avisoSessaoFim") ?? c.AvisoSessaoFim;
                    c.AvisoSessaoEspera = Json.Bool(o, "avisoSessaoEspera") ?? c.AvisoSessaoEspera;
                    c.SomAvisos = Json.Bool(o, "somAvisos") ?? c.SomAvisos;
                    c.RitmoDiario = Json.Bool(o, "ritmoDiario") ?? c.RitmoDiario;
                    c.AnelTracejado = Json.Bool(o, "anelTracejado") ?? c.AnelTracejado;
                    c.CapsulaAdaptavel = Json.Bool(o, "capsulaAdaptavel") ?? c.CapsulaAdaptavel;
                    c.Arrastavel = Json.Bool(o, "arrastavel") ?? c.Arrastavel;
                }
            }
            catch (Exception e) { Log.Erro("ler config", e); }
            if (c.Ativos.Count == 0) c.Ativos.Add("claude"); // a cápsula nunca fica vazia
            if (c.Mostrar == Mostrar.Oculto) c.IconeBandeja = true; // sem notch e sem bandeja não haveria como voltar
            Atual = c;
        }

        public void Salvar()
        {
            try
            {
                var pos = new Dictionary<string, object>();
                foreach (var kv in Posicao) pos[kv.Key] = kv.Value;
                var o = new Dictionary<string, object>
                {
                    { "mostrar", Mostrar.ToString() },
                    { "borda", Borda.ToString() },
                    { "posicao", pos },
                    { "monitor", Monitor },
                    { "tamanho", Tamanho },
                    { "tema", Tema.ToString() },
                    { "anelSemanal", AnelSemanal.ToString() },
                    { "corGradual", CorGradual },
                    { "limiteAtencao", LimiteAtencao },
                    { "limiteCritico", LimiteCritico },
                    { "ativos", Ativos.ToArray() },
                    { "antigravityLeitura", AntigravityLeitura },
                    { "antigravityModelos", AntigravityModelos },
                    { "iconeBandeja", IconeBandeja },
                    { "iniciarComWindows", IniciarComWindows },
                    { "avisoRenovacao", AvisoRenovacao },
                    { "avisoLimite", AvisoLimite },
                    { "atalho", Atalho },
                    { "atalhoMods", (long)AtalhoMods },
                    { "atalhoTecla", AtalhoTecla },
                    { "estimativa", Estimativa },
                    { "avisoSessaoFim", AvisoSessaoFim },
                    { "avisoSessaoEspera", AvisoSessaoEspera },
                    { "somAvisos", SomAvisos },
                    { "ritmoDiario", RitmoDiario },
                    { "anelTracejado", AnelTracejado },
                    { "capsulaAdaptavel", CapsulaAdaptavel },
                    { "arrastavel", Arrastavel },
                };
                Caminhos.GravarAtomico(Caminhos.Config, Json.Write(o));
            }
            catch (Exception e) { Log.Erro("salvar config", e); }
        }

        public double PosicaoNaBorda
        {
            // Fixo (não arrastável): sempre no meio; as posições salvas ficam guardadas para quando religar
            get { double v; return Arrastavel && Posicao.TryGetValue(Borda.ToString(), out v) ? v : 0.5; }
            set { Posicao[Borda.ToString()] = Math.Max(0, Math.Min(1, value)); }
        }

        public List<string> Provedores { get { return Ativos.Where(Catalogo.Existe).ToList(); } }

        public bool Ativo(string id) { return Ativos.Contains(id); }

        // Ligar põe o anel no fim da cápsula; desligar o último não vale (a cápsula nunca fica vazia)
        public void Ligar(string id, bool ligar)
        {
            if (ligar && !Ativos.Contains(id)) Ativos.Add(id);
            else if (!ligar && Ativos.Count > 1) Ativos.Remove(id);
        }

        static T Enum<T>(string s, T padrao) where T : struct
        {
            T v;
            return s != null && System.Enum.TryParse(s, true, out v) ? v : padrao;
        }

        static double Limitar(double v, double min, double max) { return Math.Max(min, Math.Min(max, v)); }
    }
}
