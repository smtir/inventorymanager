using System;
using System.Collections.Generic;
using System.Data.SQLite;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    /// <summary>Suppliers that goods are purchased from.</summary>
    public class VendorRepository
    {
        private const string SelectFrom = @"
SELECT v.Id, v.Name, v.ContactPerson, v.Phone, v.Address, v.Notes, v.CreatedAt,
       (SELECT COUNT(1) FROM Products p WHERE p.VendorId = v.Id),
       (SELECT IFNULL(SUM(t.Total), 0) FROM StockTransactions t WHERE t.VendorId = v.Id AND t.Type = 'STOCK_IN')
FROM Vendors v";

        public List<Vendor> GetAll(string search = null)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                var sql = SelectFrom;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql += " WHERE v.Name LIKE @q OR v.ContactPerson LIKE @q OR v.Phone LIKE @q OR v.Address LIKE @q";
                    cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
                }
                cmd.CommandText = sql + " ORDER BY v.Name COLLATE NOCASE";
                var list = new List<Vendor>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(Map(r));
                }
                return list;
            }
        }

        public Vendor GetById(long id)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SelectFrom + " WHERE v.Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                {
                    return r.Read() ? Map(r) : null;
                }
            }
        }

        public long Insert(Vendor v)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Vendors (Name, ContactPerson, Phone, Address, Notes, CreatedAt)
VALUES (@name, @contact, @phone, @address, @notes, @now);
SELECT last_insert_rowid();";
                AddParameters(cmd, v);
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                v.Id = Convert.ToInt64(cmd.ExecuteScalar());
                Sync.SheetSync.Request();
                return v.Id;
            }
        }

        public void Update(Vendor v)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE Vendors SET Name = @name, ContactPerson = @contact, Phone = @phone, Address = @address, Notes = @notes
WHERE Id = @id;
UPDATE StockTransactions SET VendorName = @name WHERE VendorId = @id;";
                AddParameters(cmd, v);
                cmd.Parameters.AddWithValue("@id", v.Id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Deletes a vendor. Products lose their default vendor; purchase history keeps the vendor name.</summary>
        public void Delete(long id)
        {
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE Products SET VendorId = NULL WHERE VendorId = @id;
UPDATE StockTransactions SET VendorId = NULL WHERE VendorId = @id;
DELETE FROM Vendors WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            Sync.SheetSync.Request();
        }

        private static void AddParameters(SQLiteCommand cmd, Vendor v)
        {
            cmd.Parameters.AddWithValue("@name", (v.Name ?? "").Trim());
            cmd.Parameters.AddWithValue("@contact", Clean(v.ContactPerson));
            cmd.Parameters.AddWithValue("@phone", Clean(v.Phone));
            cmd.Parameters.AddWithValue("@address", Clean(v.Address));
            cmd.Parameters.AddWithValue("@notes", Clean(v.Notes));
        }

        private static object Clean(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? (object)DBNull.Value : s.Trim();
        }

        private static Vendor Map(SQLiteDataReader r)
        {
            return new Vendor
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                ContactPerson = r.IsDBNull(2) ? "" : r.GetString(2),
                Phone = r.IsDBNull(3) ? "" : r.GetString(3),
                Address = r.IsDBNull(4) ? "" : r.GetString(4),
                Notes = r.IsDBNull(5) ? "" : r.GetString(5),
                CreatedAt = ProductRepository.ParseDate(r.GetValue(6)),
                ProductCount = Convert.ToInt32(r.GetValue(7)),
                TotalPurchased = Convert.ToDecimal(r.GetValue(8))
            };
        }
    }
}
