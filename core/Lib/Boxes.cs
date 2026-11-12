using System;
using System.Globalization;
using Lumidlet.Vm;

namespace Lumidlet.Lib
{
    public static partial class Library
    {
        static BoxObj Box(JThread t, string cls)
        {
            var c = t.Vm.LoadClass(cls);
            t.Vm.EnsureInit(t, c);
            return (BoxObj)t.Vm.NewObject(c);
        }

        public static BoxObj BoxInt(JThread t, int v) { var o = Box(t, "java/lang/Integer"); o.V.I = v; return o; }
        public static BoxObj BoxBool(JThread t, bool v) { var o = Box(t, "java/lang/Boolean"); o.V.I = v ? 1 : 0; return o; }

        static BoxObj V(JThread t, int b) => (BoxObj)t.R[b];

        static void Boxes(NativeRegistry n)
        {
            n.Define("java/lang/Number", "java/lang/Object", c => c
                .M("intValue", "()I", (t, b) => t.RetI(b, NumI(V(t, b))))
                .M("longValue", "()J", (t, b) => t.RetJ(b, NumJ(V(t, b))))
                .M("floatValue", "()F", (t, b) => t.RetF(b, (float)NumD(V(t, b))))
                .M("doubleValue", "()D", (t, b) => t.RetD(b, NumD(V(t, b))))
                .M("shortValue", "()S", (t, b) => t.RetI(b, (short)NumI(V(t, b))))
                .M("byteValue", "()B", (t, b) => t.RetI(b, (sbyte)NumI(V(t, b))))
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, t.R[b].Equals(t.R[b + 1])))
                .M("hashCode", "()I", (t, b) => t.RetI(b, t.R[b].GetHashCode())), Rt.F<BoxObj>(), access: 0x0401);

            IntLike(n, "java/lang/Integer", "I", int.MinValue, int.MaxValue);
            IntLike(n, "java/lang/Short", "S", short.MinValue, short.MaxValue);
            IntLike(n, "java/lang/Byte", "B", sbyte.MinValue, sbyte.MaxValue);

