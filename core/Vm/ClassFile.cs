using System;
using System.Collections.Generic;
using System.Text;

namespace Lumidlet.Vm
{
    public sealed class CpClass
    {
        public string Name;
        public JClass Resolved;
        public JClass ArrayType;
        public JClass NewReady;
    }

    public sealed class InlineCache
    {
        public readonly JClass Cls;
        public readonly JMethod Target;
        public InlineCache(JClass c, JMethod m) { Cls = c; Target = m; }
    }

    public sealed class MethodRes
    {
        public readonly JMethod M;
        public readonly bool Direct;
        public MethodRes(JMethod m, bool direct) { M = m; Direct = direct; }
    }

    public sealed class CpMember
    {
        public string ClassName, Name, Desc, Key;
        public bool IsInterface;
        public JField Field;
        public JField SField;
        public JMethod Method;
        public MethodRes Res;
        public InlineCache IC;
        public int ArgSlots;
        public bool Direct;
    }

    public sealed class ConstantPool
    {
        public byte[] Tags;
        public object[] Items;
        public Slot[] Nums;
        public JClass Owner;
    }

    public static class ClassFile
    {
        sealed class Reader
        {
            readonly byte[] d; public int P;
            public Reader(byte[] data) { d = data; }
            public int U1() => d[P++];
            public int U2() { int v = (d[P] << 8) | d[P + 1]; P += 2; return v; }
            public int S4() { int v = (d[P] << 24) | (d[P + 1] << 16) | (d[P + 2] << 8) | d[P + 3]; P += 4; return v; }
            public byte[] Bytes(int n) { var b = new byte[n]; Buffer.BlockCopy(d, P, b, 0, n); P += n; return b; }
            public string Utf(int n)
            {
                var sb = new StringBuilder(n);
                int end = P + n;
                while (P < end)
                {
                    int c = d[P++];
                    if (c < 0x80) sb.Append((char)c);
                    else if ((c & 0xE0) == 0xC0) { int c2 = d[P++]; sb.Append((char)(((c & 0x1F) << 6) | (c2 & 0x3F))); }
                    else { int c2 = d[P++], c3 = d[P++]; sb.Append((char)(((c & 0x0F) << 12) | ((c2 & 0x3F) << 6) | (c3 & 0x3F))); }
                }
                return sb.ToString();
            }
        }

