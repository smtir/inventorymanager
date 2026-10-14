using System;
using System.Collections.Generic;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    public class DuplicateCategoryException : Exception
    {
        public DuplicateCategoryException(string name)
            : base(L.F("A category named \"{0}\" already exists.", name)) { }
    }

    /// <summary>
    /// The managed list of categories. Products store the category name, so rename/delete
    /// also update the products that use it.
    /// </summary>
    public class CategoryRepository
    {
        public List<Category> GetAll()
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT c.Id, c.Name, (SELECT COUNT(1) FROM Products p WHERE p.Category = c.Name COLLATE NOCASE)
FROM Categories c ORDER BY c.Name COLLATE NOCASE";
                var list = new List<Category>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new Category { Id = r.GetInt64(0), Name = r.GetString(1), ProductCount = Convert.ToInt32(r.GetValue(2)) });
                }
                return list;
            }
        }

        public List<string> GetNames()
        {
            var names = new List<string>();
            foreach (var c in GetAll()) names.Add(c.Name);
            return names;
        }

        public long Add(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException(L.T("Please enter a category name."));
            if (Exists(name, 0)) throw new DuplicateCategoryException(name);

            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Categories (Name, CreatedAt) VALUES (@n, @now); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                var id = Convert.ToInt64(cmd.ExecuteScalar());
                Sync.SheetSync.Request();
                return id;
            }
        }

        /// <summary>Renames a category and every product that uses it.</summary>
        public void Rename(long id, string newName)
        {
            newName = (newName ?? "").Trim();
            if (newName.Length == 0) throw new ArgumentException(L.T("Please enter a category name."));
            if (Exists(newName, id)) throw new DuplicateCategoryException(newName);

            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                string oldName;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT Name FROM Categories WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    oldName = cmd.ExecuteScalar() as string;
                }
                if (oldName == null) return;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
UPDATE Categories SET Name = @new WHERE Id = @id;
UPDATE Products SET Category = @new WHERE Category = @old COLLATE NOCASE;";
                    cmd.Parameters.AddWithValue("@new", newName);
                    cmd.Parameters.AddWithValue("@old", oldName);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Deletes a category; products in it become uncategorized.</summary>
        public void Delete(long id)
        {
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE Products SET Category = NULL WHERE Category = (SELECT Name FROM Categories WHERE Id = @id) COLLATE NOCASE;
DELETE FROM Categories WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            Sync.SheetSync.Request();
        }

        /// <summary>Adds the category if a product uses a name that is not in the list yet.</summary>
        public static void EnsureExists(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Categories (Name, CreatedAt) VALUES (@n, @now)";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@now", ProductRepository.FormatDate(DateTime.Now));
                cmd.ExecuteNonQuery();
            }
        }

        private static bool Exists(string name, long excludeId)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM Categories WHERE Name = @n COLLATE NOCASE AND Id <> @id";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@id", excludeId);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
