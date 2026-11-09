using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class RecordStoreObj : JObject
    {
        public string Name, Path;
        public int NextId = 1, Version, OpenCount;
        public long Modified;
        public readonly SortedDictionary<int, byte[]> Recs = new SortedDictionary<int, byte[]>();
        public readonly List<object> Listeners = new List<object>();
    }

    public sealed class RecordEnumObj : JObject
    {
        public RecordStoreObj Store;
        public object Filter, Comparator;
        public bool Keep, Destroyed;
        public int[] Ids = new int[0];
        public int Pos;
    }

    public static class Rms
    {
        const string P = "javax/microedition/rms/";
        static readonly Dictionary<string, RecordStoreObj> open = new Dictionary<string, RecordStoreObj>();

        static string Dir(JThread t)
        {
            var d = System.IO.Path.Combine(MidpRuntime.Of(t).Host.StorageDir, "rms");
            Directory.CreateDirectory(d);
            return d;
        }

        static string FileFor(JThread t, string name)
        {
            var sb = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(name)) sb.Append(b.ToString("x2"));
            return System.IO.Path.Combine(Dir(t), sb + ".rms");
        }

        static string NameOf(string file)
        {
            var hex = System.IO.Path.GetFileNameWithoutExtension(file);
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return Encoding.UTF8.GetString(b, 0, b.Length);
        }

        static void Load(RecordStoreObj rs)
        {
            var d = File.ReadAllBytes(rs.Path);
            using (var r = new BinaryReader(new MemoryStream(d)))
            {
                if (r.ReadInt32() != 0x534D524C) return;
                rs.Version = r.ReadInt32();
                rs.Modified = r.ReadInt64();
                rs.NextId = r.ReadInt32();
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int id = r.ReadInt32(), len = r.ReadInt32();
                    rs.Recs[id] = r.ReadBytes(len);
                }
            }
        }

        static void Save(RecordStoreObj rs)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0x534D524C);
                w.Write(rs.Version);
                w.Write(rs.Modified);
                w.Write(rs.NextId);
                w.Write(rs.Recs.Count);
                foreach (var kv in rs.Recs) { w.Write(kv.Key); w.Write(kv.Value.Length); w.Write(kv.Value); }
            }
            var tmp = rs.Path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            if (File.Exists(rs.Path)) File.Delete(rs.Path);
            File.Move(tmp, rs.Path);
        }

        static RecordStoreObj Rs(JThread t, int b)
        {
            var rs = (RecordStoreObj)t.R[b];
            if (rs.OpenCount <= 0) throw new JavaThrow(P + "RecordStoreNotOpenException", rs.Name);
            return rs;
        }

        static void Touch(JThread t, RecordStoreObj rs, int id, string evt)
        {
            rs.Version++;
            rs.Modified = Rt.Now();
            try { Save(rs); }
            catch (IOException e) { throw new JavaThrow(P + "RecordStoreFullException", e.Message); }
            object[] ls;
            lock (rs.Listeners) ls = rs.Listeners.ToArray();
            foreach (var l in ls) t.CallVirtual(l, evt + "(L" + P + "RecordStore;I)V", rs, id);
        }

        static byte[] Rec(RecordStoreObj rs, int id)
        {
            byte[] d;
            if (!rs.Recs.TryGetValue(id, out d)) throw new JavaThrow(P + "InvalidRecordIDException", id.ToString());
            return d;
        }

        static sbyte[] Bytes(JThread t, object arr, int off, int len)
        {
            if (arr == null) return new sbyte[0];
            var a = (sbyte[])arr;
            if (off < 0 || len < 0 || off + len > a.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
            var r = new sbyte[len];
            Buffer.BlockCopy(a, off, r, 0, len);
            return r;
        }

        static void Rebuild(JThread t, RecordEnumObj e)
        {
            var rs = e.Store;
            List<KeyValuePair<int, byte[]>> recs;
            lock (rs) recs = new List<KeyValuePair<int, byte[]>>(rs.Recs);
            var ids = new List<KeyValuePair<int, sbyte[]>>();
            foreach (var kv in recs)
            {
                var sd = Enc.ToS(kv.Value);
                if (e.Filter != null && t.CallVirtualInt(e.Filter, "matches([B)Z", sd) == 0) continue;
                ids.Add(new KeyValuePair<int, sbyte[]>(kv.Key, sd));
            }
            if (e.Comparator != null)
            {
                var arr = ids.ToArray();
                for (int i = 1; i < arr.Length; i++)
                {
                    var x = arr[i];
                    int j = i - 1;
                    while (j >= 0 && t.CallVirtualInt(e.Comparator, "compare([B[B)I", arr[j].Value, x.Value) == 1) { arr[j + 1] = arr[j]; j--; }
                    arr[j + 1] = x;
                }
                ids = new List<KeyValuePair<int, sbyte[]>>(arr);
            }
            e.Ids = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++) e.Ids[i] = ids[i].Key;
            e.Pos = Math.Min(e.Pos, e.Ids.Length);
        }

        static RecordEnumObj En(JThread t, int b)
        {
            var e = (RecordEnumObj)t.R[b];
            if (e.Destroyed) throw new JavaThrow("java/lang/IllegalStateException", "destroyed");
            if (e.Keep) Rebuild(t, e);
            return e;
        }

        public static void Register(NativeRegistry n)
        {
            n.Interface(P + "RecordFilter", c => c.Abstract("matches", "([B)Z"));
            n.Interface(P + "RecordComparator", c => c.Abstract("compare", "([B[B)I"));
            n.Interface(P + "RecordListener");
            n.Interface(P + "RecordEnumeration");

            n.Define(P + "RecordStore", "java/lang/Object", c => c
                .S("openRecordStore", "(Ljava/lang/String;Z)L" + P + "RecordStore;", (t, b) => t.RetA(b, Open(t, (string)t.R[b], t.Z(b + 1))))
                .S("openRecordStore", "(Ljava/lang/String;ZIZ)L" + P + "RecordStore;", (t, b) => t.RetA(b, Open(t, (string)t.R[b], t.Z(b + 1))))
                .S("openRecordStore", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)L" + P + "RecordStore;", (t, b) => t.RetA(b, Open(t, (string)t.R[b], false)))
                .S("deleteRecordStore", "(Ljava/lang/String;)V", (t, b) =>
                {
                    var name = (string)t.R[b];
                    lock (open)
                    {
                        RecordStoreObj rs;
                        if (open.TryGetValue(name, out rs) && rs.OpenCount > 0) throw new JavaThrow(P + "RecordStoreException", "record store is open");
                        open.Remove(name);
                        var f = FileFor(t, name);
                        if (!File.Exists(f)) throw new JavaThrow(P + "RecordStoreNotFoundException", name);
                        File.Delete(f);
                    }
                })
                .S("listRecordStores", "()[Ljava/lang/String;", (t, b) =>
                {
                    var names = new List<object>();
                    foreach (var f in Directory.GetFiles(Dir(t), "*.rms")) { try { names.Add(NameOf(f)); } catch (Exception) { } }
                    t.RetA(b, names.Count == 0 ? null : new ObjArray(t.Vm.LoadClass("[Ljava/lang/String;"), names.ToArray()));
                })
                .M("closeRecordStore", "()V", (t, b) =>
                {
                    var rs = Rs(t, b);
                    lock (open) { rs.OpenCount--; if (rs.OpenCount == 0) { lock (rs.Listeners) rs.Listeners.Clear(); } }
                })
                .M("getName", "()Ljava/lang/String;", (t, b) => t.RetA(b, Rs(t, b).Name))
                .M("getVersion", "()I", (t, b) => t.RetI(b, Rs(t, b).Version))
                .M("getLastModified", "()J", (t, b) => t.RetJ(b, Rs(t, b).Modified))
                .M("getNumRecords", "()I", (t, b) => { var rs = Rs(t, b); lock (rs) t.RetI(b, rs.Recs.Count); })
                .M("getSize", "()I", (t, b) => { var rs = Rs(t, b); int s = 64; lock (rs) foreach (var v in rs.Recs.Values) s += v.Length + 8; t.RetI(b, s); })
                .M("getSizeAvailable", "()I", (t, b) => t.RetI(b, 4 * 1024 * 1024))
                .M("getNextRecordID", "()I", (t, b) => t.RetI(b, Rs(t, b).NextId))
                .M("addRecord", "([BII)I", (t, b) =>
                {
                    var rs = Rs(t, b);
                    var data = Bytes(t, t.R[b + 1], t.I(b + 2), t.I(b + 3));
                    int id;
                    lock (rs) { id = rs.NextId++; rs.Recs[id] = Enc.ToU(data); }
                    Touch(t, rs, id, "recordAdded");
                    t.RetI(b, id);
                })
                .M("setRecord", "(I[BII)V", (t, b) =>
                {
                    var rs = Rs(t, b); int id = t.I(b + 1);
                    var data = Bytes(t, t.R[b + 2], t.I(b + 3), t.I(b + 4));
                    lock (rs) { Rec(rs, id); rs.Recs[id] = Enc.ToU(data); }
                    Touch(t, rs, id, "recordChanged");
                })
                .M("deleteRecord", "(I)V", (t, b) =>
                {
                    var rs = Rs(t, b); int id = t.I(b + 1);
                    lock (rs) { Rec(rs, id); rs.Recs.Remove(id); }
                    Touch(t, rs, id, "recordDeleted");
                })
                .M("getRecord", "(I)[B", (t, b) =>
                {
                    var rs = Rs(t, b); byte[] d;
                    lock (rs) d = Rec(rs, t.I(b + 1));
                    t.RetA(b, d.Length == 0 ? null : Enc.ToS(d));
                })
                .M("getRecord", "(I[BI)I", (t, b) =>
                {
                    var rs = Rs(t, b); byte[] d;
                    lock (rs) d = Rec(rs, t.I(b + 1));
                    var buf = (sbyte[])t.R[b + 2]; int off = t.I(b + 3);
                    if (buf == null) throw new NullReferenceException();
                    if (off < 0 || off + d.Length > buf.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    Buffer.BlockCopy(d, 0, buf, off, d.Length);
                    t.RetI(b, d.Length);
                })
                .M("getRecordSize", "(I)I", (t, b) => { var rs = Rs(t, b); lock (rs) t.RetI(b, Rec(rs, t.I(b + 1)).Length); })
                .M("addRecordListener", "(L" + P + "RecordListener;)V", (t, b) => { var rs = Rs(t, b); lock (rs.Listeners) if (t.R[b + 1] != null && !rs.Listeners.Contains(t.R[b + 1])) rs.Listeners.Add(t.R[b + 1]); })
                .M("removeRecordListener", "(L" + P + "RecordListener;)V", (t, b) => { var rs = Rs(t, b); lock (rs.Listeners) rs.Listeners.Remove(t.R[b + 1]); })
                .M("setMode", "(IZ)V", (t, b) => Rs(t, b))
                .M("enumerateRecords", "(L" + P + "RecordFilter;L" + P + "RecordComparator;Z)L" + P + "RecordEnumeration;", (t, b) =>
                {
                    var rs = Rs(t, b);
                    var e = (RecordEnumObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/RecordEnum"));
                    e.Store = rs; e.Filter = t.R[b + 1]; e.Comparator = t.R[b + 2]; e.Keep = t.Z(b + 3);
                    Rebuild(t, e);
                    t.RetA(b, e);
                }),
                Rt.F<RecordStoreObj>());

            n.Define("lumidlet/RecordEnum", "java/lang/Object", c => c
                .M("numRecords", "()I", (t, b) => t.RetI(b, En(t, b).Ids.Length))
                .M("hasNextElement", "()Z", (t, b) => { var e = En(t, b); t.RetZ(b, e.Pos < e.Ids.Length); })
                .M("hasPreviousElement", "()Z", (t, b) => { var e = En(t, b); t.RetZ(b, e.Pos > 0); })
                .M("nextRecordId", "()I", (t, b) =>
                {
                    var e = En(t, b);
                    if (e.Pos >= e.Ids.Length) throw new JavaThrow(P + "InvalidRecordIDException");
                    t.RetI(b, e.Ids[e.Pos++]);
                })
                .M("nextRecord", "()[B", (t, b) =>
                {
                    var e = En(t, b);
                    if (e.Pos >= e.Ids.Length) throw new JavaThrow(P + "InvalidRecordIDException");
                    if (e.Store.OpenCount <= 0) throw new JavaThrow(P + "RecordStoreNotOpenException");
                    byte[] d;
                    lock (e.Store) d = Rec(e.Store, e.Ids[e.Pos++]);
                    t.RetA(b, Enc.ToS(d));
                })
                .M("previousRecordId", "()I", (t, b) =>
                {
                    var e = En(t, b);
                    if (e.Pos <= 0) throw new JavaThrow(P + "InvalidRecordIDException");
                    t.RetI(b, e.Ids[--e.Pos]);
                })
                .M("previousRecord", "()[B", (t, b) =>
                {
                    var e = En(t, b);
                    if (e.Pos <= 0) throw new JavaThrow(P + "InvalidRecordIDException");
                    byte[] d;
                    lock (e.Store) d = Rec(e.Store, e.Ids[--e.Pos]);
                    t.RetA(b, Enc.ToS(d));
                })
                .M("reset", "()V", (t, b) => En(t, b).Pos = 0)
                .M("rebuild", "()V", (t, b) => { var e = En(t, b); Rebuild(t, e); })
                .M("keepUpdated", "(Z)V", (t, b) => { var e = En(t, b); e.Keep = t.Z(b + 1); if (e.Keep) Rebuild(t, e); })
                .M("isKeptUpdated", "()Z", (t, b) => t.RetZ(b, En(t, b).Keep))
                .M("destroy", "()V", (t, b) => ((RecordEnumObj)t.R[b]).Destroyed = true),
                Rt.F<RecordEnumObj>(), new[] { P + "RecordEnumeration" });
        }

        static RecordStoreObj Open(JThread t, string name, bool create)
        {
            if (name == null) throw new NullReferenceException();
            if (name.Length == 0 || name.Length > 32) throw new JavaThrow("java/lang/IllegalArgumentException", "name");
            lock (open)
            {
                RecordStoreObj rs;
                if (open.TryGetValue(name, out rs) && rs.Class.Vm == t.Vm)
                {
                    if (rs.OpenCount == 0 && !File.Exists(rs.Path) && !create) throw new JavaThrow(P + "RecordStoreNotFoundException", name);
                    rs.OpenCount++;
                    return rs;
                }
                var path = FileFor(t, name);
                bool exists = File.Exists(path);
                if (!exists && !create) throw new JavaThrow(P + "RecordStoreNotFoundException", name);
                rs = (RecordStoreObj)t.Vm.NewObject(t.Vm.LoadClass(P + "RecordStore"));
                rs.Name = name; rs.Path = path; rs.Modified = Rt.Now();
                if (exists) { try { Load(rs); } catch (Exception e) { t.Vm.Log("rms load failed: " + e.Message); } }
                else Save(rs);
                rs.OpenCount = 1;
                open[name] = rs;
                return rs;
            }
        }
    }
}
