using System;
using System.Threading;

namespace Lumidlet.Vm
{
    public sealed class Frame
    {
        public JMethod M;
        public int Lp, Sp, Pc, OpPc;
        public object Lock;
    }

    public sealed partial class JThread
    {
        [ThreadStatic] public static JThread Current;

        public readonly JVm Vm;
        public Slot[] S;
        public object[] R;
        public int Top;
        public Frame[] Frames = new Frame[64];
        public int FrameCount;
        public JObject Java;
        public volatile bool Interrupted;
        public readonly ManualResetEventSlim Wake = new ManualResetEventSlim(false);
        public string Name;

        const int MaxFrames = 6000;

        public JThread(JVm vm, string name)
        {
            Vm = vm; Name = name;
            S = new Slot[4096];
            R = new object[4096];
            lock (All) All.Add(new WeakReference<JThread>(this));
        }

        public static readonly System.Collections.Generic.List<WeakReference<JThread>> All = new System.Collections.Generic.List<WeakReference<JThread>>();

        public static string DumpAll()
        {
            var sb = new System.Text.StringBuilder();
            lock (All)
                foreach (var w in All)
                {
                    JThread t;
                    if (!w.TryGetTarget(out t) || t.FrameCount == 0) continue;
                    sb.Append("thread ").Append(t.Name).AppendLine();
                    try
                    {
                        for (int i = t.FrameCount - 1; i >= 0 && i >= t.FrameCount - 12; i--)
                        { var f = t.Frames[i]; sb.Append("    at ").Append(f.M).Append(" pc ").Append(f.OpPc).AppendLine(); }
                    }
                    catch { }
                }
            return sb.ToString();
        }

        public void Ensure(int n)
        {
            if (n <= S.Length) return;
            int len = S.Length;
            while (len < n) len *= 2;
            if (len > (1 << 22)) throw new JavaThrow("java/lang/StackOverflowError");
            Array.Resize(ref S, len);
            Array.Resize(ref R, len);
        }

        public int I(int i) => S[i].I;
        public bool Z(int i) => S[i].I != 0;
        public long J(int i) => S[i].J;
        public float F(int i) => S[i].F;
        public double D(int i) => S[i].D;
        public object A(int i) => R[i];
        public string Str(int i) => (string)R[i];
        public T O<T>(int i) where T : class => (T)R[i];
        public void RetI(int b, int v) { S[b].I = v; }
        public void RetZ(int b, bool v) { S[b].I = v ? 1 : 0; }
        public void RetJ(int b, long v) { S[b].J = v; }
        public void RetF(int b, float v) { S[b].F = v; }
        public void RetD(int b, double v) { S[b].D = v; }
        public void RetA(int b, object v) { R[b] = v; }

        public JMethod[] CaptureTrace()
        {
            var a = new JMethod[FrameCount];
            for (int i = 0; i < FrameCount; i++) a[i] = Frames[FrameCount - 1 - i].M;
            return a;
        }

        public JMethod Virtual(object recv, string key)
        {
            if (recv == null) throw new NullReferenceException();
            var m = Vm.ClassOf(recv).FindVirtual(key);
            if (m == null) throw new JavaThrow("java/lang/AbstractMethodError", Vm.ClassOf(recv).JavaName + "." + key);
            return m;
        }

        int PlaceArgs(object[] args)
        {
            int b = Top;
            Ensure(b + args.Length * 2 + 16);
            int i = b;
            foreach (var a in args)
            {
                switch (a)
                {
                    case int x: S[i].I = x; R[i++] = null; break;
                    case bool x: S[i].I = x ? 1 : 0; R[i++] = null; break;
                    case char x: S[i].I = x; R[i++] = null; break;
                    case long x: S[i].J = x; R[i] = null; i += 2; break;
                    case float x: S[i].F = x; R[i++] = null; break;
                    case double x: S[i].D = x; R[i] = null; i += 2; break;
                    default: R[i++] = a; break;
                }
            }
            return b;
        }

        public void Call(JMethod m, params object[] args)
        {
            int b = PlaceArgs(args);
            Run(m, b);
        }

        public object CallRef(JMethod m, params object[] args)
        {
            int b = PlaceArgs(args);
            Run(m, b);
            var v = R[b];
            R[b] = null;
            return v;
        }

        public Slot CallPrim(JMethod m, params object[] args)
        {
            int b = PlaceArgs(args);
            Run(m, b);
            return S[b];
        }

        public void CallVirtual(object recv, string key, params object[] args)
        {
            var m = Virtual(recv, key);
            var all = new object[args.Length + 1];
            all[0] = recv;
            Array.Copy(args, 0, all, 1, args.Length);
            Call(m, all);
        }

        public object CallVirtualRef(object recv, string key, params object[] args)
        {
            var m = Virtual(recv, key);
            var all = new object[args.Length + 1];
            all[0] = recv;
            Array.Copy(args, 0, all, 1, args.Length);
            return CallRef(m, all);
        }

        public int CallVirtualInt(object recv, string key, params object[] args)
        {
            var m = Virtual(recv, key);
            var all = new object[args.Length + 1];
            all[0] = recv;
            Array.Copy(args, 0, all, 1, args.Length);
            return CallPrim(m, all).I;
        }

        public void Invoke(JMethod m) => Run(m, Top);

