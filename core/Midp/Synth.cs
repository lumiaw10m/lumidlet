using System;
using System.Collections.Generic;

namespace Lumidlet.Midp
{
    public sealed class SoftSynth
    {
        const int Ports = 8;
        const int Block = 32;
        const long DelayUs = 50000;

        sealed class Voice
        {
            public bool On, Released;
            public int Ch, Note, Wave, Stage;
            public double Phase, Inc, Amp, Env, Attack, Decay, Sustain, Release, Drop;
            public uint Noise = 0x12345;
            public long Age;
        }

        struct Ev
        {
            public long At;
            public int Port, Status, D1, D2;
        }

        int rate;
        readonly Voice[] voices = new Voice[32];
        readonly int[] program = new int[Ports * 16];
        readonly double[] vol = new double[Ports * 16], expr = new double[Ports * 16], bend = new double[Ports * 16];
        readonly double[] portVol = new double[Ports];
        readonly bool[] portUsed = new bool[Ports];
        readonly List<Ev> queue = new List<Ev>();
        readonly object gate = new object();
        static readonly float[] sine = new float[4096];
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long age;
        double env, agc = 1;
        public double Master = 0.9;

        static SoftSynth()
        {
            for (int i = 0; i < sine.Length; i++) sine[i] = (float)Math.Sin(i * 2 * Math.PI / sine.Length);
        }

        public SoftSynth(int sampleRate)
        {
            rate = sampleRate;
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice();
            for (int p = 0; p < Ports; p++) portVol[p] = 1;
            Reset();
        }

        public int Rate
        {
            get { return rate; }
            set { lock (gate) rate = Math.Max(8000, value); }
        }

        public long NowUs => clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;

        public void Reset()
        {
            lock (gate)
            {
                for (int c = 0; c < program.Length; c++) ResetChannel(c);
                foreach (var v in voices) v.On = false;
                queue.Clear();
            }
        }

        void ResetChannel(int c)
        {
            program[c] = 0; vol[c] = 100 / 127.0; expr[c] = 1; bend[c] = 1;
        }

        public int OpenPort()
        {
            lock (gate)
            {
                for (int p = 1; p < Ports; p++)
                    if (!portUsed[p])
                    {
                        portUsed[p] = true;
                        portVol[p] = 1;
                        for (int c = 0; c < 16; c++) ResetChannel(p * 16 + c);
                        return p;
                    }
                return 0;
            }
        }

        public void ClosePort(int port)
        {
            lock (gate)
            {
                Silence(port, true);
                if (port > 0) portUsed[port] = false;
            }
        }

        public void Silence(int port, bool hard)
        {
            lock (gate)
            {
                queue.RemoveAll(e => e.Port == port);
                foreach (var v in voices)
                    if (v.On && v.Ch / 16 == port) { if (hard) v.On = false; else v.Released = true; }
            }
        }

        public void SetPortVolume(int port, int level)
        {
            lock (gate) portVol[port] = Math.Max(0, Math.Min(100, level)) / 100.0;
        }

        public void Send(int status, int d1, int d2) => Send(0, 0, status, d1, d2);

        public void Send(int port, long atUs, int status, int d1, int d2)
        {
            if (atUs <= 0) atUs = NowUs;
            lock (gate)
            {
                var e = new Ev { At = atUs, Port = port, Status = status, D1 = d1, D2 = d2 };
                int i = queue.Count;
                while (i > 0 && queue[i - 1].At > atUs) i--;
                queue.Insert(i, e);
            }
        }

