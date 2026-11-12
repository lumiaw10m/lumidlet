using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Lumidlet.Vm
{
    public interface IClassPath
    {
        byte[] GetClass(string name);
        byte[] GetResource(string path);
    }

    public sealed class ClassBuilder
    {
        public readonly JClass C;
        readonly List<KeyValuePair<JField, Func<JThread, object>>> staticInits = new List<KeyValuePair<JField, Func<JThread, object>>>();
        int sp, so;

        public ClassBuilder(JClass c) { C = c; }

        public ClassBuilder M(string name, string desc, NativeFn fn)
        {
            var m = new JMethod(C, name, desc, 0x0001) { Native = fn };
            C.Methods[m.Key] = m;
            return this;
        }

        public ClassBuilder S(string name, string desc, NativeFn fn)
        {
            var m = new JMethod(C, name, desc, 0x0009) { Native = fn };
            C.Methods[m.Key] = m;
            return this;
        }

        public ClassBuilder Abstract(string name, string desc)
        {
            var m = new JMethod(C, name, desc, 0x0401);
            C.Methods[m.Key] = m;
            return this;
        }

        public ClassBuilder Static(string name, string desc, Func<JThread, object> init)
        {
            var f = new JField { Owner = C, Name = name, Desc = desc, Access = 0x0019, IsStatic = true };
            f.IsRef = desc[0] == 'L' || desc[0] == '[';
            f.Wide = desc[0] == 'J' || desc[0] == 'D';
            f.Slot = f.IsRef ? so++ : sp++;
            C.Fields[name + ":" + desc] = f;
            staticInits.Add(new KeyValuePair<JField, Func<JThread, object>>(f, init));
            return this;
        }

        public ClassBuilder Instance(string name, string desc)
        {
            var f = new JField { Owner = C, Name = name, Desc = desc, Access = 0x0001 };
            f.IsRef = desc[0] == 'L' || desc[0] == '[';
            f.Wide = desc[0] == 'J' || desc[0] == 'D';
            f.Slot = f.IsRef ? C.InstO++ : C.InstP++;
            C.Fields[name + ":" + desc] = f;
            return this;
        }

        public ClassBuilder Clinit(Action<JThread> a)
        {
            var prev = C.NativeClinit;
            C.NativeClinit = t => { prev?.Invoke(t); a(t); };
            return this;
        }

        internal void Finish()
        {
            C.StaticP = new Slot[sp];
            C.StaticO = new object[so];
            if (staticInits.Count > 0)
            {
                var inits = staticInits.ToArray();
                var prev = C.NativeClinit;
                C.NativeClinit = t =>
                {
                    foreach (var kv in inits)
                    {
                        var v = kv.Value(t);
                        if (kv.Key.IsRef) C.StaticO[kv.Key.Slot] = v;
                        else if (v is int) C.StaticP[kv.Key.Slot].I = (int)v;
                        else if (v is long) C.StaticP[kv.Key.Slot].J = (long)v;
                    }
                    prev?.Invoke(t);
                };
            }
        }
    }

    public sealed class NativeRegistry
    {
        sealed class Def
        {
            public string Super;
            public string[] Ifaces;
            public Func<JClass, JObject> Factory;
            public Action<ClassBuilder> Body;
            public int Access;
        }

        readonly Dictionary<string, Def> defs = new Dictionary<string, Def>();

        public void Define(string name, string super, Action<ClassBuilder> body, Func<JClass, JObject> factory = null, string[] ifaces = null, int access = 0x0001)
        {
            defs[name] = new Def { Super = super, Ifaces = ifaces, Factory = factory, Body = body, Access = access };
        }

        public void Interface(string name, Action<ClassBuilder> body = null, string[] ifaces = null)
        {
            defs[name] = new Def { Super = "java/lang/Object", Ifaces = ifaces, Body = body, Access = 0x0601 };
        }

        public bool Has(string name) => defs.ContainsKey(name);

        internal JClass Build(JVm vm, string name)
        {
            Def d;
            if (!defs.TryGetValue(name, out d)) return null;
            var c = new JClass { Name = name, Vm = vm, IsNative = true, Access = d.Access };
            c.IsInterface = (d.Access & 0x0200) != 0;
            vm.Register(c);
            if (d.Super != null && name != "java/lang/Object") c.Super = vm.LoadClass(d.Super);
            if (d.Ifaces != null)
            {
                c.Interfaces = new JClass[d.Ifaces.Length];
                for (int i = 0; i < d.Ifaces.Length; i++) c.Interfaces[i] = vm.LoadClass(d.Ifaces[i]);
            }
            if (c.Super != null) { c.InstP = c.Super.InstP; c.InstO = c.Super.InstO; c.Factory = c.Super.Factory; }
            if (d.Factory != null) c.Factory = d.Factory;
            var b = new ClassBuilder(c);
            d.Body?.Invoke(b);
            b.Finish();
            return c;
        }

        public NativeFn FindUnbound(string cls, string key)
        {
            return (t, b) => { throw new JavaThrow("java/lang/UnsatisfiedLinkError", cls + "." + key); };
        }
    }

    public sealed class JVm
    {
        public readonly NativeRegistry Natives = new NativeRegistry();
        readonly Dictionary<string, JClass> classes = new Dictionary<string, JClass>();
        readonly Dictionary<string, string> interned = new Dictionary<string, string>();
        readonly object loadLock = new object();
        public IClassPath ClassPath;
        public volatile bool Halted;
        public bool TraceExceptions;
        public readonly List<JThread> Threads = new List<JThread>();
        public Action<string> Log = s => { };
        public object Runtime;
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();

        public JClass ObjectClass, StringClass, ClassClass;
        JClass[] primArrays = new JClass[128];

        public JVm(IClassPath cp)
        {
            ClassPath = cp;
        }

        public void Boot()
        {
            ObjectClass = LoadClass("java/lang/Object");
            StringClass = LoadClass("java/lang/String");
            ClassClass = LoadClass("java/lang/Class");
            foreach (var k in "ZBCSIJFD") primArrays[k] = LoadClass("[" + k);
        }

        internal void Register(JClass c)
        {
            classes[c.Name] = c;
        }

        public string Intern(string s)
        {
            if (s == null) return null;
            lock (interned)
            {
                string v;
                if (interned.TryGetValue(s, out v)) return v;
                interned[s] = s;
                return s;
            }
        }

        public JClass TryLoadClass(string name)
        {
            try { return LoadClass(name); }
            catch (JavaThrow) { return null; }
        }

        public JClass LoadClass(string name)
        {
            lock (loadLock)
            {
                JClass c;
                if (classes.TryGetValue(name, out c)) return c;
                if (name[0] == '[') return MakeArray(name);
                c = Natives.Build(this, name);
                if (c != null) return c;
                if (name.StartsWith("java/", StringComparison.Ordinal) || name.StartsWith("javax/microedition/", StringComparison.Ordinal))
                    throw new JavaThrow("java/lang/NoClassDefFoundError", name.Replace('/', '.'));
                byte[] data = ClassPath?.GetClass(name);
                if (data == null) throw new JavaThrow("java/lang/NoClassDefFoundError", name.Replace('/', '.'));
                c = ClassFile.Parse(this, data, name);
                if (c.Name != name) throw new JavaThrow("java/lang/NoClassDefFoundError", name.Replace('/', '.') + " (wrong name: " + c.Name + ")");
                classes[name] = c;
                return c;
            }
        }

        JClass MakeArray(string name)
        {
            var c = new JClass { Name = name, Vm = this, IsArray = true, Access = 0x0011, Super = ObjectClass ?? LoadClass("java/lang/Object") };
            c.StaticP = new Slot[0]; c.StaticO = new object[0]; c.Init = InitState.Done;
            c.ElemKind = name[1];
            if (name.Length == 2 && "ZBCSIJFD".IndexOf(name[1]) >= 0) c.Component = Prim(name[1]);
            else
            {
                string comp = name.Substring(1);
                if (comp[0] == 'L') comp = comp.Substring(1, comp.Length - 2);
                c.Component = LoadClass(comp);
            }
            c.Interfaces = new JClass[0];
            classes[name] = c;
            return c;
        }

        readonly Dictionary<char, JClass> prims = new Dictionary<char, JClass>();

        public JClass Prim(char kind)
        {
            lock (prims)
            {
                JClass c;
                if (!prims.TryGetValue(kind, out c))
                {
                    c = new JClass { Name = kind.ToString(), Vm = this, IsPrim = true, Access = 0x0011, StaticP = new Slot[0], StaticO = new object[0], Init = InitState.Done };
                    prims[kind] = c;
                }
                return c;
            }
        }

        public JClass ArrayOf(JClass elem)
        {
            if (elem.IsArray || elem.IsPrimitive) return LoadClass("[" + elem.Name);
            return LoadClass("[L" + elem.Name + ";");
        }

        public JClass ClassOf(object o)
        {
            var j = o as JObject;
            if (j != null) return j.Class;
            if (o is string) return StringClass;
            var oa = o as ObjArray;
            if (oa != null) return oa.Type;
            if (o is int[]) return primArrays['I'];
            if (o is sbyte[]) return primArrays['B'];
            if (o is char[]) return primArrays['C'];
            if (o is short[]) return primArrays['S'];
            if (o is long[]) return primArrays['J'];
            if (o is float[]) return primArrays['F'];
            if (o is double[]) return primArrays['D'];
            if (o is bool[]) return primArrays['Z'];
            return ObjectClass;
        }

        public bool InstanceOf(object o, JClass c)
        {
            if (o == null) return false;
            if (c == ObjectClass) return true;
            if (o is sbyte[] && c == primArrays['Z']) return true;
            return ClassOf(o).IsSubclassOf(c);
        }

        public JObject NewObject(JClass c)
        {
            if (c.Factory != null) return c.Factory(c);
            return new JObject(c);
        }

        public static object NewPrimArray(char kind, int n)
        {
            if (n < 0) throw new JavaThrow("java/lang/NegativeArraySizeException", n.ToString());
            switch (kind)
            {
                case 'Z': case 'B': return new sbyte[n];
                case 'C': return new char[n];
                case 'S': return new short[n];
                case 'I': return new int[n];
                case 'J': return new long[n];
                case 'F': return new float[n];
                case 'D': return new double[n];
            }
            throw new InvalidOperationException();
        }

        public object NewArray(JClass arrType, int n)
        {
            if (n < 0) throw new JavaThrow("java/lang/NegativeArraySizeException", n.ToString());
            if (arrType.Component.IsPrimitive) return NewPrimArray(arrType.ElemKind, n);
            return new ObjArray(arrType, n);
        }

        public object NewMultiArray(JClass type, int[] dims, int d)
        {
            int n = dims[d];
            var a = NewArray(type, n);
            if (d + 1 < dims.Length)
            {
                var oa = (ObjArray)a;
                for (int i = 0; i < n; i++) oa.Data[i] = NewMultiArray(type.Component, dims, d + 1);
            }
            return a;
        }

        public static int ArrayLength(object a)
        {
            var oa = a as ObjArray;
            if (oa != null) return oa.Data.Length;
            return ((Array)a).Length;
        }

        public JObject ClassObject(JClass c)
        {
            if (c.ClassObject == null)
            {
                lock (c)
                {
                    if (c.ClassObject == null)
                    {
                        var o = new Lib.ClassObj { Ref = c };
                        o.Init(ClassClass);
                        c.ClassObject = o;
                    }
                }
            }
            return c.ClassObject;
        }

        public void EnsureInit(JThread t, JClass c)
        {
            if (c.Init == InitState.Done) return;
            lock (c)
            {
                if (c.Init == InitState.Done) return;
                if (c.Init == InitState.Running && c.InitThread == t) return;
                while (c.Init == InitState.Running) Monitor.Wait(c, 100);
                if (c.Init == InitState.Done) return;
                if (c.Init == InitState.Failed) throw new JavaThrow("java/lang/NoClassDefFoundError", c.JavaName);
                c.Init = InitState.Running;
                c.InitThread = t;
            }
            bool ok = false;
            try
            {
                if (c.Super != null) EnsureInit(t, c.Super);
                c.NativeClinit?.Invoke(t);
                JMethod cl;
                if (!c.IsNative && c.Methods.TryGetValue("<clinit>()V", out cl)) t.Invoke(cl);
                ok = true;
            }
            finally
            {
                lock (c)
                {
                    c.Init = ok ? InitState.Done : InitState.Failed;
                    c.InitThread = null;
                    Monitor.PulseAll(c);
                }
            }
        }

        public JObject MakeThrowable(JThread t, string cls, string msg)
        {
            JClass c;
            try { c = LoadClass(cls); }
            catch (JavaThrow) { c = LoadClass("java/lang/RuntimeException"); msg = cls + ": " + msg; }
            if (t != null) EnsureInit(t, c);
            var o = NewObject(c);
            var th = o as Lib.ThrowableObj;
            if (th != null)
            {
                th.Message = msg;
                if (t != null) th.Trace = t.CaptureTrace();
            }
            return o;
        }

        public JObject ToJava(JThread t, Exception e)
        {
            var je = e as JavaException;
            if (je != null) return je.Obj;
            var jt = e as JavaThrow;
            if (jt != null) return MakeThrowable(t, jt.ClassName, jt.Msg);
            if (TraceExceptions && !(e is NullReferenceException) && !(e is IndexOutOfRangeException)) Log("native exception: " + e);
            if (e is NullReferenceException) return MakeThrowable(t, "java/lang/NullPointerException", null);
            if (e is IndexOutOfRangeException) return MakeThrowable(t, "java/lang/ArrayIndexOutOfBoundsException", null);
            if (e is ArgumentOutOfRangeException) return MakeThrowable(t, "java/lang/IndexOutOfBoundsException", null);
            if (e is DivideByZeroException) return MakeThrowable(t, "java/lang/ArithmeticException", "/ by zero");
            if (e is InvalidCastException) return MakeThrowable(t, "java/lang/ClassCastException", null);
            if (e is ArrayTypeMismatchException) return MakeThrowable(t, "java/lang/ArrayStoreException", null);
            if (e is OutOfMemoryException) return MakeThrowable(t, "java/lang/OutOfMemoryError", null);
            if (e is SynchronizationLockException) return MakeThrowable(t, "java/lang/IllegalMonitorStateException", null);
            if (e is System.IO.IOException) return MakeThrowable(t, "java/io/IOException", e.Message);
            if (e is FormatException || e is OverflowException) return MakeThrowable(t, "java/lang/NumberFormatException", e.Message);
            if (e is ArgumentException) return MakeThrowable(t, "java/lang/IllegalArgumentException", e.Message);
            if (e is InvalidOperationException) return MakeThrowable(t, "java/lang/IllegalStateException", e.Message);
            Log("internal: " + e);
            return MakeThrowable(t, "java/lang/RuntimeException", e.GetType().Name + ": " + e.Message);
        }

        public int IdentityHash(object o) => RuntimeHelpers.GetHashCode(o);
    }
}
