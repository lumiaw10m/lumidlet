using System;
using System.Collections.Generic;
using Lumidlet.Midp;
using Lumidlet.Vm;

namespace Lumidlet.M3g
{
    public sealed class Graphics3DObj : JObject
    {
        public readonly Renderer R = new Renderer();
    }

    public struct LightRef
    {
        public LightObj L;
        public float[] M; // light -> world
    }

    // Software implementation of the JSR-184 immediate and retained mode pipeline.
    public sealed class Renderer
    {
        // ---- target ----
        public object Bound;          // GraphicsObj or Image2DObj
        Surface surf;
        int ox, oy, clx0, cly0, clx1, cly1;
        bool depthOn, opaqueTarget;
        float[] zbuf;
        public int Vx, Vy, Vw, Vh;
        public float DNear = 0, DFar = 1;
        public int Hints;
        public bool DepthEnabled => depthOn;

        // ---- camera / lights ----
        public CameraObj Camera;
        public float[] CamM = Mat.Identity();
        float[] view = Mat.Identity(), proj = Mat.Identity();
        public readonly List<LightRef> Lights = new List<LightRef>();

        public void Bind(object target, bool depth)
        {
            if (Bound != null) throw new JavaThrow("java/lang/IllegalStateException", "target already bound");
            var g = target as GraphicsObj;
            if (g != null)
            {
                surf = g.S; ox = g.Tx; oy = g.Ty;
                clx0 = g.Cx0; cly0 = g.Cy0; clx1 = g.Cx1; cly1 = g.Cy1;
                opaqueTarget = true;
                Vx = g.Cx0 - g.Tx; Vy = g.Cy0 - g.Ty; Vw = g.Cx1 - g.Cx0; Vh = g.Cy1 - g.Cy0;
            }
            else
            {
                var im = target as Image2DObj;
                if (im == null) throw new JavaThrow("java/lang/IllegalArgumentException", "unsupported target");
                if (!im.Mutable || (im.Format != Image2DObj.RGB && im.Format != Image2DObj.RGBA)) throw new JavaThrow("java/lang/IllegalArgumentException", "image target");
                surf = new Surface(im.W, im.H, im.Pix, im.Format == Image2DObj.RGBA);
                ox = oy = 0; clx0 = cly0 = 0; clx1 = im.W; cly1 = im.H;
                opaqueTarget = im.Format == Image2DObj.RGB;
                Vx = 0; Vy = 0; Vw = im.W; Vh = im.H;
            }
            Bound = target;
            depthOn = depth;
            int n = surf.W * surf.H;
            if (depth && (zbuf == null || zbuf.Length < n)) zbuf = new float[n];
        }

        public void Release()
        {
            if (Bound == null) throw new JavaThrow("java/lang/IllegalStateException", "no target bound");
            var im = Bound as Image2DObj;
            if (im != null) im.Version++;
            Bound = null; surf = null;
        }

        void NeedTarget() { if (Bound == null) throw new JavaThrow("java/lang/IllegalStateException", "no target bound"); }

        public void SetCamera(CameraObj c, float[] m)
        {
            Camera = c;
            CamM = m != null ? (float[])m.Clone() : Mat.Identity();
        }

        void Setup()
        {
            if (Camera == null) throw new JavaThrow("java/lang/IllegalStateException", "no camera");
            if (!Mat.Invert(CamM, view)) throw new JavaThrow("java/lang/ArithmeticException", "camera transform not invertible");
            proj = Camera.Projection();
        }

        // ---------------- clear ----------------

        public void Clear(BackgroundObj bg)
        {
            NeedTarget();
            int x0 = Math.Max(clx0, ox + Vx), y0 = Math.Max(cly0, oy + Vy);
            int x1 = Math.Min(clx1, ox + Vx + Vw), y1 = Math.Min(cly1, oy + Vy + Vh);
            if (x1 <= x0 || y1 <= y0) return;
            bool cc = bg == null || bg.ColorClear, dc = bg == null || bg.DepthClear;
            int W = surf.W;
            var pix = surf.Pix;
            if (cc)
            {
                int col = bg == null ? 0 : bg.Color;
                if (opaqueTarget) col |= unchecked((int)0xFF000000);
                var img = bg?.Image;
                if (img == null)
                {
                    for (int y = y0; y < y1; y++) { int r = y * W; for (int x = x0; x < x1; x++) pix[r + x] = col; }
                }
                else
                {
                    int cw = bg.Cw, ch = bg.Ch;
                    for (int y = y0; y < y1; y++)
                    {
                        float fy = (y + 0.5f - oy - Vy) / Vh;
                        int iy = bg.Cy + (int)Math.Floor(fy * ch);
                        bool outY = iy < 0 || iy >= img.H;
                        if (bg.ModeY == 33) { iy %= img.H; if (iy < 0) iy += img.H; outY = false; }
                        int r = y * W;
                        for (int x = x0; x < x1; x++)
                        {
                            float fx = (x + 0.5f - ox - Vx) / Vw;
                            int ix = bg.Cx + (int)Math.Floor(fx * cw);
                            bool outX = ix < 0 || ix >= img.W;
                            if (bg.ModeX == 33) { ix %= img.W; if (ix < 0) ix += img.W; outX = false; }
                            int c = (outX || outY) ? col : img.Pix[iy * img.W + ix];
                            if (opaqueTarget)
                            {
                                uint a = (uint)c >> 24;
                                if (a != 255 && !(outX || outY)) c = Blend(pix[r + x], c, (int)a);
                                c |= unchecked((int)0xFF000000);
                            }
                            pix[r + x] = c;
                        }
                    }
                }
            }
            if (dc && depthOn)
                for (int y = y0; y < y1; y++) { int r = y * W; for (int x = x0; x < x1; x++) zbuf[r + x] = 1f; }
        }

        static int Blend(int d, int s, int a)
        {
            int ia = 255 - a;
            int r = (((s >> 16) & 255) * a + ((d >> 16) & 255) * ia) / 255;
            int g = (((s >> 8) & 255) * a + ((d >> 8) & 255) * ia) / 255;
            int b = ((s & 255) * a + (d & 255) * ia) / 255;
            return (r << 16) | (g << 8) | b;
        }

        // ---------------- retained mode ----------------

        struct Item
        {
            public NodeObj Node;
            public int Sub;
            public float[] M;
            public float Alpha;
            public int Layer, Order;
            public bool Blended;
        }

