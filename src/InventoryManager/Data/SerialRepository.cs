using System;
using System.Collections.Generic;
using System.Data.SQLite;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    /// <summary>Serial numbers of sold units, for warranty lookups.</summary>
    public class SerialRepository
    {
        private const string SelectColumns = "Id, InvoiceNo, ProductId, ProductName, Serial, WarrantyMonths, SoldAt";

        public void Save(IList<SoldSerial> serials)
        {
            if (serials == null || serials.Count == 0) return;
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var s in serials)
                {
                    if (string.IsNullOrWhiteSpace(s.Serial)) continue;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO SoldSerials (InvoiceNo, ProductId, ProductName, Serial, WarrantyMonths, SoldAt)
VALUES (@inv, @pid, @name, @serial, @months, @at)";
                        cmd.Parameters.AddWithValue("@inv", s.InvoiceNo);
                        cmd.Parameters.AddWithValue("@pid", s.ProductId.HasValue ? (object)s.ProductId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@name", s.ProductName ?? "");
                        cmd.Parameters.AddWithValue("@serial", s.Serial.Trim());
                        cmd.Parameters.AddWithValue("@months", s.WarrantyMonths);
                        cmd.Parameters.AddWithValue("@at", ProductRepository.FormatDate(s.SoldAt));
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
            Sync.SheetSync.Request();
        }

        public List<SoldSerial> GetByInvoice(string invoiceNo)
        {
            return Query("WHERE InvoiceNo = @v ORDER BY Id", invoiceNo);
        }

        /// <summary>All units whose serial contains the scanned/typed text, newest sale first.</summary>
        public List<SoldSerial> Search(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return new List<SoldSerial>();
            return Query("WHERE Serial LIKE @v ORDER BY SoldAt DESC, Id DESC LIMIT 50", "%" + serial.Trim() + "%");
        }

        private static List<SoldSerial> Query(string where, object value)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + SelectColumns + " FROM SoldSerials " + where;
                cmd.Parameters.AddWithValue("@v", value);
                var list = new List<SoldSerial>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new SoldSerial
                        {
                            Id = r.GetInt64(0),
                            InvoiceNo = r.GetString(1),
                            ProductId = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                            ProductName = r.GetString(3),
                            Serial = r.GetString(4),
                            WarrantyMonths = Convert.ToInt32(r.GetValue(5)),
                            SoldAt = ProductRepository.ParseDate(r.GetValue(6))
                        });
                }
                return list;
            }
        }
    }
}
