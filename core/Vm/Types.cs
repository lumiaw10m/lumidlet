using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lumidlet.Vm
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Slot
    {
        [FieldOffset(0)] public int I;
        [FieldOffset(0)] public long J;
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public double D;
    }

    public delegate void NativeFn(JThread t, int b);

    public class JObject
    {
        public JClass Class;
        public Slot[] P;
        public object[] O;

        public JObject() { }
        public JObject(JClass c) { Init(c); }

        public void Init(JClass c)
        {
            Class = c;
            if (c.InstP > 0) P = new Slot[c.InstP];
            if (c.InstO > 0) O = new object[c.InstO];
        }
    }

    public sealed class ObjArray
    {
        public readonly JClass Type;
        public readonly object[] Data;
        public ObjArray(JClass type, int n) { Type = type; Data = new object[n]; }
        public ObjArray(JClass type, object[] data) { Type = type; Data = data; }
    }

    public sealed class JField
    {
        public JClass Owner;
        public string Name, Desc;
        public int Access;
        public bool IsStatic, IsRef, Wide;
        public int Slot;
        public object ConstValue;
        public Func<object> StaticInit;
    }

    public struct ExHandler
    {
        public int Start, End, Handler, CatchIndex;
        public JClass CatchClass;
    }

    public sealed class JMethod
    {
        public JClass Owner;
        public string Name, Desc, Key;
        public int Access;
        public byte[] Code;
        public int MaxStack, MaxLocals;
        public ExHandler[] Handlers;
        public NativeFn Native;
        public int ArgSlots, RetSlots;
        public char RetType;
        public bool IsStatic, IsSynchronized, IsAbstract;

        public JMethod(JClass owner, string name, string desc, int access)
        {
            Owner = owner; Name = name; Desc = desc; Key = name + desc; Access = access;
            IsStatic = (access & 0x0008) != 0;
            IsSynchronized = (access & 0x0020) != 0;
            IsAbstract = (access & 0x0400) != 0;
            ArgSlots = Descriptors.ArgSlots(desc) + (IsStatic ? 0 : 1);
            RetType = desc[desc.IndexOf(')') + 1];
            RetSlots = RetType == 'V' ? 0 : (RetType == 'J' || RetType == 'D') ? 2 : 1;
        }

        public override string ToString() => Owner.Name.Replace('/', '.') + "." + Name;
    }

    public enum InitState { None, Running, Done, Failed }

    public sealed class JClass
    {
        public string Name;
        public JClass Super;
        public JClass[] Interfaces = new JClass[0];
        public int Access;
        public bool IsInterface, IsArray, IsNative;
        public JClass Component;
        public char ElemKind;
        public readonly Dictionary<string, JMethod> Methods = new Dictionary<string, JMethod>();
        public readonly Dictionary<string, JField> Fields = new Dictionary<string, JField>();
        readonly Dictionary<string, JMethod> vcache = new Dictionary<string, JMethod>();
        HashSet<JClass> supers;
        public int InstP, InstO;
        public Slot[] StaticP;
        public object[] StaticO;
        public ConstantPool CP;
        public volatile InitState Init;
        public object InitThread;
        public Func<JClass, JObject> Factory;
        public Action<JThread> NativeClinit;
        public JObject ClassObject;
        public JVm Vm;
        public string SourceName;

        public bool IsFinal => (Access & 0x0010) != 0;

        public override string ToString() => Name;

        public string JavaName => Name.Replace('/', '.');

        public string Package
        {
            get { int i = Name.LastIndexOf('/'); return i < 0 ? "" : Name.Substring(0, i); }
        }

        public JMethod FindVirtual(string key)
        {
            lock (vcache)
            {
                JMethod m;
                if (vcache.TryGetValue(key, out m)) return m;
                m = LookupVirtual(key);
                vcache[key] = m;
                return m;
            }
        }

        JMethod LookupVirtual(string key)
        {
            for (var c = this; c != null; c = c.Super)
            {
                JMethod m;
                if (c.Methods.TryGetValue(key, out m) && !m.IsStatic && !m.IsAbstract) return m;
            }
            var seen = new HashSet<JClass>();
            var q = new Queue<JClass>();
            for (var c = this; c != null; c = c.Super) foreach (var i in c.Interfaces) q.Enqueue(i);
            JMethod abs = null;
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                if (!seen.Add(i)) continue;
                JMethod m;
                if (i.Methods.TryGetValue(key, out m))
                {
                    if (!m.IsAbstract) return m;
                    if (abs == null) abs = m;
                }
                foreach (var j in i.Interfaces) q.Enqueue(j);
            }
            return abs;
        }

        public JMethod FindMethod(string key)
        {
            for (var c = this; c != null; c = c.Super)
            {
                JMethod m;
                if (c.Methods.TryGetValue(key, out m)) return m;
            }
            var q = new Queue<JClass>();
            for (var c = this; c != null; c = c.Super) foreach (var i in c.Interfaces) q.Enqueue(i);
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                JMethod m;
                if (i.Methods.TryGetValue(key, out m)) return m;
                foreach (var j in i.Interfaces) q.Enqueue(j);
            }
            return null;
        }

        public JField FindField(string name, string desc)
        {
            string key = name + ":" + desc;
            for (var c = this; c != null; c = c.Super)
            {
                JField f;
                if (c.Fields.TryGetValue(key, out f)) return f;
                foreach (var i in c.AllInterfaces())
                    if (i.Fields.TryGetValue(key, out f)) return f;
            }
            for (var c = this; c != null; c = c.Super)
                foreach (var f in c.Fields.Values)
                    if (f.Name == name) return f;
            return null;
        }

        public IEnumerable<JClass> AllInterfaces()
        {
            var seen = new HashSet<JClass>();
            var q = new Queue<JClass>(Interfaces);
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                if (!seen.Add(i)) continue;
                yield return i;
                foreach (var j in i.Interfaces) q.Enqueue(j);
            }
        }

        public bool IsSubclassOf(JClass other)
        {
            if (other == this) return true;
            var s = supers;
            if (s == null)
            {
                s = new HashSet<JClass>();
                for (var c = this; c != null; c = c.Super)
                {
                    s.Add(c);
                    foreach (var i in c.AllInterfaces()) s.Add(i);
                }
                supers = s;
            }
            if (s.Contains(other)) return true;
            if (IsArray && other.IsArray && Component != null && other.Component != null && !Component.IsPrimitive && !other.Component.IsPrimitive)
                return Component.IsSubclassOf(other.Component);
            return false;
        }

        public bool IsPrimitive => Name.Length == 1;

        public JMethod Declared(string key)
        {
            JMethod m;
            Methods.TryGetValue(key, out m);
            return m;
        }
    }

    public sealed class JavaException : Exception
    {
        public readonly JObject Obj;
        public JavaException(JObject obj) : base("java exception") { Obj = obj; }
        public override string Message
        {
            get
            {
                var t = Obj as Lib.ThrowableObj;
                return Obj.Class.JavaName + (t != null && t.Message != null ? ": " + t.Message : "");
            }
        }
    }

    public sealed class VmHalt : Exception
    {
        public VmHalt() : base("vm halted") { }
    }

    public sealed class JavaThrow : Exception
    {
        public readonly string ClassName, Msg;
        public JavaThrow(string cls, string msg = null) : base(cls + (msg != null ? ": " + msg : "")) { ClassName = cls; Msg = msg; }
    }

    public static class Descriptors
    {
        public static int ArgSlots(string desc)
        {
            int n = 0, i = 1;
            while (desc[i] != ')')
            {
                char c = desc[i];
                if (c == 'J' || c == 'D') { n += 2; i++; }
                else if (c == 'L') { n++; i = desc.IndexOf(';', i) + 1; }
                else if (c == '[')
                {
                    while (desc[i] == '[') i++;
                    if (desc[i] == 'L') i = desc.IndexOf(';', i) + 1; else i++;
                    n++;
                }
                else { n++; i++; }
            }
            return n;
        }

        public static List<string> Args(string desc)
        {
            var r = new List<string>();
            int i = 1;
            while (desc[i] != ')')
            {
                int s = i;
                while (desc[i] == '[') i++;
                if (desc[i] == 'L') i = desc.IndexOf(';', i) + 1; else i++;
                r.Add(desc.Substring(s, i - s));
            }
            return r;
        }
    }
}
