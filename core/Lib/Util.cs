using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Lumidlet.Vm;

namespace Lumidlet.Lib
{
    public sealed class VectorObj : JObject
    {
        public readonly List<object> L = new List<object>();
    }

    public sealed class EnumObj : JObject
    {
        public object[] Items;
        public int Pos;
    }

    public sealed class JavaKeyComparer : IEqualityComparer<object>
    {
        public static readonly JavaKeyComparer Instance = new JavaKeyComparer();
        public new bool Equals(object a, object b) => Rt.JEquals(JThread.Current, a, b);
        public int GetHashCode(object o) => Rt.JHash(JThread.Current, o);
    }

    public sealed class HashtableObj : JObject
    {
        public readonly Dictionary<object, object> D = new Dictionary<object, object>(JavaKeyComparer.Instance);
        public readonly List<object> Order = new List<object>();
    }

    public sealed class RandomObj : JObject
    {
        public long Seed;
        public double NextGaussian;
        public bool HaveGaussian;
        public void SetSeed(long s) { Seed = (s ^ 0x5DEECE66DL) & ((1L << 48) - 1); HaveGaussian = false; }
        public int Next(int bits)
        {
            lock (this)
            {
                Seed = (Seed * 0x5DEECE66DL + 0xBL) & ((1L << 48) - 1);
                return (int)((ulong)Seed >> (48 - bits));
            }
        }
    }

    public sealed class DateObj : JObject { public long Ms; }

    public sealed class CalendarObj : JObject
    {
        public long Ms;
        public TimeZoneInfo Tz = TimeZoneInfo.Local;
    }

    public sealed class TimeZoneObj : JObject { public TimeZoneInfo Tz; public string Id; }

    public sealed class TimerObj : JObject
    {
        public readonly List<TimerTaskObj> Tasks = new List<TimerTaskObj>();
        public bool Cancelled;
        public int Errors;
        public JThread Thread;
    }

    public sealed class TimerTaskObj : JObject
    {
        public TimerObj Timer;
        public long Next, Period;
        public bool FixedRate, Cancelled, Scheduled;
        public long LastRun;
    }

    public static partial class Library
    {
        static VectorObj Vec(JThread t, int b) => (VectorObj)t.R[b];

