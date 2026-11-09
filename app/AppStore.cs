using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumidlet.Midp;
using Lumidlet.Vm;
using Windows.Storage;

namespace Lumidlet
{
    public sealed class AppEntry
    {
        public string Id, Dir, Name = "", Vendor = "", Version = "", Icon;
        public int Width = 240, Height = 320, Midlet = 1, PadLayout;
        public bool Sound = true, Keypad = true, Smooth, Rotate, DiagCombo;
        public string Encoding = "ISO-8859-1";
        public long Added;
        public List<string> Midlets = new List<string>();

        public string JarPath => Path.Combine(Dir, "app.jar");
        public string JadPath => Path.Combine(Dir, "app.jad");
        public string Subtitle => (Vendor.Length > 0 ? Vendor + " · " : "") + (Version.Length > 0 ? "v" + Version + " · " : "") + Width + "×" + Height;
        public string IconUri => Icon == null ? "ms-appx:///Assets/StoreLogo.png" : "ms-appdata:///local/apps/" + Id + "/" + Icon;

        public Dictionary<string, string> Jad()
        {
            if (!File.Exists(JadPath)) return null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            JarFile.ParseAttributes(File.ReadAllText(JadPath), d);
            return d;
        }

        public MidpConfig Config() => new MidpConfig
        {
            Width = Width, Height = Height, Sound = Sound, MidletIndex = Midlet, Encoding = Encoding,
        };
    }

    public static class AppStore
    {
        public static string Root
        {
            get
            {
                var r = Path.Combine(ApplicationData.Current.LocalFolder.Path, "apps");
                Directory.CreateDirectory(r);
                return r;
            }
        }

        public static readonly int[][] Sizes =
        {
            new[] { 128, 128 }, new[] { 128, 160 }, new[] { 176, 208 }, new[] { 176, 220 }, new[] { 208, 208 },
            new[] { 240, 320 }, new[] { 320, 240 }, new[] { 240, 400 }, new[] { 352, 416 }, new[] { 360, 640 }, new[] { 640, 360 }, new[] { 480, 800 },
        };

        public static List<AppEntry> LoadAll()
        {
            var list = new List<AppEntry>();
            foreach (var d in Directory.GetDirectories(Root))
            {
                var e = Load(Path.GetFileName(d));
                if (e != null) list.Add(e);
            }
            return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static AppEntry Load(string id)
        {
            var dir = Path.Combine(Root, id);
            var meta = Path.Combine(dir, "meta.txt");
            if (!File.Exists(meta) || !File.Exists(Path.Combine(dir, "app.jar"))) return null;
            var e = new AppEntry { Id = id, Dir = dir };
            foreach (var line in File.ReadAllLines(meta))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                var k = line.Substring(0, i);
                var v = line.Substring(i + 1);
                int n;
                switch (k)
                {
                    case "name": e.Name = v; break;
                    case "vendor": e.Vendor = v; break;
                    case "version": e.Version = v; break;
                    case "icon": e.Icon = v.Length > 0 ? v : null; break;
                    case "width": if (int.TryParse(v, out n)) e.Width = n; break;
                    case "height": if (int.TryParse(v, out n)) e.Height = n; break;
                    case "midlet": if (int.TryParse(v, out n)) e.Midlet = n; break;
                    case "sound": e.Sound = v == "1"; break;
                    case "keypad": e.Keypad = v == "1"; break;
                    case "diagcombo": e.DiagCombo = v == "1"; break;
                    case "padlayout": if (int.TryParse(v, out n)) e.PadLayout = n == 1 ? 1 : 0; break;
                    case "smooth": e.Smooth = v == "1"; break;
                    case "rotate": e.Rotate = v == "1"; break;
                    case "encoding": e.Encoding = v; break;
                    case "added": long.TryParse(v, out e.Added); break;
                    case "midlets": e.Midlets = v.Split('|').Where(x => x.Length > 0).ToList(); break;
                }
            }
            return e;
        }

        public static void Save(AppEntry e)
        {
            var sb = new StringBuilder();
            sb.Append("name=").Append(e.Name).Append('\n');
            sb.Append("vendor=").Append(e.Vendor).Append('\n');
            sb.Append("version=").Append(e.Version).Append('\n');
            sb.Append("icon=").Append(e.Icon ?? "").Append('\n');
            sb.Append("width=").Append(e.Width).Append('\n');
            sb.Append("height=").Append(e.Height).Append('\n');
            sb.Append("midlet=").Append(e.Midlet).Append('\n');
            sb.Append("sound=").Append(e.Sound ? 1 : 0).Append('\n');
            sb.Append("keypad=").Append(e.Keypad ? 1 : 0).Append('\n');
            sb.Append("diagcombo=").Append(e.DiagCombo ? 1 : 0).Append('\n');
            sb.Append("padlayout=").Append(e.PadLayout).Append('\n');
            sb.Append("smooth=").Append(e.Smooth ? 1 : 0).Append('\n');
            sb.Append("rotate=").Append(e.Rotate ? 1 : 0).Append('\n');
            sb.Append("encoding=").Append(e.Encoding).Append('\n');
            sb.Append("added=").Append(e.Added).Append('\n');
            sb.Append("midlets=").Append(string.Join("|", e.Midlets.Select(m => m.Replace("|", "/")))).Append('\n');
            File.WriteAllText(Path.Combine(e.Dir, "meta.txt"), sb.ToString());
        }

