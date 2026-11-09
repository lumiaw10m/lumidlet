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
            throw new JavaThrow(IO + "ConnectionNotFoundException", url);
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
