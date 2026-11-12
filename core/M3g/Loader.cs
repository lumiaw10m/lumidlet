using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Lumidlet.Midp;
using Lumidlet.Vm;

namespace Lumidlet.M3g
{
    public static class M3gLoader
    {
        const string P = "javax/microedition/m3g/";
        static readonly byte[] Magic = { 0xAB, 0x4A, 0x53, 0x52, 0x31, 0x38, 0x34, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

        public static T New<T>(JThread t, string cls) where T : JObject => (T)t.Vm.NewObject(t.Vm.LoadClass(P + cls));

        public static byte[] Fetch(JThread t, string name)
        {
            if (name == null) throw new NullReferenceException();
            string path = name;
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                int k = path.LastIndexOf('/');
                path = k >= 0 ? path.Substring(k + 1) : path;
            }
            while (path.StartsWith("/")) path = path.Substring(1);
            var d = t.Vm.ClassPath?.GetResource(path);
            if (d == null) throw new JavaThrow("java/io/IOException", "cannot load " + name);
            return d;
        }

        public static List<Obj3D> Load(JThread t, byte[] data, int off, string baseName = null, int depth = 0)
        {
            if (data == null) throw new NullReferenceException();
            if (off < 0 || off >= data.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            if (Png.IsPng(data, off, data.Length - off) || !IsM3g(data, off))
            {
                // a plain image file becomes a single immutable Image2D
                var s = Lcdui.Decode(t, data, off, data.Length - off);
                var im = New<Image2DObj>(t, "Image2D");
                im.Format = s.HasAlpha ? Image2DObj.RGBA : Image2DObj.RGB;
                im.W = s.W; im.H = s.H; im.Pix = new int[s.W * s.H];
                im.SetArgb(s.Pix, s.W, s.H);
                return new List<Obj3D> { im };
            }
            try { return new Parser(t, depth, baseName).Parse(data, off + Magic.Length); }
            catch (JavaException) { throw; }
            catch (VmHalt) { throw; }
            catch (Exception e) { throw new JavaThrow("java/io/IOException", "invalid M3G data: " + e.Message); }
        }

        // external references are relative to the file that contains them
        static string Resolve(string baseName, string uri)
        {
            if (string.IsNullOrEmpty(baseName) || uri.StartsWith("/") || uri.Contains(":")) return uri;
            string b = baseName;
            if (b.Contains(":")) { int k = b.LastIndexOf('/'); b = k >= 0 ? b.Substring(k + 1) : b; }
            int slash = b.LastIndexOf('/');
            var parts = new List<string>((slash >= 0 ? b.Substring(0, slash) : "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var seg in uri.Split('/'))
            {
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (seg != "." && seg.Length > 0) parts.Add(seg);
            }
            return "/" + string.Join("/", parts);
        }

        static bool IsM3g(byte[] d, int off)
        {
            if (d.Length - off < Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++) if (d[off + i] != Magic[i]) return false;
            return true;
        }

        sealed class Reader
        {
            readonly byte[] d; int p; readonly int end;
            public Reader(byte[] d, int p, int end) { this.d = d; this.p = p; this.end = end; }
            public bool More => p < end;
            public int Pos => p;
            public int Byte() { if (p >= end) throw new EndOfStreamException(); return d[p++]; }
            public bool Bool() => Byte() != 0;
            public int U16() { int v = Byte(); return v | (Byte() << 8); }
            public short I16() => (short)U16();
            public int I32() { int v = Byte(); v |= Byte() << 8; v |= Byte() << 16; v |= Byte() << 24; return v; }
            public float F32() => BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0);
            public int Rgb() { int r = Byte(), g = Byte(), b = Byte(); return (r << 16) | (g << 8) | b; }
            public int Rgba() { int r = Byte(), g = Byte(), b = Byte(), a = Byte(); return (a << 24) | (r << 16) | (g << 8) | b; }
            public byte[] Bytes(int n) { if (n < 0 || p + n > end) throw new EndOfStreamException(); var r = new byte[n]; Buffer.BlockCopy(d, p, r, 0, n); p += n; return r; }
            public string Str()
            {
                int s = p;
                while (p < end && d[p] != 0) p++;
                var r = Encoding.UTF8.GetString(d, s, p - s);
                if (p < end) p++;
                return r;
            }
            public float[] Floats(int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = F32(); return r; }
        }

        sealed class Parser
        {
            readonly JThread t;
            readonly int depth;
            readonly string baseName;
            readonly List<object> objs = new List<object> { null };
            readonly HashSet<object> referenced = new HashSet<object>();
            readonly List<SkinnedMeshObj> skinned = new List<SkinnedMeshObj>();
            Reader r;

            public Parser(JThread t, int depth, string baseName) { this.t = t; this.depth = depth; this.baseName = baseName; }

            public List<Obj3D> Parse(byte[] data, int p)
            {
                while (p < data.Length)
                {
                    if (data.Length - p < 9) break;
                    int scheme = data[p];
                    int total = BitConverter.ToInt32(data, p + 1);
                    int uncomp = BitConverter.ToInt32(data, p + 5);
                    if (total < 13 || p + total > data.Length) throw new JavaThrow("java/io/IOException", "bad section");
                    int dataLen = total - 13;
                    byte[] sec;
                    if (scheme == 0) { sec = new byte[dataLen]; Buffer.BlockCopy(data, p + 9, sec, 0, dataLen); }
                    else if (scheme == 1)
                    {
                        sec = new byte[uncomp];
                        using (var ds = new DeflateStream(new MemoryStream(data, p + 9 + 2, dataLen - 2), CompressionMode.Decompress))
                        {
                            int got = 0;
                            while (got < uncomp) { int k = ds.Read(sec, got, uncomp - got); if (k <= 0) break; got += k; }
                        }
                    }
                    else throw new JavaThrow("java/io/IOException", "unknown compression");
                    ParseObjects(sec);
                    p += total;
                }
                foreach (var sk in skinned) foreach (var b in sk.Bones) Renderer.ComputeRest(sk, b);
                var roots = new List<Obj3D>();
                for (int i = 1; i < objs.Count; i++)
                {
                    var o = objs[i] as Obj3D;
                    if (o != null && !referenced.Contains(o)) roots.Add(o);
                }
                return roots;
            }

            void ParseObjects(byte[] sec)
            {
                int p = 0;
                while (p + 5 <= sec.Length)
                {
                    int type = sec[p];
                    int len = BitConverter.ToInt32(sec, p + 1);
                    p += 5;
                    if (len < 0 || p + len > sec.Length) throw new JavaThrow("java/io/IOException", "bad object length");
                    r = new Reader(sec, p, p + len);
                    objs.Add(ReadObject(type));
                    p += len;
                }
            }

            T Ref<T>() where T : class
            {
                int idx = r.I32();
                if (idx == 0) return null;
                if (idx < 0 || idx >= objs.Count) throw new JavaThrow("java/io/IOException", "bad object index " + idx);
                var o = objs[idx];
                if (o == null) return null;
                referenced.Add(o);
                var c = o as T;
                if (c == null) throw new JavaThrow("java/io/IOException", "unexpected object type at index " + idx);
                return c;
            }

            object ReadObject(int type)
            {
                switch (type)
                {
                    case 0: // header
                        r.Byte(); r.Byte(); r.Bool(); r.I32(); r.I32(); r.Str();
                        return null;
                    case 1: { var o = New<ControllerObj>(t, "AnimationController"); Obj(o); o.Speed = r.F32(); o.Weight = r.F32(); o.Start = r.I32(); o.End = r.I32(); o.RefSeq = r.F32(); o.RefWorld = r.I32(); return o; }
                    case 2: { var o = New<TrackObj>(t, "AnimationTrack"); Obj(o); o.Seq = Ref<KeyframeObj>(); o.Ctl = Ref<ControllerObj>(); o.Property = r.I32(); return o; }
                    case 3:
                        {
                            var o = New<AppearanceObj>(t, "Appearance"); Obj(o);
                            o.Layer = (sbyte)r.Byte(); o.Comp = Ref<CompositingObj>(); o.Fog = Ref<FogObj>(); o.Poly = Ref<PolygonObj>(); o.Mat = Ref<MaterialObj>();
                            int n = r.I32();
                            if (n > o.Tex.Length) o.Tex = new TextureObj[n];
                            for (int i = 0; i < n; i++) o.Tex[i] = Ref<TextureObj>();
                            return o;
                        }
                    case 4:
                        {
                            var o = New<BackgroundObj>(t, "Background"); Obj(o);
                            o.Color = r.Rgba(); o.Image = Ref<Image2DObj>(); o.ModeX = r.Byte(); o.ModeY = r.Byte();
                            o.Cx = r.I32(); o.Cy = r.I32(); o.Cw = r.I32(); o.Ch = r.I32();
                            o.DepthClear = r.Bool(); o.ColorClear = r.Bool();
                            return o;
                        }
                    case 5:
                        {
                            var o = New<CameraObj>(t, "Camera"); Node(o);
                            o.Type = r.Byte();
                            if (o.Type == CameraObj.GENERIC) o.Proj = r.Floats(16);
                            else { o.Fovy = r.F32(); o.Aspect = r.F32(); o.Near = r.F32(); o.Far = r.F32(); }
                            return o;
                        }
                    case 6:
                        {
                            var o = New<CompositingObj>(t, "CompositingMode"); Obj(o);
                            o.DepthTest = r.Bool(); o.DepthWrite = r.Bool(); o.ColorWrite = r.Bool(); o.AlphaWrite = r.Bool();
                            o.Blending = r.Byte(); o.Threshold = r.Byte() / 255f; o.OffFactor = r.F32(); o.OffUnits = r.F32();
                            return o;
                        }
                    case 7:
                        {
                            var o = New<FogObj>(t, "Fog"); Obj(o);
                            o.Color = r.Rgb(); o.Mode = r.Byte();
                            if (o.Mode == FogObj.EXPONENTIAL) o.Density = r.F32(); else { o.Near = r.F32(); o.Far = r.F32(); }
                            return o;
                        }
                    case 8:
                        {
                            var o = New<PolygonObj>(t, "PolygonMode"); Obj(o);
                            o.Culling = r.Byte(); o.Shading = r.Byte(); o.Winding = r.Byte();
                            o.TwoSided = r.Bool(); o.LocalCamera = r.Bool(); o.Perspective = r.Bool();
                            return o;
                        }
                    case 9: { var o = New<GroupObj>(t, "Group"); GroupData(o); return o; }
                    case 10:
                        {
                            var o = New<Image2DObj>(t, "Image2D"); Obj(o);
                            o.Format = r.Byte(); o.Mutable = r.Bool(); o.W = r.I32(); o.H = r.I32();
                            o.Pix = new int[Math.Max(0, o.W * o.H)];
                            if (!o.Mutable)
                            {
                                var pal = r.Bytes(r.I32());
                                var px = r.Bytes(r.I32());
                                if (pal.Length > 0) o.SetPalette(px, 0, pal, 0);
                                else if (px.Length >= o.W * o.H * Image2DObj.Bpp(o.Format)) o.SetBytes(0, 0, o.W, o.H, px, 0);
                            }
                            return o;
                        }
                    case 11:
                        {
                            var o = New<IndexBufferObj>(t, "TriangleStripArray"); Obj(o);
                            int enc = r.Byte();
                            int[] idx = null; int start = 0;
                            switch (enc)
                            {
                                case 0: start = r.I32(); break;
                                case 1: start = r.Byte(); break;
                                case 2: start = r.U16(); break;
                                case 128: { int n = r.I32(); idx = new int[n]; for (int i = 0; i < n; i++) idx[i] = r.I32(); break; }
                                case 129: { int n = r.I32(); idx = new int[n]; for (int i = 0; i < n; i++) idx[i] = r.Byte(); break; }
                                case 130: { int n = r.I32(); idx = new int[n]; for (int i = 0; i < n; i++) idx[i] = r.U16(); break; }
                                default: throw new JavaThrow("java/io/IOException", "bad index encoding");
                            }
                            int sc = r.I32();
                            var strips = new int[sc];
                            for (int i = 0; i < sc; i++) strips[i] = r.I32();
                            Natives.BuildStrips(o, idx, start, strips);
                            return o;
                        }
                    case 12:
                        {
                            var o = New<LightObj>(t, "Light"); Node(o);
                            o.AttC = r.F32(); o.AttL = r.F32(); o.AttQ = r.F32();
                            o.Color = r.Rgb(); o.Mode = r.Byte(); o.Intensity = r.F32(); o.SpotAngle = r.F32(); o.SpotExp = r.F32();
                            return o;
                        }
                    case 13:
                        {
                            var o = New<MaterialObj>(t, "Material"); Obj(o);
                            o.Ambient = r.Rgb(); o.Diffuse = r.Rgba(); o.Emissive = r.Rgb(); o.Specular = r.Rgb();
                            o.Shininess = r.F32(); o.Tracking = r.Bool();
                            return o;
                        }
                    case 14: { var o = New<MeshObj>(t, "Mesh"); MeshData(o); return o; }
                    case 15:
                        {
                            var o = New<MorphingMeshObj>(t, "MorphingMesh"); MeshData(o);
                            int n = r.I32();
                            o.Targets = new VertexBufferObj[n]; o.Weights = new float[n];
                            for (int i = 0; i < n; i++) { o.Targets[i] = Ref<VertexBufferObj>(); o.Weights[i] = r.F32(); }
                            return o;
                        }
                    case 16:
                        {
                            var o = New<SkinnedMeshObj>(t, "SkinnedMesh"); MeshData(o);
                            o.Skeleton = Ref<GroupObj>();
                            if (o.Skeleton != null) o.Skeleton.Parent = o;
                            int n = r.I32();
                            for (int i = 0; i < n; i++)
                            {
                                var b = new Bone { Node = Ref<NodeObj>(), First = r.I32(), Count = r.I32(), Weight = r.I32() };
                                o.Bones.Add(b);
                            }
                            skinned.Add(o);
                            return o;
                        }
                    case 17:
                        {
                            var o = New<TextureObj>(t, "Texture2D"); Transformable(o);
                            o.Image = Ref<Image2DObj>(); o.BlendColor = r.Rgb(); o.Blending = r.Byte();
                            o.WrapS = r.Byte(); o.WrapT = r.Byte(); o.LevelFilter = r.Byte(); o.ImageFilter = r.Byte();
                            return o;
                        }
                    case 18:
                        {
                            var o = New<Sprite3DObj>(t, "Sprite3D"); Node(o);
                            o.Image = Ref<Image2DObj>(); o.Ap = Ref<AppearanceObj>(); o.Scaled = r.Bool();
                            o.Cx = r.I32(); o.Cy = r.I32(); o.Cw = r.I32(); o.Ch = r.I32();
                            return o;
                        }
                    case 19:
                        {
                            var o = New<KeyframeObj>(t, "KeyframeSequence"); Obj(o);
                            o.Interp = r.Byte(); o.Repeat = r.Byte();
                            int enc = r.Byte();
                            o.Duration = r.I32(); o.First = r.I32(); o.Last = r.I32();
                            o.Comps = r.I32(); o.Count = r.I32();
                            o.Times = new int[o.Count]; o.Values = new float[o.Count][];
                            float[] bias = null, scale = null;
                            if (enc != 0) { bias = r.Floats(o.Comps); scale = r.Floats(o.Comps); }
                            for (int i = 0; i < o.Count; i++)
                            {
                                o.Times[i] = r.I32();
                                var v = new float[o.Comps];
                                for (int k = 0; k < o.Comps; k++)
                                {
                                    if (enc == 0) v[k] = r.F32();
                                    else if (enc == 1) v[k] = bias[k] + scale[k] * r.Byte() / 255f;
                                    else v[k] = bias[k] + scale[k] * r.U16() / 65535f;
                                }
                                o.Values[i] = v;
                            }
                            if (o.Last >= o.Count) o.Last = o.Count - 1;
                            return o;
                        }
                    case 20:
                        {
                            var o = New<VertexArrayObj>(t, "VertexArray"); Obj(o);
                            o.Size = r.Byte(); o.Comps = r.Byte(); int enc = r.Byte(); o.Count = r.U16();
                            o.D = new short[o.Count * o.Comps];
                            var prev = new int[o.Comps];
                            for (int i = 0; i < o.Count; i++)
                                for (int k = 0; k < o.Comps; k++)
                                {
                                    int val = o.Size == 1 ? (sbyte)r.Byte() : r.I16();
                                    if (enc == 1)
                                    {
                                        val = prev[k] + val;
                                        val = o.Size == 1 ? (sbyte)val : (short)val;
                                        prev[k] = val;
                                    }
                                    o.D[i * o.Comps + k] = (short)val;
                                }
                            return o;
                        }
                    case 21:
                        {
                            var o = New<VertexBufferObj>(t, "VertexBuffer"); Obj(o);
                            o.DefaultColor = r.Rgba();
                            o.Pos = Ref<VertexArrayObj>(); o.PosBias = r.Floats(3); o.PosScale = r.F32();
                            o.Norm = Ref<VertexArrayObj>(); o.Col = Ref<VertexArrayObj>();
                            int n = r.I32();
                            if (n > 2) { o.Tc = new VertexArrayObj[n]; o.TcScale = new float[n]; o.TcBias = new float[n][]; }
                            for (int i = 0; i < n; i++) { o.Tc[i] = Ref<VertexArrayObj>(); o.TcBias[i] = r.Floats(3); o.TcScale[i] = r.F32(); }
                            return o;
                        }
                    case 22:
                        {
                            var o = New<WorldObj>(t, "World"); GroupData(o);
                            o.Camera = Ref<CameraObj>(); o.Background = Ref<BackgroundObj>();
                            return o;
                        }
                    case 255:
                        {
                            string uri = r.Str();
                            if (depth > 4) throw new JavaThrow("java/io/IOException", "external reference depth");
                            string full = Resolve(baseName, uri);
                            var list = Load(t, Fetch(t, full), 0, full, depth + 1);
                            return list.Count > 0 ? list[0] : null;
                        }
                    default:
                        throw new JavaThrow("java/io/IOException", "unknown object type " + type);
                }
            }

            void Obj(Obj3D o)
            {
                o.UserID = r.I32();
                int n = r.I32();
                for (int i = 0; i < n; i++) { var tr = Ref<TrackObj>(); if (tr != null) o.Tracks.Add(tr); }
                int pc = r.I32();
                if (pc > 0)
                {
                    var ht = (Lumidlet.Lib.HashtableObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Hashtable"));
                    for (int i = 0; i < pc; i++)
                    {
                        int id = r.I32();
                        var bytes = r.Bytes(r.I32());
                        var key = Lumidlet.Lib.Library.BoxInt(t, id);
                        if (!ht.D.ContainsKey(key)) ht.Order.Add(key);
                        ht.D[key] = Lumidlet.Lib.Enc.ToS(bytes);
                    }
                    o.UserObject = ht;
                }
            }

            void Transformable(TransformableObj o)
            {
                Obj(o);
                if (r.Bool())
                {
                    o.Tx = r.F32(); o.Ty = r.F32(); o.Tz = r.F32();
                    o.Sx = r.F32(); o.Sy = r.F32(); o.Sz = r.F32();
                    float ang = r.F32(), ax = r.F32(), ay = r.F32(), az = r.F32();
                    o.Rot = (ax == 0 && ay == 0 && az == 0) ? new float[] { 0, 0, 0, 1 } : Quat.FromAxisAngle(ang, ax, ay, az);
                }
                if (r.Bool()) o.M = r.Floats(16);
            }

            void Node(NodeObj o)
            {
                Transformable(o);
                o.Render = r.Bool(); o.Pick = r.Bool();
                o.Alpha = r.Byte() / 255f; o.Scope = r.I32();
                if (r.Bool())
                {
                    o.ZTarget = r.Byte(); o.YTarget = r.Byte();
                    o.ZRef = Ref<NodeObj>(); o.YRef = Ref<NodeObj>();
                }
            }

            void GroupData(GroupObj o)
            {
                Node(o);
                int n = r.I32();
                for (int i = 0; i < n; i++)
                {
                    var c = Ref<NodeObj>();
                    if (c == null) continue;
                    if (c.Parent != null) (c.Parent as GroupObj)?.Children.Remove(c);
                    c.Parent = o; o.Children.Add(c);
                }
            }

            void MeshData(MeshObj o)
            {
                Node(o);
                o.Vb = Ref<VertexBufferObj>();
                int n = r.I32();
                o.Ib = new IndexBufferObj[n]; o.Ap = new AppearanceObj[n];
                for (int i = 0; i < n; i++) { o.Ib[i] = Ref<IndexBufferObj>(); o.Ap[i] = Ref<AppearanceObj>(); }
            }
        }
    }
}
