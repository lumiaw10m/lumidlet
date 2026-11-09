using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumidlet.Midp
{
    public struct MidiEvent
    {
        public long Us;
        public byte Status, D1, D2;
    }

    public sealed class MidiSeq
    {
        public readonly List<MidiEvent> Events;
        public readonly long DurationUs;
        readonly SoftSynth synth;
        readonly object gate = new object();
        static readonly System.Diagnostics.Stopwatch fallback = System.Diagnostics.Stopwatch.StartNew();
        static readonly ManualResetEventSlim sleeper = new ManualResetEventSlim(false);
        volatile bool playing;
        int generation, port = -1;
        long startUs, posUs;
        public int Volume = 100;
        public int LoopsLeft = 1;
        public Action Ended;
        const long Lookahead = 120000;

        public MidiSeq(List<MidiEvent> evs, SoftSynth s)
        {
            Events = evs;
            synth = s;
            DurationUs = evs.Count > 0 ? evs[evs.Count - 1].Us : 0;
        }

        public bool Playing => playing;

        long Now() => synth != null ? synth.NowUs : fallback.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;

        public long PositionUs
        {
            get { lock (gate) return playing ? Math.Max(0, Math.Min(DurationUs, Now() - startUs)) : posUs; }
        }

        void Port()
        {
            if (port < 0 && synth != null) { port = synth.OpenPort(); synth.SetPortVolume(port, Volume); }
        }

        public void Seek(long us)
        {
            lock (gate)
            {
                us = Math.Max(0, Math.Min(us, DurationUs));
                posUs = us;
                if (!playing) return;
                generation++;
                if (synth != null) synth.Silence(port, false);
                startUs = Now() - us;
                Launch(generation);
            }
        }

        public void Start()
        {
            lock (gate)
            {
                if (playing) return;
                if (posUs >= DurationUs) posUs = 0;
                playing = true;
                Port();
                startUs = Now() - posUs;
                generation++;
                Launch(generation);
            }
        }

        void Launch(int g) => Task.Factory.StartNew(() => Run(g), TaskCreationOptions.LongRunning);

        public void Stop()
        {
            lock (gate)
            {
                if (!playing) return;
                posUs = Math.Max(0, Math.Min(DurationUs, Now() - startUs));
                playing = false;
                generation++;
            }
            if (synth != null && port >= 0) synth.Silence(port, false);
        }

        public void Close()
        {
            Stop();
            lock (gate)
            {
                if (synth != null && port >= 0) synth.ClosePort(port);
                port = -1;
            }
        }

        public void SetVolume(int v)
        {
            Volume = Math.Max(0, Math.Min(100, v));
            if (synth != null && port >= 0) synth.SetPortVolume(port, Volume);
        }

        bool Alive(int gen)
        {
            lock (gate) return gen == generation;
        }

        void Run(int gen)
        {
            try
            {
                while (true)
                {
                    long origin, from;
                    int p;
                    lock (gate)
                    {
                        if (gen != generation) return;
                        origin = startUs; p = port;
                        from = Now() - origin;
                    }
                    if (synth != null)
                        for (int c = 0; c < 16; c++) { synth.Send(p, 0, 0xB0 | c, 121, 0); synth.Send(p, 0, 0xC0 | c, 0, 0); }
                    int i = 0;
                    while (i < Events.Count && Events[i].Us < from)
                    {
                        var e = Events[i];
                        int hi = e.Status & 0xF0;
                        if (synth != null && (hi == 0xC0 || hi == 0xB0 || hi == 0xE0)) synth.Send(p, 0, e.Status, e.D1, e.D2);
                        i++;
                    }
                    for (; i < Events.Count; i++)
                    {
                        var e = Events[i];
                        long at = origin + e.Us;
                        while (at - Now() > Lookahead)
                        {
                            if (!Alive(gen)) return;
                            sleeper.Wait(10);
                        }
                        if (!Alive(gen)) return;
                        if (synth != null) synth.Send(p, at, e.Status, e.D1, e.D2);
                    }
                    long end = origin + DurationUs;
                    while (Now() < end)
                    {
                        if (!Alive(gen)) return;
                        sleeper.Wait((int)Math.Max(1, Math.Min(20, (end - Now()) / 1000)));
                    }
                    bool again;
                    lock (gate)
                    {
                        if (gen != generation) return;
                        if (LoopsLeft > 0) LoopsLeft--;
                        again = LoopsLeft != 0;
                        if (again) startUs = end;
                        else { playing = false; posUs = 0; generation++; }
                    }
                    if (!again) { Ended?.Invoke(); return; }
                }
            }
            catch (Exception) { playing = false; }
        }

        public static List<MidiEvent> ParseSmf(byte[] d)
        {
            int p = 0;
            Func<int> u32 = () => { int v = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; return v; };
            Func<int> u16 = () => { int v = (d[p] << 8) | d[p + 1]; p += 2; return v; };
            if (d.Length < 14 || d[0] != 'M' || d[1] != 'T' || d[2] != 'h' || d[3] != 'd') return null;
            p = 4;
            int hl = u32();
            int start = p;
            u16();
            int ntr = u16();
            int div = u16();
            p = start + hl;
            var raw = new List<KeyValuePair<long, MidiEvent>>();
            var tempos = new List<KeyValuePair<long, int>>();
            int seq = 0;
            for (int tr = 0; tr < ntr && p + 8 <= d.Length; tr++)
            {
                bool mtrk = d[p] == 'M' && d[p + 1] == 'T' && d[p + 2] == 'r' && d[p + 3] == 'k';
                p += 4;
                int len = u32();
                int end = Math.Min(d.Length, p + len);
                if (!mtrk) { p = end; tr--; continue; }
                long tick = 0;
                int running = 0;
                while (p < end)
                {
                    int dt = 0, b;
                    do { b = d[p++]; dt = (dt << 7) | (b & 0x7F); } while ((b & 0x80) != 0 && p < end);
                    tick += dt;
                    if (p >= end) break;
                    int st = d[p];
                    if (st == 0xFF)
                    {
                        int type = d[p + 1];
                        p += 2;
                        int ml = 0;
                        do { b = d[p++]; ml = (ml << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                        if (type == 0x51 && ml >= 3) tempos.Add(new KeyValuePair<long, int>(tick, (d[p] << 16) | (d[p + 1] << 8) | d[p + 2]));
                        p += ml;
                        if (type == 0x2F) break;
                        continue;
                    }
                    if (st == 0xF0 || st == 0xF7)
                    {
                        p++;
                        int sl = 0;
                        do { b = d[p++]; sl = (sl << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                        p += sl;
                        continue;
                    }
                    if ((st & 0x80) != 0) { running = st; p++; }
                    else if (running == 0) { p++; continue; }
                    int s = running, hi = s & 0xF0;
                    int d1 = d[p++] & 0x7F, d2 = 0;
                    if (hi != 0xC0 && hi != 0xD0) d2 = d[p++] & 0x7F;
                    raw.Add(new KeyValuePair<long, MidiEvent>(tick * 65536 + (seq++ & 0xFFFF), new MidiEvent { Status = (byte)s, D1 = (byte)d1, D2 = (byte)d2 }));
                }
                p = end;
            }
            raw.Sort((a, b2) => a.Key.CompareTo(b2.Key));
            tempos.Sort((a, b2) => a.Key.CompareTo(b2.Key));
            var outp = new List<MidiEvent>(raw.Count);
            bool smpte = (div & 0x8000) != 0;
            double usPerTickSmpte = smpte ? 1000000.0 / ((256 - (div >> 8)) * (div & 0xFF)) : 0;
            int tpq = Math.Max(1, div & 0x7FFF);
            int ti = 0;
            long lastTick = 0;
            double us = 0;
            int tempo = 500000;
            foreach (var kv in raw)
            {
                long tick = kv.Key / 65536;
                while (ti < tempos.Count && tempos[ti].Key <= tick)
                {
                    us += (tempos[ti].Key - lastTick) * (smpte ? usPerTickSmpte : (double)tempo / tpq);
                    lastTick = tempos[ti].Key;
                    tempo = tempos[ti].Value;
                    ti++;
                }
                us += (tick - lastTick) * (smpte ? usPerTickSmpte : (double)tempo / tpq);
                lastTick = tick;
                var e = kv.Value;
                e.Us = (long)us;
                outp.Add(e);
            }
            return outp;
        }

        public static List<MidiEvent> ParseToneSeq(sbyte[] s)
        {
            var evs = new List<MidiEvent>();
            if (s == null || s.Length < 2) throw new ArgumentException("tone sequence");
            int tempo = 120, res = 64, vol = 100;
            var blocks = new Dictionary<int, int>();
            int p = 0;
            if (s[p] == -2) p += 2;
            if (p < s.Length && s[p] == -3) { tempo = (s[p + 1] & 0xFF) * 4; p += 2; }
            if (p < s.Length && s[p] == -4) { res = s[p + 1] & 0xFF; p += 2; }
            double us = 0;
            evs.Add(new MidiEvent { Us = 0, Status = 0xC0, D1 = 80 });
            int depth = 0;
            Func<int, int, int> run = null;
            run = (pos, stopAtEnd) =>
            {
                while (pos < s.Length)
                {
                    int cmd = s[pos];
                    if (cmd == -5) { int id = s[pos + 1]; blocks[id] = pos + 2; pos += 2; while (pos < s.Length && !(s[pos] == -6 && s[pos + 1] == id)) pos += 2; pos += 2; continue; }
                    if (cmd == -6) { if (stopAtEnd == 1) return pos + 2; pos += 2; continue; }
                    if (cmd == -7)
                    {
                        int id = s[pos + 1], bp;
                        if (blocks.TryGetValue(id, out bp) && depth < 8) { depth++; run(bp, 1); depth--; }
                        pos += 2; continue;
                    }
                    if (cmd == -8) { vol = Math.Max(0, Math.Min(100, (int)s[pos + 1])); pos += 2; continue; }
                    if (cmd == -9)
                    {
                        int n = s[pos + 1];
                        int note = s[pos + 2], dur = s[pos + 3];
                        for (int k = 0; k < n; k++) Note(evs, ref us, note, dur, tempo, res, vol);
                        pos += 4; continue;
                    }
                    if (cmd == -3) { tempo = (s[pos + 1] & 0xFF) * 4; pos += 2; continue; }
                    if (cmd == -4) { res = s[pos + 1] & 0xFF; pos += 2; continue; }
                    if (cmd == -2) { pos += 2; continue; }
                    if (pos + 1 >= s.Length) break;
                    Note(evs, ref us, cmd, s[pos + 1], tempo, res, vol);
                    pos += 2;
                }
                return pos;
            };
            run(p, 0);
            evs.Add(new MidiEvent { Us = (long)us, Status = 0xB0, D1 = 123, D2 = 0 });
            return evs;
        }

        static void Note(List<MidiEvent> evs, ref double us, int note, int dur, int tempo, int res, int vol)
        {
            double len = 60000000.0 * 4 / Math.Max(1, tempo) * dur / Math.Max(1, res);
            if (note >= 0)
            {
                evs.Add(new MidiEvent { Us = (long)us, Status = 0x90, D1 = (byte)Math.Min(127, note), D2 = (byte)(vol * 127 / 100) });
                evs.Add(new MidiEvent { Us = (long)(us + len * 0.95), Status = 0x80, D1 = (byte)Math.Min(127, note), D2 = 0 });
            }
            us += len;
        }

        public static int FreqToNote(int hz)
        {
            if (hz <= 0) return -1;
            return Math.Max(0, Math.Min(127, (int)Math.Round(69 + 12 * Math.Log(hz / 440.0, 2))));
        }
    }
}
