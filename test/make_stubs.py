import os, sys

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "stubs")
stubs = {
"javax/microedition/midlet/MIDlet.java": """package javax.microedition.midlet;
public abstract class MIDlet { protected MIDlet(){} protected abstract void startApp() throws MIDletStateChangeException; protected abstract void pauseApp(); protected abstract void destroyApp(boolean u) throws MIDletStateChangeException;
public final String getAppProperty(String k){return null;} public final void notifyDestroyed(){} public final boolean platformRequest(String u){return false;} }""",
"javax/microedition/midlet/MIDletStateChangeException.java": "package javax.microedition.midlet; public class MIDletStateChangeException extends Exception {}",
"javax/microedition/lcdui/Display.java": """package javax.microedition.lcdui; import javax.microedition.midlet.MIDlet;
public class Display { public static Display getDisplay(MIDlet m){return null;} public void setCurrent(Displayable d){} public void setCurrent(Alert a, Displayable d){} public Displayable getCurrent(){return null;} public void callSerially(Runnable r){} public boolean vibrate(int d){return false;} }""",
"javax/microedition/lcdui/Displayable.java": """package javax.microedition.lcdui;
public abstract class Displayable { public void addCommand(Command c){} public void removeCommand(Command c){} public void setCommandListener(CommandListener l){} public int getWidth(){return 0;} public int getHeight(){return 0;} public String getTitle(){return null;} public void setTitle(String s){} public void setTicker(Ticker t){} public boolean isShown(){return false;} protected void sizeChanged(int w,int h){} }""",
"javax/microedition/lcdui/Ticker.java": "package javax.microedition.lcdui; public class Ticker { public Ticker(String s){} }",
"javax/microedition/lcdui/Screen.java": "package javax.microedition.lcdui; public abstract class Screen extends Displayable {}",
"javax/microedition/lcdui/Canvas.java": """package javax.microedition.lcdui;
public abstract class Canvas extends Displayable { public static final int UP=1,DOWN=6,LEFT=2,RIGHT=5,FIRE=8,GAME_A=9,KEY_NUM5=53;
protected Canvas(){} protected abstract void paint(Graphics g); public final void repaint(){} public final void repaint(int x,int y,int w,int h){} public final void serviceRepaints(){}
public void setFullScreenMode(boolean b){} public int getGameAction(int k){return 0;} public int getKeyCode(int a){return 0;} public String getKeyName(int k){return null;}
protected void keyPressed(int k){} protected void keyReleased(int k){} protected void keyRepeated(int k){} protected void pointerPressed(int x,int y){} protected void pointerReleased(int x,int y){} protected void pointerDragged(int x,int y){} protected void showNotify(){} protected void hideNotify(){} }""",
"javax/microedition/lcdui/Graphics.java": """package javax.microedition.lcdui;
public class Graphics { public static final int HCENTER=1,VCENTER=2,LEFT=4,RIGHT=8,TOP=16,BOTTOM=32,BASELINE=64,SOLID=0,DOTTED=1;
public void setColor(int c){} public void setColor(int r,int g,int b){} public int getColor(){return 0;} public void fillRect(int x,int y,int w,int h){} public void drawRect(int x,int y,int w,int h){} public void drawLine(int a,int b,int c,int d){}
public void fillArc(int x,int y,int w,int h,int s,int a){} public void drawArc(int x,int y,int w,int h,int s,int a){} public void fillRoundRect(int x,int y,int w,int h,int a,int b){} public void drawRoundRect(int x,int y,int w,int h,int a,int b){}
public void fillTriangle(int a,int b,int c,int d,int e,int f){} public void drawString(String s,int x,int y,int a){} public void drawChar(char c,int x,int y,int a){} public void drawImage(Image i,int x,int y,int a){}
public void drawRegion(Image i,int a,int b,int c,int d,int t,int x,int y,int an){} public void drawRGB(int[] r,int o,int s,int x,int y,int w,int h,boolean a){} public void setFont(Font f){} public Font getFont(){return null;}
public void setClip(int x,int y,int w,int h){} public void clipRect(int x,int y,int w,int h){} public void translate(int x,int y){} public int getClipX(){return 0;} public void setStrokeStyle(int s){} public void copyArea(int a,int b,int c,int d,int e,int f,int g){} }""",
"javax/microedition/lcdui/Image.java": """package javax.microedition.lcdui;
public class Image { public static Image createImage(int w,int h){return null;} public static Image createImage(String n) throws java.io.IOException {return null;} public static Image createImage(byte[] d,int o,int l){return null;}
public static Image createImage(Image i,int x,int y,int w,int h,int t){return null;} public static Image createRGBImage(int[] r,int w,int h,boolean a){return null;} public Graphics getGraphics(){return null;} public int getWidth(){return 0;} public int getHeight(){return 0;} public void getRGB(int[] r,int o,int s,int x,int y,int w,int h){} }""",
"javax/microedition/lcdui/Font.java": """package javax.microedition.lcdui;
public class Font { public static final int FACE_SYSTEM=0,FACE_MONOSPACE=32,FACE_PROPORTIONAL=64,STYLE_PLAIN=0,STYLE_BOLD=1,STYLE_ITALIC=2,STYLE_UNDERLINED=4,SIZE_SMALL=8,SIZE_MEDIUM=0,SIZE_LARGE=16;
public static Font getFont(int f,int s,int z){return null;} public static Font getDefaultFont(){return null;} public int getHeight(){return 0;} public int stringWidth(String s){return 0;} public int getBaselinePosition(){return 0;} }""",
"javax/microedition/lcdui/Command.java": "package javax.microedition.lcdui; public class Command { public static final int SCREEN=1,BACK=2,CANCEL=3,OK=4,HELP=5,STOP=6,EXIT=7,ITEM=8; public Command(String l,int t,int p){} public String getLabel(){return null;} }",
"javax/microedition/lcdui/CommandListener.java": "package javax.microedition.lcdui; public interface CommandListener { void commandAction(Command c, Displayable d); }",
"javax/microedition/lcdui/ItemStateListener.java": "package javax.microedition.lcdui; public interface ItemStateListener { void itemStateChanged(Item i); }",
"javax/microedition/lcdui/Choice.java": "package javax.microedition.lcdui; public interface Choice { int EXCLUSIVE=1,MULTIPLE=2,IMPLICIT=3,POPUP=4; int getSelectedIndex(); }",
"javax/microedition/lcdui/Item.java": "package javax.microedition.lcdui; public abstract class Item { public String getLabel(){return null;} public void setLabel(String s){} }",
"javax/microedition/lcdui/StringItem.java": "package javax.microedition.lcdui; public class StringItem extends Item { public StringItem(String l,String t){} public void setText(String t){} public String getText(){return null;} }",
"javax/microedition/lcdui/TextField.java": "package javax.microedition.lcdui; public class TextField extends Item { public static final int ANY=0,PASSWORD=0x10000; public TextField(String l,String t,int m,int c){} public String getString(){return null;} }",
"javax/microedition/lcdui/Gauge.java": "package javax.microedition.lcdui; public class Gauge extends Item { public Gauge(String l,boolean i,int m,int v){} public int getValue(){return 0;} public void setValue(int v){} }",
"javax/microedition/lcdui/ChoiceGroup.java": "package javax.microedition.lcdui; public class ChoiceGroup extends Item implements Choice { public ChoiceGroup(String l,int t){} public int append(String s,Image i){return 0;} public int getSelectedIndex(){return 0;} }",
"javax/microedition/lcdui/Form.java": "package javax.microedition.lcdui; public class Form extends Screen { public Form(String t){} public int append(Item i){return 0;} public int append(String s){return 0;} public int append(Image i){return 0;} public void setItemStateListener(ItemStateListener l){} public int size(){return 0;} }",
"javax/microedition/lcdui/List.java": "package javax.microedition.lcdui; public class List extends Screen implements Choice { public static final Command SELECT_COMMAND=null; public List(String t,int ty){} public int append(String s,Image i){return 0;} public int getSelectedIndex(){return 0;} public String getString(int i){return null;} }",
"javax/microedition/lcdui/AlertType.java": "package javax.microedition.lcdui; public class AlertType { public static final AlertType INFO=null; }",
"javax/microedition/lcdui/Alert.java": "package javax.microedition.lcdui; public class Alert extends Screen { public static final int FOREVER=-2; public Alert(String t,String s,Image i,AlertType ty){} public void setTimeout(int t){} }",
"javax/microedition/lcdui/game/GameCanvas.java": """package javax.microedition.lcdui.game; import javax.microedition.lcdui.*;
public abstract class GameCanvas extends Canvas { public static final int UP_PRESSED=2,DOWN_PRESSED=64,LEFT_PRESSED=4,RIGHT_PRESSED=32,FIRE_PRESSED=256; protected GameCanvas(boolean s){} protected Graphics getGraphics(){return null;} public void flushGraphics(){} public int getKeyStates(){return 0;} public void paint(Graphics g){} }""",
"javax/microedition/lcdui/game/Layer.java": "package javax.microedition.lcdui.game; import javax.microedition.lcdui.*; public abstract class Layer { public int getX(){return 0;} public int getY(){return 0;} public void setPosition(int x,int y){} public void move(int x,int y){} public void setVisible(boolean v){} public abstract void paint(Graphics g); }",
"javax/microedition/lcdui/game/Sprite.java": """package javax.microedition.lcdui.game; import javax.microedition.lcdui.*;
public class Sprite extends Layer { public static final int TRANS_NONE=0,TRANS_ROT90=5,TRANS_ROT180=3,TRANS_ROT270=6,TRANS_MIRROR=2; public Sprite(Image i,int w,int h){} public void nextFrame(){} public void setFrameSequence(int[] s){} public void setTransform(int t){} public void defineReferencePixel(int x,int y){} public void setRefPixelPosition(int x,int y){} public boolean collidesWith(Sprite s,boolean p){return false;} public boolean collidesWith(TiledLayer s,boolean p){return false;} public void paint(Graphics g){} }""",
"javax/microedition/lcdui/game/TiledLayer.java": "package javax.microedition.lcdui.game; import javax.microedition.lcdui.*; public class TiledLayer extends Layer { public TiledLayer(int c,int r,Image i,int w,int h){} public void setCell(int c,int r,int v){} public void fillCells(int a,int b,int c,int d,int v){} public int createAnimatedTile(int v){return 0;} public void setAnimatedTile(int i,int v){} public void paint(Graphics g){} }",
"javax/microedition/lcdui/game/LayerManager.java": "package javax.microedition.lcdui.game; import javax.microedition.lcdui.*; public class LayerManager { public void append(Layer l){} public void setViewWindow(int x,int y,int w,int h){} public void paint(Graphics g,int x,int y){} }",
"javax/microedition/rms/RecordStore.java": "package javax.microedition.rms; public class RecordStore { public static RecordStore openRecordStore(String n,boolean c) throws RecordStoreException {return null;} public int addRecord(byte[] d,int o,int l) throws RecordStoreException {return 0;} public byte[] getRecord(int id) throws RecordStoreException {return null;} public void setRecord(int id,byte[] d,int o,int l) throws RecordStoreException {} public int getNumRecords() throws RecordStoreException {return 0;} public void closeRecordStore() throws RecordStoreException {} }",
"javax/microedition/rms/RecordStoreException.java": "package javax.microedition.rms; public class RecordStoreException extends Exception {}",
"javax/microedition/media/Manager.java": "package javax.microedition.media; public class Manager { public static Player createPlayer(java.io.InputStream s,String t) throws java.io.IOException, MediaException {return null;} public static void playTone(int n,int d,int v) throws MediaException {} }",
"javax/microedition/media/Player.java": "package javax.microedition.media; public interface Player { void start() throws MediaException; void setLoopCount(int c); void realize() throws MediaException; long getDuration(); }",
"javax/microedition/media/MediaException.java": "package javax.microedition.media; public class MediaException extends Exception {}",
"com/nokia/mid/ui/DirectUtils.java": "package com.nokia.mid.ui; import javax.microedition.lcdui.*; public class DirectUtils { public static DirectGraphics getDirectGraphics(Graphics g){return null;} public static Image createImage(int w,int h,int argb){return null;} }",
"com/nokia/mid/ui/DirectGraphics.java": "package com.nokia.mid.ui; import javax.microedition.lcdui.*; public interface DirectGraphics { int FLIP_HORIZONTAL=0x2000, FLIP_VERTICAL=0x4000, ROTATE_90=90; void drawImage(Image i,int x,int y,int a,int m); void fillTriangle(int a,int b,int c,int d,int e,int f,int argb); void setARGBColor(int c); }",
}
for path, src in stubs.items():
    full = os.path.join(root, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    open(full, "w").write(src)


def png(w, h, pixels):
    import zlib, struct
    raw = b"".join(b"\x00" + bytes(sum((list(p) for p in pixels[y * w:(y + 1) * w]), [])) for y in range(h))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


res = os.path.join(os.path.dirname(os.path.abspath(__file__)), "game", "res")
os.makedirs(res, exist_ok=True)
w, h = 64, 16
px = []
for y in range(h):
    for x in range(w):
        f = x // 16
        lx, ly = x % 16, y
        cx, cy = 7.5, 7.5
        d = ((lx - cx) ** 2 + (ly - cy) ** 2) ** 0.5
        if d < 7:
            col = [(255, 60, 60), (60, 200, 60), (60, 120, 255), (250, 200, 40)][f]
            px.append((col[0], col[1], col[2], 255))
        elif d < 7.8:
            px.append((0, 0, 0, 128))
        else:
            px.append((0, 0, 0, 0))
        if d < 7 and lx > 9 and ly < 6:
            px[-1] = (255, 255, 255, 255)
open(os.path.join(res, "ball.png"), "wb").write(png(w, h, px))
tw, th = 32, 8
tp = []
for y in range(th):
    for x in range(tw):
        t = x // 8
        base = [(90, 60, 30), (40, 140, 40), (120, 120, 140), (30, 60, 160)][t]
        shade = 30 if (x % 8 == 0 or y == 0) else 0
        tp.append((min(255, base[0] + shade), min(255, base[1] + shade), min(255, base[2] + shade), 255))
open(os.path.join(res, "tiles.png"), "wb").write(png(tw, th, tp))
print("ok")
