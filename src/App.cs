using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Pulso
{
    static class Programa
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--statusline")
            {
                try { BarraDeStatus.Executar(); } catch { }
                return;
            }
            if (args.Length > 0 && args[0] == "--gravar-teste")
            {
                Mensagens.Enviar(5, "");
                return;
            }
            if (args.Length > 0 && args[0] == "--previa")
            {
                Mensagens.Enviar(4, "");
                return;
            }
            if (args.Length > 1 && args[0] == "--captura")
            {
                Mensagens.Enviar(3, System.IO.Path.GetFullPath(args[1]));
                return;
            }
            // Instalação (instalar.cmd / atualizar.cmd / desinstalar.cmd)
            if (args.Length > 0 && args[0] == "--sair")
            {
                Instalacao.FecharAberto();
                return;
            }
            if (args.Length > 0 && (args[0] == "--registrar" || args[0] == "--desinstalar"))
            {
                Application.EnableVisualStyles();
                Environment.Exit(args[0] == "--registrar" ? Instalacao.Registrar(args.Length > 1 ? args[1] : null) : Instalacao.Desinstalar());
            }

            bool novo;
            using (var unica = new Mutex(true, @"Local\Pulso", out novo))
            {
                if (!novo)
                {
                    // Já está rodando: a segunda execução só abre as Configurações da primeira
                    Mensagens.Enviar(1, "config");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Erro("interface", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Info("ERRO fatal: " + e.ExceptionObject);
                Caminhos.Garantir();
                Log.Info("Pulso iniciado");
                Config.Carregar();
                EstadoSalvo.Carregar();
                using (var app = new App()) Application.Run(app);
                EstadoSalvo.Gravar(true);
                Log.Info("Pulso encerrado");
            }
        }
    }

    class App : ApplicationContext
    {
        public readonly NotchJanela Notch;
        readonly Bandeja bandeja;
        readonly Mensagens mensagens;
        readonly ClaudeFonte claude = new ClaudeFonte();
        readonly CodexFonte codex = new CodexFonte();
        readonly CodexProjetos codexProjetos = new CodexProjetos();
        readonly Dictionary<string, FonteExtra> extras = new Dictionary<string, FonteExtra>();
        readonly Avisos avisos;
        public readonly CartaoAviso Cartao;
        readonly ContextMenuStrip menuNotch = new ContextMenuStrip();
        Configuracoes config;
        public bool AtalhoAtivo { get; private set; }

        public App()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Estado.Iniciar(SynchronizationContext.Current);

            mensagens = new Mensagens();
            mensagens.Recebeu += (tipo, texto) =>
            {
                if (tipo == 1) AbrirConfig();
                else if (tipo == 2) claude.DaBarraDeStatus(Json.Parse(texto));
                else if (tipo == 4) Cartao.Mostrar(Avisos.Exemplo());
                else if (tipo == 6) Sair(); // instalar/atualizar/desinstalar precisam do Pulso fechado
                else if (tipo == 5)
                    // Diagnóstico: grava a próxima combinação como nas Configurações e registra no log
                    Atalhos.Gravar((m, k) => Log.Info("teste de atalho: " + Atalhos.Texto(m, (int)k) + (TrocarAtalho(m, (int)k) ? " registrado" : " recusado (em uso)")),
                        () => Log.Info("teste de atalho: cancelado"));
                else if (tipo == 3)
                {
                    Notch.Capturar(texto);
                    Retrato(texto);
                    try { Configuracoes.Capturar(this, texto); } catch (Exception e) { Log.Erro("captura das configurações", e); }
                }
            };
            mensagens.Atalho += delegate { Notch.AlternarVisivel(); };

            Notch = new NotchJanela();
            Notch.AbrirConfig += AbrirConfig;
            Notch.PedirAtualizacao += Atualizar;
            Bandeja.Estilizar(menuNotch);
            Notch.AbrirMenu += p =>
            {
                bandeja.Montar(menuNotch, Notch.ProvedorEm(p) ?? Config.Atual.Provedores[0]);
                menuNotch.Show(p);
            };

            bandeja = new Bandeja(this);
            bandeja.Visivel = Config.Atual.IconeBandeja;
            Cartao = new CartaoAviso(Notch);
            avisos = new Avisos(a => Cartao.Mostrar(a));
            Notch.PrimeiroPlano += MarcarVisto;

            AplicarAtalho();
            Integracao.IniciarComWindows(Config.Atual.IniciarComWindows);

            Notch.Show();
            lock (Estado.Trava)
            {
                Estado.Claude.Presente = System.IO.Directory.Exists(Caminhos.ClaudeDir);
                Estado.Claude.Ausencia = Estado.Claude.Presente ? null : "Claude Code não instalado";
                Estado.Codex.Presente = System.IO.Directory.Exists(Caminhos.CodexDir);
                Estado.Codex.Ausencia = Estado.Codex.Presente ? null : "Codex não instalado";
            }
            claude.Iniciar();
            codex.Iniciar();
            codexProjetos.Iniciar();
            foreach (var f in Extras.Criar()) { extras[f.Key] = f.Value; f.Value.Iniciar(); }
            Atualizacao.Iniciar(SynchronizationContext.Current);
        }

        // Diagnóstico: o que o Pulso está mostrando agora, em JSON (para conferir contra a fonte)
        static void Retrato(string pasta)
        {
            try
            {
                var o = new Dictionary<string, object> { { "agora", Tempo.AgoraMs() } };
                foreach (var id in Config.Atual.Provedores)
                {
                    var p = Estado.Copia(id);
                    o[id] = new Dictionary<string, object>
                    {
                        { "confirmado", p.Confirmado.HasValue ? (object)Tempo.UnixMs(p.Confirmado.Value) : null },
                        { "fonte", p.Fonte }, { "atividade", p.Atividade.ToString() }, { "nota", p.Nota },
                        { "janelas", p.Janelas.Select(j => (object)new Dictionary<string, object>
                            {
                                { "id", j.Id }, { "usado", j.Usado }, { "estimado", j.Estimado }, { "mostrado", Texto.Pct(j.Atual, j.Estimado.HasValue && j.Estimado > j.Usado + 0.004) },
                                { "resetaEm", j.ResetaEm.HasValue ? (object)Tempo.UnixMs(j.ResetaEm.Value) : null },
                            }).ToList() },
                    };
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "estado.json"), Json.Write(o));
            }
            catch (Exception e) { Log.Erro("retrato", e); }
        }

        // Voltou para a janela de uma sessão concluída: ela foi vista, sai da lista do cartão
        void MarcarVisto(IntPtr janela)
        {
            bool mudou = false;
            foreach (var p in Estado.Todos)
            {
                List<Sessao> vistas;
                lock (Estado.Trava) vistas = p.Sessoes.Values.Where(s => s.Estado == Atividade.Concluida).Select(s => s.Copia()).ToList();
                foreach (var s in vistas)
                {
                    if (!Foco.EDaPasta(janela, s.Pasta)) continue;
                    lock (Estado.Trava) { Sessao viva; if (p.Sessoes.TryGetValue(s.Id, out viva) && viva.Estado == Atividade.Concluida) { viva.Estado = Atividade.Ociosa; mudou = true; } }
                }
            }
            if (mudou) Estado.Avisar();
        }

        public void AplicarAtalho()
        {
            mensagens.SoltarAtalho();
            AtalhoAtivo = false;
            if (!Config.Atual.Atalho) return;
            AtalhoAtivo = mensagens.RegistrarAtalho(Config.Atual.AtalhoMods, Config.Atual.AtalhoTecla);
            if (!AtalhoAtivo) Log.Info(Config.Atual.AtalhoTexto + " já está em uso por outro programa; atalho não registrado");
        }

        // Troca o atalho só se a combinação nova estiver livre; senão volta ao anterior e devolve falso
        public bool TrocarAtalho(uint mods, int tecla)
        {
            var c = Config.Atual;
            mensagens.SoltarAtalho();
            if (!mensagens.RegistrarAtalho(mods, tecla))
            {
                AtalhoAtivo = c.Atalho && mensagens.RegistrarAtalho(c.AtalhoMods, c.AtalhoTecla);
                return false;
            }
            c.AtalhoMods = mods; c.AtalhoTecla = tecla; c.Atalho = true;
            c.Salvar();
            AtalhoAtivo = true;
            return true;
        }

        public void Atualizar(string id)
        {
            if (id == null || id == "claude") claude.Atualizar();
            if (id == null || id == "codex") codex.Atualizar();
            foreach (var kv in extras) if (id == null || id == kv.Key) kv.Value.Atualizar();
            if (id == null) foreach (var p in Config.Atual.Provedores) Notch.Atualizada(p);
        }

        public void AbrirConfig()
        {
            if (config == null || config.IsDisposed)
            {
                config = new Configuracoes(this);
                config.FormClosed += delegate { config = null; };
                config.Show();
            }
            config.WindowState = FormWindowState.Normal;
            config.Activate();
        }

        public void AplicarConfig()
        {
            bandeja.Visivel = Config.Atual.IconeBandeja || Config.Atual.Mostrar == Mostrar.Oculto;
            Notch.Reposicionar();
            Estado.Avisar();
        }

        public void Sair()
        {
            EstadoSalvo.Gravar(true);
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cartao.Dispose();
                claude.Dispose();
                codex.Dispose();
                codexProjetos.Dispose();
                foreach (var f in extras.Values) f.Dispose();
                bandeja.Dispose();
                Notch.Dispose();
                mensagens.Dispose();
                menuNotch.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
