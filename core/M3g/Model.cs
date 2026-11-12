using System;
using System.Collections.Generic;
using Lumidlet.Vm;

namespace Lumidlet.M3g
{
    // 4x4 row-major matrices (m[row * 4 + col]), column vectors: v' = M v.
    public static class Mat
    {
        public static float[] Identity() { var m = new float[16]; m[0] = m[5] = m[10] = m[15] = 1; return m; }

        public static void SetIdentity(float[] m) { Array.Clear(m, 0, 16); m[0] = m[5] = m[10] = m[15] = 1; }

        public static void Mul(float[] a, float[] b, float[] r)
        {
            float[] t = (r == a || r == b) ? new float[16] : r;
            for (int i = 0; i < 4; i++)
            {
                float a0 = a[i * 4], a1 = a[i * 4 + 1], a2 = a[i * 4 + 2], a3 = a[i * 4 + 3];
                t[i * 4] = a0 * b[0] + a1 * b[4] + a2 * b[8] + a3 * b[12];
                t[i * 4 + 1] = a0 * b[1] + a1 * b[5] + a2 * b[9] + a3 * b[13];
                t[i * 4 + 2] = a0 * b[2] + a1 * b[6] + a2 * b[10] + a3 * b[14];
                t[i * 4 + 3] = a0 * b[3] + a1 * b[7] + a2 * b[11] + a3 * b[15];
            }
            if (t != r) Array.Copy(t, r, 16);
        }

        public static float[] Mul(float[] a, float[] b) { var r = new float[16]; Mul(a, b, r); return r; }

        public static bool Invert(float[] m, float[] r)
        {
            var inv = new float[16];
            inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
            inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
            inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
            inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
            inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
            inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
            inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
            inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
            inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
            inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
            inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
            inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
            inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
            inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
            inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
            inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];
            double det = (double)m[0] * inv[0] + (double)m[1] * inv[4] + (double)m[2] * inv[8] + (double)m[3] * inv[12];
            if (det == 0 || double.IsNaN(det)) return false;
            float id = (float)(1.0 / det);
            for (int i = 0; i < 16; i++) r[i] = inv[i] * id;
            return true;
        }

        public static float[] Inverse(float[] m)
        {
            var r = new float[16];
            if (!Invert(m, r)) SetIdentity(r);
            return r;
        }

