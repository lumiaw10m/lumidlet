using System;
using System.Collections.Generic;
using System.Net.Http;
using Lumidlet.Lib;
using Lumidlet.Vm;

namespace Lumidlet.Midp
{
    public sealed class HttpConnObj : JObject
    {
        public string Url, Method = "GET";
        public readonly List<KeyValuePair<string, string>> ReqHeaders = new List<KeyValuePair<string, string>>();
        public ByteArrayOutStream Body;
        public bool Done, Closed;
        public int Code;
        public string Message;
        public readonly List<KeyValuePair<string, string>> RespHeaders = new List<KeyValuePair<string, string>>();
        public byte[] Data;
    }

    public sealed class SocketConnObj : JObject
    {
        public string Host;
        public int Port;
        public System.Net.Sockets.TcpClient Tcp;
        public System.IO.Stream Stream;
        public volatile bool Closed;
    }

    public sealed class SockInStream : InStreamObj
    {
        public SocketConnObj Conn;
        bool closed;
        readonly byte[] one = new byte[1];

        void Check()
        {
            if (closed || Conn.Closed) throw new JavaThrow("java/io/IOException", "stream closed");
        }

        public override int Read(JThread t)
        {
            Check();
            int n = Io(() => Conn.Stream.Read(one, 0, 1));
            return n <= 0 ? -1 : one[0];
        }

        public override int Read(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            Check();
            if (len == 0) return 0;
            var buf = new byte[len];
            int n = Io(() => Conn.Stream.Read(buf, 0, len));
            if (n <= 0) return -1;
            Buffer.BlockCopy(buf, 0, b, off, n);
            return n;
        }

        public override int Available(JThread t)
        {
            Check();
            try { return Conn.Tcp.Available; } catch (Exception) { return 0; }
        }

        public override void Close(JThread t) { closed = true; }

        internal int Io(Func<int> f)
        {
            try { return f(); }
            catch (Exception e) when (!(e is JavaThrow) && !(e is VmHalt))
            {
                if (Conn.Closed) throw new JavaThrow("java/io/IOException", "connection closed");
                throw new JavaThrow("java/io/IOException", e.Message);
            }
        }
    }

    public sealed class SockOutStream : OutStreamObj
    {
        public SocketConnObj Conn;
        bool closed;

        void Send(byte[] data, int off, int len)
        {
            if (closed || Conn.Closed) throw new JavaThrow("java/io/IOException", "stream closed");
            try { Conn.Stream.Write(data, off, len); }
            catch (Exception e) when (!(e is JavaThrow))
            {
                throw new JavaThrow("java/io/IOException", e.Message);
            }
        }

        public override void Write(JThread t, int b) => Send(new[] { (byte)b }, 0, 1);

        public override void Write(JThread t, sbyte[] b, int off, int len)
        {
            if (b == null) throw new NullReferenceException();
            if (off < 0 || len < 0 || off + len > b.Length) throw new JavaThrow("java/lang/IndexOutOfBoundsException");
            Send(Enc.ToU(b, off, len), 0, len);
        }

        public override void Flush(JThread t)
        {
            if (closed || Conn.Closed) throw new JavaThrow("java/io/IOException", "stream closed");
            try { Conn.Stream.Flush(); } catch (Exception e) { throw new JavaThrow("java/io/IOException", e.Message); }
        }

        public override void Close(JThread t) { closed = true; }
    }

    public static class Net
    {
        const string IO = "javax/microedition/io/";
        static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static byte[] Fetch(string url)
        {
            try { return client.GetByteArrayAsync(url).Result; }
            catch (Exception) { return null; }
        }

        static HttpConnObj H(JThread t, int b)
        {
            var h = (HttpConnObj)t.R[b];
            if (h.Closed) throw new JavaThrow("java/io/IOException", "connection closed");
            return h;
        }

