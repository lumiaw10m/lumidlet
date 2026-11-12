using System;
using System.Collections.Generic;

namespace Lumidlet.M3g
{
    public static class Anim
    {
        public const int ALPHA = 256, AMBIENT_COLOR = 257, COLOR = 258, CROP = 259, DENSITY = 260, DIFFUSE_COLOR = 261,
            EMISSIVE_COLOR = 262, FAR_DISTANCE = 263, FIELD_OF_VIEW = 264, INTENSITY = 265, MORPH_WEIGHTS = 266,
            NEAR_DISTANCE = 267, ORIENTATION = 268, PICKABILITY = 269, SCALE = 270, SHININESS = 271, SPECULAR_COLOR = 272,
            SPOT_ANGLE = 273, SPOT_EXPONENT = 274, TRANSLATION = 275, VISIBILITY = 276;

        public static int Animate(Obj3D root, int time)
        {
            var seen = new HashSet<Obj3D>();
            var stack = new Stack<Obj3D>();
            stack.Push(root);
            var refs = new List<Obj3D>();
            while (stack.Count > 0)
            {
                var o = stack.Pop();
                if (o == null || !seen.Add(o)) continue;
                if (o.Tracks.Count > 0) AnimateObject(o, time);
                refs.Clear();
                o.Refs(refs);
                foreach (var r in refs) stack.Push(r);
            }
            return int.MaxValue;
        }

        static void AnimateObject(Obj3D o, int time)
        {
            // group tracks by property; blend weighted samples
            var acc = new Dictionary<int, float[]>();
            var wsum = new Dictionary<int, float>();
            foreach (var tr in o.Tracks)
            {
                var ctl = tr.Ctl; var seq = tr.Seq;
                if (ctl == null || seq == null || seq.Count == 0) continue;
                if (!ctl.Active(time) || ctl.Weight == 0) continue;
                var v = Sample(seq, ctl.SeqTime(time), tr.Property == ORIENTATION);
                if (v == null) continue;
                float[] a;
                if (!acc.TryGetValue(tr.Property, out a)) { a = new float[v.Length]; acc[tr.Property] = a; wsum[tr.Property] = 0; }
                if (tr.Property == ORIENTATION && a.Length == 4)
                {
                    // keep quaternions in the same hemisphere before summing
                    float d = a[0] * v[0] + a[1] * v[1] + a[2] * v[2] + a[3] * v[3];
                    if (d < 0) for (int i = 0; i < 4; i++) v[i] = -v[i];
                }
                for (int i = 0; i < Math.Min(a.Length, v.Length); i++) a[i] += v[i] * ctl.Weight;
                wsum[tr.Property] += ctl.Weight;
            }
            foreach (var kv in acc) Apply(o, kv.Key, kv.Value, wsum[kv.Key]);
        }

        public static float[] Sample(KeyframeObj s, float t, bool quat)
        {
            int first = Math.Max(0, Math.Min(s.First, s.Count - 1)), last = Math.Max(first, Math.Min(s.Last, s.Count - 1));
            if (s.First > s.Last) { first = 0; last = s.Count - 1; } // wrap-around ranges: approximate with full range
            int n = last - first + 1;
            if (s.Repeat == KeyframeObj.LOOP && s.Duration > 0)
            {
                t %= s.Duration;
                if (t < 0) t += s.Duration;
            }
            if (n == 1 || t <= s.Times[first])
            {
                if (s.Repeat == KeyframeObj.LOOP && n > 1 && t < s.Times[first])
                    return Interp(s, last, first, s.Times[last], s.Times[first] + s.Duration, t + s.Duration, quat);
                return (float[])s.Values[first].Clone();
            }
            if (t >= s.Times[last])
            {
                if (s.Repeat == KeyframeObj.LOOP && n > 1)
                    return Interp(s, last, first, s.Times[last], s.Times[first] + s.Duration, t, quat);
                return (float[])s.Values[last].Clone();
            }
            int k = first;
            while (k < last && s.Times[k + 1] <= t) k++;
            return Interp(s, k, k + 1, s.Times[k], s.Times[k + 1], t, quat);
        }

