using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Lumidlet.Vm;

namespace Lumidlet.Lib
{
    public class ThrowableObj : JObject
    {
        public string Message;
        public JObject Cause;
        public JMethod[] Trace;

        public string Describe()
        {
            var sb = new StringBuilder(Class.JavaName);
            if (Message != null) sb.Append(": ").Append(Message);
            if (Trace != null)
                foreach (var m in Trace) sb.Append("\n    at ").Append(m);
            return sb.ToString();
        }
    }

    public sealed class ClassObj : JObject
    {
        public JClass Ref;
    }

    public sealed class StrPlaceholder : JObject { }

    public sealed class StringBufferObj : JObject
    {
        public readonly StringBuilder Sb = new StringBuilder();
    }

    public sealed class BoxObj : JObject
    {
        public Slot V;

        public override bool Equals(object obj)
        {
            var o = obj as BoxObj;
            if (o == null || o.Class != Class) return false;
            switch (Class.Name)
            {
                case "java/lang/Long": return V.J == o.V.J;
                case "java/lang/Double": return BitConverter.DoubleToInt64Bits(V.D) == BitConverter.DoubleToInt64Bits(o.V.D);
                case "java/lang/Float": return Jf.FloatBits(V.F) == Jf.FloatBits(o.V.F);
                default: return V.I == o.V.I;
            }
        }

        public override int GetHashCode()
        {
            switch (Class.Name)
            {
                case "java/lang/Long": return (int)(V.J ^ (long)((ulong)V.J >> 32));
                case "java/lang/Double": { long l = BitConverter.DoubleToInt64Bits(V.D); return (int)(l ^ (long)((ulong)l >> 32)); }
                case "java/lang/Float": return Jf.FloatBits(V.F);
                case "java/lang/Boolean": return V.I != 0 ? 1231 : 1237;
                default: return V.I;
            }
        }
    }

    public sealed class ThreadObj : JObject
    {
        public JThread Vt;
        public object Target;
        public string Name;
        public int Priority = 5;
        public volatile bool Alive, Started;
    }

    public static class Jf
    {
        public static int FloatBits(float f)
        {
            if (float.IsNaN(f)) return 0x7fc00000;
            return BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        }

        public static float BitsFloat(int i) => BitConverter.ToSingle(BitConverter.GetBytes(i), 0);

        public static string Double(double v) => Fmt(v, v.ToString("R", CultureInfo.InvariantCulture));

        public static string Float(float v) => Fmt(v, v.ToString("R", CultureInfo.InvariantCulture));

        static string Fmt(double v, string r)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            if (v == 0) return 1 / v < 0 ? "-0.0" : "0.0";
            bool neg = r[0] == '-';
            if (neg) r = r.Substring(1);
            int e = 0;
            int ei = r.IndexOfAny(new[] { 'E', 'e' });
            if (ei >= 0) { e = int.Parse(r.Substring(ei + 1), CultureInfo.InvariantCulture); r = r.Substring(0, ei); }
            int dot = r.IndexOf('.');
            string digits; int intLen;
            if (dot < 0) { digits = r; intLen = r.Length; } else { digits = r.Remove(dot, 1); intLen = dot; }
            int lz = 0;
            while (lz < digits.Length - 1 && digits[lz] == '0') { lz++; intLen--; }
            digits = digits.Substring(lz).TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int exp10 = intLen + e;
            double a = Math.Abs(v);
            string res;
            if (a >= 1e-3 && a < 1e7)
            {
                if (exp10 <= 0) res = "0." + new string('0', -exp10) + digits;
                else if (exp10 >= digits.Length) res = digits + new string('0', exp10 - digits.Length) + ".0";
                else res = digits.Substring(0, exp10) + "." + digits.Substring(exp10);
            }
            else res = digits.Substring(0, 1) + "." + (digits.Length > 1 ? digits.Substring(1) : "0") + "E" + (exp10 - 1);
            return neg ? "-" + res : res;
        }