        static void Perform(JThread t, HttpConnObj h)
        {
            if (h.Done) return;
            h.Done = true;
            try
            {
                var req = new HttpRequestMessage(new HttpMethod(h.Method), h.Url);
                string ctype = null;
                foreach (var kv in h.ReqHeaders)
                {
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) { ctype = kv.Value; continue; }
                    if (string.Equals(kv.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
                if (h.Method == "POST" || (h.Body != null && h.Body.Count > 0))
                {
                    var body = h.Body != null ? Enc.ToU(h.Body.ToArray()) : new byte[0];
                    req.Content = new ByteArrayContent(body);
                    if (ctype != null) req.Content.Headers.TryAddWithoutValidation("Content-Type", ctype);
                }
                var resp = client.SendAsync(req).Result;
                h.Code = (int)resp.StatusCode;
                h.Message = resp.ReasonPhrase;
                foreach (var kv in resp.Headers) h.RespHeaders.Add(new KeyValuePair<string, string>(kv.Key, string.Join(", ", kv.Value)));
                foreach (var kv in resp.Content.Headers) h.RespHeaders.Add(new KeyValuePair<string, string>(kv.Key, string.Join(", ", kv.Value)));
                h.Data = resp.Content.ReadAsByteArrayAsync().Result;
            }
            catch (Exception e)
            {
                var inner = e is AggregateException && e.InnerException != null ? e.InnerException : e;
                throw new JavaThrow("java/io/IOException", inner.Message);
            }
        }

        static string Header(HttpConnObj h, string name)
        {
            foreach (var kv in h.RespHeaders) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        static JObject Open(JThread t, string url)
        {
            if (url == null) throw new JavaThrow("java/lang/IllegalArgumentException", "null url");
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var h = (HttpConnObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/HttpConnection"));
                h.Url = url;
                return h;
            }
            if (url.StartsWith("socket://", StringComparison.OrdinalIgnoreCase)) return OpenSocket(t, url);
            throw new JavaThrow(IO + "ConnectionNotFoundException", url);
        }

        static JObject OpenSocket(JThread t, string url)
        {
            var addr = url.Substring(9);
            int semi = addr.IndexOf(';');
            if (semi >= 0) addr = addr.Substring(0, semi);
            int colon = addr.LastIndexOf(':');
            int port;
            if (colon <= 0 || !int.TryParse(addr.Substring(colon + 1), out port) || port <= 0 || port > 65535)
                throw new JavaThrow(IO + "ConnectionNotFoundException", "server sockets are not supported: " + url);
            var host = addr.Substring(0, colon).Trim('[', ']');
            var rt = MidpRuntime.Of(t);
            var c = (SocketConnObj)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/SocketConnection"));
            c.Host = host; c.Port = port;
            var tcp = new System.Net.Sockets.TcpClient();
            try
            {
                var task = tcp.ConnectAsync(host, port);
                if (!task.Wait(20000)) { tcp.Dispose(); throw new JavaThrow("java/io/IOException", "connection timed out: " + host + ":" + port); }
            }
            catch (AggregateException e)
            {
                tcp.Dispose();
                throw new JavaThrow("java/io/IOException", (e.InnerException ?? e).Message);
            }
            c.Tcp = tcp;
            c.Stream = tcp.GetStream();
            lock (rt.Sockets) rt.Sockets.Add(c);
            rt.Host.Log("socket open " + host + ":" + port);
            return c;
        }

        static SocketConnObj Sock(JThread t, int b)
        {
            var c = (SocketConnObj)t.R[b];
            if (c.Closed) throw new JavaThrow("java/io/IOException", "connection closed");
            return c;
        }

        static JObject SockIn(JThread t, SocketConnObj c, bool data)
        {
            var s = (SockInStream)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/SocketInputStream"));
            s.Conn = c;
            if (!data) return s;
            var d = (FilterInStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/DataInputStream"));
            d.In = s;
            return d;
        }

        static JObject SockOut(JThread t, SocketConnObj c, bool data)
        {
            var s = (SockOutStream)t.Vm.NewObject(t.Vm.LoadClass("lumidlet/SocketOutputStream"));
            s.Conn = c;
            if (!data) return s;
            var d = (FilterOutStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/DataOutputStream"));
            d.Out = s;
            return d;
        }

        public static void CloseSocket(SocketConnObj c)
        {
            if (c.Closed) return;
            c.Closed = true;
            try { c.Stream?.Dispose(); } catch (Exception) { }
            try { c.Tcp?.Dispose(); } catch (Exception) { }
        }

        static Uri U(HttpConnObj h) => new Uri(h.Url);

        public static void Register(NativeRegistry n)
        {
            n.Interface(IO + "Connection", c => c.Abstract("close", "()V"));
            n.Interface(IO + "InputConnection", null, new[] { IO + "Connection" });
            n.Interface(IO + "OutputConnection", null, new[] { IO + "Connection" });
            n.Interface(IO + "StreamConnection", null, new[] { IO + "InputConnection", IO + "OutputConnection" });
            n.Interface(IO + "ContentConnection", null, new[] { IO + "StreamConnection" });
            n.Interface(IO + "HttpConnection", null, new[] { IO + "ContentConnection" });
            n.Interface(IO + "HttpsConnection", null, new[] { IO + "HttpConnection" });
            n.Interface(IO + "SocketConnection", null, new[] { IO + "StreamConnection" });

            n.Define("lumidlet/SocketInputStream", "java/io/InputStream", c => c
                .M("read", "()I", (t, b) => t.RetI(b, ((InStreamObj)t.R[b]).Read(t))), Rt.F<SockInStream>());
            n.Define("lumidlet/SocketOutputStream", "java/io/OutputStream", c => c
                .M("write", "(I)V", (t, b) => ((OutStreamObj)t.R[b]).Write(t, t.I(b + 1))), Rt.F<SockOutStream>());

            n.Define("lumidlet/SocketConnection", "java/lang/Object", c => c
                .M("close", "()V", (t, b) => CloseSocket((SocketConnObj)t.R[b]))
                .M("openInputStream", "()Ljava/io/InputStream;", (t, b) => t.RetA(b, SockIn(t, Sock(t, b), false)))
                .M("openDataInputStream", "()Ljava/io/DataInputStream;", (t, b) => t.RetA(b, SockIn(t, Sock(t, b), true)))
                .M("openOutputStream", "()Ljava/io/OutputStream;", (t, b) => t.RetA(b, SockOut(t, Sock(t, b), false)))
                .M("openDataOutputStream", "()Ljava/io/DataOutputStream;", (t, b) => t.RetA(b, SockOut(t, Sock(t, b), true)))
                .M("getAddress", "()Ljava/lang/String;", (t, b) => t.RetA(b, Sock(t, b).Host))
                .M("getPort", "()I", (t, b) => t.RetI(b, Sock(t, b).Port))
                .M("getLocalAddress", "()Ljava/lang/String;", (t, b) =>
                {
                    var ep = Sock(t, b).Tcp.Client?.LocalEndPoint as System.Net.IPEndPoint;
                    t.RetA(b, ep != null ? ep.Address.ToString() : "127.0.0.1");
                })
                .M("getLocalPort", "()I", (t, b) =>
                {
                    var ep = Sock(t, b).Tcp.Client?.LocalEndPoint as System.Net.IPEndPoint;
                    t.RetI(b, ep != null ? ep.Port : 0);
                })
                .M("setSocketOption", "(BI)V", (t, b) =>
                {
                    var tcp = Sock(t, b).Tcp; int v = t.I(b + 2);
                    try
                    {
                        switch (t.I(b + 1))
                        {
                            case 0: tcp.NoDelay = v == 0; break;
                            case 1: tcp.LingerState = new System.Net.Sockets.LingerOption(v > 0, Math.Max(0, v)); break;
                            case 2: tcp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, v != 0); break;
                            case 3: if (v > 0) tcp.ReceiveBufferSize = v; break;
                            case 4: if (v > 0) tcp.SendBufferSize = v; break;
                            default: throw new JavaThrow("java/lang/IllegalArgumentException", "socket option");
                        }
                    }
                    catch (Exception e) when (!(e is JavaThrow)) { }
                })
                .M("getSocketOption", "(B)I", (t, b) =>
                {
                    var tcp = Sock(t, b).Tcp;
                    switch (t.I(b + 1))
                    {
                        case 0: t.RetI(b, tcp.NoDelay ? 0 : 1); break;
                        case 1: t.RetI(b, tcp.LingerState != null && tcp.LingerState.Enabled ? tcp.LingerState.LingerTime : 0); break;
                        case 2: t.RetI(b, 1); break;
                        case 3: t.RetI(b, tcp.ReceiveBufferSize); break;
                        case 4: t.RetI(b, tcp.SendBufferSize); break;
                        default: throw new JavaThrow("java/lang/IllegalArgumentException", "socket option");
                    }
                }),
                Rt.F<SocketConnObj>(), new[] { IO + "SocketConnection" });

            n.Define(IO + "Connector", "java/lang/Object", c => c
                .S("open", "(Ljava/lang/String;)L" + IO + "Connection;", (t, b) => t.RetA(b, Open(t, (string)t.R[b])))
                .S("open", "(Ljava/lang/String;I)L" + IO + "Connection;", (t, b) => t.RetA(b, Open(t, (string)t.R[b])))
                .S("open", "(Ljava/lang/String;IZ)L" + IO + "Connection;", (t, b) => t.RetA(b, Open(t, (string)t.R[b])))
                .S("openInputStream", "(Ljava/lang/String;)Ljava/io/InputStream;", (t, b) =>
                {
                    var h = (HttpConnObj)Open(t, (string)t.R[b]);
                    Perform(t, h);
                    t.RetA(b, Stream(t, h.Data));
                })
                .S("openDataInputStream", "(Ljava/lang/String;)Ljava/io/DataInputStream;", (t, b) =>
                {
                    var h = (HttpConnObj)Open(t, (string)t.R[b]);
                    Perform(t, h);
                    var d = (FilterInStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/DataInputStream"));
                    d.In = Stream(t, h.Data);
                    t.RetA(b, d);
                }));

            n.Define("lumidlet/HttpConnection", "java/lang/Object", c => c
                .M("close", "()V", (t, b) => ((HttpConnObj)t.R[b]).Closed = true)
                .M("getURL", "()Ljava/lang/String;", (t, b) => t.RetA(b, H(t, b).Url))
                .M("getProtocol", "()Ljava/lang/String;", (t, b) => t.RetA(b, U(H(t, b)).Scheme))
                .M("getHost", "()Ljava/lang/String;", (t, b) => t.RetA(b, U(H(t, b)).Host))
                .M("getFile", "()Ljava/lang/String;", (t, b) => t.RetA(b, U(H(t, b)).AbsolutePath))
                .M("getRef", "()Ljava/lang/String;", (t, b) => { var f = U(H(t, b)).Fragment; t.RetA(b, string.IsNullOrEmpty(f) ? null : f.TrimStart('#')); })
                .M("getQuery", "()Ljava/lang/String;", (t, b) => { var q = U(H(t, b)).Query; t.RetA(b, string.IsNullOrEmpty(q) ? null : q.TrimStart('?')); })
                .M("getPort", "()I", (t, b) => t.RetI(b, U(H(t, b)).Port))
                .M("getRequestMethod", "()Ljava/lang/String;", (t, b) => t.RetA(b, H(t, b).Method))
                .M("setRequestMethod", "(Ljava/lang/String;)V", (t, b) => { var h = H(t, b); if (h.Done) throw new JavaThrow("java/io/IOException", "already connected"); h.Method = (string)t.R[b + 1]; })
                .M("getRequestProperty", "(Ljava/lang/String;)Ljava/lang/String;", (t, b) =>
                {
                    var h = H(t, b); string v = null;
                    foreach (var kv in h.ReqHeaders) if (string.Equals(kv.Key, (string)t.R[b + 1], StringComparison.OrdinalIgnoreCase)) v = kv.Value;
                    t.RetA(b, v);
                })
                .M("setRequestProperty", "(Ljava/lang/String;Ljava/lang/String;)V", (t, b) => { var h = H(t, b); if (h.Done) throw new JavaThrow("java/io/IOException", "already connected"); h.ReqHeaders.Add(new KeyValuePair<string, string>((string)t.R[b + 1], (string)t.R[b + 2])); })
                .M("getResponseCode", "()I", (t, b) => { var h = H(t, b); Perform(t, h); t.RetI(b, h.Code); })
                .M("getResponseMessage", "()Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); t.RetA(b, h.Message); })
                .M("getHeaderField", "(Ljava/lang/String;)Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); t.RetA(b, Header(h, (string)t.R[b + 1])); })
                .M("getHeaderField", "(I)Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); int i = t.I(b + 1); t.RetA(b, i >= 0 && i < h.RespHeaders.Count ? h.RespHeaders[i].Value : null); })
                .M("getHeaderFieldKey", "(I)Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); int i = t.I(b + 1); t.RetA(b, i >= 0 && i < h.RespHeaders.Count ? h.RespHeaders[i].Key : null); })
                .M("getHeaderFieldInt", "(Ljava/lang/String;I)I", (t, b) => { var h = H(t, b); Perform(t, h); int v; t.RetI(b, int.TryParse(Header(h, (string)t.R[b + 1]), out v) ? v : t.I(b + 2)); })
                .M("getHeaderFieldDate", "(Ljava/lang/String;J)J", (t, b) => { var h = H(t, b); Perform(t, h); DateTimeOffset d; t.RetJ(b, DateTimeOffset.TryParse(Header(h, (string)t.R[b + 1]) ?? "", out d) ? d.ToUnixTimeMilliseconds() : t.J(b + 2)); })
                .M("getExpiration", "()J", (t, b) => t.RetJ(b, 0))
                .M("getDate", "()J", (t, b) => t.RetJ(b, 0))
                .M("getLastModified", "()J", (t, b) => t.RetJ(b, 0))
                .M("getType", "()Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); t.RetA(b, Header(h, "Content-Type")); })
                .M("getEncoding", "()Ljava/lang/String;", (t, b) => { var h = H(t, b); Perform(t, h); t.RetA(b, Header(h, "Content-Encoding")); })
                .M("getLength", "()J", (t, b) => { var h = H(t, b); Perform(t, h); t.RetJ(b, h.Data != null ? h.Data.Length : -1); })
                .M("openInputStream", "()Ljava/io/InputStream;", (t, b) => { var h = H(t, b); Perform(t, h); t.RetA(b, Stream(t, h.Data)); })
                .M("openDataInputStream", "()Ljava/io/DataInputStream;", (t, b) =>
                {
                    var h = H(t, b); Perform(t, h);
                    var d = (FilterInStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/DataInputStream"));
                    d.In = Stream(t, h.Data);
                    t.RetA(b, d);
                })
                .M("openOutputStream", "()Ljava/io/OutputStream;", (t, b) =>
                {
                    var h = H(t, b);
                    if (h.Body == null) h.Body = (ByteArrayOutStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/ByteArrayOutputStream"));
                    t.RetA(b, h.Body);
                })
                .M("openDataOutputStream", "()Ljava/io/DataOutputStream;", (t, b) =>
                {
                    var h = H(t, b);
                    if (h.Body == null) h.Body = (ByteArrayOutStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/ByteArrayOutputStream"));
                    var d = (FilterOutStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/DataOutputStream"));
                    d.Out = h.Body;
                    t.RetA(b, d);
                }),
                Rt.F<HttpConnObj>(), new[] { IO + "HttpConnection" });
        }

        static InStreamObj Stream(JThread t, byte[] data)
        {
            var s = (ByteArrayInStream)t.Vm.NewObject(t.Vm.LoadClass("java/io/ByteArrayInputStream"));
            var d = Enc.ToS(data ?? new byte[0]);
            s.Set(d, 0, d.Length);
            return s;
        }
    }

    public static class MidletLib
    {
        const string M = "javax/microedition/midlet/";

        public static void Register(NativeRegistry n)
        {
            n.Define(M + "MIDlet", "java/lang/Object", c => c
                .M("<init>", "()V", (t, b) => { var rt = MidpRuntime.Of(t); if (rt.Midlet == null) rt.Midlet = (JObject)t.R[b]; })
                .Abstract("startApp", "()V")
                .Abstract("pauseApp", "()V")
                .Abstract("destroyApp", "(Z)V")
                .M("getAppProperty", "(Ljava/lang/String;)Ljava/lang/String;", (t, b) => t.RetA(b, MidpRuntime.Of(t).AppProperty((string)t.R[b + 1])))
                .M("notifyDestroyed", "()V", (t, b) => MidpRuntime.Of(t).Stop(null))
                .M("notifyPaused", "()V", (t, b) => { })
                .M("resumeRequest", "()V", (t, b) => { })
                .M("checkPermission", "(Ljava/lang/String;)I", (t, b) => t.RetI(b, 1))
                .M("platformRequest", "(Ljava/lang/String;)Z", (t, b) =>
                {
                    var url = (string)t.R[b + 1];
                    if (url == null) throw new NullReferenceException();
                    if (url.Length > 0) MidpRuntime.Of(t).Host.PlatformRequest(url);
                    t.RetZ(b, false);
                }), null, access: 0x0401);
        }
    }
}
