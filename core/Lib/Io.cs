using System;
using System.Collections.Generic;
using System.Text;
using Lumidlet.Vm;

namespace Lumidlet.Lib
{
    public class InStreamObj : JObject
    {
        public virtual int Read(JThread t) => t.CallVirtualInt(this, "read()I");

        public virtual int Read(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            if (len == 0) return 0;
            int c = Read(t);
            if (c < 0) return -1;
            b[off] = (sbyte)c;
            int i = 1;
            for (; i < len; i++)
            {
                c = Read(t);
                if (c < 0) break;
                b[off + i] = (sbyte)c;
            }
            return i;
        }

        public virtual long Skip(JThread t, long n)
        {
            long k = 0;
            while (k < n && Read(t) >= 0) k++;
            return k;
        }

        public virtual int Available(JThread t) => 0;
        public virtual void Close(JThread t) { }
        public virtual void Mark(int limit) { }
        public virtual void Reset(JThread t) { throw new JavaThrow("java/io/IOException", "mark/reset not supported"); }
        public virtual bool MarkSupported => false;
    }

    public sealed class ByteArrayInStream : InStreamObj
    {
        public sbyte[] Buf;
        public int Pos, Count, MarkPos;

        public void Set(sbyte[] buf, int off, int len)
        {
            Buf = buf; Pos = off; MarkPos = off; Count = Math.Min(off + len, buf.Length);
        }

        public override int Read(JThread t) => Pos < Count ? (byte)Buf[Pos++] : -1;

        public override int Read(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            if (Pos >= Count) return -1;
            if (len > Count - Pos) len = Count - Pos;
            if (len <= 0) return 0;
            Buffer.BlockCopy(Buf, Pos, b, off, len);
            Pos += len;
            return len;
        }

        public override long Skip(JThread t, long n)
        {
            if (n < 0) return 0;
            long k = Math.Min(n, Count - Pos);
            Pos += (int)k;
            return k;
        }

        public override int Available(JThread t) => Count - Pos;
        public override void Mark(int limit) { MarkPos = Pos; }
        public override void Reset(JThread t) { Pos = MarkPos; }
        public override bool MarkSupported => true;
    }

    public sealed class FilterInStream : InStreamObj
    {
        public InStreamObj In;
        InStreamObj Src { get { if (In == null) throw new JavaThrow("java/io/IOException", "stream closed"); return In; } }
        public override int Read(JThread t) => Src.Read(t);
        public override int Read(JThread t, sbyte[] b, int off, int len) => Src.Read(t, b, off, len);
        public override long Skip(JThread t, long n) => Src.Skip(t, n);
        public override int Available(JThread t) => Src.Available(t);
        public override void Close(JThread t) { In?.Close(t); }
        public override void Mark(int limit) { In?.Mark(limit); }
        public override void Reset(JThread t) { Src.Reset(t); }
        public override bool MarkSupported => In != null && In.MarkSupported;

        public int ReadByte(JThread t)
        {
            int c = Src.Read(t);
            if (c < 0) throw new JavaThrow("java/io/EOFException");
            return c;
        }

        public void ReadFully(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (len < 0 || off < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            int n = 0;
            while (n < len)
            {
                int k = Src.Read(t, b, off + n, len - n);
                if (k < 0) throw new JavaThrow("java/io/EOFException");
                n += k;
            }
        }
    }

    public class OutStreamObj : JObject
    {
        public virtual void Write(JThread t, int b) => t.CallVirtual(this, "write(I)V", b);

        public virtual void Write(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            for (int i = 0; i < len; i++) Write(t, b[off + i]);
        }

        public virtual void Flush(JThread t) { }
        public virtual void Close(JThread t) { }
    }

    public sealed class ByteArrayOutStream : OutStreamObj
    {
        public sbyte[] Buf = new sbyte[32];
        public int Count;

        void Grow(int need)
        {
            if (need <= Buf.Length) return;
            var n = new sbyte[Math.Max(need, Buf.Length * 2)];
            Buffer.BlockCopy(Buf, 0, n, 0, Count);
            Buf = n;
        }

        public override void Write(JThread t, int b) { lock (this) { Grow(Count + 1); Buf[Count++] = (sbyte)b; } }