        static float[] Interp(KeyframeObj s, int i, int j, float t0, float t1, float t, bool quat)
        {
            var a = s.Values[i]; var b = s.Values[j];
            float u = t1 > t0 ? (t - t0) / (t1 - t0) : 0;
            u = Math.Max(0, Math.Min(1, u));
            switch (s.Interp)
            {
                case KeyframeObj.STEP:
                    return (float[])a.Clone();
                case KeyframeObj.SLERP:
                case KeyframeObj.SQUAD:
                    if (a.Length == 4) return Quat.Slerp(a, b, u);
                    goto default;
                case KeyframeObj.SPLINE:
                    {
                        int ip = Prev(s, i), jn = Next(s, j);
                        var p0 = s.Values[ip]; var p3 = s.Values[jn];
                        var r = new float[a.Length];
                        float u2 = u * u, u3 = u2 * u;
                        for (int k = 0; k < r.Length; k++)
                        {
                            float m0 = (b[k] - p0[k]) * 0.5f, m1 = (p3[k] - a[k]) * 0.5f;
                            r[k] = (2 * u3 - 3 * u2 + 1) * a[k] + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * b[k] + (u3 - u2) * m1;
                        }
                        if (quat && r.Length == 4) Quat.Normalize(r);
                        return r;
                    }
                default:
                    {
                        var r = new float[a.Length];
                        for (int k = 0; k < r.Length; k++) r[k] = a[k] + (b[k] - a[k]) * u;
                        if (quat && r.Length == 4) Quat.Normalize(r);
                        return r;
                    }
            }
        }

        static int Prev(KeyframeObj s, int i)
        {
            if (i > s.First) return i - 1;
            return s.Repeat == KeyframeObj.LOOP ? s.Last : i;
        }

        static int Next(KeyframeObj s, int j)
        {
            if (j < s.Last) return j + 1;
            return s.Repeat == KeyframeObj.LOOP ? s.First : j;
        }