        static JObject MakeEnum(JThread t, object[] items)
        {
            var e = (EnumObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/Enumerator"));
            e.Items = items;
            return e;
        }

        static void Util(NativeRegistry n)
        {
            n.Interface("java/util/Enumeration", c => c.Abstract("hasMoreElements", "()Z").Abstract("nextElement", "()Ljava/lang/Object;"));
            n.Define("lumidlet/Enumerator", "java/lang/Object", c => c
                .M("hasMoreElements", "()Z", (t, b) => { var e = (EnumObj)t.R[b]; t.RetZ(b, e.Pos < e.Items.Length); })
                .M("nextElement", "()Ljava/lang/Object;", (t, b) =>
                {
                    var e = (EnumObj)t.R[b];
                    if (e.Pos >= e.Items.Length) throw new JavaThrow("java/util/NoSuchElementException");
                    t.RetA(b, e.Items[e.Pos++]);
                }), Rt.F<EnumObj>(), new[] { "java/util/Enumeration" });

            n.Define("java/util/Vector", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("<init>", "(I)V", (t, b) => { if (t.I(b + 1) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); Vec(t, b).L.Capacity = t.I(b + 1); })
                .M("<init>", "(II)V", (t, b) => { if (t.I(b + 1) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); Vec(t, b).L.Capacity = t.I(b + 1); })
                .M("addElement", "(Ljava/lang/Object;)V", (t, b) => { var l = Vec(t, b).L; lock (l) l.Add(t.R[b + 1]); })
                .M("add", "(Ljava/lang/Object;)Z", (t, b) => { var l = Vec(t, b).L; lock (l) l.Add(t.R[b + 1]); t.RetZ(b, true); })
                .M("add", "(ILjava/lang/Object;)V", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 1); if (i < 0 || i > l.Count) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException", i.ToString()); l.Insert(i, t.R[b + 2]); } })
                .M("insertElementAt", "(Ljava/lang/Object;I)V", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 2); if (i < 0 || i > l.Count) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException", i.ToString()); l.Insert(i, t.R[b + 1]); } })
                .M("setElementAt", "(Ljava/lang/Object;I)V", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 2); Rt.Idx(i, l.Count); l[i] = t.R[b + 1]; } })
                .M("set", "(ILjava/lang/Object;)Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 1); Rt.Idx(i, l.Count); var old = l[i]; l[i] = t.R[b + 2]; t.RetA(b, old); } })
                .M("removeElementAt", "(I)V", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 1); Rt.Idx(i, l.Count); l.RemoveAt(i); } })
                .M("remove", "(I)Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 1); Rt.Idx(i, l.Count); var old = l[i]; l.RemoveAt(i); t.RetA(b, old); } })
                .M("removeElement", "(Ljava/lang/Object;)Z", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = IndexOf(t, l, t.R[b + 1], 0); if (i >= 0) l.RemoveAt(i); t.RetZ(b, i >= 0); } })
                .M("remove", "(Ljava/lang/Object;)Z", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = IndexOf(t, l, t.R[b + 1], 0); if (i >= 0) l.RemoveAt(i); t.RetZ(b, i >= 0); } })
                .M("removeAllElements", "()V", (t, b) => { var l = Vec(t, b).L; lock (l) l.Clear(); })
                .M("clear", "()V", (t, b) => { var l = Vec(t, b).L; lock (l) l.Clear(); })
                .M("elementAt", "(I)Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; int i = t.I(b + 1); lock (l) { Rt.Idx(i, l.Count); t.RetA(b, l[i]); } })
                .M("get", "(I)Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; int i = t.I(b + 1); lock (l) { Rt.Idx(i, l.Count); t.RetA(b, l[i]); } })
                .M("firstElement", "()Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { if (l.Count == 0) throw new JavaThrow("java/util/NoSuchElementException"); t.RetA(b, l[0]); } })
                .M("lastElement", "()Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { if (l.Count == 0) throw new JavaThrow("java/util/NoSuchElementException"); t.RetA(b, l[l.Count - 1]); } })
                .M("size", "()I", (t, b) => t.RetI(b, Vec(t, b).L.Count))
                .M("isEmpty", "()Z", (t, b) => t.RetZ(b, Vec(t, b).L.Count == 0))
                .M("capacity", "()I", (t, b) => t.RetI(b, Vec(t, b).L.Capacity))
                .M("ensureCapacity", "(I)V", (t, b) => { var l = Vec(t, b).L; lock (l) if (t.I(b + 1) > l.Capacity) l.Capacity = t.I(b + 1); })
                .M("trimToSize", "()V", (t, b) => { })
                .M("setSize", "(I)V", (t, b) =>
                {
                    var l = Vec(t, b).L; int s = t.I(b + 1);
                    if (s < 0) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    lock (l) { if (s < l.Count) l.RemoveRange(s, l.Count - s); else while (l.Count < s) l.Add(null); }
                })
                .M("contains", "(Ljava/lang/Object;)Z", (t, b) => { var l = Vec(t, b).L; lock (l) t.RetZ(b, IndexOf(t, l, t.R[b + 1], 0) >= 0); })
                .M("indexOf", "(Ljava/lang/Object;)I", (t, b) => { var l = Vec(t, b).L; lock (l) t.RetI(b, IndexOf(t, l, t.R[b + 1], 0)); })
                .M("indexOf", "(Ljava/lang/Object;I)I", (t, b) => { var l = Vec(t, b).L; lock (l) t.RetI(b, IndexOf(t, l, t.R[b + 1], t.I(b + 2))); })
                .M("lastIndexOf", "(Ljava/lang/Object;)I", (t, b) => { var l = Vec(t, b).L; lock (l) t.RetI(b, LastIndexOf(t, l, t.R[b + 1], l.Count - 1)); })
                .M("lastIndexOf", "(Ljava/lang/Object;I)I", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = t.I(b + 2); if (i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetI(b, LastIndexOf(t, l, t.R[b + 1], i)); } })
                .M("elements", "()Ljava/util/Enumeration;", (t, b) => { var l = Vec(t, b).L; object[] a; lock (l) a = l.ToArray(); t.RetA(b, MakeEnum(t, a)); })
                .M("copyInto", "([Ljava/lang/Object;)V", (t, b) => { var l = Vec(t, b).L; lock (l) l.CopyTo(((ObjArray)t.R[b + 1]).Data, 0); })
                .M("toString", "()Ljava/lang/String;", (t, b) =>
                {
                    var l = Vec(t, b).L; object[] a; lock (l) a = l.ToArray();
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Rt.Str(t, a[i])); }
                    t.RetA(b, sb.Append(']').ToString());
                }), Rt.F<VectorObj>());

            n.Define("java/util/Stack", "java/util/Vector", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("push", "(Ljava/lang/Object;)Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) l.Add(t.R[b + 1]); t.RetA(b, t.R[b + 1]); })
                .M("pop", "()Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { if (l.Count == 0) throw new JavaThrow("java/util/EmptyStackException"); var v = l[l.Count - 1]; l.RemoveAt(l.Count - 1); t.RetA(b, v); } })
                .M("peek", "()Ljava/lang/Object;", (t, b) => { var l = Vec(t, b).L; lock (l) { if (l.Count == 0) throw new JavaThrow("java/util/EmptyStackException"); t.RetA(b, l[l.Count - 1]); } })
                .M("empty", "()Z", (t, b) => t.RetZ(b, Vec(t, b).L.Count == 0))
                .M("search", "(Ljava/lang/Object;)I", (t, b) => { var l = Vec(t, b).L; lock (l) { int i = LastIndexOf(t, l, t.R[b + 1], l.Count - 1); t.RetI(b, i >= 0 ? l.Count - i : -1); } }));

            n.Define("java/util/Hashtable", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("<init>", "(I)V", (t, b) => { if (t.I(b + 1) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); })
                .M("put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", (t, b) =>
                {
                    var h = (HashtableObj)t.R[b]; object k = t.R[b + 1], v = t.R[b + 2];
                    if (k == null || v == null) throw new NullReferenceException();
                    lock (h.D)
                    {
                        object old;
                        if (h.D.TryGetValue(k, out old)) { h.D[k] = v; t.RetA(b, old); }
                        else { h.D[k] = v; h.Order.Add(k); t.RetA(b, null); }
                    }
                })
                .M("get", "(Ljava/lang/Object;)Ljava/lang/Object;", (t, b) =>
                {
                    var h = (HashtableObj)t.R[b]; var k = t.R[b + 1];
                    if (k == null) throw new NullReferenceException();
                    object v; lock (h.D) h.D.TryGetValue(k, out v); t.RetA(b, v);
                })
                .M("remove", "(Ljava/lang/Object;)Ljava/lang/Object;", (t, b) =>
                {
                    var h = (HashtableObj)t.R[b]; var k = t.R[b + 1];
                    if (k == null) throw new NullReferenceException();
                    lock (h.D)
                    {
                        object v;
                        if (h.D.TryGetValue(k, out v))
                        {
                            h.D.Remove(k);
                            for (int i = 0; i < h.Order.Count; i++) if (JavaKeyComparer.Instance.Equals(h.Order[i], k)) { h.Order.RemoveAt(i); break; }
                        }
                        t.RetA(b, v);
                    }
                })
                .M("containsKey", "(Ljava/lang/Object;)Z", (t, b) => { var h = (HashtableObj)t.R[b]; if (t.R[b + 1] == null) throw new NullReferenceException(); lock (h.D) t.RetZ(b, h.D.ContainsKey(t.R[b + 1])); })
                .M("contains", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, HtContainsValue(t, (HashtableObj)t.R[b], t.R[b + 1])))
                .M("containsValue", "(Ljava/lang/Object;)Z", (t, b) => t.RetZ(b, HtContainsValue(t, (HashtableObj)t.R[b], t.R[b + 1])))
                .M("size", "()I", (t, b) => t.RetI(b, ((HashtableObj)t.R[b]).D.Count))
                .M("isEmpty", "()Z", (t, b) => t.RetZ(b, ((HashtableObj)t.R[b]).D.Count == 0))
                .M("clear", "()V", (t, b) => { var h = (HashtableObj)t.R[b]; lock (h.D) { h.D.Clear(); h.Order.Clear(); } })
                .M("rehash", "()V", (t, b) => { })
                .M("keys", "()Ljava/util/Enumeration;", (t, b) => { var h = (HashtableObj)t.R[b]; object[] a; lock (h.D) a = h.Order.ToArray(); t.RetA(b, MakeEnum(t, a)); })
                .M("elements", "()Ljava/util/Enumeration;", (t, b) =>
                {
                    var h = (HashtableObj)t.R[b]; object[] a;
                    lock (h.D) { a = new object[h.Order.Count]; for (int i = 0; i < a.Length; i++) a[i] = h.D[h.Order[i]]; }
                    t.RetA(b, MakeEnum(t, a));
                })
                .M("toString", "()Ljava/lang/String;", (t, b) =>
                {
                    var h = (HashtableObj)t.R[b];
                    var sb = new StringBuilder("{");
                    lock (h.D)
                        for (int i = 0; i < h.Order.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(Rt.Str(t, h.Order[i])).Append('=').Append(Rt.Str(t, h.D[h.Order[i]]));
                        }
                    t.RetA(b, sb.Append('}').ToString());
                }), Rt.F<HashtableObj>());

            n.Define("java/util/Random", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => ((RandomObj)t.R[b]).SetSeed(Rt.Now() ^ Environment.TickCount * 0x5DEECE66DL ^ Interlocked.Increment(ref randSeq) * 181783497276652981L))
                .M("<init>", "(J)V", (t, b) => ((RandomObj)t.R[b]).SetSeed(t.J(b + 1)))
                .M("setSeed", "(J)V", (t, b) => ((RandomObj)t.R[b]).SetSeed(t.J(b + 1)))
                .M("next", "(I)I", (t, b) => t.RetI(b, ((RandomObj)t.R[b]).Next(t.I(b + 1))))
                .M("nextInt", "()I", (t, b) => t.RetI(b, ((RandomObj)t.R[b]).Next(32)))
                .M("nextInt", "(I)I", (t, b) =>
                {
                    var r = (RandomObj)t.R[b]; int bound = t.I(b + 1);
                    if (bound <= 0) throw new JavaThrow("java/lang/IllegalArgumentException", "n must be positive");
                    if ((bound & -bound) == bound) { t.RetI(b, (int)((bound * (long)r.Next(31)) >> 31)); return; }
                    int bits, val;
                    do { bits = r.Next(31); val = bits % bound; } while (bits - val + (bound - 1) < 0);
                    t.RetI(b, val);
                })
                .M("nextLong", "()J", (t, b) => { var r = (RandomObj)t.R[b]; t.RetJ(b, ((long)r.Next(32) << 32) + r.Next(32)); })
                .M("nextBoolean", "()Z", (t, b) => t.RetZ(b, ((RandomObj)t.R[b]).Next(1) != 0))
                .M("nextFloat", "()F", (t, b) => t.RetF(b, ((RandomObj)t.R[b]).Next(24) / (float)(1 << 24)))
                .M("nextDouble", "()D", (t, b) => { var r = (RandomObj)t.R[b]; t.RetD(b, (((long)r.Next(26) << 27) + r.Next(27)) / (double)(1L << 53)); })
                .M("nextBytes", "([B)V", (t, b) =>
                {
                    var r = (RandomObj)t.R[b]; var a = (sbyte[])t.R[b + 1];
                    for (int i = 0; i < a.Length;)
                        for (int rv = r.Next(32), k = Math.Min(a.Length - i, 4); k-- > 0; rv >>= 8) a[i++] = (sbyte)rv;
                })
                .M("nextGaussian", "()D", (t, b) =>
                {
                    var r = (RandomObj)t.R[b];
                    if (r.HaveGaussian) { r.HaveGaussian = false; t.RetD(b, r.NextGaussian); return; }
                    double v1, v2, s;
                    do
                    {
                        v1 = 2 * ((((long)r.Next(26) << 27) + r.Next(27)) / (double)(1L << 53)) - 1;
                        v2 = 2 * ((((long)r.Next(26) << 27) + r.Next(27)) / (double)(1L << 53)) - 1;
                        s = v1 * v1 + v2 * v2;
                    } while (s >= 1 || s == 0);
                    double m = Math.Sqrt(-2 * Math.Log(s) / s);
                    r.NextGaussian = v2 * m; r.HaveGaussian = true;
                    t.RetD(b, v1 * m);
                }), Rt.F<RandomObj>());

            n.Define("java/util/Date", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => ((DateObj)t.R[b]).Ms = Rt.Now())
                .M("<init>", "(J)V", (t, b) => ((DateObj)t.R[b]).Ms = t.J(b + 1))
                .M("getTime", "()J", (t, b) => t.RetJ(b, ((DateObj)t.R[b]).Ms))
                .M("setTime", "(J)V", (t, b) => ((DateObj)t.R[b]).Ms = t.J(b + 1))
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => { var o = t.R[b + 1] as DateObj; t.RetZ(b, o != null && o.Ms == ((DateObj)t.R[b]).Ms); })
                .M("hashCode", "()I", (t, b) => { long v = ((DateObj)t.R[b]).Ms; t.RetI(b, (int)v ^ (int)(v >> 32)); })
                .M("toString", "()Ljava/lang/String;", (t, b) =>
                {
                    var d = DateTimeOffset.FromUnixTimeMilliseconds(((DateObj)t.R[b]).Ms).ToLocalTime();
                    t.RetA(b, d.ToString("ddd MMM dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " GMT" + d.ToString("zzz") + " " + d.Year);
                }), Rt.F<DateObj>());

            n.Define("java/util/Calendar", "java/lang/Object", c => c
                .S("getInstance", "()Ljava/util/Calendar;", (t, b) => { var k = (CalendarObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Calendar")); k.Ms = Rt.Now(); t.RetA(b, k); })
                .S("getInstance", "(Ljava/util/TimeZone;)Ljava/util/Calendar;", (t, b) =>
                {
                    var k = (CalendarObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Calendar"));
                    k.Ms = Rt.Now();
                    var tz = t.R[b] as TimeZoneObj;
                    if (tz != null) k.Tz = tz.Tz;
                    t.RetA(b, k);
                })
                .M("<init>", "()V", (t, b) => ((CalendarObj)t.R[b]).Ms = Rt.Now())
                .M("get", "(I)I", (t, b) => t.RetI(b, CalGet((CalendarObj)t.R[b], t.I(b + 1))))
                .M("set", "(II)V", (t, b) => CalSet((CalendarObj)t.R[b], t.I(b + 1), t.I(b + 2)))
                .M("getTime", "()Ljava/util/Date;", (t, b) => { var d = (DateObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Date")); d.Ms = ((CalendarObj)t.R[b]).Ms; t.RetA(b, d); })
                .M("setTime", "(Ljava/util/Date;)V", (t, b) => ((CalendarObj)t.R[b]).Ms = ((DateObj)t.R[b + 1]).Ms)
                .M("getTimeInMillis", "()J", (t, b) => t.RetJ(b, ((CalendarObj)t.R[b]).Ms))
                .M("setTimeInMillis", "(J)V", (t, b) => ((CalendarObj)t.R[b]).Ms = t.J(b + 1))
                .M("getTimeZone", "()Ljava/util/TimeZone;", (t, b) => t.RetA(b, MakeTz(t, ((CalendarObj)t.R[b]).Tz)))
                .M("setTimeZone", "(Ljava/util/TimeZone;)V", (t, b) => ((CalendarObj)t.R[b]).Tz = ((TimeZoneObj)t.R[b + 1]).Tz)
                .M("equals", "(Ljava/lang/Object;)Z", (t, b) => { var o = t.R[b + 1] as CalendarObj; t.RetZ(b, o != null && o.Ms == ((CalendarObj)t.R[b]).Ms); })
                .M("before", "(Ljava/lang/Object;)Z", (t, b) => { var o = t.R[b + 1] as CalendarObj; t.RetZ(b, o != null && ((CalendarObj)t.R[b]).Ms < o.Ms); })
                .M("after", "(Ljava/lang/Object;)Z", (t, b) => { var o = t.R[b + 1] as CalendarObj; t.RetZ(b, o != null && ((CalendarObj)t.R[b]).Ms > o.Ms); }),
                Rt.F<CalendarObj>());

            n.Define("java/util/TimeZone", "java/lang/Object", c => c
                .S("getDefault", "()Ljava/util/TimeZone;", (t, b) => t.RetA(b, MakeTz(t, TimeZoneInfo.Local)))
                .S("getTimeZone", "(Ljava/lang/String;)Ljava/util/TimeZone;", (t, b) =>
                {
                    var id = S(t, b);
                    var tz = id == "GMT" || id == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
                    var o = MakeTz(t, tz); o.Id = id; t.RetA(b, o);
                })
                .S("getAvailableIDs", "()[Ljava/lang/String;", (t, b) => t.RetA(b, new ObjArray(t.Vm.LoadClass("[Ljava/lang/String;"), new object[] { "GMT", "UTC" })))
                .M("getID", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((TimeZoneObj)t.R[b]).Id))
                .M("getRawOffset", "()I", (t, b) => t.RetI(b, (int)((TimeZoneObj)t.R[b]).Tz.BaseUtcOffset.TotalMilliseconds))
                .M("useDaylightTime", "()Z", (t, b) => t.RetZ(b, ((TimeZoneObj)t.R[b]).Tz.SupportsDaylightSavingTime))
                .M("getOffset", "(IIIIII)I", (t, b) => t.RetI(b, (int)((TimeZoneObj)t.R[b]).Tz.GetUtcOffset(DateTimeOffset.UtcNow).TotalMilliseconds)),
                Rt.F<TimeZoneObj>());

            n.Define("java/util/Timer", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => StartTimer(t, (TimerObj)t.R[b]))
                .M("<init>", "(Z)V", (t, b) => StartTimer(t, (TimerObj)t.R[b]))
                .M("schedule", "(Ljava/util/TimerTask;J)V", (t, b) => Schedule(t, b, Rt.Now() + Pos(t.J(b + 2)), 0, false))
                .M("schedule", "(Ljava/util/TimerTask;JJ)V", (t, b) => Schedule(t, b, Rt.Now() + Pos(t.J(b + 2)), Period(t.J(b + 4)), false))
                .M("schedule", "(Ljava/util/TimerTask;Ljava/util/Date;)V", (t, b) => Schedule(t, b, ((DateObj)t.R[b + 2]).Ms, 0, false))
                .M("schedule", "(Ljava/util/TimerTask;Ljava/util/Date;J)V", (t, b) => Schedule(t, b, ((DateObj)t.R[b + 2]).Ms, Period(t.J(b + 3)), false))
                .M("scheduleAtFixedRate", "(Ljava/util/TimerTask;JJ)V", (t, b) => Schedule(t, b, Rt.Now() + Pos(t.J(b + 2)), Period(t.J(b + 4)), true))
                .M("scheduleAtFixedRate", "(Ljava/util/TimerTask;Ljava/util/Date;J)V", (t, b) => Schedule(t, b, ((DateObj)t.R[b + 2]).Ms, Period(t.J(b + 3)), true))
                .M("cancel", "()V", (t, b) => { var tm = (TimerObj)t.R[b]; lock (tm) { tm.Cancelled = true; tm.Tasks.Clear(); Monitor.PulseAll(tm); } }),
                Rt.F<TimerObj>());

            n.Define("java/util/TimerTask", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .Abstract("run", "()V")
                .M("cancel", "()Z", (t, b) =>
                {
                    var tt = (TimerTaskObj)t.R[b];
                    bool was = tt.Scheduled && !tt.Cancelled;
                    tt.Cancelled = true;
                    var tm = tt.Timer;
                    if (tm != null) lock (tm) tm.Tasks.Remove(tt);
                    t.RetZ(b, was);
                })
                .M("scheduledExecutionTime", "()J", (t, b) => t.RetJ(b, ((TimerTaskObj)t.R[b]).LastRun)),
                Rt.F<TimerTaskObj>(), new[] { "java/lang/Runnable" }, 0x0401);
        }

        static int randSeq;

        static long Pos(long d) { if (d < 0) throw new JavaThrow("java/lang/IllegalArgumentException", "negative delay"); return d; }
        static long Period(long p) { if (p <= 0) throw new JavaThrow("java/lang/IllegalArgumentException", "non-positive period"); return p; }

        static int IndexOf(JThread t, List<object> l, object o, int from)
        {
            for (int i = Math.Max(0, from); i < l.Count; i++) if (o == null ? l[i] == null : Rt.JEquals(t, o, l[i])) return i;
            return -1;
        }

        static int LastIndexOf(JThread t, List<object> l, object o, int from)
        {
            for (int i = from; i >= 0; i--) if (o == null ? l[i] == null : Rt.JEquals(t, o, l[i])) return i;
            return -1;
        }

        static bool HtContainsValue(JThread t, HashtableObj h, object v)
        {
            if (v == null) throw new NullReferenceException();
            object[] vals;
            lock (h.D) { vals = new object[h.D.Count]; h.D.Values.CopyTo(vals, 0); }
            foreach (var x in vals) if (Rt.JEquals(t, v, x)) return true;
            return false;
        }

        static TimeZoneObj MakeTz(JThread t, TimeZoneInfo tz)
        {
            var o = (TimeZoneObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/TimeZone"));
            o.Tz = tz;
            o.Id = tz == TimeZoneInfo.Utc ? "GMT" : tz.Id;
            return o;
        }

        static DateTime CalDate(CalendarObj c) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(c.Ms), c.Tz).DateTime;

        static int CalGet(CalendarObj c, int f)
        {
            var d = CalDate(c);
            switch (f)
            {
                case 0: return 1;
                case 1: return d.Year;
                case 2: return d.Month - 1;
                case 3: return (d.DayOfYear + 6) / 7;
                case 4: return (d.Day + 6) / 7;
                case 5: return d.Day;
                case 6: return d.DayOfYear;
                case 7: return (int)d.DayOfWeek + 1;
                case 8: return (d.Day - 1) / 7 + 1;
                case 9: return d.Hour >= 12 ? 1 : 0;
                case 10: return d.Hour % 12;
                case 11: return d.Hour;
                case 12: return d.Minute;
                case 13: return d.Second;
                case 14: return d.Millisecond;
                case 15: return (int)c.Tz.BaseUtcOffset.TotalMilliseconds;
                case 16: return c.Tz.IsDaylightSavingTime(d) ? 3600000 : 0;
            }
            throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException", f.ToString());
        }

        static void CalSet(CalendarObj c, int f, int v)
        {
            var d = CalDate(c);
            switch (f)
            {
                case 1: d = d.AddYears(v - d.Year); break;
                case 2: d = d.AddMonths(v - (d.Month - 1)); break;
                case 5: d = d.AddDays(v - d.Day); break;
                case 6: d = d.AddDays(v - d.DayOfYear); break;
                case 7: d = d.AddDays(v - ((int)d.DayOfWeek + 1)); break;
                case 9: d = d.AddHours((v - (d.Hour >= 12 ? 1 : 0)) * 12); break;
                case 10: d = d.AddHours(v - d.Hour % 12); break;
                case 11: d = d.AddHours(v - d.Hour); break;
                case 12: d = d.AddMinutes(v - d.Minute); break;
                case 13: d = d.AddSeconds(v - d.Second); break;
                case 14: d = d.AddMilliseconds(v - d.Millisecond); break;
                default: return;
            }
            var off = c.Tz.GetUtcOffset(d);
            c.Ms = new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), off).ToUnixTimeMilliseconds();
        }

        static void StartTimer(JThread t, TimerObj tm)
        {
            var jt = new JThread(t.Vm, "Timer");
            tm.Thread = jt;
            RunOnNewThread(jt, () => TimerLoop(jt, tm));
        }

        static void Schedule(JThread t, int b, long at, long period, bool fixedRate)
        {
            var tm = (TimerObj)t.R[b];
            var tt = t.R[b + 1] as TimerTaskObj;
            if (tt == null) throw new NullReferenceException();
            if (at < 0) throw new JavaThrow("java/lang/IllegalArgumentException");
            lock (tm)
            {
                if (tm.Cancelled) throw new JavaThrow("java/lang/IllegalStateException", "Timer already cancelled.");
                if (tt.Scheduled || tt.Cancelled) throw new JavaThrow("java/lang/IllegalStateException", "Task already scheduled or cancelled");
                tt.Scheduled = true; tt.Timer = tm; tt.Next = at; tt.Period = period; tt.FixedRate = fixedRate;
                tm.Tasks.Add(tt);
                Monitor.PulseAll(tm);
            }
        }

        static void TimerLoop(JThread jt, TimerObj tm)
        {
            var vm = jt.Vm;
            while (!vm.Halted)
            {
                TimerTaskObj due = null;
                lock (tm)
                {
                    if (tm.Cancelled) return;
                    long now = Rt.Now(), wait = 250;
                    foreach (var x in tm.Tasks)
                    {
                        if (x.Cancelled) continue;
                        if (x.Next <= now) { if (due == null || x.Next < due.Next) due = x; }
                        else wait = Math.Min(wait, x.Next - now);
                    }
                    if (due == null) { Monitor.Wait(tm, (int)Math.Max(1, wait)); continue; }
                    due.LastRun = due.Next;
                    if (due.Period > 0) due.Next = due.FixedRate ? due.Next + due.Period : now + due.Period;
                    else tm.Tasks.Remove(due);
                }
                try { jt.CallVirtual(due, "run()V"); }
                catch (JavaException e)
                {
                    // CLDC kills the Timer here, but on real phones a task that touches
                    // not-yet-initialised state at startup usually loses that race. Keep the
                    // timer alive (like other emulators) so one early exception doesn't freeze the game.
                    if (++tm.Errors <= 3) ReportUncaught(jt, e.Obj);
                }
            }
        }
    }
}