        public override void Write(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            lock (this) { Grow(Count + len); Buffer.BlockCopy(b, off, Buf, Count, len); Count += len; }
        }

        public sbyte[] ToArray()
        {
            lock (this) { var r = new sbyte[Count]; Buffer.BlockCopy(Buf, 0, r, 0, Count); return r; }
        }
    }

    public sealed class FilterOutStream : OutStreamObj
    {
        public OutStreamObj Out;
        public int Written;
        public bool IsLog;
        public string LogName;
        public readonly StringBuilder Line = new StringBuilder();
        public bool Error;

        OutStreamObj Dst { get { if (Out == null) throw new JavaThrow("java/io/IOException", "stream closed"); return Out; } }

        public override void Write(JThread t, int b)
        {
            if (IsLog) { LogByte(t, b); return; }
            Dst.Write(t, b);
            Written++;
        }

        public override void Write(JThread t, sbyte[] b, int off, int len)
        {
            if (IsLog) { for (int i = 0; i < len; i++) LogByte(t, b[off + i]); return; }
            Dst.Write(t, b, off, len);
            Written += len;
        }

        void LogByte(JThread t, int b)
        {
            if ((b & 0xff) == '\n') { t.Vm.Log(LogName + ": " + Line); Line.Clear(); }
            else if ((b & 0xff) != '\r') Line.Append((char)(b & 0xff));
        }

        public void Text(JThread t, string s)
        {
            if (IsLog)
            {
                foreach (var ch in s)
                {
                    if (ch == '\n') { t.Vm.Log(LogName + ": " + Line); Line.Clear(); }
                    else if (ch != '\r') Line.Append(ch);
                }
                return;
            }
            try
            {
                var bytes = Enc.Encode(s, null);
                Write(t, bytes, 0, bytes.Length);
            }
            catch (JavaException) { Error = true; }
        }

        public override void Flush(JThread t) { if (!IsLog) Out?.Flush(t); }
        public override void Close(JThread t) { if (!IsLog) Out?.Close(t); }
    }

    public sealed class ReaderObj : JObject
    {
        public InStreamObj In;
        public string Enc;
        public int Pending = -1;
        public char[] Str;
        public int SPos, SMark;

        public int ReadChar(JThread t)
        {
            if (Str != null) return SPos < Str.Length ? Str[SPos++] : -1;
            if (In == null)
            {
                return t.CallVirtualInt(this, "read()I");
            }
            if (Pending >= 0) { int p = Pending; Pending = -1; return p; }
            int b0 = In.Read(t);
            if (b0 < 0) return -1;
            switch (Enc)
            {
                case "UTF8":
                    {
                        if (b0 < 0x80) return b0;
                        int need = b0 >= 0xF0 ? 3 : b0 >= 0xE0 ? 2 : b0 >= 0xC0 ? 1 : 0;
                        if (need == 0) return 0xFFFD;
                        int cp = b0 & (0x3F >> need);
                        for (int i = 0; i < need; i++)
                        {
                            int bn = In.Read(t);
                            if (bn < 0) return 0xFFFD;
                            cp = (cp << 6) | (bn & 0x3F);
                        }
                        if (cp > 0xFFFF)
                        {
                            cp -= 0x10000;
                            Pending = 0xDC00 | (cp & 0x3FF);
                            return 0xD800 | (cp >> 10);
                        }
                        return cp;
                    }
                case "CP1251": return Lib.Enc.Cp1251(b0);
                case "UTF16LE": { int b1 = In.Read(t); return b1 < 0 ? -1 : b0 | (b1 << 8); }
                case "UTF16":
                case "UTF16BE": { int b1 = In.Read(t); return b1 < 0 ? -1 : (b0 << 8) | b1; }
                default: return b0;
            }
        }
    }

    public sealed class WriterObj : JObject
    {
        public OutStreamObj Out;
        public string Enc;
        public StringBuilder Sb;

        public void WriteStr(JThread t, string s)
        {
            if (Sb != null) { Sb.Append(s); return; }
            if (Out == null) throw new JavaThrow("java/io/IOException", "closed");
            var b = Lib.Enc.Encode(s, Enc);
            Out.Write(t, b, 0, b.Length);
        }
    }

