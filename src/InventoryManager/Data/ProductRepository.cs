using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    public class DuplicateCodeException : Exception
    {
        public DuplicateCodeException(string code)
            : base(L.F("A product with code \"{0}\" already exists.", code)) { }
    }

    public class DuplicateBarcodeException : Exception
    {
        public DuplicateBarcodeException(string barcode)
            : base(L.F("Barcode \"{0}\" is already assigned to another product.", barcode)) { }
    }

    public class ProductRepository
    {
        // Columns are qualified because Vendors also has a Name column.
        private const string SelectFrom =
            "SELECT p.Id, p.Code, p.Name, p.Category, p.Price, p.Quantity, p.ReorderLevel, p.Description, " +
            "p.CreatedAt, p.UpdatedAt, p.Barcode, p.PurchasePrice, p.VendorId, v.Name, p.WarrantyMonths " +
            "FROM Products p LEFT JOIN Vendors v ON v.Id = p.VendorId";

        public List<Product> GetAll(string search = null, string category = null, bool lowStockOnly = false)
        {
            var sql = new StringBuilder(SelectFrom + " WHERE 1=1");
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql.Append(" AND (p.Code LIKE @q OR p.Name LIKE @q OR p.Category LIKE @q OR p.Description LIKE @q OR p.Barcode LIKE @q OR v.Name LIKE @q)");
                    cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
                }
                if (!string.IsNullOrWhiteSpace(category))
                {
                    sql.Append(" AND p.Category = @cat COLLATE NOCASE");
                    cmd.Parameters.AddWithValue("@cat", category);
                }
                if (lowStockOnly)
                    sql.Append(" AND p.Quantity <= p.ReorderLevel");

                sql.Append(" ORDER BY p.Name COLLATE NOCASE");
                cmd.CommandText = sql.ToString();
                return ReadAll(cmd);
            }
        }

        public List<Product> GetRecent(int count)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SelectFrom + " ORDER BY p.UpdatedAt DESC, p.Id DESC LIMIT @n";
                cmd.Parameters.AddWithValue("@n", count);
                return ReadAll(cmd);
            }
        }

        public Product GetById(long id)
        {
            return QuerySingle(SelectFrom + " WHERE p.Id = @v", id);
        }

        public Product GetByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return null;
            return QuerySingle(SelectFrom + " WHERE p.Barcode = @v", barcode.Trim());
        }

        /// <summary>Resolves a scanned/typed code: barcode first, then product code (case-insensitive).</summary>
        public Product FindByScan(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return GetByBarcode(code) ?? QuerySingle(SelectFrom + " WHERE p.Code = @v COLLATE NOCASE", code.Trim());
        }

        public bool BarcodeExists(string barcode, long excludeId = 0)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return false;
            return Count("SELECT COUNT(1) FROM Products WHERE Barcode = @v AND Id <> @id", barcode.Trim(), excludeId) > 0;
        }

        public bool CodeExists(string code, long excludeId = 0)
        {
            return Count("SELECT COUNT(1) FROM Products WHERE Code = @v COLLATE NOCASE AND Id <> @id", code.Trim(), excludeId) > 0;
        }

        public long Insert(Product p)
        {
            if (CodeExists(p.Code))
                throw new DuplicateCodeException(p.Code);
            if (BarcodeExists(p.Barcode))
                throw new DuplicateBarcodeException(p.Barcode);

            var now = DateTime.Now;
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Products (Code, Barcode, Name, Category, Price, PurchasePrice, VendorId, Quantity, ReorderLevel, WarrantyMonths, Description, CreatedAt, UpdatedAt)
VALUES (@code, @barcode, @name, @cat, @price, @cost, @vendor, @qty, @reorder, @warranty, @desc, @now, @now);
SELECT last_insert_rowid();";
                AddParameters(cmd, p);
                cmd.Parameters.AddWithValue("@now", FormatDate(now));
                p.Id = Convert.ToInt64(cmd.ExecuteScalar());
                p.CreatedAt = p.UpdatedAt = now;
            }
            CategoryRepository.EnsureExists(p.Category);
            Sync.SheetSync.Request();
            return p.Id;
        }

        public void Update(Product p)
        {
            if (CodeExists(p.Code, p.Id))
                throw new DuplicateCodeException(p.Code);
            if (BarcodeExists(p.Barcode, p.Id))
                throw new DuplicateBarcodeException(p.Barcode);

            var now = DateTime.Now;
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE Products SET
    Code = @code, Barcode = @barcode, Name = @name, Category = @cat, Price = @price, PurchasePrice = @cost,
    VendorId = @vendor, Quantity = @qty, ReorderLevel = @reorder, WarrantyMonths = @warranty, Description = @desc, UpdatedAt = @now
WHERE Id = @id";
                AddParameters(cmd, p);
                cmd.Parameters.AddWithValue("@now", FormatDate(now));
                cmd.Parameters.AddWithValue("@id", p.Id);
                cmd.ExecuteNonQuery();
                p.UpdatedAt = now;
            }
            CategoryRepository.EnsureExists(p.Category);
            Sync.SheetSync.Request();
        }

        public void Delete(long id)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Products WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Category names from the Categories list (managed in the Categories dialog).</summary>
        public List<string> GetCategories()
        {
            return new CategoryRepository().GetNames();
        }

        public InventoryStats GetStats()
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(1),
       IFNULL(SUM(Quantity), 0),
       IFNULL(SUM(Price * Quantity), 0),
       IFNULL(SUM(CASE WHEN Quantity <= ReorderLevel THEN 1 ELSE 0 END), 0),
       COUNT(DISTINCT NULLIF(Category, '')),
       IFNULL(SUM(PurchasePrice * Quantity), 0)
