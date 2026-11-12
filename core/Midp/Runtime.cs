using System;
using System.Collections.Generic;
using System.Threading;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class MidpConfig
    {
        public int Width = 240, Height = 320;
        public string Platform = "Nokia6300/07.21";
        public string Encoding = "ISO-8859-1";
        public string Locale = "en-US";
        public bool Sound = true;
        public int MidletIndex = 1;
        public bool KeyRepeat = true;
    }

    public class DisplayableObj : JObject
    {
        public readonly List<CommandObj> Commands = new List<CommandObj>();
        public object Listener;
        public string Title;
        public TickerObj Ticker;
        public bool FullScreen;
        public bool Shown;
        public int LastW, LastH;
    }

    public sealed class CommandObj : JObject
    {
        public string Label, LongLabel;
        public int Type, Priority;
    }

    public class CanvasObj : DisplayableObj
    {
        public Surface Back;
        public int Latched;
        public bool Suppress, IsGame, NokiaFull;
    }

    public sealed class TickerObj : JObject { public string Text; }

    public static class Keys
    {
        public const int Up = -1, Down = -2, Left = -3, Right = -4, Fire = -5, SoftL = -6, SoftR = -7, Clear = -8;
        public const int GUp = 1, GDown = 6, GLeft = 2, GRight = 5, GFire = 8, GA = 9, GB = 10, GC = 11, GD = 12;

        public static int GameAction(int key)
        {
            switch (key)
            {
                case Up: case '2': return GUp;
                case Down: case '8': return GDown;
                case Left: case '4': return GLeft;
                case Right: case '6': return GRight;
                case Fire: case '5': return GFire;
                case '1': return GA;
                case '3': return GB;
                case '7': return GC;
                case '9': return GD;
            }
            return 0;
        }

        public static int KeyCode(int action)
        {
            switch (action)
            {
                case GUp: return Up;
                case GDown: return Down;
                case GLeft: return Left;
                case GRight: return Right;
                case GFire: return Fire;
                case GA: return '1';
                case GB: return '3';
                case GC: return '7';
                case GD: return '9';
            }
            throw new JavaThrow("java/lang/IllegalArgumentException", "game action " + action);
        }

        public static string Name(int key)
        {
            switch (key)
            {
                case Up: return "Up";
                case Down: return "Down";
                case Left: return "Left";
                case Right: return "Right";
                case Fire: return "Select";
                case SoftL: return "Left soft";
                case SoftR: return "Right soft";
                case Clear: return "Clear";
                case '*': return "*";
                case '#': return "#";
            }
            if (key >= '0' && key <= '9') return ((char)key).ToString();
            if (key > 32) return ((char)key).ToString();
            throw new JavaThrow("java/lang/IllegalArgumentException", "key " + key);
        }
    }

    public sealed partial class MidpRuntime
    {
        public readonly JVm Vm;
        public readonly IHost Host;
        public readonly JarFile Jar;
        public readonly MidpConfig Config;
        public readonly Dictionary<string, string> Attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly int W, H;
        public readonly Surface Screen;
        readonly int[] output;
        public readonly FontCache Fonts;
        public readonly object ScreenLock = new object();
        public JThread Ev;
        public volatile DisplayableObj Current;
        public JObject Midlet, Display;
        public FontObj SysFont, SysBold, SysSmall;
        public volatile bool Paused;
        public string ExitReason;
        public int BarH;

        readonly Queue<Action<JThread>> queue = new Queue<Action<JThread>>();
        readonly object qlock = new object();
        bool repaintPending;
        int rx0, ry0, rx1, ry1;
        long paintSeq, paintDone;
        int keyHeld, heldCode;
        long heldSince, lastRepeat;
        volatile int gameHeld, gameLatched;
        public SoftMenu Menu;
        public DisplayableObj PrevDisplayable;
        public readonly List<SocketConnObj> Sockets = new List<SocketConnObj>();

        public static MidpRuntime Of(JThread t) => (MidpRuntime)t.Vm.Runtime;

        public MidpRuntime(IHost host, byte[] jar, MidpConfig cfg, Dictionary<string, string> jad = null)
        {
            Host = host;
            Config = cfg ?? new MidpConfig();
            Jar = new JarFile(jar);
            foreach (var kv in Jar.Manifest) Attrs[kv.Key] = kv.Value;
            if (jad != null) foreach (var kv in jad) Attrs[kv.Key] = kv.Value;
            W = Config.Width; H = Config.Height;
            Screen = new Surface(W, H, unchecked((int)0xFFFFFFFF));
            output = new int[W * H];
            Fonts = new FontCache(host);
            Vm = new JVm(Jar) { Runtime = this, Log = host.Log };
            Enc.Default = Config.Encoding;
            Library.Register(Vm);
            Lcdui.Register(Vm.Natives);
            Screens.Register(Vm.Natives);
            Game.Register(Vm.Natives);
            Rms.Register(Vm.Natives);
            Media.Register(Vm.Natives);
            Nokia.Register(Vm.Natives);
            Net.Register(Vm.Natives);
            Lumidlet.M3g.Natives.Register(Vm.Natives);
            MidletLib.Register(Vm.Natives);
            var p = Vm.Properties;
            p["microedition.platform"] = Config.Platform;
            p["microedition.configuration"] = "CLDC-1.1";
            p["microedition.profiles"] = "MIDP-2.0";
            p["microedition.locale"] = Config.Locale;
            p["microedition.encoding"] = Config.Encoding;
            p["microedition.media.version"] = "1.1";
            p["microedition.commports"] = "";
            p["microedition.hostname"] = "localhost";
            p["supports.mixing"] = "true";
            p["supports.audio.capture"] = "false";
            p["supports.video.capture"] = "false";
            p["supports.recording"] = "false";
            p["audio.encodings"] = "";
            p["video.encodings"] = "";
            p["streamable.contents"] = "";
            p["com.nokia.mid.ui.version"] = "1.1";
            p["com.nokia.keyboard.type"] = "PhoneKeypad";
            Library.OnExit = t => Of(t).Stop("System.exit");
        }

        public string AppProperty(string key)
        {
            string v;
            return key != null && Attrs.TryGetValue(key, out v) ? v : null;
        }

        public string MidletClass(int index, out string name)
        {
            name = null;
            var v = AppProperty("MIDlet-" + index);
            if (v == null) return null;
            var parts = v.Split(',');
            name = parts[0].Trim();
            return parts.Length >= 3 ? parts[2].Trim() : parts[parts.Length - 1].Trim();
        }

        public FontObj MakeFont(int face, int style, int size)
        {
            var f = new FontObj { Face = face, Style = style, Size = size, Cache = Fonts };
            float s = Math.Min(W, H) / 240f;
            if (s < 0.6f) s = 0.6f;
            int px = size == 8 ? 13 : size == 16 ? 19 : 15;
            f.Px = Math.Max(8, (int)Math.Round(px * s));
            int h, a;
            Host.FontMetrics(f.Px, style & 3, out h, out a);
            f.Height = h; f.Ascent = a;
            return f;
        }

        public FontObj JavaFont(JThread t, int face, int style, int size)
        {
            var f = (FontObj)Vm.NewObject(Vm.LoadClass("javax/microedition/lcdui/Font"));
            var m = MakeFont(face, style, size);
            f.Face = face; f.Style = style; f.Size = size; f.Px = m.Px; f.Height = m.Height; f.Ascent = m.Ascent; f.Cache = Fonts;
            return f;
        }

        public void Start()
        {
            Vm.Boot();
            NativeSync.EnsureHires();
            Host.Log("timer: " + NativeSync.Hires);
            SysFont = MakeFont(0, 0, 0);
            SysBold = MakeFont(0, 1, 0);
            SysSmall = MakeFont(0, 0, 8);
            BarH = SysSmall.Height + 4;
            Ev = new JThread(Vm, "event");
            Library.RunOnNewThread(Ev, () => EventMain(Ev));
        }

        void EventMain(JThread t)
        {
            string name;
            var cls = MidletClass(Config.MidletIndex, out name);
            if (cls == null)
            {
                var found = Jar.FindMidlets();
                if (found.Count > 0) cls = found[0];
            }
            if (cls == null) { Stop("No MIDlet found in this jar"); return; }
            try
            {
                var c = Vm.LoadClass(cls.Replace('.', '/'));
                Vm.EnsureInit(t, c);
                var m = Vm.NewObject(c);
                Midlet = m;
                var init = c.Declared("<init>()V") ?? c.FindMethod("<init>()V");
                t.Call(init, m);
                t.CallVirtual(m, "startApp()V");
            }
            catch (JavaException e)
            {
                var th = e.Obj as ThrowableObj;
                Stop("MIDlet failed to start: " + (th != null ? th.Describe() : e.Message));
                return;
            }
            catch (VmHalt) { return; }
            Loop(t);
        }

        public void Post(Action<JThread> a)
        {
            lock (qlock) { queue.Enqueue(a); Monitor.PulseAll(qlock); }
        }

        void Loop(JThread t)
        {
            while (!Vm.Halted)
            {
                Action<JThread> a = null;
                bool paint = false;
                lock (qlock)
                {
                    if (queue.Count == 0 && !repaintPending) Monitor.Wait(qlock, 30);
                    paint = repaintPending;
                    if (queue.Count > 0) a = queue.Dequeue();
                }
                try
                {
                    // MIDP: pending repaints are serviced before serial calls, otherwise a
                    // run(){...; repaint(); callSerially(this);} game loop never gets painted.
                    if (paint) DoPaint(t);
                    if (a != null) a(t);
                    Tick(t);
                }
                catch (JavaException e) { Library.ReportUncaught(t, e.Obj); }
                catch (VmHalt) { return; }
                catch (Exception e) { Host.Log("event error: " + e); }
            }
        }

        void Tick(JThread t)
        {
            long now = Rt.Mono();
            if (Config.KeyRepeat && keyHeld != 0 && now - heldSince > 450 && now - lastRepeat > 90)
            {
                lastRepeat = now;
                var c = Current as CanvasObj;
                if (c != null && Menu == null) t.CallVirtual(c, "keyRepeated(I)V", heldCode);
                else if (Current != null && !(Current is CanvasObj)) Screens.Key(t, this, heldCode, true);
            }
            var al = Current as AlertObj;
            if (al != null && al.Deadline > 0 && now >= al.Deadline) { al.Deadline = 0; Screens.DismissAlert(t, this, al); }
            var tk = Current?.Ticker;
            if (tk != null && !(Current is CanvasObj) && now - lastTicker > 60) { lastTicker = now; tickerOff += 2; Screens.Render(this); Present(); }
        }

        long lastTicker;
        public int tickerOff;

        public void Stop(string reason)
        {
            if (Vm.Halted) return;
            ExitReason = reason;
            Vm.Halted = true;
            lock (Vm.Threads) foreach (var th in Vm.Threads) th.Wake.Set();
            lock (qlock) Monitor.PulseAll(qlock);
            Media.StopAll(this);
            lock (Sockets) { foreach (var sc in Sockets) Net.CloseSocket(sc); Sockets.Clear(); }
            Host.Exited(reason);
        }

        public void RequestExit()
        {
            if (Vm.Halted) return;
            Post(t =>
            {
                try { if (Midlet != null) t.CallVirtual(Midlet, "destroyApp(Z)V", true); }
                catch (JavaException) { }
                Stop(null);
            });
            new Timer(_ => Stop(null), null, 1500, Timeout.Infinite);
        }

        public void Pause()
        {
            if (Paused || Vm.Halted) return;
            Paused = true;
            Post(t =>
            {
                var c = Current as CanvasObj;
                if (c != null) t.CallVirtual(c, "hideNotify()V");
                if (Midlet != null) t.CallVirtual(Midlet, "pauseApp()V");
            });
        }

        public void Resume()
        {
            if (!Paused || Vm.Halted) return;
            Paused = false;
            Post(t =>
            {
                if (Midlet != null)
                {
                    try { t.CallVirtual(Midlet, "startApp()V"); }
                    catch (JavaException e) { Library.ReportUncaught(t, e.Obj); }
                }
                var c = Current as CanvasObj;
                if (c != null) t.CallVirtual(c, "showNotify()V");
                RepaintAll();
            });
        }

        public int CanvasHeight(CanvasObj c) => c.FullScreen || c.NokiaFull ? H : H - BarH;

        public int DispHeight(DisplayableObj d)
        {
            var c = d as CanvasObj;
            if (c != null) return CanvasHeight(c);
            return Screens.ContentHeight(this, d);
        }

        public void SetCurrent(JThread t, DisplayableObj d)
        {
            if (d == null) return;
            Post(et => Switch(et, d));
        }

        void Switch(JThread t, DisplayableObj d)
        {
            var old = Current;
            if (old == d) { RepaintAll(); return; }
            Menu = null;
            if (old != null)
            {
                old.Shown = false;
                if (old is CanvasObj) t.CallVirtual(old, "hideNotify()V");
            }
            if (d is AlertObj && old != null && !(old is AlertObj)) PrevDisplayable = old;
            Current = d;
            d.Shown = true;
            var al = d as AlertObj;
            if (al != null) al.Deadline = al.Timeout > 0 ? Rt.Mono() + al.Timeout : 0;
            var c = d as CanvasObj;
            if (c != null)
            {
                int ch = CanvasHeight(c);
                if (c.LastW != W || c.LastH != ch)
                {
                    bool first = c.LastW == 0;
                    c.LastW = W; c.LastH = ch;
                    if (!first) t.CallVirtual(c, "sizeChanged(II)V", W, ch);
                }
                t.CallVirtual(c, "showNotify()V");
            }
            RepaintAll();
        }

        public void FullScreenChanged(JThread t, CanvasObj c)
        {
            if (Current != c) return;
            Post(et =>
            {
                int ch = CanvasHeight(c);
                if (c.LastH != ch) { c.LastW = W; c.LastH = ch; et.CallVirtual(c, "sizeChanged(II)V", W, ch); }
                RepaintAll();
            });
        }

        public void RepaintAll() => Repaint(Current, 0, 0, W, H);

        public void Repaint(DisplayableObj d, int x, int y, int w, int h)
        {
            if (d == null || d != Current || w <= 0 || h <= 0) return;
            lock (qlock)
            {
                if (!repaintPending) { rx0 = x; ry0 = y; rx1 = x + w; ry1 = y + h; repaintPending = true; }
                else { rx0 = Math.Min(rx0, x); ry0 = Math.Min(ry0, y); rx1 = Math.Max(rx1, x + w); ry1 = Math.Max(ry1, y + h); }
                paintSeq++;
                Monitor.PulseAll(qlock);
            }
        }

        public void ServiceRepaints(JThread t)
        {
            if (t == Ev)
            {
                bool p;
                lock (qlock) p = repaintPending;
                if (p) DoPaint(t);
                return;
            }
            long want;
            // Block until the requested paint has completed, not merely started: games
            // commonly clear the Graphics/state they share with paint() right after this.
            long end = Rt.Mono() + 2000;
            lock (qlock)
            {
                want = paintSeq;
                while (paintDone < want && !Vm.Halted && Rt.Mono() < end) Monitor.Wait(qlock, 20);
            }
        }

        void DoPaint(JThread t)
        {
            int x0, y0, x1, y1;
            long seq;
            lock (qlock)
            {
                if (!repaintPending) return;
                repaintPending = false;
                x0 = rx0; y0 = ry0; x1 = rx1; y1 = ry1; seq = paintSeq;
            }
            try
            {
            var d = Current;
            var c = d as CanvasObj;
            if (c != null && !Paused)
            {
                int ch = CanvasHeight(c);
                var g = (GraphicsObj)Vm.NewObject(Vm.LoadClass("javax/microedition/lcdui/Graphics"));
                lock (ScreenLock)
                {
                    g.Attach(Screen, 0, 0, W, ch);
                    g.Screen = true;
                    g.Font = DefaultFont(t);
                    g.SetClip(x0, y0, x1 - x0, y1 - y0);
                    try { t.CallVirtual(c, "paint(Ljavax/microedition/lcdui/Graphics;)V", g); }
                    finally { if (!c.FullScreen && !c.NokiaFull) DrawSoftBar(c); }
                }
            }
            else if (d != null && !Paused)
            {
                lock (ScreenLock) Screens.Render(this);
            }
            Present();
            }
            finally
            {
                lock (qlock) { if (paintDone < seq) paintDone = seq; Monitor.PulseAll(qlock); }
            }
        }

        FontObj defaultFont;
        public FontObj DefaultFont(JThread t)
        {
            if (defaultFont == null) defaultFont = JavaFont(t, 0, 0, 0);
            return defaultFont;
        }

        public void Flush(CanvasObj c, int x, int y, int w, int h)
        {
            if (c != Current || c.Back == null || Paused) return;
            int ch = CanvasHeight(c);
            lock (ScreenLock)
            {
                int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(Math.Min(W, c.Back.W), x + w), y1 = Math.Min(Math.Min(ch, c.Back.H), y + h);
                for (int yy = y0; yy < y1; yy++)
                    Array.Copy(c.Back.Pix, yy * c.Back.W + x0, Screen.Pix, yy * W + x0, Math.Max(0, x1 - x0));
                if (!c.FullScreen && !c.NokiaFull) DrawSoftBar(c);
            }
            Present();
        }

        public long FrameSeq;

        public void Present()
        {
            System.Threading.Interlocked.Increment(ref FrameSeq);
            Host.FrameReady();
        }

        public void CopyFrame(byte[] dest)
        {
            lock (ScreenLock)
            {
                if (Menu == null) { Buffer.BlockCopy(Screen.Pix, 0, dest, 0, Math.Min(dest.Length, Screen.Pix.Length * 4)); return; }
                Array.Copy(Screen.Pix, output, output.Length);
                Menu.Draw(this, output);
                Buffer.BlockCopy(output, 0, dest, 0, Math.Min(dest.Length, output.Length * 4));
            }
        }

        public GraphicsObj SysGraphics(Surface s, int[] pix = null)
        {
            var g = new GraphicsObj();
            g.Attach(pix == null ? s : new Surface(W, H, pix, false), 0, 0, W, H);
            g.Font = SysFont;
            return g;
        }

        public void DrawSoftBar(DisplayableObj d)
        {
            var g = SysGraphics(Screen);
            int y = H - BarH;
            g.Color = unchecked((int)0xFF202020);
            Gfx.FillRect(g, 0, y, W, BarH);
            g.Font = SysSmall;
            g.Color = -1;
            CommandObj l, r;
            bool menu;
            SoftCommands(d, out l, out r, out menu);
            if (menu) Gfx.DrawString(g, "Options", 0, 7, 3, y + 2, Gfx.TOP | Gfx.LEFT);
            else if (l != null) Gfx.DrawString(g, l.Label, 0, l.Label.Length, 3, y + 2, Gfx.TOP | Gfx.LEFT);
            if (r != null) Gfx.DrawString(g, r.Label, 0, r.Label.Length, W - 3, y + 2, Gfx.TOP | Gfx.RIGHT);
            var extra = Screens.CenterLabel(this, d);
            if (extra != null) Gfx.DrawString(g, extra, 0, extra.Length, W / 2, y + 2, Gfx.TOP | Gfx.HCENTER);
        }

        static bool Negative(CommandObj c) => c.Type == 2 || c.Type == 3 || c.Type == 6 || c.Type == 7;

        public List<CommandObj> AllCommands(DisplayableObj d)
        {
            var list = new List<CommandObj>();
            if (d == null) return list;
            lock (d.Commands) list.AddRange(d.Commands);
            Screens.AddItemCommands(this, d, list);
            return list;
        }

        public void SoftCommands(DisplayableObj d, out CommandObj left, out CommandObj right, out bool menu)
        {
            left = right = null; menu = false;
            var cmds = AllCommands(d);
            if (cmds.Count == 0) return;
            cmds.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            foreach (var c in cmds) if (Negative(c)) { right = c; break; }
            if (right == null && cmds.Count == 2) right = cmds[1];
            var r0 = right;
            var rest = cmds.FindAll(c => c != r0);
            if (rest.Count == 1) left = rest[0];
            else if (rest.Count > 1) menu = true;
        }

        public void FireCommand(JThread t, DisplayableObj d, CommandObj c)
        {
            if (Screens.FireItemCommand(t, this, d, c)) return;
            if (d.Listener != null) t.CallVirtual(d.Listener, "commandAction(Ljavax/microedition/lcdui/Command;Ljavax/microedition/lcdui/Displayable;)V", c, d);
            else if (d is AlertObj) Screens.DismissAlert(t, this, (AlertObj)d);
        }

        public void KeyDown(int code)
        {
            int bit = GameBit(code);
            if (bit != 0) { gameHeld |= bit; gameLatched |= bit; }
            Post(t => HandleKey(t, code, true));
        }

        public void KeyUp(int code)
        {
            int bit = GameBit(code);
            if (bit != 0) gameHeld &= ~bit;
            Post(t => HandleKey(t, code, false));
        }

        public int GameKeyStates()
        {
            int v = gameLatched | gameHeld;
            gameLatched = 0;
            return v;
        }

        static int GameBit(int code)
        {
            int a = Keys.GameAction(code);
            return a == 0 ? 0 : 1 << a;
        }

        void HandleKey(JThread t, int code, bool down)
        {
            if (down) { keyHeld = 1; heldCode = code; heldSince = lastRepeat = Rt.Mono(); }
            else if (code == heldCode) keyHeld = 0;
            var d = Current;
            if (d == null) return;
            if (Menu != null)
            {
                if (down) Menu.Key(t, this, code);
                return;
            }
            var c = d as CanvasObj;
            if ((code == Keys.SoftL || code == Keys.SoftR) && !(c != null && c.NokiaFull))
            {
                CommandObj l, r;
                bool menu;
                SoftCommands(d, out l, out r, out menu);
                bool handled = code == Keys.SoftL ? (l != null || menu) : r != null;
                if (handled)
                {
                    if (!down) return;
                    if (code == Keys.SoftR) FireCommand(t, d, r);
                    else if (l != null) FireCommand(t, d, l);
                    else
                    {
                        var cmds = AllCommands(d);
                        cmds.Remove(r);
                        cmds.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                        Menu = new SoftMenu(d, cmds);
                        Present();
                    }
                    return;
                }
            }
            if (c != null)
            {
                if (c.Suppress && Keys.GameAction(code) != 0 && code != Keys.SoftL && code != Keys.SoftR) return;
                t.CallVirtual(c, down ? "keyPressed(I)V" : "keyReleased(I)V", code);
            }
            else if (down) Screens.Key(t, this, code, false);
        }

        public void PointerDown(int x, int y) => Post(t => Pointer(t, 0, x, y));
        public void PointerMove(int x, int y) => Post(t => Pointer(t, 1, x, y));
        public void PointerUp(int x, int y) => Post(t => Pointer(t, 2, x, y));

        void Pointer(JThread t, int kind, int x, int y)
        {
            var d = Current;
            if (d == null) return;
            if (Menu != null) { if (kind == 0) Menu.Tap(t, this, x, y); return; }
            var c = d as CanvasObj;
            if (c != null)
            {
                if (y >= CanvasHeight(c))
                {
                    if (kind == 0) HandleKey(t, x < W / 2 ? Keys.SoftL : Keys.SoftR, true);
                    else if (kind == 2) HandleKey(t, x < W / 2 ? Keys.SoftL : Keys.SoftR, false);
                    return;
                }
                string m = kind == 0 ? "pointerPressed(II)V" : kind == 1 ? "pointerDragged(II)V" : "pointerReleased(II)V";
                t.CallVirtual(c, m, x, y);
            }
            else Screens.Pointer(t, this, kind, x, y);
        }

        public void CallSerially(object runnable)
        {
            Post(t => t.CallVirtual(runnable, "run()V"));
        }
    }

    public sealed class SoftMenu
    {
        readonly DisplayableObj owner;
        readonly List<CommandObj> items;
        int sel, top;
        int x, y, w, ih, visible;

        public SoftMenu(DisplayableObj d, List<CommandObj> cmds) { owner = d; items = cmds; }

        void Layout(MidpRuntime rt)
        {
            ih = rt.SysFont.Height + 6;
            w = rt.W * 3 / 4;
            visible = Math.Min(items.Count, Math.Max(1, (rt.H - rt.BarH - 8) / ih));
            x = 2;
            y = rt.H - rt.BarH - visible * ih - 2;
            if (sel < top) top = sel;
            if (sel >= top + visible) top = sel - visible + 1;
        }

        public void Draw(MidpRuntime rt, int[] pix)
        {
            Layout(rt);
            var g = rt.SysGraphics(null, pix);
            g.Color = unchecked((int)0xFF202020);
            Gfx.FillRect(g, 0, rt.H - rt.BarH, rt.W, rt.BarH);
            g.Font = rt.SysSmall;
            g.Color = -1;
            Gfx.DrawString(g, "Select", 0, 6, 3, rt.H - rt.BarH + 2, Gfx.TOP | Gfx.LEFT);
            Gfx.DrawString(g, "Cancel", 0, 6, rt.W - 3, rt.H - rt.BarH + 2, Gfx.TOP | Gfx.RIGHT);
            g.Font = rt.SysFont;
            g.Color = unchecked((int)0xFF404040);
            Gfx.FillRect(g, x - 1, y - 1, w + 2, visible * ih + 2);
            for (int i = 0; i < visible; i++)
            {
                int k = top + i;
                g.Color = k == sel ? unchecked((int)0xFF2A6FD6) : unchecked((int)0xFFF4F4F4);
                Gfx.FillRect(g, x, y + i * ih, w, ih);
                g.Color = k == sel ? -1 : unchecked((int)0xFF000000);
                var lab = items[k].LongLabel ?? items[k].Label ?? "";
                Gfx.DrawString(g, lab, 0, lab.Length, x + 6, y + i * ih + 3, Gfx.TOP | Gfx.LEFT);
            }
        }

        public void Key(JThread t, MidpRuntime rt, int code)
        {
            int a = Keys.GameAction(code);
            if (code == Keys.SoftR || code == Keys.Clear) { rt.Menu = null; rt.RepaintAll(); rt.Present(); return; }
            if (a == Keys.GUp) sel = (sel + items.Count - 1) % items.Count;
            else if (a == Keys.GDown) sel = (sel + 1) % items.Count;
            else if (a == Keys.GFire || code == Keys.SoftL) { Choose(t, rt, sel); return; }
            else if (code >= '1' && code <= '9' && code - '1' < items.Count) { Choose(t, rt, code - '1'); return; }
            rt.Present();
        }

        public void Tap(JThread t, MidpRuntime rt, int px, int py)
        {
            Layout(rt);
            if (py >= rt.H - rt.BarH) { Key(t, rt, px < rt.W / 2 ? Keys.SoftL : Keys.SoftR); return; }
            if (px >= x && px < x + w && py >= y && py < y + visible * ih) { Choose(t, rt, top + (py - y) / ih); return; }
            rt.Menu = null; rt.RepaintAll(); rt.Present();
        }

        void Choose(JThread t, MidpRuntime rt, int i)
        {
            rt.Menu = null;
            rt.RepaintAll();
            rt.Present();
            rt.FireCommand(t, owner, items[i]);
        }
    }
}