    public static partial class Library
    {
        static InStreamObj In(JThread t, int b) => (InStreamObj)t.R[b];
        static FilterInStream Fin(JThread t, int b) => (FilterInStream)t.R[b];
        static OutStreamObj Out(JThread t, int b) => (OutStreamObj)t.R[b];
        static FilterOutStream Fout(JThread t, int b) => (FilterOutStream)t.R[b];

        static JObject MakeLogStream(JThread t, string name)
        {
            var c = t.Vm.LoadClass("java/io/PrintStream");
            var o = (FilterOutStream)t.Vm.NewObject(c);
            o.IsLog = true;
            o.LogName = name;
            return o;
        }

        public static string ReadUtf(JThread t, FilterInStream s)
        {
            int len = (s.ReadByte(t) << 8) | s.ReadByte(t);
            var buf = new sbyte[len];
            s.ReadFully(t, buf, 0, len);
            var sb = new StringBuilder(len);
            int i = 0;
            while (i < len)
            {
                int c = (byte)buf[i];
                if (c < 0x80) { sb.Append((char)c); i++; }
                else if ((c & 0xE0) == 0xC0)
                {
                    if (i + 1 >= len) throw new JavaThrow("java/io/UTFDataFormatException");
                    sb.Append((char)(((c & 0x1F) << 6) | ((byte)buf[i + 1] & 0x3F))); i += 2;
                }
                else if ((c & 0xF0) == 0xE0)
                {
                    if (i + 2 >= len) throw new JavaThrow("java/io/UTFDataFormatException");
                    sb.Append((char)(((c & 0x0F) << 12) | (((byte)buf[i + 1] & 0x3F) << 6) | ((byte)buf[i + 2] & 0x3F))); i += 3;
                }
                else throw new JavaThrow("java/io/UTFDataFormatException");
            }
            return sb.ToString();
        }

        public static sbyte[] EncodeUtf(string s)
        {
            var l = new List<sbyte>(s.Length + 2) { 0, 0 };
            foreach (char c in s)
            {
                if (c >= 1 && c < 0x80) l.Add((sbyte)c);
                else if (c < 0x800) { l.Add((sbyte)(0xC0 | (c >> 6))); l.Add((sbyte)(0x80 | (c & 0x3F))); }
                else { l.Add((sbyte)(0xE0 | (c >> 12))); l.Add((sbyte)(0x80 | ((c >> 6) & 0x3F))); l.Add((sbyte)(0x80 | (c & 0x3F))); }
            }
            int n = l.Count - 2;
            if (n > 65535) throw new JavaThrow("java/io/UTFDataFormatException");
            l[0] = (sbyte)(n >> 8); l[1] = (sbyte)n;
            return l.ToArray();
        }

        static void WriteBytes(JThread t, FilterOutStream o, params int[] bytes)
        {
            var a = new sbyte[bytes.Length];
            for (int i = 0; i < a.Length; i++) a[i] = (sbyte)bytes[i];
            o.Write(t, a, 0, a.Length);
        }

