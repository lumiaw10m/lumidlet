using System;
using System.Collections.Generic;
using System.Threading;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class PlayerObj : JObject
    {
        public MidpRuntime Rt;
        public int State = 100;
        public string Type;
        public byte[] Data;
        public IAudioClip Clip;
        public MidiSeq Seq;
        public int Loops = 1, LoopsLeft;
        public readonly List<object> Listeners = new List<object>();
        public int Volume = 100;
        public bool Muted;
        public JObject VolumeCtl, ToneCtl;
        public bool IsTone;
        public long SilentUs = -1, SilentStart, SilentPos;
        public int SilentGen;
    }

    public sealed class ControlObj : JObject
    {
        public PlayerObj Player;
    }

    public static class Media
    {
        const string P = "javax/microedition/media/";
        const string C = "javax/microedition/media/control/";
        static readonly List<PlayerObj> all = new List<PlayerObj>();
        public static bool Trace;

        static MidpRuntime R(JThread t) => MidpRuntime.Of(t);
        static PlayerObj Pl(JThread t, int b)
        {
            var p = (PlayerObj)t.R[b];
            if (p.State == 0) throw new JavaThrow("java/lang/IllegalStateException", "player closed");
            return p;
        }

        public static void StopAll(MidpRuntime rt)
        {
            PlayerObj[] ps;
            lock (all) ps = all.ToArray();
            foreach (var p in ps)
                if (p.Rt == rt)
                {
                    try { p.Seq?.Close(); p.Clip?.Stop(); p.Clip?.Close(); } catch (Exception) { }
                    lock (all) all.Remove(p);
                }
            try { rt.Host.Midi?.Reset(); } catch (Exception) { }
        }

        public static string Sniff(byte[] d, string declared)
        {
            if (d.Length >= 4 && d[0] == 'M' && d[1] == 'T' && d[2] == 'h' && d[3] == 'd') return "audio/midi";
            if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W') return "audio/x-wav";
            if (d.Length >= 5 && d[0] == '#' && d[1] == '!' && d[2] == 'A' && d[3] == 'M' && d[4] == 'R') return "audio/amr";
            if (d.Length >= 3 && d[0] == 'I' && d[1] == 'D' && d[2] == '3') return "audio/mpeg";
            if (d.Length >= 2 && d[0] == 0xFF && (d[1] & 0xE0) == 0xE0) return "audio/mpeg";
            if (d.Length >= 4 && d[0] == 'O' && d[1] == 'g' && d[2] == 'g') return "audio/ogg";
            if (d.Length >= 8 && d[4] == 'f' && d[5] == 't' && d[6] == 'y' && d[7] == 'p') return "audio/mp4";
            return declared ?? "application/octet-stream";
        }

        static PlayerObj NewPlayer(JThread t, byte[] data, string type)
        {
            var p = (PlayerObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/Player"));
            p.Rt = R(t);
            p.Data = data;
            p.Type = data != null ? Sniff(data, type) : type;
            lock (all) all.Add(p);
            return p;
        }

        static void Event(PlayerObj p, string evt, object data)
        {
            object[] ls;
            lock (p.Listeners) ls = p.Listeners.ToArray();
            if (ls.Length == 0) return;
            p.Rt.Post(t =>
            {
                object d = data;
                if (d is long) { var bx = (BoxObj)t.Vm.NewObject(t.Vm.LoadClass("java/lang/Long")); bx.V.J = (long)d; d = bx; }
                foreach (var l in ls) t.CallVirtual(l, "playerUpdate(L" + P + "Player;Ljava/lang/String;Ljava/lang/Object;)V", p, evt, d);
            });
        }

        static void Realize(JThread t, PlayerObj p)
        {
            if (p.State >= 200) return;
            var rt = p.Rt;
            if (p.IsTone)
            {
                if (p.Seq == null) p.Seq = new MidiSeq(new List<MidiEvent>(), rt.Host.Midi);
            }
            else if (p.Type == "audio/midi" || p.Type == "audio/mid" || p.Type == "audio/x-midi" || p.Type == "audio/sp-midi")
            {
                var evs = MidiSeq.ParseSmf(p.Data);
                if (evs == null) throw new JavaThrow(P + "MediaException", "bad MIDI data");
                p.Seq = new MidiSeq(evs, rt.Config.Sound ? rt.Host.Midi : null);
            }
            else if (rt.Config.Sound)
            {
                try { p.Clip = rt.Host.CreateAudio(p.Data, p.Type); }
                catch (Exception e) { rt.Host.Log("audio: " + e.Message); }
            }
            if (p.Seq != null)
                p.Seq.Ended = () =>
                {
                    p.State = 300;
                    Event(p, "endOfMedia", p.Seq.DurationUs);
                };
            if (p.Clip != null)
                p.Clip.Ended += () =>
                {
                    if (p.LoopsLeft != 0 && --p.LoopsLeft != 0) { p.Clip.Seek(0); p.Clip.Play(1); return; }
                    p.State = 300;
                    Event(p, "endOfMedia", p.Clip.DurationMs * 1000);
                };
            ApplyVolume(p);
            p.State = 200;
        }

        static void ApplyVolume(PlayerObj p)
        {
            int v = p.Muted ? 0 : p.Volume;
            if (p.Seq != null) p.Seq.SetVolume(v);
            p.Clip?.SetVolume(v);
        }

        static void Start(JThread t, PlayerObj p)
        {
            Realize(t, p);
            if (p.State == 400) return;
            p.State = 400;
            if (Trace) p.Rt.Host.Log("player start " + p.Type + " seq=" + (p.Seq != null) + " clip=" + (p.Clip != null));
            if (p.Seq != null) { p.Seq.LoopsLeft = p.Loops; p.Seq.Start(); }
            else if (p.Clip != null) { p.LoopsLeft = p.Loops; p.Clip.Play(1); }
            else { p.LoopsLeft = p.Loops; Silent(p); }
            Event(p, "started", Time(p));
        }

        internal static long EstimateUs(byte[] d)
        {
            if (d == null || d.Length < 12) return 1000000;
            if (d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F')
            {
                long rate = 0, size = 0;
                for (int i = 12; i + 8 <= d.Length;)
                {
                    int len = d[i + 4] | (d[i + 5] << 8) | (d[i + 6] << 16) | (d[i + 7] << 24);
                    if (d[i] == 'f' && d[i + 1] == 'm' && d[i + 2] == 't' && i + 20 <= d.Length) rate = d[i + 16] | (d[i + 17] << 8) | (d[i + 18] << 16) | (d[i + 19] << 24);
                    if (d[i] == 'd' && d[i + 1] == 'a' && d[i + 2] == 't' && d[i + 3] == 'a') { size = Math.Min(len & 0x7FFFFFFF, d.Length - i - 8); break; }
                    if (len < 0) break;
                    i += 8 + len + (len & 1);
                }
                if (rate > 0 && size > 0) return Math.Max(50000, size * 1000000 / rate);
            }
            if (d.Length > 5 && d[0] == '#' && d[1] == '!' && d[2] == 'A') return Math.Max(200000, (d.Length - 6) / 32L * 20000);
            return Math.Max(300000, Math.Min(10000000, d.Length * 8000L / 16));
        }

        static void Silent(PlayerObj p)
        {
            if (p.SilentUs < 0) p.SilentUs = EstimateUs(p.Data);
            int gen = ++p.SilentGen;
            p.SilentStart = Rt.Mono() * 1000 - p.SilentPos;
            long wait = Math.Max(1, (p.SilentUs - p.SilentPos) / 1000);
            System.Threading.Tasks.Task.Delay((int)wait).ContinueWith(_ =>
            {
                if (gen != p.SilentGen || p.State != 400) return;
                p.SilentPos = 0;
                if (p.LoopsLeft < 0 || --p.LoopsLeft > 0) { Silent(p); return; }
                p.State = 300;
                Event(p, "endOfMedia", p.SilentUs);
            });
        }

        static void Stop(PlayerObj p)
        {
            if (p.State != 400) return;
            if (p.Seq == null && p.Clip == null) { p.SilentPos = Math.Min(p.SilentUs, Rt.Mono() * 1000 - p.SilentStart); p.SilentGen++; }
            p.Seq?.Stop();
            p.Clip?.Stop();
            p.State = 300;
            Event(p, "stopped", Time(p));
        }

        static long Time(PlayerObj p)
        {
            if (p.Seq != null) return p.Seq.PositionUs;
            if (p.Clip != null) return p.Clip.PositionMs * 1000;
            if (p.SilentUs >= 0) return p.State == 400 ? Math.Min(p.SilentUs, Rt.Mono() * 1000 - p.SilentStart) : p.SilentPos;
            return 0;
        }

        static long Duration(PlayerObj p)
        {
            if (p.Seq != null) return p.Seq.DurationUs;
            if (p.Clip != null && p.Clip.DurationMs > 0) return p.Clip.DurationMs * 1000;
            if (p.Clip == null && p.Data != null) { if (p.SilentUs < 0) p.SilentUs = EstimateUs(p.Data); return p.SilentUs; }
            return -1;
        }

        static void Close(PlayerObj p)
        {
            if (p.State == 0) return;
            p.Seq?.Close();
            if (p.Clip != null) { p.Clip.Stop(); p.Clip.Close(); p.Clip = null; }
            p.State = 0;
            lock (all) all.Remove(p);
            Event(p, "closed", null);
        }

        static JObject Control(JThread t, PlayerObj p, string name)
        {
            if (name == null) throw new NullReferenceException();
            int dot = name.LastIndexOf('.');
            var shortName = dot >= 0 ? name.Substring(dot + 1) : name;
            if (shortName == "VolumeControl")
            {
                if (p.VolumeCtl == null) { var c = (ControlObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/VolumeControl")); c.Player = p; p.VolumeCtl = c; }
                return p.VolumeCtl;
            }
            if (shortName == "ToneControl" && p.IsTone)
            {
                if (p.ToneCtl == null) { var c = (ControlObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/ToneControl")); c.Player = p; p.ToneCtl = c; }
                return p.ToneCtl;
            }
            return null;
        }

        static void Tone(JThread t, int note, int durMs, int vol)
        {
            var rt = R(t);
            if (note < 0 || note > 127 || durMs <= 0) throw new JavaThrow("java/lang/IllegalArgumentException", "tone");
            var m = rt.Host.Midi;
            if (m == null || !rt.Config.Sound) return;
            vol = Math.Max(0, Math.Min(100, vol));
            long now = m.NowUs;
            m.Send(0, now, 0xCF, 80, 0);
            m.Send(0, now, 0x9F, note, vol * 127 / 100);
            m.Send(0, now + durMs * 1000L, 0x8F, note, 0);
        }

        public static void Register(NativeRegistry n)
        {
            n.Interface(P + "Controllable", c => c.Abstract("getControl", "(Ljava/lang/String;)L" + P + "Control;").Abstract("getControls", "()[L" + P + "Control;"));
            n.Interface(P + "Player", null, new[] { P + "Controllable" });
            n.Interface(P + "PlayerListener", c => c.Abstract("playerUpdate", "(L" + P + "Player;Ljava/lang/String;Ljava/lang/Object;)V"));
            n.Interface(P + "Control");
            n.Interface(C + "VolumeControl", null, new[] { P + "Control" });
            n.Interface(C + "ToneControl", null, new[] { P + "Control" });
            n.Interface(P + "TimeBase", c => c.Abstract("getTime", "()J"));

            n.Define(P + "Manager", "java/lang/Object", c => c
                .S("createPlayer", "(Ljava/io/InputStream;Ljava/lang/String;)L" + P + "Player;", (t, b) =>
                {
                    var s = (InStreamObj)t.R[b];
                    if (s == null) throw new JavaThrow("java/lang/IllegalArgumentException", "stream");
                    var ms = new System.IO.MemoryStream();
                    var buf = new sbyte[8192];
                    int k;
                    while ((k = s.Read(t, buf, 0, buf.Length)) > 0) ms.Write(Enc.ToU(buf, 0, k), 0, k);
                    t.RetA(b, NewPlayer(t, ms.ToArray(), (string)t.R[b + 1]));
                })
                .S("createPlayer", "(Ljava/lang/String;)L" + P + "Player;", (t, b) =>
                {
                    var loc = (string)t.R[b];
                    if (loc == null) throw new JavaThrow("java/lang/IllegalArgumentException", "locator");
                    if (loc == "device://tone") { var p = NewPlayer(t, null, "audio/x-tone-seq"); p.IsTone = true; t.RetA(b, p); return; }
                    if (loc == "device://midi") { var p = NewPlayer(t, null, "audio/midi"); p.IsTone = true; t.RetA(b, p); return; }
                    byte[] data = null;
                    if (loc.StartsWith("resource:")) data = t.Vm.ClassPath.GetResource(loc.Substring(9).TrimStart('/'));
                    else if (loc.StartsWith("http://") || loc.StartsWith("https://")) data = Net.Fetch(loc);
                    else if (!loc.Contains("://")) data = t.Vm.ClassPath.GetResource(loc.TrimStart('/'));
                    if (data == null) throw new JavaThrow(P + "MediaException", "cannot create player for " + loc);
                    t.RetA(b, NewPlayer(t, data, null));
                })
                .S("playTone", "(III)V", (t, b) => Tone(t, t.I(b), t.I(b + 1), t.I(b + 2)))
                .S("getSupportedContentTypes", "(Ljava/lang/String;)[Ljava/lang/String;", (t, b) =>
                    t.RetA(b, new ObjArray(t.Vm.LoadClass("[Ljava/lang/String;"), new object[] { "audio/midi", "audio/x-wav", "audio/mpeg", "audio/amr", "audio/x-tone-seq" })))
                .S("getSupportedProtocols", "(Ljava/lang/String;)[Ljava/lang/String;", (t, b) =>
                    t.RetA(b, new ObjArray(t.Vm.LoadClass("[Ljava/lang/String;"), new object[] { "device", "resource", "http" })))
                .S("getSystemTimeBase", "()L" + P + "TimeBase;", (t, b) => t.RetA(b, new JObject(t.Vm.LoadClass("lumidlet/TimeBase")))));

            n.Define("lumidlet/TimeBase", "java/lang/Object", c => c.M("getTime", "()J", (t, b) => t.RetJ(b, Rt.Mono() * 1000)), null, new[] { P + "TimeBase" });

            n.Define("lumidlet/Player", "java/lang/Object", c => c
                .M("realize", "()V", (t, b) => Realize(t, Pl(t, b)))
                .M("prefetch", "()V", (t, b) => { var p = Pl(t, b); Realize(t, p); if (p.State < 300) p.State = 300; })
                .M("start", "()V", (t, b) => Start(t, Pl(t, b)))
                .M("stop", "()V", (t, b) => Stop(Pl(t, b)))
                .M("deallocate", "()V", (t, b) => { var p = Pl(t, b); Stop(p); if (p.State > 200) p.State = 200; })
                .M("close", "()V", (t, b) => Close((PlayerObj)t.R[b]))
                .M("getState", "()I", (t, b) => t.RetI(b, ((PlayerObj)t.R[b]).State))
                .M("getDuration", "()J", (t, b) => t.RetJ(b, Duration(Pl(t, b))))
                .M("getMediaTime", "()J", (t, b) => t.RetJ(b, Time(Pl(t, b))))
                .M("setMediaTime", "(J)J", (t, b) =>
                {
                    var p = Pl(t, b);
                    if (p.State < 200) throw new JavaThrow("java/lang/IllegalStateException");
                    long us = Math.Max(0, t.J(b + 1));
                    if (p.Seq != null) p.Seq.Seek(us);
                    p.Clip?.Seek(us / 1000);
                    t.RetJ(b, Time(p));
                })
                .M("getContentType", "()Ljava/lang/String;", (t, b) => t.RetA(b, Pl(t, b).Type))
                .M("setLoopCount", "(I)V", (t, b) =>
                {
                    var p = Pl(t, b); int n2 = t.I(b + 1);
                    if (n2 == 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    if (p.State == 400) throw new JavaThrow("java/lang/IllegalStateException");
                    p.Loops = n2;
                })
                .M("addPlayerListener", "(L" + P + "PlayerListener;)V", (t, b) => { var p = Pl(t, b); if (t.R[b + 1] != null) lock (p.Listeners) p.Listeners.Add(t.R[b + 1]); })
                .M("removePlayerListener", "(L" + P + "PlayerListener;)V", (t, b) => { var p = Pl(t, b); lock (p.Listeners) p.Listeners.Remove(t.R[b + 1]); })
                .M("setTimeBase", "(L" + P + "TimeBase;)V", (t, b) => { })
                .M("getTimeBase", "()L" + P + "TimeBase;", (t, b) => t.RetA(b, new JObject(t.Vm.LoadClass("lumidlet/TimeBase"))))
                .M("getControl", "(Ljava/lang/String;)L" + P + "Control;", (t, b) => { var p = Pl(t, b); t.RetA(b, Control(t, p, (string)t.R[b + 1])); })
                .M("getControls", "()[L" + P + "Control;", (t, b) =>
                {
                    var p = Pl(t, b);
                    var l = new List<object> { Control(t, p, "VolumeControl") };
                    if (p.IsTone) l.Add(Control(t, p, "ToneControl"));
                    t.RetA(b, new ObjArray(t.Vm.LoadClass("[L" + P + "Control;"), l.ToArray()));
                }),
                Rt.F<PlayerObj>(), new[] { P + "Player" });

            n.Define("lumidlet/VolumeControl", "java/lang/Object", c => c
                .M("setLevel", "(I)I", (t, b) =>
                {
                    var p = ((ControlObj)t.R[b]).Player;
                    p.Volume = Math.Max(0, Math.Min(100, t.I(b + 1)));
                    ApplyVolume(p);
                    Event(p, "volumeChanged", ((ControlObj)t.R[b]));
                    t.RetI(b, p.Volume);
                })
                .M("getLevel", "()I", (t, b) => t.RetI(b, ((ControlObj)t.R[b]).Player.Volume))
                .M("setMute", "(Z)V", (t, b) => { var p = ((ControlObj)t.R[b]).Player; p.Muted = t.Z(b + 1); ApplyVolume(p); })
                .M("isMuted", "()Z", (t, b) => t.RetZ(b, ((ControlObj)t.R[b]).Player.Muted)),
                Rt.F<ControlObj>(), new[] { C + "VolumeControl" });

            n.Define("lumidlet/ToneControl", "java/lang/Object", c => c
                .M("setSequence", "([B)V", (t, b) =>
                {
                    var p = ((ControlObj)t.R[b]).Player;
                    if (p.State == 400) throw new JavaThrow("java/lang/IllegalStateException");
                    List<MidiEvent> evs;
                    try { evs = MidiSeq.ParseToneSeq((sbyte[])t.R[b + 1]); }
                    catch (Exception) { throw new JavaThrow("java/lang/IllegalArgumentException", "tone sequence"); }
                    var rt = p.Rt;
                    p.Seq?.Close();
                    p.Seq = new MidiSeq(evs, rt.Config.Sound ? rt.Host.Midi : null);
                    p.Seq.Ended = () => { p.State = 300; Event(p, "endOfMedia", p.Seq.DurationUs); };
                    ApplyVolume(p);
                    if (p.State < 200) p.State = 200;
                }),
                Rt.F<ControlObj>(), new[] { C + "ToneControl" });
        }
    }
}
