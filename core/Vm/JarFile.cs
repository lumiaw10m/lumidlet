using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Lumidlet.Vm
{
    public sealed class JarFile : IClassPath
    {
        readonly Dictionary<string, byte[]> entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        readonly Dictionary<string, string> lower = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public JarFile(byte[] data)
        {
            using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    var name = e.FullName.Replace('\\', '/');
                    try
                    {
                        using (var s = e.Open())
                        {
                            var ms = new MemoryStream();
                            s.CopyTo(ms);
                            entries[name] = ms.ToArray();
                        }
                    }
                    catch (Exception) { continue; }
                    lower[name.ToLowerInvariant()] = name;
                }
            }
            byte[] mf;
            if (entries.TryGetValue("META-INF/MANIFEST.MF", out mf) || TryLower("meta-inf/manifest.mf", out mf))
                ParseAttributes(Encoding.UTF8.GetString(mf, 0, mf.Length), Manifest);
        }

        bool TryLower(string name, out byte[] data)
        {
            string real;
            data = null;
            return lower.TryGetValue(name, out real) && entries.TryGetValue(real, out data);
        }

        public static void ParseAttributes(string text, Dictionary<string, string> into)
        {
            string key = null;
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (raw.StartsWith(" ") && key != null) { into[key] += raw.Substring(1); continue; }
                int c = raw.IndexOf(':');
                if (c <= 0) { key = null; continue; }
                key = raw.Substring(0, c).Trim();
                into[key] = raw.Substring(c + 1).Trim();
            }
            if (into.Count > 0 && text.Length > 0 && text[0] == '﻿')
            {
                var fixedKeys = new List<string>(into.Keys);
                foreach (var k in fixedKeys)
                    if (k.Length > 0 && k[0] == '﻿') { var v = into[k]; into.Remove(k); into[k.Substring(1)] = v; }
            }
        }

        public byte[] GetClass(string name)
        {
            byte[] d;
            if (entries.TryGetValue(name + ".class", out d)) return d;
            return null;
        }

        public byte[] GetResource(string path)
        {
            byte[] d;
            if (entries.TryGetValue(path, out d)) return d;
            if (TryLower(path.ToLowerInvariant(), out d)) return d;
            return null;
        }

        public IEnumerable<string> Names => entries.Keys;

        static readonly string[] foreignPrefixes = { "java/", "javax/", "org/microemu/", "com/barteo/", "org/objectweb/", "nanoxml/" };

        public List<string> FindMidlets()
        {
            var supers = new Dictionary<string, string>(StringComparer.Ordinal);
            var concrete = new List<string>();
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".class")) continue;
                var name = kv.Key.Substring(0, kv.Key.Length - 6);
                bool foreign = false;
                foreach (var p in foreignPrefixes) if (name.StartsWith(p, StringComparison.Ordinal)) { foreign = true; break; }
                if (foreign) continue;
                string self, super;
                int access;
                if (!ReadHeader(kv.Value, out self, out super, out access) || self != name) continue;
                supers[name] = super;
                if ((access & 0x0600) == 0) concrete.Add(name);
            }
            var found = new List<string>();
            foreach (var c in concrete)
            {
                var s = c;
                for (int depth = 0; depth < 16 && s != null; depth++)
                {
                    string next;
                    if (!supers.TryGetValue(s, out next)) { if (s == "javax/microedition/midlet/MIDlet") found.Add(c); break; }
                    if (next == "javax/microedition/midlet/MIDlet") { found.Add(c); break; }
                    s = next;
                }
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        static bool ReadHeader(byte[] d, out string name, out string super, out int access)
        {
            name = super = null; access = 0;
            try
            {
                if (d.Length < 10 || d[0] != 0xCA || d[1] != 0xFE || d[2] != 0xBA || d[3] != 0xBE) return false;
                int n = (d[8] << 8) | d[9], p = 10;
                var utf = new string[n];
                var cls = new int[n];
                for (int i = 1; i < n; i++)
                {
                    int t = d[p];
                    switch (t)
                    {
                        case 1:
                            {
                                int l = (d[p + 1] << 8) | d[p + 2];
                                utf[i] = Encoding.UTF8.GetString(d, p + 3, l);
                                p += 3 + l;
                                break;
                            }
                        case 7: cls[i] = (d[p + 1] << 8) | d[p + 2]; p += 3; break;
                        case 8: case 16: p += 3; break;
                        case 3: case 4: case 9: case 10: case 11: case 12: case 18: p += 5; break;
                        case 5: case 6: p += 9; i++; break;
                        case 15: p += 4; break;
                        default: return false;
                    }
                }
                access = (d[p] << 8) | d[p + 1];
                int thisIdx = (d[p + 2] << 8) | d[p + 3], superIdx = (d[p + 4] << 8) | d[p + 5];
                name = utf[cls[thisIdx]];
                super = superIdx == 0 ? null : utf[cls[superIdx]];
                return name != null;
            }
            catch (Exception) { return false; }
        }
    }
}