        static void Io(NativeRegistry n)
        {
            n.Define("java/io/InputStream", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .Abstract("read", "()I")
                .M("read", "([B)I", (t, b) => { var a = (sbyte[])t.R[b + 1]; if (a == null) throw new NullReferenceException(); t.RetI(b, In(t, b).Read(t, a, 0, a.Length)); })
                .M("read", "([BII)I", (t, b) => t.RetI(b, In(t, b).Read(t, (sbyte[])t.R[b + 1], t.I(b + 2), t.I(b + 3))))
                .M("skip", "(J)J", (t, b) => t.RetJ(b, In(t, b).Skip(t, t.J(b + 1))))
                .M("available", "()I", (t, b) => t.RetI(b, In(t, b).Available(t)))
                .M("close", "()V", (t, b) => In(t, b).Close(t))
                .M("mark", "(I)V", (t, b) => In(t, b).Mark(t.I(b + 1)))
                .M("reset", "()V", (t, b) => In(t, b).Reset(t))
                .M("markSupported", "()Z", (t, b) => t.RetZ(b, In(t, b).MarkSupported)), Rt.F<InStreamObj>(), access: 0x0401);

            n.Define("java/io/ByteArrayInputStream", "java/io/InputStream", c => c
                .M("<init>", "([B)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; if (a == null) throw new NullReferenceException(); ((ByteArrayInStream)t.R[b]).Set(a, 0, a.Length); })
                .M("<init>", "([BII)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; if (a == null) throw new NullReferenceException(); ((ByteArrayInStream)t.R[b]).Set(a, t.I(b + 2), t.I(b + 3)); })
                .M("read", "()I", (t, b) => t.RetI(b, In(t, b).Read(t))), Rt.F<ByteArrayInStream>());

            NativeFn fread = (t, b) => t.RetI(b, In(t, b).Read(t));
            n.Interface("java/io/DataInput");
            n.Interface("java/io/DataOutput");
            n.Define("java/io/DataInputStream", "java/io/InputStream", c => c
                .M("<init>", "(Ljava/io/InputStream;)V", (t, b) => Fin(t, b).In = (InStreamObj)t.R[b + 1])
                .M("read", "()I", fread)
                .M("readFully", "([B)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; if (a == null) throw new NullReferenceException(); Fin(t, b).ReadFully(t, a, 0, a.Length); })
                .M("readFully", "([BII)V", (t, b) => Fin(t, b).ReadFully(t, (sbyte[])t.R[b + 1], t.I(b + 2), t.I(b + 3)))
                .M("skipBytes", "(I)I", (t, b) => { var s = Fin(t, b); int k = 0, want = t.I(b + 1); while (k < want && s.Read(t) >= 0) k++; t.RetI(b, k); })
                .M("readBoolean", "()Z", (t, b) => t.RetZ(b, Fin(t, b).ReadByte(t) != 0))
                .M("readByte", "()B", (t, b) => t.RetI(b, (sbyte)Fin(t, b).ReadByte(t)))
                .M("readUnsignedByte", "()I", (t, b) => t.RetI(b, Fin(t, b).ReadByte(t)))
                .M("readShort", "()S", (t, b) => { var s = Fin(t, b); t.RetI(b, (short)((s.ReadByte(t) << 8) | s.ReadByte(t))); })
                .M("readUnsignedShort", "()I", (t, b) => { var s = Fin(t, b); t.RetI(b, (s.ReadByte(t) << 8) | s.ReadByte(t)); })
                .M("readChar", "()C", (t, b) => { var s = Fin(t, b); t.RetI(b, (s.ReadByte(t) << 8) | s.ReadByte(t)); })
                .M("readInt", "()I", (t, b) => t.RetI(b, ReadInt(t, Fin(t, b))))
                .M("readLong", "()J", (t, b) => { var s = Fin(t, b); long hi = (uint)ReadInt(t, s); long lo = (uint)ReadInt(t, s); t.RetJ(b, (hi << 32) | lo); })
                .M("readFloat", "()F", (t, b) => t.RetI(b, ReadInt(t, Fin(t, b))))
                .M("readDouble", "()D", (t, b) => { var s = Fin(t, b); long hi = (uint)ReadInt(t, s); long lo = (uint)ReadInt(t, s); t.RetJ(b, (hi << 32) | lo); })
                .M("readUTF", "()Ljava/lang/String;", (t, b) => t.RetA(b, ReadUtf(t, Fin(t, b))))
                .M("readLine", "()Ljava/lang/String;", (t, b) =>
                {
                    var s = Fin(t, b); var sb = new StringBuilder(); int ch; bool any = false;
                    while ((ch = s.Read(t)) >= 0) { any = true; if (ch == '\n') break; if (ch != '\r') sb.Append((char)ch); }
                    t.RetA(b, any ? sb.ToString() : null);
                })
                .S("readUTF", "(Ljava/io/DataInput;)Ljava/lang/String;", (t, b) => t.RetA(b, ReadUtf(t, (FilterInStream)t.R[b]))),
                Rt.F<FilterInStream>(), new[] { "java/io/DataInput" });

            n.Define("java/io/OutputStream", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .Abstract("write", "(I)V")
                .M("write", "([B)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; if (a == null) throw new NullReferenceException(); Out(t, b).Write(t, a, 0, a.Length); })
                .M("write", "([BII)V", (t, b) => Out(t, b).Write(t, (sbyte[])t.R[b + 1], t.I(b + 2), t.I(b + 3)))
                .M("flush", "()V", (t, b) => Out(t, b).Flush(t))
                .M("close", "()V", (t, b) => Out(t, b).Close(t)), Rt.F<OutStreamObj>(), access: 0x0401);

            n.Define("java/io/ByteArrayOutputStream", "java/io/OutputStream", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("<init>", "(I)V", (t, b) => { int s = t.I(b + 1); if (s < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); ((ByteArrayOutStream)t.R[b]).Buf = new sbyte[Math.Max(1, s)]; })
                .M("write", "(I)V", (t, b) => Out(t, b).Write(t, t.I(b + 1)))
                .M("toByteArray", "()[B", (t, b) => t.RetA(b, ((ByteArrayOutStream)t.R[b]).ToArray()))
                .M("size", "()I", (t, b) => t.RetI(b, ((ByteArrayOutStream)t.R[b]).Count))
                .M("reset", "()V", (t, b) => ((ByteArrayOutStream)t.R[b]).Count = 0)
                .M("toString", "()Ljava/lang/String;", (t, b) => { var a = ((ByteArrayOutStream)t.R[b]).ToArray(); t.RetA(b, Enc.Decode(a, 0, a.Length, null)); }),
                Rt.F<ByteArrayOutStream>());

            n.Define("java/io/DataOutputStream", "java/io/OutputStream", c => c
                .M("<init>", "(Ljava/io/OutputStream;)V", (t, b) => Fout(t, b).Out = (OutStreamObj)t.R[b + 1])
                .M("write", "(I)V", (t, b) => Fout(t, b).Write(t, t.I(b + 1)))
                .M("writeBoolean", "(Z)V", (t, b) => Fout(t, b).Write(t, t.I(b + 1) != 0 ? 1 : 0))
                .M("writeByte", "(I)V", (t, b) => Fout(t, b).Write(t, t.I(b + 1)))
                .M("writeShort", "(I)V", (t, b) => { int v = t.I(b + 1); WriteBytes(t, Fout(t, b), v >> 8, v); })
                .M("writeChar", "(I)V", (t, b) => { int v = t.I(b + 1); WriteBytes(t, Fout(t, b), v >> 8, v); })
                .M("writeInt", "(I)V", (t, b) => { int v = t.I(b + 1); WriteBytes(t, Fout(t, b), v >> 24, v >> 16, v >> 8, v); })
                .M("writeFloat", "(F)V", (t, b) => { int v = t.I(b + 1); WriteBytes(t, Fout(t, b), v >> 24, v >> 16, v >> 8, v); })
                .M("writeLong", "(J)V", (t, b) => { long v = t.J(b + 1); WriteBytes(t, Fout(t, b), (int)(v >> 56), (int)(v >> 48), (int)(v >> 40), (int)(v >> 32), (int)(v >> 24), (int)(v >> 16), (int)(v >> 8), (int)v); })
                .M("writeDouble", "(D)V", (t, b) => { long v = t.J(b + 1); WriteBytes(t, Fout(t, b), (int)(v >> 56), (int)(v >> 48), (int)(v >> 40), (int)(v >> 32), (int)(v >> 24), (int)(v >> 16), (int)(v >> 8), (int)v); })
                .M("writeChars", "(Ljava/lang/String;)V", (t, b) => { var o = Fout(t, b); foreach (char ch in S(t, b + 1)) WriteBytes(t, o, ch >> 8, ch); })
                .M("writeBytes", "(Ljava/lang/String;)V", (t, b) => { var o = Fout(t, b); foreach (char ch in S(t, b + 1)) o.Write(t, ch); })
                .M("writeUTF", "(Ljava/lang/String;)V", (t, b) => { var a = EncodeUtf(S(t, b + 1)); Fout(t, b).Write(t, a, 0, a.Length); })
                .M("size", "()I", (t, b) => t.RetI(b, Fout(t, b).Written)),
                Rt.F<FilterOutStream>(), new[] { "java/io/DataOutput" });

            n.Define("java/io/PrintStream", "java/io/OutputStream", c => c
                .M("<init>", "(Ljava/io/OutputStream;)V", (t, b) => Fout(t, b).Out = (OutStreamObj)t.R[b + 1])
                .M("write", "(I)V", (t, b) => Fout(t, b).Write(t, t.I(b + 1)))
                .M("checkError", "()Z", (t, b) => t.RetZ(b, Fout(t, b).Error))
                .M("print", "(Ljava/lang/String;)V", (t, b) => Fout(t, b).Text(t, S(t, b + 1) ?? "null"))
                .M("print", "(Ljava/lang/Object;)V", (t, b) => Fout(t, b).Text(t, Rt.Str(t, t.R[b + 1])))
                .M("print", "([C)V", (t, b) => Fout(t, b).Text(t, new string((char[])t.R[b + 1])))
                .M("print", "(Z)V", (t, b) => Fout(t, b).Text(t, t.Z(b + 1) ? "true" : "false"))
                .M("print", "(C)V", (t, b) => Fout(t, b).Text(t, ((char)t.I(b + 1)).ToString()))
                .M("print", "(I)V", (t, b) => Fout(t, b).Text(t, t.I(b + 1).ToString()))
                .M("print", "(J)V", (t, b) => Fout(t, b).Text(t, t.J(b + 1).ToString()))
                .M("print", "(F)V", (t, b) => Fout(t, b).Text(t, Jf.Float(t.F(b + 1))))
                .M("print", "(D)V", (t, b) => Fout(t, b).Text(t, Jf.Double(t.D(b + 1))))
                .M("println", "()V", (t, b) => Fout(t, b).Text(t, "\n"))
                .M("println", "(Ljava/lang/String;)V", (t, b) => Fout(t, b).Text(t, (S(t, b + 1) ?? "null") + "\n"))
                .M("println", "(Ljava/lang/Object;)V", (t, b) => Fout(t, b).Text(t, Rt.Str(t, t.R[b + 1]) + "\n"))
                .M("println", "([C)V", (t, b) => Fout(t, b).Text(t, new string((char[])t.R[b + 1]) + "\n"))
                .M("println", "(Z)V", (t, b) => Fout(t, b).Text(t, (t.Z(b + 1) ? "true" : "false") + "\n"))
                .M("println", "(C)V", (t, b) => Fout(t, b).Text(t, ((char)t.I(b + 1)) + "\n"))
                .M("println", "(I)V", (t, b) => Fout(t, b).Text(t, t.I(b + 1) + "\n"))
                .M("println", "(J)V", (t, b) => Fout(t, b).Text(t, t.J(b + 1) + "\n"))
                .M("println", "(F)V", (t, b) => Fout(t, b).Text(t, Jf.Float(t.F(b + 1)) + "\n"))
                .M("println", "(D)V", (t, b) => Fout(t, b).Text(t, Jf.Double(t.D(b + 1)) + "\n")),
                Rt.F<FilterOutStream>());

            n.Define("java/io/Reader", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("<init>", "(Ljava/lang/Object;)V", (t, b) => { })
                .M("read", "()I", (t, b) =>
                {
                    var r = (ReaderObj)t.R[b];
                    if (r.In == null && r.Str == null) { var a = new char[1]; int k = t.CallVirtualInt(r, "read([CII)I", a, 0, 1); t.RetI(b, k <= 0 ? -1 : a[0]); }
                    else t.RetI(b, r.ReadChar(t));
                })
                .M("read", "([C)I", (t, b) => { var a = (char[])t.R[b + 1]; t.RetI(b, t.CallVirtualInt(t.R[b], "read([CII)I", a, 0, a.Length)); })
                .M("read", "([CII)I", (t, b) =>
                {
                    var r = (ReaderObj)t.R[b]; var a = (char[])t.R[b + 1]; int off = t.I(b + 2), len = t.I(b + 3);
                    if (off < 0 || len < 0 || off + len > a.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                    int i = 0;
                    for (; i < len; i++)
                    {
                        int ch = r.ReadChar(t);
                        if (ch < 0) break;
                        a[off + i] = (char)ch;
                        if (r.In != null && r.In.Available(t) <= 0 && r.Pending < 0 && !(r.In is ByteArrayInStream)) { i++; break; }
                    }
                    t.RetI(b, len > 0 && i == 0 ? -1 : i);
                })
                .M("skip", "(J)J", (t, b) => { var r = (ReaderObj)t.R[b]; long k = 0, want = t.J(b + 1); while (k < want && r.ReadChar(t) >= 0) k++; t.RetJ(b, k); })
                .M("ready", "()Z", (t, b) => { var r = (ReaderObj)t.R[b]; t.RetZ(b, r.Str != null ? r.SPos < r.Str.Length : r.In != null && r.In.Available(t) > 0); })
                .M("markSupported", "()Z", (t, b) => t.RetZ(b, ((ReaderObj)t.R[b]).Str != null))
                .M("mark", "(I)V", (t, b) => { var r = (ReaderObj)t.R[b]; r.SMark = r.SPos; })
                .M("reset", "()V", (t, b) => { var r = (ReaderObj)t.R[b]; if (r.Str == null) throw new JavaThrow("java/io/IOException", "reset"); r.SPos = r.SMark; })
                .M("close", "()V", (t, b) => { var r = (ReaderObj)t.R[b]; r.In?.Close(t); }), Rt.F<ReaderObj>(), access: 0x0401);

            n.Define("java/io/InputStreamReader", "java/io/Reader", c => c
                .M("<init>", "(Ljava/io/InputStream;)V", (t, b) => { var r = (ReaderObj)t.R[b]; r.In = (InStreamObj)t.R[b + 1]; if (r.In == null) throw new NullReferenceException(); r.Enc = Enc.Norm(null); })
                .M("<init>", "(Ljava/io/InputStream;Ljava/lang/String;)V", (t, b) => { var r = (ReaderObj)t.R[b]; r.In = (InStreamObj)t.R[b + 1]; if (r.In == null) throw new NullReferenceException(); r.Enc = Enc.Norm(S(t, b + 2)); }));

            n.Define("java/io/Writer", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("write", "(I)V", (t, b) => ((WriterObj)t.R[b]).WriteStr(t, ((char)t.I(b + 1)).ToString()))
                .M("write", "([C)V", (t, b) => ((WriterObj)t.R[b]).WriteStr(t, new string((char[])t.R[b + 1])))
                .M("write", "([CII)V", (t, b) => ((WriterObj)t.R[b]).WriteStr(t, new string((char[])t.R[b + 1], t.I(b + 2), t.I(b + 3))))
                .M("write", "(Ljava/lang/String;)V", (t, b) => ((WriterObj)t.R[b]).WriteStr(t, S(t, b + 1)))
                .M("write", "(Ljava/lang/String;II)V", (t, b) => ((WriterObj)t.R[b]).WriteStr(t, S(t, b + 1).Substring(t.I(b + 2), t.I(b + 3))))
                .M("flush", "()V", (t, b) => ((WriterObj)t.R[b]).Out?.Flush(t))
                .M("close", "()V", (t, b) => ((WriterObj)t.R[b]).Out?.Close(t)), Rt.F<WriterObj>(), access: 0x0401);

            n.Define("java/io/OutputStreamWriter", "java/io/Writer", c => c
                .M("<init>", "(Ljava/io/OutputStream;)V", (t, b) => { var w = (WriterObj)t.R[b]; w.Out = (OutStreamObj)t.R[b + 1]; w.Enc = Enc.Norm(null); })
                .M("<init>", "(Ljava/io/OutputStream;Ljava/lang/String;)V", (t, b) => { var w = (WriterObj)t.R[b]; w.Out = (OutStreamObj)t.R[b + 1]; w.Enc = Enc.Norm(S(t, b + 2)); }));
        }

        static int ReadInt(JThread t, FilterInStream s)
        {
            int a = s.ReadByte(t), b = s.ReadByte(t), c = s.ReadByte(t), d = s.ReadByte(t);
            return (a << 24) | (b << 16) | (c << 8) | d;
        }
    }
}