            n.Define("java/lang/Integer", "java/lang/Number", c => { IntLikeBody(c, "java/lang/Integer", "I", int.MinValue, int.MaxValue); c
                .S("toHexString", "(I)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.I(b), 4, 32)))
                .S("toOctalString", "(I)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.I(b), 3, 32)))
                .S("toBinaryString", "(I)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.I(b), 1, 32)))
                .S("toString", "(II)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.ToString(t.I(b), t.I(b + 1))))
                .S("bitCount", "(I)I", (t, b) => { uint v = (uint)t.I(b); int k = 0; while (v != 0) { k += (int)(v & 1); v >>= 1; } t.RetI(b, k); })
                .S("reverseBytes", "(I)I", (t, b) => { uint v = (uint)t.I(b); t.RetI(b, (int)((v >> 24) | ((v >> 8) & 0xff00) | ((v << 8) & 0xff0000) | (v << 24))); }); },
                Rt.F<BoxObj>(), ifaces: new[] { "java/lang/Comparable" }, access: 0x0011);

            n.Define("java/lang/Long", "java/lang/Number", c => c
                .M("<init>", "(J)V", (t, b) => V(t, b).V.J = t.J(b + 1))
                .M("longValue", "()J", (t, b) => t.RetJ(b, V(t, b).V.J))
                .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, V(t, b).V.J.ToString(CultureInfo.InvariantCulture)))
                .M("compareTo", "(Ljava/lang/Long;)I", (t, b) => t.RetI(b, V(t, b).V.J.CompareTo(V(t, b + 1).V.J)))
                .S("parseLong", "(Ljava/lang/String;)J", (t, b) => t.RetJ(b, Jf.ParseLong(S(t, b), 10, long.MinValue, long.MaxValue)))
                .S("parseLong", "(Ljava/lang/String;I)J", (t, b) => t.RetJ(b, Jf.ParseLong(S(t, b), t.I(b + 1), long.MinValue, long.MaxValue)))
                .S("valueOf", "(J)Ljava/lang/Long;", (t, b) => { var o = Box(t, "java/lang/Long"); o.V.J = t.J(b); t.RetA(b, o); })
                .S("valueOf", "(Ljava/lang/String;)Ljava/lang/Long;", (t, b) => { var o = Box(t, "java/lang/Long"); o.V.J = Jf.ParseLong(S(t, b), 10, long.MinValue, long.MaxValue); t.RetA(b, o); })
                .S("toString", "(J)Ljava/lang/String;", (t, b) => t.RetA(b, t.J(b).ToString(CultureInfo.InvariantCulture)))
                .S("toString", "(JI)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.ToString(t.J(b), t.I(b + 2))))
                .S("toHexString", "(J)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.J(b), 4, 64)))
                .S("toBinaryString", "(J)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.J(b), 1, 64)))
                .S("toOctalString", "(J)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Unsigned(t.J(b), 3, 64))),
                Rt.F<BoxObj>(), access: 0x0011);

            n.Define("java/lang/Float", "java/lang/Number", c => c
                .M("<init>", "(F)V", (t, b) => V(t, b).V.F = t.F(b + 1))
                .M("<init>", "(D)V", (t, b) => V(t, b).V.F = (float)t.D(b + 1))
                .M("floatValue", "()F", (t, b) => t.RetF(b, V(t, b).V.F))
                .M("isNaN", "()Z", (t, b) => t.RetZ(b, float.IsNaN(V(t, b).V.F)))
                .M("isInfinite", "()Z", (t, b) => t.RetZ(b, float.IsInfinity(V(t, b).V.F)))
                .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Float(V(t, b).V.F)))
                .S("isNaN", "(F)Z", (t, b) => t.RetZ(b, float.IsNaN(t.F(b))))
                .S("isInfinite", "(F)Z", (t, b) => t.RetZ(b, float.IsInfinity(t.F(b))))
                .S("parseFloat", "(Ljava/lang/String;)F", (t, b) => t.RetF(b, (float)Jf.ParseDouble(S(t, b))))
                .S("valueOf", "(Ljava/lang/String;)Ljava/lang/Float;", (t, b) => { var o = Box(t, "java/lang/Float"); o.V.F = (float)Jf.ParseDouble(S(t, b)); t.RetA(b, o); })
                .S("valueOf", "(F)Ljava/lang/Float;", (t, b) => { var o = Box(t, "java/lang/Float"); o.V.F = t.F(b); t.RetA(b, o); })
                .S("toString", "(F)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Float(t.F(b))))
                .S("floatToIntBits", "(F)I", (t, b) => t.RetI(b, Jf.FloatBits(t.F(b))))
                .S("floatToRawIntBits", "(F)I", (t, b) => t.RetI(b, t.S[b].I))
                .S("intBitsToFloat", "(I)F", (t, b) => { })
                .S("compare", "(FF)I", (t, b) => t.RetI(b, t.F(b).CompareTo(t.F(b + 1)))),
                Rt.F<BoxObj>(), access: 0x0011);

            n.Define("java/lang/Double", "java/lang/Number", c => c
                .M("<init>", "(D)V", (t, b) => V(t, b).V.D = t.D(b + 1))
                .M("doubleValue", "()D", (t, b) => t.RetD(b, V(t, b).V.D))
                .M("isNaN", "()Z", (t, b) => t.RetZ(b, double.IsNaN(V(t, b).V.D)))
                .M("isInfinite", "()Z", (t, b) => t.RetZ(b, double.IsInfinity(V(t, b).V.D)))
                .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Double(V(t, b).V.D)))
                .S("isNaN", "(D)Z", (t, b) => t.RetZ(b, double.IsNaN(t.D(b))))
                .S("isInfinite", "(D)Z", (t, b) => t.RetZ(b, double.IsInfinity(t.D(b))))
                .S("parseDouble", "(Ljava/lang/String;)D", (t, b) => t.RetD(b, Jf.ParseDouble(S(t, b))))
                .S("valueOf", "(Ljava/lang/String;)Ljava/lang/Double;", (t, b) => { var o = Box(t, "java/lang/Double"); o.V.D = Jf.ParseDouble(S(t, b)); t.RetA(b, o); })
                .S("valueOf", "(D)Ljava/lang/Double;", (t, b) => { var o = Box(t, "java/lang/Double"); o.V.D = t.D(b); t.RetA(b, o); })
                .S("toString", "(D)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Double(t.D(b))))
                .S("doubleToLongBits", "(D)J", (t, b) => { double d = t.D(b); t.RetJ(b, double.IsNaN(d) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(d)); })
                .S("doubleToRawLongBits", "(D)J", (t, b) => { })
                .S("longBitsToDouble", "(J)D", (t, b) => { })
                .S("compare", "(DD)I", (t, b) => t.RetI(b, t.D(b).CompareTo(t.D(b + 2)))),
                Rt.F<BoxObj>(), access: 0x0011);

            n.Define("java/lang/Boolean", "java/lang/Object", c => c
                .Static("TRUE", "Ljava/lang/Boolean;", t => { var o = Box(t, "java/lang/Boolean"); o.V.I = 1; return o; })
                .Static("FALSE", "Ljava/lang/Boolean;", t => Box(t, "java/lang/Boolean"))
                .M("<init>", "(Z)V", (t, b) => V(t, b).V.I = t.I(b + 1) & 1)
                .M("booleanValue", "()Z", (t, b) => t.RetI(b, V(t, b).V.I))
                .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, V(t, b).V.I != 0 ? "true" : "false"))
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, t.R[b].Equals(t.R[b + 1])))
                .M("hashCode", "()I", (t, b) => t.RetI(b, t.R[b].GetHashCode()))
                .S("valueOf", "(Z)Ljava/lang/Boolean;", (t, b) => { var k = t.Vm.LoadClass("java/lang/Boolean"); t.Vm.EnsureInit(t, k); t.RetA(b, k.StaticO[t.Z(b) ? 0 : 1]); })
                .S("parseBoolean", "(Ljava/lang/String;)Z", (t, b) => t.RetZ(b, string.Equals(S(t, b), "true", StringComparison.OrdinalIgnoreCase)))
                .S("toString", "(Z)Ljava/lang/String;", (t, b) => t.RetA(b, t.Z(b) ? "true" : "false")),
                Rt.F<BoxObj>(), access: 0x0011);