        public void RenderWorld(WorldObj w)
        {
            NeedTarget();
            var cam = w.Camera;
            if (cam == null) throw new JavaThrow("java/lang/IllegalStateException", "world has no active camera");
            var cm = PathTo(cam, w);
            if (cm == null) throw new JavaThrow("java/lang/IllegalStateException", "camera not in world");
            Clear(w.Background);
            var savedCam = Camera; var savedM = CamM;
            var savedLights = new List<LightRef>(Lights);
            Camera = cam; CamM = cm;
            Lights.Clear();
            CollectLights(w, Mat.Identity());
            try { RenderNode(w, Mat.Identity()); }
            finally
            {
                Camera = savedCam; CamM = savedM;
                Lights.Clear(); Lights.AddRange(savedLights);
            }
        }

        // transform from node to ancestor's coordinate system, or null
        public static float[] PathTo(NodeObj n, NodeObj ancestor)
        {
            var m = Mat.Identity();
            for (var p = n; p != null; p = p.Parent)
            {
                if (p == ancestor) return m;
                m = Mat.Mul(p.Composite(), m);
            }
            return null;
        }

        void CollectLights(NodeObj n, float[] parent)
        {
            if (!n.Render) return;
            var m = Mat.Mul(parent, n.Composite());
            var l = n as LightObj;
            if (l != null) Lights.Add(new LightRef { L = l, M = m });
            var g = n as GroupObj;
            if (g != null) foreach (var c in g.Children) CollectLights(c, m);
        }

        public void RenderNode(NodeObj node, float[] m)
        {
            NeedTarget();
            Setup();
            var items = new List<Item>();
            Collect(node, m ?? Mat.Identity(), 1f, items, node is WorldObj);
            for (int i = 0; i < items.Count; i++) { var it = items[i]; it.Order = i; items[i] = it; }
            items.Sort((a, b) =>
            {
                if (a.Layer != b.Layer) return a.Layer.CompareTo(b.Layer);
                if (a.Blended != b.Blended) return a.Blended ? 1 : -1;
                return a.Order.CompareTo(b.Order);
            });
            foreach (var it in items)
            {
                var sp = it.Node as Sprite3DObj;
                if (sp != null) DrawSprite(sp, it.M, it.Alpha);
                else
                {
                    var mesh = (MeshObj)it.Node;
                    var vd = Prepare(mesh);
                    if (vd != null) DrawSubmesh(vd, mesh.Ib[it.Sub], mesh.Ap[it.Sub], it.M, it.Alpha, mesh.Scope);
                }
            }
        }

        void Collect(NodeObj n, float[] parent, float alpha, List<Item> items, bool isRootWorld)
        {
            if (!n.Render) return;
            var m = isRootWorld ? parent : Mat.Mul(parent, n.Composite());
            alpha *= n.Alpha;
            if ((n.Scope & Camera.Scope) == 0 && !(n is GroupObj)) return;
            var mesh = n as MeshObj;
            if (mesh != null)
            {
                for (int i = 0; i < mesh.Ib.Length; i++)
                {
                    var ap = mesh.Ap[i];
                    if (ap == null || mesh.Ib[i] == null) continue;
                    items.Add(new Item { Node = mesh, Sub = i, M = m, Alpha = alpha, Layer = ap.Layer, Blended = ap.Comp != null && ap.Comp.Blending != CompositingObj.REPLACE });
                }
                var sk = mesh as SkinnedMeshObj;
                if (sk?.Skeleton != null) Collect(sk.Skeleton, m, alpha, items, false);
                return;
            }
            var sp = n as Sprite3DObj;
            if (sp != null)
            {
                if (sp.Ap != null && sp.Image != null)
                    items.Add(new Item { Node = sp, M = m, Alpha = alpha, Layer = sp.Ap.Layer, Blended = sp.Ap.Comp != null && sp.Ap.Comp.Blending != CompositingObj.REPLACE });
                return;
            }
            var g = n as GroupObj;
            if (g != null) foreach (var c in g.Children) Collect(c, m, alpha, items, false);
        }

        // ---------------- vertex data ----------------

        public sealed class VData
        {
            public int N;
            public float[] Pos, Norm;
            public int[] Col;          // ARGB per vertex, or null for default color
            public int DefaultColor;
            public float[][] Tc = new float[2][];
        }

        static float[] Positions(VertexBufferObj vb)
        {
            var va = vb.Pos;
            if (va == null) return null;
            int n = va.Count, c = va.Comps;
            var r = new float[n * 3];
            float s = vb.PosScale, bx = vb.PosBias[0], by = vb.PosBias[1], bz = vb.PosBias[2];
            for (int i = 0; i < n; i++)
            {
                r[i * 3] = va.D[i * c] * s + bx;
                r[i * 3 + 1] = (c > 1 ? va.D[i * c + 1] : 0) * s + by;
                r[i * 3 + 2] = (c > 2 ? va.D[i * c + 2] : 0) * s + bz;
            }
            return r;
        }

        static float[] Normals(VertexBufferObj vb)
        {
            var va = vb.Norm;
            if (va == null) return null;
            int n = va.Count;
            float k = va.Size == 1 ? 1f / 127 : 1f / 32767;
            var r = new float[n * 3];
            for (int i = 0; i < n * 3; i++) r[i] = va.D[i] * k;
            return r;
        }

        static int[] Colors(VertexBufferObj vb)
        {
            var va = vb.Col;
            if (va == null) return null;
            int n = va.Count, c = va.Comps;
            var r = new int[n];
            for (int i = 0; i < n; i++)
            {
                int rr = va.D[i * c] & 255, gg = va.D[i * c + 1] & 255, bb = va.D[i * c + 2] & 255;
                int aa = c > 3 ? va.D[i * c + 3] & 255 : 255;
                r[i] = (aa << 24) | (rr << 16) | (gg << 8) | bb;
            }
            return r;
        }

        static float[] TexCoords(VertexBufferObj vb, int u)
        {
            var va = vb.Tc[u];
            if (va == null) return null;
            int n = va.Count, c = va.Comps;
            var r = new float[n * 3];
            float s = vb.TcScale[u];
            var b = vb.TcBias[u];
            for (int i = 0; i < n; i++)
            {
                r[i * 3] = va.D[i * c] * s + b[0];
                r[i * 3 + 1] = (c > 1 ? va.D[i * c + 1] : 0) * s + b[1];
                r[i * 3 + 2] = (c > 2 ? va.D[i * c + 2] : 0) * s + b[2];
            }
            return r;
        }

