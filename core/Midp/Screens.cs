using System;
using System.Collections.Generic;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public class ScreenObj : DisplayableObj
    {
        public int Focus, Sub, Scroll;
    }

    public sealed class ChoiceEl
    {
        public string Text;
        public ImageObj Img;
        public bool Sel;
    }

    public class ItemObj : JObject
    {
        public string Label;
        public ScreenObj Owner;
        public readonly List<CommandObj> Commands = new List<CommandObj>();
        public CommandObj Default;
        public object CmdListener;
        public int Layout, PrefW = -1, PrefH = -1;
        public int Kind;
        public string Text;
        public FontObj Font;
        public int Appearance;
        public ImageObj Img;
        public int Max, Value, Constraints;
        public bool Interactive;
        public int ChoiceType;
        public readonly List<ChoiceEl> Els = new List<ChoiceEl>();
        public long Date = long.MinValue;
        public int Mode;
        public int MinW, MinH;
    }

    public sealed class FormObj : ScreenObj
    {
        public readonly List<ItemObj> Items = new List<ItemObj>();
        public object StateListener;
    }

    public sealed class ListObj : ScreenObj
    {
        public int Type;
        public readonly List<ChoiceEl> Els = new List<ChoiceEl>();
        public CommandObj SelectCmd;
    }

    public sealed class TextBoxObj : ScreenObj
    {
        public string Text = "";
        public int Max, Constraints;
    }

    public sealed class AlertObj : ScreenObj
    {
        public string Text;
        public ImageObj Img;
        public object Type;
        public int Timeout = 2000;
        public DisplayableObj Next;
        public ItemObj Indicator;
        public long Deadline;
    }

    public static class Screens
    {
        const string P = "javax/microedition/lcdui/";
        public const int KString = 1, KImage = 2, KText = 3, KGauge = 4, KChoice = 5, KSpacer = 6, KDate = 7, KCustom = 8;

        static MidpRuntime R(JThread t) => MidpRuntime.Of(t);
        static string S(JThread t, int i) => (string)t.R[i];
        static ItemObj I(JThread t, int b) => (ItemObj)t.R[b];

        static void Dirty(JThread t, DisplayableObj d)
        {
            if (d != null) R(t).Repaint(d, 0, 0, R(t).W, R(t).H);
        }

        static void Dirty(JThread t, ItemObj i) => Dirty(t, i.Owner);

        static int TitleH(MidpRuntime rt) => rt.SysBold.Height + 4;

        public static int ContentHeight(MidpRuntime rt, DisplayableObj d)
        {
            int h = rt.H - rt.BarH - TitleH(rt);
            if (d.Ticker != null) h -= rt.SysSmall.Height + 2;
            return h;
        }

        public static string CenterLabel(MidpRuntime rt, DisplayableObj d)
        {
            var l = d as ListObj;
            if (l != null && l.Els.Count > 0) return l.Type == 3 ? "Select" : l.Type == 2 ? "Mark" : null;
            if (d is TextBoxObj) return "Edit";
            var f = d as FormObj;
            if (f != null)
            {
                var it = FocusItem(f);
                if (it == null) return null;
                if (it.Kind == KText) return "Edit";
                if (it.Kind == KChoice) return it.ChoiceType == 2 ? "Mark" : "Select";
                if (it.Default != null) return it.Default.Label;
            }
            return null;
        }

        static ItemObj FocusItem(FormObj f)
        {
            lock (f.Items) return f.Focus >= 0 && f.Focus < f.Items.Count && Focusable(f.Items[f.Focus]) ? f.Items[f.Focus] : null;
        }

        static bool Focusable(ItemObj i) =>
            i.Kind == KText || i.Kind == KChoice || i.Kind == KDate || i.Kind == KCustom || (i.Kind == KGauge && i.Interactive) || i.Commands.Count > 0;

        public static void AddItemCommands(MidpRuntime rt, DisplayableObj d, List<CommandObj> list)
        {
            var f = d as FormObj;
            if (f == null) { var a = d as AlertObj; if (a != null && list.Count == 0) list.Add(DismissCmd(rt)); return; }
            var it = FocusItem(f);
            if (it != null) lock (it.Commands) foreach (var c in it.Commands) if (!list.Contains(c)) list.Add(c);
        }

        static CommandObj dismiss;
        static CommandObj DismissCmd(MidpRuntime rt)
        {
            var k = rt.Vm.LoadClass(P + "Alert");
            return (CommandObj)k.StaticO[0] ?? (dismiss ?? (dismiss = new CommandObj { Label = "Done", Type = 4 }));
        }

        public static bool FireItemCommand(JThread t, MidpRuntime rt, DisplayableObj d, CommandObj c)
        {
            var f = d as FormObj;
            if (f == null) return false;
            var it = FocusItem(f);
            if (it == null) return false;
            bool own;
            lock (it.Commands) own = it.Commands.Contains(c);
            if (!own) return false;
            if (it.CmdListener != null) t.CallVirtual(it.CmdListener, "commandAction(L" + P + "Command;L" + P + "Item;)V", c, it);
            return true;
        }

        public static void DismissAlert(JThread t, MidpRuntime rt, AlertObj a)
        {
            if (rt.Current != a) return;
            var next = a.Next ?? rt.PrevDisplayable;
            if (next != null && next != a) rt.SetCurrent(t, next);
        }

        public static void SetCurrentItem(JThread t, MidpRuntime rt, ItemObj it)
        {
            if (it.Owner == null) throw new JavaThrow("java/lang/IllegalStateException");
            var f = it.Owner as FormObj;
            if (f != null) lock (f.Items) f.Focus = f.Items.IndexOf(it);
            rt.SetCurrent(t, it.Owner);
        }

        static void StateChanged(JThread t, ItemObj it)
        {
            var f = it.Owner as FormObj;
            if (f != null && f.StateListener != null)
                t.CallVirtual(f.StateListener, "itemStateChanged(L" + P + "Item;)V", it);
        }

        public static List<string> Wrap(FontObj f, string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                int start = 0;
                if (para.Length == 0) { lines.Add(""); continue; }
                while (start < para.Length)
                {
                    int w = 0, i = start, lastSpace = -1;
                    while (i < para.Length)
                    {
                        int cw = f.CharWidth(para[i]);
                        if (w + cw > width && i > start) break;
                        if (para[i] == ' ') lastSpace = i;
                        w += cw; i++;
                    }
                    if (i < para.Length && lastSpace > start) i = lastSpace + 1;
                    lines.Add(para.Substring(start, i - start).TrimEnd());
                    start = i;
                }
            }
            return lines;
        }

        struct Row
        {
            public int Item, Sub, Y, H;
        }

        static readonly int Fg = unchecked((int)0xFF000000), Bg = unchecked((int)0xFFFFFFFF), Accent = unchecked((int)0xFF2A6FD6), Mid = unchecked((int)0xFF808080);

        public static void Render(MidpRuntime rt)
        {
            var d = rt.Current;
            var g = rt.SysGraphics(rt.Screen);
            g.Color = Bg;
            Gfx.FillRect(g, 0, 0, rt.W, rt.H);
            int th = TitleH(rt);
            g.Color = unchecked((int)0xFF1E3D73);
            Gfx.FillRect(g, 0, 0, rt.W, th);
            g.Color = -1;
            g.Font = rt.SysBold;
            var title = d.Title ?? "";
            Gfx.DrawString(g, title, 0, title.Length, 4, 2, Gfx.TOP | Gfx.LEFT);
            int top = th;
            if (d.Ticker != null)
            {
                int tkh = rt.SysSmall.Height + 2;
                g.Color = unchecked((int)0xFFFFF4C0);
                Gfx.FillRect(g, 0, top, rt.W, tkh);
                g.Color = Fg; g.Font = rt.SysSmall;
                var tx = d.Ticker.Text ?? "";
                int tw = rt.SysSmall.StringWidth(tx, 0, tx.Length) + rt.W;
                int off = rt.W - rt.tickerOff % Math.Max(1, tw);
                g.SetClip(0, top, rt.W, tkh);
                Gfx.DrawString(g, tx, 0, tx.Length, off, top + 1, Gfx.TOP | Gfx.LEFT);
                g.SetClip(0, 0, rt.W, rt.H);
                top += tkh;
            }
            int bottom = rt.H - rt.BarH;
            g.SetClip(0, top, rt.W, bottom - top);
            var sc = d as ScreenObj;
            if (d is FormObj) RenderForm(rt, g, (FormObj)d, top, bottom);
            else if (d is ListObj) RenderList(rt, g, (ListObj)d, top, bottom);
            else if (d is TextBoxObj) RenderTextBox(rt, g, (TextBoxObj)d, top, bottom);
            else if (d is AlertObj) RenderAlert(rt, g, (AlertObj)d, top, bottom);
            rt.DrawSoftBar(d);
        }

        static List<Row> LayoutForm(MidpRuntime rt, FormObj f, out List<ItemObj> items)
        {
            lock (f.Items) items = new List<ItemObj>(f.Items);
            var rows = new List<Row>();
            int y = 2, w = rt.W - 8;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int h = 0;
                if (!string.IsNullOrEmpty(it.Label)) h += Wrap(rt.SysBold, it.Label, w).Count * rt.SysBold.Height;
                switch (it.Kind)
                {
                    case KString: h += Wrap(it.Font ?? rt.SysFont, it.Text, w - (it.Appearance != 0 ? 8 : 0)).Count * (it.Font ?? rt.SysFont).Height + (it.Appearance == 2 ? 6 : 0); break;
                    case KImage: h += it.Img != null ? it.Img.S.H + 2 : 0; break;
                    case KText: h += rt.SysFont.Height + 6; break;
                    case KGauge: h += 12; break;
                    case KDate: h += rt.SysFont.Height + 6; break;
                    case KSpacer: h += Math.Max(0, it.MinH); break;
                    case KCustom: h += Math.Max(it.MinH, 16) + 2; break;
                    case KChoice:
                        {
                            int lh = 0;
                            if (it.ChoiceType == 4) lh = rt.SysFont.Height + 6;
                            else foreach (var e in it.Els) lh += Math.Max(rt.SysFont.Height, e.Img != null ? e.Img.S.H : 0) + 2;
                            h += lh;
                            break;
                        }
                }
                rows.Add(new Row { Item = i, Y = y, H = h + 4 });
                y += h + 4;
            }
            return rows;
        }

        static void RenderForm(MidpRuntime rt, GraphicsObj g, FormObj f, int top, int bottom)
        {
            List<ItemObj> items;
            var rows = LayoutForm(rt, f, out items);
            int viewH = bottom - top;
            if (f.Focus >= 0 && f.Focus < rows.Count)
            {
                var r = rows[f.Focus];
                int sy = r.Y, ey = r.Y + r.H;
                if (items[f.Focus].Kind == KChoice && items[f.Focus].ChoiceType != 4)
                {
                    int oy = r.Y + (string.IsNullOrEmpty(items[f.Focus].Label) ? 0 : rt.SysBold.Height);
                    for (int k = 0; k < f.Sub && k < items[f.Focus].Els.Count; k++) oy += Math.Max(rt.SysFont.Height, items[f.Focus].Els[k].Img != null ? items[f.Focus].Els[k].Img.S.H : 0) + 2;
                    sy = oy; ey = oy + rt.SysFont.Height + 2;
                }
                if (sy < f.Scroll) f.Scroll = sy;
                if (ey > f.Scroll + viewH) f.Scroll = Math.Min(sy, ey - viewH);
            }
            int total = rows.Count > 0 ? rows[rows.Count - 1].Y + rows[rows.Count - 1].H : 0;
            f.Scroll = Math.Max(0, Math.Min(f.Scroll, Math.Max(0, total - viewH)));
            int w = rt.W - 8;
            for (int i = 0; i < rows.Count; i++)
            {
                var it = items[i];
                int y = top + rows[i].Y - f.Scroll;
                if (y > bottom || y + rows[i].H < top) continue;
                bool foc = i == f.Focus && Focusable(it);
                int x = 4;
                if (foc && it.Kind != KChoice) { g.Color = unchecked((int)0xFFE3ECFA); Gfx.FillRect(g, 0, y - 1, rt.W, rows[i].H - 2); }
                if (!string.IsNullOrEmpty(it.Label))
                {
                    g.Font = rt.SysBold; g.Color = Fg;
                    foreach (var ln in Wrap(rt.SysBold, it.Label, w)) { Gfx.DrawString(g, ln, 0, ln.Length, x, y, 20); y += rt.SysBold.Height; }
                }
                switch (it.Kind)
                {
                    case KString:
                        {
                            var fnt = it.Font ?? rt.SysFont;
                            g.Font = fnt;
                            var lines = Wrap(fnt, it.Text, w - (it.Appearance != 0 ? 8 : 0));
                            if (it.Appearance == 2)
                            {
                                g.Color = unchecked((int)0xFFDDDDDD);
                                Gfx.FillRoundRect(g, x, y, w, lines.Count * fnt.Height + 4, 6, 6);
                                g.Color = Mid; Gfx.DrawRoundRect(g, x, y, w, lines.Count * fnt.Height + 4, 6, 6);
                                y += 2;
                            }
                            g.Color = it.Appearance == 1 ? Accent : Fg;
                            foreach (var ln in lines)
                            {
                                Gfx.DrawString(g, ln, 0, ln.Length, x + (it.Appearance != 0 ? 4 : 0), y, 20);
                                if (it.Appearance == 1) Gfx.DrawLine(g, x + 4, y + fnt.Ascent + 1, x + 4 + fnt.StringWidth(ln, 0, ln.Length), y + fnt.Ascent + 1);
                                y += fnt.Height;
                            }
                            break;
                        }
                    case KImage:
                        if (it.Img != null)
                        {
                            int ix = (it.Layout & 3) == 3 ? rt.W / 2 - it.Img.S.W / 2 : (it.Layout & 3) == 2 ? rt.W - 4 - it.Img.S.W : x;
                            Gfx.DrawImage(g, it.Img.S, ix, y, 20);
                        }
                        break;
                    case KText:
                    case KDate:
                        {
                            g.Color = -1; Gfx.FillRect(g, x, y, w, rt.SysFont.Height + 4);
                            g.Color = foc ? Accent : Mid; Gfx.DrawRect(g, x, y, w, rt.SysFont.Height + 4);
                            g.Color = Fg; g.Font = rt.SysFont;
                            string txt = it.Kind == KDate ? DateText(it) : (it.Constraints & 0x10000) != 0 ? new string('*', (it.Text ?? "").Length) : it.Text ?? "";
                            g.ClipRect(x + 2, y, w - 4, rt.SysFont.Height + 4);
                            Gfx.DrawString(g, txt, 0, txt.Length, x + 3, y + 2, 20);
                            g.SetClip(0, top, rt.W, bottom - top);
                            break;
                        }
                    case KGauge:
                        {
                            g.Color = Mid; Gfx.DrawRect(g, x, y + 1, w, 8);
                            int fill = it.Max > 0 ? (w - 2) * Math.Max(0, Math.Min(it.Value, it.Max)) / it.Max : (int)(Rt.Mono() / 50 % (w - 2));
                            g.Color = Accent; Gfx.FillRect(g, x + 1, y + 2, fill, 7);
                            break;
                        }
                    case KChoice:
                        {
                            g.Font = rt.SysFont;
                            if (it.ChoiceType == 4)
                            {
                                int s = SelIndex(it.Els);
                                var txt = s >= 0 ? it.Els[s].Text : "";
                                g.Color = foc ? unchecked((int)0xFFE3ECFA) : -1; Gfx.FillRect(g, x, y, w, rt.SysFont.Height + 4);
                                g.Color = foc ? Accent : Mid; Gfx.DrawRect(g, x, y, w, rt.SysFont.Height + 4);
                                g.Color = Fg; Gfx.DrawString(g, txt, 0, txt.Length, x + 3, y + 2, 20);
                                Gfx.FillTriangle(g, x + w - 12, y + 6, x + w - 4, y + 6, x + w - 8, y + 11);
                                break;
                            }
                            for (int k = 0; k < it.Els.Count; k++)
                            {
                                var e = it.Els[k];
                                int eh = Math.Max(rt.SysFont.Height, e.Img != null ? e.Img.S.H : 0) + 2;
                                if (foc && k == f.Sub) { g.Color = unchecked((int)0xFFE3ECFA); Gfx.FillRect(g, 0, y, rt.W, eh); }
                                DrawMark(g, it.ChoiceType, e.Sel, x, y + (eh - 10) / 2);
                                int tx = x + 14;
                                if (e.Img != null) { Gfx.DrawImage(g, e.Img.S, tx, y + 1, 20); tx += e.Img.S.W + 3; }
                                g.Color = Fg;
                                Gfx.DrawString(g, e.Text ?? "", 0, (e.Text ?? "").Length, tx, y + (eh - rt.SysFont.Height) / 2, 20);
                                y += eh;
                            }
                            break;
                        }
                    case KCustom:
                        {
                            int cw = w, ch = Math.Max(it.MinH, 16);
                            var cg = (GraphicsObj)rt.Vm.NewObject(rt.Vm.LoadClass(P + "Graphics"));
                            cg.Attach(rt.Screen, x, Math.Max(y, top), x + cw, Math.Min(y + ch, bottom));
                            cg.Tx = x; cg.Ty = y;
                            cg.Font = rt.SysFont;
                            var t = JThread.Current;
                            if (t != null)
                            {
                                try { t.CallVirtual(it, "paint(L" + P + "Graphics;II)V", cg, cw, ch); }
                                catch (JavaException e) { Library.ReportUncaught(t, e.Obj); }
                            }
                            break;
                        }
                }
            }
            if (total > viewH) ScrollBar(rt, g, top, viewH, f.Scroll, total);
        }

        static void ScrollBar(MidpRuntime rt, GraphicsObj g, int top, int viewH, int scroll, int total)
        {
            int bh = Math.Max(8, viewH * viewH / total);
            int by = top + (viewH - bh) * scroll / Math.Max(1, total - viewH);
            g.Color = unchecked((int)0xFFC0C0C0); Gfx.FillRect(g, rt.W - 3, top, 3, viewH);
            g.Color = Mid; Gfx.FillRect(g, rt.W - 3, by, 3, bh);
        }

        static void DrawMark(GraphicsObj g, int type, bool sel, int x, int y)
        {
            g.Color = Mid;
            if (type == 2)
            {
                Gfx.DrawRect(g, x, y, 9, 9);
                if (sel) { g.Color = Accent; Gfx.FillRect(g, x + 2, y + 2, 6, 6); }
            }
            else if (type == 1)
            {
                Gfx.DrawArc(g, x, y, 9, 9, 0, 360);
                if (sel) { g.Color = Accent; Gfx.FillArc(g, x + 2, y + 2, 6, 6, 0, 360); }
            }
        }

        static string DateText(ItemObj it)
        {
            if (it.Date == long.MinValue) return "";
            var d = DateTimeOffset.FromUnixTimeMilliseconds(it.Date).ToLocalTime();
            switch (it.Mode)
            {
                case 1: return d.ToString("yyyy-MM-dd");
                case 2: return d.ToString("HH:mm");
                default: return d.ToString("yyyy-MM-dd HH:mm");
            }
        }

        static int ElH(MidpRuntime rt, ChoiceEl e) => Math.Max(rt.SysFont.Height, e.Img != null ? e.Img.S.H : 0) + 4;

        static void RenderList(MidpRuntime rt, GraphicsObj g, ListObj l, int top, int bottom)
        {
            ChoiceEl[] els;
            lock (l.Els) els = l.Els.ToArray();
            int viewH = bottom - top, y = 0, total = 0;
            var ys = new int[els.Length];
            for (int i = 0; i < els.Length; i++) { ys[i] = total; total += ElH(rt, els[i]); }
            if (l.Focus >= 0 && l.Focus < els.Length)
            {
                if (ys[l.Focus] < l.Scroll) l.Scroll = ys[l.Focus];
                if (ys[l.Focus] + ElH(rt, els[l.Focus]) > l.Scroll + viewH) l.Scroll = ys[l.Focus] + ElH(rt, els[l.Focus]) - viewH;
            }
            l.Scroll = Math.Max(0, Math.Min(l.Scroll, Math.Max(0, total - viewH)));
            g.Font = rt.SysFont;
            for (int i = 0; i < els.Length; i++)
            {
                var e = els[i];
                int eh = ElH(rt, e);
                y = top + ys[i] - l.Scroll;
                if (y > bottom || y + eh < top) continue;
                if (i == l.Focus) { g.Color = Accent; Gfx.FillRect(g, 0, y, rt.W, eh); }
                int x = 4;
                if (l.Type != 3) { DrawMark(g, l.Type, e.Sel, x, y + (eh - 10) / 2); x += 14; }
                if (e.Img != null) { Gfx.DrawImage(g, e.Img.S, x, y + 2, 20); x += e.Img.S.W + 4; }
                g.Color = i == l.Focus ? -1 : Fg;
                var txt = e.Text ?? "";
                Gfx.DrawString(g, txt, 0, txt.Length, x, y + (eh - rt.SysFont.Height) / 2, 20);
            }
            if (total > viewH) ScrollBar(rt, g, top, viewH, l.Scroll, total);
        }

        static void RenderTextBox(MidpRuntime rt, GraphicsObj g, TextBoxObj tb, int top, int bottom)
        {
            g.Color = Mid; Gfx.DrawRect(g, 2, top + 2, rt.W - 5, bottom - top - 5);
            g.Font = rt.SysFont; g.Color = Fg;
            var txt = (tb.Constraints & 0x10000) != 0 ? new string('*', tb.Text.Length) : tb.Text;
            int y = top + 4;
            foreach (var ln in Wrap(rt.SysFont, txt, rt.W - 12)) { Gfx.DrawString(g, ln, 0, ln.Length, 6, y, 20); y += rt.SysFont.Height; }
            g.Font = rt.SysSmall; g.Color = Mid;
            var cnt = tb.Text.Length + "/" + tb.Max;
            Gfx.DrawString(g, cnt, 0, cnt.Length, rt.W - 6, bottom - 4, Gfx.BOTTOM | Gfx.RIGHT);
        }

        static void RenderAlert(MidpRuntime rt, GraphicsObj g, AlertObj a, int top, int bottom)
        {
            int y = top + 6;
            if (a.Img != null) { Gfx.DrawImage(g, a.Img.S, rt.W / 2, y, Gfx.TOP | Gfx.HCENTER); y += a.Img.S.H + 4; }
            g.Font = rt.SysFont; g.Color = Fg;
            foreach (var ln in Wrap(rt.SysFont, a.Text ?? "", rt.W - 12))
            {
                Gfx.DrawString(g, ln, 0, ln.Length, rt.W / 2, y, Gfx.TOP | Gfx.HCENTER);
                y += rt.SysFont.Height;
            }
            if (a.Indicator != null)
            {
                y += 6;
                g.Color = Mid; Gfx.DrawRect(g, 10, y, rt.W - 20, 8);
                var it = a.Indicator;
                int fill = it.Max > 0 ? (rt.W - 22) * Math.Max(0, Math.Min(it.Value, it.Max)) / it.Max : (int)(Rt.Mono() / 40 % (rt.W - 22));
                g.Color = Accent; Gfx.FillRect(g, 11, y + 1, fill, 7);
            }
        }

        static int SelIndex(List<ChoiceEl> els)
        {
            for (int i = 0; i < els.Count; i++) if (els[i].Sel) return i;
            return -1;
        }

        static void Choose(List<ChoiceEl> els, int type, int i)
        {
            if (i < 0 || i >= els.Count) return;
            if (type == 2) els[i].Sel = !els[i].Sel;
            else for (int k = 0; k < els.Count; k++) els[k].Sel = k == i;
        }

        public static void Key(JThread t, MidpRuntime rt, int code, bool repeat)
        {
            var d = rt.Current;
            int a = Keys.GameAction(code);
            if (code >= '0' && code <= '9' && code != '2' && code != '8' && code != '4' && code != '6' && code != '5') a = 0;
            if (d is ListObj)
            {
                var l = (ListObj)d;
                int n = l.Els.Count;
                if (n == 0) return;
                if (a == Keys.GUp) l.Focus = (l.Focus + n - 1) % n;
                else if (a == Keys.GDown) l.Focus = (l.Focus + 1) % n;
                else if (a == Keys.GFire && !repeat) ActivateList(t, rt, l);
                rt.RepaintAll();
            }
            else if (d is FormObj) FormKey(t, rt, (FormObj)d, code, a, repeat);
            else if (d is TextBoxObj)
            {
                if (a == Keys.GFire && !repeat) EditTextBox(t, rt, (TextBoxObj)d);
            }
            else if (d is AlertObj)
            {
                var al = (AlertObj)d;
                if (a == Keys.GFire && al.Commands.Count == 0) DismissAlert(t, rt, al);
            }
        }

        static void ActivateList(JThread t, MidpRuntime rt, ListObj l)
        {
            if (l.Focus < 0 || l.Focus >= l.Els.Count) return;
            if (l.Type == 3)
            {
                for (int k = 0; k < l.Els.Count; k++) l.Els[k].Sel = k == l.Focus;
                var cmd = l.SelectCmd;
                if (cmd != null && l.Listener != null)
                    t.CallVirtual(l.Listener, "commandAction(L" + P + "Command;L" + P + "Displayable;)V", cmd, l);
            }
            else Choose(l.Els, l.Type, l.Focus);
            rt.RepaintAll();
        }

        static void FormKey(JThread t, MidpRuntime rt, FormObj f, int code, int a, bool repeat)
        {
            List<ItemObj> items;
            lock (f.Items) items = new List<ItemObj>(f.Items);
            if (items.Count == 0) return;
            var cur = f.Focus >= 0 && f.Focus < items.Count ? items[f.Focus] : null;
            if (cur != null && cur.Kind == KCustom && Focusable(cur) && !repeat)
            {
                t.CallVirtual(cur, "keyPressed(I)V", code);
            }
            if (a == Keys.GUp || a == Keys.GDown)
            {
                int dir = a == Keys.GUp ? -1 : 1;
                if (cur != null && cur.Kind == KChoice && cur.ChoiceType != 4)
                {
                    int ns = f.Sub + dir;
                    if (ns >= 0 && ns < cur.Els.Count) { f.Sub = ns; rt.RepaintAll(); return; }
                }
                int i = f.Focus + dir;
                while (i >= 0 && i < items.Count && !Focusable(items[i])) i += dir;
                if (i >= 0 && i < items.Count)
                {
                    f.Focus = i;
                    f.Sub = dir < 0 && items[i].Kind == KChoice ? Math.Max(0, items[i].Els.Count - 1) : 0;
                }
                else f.Scroll += dir * rt.SysFont.Height * 2;
                rt.RepaintAll();
                return;
            }
            if (cur == null || !Focusable(cur)) return;
            if ((a == Keys.GLeft || a == Keys.GRight) && cur.Kind == KGauge && cur.Interactive)
            {
                cur.Value = Math.Max(0, Math.Min(cur.Max, cur.Value + (a == Keys.GLeft ? -1 : 1)));
                StateChanged(t, cur);
                rt.RepaintAll();
                return;
            }
            if ((a == Keys.GLeft || a == Keys.GRight) && cur.Kind == KChoice && cur.ChoiceType == 4 && cur.Els.Count > 0)
            {
                int s = Math.Max(0, SelIndex(cur.Els));
                Choose(cur.Els, 4, (s + (a == Keys.GLeft ? cur.Els.Count - 1 : 1)) % cur.Els.Count);
                StateChanged(t, cur);
                rt.RepaintAll();
                return;
            }
            if (a == Keys.GFire && !repeat) Activate(t, rt, f, cur);
        }

        static void Activate(JThread t, MidpRuntime rt, FormObj f, ItemObj it)
        {
            switch (it.Kind)
            {
                case KText:
                    rt.Host.RequestText(it.Label ?? f.Title ?? "", it.Text ?? "", it.Max, it.Constraints, s =>
                    {
                        if (s == null) return;
                        rt.Post(et => { it.Text = Clip(s, it.Max); StateChanged(et, it); rt.RepaintAll(); });
                    });
                    return;
                case KChoice:
                    if (it.ChoiceType == 4) Choose(it.Els, 4, (Math.Max(0, SelIndex(it.Els)) + 1) % Math.Max(1, it.Els.Count));
                    else Choose(it.Els, it.ChoiceType, f.Sub);
                    StateChanged(t, it);
                    rt.RepaintAll();
                    return;
                case KDate:
                    rt.Host.RequestText(it.Label ?? "Date", DateText(it), 32, 0, s =>
                    {
                        DateTimeOffset dt;
                        if (s == null || !DateTimeOffset.TryParse(s, out dt)) return;
                        rt.Post(et => { it.Date = dt.ToUnixTimeMilliseconds(); StateChanged(et, it); rt.RepaintAll(); });
                    });
                    return;
            }
            var cmd = it.Default;
            if (cmd == null) lock (it.Commands) if (it.Commands.Count == 1) cmd = it.Commands[0];
            if (cmd != null && it.CmdListener != null)
                t.CallVirtual(it.CmdListener, "commandAction(L" + P + "Command;L" + P + "Item;)V", cmd, it);
        }

        static string Clip(string s, int max) => max > 0 && s.Length > max ? s.Substring(0, max) : s;

        static void EditTextBox(JThread t, MidpRuntime rt, TextBoxObj tb)
        {
            rt.Host.RequestText(tb.Title ?? "", tb.Text, tb.Max, tb.Constraints, s =>
            {
                if (s == null) return;
                rt.Post(et => { tb.Text = Clip(s, tb.Max); rt.RepaintAll(); });
            });
        }

        public static void Pointer(JThread t, MidpRuntime rt, int kind, int x, int y)
        {
            if (kind != 0) return;
            var d = rt.Current;
            if (y >= rt.H - rt.BarH)
            {
                int code = x < rt.W / 3 ? Keys.SoftL : x > rt.W * 2 / 3 ? Keys.SoftR : Keys.Fire;
                rt.KeyDown(code); rt.KeyUp(code);
                return;
            }
            int top = TitleH(rt) + (d.Ticker != null ? rt.SysSmall.Height + 2 : 0);
            if (y < top) return;
            if (d is ListObj)
            {
                var l = (ListObj)d;
                int yy = top - l.Scroll;
                for (int i = 0; i < l.Els.Count; i++)
                {
                    int eh = ElH(rt, l.Els[i]);
                    if (y >= yy && y < yy + eh)
                    {
                        l.Focus = i;
                        ActivateList(t, rt, l);
                        return;
                    }
                    yy += eh;
                }
            }
            else if (d is FormObj)
            {
                var f = (FormObj)d;
                List<ItemObj> items;
                var rows = LayoutForm(rt, f, out items);
                for (int i = 0; i < rows.Count; i++)
                {
                    int ry = top + rows[i].Y - f.Scroll;
                    if (y < ry || y >= ry + rows[i].H) continue;
                    var it = items[i];
                    if (!Focusable(it)) return;
                    f.Focus = i;
                    if (it.Kind == KChoice && it.ChoiceType != 4)
                    {
                        int oy = ry + (string.IsNullOrEmpty(it.Label) ? 0 : Wrap(rt.SysBold, it.Label, rt.W - 8).Count * rt.SysBold.Height);
                        for (int k = 0; k < it.Els.Count; k++)
                        {
                            int eh = Math.Max(rt.SysFont.Height, it.Els[k].Img != null ? it.Els[k].Img.S.H : 0) + 2;
                            if (y >= oy && y < oy + eh) { f.Sub = k; break; }
                            oy += eh;
                        }
                    }
                    if (it.Kind == KCustom) t.CallVirtual(it, "pointerPressed(II)V", x - 4, y - ry);
                    else Activate(t, rt, f, it);
                    rt.RepaintAll();
                    return;
                }
            }
            else if (d is TextBoxObj) EditTextBox(t, rt, (TextBoxObj)d);
            else if (d is AlertObj && d.Commands.Count == 0) DismissAlert(t, rt, (AlertObj)d);
        }

        static void ChoiceMethods(ClassBuilder c, Func<JThread, int, List<ChoiceEl>> els, Func<JThread, int, int> type, bool isList)
        {
            Action<JThread, int> changed = (t, b) => { if (!isList) Dirty(t, I(t, b)); else Dirty(t, (DisplayableObj)t.R[b]); };
            c.M("append", "(Ljava/lang/String;L" + P + "Image;)I", (t, b) =>
            {
                var l = els(t, b);
                if (t.R[b + 1] == null) throw new NullReferenceException();
                lock (l)
                {
                    l.Add(new ChoiceEl { Text = S(t, b + 1), Img = (ImageObj)t.R[b + 2] });
                    if (type(t, b) != 2 && l.Count == 1) l[0].Sel = true;
                    t.RetI(b, l.Count - 1);
                }
                changed(t, b);
            })
             .M("insert", "(ILjava/lang/String;L" + P + "Image;)V", (t, b) =>
             {
                 var l = els(t, b); int i = t.I(b + 1);
                 lock (l)
                 {
                     if (i < 0 || i > l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                     l.Insert(i, new ChoiceEl { Text = S(t, b + 2), Img = (ImageObj)t.R[b + 3] });
                     if (type(t, b) != 2 && l.Count == 1) l[0].Sel = true;
                 }
                 changed(t, b);
             })
             .M("delete", "(I)V", (t, b) =>
             {
                 var l = els(t, b); int i = t.I(b + 1);
                 lock (l)
                 {
                     if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                     bool was = l[i].Sel;
                     l.RemoveAt(i);
                     if (was && type(t, b) != 2 && l.Count > 0) l[Math.Min(i, l.Count - 1)].Sel = true;
                 }
                 if (isList) { var lo = (ListObj)t.R[b]; if (lo.Focus >= lo.Els.Count) lo.Focus = Math.Max(0, lo.Els.Count - 1); }
                 changed(t, b);
             })
             .M("deleteAll", "()V", (t, b) => { var l = els(t, b); lock (l) l.Clear(); if (isList) ((ListObj)t.R[b]).Focus = 0; changed(t, b); })
             .M("set", "(ILjava/lang/String;L" + P + "Image;)V", (t, b) =>
             {
                 var l = els(t, b); int i = t.I(b + 1);
                 lock (l) { if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); l[i].Text = S(t, b + 2); l[i].Img = (ImageObj)t.R[b + 3]; }
                 changed(t, b);
             })
             .M("getString", "(I)Ljava/lang/String;", (t, b) => { var l = els(t, b); int i = t.I(b + 1); if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, l[i].Text); })
             .M("getImage", "(I)L" + P + "Image;", (t, b) => { var l = els(t, b); int i = t.I(b + 1); if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, l[i].Img); })
             .M("size", "()I", (t, b) => t.RetI(b, els(t, b).Count))
             .M("isSelected", "(I)Z", (t, b) => { var l = els(t, b); int i = t.I(b + 1); if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetZ(b, l[i].Sel); })
             .M("getSelectedIndex", "()I", (t, b) =>
             {
                 if (type(t, b) == 2) { t.RetI(b, -1); return; }
                 if (isList && type(t, b) == 3) { var lo = (ListObj)t.R[b]; t.RetI(b, lo.Els.Count == 0 ? -1 : lo.Focus); return; }
                 t.RetI(b, SelIndex(els(t, b)));
             })
             .M("setSelectedIndex", "(IZ)V", (t, b) =>
             {
                 var l = els(t, b); int i = t.I(b + 1); bool on = t.Z(b + 2);
                 if (i < 0 || i >= l.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                 if (type(t, b) == 2) l[i].Sel = on;
                 else if (on) { for (int k = 0; k < l.Count; k++) l[k].Sel = k == i; if (isList) ((ListObj)t.R[b]).Focus = i; }
                 changed(t, b);
             })
             .M("getSelectedFlags", "([Z)I", (t, b) =>
             {
                 var l = els(t, b); var a = (sbyte[])t.R[b + 1];
                 if (a.Length < l.Count) throw new JavaThrow("java/lang/IllegalArgumentException");
                 int n = 0;
                 for (int k = 0; k < a.Length; k++) { a[k] = (sbyte)(k < l.Count && l[k].Sel ? 1 : 0); n += a[k]; }
                 t.RetI(b, n);
             })
             .M("setSelectedFlags", "([Z)V", (t, b) =>
             {
                 var l = els(t, b); var a = (sbyte[])t.R[b + 1];
                 if (a.Length < l.Count) throw new JavaThrow("java/lang/IllegalArgumentException");
                 bool any = false;
                 for (int k = 0; k < l.Count; k++)
                 {
                     bool v = a[k] != 0 && (type(t, b) == 2 || !any);
                     l[k].Sel = v; any |= v;
                 }
                 if (!any && type(t, b) != 2 && l.Count > 0) l[0].Sel = true;
                 changed(t, b);
             })
             .M("setFitPolicy", "(I)V", (t, b) => { })
             .M("getFitPolicy", "()I", (t, b) => t.RetI(b, 0))
             .M("setFont", "(IL" + P + "Font;)V", (t, b) => { })
             .M("getFont", "(I)L" + P + "Font;", (t, b) => t.RetA(b, R(t).DefaultFont(t)));
        }

        static void InitChoices(JThread t, List<ChoiceEl> l, int type, object strs, object imgs)
        {
            var sa = strs as ObjArray;
            if (sa == null) throw new NullReferenceException();
            var ia = imgs as ObjArray;
            if (ia != null && ia.Data.Length != sa.Data.Length) throw new JavaThrow("java/lang/IllegalArgumentException");
            for (int i = 0; i < sa.Data.Length; i++)
            {
                if (sa.Data[i] == null) throw new NullReferenceException();
                l.Add(new ChoiceEl { Text = (string)sa.Data[i], Img = ia != null ? (ImageObj)ia.Data[i] : null });
            }
            if (type != 2 && l.Count > 0) l[0].Sel = true;
        }

        static int Owned(JThread t, ItemObj it)
        {
            if (it == null) throw new NullReferenceException();
            if (it.Owner != null) throw new JavaThrow("java/lang/IllegalStateException", "item already owned");
            return 0;
        }

        static ItemObj NewItem(JThread t, string cls, int kind)
        {
            var it = (ItemObj)t.Vm.NewObject(t.Vm.LoadClass(P + cls));
            it.Kind = kind;
            return it;
        }

        public static void Register(NativeRegistry n)
        {
            n.Define(P + "Screen", P + "Displayable", c => c.M("<init>", "()V", (t, b) => { }), Rt.F<ScreenObj>(), access: 0x0401);

            n.Define(P + "Ticker", "java/lang/Object", c => c
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => ((TickerObj)t.R[b]).Text = S(t, b + 1) ?? throw new NullReferenceException())
                .M("getString", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((TickerObj)t.R[b]).Text))
                .M("setString", "(Ljava/lang/String;)V", (t, b) => ((TickerObj)t.R[b]).Text = S(t, b + 1) ?? throw new NullReferenceException()),
                Rt.F<TickerObj>());

            n.Define(P + "Form", P + "Screen", c => c
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => ((FormObj)t.R[b]).Title = S(t, b + 1))
                .M("<init>", "(Ljava/lang/String;[L" + P + "Item;)V", (t, b) =>
                {
                    var f = (FormObj)t.R[b];
                    f.Title = S(t, b + 1);
                    var arr = t.R[b + 2] as ObjArray;
                    if (arr != null) foreach (ItemObj it in arr.Data) { Owned(t, it); it.Owner = f; f.Items.Add(it); }
                })
                .M("append", "(L" + P + "Item;)I", (t, b) =>
                {
                    var f = (FormObj)t.R[b]; var it = (ItemObj)t.R[b + 1];
                    Owned(t, it);
                    it.Owner = f;
                    lock (f.Items) { f.Items.Add(it); t.RetI(b, f.Items.Count - 1); }
                    Dirty(t, f);
                })
                .M("append", "(Ljava/lang/String;)I", (t, b) =>
                {
                    var f = (FormObj)t.R[b];
                    var it = NewItem(t, "StringItem", KString);
                    it.Text = S(t, b + 1) ?? throw new NullReferenceException();
                    it.Owner = f;
                    lock (f.Items) { f.Items.Add(it); t.RetI(b, f.Items.Count - 1); }
                    Dirty(t, f);
                })
                .M("append", "(L" + P + "Image;)I", (t, b) =>
                {
                    var f = (FormObj)t.R[b];
                    var it = NewItem(t, "ImageItem", KImage);
                    it.Img = (ImageObj)t.R[b + 1] ?? throw new NullReferenceException();
                    it.Owner = f;
                    lock (f.Items) { f.Items.Add(it); t.RetI(b, f.Items.Count - 1); }
                    Dirty(t, f);
                })
                .M("insert", "(IL" + P + "Item;)V", (t, b) =>
                {
                    var f = (FormObj)t.R[b]; var it = (ItemObj)t.R[b + 2]; int i = t.I(b + 1);
                    Owned(t, it);
                    lock (f.Items)
                    {
                        if (i < 0 || i > f.Items.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                        it.Owner = f; f.Items.Insert(i, it);
                    }
                    Dirty(t, f);
                })
                .M("set", "(IL" + P + "Item;)V", (t, b) =>
                {
                    var f = (FormObj)t.R[b]; var it = (ItemObj)t.R[b + 2]; int i = t.I(b + 1);
                    Owned(t, it);
                    lock (f.Items)
                    {
                        if (i < 0 || i >= f.Items.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                        f.Items[i].Owner = null; it.Owner = f; f.Items[i] = it;
                    }
                    Dirty(t, f);
                })
                .M("delete", "(I)V", (t, b) =>
                {
                    var f = (FormObj)t.R[b]; int i = t.I(b + 1);
                    lock (f.Items)
                    {
                        if (i < 0 || i >= f.Items.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
                        f.Items[i].Owner = null; f.Items.RemoveAt(i);
                        if (f.Focus >= f.Items.Count) f.Focus = Math.Max(0, f.Items.Count - 1);
                    }
                    Dirty(t, f);
                })
                .M("deleteAll", "()V", (t, b) => { var f = (FormObj)t.R[b]; lock (f.Items) { foreach (var it in f.Items) it.Owner = null; f.Items.Clear(); f.Focus = 0; f.Scroll = 0; } Dirty(t, f); })
                .M("get", "(I)L" + P + "Item;", (t, b) =>
                {
                    var f = (FormObj)t.R[b]; int i = t.I(b + 1);
                    lock (f.Items) { if (i < 0 || i >= f.Items.Count) throw new JavaThrow("java/lang/IndexOutOfBoundsException"); t.RetA(b, f.Items[i]); }
                })
                .M("size", "()I", (t, b) => t.RetI(b, ((FormObj)t.R[b]).Items.Count))
                .M("setItemStateListener", "(L" + P + "ItemStateListener;)V", (t, b) => ((FormObj)t.R[b]).StateListener = t.R[b + 1]),
                Rt.F<FormObj>());

            n.Define(P + "Item", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getLabel", "()Ljava/lang/String;", (t, b) => t.RetA(b, I(t, b).Label))
                .M("setLabel", "(Ljava/lang/String;)V", (t, b) => { I(t, b).Label = S(t, b + 1); Dirty(t, I(t, b)); })
                .M("getLayout", "()I", (t, b) => t.RetI(b, I(t, b).Layout))
                .M("setLayout", "(I)V", (t, b) => { I(t, b).Layout = t.I(b + 1); Dirty(t, I(t, b)); })
                .M("addCommand", "(L" + P + "Command;)V", (t, b) => { var it = I(t, b); var cmd = (CommandObj)t.R[b + 1] ?? throw new NullReferenceException(); lock (it.Commands) if (!it.Commands.Contains(cmd)) it.Commands.Add(cmd); Dirty(t, it); })
                .M("removeCommand", "(L" + P + "Command;)V", (t, b) => { var it = I(t, b); lock (it.Commands) it.Commands.Remove((CommandObj)t.R[b + 1]); if (it.Default == t.R[b + 1]) it.Default = null; Dirty(t, it); })
                .M("setItemCommandListener", "(L" + P + "ItemCommandListener;)V", (t, b) => I(t, b).CmdListener = t.R[b + 1])
                .M("setDefaultCommand", "(L" + P + "Command;)V", (t, b) =>
                {
                    var it = I(t, b); var cmd = (CommandObj)t.R[b + 1];
                    it.Default = cmd;
                    if (cmd != null) lock (it.Commands) if (!it.Commands.Contains(cmd)) it.Commands.Add(cmd);
                    Dirty(t, it);
                })
                .M("getPreferredWidth", "()I", (t, b) => t.RetI(b, I(t, b).PrefW > 0 ? I(t, b).PrefW : R(t).W - 8))
                .M("getPreferredHeight", "()I", (t, b) => t.RetI(b, I(t, b).PrefH > 0 ? I(t, b).PrefH : R(t).SysFont.Height + 4))
                .M("getMinimumWidth", "()I", (t, b) => t.RetI(b, 16))
                .M("getMinimumHeight", "()I", (t, b) => t.RetI(b, R(t).SysFont.Height))
                .M("setPreferredSize", "(II)V", (t, b) => { I(t, b).PrefW = t.I(b + 1); I(t, b).PrefH = t.I(b + 2); })
                .M("notifyStateChanged", "()V", (t, b) => StateChanged(t, I(t, b))),
                Rt.F<ItemObj>(), access: 0x0401);

            n.Define(P + "StringItem", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;)V", (t, b) => { var it = I(t, b); it.Kind = KString; it.Label = S(t, b + 1); it.Text = S(t, b + 2); })
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;I)V", (t, b) => { var it = I(t, b); it.Kind = KString; it.Label = S(t, b + 1); it.Text = S(t, b + 2); it.Appearance = t.I(b + 3); })
                .M("getText", "()Ljava/lang/String;", (t, b) => t.RetA(b, I(t, b).Text))
                .M("setText", "(Ljava/lang/String;)V", (t, b) => { I(t, b).Text = S(t, b + 1); Dirty(t, I(t, b)); })
                .M("getAppearanceMode", "()I", (t, b) => t.RetI(b, I(t, b).Appearance))
                .M("setFont", "(L" + P + "Font;)V", (t, b) => { I(t, b).Font = (FontObj)t.R[b + 1]; Dirty(t, I(t, b)); })
                .M("getFont", "()L" + P + "Font;", (t, b) => t.RetA(b, I(t, b).Font ?? R(t).DefaultFont(t))));

            n.Define(P + "ImageItem", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;L" + P + "Image;ILjava/lang/String;)V", (t, b) => { var it = I(t, b); it.Kind = KImage; it.Label = S(t, b + 1); it.Img = (ImageObj)t.R[b + 2]; it.Layout = t.I(b + 3); it.Text = S(t, b + 4); })
                .M("<init>", "(Ljava/lang/String;L" + P + "Image;ILjava/lang/String;I)V", (t, b) => { var it = I(t, b); it.Kind = KImage; it.Label = S(t, b + 1); it.Img = (ImageObj)t.R[b + 2]; it.Layout = t.I(b + 3); it.Text = S(t, b + 4); it.Appearance = t.I(b + 5); })
                .M("getImage", "()L" + P + "Image;", (t, b) => t.RetA(b, I(t, b).Img))
                .M("setImage", "(L" + P + "Image;)V", (t, b) => { I(t, b).Img = (ImageObj)t.R[b + 1]; Dirty(t, I(t, b)); })
                .M("getAltText", "()Ljava/lang/String;", (t, b) => t.RetA(b, I(t, b).Text))
                .M("setAltText", "(Ljava/lang/String;)V", (t, b) => I(t, b).Text = S(t, b + 1))
                .M("getAppearanceMode", "()I", (t, b) => t.RetI(b, I(t, b).Appearance)));

            NativeFn tfGet = (t, b) => t.RetA(b, I(t, b).Text ?? "");
            n.Define(P + "TextField", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;II)V", (t, b) =>
                {
                    var it = I(t, b); it.Kind = KText; it.Label = S(t, b + 1); it.Max = t.I(b + 3); it.Constraints = t.I(b + 4);
                    if (it.Max <= 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    var s = S(t, b + 2) ?? "";
                    if (s.Length > it.Max) throw new JavaThrow("java/lang/IllegalArgumentException");
                    it.Text = s;
                })
                .M("getString", "()Ljava/lang/String;", tfGet)
                .M("setString", "(Ljava/lang/String;)V", (t, b) => { var it = I(t, b); var s = S(t, b + 1) ?? ""; if (s.Length > it.Max) throw new JavaThrow("java/lang/IllegalArgumentException"); it.Text = s; Dirty(t, it); })
                .M("getChars", "([C)I", (t, b) => { var s = I(t, b).Text ?? ""; var a = (char[])t.R[b + 1]; if (a.Length < s.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException"); s.CopyTo(0, a, 0, s.Length); t.RetI(b, s.Length); })
                .M("setChars", "([CII)V", (t, b) => { var a = (char[])t.R[b + 1]; I(t, b).Text = a == null ? "" : new string(a, t.I(b + 2), t.I(b + 3)); Dirty(t, I(t, b)); })
                .M("insert", "(Ljava/lang/String;I)V", (t, b) => { var it = I(t, b); var s = it.Text ?? ""; int p = Math.Max(0, Math.Min(s.Length, t.I(b + 2))); var ns = s.Insert(p, S(t, b + 1)); if (ns.Length > it.Max) throw new JavaThrow("java/lang/IllegalArgumentException"); it.Text = ns; Dirty(t, it); })
                .M("insert", "([CIII)V", (t, b) => { var it = I(t, b); var s = it.Text ?? ""; int p = Math.Max(0, Math.Min(s.Length, t.I(b + 4))); it.Text = s.Insert(p, new string((char[])t.R[b + 1], t.I(b + 2), t.I(b + 3))); Dirty(t, it); })
                .M("delete", "(II)V", (t, b) => { var it = I(t, b); var s = it.Text ?? ""; int o = t.I(b + 1), l = t.I(b + 2); if (o < 0 || l < 0 || o + l > s.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); it.Text = s.Remove(o, l); Dirty(t, it); })
                .M("getMaxSize", "()I", (t, b) => t.RetI(b, I(t, b).Max))
                .M("setMaxSize", "(I)I", (t, b) => { var it = I(t, b); it.Max = Math.Max(1, t.I(b + 1)); if ((it.Text ?? "").Length > it.Max) it.Text = it.Text.Substring(0, it.Max); t.RetI(b, it.Max); })
                .M("size", "()I", (t, b) => t.RetI(b, (I(t, b).Text ?? "").Length))
                .M("getCaretPosition", "()I", (t, b) => t.RetI(b, (I(t, b).Text ?? "").Length))
                .M("setConstraints", "(I)V", (t, b) => I(t, b).Constraints = t.I(b + 1))
                .M("getConstraints", "()I", (t, b) => t.RetI(b, I(t, b).Constraints))
                .M("setInitialInputMode", "(Ljava/lang/String;)V", (t, b) => { }));

            n.Define(P + "Gauge", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;ZII)V", (t, b) => { var it = I(t, b); it.Kind = KGauge; it.Label = S(t, b + 1); it.Interactive = t.Z(b + 2); it.Max = t.I(b + 3); it.Value = t.I(b + 4); })
                .M("getValue", "()I", (t, b) => t.RetI(b, I(t, b).Value))
                .M("setValue", "(I)V", (t, b) => { var it = I(t, b); it.Value = it.Max > 0 ? Math.Max(0, Math.Min(it.Max, t.I(b + 1))) : t.I(b + 1); Dirty(t, it); var al = R(t).Current as AlertObj; if (al != null && al.Indicator == it) R(t).RepaintAll(); })
                .M("getMaxValue", "()I", (t, b) => t.RetI(b, I(t, b).Max))
                .M("setMaxValue", "(I)V", (t, b) => { I(t, b).Max = t.I(b + 1); Dirty(t, I(t, b)); })
                .M("isInteractive", "()Z", (t, b) => t.RetZ(b, I(t, b).Interactive)));

            n.Define(P + "ChoiceGroup", P + "Item", c =>
            {
                c.M("<init>", "(Ljava/lang/String;I)V", (t, b) => { var it = I(t, b); it.Kind = KChoice; it.Label = S(t, b + 1); it.ChoiceType = t.I(b + 2); if (it.ChoiceType < 1 || it.ChoiceType > 4 || it.ChoiceType == 3) throw new JavaThrow("java/lang/IllegalArgumentException"); })
                 .M("<init>", "(Ljava/lang/String;I[Ljava/lang/String;[L" + P + "Image;)V", (t, b) =>
                 {
                     var it = I(t, b); it.Kind = KChoice; it.Label = S(t, b + 1); it.ChoiceType = t.I(b + 2);
                     InitChoices(t, it.Els, it.ChoiceType, t.R[b + 3], t.R[b + 4]);
                 });
                ChoiceMethods(c, (t, b) => I(t, b).Els, (t, b) => I(t, b).ChoiceType, false);
            }, null, new[] { P + "Choice" });

            n.Define(P + "Spacer", P + "Item", c => c
                .M("<init>", "(II)V", (t, b) => { var it = I(t, b); it.Kind = KSpacer; it.MinW = t.I(b + 1); it.MinH = t.I(b + 2); })
                .M("setMinimumSize", "(II)V", (t, b) => { var it = I(t, b); it.MinW = t.I(b + 1); it.MinH = t.I(b + 2); Dirty(t, it); }));

            n.Define(P + "DateField", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;I)V", (t, b) => { var it = I(t, b); it.Kind = KDate; it.Label = S(t, b + 1); it.Mode = t.I(b + 2); })
                .M("<init>", "(Ljava/lang/String;ILjava/util/TimeZone;)V", (t, b) => { var it = I(t, b); it.Kind = KDate; it.Label = S(t, b + 1); it.Mode = t.I(b + 2); })
                .M("getDate", "()Ljava/util/Date;", (t, b) =>
                {
                    var it = I(t, b);
                    if (it.Date == long.MinValue) { t.RetA(b, null); return; }
                    var d = (DateObj)t.Vm.NewObject(t.Vm.LoadClass("java/util/Date")); d.Ms = it.Date; t.RetA(b, d);
                })
                .M("setDate", "(Ljava/util/Date;)V", (t, b) => { var d = t.R[b + 1] as DateObj; I(t, b).Date = d == null ? long.MinValue : d.Ms; Dirty(t, I(t, b)); })
                .M("getInputMode", "()I", (t, b) => t.RetI(b, I(t, b).Mode))
                .M("setInputMode", "(I)V", (t, b) => I(t, b).Mode = t.I(b + 1)));

            n.Define(P + "CustomItem", P + "Item", c => c
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => { var it = I(t, b); it.Kind = KCustom; it.Label = S(t, b + 1); it.MinH = 24; })
                .M("repaint", "()V", (t, b) => Dirty(t, I(t, b)))
                .M("repaint", "(IIII)V", (t, b) => Dirty(t, I(t, b)))
                .M("invalidate", "()V", (t, b) =>
                {
                    var it = I(t, b);
                    try { it.MinH = t.CallVirtualInt(it, "getPrefContentHeight(I)I", -1); } catch (JavaException) { }
                    Dirty(t, it);
                })
                .M("getGameAction", "(I)I", (t, b) => t.RetI(b, Keys.GameAction(t.I(b + 1))))
                .M("getInteractionModes", "()I", (t, b) => t.RetI(b, 0x1F | 0x20 | 0x40))
                .M("keyPressed", "(I)V", (t, b) => { })
                .M("keyReleased", "(I)V", (t, b) => { })
                .M("keyRepeated", "(I)V", (t, b) => { })
                .M("pointerPressed", "(II)V", (t, b) => { })
                .M("pointerReleased", "(II)V", (t, b) => { })
                .M("pointerDragged", "(II)V", (t, b) => { })
                .M("showNotify", "()V", (t, b) => { })
                .M("hideNotify", "()V", (t, b) => { })
                .M("sizeChanged", "(II)V", (t, b) => { })
                .M("traverse", "(III[I)Z", (t, b) => t.RetZ(b, false))
                .M("traverseOut", "()V", (t, b) => { }), null, access: 0x0401);

            n.Define(P + "List", P + "Screen", c =>
            {
                c.Static("SELECT_COMMAND", "L" + P + "Command;", t =>
                {
                    var cmd = (CommandObj)t.Vm.NewObject(t.Vm.LoadClass(P + "Command"));
                    cmd.Label = ""; cmd.Type = 1; cmd.Priority = 0;
                    return cmd;
                })
                 .M("<init>", "(Ljava/lang/String;I)V", (t, b) => ListInit(t, b, null, null))
                 .M("<init>", "(Ljava/lang/String;I[Ljava/lang/String;[L" + P + "Image;)V", (t, b) => ListInit(t, b, t.R[b + 3], t.R[b + 4]))
                 .M("setSelectCommand", "(L" + P + "Command;)V", (t, b) =>
                 {
                     var l = (ListObj)t.R[b];
                     if (l.Type != 3) return;
                     var cmd = (CommandObj)t.R[b + 1];
                     var def = (CommandObj)t.Vm.LoadClass(P + "List").StaticO[0];
                     if (l.SelectCmd != null && l.SelectCmd != def) lock (l.Commands) l.Commands.Remove(l.SelectCmd);
                     l.SelectCmd = cmd;
                     if (cmd != null && cmd != def) lock (l.Commands) if (!l.Commands.Contains(cmd)) l.Commands.Add(cmd);
                 })
                 .M("removeCommand", "(L" + P + "Command;)V", (t, b) => { var l = (ListObj)t.R[b]; lock (l.Commands) l.Commands.Remove((CommandObj)t.R[b + 1]); if (l.SelectCmd == t.R[b + 1]) l.SelectCmd = null; Dirty(t, l); });
                ChoiceMethods(c, (t, b) => ((ListObj)t.R[b]).Els, (t, b) => ((ListObj)t.R[b]).Type, true);
            }, Rt.F<ListObj>(), new[] { P + "Choice" });

            n.Define(P + "TextBox", P + "Screen", c => c
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;II)V", (t, b) =>
                {
                    var tb = (TextBoxObj)t.R[b];
                    tb.Title = S(t, b + 1); tb.Max = t.I(b + 3); tb.Constraints = t.I(b + 4);
                    if (tb.Max <= 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    tb.Text = S(t, b + 2) ?? "";
                    if (tb.Text.Length > tb.Max) throw new JavaThrow("java/lang/IllegalArgumentException");
                })
                .M("getString", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((TextBoxObj)t.R[b]).Text))
                .M("setString", "(Ljava/lang/String;)V", (t, b) => { var tb = (TextBoxObj)t.R[b]; tb.Text = S(t, b + 1) ?? ""; if (tb.Text.Length > tb.Max) throw new JavaThrow("java/lang/IllegalArgumentException"); Dirty(t, tb); })
                .M("getChars", "([C)I", (t, b) => { var s = ((TextBoxObj)t.R[b]).Text; var a = (char[])t.R[b + 1]; if (a.Length < s.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException"); s.CopyTo(0, a, 0, s.Length); t.RetI(b, s.Length); })
                .M("setChars", "([CII)V", (t, b) => { var tb = (TextBoxObj)t.R[b]; var a = (char[])t.R[b + 1]; tb.Text = a == null ? "" : new string(a, t.I(b + 2), t.I(b + 3)); Dirty(t, tb); })
                .M("insert", "(Ljava/lang/String;I)V", (t, b) => { var tb = (TextBoxObj)t.R[b]; int p = Math.Max(0, Math.Min(tb.Text.Length, t.I(b + 2))); tb.Text = tb.Text.Insert(p, S(t, b + 1)); Dirty(t, tb); })
                .M("insert", "([CIII)V", (t, b) => { var tb = (TextBoxObj)t.R[b]; int p = Math.Max(0, Math.Min(tb.Text.Length, t.I(b + 4))); tb.Text = tb.Text.Insert(p, new string((char[])t.R[b + 1], t.I(b + 2), t.I(b + 3))); Dirty(t, tb); })
                .M("delete", "(II)V", (t, b) => { var tb = (TextBoxObj)t.R[b]; int o = t.I(b + 1), l = t.I(b + 2); if (o < 0 || l < 0 || o + l > tb.Text.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException"); tb.Text = tb.Text.Remove(o, l); Dirty(t, tb); })
                .M("getMaxSize", "()I", (t, b) => t.RetI(b, ((TextBoxObj)t.R[b]).Max))
                .M("setMaxSize", "(I)I", (t, b) => { var tb = (TextBoxObj)t.R[b]; tb.Max = Math.Max(1, t.I(b + 1)); if (tb.Text.Length > tb.Max) tb.Text = tb.Text.Substring(0, tb.Max); t.RetI(b, tb.Max); })
                .M("size", "()I", (t, b) => t.RetI(b, ((TextBoxObj)t.R[b]).Text.Length))
                .M("getCaretPosition", "()I", (t, b) => t.RetI(b, ((TextBoxObj)t.R[b]).Text.Length))
                .M("setConstraints", "(I)V", (t, b) => ((TextBoxObj)t.R[b]).Constraints = t.I(b + 1))
                .M("getConstraints", "()I", (t, b) => t.RetI(b, ((TextBoxObj)t.R[b]).Constraints))
                .M("setInitialInputMode", "(Ljava/lang/String;)V", (t, b) => { }),
                Rt.F<TextBoxObj>());

            n.Define(P + "AlertType", "java/lang/Object", c => c
                .Static("INFO", "L" + P + "AlertType;", t => new JObject(t.Vm.LoadClass(P + "AlertType")))
                .Static("WARNING", "L" + P + "AlertType;", t => new JObject(t.Vm.LoadClass(P + "AlertType")))
                .Static("ERROR", "L" + P + "AlertType;", t => new JObject(t.Vm.LoadClass(P + "AlertType")))
                .Static("ALARM", "L" + P + "AlertType;", t => new JObject(t.Vm.LoadClass(P + "AlertType")))
                .Static("CONFIRMATION", "L" + P + "AlertType;", t => new JObject(t.Vm.LoadClass(P + "AlertType")))
                .M("<init>", "()V", (t, b) => { })
                .M("playSound", "(L" + P + "Display;)Z", (t, b) => t.RetZ(b, true)));

            n.Define(P + "Alert", P + "Screen", c => c
                .Static("DISMISS_COMMAND", "L" + P + "Command;", t =>
                {
                    var cmd = (CommandObj)t.Vm.NewObject(t.Vm.LoadClass(P + "Command"));
                    cmd.Label = "Done"; cmd.Type = 4; cmd.Priority = 0;
                    return cmd;
                })
                .M("<init>", "(Ljava/lang/String;)V", (t, b) => ((AlertObj)t.R[b]).Title = S(t, b + 1))
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;L" + P + "Image;L" + P + "AlertType;)V", (t, b) =>
                {
                    var a = (AlertObj)t.R[b];
                    a.Title = S(t, b + 1); a.Text = S(t, b + 2); a.Img = (ImageObj)t.R[b + 3]; a.Type = t.R[b + 4];
                })
                .M("getTimeout", "()I", (t, b) => t.RetI(b, ((AlertObj)t.R[b]).Timeout <= 0 ? -2 : ((AlertObj)t.R[b]).Timeout))
                .M("setTimeout", "(I)V", (t, b) => { int v = t.I(b + 1); if (v <= 0 && v != -2) throw new JavaThrow("java/lang/IllegalArgumentException"); ((AlertObj)t.R[b]).Timeout = v == -2 ? 0 : v; })
                .M("getDefaultTimeout", "()I", (t, b) => t.RetI(b, 2000))
                .M("getString", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((AlertObj)t.R[b]).Text))
                .M("setString", "(Ljava/lang/String;)V", (t, b) => { ((AlertObj)t.R[b]).Text = S(t, b + 1); Dirty(t, (AlertObj)t.R[b]); })
                .M("getImage", "()L" + P + "Image;", (t, b) => t.RetA(b, ((AlertObj)t.R[b]).Img))
                .M("setImage", "(L" + P + "Image;)V", (t, b) => { ((AlertObj)t.R[b]).Img = (ImageObj)t.R[b + 1]; Dirty(t, (AlertObj)t.R[b]); })
                .M("getType", "()L" + P + "AlertType;", (t, b) => t.RetA(b, ((AlertObj)t.R[b]).Type))
                .M("setType", "(L" + P + "AlertType;)V", (t, b) => ((AlertObj)t.R[b]).Type = t.R[b + 1])
                .M("setIndicator", "(L" + P + "Gauge;)V", (t, b) => { ((AlertObj)t.R[b]).Indicator = (ItemObj)t.R[b + 1]; Dirty(t, (AlertObj)t.R[b]); })
                .M("getIndicator", "()L" + P + "Gauge;", (t, b) => t.RetA(b, ((AlertObj)t.R[b]).Indicator))
                .M("addCommand", "(L" + P + "Command;)V", (t, b) =>
                {
                    var a = (AlertObj)t.R[b]; var cmd = (CommandObj)t.R[b + 1] ?? throw new NullReferenceException();
                    if (cmd == t.Vm.LoadClass(P + "Alert").StaticO[0]) return;
                    lock (a.Commands) if (!a.Commands.Contains(cmd)) a.Commands.Add(cmd);
                    a.Timeout = 0;
                    Dirty(t, a);
                }),
                Rt.F<AlertObj>());
        }

        static void ListInit(JThread t, int b, object strs, object imgs)
        {
            var l = (ListObj)t.R[b];
            l.Title = (string)t.R[b + 1];
            l.Type = t.I(b + 2);
            if (l.Type < 1 || l.Type > 3) throw new JavaThrow("java/lang/IllegalArgumentException", "list type");
            if (strs != null) InitChoices(t, l.Els, l.Type, strs, imgs);
            var k = t.Vm.LoadClass(P + "List");
            t.Vm.EnsureInit(t, k);
            if (l.Type == 3) l.SelectCmd = (CommandObj)k.StaticO[0];
        }
    }
}
