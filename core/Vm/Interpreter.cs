using System;
using System.Threading;

namespace Lumidlet.Vm
{
    public sealed partial class JThread
    {
        internal void Execute(int stop)
        {
            var vm = Vm;
            Frame f = Frames[FrameCount - 1];
            JMethod m = f.M;
            byte[] code = m.Code;
            ConstantPool cp = m.Owner.CP;
            int pc = f.Pc, lp = f.Lp, sp = f.Sp, opPc = pc, hb = 2048;
            Slot[] s = S;
            object[] r = R;

            while (true)
            {
                try
                {
                    while (true)
                    {
                        opPc = pc;
                        int op = code[pc++];
                        switch (op)
                        {
                            case 0x00: break;
                            case 0x01: r[sp++] = null; break;
                            case 0x02: case 0x03: case 0x04: case 0x05: case 0x06: case 0x07: case 0x08:
                                s[sp++].I = op - 3; break;
                            case 0x09: case 0x0a: s[sp].J = op - 9; sp += 2; break;
                            case 0x0b: case 0x0c: case 0x0d: s[sp++].F = op - 0x0b; break;
                            case 0x0e: case 0x0f: s[sp].D = op - 0x0e; sp += 2; break;
                            case 0x10: s[sp++].I = (sbyte)code[pc++]; break;
                            case 0x11: s[sp++].I = (short)((code[pc] << 8) | code[pc + 1]); pc += 2; break;
                            case 0x12:
                            case 0x13:
                                {
                                    int idx;
                                    if (op == 0x12) idx = code[pc++]; else { idx = (code[pc] << 8) | code[pc + 1]; pc += 2; }
                                    int tag = cp.Tags[idx];
                                    if (tag == 3 || tag == 4) s[sp++] = cp.Nums[idx];
                                    else if (tag == 8) r[sp++] = cp.Items[idx];
                                    else if (tag == 7) r[sp++] = vm.ClassObject(ResolveClass((CpClass)cp.Items[idx]));
                                    else throw new JavaThrow("java/lang/VerifyError", "ldc tag " + tag);
                                    break;
                                }
                            case 0x14: s[sp] = cp.Nums[(code[pc] << 8) | code[pc + 1]]; pc += 2; sp += 2; break;
                            case 0x15: case 0x17: s[sp++] = s[lp + code[pc++]]; break;
                            case 0x16: case 0x18: s[sp] = s[lp + code[pc++]]; sp += 2; break;
                            case 0x19: r[sp++] = r[lp + code[pc++]]; break;
                            case 0x1a: case 0x1b: case 0x1c: case 0x1d: s[sp++] = s[lp + op - 0x1a]; break;
                            case 0x1e: case 0x1f: case 0x20: case 0x21: s[sp] = s[lp + op - 0x1e]; sp += 2; break;
                            case 0x22: case 0x23: case 0x24: case 0x25: s[sp++] = s[lp + op - 0x22]; break;
                            case 0x26: case 0x27: case 0x28: case 0x29: s[sp] = s[lp + op - 0x26]; sp += 2; break;
                            case 0x2a: case 0x2b: case 0x2c: case 0x2d: r[sp++] = r[lp + op - 0x2a]; break;
                            case 0x2e: sp--; s[sp - 1].I = ((int[])r[sp - 1])[s[sp].I]; break;
                            case 0x2f: s[sp - 2].J = ((long[])r[sp - 2])[s[sp - 1].I]; break;
                            case 0x30: sp--; s[sp - 1].F = ((float[])r[sp - 1])[s[sp].I]; break;
                            case 0x31: s[sp - 2].D = ((double[])r[sp - 2])[s[sp - 1].I]; break;
                            case 0x32: sp--; r[sp - 1] = ((ObjArray)r[sp - 1]).Data[s[sp].I]; break;
                            case 0x33:
                                {
                                    sp--;
                                    var arr = r[sp - 1];
                                    var ba = arr as sbyte[];
                                    if (ba != null) s[sp - 1].I = ba[s[sp].I];
                                    else s[sp - 1].I = ((bool[])arr)[s[sp].I] ? 1 : 0;
                                    break;
                                }
                            case 0x34: sp--; s[sp - 1].I = ((char[])r[sp - 1])[s[sp].I]; break;
                            case 0x35: sp--; s[sp - 1].I = ((short[])r[sp - 1])[s[sp].I]; break;
                            case 0x36: case 0x38: s[lp + code[pc++]] = s[--sp]; break;
                            case 0x37: case 0x39: sp -= 2; s[lp + code[pc++]] = s[sp]; break;
                            case 0x3a: { int i = lp + code[pc++]; sp--; r[i] = r[sp]; s[i] = s[sp]; break; }
                            case 0x3b: case 0x3c: case 0x3d: case 0x3e: s[lp + op - 0x3b] = s[--sp]; break;
                            case 0x3f: case 0x40: case 0x41: case 0x42: sp -= 2; s[lp + op - 0x3f] = s[sp]; break;
                            case 0x43: case 0x44: case 0x45: case 0x46: s[lp + op - 0x43] = s[--sp]; break;
                            case 0x47: case 0x48: case 0x49: case 0x4a: sp -= 2; s[lp + op - 0x47] = s[sp]; break;
                            case 0x4b: case 0x4c: case 0x4d: case 0x4e: { int i = lp + op - 0x4b; sp--; r[i] = r[sp]; s[i] = s[sp]; break; }
                            case 0x4f: sp -= 3; ((int[])r[sp])[s[sp + 1].I] = s[sp + 2].I; break;
                            case 0x50: sp -= 4; ((long[])r[sp])[s[sp + 1].I] = s[sp + 2].J; break;
                            case 0x51: sp -= 3; ((float[])r[sp])[s[sp + 1].I] = s[sp + 2].F; break;
                            case 0x52: sp -= 4; ((double[])r[sp])[s[sp + 1].I] = s[sp + 2].D; break;
                            case 0x53:
                                {
                                    sp -= 3;
                                    var oa = (ObjArray)r[sp];
                                    oa.Data[s[sp + 1].I] = r[sp + 2];
                                    break;
                                }
                            case 0x54:
                                {
                                    sp -= 3;
                                    var arr = r[sp];
                                    var ba = arr as sbyte[];
                                    if (ba != null) ba[s[sp + 1].I] = (sbyte)s[sp + 2].I;
                                    else ((bool[])arr)[s[sp + 1].I] = (s[sp + 2].I & 1) != 0;
                                    break;
                                }
                            case 0x55: sp -= 3; ((char[])r[sp])[s[sp + 1].I] = (char)s[sp + 2].I; break;
                            case 0x56: sp -= 3; ((short[])r[sp])[s[sp + 1].I] = (short)s[sp + 2].I; break;
                            case 0x57: sp--; break;
                            case 0x58: sp -= 2; break;
                            case 0x59: s[sp] = s[sp - 1]; r[sp] = r[sp - 1]; sp++; break;
                            case 0x5a:
                                s[sp] = s[sp - 1]; r[sp] = r[sp - 1];
                                s[sp - 1] = s[sp - 2]; r[sp - 1] = r[sp - 2];
                                s[sp - 2] = s[sp]; r[sp - 2] = r[sp];
                                sp++; break;
                            case 0x5b:
                                s[sp] = s[sp - 1]; r[sp] = r[sp - 1];
                                s[sp - 1] = s[sp - 2]; r[sp - 1] = r[sp - 2];
                                s[sp - 2] = s[sp - 3]; r[sp - 2] = r[sp - 3];
                                s[sp - 3] = s[sp]; r[sp - 3] = r[sp];
                                sp++; break;
                            case 0x5c:
                                s[sp] = s[sp - 2]; r[sp] = r[sp - 2];
                                s[sp + 1] = s[sp - 1]; r[sp + 1] = r[sp - 1];
                                sp += 2; break;
                            case 0x5d:
                                s[sp + 1] = s[sp - 1]; r[sp + 1] = r[sp - 1];
                                s[sp] = s[sp - 2]; r[sp] = r[sp - 2];
                                s[sp - 1] = s[sp - 3]; r[sp - 1] = r[sp - 3];
                                s[sp - 2] = s[sp + 1]; r[sp - 2] = r[sp + 1];
                                s[sp - 3] = s[sp]; r[sp - 3] = r[sp];
                                sp += 2; break;
                            case 0x5e:
                                s[sp + 1] = s[sp - 1]; r[sp + 1] = r[sp - 1];
                                s[sp] = s[sp - 2]; r[sp] = r[sp - 2];
                                s[sp - 1] = s[sp - 3]; r[sp - 1] = r[sp - 3];
                                s[sp - 2] = s[sp - 4]; r[sp - 2] = r[sp - 4];
                                s[sp - 3] = s[sp + 1]; r[sp - 3] = r[sp + 1];
                                s[sp - 4] = s[sp]; r[sp - 4] = r[sp];
                                sp += 2; break;
                            case 0x5f:
                                {
                                    var ts = s[sp - 1]; var tr = r[sp - 1];
                                    s[sp - 1] = s[sp - 2]; r[sp - 1] = r[sp - 2];
                                    s[sp - 2] = ts; r[sp - 2] = tr;
                                    break;
                                }
                            case 0x60: sp--; s[sp - 1].I += s[sp].I; break;
                            case 0x61: sp -= 2; s[sp - 2].J += s[sp].J; break;
                            case 0x62: sp--; s[sp - 1].F += s[sp].F; break;
                            case 0x63: sp -= 2; s[sp - 2].D += s[sp].D; break;
                            case 0x64: sp--; s[sp - 1].I -= s[sp].I; break;
                            case 0x65: sp -= 2; s[sp - 2].J -= s[sp].J; break;
                            case 0x66: sp--; s[sp - 1].F -= s[sp].F; break;
                            case 0x67: sp -= 2; s[sp - 2].D -= s[sp].D; break;
                            case 0x68: sp--; s[sp - 1].I *= s[sp].I; break;
                            case 0x69: sp -= 2; s[sp - 2].J *= s[sp].J; break;
                            case 0x6a: sp--; s[sp - 1].F *= s[sp].F; break;
                            case 0x6b: sp -= 2; s[sp - 2].D *= s[sp].D; break;
                            case 0x6c:
                                {
                                    sp--;
                                    int d = s[sp].I;
                                    if (d == 0) throw new DivideByZeroException();
                                    s[sp - 1].I = d == -1 ? -s[sp - 1].I : s[sp - 1].I / d;
                                    break;
                                }
                            case 0x6d:
                                {
                                    sp -= 2;
                                    long d = s[sp].J;
                                    if (d == 0) throw new DivideByZeroException();
                                    s[sp - 2].J = d == -1 ? -s[sp - 2].J : s[sp - 2].J / d;
                                    break;
                                }
                            case 0x6e: sp--; s[sp - 1].F /= s[sp].F; break;
                            case 0x6f: sp -= 2; s[sp - 2].D /= s[sp].D; break;
                            case 0x70:
                                {
                                    sp--;
                                    int d = s[sp].I;
                                    if (d == 0) throw new DivideByZeroException();
                                    s[sp - 1].I = d == -1 ? 0 : s[sp - 1].I % d;
                                    break;
                                }
                            case 0x71:
                                {
                                    sp -= 2;
                                    long d = s[sp].J;
                                    if (d == 0) throw new DivideByZeroException();
                                    s[sp - 2].J = d == -1 ? 0 : s[sp - 2].J % d;
                                    break;
                                }
                            case 0x72: sp--; s[sp - 1].F %= s[sp].F; break;
                            case 0x73: sp -= 2; s[sp - 2].D %= s[sp].D; break;
                            case 0x74: s[sp - 1].I = -s[sp - 1].I; break;
                            case 0x75: s[sp - 2].J = -s[sp - 2].J; break;
                            case 0x76: s[sp - 1].F = -s[sp - 1].F; break;
                            case 0x77: s[sp - 2].D = -s[sp - 2].D; break;
                            case 0x78: sp--; s[sp - 1].I <<= s[sp].I; break;
                            case 0x79: sp--; s[sp - 2].J <<= s[sp].I; break;
                            case 0x7a: sp--; s[sp - 1].I >>= s[sp].I; break;
                            case 0x7b: sp--; s[sp - 2].J >>= s[sp].I; break;
                            case 0x7c: sp--; s[sp - 1].I = (int)((uint)s[sp - 1].I >> s[sp].I); break;
                            case 0x7d: sp--; s[sp - 2].J = (long)((ulong)s[sp - 2].J >> s[sp].I); break;
                            case 0x7e: sp--; s[sp - 1].I &= s[sp].I; break;
                            case 0x7f: sp -= 2; s[sp - 2].J &= s[sp].J; break;
                            case 0x80: sp--; s[sp - 1].I |= s[sp].I; break;
                            case 0x81: sp -= 2; s[sp - 2].J |= s[sp].J; break;
                            case 0x82: sp--; s[sp - 1].I ^= s[sp].I; break;
                            case 0x83: sp -= 2; s[sp - 2].J ^= s[sp].J; break;
                            case 0x84: s[lp + code[pc]].I += (sbyte)code[pc + 1]; pc += 2; break;
                            case 0x85: s[sp - 1].J = s[sp - 1].I; sp++; break;
                            case 0x86: s[sp - 1].F = s[sp - 1].I; break;
                            case 0x87: s[sp - 1].D = s[sp - 1].I; sp++; break;
                            case 0x88: sp--; s[sp - 1].I = (int)s[sp - 1].J; break;
                            case 0x89: sp--; s[sp - 1].F = s[sp - 1].J; break;
                            case 0x8a: s[sp - 2].D = s[sp - 2].J; break;
                            case 0x8b: s[sp - 1].I = F2I(s[sp - 1].F); break;
                            case 0x8c: s[sp - 1].J = D2L(s[sp - 1].F); sp++; break;
                            case 0x8d: s[sp - 1].D = s[sp - 1].F; sp++; break;
                            case 0x8e: sp--; s[sp - 1].I = D2I(s[sp - 1].D); break;
                            case 0x8f: s[sp - 2].J = D2L(s[sp - 2].D); break;
                            case 0x90: sp--; s[sp - 1].F = (float)s[sp - 1].D; break;
                            case 0x91: s[sp - 1].I = (sbyte)s[sp - 1].I; break;
                            case 0x92: s[sp - 1].I = (char)s[sp - 1].I; break;
                            case 0x93: s[sp - 1].I = (short)s[sp - 1].I; break;
                            case 0x94:
                                {
                                    sp -= 3;
                                    long a = s[sp - 1].J, b2 = s[sp + 1].J;
                                    s[sp - 1].I = a > b2 ? 1 : a == b2 ? 0 : -1;
                                    break;
                                }
                            case 0x95:
                            case 0x96:
                                {
                                    sp--;
                                    float a = s[sp - 1].F, b2 = s[sp].F;
                                    s[sp - 1].I = a > b2 ? 1 : a == b2 ? 0 : a < b2 ? -1 : (op == 0x95 ? -1 : 1);
                                    break;
                                }
                            case 0x97:
                            case 0x98:
                                {
                                    sp -= 3;
                                    double a = s[sp - 1].D, b2 = s[sp + 1].D;
                                    s[sp - 1].I = a > b2 ? 1 : a == b2 ? 0 : a < b2 ? -1 : (op == 0x97 ? -1 : 1);
                                    break;
                                }
                            case 0x99: case 0x9a: case 0x9b: case 0x9c: case 0x9d: case 0x9e:
                                {
                                    int v = s[--sp].I;
                                    bool t;
                                    switch (op)
                                    {
                                        case 0x99: t = v == 0; break;
                                        case 0x9a: t = v != 0; break;
                                        case 0x9b: t = v < 0; break;
                                        case 0x9c: t = v >= 0; break;
                                        case 0x9d: t = v > 0; break;
                                        default: t = v <= 0; break;
                                    }
                                    if (t) { int off = (short)((code[pc] << 8) | code[pc + 1]); pc = opPc + off; if (off <= 0 && --hb <= 0) { hb = 2048; if (vm.Halted) throw new VmHalt(); } }
                                    else pc += 2;
                                    break;
                                }
                            case 0x9f: case 0xa0: case 0xa1: case 0xa2: case 0xa3: case 0xa4:
                                {
                                    sp -= 2;
                                    int a = s[sp].I, b2 = s[sp + 1].I;
                                    bool t;
                                    switch (op)
                                    {
                                        case 0x9f: t = a == b2; break;
                                        case 0xa0: t = a != b2; break;
                                        case 0xa1: t = a < b2; break;
                                        case 0xa2: t = a >= b2; break;
                                        case 0xa3: t = a > b2; break;
                                        default: t = a <= b2; break;
                                    }
                                    if (t) { int off = (short)((code[pc] << 8) | code[pc + 1]); pc = opPc + off; if (off <= 0 && --hb <= 0) { hb = 2048; if (vm.Halted) throw new VmHalt(); } }
                                    else pc += 2;
                                    break;
                                }
                            case 0xa5:
                            case 0xa6:
                                {
                                    sp -= 2;
                                    bool eq = ReferenceEquals(r[sp], r[sp + 1]);
                                    if (eq == (op == 0xa5)) pc = opPc + (short)((code[pc] << 8) | code[pc + 1]);
                                    else pc += 2;
                                    break;
                                }
                            case 0xa7:
                                {
                                    int off = (short)((code[pc] << 8) | code[pc + 1]);
                                    pc = opPc + off;
                                    if (off <= 0 && --hb <= 0) { hb = 2048; if (vm.Halted) throw new VmHalt(); }
                                    break;
                                }
                            case 0xa8:
                                s[sp].I = pc + 2; r[sp] = null; sp++;
                                pc = opPc + (short)((code[pc] << 8) | code[pc + 1]);
                                break;
                            case 0xa9: pc = s[lp + code[pc]].I; break;
                            case 0xaa:
                                {
                                    int p = (opPc + 4) & ~3;
                                    int def = S4(code, p), lo = S4(code, p + 4), hi = S4(code, p + 8);
                                    int key = s[--sp].I;
                                    if (key < lo || key > hi) pc = opPc + def;
                                    else pc = opPc + S4(code, p + 12 + (key - lo) * 4);
                                    break;
                                }
                            case 0xab:
                                {
                                    int p = (opPc + 4) & ~3;
                                    int def = S4(code, p), n = S4(code, p + 4);
                                    int key = s[--sp].I;
                                    int lo = 0, hi = n - 1, target = def;
                                    while (lo <= hi)
                                    {
                                        int mid = (lo + hi) >> 1;
                                        int mk = S4(code, p + 8 + mid * 8);
                                        if (mk == key) { target = S4(code, p + 12 + mid * 8); break; }
                                        if (mk < key) lo = mid + 1; else hi = mid - 1;
                                    }
                                    pc = opPc + target;
                                    break;
                                }
                            case 0xac: case 0xad: case 0xae: case 0xaf: case 0xb0: case 0xb1:
                                {
                                    int rs;
                                    if (op == 0xac || op == 0xae) { s[lp] = s[sp - 1]; rs = 1; }
                                    else if (op == 0xad || op == 0xaf) { s[lp] = s[sp - 2]; rs = 2; }
                                    else if (op == 0xb0) { r[lp] = r[sp - 1]; rs = 1; }
                                    else rs = 0;
                                    PopFrame();
                                    if (FrameCount == stop) return;
                                    f = Frames[FrameCount - 1];
                                    m = f.M; code = m.Code; cp = m.Owner.CP;
                                    lp = f.Lp; pc = f.Pc; sp = f.Sp + rs;
                                    break;
                                }
                            case 0xb2:
                                {
                                    var fr = (CpMember)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var fd = fr.SField;
                                    if (fd == null) { Top = sp; fd = ResolveStatic(fr); s = S; r = R; }
                                    if (fd.IsRef) r[sp++] = fd.Owner.StaticO[fd.Slot];
                                    else { s[sp++] = fd.Owner.StaticP[fd.Slot]; if (fd.Wide) sp++; }
                                    break;
                                }
                            case 0xb3:
                                {
                                    var fr = (CpMember)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var fd = fr.SField;
                                    if (fd == null) { Top = sp; fd = ResolveStatic(fr); s = S; r = R; }
                                    if (fd.IsRef) fd.Owner.StaticO[fd.Slot] = r[--sp];
                                    else if (fd.Wide) { sp -= 2; fd.Owner.StaticP[fd.Slot] = s[sp]; }
                                    else fd.Owner.StaticP[fd.Slot] = s[--sp];
                                    break;
                                }
                            case 0xb4:
                                {
                                    var fr = (CpMember)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var fd = fr.Field ?? ResolveField(fr);
                                    var o = (JObject)r[sp - 1];
                                    if (fd.IsRef) r[sp - 1] = o.O[fd.Slot];
                                    else { s[sp - 1] = o.P[fd.Slot]; if (fd.Wide) sp++; }
                                    break;
                                }
                            case 0xb5:
                                {
                                    var fr = (CpMember)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var fd = fr.Field ?? ResolveField(fr);
                                    if (fd.IsRef) { sp -= 2; ((JObject)r[sp]).O[fd.Slot] = r[sp + 1]; }
                                    else if (fd.Wide) { sp -= 3; ((JObject)r[sp]).P[fd.Slot] = s[sp + 1]; }
                                    else { sp -= 2; ((JObject)r[sp]).P[fd.Slot] = s[sp + 1]; }
                                    break;
                                }
                            case 0xb6: case 0xb7: case 0xb8: case 0xb9:
                                {
                                    var mr = (CpMember)cp.Items[(code[pc] << 8) | code[pc + 1]];
                                    pc += op == 0xb9 ? 4 : 2;
                                    var res = mr.Res;
                                    if (res == null) { Top = sp; res = ResolveMethod(mr, op); s = S; r = R; }
                                    JMethod target;
                                    int b = sp - mr.ArgSlots - (op == 0xb8 ? 0 : 1);
                                    if (op == 0xb8 || op == 0xb7 || res.Direct)
                                    {
                                        target = res.M;
                                        if (op != 0xb8 && r[b] == null) throw new NullReferenceException();
                                    }
                                    else
                                    {
                                        var recv = r[b];
                                        if (recv == null) throw new NullReferenceException();
                                        var jo = recv as JObject;
                                        var rc = jo != null ? jo.Class : vm.ClassOf(recv);
                                        var ic = mr.IC;
                                        if (ic != null && ic.Cls == rc) target = ic.Target;
                                        else
                                        {
                                            target = rc.FindVirtual(mr.Key);
                                            if (target == null) throw new JavaThrow("java/lang/AbstractMethodError", rc.JavaName + "." + mr.Key);
                                            Volatile.Write(ref mr.IC, new InlineCache(rc, target));
                                        }
                                    }
                                    f.Pc = pc; f.OpPc = opPc; f.Sp = b;
                                    if (target.Native != null)
                                    {
                                        Top = sp;
                                        target.Native(this, b);
                                        s = S; r = R;
                                        sp = b + target.RetSlots;
                                        break;
                                    }
                                    if (target.Code == null) throw new JavaThrow("java/lang/AbstractMethodError", target.ToString());
                                    if (--hb <= 0) { hb = 2048; if (vm.Halted) throw new VmHalt(); }
                                    int need = b + target.MaxLocals + target.MaxStack + 16;
                                    if (need > s.Length) { Ensure(need); s = S; r = R; }
                                    f = PushFrame();
                                    f.M = target; f.Lp = b; f.Lock = null;
                                    m = target; code = m.Code; cp = m.Owner.CP;
                                    lp = b; pc = 0; sp = b + m.MaxLocals;
                                    if (m.IsSynchronized)
                                    {
                                        f.Lock = m.IsStatic ? vm.ClassObject(m.Owner) : r[b];
                                        Monitor.Enter(f.Lock);
                                    }
                                    break;
                                }
                            case 0xba: throw new JavaThrow("java/lang/IncompatibleClassChangeError", "invokedynamic");
                            case 0xbb:
                                {
                                    var cc = (CpClass)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var c = cc.NewReady;
                                    if (c == null) { Top = sp; c = ResolveNew(cc); s = S; r = R; }
                                    r[sp++] = vm.NewObject(c);
                                    break;
                                }
                            case 0xbc:
                                {
                                    int n = s[sp - 1].I;
                                    char k;
                                    switch (code[pc++])
                                    {
                                        case 4: k = 'Z'; break;
                                        case 5: k = 'C'; break;
                                        case 6: k = 'F'; break;
                                        case 7: k = 'D'; break;
                                        case 8: k = 'B'; break;
                                        case 9: k = 'S'; break;
                                        case 10: k = 'I'; break;
                                        default: k = 'J'; break;
                                    }
                                    r[sp - 1] = JVm.NewPrimArray(k, n);
                                    break;
                                }
                            case 0xbd:
                                {
                                    var cc = (CpClass)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var at = cc.ArrayType;
                                    if (at == null) { at = vm.ArrayOf(cc.Resolved ?? ResolveClass(cc)); cc.ArrayType = at; }
                                    int n = s[sp - 1].I;
                                    if (n < 0) throw new JavaThrow("java/lang/NegativeArraySizeException", n.ToString());
                                    r[sp - 1] = new ObjArray(at, n);
                                    break;
                                }
                            case 0xbe: s[sp - 1].I = JVm.ArrayLength(r[sp - 1]); break;
                            case 0xbf:
                                {
                                    var ex = r[sp - 1];
                                    if (ex == null) throw new NullReferenceException();
                                    throw new JavaException((JObject)ex);
                                }
                            case 0xc0:
                                {
                                    var cc = (CpClass)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var o = r[sp - 1];
                                    if (o != null)
                                    {
                                        var c = cc.Resolved ?? ResolveClass(cc);
                                        if (!vm.InstanceOf(o, c)) throw new JavaThrow("java/lang/ClassCastException", vm.ClassOf(o).JavaName + " cannot be cast to " + c.JavaName);
                                    }
                                    break;
                                }
                            case 0xc1:
                                {
                                    var cc = (CpClass)cp.Items[(code[pc] << 8) | code[pc + 1]]; pc += 2;
                                    var o = r[sp - 1];
                                    if (o == null) s[sp - 1].I = 0;
                                    else
                                    {
                                        JClass c = cc.Resolved;
                                        if (c == null) { c = vm.TryLoadClass(cc.Name); cc.Resolved = c; }
                                        s[sp - 1].I = c != null && vm.InstanceOf(o, c) ? 1 : 0;
                                    }
                                    break;
                                }
                            case 0xc2:
                                {
                                    var o = r[--sp];
                                    if (o == null) throw new NullReferenceException();
                                    Monitor.Enter(o);
                                    break;
                                }
                            case 0xc3:
                                {
                                    var o = r[--sp];
                                    if (o == null) throw new NullReferenceException();
                                    Monitor.Exit(o);
                                    break;
                                }
                            case 0xc4:
                                {
                                    int op2 = code[pc];
                                    int idx = (code[pc + 1] << 8) | code[pc + 2];
                                    pc += 3;
                                    switch (op2)
                                    {
                                        case 0x15: case 0x17: s[sp++] = s[lp + idx]; break;
                                        case 0x16: case 0x18: s[sp] = s[lp + idx]; sp += 2; break;
                                        case 0x19: r[sp++] = r[lp + idx]; break;
                                        case 0x36: case 0x38: s[lp + idx] = s[--sp]; break;
                                        case 0x37: case 0x39: sp -= 2; s[lp + idx] = s[sp]; break;
                                        case 0x3a: sp--; r[lp + idx] = r[sp]; s[lp + idx] = s[sp]; break;
                                        case 0x84: s[lp + idx].I += (short)((code[pc] << 8) | code[pc + 1]); pc += 2; break;
                                        case 0xa9: pc = s[lp + idx].I; break;
                                        default: throw new JavaThrow("java/lang/VerifyError", "wide " + op2);
                                    }
                                    break;
                                }
                            case 0xc5:
                                {
                                    var cc = (CpClass)cp.Items[(code[pc] << 8) | code[pc + 1]];
                                    int nd = code[pc + 2];
                                    pc += 3;
                                    var dims = new int[nd];
                                    sp -= nd;
                                    for (int i = 0; i < nd; i++)
                                    {
                                        dims[i] = s[sp + i].I;
                                        if (dims[i] < 0) throw new JavaThrow("java/lang/NegativeArraySizeException");
                                    }
                                    r[sp++] = vm.NewMultiArray(cc.Resolved ?? ResolveClass(cc), dims, 0);
                                    break;
                                }
                            case 0xc6:
                            case 0xc7:
                                {
                                    bool isNull = r[--sp] == null;
                                    if (isNull == (op == 0xc6)) pc = opPc + (short)((code[pc] << 8) | code[pc + 1]);
                                    else pc += 2;
                                    break;
                                }
                            case 0xc8:
                                {
                                    int off = S4(code, pc);
                                    pc = opPc + off;
                                    if (off <= 0 && --hb <= 0) { hb = 2048; if (vm.Halted) throw new VmHalt(); }
                                    break;
                                }
                            case 0xc9:
                                s[sp].I = pc + 4; r[sp] = null; sp++;
                                pc = opPc + S4(code, pc);
                                break;
                            default:
                                throw new JavaThrow("java/lang/VerifyError", "bad opcode " + op + " in " + m);
                        }
                    }
                }
                catch (VmHalt)
                {
                    while (FrameCount > stop) PopFrame();
                    throw;
                }
                catch (Exception e)
                {
                    JObject ex;
                    Top = lp + m.MaxLocals + m.MaxStack;
                    try { ex = vm.ToJava(this, e); }
                    catch (VmHalt) { while (FrameCount > stop) PopFrame(); throw; }
                    s = S; r = R;
                    if (vm.TraceExceptions)
                    {
                        var th = ex as Lib.ThrowableObj;
                        vm.Log("throw " + ex.Class.JavaName + (th != null && th.Message != null ? ": " + th.Message : "") + " at " + m + " pc " + opPc);
                    }
                    while (true)
                    {
                        int h = FindHandler(m, opPc, ex);
                        if (h >= 0)
                        {
                            sp = lp + m.MaxLocals;
                            r[sp++] = ex;
                            pc = h;
                            break;
                        }
                        PopFrame();
                        if (FrameCount == stop) throw new JavaException(ex);
                        f = Frames[FrameCount - 1];
                        m = f.M; code = m.Code; cp = m.Owner.CP;
                        lp = f.Lp; opPc = f.OpPc; pc = f.Pc;
                    }
                }
            }
        }
    }
}