        public static VData Base(VertexBufferObj vb)
        {
            var d = new VData { N = vb.VertexCount, Pos = Positions(vb), Norm = Normals(vb), Col = Colors(vb), DefaultColor = vb.DefaultColor };
            d.Tc[0] = TexCoords(vb, 0); d.Tc[1] = TexCoords(vb, 1);
            return d;
        }

        public static VData Prepare(MeshObj mesh)
        {
            var vb = mesh.Vb;
            if (vb == null || vb.Pos == null) return null;
            var d = Base(vb);
            var mm = mesh as MorphingMeshObj;
            if (mm != null && mm.Targets.Length > 0) Morph(d, mm);
            var sk = mesh as SkinnedMeshObj;
            if (sk != null && sk.Bones.Count > 0) Skin(d, sk);
            return d;
        }

        static void Morph(VData d, MorphingMeshObj mm)
        {
            var bp = (float[])d.Pos.Clone();
            var bn = d.Norm != null ? (float[])d.Norm.Clone() : null;
            int[] bc = d.Col;
            float[] cacc = null;
            if (bc != null) { cacc = new float[d.N * 4]; for (int i = 0; i < d.N; i++) { int c = bc[i]; cacc[i * 4] = (c >> 16) & 255; cacc[i * 4 + 1] = (c >> 8) & 255; cacc[i * 4 + 2] = c & 255; cacc[i * 4 + 3] = (c >> 24) & 255; } }
            for (int k = 0; k < mm.Targets.Length; k++)
            {
                float w = k < mm.Weights.Length ? mm.Weights[k] : 0;
                if (w == 0) continue;
                var t = mm.Targets[k];
                var tp = Positions(t);
                if (tp != null) for (int i = 0; i < Math.Min(tp.Length, bp.Length); i++) d.Pos[i] += w * (tp[i] - bp[i]);
                var tn = Normals(t);
                if (tn != null && bn != null) for (int i = 0; i < Math.Min(tn.Length, bn.Length); i++) d.Norm[i] += w * (tn[i] - bn[i]);
                var tc = Colors(t);
                if (tc != null && cacc != null)
                    for (int i = 0; i < Math.Min(tc.Length, d.N); i++)
                    {
                        int c = tc[i], b = bc[i];
                        cacc[i * 4] += w * (((c >> 16) & 255) - ((b >> 16) & 255));
                        cacc[i * 4 + 1] += w * (((c >> 8) & 255) - ((b >> 8) & 255));
                        cacc[i * 4 + 2] += w * ((c & 255) - (b & 255));
                        cacc[i * 4 + 3] += w * (((c >> 24) & 255) - ((b >> 24) & 255));
                    }
            }
            if (cacc != null)
            {
                d.Col = new int[d.N];
                for (int i = 0; i < d.N; i++)
                    d.Col[i] = (Clamp255(cacc[i * 4 + 3]) << 24) | (Clamp255(cacc[i * 4]) << 16) | (Clamp255(cacc[i * 4 + 1]) << 8) | Clamp255(cacc[i * 4 + 2]);
            }
        }

        static int Clamp255(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f);

        public static void ComputeRest(SkinnedMeshObj sk, Bone b)
        {
            var m = PathTo(b.Node, sk);
            b.RestInv = m != null ? Mat.Inverse(m) : Mat.Identity();
        }

        static void Skin(VData d, SkinnedMeshObj sk)
        {
            int n = d.N;
            var acc = new float[n * 3];
            var nacc = d.Norm != null ? new float[n * 3] : null;
            var wsum = new float[n];
            var tmp = new float[4];
            foreach (var b in sk.Bones)
            {
                if (b.Weight <= 0 || b.Node == null) continue;
                if (b.RestInv == null) ComputeRest(sk, b);
                var bm = PathTo(b.Node, sk);
                if (bm == null) continue;
                var m = Mat.Mul(bm, b.RestInv);
                int end = Math.Min(n, b.First + b.Count);
                float w = b.Weight;
                for (int i = Math.Max(0, b.First); i < end; i++)
                {
                    Mat.Apply(m, d.Pos[i * 3], d.Pos[i * 3 + 1], d.Pos[i * 3 + 2], 1, tmp, 0);
                    acc[i * 3] += w * tmp[0]; acc[i * 3 + 1] += w * tmp[1]; acc[i * 3 + 2] += w * tmp[2];
                    if (nacc != null)
                    {
                        Mat.Apply(m, d.Norm[i * 3], d.Norm[i * 3 + 1], d.Norm[i * 3 + 2], 0, tmp, 0);
                        nacc[i * 3] += w * tmp[0]; nacc[i * 3 + 1] += w * tmp[1]; nacc[i * 3 + 2] += w * tmp[2];
                    }
                    wsum[i] += w;
                }
            }
            var np = (float[])d.Pos.Clone();
            for (int i = 0; i < n; i++)
            {
                if (wsum[i] <= 0) continue;
                float k = 1 / wsum[i];
                np[i * 3] = acc[i * 3] * k; np[i * 3 + 1] = acc[i * 3 + 1] * k; np[i * 3 + 2] = acc[i * 3 + 2] * k;
                if (nacc != null) { d.Norm[i * 3] = nacc[i * 3] * k; d.Norm[i * 3 + 1] = nacc[i * 3 + 1] * k; d.Norm[i * 3 + 2] = nacc[i * 3 + 2] * k; }
            }
            d.Pos = np;
        }

        // ---------------- immediate mode ----------------

        public void RenderImmediate(VertexBufferObj vb, IndexBufferObj ib, AppearanceObj ap, float[] m, int scope)
        {
            NeedTarget();
            if (vb == null || ib == null || ap == null) throw new NullReferenceException();
            Setup();
            if ((scope & Camera.Scope) == 0) return;
            if (vb.Pos == null) return;
            DrawSubmesh(Base(vb), ib, ap, m ?? Mat.Identity(), 1f, scope);
        }

        // ---------------- per-submesh pipeline ----------------

        const int ST = 17; // vertex stride: x y z w | r g b a | s0 t0 s1 t1 | fog | br bg bb ba
        float[] vbuf = new float[0];
        readonly Raster rast = new Raster();

