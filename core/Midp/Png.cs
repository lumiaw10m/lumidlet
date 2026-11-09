using System;
using System.IO;
using System.IO.Compression;

namespace Lumidlet.Midp
{
    public static class Png
    {
        static readonly int[] Ax = { 0, 4, 0, 2, 0, 1, 0 }, Ay = { 0, 0, 4, 0, 2, 0, 1 }, Dx = { 8, 8, 4, 4, 2, 2, 1 }, Dy = { 8, 8, 8, 4, 4, 2, 2 };

        public static bool IsPng(byte[] d, int off, int len) =>
            len > 8 && d[off] == 0x89 && d[off + 1] == 'P' && d[off + 2] == 'N' && d[off + 3] == 'G';

        public static int[] Decode(byte[] d, int off, int len, out int w, out int h, out bool alpha)
        {
            w = h = 0; alpha = false;
            if (!IsPng(d, off, len)) return null;
            int p = off + 8, end = off + len;
            int depth = 0, ctype = 0, interlace = 0;
            int[] pal = null;
            byte[] trnsPal = null;
            int trnsKey = -1, trnsR = -1, trnsG = -1, trnsB = -1;
            var idat = new MemoryStream();
            while (p + 8 <= end)
            {
                int clen = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
                string type = new string(new[] { (char)d[p + 4], (char)d[p + 5], (char)d[p + 6], (char)d[p + 7] });
                int cd = p + 8;
                if (clen < 0 || cd + clen > end) clen = Math.Max(0, end - cd);
                switch (type)
                {
                    case "IHDR":
                        w = (d[cd] << 24) | (d[cd + 1] << 16) | (d[cd + 2] << 8) | d[cd + 3];
                        h = (d[cd + 4] << 24) | (d[cd + 5] << 16) | (d[cd + 6] << 8) | d[cd + 7];
                        depth = d[cd + 8]; ctype = d[cd + 9]; interlace = d[cd + 12];
                        break;
                    case "PLTE":
                        pal = new int[256];
                        for (int i = 0; i < clen / 3; i++) pal[i] = unchecked((int)0xFF000000) | (d[cd + i * 3] << 16) | (d[cd + i * 3 + 1] << 8) | d[cd + i * 3 + 2];
                        break;
                    case "tRNS":
                        if (ctype == 3) { trnsPal = new byte[clen]; Buffer.BlockCopy(d, cd, trnsPal, 0, clen); }
                        else if (ctype == 0 && clen >= 2) trnsKey = (d[cd] << 8) | d[cd + 1];
                        else if (ctype == 2 && clen >= 6) { trnsR = (d[cd] << 8) | d[cd + 1]; trnsG = (d[cd + 2] << 8) | d[cd + 3]; trnsB = (d[cd + 4] << 8) | d[cd + 5]; }
                        break;
                    case "IDAT": idat.Write(d, cd, clen); break;
                }
                if (type == "IEND") break;
                p = cd + clen + 4;
            }
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
            if (pal != null && trnsPal != null)
                for (int i = 0; i < trnsPal.Length && i < 256; i++) pal[i] = (pal[i] & 0xFFFFFF) | (trnsPal[i] << 24);

            int channels;
            switch (ctype)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: return null;
            }
            int bitsPP = channels * depth;
            int bpp = Math.Max(1, bitsPP / 8);
            var z = idat.ToArray();
            byte[] raw;
            try
            {
                var ms = new MemoryStream();
                using (var ds = new DeflateStream(new MemoryStream(z, 2, Math.Max(0, z.Length - 2)), CompressionMode.Decompress))
                {
                    var buf = new byte[16384];
                    int n;
                    try { while ((n = ds.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n); }
                    catch (InvalidDataException) { }
                }
                raw = ms.ToArray();
            }
            catch (Exception) { return null; }

            var outp = new int[w * h];
            int rp = 0;
            bool hasAlpha = ctype == 4 || ctype == 6 || trnsPal != null || trnsKey >= 0 || trnsR >= 0;
            int passes = interlace == 1 ? 7 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                int x0 = interlace == 1 ? Ax[pass] : 0, y0 = interlace == 1 ? Ay[pass] : 0;
                int dx = interlace == 1 ? Dx[pass] : 1, dy = interlace == 1 ? Dy[pass] : 1;
                int pw = (w - x0 + dx - 1) / dx, ph = (h - y0 + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;
                int stride = (pw * bitsPP + 7) / 8;
                var prev = new byte[stride];
                var cur = new byte[stride];
                for (int row = 0; row < ph; row++)
                {
                    if (rp >= raw.Length) break;
                    int filter = raw[rp++];
                    int avail = Math.Min(stride, raw.Length - rp);
                    Buffer.BlockCopy(raw, rp, cur, 0, avail);
                    if (avail < stride) Array.Clear(cur, avail, stride - avail);
                    rp += avail;
                    Unfilter(filter, cur, prev, bpp);
                    int y = y0 + row * dy;
                    for (int col = 0; col < pw; col++)
                    {
                        int x = x0 + col * dx;
                        outp[y * w + x] = Pixel(cur, col, ctype, depth, pal, trnsKey, trnsR, trnsG, trnsB);
                    }
                    var tmp = prev; prev = cur; cur = tmp;
                }
            }
            alpha = hasAlpha;
            return outp;
        }

