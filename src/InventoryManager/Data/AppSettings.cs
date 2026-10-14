using System;
using System.Collections.Generic;
using System.Globalization;

namespace InventoryManager.Data
{
    /// <summary>
    /// User-editable settings stored as key/value rows in the Settings table, cached in memory.
    /// </summary>
    public static class AppSettings
    {
        public static string BusinessName { get; set; }
        public static string BusinessAddress { get; set; }
        public static string BusinessPhone { get; set; }
        public static string CurrencySymbol { get; set; }
        public static int DefaultReorderLevel { get; set; }
        public static string Language { get; set; }         // "bn" (default) or "en"

        // Phone scanner
        public static int ScannerPort { get; set; }
        public static bool ScannerAutoStart { get; set; }
        public static string ScannerToken { get; set; }     // pairing secret in the QR code URL

        // Google Sheet sync
        public static string SheetUrl { get; set; }          // Apps Script web app link (…/exec)
        public static string SheetKey { get; set; }          // secret shared with the script
        public static bool SheetSyncEnabled { get; set; }

        // Backup / security / labels
        public static string AutoBackupFolder { get; set; }  // optional extra copy of the daily backup
        public static string OwnerPinSalt { get; set; }
        public static string OwnerPinHash { get; set; }      // empty = PIN mode off
        public static string LabelLayout { get; set; }       // last used barcode label layout
        public static string Shortcuts { get; set; }         // changed keyboard shortcuts, "id=keys;..." (see UI/Shortcuts.cs)

        // Receipts
        public static string ReceiptPrinter { get; set; }   // empty = Windows default printer
        public static string ReceiptPaper { get; set; }     // "58mm", "80mm" or "A4"
        public static bool ReceiptAutoPrint { get; set; }

        /// <summary>Raised after settings are saved or reloaded so open screens can update.</summary>
        public static event EventHandler Changed;

        public static void Load()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Key, Value FROM Settings";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        values[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
                }
            }

            BusinessName = Get(values, "BusinessName", "");
            BusinessAddress = Get(values, "BusinessAddress", "");
            BusinessPhone = Get(values, "BusinessPhone", "");
            CurrencySymbol = Get(values, "CurrencySymbol", "Tk");
            Language = Get(values, "Language", "bn") == "en" ? "en" : "bn";

            int reorder;
            DefaultReorderLevel = int.TryParse(Get(values, "DefaultReorderLevel", "5"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out reorder) ? reorder : 5;

            int port;
            ScannerPort = int.TryParse(Get(values, "ScannerPort", "8443"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536 ? port : 8443;
            ScannerAutoStart = Get(values, "ScannerAutoStart", "0") == "1";
            ScannerToken = Get(values, "ScannerToken", "");
            SheetUrl = Get(values, "SheetUrl", "");
            SheetKey = Get(values, "SheetKey", "");
            SheetSyncEnabled = Get(values, "SheetSyncEnabled", "0") == "1";

            AutoBackupFolder = Get(values, "AutoBackupFolder", "");
            OwnerPinSalt = Get(values, "OwnerPinSalt", "");
            OwnerPinHash = Get(values, "OwnerPinHash", "");
            LabelLayout = Get(values, "LabelLayout", "");
            Shortcuts = Get(values, "Shortcuts", "");
            ReceiptPrinter = Get(values, "ReceiptPrinter", "");
            ReceiptPaper = Get(values, "ReceiptPaper", "80mm");
            ReceiptAutoPrint = Get(values, "ReceiptAutoPrint", "0") == "1";

            OnChanged();
        }

        public static void Save()
        {
            var values = new Dictionary<string, string>
            {
                { "BusinessName", BusinessName ?? "" },
                { "BusinessAddress", BusinessAddress ?? "" },
                { "BusinessPhone", BusinessPhone ?? "" },
                { "CurrencySymbol", CurrencySymbol ?? "" },
                { "Language", Language == "en" ? "en" : "bn" },
                { "DefaultReorderLevel", DefaultReorderLevel.ToString(CultureInfo.InvariantCulture) },
                { "ScannerPort", ScannerPort.ToString(CultureInfo.InvariantCulture) },
                { "ScannerAutoStart", ScannerAutoStart ? "1" : "0" },
                { "ScannerToken", ScannerToken ?? "" },
                { "SheetUrl", SheetUrl ?? "" },
                { "SheetKey", SheetKey ?? "" },
                { "SheetSyncEnabled", SheetSyncEnabled ? "1" : "0" },
                { "AutoBackupFolder", AutoBackupFolder ?? "" },
                { "OwnerPinSalt", OwnerPinSalt ?? "" },
                { "OwnerPinHash", OwnerPinHash ?? "" },
                { "LabelLayout", LabelLayout ?? "" },
                { "Shortcuts", Shortcuts ?? "" },
                { "ReceiptPrinter", ReceiptPrinter ?? "" },
                { "ReceiptPaper", string.IsNullOrEmpty(ReceiptPaper) ? "80mm" : ReceiptPaper },
                { "ReceiptAutoPrint", ReceiptAutoPrint ? "1" : "0" }
            };

            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var kv in values)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@k, @v)";
                        cmd.Parameters.AddWithValue("@k", kv.Key);
                        cmd.Parameters.AddWithValue("@v", kv.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }

            OnChanged();
        }

        private static string Get(Dictionary<string, string> values, string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        private static void OnChanged()
        {
            var handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