        void DrawSubmesh(VData d, IndexBufferObj ib, AppearanceObj ap, float[] model, float alpha, int scope)
        {
            if (ap == null || ib == null || ib.Tris.Length == 0) return;
            int n = d.N;
            if (ib.MaxIndex >= n) throw new JavaThrow("java/lang/IllegalArgumentException", "index out of range");
            var mv = Mat.Mul(view, model);
            var mvp = Mat.Mul(proj, mv);
            if (vbuf.Length < n * ST) vbuf = new float[n * ST];
            var v = vbuf;
            var mat = ap.Mat;
            var fog = ap.Fog;
            var poly = ap.Poly;
            bool twoSided = poly != null && poly.TwoSided && mat != null;
            var p = d.Pos;
            for (int i = 0; i < n; i++)
            {
                float x = p[i * 3], y = p[i * 3 + 1], z = p[i * 3 + 2];
                int o = i * ST;
                v[o] = mvp[0] * x + mvp[1] * y + mvp[2] * z + mvp[3];
                v[o + 1] = mvp[4] * x + mvp[5] * y + mvp[6] * z + mvp[7];
                v[o + 2] = mvp[8] * x + mvp[9] * y + mvp[10] * z + mvp[11];
                v[o + 3] = mvp[12] * x + mvp[13] * y + mvp[14] * z + mvp[15];
            }
            // colors
            if (mat == null)
            {
                for (int i = 0; i < n; i++)
                {
                    int c = d.Col != null ? d.Col[i] : d.DefaultColor;
                    int o = i * ST;
                    v[o + 4] = ((c >> 16) & 255) / 255f; v[o + 5] = ((c >> 8) & 255) / 255f; v[o + 6] = (c & 255) / 255f;
                    v[o + 7] = (((c >> 24) & 255) / 255f) * alpha;
                }
            }
            else Light(d, mv, mat, poly, alpha, v, twoSided, scope);
            // texture coordinates
            for (int u = 0; u < 2; u++)
            {
                var tex = ap.Tex[u];
                var tc = d.Tc[u];
                if (tex == null || tc == null) continue;
                var tm = tex.Composite();
                bool ident = IsIdentity(tm);
                for (int i = 0; i < n; i++)
                {
                    float s = tc[i * 3], t = tc[i * 3 + 1], r = tc[i * 3 + 2];
                    if (!ident)
                    {
                        float ns = tm[0] * s + tm[1] * t + tm[2] * r + tm[3];
                        float nt = tm[4] * s + tm[5] * t + tm[6] * r + tm[7];
                        float q = tm[12] * s + tm[13] * t + tm[14] * r + tm[15];
                        if (q != 0 && q != 1) { ns /= q; nt /= q; }
                        s = ns; t = nt;
                    }
                    v[i * ST + 8 + u * 2] = s; v[i * ST + 9 + u * 2] = t;
                }
            }
            // fog
            if (fog != null)
            {
                for (int i = 0; i < n; i++)
                {
                    float ez = -(mv[8] * p[i * 3] + mv[9] * p[i * 3 + 1] + mv[10] * p[i * 3 + 2] + mv[11]);
                    float f;
                    if (fog.Mode == FogObj.LINEAR) f = fog.Far == fog.Near ? 1 : (fog.Far - ez) / (fog.Far - fog.Near);
                    else f = (float)Math.Exp(-fog.Density * ez);
                    v[i * ST + 12] = f < 0 ? 0 : f > 1 ? 1 : f;
                }
            }
            rast.Begin(this, ap, false, null);
            int cull = poly?.Culling ?? PolygonObj.CULL_BACK;
            bool ccw = (poly?.Winding ?? PolygonObj.WINDING_CCW) == PolygonObj.WINDING_CCW;
            bool flat = poly != null && poly.Shading == PolygonObj.SHADE_FLAT;
            var tris = ib.Tris;
            for (int k = 0; k + 2 < tris.Length; k += 3)
                Triangle(v, tris[k], tris[k + 1], tris[k + 2], cull, ccw, flat, twoSided);
        }

        static bool IsIdentity(float[] m)
        {
            for (int i = 0; i < 16; i++) if (m[i] != ((i % 5 == 0) ? 1 : 0)) return false;
            return true;
        }