        static void Unfilter(int f, byte[] c, byte[] p, int bpp)
        {
            int n = c.Length;
            switch (f)
            {
                case 1: for (int i = bpp; i < n; i++) c[i] += c[i - bpp]; break;
                case 2: for (int i = 0; i < n; i++) c[i] += p[i]; break;
                case 3:
                    for (int i = 0; i < n; i++) c[i] += (byte)(((i >= bpp ? c[i - bpp] : 0) + p[i]) >> 1);
                    break;
                case 4:
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? c[i - bpp] : 0, b = p[i], cc = i >= bpp ? p[i - bpp] : 0;
                        int pp = a + b - cc, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - cc);
                        c[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : cc);
                    }
                    break;
            }
        }

        static int Sample(byte[] r, int idx, int depth)
        {
            switch (depth)
            {
                case 8: return r[idx];
                case 16: return (r[idx * 2] << 8) | r[idx * 2 + 1];
                default:
                    {
                        int bit = idx * depth;
                        int v = r[bit >> 3];
                        int shift = 8 - depth - (bit & 7);
                        return (v >> shift) & ((1 << depth) - 1);
                    }
            }
        }

        static int To8(int v, int depth)
        {
            switch (depth)
            {
                case 1: return v * 255;
                case 2: return v * 85;
                case 4: return v * 17;
                case 16: return v >> 8;
                default: return v;
            }
        }

        static int Pixel(byte[] r, int x, int ct, int depth, int[] pal, int tk, int tr, int tg, int tb)
        {
            switch (ct)
            {
                case 0:
                    {
                        int s = Sample(r, x, depth);
                        int g = To8(s, depth);
                        int a = s == tk ? 0 : 255;
                        return (a << 24) | (g << 16) | (g << 8) | g;
                    }
                case 2:
                    {
                        int rr = Sample(r, x * 3, depth), gg = Sample(r, x * 3 + 1, depth), bb = Sample(r, x * 3 + 2, depth);
                        int a = rr == tr && gg == tg && bb == tb ? 0 : 255;
                        return (a << 24) | (To8(rr, depth) << 16) | (To8(gg, depth) << 8) | To8(bb, depth);
                    }
                case 3:
                    {
                        int i = Sample(r, x, depth);
                        return pal != null ? pal[i] : unchecked((int)0xFF000000);
                    }
                case 4:
                    {
                        int g = To8(Sample(r, x * 2, depth), depth), a = To8(Sample(r, x * 2 + 1, depth), depth);
                        return (a << 24) | (g << 16) | (g << 8) | g;
                    }
                default:
                    {
                        int rr = To8(Sample(r, x * 4, depth), depth), gg = To8(Sample(r, x * 4 + 1, depth), depth);
                        int bb = To8(Sample(r, x * 4 + 2, depth), depth), a = To8(Sample(r, x * 4 + 3, depth), depth);
                        return (a << 24) | (rr << 16) | (gg << 8) | bb;
                    }
            }
        }
    }
}
