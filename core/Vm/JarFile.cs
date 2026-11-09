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
    }
}
