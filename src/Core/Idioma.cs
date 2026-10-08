using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pulso
{
    // Idioma da interface (Config.Idioma; padrão português). O texto fica em português no código e "texto".T()
    // devolve a tradução do idioma escolhido, ou o próprio texto se não houver. Com partes variáveis, o texto vira
    // um formato: "renova às {0}".T(hora). As traduções ficam em tabelas por área (Idioma.*.cs): { pt, en, es }.
    // Traduzir na hora de mostrar (não guardar o texto traduzido), para a troca de idioma valer na hora.
    static partial class Idioma
    {
        public static readonly string[] Codigos = { "pt", "en", "es" };
        public static readonly string[] Nomes = { "Português", "English", "Español" };

        static readonly CultureInfo[] culturas = { new CultureInfo("pt-BR"), new CultureInfo("en-US"), new CultureInfo("es-ES") };
        static readonly Dictionary<string, string[]> traducoes = new Dictionary<string, string[]>();

        static Idioma()
        {
            foreach (var t in new[] { tConfiguracoes, tInterface, tFontes, tSistema })
                for (int i = 0; i < t.GetLength(0); i++) traducoes[t[i, 0]] = new[] { t[i, 1], t[i, 2] };
        }

        // 0 = português, 1 = inglês, 2 = espanhol
        public static int Indice
        {
            get
            {
                var c = Config.Atual;
                int i = c == null ? 0 : Array.IndexOf(Codigos, c.Idioma);
                return i < 0 ? 0 : i;
            }
        }

        // Datas, dias da semana e separador decimal do idioma
        public static CultureInfo Cultura { get { return culturas[Indice]; } }

        public static string T(this string pt)
        {
            int i = Indice;
            string[] t;
            if (i == 0 || pt == null || !traducoes.TryGetValue(pt, out t) || string.IsNullOrEmpty(t[i - 1])) return pt;
            return t[i - 1];
        }

        public static string T(this string pt, params object[] args)
        {
            return string.Format(Cultura, T(pt), args);
        }
    }
}
