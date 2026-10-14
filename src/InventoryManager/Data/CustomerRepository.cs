using System;
using System.Collections.Generic;
using System.Data.SQLite;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    /// <summary>
    /// Customers and their due ledger. A customer's due = what their invoices left unpaid
    /// (returns subtract) minus the payments collected later.
    /// </summary>
    public class CustomerRepository
    {
        private const string DueSql =
            "IFNULL((SELECT SUM(i.Total - i.Paid) FROM Invoices i WHERE i.CustomerId = c.Id), 0) - " +
            "IFNULL((SELECT SUM(p.Amount) FROM Payments p WHERE p.CustomerId = c.Id), 0)";

        public List<Customer> GetAll(string search = null)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                var sql = @"
SELECT c.Id, c.Name, c.Phone, c.Address, c.Notes, c.CreatedAt, " + DueSql + @" AS Due,
       (SELECT MAX(d) FROM (
            SELECT MAX(i.CreatedAt) AS d FROM Invoices i WHERE i.CustomerId = c.Id
            UNION ALL SELECT MAX(p.CreatedAt) FROM Payments p WHERE p.CustomerId = c.Id))
FROM Customers c";
                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql += " WHERE c.Name LIKE @q OR c.Phone LIKE @q OR c.Address LIKE @q";
                    cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
                }
                sql += " ORDER BY Due DESC, c.Name COLLATE NOCASE";
                cmd.CommandText = sql;

                var list = new List<Customer>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(Map(r));
                }
                return list;
            }
        }

        public Customer GetById(long id)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT c.Id, c.Name, c.Phone, c.Address, c.Notes, c.CreatedAt, " + DueSql + @", NULL
FROM Customers c WHERE c.Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                {
                    return r.Read() ? Map(r) : null;
                }
            }
        }

        public decimal GetBalance(long id)
        {
            var c = GetById(id);
            return c != null ? c.Due : 0m;
        }

        public long Insert(Customer c)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Customers (Name, Phone, Address, Notes, CreatedAt)
VALUES (@name, @phone, @address, @notes, @now);
SELECT last_insert_rowid();";
                AddParameters(cmd, c);
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                c.Id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            Sync.SheetSync.Request();
            return c.Id;
        }

        public void Update(Customer c)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Customers SET Name = @name, Phone = @phone, Address = @address, Notes = @notes WHERE Id = @id";
                AddParameters(cmd, c);
                cmd.Parameters.AddWithValue("@id", c.Id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Deleting is only allowed when nothing is owed either way; history keeps the invoices.</summary>
        public void Delete(long id)
        {
            if (Math.Abs(GetBalance(id)) >= 0.005m)
                throw new InvalidOperationException(L.T("This customer still has a due balance. Settle it before deleting."));
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Customers WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Records money collected against a customer's due. Returns the new balance.</summary>
        public decimal AddPayment(long customerId, decimal amount, string method, string note)
        {
            if (amount <= 0)
                throw new ArgumentException(L.T("Please enter a valid amount."));
            if (string.IsNullOrEmpty(method)) method = PayMethod.Cash;
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Payments (CustomerId, Amount, Method, Note, CreatedAt)
VALUES (@cust, @amount, @method, @note, @now)";
                cmd.Parameters.AddWithValue("@cust", customerId);
                cmd.Parameters.AddWithValue("@amount", (double)amount);
                cmd.Parameters.AddWithValue("@method", method);
                cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? (object)DBNull.Value : note.Trim());
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
            return GetBalance(customerId);
        }

        /// <summary>The customer's full history (sales, returns, payments) with a running due balance.</summary>
        public List<LedgerEntry> GetLedger(long customerId)
        {
            var rows = new List<LedgerEntry>();
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT CreatedAt, Type, InvoiceNo, Total, Paid FROM Invoices WHERE CustomerId = @id
UNION ALL
SELECT CreatedAt, 'PAYMENT', IFNULL(Note, ''), 0, Amount FROM Payments WHERE CustomerId = @id
ORDER BY CreatedAt, InvoiceNo";
                cmd.Parameters.AddWithValue("@id", customerId);
                using (var r = cmd.ExecuteReader())
                {
                    decimal balance = 0;
                    while (r.Read())
                    {
                        var kind = r.GetString(1);
                        var total = Convert.ToDecimal(r.GetValue(3));
                        var paid = Convert.ToDecimal(r.GetValue(4));
                        balance += total - paid;
                        if (kind == "PAYMENT") { /* total is 0, paid is the collected amount */ }
                        rows.Add(new LedgerEntry
                        {
                            Date = ProductRepository.ParseDate(r.GetValue(0)),
                            Kind = kind,
                            Reference = r.IsDBNull(2) ? "" : r.GetString(2),
                            Charge = total,
                            Credit = paid,
                            Balance = balance
                        });
                    }
                }
            }
            return rows;
        }

        private static void AddParameters(SQLiteCommand cmd, Customer c)
        {
            cmd.Parameters.AddWithValue("@name", (c.Name ?? "").Trim());
            cmd.Parameters.AddWithValue("@phone", Clean(c.Phone));
            cmd.Parameters.AddWithValue("@address", Clean(c.Address));
            cmd.Parameters.AddWithValue("@notes", Clean(c.Notes));
        }

        private static object Clean(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? (object)DBNull.Value : s.Trim();
        }

        private static Customer Map(SQLiteDataReader r)
        {
            return new Customer
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Phone = r.IsDBNull(2) ? "" : r.GetString(2),
                Address = r.IsDBNull(3) ? "" : r.GetString(3),
                Notes = r.IsDBNull(4) ? "" : r.GetString(4),
                CreatedAt = ProductRepository.ParseDate(r.GetValue(5)),
                Due = Convert.ToDecimal(r.GetValue(6)),
                LastActivity = r.IsDBNull(7) ? (DateTime?)null : ProductRepository.ParseDate(r.GetValue(7))
            };
        }
    }
}