        public static JClass Parse(JVm vm, byte[] data, string expectName)
        {
            var r = new Reader(data);
            if (r.S4() != unchecked((int)0xCAFEBABE)) throw new JavaThrow("java/lang/ClassFormatError", expectName);
            r.U2(); r.U2();
            int n = r.U2();
            var cp = new ConstantPool { Tags = new byte[n], Items = new object[n], Nums = new Slot[n] };
            var raw = new int[n, 2];
            for (int i = 1; i < n; i++)
            {
                int tag = r.U1();
                cp.Tags[i] = (byte)tag;
                switch (tag)
                {
                    case 1: cp.Items[i] = r.Utf(r.U2()); break;
                    case 3: cp.Nums[i].I = r.S4(); break;
                    case 4: cp.Nums[i].I = r.S4(); break;
                    case 5:
                    case 6:
                        {
                            long hi = (uint)r.S4(), lo = (uint)r.S4();
                            cp.Nums[i].J = (hi << 32) | lo;
                            i++;
                            break;
                        }
                    case 7: case 8: case 16: raw[i, 0] = r.U2(); break;
                    case 9: case 10: case 11: case 12: case 18: raw[i, 0] = r.U2(); raw[i, 1] = r.U2(); break;
                    case 15: r.U1(); r.U2(); break;
                    default: throw new JavaThrow("java/lang/ClassFormatError", "bad cp tag " + tag);
                }
            }
            for (int i = 1; i < n; i++)
            {
                switch (cp.Tags[i])
                {
                    case 7: cp.Items[i] = new CpClass { Name = (string)cp.Items[raw[i, 0]] }; break;
                    case 8: cp.Items[i] = vm.Intern((string)cp.Items[raw[i, 0]]); break;
                }
            }
            for (int i = 1; i < n; i++)
            {
                int t = cp.Tags[i];
                if (t == 9 || t == 10 || t == 11)
                {
                    var c = (CpClass)cp.Items[raw[i, 0]];
                    int nat = raw[i, 1];
                    var m = new CpMember
                    {
                        ClassName = c.Name,
                        Name = (string)cp.Items[raw[nat, 0]],
                        Desc = (string)cp.Items[raw[nat, 1]],
                        IsInterface = t == 11
                    };
                    m.Key = m.Name + m.Desc;
                    if (t != 9) m.ArgSlots = Descriptors.ArgSlots(m.Desc);
                    cp.Items[i] = m;
                }
            }

            var cls = new JClass { Vm = vm, CP = cp };
            cp.Owner = cls;
            cls.Access = r.U2();
            cls.IsInterface = (cls.Access & 0x0200) != 0;
            cls.Name = ((CpClass)cp.Items[r.U2()]).Name;
            int sup = r.U2();
            string superName = sup == 0 ? null : ((CpClass)cp.Items[sup]).Name;
            int ni = r.U2();
            var ifaces = new string[ni];
            for (int i = 0; i < ni; i++) ifaces[i] = ((CpClass)cp.Items[r.U2()]).Name;

            if (superName != null) cls.Super = vm.LoadClass(superName);
            cls.Interfaces = new JClass[ni];
            for (int i = 0; i < ni; i++) cls.Interfaces[i] = vm.LoadClass(ifaces[i]);
            if (cls.Super != null)
            {
                cls.InstP = cls.Super.InstP;
                cls.InstO = cls.Super.InstO;
                cls.Factory = cls.Super.Factory;
            }

            int nf = r.U2();
            int sp = 0, so = 0;
            var statics = new List<JField>();
            for (int i = 0; i < nf; i++)
            {
                var f = new JField { Owner = cls, Access = r.U2() };
                f.Name = (string)cp.Items[r.U2()];
                f.Desc = (string)cp.Items[r.U2()];
                f.IsStatic = (f.Access & 0x0008) != 0;
                f.IsRef = f.Desc[0] == 'L' || f.Desc[0] == '[';
                f.Wide = f.Desc[0] == 'J' || f.Desc[0] == 'D';
                int na = r.U2();
                for (int a = 0; a < na; a++)
                {
                    string an = (string)cp.Items[r.U2()];
                    int len = r.S4();
                    if (an == "ConstantValue" && f.IsStatic)
                    {
                        int ci = r.U2();
                        if (cp.Tags[ci] == 8) f.ConstValue = cp.Items[ci];
                        else f.ConstValue = cp.Nums[ci];
                    }
                    else r.P += len;
                }
                if (f.IsStatic) { f.Slot = f.IsRef ? so++ : sp++; statics.Add(f); }
                else f.Slot = f.IsRef ? cls.InstO++ : cls.InstP++;
                cls.Fields[f.Name + ":" + f.Desc] = f;
            }
            cls.StaticP = new Slot[sp];
            cls.StaticO = new object[so];
            foreach (var f in statics)
            {
                if (f.ConstValue == null) continue;
                if (f.IsRef) cls.StaticO[f.Slot] = f.ConstValue as string;
                else
                {
                    var v = (Slot)f.ConstValue;
                    cls.StaticP[f.Slot] = v;
                }
            }

            int nm = r.U2();
            for (int i = 0; i < nm; i++)
            {
                int acc = r.U2();
                string name = (string)cp.Items[r.U2()];
                string desc = (string)cp.Items[r.U2()];
                var m = new JMethod(cls, name, desc, acc);
                int na = r.U2();
                for (int a = 0; a < na; a++)
                {
                    string an = (string)cp.Items[r.U2()];
                    int len = r.S4();
                    if (an == "Code")
                    {
                        int end = r.P + len;
                        m.MaxStack = r.U2();
                        m.MaxLocals = r.U2();
                        if (m.MaxLocals < m.ArgSlots) m.MaxLocals = m.ArgSlots;
                        m.Code = r.Bytes(r.S4());
                        int ne = r.U2();
                        m.Handlers = new ExHandler[ne];
                        for (int e = 0; e < ne; e++)
                            m.Handlers[e] = new ExHandler { Start = r.U2(), End = r.U2(), Handler = r.U2(), CatchIndex = r.U2() };
                        r.P = end;
                    }
                    else r.P += len;
                }
                if ((acc & 0x0100) != 0) m.Native = vm.Natives.FindUnbound(cls.Name, m.Key);
                cls.Methods[m.Key] = m;
            }
            return cls;
        }
    }
}
