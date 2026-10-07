using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Pulso
{
    // Texto pelo DirectWrite — o mesmo motor de texto dos navegadores. O GDI encaixa os traços no
    // pixel (letra mais fina e espaçada); o DirectWrite em modo natural posiciona em subpixel, como o Chromium.
    // Interop mínimo: fonte pela coleção do sistema (família + peso, como o CSS), glyph run montado à mão
    // (índices e avanços da própria fonte) e desenhado num render target de bitmap do GDI interop.
    static class DWrite
    {
        // ---- interfaces COM (só os métodos usados; os demais ocupam a posição na vtable) ----
        [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDWriteFactory
        {
            [PreserveSig] int GetSystemFontCollection(out IDWriteFontCollection colecao, [MarshalAs(UnmanagedType.Bool)] bool verificar);
            void _CreateCustomFontCollection(); void _RegisterFontCollectionLoader(); void _UnregisterFontCollectionLoader();
            void _CreateFontFileReference(); void _CreateCustomFontFileReference(); void _CreateFontFace();
            [PreserveSig] int CreateRenderingParams(out IntPtr parametros);
            void _CreateMonitorRenderingParams(); void _CreateCustomRenderingParams(); void _RegisterFontFileLoader();
            void _UnregisterFontFileLoader(); void _CreateTextFormat(); void _CreateTypography();
            [PreserveSig] int GetGdiInterop(out IDWriteGdiInterop interop);
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
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GLYPH_RUN
        {
            public IntPtr fontFace; public float fontEmSize; public uint glyphCount;
            public IntPtr glyphIndices, glyphAdvances, glyphOffsets; public int isSideways; public uint bidiLevel;
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

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

        // ---- fontes ----
        public class Fonte
        {
            internal IDWriteFontFace Face;
            internal IntPtr PFace;
            internal float Upm, Ascent, Descent;
            internal readonly Dictionary<char, KeyValuePair<ushort, float>> Glifos = new Dictionary<char, KeyValuePair<ushort, float>>();
            internal float CelulaDigito; // maior avanço entre 0–9 (tabular-nums), em unidades de desenho
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
                Marshal.ThrowExceptionForHR(fabrica.CreateRenderingParams(out parametros)); // ClearType do sistema, modo natural
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

        // Uma linha num bitmap opaco (cor de fundo real), com a linha de base centrada numa caixa de 'alturaLinha'
        public static Bitmap Desenhar(Fonte f, float em, string texto, Color cor, Color fundo, float alturaLinha, bool tabular = false)
        {
            if (f == null || string.IsNullOrEmpty(texto)) return null;
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
                // Meia entrelinha em cima e embaixo, como o CSS faz com line-height
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
