using System;
using System.Collections.Generic;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class DirectGraphicsObj : JObject
    {
        public GraphicsObj G;
        public int Argb = unchecked((int)0xFF000000);
    }

    public sealed class SoundObj : JObject
    {
        public MidpRuntime Rt;
        public byte[] Data;
        public int Freq, State = 3, Gain = 255;
        public long Dur;
        public IAudioClip Clip;
        public MidiSeq Seq;
        public object Listener;
    }

    public static class Nokia
    {
        const string P = "javax/microedition/lcdui/";
        const string N = "com/nokia/mid/ui/";
        const string S = "com/nokia/mid/sound/";
        static MidpRuntime R(JThread t) => MidpRuntime.Of(t);

        public static int Transform(int manip)
        {
            bool m = false;
            int a = 0;
            bool fh = (manip & 0x2000) != 0, fv = (manip & 0x4000) != 0;
            if (fh && fv) a = 180;
            else if (fh) m = true;
            else if (fv) { m = true; a = 180; }
            int rot = manip & 0x1FFF;
            if (rot == 90) a += 270;
            else if (rot == 180) a += 180;
            else if (rot == 270) a += 90;
            a %= 360;
            if (!m) return a == 0 ? 0 : a == 90 ? 5 : a == 180 ? 3 : 6;
            return a == 0 ? 2 : a == 90 ? 7 : a == 180 ? 1 : 4;
        }

        static int Conv(int v, int fmt, bool transp)
        {
            switch (fmt)
            {
                case 888: return v | unchecked((int)0xFF000000);
                case 8888: return transp ? v : v | unchecked((int)0xFF000000);
                case 4444: { int a = transp ? ((v >> 12) & 15) * 17 : 255; return (a << 24) | (((v >> 8) & 15) * 17 << 16) | (((v >> 4) & 15) * 17 << 8) | ((v & 15) * 17); }
                case 444: return unchecked((int)0xFF000000) | (((v >> 8) & 15) * 17 << 16) | (((v >> 4) & 15) * 17 << 8) | ((v & 15) * 17);
                case 565: return unchecked((int)0xFF000000) | (((v >> 11) & 31) * 255 / 31 << 16) | (((v >> 5) & 63) * 255 / 63 << 8) | ((v & 31) * 255 / 31);
                case 555: return unchecked((int)0xFF000000) | (((v >> 10) & 31) * 255 / 31 << 16) | (((v >> 5) & 31) * 255 / 31 << 8) | ((v & 31) * 255 / 31);
                case 1555: { int a = transp ? ((v & 0x8000) != 0 ? 255 : 0) : 255; return (a << 24) | (((v >> 10) & 31) * 255 / 31 << 16) | (((v >> 5) & 31) * 255 / 31 << 8) | ((v & 31) * 255 / 31); }
            }
            throw new JavaThrow("java/lang/IllegalArgumentException", "format " + fmt);
        }

        static int Back(int argb, int fmt)
        {
            int a = (argb >> 24) & 255, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            switch (fmt)
            {
                case 888: return argb & 0xFFFFFF;
                case 8888: return argb;
                case 4444: return ((a >> 4) << 12) | ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                case 444: return ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                case 565: return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
                case 555: return ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                case 1555: return (a >= 128 ? 0x8000 : 0) | ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            }
            throw new JavaThrow("java/lang/IllegalArgumentException", "format " + fmt);
        }

        static void DrawPixels(DirectGraphicsObj dg, int[] px, int w, int h, int x, int y, int manip, bool alpha)
        {
            var s = new Surface(w, h, px, alpha);
            Gfx.DrawRegion(dg.G, s, 0, 0, w, h, Transform(manip), x, y, Gfx.TOP | Gfx.LEFT);
        }

        static DirectGraphicsObj D(JThread t, int b) => (DirectGraphicsObj)t.R[b];

        static void WithColor(DirectGraphicsObj dg, int argb, Action a)
        {
            int old = dg.G.Color;
            dg.G.Color = argb;
            try { a(); } finally { dg.G.Color = old; }
        }

        public static void Register(NativeRegistry n)
        {
            n.Define(N + "FullCanvas", P + "Canvas", c => c
                .M("<init>", "()V", (t, b) => { var cv = (CanvasObj)t.R[b]; cv.NokiaFull = true; cv.FullScreen = true; })
                .M("addCommand", "(L" + P + "Command;)V", (t, b) => { throw new JavaThrow("java/lang/IllegalStateException"); })
                .M("setCommandListener", "(L" + P + "CommandListener;)V", (t, b) => { throw new JavaThrow("java/lang/IllegalStateException"); }),
                null, access: 0x0401);

            n.Interface(N + "DirectGraphics");
            n.Define(N + "DirectUtils", "java/lang/Object", c => c
                .S("getDirectGraphics", "(L" + P + "Graphics;)L" + N + "DirectGraphics;", (t, b) =>
                {
                    var g = (GraphicsObj)t.R[b];
                    if (g == null) throw new NullReferenceException();
                    var dg = (DirectGraphicsObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/DirectGraphics"));
                    dg.G = g;
                    t.RetA(b, dg);
                })
                .S("createImage", "([BII)L" + P + "Image;", (t, b) =>
                {
                    var a = (sbyte[])t.R[b] ?? throw new NullReferenceException();
                    var s = Lcdui.Decode(t, Enc.ToU(a), t.I(b + 1), t.I(b + 2));
                    t.RetA(b, Lcdui.NewImage(t, s, false));
                })
                .S("createImage", "(III)L" + P + "Image;", (t, b) =>
                {
                    int w = t.I(b), h = t.I(b + 1), argb = t.I(b + 2);
                    if (w <= 0 || h <= 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    var s = new Surface(w, h, argb == 0 ? 0 : argb) { HasAlpha = true };
                    if (argb == 0) Array.Clear(s.Pix, 0, s.Pix.Length);
                    t.RetA(b, Lcdui.NewImage(t, s, true));
                }));

            n.Define("lumidlet/DirectGraphics", "java/lang/Object", c => c
                .M("setARGBColor", "(I)V", (t, b) => { D(t, b).Argb = t.I(b + 1); D(t, b).G.Color = t.I(b + 1); })
                .M("getAlphaComponent", "()I", (t, b) => t.RetI(b, (D(t, b).G.Color >> 24) & 255))
                .M("getNativePixelFormat", "()I", (t, b) => t.RetI(b, 8888))
                .M("drawImage", "(L" + P + "Image;IIII)V", (t, b) =>
                {
                    var img = (ImageObj)t.R[b + 1] ?? throw new NullReferenceException();
                    int tr = Transform(t.I(b + 5));
                    Gfx.DrawRegion(D(t, b).G, img.S, 0, 0, img.S.W, img.S.H, tr, t.I(b + 2), t.I(b + 3), t.I(b + 4) == 0 ? 20 : t.I(b + 4));
                })
                .M("drawPixels", "([IZIIIIIIII)V", (t, b) =>
                {
                    var src = (int[])t.R[b + 1] ?? throw new NullReferenceException();
                    bool tr = t.Z(b + 2); int off = t.I(b + 3), scan = t.I(b + 4), x = t.I(b + 5), y = t.I(b + 6), w = t.I(b + 7), h = t.I(b + 8), manip = t.I(b + 9), fmt = t.I(b + 10);
                    if (w <= 0 || h <= 0) return;
                    var px = new int[w * h];
                    for (int r = 0; r < h; r++) for (int k = 0; k < w; k++) px[r * w + k] = Conv(src[off + r * scan + k], fmt, tr);
                    DrawPixels(D(t, b), px, w, h, x, y, manip, tr);
                })
                .M("drawPixels", "([SZIIIIIIII)V", (t, b) =>
                {
                    var src = (short[])t.R[b + 1] ?? throw new NullReferenceException();
                    bool tr = t.Z(b + 2); int off = t.I(b + 3), scan = t.I(b + 4), x = t.I(b + 5), y = t.I(b + 6), w = t.I(b + 7), h = t.I(b + 8), manip = t.I(b + 9), fmt = t.I(b + 10);
                    if (w <= 0 || h <= 0) return;
                    var px = new int[w * h];
                    for (int r = 0; r < h; r++) for (int k = 0; k < w; k++) px[r * w + k] = Conv(src[off + r * scan + k] & 0xFFFF, fmt, tr);
                    DrawPixels(D(t, b), px, w, h, x, y, manip, tr);
                })
                .M("drawPixels", "([B[BIIIIIIII)V", (t, b) =>
                {
                    var src = (sbyte[])t.R[b + 1] ?? throw new NullReferenceException();
                    var mask = (sbyte[])t.R[b + 2];
                    int off = t.I(b + 3), scan = t.I(b + 4), x = t.I(b + 5), y = t.I(b + 6), w = t.I(b + 7), h = t.I(b + 8), manip = t.I(b + 9), fmt = t.I(b + 10);
                    if (w <= 0 || h <= 0) return;
                    var px = new int[w * h];
                    for (int r = 0; r < h; r++)
                        for (int k = 0; k < w; k++)
                        {
                            int v;
                            if (fmt == 332) { int bv = src[off + r * scan + k] & 255; v = unchecked((int)0xFF000000) | (((bv >> 5) & 7) * 255 / 7 << 16) | (((bv >> 2) & 7) * 255 / 7 << 8) | ((bv & 3) * 85); }
                            else if (fmt == 8) { int g2 = src[off + r * scan + k] & 255; v = unchecked((int)0xFF000000) | (g2 << 16) | (g2 << 8) | g2; }
                            else
                            {
                                int bit = (off + r * scan + k);
                                bool on = ((src[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                                v = on ? unchecked((int)0xFF000000) : -1;
                                if (mask != null && ((mask[bit >> 3] >> (7 - (bit & 7))) & 1) == 0) v = 0;
                            }
                            px[r * w + k] = v;
                        }
                    DrawPixels(D(t, b), px, w, h, x, y, manip, mask != null);
                })
                .M("getPixels", "([IIIIIIII)V", (t, b) =>
                {
                    var dst = (int[])t.R[b + 1]; int off = t.I(b + 2), scan = t.I(b + 3), x = t.I(b + 4), y = t.I(b + 5), w = t.I(b + 6), h = t.I(b + 7), fmt = t.I(b + 8);
                    var g = D(t, b).G; var s = g.S;
                    for (int r = 0; r < h; r++) for (int k = 0; k < w; k++)
                        {
                            int sx = x + k + g.Tx, sy = y + r + g.Ty;
                            int v = sx >= 0 && sy >= 0 && sx < s.W && sy < s.H ? s.Pix[sy * s.W + sx] : 0;
                            dst[off + r * scan + k] = Back(v, fmt);
                        }
                })
                .M("getPixels", "([SIIIIIII)V", (t, b) =>
                {
                    var dst = (short[])t.R[b + 1]; int off = t.I(b + 2), scan = t.I(b + 3), x = t.I(b + 4), y = t.I(b + 5), w = t.I(b + 6), h = t.I(b + 7), fmt = t.I(b + 8);
                    var g = D(t, b).G; var s = g.S;
                    for (int r = 0; r < h; r++) for (int k = 0; k < w; k++)
                        {
                            int sx = x + k + g.Tx, sy = y + r + g.Ty;
                            int v = sx >= 0 && sy >= 0 && sx < s.W && sy < s.H ? s.Pix[sy * s.W + sx] : 0;
                            dst[off + r * scan + k] = (short)Back(v, fmt);
                        }
                })
                .M("drawTriangle", "(IIIIIII)V", (t, b) => { var dg = D(t, b); WithColor(dg, t.I(b + 7), () => { Gfx.DrawPolygon(dg.G, new[] { t.I(b + 1), t.I(b + 3), t.I(b + 5) }, 0, new[] { t.I(b + 2), t.I(b + 4), t.I(b + 6) }, 0, 3); }); })
                .M("fillTriangle", "(IIIIIII)V", (t, b) => { var dg = D(t, b); int x1 = t.I(b + 1), y1 = t.I(b + 2), x2 = t.I(b + 3), y2 = t.I(b + 4), x3 = t.I(b + 5), y3 = t.I(b + 6); WithColor(dg, t.I(b + 7), () => Gfx.FillTriangle(dg.G, x1, y1, x2, y2, x3, y3)); })
                .M("drawPolygon", "([II[IIII)V", (t, b) => { var dg = D(t, b); var xs = (int[])t.R[b + 1]; int xo = t.I(b + 2); var ys = (int[])t.R[b + 3]; int yo = t.I(b + 4), np = t.I(b + 5); WithColor(dg, t.I(b + 6), () => Gfx.DrawPolygon(dg.G, xs, xo, ys, yo, np)); })
                .M("fillPolygon", "([II[IIII)V", (t, b) => { var dg = D(t, b); var xs = (int[])t.R[b + 1]; int xo = t.I(b + 2); var ys = (int[])t.R[b + 3]; int yo = t.I(b + 4), np = t.I(b + 5); WithColor(dg, t.I(b + 6), () => Gfx.FillPolygon(dg.G, xs, xo, ys, yo, np)); }),
                Rt.F<DirectGraphicsObj>(), new[] { N + "DirectGraphics" });

            n.Define(N + "DeviceControl", "java/lang/Object", c => c
                .S("setLights", "(II)V", (t, b) => { })
                .S("flashLights", "(J)V", (t, b) => { })
                .S("startVibra", "(IJ)V", (t, b) => { if (t.I(b) < 0 || t.I(b) > 100 || t.J(b + 1) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); if (t.I(b) > 0) R(t).Host.Vibrate((int)Math.Min(5000, t.J(b + 1))); })
                .S("stopVibra", "()V", (t, b) => R(t).Host.Vibrate(0)));

            n.Interface(S + "SoundListener", c => c.Abstract("soundStateChanged", "(L" + S + "Sound;I)V"));
            n.Define(S + "Sound", "java/lang/Object", c => c
                .M("<init>", "([BI)V", (t, b) => SoundInit(t, (SoundObj)t.R[b], (sbyte[])t.R[b + 1], t.I(b + 2)))
                .M("<init>", "(IJ)V", (t, b) => ToneInit(t, (SoundObj)t.R[b], t.I(b + 1), t.J(b + 2)))
                .M("init", "([BI)V", (t, b) => SoundInit(t, (SoundObj)t.R[b], (sbyte[])t.R[b + 1], t.I(b + 2)))
                .M("init", "(IJ)V", (t, b) => ToneInit(t, (SoundObj)t.R[b], t.I(b + 1), t.J(b + 2)))
                .M("play", "(I)V", (t, b) =>
                {
                    var s = (SoundObj)t.R[b]; int loops = t.I(b + 1);
                    if (loops < 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    SoundStop(s);
                    if (s.Seq != null) { s.Seq.LoopsLeft = loops == 0 ? -1 : loops; s.Seq.SetVolume(GainPct(s.Gain)); s.Seq.Start(); }
                    else if (s.Clip != null) { s.Clip.SetVolume(GainPct(s.Gain)); s.Clip.Play(loops == 0 ? -1 : loops); }
                    SoundState(s, 0);
                })
                .M("stop", "()V", (t, b) => { var s = (SoundObj)t.R[b]; SoundStop(s); SoundState(s, 1); })
                .M("resume", "()V", (t, b) => { var s = (SoundObj)t.R[b]; s.Seq?.Start(); s.Clip?.Play(1); SoundState(s, 0); })
                .M("release", "()V", (t, b) => { var s = (SoundObj)t.R[b]; SoundStop(s); s.Seq?.Close(); s.Clip?.Close(); s.Clip = null; s.Seq = null; SoundState(s, 3); })
                .M("getState", "()I", (t, b) => t.RetI(b, ((SoundObj)t.R[b]).State))
                .M("setGain", "(I)V", (t, b) => { var s = (SoundObj)t.R[b]; s.Gain = Math.Max(0, Math.Min(255, t.I(b + 1))); s.Seq?.SetVolume(GainPct(s.Gain)); s.Clip?.SetVolume(GainPct(s.Gain)); })
                .M("getGain", "()I", (t, b) => t.RetI(b, ((SoundObj)t.R[b]).Gain))
                .M("setSoundListener", "(L" + S + "SoundListener;)V", (t, b) => ((SoundObj)t.R[b]).Listener = t.R[b + 1])
                .S("getConcurrentSoundCount", "(I)I", (t, b) => t.RetI(b, 1))
                .S("getSupportedFormats", "()[I", (t, b) => t.RetA(b, new[] { 1, 5 })),
                Rt.F<SoundObj>());
        }

        static int GainPct(int g) => (int)Math.Round(Math.Sqrt(Math.Max(0, Math.Min(255, g)) / 255.0) * 100);

        static void SoundState(SoundObj s, int st)
        {
            if (s.State == st) return;
            s.State = st;
            var l = s.Listener;
            if (l != null) s.Rt.Post(t => t.CallVirtual(l, "soundStateChanged(L" + S + "Sound;I)V", s, st));
        }

        static void SoundStop(SoundObj s)
        {
            s.Seq?.Stop();
            s.Clip?.Stop();
        }

        static void ToneInit(JThread t, SoundObj s, int freq, long dur)
        {
            var rt = R(t);
            s.Seq?.Close();
            s.Rt = rt;
            int note = MidiSeq.FreqToNote(freq);
            var evs = new List<MidiEvent> { new MidiEvent { Us = 0, Status = 0xC0, D1 = 80 } };
            if (note >= 0)
            {
                evs.Add(new MidiEvent { Us = 0, Status = 0x90, D1 = (byte)note, D2 = 110 });
                evs.Add(new MidiEvent { Us = dur * 1000, Status = 0x80, D1 = (byte)note });
            }
            else evs.Add(new MidiEvent { Us = dur * 1000, Status = 0xB0, D1 = 123 });
            s.Seq = new MidiSeq(evs, rt.Config.Sound ? rt.Host.Midi : null);
            s.Seq.Ended = () => SoundState(s, 1);
            s.State = 1;
        }

        static void SoundInit(JThread t, SoundObj s, sbyte[] data, int type)
        {
            var rt = R(t);
            s.Seq?.Close();
            s.Rt = rt;
            if (data == null) throw new NullReferenceException();
            var d = Enc.ToU(data);
            var mime = Media.Sniff(d, null);
            if (mime == "audio/midi")
            {
                var evs = MidiSeq.ParseSmf(d);
                if (evs != null) { s.Seq = new MidiSeq(evs, rt.Config.Sound ? rt.Host.Midi : null); s.Seq.Ended = () => SoundState(s, 1); }
            }
            else if (type == 1)
            {
                var evs = OtaToEvents(d);
                if (evs != null) { s.Seq = new MidiSeq(evs, rt.Config.Sound ? rt.Host.Midi : null); s.Seq.Ended = () => SoundState(s, 1); }
            }
            else if (rt.Config.Sound)
            {
                try
                {
                    s.Clip = rt.Host.CreateAudio(d, mime);
                    if (s.Clip != null) s.Clip.Ended += () => SoundState(s, 1);
                }
                catch (Exception e) { rt.Host.Log("sound: " + e.Message); }
            }
            s.State = 1;
        }

        sealed class Bits
        {
            readonly byte[] d; int p;
            public Bits(byte[] data) { d = data; }
            public bool More => p < d.Length * 8;
            public int Get(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    int bit = p < d.Length * 8 ? (d[p >> 3] >> (7 - (p & 7))) & 1 : 0;
                    p++;
                    v = (v << 1) | bit;
                }
                return v;
            }
        }

        static readonly int[] OtaBpm = { 25, 28, 31, 35, 40, 45, 50, 56, 63, 70, 80, 90, 100, 112, 125, 140, 160, 180, 200, 225, 250, 285, 320, 355, 400, 450, 500, 565, 635, 715, 800, 900 };

        static List<MidiEvent> OtaToEvents(byte[] d)
        {
            try
            {
                var b = new Bits(d);
                var evs = new List<MidiEvent> { new MidiEvent { Us = 0, Status = 0xC0, D1 = 80 } };
                int cmdLen = b.Get(8);
                for (int i = 0; i < cmdLen && b.More; i++)
                {
                    int cmd = b.Get(7);
                    if (cmd == 0x25) continue;
                    if (cmd == 0x1D) { b.Get(1); continue; }
                    if (cmd == 0x1A || cmd == 0x0A) break;
                }
                int songType = b.Get(3);
                if (songType == 1) { int len = b.Get(4); for (int i = 0; i < len; i++) b.Get(8); }
                int patterns = b.Get(8);
                double us = 0;
                int bpm = 63, scale = 1, style = 0;
                for (int pt = 0; pt < patterns && b.More; pt++)
                {
                    b.Get(3); b.Get(2); b.Get(4);
                    int instr = b.Get(8);
                    for (int k = 0; k < instr && b.More; k++)
                    {
                        int id = b.Get(3);
                        switch (id)
                        {
                            case 1:
                                {
                                    int note = b.Get(4), dur = b.Get(3), spec = b.Get(2);
                                    double whole = 60000000.0 * 4 / bpm;
                                    double len = whole / (1 << dur);
                                    if (spec == 1) len *= 1.5;
                                    else if (spec == 2) len *= 1.75;
                                    else if (spec == 3) len *= 2.0 / 3;
                                    if (note > 0 && note <= 12)
                                    {
                                        int midi = 57 + note - 1 + 12 * scale;
                                        evs.Add(new MidiEvent { Us = (long)us, Status = 0x90, D1 = (byte)midi, D2 = 110 });
                                        evs.Add(new MidiEvent { Us = (long)(us + len * (style == 2 ? 1.0 : 0.9)), Status = 0x80, D1 = (byte)midi });
                                    }
                                    us += len;
                                    break;
                                }
                            case 2: scale = b.Get(2); break;
                            case 3: style = b.Get(2); break;
                            case 4: bpm = OtaBpm[b.Get(5)]; break;
                            case 5: b.Get(4); break;
                            default: k = instr; break;
                        }
                    }
                }
                evs.Add(new MidiEvent { Us = (long)us, Status = 0xB0, D1 = 123 });
                return evs;
            }
            catch (Exception) { return null; }
        }
    }
}
