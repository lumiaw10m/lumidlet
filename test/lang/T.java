import java.util.*;
import java.io.*;

public class T {
    interface Shape { int area(); }
    static abstract class Base implements Shape { String name() { return "base"; } public String toString() { return name() + ":" + area(); } }
    static class Sq extends Base { int s; Sq(int s) { this.s = s; } public int area() { return s * s; } String name() { return "sq"; } }
    static class Key { int k; Key(int k) { this.k = k; } public int hashCode() { return k % 3; } public boolean equals(Object o) { return o instanceof Key && ((Key) o).k == k; } }
    static int fib(int n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); }
    static int counter;
    static synchronized void inc() { counter++; }
    static long lsum(long a, long b) { return a * b + (a >>> 3) - (b << 2); }

    public static void main(String[] args) throws Exception {
        StringBuffer sb = new StringBuffer();
        sb.append("x=").append(5).append(',').append(2.5f).append(',').append(1e20).append(',').append(true).append(',').append(123456789012L);
        System.out.println(sb.toString());
        System.out.println("fib(20)=" + fib(20));
        Shape s = new Sq(7);
        System.out.println("shape " + s + " " + (s instanceof Base));
        int[] a = new int[10];
        for (int i = 0; i < a.length; i++) a[i] = i * i;
        int[][] m = new int[3][4];
        m[2][3] = 9;
        System.out.println("arr " + a[9] + " " + m[2][3] + " " + m.length + " " + m[0].length);
        try { a[10] = 1; } catch (ArrayIndexOutOfBoundsException e) { System.out.println("caught AIOOBE"); }
        try { Object o = null; o.toString(); } catch (NullPointerException e) { System.out.println("caught NPE"); }
        try { int z = 0; System.out.println(5 / z); } catch (ArithmeticException e) { System.out.println("caught " + e.getMessage()); }
        try { throw new IOException("boom"); } catch (Exception e) { System.out.println("caught " + e); } finally { System.out.println("finally"); }
        Vector v = new Vector();
        for (int i = 0; i < 5; i++) v.addElement(new Integer(i));
        v.removeElement(new Integer(2));
        System.out.println("vector " + v + " size " + v.size() + " has3 " + v.contains(new Integer(3)));
        Hashtable h = new Hashtable();
        for (int i = 0; i < 10; i++) h.put(new Key(i), "v" + i);
        System.out.println("hash " + h.get(new Key(7)) + " " + h.size());
        Hashtable hs = new Hashtable(); hs.put("a", "1"); hs.put("b", "2");
        System.out.println("hs " + hs.get("b") + " " + hs.containsKey("a"));
        String str = "Hello, World";
        System.out.println(str.toUpperCase() + " " + str.indexOf("World") + " " + str.substring(7) + " " + str.charAt(4) + " " + str.hashCode() + " " + "a,b".equals("a" + ",b"));
        System.out.println("parse " + Integer.parseInt("-1234") + " " + Integer.toHexString(-1) + " " + Long.parseLong("9999999999") + " " + Integer.toString(255, 2));
        System.out.println("long " + lsum(123456789L, -987654321L) + " " + (Long.MIN_VALUE / -1) + " " + (int) 3.99e10 + " " + (char) 65);
        System.out.println("float " + (1.0f / 3) + " " + Math.sqrt(2) + " " + (0.1 + 0.2) + " " + (float) 100 + " " + 1.0e-5 + " " + Math.round(2.5));
        Random r = new Random(42);
        System.out.println("rand " + r.nextInt() + " " + r.nextInt(100) + " " + r.nextLong());
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        DataOutputStream dos = new DataOutputStream(bo);
        dos.writeInt(0x12345678); dos.writeUTF("héllo"); dos.writeLong(-2); dos.writeDouble(3.25); dos.writeBoolean(true);
        DataInputStream dis = new DataInputStream(new ByteArrayInputStream(bo.toByteArray()));
        System.out.println("data " + Integer.toHexString(dis.readInt()) + " " + dis.readUTF() + " " + dis.readLong() + " " + dis.readDouble() + " " + dis.readBoolean() + " len " + bo.size());
        final Object lock = new Object();
        Thread[] ts = new Thread[4];
        for (int i = 0; i < 4; i++) { ts[i] = new Thread(new Runnable() { public void run() { for (int j = 0; j < 10000; j++) inc(); } }); ts[i].start(); }
        for (int i = 0; i < 4; i++) ts[i].join();
        System.out.println("threads counter " + counter);
        switch (counter % 7) { case 0: System.out.println("sw0"); break; case 2: System.out.println("sw2"); break; default: System.out.println("swd " + (counter % 7)); }
        String k = "beta";
        int code = 0;
        if (k == "beta") code = 1;
        System.out.println("interned " + code + " " + new String("x").equals("x") + " " + new String(new char[]{'o','k'}));
        char[] cs = str.toCharArray(); Arrays_reverse(cs);
        System.out.println("rev " + new String(cs));
        long t0 = System.currentTimeMillis(); int acc = 0;
        for (int i = 0; i < 20000000; i++) acc += i ^ (i >> 3);
        System.out.println("loop " + acc + " in " + (System.currentTimeMillis() - t0) + "ms");
        Object o = new int[3]; System.out.println("clone " + ((int[]) ((int[]) o).clone()).length + " " + (o instanceof int[]));
        String[] names = {"b", "a"}; Object[] objs = names; System.out.println("arr cast " + (objs instanceof String[]) + " " + names.getClass().getName());
    }
    static void Arrays_reverse(char[] c) { for (int i = 0, j = c.length - 1; i < j; i++, j--) { char x = c[i]; c[i] = c[j]; c[j] = x; } }
}
