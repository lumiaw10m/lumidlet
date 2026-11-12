using System;
using System.IO;
using System.Threading;
using Lumidlet.Midp;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Graphics.Display;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Lumidlet
{
    public sealed partial class GamePage : Page
    {
        AppEntry app;
        UwpHost host;
        MidpRuntime rt;
        Keypad pad = new Keypad();
        Rect dest;
        CanvasBitmap bitmap;
        byte[] bytes;
        long drawnSeq = -1;
        bool leaving;
        uint touchId;
        bool touching;
        string script;
        DispatcherTimer scriptTimer;

        void RunScript()
        {
            if (string.IsNullOrEmpty(script)) return;
            var steps = new System.Collections.Generic.List<int[]>();
            foreach (var part in script.Split(','))
            {
                var p = part.Split(':');
                int at, code, hold = 80;
                if (p.Length < 2 || !int.TryParse(p[0], out at)) continue;
                var ch = p[1].Split('+');
                if (!int.TryParse(ch[0], out code)) continue;
                if (ch.Length > 1) int.TryParse(ch[1], out hold);
                steps.Add(new[] { at, code, 1 });
                steps.Add(new[] { at + hold, code, 0 });
            }
            steps.Sort((x, y) => x[0].CompareTo(y[0]));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int i = 0;
            scriptTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            scriptTimer.Tick += (s, e) =>
            {
                while (i < steps.Count && steps[i][0] <= sw.ElapsedMilliseconds)
                {
                    if (steps[i][2] == 1) rt?.KeyDown(steps[i][1]); else rt?.KeyUp(steps[i][1]);
                    i++;
                }
                if (i >= steps.Count) scriptTimer.Stop();
            };
            scriptTimer.Start();
        }

        public GamePage()
        {
            InitializeComponent();
            AttachPad(Keypad.Classic);
            SizeChanged += (s, e) => Layout();
        }

        void AttachPad(int layout)
        {
            if (pad != null)
            {
                pad.ReleaseAll();
                PadLayer.Children.Remove(pad.Nav);
                PadLayer.Children.Remove(pad.Num);
            }
            pad = new Keypad(layout);
            pad.SetAbc(abc, null);
            PadLayer.Children.Add(pad.Nav);
            PadLayer.Children.Add(pad.Num);
            pad.Down = code =>
            {
                if (code == Keypad.MenuKey) { ShowMenu(); return; }
                if (code == Keypad.AbcKey) { SetAbcMode(!abc); return; }
                if (abc && code >= '0' && code <= '9') { Tap(code); return; }
                if (abc) Commit();
                if (abc && code == '*') { rt?.KeyDown(Keys.Clear); return; }
                if (!Keypad.IsDiagonal(code)) { rt?.KeyDown(code); return; }
                if (app != null && app.DiagCombo) foreach (var k in Keypad.DiagonalArrows(code)) rt?.KeyDown(k);
                else rt?.KeyDown(Keypad.DiagonalDigit(code));
            };
            pad.Up = code =>
            {
                if (code == Keypad.MenuKey || code == Keypad.AbcKey) return;
                if (abc && code >= '0' && code <= '9') return;
                if (abc && code == '*') { rt?.KeyUp(Keys.Clear); return; }
                if (!Keypad.IsDiagonal(code)) { rt?.KeyUp(code); return; }
                if (app != null && app.DiagCombo) foreach (var k in Keypad.DiagonalArrows(code)) rt?.KeyUp(k);
                else rt?.KeyUp(Keypad.DiagonalDigit(code));
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var param = e.Parameter as string ?? "";
            int bar = param.IndexOf('|');
            if (bar >= 0) { script = param.Substring(bar + 1); param = param.Substring(0, bar); }
            app = AppStore.Load(param);
            if (app == null) { Frame.GoBack(); return; }
            ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            DisplayInformation.AutoRotationPreferences = app.Width > app.Height || app.Rotate ? DisplayOrientations.Landscape | DisplayOrientations.LandscapeFlipped : DisplayOrientations.Portrait;
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBack;
            Window.Current.CoreWindow.KeyDown += OnKeyDown;
            Window.Current.CoreWindow.CharacterReceived += OnChar;
            Window.Current.CoreWindow.KeyUp += OnKeyUp;
            Application.Current.Suspending += OnSuspending;
            Application.Current.Resuming += OnResuming;
            MenuTitle.Text = app.Name;
            if (pad.Layout != app.PadLayout) AttachPad(app.PadLayout);
            pad.SetAbc(abc, null);
            Start();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            leaving = true;
            scriptTimer?.Stop();
            SystemNavigationManager.GetForCurrentView().BackRequested -= OnBack;
            Window.Current.CoreWindow.KeyDown -= OnKeyDown;
            Window.Current.CoreWindow.CharacterReceived -= OnChar;
            Window.Current.CoreWindow.KeyUp -= OnKeyUp;
            Application.Current.Suspending -= OnSuspending;
            Application.Current.Resuming -= OnResuming;
            Shutdown();
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.None;
            Screen.RemoveFromVisualTree();
        }

        void Start()
        {
            UpdateMenuLabels();
            Layout();
            try
            {
                host = new UwpHost(this, app, Dispatcher);
                rt = new MidpRuntime(host, File.ReadAllBytes(app.JarPath), app.Config(), app.Jad());
                rt.Start();
                RunScript();
            }
            catch (Exception ex)
            {
                host?.Log("start failed: " + ex);
                OnMidletExited("Could not start: " + ex.Message);
            }
        }

        void Shutdown()
        {
            pad.ReleaseAll();
            if (host != null) host.Detached = true;
            try { rt?.Stop(null); } catch (Exception) { }
            host?.Dispose();
            rt = null;
            host = null;
        }

        public void InvalidateScreen()
        {
            if (!leaving) Screen.Invalidate();
        }

        public async void OnMidletExited(string reason)
        {
            if (leaving) return;
            if (reason != null && reason != "System.exit")
            {
                try { await new MessageDialog(reason, app.Name).ShowAsync(); } catch (Exception) { }
            }
            if (!leaving && Frame.CanGoBack) Frame.GoBack();
        }

        void Layout()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || app == null) return;
            Rect area;
            bool show = app.Keypad;
            pad.Nav.Visibility = pad.Num.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) area = new Rect(0, 0, w, h);
            else if (w > h)
            {
                double side = Math.Min(w * 0.24, h * 0.75);
                if (pad.Layout == Keypad.Split)
                {
                    Place(pad.Num, 0, h - side * 4 / 3, side, side * 4 / 3);
                    Place(pad.Nav, w - side, h - side * 4 / 3, side, side * 4 / 3);
                }
                else
                {
                    Place(pad.Nav, 0, h - side * 1.0, side, side);
                    Place(pad.Num, w - side, h - side * 4 / 3, side, side * 4 / 3);
                }
                area = new Rect(side, 0, w - 2 * side, h);
                pad.SetFontSize(side / 9);
            }
            else
            {
                double ph = Math.Min(h * 0.40, w * 0.62);
                if (pad.Layout == Keypad.Split)
                {
                    Place(pad.Num, 0, h - ph, w / 2, ph);
                    Place(pad.Nav, w / 2, h - ph, w / 2, ph);
                }
                else
                {
                    Place(pad.Nav, 0, h - ph * 0.75, w / 2, ph * 0.75);
                    Place(pad.Num, w / 2, h - ph, w / 2, ph);
                }
                area = new Rect(0, 0, w, h - ph);
                pad.SetFontSize(ph / 12);
            }
            double fw = app.Width, fh = app.Height;
            double scale = Math.Min(area.Width / fw, area.Height / fh);
            if (!app.Smooth && scale > 1)
            {
                double iscale = Math.Floor(scale);
                if (iscale / scale > 0.9) scale = iscale;
            }
            dest = new Rect(area.X + (area.Width - fw * scale) / 2, area.Y + (area.Height - fh * scale) / 2, fw * scale, fh * scale);
            Screen.Invalidate();
        }

        static void Place(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.Width = Math.Max(0, w);
            el.Height = Math.Max(0, h);
        }

        void Screen_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var r = rt;
            if (r == null) return;
            int w = r.W, hh = r.H;
            long seq = Interlocked.Read(ref r.FrameSeq);
            if (seq == 0) return;
            if (seq != drawnSeq || bitmap == null)
            {
                if (bytes == null || bytes.Length != w * hh * 4) bytes = new byte[w * hh * 4];
                r.CopyFrame(bytes);
                drawnSeq = seq;
                if (bitmap == null || bitmap.SizeInPixels.Width != w || bitmap.SizeInPixels.Height != hh)
                {
                    bitmap?.Dispose();
                    bitmap = CanvasBitmap.CreateFromBytes(sender, bytes, w, hh, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Ignore);
                }
                else bitmap.SetPixelBytes(bytes);
            }
            args.DrawingSession.DrawImage(bitmap, dest, new Rect(0, 0, w, hh), 1f,
                app.Smooth ? CanvasImageInterpolation.Linear : CanvasImageInterpolation.NearestNeighbor);
        }

        bool ToGame(Point p, out int x, out int y)
        {
            x = (int)((p.X - dest.X) * app.Width / dest.Width);
            y = (int)((p.Y - dest.Y) * app.Height / dest.Height);
            return x >= 0 && y >= 0 && x < app.Width && y < app.Height;
        }

        void Screen_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (touching || rt == null) return;
            int x, y;
            if (!ToGame(e.GetCurrentPoint(Screen).Position, out x, out y)) return;
            touching = true;
            touchId = e.Pointer.PointerId;
            Screen.CapturePointer(e.Pointer);
            rt.PointerDown(x, y);
        }

        void Screen_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!touching || e.Pointer.PointerId != touchId || rt == null) return;
            int x, y;
            ToGame(e.GetCurrentPoint(Screen).Position, out x, out y);
            rt.PointerMove(Math.Max(0, Math.Min(app.Width - 1, x)), Math.Max(0, Math.Min(app.Height - 1, y)));
        }

        void Screen_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!touching || e.Pointer.PointerId != touchId) return;
            touching = false;
            int x, y;
            ToGame(e.GetCurrentPoint(Screen).Position, out x, out y);
            rt?.PointerUp(Math.Max(0, Math.Min(app.Width - 1, x)), Math.Max(0, Math.Min(app.Height - 1, y)));
        }

        static int MapKey(VirtualKey k)
        {
            switch (k)
            {
                case VirtualKey.Up: case VirtualKey.GamepadDPadUp: return Keys.Up;
                case VirtualKey.Down: case VirtualKey.GamepadDPadDown: return Keys.Down;
                case VirtualKey.Left: case VirtualKey.GamepadDPadLeft: return Keys.Left;
                case VirtualKey.Right: case VirtualKey.GamepadDPadRight: return Keys.Right;
                case VirtualKey.Enter: case VirtualKey.Space: case VirtualKey.GamepadA: return Keys.Fire;
                case VirtualKey.F1: case VirtualKey.GamepadLeftShoulder: return Keys.SoftL;
                case VirtualKey.F2: case VirtualKey.GamepadRightShoulder: return Keys.SoftR;
                case VirtualKey.Back: case VirtualKey.Delete: case VirtualKey.GamepadB: return Keys.Clear;
                case VirtualKey.Multiply: return '*';
                case VirtualKey.Divide: return '#';
            }
            if (k >= VirtualKey.Number0 && k <= VirtualKey.Number9) return '0' + (k - VirtualKey.Number0);
            if (k >= VirtualKey.NumberPad0 && k <= VirtualKey.NumberPad9) return '0' + (k - VirtualKey.NumberPad0);
            return 0;
        }

        void OnChar(CoreWindow sender, CharacterReceivedEventArgs e)
        {
            if (Menu.Visibility == Visibility.Visible || rt == null) return;
            char c = (char)e.KeyCode;
            if (c < 33 || char.IsDigit(c) || c == '*' || c == '#' || c == 127) return;
            rt.KeyDown(c);
            rt.KeyUp(c);
            e.Handled = true;
        }

        bool abc;
        int pendKey, pendIdx;
        DispatcherTimer pendTimer;

        void SetAbcMode(bool on)
        {
            Commit();
            abc = on;
            pad.SetAbc(abc, null);
            UpdateMenuLabels();
        }

        void Tap(int digit)
        {
            if (pendTimer == null)
            {
                pendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                pendTimer.Tick += (s, a) => Commit();
            }
            var seq = Keypad.Letters[digit - '0'] + (char)digit;
            if (pendKey == digit) pendIdx = (pendIdx + 1) % seq.Length;
            else { Commit(); pendKey = digit; pendIdx = 0; }
            pendTimer.Stop();
            pendTimer.Start();
            pad.SetAbc(true, seq[pendIdx].ToString());
        }

        void Commit()
        {
            pendTimer?.Stop();
            if (pendKey == 0) return;
            var seq = Keypad.Letters[pendKey - '0'] + (char)pendKey;
            int c = char.ToLowerInvariant(seq[pendIdx]);
            pendKey = 0; pendIdx = 0;
            pad.SetAbc(abc, null);
            rt?.KeyDown(c);
            rt?.KeyUp(c);
        }

        void Abc_Click(object sender, RoutedEventArgs e)
        {
            SetAbcMode(!abc);
            HideMenu();
        }

        void OnKeyDown(CoreWindow sender, KeyEventArgs e)
        {
            if (Menu.Visibility == Visibility.Visible || rt == null) return;
            if (e.VirtualKey == VirtualKey.Escape) { ShowMenu(); e.Handled = true; return; }
            int c = MapKey(e.VirtualKey);
            if (c == 0 || e.KeyStatus.WasKeyDown) return;
            rt.KeyDown(c);
            e.Handled = true;
        }

        void OnKeyUp(CoreWindow sender, KeyEventArgs e)
        {
            int c = MapKey(e.VirtualKey);
            if (c == 0 || rt == null) return;
            rt.KeyUp(c);
            e.Handled = true;
        }

        void OnBack(object sender, BackRequestedEventArgs e)
        {
            e.Handled = true;
            if (Menu.Visibility == Visibility.Visible) HideMenu();
            else ShowMenu();
        }

        void OnSuspending(object sender, SuspendingEventArgs e) => rt?.Pause();

        void OnResuming(object sender, object e)
        {
            if (Menu.Visibility != Visibility.Visible) rt?.Resume();
        }

        void ShowMenu()
        {
            pad.ReleaseAll();
            UpdateMenuLabels();
            Menu.Visibility = Visibility.Visible;
            rt?.Pause();
            ResumeButton.Focus(FocusState.Programmatic);
        }

        void HideMenu()
        {
            Menu.Visibility = Visibility.Collapsed;
            rt?.Resume();
        }

        void UpdateMenuLabels()
        {
            KeypadButton.Content = app.Keypad ? "Hide keypad" : "Show keypad";
            LayoutButton.Content = "Keypad layout: " + Keypad.LayoutNames[app.PadLayout];
            LayoutButton.Visibility = app.Keypad ? Visibility.Visible : Visibility.Collapsed;
            AbcButton.Content = abc ? "Letters on number keys: on" : "Letters on number keys: off";
            DiagButton.Content = app.DiagCombo ? "Diagonals: arrow combos" : "Diagonals: 1 3 7 9";
            DiagButton.Visibility = app.Keypad && app.PadLayout == Keypad.Split ? Visibility.Visible : Visibility.Collapsed;
            SmoothButton.Content = app.Smooth ? "Sharp pixels" : "Smooth scaling";
            SoundButton.Content = app.Sound ? "Sound off (restart)" : "Sound on (restart)";
        }

        void Menu_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource == Menu) HideMenu();
        }

        void Resume_Click(object sender, RoutedEventArgs e) => HideMenu();

        void Layout_Click(object sender, RoutedEventArgs e)
        {
            app.PadLayout = (app.PadLayout + 1) % Keypad.LayoutNames.Length;
            AppStore.Save(app);
            AttachPad(app.PadLayout);
            Layout();
            UpdateMenuLabels();
        }

        void Diag_Click(object sender, RoutedEventArgs e)
        {
            pad.ReleaseAll();
            app.DiagCombo = !app.DiagCombo;
            AppStore.Save(app);
            UpdateMenuLabels();
        }

        void Keypad_Click(object sender, RoutedEventArgs e)
        {
            app.Keypad = !app.Keypad;
            AppStore.Save(app);
            Layout();
            HideMenu();
        }

        void Smooth_Click(object sender, RoutedEventArgs e)
        {
            app.Smooth = !app.Smooth;
            AppStore.Save(app);
            Layout();
            HideMenu();
        }

        void Sound_Click(object sender, RoutedEventArgs e)
        {
            app.Sound = !app.Sound;
            AppStore.Save(app);
            Restart_Click(sender, e);
        }

        void Restart_Click(object sender, RoutedEventArgs e)
        {
            Menu.Visibility = Visibility.Collapsed;
            Shutdown();
            bitmap = null;
            drawnSeq = -1;
            Start();
        }

        void Exit_Click(object sender, RoutedEventArgs e)
        {
            Menu.Visibility = Visibility.Collapsed;
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