FROM Products";
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    return new InventoryStats
                    {
                        TotalProducts = Convert.ToInt32(r.GetValue(0)),
                        TotalUnits = Convert.ToInt64(r.GetValue(1)),
                        InventoryValue = Convert.ToDecimal(r.GetValue(2)),
                        LowStockCount = Convert.ToInt32(r.GetValue(3)),
                        CategoryCount = Convert.ToInt32(r.GetValue(4)),
                        InventoryCost = Convert.ToDecimal(r.GetValue(5))
                    };
                }
            }
        }

        // ---------------- helpers ----------------

        private static Product QuerySingle(string sql, object value)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@v", value);
                var list = ReadAll(cmd);
                return list.Count > 0 ? list[0] : null;
            }
        }

        private static long Count(string sql, object value, long excludeId)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@v", value);
                cmd.Parameters.AddWithValue("@id", excludeId);
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private static List<Product> ReadAll(SQLiteCommand cmd)
        {
            var list = new List<Product>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(Map(r));
            }
            return list;
        }

        private static void AddParameters(SQLiteCommand cmd, Product p)
        {
            cmd.Parameters.AddWithValue("@code", p.Code.Trim());
            cmd.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(p.Barcode) ? (object)DBNull.Value : p.Barcode.Trim());
            cmd.Parameters.AddWithValue("@name", p.Name.Trim());
            cmd.Parameters.AddWithValue("@cat", string.IsNullOrWhiteSpace(p.Category) ? (object)DBNull.Value : p.Category.Trim());
            cmd.Parameters.AddWithValue("@price", (double)p.Price);
            cmd.Parameters.AddWithValue("@cost", (double)p.PurchasePrice);
            cmd.Parameters.AddWithValue("@vendor", p.VendorId.HasValue ? (object)p.VendorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@qty", p.Quantity);
            cmd.Parameters.AddWithValue("@reorder", p.ReorderLevel);
            cmd.Parameters.AddWithValue("@warranty", p.WarrantyMonths);
            cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(p.Description) ? (object)DBNull.Value : p.Description.Trim());
        }

        private static Product Map(SQLiteDataReader r)
        {
            return new Product
            {
                Id = r.GetInt64(0),
                Code = r.GetString(1),
                Name = r.GetString(2),
                Category = r.IsDBNull(3) ? "" : r.GetString(3),
                Price = Convert.ToDecimal(r.GetValue(4)),
                Quantity = Convert.ToInt32(r.GetValue(5)),
                ReorderLevel = Convert.ToInt32(r.GetValue(6)),
                Description = r.IsDBNull(7) ? "" : r.GetString(7),
                CreatedAt = ParseDate(r.GetValue(8)),
                UpdatedAt = ParseDate(r.GetValue(9)),
                Barcode = r.IsDBNull(10) ? "" : r.GetString(10),
                PurchasePrice = r.IsDBNull(11) ? 0m : Convert.ToDecimal(r.GetValue(11)),
                VendorId = r.IsDBNull(12) ? (long?)null : r.GetInt64(12),
                VendorName = r.IsDBNull(13) ? "" : r.GetString(13),
                WarrantyMonths = r.IsDBNull(14) ? 0 : Convert.ToInt32(r.GetValue(14))
            };
        }

        internal static string FormatDate(DateTime d)
        {
            return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        internal static DateTime ParseDate(object value)
        {
            if (value is DateTime)
                return (DateTime)value;
            DateTime d;
            return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }
    }
}
