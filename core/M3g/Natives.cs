using System;
using System.Collections.Generic;
using Lumidlet.Lib;
using Lumidlet.Midp;
using Lumidlet.Vm;

namespace Lumidlet.M3g
{
    public static class Natives
    {
        const string P = "javax/microedition/m3g/";
        const string O3 = "L" + P + "Object3D;";

        static T A<T>(JThread t, int i) where T : class => t.R[i] as T;
        static T N<T>(JThread t, int i) where T : class { var o = t.R[i]; if (o == null) throw new NullReferenceException(); return (T)o; }
        static float[] Fa(JThread t, int i) => (float[])t.R[i];
        static float[] Fn(JThread t, int i) { var a = (float[])t.R[i]; if (a == null) throw new NullReferenceException(); return a; }
        static void Iae(string m) { throw new JavaThrow("java/lang/IllegalArgumentException", m); }
        static void Need(float[] a, int n) { if (a == null) throw new NullReferenceException(); if (a.Length < n) Iae("array too short"); }

        static ObjArray Arr(JThread t, string cls, IList<Obj3D> items)
        {
            var a = new ObjArray(t.Vm.LoadClass("[L" + P + cls + ";"), items.Count);
            for (int i = 0; i < items.Count; i++) a.Data[i] = items[i];
            return a;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JVm, Graphics3DObj> instances = new System.Runtime.CompilerServices.ConditionalWeakTable<JVm, Graphics3DObj>();

        // ---------------- index buffers ----------------

        public static void BuildStrips(IndexBufferObj o, int[] idx, int start, int[] strips)
        {
            if (strips == null) throw new NullReferenceException();
            var tris = new List<int>();
            int p = 0, max = -1;
            foreach (int len in strips)
            {
                if (len < 3) Iae("strip length < 3");
                for (int k = 0; k + 2 < len; k++)
                {
                    int a = Get(idx, start, p + k), b = Get(idx, start, p + k + 1), c = Get(idx, start, p + k + 2);
                    if (a == b || b == c || a == c) continue;
                    if ((k & 1) == 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                    else { tris.Add(b); tris.Add(a); tris.Add(c); }
                }
                for (int k = 0; k < len; k++) max = Math.Max(max, Get(idx, start, p + k));
                p += len;
            }
            if (idx != null && p > idx.Length) Iae("indices too short");
            o.Tris = tris.ToArray();
            o.MaxIndex = max;
            o.Explicit = idx != null ? (int[])idx.Clone() : null;
            o.Start = start; o.Strips = (int[])strips.Clone();
        }

        static int Get(int[] idx, int start, int i) => idx == null ? start + i : (i < idx.Length ? idx[i] : 0);

        // ---------------- tree helpers ----------------

        static Obj3D Find(Obj3D root, int id)
        {
            var seen = new HashSet<Obj3D>();
            var q = new Queue<Obj3D>();
            q.Enqueue(root);
            var refs = new List<Obj3D>();
            while (q.Count > 0)
            {
                var o = q.Dequeue();
                if (o == null || !seen.Add(o)) continue;
                if (o.UserID == id) return o;
                refs.Clear(); o.Refs(refs);
                foreach (var r in refs) q.Enqueue(r);
            }
            return null;
        }

        static void AddChild(GroupObj g, NodeObj c)
        {
            if (c == null) throw new NullReferenceException();
            if (c == g || c is WorldObj) Iae("invalid child");
            if (c.Parent == g) return;
            if (c.Parent != null) Iae("node already has a parent");
            if (c.IsAncestorOrSelf(g)) Iae("cycle");
            c.Parent = g; g.Children.Add(c);
        }

        static void RemoveChild(GroupObj g, NodeObj c)
        {
            if (c == null) return;
            if (g.Children.Remove(c)) c.Parent = null;
        }

        static float[] RootPath(NodeObj n, out NodeObj root)
        {
            var m = Mat.Identity();
            root = n;
            for (var p = n; p != null; p = p.Parent) { m = Mat.Mul(p.Composite(), m); root = p; }
            return m;
        }

        static bool TransformTo(NodeObj from, NodeObj to, float[] outM)
        {
            NodeObj ra, rb;
            var a = RootPath(from, out ra); var b = RootPath(to, out rb);
            if (ra != rb) return false;
            var r = Mat.Mul(Mat.Inverse(b), a);
            Array.Copy(r, outM, 16);
            return true;
        }

        // ---------------- alignment ----------------

        static void Align(NodeObj n, NodeObj reference)
        {
            if (n.ZTarget != 144 || n.YTarget != 144) AlignOne(n, reference);
            var g = n as GroupObj;
            if (g != null) foreach (var c in g.Children) Align(c, reference);
        }

        static float[] TargetVec(NodeObj n, int target, NodeObj refNode, out bool ok)
        {
            ok = false;
            if (target == 144 || refNode == null) return null;
            NodeObj r1 = n, r2;
            var parentM = n.Parent != null ? RootPath(n.Parent, out r1) : Mat.Identity();
            if (n.Parent == null) r1 = n;
            var refM = RootPath(refNode, out r2);
            if (r1 != r2 && n.Parent != null) return null;
            var m = Mat.Mul(Mat.Inverse(parentM), refM);
            var v = new float[4];
            switch (target)
            {
                case 145: Mat.Apply(m, 0, 0, 0, 1, v, 0); v[0] -= n.Tx; v[1] -= n.Ty; v[2] -= n.Tz; break;
                case 146: Mat.Apply(m, 1, 0, 0, 0, v, 0); break;
                case 147: Mat.Apply(m, 0, 1, 0, 0, v, 0); break;
                case 148: Mat.Apply(m, 0, 0, 1, 0, v, 0); break;
                default: return null;
            }
            float l = (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (l < 1e-9f) return null;
            ok = true;
            return new[] { v[0] / l, v[1] / l, v[2] / l };
        }

        static void AlignOne(NodeObj n, NodeObj reference)
        {
            bool zok, yok;
            var z = TargetVec(n, n.ZTarget, n.ZRef ?? reference, out zok);
            var y = TargetVec(n, n.YTarget, n.YRef ?? reference, out yok);
            var cur = Mat.FromQuat(n.Rot);
            if (!zok) z = new[] { cur[2], cur[6], cur[10] };
            if (!yok) y = new[] { cur[1], cur[5], cur[9] };
            // orthonormalize, z dominant
            float d = y[0] * z[0] + y[1] * z[1] + y[2] * z[2];
            y = new[] { y[0] - d * z[0], y[1] - d * z[1], y[2] - d * z[2] };
            float yl = (float)Math.Sqrt(y[0] * y[0] + y[1] * y[1] + y[2] * y[2]);
            if (yl < 1e-6f)
            {
                y = Math.Abs(z[1]) < 0.9f ? new float[] { 0, 1, 0 } : new float[] { 1, 0, 0 };
                d = y[0] * z[0] + y[1] * z[1] + y[2] * z[2];
                y = new[] { y[0] - d * z[0], y[1] - d * z[1], y[2] - d * z[2] };
                yl = (float)Math.Sqrt(y[0] * y[0] + y[1] * y[1] + y[2] * y[2]);
            }
            y[0] /= yl; y[1] /= yl; y[2] /= yl;
            var x = new[] { y[1] * z[2] - y[2] * z[1], y[2] * z[0] - y[0] * z[2], y[0] * z[1] - y[1] * z[0] };
            n.Rot = MatToQuat(x, y, z);
        }

        static float[] MatToQuat(float[] x, float[] y, float[] z)
        {
            // columns x, y, z
            float m00 = x[0], m10 = x[1], m20 = x[2], m01 = y[0], m11 = y[1], m21 = y[2], m02 = z[0], m12 = z[1], m22 = z[2];
            float tr = m00 + m11 + m22;
            float qx, qy, qz, qw;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; qw = 0.25f * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s; }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; qw = (m21 - m12) / s; qx = 0.25f * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s; }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25f * s; qz = (m12 + m21) / s; }
            else { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25f * s; }
            var q = new[] { qx, qy, qz, qw }; Quat.Normalize(q);
            return q;
        }

        // ---------------- picking ----------------

        static bool Pick(GroupObj g, int scope, float[] org, float[] dir, RayObj ri)
        {
            float best = float.MaxValue;
            NodeObj hit = null; int hitSub = 0; float[] hitN = null; float hs = 0, ht = 0;
            PickWalk(g, g, scope, org, dir, ref best, ref hit, ref hitSub, ref hitN, ref hs, ref ht);
            if (hit == null) return false;
            if (ri != null)
            {
                ri.Node = hit; ri.Distance = best; ri.Submesh = hitSub;
                ri.Nx = hitN[0]; ri.Ny = hitN[1]; ri.Nz = hitN[2];
                ri.S0 = hs; ri.T0 = ht;
                ri.Ray = new[] { org[0], org[1], org[2], dir[0], dir[1], dir[2] };
            }
            return true;
        }

        static void PickWalk(NodeObj n, GroupObj root, int scope, float[] org, float[] dir, ref float best, ref NodeObj hit, ref int hitSub, ref float[] hitN, ref float hs, ref float ht)
        {
            if (!n.Pick) return;
            var g = n as GroupObj;
            if (g != null) { foreach (var c in g.Children) PickWalk(c, root, scope, org, dir, ref best, ref hit, ref hitSub, ref hitN, ref hs, ref ht); return; }
            var mesh = n as MeshObj;
            if (mesh == null || (mesh.Scope & scope) == 0) return;
            var m = Renderer.PathTo(mesh, root);
            if (m == null) return;
            var inv = Mat.Inverse(m);
            var lo = new float[4]; var ld = new float[4];
            Mat.Apply(inv, org[0], org[1], org[2], 1, lo, 0);
            Mat.Apply(inv, dir[0], dir[1], dir[2], 0, ld, 0);
            var vd = Renderer.Prepare(mesh);
            if (vd == null) return;
            var p = vd.Pos;
            for (int s = 0; s < mesh.Ib.Length; s++)
            {
                var ib = mesh.Ib[s];
                if (ib == null || mesh.Ap[s] == null) continue;
                var tr = ib.Tris;
                for (int k = 0; k + 2 < tr.Length; k += 3)
                {
                    int a = tr[k] * 3, b = tr[k + 1] * 3, c = tr[k + 2] * 3;
                    if (c + 2 >= p.Length || a + 2 >= p.Length || b + 2 >= p.Length) continue;
                    float e1x = p[b] - p[a], e1y = p[b + 1] - p[a + 1], e1z = p[b + 2] - p[a + 2];
                    float e2x = p[c] - p[a], e2y = p[c + 1] - p[a + 1], e2z = p[c + 2] - p[a + 2];
                    float px = ld[1] * e2z - ld[2] * e2y, py = ld[2] * e2x - ld[0] * e2z, pz = ld[0] * e2y - ld[1] * e2x;
                    float det = e1x * px + e1y * py + e1z * pz;
                    if (Math.Abs(det) < 1e-12f) continue;
                    float id = 1 / det;
                    float sx = lo[0] - p[a], sy = lo[1] - p[a + 1], sz = lo[2] - p[a + 2];
                    float u = (sx * px + sy * py + sz * pz) * id;
                    if (u < 0 || u > 1) continue;
                    float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                    float v = (ld[0] * qx + ld[1] * qy + ld[2] * qz) * id;
                    if (v < 0 || u + v > 1) continue;
                    float tt = (e2x * qx + e2y * qy + e2z * qz) * id;
                    if (tt < 0 || tt >= best) continue;
                    best = tt; hit = mesh; hitSub = s;
                    float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    hitN = nl > 0 ? new[] { nx / nl, ny / nl, nz / nl } : new float[] { 0, 0, 1 };
                    var tc = vd.Tc[0];
                    if (tc != null) { int ta = tr[k] * 3, tb = tr[k + 1] * 3, tcI = tr[k + 2] * 3; float w0 = 1 - u - v; hs = w0 * tc[ta] + u * tc[tb] + v * tc[tcI]; ht = w0 * tc[ta + 1] + u * tc[tb + 1] + v * tc[tcI + 1]; }
                }
            }
        }

