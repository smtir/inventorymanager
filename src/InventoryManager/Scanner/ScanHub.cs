using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.UI;

namespace InventoryManager.Scanner
{
    /// <summary>Result of handling one scan, sent back to the phone as JSON.</summary>
    public class ScanResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string ProductName { get; set; }
        public decimal? Price { get; set; }
        public int? Quantity { get; set; }

        public static ScanResult Fail(string message)
        {
            return new ScanResult { Ok = false, Message = message };
        }
    }

    /// <summary>Anything that can receive a barcode: the POS screen, or an open product dialog.</summary>
    public interface IScanTarget
    {
        /// <summary>Called on the UI thread. Must not show modal dialogs (the phone is waiting for the answer).</summary>
        ScanResult HandleScan(string code);
    }

    public class PosStatus
    {
        public bool PosOpen { get; set; }
        public bool IsSale { get; set; }
        public int CartCount { get; set; }
        public decimal CartTotal { get; set; }
        public List<PosLine> Lines { get; set; }
    }

    public class PosLine
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int Qty { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>Implemented by the main window: tells the hub where scans should go.</summary>
    public interface IScanHost
    {
        IScanTarget CurrentScanTarget { get; }
        PosStatus GetPosStatus();
        void SetPosMode(bool isSale);

        /// <summary>Cart change from the phone: "undo", "inc", "dec", "remove" or "clear".</summary>
        ScanResult CartAction(string action, long productId);
    }

    /// <summary>
    /// Runs the phone-scanner HTTPS server and routes its requests to the UI thread.
    /// </summary>
    public static class ScanHub
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static ScanServer _server;
        private static Control _ui;
        private static IScanHost _host;

        public static bool IsRunning { get { return _server != null; } }
        public static int Port { get { return _server != null ? _server.Port : AppSettings.ScannerPort; } }
        public static DateTime? LastSeen { get; private set; }
        public static string LastDevice { get; private set; }

        /// <summary>True when the phone talked to us in the last few seconds (the page polls every 2 s).</summary>
        public static bool PhoneConnected
        {
            get { return LastSeen.HasValue && (DateTime.Now - LastSeen.Value).TotalSeconds < 8; }
        }

        public static event EventHandler StateChanged;

        public static string Token
        {
            get
            {
                if (string.IsNullOrEmpty(AppSettings.ScannerToken))
                {
                    AppSettings.ScannerToken = NewToken();
                    AppSettings.Save();
                }
                return AppSettings.ScannerToken;
            }
        }

        /// <summary>Forgets the paired phone(s): the old QR code stops working.</summary>
        public static void ResetPairing()
        {
            AppSettings.ScannerToken = NewToken();
            AppSettings.Save();
            LastSeen = null;
            OnStateChanged();
        }

        public static void Start(Control ui, IScanHost host)
        {
            if (_server != null) return;
            _ui = ui;
            _host = host;
            var token = Token;   // make sure a pairing token exists (saved on the UI thread)

            var ips = GetLocalAddresses();
            var creds = CertificateManager.LoadOrCreate(ips);
            var server = new ScanServer(AppSettings.ScannerPort, creds, Handle);
            try
            {
                server.Start();
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(L.F(
                    "Could not start the phone scanner on port {0}. Another program may be using it.\n\nChange the port in Settings and try again.\n\n({1})",
                    AppSettings.ScannerPort, ex.Message));
            }
            _server = server;
            OnStateChanged();
        }

        public static void Stop()
        {
            if (_server == null) return;
            _server.Stop();
            _server = null;
            LastSeen = null;
            OnStateChanged();
        }

        public static string UrlFor(string ip)
        {
            return string.Format("https://{0}:{1}/?t={2}", ip, Port, Token);
        }

        /// <summary>IPv4 addresses of active network adapters, adapters with a gateway (real LAN/Wi-Fi) first.</summary>
        public static List<string> GetLocalAddresses()
        {
            var result = new List<KeyValuePair<string, bool>>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); } catch { continue; }
                bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().Equals("0.0.0.0"));

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                    result.Add(new KeyValuePair<string, bool>(ip, hasGateway));
                }
            }
            return result.OrderByDescending(r => r.Value).Select(r => r.Key).Distinct().ToList();
        }

        // ---------------- Request handling (network threads) ----------------

        private static HttpResponse Handle(HttpRequest req)
        {
            if (req.Method == "GET" && (req.Path == "/" || req.Path == "/index.html"))
                return Resource("scanner.html", "text/html; charset=utf-8");
            if (req.Method == "GET" && req.Path == "/zxing.min.js")
                return Resource("zxing.min.js", "application/javascript");
            if (req.Path == "/favicon.ico")
                return new HttpResponse { Status = 404, ContentType = "text/plain", Body = new byte[0] };

            if (!req.Path.StartsWith("/api/"))
                return HttpResponse.Text(404, "text/plain", "Not found");

            if (!TokenMatches(req.QueryValue("t")))
                return JsonResponse(401, new { ok = false, message = L.T("This phone is not paired. Scan the QR code shown on the PC again."), lang = L.Bangla ? "bn" : "en" });

            LastSeen = DateTime.Now;
            string agent;
            LastDevice = req.Headers.TryGetValue("user-agent", out agent) ? DescribeDevice(agent) : req.RemoteAddress;
            OnStateChanged();

            switch (req.Path)
            {
                case "/api/status":
                    return JsonResponse(200, StatusPayload(null));

                case "/api/mode":
                {
                    var body = ParseBody(req.Body);
                    bool isSale = !string.Equals(Convert.ToString(Get(body, "mode")), "stockin", StringComparison.OrdinalIgnoreCase);
                    OnUi<object>(() => { _host.SetPosMode(isSale); return null; });
                    return JsonResponse(200, StatusPayload(null));
                }

                case "/api/cart":
                {
                    var body = ParseBody(req.Body);
                    var action = (Convert.ToString(Get(body, "action")) ?? "").Trim().ToLowerInvariant();
                    long id;
                    long.TryParse(Convert.ToString(Get(body, "id")), out id);
                    var result = OnUi(() => _host.CartAction(action, id)) ?? ScanResult.Fail(L.T("The PC did not respond."));
                    return JsonResponse(200, StatusPayload(result));
                }

                case "/api/scan":
                {
                    var body = ParseBody(req.Body);
                    var code = (Convert.ToString(Get(body, "code")) ?? "").Trim();
                    if (code.Length == 0 || code.Length > 128)
                        return JsonResponse(400, new { ok = false, message = L.T("Empty or invalid barcode.") });

                    var result = OnUi(() =>
                    {
                        var target = _host.CurrentScanTarget;
                        return target != null
                            ? target.HandleScan(code)
                            : ScanResult.Fail(L.T("Open the Sales or Stock In screen on the PC first."));
                    }) ?? ScanResult.Fail(L.T("The PC did not respond."));
                    return JsonResponse(200, StatusPayload(result));
                }
            }
            return JsonResponse(404, new { ok = false, message = L.T("Unknown request.") });
        }

        private static object StatusPayload(ScanResult scan)
        {
            var status = OnUi(() => _host.GetPosStatus()) ?? new PosStatus();
            var payload = new Dictionary<string, object>
            {
                { "ok", scan == null || scan.Ok },
                { "posOpen", status.PosOpen },
                { "mode", status.IsSale ? "sale" : "stockin" },
                { "cartCount", status.CartCount },
                { "cartCountText", L.Num(status.CartCount) },
                { "cartTotal", Theme.Money(status.CartTotal) },
                { "lines", (status.Lines ?? new List<PosLine>()).Select(l => new Dictionary<string, object>
                    {
                        { "id", l.Id }, { "name", l.Name }, { "qty", L.Num(l.Qty) }, { "total", Theme.Money(l.Total) }
                    }).ToList() },
                { "business", string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName },
                { "lang", L.Bangla ? "bn" : "en" }
            };
            if (scan != null)
            {
                payload["message"] = scan.Message ?? "";
                payload["productName"] = scan.ProductName ?? "";
                payload["price"] = scan.Price.HasValue ? Theme.Money(scan.Price.Value) : "";
                payload["qty"] = scan.Quantity.HasValue ? (object)L.Num(scan.Quantity.Value) : null;
            }
            return payload;
        }

        private static T OnUi<T>(Func<T> func) where T : class
        {
            var ui = _ui;
            if (ui == null || ui.IsDisposed || !ui.IsHandleCreated) return null;
            try
            {
                return (T)ui.Invoke(func);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool TokenMatches(string supplied)
        {
            var expected = AppSettings.ScannerToken;
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied) || supplied.Length != expected.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                diff |= expected[i] ^ supplied[i];
            return diff == 0;
        }

        private static Dictionary<string, object> ParseBody(string body)
        {
            try
            {
                return string.IsNullOrWhiteSpace(body) ? new Dictionary<string, object>() : Json.Deserialize<Dictionary<string, object>>(body);
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static HttpResponse JsonResponse(int status, object payload)
        {
            return HttpResponse.Text(status, "application/json; charset=utf-8", Json.Serialize(payload));
        }

        private static HttpResponse Resource(string name, string contentType)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("InventoryManager.web." + name))
            {
                if (s == null) return HttpResponse.Text(404, "text/plain", "Missing resource " + name);
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return new HttpResponse { Status = 200, ContentType = contentType, Body = ms.ToArray() };
            }
        }

        private static string DescribeDevice(string userAgent)
        {
            // e.g. "Mozilla/5.0 (Linux; Android 13; SM-A536E) ..." -> "Android 13; SM-A536E"
            int start = userAgent.IndexOf("Android", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int end = userAgent.IndexOf(')', start);
                if (end > start) return userAgent.Substring(start, end - start).Replace(" Build/", " ").Trim();
            }
            if (userAgent.Contains("iPhone")) return "iPhone";
            return "Phone";
        }

        private static string NewToken()
        {
            var bytes = new byte[12];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void OnStateChanged()
        {
            var handler = StateChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
