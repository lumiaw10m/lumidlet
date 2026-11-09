import javax.microedition.midlet.*;
import javax.microedition.lcdui.*;
import javax.microedition.lcdui.game.*;

public class Bench extends MIDlet implements Runnable {
    C c;
    protected void startApp() { c = new C(); Display.getDisplay(this).setCurrent(c); new Thread(this).start(); }
    protected void pauseApp() {}
    protected void destroyApp(boolean u) {}
    static int fib(int n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); }
    int[] arr = new int[1000];
    int field;
    static class C extends GameCanvas { C() { super(false); setFullScreenMode(true); } Graphics gg() { return getGraphics(); } }
    void log(String s) { System.out.println("BENCH " + s); }
    public void run() {
        long t = System.currentTimeMillis(); int acc = 0;
        for (int i = 0; i < 3000000; i++) acc += i ^ (i >> 3);
        long loop = System.currentTimeMillis() - t;
        t = System.currentTimeMillis(); int f = fib(24); long calls = System.currentTimeMillis() - t;
        t = System.currentTimeMillis();
        for (int k = 0; k < 1000; k++) for (int i = 0; i < 1000; i++) { arr[i] = arr[i] + i; field += arr[i] & 7; }
        long mem = System.currentTimeMillis() - t;
        log("loop3M=" + loop + "ms fib24=" + calls + "ms arr1M=" + mem + "ms (" + acc + "," + f + ")");
        Graphics g = c.gg();
        Image img = Image.createImage(32, 32); Graphics ig = img.getGraphics(); ig.setColor(0xff0000); ig.fillArc(0, 0, 32, 32, 0, 360);
        int[] px = new int[32 * 32]; for (int i = 0; i < px.length; i++) px[i] = ((i % 32) < 16 ? 0x8000ff00 : 0xff0000ff);
        Image rgb = Image.createRGBImage(px, 32, 32, true);
        int w = c.getWidth(), h = c.getHeight();
        t = System.currentTimeMillis();
        int frames = 0;
        while (System.currentTimeMillis() - t < 4000) {
            g.setColor(0x203040); g.fillRect(0, 0, w, h);
            for (int i = 0; i < 40; i++) { g.drawImage(img, (i * 37 + frames) % w, (i * 53) % h, 0); g.drawRegion(rgb, 0, 0, 32, 32, i & 7, (i * 29) % w, (i * 41 + frames) % h, 0); }
            g.setColor(0xffffff); g.drawString("frame " + frames, 2, 2, 0);
            c.flushGraphics(); frames++;
        }
        long dt = System.currentTimeMillis() - t;
        log("draw fps=" + (frames * 1000 / dt));
        t = System.currentTimeMillis(); for (int i = 0; i < 50; i++) { try { Thread.sleep(10); } catch (Exception e) {} }
        long s10 = System.currentTimeMillis() - t;
        t = System.currentTimeMillis(); for (int i = 0; i < 20; i++) { try { Thread.sleep(33); } catch (Exception e) {} }
        long s33 = System.currentTimeMillis() - t;
        log("sleep10x50=" + s10 + "ms (ideal 500) sleep33x20=" + s33 + "ms (ideal 660)");
        g.setColor(0); g.fillRect(0, 0, w, h); g.setColor(0xffffff);
        g.drawString("loop " + loop + " fib " + calls + " arr " + mem, 2, 2, 0);
        g.drawString("fps " + (frames * 1000 / dt) + " s10 " + s10 + " s33 " + s33, 2, 20, 0);
        c.flushGraphics();
    }
}
