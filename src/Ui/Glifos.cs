using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Pulso
{
    // Marcas dos provedores (viewBox 24×24). Origem: @lobehub/icons-static-svg 1.95.0 (MIT, © LobeHub),
    // As marcas pertencem a cada empresa; aqui só identificam o produto.
    static class Glifos
    {
        const string Claude = "M4.709 15.955l4.72-2.647.08-.23-.08-.128H9.2l-.79-.048-2.698-.073-2.339-.097-2.266-.122-.571-.121L0 11.784l.055-.352.48-.321.686.06 1.52.103 2.278.158 1.652.097 2.449.255h.389l.055-.157-.134-.098-.103-.097-2.358-1.596-2.552-1.688-1.336-.972-.724-.491-.364-.462-.158-1.008.656-.722.881.06.225.061.893.686 1.908 1.476 2.491 1.833.365.304.145-.103.019-.073-.164-.274-1.355-2.446-1.446-2.49-.644-1.032-.17-.619a2.97 2.97 0 01-.104-.729L6.283.134 6.696 0l.996.134.42.364.62 1.414 1.002 2.229 1.555 3.03.456.898.243.832.091.255h.158V9.01l.128-1.706.237-2.095.23-2.695.08-.76.376-.91.747-.492.584.28.48.685-.067.444-.286 1.851-.559 2.903-.364 1.942h.212l.243-.242.985-1.306 1.652-2.064.73-.82.85-.904.547-.431h1.033l.76 1.129-.34 1.166-1.064 1.347-.881 1.142-1.264 1.7-.79 1.36.073.11.188-.02 2.856-.606 1.543-.28 1.841-.315.833.388.091.395-.328.807-1.969.486-2.309.462-3.439.813-.042.03.049.061 1.549.146.662.036h1.622l3.02.225.79.522.474.638-.079.485-1.215.62-1.64-.389-3.829-.91-1.312-.329h-.182v.11l1.093 1.068 2.006 1.81 2.509 2.33.127.578-.322.455-.34-.049-2.205-1.657-.851-.747-1.926-1.62h-.128v.17l.444.649 2.345 3.521.122 1.08-.17.353-.608.213-.668-.122-1.374-1.925-1.415-2.167-1.143-1.943-.14.08-.674 7.254-.316.37-.729.28-.607-.461-.322-.747.322-1.476.389-1.924.315-1.53.286-1.9.17-.632-.012-.042-.14.018-1.434 1.967-2.18 2.945-1.726 1.845-.414.164-.717-.37.067-.662.401-.589 2.388-3.036 1.44-1.882.93-1.086-.006-.158h-.055L4.132 18.56l-1.13.146-.487-.456.061-.746.231-.243 1.908-1.312-.006.006z";
        const string OpenAI = "M9.205 8.658v-2.26c0-.19.072-.333.238-.428l4.543-2.616c.619-.357 1.356-.523 2.117-.523 2.854 0 4.662 2.212 4.662 4.566 0 .167 0 .357-.024.547l-4.71-2.759a.797.797 0 00-.856 0l-5.97 3.473zm10.609 8.8V12.06c0-.333-.143-.57-.429-.737l-5.97-3.473 1.95-1.118a.433.433 0 01.476 0l4.543 2.617c1.309.76 2.189 2.378 2.189 3.948 0 1.808-1.07 3.473-2.76 4.163zM7.802 12.703l-1.950-1.142c-.167-.095-.239-.238-.239-.428V5.899c0-2.545 1.950-4.472 4.591-4.472 1 0 1.927.333 2.712.928L8.23 5.067c-.285.166-.428.404-.428.737v6.898zM12 15.128l-2.795-1.57v-3.33L12 8.658l2.795 1.57v3.33L12 15.128zm1.796 7.23c-1 0-1.927-.332-2.712-.927l4.686-2.712c.285-.166.428-.404.428-.737v-6.898l1.974 1.142c.167.095.238.238.238.428v5.233c0 2.545-1.974 4.472-4.614 4.472zm-5.637-5.303l-4.544-2.617c-1.308-.761-2.188-2.378-2.188-3.948A4.482 4.482 0 014.21 6.327v5.423c0 .333.143.571.428.738l5.947 3.449-1.950 1.118a.432.432 0 01-.476 0zm-.262 3.9c-2.688 0-4.662-2.021-4.662-4.519 0-.19.024-.38.047-.57l4.686 2.71c.286.167.571.167.856 0l5.970-3.448v2.26c0 .19-.07.333-.237.428l-4.543 2.616c-.619.357-1.356.523-2.117.523zm5.899 2.83a5.947 5.947 0 005.827-4.756C22.287 18.339 24 15.840 24 13.296c0-1.665-.713-3.282-1.998-4.448.119-.5.19-.999.19-1.498 0-3.401-2.759-5.947-5.946-5.947-.642 0-1.260.095-1.880.31A5.962 5.962 0 0010.205 0a5.947 5.947 0 00-5.827 4.757C1.713 5.447 0 7.945 0 10.490c0 1.666.713 3.283 1.998 4.448-.119.5-.19 1-.19 1.499 0 3.401 2.759 5.946 5.946 5.946.642 0 1.260-.095 1.880-.309a5.960 5.960 0 004.162 1.713z";

        // Os demais: Copilot, Cursor, Grok, Antigravity (gemini.svg = marca do Antigravity) e OpenCode
        static readonly Dictionary<string, string> Outros = new Dictionary<string, string>
        {
            { "copilot", "M19.245 5.364c1.322 1.36 1.877 3.216 2.11 5.817.622 0 1.2.135 1.592.654l.73.964c.21.278.323.61.323.955v2.62c0 .339-.173.669-.453.868C20.239 19.602 16.157 21.5 12 21.5c-4.6 0-9.205-2.583-11.547-4.258-.28-.2-.452-.53-.453-.868v-2.62c0-.345.113-.679.321-.956l.73-.963c.392-.517.974-.654 1.593-.654l.029-.297c.25-2.446.81-4.213 2.082-5.52 2.461-2.54 5.71-2.851 7.146-2.864h.198c1.436.013 4.685.323 7.146 2.864zm-7.244 4.328c-.284 0-.613.016-.962.05-.123.447-.305.85-.57 1.108-1.05 1.023-2.316 1.18-2.994 1.18-.638 0-1.306-.13-1.851-.464-.516.165-1.012.403-1.044.996a65.882 65.882 0 00-.063 2.884l-.002.48c-.002.563-.005 1.126-.013 1.69.002.326.204.63.51.765 2.482 1.102 4.83 1.657 6.99 1.657 2.156 0 4.504-.555 6.985-1.657a.854.854 0 00.51-.766c.03-1.682.006-3.372-.076-5.053-.031-.596-.528-.83-1.046-.996-.546.333-1.212.464-1.85.464-.677 0-1.942-.157-2.993-1.18-.266-.258-.447-.661-.57-1.108-.32-.032-.64-.049-.96-.05zm-2.525 4.013c.539 0 .976.426.976.95v1.753c0 .525-.437.95-.976.95a.964.964 0 01-.976-.95v-1.752c0-.525.437-.951.976-.951zm5 0c.539 0 .976.426.976.95v1.753c0 .525-.437.95-.976.95a.964.964 0 01-.976-.95v-1.752c0-.525.437-.951.976-.951zM7.635 5.087c-1.05.102-1.935.438-2.385.906-.975 1.037-.765 3.668-.21 4.224.405.394 1.17.657 1.995.657h.09c.649-.013 1.785-.176 2.73-1.11.435-.41.705-1.433.675-2.47-.03-.834-.27-1.52-.63-1.813-.39-.336-1.275-.482-2.265-.394zm6.465.394c-.36.292-.6.98-.63 1.813-.03 1.037.24 2.06.675 2.47.968.957 2.136 1.104 2.776 1.11h.044c.825 0 1.59-.263 1.995-.657.555-.556.765-3.187-.21-4.224-.45-.468-1.335-.804-2.385-.906-.99-.088-1.875.058-2.265.394zM12 7.615c-.24 0-.525.015-.84.044.03.16.045.336.06.526l-.001.159a2.94 2.94 0 01-.014.25c.225-.022.425-.027.612-.028h.366c.187 0 .387.006.612.028-.015-.146-.015-.277-.015-.409.015-.19.03-.365.06-.526a9.29 9.29 0 00-.84-.044z" },
            { "cursor", "M22.106 5.68L12.5.135a.998.998 0 00-.998 0L1.893 5.68a.84.84 0 00-.419.726v11.186c0 .3.16.577.42.727l9.607 5.547a.999.999 0 00.998 0l9.608-5.547a.84.84 0 00.42-.727V6.407a.84.84 0 00-.42-.726zm-.603 1.176L12.228 22.92c-.063.108-.228.064-.228-.061V12.34a.59.59 0 00-.295-.51l-9.11-5.26c-.107-.062-.063-.228.062-.228h18.55c.264 0 .428.286.296.514z" },
            { "grok", "M9.27 15.29l7.978-5.897c.391-.29.95-.177 1.137.272.98 2.369.542 5.215-1.41 7.169-1.951 1.954-4.667 2.382-7.149 1.406l-2.711 1.257c3.889 2.661 8.611 2.003 11.562-.953 2.341-2.344 3.066-5.539 2.388-8.42l.006.007c-.983-4.232.242-5.924 2.75-9.383.06-.082.12-.164.179-.248l-3.301 3.305v-.01L9.267 15.292M7.623 16.723c-2.792-2.67-2.31-6.801.071-9.184 1.761-1.763 4.647-2.483 7.166-1.425l2.705-1.25a7.808 7.808 0 00-1.829-1A8.975 8.975 0 005.984 5.83c-2.533 2.536-3.33 6.436-1.962 9.764 1.022 2.487-.653 4.246-2.34 6.022-.599.63-1.199 1.259-1.682 1.925l7.62-6.815" },
            { "gemini", "M21.751 22.607c1.34 1.005 3.35.335 1.508-1.508C17.73 15.74 18.904 1 12.037 1 5.17 1 6.342 15.74.815 21.1c-2.01 2.009.167 2.511 1.507 1.506 5.192-3.517 4.857-9.714 9.715-9.714 4.857 0 4.522 6.197 9.714 9.715z" },
            { "opencode", "M16 6H8v12h8V6zm4 16H4V2h16v20z" },
        };

        static readonly Dictionary<string, GraphicsPath> cache = new Dictionary<string, GraphicsPath>();

        static GraphicsPath Base(string provedor)
        {
            if (provedor == "claude") return Svg.Interpretar(Claude);
            if (provedor == "codex") return Svg.Interpretar(OpenAI);
            string d;
            if (Outros.TryGetValue(provedor, out d)) return Svg.Interpretar(d);
            // Sem marca (z.ai): letra
            var p = new GraphicsPath();
            using (var f = new FontFamily("Segoe UI"))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                p.AddString(provedor == "glm" ? "Z" : provedor.Substring(0, 1).ToUpperInvariant(), f, (int)FontStyle.Bold, 20, new RectangleF(0, 0, 24, 24), sf);
            return p;
        }

        // Caminho já escalado para caber num quadrado de 'tamanho' px centrado em (cx, cy)
        public static GraphicsPath Caminho(string provedor, float cx, float cy, float tamanho)
        {
            GraphicsPath basePath;
            lock (cache)
            {
                if (!cache.TryGetValue(provedor, out basePath))
                {
                    basePath = Base(provedor);
                    basePath.FillMode = FillMode.Alternate;
                    cache[provedor] = basePath;
                }
            }
            var p = (GraphicsPath)basePath.Clone();
            float k = tamanho / 24f;
            using (var m = new Matrix())
            {
                m.Translate(cx - tamanho / 2, cy - tamanho / 2);
                m.Scale(k, k);
                p.Transform(m);
            }
            return p;
        }
    }

    // Interpretador mínimo de "d" de SVG: M L H V C S Q T A Z, absolutos e relativos.
    static class Svg
    {
        public static GraphicsPath Interpretar(string d)
        {
            var g = new GraphicsPath();
            int i = 0;
            char cmd = 'M';
            float x = 0, y = 0, inicioX = 0, inicioY = 0, ctrlX = 0, ctrlY = 0;
            char ultimo = ' ';
            bool figuraAberta = false;
            while (true)
            {
                Pular(d, ref i);
                if (i >= d.Length) break;
                char c = d[i];
                if (char.IsLetter(c)) { cmd = c; i++; }
                else if (cmd == 'M') cmd = 'L';
                else if (cmd == 'm') cmd = 'l';
                bool rel = char.IsLower(cmd);
                char C = char.ToUpperInvariant(cmd);
                switch (C)
                {
                    case 'M':
                        {
                            float nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { nx += x; ny += y; }
                            if (figuraAberta) g.CloseFigure();
                            g.StartFigure();
                            figuraAberta = true;
                            x = inicioX = nx; y = inicioY = ny;
                            break;
                        }
                    case 'L':
                        {
                            float nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { nx += x; ny += y; }
                            g.AddLine(x, y, nx, ny); x = nx; y = ny;
                            break;
                        }
                    case 'H':
                        {
                            float nx = Num(d, ref i); if (rel) nx += x;
                            g.AddLine(x, y, nx, y); x = nx;
                            break;
                        }
                    case 'V':
                        {
                            float ny = Num(d, ref i); if (rel) ny += y;
                            g.AddLine(x, y, x, ny); y = ny;
                            break;
                        }
                    case 'C':
                        {
                            float x1 = Num(d, ref i), y1 = Num(d, ref i), x2 = Num(d, ref i), y2 = Num(d, ref i), nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { x1 += x; y1 += y; x2 += x; y2 += y; nx += x; ny += y; }
                            g.AddBezier(x, y, x1, y1, x2, y2, nx, ny);
                            ctrlX = x2; ctrlY = y2; x = nx; y = ny;
                            break;
                        }
                    case 'S':
                        {
                            float x2 = Num(d, ref i), y2 = Num(d, ref i), nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { x2 += x; y2 += y; nx += x; ny += y; }
                            char u = char.ToUpperInvariant(ultimo);
                            float x1 = u == 'C' || u == 'S' ? 2 * x - ctrlX : x, y1 = u == 'C' || u == 'S' ? 2 * y - ctrlY : y;
                            g.AddBezier(x, y, x1, y1, x2, y2, nx, ny);
                            ctrlX = x2; ctrlY = y2; x = nx; y = ny;
                            break;
                        }
                    case 'Q':
                        {
                            float qx = Num(d, ref i), qy = Num(d, ref i), nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { qx += x; qy += y; nx += x; ny += y; }
                            Quad(g, x, y, qx, qy, nx, ny);
                            ctrlX = qx; ctrlY = qy; x = nx; y = ny;
                            break;
                        }
                    case 'T':
                        {
                            float nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { nx += x; ny += y; }
                            char u = char.ToUpperInvariant(ultimo);
                            float qx = u == 'Q' || u == 'T' ? 2 * x - ctrlX : x, qy = u == 'Q' || u == 'T' ? 2 * y - ctrlY : y;
                            Quad(g, x, y, qx, qy, nx, ny);
                            ctrlX = qx; ctrlY = qy; x = nx; y = ny;
                            break;
                        }
                    case 'A':
                        {
                            float rx = Num(d, ref i), ry = Num(d, ref i), rot = Num(d, ref i);
                            bool grande = Flag(d, ref i), horario = Flag(d, ref i);
                            float nx = Num(d, ref i), ny = Num(d, ref i);
                            if (rel) { nx += x; ny += y; }
                            Arco(g, x, y, rx, ry, rot, grande, horario, nx, ny);
                            x = nx; y = ny;
                            break;
                        }
                    case 'Z':
                        g.CloseFigure();
                        figuraAberta = false;
                        x = inicioX; y = inicioY;
                        break;
                    default:
                        i++;
                        break;
                }
                ultimo = cmd;
            }
            return g;
        }

        static void Quad(GraphicsPath g, float x0, float y0, float qx, float qy, float x, float y)
        {
            g.AddBezier(x0, y0, x0 + 2f / 3 * (qx - x0), y0 + 2f / 3 * (qy - y0), x + 2f / 3 * (qx - x), y + 2f / 3 * (qy - y), x, y);
        }

        // Arco elíptico do SVG convertido em Béziers (parametrização por centro, segmentos de até 90°)
        static void Arco(GraphicsPath g, float x1, float y1, float rx, float ry, float rotGraus, bool grande, bool horario, float x2, float y2)
        {
            if (rx == 0 || ry == 0) { g.AddLine(x1, y1, x2, y2); return; }
            double phi = rotGraus * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            double dx = (x1 - x2) / 2.0, dy = (y1 - y2) / 2.0;
            double x1p = cos * dx + sin * dy, y1p = -sin * dx + cos * dy;
            double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1) { double s = Math.Sqrt(lambda); rx = (float)(rx * s); ry = (float)(ry * s); }
            double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den)) * (grande == horario ? -1 : 1);
            double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
            double cx = cos * cxp - sin * cyp + (x1 + x2) / 2.0, cy = sin * cxp + cos * cyp + (y1 + y2) / 2.0;
            double t1 = Angulo(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double dt = Angulo((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
            if (!horario && dt > 0) dt -= 2 * Math.PI;
            else if (horario && dt < 0) dt += 2 * Math.PI;
            int n = (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2));
            double passo = dt / n, k = 4.0 / 3 * Math.Tan(passo / 4);
            double px = x1, py = y1;
            for (int s = 0; s < n; s++)
            {
                double a1 = t1 + s * passo, a2 = a1 + passo;
                double c1x = Math.Cos(a1) - k * Math.Sin(a1), c1y = Math.Sin(a1) + k * Math.Cos(a1);
                double c2x = Math.Cos(a2) + k * Math.Sin(a2), c2y = Math.Sin(a2) - k * Math.Cos(a2);
                double ex = Math.Cos(a2), ey = Math.Sin(a2);
                Func<double, double, PointF> P = (u, v) => new PointF(
                    (float)(cx + cos * rx * u - sin * ry * v), (float)(cy + sin * rx * u + cos * ry * v));
                PointF q1 = P(c1x, c1y), q2 = P(c2x, c2y), fim = s == n - 1 ? new PointF(x2, y2) : P(ex, ey);
                g.AddBezier(new PointF((float)px, (float)py), q1, q2, fim);
                px = fim.X; py = fim.Y;
            }
        }

        static double Angulo(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }

        static void Pular(string d, ref int i)
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\r' || d[i] == '\t')) i++;
        }

        static bool Flag(string d, ref int i)
        {
            Pular(d, ref i);
            bool v = i < d.Length && d[i] == '1';
            i++;
            return v;
        }

        static float Num(string d, ref int i)
        {
            Pular(d, ref i);
            int ini = i;
            if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            bool ponto = false, exp = false;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsDigit(c)) { i++; continue; }
                if (c == '.' && !ponto && !exp) { ponto = true; i++; continue; }
                if ((c == 'e' || c == 'E') && !exp)
                {
                    exp = true; i++;
                    if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                    continue;
                }
                break;
            }
            if (i == ini) { i++; return 0; }
            return float.Parse(d.Substring(ini, i - ini), CultureInfo.InvariantCulture);
        }
    }
}
