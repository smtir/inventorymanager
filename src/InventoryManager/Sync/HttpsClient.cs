using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace InventoryManager.Sync
{
    /// <summary>
    /// Small HTTPS client for the Google Sheet sync.
    /// Uses Windows' own HTTPS first; if that fails at the TLS level (typical on Windows 7, where TLS 1.2
    /// is off by default) it falls back to BouncyCastle TLS with the certificate checked against the
    /// Windows certificate store and the host name.
    /// </summary>
    public static class HttpsClient
    {
        /// <summary>For testing: skip the Windows HTTPS stack and always use the BouncyCastle path.</summary>
        public static bool ForceBouncyCastle { get; set; }

        static HttpsClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768;   // TLS 1.2 | 1.1
            }
            catch (NotSupportedException)
            {
            }
        }

        public static string Post(string url, string json)
        {
            return Send("POST", url, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8");
        }

        public static string Get(string url)
        {
            return Send("GET", url, null, null);
        }

        private static string Send(string method, string url, byte[] body, string contentType)
        {
            if (!ForceBouncyCastle)
            {
                try
                {
                    return NativeSend(method, url, body, contentType);
                }
                catch (WebException ex)
                {
                    if (ex.Status != WebExceptionStatus.SecureChannelFailure && ex.Status != WebExceptionStatus.TrustFailure &&
                        ex.Status != WebExceptionStatus.SendFailure && ex.Status != WebExceptionStatus.ReceiveFailure &&
                        ex.Status != WebExceptionStatus.ConnectionClosed)
                        throw;
                    // TLS problem with the Windows stack: try BouncyCastle below.
                }
            }
            return BcSend(method, new Uri(url), body, contentType, 5);
        }

        // ---------------- Windows HTTPS ----------------

        private static string NativeSend(string method, string url, byte[] body, string contentType)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;       // Apps Script answers POST with 302 → GET of the result
            req.UserAgent = "InventoryManager";
            if (body != null)
            {
                req.ContentType = contentType;
                req.ContentLength = body.Length;
                using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) throw;
                using (resp)
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    throw new IOException(string.Format("HTTP {0}: {1}", (int)resp.StatusCode, Truncate(reader.ReadToEnd())));
            }
        }

        // ---------------- BouncyCastle fallback ----------------

        private static string BcSend(string method, Uri uri, byte[] body, string contentType, int redirectsLeft)
        {
            if (uri.Scheme != "https") throw new IOException("Only https:// links are supported.");
            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = 60000;
                tcp.SendTimeout = 60000;
                tcp.Connect(uri.Host, uri.Port);
                var protocol = new TlsClientProtocol(tcp.GetStream());
                protocol.Connect(new VerifiedTlsClient(new BcTlsCrypto(new SecureRandom()), uri.Host));
                try
                {
                    var stream = protocol.Stream;
                    var head = new StringBuilder();
                    head.Append(method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                    head.Append("Host: ").Append(uri.Host).Append("\r\n");
                    head.Append("User-Agent: InventoryManager\r\nAccept: */*\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
                    if (body != null)
                        head.Append("Content-Type: ").Append(contentType).Append("\r\nContent-Length: ").Append(body.Length).Append("\r\n");
                    head.Append("\r\n");
                    var h = Encoding.ASCII.GetBytes(head.ToString());
                    stream.Write(h, 0, h.Length);
                    if (body != null) stream.Write(body, 0, body.Length);
                    stream.Flush();

                    int status;
                    Dictionary<string, string> headers;
                    var content = ReadResponse(stream, out status, out headers);

                    if (status >= 300 && status < 400 && headers.ContainsKey("location"))
                    {
                        if (redirectsLeft <= 0) throw new IOException("Too many redirects.");
                        var next = new Uri(uri, headers["location"]);
                        bool keep = status == 307 || status == 308;
                        return BcSend(keep ? method : "GET", next, keep ? body : null, keep ? contentType : null, redirectsLeft - 1);
                    }
                    var text = Encoding.UTF8.GetString(content);
                    if (status >= 400) throw new IOException(string.Format("HTTP {0}: {1}", status, Truncate(text)));
                    return text;
                }
                finally
                {
                    try { protocol.Close(); } catch { }
                }
            }
        }

        private static byte[] ReadResponse(Stream s, out int status, out Dictionary<string, string> headers)
        {
            var headerBytes = new MemoryStream();
            uint last4 = 0;
            while (last4 != 0x0D0A0D0A)
            {
                int b = s.ReadByte();
                if (b < 0) throw new IOException("Connection closed before the response headers.");
                headerBytes.WriteByte((byte)b);
                last4 = (last4 << 8) | (uint)b;
                if (headerBytes.Length > 65536) throw new IOException("Response headers too large.");
            }
            var lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var parts = lines[0].Split(' ');
            status = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            var body = new MemoryStream();
            string te, cl;
            if (headers.TryGetValue("transfer-encoding", out te) && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                while (true)
                {
                    var sizeLine = ReadLine(s);
                    int semi = sizeLine.IndexOf(';');
                    int size = int.Parse(semi >= 0 ? sizeLine.Substring(0, semi) : sizeLine, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (size == 0) break;
                    CopyExactly(s, body, size);
                    ReadLine(s);   // CRLF after each chunk
                }
            }
            else if (headers.TryGetValue("content-length", out cl))
            {
                CopyExactly(s, body, int.Parse(cl, CultureInfo.InvariantCulture));
            }
            else
            {
                var buf = new byte[8192];
                try
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) body.Write(buf, 0, n);
                }
                catch (IOException)
                {
                    // Some servers close without a TLS close_notify; keep what we have.
                }
            }
            return body.ToArray();
        }

        private static string ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
            }
            return sb.ToString();
        }

        private static void CopyExactly(Stream s, Stream target, int count)
        {
            var buf = new byte[8192];
            while (count > 0)
            {
                int n = s.Read(buf, 0, Math.Min(buf.Length, count));
                if (n <= 0) throw new IOException("Connection closed in the middle of the response.");
                target.Write(buf, 0, n);
                count -= n;
            }
        }

        private static string Truncate(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        /// <summary>TLS client that sends SNI and verifies the server certificate (chain + host name).</summary>
        private sealed class VerifiedTlsClient : DefaultTlsClient
        {
            private readonly string _host;

            public VerifiedTlsClient(BcTlsCrypto crypto, string host) : base(crypto)
            {
                _host = host;
            }

            protected override IList GetSniServerNames()
            {
                return new ArrayList { new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(_host)) };
            }

            public override TlsAuthentication GetAuthentication()
            {
                return new ServerAuthentication(_host);
            }
        }

        private sealed class ServerAuthentication : TlsAuthentication
        {
            private readonly string _host;

            public ServerAuthentication(string host)
            {
                _host = host;
            }

            public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
            {
                var chainCerts = serverCertificate.Certificate;
                if (chainCerts == null || chainCerts.IsEmpty)
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);

                var leaf = new X509Certificate2(chainCerts.GetCertificateAt(0).GetEncoded());
                var chain = new X509Chain();   // not IDisposable on .NET 4.5
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;   // revocation servers may be unreachable; chain + name are still checked
                for (int i = 1; i < chainCerts.Length; i++)
                    chain.ChainPolicy.ExtraStore.Add(new X509Certificate2(chainCerts.GetCertificateAt(i).GetEncoded()));
                if (!chain.Build(leaf))
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
                if (!HostMatches(leaf, _host))
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
            }

            public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
            {
                return null;
            }

            private static bool HostMatches(X509Certificate2 cert, string host)
            {
                var bc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(cert.RawData);
                var names = new List<string>();
                var sans = bc.GetSubjectAlternativeNames();
                if (sans != null)
                    foreach (IList entry in sans)
                        if (entry.Count >= 2 && Convert.ToInt32(entry[0]) == 2)   // dNSName
                            names.Add(Convert.ToString(entry[1]));
                return names.Any(n => Matches(n, host));
            }

            private static bool Matches(string pattern, string host)
            {
                pattern = pattern.ToLowerInvariant();
                host = host.ToLowerInvariant();
                if (pattern == host) return true;
                if (pattern.StartsWith("*."))
                {
                    int dot = host.IndexOf('.');
                    return dot > 0 && host.Substring(dot) == pattern.Substring(1);
                }
                return false;
            }
        }
    }
}