        static double Freq(int note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

        void Apply(Ev e)
        {
            int hi = e.Status & 0xF0, ch = e.Port * 16 + (e.Status & 15);
            int d1 = e.D1, d2 = e.D2;
            switch (hi)
            {
                case 0x90:
                    if (d2 == 0) goto case 0x80;
                    NoteOn(ch, d1, d2);
                    break;
                case 0x80:
                    foreach (var v in voices) if (v.On && !v.Released && v.Ch == ch && v.Note == d1) v.Released = true;
                    break;
                case 0xB0:
                    if (d1 == 7) vol[ch] = d2 / 127.0;
                    else if (d1 == 11) expr[ch] = d2 / 127.0;
                    else if (d1 == 120 || d1 == 123) { foreach (var v in voices) if (v.On && v.Ch == ch) { if (d1 == 120) v.On = false; else v.Released = true; } }
                    else if (d1 == 121) { vol[ch] = 100 / 127.0; expr[ch] = 1; bend[ch] = 1; }
                    break;
                case 0xC0: program[ch] = d1; break;
                case 0xE0:
                    bend[ch] = Math.Pow(2, (((d2 << 7) | d1) - 8192) / 8192.0 * 2 / 12.0);
                    foreach (var v in voices) if (v.On && v.Ch == ch && v.Wave < 10) v.Inc = Freq(v.Note) * bend[ch] / rate;
                    break;
            }
        }

        void NoteOn(int ch, int note, int vel)
        {
            Voice b = null;
            foreach (var v in voices) if (!v.On) { b = v; break; }
            if (b == null) { b = voices[0]; foreach (var v in voices) if (v.Age < b.Age) b = v; }
            foreach (var v in voices) if (v.On && v != b && v.Ch == ch && v.Note == note && (ch & 15) != 9) v.Released = true;
            b.On = true; b.Released = false; b.Ch = ch; b.Note = note; b.Phase = 0; b.Env = 0; b.Drop = 0; b.Stage = 0;
            b.Age = ++age;
            b.Amp = vel / 127.0;
            b.Inc = Freq(note) * bend[ch] / rate;
            if ((ch & 15) == 9) { Drum(b, note); return; }
            switch (program[ch] / 8)
            {
                case 0: Set(b, 2, 0.002, 0.9, 0.0, 0.25); break;
                case 1: Set(b, 0, 0.001, 0.5, 0.0, 0.2); break;
                case 2: Set(b, 1, 0.005, 0.1, 0.8, 0.08); b.Amp *= 0.6; break;
                case 3: Set(b, 3, 0.002, 0.6, 0.15, 0.15); break;
                case 4: Set(b, 2, 0.003, 0.4, 0.5, 0.1); break;
                case 5: case 6: Set(b, 3, 0.06, 0.2, 0.75, 0.25); b.Amp *= 0.6; break;
                case 7: Set(b, 3, 0.02, 0.2, 0.7, 0.1); b.Amp *= 0.7; break;
                case 8: case 9: Set(b, 1, 0.01, 0.2, 0.7, 0.1); b.Amp *= 0.6; break;
                case 10: Set(b, 1, 0.005, 0.15, 0.6, 0.08); b.Amp *= 0.55; break;
                case 11: case 12: Set(b, 2, 0.15, 0.4, 0.7, 0.4); break;
                default: Set(b, 0, 0.005, 0.3, 0.3, 0.15); break;
            }
        }

        void Set(Voice v, int wave, double a, double d, double s, double r)
        {
            v.Wave = wave;
            v.Attack = 1.0 / Math.Max(1, a * rate);
            v.Decay = 1.0 / Math.Max(1, d * rate);
            v.Sustain = s;
            v.Release = 1.0 / Math.Max(1, r * rate);
        }

        void Drum(Voice v, int note)
        {
            switch (note)
            {
                case 35: case 36:
                    Set(v, 10, 0.001, 0.18, 0, 0.05); v.Inc = 110.0 / rate; v.Drop = Math.Pow(0.99985, 44100.0 / rate); v.Amp *= 1.4; break;
                case 41: case 43: case 45: case 47: case 48: case 50:
                    Set(v, 10, 0.001, 0.22, 0, 0.08); v.Inc = (80 + (note - 41) * 18) / (double)rate; v.Drop = Math.Pow(0.99993, 44100.0 / rate); break;
                case 42: case 44: case 46: case 49: case 51: case 52: case 53: case 55: case 57: case 59:
                    Set(v, 11, 0.001, note == 46 || note >= 49 ? 0.35 : 0.06, 0, 0.05); v.Amp *= 0.35; break;
                default:
                    Set(v, 12, 0.001, 0.14, 0, 0.05); v.Inc = 180.0 / rate; v.Amp *= 0.8; break;
            }
        }

        public void Render(float[] buf, int frames, int channels)
        {
            Array.Clear(buf, 0, frames * channels);
            lock (gate)
            {
                long t0 = NowUs - DelayUs;
                for (int start = 0; start < frames; start += Block)
                {
                    int n = Math.Min(Block, frames - start);
                    long blockTime = t0 + (long)start * 1000000 / rate;
                    while (queue.Count > 0 && queue[0].At <= blockTime)
                    {
                        Apply(queue[0]);
                        queue.RemoveAt(0);
                    }
                    foreach (var v in voices) if (v.On) RenderVoice(v, buf, start, n, channels);
                }
            }
            int total = frames * channels;
            float peak = 0;
            for (int i = 0; i < total; i++) { float a = buf[i] < 0 ? -buf[i] : buf[i]; if (a > peak) peak = a; }
            double fall = Math.Pow(0.99997, frames);
            env = Math.Max(peak, env * fall);
            double target = Math.Max(0.6, Math.Min(3.0, 0.55 / Math.Max(env, 0.02)));
            double g0 = agc;
            if (target < agc) g0 = agc = target;
            else if (peak > 0.002) agc += (target - agc) * (1 - Math.Pow(0.9999, frames));
            for (int i = 0; i < total; i++)
            {
                double g = g0 + (agc - g0) * i / total;
                double x = buf[i] * g;
                buf[i] = (float)(x > 0.8 ? 0.8 + 0.2 * Math.Tanh((x - 0.8) / 0.2) : x < -0.8 ? -0.8 + 0.2 * Math.Tanh((x + 0.8) / 0.2) : x);
            }
        }

        void RenderVoice(Voice v, float[] buf, int start, int n, int channels)
        {
            int ch = v.Ch;
            double gain = v.Amp * vol[ch] * expr[ch] * portVol[ch / 16] * 0.16 * Master;
            for (int i = start; i < start + n; i++)
            {
                if (v.Released) { v.Env -= v.Release; if (v.Env <= 0) { v.On = false; return; } }
                else if (v.Stage == 0) { v.Env += v.Attack; if (v.Env >= 1) { v.Env = 1; v.Stage = 1; } }
                else if (v.Stage == 1)
                {
                    v.Env -= v.Decay;
                    if (v.Env <= v.Sustain) { v.Env = v.Sustain; v.Stage = 2; if (v.Sustain <= 0) { v.On = false; return; } }
                }
                double ph = v.Phase, s;
                switch (v.Wave)
                {
                    case 0: s = sine[(int)(ph * 4096) & 4095]; break;
                    case 1: s = ph < 0.5 ? 0.8 : -0.8; break;
                    case 2: s = ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph; break;
                    case 3: s = (2 * ph - 1) * 0.8; break;
                    case 10: s = sine[(int)(ph * 4096) & 4095]; v.Inc *= v.Drop; break;
                    case 11: v.Noise ^= v.Noise << 13; v.Noise ^= v.Noise >> 17; v.Noise ^= v.Noise << 5; s = (v.Noise & 0xFFFF) / 32768.0 - 1; break;
                    default:
                        v.Noise ^= v.Noise << 13; v.Noise ^= v.Noise >> 17; v.Noise ^= v.Noise << 5;
                        s = 0.6 * ((v.Noise & 0xFFFF) / 32768.0 - 1) + 0.4 * sine[(int)(ph * 4096) & 4095]; break;
                }
                ph += v.Inc;
                v.Phase = ph >= 1 ? ph - Math.Floor(ph) : ph;
                float o = (float)(s * v.Env * gain);
                int k = i * channels;
                for (int c = 0; c < channels; c++) buf[k + c] += o;
            }
        }
    }
}
