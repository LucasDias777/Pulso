using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Pulso
{
    // Texto pelo DirectWrite com a receita do Chromium/Skia no Windows (o motor do Codenotch):
    // - máscara de cobertura crua do DirectWrite, glifo a glifo (IDWriteGlyphRunAnalysis, textura ClearType 3x1);
    // - modo de renderização pela tabela gasp da fonte, como o SkScalerContext_DW (NATURAL ou NATURAL_SYMMETRIC),
    //   com o grid-fit padrão (= ligado);
    // - posição horizontal em 1/4 de pixel (subpixel), linha de base no pixel inteiro;
    // - mistura do Skia: tabela de pré-mistura com gamma sRGB e contraste 1,0 (SK_GAMMA_SRGB, SK_GAMMA_CONTRAST=1.0
    //   do Chromium para Windows), máscara LCD 5-6-5 e blend_32.
    // ClearType RGB/BGR conforme o sistema (como o Chromium); sem ClearType, ou com SemFranja, cinza tirado da própria
    // máscara ClearType (média dos 3 subpixels), que é o que o Chromium faz quando não pode usar LCD.
    // Só usa APIs do Windows 7+: IDWriteFactory::CreateGlyphRunAnalysis e IDWriteGlyphRunAnalysis.
    static class DWrite
    {
        // true = sempre cinza (sem a franja colorida do ClearType), mesmo com ClearType ligado no sistema
        public static bool SemFranja = false;

        // Gama e contraste do caminho antigo (render target de bitmap), mantido só como reserva se o novo falhar
        const float Gamma = 2.2f;
        const float Contraste = 3.0f;

        // ---- interfaces COM (só os métodos usados; os demais ocupam a posição na vtable) ----
        [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteFactory
        {
            [PreserveSig] int GetSystemFontCollection(out IDWriteFontCollection colecao, [MarshalAs(UnmanagedType.Bool)] bool verificar);
            void _CreateCustomFontCollection(); void _RegisterFontCollectionLoader(); void _UnregisterFontCollectionLoader();
            void _CreateFontFileReference(); void _CreateCustomFontFileReference(); void _CreateFontFace();
            void _CreateRenderingParams(); void _CreateMonitorRenderingParams();
            [PreserveSig] int CreateCustomRenderingParams(float gamma, float contraste, float nivelClearType, int geometria, int modo, out IntPtr parametros);
            void _RegisterFontFileLoader(); void _UnregisterFontFileLoader(); void _CreateTextFormat(); void _CreateTypography();
            [PreserveSig] int GetGdiInterop(out IDWriteGdiInterop interop);
            void _CreateTextLayout(); void _CreateGdiCompatibleTextLayout(); void _CreateEllipsisTrimmingSign();
            void _CreateTextAnalyzer(); void _CreateNumberSubstitution();
            [PreserveSig] int CreateGlyphRunAnalysis(ref GLYPH_RUN run, float pixelsPorDip, ref MATRIX transformacao,
                int modo, int medicao, float origemX, float origemY, out IDWriteGlyphRunAnalysis analise); // 21º método (índice 20)
        }

        [ComImport, Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteFontCollection
        {
            [PreserveSig] uint GetFontFamilyCount();
            [PreserveSig] int GetFontFamily(uint indice, out IDWriteFontFamily familia);
            [PreserveSig] int FindFamilyName([MarshalAs(UnmanagedType.LPWStr)] string nome, out uint indice, [MarshalAs(UnmanagedType.Bool)] out bool existe);
        }

        [ComImport, Guid("da20d8ef-812a-4c43-9802-62ec4abd7add"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteFontFamily
        {
            void _GetFontCollection(); void _GetFontCount(); void _GetFont(); void _GetFamilyNames();
            [PreserveSig] int GetFirstMatchingFont(int peso, int largura, int estilo, out IDWriteFont fonte);
        }

        [ComImport, Guid("acd16696-8c14-4f5d-877e-fe3fc1d32737"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteFont
        {
            void _GetFontFamily(); void _GetWeight(); void _GetStretch(); void _GetStyle(); void _IsSymbolFont();
            void _GetFaceNames(); void _GetInformationalStrings(); void _GetSimulations(); void _GetMetrics(); void _HasCharacter();
            [PreserveSig] int CreateFontFace(out IDWriteFontFace face);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FONT_METRICS
        {
            public ushort designUnitsPerEm, ascent, descent; public short lineGap;
            public ushort capHeight, xHeight; public short underlinePosition; public ushort underlineThickness;
            public short strikethroughPosition; public ushort strikethroughThickness;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GLYPH_METRICS { public int leftSideBearing; public uint advanceWidth; public int rightSideBearing, topSideBearing; public uint advanceHeight; public int bottomSideBearing, verticalOriginY; }

        [ComImport, Guid("5f49804d-7024-4d43-bfa9-d25984f53849"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IDWriteFontFace
        {
            void _GetType(); void _GetFiles(); void _GetIndex(); void _GetSimulations(); void _IsSymbolFont();
            [PreserveSig] void GetMetrics(out FONT_METRICS m);
            void _GetGlyphCount();
            [PreserveSig] int GetDesignGlyphMetrics([In, MarshalAs(UnmanagedType.LPArray)] ushort[] indices, uint quantos, [Out, MarshalAs(UnmanagedType.LPArray)] GLYPH_METRICS[] metricas, [MarshalAs(UnmanagedType.Bool)] bool deLado);
            [PreserveSig] int GetGlyphIndices([In, MarshalAs(UnmanagedType.LPArray)] uint[] codigos, uint quantos, [Out, MarshalAs(UnmanagedType.LPArray)] ushort[] indices);
            [PreserveSig] int TryGetFontTable(uint etiqueta, out IntPtr dados, out uint tamanho, out IntPtr contexto, [MarshalAs(UnmanagedType.Bool)] out bool existe);
            [PreserveSig] void ReleaseFontTable(IntPtr contexto);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GLYPH_RUN
        {
            public IntPtr fontFace; public float fontEmSize; public uint glyphCount;
            public IntPtr glyphIndices, glyphAdvances, glyphOffsets; public int isSideways; public uint bidiLevel;
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MATRIX { public float m11, m12, m21, m22, dx, dy; }

        // Máscara de cobertura crua, sem gamma nem contraste (dwrite.h, Windows 7+)
        [ComImport, Guid("7d97dbf7-e085-42d4-81e3-6a883bded118"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteGlyphRunAnalysis
        {
            [PreserveSig] int GetAlphaTextureBounds(int tipo, out RECT limites); // 1 = DWRITE_TEXTURE_CLEARTYPE_3x1
            [PreserveSig] int CreateAlphaTexture(int tipo, ref RECT limites, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] alfa, uint tamanho);
        }

        [ComImport, Guid("1edd9491-9853-4299-898f-6432983b6f3a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteGdiInterop
        {
            void _CreateFontFromLOGFONT(); void _ConvertFontToLOGFONT(); void _ConvertFontFaceToLOGFONT(); void _CreateFontFaceFromHdc();
            [PreserveSig] int CreateBitmapRenderTarget(IntPtr hdc, uint largura, uint altura, out IDWriteBitmapRenderTarget alvo);
        }

        [ComImport, Guid("5e5a32a3-8dff-4773-9ff6-0696eab77267"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteBitmapRenderTarget
        {
            [PreserveSig] int DrawGlyphRun(float x, float yBase, int medicao, ref GLYPH_RUN run, IntPtr parametros, int corRef, out RECT caixa);
            [PreserveSig] IntPtr GetMemoryDC();
            [PreserveSig] float GetPixelsPerDip();
            [PreserveSig] int SetPixelsPerDip(float ppd);
        }

        [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(int tipo, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object fabrica);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int cor);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint acao, uint param, ref uint valor, uint ini);

        // ---- fontes ----
        public class Fonte
        {
            internal IDWriteFontFace Face;
            internal IntPtr PFace;
            internal float Upm, Ascent, Descent;
            internal readonly Dictionary<char, KeyValuePair<ushort, float>> Glifos = new Dictionary<char, KeyValuePair<ushort, float>>();
            internal float CelulaDigito; // maior avanço entre 0–9 (tabular-nums), em unidades de desenho
            internal readonly Dictionary<int, int> ModoPorPpem = new Dictionary<int, int>();
        }

        static IDWriteFactory fabrica;
        static IDWriteFontCollection colecao;
        static IDWriteGdiInterop interop;
        static IntPtr parametros;
        static bool falhou;
        static readonly Dictionary<string, Fonte> fontes = new Dictionary<string, Fonte>();

        public static bool Disponivel { get { return Iniciar(); } }

        static bool Iniciar()
        {
            if (fabrica != null) return true;
            if (falhou) return false;
            try
            {
                var iid = typeof(IDWriteFactory).GUID;
                object o;
                Marshal.ThrowExceptionForHR(DWriteCreateFactory(0, ref iid, out o)); // DWRITE_FACTORY_TYPE_SHARED
                fabrica = (IDWriteFactory)o;
                Marshal.ThrowExceptionForHR(fabrica.GetSystemFontCollection(out colecao, false));
                Marshal.ThrowExceptionForHR(fabrica.GetGdiInterop(out interop));
                Marshal.ThrowExceptionForHR(fabrica.CreateCustomRenderingParams(Gamma, Contraste, 0f, 1, 5, out parametros));
                return true;
            }
            catch (Exception e) { Log.Erro("DirectWrite indisponível; texto pelo GDI", e); falhou = true; return false; }
        }

        // Família e peso como no CSS (ex.: "Segoe UI Variable Text", 600); cai para "Segoe UI" se não houver
        public static Fonte Obter(string familia, int peso)
        {
            if (!Iniciar()) return null;
            string chave = familia + "|" + peso;
            Fonte f;
            if (fontes.TryGetValue(chave, out f)) return f;
            try
            {
                uint i; bool existe;
                Marshal.ThrowExceptionForHR(colecao.FindFamilyName(familia, out i, out existe));
                if (!existe) Marshal.ThrowExceptionForHR(colecao.FindFamilyName("Segoe UI", out i, out existe));
                IDWriteFontFamily fam; IDWriteFont fonte; IDWriteFontFace face;
                Marshal.ThrowExceptionForHR(colecao.GetFontFamily(i, out fam));
                Marshal.ThrowExceptionForHR(fam.GetFirstMatchingFont(peso, 5, 0, out fonte)); // largura normal, estilo normal
                Marshal.ThrowExceptionForHR(fonte.CreateFontFace(out face));
                Marshal.ReleaseComObject(fonte); Marshal.ReleaseComObject(fam);
                FONT_METRICS m;
                face.GetMetrics(out m);
                f = new Fonte { Face = face, PFace = Marshal.GetComInterfaceForObject(face, typeof(IDWriteFontFace)), Upm = m.designUnitsPerEm, Ascent = m.ascent, Descent = m.descent };
                for (char d = '0'; d <= '9'; d++) f.CelulaDigito = Math.Max(f.CelulaDigito, Glifo(f, d).Value);
                fontes[chave] = f;
                return f;
            }
            catch (Exception e) { Log.Erro("fonte " + familia, e); return null; }
        }

        static KeyValuePair<ushort, float> Glifo(Fonte f, char c)
        {
            KeyValuePair<ushort, float> g;
            if (f.Glifos.TryGetValue(c, out g)) return g;
            var idx = new ushort[1];
            f.Face.GetGlyphIndices(new uint[] { c }, 1, idx);
            var met = new GLYPH_METRICS[1];
            f.Face.GetDesignGlyphMetrics(idx, 1, met, false);
            g = new KeyValuePair<ushort, float>(idx[0], met[0].advanceWidth);
            f.Glifos[c] = g;
            return g;
        }

        public static float Largura(Fonte f, float em, string texto, bool tabular = false)
        {
            float soma = 0;
            foreach (char c in texto ?? "") soma += tabular && char.IsDigit(c) ? f.CelulaDigito : Glifo(f, c).Value;
            return soma * em / f.Upm;
        }

        // Sem fallback de fonte: o caractere que a fonte não tem sairia como caixinha (quem chama vai pelo GDI)
        public static bool Cobre(Fonte f, string texto)
        {
            foreach (char c in texto ?? "") if (c != '\n' && c != '\r' && Glifo(f, c).Key == 0) return false;
            return true;
        }

        // Quebra em linhas por palavra para caber em 'largura'
        public static List<string> Quebrar(Fonte f, float em, string texto, float largura)
        {
            var linhas = new List<string>();
            foreach (var paragrafo in (texto ?? "").Split('\n'))
            {
                string atual = "";
                foreach (var palavra in paragrafo.Split(' '))
                {
                    string tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
                    if (atual.Length > 0 && Largura(f, em, tentativa) > largura) { linhas.Add(atual); atual = palavra; }
                    else atual = tentativa;
                }
                linhas.Add(atual);
            }
            return linhas;
        }

        // ---- modo de renderização pela tabela gasp (regra do SkScalerContext_DW) ----
        // gasp versão 1 com SYMMETRIC_SMOOTHING (0x8) => NATURAL_SYMMETRIC (5); sem o bit => NATURAL (4).
        // Sem gasp versão 1: acima de 20 px => NATURAL_SYMMETRIC; abaixo => NATURAL (fonte com hinting).
        static int ModoGasp(Fonte f, float em)
        {
            int ppem = (int)Math.Round(Math.Round(em * 64f) / 64f, MidpointRounding.AwayFromZero);
            int modo;
            if (f.ModoPorPpem.TryGetValue(ppem, out modo)) return modo;
            modo = em > 20 ? 5 : 4;
            IntPtr dados, contexto; uint tamanho; bool existe;
            const uint gasp = 'g' | ('a' << 8) | ('s' << 16) | ('p' << 24);
            if (f.Face.TryGetFontTable(gasp, out dados, out tamanho, out contexto, out existe) >= 0 && existe)
            {
                var t = new byte[tamanho];
                Marshal.Copy(dados, t, 0, (int)tamanho);
                f.Face.ReleaseFontTable(contexto);
                int versao = (t[0] << 8) | t[1], faixas = (t[2] << 8) | t[3];
                for (int i = 0; i < faixas && 8 + 4 * i <= t.Length; i++)
                {
                    int max = (t[4 + 4 * i] << 8) | t[5 + 4 * i], flags = (t[6 + 4 * i] << 8) | t[7 + 4 * i];
                    if (ppem <= max) { if (versao >= 1) modo = (flags & 0x8) != 0 ? 5 : 4; break; }
                }
            }
            f.ModoPorPpem[ppem] = modo;
            return modo;
        }

        // ---- ClearType do sistema, lido como o Chromium (font_render_params_win.cc): 0 = cinza, 1 = RGB, 2 = BGR ----
        // Liga/desliga e tipo pelo SystemParametersInfo; a ordem dos subpixels vem primeiro do ajuste do ClearType
        // (HKCU\SOFTWARE\Microsoft\Avalon.Graphics\DISPLAYn\PixelStructure) e, sem ele, da orientação do sistema.
        static int subpixel = -1;
        static int subpixelLidoEm;
        static int Subpixel()
        {
            if (subpixel >= 0 && unchecked(Environment.TickCount - subpixelLidoEm) < 2000) return subpixel;
            subpixelLidoEm = Environment.TickCount;
            subpixel = LerSubpixel();
            return subpixel;
        }
        static int LerSubpixel()
        {
            uint ligado = 0, tipo = 0, ordem = 0;
            if (!SystemParametersInfo(0x004A /* SPI_GETFONTSMOOTHING */, 0, ref ligado, 0) || ligado == 0) return 0;
            if (!SystemParametersInfo(0x200A /* SPI_GETFONTSMOOTHINGTYPE */, 0, ref tipo, 0) || tipo != 2 /* FE_FONTSMOOTHINGCLEARTYPE */) return 0;
            try
            {
                string tela = System.IO.Path.GetFileName(System.Windows.Forms.Screen.PrimaryScreen.DeviceName); // "\\.\DISPLAY1" -> "DISPLAY1"
                using (var chave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Avalon.Graphics\" + tela))
                {
                    object v = chave == null ? null : chave.GetValue("PixelStructure");
                    if (v is int) return (int)v == 1 ? 1 : (int)v == 2 ? 2 : 0;
                }
            }
            catch (Exception) { }
            if (!SystemParametersInfo(0x2012 /* SPI_GETFONTSMOOTHINGORIENTATION */, 0, ref ordem, 0)) return 0;
            return ordem == 0 ? 2 /* FE_FONTSMOOTHINGORIENTATIONBGR */ : 1 /* RGB */;
        }

        // ---- tabela de pré-mistura do Skia (SkTMaskGamma_build_correcting_lut), gamma sRGB e contraste 1,0 ----
        static readonly byte[][] tabelas = new byte[8][];
        static byte[] Tabela(int canal)
        {
            int i3 = canal >> 5; // 3 bits de luminância, como o SkMaskGamma
            if (tabelas[i3] != null) return tabelas[i3];
            int srcI = (i3 << 5) | (i3 << 2) | (i3 >> 1); // sk_t_scale255<3>
            const float contraste = 1.0f;
            float src = srcI / 255f, dst = 1f - src;
            float linSrc = ParaLinear(src), linDst = ParaLinear(dst);
            float c = contraste * linDst; // o contraste some à medida que a cor vai para o branco
            var t = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                float a = i / 255f;
                float sa = a + ((1f - a) * c * a);
                if (Math.Abs(src - dst) < 1f / 256f) { t[i] = (byte)Math.Round(255f * sa, MidpointRounding.AwayFromZero); continue; }
                float saida = DeLinear(linSrc * sa + (1f - sa) * linDst);
                t[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(255f * (saida - dst) / (src - dst), MidpointRounding.AwayFromZero)));
            }
            tabelas[i3] = t;
            return t;
        }
        static float ParaLinear(float v) { return v <= 0.04045f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4f); }
        static float DeLinear(float v) { return v <= 0.0031308f ? v * 12.92f : 1.055f * (float)Math.Pow(v, 1 / 2.4f) - 0.055f; }

        // ---- máscara ClearType de um glifo numa fase de 1/4 px, com cache ----
        class Mascara { public int Esq, Topo, Larg, Alt; public byte[] Alfa; }
        static readonly Dictionary<string, Mascara> mascaras = new Dictionary<string, Mascara>();
        static readonly ushort[] umIndice = new ushort[1];
        static readonly float[] umAvanco = new float[1], umDesloc = new float[2];

        static Mascara ObterMascara(Fonte f, ushort glifo, float em, float fase, int modo)
        {
            string chave = f.PFace.ToString() + "|" + glifo + "|" + em.ToString("R") + "|" + fase.ToString("R") + "|" + modo;
            Mascara m;
            if (mascaras.TryGetValue(chave, out m)) return m;
            if (mascaras.Count > 4096) mascaras.Clear();
            umIndice[0] = glifo;
            GCHandle pI = GCHandle.Alloc(umIndice, GCHandleType.Pinned), pA = GCHandle.Alloc(umAvanco, GCHandleType.Pinned), pD = GCHandle.Alloc(umDesloc, GCHandleType.Pinned);
            IDWriteGlyphRunAnalysis analise = null;
            try
            {
                var run = new GLYPH_RUN { fontFace = f.PFace, fontEmSize = em, glyphCount = 1, glyphIndices = pI.AddrOfPinnedObject(), glyphAdvances = pA.AddrOfPinnedObject(), glyphOffsets = pD.AddrOfPinnedObject() };
                var transformacao = new MATRIX { m11 = 1, m22 = 1, dx = fase };
                // pixelsPorDip 1 (em já em pixels do aparelho), medição NATURAL, origem (0, 0)
                Marshal.ThrowExceptionForHR(fabrica.CreateGlyphRunAnalysis(ref run, 1f, ref transformacao, modo, 0, 0f, 0f, out analise));
                RECT r;
                Marshal.ThrowExceptionForHR(analise.GetAlphaTextureBounds(1, out r));
                if (r.Left < r.Right && r.Top < r.Bottom)
                {
                    m = new Mascara { Esq = r.Left, Topo = r.Top, Larg = r.Right - r.Left, Alt = r.Bottom - r.Top };
                    m.Alfa = new byte[m.Larg * m.Alt * 3];
                    Marshal.ThrowExceptionForHR(analise.CreateAlphaTexture(1, ref r, m.Alfa, (uint)m.Alfa.Length));
                }
            }
            finally
            {
                pI.Free(); pA.Free(); pD.Free();
                if (analise != null) Marshal.ReleaseComObject(analise);
            }
            mascaras[chave] = m; // null = glifo sem tinta (espaço)
            return m;
        }

        // Uma linha num bitmap opaco (cor de fundo real), com a linha de base centrada numa caixa de 'alturaLinha'
        public static Bitmap Desenhar(Fonte f, float em, string texto, Color cor, Color fundo, float alturaLinha, bool tabular = false)
        {
            if (f == null || string.IsNullOrEmpty(texto)) return null;
            try { return DesenharComoChromium(f, em, texto, cor, fundo, alturaLinha, tabular); }
            catch (Exception e) { Log.Erro("DirectWrite (máscara de glifo); caminho antigo", e); }
            return DesenharRenderTarget(f, em, texto, cor, fundo, alturaLinha, tabular);
        }

        static Bitmap DesenharComoChromium(Fonte f, float em, string texto, Color cor, Color fundo, float alturaLinha, bool tabular)
        {
            float k = em / f.Upm;
            var idx = new ushort[texto.Length];
            var avancos = new float[texto.Length];
            var deslocs = new float[texto.Length];
            float total = 0;
            for (int i = 0; i < texto.Length; i++)
            {
                var g = Glifo(f, texto[i]);
                idx[i] = g.Key;
                float a = g.Value * k;
                if (tabular && char.IsDigit(texto[i])) { deslocs[i] = (f.CelulaDigito * k - a) / 2; a = f.CelulaDigito * k; }
                avancos[i] = a;
                total += a;
            }
            int w = (int)Math.Ceiling(total) + 4, h = Math.Max(1, (int)Math.Ceiling(alturaLinha));
            // Meia entrelinha em cima e embaixo, como o CSS faz com line-height
            int baseY = (int)Math.Round((alturaLinha - (f.Ascent + f.Descent) * k) / 2 + f.Ascent * k);
            int modo = ModoGasp(f, em);
            int subpixel = SemFranja ? 0 : Subpixel();

            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var dados = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int passo = dados.Stride;
            var px = new byte[passo * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) { int o = y * passo + x * 3; px[o] = fundo.B; px[o + 1] = fundo.G; px[o + 2] = fundo.R; }

            byte[] tR = Tabela(cor.R), tG = Tabela(cor.G), tB = Tabela(cor.B);
            byte[] tA = Tabela((cor.R * 54 + cor.G * 183 + cor.B * 19) >> 8); // SkComputeLuminance
            double caneta = 1; // mesma origem horizontal do caminho antigo
            for (int i = 0; i < idx.Length; i++)
            {
                double gx = caneta + deslocs[i];
                caneta += avancos[i];
                double fx = Math.Floor((gx + 0.125) * 4) / 4; // posição em 1/4 de pixel, como o Skia
                int ix = (int)Math.Floor(fx);
                var m = ObterMascara(f, idx[i], em, (float)(fx - ix), modo);
                if (m == null) continue;
                for (int yy = 0; yy < m.Alt; yy++)
                {
                    int Y = baseY + m.Topo + yy;
                    if (Y < 0 || Y >= h) continue;
                    for (int xx = 0; xx < m.Larg; xx++)
                    {
                        int X = ix + m.Esq + xx;
                        if (X < 0 || X >= w) continue;
                        int a = (yy * m.Larg + xx) * 3, o = Y * passo + X * 3;
                        int s0 = m.Alfa[a], s1 = m.Alfa[a + 1], s2 = m.Alfa[a + 2];
                        if (s0 == 0 && s1 == 0 && s2 == 0) continue;
                        if (subpixel == 0)
                        {
                            int sc = tA[(s0 + s1 + s2) / 3] + 1; // A8 a partir da máscara LCD, SkAlpha255To256
                            px[o] = (byte)(px[o] + (((cor.B - px[o]) * sc) >> 8));
                            px[o + 1] = (byte)(px[o + 1] + (((cor.G - px[o + 1]) * sc) >> 8));
                            px[o + 2] = (byte)(px[o + 2] + (((cor.R - px[o + 2]) * sc) >> 8));
                        }
                        else
                        {
                            int cr = tR[subpixel == 1 ? s0 : s2] >> 3, cg = tG[s1] >> 3, cb = tB[subpixel == 1 ? s2 : s0] >> 3; // LCD 5-6-5
                            cr += cr >> 4; cg += cg >> 4; cb += cb >> 4; // 0..31 -> 0..32
                            px[o] = (byte)(px[o] + (((cor.B - px[o]) * cb) >> 5));
                            px[o + 1] = (byte)(px[o + 1] + (((cor.G - px[o + 1]) * cg) >> 5));
                            px[o + 2] = (byte)(px[o + 2] + (((cor.R - px[o + 2]) * cr) >> 5));
                        }
                    }
                }
            }
            Marshal.Copy(px, 0, dados.Scan0, px.Length);
            bmp.UnlockBits(dados);
            return bmp;
        }

        // Caminho antigo (render target de bitmap do GDI interop, cinza simétrico): só reserva
        static Bitmap DesenharRenderTarget(Fonte f, float em, string texto, Color cor, Color fundo, float alturaLinha, bool tabular)
        {
            IDWriteBitmapRenderTarget alvo = null;
            var presos = new List<GCHandle>();
            try
            {
                float k = em / f.Upm;
                var idx = new ushort[texto.Length];
                var avancos = new float[texto.Length];
                var deslocs = new float[texto.Length * 2];
                float total = 0;
                for (int i = 0; i < texto.Length; i++)
                {
                    var g = Glifo(f, texto[i]);
                    idx[i] = g.Key;
                    float a = g.Value * k;
                    if (tabular && char.IsDigit(texto[i])) { deslocs[i * 2] = (f.CelulaDigito * k - a) / 2; a = f.CelulaDigito * k; }
                    avancos[i] = a;
                    total += a;
                }
                int w = (int)Math.Ceiling(total) + 4, h = Math.Max(1, (int)Math.Ceiling(alturaLinha));
                Marshal.ThrowExceptionForHR(interop.CreateBitmapRenderTarget(IntPtr.Zero, (uint)w, (uint)h, out alvo));
                alvo.SetPixelsPerDip(1);
                IntPtr dc = alvo.GetMemoryDC();
                var r = new RECT { Right = w, Bottom = h };
                IntPtr pincel = CreateSolidBrush(ColorTranslator.ToWin32(fundo));
                FillRect(dc, ref r, pincel);
                DeleteObject(pincel);

                GCHandle pIdx = GCHandle.Alloc(idx, GCHandleType.Pinned), pAv = GCHandle.Alloc(avancos, GCHandleType.Pinned), pDs = GCHandle.Alloc(deslocs, GCHandleType.Pinned);
                presos.Add(pIdx); presos.Add(pAv); presos.Add(pDs);
                var run = new GLYPH_RUN
                {
                    fontFace = f.PFace, fontEmSize = em, glyphCount = (uint)idx.Length,
                    glyphIndices = pIdx.AddrOfPinnedObject(), glyphAdvances = pAv.AddrOfPinnedObject(), glyphOffsets = pDs.AddrOfPinnedObject(),
                };
                float baseY = (alturaLinha - (f.Ascent + f.Descent) * k) / 2 + f.Ascent * k;
                RECT caixa;
                Marshal.ThrowExceptionForHR(alvo.DrawGlyphRun(1, (float)Math.Round(baseY), 0, ref run, parametros, ColorTranslator.ToWin32(cor), out caixa));

                var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var gr = Graphics.FromImage(bmp))
                {
                    IntPtr destino = gr.GetHdc();
                    BitBlt(destino, 0, 0, w, h, dc, 0, 0, 0x00CC0020); // SRCCOPY
                    gr.ReleaseHdc(destino);
                }
                return bmp;
            }
            catch (Exception e) { Log.Erro("DirectWrite", e); return null; }
            finally
            {
                foreach (var p in presos) if (p.IsAllocated) p.Free();
                if (alvo != null) Marshal.ReleaseComObject(alvo);
            }
        }

        // Números do notch (compatível com o uso anterior)
        public static Bitmap Desenhar(string texto, string familia, int peso, float emPx, Color cor, Color fundo)
        {
            var f = Obter(familia, peso);
            if (f == null) return null;
            return Desenhar(f, emPx, texto, cor, fundo, (float)Math.Ceiling((f.Ascent + f.Descent) * emPx / f.Upm) + 2, true);
        }
    }
}
