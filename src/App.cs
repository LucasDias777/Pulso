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
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        [STAThread]
        static void Main(string[] args)
        {
            // Aberto pelo Pulso que acabou de se atualizar (pelo download): espera ele fechar e segue como uma abertura comum
            if (args.Length > 1 && args[0] == "--apos-atualizar")
            {
                Instalacao.AposAtualizar(args[1]);
                args = new string[0];
            }
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
                // --captura <pasta> [escala] [cinza]
                Mensagens.Enviar(3, System.IO.Path.GetFullPath(args[1]) + (args.Length > 2 ? "|" + string.Join("|", args, 2, args.Length - 2) : ""));
                return;
            }
            // Diagnóstico: notch e cartões em PNG em várias escalas, sem abrir o app (só lê a config e o estado)
            if (args.Length > 1 && args[0] == "--bancada")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Config.Carregar();
                if (args.Length > 3) Config.Atual.Idioma = args[3]; // --bancada <pasta> <combinações> <pt|en|es>
                EstadoSalvo.Carregar();
                NotchJanela.Bancada(System.IO.Path.GetFullPath(args[1]), args.Length > 2 ? args[2] : "1x0.8,1x1,1x1.25,1.25x0.8,1.25x1,1.25x1.25,1.5x0.8,1.5x1,1.5x1.25");
                return;
            }
            // Diagnóstico: relatório do livro de consumo em HTML e CSV, sem abrir o app (--relatorio <pasta> [dia|semana|mes] [AAAA-MM-DD])
            if (args.Length > 1 && args[0] == "--relatorio")
            {
                Config.Carregar(); // idioma do relatório
                Environment.Exit(Relatorio.Gerar(System.IO.Path.GetFullPath(args[1]), args.Length > 2 ? args[2] : "mes", args.Length > 3 ? args[3] : null));
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
                Application.SetCompatibleTextRenderingDefault(false);
                Config.Carregar(); // idioma das mensagens
                Environment.Exit(args[0] == "--registrar" ? Instalacao.Registrar(args) : Instalacao.Desinstalar());
            }
            // Pulso.exe baixado das Releases e aberto de outra pasta: instala (perguntando antes) em vez de rodar dali
            if (args.Length == 0 && Instalacao.PrecisaInstalar)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Config.Carregar();
                Environment.Exit(Instalacao.InstalarDaqui());
            }
            // Diagnóstico: a janela de instalação em PNG, sem mostrar nem instalar nada (--previa-instalacao <arquivo> [pt|en|es])
            if (args.Length > 1 && args[0] == "--previa-instalacao")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Config.Carregar();
                if (args.Length > 2) Config.Atual.Idioma = args[2];
                using (var j = new OpcoesInstalacao())
                {
                    // Fora da tela: os controles só se desenham com a janela aberta
                    j.StartPosition = FormStartPosition.Manual;
                    j.Location = new Point(-20000, -20000);
                    j.ShowInTaskbar = false;
                    j.Show();
                    Application.DoEvents();
                    using (var b = new Bitmap(j.Width, j.Height))
                    {
                        Fotografar(j, b, 2); // PW_RENDERFULLCONTENT: moldura como o Windows desenha
                        // O Windows 10 não compõe janela fora da tela: o miolo vem vazio. Sem o PW_RENDERFULLCONTENT os
                        // controles se desenham (a moldura sai no estilo antigo)
                        if (MioloVazio(j, b)) Fotografar(j, b, 0);
                        b.Save(System.IO.Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }

            bool inicio = args.Length > 0 && args[0] == "--inicio"; // aberto pelo Windows ao entrar (chave Run)
            bool novo;
            using (var unica = new Mutex(true, @"Local\Pulso", out novo))
            {
                if (!novo)
                {
                    // Já está rodando: a segunda execução só abre as Configurações da primeira (a do Windows, nada)
                    if (!inicio) Mensagens.Enviar(1, "config");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Erro("interface", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Info("ERRO fatal: " + e.ExceptionObject);
                Caminhos.Garantir();
                Log.Info(inicio ? "Pulso iniciado pelo Windows" : "Pulso iniciado");
                Config.Carregar();
                EstadoSalvo.Carregar();
                using (var app = new App(inicio)) Application.Run(app);
                EstadoSalvo.Gravar(true);
                Log.Info("Pulso encerrado");
            }
        }

        static void Fotografar(Form j, Bitmap b, uint modo)
        {
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                IntPtr hdc = g.GetHdc();
                PrintWindow(j.Handle, hdc, modo);
                g.ReleaseHdc(hdc);
            }
        }

        // Área do cliente toda de uma cor só = nada se desenhou
        static bool MioloVazio(Form j, Bitmap b)
        {
            var o = j.PointToScreen(Point.Empty);
            var r = new Rectangle(o.X - j.Left, o.Y - j.Top, j.ClientSize.Width, j.ClientSize.Height);
            r.Intersect(new Rectangle(0, 0, b.Width, b.Height));
            if (r.Width <= 0 || r.Height <= 0) return true;
            var cor = b.GetPixel(r.X, r.Y);
            for (int y = r.Top; y < r.Bottom; y += 2)
                for (int x = r.Left; x < r.Right; x += 2)
                    if (b.GetPixel(x, y) != cor) return false;
            return true;
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

        public App(bool inicio)
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
                    // "pasta" ou "pasta|escala|cinza" (imagens do README: escala fixa e texto em cinza)
                    var partes = texto.Split('|');
                    float escala;
                    if (partes.Length > 1 && float.TryParse(partes[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out escala) && escala > 0)
                    {
                        DWrite.Cinza = partes.Length > 2 && partes[2] == "cinza";
                        try { Notch.Capturar(partes[0], escala); } finally { DWrite.Cinza = false; }
                        return;
                    }
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

            // Aberto pelo Windows: em segundo plano, a cápsula só aparece pelo ícone da bandeja ou pelo atalho
            // (sem nenhum dos dois não haveria como chamá-la, então abre como sempre)
            if (inicio && Config.Atual.Mostrar != Mostrar.Oculto && (bandeja.Visivel || AtalhoAtivo)) Notch.OcultoPeloAtalho = true;
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
            Atualizacao.NovaVersao += bandeja.AvisarAtualizacao;
            Atualizacao.Iniciar(SynchronizationContext.Current, Sair);
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

        // Clique na notificação de versão nova
        public void AbrirAtualizacoes()
        {
            AbrirConfig();
            config.MostrarAtualizacoes();
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
