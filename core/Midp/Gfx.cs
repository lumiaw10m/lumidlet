using System;
using System.Collections.Generic;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class Surface
    {
        public readonly int W, H;
        public readonly int[] Pix;
        public bool HasAlpha;

        public Surface(int w, int h, int fill = -1)
        {
            W = Math.Max(1, w); H = Math.Max(1, h);
            Pix = new int[W * H];
            if (fill != 0) for (int i = 0; i < Pix.Length; i++) Pix[i] = fill;
        }

        public Surface(int w, int h, int[] pix, bool alpha) { W = w; H = h; Pix = pix; HasAlpha = alpha; }
    }

    public sealed class ImageObj : JObject
    {
        public Surface S;
        public bool Mutable;
    }

    public sealed class FontObj : JObject
    {
        public int Face, Style, Size, Px, Height, Ascent;
        public FontCache Cache;

        public Glyph G(char c) => Cache.Get(c, Px, Style & 3);

        public int CharWidth(char c) => G(c).Advance;

        public int StringWidth(string s, int off, int len)
        {
            int w = 0;
            for (int i = 0; i < len; i++) w += G(s[off + i]).Advance;
            return w;
        }

        public int CharsWidth(char[] s, int off, int len)
        {
            int w = 0;
            for (int i = 0; i < len; i++) w += G(s[off + i]).Advance;
            return w;
        }
    }

    public sealed class FontCache
    {
        readonly IHost host;
        readonly Dictionary<long, Glyph> map = new Dictionary<long, Glyph>();
        public FontCache(IHost h) { host = h; }

        public Glyph Get(char c, int px, int style)
        {
            long k = c | ((long)px << 16) | ((long)style << 32);
            lock (map)
            {
                Glyph g;
                if (map.TryGetValue(k, out g)) return g;
            }
            var ng = host.RenderGlyph(c, px, style) ?? new Glyph { W = 0, H = 0, Advance = px / 2, A = new byte[0] };
            lock (map) map[k] = ng;
            return ng;
        }
    }

    public sealed class GraphicsObj : JObject
    {
        public Surface S;
        public int Tx, Ty;
        public int Cx0, Cy0, Cx1, Cy1;
        public int Lx0, Ly0, Lx1, Ly1;
        public int Color = unchecked((int)0xFF000000);
        public FontObj Font;
        public int Stroke;
        public bool Screen;

        public void Attach(Surface s, int x0, int y0, int x1, int y1)
        {
            S = s;
            Lx0 = Math.Max(0, x0); Ly0 = Math.Max(0, y0); Lx1 = Math.Min(s.W, x1); Ly1 = Math.Min(s.H, y1);
            Tx = Ty = 0;
            Cx0 = Lx0; Cy0 = Ly0; Cx1 = Lx1; Cy1 = Ly1;
            Color = unchecked((int)0xFF000000);
            Stroke = 0;
        }

        public void SetClip(int x, int y, int w, int h)
        {
            x += Tx; y += Ty;
            Cx0 = Math.Max(Lx0, x); Cy0 = Math.Max(Ly0, y);
            Cx1 = Math.Min(Lx1, x + Math.Max(0, w)); Cy1 = Math.Min(Ly1, y + Math.Max(0, h));
            if (Cx1 < Cx0) Cx1 = Cx0;
            if (Cy1 < Cy0) Cy1 = Cy0;
        }

        public void ClipRect(int x, int y, int w, int h)
        {
            x += Tx; y += Ty;
            Cx0 = Math.Max(Cx0, x); Cy0 = Math.Max(Cy0, y);
            Cx1 = Math.Min(Cx1, x + Math.Max(0, w)); Cy1 = Math.Min(Cy1, y + Math.Max(0, h));
            if (Cx1 < Cx0) Cx1 = Cx0;
            if (Cy1 < Cy0) Cy1 = Cy0;
        }
    }

    public static class Gfx
    {
        public const int HCENTER = 1, VCENTER = 2, LEFT = 4, RIGHT = 8, TOP = 16, BOTTOM = 32, BASELINE = 64;

        public static int Over(int d, int s)
        {
            uint sa = (uint)s >> 24;
            if (sa == 255) return s;
            if (sa == 0) return d;
            uint da = (uint)d >> 24;
            uint sr = (uint)(s >> 16) & 255, sg = (uint)(s >> 8) & 255, sb = (uint)s & 255;
            uint dr = (uint)(d >> 16) & 255, dg = (uint)(d >> 8) & 255, db = (uint)d & 255;
            if (da == 255)
            {
                uint ia = 255 - sa;
                return unchecked((int)(0xFF000000 | (((sr * sa + dr * ia) / 255) << 16) | (((sg * sa + dg * ia) / 255) << 8) | ((sb * sa + db * ia) / 255)));
            }
            uint dfac = da * (255 - sa) / 255;
            uint oa = sa + dfac;
            if (oa == 0) return 0;
            return unchecked((int)((oa << 24) | (((sr * sa + dr * dfac) / oa) << 16) | (((sg * sa + dg * dfac) / oa) << 8) | ((sb * sa + db * dfac) / oa)));
        }

        static void Put(GraphicsObj g, int x, int y, int c)
        {
            if (x < g.Cx0 || y < g.Cy0 || x >= g.Cx1 || y >= g.Cy1) return;
            int i = y * g.S.W + x;
            g.S.Pix[i] = ((uint)c >> 24) == 255 ? c : Over(g.S.Pix[i], c);
        }

        static void Span(GraphicsObj g, int x0, int x1, int y, int c)
        {
            if (y < g.Cy0 || y >= g.Cy1) return;
            if (x0 < g.Cx0) x0 = g.Cx0;
            if (x1 > g.Cx1) x1 = g.Cx1;
            if (x0 >= x1) return;
            var p = g.S.Pix;
            int i = y * g.S.W + x0, e = y * g.S.W + x1;
            if (((uint)c >> 24) == 255) for (; i < e; i++) p[i] = c;
            else for (; i < e; i++) p[i] = Over(p[i], c);
        }

        public static void FillRect(GraphicsObj g, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            x += g.Tx; y += g.Ty;
            int y0 = Math.Max(y, g.Cy0), y1 = Math.Min(y + h, g.Cy1);
            for (int yy = y0; yy < y1; yy++) Span(g, x, x + w, yy, g.Color);
        }

        public static void DrawLine(GraphicsObj g, int x0, int y0, int x1, int y1)
        {
            x0 += g.Tx; y0 += g.Ty; x1 += g.Tx; y1 += g.Ty;
            int c = g.Color;
            if (y0 == y1 && g.Stroke == 0) { Span(g, Math.Min(x0, x1), Math.Max(x0, x1) + 1, y0, c); return; }
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, n = 0;
            while (true)
            {
                if (g.Stroke == 0 || (n++ % 6) < 3) Put(g, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public static void DrawRect(GraphicsObj g, int x, int y, int w, int h)
        {
            if (w < 0 || h < 0) return;
            if (w == 0 || h == 0) { DrawLine(g, x, y, x + w, y + h); return; }
            DrawLine(g, x, y, x + w, y);
            DrawLine(g, x, y + h, x + w, y + h);
            DrawLine(g, x, y + 1, x, y + h - 1);
            DrawLine(g, x + w, y + 1, x + w, y + h - 1);
        }

        public static void FillRoundRect(GraphicsObj g, int x, int y, int w, int h, int aw, int ah)
        {
            if (w <= 0 || h <= 0) return;
            aw = Math.Min(Math.Abs(aw), w); ah = Math.Min(Math.Abs(ah), h);
            double rx = aw / 2.0, ry = ah / 2.0;
            int ax = x + g.Tx, ay = y + g.Ty;
            for (int row = 0; row < h; row++)
            {
                int inset = 0;
                double py = row + 0.5;
                double dy = py < ry ? ry - py : py > h - ry ? py - (h - ry) : 0;
                if (dy > 0 && ry > 0)
                {
                    double t = 1 - (dy * dy) / (ry * ry);
                    inset = (int)Math.Round(rx - rx * Math.Sqrt(Math.Max(0, t)));
                }
                Span(g, ax + inset, ax + w - inset, ay + row, g.Color);
            }
        }

        public static void DrawRoundRect(GraphicsObj g, int x, int y, int w, int h, int aw, int ah)
        {
            if (w < 0 || h < 0) return;
            aw = Math.Min(Math.Abs(aw), w); ah = Math.Min(Math.Abs(ah), h);
            int rx = aw / 2, ry = ah / 2;
            DrawLine(g, x + rx, y, x + w - rx, y);
            DrawLine(g, x + rx, y + h, x + w - rx, y + h);
            DrawLine(g, x, y + ry, x, y + h - ry);
            DrawLine(g, x + w, y + ry, x + w, y + h - ry);
            if (rx > 0 && ry > 0)
            {
                DrawArc(g, x, y, aw, ah, 90, 90);
                DrawArc(g, x + w - aw, y, aw, ah, 0, 90);
                DrawArc(g, x, y + h - ah, aw, ah, 180, 90);
                DrawArc(g, x + w - aw, y + h - ah, aw, ah, 270, 90);
            }
        }

        static void NormArc(ref int start, ref int arc)
        {
            if (arc < 0) { start += arc; arc = -arc; }
            start %= 360;
            if (start < 0) start += 360;
        }

        static bool InArc(double ang, int start, int arc)
        {
            if (arc >= 360) return true;
            double a = ang - start;
            while (a < 0) a += 360;
            while (a >= 360) a -= 360;
            return a <= arc;
        }

        public static void FillArc(GraphicsObj g, int x, int y, int w, int h, int start, int arc)
        {
            if (w <= 0 || h <= 0 || arc == 0) return;
            NormArc(ref start, ref arc);
            double rx = w / 2.0, ry = h / 2.0, cx = x + rx, cy = y + ry;
            int c = g.Color;
            for (int row = 0; row < h; row++)
            {
                double py = y + row + 0.5, dy = (py - cy) / ry;
                if (dy * dy > 1) continue;
                double half = rx * Math.Sqrt(1 - dy * dy);
                int xl = (int)Math.Round(cx - half), xr = (int)Math.Round(cx + half);
                if (arc >= 360) { Span(g, xl + g.Tx, xr + g.Tx, y + row + g.Ty, c); continue; }
                for (int px = xl; px < xr; px++)
                {
                    double dx = (px + 0.5 - cx) / rx;
                    double ang = Math.Atan2(-dy, dx) * 180 / Math.PI;
                    if (InArc(ang, start, arc)) Put(g, px + g.Tx, y + row + g.Ty, c);
                }
            }
        }

        public static void DrawArc(GraphicsObj g, int x, int y, int w, int h, int start, int arc)
        {
            if (w < 0 || h < 0 || arc == 0) return;
            NormArc(ref start, ref arc);
            if (arc > 360) arc = 360;
            double rx = w / 2.0, ry = h / 2.0, cx = x + rx, cy = y + ry;
            int steps = Math.Max(8, (int)((rx + ry) * Math.PI * arc / 180.0));
            int px = int.MinValue, py = 0;
            for (int i = 0; i <= steps; i++)
            {
                double a = (start + arc * (double)i / steps) * Math.PI / 180;
                int nx = (int)Math.Round(cx + rx * Math.Cos(a)), ny = (int)Math.Round(cy - ry * Math.Sin(a));
                if (px == int.MinValue) Put(g, nx + g.Tx, ny + g.Ty, g.Color);
                else if (nx != px || ny != py) DrawLine(g, px, py, nx, ny);
                px = nx; py = ny;
            }
        }

        public static void FillTriangle(GraphicsObj g, int x1, int y1, int x2, int y2, int x3, int y3)
        {
            FillPolygon(g, new[] { x1, x2, x3 }, 0, new[] { y1, y2, y3 }, 0, 3);
        }

        public static void FillPolygon(GraphicsObj g, int[] xs, int xo, int[] ys, int yo, int n)
        {
            if (n < 3) return;
            int miny = int.MaxValue, maxy = int.MinValue;
            for (int i = 0; i < n; i++) { miny = Math.Min(miny, ys[yo + i]); maxy = Math.Max(maxy, ys[yo + i]); }
            if (miny + g.Ty < g.Cy0) miny = g.Cy0 - g.Ty;
            if (maxy + g.Ty >= g.Cy1) maxy = g.Cy1 - g.Ty - 1;
            var xsect = new List<double>(8);
            for (int y = miny; y <= maxy; y++)
            {
                xsect.Clear();
                double sy = y + 0.5;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double ay = ys[yo + i], by = ys[yo + j];
                    if (ay == by) continue;
                    if ((sy >= ay && sy < by) || (sy >= by && sy < ay))
                        xsect.Add(xs[xo + i] + (sy - ay) * (xs[xo + j] - xs[xo + i]) / (by - ay));
                }
                xsect.Sort();
                for (int k = 0; k + 1 < xsect.Count; k += 2)
                {
                    int xl = (int)Math.Ceiling(xsect[k] - 0.5), xr = (int)Math.Ceiling(xsect[k + 1] - 0.5);
                    if (xr == xl) xr = xl + 1;
                    Span(g, xl + g.Tx, xr + g.Tx, y + g.Ty, g.Color);
                }
            }
        }

        public static void DrawPolygon(GraphicsObj g, int[] xs, int xo, int[] ys, int yo, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                DrawLine(g, xs[xo + i], ys[yo + i], xs[xo + j], ys[yo + j]);
            }
        }

        public static void Anchor(int anchor, int w, int h, ref int x, ref int y, int baseline)
        {
            if ((anchor & HCENTER) != 0) x -= w / 2;
            else if ((anchor & RIGHT) != 0) x -= w;
            if ((anchor & VCENTER) != 0) y -= h / 2;
            else if ((anchor & BOTTOM) != 0) y -= h;
            else if ((anchor & BASELINE) != 0) y -= baseline;
        }

        public static void DrawString(GraphicsObj g, string s, int off, int len, int x, int y, int anchor)
        {
            var f = g.Font;
            if (anchor == 0) anchor = TOP | LEFT;
            int w = f.StringWidth(s, off, len);
            Anchor(anchor, w, f.Height, ref x, ref y, f.Ascent);
            x += g.Tx; y += g.Ty;
            if (y >= g.Cy1 || y + f.Height <= g.Cy0) return;
            int pen = x;
            for (int i = 0; i < len; i++)
            {
                var gl = f.G(s[off + i]);
                if (pen < g.Cx1 && pen + gl.W > g.Cx0) Glyph(g, gl, pen, y);
                pen += gl.Advance;
            }
            if ((f.Style & 4) != 0) Span(g, x, x + w, y + f.Ascent + 1, g.Color);
        }

        static void Glyph(GraphicsObj g, Glyph gl, int x, int y)
        {
            int c = g.Color & 0xFFFFFF, ca = (int)((uint)g.Color >> 24);
            var p = g.S.Pix;
            int sw = g.S.W;
            int y0 = Math.Max(y, g.Cy0), y1 = Math.Min(y + gl.H, g.Cy1);
            int x0 = Math.Max(x, g.Cx0), x1 = Math.Min(x + gl.W, g.Cx1);
            for (int yy = y0; yy < y1; yy++)
            {
                int gr = (yy - y) * gl.W - x, row = yy * sw;
                for (int xx = x0; xx < x1; xx++)
                {
                    int a = gl.A[gr + xx];
                    if (a == 0) continue;
                    if (ca != 255) a = a * ca / 255;
                    p[row + xx] = a >= 255 ? (c | unchecked((int)0xFF000000)) : Over(p[row + xx], (a << 24) | c);
                }
            }
        }

        public static void DrawImage(GraphicsObj g, Surface src, int x, int y, int anchor)
        {
            if (anchor == 0) anchor = TOP | LEFT;
            Anchor(anchor, src.W, src.H, ref x, ref y, src.H);
            Blit(g, src, 0, 0, src.W, src.H, x + g.Tx, y + g.Ty, true);
        }

        public static void Blit(GraphicsObj g, Surface src, int sx, int sy, int w, int h, int dx, int dy, bool alpha)
        {
            int x0 = Math.Max(dx, g.Cx0), y0 = Math.Max(dy, g.Cy0);
            int x1 = Math.Min(dx + w, g.Cx1), y1 = Math.Min(dy + h, g.Cy1);
            if (x0 >= x1 || y0 >= y1) return;
            var sp = src.Pix; var dp = g.S.Pix;
            int sw = src.W, dw = g.S.W;
            bool blend = alpha && src.HasAlpha;
            bool keepAlpha = g.S.HasAlpha;
            for (int yy = y0; yy < y1; yy++)
            {
                int si = (sy + yy - dy) * sw + sx + x0 - dx;
                int di = yy * dw + x0;
                int n = x1 - x0;
                if (!blend)
                {
                    if (keepAlpha || !src.HasAlpha) Array.Copy(sp, si, dp, di, n);
                    else for (int k = 0; k < n; k++) dp[di + k] = sp[si + k] | unchecked((int)0xFF000000);
                }
                else
                {
                    for (int k = 0; k < n; k++)
                    {
                        int s = sp[si + k];
                        uint a = (uint)s >> 24;
                        if (a == 255) dp[di + k] = s;
                        else if (a != 0) dp[di + k] = Over(dp[di + k], s);
                    }
                }
            }
        }

        public static void TransformPoint(int t, int sx, int sy, int w, int h, out int dx, out int dy)
        {
            switch (t)
            {
                case 0: dx = sx; dy = sy; break;
                case 2: dx = w - 1 - sx; dy = sy; break;
                case 1: dx = sx; dy = h - 1 - sy; break;
                case 3: dx = w - 1 - sx; dy = h - 1 - sy; break;
                case 5: dx = h - 1 - sy; dy = sx; break;
                case 7: dx = h - 1 - sy; dy = w - 1 - sx; break;
                case 6: dx = sy; dy = w - 1 - sx; break;
                default: dx = sy; dy = sx; break;
            }
        }

        public static bool Swaps(int t) => t >= 4;

        public static void DrawRegion(GraphicsObj g, Surface src, int sx, int sy, int w, int h, int t, int x, int y, int anchor)
        {
            if (w <= 0 || h <= 0) return;
            if (sx < 0 || sy < 0 || sx + w > src.W || sy + h > src.H || t < 0 || t > 7)
                throw new JavaThrow("java/lang/IllegalArgumentException", "region");
            if (anchor == 0) anchor = TOP | LEFT;
            int tw = Swaps(t) ? h : w, th = Swaps(t) ? w : h;
            Anchor(anchor, tw, th, ref x, ref y, th);
            x += g.Tx; y += g.Ty;
            if (t == 0) { Blit(g, src, sx, sy, w, h, x, y, true); return; }
            int x0 = Math.Max(x, g.Cx0), y0 = Math.Max(y, g.Cy0);
            int x1 = Math.Min(x + tw, g.Cx1), y1 = Math.Min(y + th, g.Cy1);
            var sp = src.Pix; var dp = g.S.Pix;
            int sw = src.W, dw = g.S.W;
            bool blend = src.HasAlpha;
            bool keep = g.S.HasAlpha;
            for (int yy = y0; yy < y1; yy++)
            {
                int ty = yy - y;
                for (int xx = x0; xx < x1; xx++)
                {
                    int tx = xx - x, ox, oy;
                    switch (t)
                    {
                        case 2: ox = w - 1 - tx; oy = ty; break;
                        case 1: ox = tx; oy = h - 1 - ty; break;
                        case 3: ox = w - 1 - tx; oy = h - 1 - ty; break;
                        case 5: ox = ty; oy = h - 1 - tx; break;
                        case 7: ox = w - 1 - ty; oy = h - 1 - tx; break;
                        case 6: ox = w - 1 - ty; oy = tx; break;
                        default: ox = ty; oy = tx; break;
                    }
                    int s = sp[(sy + oy) * sw + sx + ox];
                    int di = yy * dw + xx;
                    if (!blend) dp[di] = keep ? s : s | unchecked((int)0xFF000000);
                    else
                    {
                        uint a = (uint)s >> 24;
                        if (a == 255) dp[di] = s;
                        else if (a != 0) dp[di] = Over(dp[di], s);
                    }
                }
            }
        }

        public static void DrawRGB(GraphicsObj g, int[] rgb, int off, int scan, int x, int y, int w, int h, bool alpha)
        {
            if (rgb == null) throw new NullReferenceException();
            if (w <= 0 || h <= 0) return;
            x += g.Tx; y += g.Ty;
            int x0 = Math.Max(x, g.Cx0), y0 = Math.Max(y, g.Cy0);
            int x1 = Math.Min(x + w, g.Cx1), y1 = Math.Min(y + h, g.Cy1);
            var dp = g.S.Pix; int dw = g.S.W;
            for (int yy = y0; yy < y1; yy++)
            {
                int si = off + (yy - y) * scan + (x0 - x);
                int di = yy * dw + x0;
                for (int xx = x0; xx < x1; xx++, si++, di++)
                {
                    if (si < 0 || si >= rgb.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    int s = rgb[si];
                    if (!alpha) dp[di] = s | unchecked((int)0xFF000000);
                    else
                    {
                        uint a = (uint)s >> 24;
                        if (a == 255) dp[di] = s;
                        else if (a != 0) dp[di] = Over(dp[di], s);
                    }
                }
            }
        }

        public static void CopyArea(GraphicsObj g, int sx, int sy, int w, int h, int dx, int dy, int anchor)
        {
            var s = g.S;
            sx += g.Tx; sy += g.Ty;
            if (sx < 0 || sy < 0 || sx + w > s.W || sy + h > s.H || w < 0 || h < 0) throw new JavaThrow("java/lang/IllegalArgumentException", "copyArea");
            var tmp = new Surface(w, h, new int[w * h], false);
            for (int r = 0; r < h; r++) Array.Copy(s.Pix, (sy + r) * s.W + sx, tmp.Pix, r * w, w);
            if (anchor == 0) anchor = TOP | LEFT;
            Anchor(anchor, w, h, ref dx, ref dy, h);
            Blit(g, tmp, 0, 0, w, h, dx + g.Tx, dy + g.Ty, false);
        }
    }
}