        // ---------------- transformable ----------------

        static TransformableObj Tf(JThread t, int b) => (TransformableObj)t.R[b];
        static NodeObj Nd(JThread t, int b) => (NodeObj)t.R[b];

        static void Orientation(TransformableObj o, float[] a)
        {
            Need(a, 4);
            Quat.ToAxisAngle(o.Rot, a);
        }

        public static void Register(NativeRegistry n)
        {
            n.Define(P + "Object3D", "java/lang/Object", c => c
                .M("animate", "(I)I", (t, b) => t.RetI(b, Anim.Animate((Obj3D)t.R[b], t.I(b + 1))))
                .M("duplicate", "()" + O3, (t, b) => t.RetA(b, ((Obj3D)t.R[b]).Clone()))
                .M("find", "(I)" + O3, (t, b) => t.RetA(b, Find((Obj3D)t.R[b], t.I(b + 1))))
                .M("getReferences", "([" + O3 + ")I", (t, b) =>
                {
                    var l = new List<Obj3D>(); ((Obj3D)t.R[b]).Refs(l);
                    var a = t.R[b + 1] as ObjArray;
                    if (a != null) { if (a.Data.Length < l.Count) Iae("array too short"); for (int i = 0; i < l.Count; i++) a.Data[i] = l[i]; }
                    t.RetI(b, l.Count);
                })
                .M("getUserID", "()I", (t, b) => t.RetI(b, ((Obj3D)t.R[b]).UserID))
                .M("setUserID", "(I)V", (t, b) => ((Obj3D)t.R[b]).UserID = t.I(b + 1))
                .M("getUserObject", "()Ljava/lang/Object;", (t, b) => t.RetA(b, ((Obj3D)t.R[b]).UserObject))
                .M("setUserObject", "(Ljava/lang/Object;)V", (t, b) => ((Obj3D)t.R[b]).UserObject = t.R[b + 1])
                .M("addAnimationTrack", "(L" + P + "AnimationTrack;)V", (t, b) =>
                {
                    var o = (Obj3D)t.R[b]; var tr = N<TrackObj>(t, b + 1);
                    if (o.Tracks.Contains(tr)) Iae("track already added");
                    o.Tracks.Add(tr);
                })
                .M("getAnimationTrack", "(I)L" + P + "AnimationTrack;", (t, b) =>
                {
                    var o = (Obj3D)t.R[b]; int i = t.I(b + 1);
                    if (i < 0 || i >= o.Tracks.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    t.RetA(b, o.Tracks[i]);
                })
                .M("getAnimationTrackCount", "()I", (t, b) => t.RetI(b, ((Obj3D)t.R[b]).Tracks.Count))
                .M("removeAnimationTrack", "(L" + P + "AnimationTrack;)V", (t, b) => ((Obj3D)t.R[b]).Tracks.Remove(A<TrackObj>(t, b + 1))),
                null, access: 0x0401);

            n.Define(P + "Transformable", P + "Object3D", c => c
                .M("getCompositeTransform", "(L" + P + "Transform;)V", (t, b) => N<TransformObj>(t, b + 1).M = Tf(t, b).Composite())
                .M("getTransform", "(L" + P + "Transform;)V", (t, b) => N<TransformObj>(t, b + 1).M = (float[])Tf(t, b).M.Clone())
                .M("setTransform", "(L" + P + "Transform;)V", (t, b) => { var tr = A<TransformObj>(t, b + 1); Tf(t, b).M = tr != null ? (float[])tr.M.Clone() : Mat.Identity(); })
                .M("getOrientation", "([F)V", (t, b) => Orientation(Tf(t, b), Fa(t, b + 1)))
                .M("setOrientation", "(FFFF)V", (t, b) => Tf(t, b).Rot = Quat.FromAxisAngle(t.F(b + 1), t.F(b + 2), t.F(b + 3), t.F(b + 4)))
                .M("preRotate", "(FFFF)V", (t, b) => { var o = Tf(t, b); o.Rot = Quat.Mul(Quat.FromAxisAngle(t.F(b + 1), t.F(b + 2), t.F(b + 3), t.F(b + 4)), o.Rot); Quat.Normalize(o.Rot); })
                .M("postRotate", "(FFFF)V", (t, b) => { var o = Tf(t, b); o.Rot = Quat.Mul(o.Rot, Quat.FromAxisAngle(t.F(b + 1), t.F(b + 2), t.F(b + 3), t.F(b + 4))); Quat.Normalize(o.Rot); })
                .M("getScale", "([F)V", (t, b) => { var a = Fa(t, b + 1); Need(a, 3); var o = Tf(t, b); a[0] = o.Sx; a[1] = o.Sy; a[2] = o.Sz; })
                .M("setScale", "(FFF)V", (t, b) => { var o = Tf(t, b); o.Sx = t.F(b + 1); o.Sy = t.F(b + 2); o.Sz = t.F(b + 3); })
                .M("scale", "(FFF)V", (t, b) => { var o = Tf(t, b); o.Sx *= t.F(b + 1); o.Sy *= t.F(b + 2); o.Sz *= t.F(b + 3); })
                .M("getTranslation", "([F)V", (t, b) => { var a = Fa(t, b + 1); Need(a, 3); var o = Tf(t, b); a[0] = o.Tx; a[1] = o.Ty; a[2] = o.Tz; })
                .M("setTranslation", "(FFF)V", (t, b) => { var o = Tf(t, b); o.Tx = t.F(b + 1); o.Ty = t.F(b + 2); o.Tz = t.F(b + 3); })
                .M("translate", "(FFF)V", (t, b) => { var o = Tf(t, b); o.Tx += t.F(b + 1); o.Ty += t.F(b + 2); o.Tz += t.F(b + 3); }),
                null, access: 0x0401);

            n.Define(P + "Node", P + "Transformable", c => c
                .Static("NONE", "I", t => 144).Static("ORIGIN", "I", t => 145).Static("X_AXIS", "I", t => 146).Static("Y_AXIS", "I", t => 147).Static("Z_AXIS", "I", t => 148)
                .M("align", "(L" + P + "Node;)V", (t, b) => Align(Nd(t, b), A<NodeObj>(t, b + 1)))
                .M("setAlignment", "(L" + P + "Node;IL" + P + "Node;I)V", (t, b) =>
                {
                    var o = Nd(t, b);
                    o.ZRef = A<NodeObj>(t, b + 1); o.ZTarget = t.I(b + 2); o.YRef = A<NodeObj>(t, b + 3); o.YTarget = t.I(b + 4);
                })
                .M("getAlignmentReference", "(I)L" + P + "Node;", (t, b) => t.RetA(b, t.I(b + 1) == 148 ? Nd(t, b).ZRef : Nd(t, b).YRef))
                .M("getAlignmentTarget", "(I)I", (t, b) => t.RetI(b, t.I(b + 1) == 148 ? Nd(t, b).ZTarget : Nd(t, b).YTarget))
                .M("getAlphaFactor", "()F", (t, b) => t.RetF(b, Nd(t, b).Alpha))
                .M("setAlphaFactor", "(F)V", (t, b) => { float a = t.F(b + 1); if (a < 0 || a > 1) Iae("alpha"); Nd(t, b).Alpha = a; })
                .M("getParent", "()L" + P + "Node;", (t, b) => t.RetA(b, Nd(t, b).Parent))
                .M("getScope", "()I", (t, b) => t.RetI(b, Nd(t, b).Scope))
                .M("setScope", "(I)V", (t, b) => Nd(t, b).Scope = t.I(b + 1))
                .M("getTransformTo", "(L" + P + "Node;L" + P + "Transform;)Z", (t, b) =>
                {
                    var target = N<NodeObj>(t, b + 1); var tr = N<TransformObj>(t, b + 2);
                    t.RetZ(b, TransformTo(Nd(t, b), target, tr.M));
                })
                .M("isPickingEnabled", "()Z", (t, b) => t.RetZ(b, Nd(t, b).Pick))
                .M("setPickingEnable", "(Z)V", (t, b) => Nd(t, b).Pick = t.Z(b + 1))
                .M("isRenderingEnabled", "()Z", (t, b) => t.RetZ(b, Nd(t, b).Render))
                .M("setRenderingEnable", "(Z)V", (t, b) => Nd(t, b).Render = t.Z(b + 1)),
                null, access: 0x0401);

            n.Define(P + "Group", P + "Node", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("addChild", "(L" + P + "Node;)V", (t, b) => AddChild((GroupObj)t.R[b], A<NodeObj>(t, b + 1)))
                .M("removeChild", "(L" + P + "Node;)V", (t, b) => RemoveChild((GroupObj)t.R[b], A<NodeObj>(t, b + 1)))
                .M("getChild", "(I)L" + P + "Node;", (t, b) =>
                {
                    var g = (GroupObj)t.R[b]; int i = t.I(b + 1);
                    if (i < 0 || i >= g.Children.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    t.RetA(b, g.Children[i]);
                })
                .M("getChildCount", "()I", (t, b) => t.RetI(b, ((GroupObj)t.R[b]).Children.Count))
                .M("pick", "(IFFL" + P + "Camera;L" + P + "RayIntersection;)Z", (t, b) =>
                {
                    var g = (GroupObj)t.R[b]; var cam = N<CameraObj>(t, b + 4); var ri = A<RayObj>(t, b + 5);
                    float x = t.F(b + 2), y = t.F(b + 3);
                    var inv = Mat.Inverse(cam.Projection());
                    var nearP = new float[4]; var farP = new float[4];
                    Mat.Apply(inv, 2 * x - 1, 1 - 2 * y, -1, 1, nearP, 0);
                    Mat.Apply(inv, 2 * x - 1, 1 - 2 * y, 1, 1, farP, 0);
                    for (int i = 0; i < 3; i++) { nearP[i] /= nearP[3]; farP[i] /= farP[3]; }
                    var cm = new float[16];
                    if (!TransformTo(cam, g, cm)) throw new JavaThrow("java/lang/IllegalStateException", "camera not in group's tree");
                    var o = new float[4]; var f = new float[4];
                    Mat.Apply(cm, nearP[0], nearP[1], nearP[2], 1, o, 0);
                    Mat.Apply(cm, farP[0], farP[1], farP[2], 1, f, 0);
                    t.RetZ(b, Pick(g, t.I(b + 1), new[] { o[0], o[1], o[2] }, new[] { f[0] - o[0], f[1] - o[1], f[2] - o[2] }, ri));
                })
                .M("pick", "(IFFFFFFL" + P + "RayIntersection;)Z", (t, b) =>
                {
                    float dx = t.F(b + 5), dy = t.F(b + 6), dz = t.F(b + 7);
                    if (dx == 0 && dy == 0 && dz == 0) Iae("zero direction");
                    t.RetZ(b, Pick((GroupObj)t.R[b], t.I(b + 1), new[] { t.F(b + 2), t.F(b + 3), t.F(b + 4) }, new[] { dx, dy, dz }, A<RayObj>(t, b + 8)));
                }),
                Rt.F<GroupObj>());

            n.Define(P + "World", P + "Group", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getActiveCamera", "()L" + P + "Camera;", (t, b) => t.RetA(b, ((WorldObj)t.R[b]).Camera))
                .M("setActiveCamera", "(L" + P + "Camera;)V", (t, b) => ((WorldObj)t.R[b]).Camera = N<CameraObj>(t, b + 1))
                .M("getBackground", "()L" + P + "Background;", (t, b) => t.RetA(b, ((WorldObj)t.R[b]).Background))
                .M("setBackground", "(L" + P + "Background;)V", (t, b) => ((WorldObj)t.R[b]).Background = A<BackgroundObj>(t, b + 1)),
                Rt.F<WorldObj>());

            n.Define(P + "Camera", P + "Node", c => c
                .Static("GENERIC", "I", t => 48).Static("PARALLEL", "I", t => 49).Static("PERSPECTIVE", "I", t => 50)
                .M("<init>", "()V", (t, b) => { })
                .M("setPerspective", "(FFFF)V", (t, b) =>
                {
                    var o = (CameraObj)t.R[b];
                    float fov = t.F(b + 1), asp = t.F(b + 2), nr = t.F(b + 3), fr = t.F(b + 4);
                    if (fov <= 0 || fov >= 180 || asp <= 0 || nr <= 0 || fr <= 0 || nr == fr) Iae("perspective");
                    o.Type = CameraObj.PERSPECTIVE; o.Fovy = fov; o.Aspect = asp; o.Near = nr; o.Far = fr;
                })
                .M("setParallel", "(FFFF)V", (t, b) =>
                {
                    var o = (CameraObj)t.R[b];
                    float h = t.F(b + 1), asp = t.F(b + 2), nr = t.F(b + 3), fr = t.F(b + 4);
                    if (h <= 0 || asp <= 0 || nr == fr) Iae("parallel");
                    o.Type = CameraObj.PARALLEL; o.Fovy = h; o.Aspect = asp; o.Near = nr; o.Far = fr;
                })
                .M("setGeneric", "(L" + P + "Transform;)V", (t, b) => { var o = (CameraObj)t.R[b]; o.Type = CameraObj.GENERIC; o.Proj = (float[])N<TransformObj>(t, b + 1).M.Clone(); })
                .M("getProjection", "(L" + P + "Transform;)I", (t, b) => { var o = (CameraObj)t.R[b]; var tr = A<TransformObj>(t, b + 1); if (tr != null) tr.M = o.Projection(); t.RetI(b, o.Type); })
                .M("getProjection", "([F)I", (t, b) =>
                {
                    var o = (CameraObj)t.R[b]; var a = Fa(t, b + 1);
                    if (a != null && o.Type != CameraObj.GENERIC) { Need(a, 4); a[0] = o.Fovy; a[1] = o.Aspect; a[2] = o.Near; a[3] = o.Far; }
                    t.RetI(b, o.Type);
                }),
                Rt.F<CameraObj>());

            n.Define(P + "Light", P + "Node", c => c
                .Static("AMBIENT", "I", t => 128).Static("DIRECTIONAL", "I", t => 129).Static("OMNI", "I", t => 130).Static("SPOT", "I", t => 131)
                .M("<init>", "()V", (t, b) => { })
                .M("getColor", "()I", (t, b) => t.RetI(b, ((LightObj)t.R[b]).Color))
                .M("setColor", "(I)V", (t, b) => ((LightObj)t.R[b]).Color = t.I(b + 1) & 0xFFFFFF)
                .M("getIntensity", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).Intensity))
                .M("setIntensity", "(F)V", (t, b) => ((LightObj)t.R[b]).Intensity = t.F(b + 1))
                .M("getMode", "()I", (t, b) => t.RetI(b, ((LightObj)t.R[b]).Mode))
                .M("setMode", "(I)V", (t, b) => { int m = t.I(b + 1); if (m < 128 || m > 131) Iae("mode"); ((LightObj)t.R[b]).Mode = m; })
                .M("setAttenuation", "(FFF)V", (t, b) => { var o = (LightObj)t.R[b]; o.AttC = t.F(b + 1); o.AttL = t.F(b + 2); o.AttQ = t.F(b + 3); })
                .M("getConstantAttenuation", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).AttC))
                .M("getLinearAttenuation", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).AttL))
                .M("getQuadraticAttenuation", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).AttQ))
                .M("getSpotAngle", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).SpotAngle))
                .M("setSpotAngle", "(F)V", (t, b) => ((LightObj)t.R[b]).SpotAngle = t.F(b + 1))
                .M("getSpotExponent", "()F", (t, b) => t.RetF(b, ((LightObj)t.R[b]).SpotExp))
                .M("setSpotExponent", "(F)V", (t, b) => ((LightObj)t.R[b]).SpotExp = t.F(b + 1)),
                Rt.F<LightObj>());

            NativeFn meshInit1 = (t, b) =>
            {
                var m = (MeshObj)t.R[b];
                m.Vb = N<VertexBufferObj>(t, b + 1);
                m.Ib = new[] { N<IndexBufferObj>(t, b + 2) };
                m.Ap = new[] { A<AppearanceObj>(t, b + 3) };
            };
            NativeFn meshInitN = (t, b) =>
            {
                var m = (MeshObj)t.R[b];
                m.Vb = N<VertexBufferObj>(t, b + 1);
                var ibs = N<ObjArray>(t, b + 2); var aps = A<ObjArray>(t, b + 3);
                if (ibs.Data.Length == 0 || (aps != null && aps.Data.Length < ibs.Data.Length)) Iae("submeshes");
                m.Ib = new IndexBufferObj[ibs.Data.Length]; m.Ap = new AppearanceObj[ibs.Data.Length];
                for (int i = 0; i < m.Ib.Length; i++)
                {
                    m.Ib[i] = (IndexBufferObj)ibs.Data[i] ?? throw new NullReferenceException();
                    m.Ap[i] = aps != null ? (AppearanceObj)aps.Data[i] : null;
                }
            };
            string VB = "L" + P + "VertexBuffer;", IB = "L" + P + "IndexBuffer;", AP = "L" + P + "Appearance;";

            n.Define(P + "Mesh", P + "Node", c => c
                .M("<init>", "(" + VB + IB + AP + ")V", meshInit1)
                .M("<init>", "(" + VB + "[" + IB + "[" + AP + ")V", meshInitN)
                .M("getAppearance", "(I)" + AP, (t, b) => { var m = (MeshObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= m.Ap.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, m.Ap[i]); })
                .M("setAppearance", "(I" + AP + ")V", (t, b) => { var m = (MeshObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= m.Ap.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); m.Ap[i] = A<AppearanceObj>(t, b + 2); })
                .M("getIndexBuffer", "(I)" + IB, (t, b) => { var m = (MeshObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= m.Ib.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, m.Ib[i]); })
                .M("getSubmeshCount", "()I", (t, b) => t.RetI(b, ((MeshObj)t.R[b]).Ib.Length))
                .M("getVertexBuffer", "()" + VB, (t, b) => t.RetA(b, ((MeshObj)t.R[b]).Vb)),
                Rt.F<MeshObj>());

            n.Define(P + "MorphingMesh", P + "Mesh", c => c
                .M("<init>", "(" + VB + "[" + VB + IB + AP + ")V", (t, b) =>
                {
                    var m = (MorphingMeshObj)t.R[b];
                    m.Vb = N<VertexBufferObj>(t, b + 1); SetTargets(m, N<ObjArray>(t, b + 2));
                    m.Ib = new[] { N<IndexBufferObj>(t, b + 3) }; m.Ap = new[] { A<AppearanceObj>(t, b + 4) };
                })
                .M("<init>", "(" + VB + "[" + VB + "[" + IB + "[" + AP + ")V", (t, b) =>
                {
                    var m = (MorphingMeshObj)t.R[b];
                    var tg = N<ObjArray>(t, b + 2);
                    t.R[b + 2] = t.R[b + 3]; t.R[b + 3] = t.R[b + 4];
                    meshInitN(t, b);
                    SetTargets(m, tg);
                })
                .M("getMorphTarget", "(I)" + VB, (t, b) => { var m = (MorphingMeshObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= m.Targets.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, m.Targets[i]); })
                .M("getMorphTargetCount", "()I", (t, b) => t.RetI(b, ((MorphingMeshObj)t.R[b]).Targets.Length))
                .M("getWeights", "([F)V", (t, b) => { var m = (MorphingMeshObj)t.R[b]; var a = Fa(t, b + 1); Need(a, m.Weights.Length); Array.Copy(m.Weights, a, m.Weights.Length); })
                .M("setWeights", "([F)V", (t, b) => { var m = (MorphingMeshObj)t.R[b]; var a = Fn(t, b + 1); if (a.Length < m.Weights.Length) Iae("weights"); Array.Copy(a, m.Weights, m.Weights.Length); }),
                Rt.F<MorphingMeshObj>());

            n.Define(P + "SkinnedMesh", P + "Mesh", c => c
                .M("<init>", "(" + VB + IB + AP + "L" + P + "Group;)V", (t, b) =>
                {
                    var sk = N<GroupObj>(t, b + 4);
                    meshInit1(t, b);
                    SetSkeleton((SkinnedMeshObj)t.R[b], sk);
                })
                .M("<init>", "(" + VB + "[" + IB + "[" + AP + "L" + P + "Group;)V", (t, b) =>
                {
                    var sk = N<GroupObj>(t, b + 4);
                    meshInitN(t, b);
                    SetSkeleton((SkinnedMeshObj)t.R[b], sk);
                })
                .M("addTransform", "(L" + P + "Node;III)V", (t, b) =>
                {
                    var m = (SkinnedMeshObj)t.R[b];
                    var node = N<NodeObj>(t, b + 1);
                    int w = t.I(b + 2), first = t.I(b + 3), cnt = t.I(b + 4);
                    if (w <= 0 || cnt <= 0 || first < 0) Iae("bone");
                    var bone = new Bone { Node = node, Weight = w, First = first, Count = cnt };
                    Renderer.ComputeRest(m, bone);
                    m.Bones.Add(bone);
                })
                .M("getSkeleton", "()L" + P + "Group;", (t, b) => t.RetA(b, ((SkinnedMeshObj)t.R[b]).Skeleton))
                .M("getBoneTransform", "(L" + P + "Node;L" + P + "Transform;)V", (t, b) =>
                {
                    var m = (SkinnedMeshObj)t.R[b]; var node = N<NodeObj>(t, b + 1); var tr = N<TransformObj>(t, b + 2);
                    foreach (var bn in m.Bones) if (bn.Node == node) { tr.M = Mat.Inverse(bn.RestInv); return; }
                    Iae("not a bone");
                }),
                Rt.F<SkinnedMeshObj>());

            n.Define(P + "Sprite3D", P + "Node", c => c
                .M("<init>", "(ZL" + P + "Image2D;" + AP + ")V", (t, b) =>
                {
                    var s = (Sprite3DObj)t.R[b];
                    s.Scaled = t.Z(b + 1); SetSpriteImage(s, N<Image2DObj>(t, b + 2)); s.Ap = A<AppearanceObj>(t, b + 3);
                })
                .M("getAppearance", "()" + AP, (t, b) => t.RetA(b, ((Sprite3DObj)t.R[b]).Ap))
                .M("setAppearance", "(" + AP + ")V", (t, b) => ((Sprite3DObj)t.R[b]).Ap = A<AppearanceObj>(t, b + 1))
                .M("getImage", "()L" + P + "Image2D;", (t, b) => t.RetA(b, ((Sprite3DObj)t.R[b]).Image))
                .M("setImage", "(L" + P + "Image2D;)V", (t, b) => SetSpriteImage((Sprite3DObj)t.R[b], N<Image2DObj>(t, b + 1)))
                .M("isScaled", "()Z", (t, b) => t.RetZ(b, ((Sprite3DObj)t.R[b]).Scaled))
                .M("setCrop", "(IIII)V", (t, b) => { var s = (Sprite3DObj)t.R[b]; s.Cx = t.I(b + 1); s.Cy = t.I(b + 2); s.Cw = t.I(b + 3); s.Ch = t.I(b + 4); })
                .M("getCropX", "()I", (t, b) => t.RetI(b, ((Sprite3DObj)t.R[b]).Cx))
                .M("getCropY", "()I", (t, b) => t.RetI(b, ((Sprite3DObj)t.R[b]).Cy))
                .M("getCropWidth", "()I", (t, b) => t.RetI(b, ((Sprite3DObj)t.R[b]).Cw))
                .M("getCropHeight", "()I", (t, b) => t.RetI(b, ((Sprite3DObj)t.R[b]).Ch)),
                Rt.F<Sprite3DObj>());

            n.Define(P + "Background", P + "Object3D", c => c
                .Static("BORDER", "I", t => 32).Static("REPEAT", "I", t => 33)
                .M("<init>", "()V", (t, b) => { })
                .M("getColor", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).Color))
                .M("setColor", "(I)V", (t, b) => ((BackgroundObj)t.R[b]).Color = t.I(b + 1))
                .M("getImage", "()L" + P + "Image2D;", (t, b) => t.RetA(b, ((BackgroundObj)t.R[b]).Image))
                .M("setImage", "(L" + P + "Image2D;)V", (t, b) =>
                {
                    var o = (BackgroundObj)t.R[b]; var im = A<Image2DObj>(t, b + 1);
                    o.Image = im;
                    if (im != null) { o.Cx = 0; o.Cy = 0; o.Cw = im.W; o.Ch = im.H; }
                })
                .M("setImageMode", "(II)V", (t, b) => { var o = (BackgroundObj)t.R[b]; o.ModeX = t.I(b + 1); o.ModeY = t.I(b + 2); })
                .M("getImageModeX", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).ModeX))
                .M("getImageModeY", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).ModeY))
                .M("setCrop", "(IIII)V", (t, b) =>
                {
                    var o = (BackgroundObj)t.R[b];
                    if (t.I(b + 3) < 0 || t.I(b + 4) < 0) Iae("crop");
                    o.Cx = t.I(b + 1); o.Cy = t.I(b + 2); o.Cw = t.I(b + 3); o.Ch = t.I(b + 4);
                })
                .M("getCropX", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).Cx))
                .M("getCropY", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).Cy))
                .M("getCropWidth", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).Cw))
                .M("getCropHeight", "()I", (t, b) => t.RetI(b, ((BackgroundObj)t.R[b]).Ch))
                .M("isColorClearEnabled", "()Z", (t, b) => t.RetZ(b, ((BackgroundObj)t.R[b]).ColorClear))
                .M("setColorClearEnable", "(Z)V", (t, b) => ((BackgroundObj)t.R[b]).ColorClear = t.Z(b + 1))
                .M("isDepthClearEnabled", "()Z", (t, b) => t.RetZ(b, ((BackgroundObj)t.R[b]).DepthClear))
                .M("setDepthClearEnable", "(Z)V", (t, b) => ((BackgroundObj)t.R[b]).DepthClear = t.Z(b + 1)),
                Rt.F<BackgroundObj>());

            n.Define(P + "Appearance", P + "Object3D", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getLayer", "()I", (t, b) => t.RetI(b, ((AppearanceObj)t.R[b]).Layer))
                .M("setLayer", "(I)V", (t, b) => { int l = t.I(b + 1); if (l < -63 || l > 63) Iae("layer"); ((AppearanceObj)t.R[b]).Layer = l; })
                .M("getCompositingMode", "()L" + P + "CompositingMode;", (t, b) => t.RetA(b, ((AppearanceObj)t.R[b]).Comp))
                .M("setCompositingMode", "(L" + P + "CompositingMode;)V", (t, b) => ((AppearanceObj)t.R[b]).Comp = A<CompositingObj>(t, b + 1))
                .M("getFog", "()L" + P + "Fog;", (t, b) => t.RetA(b, ((AppearanceObj)t.R[b]).Fog))
                .M("setFog", "(L" + P + "Fog;)V", (t, b) => ((AppearanceObj)t.R[b]).Fog = A<FogObj>(t, b + 1))
                .M("getMaterial", "()L" + P + "Material;", (t, b) => t.RetA(b, ((AppearanceObj)t.R[b]).Mat))
                .M("setMaterial", "(L" + P + "Material;)V", (t, b) => ((AppearanceObj)t.R[b]).Mat = A<MaterialObj>(t, b + 1))
                .M("getPolygonMode", "()L" + P + "PolygonMode;", (t, b) => t.RetA(b, ((AppearanceObj)t.R[b]).Poly))
                .M("setPolygonMode", "(L" + P + "PolygonMode;)V", (t, b) => ((AppearanceObj)t.R[b]).Poly = A<PolygonObj>(t, b + 1))
                .M("getTexture", "(I)L" + P + "Texture2D;", (t, b) => { var a = (AppearanceObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= a.Tex.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, a.Tex[i]); })
                .M("setTexture", "(IL" + P + "Texture2D;)V", (t, b) => { var a = (AppearanceObj)t.R[b]; int i = t.I(b + 1); if (i < 0 || i >= a.Tex.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); a.Tex[i] = A<TextureObj>(t, b + 2); }),
                Rt.F<AppearanceObj>());

            n.Define(P + "CompositingMode", P + "Object3D", c => c
                .Static("ALPHA", "I", t => 64).Static("ALPHA_ADD", "I", t => 65).Static("MODULATE", "I", t => 66).Static("MODULATE_X2", "I", t => 67).Static("REPLACE", "I", t => 68)
                .M("<init>", "()V", (t, b) => { })
                .M("getBlending", "()I", (t, b) => t.RetI(b, ((CompositingObj)t.R[b]).Blending))
                .M("setBlending", "(I)V", (t, b) => { int m = t.I(b + 1); if (m < 64 || m > 68) Iae("blending"); ((CompositingObj)t.R[b]).Blending = m; })
                .M("getAlphaThreshold", "()F", (t, b) => t.RetF(b, ((CompositingObj)t.R[b]).Threshold))
                .M("setAlphaThreshold", "(F)V", (t, b) => { float v = t.F(b + 1); if (v < 0 || v > 1) Iae("threshold"); ((CompositingObj)t.R[b]).Threshold = v; })
                .M("setDepthOffset", "(FF)V", (t, b) => { var o = (CompositingObj)t.R[b]; o.OffFactor = t.F(b + 1); o.OffUnits = t.F(b + 2); })
                .M("getDepthOffsetFactor", "()F", (t, b) => t.RetF(b, ((CompositingObj)t.R[b]).OffFactor))
                .M("getDepthOffsetUnits", "()F", (t, b) => t.RetF(b, ((CompositingObj)t.R[b]).OffUnits))
                .M("isDepthTestEnabled", "()Z", (t, b) => t.RetZ(b, ((CompositingObj)t.R[b]).DepthTest))
                .M("setDepthTestEnable", "(Z)V", (t, b) => ((CompositingObj)t.R[b]).DepthTest = t.Z(b + 1))
                .M("isDepthWriteEnabled", "()Z", (t, b) => t.RetZ(b, ((CompositingObj)t.R[b]).DepthWrite))
                .M("setDepthWriteEnable", "(Z)V", (t, b) => ((CompositingObj)t.R[b]).DepthWrite = t.Z(b + 1))
                .M("isColorWriteEnabled", "()Z", (t, b) => t.RetZ(b, ((CompositingObj)t.R[b]).ColorWrite))
                .M("setColorWriteEnable", "(Z)V", (t, b) => ((CompositingObj)t.R[b]).ColorWrite = t.Z(b + 1))
                .M("isAlphaWriteEnabled", "()Z", (t, b) => t.RetZ(b, ((CompositingObj)t.R[b]).AlphaWrite))
                .M("setAlphaWriteEnable", "(Z)V", (t, b) => ((CompositingObj)t.R[b]).AlphaWrite = t.Z(b + 1)),
                Rt.F<CompositingObj>());

            n.Define(P + "PolygonMode", P + "Object3D", c => c
                .Static("CULL_BACK", "I", t => 160).Static("CULL_FRONT", "I", t => 161).Static("CULL_NONE", "I", t => 162)
                .Static("SHADE_FLAT", "I", t => 164).Static("SHADE_SMOOTH", "I", t => 165).Static("WINDING_CCW", "I", t => 168).Static("WINDING_CW", "I", t => 169)
                .M("<init>", "()V", (t, b) => { })
                .M("getCulling", "()I", (t, b) => t.RetI(b, ((PolygonObj)t.R[b]).Culling))
                .M("setCulling", "(I)V", (t, b) => { int m = t.I(b + 1); if (m < 160 || m > 162) Iae("culling"); ((PolygonObj)t.R[b]).Culling = m; })
                .M("getShading", "()I", (t, b) => t.RetI(b, ((PolygonObj)t.R[b]).Shading))
                .M("setShading", "(I)V", (t, b) => { int m = t.I(b + 1); if (m != 164 && m != 165) Iae("shading"); ((PolygonObj)t.R[b]).Shading = m; })
                .M("getWinding", "()I", (t, b) => t.RetI(b, ((PolygonObj)t.R[b]).Winding))
                .M("setWinding", "(I)V", (t, b) => { int m = t.I(b + 1); if (m != 168 && m != 169) Iae("winding"); ((PolygonObj)t.R[b]).Winding = m; })
                .M("isTwoSidedLightingEnabled", "()Z", (t, b) => t.RetZ(b, ((PolygonObj)t.R[b]).TwoSided))
                .M("setTwoSidedLightingEnable", "(Z)V", (t, b) => ((PolygonObj)t.R[b]).TwoSided = t.Z(b + 1))
                .M("isLocalCameraLightingEnabled", "()Z", (t, b) => t.RetZ(b, ((PolygonObj)t.R[b]).LocalCamera))
                .M("setLocalCameraLightingEnable", "(Z)V", (t, b) => ((PolygonObj)t.R[b]).LocalCamera = t.Z(b + 1))
                .M("isPerspectiveCorrectionEnabled", "()Z", (t, b) => t.RetZ(b, ((PolygonObj)t.R[b]).Perspective))
                .M("setPerspectiveCorrectionEnable", "(Z)V", (t, b) => ((PolygonObj)t.R[b]).Perspective = t.Z(b + 1)),
                Rt.F<PolygonObj>());

            n.Define(P + "Material", P + "Object3D", c => c
                .Static("AMBIENT", "I", t => 1024).Static("DIFFUSE", "I", t => 2048).Static("EMISSIVE", "I", t => 4096).Static("SPECULAR", "I", t => 8192)
                .M("<init>", "()V", (t, b) => { })
                .M("setColor", "(II)V", (t, b) =>
                {
                    var m = (MaterialObj)t.R[b]; int tg = t.I(b + 1), col = t.I(b + 2);
                    if ((tg & ~0x3C00) != 0 || tg == 0) Iae("target");
                    if ((tg & 1024) != 0) m.Ambient = col & 0xFFFFFF;
                    if ((tg & 2048) != 0) m.Diffuse = col;
                    if ((tg & 4096) != 0) m.Emissive = col & 0xFFFFFF;
                    if ((tg & 8192) != 0) m.Specular = col & 0xFFFFFF;
                })
                .M("getColor", "(I)I", (t, b) =>
                {
                    var m = (MaterialObj)t.R[b];
                    switch (t.I(b + 1))
                    {
                        case 1024: t.RetI(b, m.Ambient); break;
                        case 2048: t.RetI(b, m.Diffuse); break;
                        case 4096: t.RetI(b, m.Emissive); break;
                        case 8192: t.RetI(b, m.Specular); break;
                        default: Iae("target"); break;
                    }
                })
                .M("getShininess", "()F", (t, b) => t.RetF(b, ((MaterialObj)t.R[b]).Shininess))
                .M("setShininess", "(F)V", (t, b) => { float s = t.F(b + 1); if (s < 0 || s > 128) Iae("shininess"); ((MaterialObj)t.R[b]).Shininess = s; })
                .M("isVertexColorTrackingEnabled", "()Z", (t, b) => t.RetZ(b, ((MaterialObj)t.R[b]).Tracking))
                .M("setVertexColorTrackingEnable", "(Z)V", (t, b) => ((MaterialObj)t.R[b]).Tracking = t.Z(b + 1)),
                Rt.F<MaterialObj>());

            n.Define(P + "Fog", P + "Object3D", c => c
                .Static("EXPONENTIAL", "I", t => 80).Static("LINEAR", "I", t => 81)
                .M("<init>", "()V", (t, b) => { })
                .M("getColor", "()I", (t, b) => t.RetI(b, ((FogObj)t.R[b]).Color))
                .M("setColor", "(I)V", (t, b) => ((FogObj)t.R[b]).Color = t.I(b + 1) & 0xFFFFFF)
                .M("getDensity", "()F", (t, b) => t.RetF(b, ((FogObj)t.R[b]).Density))
                .M("setDensity", "(F)V", (t, b) => { float d = t.F(b + 1); if (d < 0) Iae("density"); ((FogObj)t.R[b]).Density = d; })
                .M("setLinear", "(FF)V", (t, b) => { var f = (FogObj)t.R[b]; f.Near = t.F(b + 1); f.Far = t.F(b + 2); })
                .M("getNearDistance", "()F", (t, b) => t.RetF(b, ((FogObj)t.R[b]).Near))
                .M("getFarDistance", "()F", (t, b) => t.RetF(b, ((FogObj)t.R[b]).Far))
                .M("getMode", "()I", (t, b) => t.RetI(b, ((FogObj)t.R[b]).Mode))
                .M("setMode", "(I)V", (t, b) => { int m = t.I(b + 1); if (m != 80 && m != 81) Iae("mode"); ((FogObj)t.R[b]).Mode = m; }),
                Rt.F<FogObj>());

            n.Define(P + "Image2D", P + "Object3D", c => c
                .Static("ALPHA", "I", t => 96).Static("LUMINANCE", "I", t => 97).Static("LUMINANCE_ALPHA", "I", t => 98).Static("RGB", "I", t => 99).Static("RGBA", "I", t => 100)
                .M("<init>", "(ILjava/lang/Object;)V", (t, b) =>
                {
                    var o = (Image2DObj)t.R[b]; int f = Fmt(t.I(b + 1));
                    var src = t.R[b + 2];
                    if (src == null) throw new NullReferenceException();
                    Surface s = (src as ImageObj)?.S;
                    var im2 = src as Image2DObj;
                    o.Format = f;
                    if (s != null) { o.W = s.W; o.H = s.H; o.Pix = new int[s.W * s.H]; o.SetArgb(s.Pix, s.W, s.H); }
                    else if (im2 != null) { o.W = im2.W; o.H = im2.H; o.Pix = new int[o.W * o.H]; o.SetArgb(im2.Pix, o.W, o.H); }
                    else Iae("unsupported image");
                })
                .M("<init>", "(III)V", (t, b) =>
                {
                    var o = (Image2DObj)t.R[b]; o.Format = Fmt(t.I(b + 1)); o.W = t.I(b + 2); o.H = t.I(b + 3);
                    if (o.W <= 0 || o.H <= 0) Iae("size");
                    o.Mutable = true; o.Pix = new int[o.W * o.H];
                    for (int i = 0; i < o.Pix.Length; i++) o.Pix[i] = Image2DObj.Pack(o.Format, 255, 255, 255, 255);
                })
                .M("<init>", "(III[B)V", (t, b) =>
                {
                    var o = (Image2DObj)t.R[b]; o.Format = Fmt(t.I(b + 1)); o.W = t.I(b + 2); o.H = t.I(b + 3);
                    var px = N<sbyte[]>(t, b + 4);
                    if (o.W <= 0 || o.H <= 0) Iae("size");
                    if (px.Length < o.W * o.H * Image2DObj.Bpp(o.Format)) Iae("pixels too short");
                    o.Pix = new int[o.W * o.H];
                    o.SetBytes(0, 0, o.W, o.H, (byte[])(Array)px, 0);
                })
                .M("<init>", "(III[B[B)V", (t, b) =>
                {
                    var o = (Image2DObj)t.R[b]; o.Format = Fmt(t.I(b + 1)); o.W = t.I(b + 2); o.H = t.I(b + 3);
                    var px = N<sbyte[]>(t, b + 4); var pal = N<sbyte[]>(t, b + 5);
                    if (o.W <= 0 || o.H <= 0) Iae("size");
                    if (px.Length < o.W * o.H) Iae("pixels too short");
                    o.Pix = new int[o.W * o.H];
                    o.SetPalette((byte[])(Array)px, 0, (byte[])(Array)pal, 0);
                })
                .M("getFormat", "()I", (t, b) => t.RetI(b, ((Image2DObj)t.R[b]).Format))
                .M("getWidth", "()I", (t, b) => t.RetI(b, ((Image2DObj)t.R[b]).W))
                .M("getHeight", "()I", (t, b) => t.RetI(b, ((Image2DObj)t.R[b]).H))
                .M("isMutable", "()Z", (t, b) => t.RetZ(b, ((Image2DObj)t.R[b]).Mutable))
                .M("set", "(IIII[B)V", (t, b) =>
                {
                    var o = (Image2DObj)t.R[b];
                    if (!o.Mutable) throw new JavaThrow("java/lang/IllegalStateException", "immutable");
                    int x = t.I(b + 1), y = t.I(b + 2), w = t.I(b + 3), h = t.I(b + 4);
                    var px = N<sbyte[]>(t, b + 5);
                    if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > o.W || y + h > o.H) Iae("region");
                    if (px.Length < w * h * Image2DObj.Bpp(o.Format)) Iae("pixels too short");
                    o.SetBytes(x, y, w, h, (byte[])(Array)px, 0);
                }),
                Rt.F<Image2DObj>());

            n.Define(P + "Texture2D", P + "Transformable", c => c
                .Static("FILTER_BASE_LEVEL", "I", t => 208).Static("FILTER_LINEAR", "I", t => 209).Static("FILTER_NEAREST", "I", t => 210)
                .Static("FUNC_ADD", "I", t => 224).Static("FUNC_BLEND", "I", t => 225).Static("FUNC_DECAL", "I", t => 226).Static("FUNC_MODULATE", "I", t => 227).Static("FUNC_REPLACE", "I", t => 228)
                .Static("WRAP_CLAMP", "I", t => 240).Static("WRAP_REPEAT", "I", t => 241)
                .M("<init>", "(L" + P + "Image2D;)V", (t, b) => ((TextureObj)t.R[b]).Image = N<Image2DObj>(t, b + 1))
                .M("getImage", "()L" + P + "Image2D;", (t, b) => t.RetA(b, ((TextureObj)t.R[b]).Image))
                .M("setImage", "(L" + P + "Image2D;)V", (t, b) => ((TextureObj)t.R[b]).Image = N<Image2DObj>(t, b + 1))
                .M("getBlending", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).Blending))
                .M("setBlending", "(I)V", (t, b) => { int m = t.I(b + 1); if (m < 224 || m > 228) Iae("func"); ((TextureObj)t.R[b]).Blending = m; })
                .M("getBlendColor", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).BlendColor))
                .M("setBlendColor", "(I)V", (t, b) => ((TextureObj)t.R[b]).BlendColor = t.I(b + 1) & 0xFFFFFF)
                .M("setFiltering", "(II)V", (t, b) => { var o = (TextureObj)t.R[b]; o.LevelFilter = t.I(b + 1); o.ImageFilter = t.I(b + 2); })
                .M("getLevelFilter", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).LevelFilter))
                .M("getImageFilter", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).ImageFilter))
                .M("setWrapping", "(II)V", (t, b) => { var o = (TextureObj)t.R[b]; o.WrapS = t.I(b + 1); o.WrapT = t.I(b + 2); })
                .M("getWrappingS", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).WrapS))
                .M("getWrappingT", "()I", (t, b) => t.RetI(b, ((TextureObj)t.R[b]).WrapT)),
                Rt.F<TextureObj>());

            n.Define(P + "VertexArray", P + "Object3D", c => c
                .M("<init>", "(III)V", (t, b) =>
                {
                    var o = (VertexArrayObj)t.R[b];
                    int cnt = t.I(b + 1), comps = t.I(b + 2), size = t.I(b + 3);
                    if (cnt < 1 || cnt > 65535 || comps < 2 || comps > 4 || (size != 1 && size != 2)) Iae("vertex array");
                    o.Count = cnt; o.Comps = comps; o.Size = size; o.D = new short[cnt * comps];
                })
                .M("set", "(II[B)V", (t, b) =>
                {
                    var o = (VertexArrayObj)t.R[b]; int f = t.I(b + 1), k = t.I(b + 2); var a = N<sbyte[]>(t, b + 3);
                    if (o.Size != 1) throw new JavaThrow("java/lang/IllegalStateException", "component size");
                    Range(o, f, k, a.Length);
                    for (int i = 0; i < k * o.Comps; i++) o.D[f * o.Comps + i] = a[i];
                })
                .M("set", "(II[S)V", (t, b) =>
                {
                    var o = (VertexArrayObj)t.R[b]; int f = t.I(b + 1), k = t.I(b + 2); var a = N<short[]>(t, b + 3);
                    if (o.Size != 2) throw new JavaThrow("java/lang/IllegalStateException", "component size");
                    Range(o, f, k, a.Length);
                    Array.Copy(a, 0, o.D, f * o.Comps, k * o.Comps);
                })
                .M("get", "(II[B)V", (t, b) =>
                {
                    var o = (VertexArrayObj)t.R[b]; int f = t.I(b + 1), k = t.I(b + 2); var a = N<sbyte[]>(t, b + 3);
                    Range(o, f, k, a.Length);
                    for (int i = 0; i < k * o.Comps; i++) a[i] = (sbyte)o.D[f * o.Comps + i];
                })
                .M("get", "(II[S)V", (t, b) =>
                {
                    var o = (VertexArrayObj)t.R[b]; int f = t.I(b + 1), k = t.I(b + 2); var a = N<short[]>(t, b + 3);
                    Range(o, f, k, a.Length);
                    Array.Copy(o.D, f * o.Comps, a, 0, k * o.Comps);
                })
                .M("getComponentCount", "()I", (t, b) => t.RetI(b, ((VertexArrayObj)t.R[b]).Comps))
                .M("getComponentType", "()I", (t, b) => t.RetI(b, ((VertexArrayObj)t.R[b]).Size))
                .M("getVertexCount", "()I", (t, b) => t.RetI(b, ((VertexArrayObj)t.R[b]).Count)),
                Rt.F<VertexArrayObj>());

            string VA = "L" + P + "VertexArray;";
            n.Define(P + "VertexBuffer", P + "Object3D", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getVertexCount", "()I", (t, b) => t.RetI(b, ((VertexBufferObj)t.R[b]).VertexCount))
                .M("setPositions", "(" + VA + "F[F)V", (t, b) =>
                {
                    var o = (VertexBufferObj)t.R[b]; var va = A<VertexArrayObj>(t, b + 1);
                    if (va != null && va.Comps != 3) Iae("positions need 3 components");
                    o.Pos = va; o.PosScale = t.F(b + 2);
                    var bias = Fa(t, b + 3);
                    o.PosBias = bias != null ? new[] { bias[0], bias[1], bias[2] } : new float[3];
                })
                .M("getPositions", "([F)" + VA, (t, b) =>
                {
                    var o = (VertexBufferObj)t.R[b]; var a = Fa(t, b + 1);
                    if (a != null && o.Pos != null) { Need(a, 4); a[0] = o.PosScale; a[1] = o.PosBias[0]; a[2] = o.PosBias[1]; a[3] = o.PosBias[2]; }
                    t.RetA(b, o.Pos);
                })
                .M("setNormals", "(" + VA + ")V", (t, b) => { var va = A<VertexArrayObj>(t, b + 1); if (va != null && va.Comps != 3) Iae("normals need 3 components"); ((VertexBufferObj)t.R[b]).Norm = va; })
                .M("getNormals", "()" + VA, (t, b) => t.RetA(b, ((VertexBufferObj)t.R[b]).Norm))
                .M("setColors", "(" + VA + ")V", (t, b) => { var va = A<VertexArrayObj>(t, b + 1); if (va != null && (va.Size != 1 || va.Comps < 3)) Iae("colors"); ((VertexBufferObj)t.R[b]).Col = va; })
                .M("getColors", "()" + VA, (t, b) => t.RetA(b, ((VertexBufferObj)t.R[b]).Col))
                .M("setTexCoords", "(I" + VA + "F[F)V", (t, b) =>
                {
                    var o = (VertexBufferObj)t.R[b]; int u = t.I(b + 1);
                    if (u < 0 || u >= o.Tc.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    var va = A<VertexArrayObj>(t, b + 2);
                    o.Tc[u] = va; o.TcScale[u] = t.F(b + 3);
                    var bias = Fa(t, b + 4);
                    o.TcBias[u] = new float[3];
                    if (bias != null) for (int i = 0; i < Math.Min(3, bias.Length); i++) o.TcBias[u][i] = bias[i];
                })
                .M("getTexCoords", "(I[F)" + VA, (t, b) =>
                {
                    var o = (VertexBufferObj)t.R[b]; int u = t.I(b + 1);
                    if (u < 0 || u >= o.Tc.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    var a = Fa(t, b + 2); var va = o.Tc[u];
                    if (a != null && va != null)
                    {
                        Need(a, va.Comps + 1);
                        a[0] = o.TcScale[u];
                        for (int i = 0; i < va.Comps; i++) a[i + 1] = o.TcBias[u][Math.Min(i, 2)];
                    }
                    t.RetA(b, va);
                })
                .M("getDefaultColor", "()I", (t, b) => t.RetI(b, ((VertexBufferObj)t.R[b]).DefaultColor))
                .M("setDefaultColor", "(I)V", (t, b) => ((VertexBufferObj)t.R[b]).DefaultColor = t.I(b + 1)),
                Rt.F<VertexBufferObj>());

            n.Define(P + "IndexBuffer", P + "Object3D", c => c
                .M("getIndexCount", "()I", (t, b) => t.RetI(b, ((IndexBufferObj)t.R[b]).Tris.Length))
                .M("getIndices", "([I)V", (t, b) =>
                {
                    var o = (IndexBufferObj)t.R[b]; var a = N<int[]>(t, b + 1);
                    if (a.Length < o.Tris.Length) Iae("array too short");
                    Array.Copy(o.Tris, a, o.Tris.Length);
                }),
                null, access: 0x0401);

            n.Define(P + "TriangleStripArray", P + "IndexBuffer", c => c
                .M("<init>", "(I[I)V", (t, b) =>
                {
                    int first = t.I(b + 1);
                    if (first < 0) Iae("first index");
                    BuildStrips((IndexBufferObj)t.R[b], null, first, N<int[]>(t, b + 2));
                })
                .M("<init>", "([I[I)V", (t, b) => BuildStrips((IndexBufferObj)t.R[b], N<int[]>(t, b + 1), 0, N<int[]>(t, b + 2))),
                Rt.F<IndexBufferObj>());

            n.Define(P + "KeyframeSequence", P + "Object3D", c => c
                .Static("LINEAR", "I", t => 176).Static("SLERP", "I", t => 177).Static("SPLINE", "I", t => 178).Static("SQUAD", "I", t => 179).Static("STEP", "I", t => 180)
                .Static("CONSTANT", "I", t => 192).Static("LOOP", "I", t => 193)
                .M("<init>", "(III)V", (t, b) =>
                {
                    var o = (KeyframeObj)t.R[b];
                    int cnt = t.I(b + 1), comps = t.I(b + 2), interp = t.I(b + 3);
                    if (cnt < 1 || comps < 1 || interp < 176 || interp > 180) Iae("keyframes");
                    o.Count = cnt; o.Comps = comps; o.Interp = interp;
                    o.Times = new int[cnt]; o.Values = new float[cnt][];
                    for (int i = 0; i < cnt; i++) o.Values[i] = new float[comps];
                    o.First = 0; o.Last = cnt - 1;
                })
                .M("getComponentCount", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Comps))
                .M("getKeyframeCount", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Count))
                .M("getInterpolationType", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Interp))
                .M("getDuration", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Duration))
                .M("setDuration", "(I)V", (t, b) => { int d = t.I(b + 1); if (d <= 0) Iae("duration"); ((KeyframeObj)t.R[b]).Duration = d; })
                .M("getRepeatMode", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Repeat))
                .M("setRepeatMode", "(I)V", (t, b) => { int m = t.I(b + 1); if (m != 192 && m != 193) Iae("repeat"); ((KeyframeObj)t.R[b]).Repeat = m; })
                .M("setKeyframe", "(II[F)V", (t, b) =>
                {
                    var o = (KeyframeObj)t.R[b]; int i = t.I(b + 1), time = t.I(b + 2); var v = Fn(t, b + 3);
                    if (i < 0 || i >= o.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    if (time < 0 || v.Length < o.Comps) Iae("keyframe");
                    o.Times[i] = time;
                    Array.Copy(v, o.Values[i], o.Comps);
                })
                .M("getKeyframe", "(I[F)I", (t, b) =>
                {
                    var o = (KeyframeObj)t.R[b]; int i = t.I(b + 1); var v = Fa(t, b + 2);
                    if (i < 0 || i >= o.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    if (v != null) { Need(v, o.Comps); Array.Copy(o.Values[i], v, o.Comps); }
                    t.RetI(b, o.Times[i]);
                })
                .M("setValidRange", "(II)V", (t, b) =>
                {
                    var o = (KeyframeObj)t.R[b]; int f = t.I(b + 1), l = t.I(b + 2);
                    if (f < 0 || l < 0 || f >= o.Count || l >= o.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    o.First = f; o.Last = l;
                })
                .M("getValidRangeFirst", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).First))
                .M("getValidRangeLast", "()I", (t, b) => t.RetI(b, ((KeyframeObj)t.R[b]).Last)),
                Rt.F<KeyframeObj>());

            n.Define(P + "AnimationTrack", P + "Object3D", c => c
                .Static("ALPHA", "I", t => 256).Static("AMBIENT_COLOR", "I", t => 257).Static("COLOR", "I", t => 258).Static("CROP", "I", t => 259)
                .Static("DENSITY", "I", t => 260).Static("DIFFUSE_COLOR", "I", t => 261).Static("EMISSIVE_COLOR", "I", t => 262).Static("FAR_DISTANCE", "I", t => 263)
                .Static("FIELD_OF_VIEW", "I", t => 264).Static("INTENSITY", "I", t => 265).Static("MORPH_WEIGHTS", "I", t => 266).Static("NEAR_DISTANCE", "I", t => 267)
                .Static("ORIENTATION", "I", t => 268).Static("PICKABILITY", "I", t => 269).Static("SCALE", "I", t => 270).Static("SHININESS", "I", t => 271)
                .Static("SPECULAR_COLOR", "I", t => 272).Static("SPOT_ANGLE", "I", t => 273).Static("SPOT_EXPONENT", "I", t => 274).Static("TRANSLATION", "I", t => 275)
                .Static("VISIBILITY", "I", t => 276)
                .M("<init>", "(L" + P + "KeyframeSequence;I)V", (t, b) =>
                {
                    var o = (TrackObj)t.R[b]; o.Seq = N<KeyframeObj>(t, b + 1); o.Property = t.I(b + 2);
                    if (o.Property < 256 || o.Property > 276) Iae("property");
                })
                .M("getController", "()L" + P + "AnimationController;", (t, b) => t.RetA(b, ((TrackObj)t.R[b]).Ctl))
                .M("setController", "(L" + P + "AnimationController;)V", (t, b) => ((TrackObj)t.R[b]).Ctl = A<ControllerObj>(t, b + 1))
                .M("getKeyframeSequence", "()L" + P + "KeyframeSequence;", (t, b) => t.RetA(b, ((TrackObj)t.R[b]).Seq))
                .M("getTargetProperty", "()I", (t, b) => t.RetI(b, ((TrackObj)t.R[b]).Property)),
                Rt.F<TrackObj>());

            n.Define(P + "AnimationController", P + "Object3D", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("setActiveInterval", "(II)V", (t, b) => { var o = (ControllerObj)t.R[b]; if (t.I(b + 1) > t.I(b + 2)) Iae("interval"); o.Start = t.I(b + 1); o.End = t.I(b + 2); })
                .M("getActiveIntervalStart", "()I", (t, b) => t.RetI(b, ((ControllerObj)t.R[b]).Start))
                .M("getActiveIntervalEnd", "()I", (t, b) => t.RetI(b, ((ControllerObj)t.R[b]).End))
                .M("setPosition", "(FI)V", (t, b) => { var o = (ControllerObj)t.R[b]; o.RefSeq = t.F(b + 1); o.RefWorld = t.I(b + 2); })
                .M("getPosition", "(I)F", (t, b) => t.RetF(b, ((ControllerObj)t.R[b]).SeqTime(t.I(b + 1))))
                .M("setSpeed", "(FI)V", (t, b) =>
                {
                    var o = (ControllerObj)t.R[b]; int wt = t.I(b + 2);
                    o.RefSeq = o.SeqTime(wt); o.RefWorld = wt; o.Speed = t.F(b + 1);
                })
                .M("getSpeed", "()F", (t, b) => t.RetF(b, ((ControllerObj)t.R[b]).Speed))
                .M("setWeight", "(F)V", (t, b) => { float w = t.F(b + 1); if (w < 0) Iae("weight"); ((ControllerObj)t.R[b]).Weight = w; })
                .M("getWeight", "()F", (t, b) => t.RetF(b, ((ControllerObj)t.R[b]).Weight))
                .M("getRefWorldTime", "()I", (t, b) => t.RetI(b, ((ControllerObj)t.R[b]).RefWorld)),
                Rt.F<ControllerObj>());

            n.Define(P + "Transform", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("<init>", "(L" + P + "Transform;)V", (t, b) => ((TransformObj)t.R[b]).M = (float[])N<TransformObj>(t, b + 1).M.Clone())
                .M("get", "([F)V", (t, b) => { var a = Fa(t, b + 1); Need(a, 16); Array.Copy(((TransformObj)t.R[b]).M, a, 16); })
                .M("set", "([F)V", (t, b) => { var a = Fa(t, b + 1); Need(a, 16); Array.Copy(a, ((TransformObj)t.R[b]).M, 16); })
                .M("set", "(L" + P + "Transform;)V", (t, b) => Array.Copy(N<TransformObj>(t, b + 1).M, ((TransformObj)t.R[b]).M, 16))
                .M("setIdentity", "()V", (t, b) => Mat.SetIdentity(((TransformObj)t.R[b]).M))
                .M("invert", "()V", (t, b) =>
                {
                    var m = ((TransformObj)t.R[b]).M;
                    if (!Mat.Invert(m, m)) throw new JavaThrow("java/lang/ArithmeticException", "singular matrix");
                })
                .M("transpose", "()V", (t, b) => Mat.Transpose(((TransformObj)t.R[b]).M))
                .M("postMultiply", "(L" + P + "Transform;)V", (t, b) => { var m = ((TransformObj)t.R[b]).M; Mat.Mul(m, N<TransformObj>(t, b + 1).M, m); })
                .M("postRotate", "(FFFF)V", (t, b) => { var m = ((TransformObj)t.R[b]).M; Mat.Mul(m, Mat.Rotation(t.F(b + 1), t.F(b + 2), t.F(b + 3), t.F(b + 4)), m); })
                .M("postRotateQuat", "(FFFF)V", (t, b) =>
                {
                    var q = new[] { t.F(b + 1), t.F(b + 2), t.F(b + 3), t.F(b + 4) };
                    if (q[0] == 0 && q[1] == 0 && q[2] == 0 && q[3] == 0) Iae("zero quaternion");
                    var m = ((TransformObj)t.R[b]).M; Mat.Mul(m, Mat.FromQuat(q), m);
                })
                .M("postScale", "(FFF)V", (t, b) => { var m = ((TransformObj)t.R[b]).M; Mat.Mul(m, Mat.Scale(t.F(b + 1), t.F(b + 2), t.F(b + 3)), m); })
                .M("postTranslate", "(FFF)V", (t, b) => { var m = ((TransformObj)t.R[b]).M; Mat.Mul(m, Mat.Translation(t.F(b + 1), t.F(b + 2), t.F(b + 3)), m); })
                .M("transform", "([F)V", (t, b) =>
                {
                    var m = ((TransformObj)t.R[b]).M; var a = Fn(t, b + 1);
                    if (a.Length % 4 != 0) Iae("length not a multiple of 4");
                    var o = new float[4];
                    for (int i = 0; i < a.Length; i += 4) { Mat.Apply(m, a[i], a[i + 1], a[i + 2], a[i + 3], o, 0); Array.Copy(o, 0, a, i, 4); }
                })
                .M("transform", "(" + VA + "[FZ)V", (t, b) =>
                {
                    var m = ((TransformObj)t.R[b]).M; var va = N<VertexArrayObj>(t, b + 1); var a = Fn(t, b + 2); bool w = t.Z(b + 3);
                    if (a.Length < va.Count * 4) Iae("result array too short");
                    var o = new float[4];
                    for (int i = 0; i < va.Count; i++)
                    {
                        float x = va.D[i * va.Comps], y = va.D[i * va.Comps + 1], z = va.Comps > 2 ? va.D[i * va.Comps + 2] : 0;
                        float ww = va.Comps > 3 ? va.D[i * va.Comps + 3] : (w ? 1 : 0);
                        Mat.Apply(m, x, y, z, ww, o, 0);
                        Array.Copy(o, 0, a, i * 4, 4);
                    }
                }),
                Rt.F<TransformObj>());

            n.Define(P + "RayIntersection", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getDistance", "()F", (t, b) => t.RetF(b, ((RayObj)t.R[b]).Distance))
                .M("getIntersected", "()L" + P + "Node;", (t, b) => t.RetA(b, ((RayObj)t.R[b]).Node))
                .M("getNormalX", "()F", (t, b) => t.RetF(b, ((RayObj)t.R[b]).Nx))
                .M("getNormalY", "()F", (t, b) => t.RetF(b, ((RayObj)t.R[b]).Ny))
                .M("getNormalZ", "()F", (t, b) => t.RetF(b, ((RayObj)t.R[b]).Nz))
                .M("getSubmeshIndex", "()I", (t, b) => t.RetI(b, ((RayObj)t.R[b]).Submesh))
                .M("getTextureS", "(I)F", (t, b) => t.RetF(b, t.I(b + 1) == 0 ? ((RayObj)t.R[b]).S0 : 0))
                .M("getTextureT", "(I)F", (t, b) => t.RetF(b, t.I(b + 1) == 0 ? ((RayObj)t.R[b]).T0 : 0))
                .M("getRay", "([F)V", (t, b) => { var a = Fa(t, b + 1); Need(a, 6); Array.Copy(((RayObj)t.R[b]).Ray, a, 6); }),
                Rt.F<RayObj>());

            string TR = "L" + P + "Transform;";
            n.Define(P + "Graphics3D", "java/lang/Object", c => c
                .Static("ANTIALIAS", "I", t => 2).Static("DITHER", "I", t => 4).Static("TRUE_COLOR", "I", t => 8).Static("OVERWRITE", "I", t => 16)
                .S("getInstance", "()L" + P + "Graphics3D;", (t, b) =>
                {
                    Graphics3DObj g;
                    lock (instances)
                    {
                        if (!instances.TryGetValue(t.Vm, out g))
                        {
                            g = (Graphics3DObj)t.Vm.NewObject(t.Vm.LoadClass(P + "Graphics3D"));
                            instances.Add(t.Vm, g);
                        }
                    }
                    t.RetA(b, g);
                })
                .S("getProperties", "()Ljava/util/Hashtable;", (t, b) =>
                {
                    var ht = (HashtableObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Hashtable"));
                    void Put(string k, object v) { ht.Order.Add(k); ht.D[k] = v; }
                    Put("supportAntialiasing", Library.BoxBool(t, false));
                    Put("supportTrueColor", Library.BoxBool(t, false));
                    Put("supportDithering", Library.BoxBool(t, false));
                    Put("supportMipmapping", Library.BoxBool(t, false));
                    Put("supportPerspectiveCorrection", Library.BoxBool(t, true));
                    Put("supportLocalCameraLighting", Library.BoxBool(t, true));
                    Put("maxLights", Library.BoxInt(t, 8));
                    Put("maxViewportWidth", Library.BoxInt(t, 1024));
                    Put("maxViewportHeight", Library.BoxInt(t, 1024));
                    Put("maxViewportDimension", Library.BoxInt(t, 1024));
                    Put("maxTextureDimension", Library.BoxInt(t, 1024));
                    Put("maxSpriteCropDimension", Library.BoxInt(t, 1024));
                    Put("maxTransformsPerVertex", Library.BoxInt(t, 4));
                    Put("numTextureUnits", Library.BoxInt(t, 2));
                    t.RetA(b, ht);
                })
                .M("bindTarget", "(Ljava/lang/Object;)V", (t, b) => G(t, b).Bind(N<object>(t, b + 1), true))
                .M("bindTarget", "(Ljava/lang/Object;ZI)V", (t, b) => { G(t, b).Bind(N<object>(t, b + 1), t.Z(b + 2)); G(t, b).Hints = t.I(b + 3); })
                .M("releaseTarget", "()V", (t, b) => { var r = G(t, b); if (r.Bound != null) r.Release(); })
                .M("getTarget", "()Ljava/lang/Object;", (t, b) => t.RetA(b, G(t, b).Bound))
                .M("getHints", "()I", (t, b) => t.RetI(b, G(t, b).Hints))
                .M("isDepthBufferEnabled", "()Z", (t, b) => t.RetZ(b, G(t, b).DepthEnabled))
                .M("clear", "(L" + P + "Background;)V", (t, b) => G(t, b).Clear(A<BackgroundObj>(t, b + 1)))
                .M("render", "(L" + P + "World;)V", (t, b) => G(t, b).RenderWorld(N<WorldObj>(t, b + 1)))
                .M("render", "(L" + P + "Node;" + TR + ")V", (t, b) =>
                {
                    var node = N<NodeObj>(t, b + 1);
                    if (!(node is MeshObj || node is Sprite3DObj || node is GroupObj)) Iae("node type");
                    G(t, b).RenderNode(node, A<TransformObj>(t, b + 2)?.M);
                })
                .M("render", "(" + VB + IB + AP + TR + ")V", (t, b) =>
                    G(t, b).RenderImmediate(N<VertexBufferObj>(t, b + 1), N<IndexBufferObj>(t, b + 2), N<AppearanceObj>(t, b + 3), A<TransformObj>(t, b + 4)?.M, -1))
                .M("render", "(" + VB + IB + AP + TR + "I)V", (t, b) =>
                    G(t, b).RenderImmediate(N<VertexBufferObj>(t, b + 1), N<IndexBufferObj>(t, b + 2), N<AppearanceObj>(t, b + 3), A<TransformObj>(t, b + 4)?.M, t.I(b + 5)))
                .M("setCamera", "(L" + P + "Camera;" + TR + ")V", (t, b) => G(t, b).SetCamera(A<CameraObj>(t, b + 1), A<TransformObj>(t, b + 2)?.M))
                .M("getCamera", "(" + TR + ")L" + P + "Camera;", (t, b) =>
                {
                    var r = G(t, b); var tr = A<TransformObj>(t, b + 1);
                    if (tr != null) tr.M = (float[])r.CamM.Clone();
                    t.RetA(b, r.Camera);
                })
                .M("addLight", "(L" + P + "Light;" + TR + ")I", (t, b) =>
                {
                    var r = G(t, b);
                    r.Lights.Add(new LightRef { L = N<LightObj>(t, b + 1), M = A<TransformObj>(t, b + 2) != null ? (float[])A<TransformObj>(t, b + 2).M.Clone() : Mat.Identity() });
                    t.RetI(b, r.Lights.Count - 1);
                })
                .M("setLight", "(IL" + P + "Light;" + TR + ")V", (t, b) =>
                {
                    var r = G(t, b); int i = t.I(b + 1);
                    if (i < 0 || i >= r.Lights.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    var l = A<LightObj>(t, b + 2);
                    r.Lights[i] = new LightRef { L = l, M = A<TransformObj>(t, b + 3) != null ? (float[])A<TransformObj>(t, b + 3).M.Clone() : Mat.Identity() };
                    if (l == null) r.Lights[i] = new LightRef { L = new LightObj { Intensity = 0, Mode = LightObj.AMBIENT }, M = Mat.Identity() };
                })
                .M("resetLights", "()V", (t, b) => G(t, b).Lights.Clear())
                .M("getLightCount", "()I", (t, b) => t.RetI(b, G(t, b).Lights.Count))
                .M("getLight", "(I" + TR + ")L" + P + "Light;", (t, b) =>
                {
                    var r = G(t, b); int i = t.I(b + 1);
                    if (i < 0 || i >= r.Lights.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    var tr = A<TransformObj>(t, b + 2);
                    if (tr != null) tr.M = (float[])r.Lights[i].M.Clone();
                    t.RetA(b, r.Lights[i].L);
                })
                .M("setViewport", "(IIII)V", (t, b) =>
                {
                    var r = G(t, b); int w = t.I(b + 3), h = t.I(b + 4);
                    if (w <= 0 || h <= 0) Iae("viewport");
                    r.Vx = t.I(b + 1); r.Vy = t.I(b + 2); r.Vw = w; r.Vh = h;
                })
                .M("getViewportX", "()I", (t, b) => t.RetI(b, G(t, b).Vx))
                .M("getViewportY", "()I", (t, b) => t.RetI(b, G(t, b).Vy))
                .M("getViewportWidth", "()I", (t, b) => t.RetI(b, G(t, b).Vw))
                .M("getViewportHeight", "()I", (t, b) => t.RetI(b, G(t, b).Vh))
                .M("setDepthRange", "(FF)V", (t, b) =>
                {
                    float nr = t.F(b + 1), fr = t.F(b + 2);
                    if (nr < 0 || nr > 1 || fr < 0 || fr > 1) Iae("depth range");
                    G(t, b).DNear = nr; G(t, b).DFar = fr;
                })
                .M("getDepthRangeNear", "()F", (t, b) => t.RetF(b, G(t, b).DNear))
                .M("getDepthRangeFar", "()F", (t, b) => t.RetF(b, G(t, b).DFar)),
                Rt.F<Graphics3DObj>());

            n.Define(P + "Loader", "java/lang/Object", c => c
                .S("load", "(Ljava/lang/String;)[" + O3, (t, b) =>
                {
                    var name = N<string>(t, b);
                    t.RetA(b, Arr(t, "Object3D", M3gLoader.Load(t, M3gLoader.Fetch(t, name), 0, name)));
                })
                .S("load", "([BI)[" + O3, (t, b) =>
                {
                    var d = N<sbyte[]>(t, b);
                    t.RetA(b, Arr(t, "Object3D", M3gLoader.Load(t, (byte[])(Array)d, t.I(b + 1))));
                }));
        }

        static Renderer G(JThread t, int b) => ((Graphics3DObj)t.R[b]).R;

        static int Fmt(int f)
        {
            if (f < 96 || f > 100) Iae("format");
            return f;
        }

        static void Range(VertexArrayObj o, int first, int count, int len)
        {
            if (count < 0 || first < 0 || first + count > o.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            if (len < count * o.Comps) Iae("array too short");
        }

        static void SetTargets(MorphingMeshObj m, ObjArray tg)
        {
            if (tg.Data.Length == 0) Iae("no morph targets");
            m.Targets = new VertexBufferObj[tg.Data.Length];
            for (int i = 0; i < m.Targets.Length; i++) m.Targets[i] = (VertexBufferObj)tg.Data[i] ?? throw new NullReferenceException();
            m.Weights = new float[m.Targets.Length];
        }

        static void SetSkeleton(SkinnedMeshObj m, GroupObj sk)
        {
            if (sk.Parent != null || sk is WorldObj) Iae("skeleton has a parent");
            m.Skeleton = sk; sk.Parent = m;
        }

        static void SetSpriteImage(Sprite3DObj s, Image2DObj im)
        {
            s.Image = im;
            s.Cx = 0; s.Cy = 0; s.Cw = im.W; s.Ch = im.H;
        }
    }
}
