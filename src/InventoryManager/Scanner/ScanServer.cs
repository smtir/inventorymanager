using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace InventoryManager.Scanner
{
    public class HttpRequest
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public Dictionary<string, string> Query { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
        public string RemoteAddress { get; set; }

        public string QueryValue(string key)
        {
            string v;
            return Query.TryGetValue(key, out v) ? v : null;
        }
    }

    public class HttpResponse
    {
        public int Status { get; set; }
        public string ContentType { get; set; }
        public byte[] Body { get; set; }

        public static HttpResponse Text(int status, string contentType, string body)
        {
            return new HttpResponse { Status = status, ContentType = contentType, Body = Encoding.UTF8.GetBytes(body ?? "") };
        }
    }

    /// <summary>
    /// Minimal HTTPS server for the phone scanner page. Uses TcpListener (no admin rights / URL ACL needed)
    /// and BouncyCastle TLS 1.2, so it works the same on Windows 7, 8 and 10 regardless of OS TLS settings.
    /// </summary>
    public class ScanServer
    {
        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 64 * 1024;

        private readonly ServerCredentials _credentials;
        private readonly Func<HttpRequest, HttpResponse> _handler;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public int Port { get; private set; }

        public ScanServer(int port, ServerCredentials credentials, Func<HttpRequest, HttpResponse> handler)
        {
            Port = port;
            _credentials = credentials;
            _handler = handler;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ScanServer" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (!_running) return;
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    var remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    var net = client.GetStream();

                    // Peek the first byte: 0x16 = TLS handshake. Anything else is probably plain HTTP,
                    // e.g. the user typed http:// - answer with a redirect to https.
                    int first = net.ReadByte();
                    if (first < 0) return;
                    var stream = new PrefixedStream((byte)first, net);

                    if (first != 0x16)
                    {
                        var plain = ReadRequest(stream, remote);
                        if (plain == null) return;
                        string host;
                        plain.Headers.TryGetValue("host", out host);
                        host = (host ?? "").Split(':')[0];
                        var location = "https://" + host + ":" + Port + plain.Path + RebuildQuery(plain.Query);
                        WriteRaw(stream, "HTTP/1.1 301 Moved Permanently\r\nLocation: " + location + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", null);
                        return;
                    }

                    var protocol = new TlsServerProtocol(stream);
                    protocol.Accept(new ScannerTlsServer(new BcTlsCrypto(new SecureRandom()), _credentials));
                    try
                    {
                        var tls = protocol.Stream;
                        var request = ReadRequest(tls, remote);
                        if (request == null) return;

                        HttpResponse response;
                        try
                        {
                            response = _handler(request) ?? HttpResponse.Text(404, "text/plain", "Not found");
                        }
                        catch (Exception ex)
                        {
                            response = HttpResponse.Text(500, "text/plain", "Server error: " + ex.Message);
                        }
                        WriteResponse(tls, response);
                    }
                    finally
                    {
                        try { protocol.Close(); } catch { }
                    }
                }
                catch
                {
                    // Bad handshakes (e.g. the browser rejecting our certificate before the user accepts it)
                    // and dropped connections are normal; just close the socket.
                }
            }
        }

        private static HttpRequest ReadRequest(Stream stream, string remote)
        {
            // Read until the blank line (\r\n\r\n) that ends the headers.
            var header = new MemoryStream();
            uint last4 = 0;
            while (last4 != 0x0D0A0D0A)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                header.WriteByte((byte)b);
                if (header.Length > MaxHeaderBytes) return null;
                last4 = (last4 << 8) | (uint)b;
            }

            var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                    headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            string body = "";
            string lengthText;
            int length;
            if (headers.TryGetValue("content-length", out lengthText) &&
                int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) && length > 0)
            {
                if (length > MaxBodyBytes) return null;
                var buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(buffer, read, length - read);
                    if (n <= 0) return null;
                    read += n;
                }
                body = Encoding.UTF8.GetString(buffer);
            }

            var target = parts[1];
            var q = target.IndexOf('?');
            return new HttpRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Path = q >= 0 ? target.Substring(0, q) : target,
                Query = ParseQuery(q >= 0 ? target.Substring(q + 1) : ""),
                Headers = headers,
                Body = body,
                RemoteAddress = remote
            };
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((eq >= 0 ? pair.Substring(0, eq) : pair).Replace('+', ' '));
                var value = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')) : "";
                result[key] = value;
            }
            return result;
        }

        private static string RebuildQuery(Dictionary<string, string> query)
        {
            if (query.Count == 0) return "";
            var sb = new StringBuilder("?");
            foreach (var kv in query)
            {
                if (sb.Length > 1) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }
            return sb.ToString();
        }

        private static void WriteResponse(Stream stream, HttpResponse r)
        {
            var body = r.Body ?? new byte[0];
            var head = string.Format(CultureInfo.InvariantCulture,
                "HTTP/1.1 {0} {1}\r\nContent-Type: {2}\r\nContent-Length: {3}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
                r.Status, Reason(r.Status), r.ContentType ?? "application/octet-stream", body.Length);
            WriteRaw(stream, head, body);
        }

        private static void WriteRaw(Stream stream, string head, byte[] body)
        {
            var h = Encoding.ASCII.GetBytes(head);
            stream.Write(h, 0, h.Length);
            if (body != null && body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 301: return "Moved Permanently";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                default: return status >= 500 ? "Internal Server Error" : "OK";
            }
        }

        /// <summary>TLS 1.2 server presenting our self-signed RSA certificate.</summary>
        private sealed class ScannerTlsServer : DefaultTlsServer
        {
            private readonly ServerCredentials _creds;

            public ScannerTlsServer(BcTlsCrypto crypto, ServerCredentials creds) : base(crypto)
            {
                _creds = creds;
            }

            protected override ProtocolVersion[] GetSupportedVersions()
            {
                return ProtocolVersion.TLSv12.Only();
            }

            protected override TlsCredentialedSigner GetRsaSignerCredentials()
            {
                SignatureAndHashAlgorithm alg = null;
                var clientAlgs = m_context.SecurityParameters.ClientSigAlgs;
                if (clientAlgs != null)
                {
                    foreach (SignatureAndHashAlgorithm a in clientAlgs)
                    {
                        if (a.Signature == SignatureAlgorithm.rsa && a.Hash == HashAlgorithm.sha256)
                        {
                            alg = a;
                            break;
                        }
                    }
                }
                if (alg == null)
                    alg = SignatureAndHashAlgorithm.GetInstance(HashAlgorithm.sha256, SignatureAlgorithm.rsa);

                var crypto = (BcTlsCrypto)Crypto;
                var chain = new Certificate(new TlsCertificate[] { new BcTlsCertificate(crypto, _creds.Certificate.GetEncoded()) });
                return new BcDefaultTlsCredentialedSigner(new TlsCryptoParameters(m_context), crypto, _creds.PrivateKey, chain, alg);
            }
        }

        /// <summary>Re-inserts one already-read byte in front of a stream.</summary>
        private sealed class PrefixedStream : Stream
        {
            private readonly Stream _inner;
            private int _prefix;

            public PrefixedStream(byte prefix, Stream inner)
            {
                _prefix = prefix;
                _inner = inner;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0) return 0;
                if (_prefix >= 0)
                {
                    buffer[offset] = (byte)_prefix;
                    _prefix = -1;
                    return 1;
                }
                return _inner.Read(buffer, offset, count);
            }

            public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); }
            public override void Flush() { _inner.Flush(); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }
}
