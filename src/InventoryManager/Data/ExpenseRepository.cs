using System;
using System.Collections.Generic;
using System.Data.SQLite;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    /// <summary>Shop expenses (rent, bills, salary...) used for the net profit figures.</summary>
    public class ExpenseRepository
    {
        public List<Expense> GetAll(DateTime? from, DateTime? to, string search = null)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                var sql = "SELECT Id, Date, Category, Amount, Note, CreatedAt FROM Expenses WHERE 1=1";
                if (from.HasValue)
                {
                    sql += " AND Date >= @from";
                    cmd.Parameters.AddWithValue("@from", Day(from.Value));
                }
                if (to.HasValue)
                {
                    sql += " AND Date < @to";
                    cmd.Parameters.AddWithValue("@to", Day(to.Value));
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql += " AND (Category LIKE @q OR Note LIKE @q)";
                    cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
                }
                cmd.CommandText = sql + " ORDER BY Date DESC, Id DESC";

                var list = new List<Expense>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new Expense
                        {
                            Id = r.GetInt64(0),
                            Date = ProductRepository.ParseDate(r.GetValue(1)),
                            Category = r.IsDBNull(2) ? "" : r.GetString(2),
                            Amount = Convert.ToDecimal(r.GetValue(3)),
                            Note = r.IsDBNull(4) ? "" : r.GetString(4),
                            CreatedAt = ProductRepository.ParseDate(r.GetValue(5))
                        });
                }
                return list;
            }
        }

        public decimal Total(DateTime? from, DateTime? to)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                var sql = "SELECT IFNULL(SUM(Amount), 0) FROM Expenses WHERE 1=1";
                if (from.HasValue)
                {
                    sql += " AND Date >= @from";
                    cmd.Parameters.AddWithValue("@from", Day(from.Value));
                }
                if (to.HasValue)
                {
                    sql += " AND Date < @to";
                    cmd.Parameters.AddWithValue("@to", Day(to.Value));
                }
                cmd.CommandText = sql;
                return Convert.ToDecimal(cmd.ExecuteScalar());
            }
        }

        /// <summary>Totals per category in [from, to), largest first (for the report).</summary>
        public List<Tuple<string, decimal>> TotalsByCategory(DateTime? from, DateTime? to)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                var sql = "SELECT IFNULL(NULLIF(TRIM(Category), ''), '-'), IFNULL(SUM(Amount), 0) AS t FROM Expenses WHERE 1=1";
                if (from.HasValue)
                {
                    sql += " AND Date >= @from";
                    cmd.Parameters.AddWithValue("@from", Day(from.Value));
                }
                if (to.HasValue)
                {
                    sql += " AND Date < @to";
                    cmd.Parameters.AddWithValue("@to", Day(to.Value));
                }
                cmd.CommandText = sql + " GROUP BY 1 ORDER BY t DESC";
                var list = new List<Tuple<string, decimal>>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(Tuple.Create(r.GetString(0), Convert.ToDecimal(r.GetValue(1))));
                }
                return list;
            }
        }

        public List<string> Categories()
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT TRIM(Category) FROM Expenses WHERE Category IS NOT NULL AND TRIM(Category) <> '' ORDER BY 1 COLLATE NOCASE";
                var list = new List<string>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(r.GetString(0));
                }
                return list;
            }
        }

        public long Insert(Expense e)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Expenses (Date, Category, Amount, Note, CreatedAt)
VALUES (@date, @cat, @amount, @note, @now);
SELECT last_insert_rowid();";
                AddParameters(cmd, e);
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                e.Id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            Sync.SheetSync.Request();
            return e.Id;
        }

        public void Update(Expense e)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Expenses SET Date = @date, Category = @cat, Amount = @amount, Note = @note WHERE Id = @id";
                AddParameters(cmd, e);
                cmd.Parameters.AddWithValue("@id", e.Id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        public void Delete(long id)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Expenses WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            Sync.SheetSync.Request();
        }

        private static void AddParameters(SQLiteCommand cmd, Expense e)
        {
            cmd.Parameters.AddWithValue("@date", Day(e.Date));
            cmd.Parameters.AddWithValue("@cat", string.IsNullOrWhiteSpace(e.Category) ? (object)DBNull.Value : e.Category.Trim());
            cmd.Parameters.AddWithValue("@amount", (double)e.Amount);
            cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(e.Note) ? (object)DBNull.Value : e.Note.Trim());
        }

        private static string Day(DateTime d)
        {
            return d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