        public static void Transpose(float[] m)
        {
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++) { float t = m[i * 4 + j]; m[i * 4 + j] = m[j * 4 + i]; m[j * 4 + i] = t; }
        }

        public static float[] Translation(float x, float y, float z) { var m = Identity(); m[3] = x; m[7] = y; m[11] = z; return m; }
        public static float[] Scale(float x, float y, float z) { var m = Identity(); m[0] = x; m[5] = y; m[10] = z; return m; }

        public static float[] Rotation(float deg, float ax, float ay, float az)
        {
            var m = Identity();
            float len = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            if (deg == 0) return m;
            if (len == 0) throw new JavaThrow("java/lang/IllegalArgumentException", "zero rotation axis");
            ax /= len; ay /= len; az /= len;
            double a = deg * Math.PI / 180;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a), t = 1 - c;
            m[0] = t * ax * ax + c; m[1] = t * ax * ay - s * az; m[2] = t * ax * az + s * ay;
            m[4] = t * ax * ay + s * az; m[5] = t * ay * ay + c; m[6] = t * ay * az - s * ax;
            m[8] = t * ax * az - s * ay; m[9] = t * ay * az + s * ax; m[10] = t * az * az + c;
            return m;
        }

        public static float[] FromQuat(float[] q)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            float n = x * x + y * y + z * z + w * w;
            var m = Identity();
            if (n == 0) return m;
            float s = 2 / n;
            m[0] = 1 - s * (y * y + z * z); m[1] = s * (x * y - z * w); m[2] = s * (x * z + y * w);
            m[4] = s * (x * y + z * w); m[5] = 1 - s * (x * x + z * z); m[6] = s * (y * z - x * w);
            m[8] = s * (x * z - y * w); m[9] = s * (y * z + x * w); m[10] = 1 - s * (x * x + y * y);
            return m;
        }

        public static void Apply(float[] m, float x, float y, float z, float w, float[] o, int off)
        {
            o[off] = m[0] * x + m[1] * y + m[2] * z + m[3] * w;
            o[off + 1] = m[4] * x + m[5] * y + m[6] * z + m[7] * w;
            o[off + 2] = m[8] * x + m[9] * y + m[10] * z + m[11] * w;
            o[off + 3] = m[12] * x + m[13] * y + m[14] * z + m[15] * w;
        }
    }

    public static class Quat
    {
        public static float[] FromAxisAngle(float deg, float ax, float ay, float az)
        {
            float len = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            if (deg == 0 || len == 0)
            {
                if (len == 0 && deg != 0) throw new JavaThrow("java/lang/IllegalArgumentException", "zero rotation axis");
                return new float[] { 0, 0, 0, 1 };
            }
            double h = deg * Math.PI / 360;
            float s = (float)Math.Sin(h) / len;
            return new float[] { ax * s, ay * s, az * s, (float)Math.Cos(h) };
        }

        public static float[] Mul(float[] a, float[] b)
        {
            return new float[]
            {
                a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
                a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
            };
        }

        public static void Normalize(float[] q)
        {
            float n = (float)Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            if (n == 0) { q[0] = q[1] = q[2] = 0; q[3] = 1; return; }
            for (int i = 0; i < 4; i++) q[i] /= n;
        }

        public static void ToAxisAngle(float[] q, float[] o)
        {
            var n = (float[])q.Clone(); Normalize(n);
            float w = Math.Max(-1, Math.Min(1, n[3]));
            float ang = (float)(2 * Math.Acos(w) * 180 / Math.PI);
            float s = (float)Math.Sqrt(1 - w * w);
            if (s < 1e-6f || ang == 0) { o[0] = 0; o[1] = 0; o[2] = 0; o[3] = 1; return; }
            o[0] = ang; o[1] = n[0] / s; o[2] = n[1] / s; o[3] = n[2] / s;
        }

        public static float[] Slerp(float[] a, float[] b, float t)
        {
            float d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
            float sgn = 1;
            if (d < 0) { d = -d; sgn = -1; }
            float k0, k1;
            if (d > 0.9995f) { k0 = 1 - t; k1 = t; }
            else
            {
                double th = Math.Acos(d), st = Math.Sin(th);
                k0 = (float)(Math.Sin((1 - t) * th) / st); k1 = (float)(Math.Sin(t * th) / st);
            }
            k1 *= sgn;
            var r = new float[] { a[0] * k0 + b[0] * k1, a[1] * k0 + b[1] * k1, a[2] * k0 + b[2] * k1, a[3] * k0 + b[3] * k1 };
            Normalize(r);
            return r;
        }
    }

    // ---------------- object model ----------------

    public class Obj3D : JObject
    {
        public int UserID;
        public object UserObject;
        public List<TrackObj> Tracks = new List<TrackObj>();

        public virtual void Refs(List<Obj3D> o) { foreach (var t in Tracks) o.Add(t); }

        public Obj3D Clone()
        {
            var c = (Obj3D)MemberwiseClone();
            if (P != null) c.P = (Slot[])P.Clone();
            if (O != null) c.O = (object[])O.Clone();
            c.Tracks = new List<TrackObj>(Tracks);
            c.CopyDeep();
            return c;
        }

        protected virtual void CopyDeep() { }
    }

    public class TransformableObj : Obj3D
    {
        public float Tx, Ty, Tz;
        public float Sx = 1, Sy = 1, Sz = 1;
        public float[] Rot = { 0, 0, 0, 1 };
        public float[] M = Mat.Identity();

        protected override void CopyDeep() { base.CopyDeep(); Rot = (float[])Rot.Clone(); M = (float[])M.Clone(); }

        public float[] Composite()
        {
            var r = Mat.Translation(Tx, Ty, Tz);
            Mat.Mul(r, Mat.FromQuat(Rot), r);
            Mat.Mul(r, Mat.Scale(Sx, Sy, Sz), r);
            Mat.Mul(r, M, r);
            return r;
        }
    }

    public class NodeObj : TransformableObj
    {
        public NodeObj Parent;
        public bool Render = true, Pick = true;
        public float Alpha = 1;
        public int Scope = -1;
        public int ZTarget = 144, YTarget = 144;
        public NodeObj ZRef, YRef;

        protected override void CopyDeep() { base.CopyDeep(); Parent = null; }

        // transform from this node to the root of its tree
        public float[] ToRoot(out NodeObj root)
        {
            var m = Composite();
            var n = Parent; root = this;
            while (n != null) { m = Mat.Mul(n.Composite(), m); root = n; n = n.Parent; }
            return m;
        }

        public bool IsAncestorOrSelf(NodeObj other)
        {
            for (var n = other; n != null; n = n.Parent) if (n == this) return true;
            return false;
        }
    }

    public class GroupObj : NodeObj
    {
        public List<NodeObj> Children = new List<NodeObj>();

        public override void Refs(List<Obj3D> o) { base.Refs(o); foreach (var c in Children) o.Add(c); }

        protected override void CopyDeep()
        {
            base.CopyDeep();
            var old = Children; Children = new List<NodeObj>();
            foreach (var c in old) { var d = (NodeObj)c.Clone(); d.Parent = this; Children.Add(d); }
        }
    }

    public sealed class WorldObj : GroupObj
    {
        public CameraObj Camera;
        public BackgroundObj Background;

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Camera != null) o.Add(Camera); if (Background != null) o.Add(Background); }
    }

    public sealed class CameraObj : NodeObj
    {
        public const int GENERIC = 48, PARALLEL = 49, PERSPECTIVE = 50;
        public int Type = PERSPECTIVE;
        public float Fovy = 60, Aspect = 1, Near = 0.1f, Far = 1;
        public float[] Proj = Mat.Identity();

        protected override void CopyDeep() { base.CopyDeep(); Proj = (float[])Proj.Clone(); }

        public float[] Projection()
        {
            if (Type == GENERIC) return (float[])Proj.Clone();
            var m = new float[16];
            float n = Near, f = Far;
            if (Type == PERSPECTIVE)
            {
                float h = (float)(1 / Math.Tan(Fovy * Math.PI / 360));
                m[0] = h / Aspect; m[5] = h;
                m[10] = -(f + n) / (f - n); m[11] = -2 * f * n / (f - n);
                m[14] = -1;
            }
            else
            {
                m[0] = 2 / (Aspect * Fovy); m[5] = 2 / Fovy;
                m[10] = -2 / (f - n); m[11] = -(f + n) / (f - n);
                m[15] = 1;
            }
            return m;
        }
    }

    public sealed class LightObj : NodeObj
    {
        public const int AMBIENT = 128, DIRECTIONAL = 129, OMNI = 130, SPOT = 131;
        public int Mode = DIRECTIONAL;
        public int Color = 0xFFFFFF;
        public float Intensity = 1, AttC = 1, AttL, AttQ, SpotAngle = 45, SpotExp;
    }

    public class MeshObj : NodeObj
    {
        public VertexBufferObj Vb;
        public IndexBufferObj[] Ib;
        public AppearanceObj[] Ap;

        public override void Refs(List<Obj3D> o)
        {
            base.Refs(o);
            if (Vb != null) o.Add(Vb);
            foreach (var i in Ib) if (i != null) o.Add(i);
            foreach (var a in Ap) if (a != null) o.Add(a);
        }

        protected override void CopyDeep() { base.CopyDeep(); Ib = (IndexBufferObj[])Ib.Clone(); Ap = (AppearanceObj[])Ap.Clone(); }
    }

    public sealed class MorphingMeshObj : MeshObj
    {
        public VertexBufferObj[] Targets;
        public float[] Weights;

        public override void Refs(List<Obj3D> o) { base.Refs(o); foreach (var t in Targets) if (t != null) o.Add(t); }
        protected override void CopyDeep() { base.CopyDeep(); Weights = (float[])Weights.Clone(); }
    }

    public sealed class Bone
    {
        public NodeObj Node;
        public int Weight, First, Count;
        public float[] RestInv;
    }

    public sealed class SkinnedMeshObj : MeshObj
    {
        public GroupObj Skeleton;
        public List<Bone> Bones = new List<Bone>();

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Skeleton != null) o.Add(Skeleton); }

        protected override void CopyDeep()
        {
            base.CopyDeep();
            if (Skeleton != null)
            {
                var old = Skeleton;
                Skeleton = (GroupObj)old.Clone(); Skeleton.Parent = this;
                var map = new Dictionary<NodeObj, NodeObj>();
                MapNodes(old, Skeleton, map);
                var nb = new List<Bone>();
                foreach (var b in Bones)
                {
                    NodeObj n;
                    nb.Add(new Bone { Node = map.TryGetValue(b.Node, out n) ? n : b.Node, Weight = b.Weight, First = b.First, Count = b.Count, RestInv = b.RestInv });
                }
                Bones = nb;
            }
            else Bones = new List<Bone>(Bones);
        }

        static void MapNodes(NodeObj a, NodeObj b, Dictionary<NodeObj, NodeObj> map)
        {
            map[a] = b;
            var ga = a as GroupObj; var gb = b as GroupObj;
            if (ga != null && gb != null) for (int i = 0; i < ga.Children.Count && i < gb.Children.Count; i++) MapNodes(ga.Children[i], gb.Children[i], map);
        }
    }

    public sealed class Sprite3DObj : NodeObj
    {
        public bool Scaled;
        public Image2DObj Image;
        public AppearanceObj Ap;
        public int Cx, Cy, Cw, Ch;

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Image != null) o.Add(Image); if (Ap != null) o.Add(Ap); }
    }

    public sealed class BackgroundObj : Obj3D
    {
        public int Color;
        public Image2DObj Image;
        public int ModeX = 32, ModeY = 32;
        public int Cx, Cy, Cw, Ch;
        public bool ColorClear = true, DepthClear = true;

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Image != null) o.Add(Image); }
    }

    public sealed class AppearanceObj : Obj3D
    {
        public int Layer;
        public CompositingObj Comp;
        public FogObj Fog;
        public PolygonObj Poly;
        public MaterialObj Mat;
        public TextureObj[] Tex = new TextureObj[2];

        public override void Refs(List<Obj3D> o)
        {
            base.Refs(o);
            if (Comp != null) o.Add(Comp); if (Fog != null) o.Add(Fog); if (Poly != null) o.Add(Poly); if (Mat != null) o.Add(Mat);
            foreach (var t in Tex) if (t != null) o.Add(t);
        }

        protected override void CopyDeep() { base.CopyDeep(); Tex = (TextureObj[])Tex.Clone(); }
    }

    public sealed class CompositingObj : Obj3D
    {
        public const int ALPHA = 64, ALPHA_ADD = 65, MODULATE = 66, MODULATE_X2 = 67, REPLACE = 68;
        public int Blending = REPLACE;
        public float Threshold, OffFactor, OffUnits;
        public bool DepthTest = true, DepthWrite = true, ColorWrite = true, AlphaWrite = true;
    }

    public sealed class PolygonObj : Obj3D
    {
        public const int CULL_BACK = 160, CULL_FRONT = 161, CULL_NONE = 162, SHADE_FLAT = 164, SHADE_SMOOTH = 165, WINDING_CCW = 168, WINDING_CW = 169;
        public int Culling = CULL_BACK, Shading = SHADE_SMOOTH, Winding = WINDING_CCW;
        public bool TwoSided, LocalCamera, Perspective = true;
    }

    public sealed class MaterialObj : Obj3D
    {
        public const int AMBIENT = 1024, DIFFUSE = 2048, EMISSIVE = 4096, SPECULAR = 8192;
        public int Ambient = 0x333333, Diffuse = unchecked((int)0xFFCCCCCC), Emissive, Specular;
        public float Shininess;
        public bool Tracking;
    }

    public sealed class FogObj : Obj3D
    {
        public const int EXPONENTIAL = 80, LINEAR = 81;
        public int Mode = LINEAR, Color;
        public float Density = 1, Near, Far = 1;
    }

    public sealed class Image2DObj : Obj3D
    {
        public const int ALPHA = 96, LUMINANCE = 97, LUMINANCE_ALPHA = 98, RGB = 99, RGBA = 100;
        public int Format, W, H;
        public int[] Pix;   // ARGB, with format semantics applied (ALPHA -> white + alpha, etc.)
        public bool Mutable;
        public int Version;

        protected override void CopyDeep() { base.CopyDeep(); Pix = (int[])Pix.Clone(); }

        public bool HasAlpha => Format == ALPHA || Format == LUMINANCE_ALPHA || Format == RGBA;
        public bool HasColor => Format != ALPHA;

        public static int Pack(int format, int r, int g, int b, int a)
        {
            switch (format)
            {
                case ALPHA: return (a << 24) | 0xFFFFFF;
                case LUMINANCE: { int l = r; return unchecked((int)0xFF000000) | (l << 16) | (l << 8) | l; }
                case LUMINANCE_ALPHA: { int l = r; return (a << 24) | (l << 16) | (l << 8) | l; }
                case RGB: return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                default: return (a << 24) | (r << 16) | (g << 8) | b;
            }
        }

        public static int Bpp(int format)
        {
            switch (format) { case ALPHA: case LUMINANCE: return 1; case LUMINANCE_ALPHA: return 2; case RGB: return 3; default: return 4; }
        }

        public void SetBytes(int x0, int y0, int w, int h, byte[] d, int off)
        {
            int bpp = Bpp(Format);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = off + (y * w + x) * bpp;
                    int r, g = 0, b = 0, a = 255;
                    switch (Format)
                    {
                        case ALPHA: r = 255; a = d[p]; break;
                        case LUMINANCE: r = d[p]; break;
                        case LUMINANCE_ALPHA: r = d[p]; a = d[p + 1]; break;
                        case RGB: r = d[p]; g = d[p + 1]; b = d[p + 2]; break;
                        default: r = d[p]; g = d[p + 1]; b = d[p + 2]; a = d[p + 3]; break;
                    }
                    if (Format == LUMINANCE || Format == LUMINANCE_ALPHA) g = b = r;
                    Pix[(y0 + y) * W + x0 + x] = Pack(Format, r, g, b, a);
                }
            Version++;
        }

        public void SetPalette(byte[] idx, int ioff, byte[] pal, int poff)
        {
            int bpp = Bpp(Format);
            for (int i = 0; i < W * H; i++)
            {
                int p = poff + idx[ioff + i] * bpp;
                if (p + bpp > pal.Length) { Pix[i] = 0; continue; }
                int r, g = 0, b = 0, a = 255;
                switch (Format)
                {
                    case ALPHA: r = 255; a = pal[p]; break;
                    case LUMINANCE: r = pal[p]; g = b = r; break;
                    case LUMINANCE_ALPHA: r = pal[p]; g = b = r; a = pal[p + 1]; break;
                    case RGB: r = pal[p]; g = pal[p + 1]; b = pal[p + 2]; break;
                    default: r = pal[p]; g = pal[p + 1]; b = pal[p + 2]; a = pal[p + 3]; break;
                }
                Pix[i] = Pack(Format, r, g, b, a);
            }
            Version++;
        }

        public void SetArgb(int[] src, int sw, int sh)
        {
            for (int i = 0; i < W * H && i < src.Length; i++)
            {
                int c = src[i];
                int a = (c >> 24) & 255, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                if (Format == LUMINANCE || Format == LUMINANCE_ALPHA) r = g = b = (r * 77 + g * 151 + b * 28) >> 8;
                Pix[i] = Pack(Format, r, g, b, a);
            }
            Version++;
        }
    }

    public sealed class TextureObj : TransformableObj
    {
        public const int FILTER_BASE_LEVEL = 208, FILTER_LINEAR = 209, FILTER_NEAREST = 210;
        public const int FUNC_ADD = 224, FUNC_BLEND = 225, FUNC_DECAL = 226, FUNC_MODULATE = 227, FUNC_REPLACE = 228;
        public const int WRAP_CLAMP = 240, WRAP_REPEAT = 241;
        public Image2DObj Image;
        public int Blending = FUNC_MODULATE, WrapS = WRAP_REPEAT, WrapT = WRAP_REPEAT, LevelFilter = FILTER_BASE_LEVEL, ImageFilter = FILTER_NEAREST;
        public int BlendColor;

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Image != null) o.Add(Image); }
    }

    public sealed class VertexArrayObj : Obj3D
    {
        public int Count, Comps, Size;
        public short[] D;

        protected override void CopyDeep() { base.CopyDeep(); D = (short[])D.Clone(); }
    }

    public sealed class VertexBufferObj : Obj3D
    {
        public VertexArrayObj Pos, Norm, Col;
        public float PosScale = 1;
        public float[] PosBias = new float[3];
        public VertexArrayObj[] Tc = new VertexArrayObj[2];
        public float[] TcScale = { 1, 1 };
        public float[][] TcBias = { new float[3], new float[3] };
        public int DefaultColor = unchecked((int)0xFFFFFFFF);

        public int VertexCount
        {
            get
            {
                if (Pos != null) return Pos.Count;
                if (Norm != null) return Norm.Count;
                if (Col != null) return Col.Count;
                foreach (var t in Tc) if (t != null) return t.Count;
                return 0;
            }
        }

        public override void Refs(List<Obj3D> o)
        {
            base.Refs(o);
            if (Pos != null) o.Add(Pos); if (Norm != null) o.Add(Norm); if (Col != null) o.Add(Col);
            foreach (var t in Tc) if (t != null) o.Add(t);
        }

        protected override void CopyDeep()
        {
            base.CopyDeep();
            PosBias = (float[])PosBias.Clone(); Tc = (VertexArrayObj[])Tc.Clone(); TcScale = (float[])TcScale.Clone();
            TcBias = new[] { (float[])TcBias[0].Clone(), (float[])TcBias[1].Clone() };
        }
    }

    public class IndexBufferObj : Obj3D
    {
        // triangle list; each triangle stored with consistent (strip-corrected) winding
        public int[] Tris = new int[0];
        public int MaxIndex = -1;
        public int[] Explicit, Strips;
        public int Start;
    }

    public sealed class KeyframeObj : Obj3D
    {
        public const int LINEAR = 176, SLERP = 177, SPLINE = 178, SQUAD = 179, STEP = 180, CONSTANT = 192, LOOP = 193;
        public int Interp, Repeat = CONSTANT, Comps, Count, Duration, First, Last;
        public int[] Times;
        public float[][] Values;

        protected override void CopyDeep()
        {
            base.CopyDeep();
            Times = (int[])Times.Clone();
            var v = new float[Values.Length][];
            for (int i = 0; i < v.Length; i++) v[i] = (float[])Values[i].Clone();
            Values = v;
        }
    }

    public sealed class ControllerObj : Obj3D
    {
        public int Start, End;
        public float Speed = 1, Weight = 1, RefSeq;
        public int RefWorld;

        public bool Active(int t) => Start == End || (t >= Start && t < End);
        public float SeqTime(int worldTime) => RefSeq + Speed * (worldTime - RefWorld);
    }

    public sealed class TrackObj : Obj3D
    {
        public KeyframeObj Seq;
        public ControllerObj Ctl;
        public int Property;

        public override void Refs(List<Obj3D> o) { base.Refs(o); if (Seq != null) o.Add(Seq); if (Ctl != null) o.Add(Ctl); }
    }

    public sealed class TransformObj : JObject
    {
        public float[] M = Mat.Identity();
    }

    public sealed class RayObj : JObject
    {
        public NodeObj Node;
        public float Distance, Nx, Ny, Nz = 1, S0, T0, S1, T1;
        public int Submesh;
        public float[] Ray = new float[6];
    }
}
