using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.Sync
{
    /// <summary>
    /// One-way sync of all data to a Google Sheet through a small Apps Script web app (see Resources\sync\Code.gs).
    ///  - Products, vendors, categories and the daily summary are replaced on every sync (small tables).
    ///  - Sales and stock-ins are appended incrementally (by transaction ID); undone ones are deleted.
    /// Runs on a background thread a few seconds after any change, and retries with back-off when offline.
    /// </summary>
    public static class SheetSync
    {
        private const int SchemaVersion = 2;
        private const int ChunkSize = 1500;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly AutoResetEvent Signal = new AutoResetEvent(false);
        private static readonly object Gate = new object();
        private static Thread _worker;
        private static volatile bool _fullRequested;

        /// <summary>Short status for the UI, e.g. "Synced 2:45 PM" or the last error.</summary>
        public static string StatusText { get; private set; }
        public static bool LastFailed { get; private set; }
        public static DateTime? LastSuccess { get; private set; }
        public static bool Busy { get; private set; }

        public static bool Configured
        {
            get { return AppSettings.SheetSyncEnabled && IsValidUrl(AppSettings.SheetUrl); }
        }

        public static bool IsValidUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Trim().StartsWith("https://script.google.com/", StringComparison.OrdinalIgnoreCase) &&
                   url.Trim().TrimEnd('/').EndsWith("/exec", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Secret shared with the Apps Script; created on first use.</summary>
        public static string Key
        {
            get
            {
                if (string.IsNullOrEmpty(AppSettings.SheetKey))
                {
                    var bytes = new byte[18];
                    using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                    AppSettings.SheetKey = Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
                    AppSettings.Save();
                }
                return AppSettings.SheetKey;
            }
        }

        /// <summary>The Apps Script source with this installation's secret key filled in.</summary>
        public static string ScriptSource()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("InventoryManager.sync.Code.gs"))
            using (var r = new StreamReader(s))
                return r.ReadToEnd().Replace("__SECRET__", Key);
        }

        /// <summary>Starts the background worker (call once at startup).</summary>
        public static void Start()
        {
            var key = Key;   // create the secret on the UI thread (saving settings notifies the UI)
            lock (Gate)
            {
                if (_worker != null) return;
                var state = LoadState();
                DateTime last;
                if (DateTime.TryParse(state.LastSyncAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out last)) LastSuccess = last;
                StatusText = Configured ? (LastSuccess.HasValue ? "" : L.T("Not synced yet")) : L.T("Off");
                _worker = new Thread(Loop) { IsBackground = true, Name = "SheetSync" };
                _worker.Start();
            }
            Request();
        }

        /// <summary>Something changed: sync soon (changes within a few seconds are combined).</summary>
        public static void Request()
        {
            if (Configured) Signal.Set();
        }

        /// <summary>Re-send everything (e.g. after a restore, a reset, or when the Sheet was edited by hand).</summary>
        public static void RequestFull()
        {
            _fullRequested = true;
            Signal.Set();
        }

        /// <summary>Checks the link + key by sending an empty batch. Returns null on success, else an error message.</summary>
        public static string Test(string url)
        {
            try
            {
                var reply = HttpsClient.Post(url.Trim(), Json.Serialize(new Dictionary<string, object> { { "key", Key }, { "ops", new object[0] } }));
                return CheckReply(reply);
            }
            catch (Exception ex)
            {
                return Friendly(ex);
            }
        }

        /// <summary>Turns HTTP/network errors into a short, actionable message (no raw HTML pages).</summary>
        private static string Friendly(Exception ex)
        {
            var msg = ex.Message ?? "";
            if (msg.StartsWith("HTTP 404")) return L.T("The link was not found. Copy the Web app link again from Deploy → Manage deployments.");
            if (msg.StartsWith("HTTP 401") || msg.StartsWith("HTTP 403")) return L.T("Google refused access. In the deployment set \"Who has access\" to \"Anyone\".");
            if (msg.StartsWith("HTTP ")) return msg.Length > 40 ? msg.Substring(0, msg.IndexOf(':') > 0 ? msg.IndexOf(':') : 40) : msg;
            if (ex is System.Net.WebException || ex is System.Net.Sockets.SocketException || ex.InnerException is System.Net.Sockets.SocketException)
                return L.T("No internet connection (or Google could not be reached).");
            return msg;
        }

        // ---------------- worker ----------------

        private static void Loop()
        {
            int failures = 0;
            while (true)
            {
                // After a failure, wake up by ourselves to retry: 30 s, 1, 2, 5 minutes...
                int wait = failures == 0 ? Timeout.Infinite : Math.Min(300, 30 * (1 << Math.Min(failures - 1, 3))) * 1000;
                Signal.WaitOne(wait);
                Thread.Sleep(3000);          // combine bursts of changes (e.g. a checkout with many lines)
                Signal.Reset();
                if (!Configured && !_fullRequested) { failures = 0; StatusText = L.T("Off"); continue; }
                if (!Configured) continue;

                try
                {
                    Busy = true;
                    StatusText = L.T("Syncing...");
                    RunOnce();
                    failures = 0;
                    LastFailed = false;
                    LastSuccess = DateTime.Now;
                    StatusText = L.F("Synced {0}", L.Date(LastSuccess.Value, "hh:mm tt"));
                }
                catch (Exception ex)
                {
                    failures++;
                    LastFailed = true;
                    StatusText = L.T("Sync failed, will retry:") + " " + Friendly(ex);
                    SaveState("SheetLastError", ex.Message);
                }
                finally
                {
                    Busy = false;
                }
            }
        }

        private static void RunOnce()
        {
            var url = AppSettings.SheetUrl.Trim();
            var state = LoadState();
            string signature = SchemaVersion + "|" + (L.Bangla ? "bn" : "en") + "|" + url;
            bool full = _fullRequested || state.Signature != signature;
            _fullRequested = false;
            long lastId = full ? 0 : state.LastTxId;

            // 1) Small tables (always replaced) + deletions of undone transactions.
            var ops = new List<object>();
            ops.AddRange(SnapshotOps());
            var deleted = full ? new List<long>() : PendingDeletes();
            if (deleted.Count > 0)
            {
                ops.Add(new Dictionary<string, object> { { "type", "delete" }, { "sheet", SalesSheet }, { "ids", deleted } });
                ops.Add(new Dictionary<string, object> { { "type", "delete" }, { "sheet", StockInSheet }, { "ids", deleted } });
            }

            // 2) Transactions after the last synced ID, in chunks.
            var txs = TransactionsAfter(lastId);
            var sales = txs.Where(t => t.Type == TransactionType.Sale || t.Type == TransactionType.Return).ToList();
            var stockIns = txs.Where(t => t.Type == TransactionType.StockIn || t.Type == TransactionType.Adjust).ToList();

            ops.Add(TxOp(SalesSheet, SalesHeaders, SalesFormats, sales.Take(ChunkSize).Select(SalesRow), full ? "replace" : "append"));
            ops.Add(TxOp(StockInSheet, StockInHeaders, StockInFormats, stockIns.Take(ChunkSize).Select(StockInRow), full ? "replace" : "append"));
            Send(url, ops);

            for (int skip = ChunkSize; skip < Math.Max(sales.Count, stockIns.Count); skip += ChunkSize)
            {
                var more = new List<object>();
                if (skip < sales.Count) more.Add(TxOp(SalesSheet, SalesHeaders, SalesFormats, sales.Skip(skip).Take(ChunkSize).Select(SalesRow), "append"));
                if (skip < stockIns.Count) more.Add(TxOp(StockInSheet, StockInHeaders, StockInFormats, stockIns.Skip(skip).Take(ChunkSize).Select(StockInRow), "append"));
                Send(url, more);
            }

            // 3) Due payments and warranty serials, appended by their IDs.
            long lastPay = full ? 0 : state.LastPayId;
            var pays = PaymentsAfter(lastPay);
            ops = new List<object>();
            if (full || pays.Count > 0)
                ops.Add(TxOp(L.T("Payments"), PaymentHeaders, PaymentFormats, pays.Select(x => x), full ? "replace" : "append"));

            long lastSerial = full ? 0 : state.LastSerialId;
            var serials = SerialsAfter(lastSerial);
            if (full || serials.Count > 0)
                ops.Add(TxOp(L.T("Serials"), SerialHeaders, SerialFormats, serials.Select(x => x), full ? "replace" : "append"));
            if (ops.Count > 0)
                Send(url, ops);

            // 4) Remember progress.
            if (txs.Count > 0) lastId = txs.Max(t => t.Id);
            if (pays.Count > 0) lastPay = Math.Max(lastPay, pays.Max(x => Convert.ToInt64(x[0])));
            if (serials.Count > 0) lastSerial = Math.Max(lastSerial, serials.Max(x => Convert.ToInt64(x[0])));
            SaveState("SheetLastPayId", lastPay.ToString(CultureInfo.InvariantCulture));
            SaveState("SheetLastSerialId", lastSerial.ToString(CultureInfo.InvariantCulture));
            SaveState("SheetLastTxId", lastId.ToString(CultureInfo.InvariantCulture));
            SaveState("SheetSignature", signature);
            SaveState("SheetLastSyncAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            SaveState("SheetLastError", "");
            ClearDeletes(deleted);
        }

        private static void Send(string url, List<object> ops)
        {
            var payload = Json.Serialize(new Dictionary<string, object> { { "key", Key }, { "ops", ops } });
            var error = CheckReply(HttpsClient.Post(url, payload));
            if (error != null) throw new IOException(error);
        }

        private static string CheckReply(string reply)
        {
            try
            {
                var obj = Json.Deserialize<Dictionary<string, object>>(reply);
                object ok;
                if (obj != null && obj.TryGetValue("ok", out ok) && ok is bool && (bool)ok) return null;
                object err;
                var msg = obj != null && obj.TryGetValue("error", out err) ? Convert.ToString(err) : reply;
                if (msg == "Wrong key") return L.T("The Sheet rejected the key. Copy the script again from the app and redeploy it.");
                return msg;
            }
            catch
            {
                // An HTML page usually means the link is wrong or the web app is not shared with "Anyone".
                return L.T("Unexpected answer from Google. Check the link and that the web app access is set to \"Anyone\".");
            }
        }

        // ---------------- sheets ----------------

        private static string SalesSheet { get { return L.T("Sales"); } }
        private static string StockInSheet { get { return L.T("Stock In"); } }

        private static string[] SalesHeaders
        {
            get { return T("ID", "Date & Time", "Invoice", "Type", "Code", "Product", "Qty", "Unit Price", "Total", "Unit Cost", "Profit", "Note"); }
        }
        private static readonly string[] SalesFormats = { "0", "yyyy-mm-dd hh:mm", "", "", "", "", "0", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "" };

        private static string[] PaymentHeaders
        {
            get { return T("ID", "Date & Time", "Customer", "Amount", "Method", "Note"); }
        }
        private static readonly string[] PaymentFormats = { "0", "yyyy-mm-dd hh:mm", "", "#,##0.00", "", "" };

        private static string[] SerialHeaders
        {
            get { return T("ID", "Date", "Invoice", "Product", "Serial", "Warranty months", "Warranty until"); }
        }
        private static readonly string[] SerialFormats = { "0", "yyyy-mm-dd", "", "", "@", "0", "yyyy-mm-dd" };

        private static string[] StockInHeaders
        {
            get { return T("ID", "Date & Time", "Invoice", "Type", "Vendor", "Code", "Product", "Qty", "Unit Cost", "Total", "Note"); }
        }
        private static readonly string[] StockInFormats = { "0", "yyyy-mm-dd hh:mm", "", "", "", "", "", "0", "#,##0.00", "#,##0.00", "" };

        private static object[] SalesRow(StockTransaction t)
        {
            return new object[] { t.Id, Stamp(t.CreatedAt), t.InvoiceNo ?? "", L.T(t.TypeDisplay), t.ProductCode, t.ProductName, t.Quantity, t.UnitPrice, t.Total, t.UnitCost, t.Profit, t.Note ?? "" };
        }

        private static object[] StockInRow(StockTransaction t)
        {
            return new object[] { t.Id, Stamp(t.CreatedAt), t.InvoiceNo ?? "", L.T(t.TypeDisplay), t.VendorName ?? "", t.ProductCode, t.ProductName, t.Quantity, t.UnitPrice, t.Total, t.Note ?? "" };
        }

        private static Dictionary<string, object> TxOp(string sheet, string[] headers, string[] formats, IEnumerable<object[]> rows, string type)
        {
            return new Dictionary<string, object>
            {
                { "type", type }, { "sheet", sheet }, { "headers", headers }, { "formats", formats }, { "rows", rows.ToList() }
            };
        }

        private static IEnumerable<object> SnapshotOps()
        {
            var products = new ProductRepository().GetAll();
            yield return Replace(L.T("Products"),
                T("Code", "Barcode", "Product Name", "Category", "Vendor", "Purchase Rate", "Sale Price", "Profit/Unit",
                  "Quantity in Stock", "Reorder Level", "Stock Value (at cost)", "Stock Value", "Low stock", "Last Updated"),
                new[] { "", "@", "", "", "", "#,##0.00", "#,##0.00", "#,##0.00", "0", "0", "#,##0.00", "#,##0.00", "", "yyyy-mm-dd hh:mm" },
                products.Select(p => new object[]
                {
                    p.Code, p.Barcode ?? "", p.Name, p.Category ?? "", p.VendorName ?? "", p.PurchasePrice, p.Price, p.ProfitPerUnit,
                    p.Quantity, p.ReorderLevel, p.PurchasePrice * p.Quantity, p.StockValue, p.IsLowStock ? L.T("Yes") : "", Stamp(p.UpdatedAt)
                }));

            yield return Replace(L.T("Vendors"),
                T("Vendor", "Contact Person", "Phone", "Address", "Products", "Total Purchased", "Notes"),
                new[] { "", "", "@", "", "0", "#,##0.00", "" },
                new VendorRepository().GetAll().Select(v => new object[] { v.Name, v.ContactPerson ?? "", v.Phone ?? "", v.Address ?? "", v.ProductCount, v.TotalPurchased, v.Notes ?? "" }));

            yield return Replace(L.T("Categories"), T("Category", "Products"), new[] { "", "0" },
                new CategoryRepository().GetAll().Select(c => new object[] { c.Name, c.ProductCount }));

            yield return Replace(L.T("Customers"),
                T("Name", "Phone", "Address", "Due", "Notes"),
                new[] { "", "@", "", "#,##0.00", "" },
                new CustomerRepository().GetAll().Select(c => new object[] { c.Name, c.Phone ?? "", c.Address ?? "", c.Due, c.Notes ?? "" }));

            yield return Replace(L.T("Expenses"),
                T("Date", "Category", "Amount", "Note"),
                new[] { "yyyy-mm-dd", "", "#,##0.00", "" },
                new ExpenseRepository().GetAll(null, null).Select(x => new object[] { x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Category ?? "", x.Amount, x.Note ?? "" }));

            yield return Replace(L.T("Daily summary"),
                T("Date", "Number of sales", "Units sold", "Discount", "Net sales", "Profit", "Purchase Total", "Expenses", "Net profit"),
                new[] { "yyyy-mm-dd", "0", "0", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00" },
                DailySummary());

            yield return Replace(L.T("Sync info"), T("Item", "Value"), new[] { "", "" }, new List<object[]>
            {
                new object[] { L.T("Business name"), AppSettings.BusinessName ?? "" },
                new object[] { L.T("Last synced"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                new object[] { L.T("Products"), products.Count },
                new object[] { L.T("Source"), L.T(AppInfo.ProductName) + " " + AppInfo.Version }
            });
        }

        private static Dictionary<string, object> Replace(string sheet, string[] headers, string[] formats, IEnumerable<object[]> rows)
        {
            return new Dictionary<string, object>
            {
                { "type", "replace" }, { "sheet", sheet }, { "headers", headers }, { "formats", formats }, { "rows", rows.ToList() }
            };
        }

        private static string[] T(params string[] english)
        {
            return english.Select(L.T).ToArray();
        }

        private static string Stamp(DateTime d)
        {
            return d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // ---------------- data ----------------

        private static List<StockTransaction> TransactionsAfter(long lastId)
        {
            // Oldest first so the Sheet reads in date order; IDs only grow.
            var list = new TransactionRepository().GetTransactions(null, null, null, 0);
            return list.Where(t => t.Id > lastId).OrderBy(t => t.Id).ToList();
        }

        private static IEnumerable<object[]> DailySummary()
        {
            // Invoice-level money (discounts, returns netted), plus units from the lines and expenses per day.
            var days = new SortedDictionary<string, decimal[]>(StringComparer.Ordinal);
            Func<string, decimal[]> row = d =>
            {
                decimal[] v;
                if (!days.TryGetValue(d, out v)) { v = new decimal[8]; days[d] = v; }
                return v;   // [0]=sales count, [1]=units, [2]=discount, [3]=net sales, [4]=profit, [5]=purchases, [6]=expenses
            };

            using (var conn = Database.Open())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT substr(CreatedAt, 1, 10) AS d,
       SUM(CASE WHEN Type = 'SALE' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Type = 'SALE' THEN Discount ELSE 0 END),
       SUM(CASE WHEN Type IN ('SALE', 'RETURN') THEN Total ELSE 0 END),
       SUM(CASE WHEN Type IN ('SALE', 'RETURN') THEN Total - CostTotal ELSE 0 END),
       SUM(CASE WHEN Type = 'STOCK_IN' THEN Total ELSE 0 END)
FROM Invoices GROUP BY d";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var v = row(r.GetString(0));
                            v[0] = Convert.ToDecimal(r.GetValue(1));
                            v[2] = Convert.ToDecimal(r.GetValue(2));
                            v[3] = Convert.ToDecimal(r.GetValue(3));
                            v[4] = Convert.ToDecimal(r.GetValue(4));
                            v[5] = Convert.ToDecimal(r.GetValue(5));
                        }
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT substr(CreatedAt, 1, 10) AS d, IFNULL(SUM(Quantity), 0)
FROM StockTransactions WHERE Type IN ('SALE', 'RETURN') GROUP BY d";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            row(r.GetString(0))[1] = Convert.ToDecimal(r.GetValue(1));
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Date, IFNULL(SUM(Amount), 0) FROM Expenses GROUP BY Date";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            row(r.GetString(0))[6] = Convert.ToDecimal(r.GetValue(1));
                    }
                }
            }

            var rows = new List<object[]>();
            foreach (var kv in days)
            {
                var v = kv.Value;
                rows.Add(new object[] { kv.Key, (long)v[0], (long)v[1], v[2], v[3], v[4], v[5], v[6], v[4] - v[6] });
            }
            return rows;
        }

        private static List<object[]> PaymentsAfter(long lastId)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT p.Id, p.CreatedAt, IFNULL(c.Name, ''), p.Amount, p.Method, IFNULL(p.Note, '')
FROM Payments p LEFT JOIN Customers c ON c.Id = p.CustomerId
WHERE p.Id > @last ORDER BY p.Id";
                cmd.Parameters.AddWithValue("@last", lastId);
                var rows = new List<object[]>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        rows.Add(new object[]
                        {
                            r.GetInt64(0), Stamp(ProductRepository.ParseDate(r.GetValue(1))), r.GetString(2),
                            Convert.ToDecimal(r.GetValue(3)), PayMethod.Display(r.GetString(4)), r.GetString(5)
                        });
                }
                return rows;
            }
        }

        private static List<object[]> SerialsAfter(long lastId)
        {
            var rows = new List<object[]>();
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, SoldAt, InvoiceNo, ProductName, Serial, WarrantyMonths FROM SoldSerials WHERE Id > @last ORDER BY Id";
                cmd.Parameters.AddWithValue("@last", lastId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var sold = ProductRepository.ParseDate(r.GetValue(1));
                        int months = Convert.ToInt32(r.GetValue(5));
                        rows.Add(new object[]
                        {
                            r.GetInt64(0), sold.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetString(2),
                            r.GetString(3), r.GetString(4), months,
                            months > 0 ? sold.Date.AddMonths(months).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }
                }
            }
            return rows;
        }

        /// <summary>Called when a transaction is undone (deleted), so the Sheet row is removed on the next sync.</summary>
        public static void RecordDelete(SQLiteConnection conn, SQLiteTransaction tx, long transactionId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO SyncDeletes (TxId) VALUES (@id)";
                cmd.Parameters.AddWithValue("@id", transactionId);
                cmd.ExecuteNonQuery();
            }
        }

        private static List<long> PendingDeletes()
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT TxId FROM SyncDeletes";
                var ids = new List<long>();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) ids.Add(r.GetInt64(0));
                return ids;
            }
        }

        private static void ClearDeletes(List<long> ids)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = ids.Count == 0 ? "DELETE FROM SyncDeletes WHERE 0" : "DELETE FROM SyncDeletes WHERE TxId IN (" + string.Join(",", ids) + ")";
                cmd.ExecuteNonQuery();
            }
        }

        private sealed class State
        {
            public long LastTxId;
            public long LastPayId;
            public long LastSerialId;
            public string Signature = "";
            public string LastSyncAt = "";
        }

        private static State LoadState()
        {
            var s = new State();
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Key, Value FROM Settings WHERE Key IN ('SheetLastTxId', 'SheetLastPayId', 'SheetLastSerialId', 'SheetSignature', 'SheetLastSyncAt')";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var v = r.IsDBNull(1) ? "" : r.GetString(1);
                        switch (r.GetString(0))
                        {
                            case "SheetLastTxId": long.TryParse(v, out s.LastTxId); break;
                            case "SheetLastPayId": long.TryParse(v, out s.LastPayId); break;
                            case "SheetLastSerialId": long.TryParse(v, out s.LastSerialId); break;
                            case "SheetSignature": s.Signature = v; break;
                            case "SheetLastSyncAt": s.LastSyncAt = v; break;
                        }
                    }
                }
            }
            return s;
        }

        private static void SaveState(string key, string value)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@k, @v)";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>After restore/reset the Sheet must be rebuilt from scratch.</summary>
        public static void ResetProgress()
        {
            SaveState("SheetSignature", "");
            SaveState("SheetLastTxId", "0");
            SaveState("SheetLastPayId", "0");
            SaveState("SheetLastSerialId", "0");
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM SyncDeletes";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
