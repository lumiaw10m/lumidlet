using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Lumidlet.Midp;

namespace Lumidlet.Harness
{
    sealed class DesktopHost : IHost
    {
        public int Frames;
        public volatile bool Done;
        public string Reason;
        readonly string dir;
        public DesktopHost(string storage) { dir = storage; Directory.CreateDirectory(dir); }

        public MidpRuntime Rt;

        public void FrameReady()
        {
            lock (this) Frames++;
        }

        static Font MakeFont(int px, int style) =>
            new Font("Segoe UI", px * 0.82f, (FontStyle)(style & 3), GraphicsUnit.Pixel);

        public Glyph RenderGlyph(char c, int px, int style)
        {
            using (var f = MakeFont(px, style))
            using (var bmp0 = new Bitmap(1, 1))
            using (var g0 = Graphics.FromImage(bmp0))
            {
                int h, a;
                FontMetrics(px, style, out h, out a);
                var fmt = StringFormat.GenericTypographic;
                fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                int adv = (int)Math.Ceiling(g0.MeasureString(c.ToString(), f, 1000, fmt).Width);
                if (adv <= 0) adv = px / 3;
                int w = adv + 1;
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(c.ToString(), f, Brushes.White, 0, 0, fmt);
                    var gl = new Glyph { W = w, H = h, Advance = adv, A = new byte[w * h] };
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) gl.A[y * w + x] = bmp.GetPixel(x, y).R;
                    return gl;
                }
            }
        }

        public void FontMetrics(int px, int style, out int height, out int ascent)
        {
            using (var f = MakeFont(px, style))
            {
                var fam = f.FontFamily;
                var st = (FontStyle)(style & 3);
                float em = fam.GetEmHeight(st);
                ascent = (int)Math.Ceiling(f.Size * fam.GetCellAscent(st) / em);
                height = (int)Math.Ceiling(f.Size * (fam.GetCellAscent(st) + fam.GetCellDescent(st)) / em);
            }
        }

        public int[] DecodeImage(byte[] data, out int w, out int h)
        {
            try
            {
                using (var bmp = new Bitmap(new MemoryStream(data)))
                {
                    w = bmp.Width; h = bmp.Height;
                    var r = new int[w * h];
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(bd.Scan0 + y * bd.Stride, r, y * w, w);
                    bmp.UnlockBits(bd);
                    return r;
                }
            }
            catch (Exception) { w = h = 0; return null; }
        }

        public IAudioClip CreateAudio(byte[] data, string mime) { Log("audio clip " + mime + " " + data.Length + " bytes"); return null; }
        public SoftSynth Synth = new SoftSynth(22050);
        public SoftSynth Midi => Synth;
        public double Peak;
        public void StartRecorder(string path)
        {
            var th = new Thread(() =>
            {
                var buf = new float[1102];
                var pcm = new MemoryStream();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long written = 0;
                while (!Done)
                {
                    long want = sw.ElapsedMilliseconds * 22050 / 1000;
                    while (written < want)
                    {
                        Synth.Render(buf, 1102, 1);
                        foreach (var f in buf) { Peak = Math.Max(Peak, Math.Abs(f)); short v = (short)(f * 32000); pcm.WriteByte((byte)v); pcm.WriteByte((byte)(v >> 8)); }
                        written += 1102;
                    }
                    Thread.Sleep(20);
                }
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    w.Write(0x46464952); w.Write((int)(36 + pcm.Length)); w.Write(0x45564157); w.Write(0x20746D66); w.Write(16); w.Write((short)1); w.Write((short)1);
                    w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16); w.Write(0x61746164); w.Write((int)pcm.Length); w.Write(pcm.ToArray());
                }
            }) { IsBackground = true };
            th.Start();
        }
        public void Vibrate(int ms) => Log("vibrate " + ms);
        public void RequestText(string title, string text, int max, int c, Action<string> done) { Log("text input '" + title + "'"); done(text + "!"); }
        public bool PlatformRequest(string url) { Log("platformRequest " + url); return false; }
        public void Log(string s) => Console.WriteLine("[log] " + s);
        public void Exited(string reason) { Reason = reason; Done = true; Console.WriteLine("[exit] " + (reason ?? "normal")); }
        public string StorageDir => dir;

        public void Save(string path)
        {
            if (Rt == null || Interlocked.Read(ref Rt.FrameSeq) == 0) { Console.WriteLine("[shot] no frame yet"); return; }
            int w = Rt.W, h = Rt.H;
            var bytes = new byte[w * h * 4];
            Rt.CopyFrame(bytes);
            var px = new int[w * h];
            Buffer.BlockCopy(bytes, 0, px, 0, bytes.Length);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, bd.Scan0 + y * bd.Stride, w);
                bmp.UnlockBits(bd);
                using (var big = new Bitmap(w * 2, h * 2))
                using (var g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(bmp, 0, 0, w * 2, h * 2);
                    big.Save(path, ImageFormat.Png);
                }
            }
            Console.WriteLine("[shot] " + path);
        }
    }

    static partial class Program
    {
        static int RunMidlet(string[] args)
        {
            if (args.Length < 1) { Console.WriteLine("usage: harness <jar> [--time ms] [--size WxH] [--keys t:code[+hold],...] [--taps t:x:y,...] [--shots dir] [--every ms]"); return 2; }
            string jar = args[0], shots = "shots";
            int time = 3000, every = 0;
            var cfg = new MidpConfig();
            var keys = new List<int[]>();
            var taps = new List<int[]>();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--time": time = int.Parse(args[++i]); break;
                    case "--every": every = int.Parse(args[++i]); break;
                    case "--shots": shots = args[++i]; break;
                    case "--size": { var p = args[++i].Split('x'); cfg.Width = int.Parse(p[0]); cfg.Height = int.Parse(p[1]); break; }
                    case "--keys":
                        foreach (var k in args[++i].Split(','))
                        {
                            var p = k.Split(':');
                            var hold = p[1].Split('+');
                            keys.Add(new[] { int.Parse(p[0]), int.Parse(hold[0]), hold.Length > 1 ? int.Parse(hold[1]) : 80 });
                        }
                        break;
                    case "--taps":
                        foreach (var k in args[++i].Split(','))
                        {
                            var p = k.Split(':');
                            taps.Add(new[] { int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]) });
                        }
                        break;
                }
            }
            Directory.CreateDirectory(shots);
            var host = new DesktopHost(Path.Combine(shots, "storage"));
            var rt = new MidpRuntime(host, File.ReadAllBytes(jar), cfg);
            host.Rt = rt;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Media.Trace = true;
            host.StartRecorder(Path.Combine(shots, "audio.wav"));
            rt.Vm.TraceExceptions = Environment.GetEnvironmentVariable("LUMIDLET_TRACE") == "1";
            rt.Start();
            var events = new List<KeyValuePair<int, Action>>();
            foreach (var k in keys)
            {
                var kk = k;
                events.Add(new KeyValuePair<int, Action>(kk[0], () => rt.KeyDown(kk[1])));
                events.Add(new KeyValuePair<int, Action>(kk[0] + kk[2], () => rt.KeyUp(kk[1])));
            }
            foreach (var tp in taps)
            {
                var p = tp;
                events.Add(new KeyValuePair<int, Action>(p[0], () => rt.PointerDown(p[1], p[2])));
                events.Add(new KeyValuePair<int, Action>(p[0] + 60, () => rt.PointerUp(p[1], p[2])));
            }
            if (every > 0)
                for (int at = every, n = 0; at < time; at += every, n++)
                {
                    var name = Path.Combine(shots, "t" + at.ToString("D6") + ".png");
                    events.Add(new KeyValuePair<int, Action>(at, () => host.Save(name)));
                }
            events.Sort((a, b) => a.Key.CompareTo(b.Key));
            int ei = 0;
            while (sw.ElapsedMilliseconds < time && !host.Done)
            {
                while (ei < events.Count && events[ei].Key <= sw.ElapsedMilliseconds) events[ei++].Value();
                Thread.Sleep(5);
            }
            host.Save(Path.Combine(shots, "final.png"));
            Console.WriteLine("[frames] " + host.Frames + " in " + sw.ElapsedMilliseconds + " ms");
            rt.Stop("harness end");
            Thread.Sleep(300);
            Console.WriteLine("[audio] peak " + host.Peak.ToString("0.00"));
            return 0;
        }
    }
}
