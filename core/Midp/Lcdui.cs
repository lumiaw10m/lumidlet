using System;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public static class Lcdui
    {
        const string P = "javax/microedition/lcdui/";
        static MidpRuntime R(JThread t) => MidpRuntime.Of(t);
        static GraphicsObj G(JThread t, int b) => (GraphicsObj)t.R[b];
        static DisplayableObj D(JThread t, int b) => (DisplayableObj)t.R[b];
        static string S(JThread t, int i) => (string)t.R[i];

        public static ImageObj NewImage(JThread t, Surface s, bool mutable)
        {
            var o = (ImageObj)t.Vm.NewObject(t.Vm.LoadClass(P + "Image"));
            o.S = s; o.Mutable = mutable;
            return o;
        }

        public static Surface Decode(JThread t, byte[] data, int off, int len)
        {
            if (data == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > data.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
            int w, h;
            bool alpha;
            if (Png.IsPng(data, off, len))
            {
                var px = Png.Decode(data, off, len, out w, out h, out alpha);
                if (px != null) return new Surface(w, h, px, alpha);
            }
            var buf = new byte[len];
            Buffer.BlockCopy(data, off, buf, 0, len);
            var p2 = R(t).Host.DecodeImage(buf, out w, out h);
            if (p2 == null || w <= 0 || h <= 0) throw new JavaThrow("java/lang/IllegalArgumentException", "cannot decode image");
            bool a2 = false;
            foreach (var v in p2) if (((uint)v >> 24) != 255) { a2 = true; break; }
            return new Surface(w, h, p2, a2);
        }

        public static GraphicsObj NewGraphics(JThread t, Surface s)
        {
            var g = (GraphicsObj)t.Vm.NewObject(t.Vm.LoadClass(P + "Graphics"));
            g.Attach(s, 0, 0, s.W, s.H);
            g.Font = R(t).DefaultFont(t);
            return g;
        }

        public static void Register(NativeRegistry n)
        {
            n.Interface(P + "CommandListener", c => c.Abstract("commandAction", "(L" + P + "Command;L" + P + "Displayable;)V"));
            n.Interface(P + "ItemCommandListener", c => c.Abstract("commandAction", "(L" + P + "Command;L" + P + "Item;)V"));
            n.Interface(P + "ItemStateListener", c => c.Abstract("itemStateChanged", "(L" + P + "Item;)V"));
            n.Interface(P + "Choice");

            n.Define(P + "Display", "java/lang/Object", c => c
                .S("getDisplay", "(Ljavax/microedition/midlet/MIDlet;)L" + P + "Display;", (t, b) =>
                {
                    var rt = R(t);
                    if (rt.Display == null) rt.Display = new JObject(t.Vm.LoadClass(P + "Display"));
                    t.RetA(b, rt.Display);
                })
                .M("getCurrent", "()L" + P + "Displayable;", (t, b) => t.RetA(b, R(t).Current))
                .M("setCurrent", "(L" + P + "Displayable;)V", (t, b) => R(t).SetCurrent(t, (DisplayableObj)t.R[b + 1]))
                .M("setCurrent", "(L" + P + "Alert;L" + P + "Displayable;)V", (t, b) =>
                {
                    var a = (AlertObj)t.R[b + 1];
                    if (a == null || t.R[b + 2] == null) throw new NullReferenceException();
                    a.Next = (DisplayableObj)t.R[b + 2];
                    R(t).SetCurrent(t, a);
                })
                .M("setCurrentItem", "(L" + P + "Item;)V", (t, b) => Screens.SetCurrentItem(t, R(t), (ItemObj)t.R[b + 1]))
                .M("isColor", "()Z", (t, b) => t.RetZ(b, true))
                .M("numColors", "()I", (t, b) => t.RetI(b, 1 << 24))
                .M("numAlphaLevels", "()I", (t, b) => t.RetI(b, 256))
                .M("vibrate", "(I)Z", (t, b) => { if (t.I(b + 1) < 0) throw new JavaThrow("java/lang/IllegalArgumentException"); R(t).Host.Vibrate(t.I(b + 1)); t.RetZ(b, true); })
                .M("flashBacklight", "(I)Z", (t, b) => t.RetZ(b, true))
                .M("callSerially", "(Ljava/lang/Runnable;)V", (t, b) => R(t).CallSerially(t.R[b + 1]))
                .M("getBestImageWidth", "(I)I", (t, b) => t.RetI(b, R(t).W / 4))
                .M("getBestImageHeight", "(I)I", (t, b) => t.RetI(b, R(t).W / 4))
                .M("getBorderStyle", "(Z)I", (t, b) => t.RetI(b, t.Z(b + 1) ? 0 : 1))
                .M("getColor", "(I)I", (t, b) =>
                {
                    switch (t.I(b + 1))
                    {
                        case 0: t.RetI(b, 0xFFFFFF); break;
                        case 1: t.RetI(b, 0x000000); break;
                        case 2: t.RetI(b, 0x2A6FD6); break;
                        case 3: t.RetI(b, 0xFFFFFF); break;
                        case 4: t.RetI(b, 0x000000); break;
                        default: t.RetI(b, 0x2A6FD6); break;
                    }
                }));

            n.Define(P + "Displayable", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { })
                .M("getTitle", "()Ljava/lang/String;", (t, b) => t.RetA(b, D(t, b).Title))
                .M("setTitle", "(Ljava/lang/String;)V", (t, b) => { D(t, b).Title = S(t, b + 1); R(t).Repaint(D(t, b), 0, 0, R(t).W, R(t).H); })
                .M("getTicker", "()L" + P + "Ticker;", (t, b) => t.RetA(b, D(t, b).Ticker))
                .M("setTicker", "(L" + P + "Ticker;)V", (t, b) => { D(t, b).Ticker = (TickerObj)t.R[b + 1]; R(t).Repaint(D(t, b), 0, 0, R(t).W, R(t).H); })
                .M("addCommand", "(L" + P + "Command;)V", (t, b) =>
                {
                    var d = D(t, b); var cmd = (CommandObj)t.R[b + 1];
                    if (cmd == null) throw new NullReferenceException();
                    if (d is CanvasObj && ((CanvasObj)d).NokiaFull) throw new JavaThrow("java/lang/IllegalStateException", "FullCanvas has no commands");
                    lock (d.Commands) if (!d.Commands.Contains(cmd)) d.Commands.Add(cmd);
                    R(t).Repaint(d, 0, 0, R(t).W, R(t).H);
                })
                .M("removeCommand", "(L" + P + "Command;)V", (t, b) => { var d = D(t, b); lock (d.Commands) d.Commands.Remove((CommandObj)t.R[b + 1]); R(t).Repaint(d, 0, 0, R(t).W, R(t).H); })
                .M("setCommandListener", "(L" + P + "CommandListener;)V", (t, b) => D(t, b).Listener = t.R[b + 1])
                .M("isShown", "()Z", (t, b) => t.RetZ(b, D(t, b).Shown && R(t).Current == D(t, b) && !R(t).Paused))
                .M("getWidth", "()I", (t, b) => t.RetI(b, R(t).W))
                .M("getHeight", "()I", (t, b) => t.RetI(b, R(t).DispHeight(D(t, b))))
                .M("sizeChanged", "(II)V", (t, b) => { }),
                Rt.F<DisplayableObj>(), access: 0x0401);

            n.Define(P + "Canvas", P + "Displayable", c => c
                .M("<init>", "()V", (t, b) => { })
                .Abstract("paint", "(L" + P + "Graphics;)V")
                .M("repaint", "()V", (t, b) => R(t).Repaint(D(t, b), 0, 0, R(t).W, R(t).H))
                .M("repaint", "(IIII)V", (t, b) => R(t).Repaint(D(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("serviceRepaints", "()V", (t, b) => R(t).ServiceRepaints(t))
                .M("isDoubleBuffered", "()Z", (t, b) => t.RetZ(b, true))
                .M("hasPointerEvents", "()Z", (t, b) => t.RetZ(b, true))
                .M("hasPointerMotionEvents", "()Z", (t, b) => t.RetZ(b, true))
                .M("hasRepeatEvents", "()Z", (t, b) => t.RetZ(b, true))
                .M("getKeyCode", "(I)I", (t, b) => t.RetI(b, Keys.KeyCode(t.I(b + 1))))
                .M("getGameAction", "(I)I", (t, b) => t.RetI(b, Keys.GameAction(t.I(b + 1))))
                .M("getKeyName", "(I)Ljava/lang/String;", (t, b) => t.RetA(b, Keys.Name(t.I(b + 1))))
                .M("setFullScreenMode", "(Z)V", (t, b) =>
                {
                    var cv = (CanvasObj)t.R[b];
                    bool f = t.Z(b + 1);
                    if (cv.FullScreen == f) return;
                    cv.FullScreen = f;
                    R(t).FullScreenChanged(t, cv);
                })
                .M("keyPressed", "(I)V", (t, b) => { })
                .M("keyReleased", "(I)V", (t, b) => { })
                .M("keyRepeated", "(I)V", (t, b) => { })
                .M("pointerPressed", "(II)V", (t, b) => { })
                .M("pointerReleased", "(II)V", (t, b) => { })
                .M("pointerDragged", "(II)V", (t, b) => { })
                .M("showNotify", "()V", (t, b) => { })
                .M("hideNotify", "()V", (t, b) => { }),
                Rt.F<CanvasObj>(), access: 0x0401);

            n.Define(P + "Command", "java/lang/Object", c => c
                .M("<init>", "(Ljava/lang/String;II)V", (t, b) =>
                {
                    var o = (CommandObj)t.R[b];
                    o.Label = S(t, b + 1) ?? throw new NullReferenceException(); o.Type = t.I(b + 2); o.Priority = t.I(b + 3);
                    if (o.Type < 1 || o.Type > 8) throw new JavaThrow("java/lang/IllegalArgumentException");
                })
                .M("<init>", "(Ljava/lang/String;Ljava/lang/String;II)V", (t, b) =>
                {
                    var o = (CommandObj)t.R[b];
                    o.Label = S(t, b + 1) ?? throw new NullReferenceException(); o.LongLabel = S(t, b + 2); o.Type = t.I(b + 3); o.Priority = t.I(b + 4);
                })
                .M("getLabel", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((CommandObj)t.R[b]).Label))
                .M("getLongLabel", "()Ljava/lang/String;", (t, b) => t.RetA(b, ((CommandObj)t.R[b]).LongLabel))
                .M("getCommandType", "()I", (t, b) => t.RetI(b, ((CommandObj)t.R[b]).Type))
                .M("getPriority", "()I", (t, b) => t.RetI(b, ((CommandObj)t.R[b]).Priority)),
                Rt.F<CommandObj>());

            n.Define(P + "Font", "java/lang/Object", c => c
                .S("getDefaultFont", "()L" + P + "Font;", (t, b) => t.RetA(b, R(t).DefaultFont(t)))
                .S("getFont", "(III)L" + P + "Font;", (t, b) =>
                {
                    int face = t.I(b), style = t.I(b + 1), size = t.I(b + 2);
                    if ((face != 0 && face != 32 && face != 64) || (size != 0 && size != 8 && size != 16) || (style & ~7) != 0)
                        throw new JavaThrow("java/lang/IllegalArgumentException", "font");
                    t.RetA(b, R(t).JavaFont(t, face, style, size));
                })
                .S("getFont", "(I)L" + P + "Font;", (t, b) => t.RetA(b, R(t).DefaultFont(t)))
                .M("getFace", "()I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).Face))
                .M("getStyle", "()I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).Style))
                .M("getSize", "()I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).Size))
                .M("isPlain", "()Z", (t, b) => t.RetZ(b, ((FontObj)t.R[b]).Style == 0))
                .M("isBold", "()Z", (t, b) => t.RetZ(b, (((FontObj)t.R[b]).Style & 1) != 0))
                .M("isItalic", "()Z", (t, b) => t.RetZ(b, (((FontObj)t.R[b]).Style & 2) != 0))
                .M("isUnderlined", "()Z", (t, b) => t.RetZ(b, (((FontObj)t.R[b]).Style & 4) != 0))
                .M("getHeight", "()I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).Height))
                .M("getBaselinePosition", "()I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).Ascent))
                .M("charWidth", "(C)I", (t, b) => t.RetI(b, ((FontObj)t.R[b]).CharWidth((char)t.I(b + 1))))
                .M("charsWidth", "([CII)I", (t, b) =>
                {
                    var a = (char[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3);
                    if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    t.RetI(b, ((FontObj)t.R[b]).CharsWidth(a, o, l));
                })
                .M("stringWidth", "(Ljava/lang/String;)I", (t, b) => { var s = S(t, b + 1); if (s == null) throw new NullReferenceException(); t.RetI(b, ((FontObj)t.R[b]).StringWidth(s, 0, s.Length)); })
                .M("substringWidth", "(Ljava/lang/String;II)I", (t, b) =>
                {
                    var s = S(t, b + 1); int o = t.I(b + 2), l = t.I(b + 3);
                    if (o < 0 || l < 0 || o + l > s.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException");
                    t.RetI(b, ((FontObj)t.R[b]).StringWidth(s, o, l));
                }),
                Rt.F<FontObj>(), access: 0x0011);

            n.Define(P + "Image", "java/lang/Object", c => c
                .S("createImage", "(II)L" + P + "Image;", (t, b) =>
                {
                    int w = t.I(b), h = t.I(b + 1);
                    if (w <= 0 || h <= 0) throw new JavaThrow("java/lang/IllegalArgumentException", "size");
                    t.RetA(b, NewImage(t, new Surface(w, h, -1), true));
                })
                .S("createImage", "(L" + P + "Image;)L" + P + "Image;", (t, b) =>
                {
                    var src = (ImageObj)t.R[b];
                    if (src == null) throw new NullReferenceException();
                    if (!src.Mutable) { t.RetA(b, src); return; }
                    t.RetA(b, NewImage(t, new Surface(src.S.W, src.S.H, (int[])src.S.Pix.Clone(), src.S.HasAlpha), false));
                })
                .S("createImage", "(Ljava/lang/String;)L" + P + "Image;", (t, b) =>
                {
                    var name = S(t, b);
                    if (name == null) throw new NullReferenceException();
                    var data = t.Vm.ClassPath.GetResource(name.TrimStart('/'));
                    if (data == null) throw new JavaThrow("java/io/IOException", name);
                    try { t.RetA(b, NewImage(t, Decode(t, data, 0, data.Length), false)); }
                    catch (JavaThrow e) when (e.ClassName == "java/lang/IllegalArgumentException") { throw new JavaThrow("java/io/IOException", name); }
                })
                .S("createImage", "([BII)L" + P + "Image;", (t, b) =>
                {
                    var a = (sbyte[])t.R[b];
                    if (a == null) throw new NullReferenceException();
                    var u = Enc.ToU(a);
                    t.RetA(b, NewImage(t, Decode(t, u, t.I(b + 1), t.I(b + 2)), false));
                })
                .S("createImage", "(Ljava/io/InputStream;)L" + P + "Image;", (t, b) =>
                {
                    var s = (InStreamObj)t.R[b];
                    if (s == null) throw new NullReferenceException();
                    var ms = new System.IO.MemoryStream();
                    var buf = new sbyte[4096];
                    int k;
                    while ((k = s.Read(t, buf, 0, buf.Length)) > 0) ms.Write(Enc.ToU(buf, 0, k), 0, k);
                    var data = ms.ToArray();
                    try { t.RetA(b, NewImage(t, Decode(t, data, 0, data.Length), false)); }
                    catch (JavaThrow e) when (e.ClassName == "java/lang/IllegalArgumentException") { throw new JavaThrow("java/io/IOException", "image"); }
                })
                .S("createImage", "(L" + P + "Image;IIIII)L" + P + "Image;", (t, b) =>
                {
                    var src = (ImageObj)t.R[b];
                    int x = t.I(b + 1), y = t.I(b + 2), w = t.I(b + 3), h = t.I(b + 4), tr = t.I(b + 5);
                    if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > src.S.W || y + h > src.S.H) throw new JavaThrow("java/lang/IllegalArgumentException");
                    int tw = Gfx.Swaps(tr) ? h : w, th = Gfx.Swaps(tr) ? w : h;
                    var dst = new Surface(tw, th, 0) { HasAlpha = true };
                    var g = new GraphicsObj();
                    g.Attach(dst, 0, 0, tw, th);
                    Gfx.DrawRegion(g, new Surface(src.S.W, src.S.H, src.S.Pix, true) , x, y, w, h, tr, 0, 0, 20);
                    if (!src.S.HasAlpha) for (int i = 0; i < dst.Pix.Length; i++) dst.Pix[i] |= unchecked((int)0xFF000000);
                    dst.HasAlpha = src.S.HasAlpha;
                    t.RetA(b, NewImage(t, dst, false));
                })
                .S("createRGBImage", "([IIIZ)L" + P + "Image;", (t, b) =>
                {
                    var rgb = (int[])t.R[b]; int w = t.I(b + 1), h = t.I(b + 2); bool alpha = t.Z(b + 3);
                    if (rgb == null) throw new NullReferenceException();
                    if (w <= 0 || h <= 0) throw new JavaThrow("java/lang/IllegalArgumentException");
                    if (w * h > rgb.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    var px = new int[w * h];
                    Array.Copy(rgb, px, w * h);
                    if (!alpha) for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xFF000000);
                    t.RetA(b, NewImage(t, new Surface(w, h, px, alpha), false));
                })
                .M("getGraphics", "()L" + P + "Graphics;", (t, b) =>
                {
                    var img = (ImageObj)t.R[b];
                    if (!img.Mutable) throw new JavaThrow("java/lang/IllegalStateException", "immutable image");
                    t.RetA(b, NewGraphics(t, img.S));
                })
                .M("getWidth", "()I", (t, b) => t.RetI(b, ((ImageObj)t.R[b]).S.W))
                .M("getHeight", "()I", (t, b) => t.RetI(b, ((ImageObj)t.R[b]).S.H))
                .M("isMutable", "()Z", (t, b) => t.RetZ(b, ((ImageObj)t.R[b]).Mutable))
                .M("getRGB", "([IIIIIII)V", (t, b) =>
                {
                    var img = ((ImageObj)t.R[b]).S;
                    var rgb = (int[])t.R[b + 1];
                    int off = t.I(b + 2), scan = t.I(b + 3), x = t.I(b + 4), y = t.I(b + 5), w = t.I(b + 6), h = t.I(b + 7);
                    if (rgb == null) throw new NullReferenceException();
                    if (x < 0 || y < 0 || x + w > img.W || y + h > img.H) throw new JavaThrow("java/lang/IllegalArgumentException", "getRGB");
                    if (w <= 0 || h <= 0) return;
                    for (int r = 0; r < h; r++)
                    {
                        int d = off + r * scan;
                        if (d < 0 || d + w > rgb.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                        Array.Copy(img.Pix, (y + r) * img.W + x, rgb, d, w);
                    }
                }),
                Rt.F<ImageObj>());

            n.Define(P + "Graphics", "java/lang/Object", c => c
                .M("translate", "(II)V", (t, b) => { G(t, b).Tx += t.I(b + 1); G(t, b).Ty += t.I(b + 2); })
                .M("getTranslateX", "()I", (t, b) => t.RetI(b, G(t, b).Tx))
                .M("getTranslateY", "()I", (t, b) => t.RetI(b, G(t, b).Ty))
                .M("getColor", "()I", (t, b) => t.RetI(b, G(t, b).Color & 0xFFFFFF))
                .M("getRedComponent", "()I", (t, b) => t.RetI(b, (G(t, b).Color >> 16) & 255))
                .M("getGreenComponent", "()I", (t, b) => t.RetI(b, (G(t, b).Color >> 8) & 255))
                .M("getBlueComponent", "()I", (t, b) => t.RetI(b, G(t, b).Color & 255))
                .M("getGrayScale", "()I", (t, b) => { int c2 = G(t, b).Color; t.RetI(b, (((c2 >> 16) & 255) * 30 + ((c2 >> 8) & 255) * 59 + (c2 & 255) * 11) / 100); })
                .M("setColor", "(III)V", (t, b) =>
                {
                    int r = t.I(b + 1), gg = t.I(b + 2), bb = t.I(b + 3);
                    if (((r | gg | bb) & ~255) != 0) throw new JavaThrow("java/lang/IllegalArgumentException", "color");
                    G(t, b).Color = unchecked((int)0xFF000000) | (r << 16) | (gg << 8) | bb;
                })
                .M("setColor", "(I)V", (t, b) => G(t, b).Color = t.I(b + 1) | unchecked((int)0xFF000000))
                .M("setGrayScale", "(I)V", (t, b) => { int v = t.I(b + 1) & 255; G(t, b).Color = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v; })
                .M("getFont", "()L" + P + "Font;", (t, b) => t.RetA(b, G(t, b).Font))
                .M("setFont", "(L" + P + "Font;)V", (t, b) => G(t, b).Font = (FontObj)t.R[b + 1] ?? R(t).DefaultFont(t))
                .M("setStrokeStyle", "(I)V", (t, b) => { int s = t.I(b + 1); if (s != 0 && s != 1) throw new JavaThrow("java/lang/IllegalArgumentException"); G(t, b).Stroke = s; })
                .M("getStrokeStyle", "()I", (t, b) => t.RetI(b, G(t, b).Stroke))
                .M("getClipX", "()I", (t, b) => t.RetI(b, G(t, b).Cx0 - G(t, b).Tx))
                .M("getClipY", "()I", (t, b) => t.RetI(b, G(t, b).Cy0 - G(t, b).Ty))
                .M("getClipWidth", "()I", (t, b) => t.RetI(b, G(t, b).Cx1 - G(t, b).Cx0))
                .M("getClipHeight", "()I", (t, b) => t.RetI(b, G(t, b).Cy1 - G(t, b).Cy0))
                .M("clipRect", "(IIII)V", (t, b) => G(t, b).ClipRect(t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("setClip", "(IIII)V", (t, b) => G(t, b).SetClip(t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("drawLine", "(IIII)V", (t, b) => Gfx.DrawLine(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("fillRect", "(IIII)V", (t, b) => Gfx.FillRect(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("drawRect", "(IIII)V", (t, b) => Gfx.DrawRect(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("drawRoundRect", "(IIIIII)V", (t, b) => Gfx.DrawRoundRect(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6)))
                .M("fillRoundRect", "(IIIIII)V", (t, b) => Gfx.FillRoundRect(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6)))
                .M("fillArc", "(IIIIII)V", (t, b) => Gfx.FillArc(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6)))
                .M("drawArc", "(IIIIII)V", (t, b) => Gfx.DrawArc(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6)))
                .M("fillTriangle", "(IIIIII)V", (t, b) => Gfx.FillTriangle(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6)))
                .M("drawString", "(Ljava/lang/String;III)V", (t, b) =>
                {
                    var s = S(t, b + 1);
                    if (s == null) throw new NullReferenceException();
                    Gfx.DrawString(G(t, b), s, 0, s.Length, t.I(b + 2), t.I(b + 3), t.I(b + 4));
                })
                .M("drawSubstring", "(Ljava/lang/String;IIIII)V", (t, b) =>
                {
                    var s = S(t, b + 1); int o = t.I(b + 2), l = t.I(b + 3);
                    if (s == null) throw new NullReferenceException();
                    if (o < 0 || l < 0 || o + l > s.Length) throw new JavaThrow("java/lang/StringIndexOutOfBoundsException");
                    Gfx.DrawString(G(t, b), s, o, l, t.I(b + 4), t.I(b + 5), t.I(b + 6));
                })
                .M("drawChar", "(CIII)V", (t, b) => Gfx.DrawString(G(t, b), ((char)t.I(b + 1)).ToString(), 0, 1, t.I(b + 2), t.I(b + 3), t.I(b + 4)))
                .M("drawChars", "([CIIIII)V", (t, b) =>
                {
                    var a = (char[])t.R[b + 1]; int o = t.I(b + 2), l = t.I(b + 3);
                    if (a == null) throw new NullReferenceException();
                    if (o < 0 || l < 0 || o + l > a.Length) throw new JavaThrow("java/lang/ArrayIndexOutOfBoundsException");
                    Gfx.DrawString(G(t, b), new string(a, o, l), 0, l, t.I(b + 4), t.I(b + 5), t.I(b + 6));
                })
                .M("drawImage", "(L" + P + "Image;III)V", (t, b) =>
                {
                    var img = (ImageObj)t.R[b + 1];
                    if (img == null) throw new NullReferenceException();
                    Gfx.DrawImage(G(t, b), img.S, t.I(b + 2), t.I(b + 3), t.I(b + 4));
                })
                .M("drawRegion", "(L" + P + "Image;IIIIIIII)V", (t, b) =>
                {
                    var img = (ImageObj)t.R[b + 1];
                    if (img == null) throw new NullReferenceException();
                    Gfx.DrawRegion(G(t, b), img.S, t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6), t.I(b + 7), t.I(b + 8), t.I(b + 9));
                })
                .M("drawRGB", "([IIIIIIIZ)V", (t, b) => Gfx.DrawRGB(G(t, b), (int[])t.R[b + 1], t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6), t.I(b + 7), t.Z(b + 8)))
                .M("copyArea", "(IIIIIII)V", (t, b) => Gfx.CopyArea(G(t, b), t.I(b + 1), t.I(b + 2), t.I(b + 3), t.I(b + 4), t.I(b + 5), t.I(b + 6), t.I(b + 7)))
                .M("getDisplayColor", "(I)I", (t, b) => t.RetI(b, t.I(b + 1) & 0xFFFFFF)),
                Rt.F<GraphicsObj>());
        }
    }
}
