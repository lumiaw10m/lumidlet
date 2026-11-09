using System;
using System.Collections.Generic;
using System.Linq;
using Lumidlet.Midp;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Lumidlet
{
    public sealed class Keypad
    {
        public const int MenuKey = int.MinValue;
        public const int DiagBase = -1000;

        public static bool IsDiagonal(int code) => code > DiagBase - 100 && code < DiagBase + 100;
        public static int DiagonalDigit(int code) => code - DiagBase;

        public static int[] DiagonalArrows(int code)
        {
            switch (code - DiagBase)
            {
                case '1': return new[] { Keys.Up, Keys.Left };
                case '3': return new[] { Keys.Up, Keys.Right };
                case '7': return new[] { Keys.Down, Keys.Left };
                default: return new[] { Keys.Down, Keys.Right };
            }
        }

        sealed class Key
        {
            public int Code;
            public Border View;
            public int Down;
        }

        static readonly Color Normal = Color.FromArgb(255, 40, 40, 44), Pressed = Color.FromArgb(255, 42, 111, 214), Soft = Color.FromArgb(255, 30, 61, 115);

        public readonly Grid Nav = new Grid(), Num = new Grid();
        readonly List<Key> keys = new List<Key>();
        readonly Dictionary<uint, Key> pointers = new Dictionary<uint, Key>();
        public Action<int> Down, Up;

        public const int Classic = 0, Split = 1;
        public static readonly string[] LayoutNames = { "Classic", "Split (8-way)" };
        public readonly int Layout;

        public Keypad(int layout = Classic)
        {
            Layout = layout == Split ? Split : Classic;
            if (Layout == Split)
                Build(Nav, new object[,]
                {
                    { Lbl("L", Keys.SoftL), null, Lbl("R", Keys.SoftR) },
                    { Lbl("↖", DiagBase + '1'), Lbl("↑", Keys.Up), Lbl("↗", DiagBase + '3') },
                    { Lbl("←", Keys.Left), Lbl("F", Keys.Fire), Lbl("→", Keys.Right) },
                    { Lbl("↙", DiagBase + '7'), Lbl("↓", Keys.Down), Lbl("↘", DiagBase + '9') },
                });
            else
                Build(Nav, new object[,]
                {
                    { Lbl("▬", Keys.SoftL), Lbl("▲", Keys.Up), Lbl("▬", Keys.SoftR) },
                    { Lbl("◄", Keys.Left), Lbl("OK", Keys.Fire), Lbl("►", Keys.Right) },
                    { Lbl("C", Keys.Clear), Lbl("▼", Keys.Down), Lbl("☰", MenuKey) },
                });
            Build(Num, new object[,]
            {
                { Lbl("1", '1'), Lbl("2", '2'), Lbl("3", '3') },
                { Lbl("4", '4'), Lbl("5", '5'), Lbl("6", '6') },
                { Lbl("7", '7'), Lbl("8", '8'), Lbl("9", '9') },
                { Lbl("*", '*'), Lbl("0", '0'), Lbl("#", '#') },
            });
            foreach (var g in new[] { Nav, Num })
            {
                g.Background = new SolidColorBrush(Colors.Transparent);
                g.PointerPressed += (s, e) => Pointer(g, e, 0);
                g.PointerMoved += (s, e) => Pointer(g, e, 1);
                g.PointerReleased += (s, e) => Pointer(g, e, 2);
                g.PointerCanceled += (s, e) => Pointer(g, e, 2);
                g.PointerCaptureLost += (s, e) => Pointer(g, e, 2);
            }
        }

        static KeyValuePair<string, int> Lbl(string s, int code) => new KeyValuePair<string, int>(s, code);

        void Build(Grid g, object[,] layout)
        {
            int rows = layout.GetLength(0), cols = layout.GetLength(1);
            for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (layout[r, c] == null) continue;
                    var kv = (KeyValuePair<string, int>)layout[r, c];
                    bool soft = kv.Value == Keys.SoftL || kv.Value == Keys.SoftR || kv.Value == MenuKey;
                    var tb = new TextBlock
                    {
                        Text = kv.Key,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 20,
                        FontFamily = new FontFamily("Segoe UI Symbol"),
                        IsHitTestVisible = false,
                    };
                    var b = new Border
                    {
                        Background = new SolidColorBrush(soft ? Soft : Normal),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(3),
                        Child = tb,
                        IsHitTestVisible = false,
                    };
                    Grid.SetRow(b, r);
                    Grid.SetColumn(b, c);
                    g.Children.Add(b);
                    keys.Add(new Key { Code = kv.Value, View = b });
                }
        }

        public void SetFontSize(double size)
        {
            foreach (var k in keys) ((TextBlock)k.View.Child).FontSize = size;
        }

        Key HitTest(Grid g, Point p)
        {
            foreach (var k in keys)
            {
                if (k.View.Parent != g) continue;
                var tr = k.View.TransformToVisual(g);
                var r = tr.TransformBounds(new Rect(0, 0, k.View.ActualWidth, k.View.ActualHeight));
                r = new Rect(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
                if (r.Contains(p)) return k;
            }
            return null;
        }

        void Pointer(Grid g, PointerRoutedEventArgs e, int kind)
        {
            var id = e.Pointer.PointerId;
            e.Handled = true;
            if (kind == 1 && e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse && !e.Pointer.IsInContact) return;
            Key cur;
            pointers.TryGetValue(id, out cur);
            if (kind == 2)
            {
                if (cur != null) Release(cur);
                pointers.Remove(id);
                return;
            }
            if (kind == 0) g.CapturePointer(e.Pointer);
            else if (cur == null) return;
            var k = HitTest(g, e.GetCurrentPoint(g).Position);
            if (k == cur) return;
            if (cur != null) Release(cur);
            if (k != null && (kind == 0 || k.Code != MenuKey))
            {
                pointers[id] = k;
                Press(k);
            }
            else pointers.Remove(id);
        }

        void Press(Key k)
        {
            if (k.Down++ == 0)
            {
                k.View.Background = new SolidColorBrush(Pressed);
                Down?.Invoke(k.Code);
            }
        }

        void Release(Key k)
        {
            if (k.Down > 0 && --k.Down == 0)
            {
                bool soft = k.Code == Keys.SoftL || k.Code == Keys.SoftR || k.Code == MenuKey;
                k.View.Background = new SolidColorBrush(soft ? Soft : Normal);
                Up?.Invoke(k.Code);
            }
        }

        public void ReleaseAll()
        {
            foreach (var k in keys.Where(k => k.Down > 0)) { k.Down = 1; Release(k); }
            pointers.Clear();
        }
    }
}