        public static int Digit(char c, int radix)
        {
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'z') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'Z') d = c - 'A' + 10;
            else if (char.IsDigit(c)) d = (int)char.GetNumericValue(c);
            else return -1;
            return d < radix ? d : -1;
        }

        public static long ParseLong(string s, int radix, long min, long max)
        {
            if (s == null) throw new JavaThrow("java/lang/NumberFormatException", "null");
            if (radix < 2 || radix > 36 || s.Length == 0) throw new JavaThrow("java/lang/NumberFormatException", s);
            int i = 0;
            bool neg = false;
            if (s[0] == '-') { neg = true; i++; } else if (s[0] == '+') i++;
            if (i >= s.Length) throw new JavaThrow("java/lang/NumberFormatException", s);
            long acc = 0;
            try
            {
                checked
                {
                    for (; i < s.Length; i++)
                    {
                        int d = Digit(s[i], radix);
                        if (d < 0) throw new JavaThrow("java/lang/NumberFormatException", s);
                        acc = acc * radix - d;
                    }
                    if (!neg) acc = -acc;
                }
            }
            catch (OverflowException) { throw new JavaThrow("java/lang/NumberFormatException", s); }
            if (acc < min || acc > max) throw new JavaThrow("java/lang/NumberFormatException", s);
            return acc;
        }

        public static string ToString(long v, int radix)
        {
            if (radix < 2 || radix > 36) radix = 10;
            if (radix == 10) return v.ToString(CultureInfo.InvariantCulture);
            if (v == 0) return "0";
            var sb = new StringBuilder();
            bool neg = v < 0;
            while (v != 0)
            {
                int d = (int)(v % radix);
                if (d < 0) d = -d;
                sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10));
                v /= radix;
            }
            if (neg) sb.Insert(0, '-');
            return sb.ToString();
        }

        public static string Unsigned(long v, int shift, int bits)
        {
            ulong u = bits == 32 ? (uint)v : (ulong)v;
            if (u == 0) return "0";
            var sb = new StringBuilder();
            ulong mask = (1UL << shift) - 1;
            while (u != 0)
            {
                int d = (int)(u & mask);
                sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10));
                u >>= shift;
            }
            return sb.ToString();
        }

        public static double ParseDouble(string s)
        {
            if (s == null) throw new NullReferenceException();
            var t = s.Trim();
            if (t.EndsWith("f") || t.EndsWith("F") || t.EndsWith("d") || t.EndsWith("D")) t = t.Substring(0, t.Length - 1);
            if (t == "NaN") return double.NaN;
            if (t == "Infinity" || t == "+Infinity") return double.PositiveInfinity;
            if (t == "-Infinity") return double.NegativeInfinity;
            double d;
            if (t.Length == 0 || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JavaThrow("java/lang/NumberFormatException", s);
            return d;
        }
    }

    public static class Enc
    {
        public static string Default = "ISO-8859-1";

        static readonly char[] cp1251 =
            ("ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏ" +
             "ђ‘’“”•–—�™љ›њќћџ" +
             " ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї" +
             "°±Ііґµ¶·ё№є»јЅѕї").ToCharArray();

        public static string Norm(string enc)
        {
            if (enc == null) enc = Default;
            var e = enc.ToUpperInvariant().Replace("-", "").Replace("_", "");
            switch (e)
            {
                case "UTF8": return "UTF8";
                case "ISO88591": case "LATIN1": case "ISO8859_1": case "8859_1": return "LATIN1";
                case "USASCII": case "ASCII": return "ASCII";
                case "CP1251": case "WINDOWS1251": case "WIN1251": return "CP1251";
                case "UTF16BE": case "UNICODEBIG": case "UNICODEBIGUNMARKED": return "UTF16BE";
                case "UTF16LE": case "UNICODELITTLE": case "UNICODELITTLEUNMARKED": return "UTF16LE";
                case "UTF16": return "UTF16";
            }
            throw new JavaThrow("java/io/UnsupportedEncodingException", enc);
        }

        public static sbyte[] Encode(string s, string enc)
        {
            switch (Norm(enc))
            {
                case "UTF8": return ToS(Encoding.UTF8.GetBytes(s));
                case "ASCII":
                case "LATIN1":
                    {
                        int lim = Norm(enc) == "ASCII" ? 0x80 : 0x100;
                        var r = new sbyte[s.Length];
                        for (int i = 0; i < s.Length; i++) r[i] = (sbyte)(s[i] < lim ? s[i] : '?');
                        return r;
                    }
                case "CP1251":
                    {
                        var r = new sbyte[s.Length];
                        for (int i = 0; i < s.Length; i++)
                        {
                            char c = s[i];
                            if (c < 0x80) r[i] = (sbyte)c;
                            else if (c >= 0x410 && c <= 0x44F) r[i] = (sbyte)(c - 0x410 + 0xC0);
                            else
                            {
                                int k = Array.IndexOf(cp1251, c);
                                r[i] = (sbyte)(k >= 0 ? k + 0x80 : '?');
                            }
                        }
                        return r;
                    }
                case "UTF16LE": return ToS(Encoding.Unicode.GetBytes(s));
                case "UTF16":
                    {
                        var b = Encoding.BigEndianUnicode.GetBytes(s);
                        var r = new sbyte[b.Length + 2];
                        r[0] = -2; r[1] = -1;
                        Buffer.BlockCopy(b, 0, r, 2, b.Length);
                        return r;
                    }
                default: return ToS(Encoding.BigEndianUnicode.GetBytes(s));
            }
        }

        public static string Decode(sbyte[] b, int off, int len, string enc)
        {
            switch (Norm(enc))
            {
                case "UTF8": return Encoding.UTF8.GetString(ToU(b, off, len), 0, len);
                case "ASCII":
                case "LATIN1":
                    {
                        var c = new char[len];
                        for (int i = 0; i < len; i++) c[i] = (char)(byte)b[off + i];
                        return new string(c);
                    }
                case "CP1251":
                    {
                        var c = new char[len];
                        for (int i = 0; i < len; i++) c[i] = Cp1251((byte)b[off + i]);
                        return new string(c);
                    }
                case "UTF16LE": return Encoding.Unicode.GetString(ToU(b, off, len), 0, len);
                case "UTF16":
                    if (len >= 2 && b[off] == -1 && b[off + 1] == -2) return Encoding.Unicode.GetString(ToU(b, off + 2, len - 2), 0, len - 2);
                    if (len >= 2 && b[off] == -2 && b[off + 1] == -1) { off += 2; len -= 2; }
                    return Encoding.BigEndianUnicode.GetString(ToU(b, off, len), 0, len);
                default: return Encoding.BigEndianUnicode.GetString(ToU(b, off, len), 0, len);
            }
        }

        public static char Cp1251(int b)
        {
            if (b < 0x80) return (char)b;
            if (b >= 0xC0) return (char)(b - 0xC0 + 0x410);
            return cp1251[b - 0x80];
        }

        public static sbyte[] ToS(byte[] b)
        {
            var r = new sbyte[b.Length];
            Buffer.BlockCopy(b, 0, r, 0, b.Length);
            return r;
        }

        public static byte[] ToU(sbyte[] b, int off, int len)
        {
            var r = new byte[len];
            Buffer.BlockCopy(b, off, r, 0, len);
            return r;
        }

        public static byte[] ToU(sbyte[] b) => ToU(b, 0, b.Length);
    }

    public static class Rt
    {
        public static int JavaHash(string s)
        {
            int h = 0;
            for (int i = 0; i < s.Length; i++) h = 31 * h + s[i];
            return h;
        }

        public static string Str(JThread t, object o)
        {
            if (o == null) return "null";
            var s = o as string;
            if (s != null) return s;
            var m = t.Virtual(o, "toString()Ljava/lang/String;");
            return (string)t.CallRef(m, o) ?? "null";
        }

        public static bool JEquals(JThread t, object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            var sa = a as string;
            if (sa != null) return b is string && sa == (string)b;
            if (a is BoxObj) return a.Equals(b);
            if (!(a is JObject)) return false;
            var m = t.Virtual(a, "equals(Ljava/lang/Object;)Z");
            if (m.Owner == t.Vm.ObjectClass) return false;
            return t.CallPrim(m, a, b).I != 0;
        }

        public static int JHash(JThread t, object o)
        {
            var s = o as string;
            if (s != null) return JavaHash(s);
            if (o is BoxObj) return o.GetHashCode();
            if (!(o is JObject)) return t.Vm.IdentityHash(o);
            var m = t.Virtual(o, "hashCode()I");
            if (m.Owner == t.Vm.ObjectClass) return t.Vm.IdentityHash(o);
            return t.CallPrim(m, o).I;
        }

        public static void Idx(int i, int n)
        {
            if (i < 0 || i >= n) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException", i + " >= " + n);
        }

        public static void SIdx(int i, int n)
        {
            if (i < 0 || i > n) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", "index " + i + ", length " + n);
        }

        public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public static long Mono() => clock.ElapsedMilliseconds;

        public static long MonoUs() => clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;

        public static void Sleep(JThread t, long ms)
        {
            if (t.Interrupted) { t.Interrupted = false; throw new JavaThrow("java/lang/InterruptedException"); }
            NativeSync.EnsureHires();
            long end = MonoUs() + ms * 1000;
            long slack = NativeSync.HiresOk ? 1500 : 16500;
            t.Wake.Reset();
            while (true)
            {
                long left = end - MonoUs();
                if (left <= 0) break;
                if (left > slack) t.Wake.Wait((int)Math.Max(1, Math.Min((left - slack) / 1000, 250)));
                else NativeSync.Pause();
                if (t.Vm.Halted) throw new VmHalt();
                if (t.Interrupted) { t.Interrupted = false; throw new JavaThrow("java/lang/InterruptedException"); }
            }
            if (t.Vm.Halted) throw new VmHalt();
        }

        public static void Yield(JThread t)
        {
            if (t.Vm.Halted) throw new VmHalt();
            NativeSync.Pause();
        }

        public static void Wait(JThread t, object o, long ms)
        {
            if (!Monitor.IsEntered(o)) throw new JavaThrow("java/lang/IllegalMonitorStateException");
            if (t.Interrupted) { t.Interrupted = false; throw new JavaThrow("java/lang/InterruptedException"); }
            long end = ms > 0 ? Mono() + ms : long.MaxValue;
            while (true)
            {
                long left = end - Mono();
                if (left <= 0) return;
                if (Monitor.Wait(o, (int)Math.Min(left, 250))) return;
                if (t.Vm.Halted) throw new VmHalt();
                if (t.Interrupted) { t.Interrupted = false; throw new JavaThrow("java/lang/InterruptedException"); }
            }
        }

        public static string ClassToResource(JClass c, string name)
        {
            if (name.StartsWith("/")) return name.Substring(1);
            var p = c.Package;
            return p.Length == 0 ? name : p + "/" + name;
        }

        public static Func<JClass, JObject> F<T>() where T : JObject, new()
        {
            return k => { var o = new T(); o.Init(k); return o; };
        }
    }

    public static class NativeSync
    {
        [System.Runtime.InteropServices.DllImport("api-ms-win-core-synch-l1-2-0.dll")]
        public static extern void Sleep(uint ms);

        [System.Runtime.InteropServices.DllImport("api-ms-win-mm-time-l1-1-0.dll")]
        static extern uint timeBeginPeriod(uint period);

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        static extern int NtSetTimerResolution(uint desired, bool set, out uint current);

        static bool tried, sleepOk = true;
        public static bool HiresOk;
        public static string Hires = "untried";

        public static void EnsureHires()
        {
            if (tried) return;
            tried = true;
            try { if (timeBeginPeriod(1) == 0) { HiresOk = true; Hires = "timeBeginPeriod(1)"; return; } }
            catch (Exception) { }
            try
            {
                uint cur;
                if (NtSetTimerResolution(10000, true, out cur) == 0) { HiresOk = cur <= 20000; Hires = "NtSetTimerResolution " + cur / 10000.0 + "ms"; return; }
            }
            catch (Exception) { }
            Hires = "default timer";
        }

        public static void Pause()
        {
            if (sleepOk)
            {
                try { Sleep(0); return; }
                catch (Exception) { sleepOk = false; }
            }
            var sw = new SpinWait();
            for (int i = 0; i < 20; i++) sw.SpinOnce();
        }
    }
}