        void Light(VData d, float[] mv, MaterialObj mat, PolygonObj poly, float alpha, float[] v, bool twoSided, int scope)
        {
            int n = d.N;
            // normal matrix = inverse transpose of upper 3x3
            var inv = new float[16];
            if (!Mat.Invert(mv, inv)) Mat.SetIdentity(inv);
            float n00 = inv[0], n01 = inv[4], n02 = inv[8], n10 = inv[1], n11 = inv[5], n12 = inv[9], n20 = inv[2], n21 = inv[6], n22 = inv[10];
            bool local = poly != null && poly.LocalCamera;
            // lights in eye space
            var ls = new List<float[]>(); // type, r,g,b, px,py,pz, dx,dy,dz, c,l,q, cosCut, exp
            float ambR = 0, ambG = 0, ambB = 0;
            foreach (var lr in Lights)
            {
                var l = lr.L;
                if ((l.Scope & scope) == 0) continue;
                float lrr = ((l.Color >> 16) & 255) / 255f * l.Intensity, lgg = ((l.Color >> 8) & 255) / 255f * l.Intensity, lbb = (l.Color & 255) / 255f * l.Intensity;
                if (l.Mode == LightObj.AMBIENT) { ambR += lrr; ambG += lgg; ambB += lbb; continue; }
                var em = Mat.Mul(view, lr.M);
                var pos = new float[4]; var dir = new float[4];
                Mat.Apply(em, 0, 0, 0, 1, pos, 0);
                Mat.Apply(em, 0, 0, -1, 0, dir, 0);
                Norm3(dir);
                ls.Add(new float[] { l.Mode, lrr, lgg, lbb, pos[0], pos[1], pos[2], dir[0], dir[1], dir[2], l.AttC, l.AttL, l.AttQ, (float)Math.Cos(l.SpotAngle * Math.PI / 180), l.SpotExp });
            }
            float mar = ((mat.Ambient >> 16) & 255) / 255f, mag = ((mat.Ambient >> 8) & 255) / 255f, mab = (mat.Ambient & 255) / 255f;
            float mdr = ((mat.Diffuse >> 16) & 255) / 255f, mdg = ((mat.Diffuse >> 8) & 255) / 255f, mdb = (mat.Diffuse & 255) / 255f, mda = ((mat.Diffuse >> 24) & 255) / 255f;
            float mer = ((mat.Emissive >> 16) & 255) / 255f, meg = ((mat.Emissive >> 8) & 255) / 255f, meb = (mat.Emissive & 255) / 255f;
            float msr = ((mat.Specular >> 16) & 255) / 255f, msg = ((mat.Specular >> 8) & 255) / 255f, msb = (mat.Specular & 255) / 255f;
            bool spec = (msr + msg + msb) > 0;
            var P = d.Pos; var N = d.Norm;
            for (int i = 0; i < n; i++)
            {
                float ar = mar, ag = mag, ab = mab, dr = mdr, dg = mdg, db = mdb, da = mda;
                if (mat.Tracking)
                {
                    int c = d.Col != null ? d.Col[i] : d.DefaultColor;
                    ar = dr = ((c >> 16) & 255) / 255f; ag = dg = ((c >> 8) & 255) / 255f; ab = db = (c & 255) / 255f; da = ((c >> 24) & 255) / 255f;
                }
                float px = P[i * 3], py = P[i * 3 + 1], pz = P[i * 3 + 2];
                float ex = mv[0] * px + mv[1] * py + mv[2] * pz + mv[3];
                float ey = mv[4] * px + mv[5] * py + mv[6] * pz + mv[7];
                float ez = mv[8] * px + mv[9] * py + mv[10] * pz + mv[11];
                float nx = 0, ny = 0, nz = 1;
                if (N != null)
                {
                    float ox_ = N[i * 3], oy_ = N[i * 3 + 1], oz_ = N[i * 3 + 2];
                    nx = n00 * ox_ + n01 * oy_ + n02 * oz_; ny = n10 * ox_ + n11 * oy_ + n12 * oz_; nz = n20 * ox_ + n21 * oy_ + n22 * oz_;
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 0) { nx /= len; ny /= len; nz /= len; }
                }
                for (int side = 0; side < (twoSided ? 2 : 1); side++)
                {
                    if (side == 1) { nx = -nx; ny = -ny; nz = -nz; }
                    float cr = mer + ar * ambR, cg = meg + ag * ambG, cb = meb + ab * ambB;
                    foreach (var L in ls)
                    {
                        float lx, ly, lz, att = 1;
                        if (L[0] == LightObj.DIRECTIONAL) { lx = -L[7]; ly = -L[8]; lz = -L[9]; }
                        else
                        {
                            lx = L[4] - ex; ly = L[5] - ey; lz = L[6] - ez;
                            float dist = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
                            if (dist > 0) { lx /= dist; ly /= dist; lz /= dist; }
                            float den = L[10] + L[11] * dist + L[12] * dist * dist;
                            att = den > 0 ? 1 / den : 1;
                            if (L[0] == LightObj.SPOT)
                            {
                                float ca = -(lx * L[7] + ly * L[8] + lz * L[9]);
                                if (ca < L[13]) continue;
                                att *= L[14] == 0 ? 1 : (float)Math.Pow(ca, L[14]);
                            }
                        }
                        float ndl = nx * lx + ny * ly + nz * lz;
                        if (ndl <= 0) continue;
                        cr += att * L[1] * dr * ndl; cg += att * L[2] * dg * ndl; cb += att * L[3] * db * ndl;
                        if (spec)
                        {
                            float vx = 0, vy = 0, vz = 1;
                            if (local) { float vl = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez); if (vl > 0) { vx = -ex / vl; vy = -ey / vl; vz = -ez / vl; } }
                            float hx = lx + vx, hy = ly + vy, hz = lz + vz;
                            float hl = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz);
                            if (hl > 0)
                            {
                                float ndh = (nx * hx + ny * hy + nz * hz) / hl;
                                if (ndh > 0)
                                {
                                    float sp = mat.Shininess > 0 ? (float)Math.Pow(ndh, mat.Shininess) : 1;
                                    cr += att * L[1] * msr * sp; cg += att * L[2] * msg * sp; cb += att * L[3] * msb * sp;
                                }
                            }
                        }
                    }
                    int o = i * ST + (side == 0 ? 4 : 13);
                    v[o] = cr > 1 ? 1 : cr < 0 ? 0 : cr;
                    v[o + 1] = cg > 1 ? 1 : cg < 0 ? 0 : cg;
                    v[o + 2] = cb > 1 ? 1 : cb < 0 ? 0 : cb;
                    v[o + 3] = da * alpha;
                }
            }
        }

        static void Norm3(float[] d)
        {
            float l = (float)Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            if (l > 0) { d[0] /= l; d[1] /= l; d[2] /= l; }
        }

        // ---------------- clipping & triangle setup ----------------

        readonly float[] polyA = new float[12 * ST], polyB = new float[12 * ST];

        static int Outcode(float[] v, int o)
        {
            float x = v[o], y = v[o + 1], z = v[o + 2], w = v[o + 3];
            int c = 0;
            if (x < -w) c |= 1; if (x > w) c |= 2;
            if (y < -w) c |= 4; if (y > w) c |= 8;
            if (z < -w) c |= 16; if (z > w) c |= 32;
            return c;
        }

        void Triangle(float[] v, int a, int b, int c, int cull, bool ccw, bool flat, bool twoSided)
        {
            int oa = a * ST, ob = b * ST, oc = c * ST;
            int ca = Outcode(v, oa), cb = Outcode(v, ob), cc = Outcode(v, oc);
            if ((ca & cb & cc) != 0) return;
            var pa = polyA;
            Array.Copy(v, oa, pa, 0, ST); Array.Copy(v, ob, pa, ST, ST); Array.Copy(v, oc, pa, 2 * ST, ST);
            if (flat)
                for (int k = 0; k < 2; k++) { Array.Copy(pa, 2 * ST + 4, pa, k * ST + 4, 4); Array.Copy(pa, 2 * ST + 13, pa, k * ST + 13, 4); }
            int n = 3;
            var cur = pa;
            if ((ca | cb | cc) != 0)
            {
                var other = polyB;
                for (int plane = 0; plane < 6 && n >= 3; plane++)
                {
                    int bit = 1 << plane;
                    if (((ca | cb | cc) & bit) == 0) continue;
                    n = ClipPlane(cur, n, other, plane);
                    var t = cur; cur = other; other = t;
                }
                if (n < 3) return;
            }
            // project
            float hw = Vw * 0.5f, hh = Vh * 0.5f, bx = ox + Vx + hw, by = oy + Vy + hh;
            float dh = (DFar - DNear) * 0.5f, dm = DNear + dh;
            for (int i = 0; i < n; i++)
            {
                int o = i * ST;
                float w = cur[o + 3];
                if (w <= 1e-6f) w = 1e-6f;
                float iw = 1 / w;
                cur[o] = bx + cur[o] * iw * hw;
                cur[o + 1] = by - cur[o + 1] * iw * hh;
                cur[o + 2] = dm + cur[o + 2] * iw * dh;
                cur[o + 3] = iw;
            }
            // orientation
            float area = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += cur[i * ST] * cur[j * ST + 1] - cur[j * ST] * cur[i * ST + 1];
            }
            if (area == 0) return;
            bool front = ccw ? area < 0 : area > 0;
            if (cull == PolygonObj.CULL_BACK && !front) return;
            if (cull == PolygonObj.CULL_FRONT && front) return;
            bool back = twoSided && !front;
            for (int i = 1; i + 1 < n; i++) rast.Tri(cur, 0, i * ST, (i + 1) * ST, back);
        }

        static float Dist(float[] p, int o, int plane)
        {
            float w = p[o + 3];
            switch (plane)
            {
                case 0: return w + p[o];
                case 1: return w - p[o];
                case 2: return w + p[o + 1];
                case 3: return w - p[o + 1];
                case 4: return w + p[o + 2];
                default: return w - p[o + 2];
            }
        }

        static int ClipPlane(float[] src, int n, float[] dst, int plane)
        {
            int m = 0;
            for (int i = 0; i < n && m < 11; i++)
            {
                int j = (i + 1) % n;
                int oi = i * ST, oj = j * ST;
                float di = Dist(src, oi, plane), dj = Dist(src, oj, plane);
                if (di >= 0) { Array.Copy(src, oi, dst, m * ST, ST); m++; }
                if ((di >= 0) != (dj >= 0))
                {
                    float t = di / (di - dj);
                    int od = m * ST;
                    for (int k = 0; k < ST; k++) dst[od + k] = src[oi + k] + t * (src[oj + k] - src[oi + k]);
                    m++;
                }
            }
            return m;
        }

        // ---------------- sprites ----------------

        void DrawSprite(Sprite3DObj sp, float[] model, float alpha)
        {
            var ap = sp.Ap; var img = sp.Image;
            if (ap == null || img == null || sp.Cw == 0 || sp.Ch == 0) return;
            var mv = Mat.Mul(view, model);
            var c = new float[4]; var ax = new float[4]; var ay = new float[4];
            Mat.Apply(mv, 0, 0, 0, 1, c, 0);
            float hwx, hwy; // half extents in eye space
            float[] cc = new float[4];
            Mat.Apply(proj, c[0], c[1], c[2], 1, cc, 0);
            if (cc[3] <= 0) return;
            if (sp.Scaled)
            {
                Mat.Apply(mv, 1, 0, 0, 1, ax, 0); Mat.Apply(mv, 0, 1, 0, 1, ay, 0);
                float sx = Len3(ax[0] - c[0], ax[1] - c[1], ax[2] - c[2]);
                float sy = Len3(ay[0] - c[0], ay[1] - c[1], ay[2] - c[2]);
                hwx = sx * 0.5f; hwy = sy * 0.5f;
            }
            else
            {
                // pixel size -> eye size at this depth
                float pxw = Math.Abs(sp.Cw), pxh = Math.Abs(sp.Ch);
                float ndcW = pxw / Vw * 2, ndcH = pxh / Vh * 2;
                hwx = ndcW * 0.5f * cc[3] / Math.Max(1e-6f, Math.Abs(proj[0]));
                hwy = ndcH * 0.5f * cc[3] / Math.Max(1e-6f, Math.Abs(proj[5]));
            }
            var q = new float[4 * ST];
            float[] xs = { -hwx, hwx, hwx, -hwx }, ys = { hwy, hwy, -hwy, -hwy };
            float u0 = sp.Cx, v0 = sp.Cy, u1 = sp.Cx + sp.Cw, v1 = sp.Cy + sp.Ch;
            float[] us = { u0, u1, u1, u0 }, vs = { v0, v0, v1, v1 };
            var tmp = new float[4];
            for (int i = 0; i < 4; i++)
            {
                Mat.Apply(proj, c[0] + xs[i], c[1] + ys[i], c[2], 1, tmp, 0);
                int o = i * ST;
                q[o] = tmp[0]; q[o + 1] = tmp[1]; q[o + 2] = cc[2] * tmp[3] / cc[3]; q[o + 3] = tmp[3];
                q[o + 4] = q[o + 5] = q[o + 6] = 1; q[o + 7] = alpha;
                q[o + 8] = us[i] / img.W; q[o + 9] = vs[i] / img.H;
                q[o + 12] = 1;
            }
            var fog = ap.Fog;
            if (fog != null)
            {
                float ez = -c[2];
                float f = fog.Mode == FogObj.LINEAR ? (fog.Far == fog.Near ? 1 : (fog.Far - ez) / (fog.Far - fog.Near)) : (float)Math.Exp(-fog.Density * ez);
                f = f < 0 ? 0 : f > 1 ? 1 : f;
                for (int i = 0; i < 4; i++) q[i * ST + 12] = f;
            }
            rast.Begin(this, ap, true, img);
            Triangle(q, 0, 1, 2, PolygonObj.CULL_NONE, true, false, false);
            Triangle(q, 0, 2, 3, PolygonObj.CULL_NONE, true, false, false);
        }

        static float Len3(float x, float y, float z) => (float)Math.Sqrt(x * x + y * y + z * z);

        // ---------------- rasterizer ----------------

        sealed class Raster
        {
            Renderer r;
            int[] pix; float[] zb; int W;
            int x0, y0, x1, y1;
            bool depthTest, depthWrite, colorWrite, opaque;
            int blend, thr;
            float offF, offU;
            bool fog; int fogR, fogG, fogB;
            readonly Tex[] tex = new Tex[2];
            int ntex;

            sealed class Tex
            {
                public int[] P; public int W, H, Func; public bool RepS, RepT, HasA, HasC;
                public int Br, Bg, Bb;
                public int Unit;
            }

            public void Begin(Renderer rr, AppearanceObj ap, bool sprite, Image2DObj spriteImg)
            {
                r = rr;
                pix = rr.surf.Pix; W = rr.surf.W;
                zb = rr.depthOn ? rr.zbuf : null;
                opaque = rr.opaqueTarget;
                x0 = Math.Max(rr.clx0, rr.ox + rr.Vx); y0 = Math.Max(rr.cly0, rr.oy + rr.Vy);
                x1 = Math.Min(rr.clx1, rr.ox + rr.Vx + rr.Vw); y1 = Math.Min(rr.cly1, rr.oy + rr.Vy + rr.Vh);
                var cm = ap.Comp;
                depthTest = zb != null && (cm == null || cm.DepthTest);
                depthWrite = zb != null && (cm == null || cm.DepthWrite);
                colorWrite = cm == null || cm.ColorWrite;
                blend = cm?.Blending ?? CompositingObj.REPLACE;
                thr = cm == null ? 0 : (int)Math.Ceiling(cm.Threshold * 255);
                offF = cm?.OffFactor ?? 0; offU = cm?.OffUnits ?? 0;
                fog = ap.Fog != null;
                if (fog) { fogR = (ap.Fog.Color >> 16) & 255; fogG = (ap.Fog.Color >> 8) & 255; fogB = ap.Fog.Color & 255; }
                ntex = 0;
                if (sprite)
                {
                    tex[0] = Make(spriteImg, TextureObj.FUNC_REPLACE, false, false, 0, 0);
                    ntex = 1;
                }
                else
                    for (int u = 0; u < 2; u++)
                    {
                        var t = ap.Tex[u];
                        if (t?.Image == null) continue;
                        tex[ntex++] = Make(t.Image, t.Blending, t.WrapS == TextureObj.WRAP_REPEAT, t.WrapT == TextureObj.WRAP_REPEAT, t.BlendColor, u);
                    }
            }

            static Tex Make(Image2DObj im, int func, bool rs, bool rt, int bc, int unit)
            {
                return new Tex { P = im.Pix, W = im.W, H = im.H, Func = func, RepS = rs, RepT = rt, HasA = im.HasAlpha, HasC = im.HasColor, Br = (bc >> 16) & 255, Bg = (bc >> 8) & 255, Bb = bc & 255, Unit = unit };
            }

            // gradients
            float ax, ay, ac; // helper

            public void Tri(float[] v, int a, int b, int c, bool back)
            {
                // sort by y
                if (v[b + 1] < v[a + 1]) { int t = a; a = b; b = t; }
                if (v[c + 1] < v[a + 1]) { int t = a; a = c; c = t; }
                if (v[c + 1] < v[b + 1]) { int t = b; b = c; c = t; }
                float xa = v[a], ya = v[a + 1], xb = v[b], yb = v[b + 1], xc = v[c], yc = v[c + 1];
                float area = (xb - xa) * (yc - ya) - (xc - xa) * (yb - ya);
                if (area == 0 || float.IsNaN(area)) return;
                float ia = 1 / area;
                float e1x = xb - xa, e1y = yb - ya, e2x = xc - xa, e2y = yc - ya;
                int co = back ? 13 : 4;
                // attribute gradients (value at a, d/dx, d/dy)
                float zA = v[a + 2], zX, zY; Grad(v, a, b, c, 2, e1x, e1y, e2x, e2y, ia, out zX, out zY);
                float wA = v[a + 3], wX, wY; Grad(v, a, b, c, 3, e1x, e1y, e2x, e2y, ia, out wX, out wY);
                float rA = v[a + co], rX, rY; Grad(v, a, b, c, co, e1x, e1y, e2x, e2y, ia, out rX, out rY);
                float gA = v[a + co + 1], gX, gY; Grad(v, a, b, c, co + 1, e1x, e1y, e2x, e2y, ia, out gX, out gY);
                float bA = v[a + co + 2], bX, bY; Grad(v, a, b, c, co + 2, e1x, e1y, e2x, e2y, ia, out bX, out bY);
                float aA = v[a + co + 3], aX, aY; Grad(v, a, b, c, co + 3, e1x, e1y, e2x, e2y, ia, out aX, out aY);
                float fA = 1, fX = 0, fY = 0;
                if (fog) { fA = v[a + 12]; Grad(v, a, b, c, 12, e1x, e1y, e2x, e2y, ia, out fX, out fY); }
                // perspective-correct texcoords: interpolate s*iw, t*iw
                float s0A = 0, s0X = 0, s0Y = 0, t0A = 0, t0X = 0, t0Y = 0, s1A = 0, s1X = 0, s1Y = 0, t1A = 0, t1X = 0, t1Y = 0;
                if (ntex > 0) GradPersp(v, a, b, c, 8 + tex[0].Unit * 2, e1x, e1y, e2x, e2y, ia, out s0A, out s0X, out s0Y, out t0A, out t0X, out t0Y);
                if (ntex > 1) GradPersp(v, a, b, c, 8 + tex[1].Unit * 2, e1x, e1y, e2x, e2y, ia, out s1A, out s1X, out s1Y, out t1A, out t1X, out t1Y);
                float zoff = 0;
                if (offF != 0 || offU != 0) zoff = offF * Math.Max(Math.Abs(zX), Math.Abs(zY)) + offU * (1f / 65536);

                int iy0 = Math.Max(y0, (int)Math.Ceiling(ya - 0.5f));
                int iy1 = Math.Min(y1, (int)Math.Ceiling(yc - 0.5f));
                for (int y = iy0; y < iy1; y++)
                {
                    float py = y + 0.5f;
                    // long edge a->c, short edges a->b / b->c
                    float xl = xa + (xc - xa) * (py - ya) / (yc - ya);
                    float xr = py < yb ? (yb == ya ? xb : xa + (xb - xa) * (py - ya) / (yb - ya)) : (yc == yb ? xb : xb + (xc - xb) * (py - yb) / (yc - yb));
                    if (xl > xr) { float t = xl; xl = xr; xr = t; }
                    int ix0 = Math.Max(x0, (int)Math.Ceiling(xl - 0.5f));
                    int ix1 = Math.Min(x1, (int)Math.Ceiling(xr - 0.5f));
                    if (ix1 <= ix0) continue;
                    float dx = ix0 + 0.5f - xa, dy = py - ya;
                    float z = zA + zX * dx + zY * dy + zoff, w = wA + wX * dx + wY * dy;
                    float cr = rA + rX * dx + rY * dy, cg = gA + gX * dx + gY * dy, cb = bA + bX * dx + bY * dy, ca = aA + aX * dx + aY * dy;
                    float f = fA + fX * dx + fY * dy;
                    float s0 = s0A + s0X * dx + s0Y * dy, t0 = t0A + t0X * dx + t0Y * dy, s1 = s1A + s1X * dx + s1Y * dy, t1 = t1A + t1X * dx + t1Y * dy;
                    int row = y * W;
                    for (int x = ix0; x < ix1; x++, z += zX, w += wX, cr += rX, cg += gX, cb += bX, ca += aX, f += fX, s0 += s0X, t0 += t0X, s1 += s1X, t1 += t1X)
                    {
                        int idx = row + x;
                        if (depthTest && z > zb[idx]) continue;
                        int R = (int)(cr * 255 + 0.5f), G = (int)(cg * 255 + 0.5f), B = (int)(cb * 255 + 0.5f), A = (int)(ca * 255 + 0.5f);
                        if (ntex > 0)
                        {
                            float iw = 1 / w;
                            Apply(tex[0], s0 * iw, t0 * iw, ref R, ref G, ref B, ref A);
                            if (ntex > 1) Apply(tex[1], s1 * iw, t1 * iw, ref R, ref G, ref B, ref A);
                        }
                        if (fog)
                        {
                            int fi = (int)(f * 256);
                            if (fi < 0) fi = 0; else if (fi > 256) fi = 256;
                            R = (R * fi + fogR * (256 - fi)) >> 8; G = (G * fi + fogG * (256 - fi)) >> 8; B = (B * fi + fogB * (256 - fi)) >> 8;
                        }
                        if (A < thr) continue;
                        if (depthWrite) zb[idx] = z;
                        if (!colorWrite) continue;
                        R = R < 0 ? 0 : R > 255 ? 255 : R; G = G < 0 ? 0 : G > 255 ? 255 : G; B = B < 0 ? 0 : B > 255 ? 255 : B; A = A < 0 ? 0 : A > 255 ? 255 : A;
                        int d = pix[idx];
                        int dr = (d >> 16) & 255, dg = (d >> 8) & 255, db = d & 255;
                        switch (blend)
                        {
                            case CompositingObj.ALPHA:
                                R = (R * A + dr * (255 - A)) / 255; G = (G * A + dg * (255 - A)) / 255; B = (B * A + db * (255 - A)) / 255; break;
                            case CompositingObj.ALPHA_ADD:
                                R = Math.Min(255, dr + R * A / 255); G = Math.Min(255, dg + G * A / 255); B = Math.Min(255, db + B * A / 255); break;
                            case CompositingObj.MODULATE:
                                R = R * dr / 255; G = G * dg / 255; B = B * db / 255; break;
                            case CompositingObj.MODULATE_X2:
                                R = Math.Min(255, 2 * R * dr / 255); G = Math.Min(255, 2 * G * dg / 255); B = Math.Min(255, 2 * B * db / 255); break;
                        }
                        int outA = opaque ? 255 : (blend == CompositingObj.REPLACE ? A : Math.Max(A, (d >> 24) & 255));
                        pix[idx] = (outA << 24) | (R << 16) | (G << 8) | B;
                    }
                }
            }

            static void Grad(float[] v, int a, int b, int c, int k, float e1x, float e1y, float e2x, float e2y, float ia, out float gx, out float gy)
            {
                float d1 = v[b + k] - v[a + k], d2 = v[c + k] - v[a + k];
                gx = (d1 * e2y - d2 * e1y) * ia;
                gy = (d2 * e1x - d1 * e2x) * ia;
            }

            static void GradPersp(float[] v, int a, int b, int c, int k, float e1x, float e1y, float e2x, float e2y, float ia,
                out float sA, out float sX, out float sY, out float tA, out float tX, out float tY)
            {
                float sa = v[a + k] * v[a + 3], sb = v[b + k] * v[b + 3], sc = v[c + k] * v[c + 3];
                float ta = v[a + k + 1] * v[a + 3], tb = v[b + k + 1] * v[b + 3], tc = v[c + k + 1] * v[c + 3];
                sA = sa; tA = ta;
                sX = ((sb - sa) * e2y - (sc - sa) * e1y) * ia; sY = ((sc - sa) * e1x - (sb - sa) * e2x) * ia;
                tX = ((tb - ta) * e2y - (tc - ta) * e1y) * ia; tY = ((tc - ta) * e1x - (tb - ta) * e2x) * ia;
            }

            static void Apply(Tex t, float s, float u, ref int R, ref int G, ref int B, ref int A)
            {
                float fs = s * t.W, ft = u * t.H;
                int ix = fs >= -1e9f && fs <= 1e9f ? (int)Math.Floor(fs) : 0;
                int iy = ft >= -1e9f && ft <= 1e9f ? (int)Math.Floor(ft) : 0;
                if (t.RepS) { ix %= t.W; if (ix < 0) ix += t.W; } else ix = ix < 0 ? 0 : ix >= t.W ? t.W - 1 : ix;
                if (t.RepT) { iy %= t.H; if (iy < 0) iy += t.H; } else iy = iy < 0 ? 0 : iy >= t.H ? t.H - 1 : iy;
                int c = t.P[iy * t.W + ix];
                int tr = (c >> 16) & 255, tg = (c >> 8) & 255, tb = c & 255, ta = (c >> 24) & 255;
                switch (t.Func)
                {
                    case TextureObj.FUNC_REPLACE:
                        if (t.HasC) { R = tr; G = tg; B = tb; }
                        if (t.HasA) A = ta;
                        break;
                    case TextureObj.FUNC_DECAL:
                        if (t.HasA) { R = (R * (255 - ta) + tr * ta) / 255; G = (G * (255 - ta) + tg * ta) / 255; B = (B * (255 - ta) + tb * ta) / 255; }
                        else { R = tr; G = tg; B = tb; }
                        break;
                    case TextureObj.FUNC_BLEND:
                        R = (R * (255 - tr) + t.Br * tr) / 255; G = (G * (255 - tg) + t.Bg * tg) / 255; B = (B * (255 - tb) + t.Bb * tb) / 255;
                        A = A * ta / 255;
                        break;
                    case TextureObj.FUNC_ADD:
                        R = Math.Min(255, R + tr); G = Math.Min(255, G + tg); B = Math.Min(255, B + tb);
                        A = A * ta / 255;
                        break;
                    default: // MODULATE
                        R = R * tr / 255; G = G * tg / 255; B = B * tb / 255; A = A * ta / 255;
                        break;
                }
            }
        }
    }
}