        public static void Delete(AppEntry e)
        {
            try { Directory.Delete(e.Dir, true); } catch (Exception) { }
        }

        static async Task<byte[]> ReadAll(StorageFile f)
        {
            using (var s = await f.OpenStreamForReadAsync())
            {
                var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                return ms.ToArray();
            }
        }

        public static async Task<List<AppEntry>> Import(IReadOnlyList<IStorageItem> items)
        {
            var files = items.OfType<StorageFile>().ToList();
            var jads = files.Where(f => f.FileType.Equals(".jad", StringComparison.OrdinalIgnoreCase)).ToList();
            var jars = files.Where(f => f.FileType.Equals(".jar", StringComparison.OrdinalIgnoreCase)).ToList();
            var result = new List<AppEntry>();
            foreach (var jar in jars)
            {
                var jad = jads.FirstOrDefault(j => Path.GetFileNameWithoutExtension(j.Name).Equals(Path.GetFileNameWithoutExtension(jar.Name), StringComparison.OrdinalIgnoreCase))
                          ?? (jars.Count == 1 && jads.Count == 1 ? jads[0] : null);
                string jadText = null;
                if (jad != null) jadText = Encoding.UTF8.GetString(await ReadAll(jad));
                var e = await Task.Run(async () => ImportBytes(await ReadAll(jar), jadText, Path.GetFileNameWithoutExtension(jar.Name)));
                result.Add(e);
            }
            return result;
        }

        public static AppEntry ImportBytes(byte[] jarBytes, string jadText, string fallbackName)
        {
            var jar = new JarFile(jarBytes);
            var attrs = new Dictionary<string, string>(jar.Manifest, StringComparer.Ordinal);
            if (jadText != null)
            {
                var jad = new Dictionary<string, string>(StringComparer.Ordinal);
                JarFile.ParseAttributes(jadText, jad);
                foreach (var kv in jad) attrs[kv.Key] = kv.Value;
            }
            string v;
            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            var e = new AppEntry { Id = id, Dir = Path.Combine(Root, id), Added = DateTime.UtcNow.Ticks };
            Directory.CreateDirectory(e.Dir);
            File.WriteAllBytes(e.JarPath, jarBytes);
            if (jadText != null) File.WriteAllText(e.JadPath, jadText);
            e.Name = attrs.TryGetValue("MIDlet-Name", out v) ? v : fallbackName;
            e.Vendor = attrs.TryGetValue("MIDlet-Vendor", out v) ? v : "";
            e.Version = attrs.TryGetValue("MIDlet-Version", out v) ? v : "";
            string icon = attrs.TryGetValue("MIDlet-Icon", out v) ? v : null;
            for (int i = 1; i < 32; i++)
            {
                if (!attrs.TryGetValue("MIDlet-" + i, out v)) break;
                var parts = v.Split(',');
                e.Midlets.Add(parts[0].Trim());
                if (icon == null && parts.Length >= 2 && parts[1].Trim().Length > 0) icon = parts[1].Trim();
            }
            if (icon != null)
            {
                var data = jar.GetResource(icon.Trim().TrimStart('/'));
                if (data != null && Png.IsPng(data, 0, data.Length))
                {
                    File.WriteAllBytes(Path.Combine(e.Dir, "icon.png"), data);
                    e.Icon = "icon.png";
                }
            }
            var size = GuessSize(jar, attrs);
            e.Width = size[0]; e.Height = size[1];
            Save(e);
            return e;
        }

        static int[] GuessSize(JarFile jar, Dictionary<string, string> attrs)
        {
            string v;
            foreach (var key in new[] { "Nokia-MIDlet-Original-Display-Size", "MIDlet-ScreenSize", "Display-Size" })
            {
                if (!attrs.TryGetValue(key, out v)) continue;
                var p = v.Split(',', 'x', 'X', '*');
                int w, h;
                if (p.Length == 2 && int.TryParse(p[0].Trim(), out w) && int.TryParse(p[1].Trim(), out h) && w >= 96 && h >= 96) return new[] { w, h };
            }
            var votes = new Dictionary<int, int>();
            foreach (var name in jar.Names)
            {
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                var d = jar.GetResource(name);
                if (d == null || d.Length < 24 || !Png.IsPng(d, 0, d.Length)) continue;
                int w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
                int h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
                for (int i = 0; i < Sizes.Length; i++)
                    if (w == Sizes[i][0] && h == Sizes[i][1])
                    {
                        int c;
                        votes.TryGetValue(i, out c);
                        votes[i] = c + 1;
                    }
            }
            if (votes.Count > 0)
            {
                var best = votes.OrderByDescending(kv => kv.Value).First();
                if (best.Value >= 2 || votes.Count == 1) return Sizes[best.Key];
            }
            return new[] { 240, 320 };
        }
    }
}
