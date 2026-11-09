using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumidlet.Vm;

namespace Lumidlet.Lib
{
    public static partial class Library
    {
        public static void Register(JVm vm)
        {
            var n = vm.Natives;
            Lang(n);
            Boxes(n);
            Throwables(n);
            Util(n);
            Io(n);
        }

        static string S(JThread t, int i) => (string)t.R[i];

        static void Lang(NativeRegistry n)
        {
            n.Define("java/lang/Object", null, c => c
                .M("<init>", "()V", (t, b) => { })
                .M("hashCode", "()I", (t, b) => t.RetI(b, t.Vm.IdentityHash(t.R[b])))
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, ReferenceEquals(t.R[b], t.R[b + 1])))
                .M("toString", "()Ljava/lang/String;", (t, b) =>
                {
                    var o = t.R[b];
                    int h = t.CallVirtualInt(o, "hashCode()I");
                    t.RetA(b, t.Vm.ClassOf(o).JavaName + "@" + ((uint)h).ToString("x"));
                })
                .M("getClass", "()Ljava/lang/Class;", (t, b) => t.RetA(b, t.Vm.ClassObject(t.Vm.ClassOf(t.R[b]))))
                .M("notify", "()V", (t, b) => { if (!Monitor.IsEntered(t.R[b])) throw new JavaThrow("java/lang/IllegalMonitorStateException"); Monitor.Pulse(t.R[b]); })
                .M("notifyAll", "()V", (t, b) => { if (!Monitor.IsEntered(t.R[b])) throw new JavaThrow("java/lang/IllegalMonitorStateException"); Monitor.PulseAll(t.R[b]); })
                .M("wait", "()V", (t, b) => Rt.Wait(t, t.R[b], 0))
                .M("wait", "(J)V", (t, b) => Rt.Wait(t, t.R[b], t.J(b + 1)))
                .M("wait", "(JI)V", (t, b) => Rt.Wait(t, t.R[b], Math.Max(t.J(b + 1), t.I(b + 3) > 0 ? 1 : 0)))
                .M("finalize", "()V", (t, b) => { })
                .M("clone", "()Ljava/lang/Object;", (t, b) => t.RetA(b, Clone(t, t.R[b]))));

            n.Define("java/lang/String", "java/lang/Object", Str, Rt.F<StrPlaceholder>(), ifaces: new[] { "java/lang/CharSequence", "java/lang/Comparable" }, access: 0x0011);