        void Run(JMethod m, int b)
        {
            int saved = Top;
            try
            {
                if (m.Native != null)
                {
                    Top = b + m.ArgSlots;
                    Ensure(Top + 16);
                    m.Native(this, b);
                    return;
                }
                if (m.Code == null) throw new JavaThrow("java/lang/AbstractMethodError", m.ToString());
                Top = b + m.ArgSlots;
                if (m.IsStatic) Vm.EnsureInit(this, m.Owner);
                Ensure(b + m.MaxLocals + m.MaxStack + 16);
                int depth = FrameCount;
                var f = PushFrame();
                f.M = m; f.Lp = b; f.Pc = 0; f.Sp = b + m.MaxLocals; f.Lock = null;
                if (m.IsSynchronized)
                {
                    f.Lock = m.IsStatic ? Vm.ClassObject(m.Owner) : R[b];
                    Monitor.Enter(f.Lock);
                }
                Execute(depth);
            }
            finally { Top = saved; }
        }

        Frame PushFrame()
        {
            if (FrameCount == Frames.Length)
            {
                if (FrameCount >= MaxFrames) throw new JavaThrow("java/lang/StackOverflowError");
                Array.Resize(ref Frames, Frames.Length * 2);
            }
            var f = Frames[FrameCount] ?? (Frames[FrameCount] = new Frame());
            FrameCount++;
            return f;
        }

        void PopFrame()
        {
            var f = Frames[--FrameCount];
            if (f.Lock != null)
            {
                try { Monitor.Exit(f.Lock); } catch (SynchronizationLockException) { }
                f.Lock = null;
            }
        }

        int FindHandler(JMethod m, int pc, JObject ex)
        {
            var hs = m.Handlers;
            if (hs == null) return -1;
            for (int i = 0; i < hs.Length; i++)
            {
                if (pc < hs[i].Start || pc >= hs[i].End) continue;
                if (hs[i].CatchIndex == 0) return hs[i].Handler;
                var cc = hs[i].CatchClass;
                if (cc == null)
                {
                    var name = ((CpClass)m.Owner.CP.Items[hs[i].CatchIndex]).Name;
                    cc = Vm.TryLoadClass(name);
                    if (cc == null) continue;
                    hs[i].CatchClass = cc;
                }
                if (ex.Class.IsSubclassOf(cc)) return hs[i].Handler;
            }
            return -1;
        }

        static int F2I(float v)
        {
            if (float.IsNaN(v)) return 0;
            if (v >= 2147483647f) return int.MaxValue;
            if (v <= -2147483648f) return int.MinValue;
            return (int)v;
        }

        static int D2I(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= 2147483647.0) return int.MaxValue;
            if (v <= -2147483648.0) return int.MinValue;
            return (int)v;
        }

        static long D2L(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= 9223372036854775807.0) return long.MaxValue;
            if (v <= -9223372036854775808.0) return long.MinValue;
            return (long)v;
        }

        static int S4(byte[] c, int p) => (c[p] << 24) | (c[p + 1] << 16) | (c[p + 2] << 8) | c[p + 3];

        JField LookupField(CpMember fr)
        {
            var c = Vm.LoadClass(fr.ClassName);
            var f = c.FindField(fr.Name, fr.Desc);
            if (f == null) throw new JavaThrow("java/lang/NoSuchFieldError", fr.ClassName + "." + fr.Name);
            return f;
        }

        JField ResolveField(CpMember fr)
        {
            var f = LookupField(fr);
            Volatile.Write(ref fr.Field, f);
            return f;
        }

        JField ResolveStatic(CpMember fr)
        {
            var f = LookupField(fr);
            Vm.EnsureInit(this, f.Owner);
            if (f.Owner.Init == InitState.Done) Volatile.Write(ref fr.SField, f);
            return f;
        }

        JClass ResolveNew(CpClass cc)
        {
            var c = cc.Resolved ?? ResolveClass(cc);
            if (c.Init != InitState.Done) Vm.EnsureInit(this, c);
            if (c.IsInterface || (c.Access & 0x0400) != 0) throw new JavaThrow("java/lang/InstantiationError", c.JavaName);
            if (c.Init == InitState.Done) Volatile.Write(ref cc.NewReady, c);
            return c;
        }

        MethodRes ResolveMethod(CpMember mr, int op)
        {
            var c = Vm.LoadClass(mr.ClassName);
            var m = c.FindMethod(mr.Key);
            if (m == null && c.IsInterface) m = Vm.ObjectClass.FindMethod(mr.Key);
            if (m == null && (op == 0xb9 || op == 0xb6) && (c.IsInterface || (c.Access & 0x0400) != 0))
                m = new JMethod(c, mr.Name, mr.Desc, 0x0401);
            if (m == null) throw new JavaThrow("java/lang/NoSuchMethodError", c.JavaName + "." + mr.Key);
            mr.Method = m;
            bool direct = op == 0xb6 && ((m.Access & 0x0012) != 0 || m.Owner.IsFinal) && !m.IsAbstract;
            var res = new MethodRes(m, direct);
            if (op == 0xb8)
            {
                Vm.EnsureInit(this, m.Owner);
                if (m.Owner.Init != InitState.Done) return res;
            }
            Volatile.Write(ref mr.Res, res);
            return res;
        }

        JClass ResolveClass(CpClass cc)
        {
            var c = cc.Resolved;
            if (c != null) return c;
            c = Vm.LoadClass(cc.Name);
            cc.Resolved = c;
            return c;
        }
    }
}