        static int Col(float[] v, float w, int old, bool alpha)
        {
            int r = C(v.Length > 0 ? v[0] / w : 0), g = C(v.Length > 1 ? v[1] / w : 0), b = C(v.Length > 2 ? v[2] / w : 0);
            int a = alpha && v.Length > 3 ? C(v[3] / w) : (old >> 24) & 255;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        static int C(float f) { int i = (int)(f * 255 + 0.5f); return i < 0 ? 0 : i > 255 ? 255 : i; }

        static void Apply(Obj3D o, int prop, float[] v, float w)
        {
            if (w <= 0) return;
            float s0 = v.Length > 0 ? v[0] / w : 0;
            var node = o as NodeObj;
            var tf = o as TransformableObj;
            switch (prop)
            {
                case ALPHA:
                    if (node != null) node.Alpha = Math.Max(0, Math.Min(1, s0));
                    else if (o is MaterialObj) { var m = (MaterialObj)o; m.Diffuse = (C(s0) << 24) | (m.Diffuse & 0xFFFFFF); }
                    else if (o is BackgroundObj) { var b = (BackgroundObj)o; b.Color = (C(s0) << 24) | (b.Color & 0xFFFFFF); }
                    else if (o is VertexBufferObj) { var b = (VertexBufferObj)o; b.DefaultColor = (C(s0) << 24) | (b.DefaultColor & 0xFFFFFF); }
                    break;
                case AMBIENT_COLOR: if (o is MaterialObj) ((MaterialObj)o).Ambient = Col(v, w, 0, false) & 0xFFFFFF; break;
                case DIFFUSE_COLOR: if (o is MaterialObj) { var m = (MaterialObj)o; m.Diffuse = Col(v, w, m.Diffuse, true); } break;
                case EMISSIVE_COLOR: if (o is MaterialObj) ((MaterialObj)o).Emissive = Col(v, w, 0, false) & 0xFFFFFF; break;
                case SPECULAR_COLOR: if (o is MaterialObj) ((MaterialObj)o).Specular = Col(v, w, 0, false) & 0xFFFFFF; break;
                case SHININESS: if (o is MaterialObj) ((MaterialObj)o).Shininess = Math.Max(0, Math.Min(128, s0)); break;
                case COLOR:
                    if (o is LightObj) ((LightObj)o).Color = Col(v, w, 0, false) & 0xFFFFFF;
                    else if (o is BackgroundObj) { var b = (BackgroundObj)o; b.Color = Col(v, w, b.Color, true); }
                    else if (o is FogObj) ((FogObj)o).Color = Col(v, w, 0, false) & 0xFFFFFF;
                    else if (o is TextureObj) ((TextureObj)o).BlendColor = Col(v, w, 0, false) & 0xFFFFFF;
                    else if (o is VertexBufferObj) { var b = (VertexBufferObj)o; b.DefaultColor = Col(v, w, b.DefaultColor, true); }
                    break;
                case CROP:
                    {
                        var sp = o as Sprite3DObj; var bg = o as BackgroundObj;
                        int cx = (int)Math.Round(s0), cy = (int)Math.Round(v.Length > 1 ? v[1] / w : 0);
                        int cw = v.Length > 2 ? (int)Math.Round(v[2] / w) : int.MinValue, ch = v.Length > 3 ? (int)Math.Round(v[3] / w) : int.MinValue;
                        if (sp != null) { sp.Cx = cx; sp.Cy = cy; if (cw != int.MinValue) { sp.Cw = cw; sp.Ch = ch; } }
                        if (bg != null) { bg.Cx = cx; bg.Cy = cy; if (cw != int.MinValue) { bg.Cw = Math.Max(0, cw); bg.Ch = Math.Max(0, ch); } }
                    }
                    break;
                case DENSITY: if (o is FogObj) ((FogObj)o).Density = Math.Max(0, s0); break;
                case FAR_DISTANCE:
                    if (o is CameraObj) ((CameraObj)o).Far = s0; else if (o is FogObj) ((FogObj)o).Far = s0; break;
                case NEAR_DISTANCE:
                    if (o is CameraObj) ((CameraObj)o).Near = s0; else if (o is FogObj) ((FogObj)o).Near = s0; break;
                case FIELD_OF_VIEW: if (o is CameraObj) ((CameraObj)o).Fovy = s0; break;
                case INTENSITY: if (o is LightObj) ((LightObj)o).Intensity = s0; break;
                case SPOT_ANGLE: if (o is LightObj) ((LightObj)o).SpotAngle = Math.Max(0, Math.Min(90, s0)); break;
                case SPOT_EXPONENT: if (o is LightObj) ((LightObj)o).SpotExp = Math.Max(0, Math.Min(128, s0)); break;
                case MORPH_WEIGHTS:
                    if (o is MorphingMeshObj)
                    {
                        var mm = (MorphingMeshObj)o;
                        for (int i = 0; i < mm.Weights.Length; i++) mm.Weights[i] = i < v.Length ? v[i] / w : 0;
                    }
                    break;
                case ORIENTATION:
                    if (tf != null && v.Length >= 4) { var q = new[] { v[0], v[1], v[2], v[3] }; Quat.Normalize(q); tf.Rot = q; }
                    break;
                case SCALE:
                    if (tf != null)
                    {
                        if (v.Length >= 3) { tf.Sx = v[0] / w; tf.Sy = v[1] / w; tf.Sz = v[2] / w; }
                        else { tf.Sx = tf.Sy = tf.Sz = s0; }
                    }
                    break;
                case TRANSLATION:
                    if (tf != null)
                    {
                        tf.Tx = s0; tf.Ty = v.Length > 1 ? v[1] / w : 0; tf.Tz = v.Length > 2 ? v[2] / w : 0;
                    }
                    break;
                case PICKABILITY: if (node != null) node.Pick = s0 >= 0.5f; break;
                case VISIBILITY: if (node != null) node.Render = s0 >= 0.5f; break;
            }
        }
    }
}
