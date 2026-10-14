using System;
using System.Data.SQLite;
using System.IO;

namespace InventoryManager.Data
{
    /// <summary>
    /// Owns the SQLite database file location and schema.
    /// The file lives in %AppData% because Program Files is read-only for normal users.
    /// </summary>
    public static class Database
    {
        public static string DataFolder { get; private set; }
        public static string FilePath { get; private set; }
        public static string BackupFolder { get { return Path.Combine(DataFolder, "Backups"); } }

        private static string _connectionString;

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS Products (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Code          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    Name          TEXT    NOT NULL,
    Category      TEXT,
    Price         REAL    NOT NULL DEFAULT 0,
    Quantity      INTEGER NOT NULL DEFAULT 0,
    ReorderLevel  INTEGER NOT NULL DEFAULT 5,
    Description   TEXT,
    CreatedAt     TEXT    NOT NULL,
    UpdatedAt     TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Products_Name ON Products(Name);
CREATE INDEX IF NOT EXISTS IX_Products_Category ON Products(Category);

-- Every sale and stock-in. Product code/name are copied so history survives product deletion.
CREATE TABLE IF NOT EXISTS StockTransactions (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId    INTEGER,
    ProductCode  TEXT    NOT NULL,
    ProductName  TEXT    NOT NULL,
    Type         TEXT    NOT NULL,          -- 'SALE' or 'STOCK_IN'
    Quantity     INTEGER NOT NULL,
    UnitPrice    REAL    NOT NULL DEFAULT 0,
    Total        REAL    NOT NULL DEFAULT 0,
    Note         TEXT,
    CreatedAt    TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StockTransactions_CreatedAt ON StockTransactions(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_StockTransactions_Product ON StockTransactions(ProductId);

CREATE TABLE IF NOT EXISTS Settings (
    Key    TEXT PRIMARY KEY,
    Value  TEXT
);";

        public static void Initialize()
        {
            // INVMGR_DATA points test runs at an isolated folder so they can never touch real data.
            var overrideFolder = Environment.GetEnvironmentVariable("INVMGR_DATA");
            DataFolder = !string.IsNullOrEmpty(overrideFolder)
                ? overrideFolder
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "InventoryManager");
            Directory.CreateDirectory(DataFolder);
            FilePath = Path.Combine(DataFolder, "inventory.db");

            bool isNew = !File.Exists(FilePath);

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = FilePath,
                Version = 3,
                ForeignKeys = true,
                JournalMode = SQLiteJournalModeEnum.Wal
            };
            _connectionString = builder.ToString();

            // CREATE IF NOT EXISTS also upgrades databases made by older versions of the app.
            using (var conn = Open())
            {
                Exec(conn, Schema);
                Migrate(conn);
            }

            if (isNew)
                SeedSampleData();

            AppSettings.Load();
        }

        public static SQLiteConnection Open()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        /// <summary>Upgrades databases created by earlier versions (adds columns that came later).</summary>
        private static void Migrate(SQLiteConnection conn)
        {
            AddColumnIfMissing(conn, "Products", "Barcode", "TEXT");
            AddColumnIfMissing(conn, "StockTransactions", "InvoiceNo", "TEXT");

            // v1.2: categories list, vendors, purchase rate and profit tracking
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS Categories (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       TEXT NOT NULL UNIQUE COLLATE NOCASE,
    CreatedAt  TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Vendors (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Name           TEXT NOT NULL,
    ContactPerson  TEXT,
    Phone          TEXT,
    Address        TEXT,
    Notes          TEXT,
    CreatedAt      TEXT NOT NULL
);");
            AddColumnIfMissing(conn, "Products", "PurchasePrice", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, "Products", "VendorId", "INTEGER");
            AddColumnIfMissing(conn, "StockTransactions", "UnitCost", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, "StockTransactions", "VendorId", "INTEGER");
            AddColumnIfMissing(conn, "StockTransactions", "VendorName", "TEXT");

            // v1.3: Google Sheet sync - transactions undone since the last sync
            Exec(conn, "CREATE TABLE IF NOT EXISTS SyncDeletes (TxId INTEGER PRIMARY KEY)");

            // Categories that products already use become part of the managed list.
            Exec(conn, @"
INSERT OR IGNORE INTO Categories (Name, CreatedAt)
SELECT DISTINCT TRIM(Category), datetime('now', 'localtime') FROM Products
WHERE Category IS NOT NULL AND TRIM(Category) <> '';");
            Exec(conn, @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_Products_Barcode ON Products(Barcode) WHERE Barcode IS NOT NULL AND Barcode <> '';
CREATE INDEX IF NOT EXISTS IX_StockTransactions_Invoice ON StockTransactions(InvoiceNo);");

            // v1.4: invoice headers (discount, payment, customer), dues, returns, expenses, warranty serials
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS Invoices (
    InvoiceNo  TEXT PRIMARY KEY,
    Type       TEXT NOT NULL,
    CustomerId INTEGER,
    Subtotal   REAL NOT NULL DEFAULT 0,
    Discount   REAL NOT NULL DEFAULT 0,
    Total      REAL NOT NULL DEFAULT 0,
    CostTotal  REAL NOT NULL DEFAULT 0,
    Paid       REAL NOT NULL DEFAULT 0,
    Method     TEXT NOT NULL DEFAULT 'CASH',
    Note       TEXT,
    CreatedAt  TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Invoices_CreatedAt ON Invoices(CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Invoices_Customer ON Invoices(CustomerId);
CREATE TABLE IF NOT EXISTS Customers (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       TEXT NOT NULL,
    Phone      TEXT,
    Address    TEXT,
    Notes      TEXT,
    CreatedAt  TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Payments (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    InvoiceNo  TEXT,
    Amount     REAL NOT NULL,
    Method     TEXT NOT NULL DEFAULT 'CASH',
    Note       TEXT,
    CreatedAt  TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Payments_Customer ON Payments(CustomerId);
CREATE TABLE IF NOT EXISTS Expenses (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Date       TEXT NOT NULL,
    Category   TEXT,
    Amount     REAL NOT NULL,
    Note       TEXT,
    CreatedAt  TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Expenses_Date ON Expenses(Date);
CREATE TABLE IF NOT EXISTS SoldSerials (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo      TEXT NOT NULL,
    ProductId      INTEGER,
    ProductName    TEXT NOT NULL,
    Serial         TEXT NOT NULL COLLATE NOCASE,
    WarrantyMonths INTEGER NOT NULL DEFAULT 0,
    SoldAt         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SoldSerials_Serial ON SoldSerials(Serial);");
            AddColumnIfMissing(conn, "Products", "WarrantyMonths", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(conn, "StockTransactions", "RefTxId", "INTEGER");

            // Backfill invoice headers for rows written before v1.4. Legacy rows without an
            // invoice number each get their own 'LEG-<id>' header so every line has a header.
            Exec(conn, @"
UPDATE StockTransactions SET InvoiceNo = 'LEG-' || Id WHERE InvoiceNo IS NULL OR InvoiceNo = '';
INSERT INTO Invoices (InvoiceNo, Type, Subtotal, Discount, Total, CostTotal, Paid, Method, Note, CreatedAt)
SELECT t.InvoiceNo, MAX(t.Type), SUM(t.Total), 0, SUM(t.Total), SUM(t.UnitCost * t.Quantity), SUM(t.Total),
       'CASH', NULLIF(MAX(IFNULL(t.Note, '')), ''), MIN(t.CreatedAt)
FROM StockTransactions t
WHERE t.InvoiceNo NOT IN (SELECT InvoiceNo FROM Invoices)
GROUP BY t.InvoiceNo;");
        }

        /// <summary>Reads one value from the Settings table (used for app state, not user preferences).</summary>
        public static string GetSetting(string key, string fallback)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
                cmd.Parameters.AddWithValue("@k", key);
                var v = cmd.ExecuteScalar() as string;
                return v ?? fallback;
            }
        }

        public static void SetSetting(string key, string value)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@k, @v)";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Keeps only the newest <paramref name="keep"/> automatic backups of one kind.</summary>
        public static void PruneAutoBackups(string reason, int keep)
        {
            try
            {
                if (!Directory.Exists(BackupFolder)) return;
                var files = Directory.GetFiles(BackupFolder, "auto_" + reason + "_*.db");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);   // timestamped names sort chronologically
                for (int i = 0; i < files.Length - keep; i++)
                    File.Delete(files[i]);
            }
            catch
            {
                // Cleaning up old backups must never break the app.
            }
        }

        private static void AddColumnIfMissing(SQLiteConnection conn, string table, string column, string type)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(" + table + ")";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }
            Exec(conn, "ALTER TABLE " + table + " ADD COLUMN " + column + " " + type);
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Copies the live database to <paramref name="targetFile"/> (safe while the app is running).</summary>
        public static void BackupTo(string targetFile)
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);

            using (var source = Open())
            using (var target = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = targetFile, Version = 3 }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target, "main", "main", -1, null, 0);
            }
        }

        /// <summary>Writes a timestamped safety copy to the Backups folder and returns its path.</summary>
        public static string AutoBackup(string reason)
        {
            Directory.CreateDirectory(BackupFolder);
            var file = Path.Combine(BackupFolder,
                string.Format("auto_{0}_{1:yyyyMMdd_HHmmss}.db", reason, DateTime.Now));
            BackupTo(file);
            return file;
        }

        /// <summary>Replaces the live database contents with the backup file.</summary>
        public static void RestoreFrom(string backupFile)
        {
            var sourceCs = new SQLiteConnectionStringBuilder { DataSource = backupFile, Version = 3, ReadOnly = true }.ToString();
            using (var source = new SQLiteConnection(sourceCs))
            {
                source.Open();
                using (var cmd = source.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'Products'";
                    if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                        throw new InvalidDataException(L.T("The selected file is not an Inventory Manager backup."));
                }

                using (var target = Open())
                {
                    source.BackupDatabase(target, "main", "main", -1, null, 0);
                    // Backups from older versions may lack newer tables/columns.
                    Exec(target, Schema);
                    Migrate(target);
                }
            }
            AppSettings.Load();
            Sync.SheetSync.ResetProgress();
            Sync.SheetSync.RequestFull();
        }

        /// <summary>Deletes all products and transactions. Settings are kept.</summary>
        public static void ResetData(bool loadSamples)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
DELETE FROM StockTransactions;
DELETE FROM Invoices;
DELETE FROM Products;
DELETE FROM Categories;
DELETE FROM Vendors;
DELETE FROM Customers;
DELETE FROM Payments;
DELETE FROM Expenses;
DELETE FROM SoldSerials;
DELETE FROM SyncDeletes;
DELETE FROM sqlite_sequence WHERE name IN ('Products', 'StockTransactions', 'Categories', 'Vendors', 'Customers', 'Payments', 'Expenses', 'SoldSerials');";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }

            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
            }

            if (loadSamples)
                SeedSampleData();
            Sync.SheetSync.ResetProgress();
            Sync.SheetSync.RequestFull();
        }

        private static void SeedSampleData()
        {
            var repo = new ProductRepository();
            var vendors = new VendorRepository();
            long techId = vendors.Insert(new Models.Vendor { Name = "Tech Distribution Ltd", ContactPerson = "Karim Ahmed", Phone = "01711-000001", Address = "Elephant Road, Dhaka" });
            long officeId = vendors.Insert(new Models.Vendor { Name = "Office Furniture House", ContactPerson = "Nusrat Jahan", Phone = "01811-000002", Address = "Mirpur 10, Dhaka" });

            //                          code      name                    category       price   cost    qty reorder description                       barcode          vendor
            object[][] samples =
            {
                new object[] { "P-1001", "Wireless Mouse",        "Accessories", 850m,   620m,   42, 10, "2.4 GHz optical wireless mouse", "8901234500017", techId },
                new object[] { "P-1002", "Mechanical Keyboard",   "Accessories", 3500m,  2600m,  15, 5,  "RGB backlit, blue switches",     "8901234500024", techId },
                new object[] { "P-1003", "24\" LED Monitor",      "Displays",    14500m, 12000m, 8,  3,  "Full HD IPS panel",              "8901234500031", techId },
                new object[] { "P-1004", "USB-C Hub 7-in-1",      "Accessories", 2200m,  1500m,  4,  5,  "HDMI, USB 3.0, SD card",         "8901234500048", techId },
                new object[] { "P-1005", "Laptop Stand",          "Furniture",   1800m,  1200m,  20, 5,  "Adjustable aluminium stand",     "8901234500055", officeId },
                new object[] { "P-1006", "1TB External SSD",      "Storage",     9800m,  8200m,  2,  4,  "USB 3.2 Gen 2 portable SSD",     "8901234500062", techId },
                new object[] { "P-1007", "Office Chair",          "Furniture",   12500m, 9500m,  6,  2,  "Ergonomic mesh chair",           "8901234500079", officeId },
                new object[] { "P-1008", "HD Webcam",             "Accessories", 2700m,  2000m,  11, 5,  "1080p with microphone",          "8901234500086", techId }
            };

            foreach (var s in samples)
            {
                repo.Insert(new Models.Product
                {
                    Code = (string)s[0],
                    Name = (string)s[1],
                    Category = (string)s[2],
                    Price = (decimal)s[3],
                    PurchasePrice = (decimal)s[4],
                    Quantity = (int)s[5],
                    ReorderLevel = (int)s[6],
                    Description = (string)s[7],
                    Barcode = (string)s[8],
                    VendorId = (long)s[9]
                });
            }
        }
    }
}