            NativeFn sbAppendStr = (t, b) => { ((StringBufferObj)t.R[b]).Sb.Append(S(t, b + 1) ?? "null"); };
            foreach (var name in new[] { "java/lang/StringBuffer", "java/lang/StringBuilder" })
            {
                var self = "L" + name + ";";
                n.Define(name, "java/lang/Object", c => c
                    .M("<init>", "()V", (t, b) => { })
                    .M("<init>", "(I)V", (t, b) => { if (t.I(b + 1) < 0) throw new JavaThrow("java/lang/NegativeArraySizeException"); ((StringBufferObj)t.R[b]).Sb.Capacity = t.I(b + 1); })
                    .M("<init>", "(Ljava/lang/String;)V", (t, b) => { if (t.R[b + 1] == null) throw new NullReferenceException(); ((StringBufferObj)t.R[b]).Sb.Append(S(t, b + 1)); })
                    .M("<init>", "(Ljava/lang/CharSequence;)V", (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Rt.Str(t, t.R[b + 1])))
                    .M("append", "(Ljava/lang/String;)" + self, sbAppendStr)
                    .M("append", "(Ljava/lang/Object;)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Rt.Str(t, t.R[b + 1])))
                    .M("append", "(Ljava/lang/CharSequence;)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Rt.Str(t, t.R[b + 1])))
                    .M("append", "(Ljava/lang/StringBuffer;)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Rt.Str(t, t.R[b + 1])))
                    .M("append", "([C)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append((char[])t.R[b + 1]))
                    .M("append", "([CII)" + self, (t, b) => { var a = (char[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3); if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); ((StringBufferObj)t.R[b]).Sb.Append(a, o, l); })
                    .M("append", "(Z)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(t.Z(b + 1) ? "true" : "false"))
                    .M("append", "(C)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append((char)t.I(b + 1)))
                    .M("append", "(I)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(t.I(b + 1).ToString(CultureInfo.InvariantCulture)))
                    .M("append", "(J)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(t.J(b + 1).ToString(CultureInfo.InvariantCulture)))
                    .M("append", "(F)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Jf.Float(t.F(b + 1))))
                    .M("append", "(D)" + self, (t, b) => ((StringBufferObj)t.R[b]).Sb.Append(Jf.Double(t.D(b + 1))))
                    .M("insert", "(ILjava/lang/String;)" + self, (t, b) => Ins(t, b, S(t, b + 2) ?? "null"))
                    .M("insert", "(ILjava/lang/Object;)" + self, (t, b) => Ins(t, b, Rt.Str(t, t.R[b + 2])))
                    .M("insert", "(I[C)" + self, (t, b) => Ins(t, b, new string((char[])t.R[b + 2])))
                    .M("insert", "(IZ)" + self, (t, b) => Ins(t, b, t.Z(b + 2) ? "true" : "false"))
                    .M("insert", "(IC)" + self, (t, b) => Ins(t, b, ((char)t.I(b + 2)).ToString()))
                    .M("insert", "(II)" + self, (t, b) => Ins(t, b, t.I(b + 2).ToString(CultureInfo.InvariantCulture)))
                    .M("insert", "(IJ)" + self, (t, b) => Ins(t, b, t.J(b + 2).ToString(CultureInfo.InvariantCulture)))
                    .M("insert", "(IF)" + self, (t, b) => Ins(t, b, Jf.Float(t.F(b + 2))))
                    .M("insert", "(ID)" + self, (t, b) => Ins(t, b, Jf.Double(t.D(b + 2))))
                    .M("length", "()I", (t, b) => t.RetI(b, ((StringBufferObj)t.R[b]).Sb.Length))
                    .M("capacity", "()I", (t, b) => t.RetI(b, ((StringBufferObj)t.R[b]).Sb.Capacity))
                    .M("ensureCapacity", "(I)V", (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; if (t.I(b + 1) > sb.Capacity) sb.Capacity = t.I(b + 1); })
                    .M("trimToSize", "()V", (t, b) => { })
                    .M("charAt", "(I)C", (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int i = t.I(b + 1); if (i < 0 || i >= sb.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", i.ToString()); t.RetI(b, sb[i]); })
                    .M("setCharAt", "(IC)V", (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int i = t.I(b + 1); if (i < 0 || i >= sb.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", i.ToString()); sb[i] = (char)t.I(b + 2); })
                    .M("setLength", "(I)V", (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int l = t.I(b + 1); if (l < 0) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); if (l < sb.Length) sb.Length = l; else sb.Append('\0', l - sb.Length); })
                    .M("deleteCharAt", "(I)" + self, (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int i = t.I(b + 1); if (i < 0 || i >= sb.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", i.ToString()); sb.Remove(i, 1); })
                    .M("delete", "(II)" + self, (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int s = t.I(b + 1), e = Math.Min(t.I(b + 2), sb.Length); if (s < 0 || s > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); sb.Remove(s, e - s); })
                    .M("replace", "(IILjava/lang/String;)" + self, (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int s = t.I(b + 1), e = Math.Min(t.I(b + 2), sb.Length); if (s < 0 || s > sb.Length || s > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); sb.Remove(s, e - s).Insert(s, S(t, b + 3)); })
                    .M("reverse", "()" + self, (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; var a = sb.ToString().ToCharArray(); Array.Reverse(a); sb.Clear().Append(a); })
                    .M("indexOf", "(Ljava/lang/String;)I", (t, b) => t.RetI(b, ((StringBufferObj)t.R[b]).Sb.ToString().IndexOf(S(t, b + 1), StringComparison.Ordinal)))
                    .M("indexOf", "(Ljava/lang/String;I)I", (t, b) => { var s = ((StringBufferObj)t.R[b]).Sb.ToString(); t.RetI(b, StrIndexOf(s, S(t, b + 1), t.I(b + 2))); })
                    .M("lastIndexOf", "(Ljava/lang/String;)I", (t, b) => { var s = ((StringBufferObj)t.R[b]).Sb.ToString(); t.RetI(b, StrLastIndexOf(s, S(t, b + 1), s.Length)); })
                    .M("substring", "(I)Ljava/lang/String;", (t, b) => { var s = ((StringBufferObj)t.R[b]).Sb.ToString(); int i = t.I(b + 1); Rt.SIdx(i, s.Length); t.RetA(b, s.Substring(i)); })
                    .M("substring", "(II)Ljava/lang/String;", (t, b) => { var s = ((StringBufferObj)t.R[b]).Sb.ToString(); int i = t.I(b + 1), e = t.I(b + 2); if (i < 0 || e > s.Length || i > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); t.RetA(b, s.Substring(i, e - i)); })
                    .M("getChars", "(II[CI)V", (t, b) => { var sb = ((StringBufferObj)t.R[b]).Sb; int s = t.I(b + 1), e = t.I(b + 2); if (s < 0 || e > sb.Length || s > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); sb.CopyTo(s, (char[])t.R[b + 3], t.I(b + 4), e - s); })
                    .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((StringBufferObj)t.R[b]).Sb.ToString())),
                    Rt.F<StringBufferObj>(), ifaces: new[] { "java/lang/CharSequence" }, access: 0x0011);
            }
            n.Interface("java/lang/CharSequence", c => c.Abstract("length", "()I").Abstract("charAt", "(I)C").Abstract("toString", "()Ljava/lang/String;"));
            n.Interface("java/lang/Runnable", c => c.Abstract("run", "()V"));
            n.Interface("java/lang/Cloneable");
            n.Interface("java/lang/Comparable", c => c.Abstract("compareTo", "(Ljava/lang/Object;)I"));

            n.Define("java/lang/Math", "java/lang/Object", c => c
                .S("abs", "(I)I", (t, b) => { int v = t.I(b); t.RetI(b, v < 0 ? -v : v); })
                .S("abs", "(J)J", (t, b) => { long v = t.J(b); t.RetJ(b, v < 0 ? -v : v); })
                .S("abs", "(F)F", (t, b) => t.RetF(b, Math.Abs(t.F(b))))
                .S("abs", "(D)D", (t, b) => t.RetD(b, Math.Abs(t.D(b))))
                .S("max", "(II)I", (t, b) => t.RetI(b, Math.Max(t.I(b), t.I(b + 1))))
                .S("max", "(JJ)J", (t, b) => t.RetJ(b, Math.Max(t.J(b), t.J(b + 2))))
                .S("max", "(FF)F", (t, b) => t.RetF(b, Math.Max(t.F(b), t.F(b + 1))))
                .S("max", "(DD)D", (t, b) => t.RetD(b, Math.Max(t.D(b), t.D(b + 2))))
                .S("min", "(II)I", (t, b) => t.RetI(b, Math.Min(t.I(b), t.I(b + 1))))
                .S("min", "(JJ)J", (t, b) => t.RetJ(b, Math.Min(t.J(b), t.J(b + 2))))
                .S("min", "(FF)F", (t, b) => t.RetF(b, Math.Min(t.F(b), t.F(b + 1))))
                .S("min", "(DD)D", (t, b) => t.RetD(b, Math.Min(t.D(b), t.D(b + 2))))
                .S("sin", "(D)D", (t, b) => t.RetD(b, Math.Sin(t.D(b))))
                .S("cos", "(D)D", (t, b) => t.RetD(b, Math.Cos(t.D(b))))
                .S("tan", "(D)D", (t, b) => t.RetD(b, Math.Tan(t.D(b))))
                .S("asin", "(D)D", (t, b) => t.RetD(b, Math.Asin(t.D(b))))
                .S("acos", "(D)D", (t, b) => t.RetD(b, Math.Acos(t.D(b))))
                .S("atan", "(D)D", (t, b) => t.RetD(b, Math.Atan(t.D(b))))
                .S("atan2", "(DD)D", (t, b) => t.RetD(b, Math.Atan2(t.D(b), t.D(b + 2))))
                .S("sqrt", "(D)D", (t, b) => t.RetD(b, Math.Sqrt(t.D(b))))
                .S("exp", "(D)D", (t, b) => t.RetD(b, Math.Exp(t.D(b))))
                .S("log", "(D)D", (t, b) => t.RetD(b, Math.Log(t.D(b))))
                .S("log10", "(D)D", (t, b) => t.RetD(b, Math.Log10(t.D(b))))
                .S("pow", "(DD)D", (t, b) => t.RetD(b, Math.Pow(t.D(b), t.D(b + 2))))
                .S("ceil", "(D)D", (t, b) => t.RetD(b, Math.Ceiling(t.D(b))))
                .S("floor", "(D)D", (t, b) => t.RetD(b, Math.Floor(t.D(b))))
                .S("rint", "(D)D", (t, b) => t.RetD(b, Math.Round(t.D(b), MidpointRounding.ToEven)))
                .S("round", "(F)I", (t, b) => { float v = t.F(b); t.RetI(b, float.IsNaN(v) ? 0 : (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Floor(v + 0.5)))); })
                .S("round", "(D)J", (t, b) => { double v = t.D(b); t.RetJ(b, double.IsNaN(v) ? 0 : (long)Math.Max(long.MinValue, Math.Min(long.MaxValue, Math.Floor(v + 0.5)))); })
                .S("toRadians", "(D)D", (t, b) => t.RetD(b, t.D(b) * Math.PI / 180))
                .S("toDegrees", "(D)D", (t, b) => t.RetD(b, t.D(b) * 180 / Math.PI))
                .S("signum", "(F)F", (t, b) => t.RetF(b, Math.Sign(t.F(b))))
                .S("signum", "(D)D", (t, b) => t.RetD(b, Math.Sign(t.D(b))))
                .S("random", "()D", (t, b) => { lock (rnd) t.RetD(b, rnd.NextDouble()); }), access: 0x0011);

            n.Define("java/lang/System", "java/lang/Object", c => c
                .Static("out", "Ljava/io/PrintStream;", t => MakeLogStream(t, "out"))
                .Static("err", "Ljava/io/PrintStream;", t => MakeLogStream(t, "err"))
                .S("currentTimeMillis", "()J", (t, b) => t.RetJ(b, Rt.Now()))
                .S("nanoTime", "()J", (t, b) => t.RetJ(b, Rt.Mono() * 1000000L))
                .S("arraycopy", "(Ljava/lang/Object;ILjava/lang/Object;II)V", (t, b) => ArrayCopy(t, t.R[b], t.I(b + 1), t.R[b + 2], t.I(b + 3), t.I(b + 4)))
                .S("gc", "()V", (t, b) => { })
                .S("exit", "(I)V", (t, b) => Exit(t))
                .S("identityHashCode", "(Ljava/lang/Object;)I", (t, b) => t.RetI(b, t.R[b] == null ? 0 : t.Vm.IdentityHash(t.R[b])))
                .S("getProperty", "(Ljava/lang/String;)Ljava/lang/String;", (t, b) => { string v; t.Vm.Properties.TryGetValue(S(t, b) ?? "", out v); t.RetA(b, v); })
                .S("getProperty", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", (t, b) => { string v; if (!t.Vm.Properties.TryGetValue(S(t, b) ?? "", out v)) v = S(t, b + 1); t.RetA(b, v); }), access: 0x0011);

            n.Define("java/lang/Runtime", "java/lang/Object", c => c
                .S("getRuntime", "()Ljava/lang/Runtime;", (t, b) =>
                {
                    lock (runtimeLock) { if (runtime == null || runtime.Class.Vm != t.Vm) runtime = new JObject(t.Vm.LoadClass("java/lang/Runtime")); }
                    t.RetA(b, runtime);
                })
                .M("freeMemory", "()J", (t, b) => t.RetJ(b, Math.Max(512 * 1024, 8L * 1024 * 1024 - GC.GetTotalMemory(false) % (4L * 1024 * 1024))))
                .M("totalMemory", "()J", (t, b) => t.RetJ(b, 8L * 1024 * 1024))
                .M("maxMemory", "()J", (t, b) => t.RetJ(b, 8L * 1024 * 1024))
                .M("gc", "()V", (t, b) => { })
                .M("exit", "(I)V", (t, b) => Exit(t)));

            n.Define("java/lang/Thread", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => ThreadInit(t, b, null, null))
                .M("<init>", "(Ljava/lang/Runnable;)V", (t, b) => ThreadInit(t, b, t.R[b + 1], null))
                .M("<init>", "(Ljava/lang/Runnable;Ljava/lang/String;)V", (t, b) => ThreadInit(t, b, t.R[b + 1], S(t, b + 2)))
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => ThreadInit(t, b, null, S(t, b + 1)))
                .M("run", "()V", (t, b) => { var tg = ((ThreadObj)t.R[b]).Target; if (tg != null) t.CallVirtual(tg, "run()V"); })
                .M("start", "()V", (t, b) => StartThread(t, (ThreadObj)t.R[b]))
                .M("isAlive", "()Z", (t, b) => t.RetZ(b, ((ThreadObj)t.R[b]).Alive))
                .M("join", "()V", (t, b) => Join(t, (ThreadObj)t.R[b], 0))
                .M("join", "(J)V", (t, b) => Join(t, (ThreadObj)t.R[b], t.J(b + 1)))
                .M("interrupt", "()V", (t, b) => { var th = (ThreadObj)t.R[b]; if (th.Vt != null) { th.Vt.Interrupted = true; th.Vt.Wake.Set(); } })
                .M("isInterrupted", "()Z", (t, b) => { var th = (ThreadObj)t.R[b]; t.RetZ(b, th.Vt != null && th.Vt.Interrupted); })
                .M("setPriority", "(I)V", (t, b) => { int p = t.I(b + 1); if (p < 1 || p > 10) throw new JavaThrow("java/lang/IllegalArgumentException"); ((ThreadObj)t.R[b]).Priority = p; })
                .M("getPriority", "()I", (t, b) => t.RetI(b, ((ThreadObj)t.R[b]).Priority))
                .M("getName", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((ThreadObj)t.R[b]).Name))
                .M("setName", "(Ljava/lang/String;)V", (t, b) => ((ThreadObj)t.R[b]).Name = S(t, b + 1))
                .M("setDaemon", "(Z)V", (t, b) => { })
                .M("toString", "()Ljava/lang/String;", (t, b) => { var th = (ThreadObj)t.R[b]; t.RetA(b, "Thread[" + th.Name + "," + th.Priority + "]"); })
                .S("currentThread", "()Ljava/lang/Thread;", (t, b) => t.RetA(b, CurrentThread(t)))
                .S("sleep", "(J)V", (t, b) => { if (t.J(b) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); Rt.Sleep(t, t.J(b)); })
                .S("sleep", "(JI)V", (t, b) => Rt.Sleep(t, t.J(b)))
                .S("yield", "()V", (t, b) => Rt.Yield(t))
                .S("interrupted", "()Z", (t, b) => { bool v = t.Interrupted; t.Interrupted = false; t.RetZ(b, v); })
                .S("activeCount", "()I", (t, b) => { lock (t.Vm.Threads) t.RetI(b, t.Vm.Threads.Count); }),
                Rt.F<ThreadObj>(), ifaces: new[] { "java/lang/Runnable" });

            n.Define("java/lang/Class", "java/lang/Object", c => c
                .S("forName", "(Ljava/lang/String;)Ljava/lang/Class;", (t, b) =>
                {
                    var name = S(t, b);
                    if (name == null) throw new NullReferenceException();
                    var k = t.Vm.TryLoadClass(name.Replace('.', '/'));
                    if (k == null || name.IndexOf('/') >= 0) throw new JavaThrow("java/lang/ClassNotFoundException", name);
                    t.Vm.EnsureInit(t, k);
                    t.RetA(b, t.Vm.ClassObject(k));
                })
                .M("newInstance", "()Ljava/lang/Object;", (t, b) =>
                {
                    var k = ((ClassObj)t.R[b]).Ref;
                    if (k.IsInterface || k.IsArray || (k.Access & 0x0400) != 0) throw new JavaThrow("java/lang/InstantiationException", k.JavaName);
                    var init = k.Declared("<init>()V");
                    if (init == null) throw new JavaThrow("java/lang/InstantiationException", k.JavaName);
                    t.Vm.EnsureInit(t, k);
                    var o = t.Vm.NewObject(k);
                    t.Call(init, o);
                    t.RetA(b, o);
                })
                .M("getName", "()Ljava/lang/String;", (t, b) => { var k = ((ClassObj)t.R[b]).Ref; t.RetA(b, k.IsArray ? k.Name.Replace('/', '.') : k.JavaName); })
                .M("toString", "()Ljava/lang/String;", (t, b) => { var k = ((ClassObj)t.R[b]).Ref; t.RetA(b, (k.IsInterface ? "interface " : "class ") + k.JavaName); })
                .M("isInstance", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, t.Vm.InstanceOf(t.R[b + 1], ((ClassObj)t.R[b]).Ref)))
                .M("isAssignableFrom", "(Ljava/lang/Class;)Z", (t, b) => t.RetZ(b, ((ClassObj)t.R[b + 1]).Ref.IsSubclassOf(((ClassObj)t.R[b]).Ref)))
                .M("isInterface", "()Z", (t, b) => t.RetZ(b, ((ClassObj)t.R[b]).Ref.IsInterface))
                .M("isArray", "()Z", (t, b) => t.RetZ(b, ((ClassObj)t.R[b]).Ref.IsArray))
                .M("getResourceAsStream", "(Ljava/lang/String;)Ljava/io/InputStream;", (t, b) =>
                {
                    var name = S(t, b + 1);
                    if (name == null) throw new NullReferenceException();
                    var path = Rt.ClassToResource(((ClassObj)t.R[b]).Ref, name);
                    t.RetA(b, OpenResource(t, path));
                }), access: 0x0011);
        }

        static readonly Random rnd = new Random();
        static readonly object runtimeLock = new object();
        static JObject runtime;

        public static Action<JThread> OnExit;

        static void Exit(JThread t)
        {
            OnExit?.Invoke(t);
            throw new VmHalt();
        }

        public static JObject OpenResource(JThread t, string path)
        {
            while (path.StartsWith("/")) path = path.Substring(1);
            var data = t.Vm.ClassPath?.GetResource(path);
            if (data == null) return null;
            var c = t.Vm.LoadClass("java/io/ByteArrayInputStream");
            var o = (InStreamObj)t.Vm.NewObject(c);
            ((ByteArrayInStream)o).Set(Enc.ToS(data), 0, data.Length);
            return o;
        }

        static void Ins(JThread t, int b, string s)
        {
            var sb = ((StringBufferObj)t.R[b]).Sb;
            int i = t.I(b + 1);
            Rt.SIdx(i, sb.Length);
            sb.Insert(i, s);
        }

        public static object Clone(JThread t, object o)
        {
            var oa = o as ObjArray;
            if (oa != null) return new ObjArray(oa.Type, (object[])oa.Data.Clone());
            var arr = o as Array;
            if (arr != null) return arr.Clone();
            var jo = o as JObject;
            if (jo != null && jo.Class.IsSubclassOf(t.Vm.LoadClass("java/lang/Cloneable")))
            {
                var c = t.Vm.NewObject(jo.Class);
                if (jo.P != null) c.P = (Slot[])jo.P.Clone();
                if (jo.O != null) c.O = (object[])jo.O.Clone();
                return c;
            }
            throw new JavaThrow("java/lang/CloneNotSupportedException", t.Vm.ClassOf(o).JavaName);
        }

        public static void ArrayCopy(JThread t, object src, int sp, object dst, int dp, int len)
        {
            if (src == null || dst == null) throw new NullReferenceException();
            var so = src as ObjArray;
            if (so != null)
            {
                var d = dst as ObjArray;
                if (d == null) throw new JavaThrow("java/lang/ArrayStoreException");
                if (sp < 0 || dp < 0 || len < 0 || sp + len > so.Data.Length || dp + len > d.Data.Length)
                    throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                Array.Copy(so.Data, sp, d.Data, dp, len);
                return;
            }
            var sa = src as Array;
            var da = dst as Array;
            if (sa == null || da == null || sa.GetType() != da.GetType()) throw new JavaThrow("java/lang/ArrayStoreException");
            if (sp < 0 || dp < 0 || len < 0 || sp + len > sa.Length || dp + len > da.Length)
                throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException", "arraycopy");
            Array.Copy(sa, sp, da, dp, len);
        }

        static int threadSeq;

        static void ThreadInit(JThread t, int b, object target, string name)
        {
            var th = (ThreadObj)t.R[b];
            th.Target = target;
            th.Name = name ?? "Thread-" + Interlocked.Increment(ref threadSeq);
        }

        public static JObject CurrentThread(JThread t)
        {
            if (t.Java == null)
            {
                var th = (ThreadObj)t.Vm.NewObject(t.Vm.LoadClass("java/lang/Thread"));
                th.Name = t.Name; th.Vt = t; th.Alive = th.Started = true;
                t.Java = th;
            }
            return t.Java;
        }

        public static void StartThread(JThread t, ThreadObj th)
        {
            if (th.Started) throw new JavaThrow("java/lang/IllegalThreadStateException");
            th.Started = true; th.Alive = true;
            var vm = t.Vm;
            var jt = new JThread(vm, th.Name) { Java = th };
            th.Vt = jt;
            lock (vm.Threads) vm.Threads.Add(jt);
            RunOnNewThread(jt, () => jt.CallVirtual(th, "run()V"), () =>
            {
                th.Alive = false;
                lock (th) Monitor.PulseAll(th);
            });
        }

        public static void RunOnNewThread(JThread jt, Action body, Action done = null)
        {
            var vm = jt.Vm;
            lock (vm.Threads) if (!vm.Threads.Contains(jt)) vm.Threads.Add(jt);
            Task.Factory.StartNew(() =>
            {
                JThread.Current = jt;
                try { if (!vm.Halted) body(); }
                catch (JavaException e) { ReportUncaught(jt, e.Obj); }
                catch (VmHalt) { }
                catch (Exception e) { vm.Log("thread " + jt.Name + " crashed: " + e); }
                finally
                {
                    try { done?.Invoke(); } catch (Exception) { }
                    lock (vm.Threads) vm.Threads.Remove(jt);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public static void ReportUncaught(JThread t, JObject ex)
        {
            var th = ex as ThrowableObj;
            t.Vm.Log("Uncaught exception in " + t.Name + ": " + (th != null ? th.Describe() : ex.Class.JavaName));
        }

        static void Join(JThread t, ThreadObj th, long ms)
        {
            long end = ms > 0 ? Rt.Mono() + ms : long.MaxValue;
            lock (th)
            {
                while (th.Alive)
                {
                    long left = end - Rt.Mono();
                    if (left <= 0) return;
                    Monitor.Wait(th, (int)Math.Min(left, 100));
                    if (t.Vm.Halted) throw new VmHalt();
                    if (t.Interrupted) { t.Interrupted = false; throw new JavaThrow("java/lang/InterruptedException"); }
                }
            }
        }

        static int StrIndexOf(string s, string p, int from)
        {
            if (p == null) throw new NullReferenceException();
            if (from < 0) from = 0;
            if (from > s.Length) return p.Length == 0 ? s.Length : -1;
            return s.IndexOf(p, from, StringComparison.Ordinal);
        }

        static int StrLastIndexOf(string s, string p, int from)
        {
            if (p == null) throw new NullReferenceException();
            int max = s.Length - p.Length;
            if (from > max) from = max;
            for (int i = from; i >= 0; i--)
                if (string.CompareOrdinal(s, i, p, 0, p.Length) == 0) return i;
            return -1;
        }

        static void StrInit(JThread t, int b, string v)
        {
            var ph = t.R[b];
            int lo = t.FrameCount > 0 ? t.Frames[t.FrameCount - 1].Lp : 0;
            for (int i = lo; i < b; i++) if (ReferenceEquals(t.R[i], ph)) t.R[i] = v;
        }

        static string Self(JThread t, int b)
        {
            var s = t.R[b] as string;
            if (s == null) throw new NullReferenceException();
            return s;
        }

        static void Str(ClassBuilder c)
        {
            c.M("<init>", "()V", (t, b) => StrInit(t, b, ""))
             .M("<init>", "(Ljava/lang/String;)V", (t, b) => StrInit(t, b, Self(t, b + 1)))
             .M("<init>", "([C)V", (t, b) => StrInit(t, b, new string((char[])t.R[b + 1])))
             .M("<init>", "([CII)V", (t, b) => { var a = (char[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3); if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); StrInit(t, b, new string(a, o, l)); })
             .M("<init>", "([B)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; StrInit(t, b, Enc.Decode(a, 0, a.Length, null)); })
             .M("<init>", "([BII)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3); if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); StrInit(t, b, Enc.Decode(a, o, l, null)); })
             .M("<init>", "([BLjava/lang/String;)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; StrInit(t, b, Enc.Decode(a, 0, a.Length, S(t, b + 2))); })
             .M("<init>", "([BIILjava/lang/String;)V", (t, b) => { var a = (sbyte[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3); if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); StrInit(t, b, Enc.Decode(a, o, l, S(t, b + 4))); })
             .M("<init>", "(Ljava/lang/StringBuffer;)V", (t, b) => StrInit(t, b, ((StringBufferObj)t.R[b + 1]).Sb.ToString()))
             .M("<init>", "(Ljava/lang/StringBuilder;)V", (t, b) => StrInit(t, b, ((StringBufferObj)t.R[b + 1]).Sb.ToString()))
             .M("length", "()I", (t, b) => t.RetI(b, Self(t, b).Length))
             .M("isEmpty", "()Z", (t, b) => t.RetZ(b, Self(t, b).Length == 0))
             .M("charAt", "(I)C", (t, b) => { var s = Self(t, b); int i = t.I(b + 1); if (i < 0 || i >= s.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", "index " + i); t.RetI(b, s[i]); })
             .M("getChars", "(II[CI)V", (t, b) => { var s = Self(t, b); int sb = t.I(b + 1), se = t.I(b + 2); if (sb < 0 || se > s.Length || sb > se) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); var d = (char[])t.R[b + 3]; int db = t.I(b + 4); if (db < 0 || db + se - sb > d.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException"); s.CopyTo(sb, d, db, se - sb); })
             .M("getBytes", "()[B", (t, b) => t.RetA(b, Enc.Encode(Self(t, b), null)))
             .M("getBytes", "(Ljava/lang/String;)[B", (t, b) => t.RetA(b, Enc.Encode(Self(t, b), S(t, b + 1))))
             .M("equals", "(Ljava/lang/Object;)Z", (t, b) => { var o = t.R[b + 1] as string; t.RetZ(b, o != null && o == Self(t, b)); })
             .M("equalsIgnoreCase", "(Ljava/lang/String;)Z", (t, b) => { var o = S(t, b + 1); t.RetZ(b, o != null && string.Equals(Self(t, b), o, StringComparison.OrdinalIgnoreCase)); })
             .M("compareTo", "(Ljava/lang/String;)I", (t, b) => t.RetI(b, Compare(Self(t, b), S(t, b + 1))))
             .M("compareTo", "(Ljava/lang/Object;)I", (t, b) => t.RetI(b, Compare(Self(t, b), (string)t.R[b + 1])))
             .M("compareToIgnoreCase", "(Ljava/lang/String;)I", (t, b) => t.RetI(b, Compare(Self(t, b).ToLowerInvariant(), S(t, b + 1).ToLowerInvariant())))
             .M("regionMatches", "(ZILjava/lang/String;II)Z", (t, b) => t.RetZ(b, Region(Self(t, b), t.Z(b + 1), t.I(b + 2), S(t, b + 3), t.I(b + 4), t.I(b + 5))))
             .M("regionMatches", "(ILjava/lang/String;II)Z", (t, b) => t.RetZ(b, Region(Self(t, b), false, t.I(b + 1), S(t, b + 2), t.I(b + 3), t.I(b + 4))))
             .M("startsWith", "(Ljava/lang/String;)Z", (t, b) => t.RetZ(b, Self(t, b).StartsWith(Self(t, b + 1), StringComparison.Ordinal)))
             .M("startsWith", "(Ljava/lang/String;I)Z", (t, b) => { var s = Self(t, b); var p = Self(t, b + 1); int o = t.I(b + 2); t.RetZ(b, o >= 0 && o + p.Length <= s.Length && string.CompareOrdinal(s, o, p, 0, p.Length) == 0); })
             .M("endsWith", "(Ljava/lang/String;)Z", (t, b) => t.RetZ(b, Self(t, b).EndsWith(Self(t, b + 1), StringComparison.Ordinal)))
             .M("hashCode", "()I", (t, b) => t.RetI(b, Rt.JavaHash(Self(t, b))))
             .M("indexOf", "(I)I", (t, b) => t.RetI(b, Self(t, b).IndexOf((char)t.I(b + 1))))
             .M("indexOf", "(II)I", (t, b) => { var s = Self(t, b); int f = Math.Max(0, t.I(b + 2)); t.RetI(b, f >= s.Length ? -1 : s.IndexOf((char)t.I(b + 1), f)); })
             .M("indexOf", "(Ljava/lang/String;)I", (t, b) => t.RetI(b, StrIndexOf(Self(t, b), S(t, b + 1), 0)))
             .M("indexOf", "(Ljava/lang/String;I)I", (t, b) => t.RetI(b, StrIndexOf(Self(t, b), S(t, b + 1), t.I(b + 2))))
             .M("lastIndexOf", "(I)I", (t, b) => t.RetI(b, Self(t, b).LastIndexOf((char)t.I(b + 1))))
             .M("lastIndexOf", "(II)I", (t, b) => { var s = Self(t, b); int f = Math.Min(t.I(b + 2), s.Length - 1); t.RetI(b, f < 0 ? -1 : s.LastIndexOf((char)t.I(b + 1), f)); })
             .M("lastIndexOf", "(Ljava/lang/String;)I", (t, b) => { var s = Self(t, b); t.RetI(b, StrLastIndexOf(s, S(t, b + 1), s.Length)); })
             .M("lastIndexOf", "(Ljava/lang/String;I)I", (t, b) => t.RetI(b, StrLastIndexOf(Self(t, b), S(t, b + 1), t.I(b + 2))))
             .M("substring", "(I)Ljava/lang/String;", (t, b) => { var s = Self(t, b); int i = t.I(b + 1); Rt.SIdx(i, s.Length); t.RetA(b, i == 0 ? s : s.Substring(i)); })
             .M("substring", "(II)Ljava/lang/String;", (t, b) => { var s = Self(t, b); int i = t.I(b + 1), e = t.I(b + 2); if (i < 0 || e > s.Length || i > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException", "begin " + i + ", end " + e + ", length " + s.Length); t.RetA(b, i == 0 && e == s.Length ? s : s.Substring(i, e - i)); })
             .M("subSequence", "(II)Ljava/lang/CharSequence;", (t, b) => { var s = Self(t, b); int i = t.I(b + 1), e = t.I(b + 2); if (i < 0 || e > s.Length || i > e) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); t.RetA(b, s.Substring(i, e - i)); })
             .M("concat", "(Ljava/lang/String;)Ljava/lang/String;", (t, b) => t.RetA(b, Self(t, b) + Self(t, b + 1)))
             .M("replace", "(CC)Ljava/lang/String;", (t, b) => t.RetA(b, Self(t, b).Replace((char)t.I(b + 1), (char)t.I(b + 2))))
             .M("replace", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Ljava/lang/String;", (t, b) => { var f = Rt.Str(t, t.R[b + 1]); t.RetA(b, f.Length == 0 ? Self(t, b) : Self(t, b).Replace(f, Rt.Str(t, t.R[b + 2]))); })
             .M("contains", "(Ljava/lang/CharSequence;)Z", (t, b) => t.RetZ(b, Self(t, b).IndexOf(Rt.Str(t, t.R[b + 1]), StringComparison.Ordinal) >= 0))
             .M("toLowerCase", "()Ljava/lang/String;", (t, b) => t.RetA(b, Self(t, b).ToLowerInvariant()))
             .M("toUpperCase", "()Ljava/lang/String;", (t, b) => t.RetA(b, Self(t, b).ToUpperInvariant()))
             .M("trim", "()Ljava/lang/String;", (t, b) => { var s = Self(t, b); int i = 0, e = s.Length; while (i < e && s[i] <= ' ') i++; while (e > i && s[e - 1] <= ' ') e--; t.RetA(b, i == 0 && e == s.Length ? s : s.Substring(i, e - i)); })
             .M("toString", "()Ljava/lang/String;", (t, b) => t.RetA(b, Self(t, b)))
             .M("toCharArray", "()[C", (t, b) => t.RetA(b, Self(t, b).ToCharArray()))
             .M("intern", "()Ljava/lang/String;", (t, b) => t.RetA(b, t.Vm.Intern(Self(t, b))))
             .S("valueOf", "(Ljava/lang/Object;)Ljava/lang/String;", (t, b) => t.RetA(b, Rt.Str(t, t.R[b])))
             .S("valueOf", "([C)Ljava/lang/String;", (t, b) => t.RetA(b, new string((char[])t.R[b])))
             .S("valueOf", "([CII)Ljava/lang/String;", (t, b) => t.RetA(b, new string((char[])t.R[b], t.I(b + 1), t.I(b + 2))))
             .S("copyValueOf", "([C)Ljava/lang/String;", (t, b) => t.RetA(b, new string((char[])t.R[b])))
             .S("copyValueOf", "([CII)Ljava/lang/String;", (t, b) => t.RetA(b, new string((char[])t.R[b], t.I(b + 1), t.I(b + 2))))
             .S("valueOf", "(Z)Ljava/lang/String;", (t, b) => t.RetA(b, t.Z(b) ? "true" : "false"))
             .S("valueOf", "(C)Ljava/lang/String;", (t, b) => t.RetA(b, ((char)t.I(b)).ToString()))
             .S("valueOf", "(I)Ljava/lang/String;", (t, b) => t.RetA(b, t.I(b).ToString(CultureInfo.InvariantCulture)))
             .S("valueOf", "(J)Ljava/lang/String;", (t, b) => t.RetA(b, t.J(b).ToString(CultureInfo.InvariantCulture)))
             .S("valueOf", "(F)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Float(t.F(b))))
             .S("valueOf", "(D)Ljava/lang/String;", (t, b) => t.RetA(b, Jf.Double(t.D(b))));
        }

        static int Compare(string a, string b)
        {
            if (b == null) throw new NullReferenceException();
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i] - b[i];
            return a.Length - b.Length;
        }

        static bool Region(string s, bool ic, int to, string o, int oo, int len)
        {
            if (o == null) throw new NullReferenceException();
            if (to < 0 || oo < 0 || to > s.Length - len || oo > o.Length - len) return false;
            if (len <= 0) return true;
            return string.Compare(s, to, o, oo, len, ic ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0;
        }
    }
}
