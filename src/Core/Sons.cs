using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Pulso
{
    enum Som { Renovacao, Limite, Concluida, Esperando }

    // Sons do próprio Windows. Renovação = o bipe "Asterisco" do sistema;
    // sessão concluída curto; esperando você com dois toques, para soar diferente sem virar alarme.
    static class Sons
    {
        static readonly Dictionary<Som, SoundPlayer> carregados = new Dictionary<Som, SoundPlayer>();

        static string Arquivo(Som s)
        {
            string pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
            switch (s)
            {
                case Som.Limite: return Path.Combine(pasta, "Windows Notify System Generic.wav");
                case Som.Concluida: return Path.Combine(pasta, "Windows Notify Messaging.wav");
                case Som.Esperando: return Path.Combine(pasta, "Windows Message Nudge.wav");
                default: return null;
            }
        }

        public static void Tocar(Som s)
        {
            if (!Config.Atual.SomAvisos) return;
            try
            {
                if (s == Som.Renovacao) { SystemSounds.Asterisk.Play(); return; }
                SoundPlayer p;
                if (!carregados.TryGetValue(s, out p))
                {
                    string arq = Arquivo(s);
                    if (arq == null || !File.Exists(arq)) { SystemSounds.Asterisk.Play(); return; }
                    p = new SoundPlayer(arq);
                    p.LoadAsync();
                    carregados[s] = p;
                }
                p.Play();
            }
            catch (Exception e) { Log.Erro("som", e); }
        }
    }
}
