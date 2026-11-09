using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Lumidlet.Midp;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Lumidlet
{
    [ComImport, System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    sealed class UwpClip : IAudioClip
    {
        readonly MediaPlayer mp;
        public event Action Ended;

        public UwpClip(byte[] data, string mime)
        {
            var ms = new InMemoryRandomAccessStream();
            ms.WriteAsync(data.AsBuffer()).AsTask().Wait();
            ms.Seek(0);
            if (mime == "audio/x-wav" || mime == "audio/wave") mime = "audio/wav";
            if (mime == "audio/mp3") mime = "audio/mpeg";
            mp = new MediaPlayer { AutoPlay = false };
            mp.AudioCategory = MediaPlayerAudioCategory.GameEffects;
            mp.Source = MediaSource.CreateFromStream(ms, mime);
            mp.MediaEnded += (s, e) => Ended?.Invoke();
        }

        public void Play(int loops)
        {
            mp.IsLoopingEnabled = loops < 0;
            mp.Play();
        }

        public void Stop()
        {
            mp.Pause();
            try { mp.PlaybackSession.Position = TimeSpan.Zero; } catch (Exception) { }
        }

        public void SetVolume(int level) => mp.Volume = Math.Max(0, Math.Min(100, level)) / 100.0;
        public void Seek(long ms) { try { mp.PlaybackSession.Position = TimeSpan.FromMilliseconds(ms); } catch (Exception) { } }
        public long DurationMs { get { try { return (long)mp.PlaybackSession.NaturalDuration.TotalMilliseconds; } catch (Exception) { return -1; } } }
        public long PositionMs { get { try { return (long)mp.PlaybackSession.Position.TotalMilliseconds; } catch (Exception) { return 0; } } }
        public void Close() { try { mp.Dispose(); } catch (Exception) { } }
    }

    sealed class SynthOut
    {
        AudioGraph graph;
        public readonly SoftSynth Synth = new SoftSynth(48000);
        float[] mix = new float[0];
        int channels = 2;

        public async Task Init()
        {
            try
            {
                var settings = new AudioGraphSettings(AudioRenderCategory.GameMedia) { QuantumSizeSelectionMode = QuantumSizeSelectionMode.SystemDefault };
                var res = await AudioGraph.CreateAsync(settings);
                if (res.Status != AudioGraphCreationStatus.Success) return;
                var g = res.Graph;
                var outNode = await g.CreateDeviceOutputNodeAsync();
                if (outNode.Status != AudioDeviceNodeCreationStatus.Success) { g.Dispose(); return; }
                var props = g.EncodingProperties;
                channels = (int)props.ChannelCount;
                Synth.Rate = (int)props.SampleRate;
                var inp = g.CreateFrameInputNode(props);
                inp.AddOutgoingConnection(outNode.DeviceOutputNode);
                inp.QuantumStarted += Quantum;
                graph = g;
                g.Start();
            }
            catch (Exception) { }
        }

        unsafe void Quantum(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            int n = args.RequiredSamples;
            if (n <= 0) return;
            int count = n * channels;
            if (mix.Length < count) mix = new float[count];
            Synth.Render(mix, n, channels);
            var frame = new AudioFrame((uint)(count * 4));
            using (var buf = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var r = buf.CreateReference())
            {
                byte* data; uint cap;
                ((IMemoryBufferByteAccess)r).GetBuffer(out data, out cap);
                float* f = (float*)data;
                int lim = Math.Min(count, (int)(cap / 4));
                for (int i = 0; i < lim; i++) f[i] = mix[i];
            }
            sender.AddFrame(frame);
        }

        public void Dispose()
        {
            try { graph?.Stop(); graph?.Dispose(); } catch (Exception) { }
            graph = null;
        }
    }

    sealed class UwpHost : IHost
    {
        readonly GamePage page;
        readonly AppEntry app;
        readonly CoreDispatcher disp;
        readonly object fontLock = new object();
        readonly Dictionary<long, CanvasTextFormat> formats = new Dictionary<long, CanvasTextFormat>();
        volatile bool invalidatePending;
        public readonly SynthOut Synth = new SynthOut();
        readonly string logPath;

        public UwpHost(GamePage p, AppEntry a, CoreDispatcher d)
        {
            page = p; app = a; disp = d;
            logPath = Path.Combine(a.Dir, "log.txt");
            try { if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024) File.Delete(logPath); } catch (Exception) { }
            Log("--- start " + DateTime.Now);
            var _ = Synth.Init();
        }

        public void FrameReady()
        {
            if (invalidatePending) return;
            invalidatePending = true;
            var _ = disp.RunAsync(CoreDispatcherPriority.High, () => { invalidatePending = false; page.InvalidateScreen(); });
        }

        CanvasTextFormat Format(int px, int style)
        {
            long k = px | ((long)style << 32);
            CanvasTextFormat f;
            if (formats.TryGetValue(k, out f)) return f;
            f = new CanvasTextFormat
            {
                FontFamily = "Segoe UI",
                FontSize = px * 0.82f,
                FontWeight = (style & 1) != 0 ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = (style & 2) != 0 ? FontStyle.Italic : FontStyle.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            formats[k] = f;
            return f;
        }

        public void FontMetrics(int px, int style, out int height, out int ascent)
        {
            lock (fontLock)
            {
                var dev = CanvasDevice.GetSharedDevice();
                using (var lay = new CanvasTextLayout(dev, "Ag", Format(px, style), 1000, 1000))
                {
                    var lm = lay.LineMetrics[0];
                    height = (int)Math.Ceiling(lm.Height);
                    ascent = (int)Math.Ceiling(lm.Baseline);
                }
            }
        }

        public Glyph RenderGlyph(char c, int px, int style)
        {
            lock (fontLock)
            {
                try
                {
                    var dev = CanvasDevice.GetSharedDevice();
                    var fmt = Format(px, style);
                    var s = c.ToString();
                    int h, a;
                    FontMetrics(px, style, out h, out a);
                    int adv;
                    using (var lay = new CanvasTextLayout(dev, s, fmt, 1000, 1000))
                        adv = (int)Math.Ceiling(lay.LayoutBoundsIncludingTrailingWhitespace.Width);
                    if (adv <= 0) adv = Math.Max(1, px / 3);
                    int w = adv + 1 + ((style & 2) != 0 ? px / 5 : 0);
                    var g = new Glyph { W = w, H = h, Advance = adv, A = new byte[w * h] };
                    if (char.IsWhiteSpace(c)) return g;
                    using (var rt = new CanvasRenderTarget(dev, w, h, 96))
                    {
                        using (var ds = rt.CreateDrawingSession())
                        {
                            ds.Clear(Colors.Transparent);
                            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
                            ds.DrawText(s, 0, 0, Colors.White, fmt);
                        }
                        var bytes = rt.GetPixelBytes();
                        for (int i = 0; i < w * h; i++) g.A[i] = bytes[i * 4 + 3];
                    }
                    return g;
                }
                catch (Exception e)
                {
                    Log("glyph: " + e.Message);
                    return null;
                }
            }
        }

        public int[] DecodeImage(byte[] data, out int w, out int h)
        {
            w = h = 0;
            try
            {
                var ms = new InMemoryRandomAccessStream();
                ms.WriteAsync(data.AsBuffer()).AsTask().Wait();
                ms.Seek(0);
                var dec = BitmapDecoder.CreateAsync(ms).AsTask().Result;
                var pd = dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask().Result;
                var px = pd.DetachPixelData();
                w = (int)dec.PixelWidth; h = (int)dec.PixelHeight;
                var r = new int[w * h];
                System.Buffer.BlockCopy(px, 0, r, 0, Math.Min(px.Length, r.Length * 4));
                return r;
            }
            catch (Exception e)
            {
                Log("decode: " + e.Message);
                return null;
            }
        }

        public IAudioClip CreateAudio(byte[] data, string mime) => new UwpClip(data, mime);

        public SoftSynth Midi => Synth.Synth;

        public void Vibrate(int ms)
        {
            try
            {
                if (!ApiInformation.IsTypePresent("Windows.Phone.Devices.Notification.VibrationDevice")) return;
                var v = Windows.Phone.Devices.Notification.VibrationDevice.GetDefault();
                if (ms <= 0) v.Cancel();
                else v.Vibrate(TimeSpan.FromMilliseconds(Math.Min(ms, 5000)));
            }
            catch (Exception) { }
        }

        public void RequestText(string title, string text, int maxSize, int constraints, Action<string> done)
        {
            var _ = disp.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                string result = null;
                try
                {
                    var panel = new StackPanel();
                    Control input;
                    Func<string> get;
                    if ((constraints & 0x10000) != 0)
                    {
                        var pb = new PasswordBox { Password = text ?? "", MaxLength = maxSize };
                        input = pb; get = () => pb.Password;
                    }
                    else
                    {
                        var tb = new TextBox { Text = text ?? "", MaxLength = maxSize, AcceptsReturn = (constraints & 0xFFFF) == 0 && maxSize > 64, TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap };
                        var scope = new InputScope();
                        var kind = constraints & 0xFFFF;
                        var name = kind == 2 || kind == 5 ? InputScopeNameValue.Number : kind == 3 ? InputScopeNameValue.TelephoneNumber : kind == 1 ? InputScopeNameValue.EmailSmtpAddress : kind == 4 ? InputScopeNameValue.Url : InputScopeNameValue.Default;
                        scope.Names.Add(new InputScopeName(name));
                        tb.InputScope = scope;
                        tb.SelectionStart = tb.Text.Length;
                        input = tb; get = () => tb.Text;
                    }
                    panel.Children.Add(input);
                    var dlg = new ContentDialog { Title = string.IsNullOrEmpty(title) ? "Input" : title, Content = panel, PrimaryButtonText = "OK", SecondaryButtonText = "Cancel" };
                    dlg.Opened += (s, e) => input.Focus(Windows.UI.Xaml.FocusState.Programmatic);
                    if (await dlg.ShowAsync() == ContentDialogResult.Primary) result = get();
                }
                catch (Exception e) { Log("input: " + e.Message); }
                done(result);
            });
        }

        public bool PlatformRequest(string url)
        {
            var _ = disp.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); } catch (Exception) { }
            });
            return false;
        }

        public void Log(string s)
        {
            System.Diagnostics.Debug.WriteLine(s);
            try { lock (logPath) File.AppendAllText(logPath, s + "\n"); } catch (Exception) { }
        }

        public volatile bool Detached;

        public void Exited(string reason)
        {
            if (Detached) return;
            if (reason != null) Log("exit: " + reason);
            var _ = disp.RunAsync(CoreDispatcherPriority.Normal, () => page.OnMidletExited(reason));
        }

        public string StorageDir => app.Dir;

        public void Dispose() => Synth.Dispose();
    }
}
