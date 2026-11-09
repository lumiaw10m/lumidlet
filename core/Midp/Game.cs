using System;
using System.Collections.Generic;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public class LayerObj : JObject
    {
        public int X, Y, W, H;
        public bool Visible = true;
    }

    public sealed class SpriteObj : LayerObj
    {
        public ImageObj Img;
        public int FrameW, FrameH, Cols, Frames;
        public int[] Seq;
        public int SeqIdx;
        public int RefX, RefY, Trans;
        public int Cx, Cy, Cw, Ch;
    }

    public sealed class TiledObj : LayerObj
    {
        public ImageObj Img;
        public int TileW, TileH, Cols, Rows, ImgCols, NumTiles;
        public int[] Cells;
        public readonly List<int> Anim = new List<int>();
    }

    public sealed class LayerManagerObj : JObject
    {
        public readonly List<LayerObj> Layers = new List<LayerObj>();
        public int Vx, Vy, Vw = int.MaxValue, Vh = int.MaxValue;
    }

    public static class Game
    {
        const string P = "javax/microedition/lcdui/";
        const string G = "javax/microedition/lcdui/game/";
        static MidpRuntime R(JThread t) => MidpRuntime.Of(t);
        static SpriteObj Sp(JThread t, int b) => (SpriteObj)t.R[b];
        static TiledObj Tl(JThread t, int b) => (TiledObj)t.R[b];
        static LayerObj L(JThread t, int b) => (LayerObj)t.R[b];

        static void SetFrames(SpriteObj s, ImageObj img, int fw, int fh)
        {
            if (img == null) throw new NullReferenceException();
            if (fw <= 0 || fh <= 0 || img.S.W % fw != 0 || img.S.H % fh != 0) throw new JavaThrow("java/lang/IllegalArgumentException", "frame size");
            int oldFrames = s.Frames;
            s.Img = img; s.FrameW = fw; s.FrameH = fh;
            s.Cols = img.S.W / fw;
            s.Frames = s.Cols * (img.S.H / fh);
            if (s.Seq == null || s.Frames < oldFrames || IsDefault(s.Seq, oldFrames))
            {
                s.Seq = new int[s.Frames];
                for (int i = 0; i < s.Frames; i++) s.Seq[i] = i;
                s.SeqIdx = 0;
            }
            s.Cx = 0; s.Cy = 0; s.Cw = fw; s.Ch = fh;
            UpdateSize(s);
        }

        static bool IsDefault(int[] seq, int n)
        {
            if (seq.Length != n) return false;
            for (int i = 0; i < n; i++) if (seq[i] != i) return false;
            return true;
        }

        static void UpdateSize(SpriteObj s)
        {
            if (Gfx.Swaps(s.Trans)) { s.W = s.FrameH; s.H = s.FrameW; }
            else { s.W = s.FrameW; s.H = s.FrameH; }
        }

        static void TP(SpriteObj s, int x, int y, int t, out int dx, out int dy) => Gfx.TransformPoint(t, x, y, s.FrameW, s.FrameH, out dx, out dy);

        static void TransRect(SpriteObj s, out int x, out int y, out int w, out int h)
        {
            int ax, ay, bx, by;
            TP(s, s.Cx, s.Cy, s.Trans, out ax, out ay);
            TP(s, s.Cx + s.Cw - 1, s.Cy + s.Ch - 1, s.Trans, out bx, out by);
            x = s.X + Math.Min(ax, bx); y = s.Y + Math.Min(ay, by);
            w = Math.Abs(bx - ax) + 1; h = Math.Abs(by - ay) + 1;
            if (s.Cw <= 0 || s.Ch <= 0) { w = 0; h = 0; }
        }

        static bool SpriteOpaque(SpriteObj s, int wx, int wy)
        {
            int tx = wx - s.X, ty = wy - s.Y;
            int w = s.FrameW, h = s.FrameH, ox, oy;
            switch (s.Trans)
            {
                case 0: ox = tx; oy = ty; break;
                case 2: ox = w - 1 - tx; oy = ty; break;
                case 1: ox = tx; oy = h - 1 - ty; break;
                case 3: ox = w - 1 - tx; oy = h - 1 - ty; break;
                case 5: ox = ty; oy = h - 1 - tx; break;
                case 7: ox = w - 1 - ty; oy = h - 1 - tx; break;
                case 6: ox = w - 1 - ty; oy = tx; break;
                default: ox = ty; oy = tx; break;
            }
            if (ox < 0 || oy < 0 || ox >= w || oy >= h) return false;
            if (ox < s.Cx || oy < s.Cy || ox >= s.Cx + s.Cw || oy >= s.Cy + s.Ch) return false;
            int f = s.Seq[s.SeqIdx];
            int sx = (f % s.Cols) * w + ox, sy = (f / s.Cols) * h + oy;
            return ((uint)s.Img.S.Pix[sy * s.Img.S.W + sx] >> 24) != 0;
        }

        static int TileAt(TiledObj tl, int wx, int wy)
        {
            int cx = (wx - tl.X) / tl.TileW, cy = (wy - tl.Y) / tl.TileH;
            if (wx < tl.X || wy < tl.Y || cx >= tl.Cols || cy >= tl.Rows) return 0;
            int v = tl.Cells[cy * tl.Cols + cx];
            if (v < 0) v = -v - 1 < tl.Anim.Count ? tl.Anim[-v - 1] : 0;
            return v;
        }

        static bool TileOpaque(TiledObj tl, int wx, int wy)
        {
            int v = TileAt(tl, wx, wy);
            if (v <= 0) return false;
            int lx = (wx - tl.X) % tl.TileW, ly = (wy - tl.Y) % tl.TileH;
            int sx = ((v - 1) % tl.ImgCols) * tl.TileW + lx, sy = ((v - 1) / tl.ImgCols) * tl.TileH + ly;
            return ((uint)tl.Img.S.Pix[sy * tl.Img.S.W + sx] >> 24) != 0;
        }

        static bool Intersect(ref int x0, ref int y0, ref int x1, ref int y1, int bx, int by, int bw, int bh)
        {
            x0 = Math.Max(x0, bx); y0 = Math.Max(y0, by);
            x1 = Math.Min(x1, bx + bw); y1 = Math.Min(y1, by + bh);
            return x0 < x1 && y0 < y1;
        }

        static bool Collide(SpriteObj s, object other, int ix, int iy, bool pixel)
        {
            if (!s.Visible) return false;
            int sx, sy, sw, sh;
            TransRect(s, out sx, out sy, out sw, out sh);
            int x0 = sx, y0 = sy, x1 = sx + sw, y1 = sy + sh;
            var o = other as SpriteObj;
            if (o != null)
            {
                if (!o.Visible) return false;
                int ox, oy, ow, oh;
                TransRect(o, out ox, out oy, out ow, out oh);
                if (!Intersect(ref x0, ref y0, ref x1, ref y1, ox, oy, ow, oh)) return false;
                if (!pixel) return true;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        if (SpriteOpaque(s, x, y) && SpriteOpaque(o, x, y)) return true;
                return false;
            }
            var tl = other as TiledObj;
            if (tl != null)
            {
                if (!tl.Visible) return false;
                if (!Intersect(ref x0, ref y0, ref x1, ref y1, tl.X, tl.Y, tl.W, tl.H)) return false;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (TileAt(tl, x, y) == 0) continue;
                        if (!pixel) return true;
                        if (SpriteOpaque(s, x, y) && TileOpaque(tl, x, y)) return true;
                    }
                return false;
            }
            var img = (ImageObj)other;
            if (!Intersect(ref x0, ref y0, ref x1, ref y1, ix, iy, img.S.W, img.S.H)) return false;
            if (!pixel) return true;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (SpriteOpaque(s, x, y) && ((uint)img.S.Pix[(y - iy) * img.S.W + (x - ix)] >> 24) != 0) return true;
            return false;
        }

        static void PaintLayer(JThread t, LayerObj l, GraphicsObj g)
        {
            t.Call(t.Virtual(l, "paint(L" + P + "Graphics;)V"), l, g);
        }

        public static void PaintSprite(SpriteObj s, GraphicsObj g)
        {
            if (!s.Visible || s.Img == null) return;
            int f = s.Seq[s.SeqIdx];
            int sx = (f % s.Cols) * s.FrameW, sy = (f / s.Cols) * s.FrameH;
            Gfx.DrawRegion(g, s.Img.S, sx, sy, s.FrameW, s.FrameH, s.Trans, s.X, s.Y, Gfx.TOP | Gfx.LEFT);
        }

        public static void PaintTiled(TiledObj tl, GraphicsObj g)
        {
            if (!tl.Visible) return;
            int c0 = Math.Max(0, (g.Cx0 - g.Tx - tl.X) / tl.TileW), r0 = Math.Max(0, (g.Cy0 - g.Ty - tl.Y) / tl.TileH);
            int c1 = Math.Min(tl.Cols - 1, (g.Cx1 - g.Tx - tl.X) / tl.TileW), r1 = Math.Min(tl.Rows - 1, (g.Cy1 - g.Ty - tl.Y) / tl.TileH);
            var src = tl.Img.S;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int v = tl.Cells[r * tl.Cols + c];
                    if (v < 0) v = -v - 1 < tl.Anim.Count ? tl.Anim[-v - 1] : 0;
                    if (v <= 0) continue;
                    int sx = ((v - 1) % tl.ImgCols) * tl.TileW, sy = ((v - 1) / tl.ImgCols) * tl.TileH;
                    Gfx.Blit(g, src, sx, sy, tl.TileW, tl.TileH, tl.X + c * tl.TileW + g.Tx, tl.Y + r * tl.TileH + g.Ty, true);
                }
        }

        static void SetTiles(TiledObj tl, ImageObj img, int tw, int th)
        {
            if (img == null) throw new NullReferenceException();
            if (tw <= 0 || th <= 0 || img.S.W % tw != 0 || img.S.H % th != 0) throw new JavaThrow("java/lang/IllegalArgumentException", "tile size");
            int n = (img.S.W / tw) * (img.S.H / th);
            bool keep = tl.Img != null && n >= tl.NumTiles;
            tl.Img = img; tl.TileW = tw; tl.TileH = th; tl.ImgCols = img.S.W / tw; tl.NumTiles = n;
            tl.W = tl.Cols * tw; tl.H = tl.Rows * th;
            if (!keep) { if (tl.Cells != null) Array.Clear(tl.Cells, 0, tl.Cells.Length); tl.Anim.Clear(); }
        }

        static int Cell(TiledObj tl, int c, int r)
        {
            if (c < 0 || r < 0 || c >= tl.Cols || r >= tl.Rows) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            return r * tl.Cols + c;
        }

        static void CheckTile(TiledObj tl, int v)
        {
            if (v > tl.NumTiles || (v < 0 && -v > tl.Anim.Count)) throw new JavaThrow("java/lang/IndexOutOfBoundsException", "tile " + v);
        }

        public static void Register(NativeRegistry n)
        {
            n.Define(G + "GameCanvas", P + "Canvas", c => c
                .M("<init>", "(Z)V", (t, b) =>
                {
                    var cv = (CanvasObj)t.R[b];
                    var rt = R(t);
                    cv.IsGame = true; cv.Suppress = t.Z(b + 1);
                    cv.Back = new Surface(rt.W, rt.H, -1);
                })
                .M("getGraphics", "()L" + P + "Graphics;", (t, b) => t.RetA(b, Lcdui.NewGraphics(t, ((CanvasObj)t.R[b]).Back)))
                .M("getKeyStates", "()I", (t, b) => { var cv = (CanvasObj)t.R[b]; t.RetI(b, R(t).Current == cv ? R(t).GameKeyStates() : 0); })
                .M("flushGraphics", "()V", (t, b) => R(t).Flush((CanvasObj)t.R[b], 0, 0, R(t).W, R(t).H))
                .M("flushGraphics", "(IIII)V", (t, b) => { if (t.I(b + 3) > 0 && t.I(b + 4) > 0) R(t).Flush((CanvasObj)t.R[b], t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)); })
                .M("paint", "(L" + P + "Graphics;)V", (t, b) =>
                {
                    var cv = (CanvasObj)t.R[b];
                    var g = (GraphicsObj)t.R[b + 1];
                    Gfx.Blit(g, cv.Back, 0, 0, cv.Back.W, cv.Back.H, g.Tx, g.Ty, false);
                }), null, access: 0x0401);

            n.Define(G + "Layer", "java/lang/Object", c => c
                .M("getX", "()I", (t, b) => t.RetI(b, L(t, b).X))
                .M("getY", "()I", (t, b) => t.RetI(b, L(t, b).Y))
                .M("getWidth", "()I", (t, b) => t.RetI(b, L(t, b).W))
                .M("getHeight", "()I", (t, b) => t.RetI(b, L(t, b).H))
                .M("isVisible", "()Z", (t, b) => t.RetZ(b, L(t, b).Visible))
                .M("setVisible", "(Z)V", (t, b) => L(t, b).Visible = t.Z(b + 1))
                .M("move", "(II)V", (t, b) => { L(t, b).X += t.I(b + 1); L(t, b).Y += t.I(b + 2); })
                .M("setPosition", "(II)V", (t, b) => { L(t, b).X = t.I(b + 1); L(t, b).Y = t.I(b + 2); })
                .Abstract("paint", "(L" + P + "Graphics;)V"),
                Rt.F<LayerObj>(), access: 0x0401);

            n.Define(G + "Sprite", G + "Layer", c => c
                .M("<init>", "(L" + P + "Image;)V", (t, b) => { var img = (ImageObj)t.R[b + 1] ?? throw new NullReferenceException(); SetFrames(Sp(t, b), img, img.S.W, img.S.H); })
                .M("<init>", "(L" + P + "Image;II)V", (t, b) => SetFrames(Sp(t, b), (ImageObj)t.R[b + 1], t.I(b + 2), t.I(b + 3)))
                .M("<init>", "(L" + G + "Sprite;)V", (t, b) =>
                {
                    var s = Sp(t, b); var o = (SpriteObj)t.R[b + 1] ?? throw new NullReferenceException();
                    s.Img = o.Img; s.FrameW = o.FrameW; s.FrameH = o.FrameH; s.Cols = o.Cols; s.Frames = o.Frames;
                    s.Seq = (int[])o.Seq.Clone(); s.SeqIdx = o.SeqIdx; s.RefX = o.RefX; s.RefY = o.RefY; s.Trans = o.Trans;
                    s.Cx = o.Cx; s.Cy = o.Cy; s.Cw = o.Cw; s.Ch = o.Ch; s.X = o.X; s.Y = o.Y; s.W = o.W; s.H = o.H; s.Visible = o.Visible;
                })
                .M("setImage", "(L" + P + "Image;II)V", (t, b) =>
                {
                    var s = Sp(t, b);
                    int rx = s.X, ry = s.Y, ox, oy;
                    TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy);
                    int refWX = rx + ox, refWY = ry + oy;
                    SetFrames(s, (ImageObj)t.R[b + 1], t.I(b + 2), t.I(b + 3));
                    TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy);
                    s.X = refWX - ox; s.Y = refWY - oy;
                })
                .M("defineReferencePixel", "(II)V", (t, b) => { Sp(t, b).RefX = t.I(b + 1); Sp(t, b).RefY = t.I(b + 2); })
                .M("setRefPixelPosition", "(II)V", (t, b) =>
                {
                    var s = Sp(t, b); int ox, oy;
                    TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy);
                    s.X = t.I(b + 1) - ox; s.Y = t.I(b + 2) - oy;
                })
                .M("getRefPixelX", "()I", (t, b) => { var s = Sp(t, b); int ox, oy; TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy); t.RetI(b, s.X + ox); })
                .M("getRefPixelY", "()I", (t, b) => { var s = Sp(t, b); int ox, oy; TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy); t.RetI(b, s.Y + oy); })
                .M("setFrame", "(I)V", (t, b) => { var s = Sp(t, b); int i = t.I(b + 1); if (i < 0 || i >= s.Seq.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); s.SeqIdx = i; })
                .M("getFrame", "()I", (t, b) => t.RetI(b, Sp(t, b).SeqIdx))
                .M("getRawFrameCount", "()I", (t, b) => t.RetI(b, Sp(t, b).Frames))
                .M("getFrameSequenceLength", "()I", (t, b) => t.RetI(b, Sp(t, b).Seq.Length))
                .M("nextFrame", "()V", (t, b) => { var s = Sp(t, b); s.SeqIdx = (s.SeqIdx + 1) % s.Seq.Length; })
                .M("prevFrame", "()V", (t, b) => { var s = Sp(t, b); s.SeqIdx = (s.SeqIdx + s.Seq.Length - 1) % s.Seq.Length; })
                .M("setFrameSequence", "([I)V", (t, b) =>
                {
                    var s = Sp(t, b); var seq = (int[])t.R[b + 1];
                    if (seq == null) { s.Seq = new int[s.Frames]; for (int i = 0; i < s.Frames; i++) s.Seq[i] = i; s.SeqIdx = 0; return; }
                    if (seq.Length == 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    foreach (var f in seq) if (f < 0 || f >= s.Frames) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    s.Seq = (int[])seq.Clone(); s.SeqIdx = 0;
                })
                .M("setTransform", "(I)V", (t, b) =>
                {
                    var s = Sp(t, b); int tr = t.I(b + 1);
                    if (tr < 0 || tr > 7) throw new JavaThrow("java/lang/IllegalArgumentException");
                    int ox, oy;
                    TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy);
                    int wx = s.X + ox, wy = s.Y + oy;
                    s.Trans = tr;
                    UpdateSize(s);
                    TP(s, s.RefX, s.RefY, s.Trans, out ox, out oy);
                    s.X = wx - ox; s.Y = wy - oy;
                })
                .M("defineCollisionRectangle", "(IIII)V", (t, b) =>
                {
                    var s = Sp(t, b);
                    if (t.I(b + 3) < 0 || t.I(b + 4) < 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    s.Cx = t.I(b + 1); s.Cy = t.I(b + 2); s.Cw = t.I(b + 3); s.Ch = t.I(b + 4);
                })
                .M("collidesWith", "(L" + G + "Sprite;Z)Z", (t, b) => t.RetZ(b, Collide(Sp(t, b), t.R[b + 1] ?? throw new NullReferenceException(), 0, 0, t.Z(b + 2))))
                .M("collidesWith", "(L" + G + "TiledLayer;Z)Z", (t, b) => t.RetZ(b, Collide(Sp(t, b), t.R[b + 1] ?? throw new NullReferenceException(), 0, 0, t.Z(b + 2))))
                .M("collidesWith", "(L" + P + "Image;IIZ)Z", (t, b) => t.RetZ(b, Collide(Sp(t, b), t.R[b + 1] ?? throw new NullReferenceException(), t.I(b + 2), t.I(b + 3), t.Z(b + 4))))
                .M("paint", "(L" + P + "Graphics;)V", (t, b) => PaintSprite(Sp(t, b), (GraphicsObj)t.R[b + 1] ?? throw new NullReferenceException())),
                Rt.F<SpriteObj>());

            n.Define(G + "TiledLayer", G + "Layer", c => c
                .M("<init>", "(IIL" + P + "Image;II)V", (t, b) =>
                {
                    var tl = Tl(t, b);
                    tl.Cols = t.I(b + 1); tl.Rows = t.I(b + 2);
                    if (tl.Cols < 1 || tl.Rows < 1) throw new JavaThrow("java/lang/IllegalArgumentException");
                    tl.Cells = new int[tl.Cols * tl.Rows];
                    SetTiles(tl, (ImageObj)t.R[b + 3], t.I(b + 4), t.I(b + 5));
                })
                .M("createAnimatedTile", "(I)I", (t, b) => { var tl = Tl(t, b); int v = t.I(b + 1); if (v < 0 || v > tl.NumTiles) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); tl.Anim.Add(v); t.RetI(b, -tl.Anim.Count); })
                .M("setAnimatedTile", "(II)V", (t, b) =>
                {
                    var tl = Tl(t, b); int i = -t.I(b + 1) - 1, v = t.I(b + 2);
                    if (i < 0 || i >= tl.Anim.Count || v < 0 || v > tl.NumTiles) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    tl.Anim[i] = v;
                })
                .M("getAnimatedTile", "(I)I", (t, b) => { var tl = Tl(t, b); int i = -t.I(b + 1) - 1; if (i < 0 || i >= tl.Anim.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetI(b, tl.Anim[i]); })
                .M("setCell", "(III)V", (t, b) => { var tl = Tl(t, b); CheckTile(tl, t.I(b + 3)); tl.Cells[Cell(tl, t.I(b + 1), t.I(b + 2))] = t.I(b + 3); })
                .M("getCell", "(II)I", (t, b) => { var tl = Tl(t, b); t.RetI(b, tl.Cells[Cell(tl, t.I(b + 1), t.I(b + 2))]); })
                .M("fillCells", "(IIIII)V", (t, b) =>
                {
                    var tl = Tl(t, b); int c0 = t.I(b + 1), r0 = t.I(b + 2), nc = t.I(b + 3), nr = t.I(b + 4), v = t.I(b + 5);
                    if (nc < 0 || nr < 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    if (c0 < 0 || r0 < 0 || c0 + nc > tl.Cols || r0 + nr > tl.Rows) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    CheckTile(tl, v);
                    for (int r = r0; r < r0 + nr; r++) for (int cc = c0; cc < c0 + nc; cc++) tl.Cells[r * tl.Cols + cc] = v;
                })
                .M("getCellWidth", "()I", (t, b) => t.RetI(b, Tl(t, b).TileW))
                .M("getCellHeight", "()I", (t, b) => t.RetI(b, Tl(t, b).TileH))
                .M("getColumns", "()I", (t, b) => t.RetI(b, Tl(t, b).Cols))
                .M("getRows", "()I", (t, b) => t.RetI(b, Tl(t, b).Rows))
                .M("setStaticTileSet", "(L" + P + "Image;II)V", (t, b) => SetTiles(Tl(t, b), (ImageObj)t.R[b + 1], t.I(b + 2), t.I(b + 3)))
                .M("paint", "(L" + P + "Graphics;)V", (t, b) => PaintTiled(Tl(t, b), (GraphicsObj)t.R[b + 1] ?? throw new NullReferenceException())),
                Rt.F<TiledObj>(), access: 0x0011);

            n.Define(G + "LayerManager", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("append", "(L" + G + "Layer;)V", (t, b) =>
                {
                    var lm = (LayerManagerObj)t.R[b]; var l = (LayerObj)t.R[b + 1] ?? throw new NullReferenceException();
                    lock (lm.Layers) { lm.Layers.Remove(l); lm.Layers.Add(l); }
                })
                .M("insert", "(L" + G + "Layer;I)V", (t, b) =>
                {
                    var lm = (LayerManagerObj)t.R[b]; var l = (LayerObj)t.R[b + 1] ?? throw new NullReferenceException(); int i = t.I(b + 2);
                    lock (lm.Layers)
                    {
                        int old = lm.Layers.IndexOf(l);
                        if (old >= 0) { lm.Layers.RemoveAt(old); if (old < i) i--; }
                        if (i < 0 || i > lm.Layers.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                        lm.Layers.Insert(i, l);
                    }
                })
                .M("remove", "(L" + G + "Layer;)V", (t, b) => { var lm = (LayerManagerObj)t.R[b]; if (t.R[b + 1] == null) throw new NullReferenceException(); lock (lm.Layers) lm.Layers.Remove((LayerObj)t.R[b + 1]); })
                .M("getLayerAt", "(I)L" + G + "Layer;", (t, b) =>
                {
                    var lm = (LayerManagerObj)t.R[b]; int i = t.I(b + 1);
                    lock (lm.Layers) { if (i < 0 || i >= lm.Layers.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, lm.Layers[i]); }
                })
                .M("getSize", "()I", (t, b) => t.RetI(b, ((LayerManagerObj)t.R[b]).Layers.Count))
                .M("setViewWindow", "(IIII)V", (t, b) =>
                {
                    var lm = (LayerManagerObj)t.R[b];
                    if (t.I(b + 3) < 0 || t.I(b + 4) < 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    lm.Vx = t.I(b + 1); lm.Vy = t.I(b + 2); lm.Vw = t.I(b + 3); lm.Vh = t.I(b + 4);
                })
                .M("paint", "(L" + P + "Graphics;II)V", (t, b) =>
                {
                    var lm = (LayerManagerObj)t.R[b]; var g = (GraphicsObj)t.R[b + 1] ?? throw new NullReferenceException();
                    int x = t.I(b + 2), y = t.I(b + 3);
                    int ocx0 = g.Cx0, ocy0 = g.Cy0, ocx1 = g.Cx1, ocy1 = g.Cy1, otx = g.Tx, oty = g.Ty;
                    g.ClipRect(x, y, (int)Math.Min(lm.Vw, int.MaxValue / 2), (int)Math.Min(lm.Vh, int.MaxValue / 2));
                    g.Tx += x - lm.Vx; g.Ty += y - lm.Vy;
                    LayerObj[] ls;
                    lock (lm.Layers) ls = lm.Layers.ToArray();
                    try
                    {
                        for (int i = ls.Length - 1; i >= 0; i--)
                            if (ls[i].Visible) PaintLayer(t, ls[i], g);
                    }
                    finally
                    {
                        g.Cx0 = ocx0; g.Cy0 = ocy0; g.Cx1 = ocx1; g.Cy1 = ocy1; g.Tx = otx; g.Ty = oty;
                    }
                }),
                Rt.F<LayerManagerObj>());
        }
    }
}