            n.Define("java/lang/Character", "java/lang/Object", c => c
                .M("<init>", "(C)V", (t, b) => V(t, b).V.I = (char)t.I(b + 1))
                .M("charValue", "()C", (t, b) => t.RetI(b, V(t, b).V.I))
                .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((char)V(t, b).V.I).ToString()))
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, t.R[b].Equals(t.R[b + 1])))
                .M("hashCode", "()I", (t, b) => t.RetI(b, V(t, b).V.I))
                .M("compareTo", "(Ljava/lang/Character;)I", (t, b) => t.RetI(b, V(t, b).V.I - V(t, b + 1).V.I))
                .S("valueOf", "(C)Ljava/lang/Character;", (t, b) => { var o = Box(t, "java/lang/Character"); o.V.I = (char)t.I(b); t.RetA(b, o); })
                .S("toString", "(C)Ljava/lang/String;", (t, b) => t.RetA(b, ((char)t.I(b)).ToString()))
                .S("isDigit", "(C)Z", (t, b) => t.RetZ(b, char.IsDigit((char)t.I(b))))
                .S("isLetter", "(C)Z", (t, b) => t.RetZ(b, char.IsLetter((char)t.I(b))))
                .S("isLetterOrDigit", "(C)Z", (t, b) => t.RetZ(b, char.IsLetterOrDigit((char)t.I(b))))
                .S("isUpperCase", "(C)Z", (t, b) => t.RetZ(b, char.IsUpper((char)t.I(b))))
                .S("isLowerCase", "(C)Z", (t, b) => t.RetZ(b, char.IsLower((char)t.I(b))))
                .S("isSpaceChar", "(C)Z", (t, b) => t.RetZ(b, char.IsSeparator((char)t.I(b))))
                .S("isWhitespace", "(C)Z", (t, b) => t.RetZ(b, char.IsWhiteSpace((char)t.I(b))))
                .S("toUpperCase", "(C)C", (t, b) => t.RetI(b, char.ToUpperInvariant((char)t.I(b))))
                .S("toLowerCase", "(C)C", (t, b) => t.RetI(b, char.ToLowerInvariant((char)t.I(b))))
                .S("digit", "(CI)I", (t, b) => t.RetI(b, Jf.Digit((char)t.I(b), t.I(b + 1))))
                .S("forDigit", "(II)C", (t, b) => { int d = t.I(b), r = t.I(b + 1); t.RetI(b, r < 2 || r > 36 || d < 0 || d >= r ? 0 : d < 10 ? '0' + d : 'a' + d - 10); }),
                Rt.F<BoxObj>(), access: 0x0011);
        }

        static void IntLike(NativeRegistry n, string cls, string k, long min, long max)
        {
            if (cls == "java/lang/Integer") return;
            n.Define(cls, "java/lang/Number", c => IntLikeBody(c, cls, k, min, max), Rt.F<BoxObj>(), access: 0x0011);
        }

        static void IntLikeBody(ClassBuilder c, string cls, string k, long min, long max)
        {
            string self = "L" + cls + ";";
            string parse = cls == "java/lang/Integer" ? "parseInt" : cls == "java/lang/Short" ? "parseShort" : "parseByte";
            string getter = cls == "java/lang/Integer" ? "intValue" : cls == "java/lang/Short" ? "shortValue" : "byteValue";
            c.M("<init>", "(" + k + ")V", (t, b) => V(t, b).V.I = t.I(b + 1))
             .M(getter, "()" + k, (t, b) => t.RetI(b, V(t, b).V.I))
             .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, V(t, b).V.I.ToString(CultureInfo.InvariantCulture)))
             .M("compareTo", "(" + self + ")I", (t, b) => t.RetI(b, V(t, b).V.I.CompareTo(V(t, b + 1).V.I)))
             .M("compareTo", "(Ljava/lang/Object;)I", (t, b) => t.RetI(b, V(t, b).V.I.CompareTo(V(t, b + 1).V.I)))
             .S(parse, "(Ljava/lang/String;)" + k, (t, b) => t.RetI(b, (int)Jf.ParseLong(S(t, b), 10, min, max)))
             .S(parse, "(Ljava/lang/String;I)" + k, (t, b) => t.RetI(b, (int)Jf.ParseLong(S(t, b), t.I(b + 1), min, max)))
             .S("valueOf", "(Ljava/lang/String;)" + self, (t, b) => { var o = Box(t, cls); o.V.I = (int)Jf.ParseLong(S(t, b), 10, min, max); t.RetA(b, o); })
             .S("valueOf", "(Ljava/lang/String;I)" + self, (t, b) => { var o = Box(t, cls); o.V.I = (int)Jf.ParseLong(S(t, b), t.I(b + 1), min, max); t.RetA(b, o); })
             .S("valueOf", "(" + k + ")" + self, (t, b) => { var o = Box(t, cls); o.V.I = t.I(b); t.RetA(b, o); })
             .S("toString", "(" + k + ")Ljava/lang/String;", (t, b) => t.RetA(b, t.I(b).ToString(CultureInfo.InvariantCulture)));
        }

        static int NumI(BoxObj o)
        {
            switch (o.Class.Name)
            {
                case "java/lang/Long": return (int)o.V.J;
                case "java/lang/Float": return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, float.IsNaN(o.V.F) ? 0 : o.V.F));
                case "java/lang/Double": return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, double.IsNaN(o.V.D) ? 0 : o.V.D));
                default: return o.V.I;
            }
        }

        static long NumJ(BoxObj o)
        {
            switch (o.Class.Name)
            {
                case "java/lang/Long": return o.V.J;
                case "java/lang/Float": return float.IsNaN(o.V.F) ? 0 : (long)Math.Max(long.MinValue, Math.Min(long.MaxValue, o.V.F));
                case "java/lang/Double": return double.IsNaN(o.V.D) ? 0 : (long)Math.Max(long.MinValue, Math.Min(long.MaxValue, o.V.D));
                default: return o.V.I;
            }
        }

        static double NumD(BoxObj o)
        {
            switch (o.Class.Name)
            {
                case "java/lang/Long": return o.V.J;
                case "java/lang/Float": return o.V.F;
                case "java/lang/Double": return o.V.D;
                default: return o.V.I;
            }
        }

        static void Throwables(NativeRegistry n)
        {
            n.Define("java/lang/Throwable", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => TInit(t, b, null, null))
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => TInit(t, b, S(t, b + 1), null))
                .M("<init>", "(Ljava/lang/String;Ljava/lang/Throwable;)V", (t, b) => TInit(t, b, S(t, b + 1), (JObject)t.R[b + 2]))
                .M("<init>", "(Ljava/lang/Throwable;)V", (t, b) =>
                {
                    var cause = (JObject)t.R[b + 1];
                    TInit(t, b, cause == null ? null : Rt.Str(t, cause), cause);
                })
                .M("getMessage", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((ThrowableObj)t.R[b]).Message))
                .M("getLocalizedMessage", "()Ljava/lang/String;", (t, b) => t.RetA(b, t.CallVirtualRef(t.R[b], "getMessage()Ljava/lang/String;")))
                .M("getCause", "()Ljava/lang/Throwable;", (t, b) => t.RetA(b, ((ThrowableObj)t.R[b]).Cause))
                .M("initCause", "(Ljava/lang/Throwable;)Ljava/lang/Throwable;", (t, b) => { ((ThrowableObj)t.R[b]).Cause = (JObject)t.R[b + 1]; })
                .M("fillInStackTrace", "()Ljava/lang/Throwable;", (t, b) => { ((ThrowableObj)t.R[b]).Trace = t.CaptureTrace(); })
                .M("toString", "()Ljava/lang/String;", (t, b) =>
                {
                    var msg = (string)t.CallVirtualRef(t.R[b], "getLocalizedMessage()Ljava/lang/String;");
                    var name = ((JObject)t.R[b]).Class.JavaName;
                    t.RetA(b, msg != null ? name + ": " + msg : name);
                })
                .M("printStackTrace", "()V", (t, b) =>
                {
                    var th = (ThrowableObj)t.R[b];
                    var head = Rt.Str(t, th);
                    var sb = new System.Text.StringBuilder(head);
                    if (th.Trace != null) foreach (var m in th.Trace) sb.Append("\n    at ").Append(m);
                    t.Vm.Log(sb.ToString());
                }), Rt.F<ThrowableObj>());

            string[,] tree =
            {
                { "java/lang/Exception", "java/lang/Throwable" },
                { "java/lang/Error", "java/lang/Throwable" },
                { "java/lang/RuntimeException", "java/lang/Exception" },
                { "java/lang/ArithmeticException", "java/lang/RuntimeException" },
                { "java/lang/ArrayStoreException", "java/lang/RuntimeException" },
                { "java/lang/ClassCastException", "java/lang/RuntimeException" },
                { "java/lang/IllegalArgumentException", "java/lang/RuntimeException" },
                { "java/lang/NumberFormatException", "java/lang/IllegalArgumentException" },
                { "java/lang/IllegalThreadStateException", "java/lang/IllegalArgumentException" },
                { "java/lang/IllegalMonitorStateException", "java/lang/RuntimeException" },
                { "java/lang/IllegalStateException", "java/lang/RuntimeException" },
                { "java/lang/IndexOutOfBoundsException", "java/lang/RuntimeException" },
                { "java/lang/ArrayIndexOutOfBoundsException", "java/lang/IndexOutOfBoundsException" },
                { "java/lang/StringIndexOutOfBoundsException", "java/lang/IndexOutOfBoundsException" },
                { "java/lang/NegativeArraySizeException", "java/lang/RuntimeException" },
                { "java/lang/NullPointerException", "java/lang/RuntimeException" },
                { "java/lang/SecurityException", "java/lang/RuntimeException" },
                { "java/lang/UnsupportedOperationException", "java/lang/RuntimeException" },
                { "java/util/EmptyStackException", "java/lang/RuntimeException" },
                { "java/util/NoSuchElementException", "java/lang/RuntimeException" },
                { "java/util/ConcurrentModificationException", "java/lang/RuntimeException" },
                { "java/lang/ClassNotFoundException", "java/lang/Exception" },
                { "java/lang/CloneNotSupportedException", "java/lang/Exception" },
                { "java/lang/IllegalAccessException", "java/lang/Exception" },
                { "java/lang/InstantiationException", "java/lang/Exception" },
                { "java/lang/InterruptedException", "java/lang/Exception" },
                { "java/io/IOException", "java/lang/Exception" },
                { "java/io/EOFException", "java/io/IOException" },
                { "java/io/InterruptedIOException", "java/io/IOException" },
                { "java/io/UnsupportedEncodingException", "java/io/IOException" },
                { "java/io/UTFDataFormatException", "java/io/IOException" },
                { "javax/microedition/io/ConnectionNotFoundException", "java/io/IOException" },
                { "javax/microedition/midlet/MIDletStateChangeException", "java/lang/Exception" },
                { "javax/microedition/rms/RecordStoreException", "java/lang/Exception" },
                { "javax/microedition/rms/RecordStoreFullException", "javax/microedition/rms/RecordStoreException" },
                { "javax/microedition/rms/RecordStoreNotFoundException", "javax/microedition/rms/RecordStoreException" },
                { "javax/microedition/rms/RecordStoreNotOpenException", "javax/microedition/rms/RecordStoreException" },
                { "javax/microedition/rms/InvalidRecordIDException", "javax/microedition/rms/RecordStoreException" },
                { "javax/microedition/media/MediaException", "java/lang/Exception" },
                { "javax/microedition/lcdui/game/GameCanvasException", "java/lang/RuntimeException" },
                { "java/lang/VirtualMachineError", "java/lang/Error" },
                { "java/lang/OutOfMemoryError", "java/lang/VirtualMachineError" },
                { "java/lang/StackOverflowError", "java/lang/VirtualMachineError" },
                { "java/lang/InternalError", "java/lang/VirtualMachineError" },
                { "java/lang/LinkageError", "java/lang/Error" },
                { "java/lang/NoClassDefFoundError", "java/lang/LinkageError" },
                { "java/lang/ClassFormatError", "java/lang/LinkageError" },
                { "java/lang/VerifyError", "java/lang/LinkageError" },
                { "java/lang/UnsatisfiedLinkError", "java/lang/LinkageError" },
                { "java/lang/ExceptionInInitializerError", "java/lang/LinkageError" },
                { "java/lang/IncompatibleClassChangeError", "java/lang/LinkageError" },
                { "java/lang/AbstractMethodError", "java/lang/IncompatibleClassChangeError" },
                { "java/lang/NoSuchFieldError", "java/lang/IncompatibleClassChangeError" },
                { "java/lang/NoSuchMethodError", "java/lang/IncompatibleClassChangeError" },
                { "java/lang/InstantiationError", "java/lang/IncompatibleClassChangeError" },
            };
            for (int i = 0; i < tree.GetLength(0); i++) n.Define(tree[i, 0], tree[i, 1], null);
        }

        static void TInit(JThread t, int b, string msg, JObject cause)
        {
            var th = (ThrowableObj)t.R[b];
            th.Message = msg;
            th.Cause = cause;
            th.Trace = t.CaptureTrace();
        }
    }
}
