using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text;
using InventoryManager.Models;

namespace InventoryManager.Data
{
    public class InsufficientStockException : Exception
    {
        public InsufficientStockException(string productName, int available, int requested)
            : base(L.F("Not enough stock for \"{0}\". Available: {1}, requested: {2}.",
                productName, L.Num(available), L.Num(requested))) { }
    }

    /// <summary>What a return would refund, shown to the user before confirming.</summary>
    public class ReturnPreview
    {
        public decimal Refund { get; set; }       // total value returned (discount-adjusted)
        public decimal DueReduced { get; set; }   // part that reduces the customer's due
        public decimal CashBack { get; set; }     // part handed back
    }

    /// <summary>
    /// Records sales, stock-ins, returns and adjustments. Every checkout writes an Invoices header
    /// (customer, discount, paid, method) plus its StockTransactions lines in one DB transaction,
    /// and keeps product quantities in sync.
    /// </summary>
    public class TransactionRepository
    {
        private const string SelectColumns =
            "Id, ProductId, ProductCode, ProductName, Type, Quantity, UnitPrice, Total, Note, CreatedAt, InvoiceNo, UnitCost, VendorId, VendorName, RefTxId";

        public const string SaleInvoicePrefix = "INV-";
        public const string StockInInvoicePrefix = "SI-";
        public const string ReturnInvoicePrefix = "RET-";
        public const string AdjustInvoicePrefix = "ADJ-";

        public string Checkout(IList<CartLine> lines, string type, string note, long? vendorId)
        {
            return Checkout(lines, type, note, vendorId, 0m, null, null, PayMethod.Cash);
        }

        /// <summary>
        /// Records every cart line under one new invoice number, all-or-nothing.
        /// Sales: stock is re-checked per product and the product's current purchase rate is saved as the
        /// line's cost, so profit stays correct if prices change later. A discount applies to the whole
        /// invoice; paying less than the total needs a customer (the rest becomes their due).
        /// Stock-ins: the line price is the purchase cost; it becomes the product's purchase rate.
        /// </summary>
        public string Checkout(IList<CartLine> lines, string type, string note, long? vendorId,
            decimal discount, long? customerId, decimal? paid, string method)
        {
            if (lines == null || lines.Count == 0)
                throw new ArgumentException(L.T("The cart is empty."));
            foreach (var l in lines)
                if (l.Quantity <= 0)
                    throw new ArgumentOutOfRangeException("lines", "Quantity must be greater than zero for \"" + l.Name + "\".");

            bool isSale = type == TransactionType.Sale;
            string prefix = isSale ? SaleInvoicePrefix : StockInInvoicePrefix;
            var now = DateTime.Now;
            string cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (!isSale) { discount = 0; customerId = null; paid = null; method = PayMethod.Cash; }
            if (string.IsNullOrEmpty(method)) method = PayMethod.Cash;

            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                string vendorName = null;
                if (!isSale && vendorId.HasValue)
                {
                    vendorName = Scalar(conn, tx, "SELECT Name FROM Vendors WHERE Id = @v", vendorId.Value) as string;
                    if (vendorName == null) vendorId = null;
                }
                if (customerId.HasValue &&
                    Scalar(conn, tx, "SELECT Name FROM Customers WHERE Id = @v", customerId.Value) == null)
                    customerId = null;

                var needed = new Dictionary<long, int>();
                foreach (var l in lines)
                {
                    int q;
                    needed.TryGetValue(l.ProductId, out q);
                    needed[l.ProductId] = q + l.Quantity;
                }

                var info = new Dictionary<long, ProductSnapshot>();
                foreach (var id in needed.Keys)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT Code, Name, Quantity, PurchasePrice FROM Products WHERE Id = @id";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read())
                                throw new InvalidOperationException(L.T("A product in the cart no longer exists. Please remove it and try again."));
                            info[id] = new ProductSnapshot
                            {
                                Code = r.GetString(0),
                                Name = r.GetString(1),
                                Stock = Convert.ToInt32(r.GetValue(2)),
                                Cost = Convert.ToDecimal(r.GetValue(3))
                            };
                        }
                    }
                    if (isSale && needed[id] > info[id].Stock)
                        throw new InsufficientStockException(info[id].Name, info[id].Stock, needed[id]);
                }

                // Money for the invoice header.
                decimal subtotal = 0, costTotal = 0;
                foreach (var l in lines)
                {
                    subtotal += l.LineTotal;
                    costTotal += (isSale ? info[l.ProductId].Cost : l.UnitPrice) * l.Quantity;
                }
                if (discount < 0) discount = 0;
                if (discount > subtotal)
                    throw new ArgumentException(L.T("The discount cannot be more than the total."));
                decimal total = subtotal - discount;
                decimal paidValue = paid.HasValue ? Math.Max(0, Math.Min(paid.Value, total)) : total;
                if (isSale && paidValue < total && !customerId.HasValue)
                    throw new InvalidOperationException(L.T("Selling on due needs a customer. Pick a customer first."));

                string invoiceNo = NextInvoiceNo(conn, tx, prefix);
                InsertInvoice(conn, tx, invoiceNo, type, customerId, subtotal, discount, total, costTotal,
                    paidValue, method, cleanNote, now);

                foreach (var l in lines)
                {
                    var p = info[l.ProductId];
                    decimal unitCost = isSale ? p.Cost : l.UnitPrice;
                    AdjustStock(conn, tx, l.ProductId, isSale ? -l.Quantity : l.Quantity, now);
                    InsertLine(conn, tx, invoiceNo, l.ProductId, p.Code, p.Name, type, l.Quantity,
                        l.UnitPrice, unitCost, l.LineTotal, cleanNote, vendorId, vendorName, null, now);

                    if (!isSale)
                    {
                        // Latest purchase price becomes the product's purchase rate; first vendor becomes its default.
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
UPDATE Products SET
    PurchasePrice = CASE WHEN @cost > 0 THEN @cost ELSE PurchasePrice END,
    VendorId = IFNULL(VendorId, @vid)
WHERE Id = @id";
                            cmd.Parameters.AddWithValue("@cost", (double)l.UnitPrice);
                            cmd.Parameters.AddWithValue("@vid", vendorId.HasValue ? (object)vendorId.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@id", l.ProductId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                tx.Commit();
                Sync.SheetSync.Request();
                return invoiceNo;
            }
        }

        // ---------------- returns ----------------

        /// <summary>What refunding these items would give back, without changing anything.</summary>
        public ReturnPreview PreviewReturn(string invoiceNo, IList<Tuple<long, int>> items)
        {
            using (var conn = Database.Open())
            {
                var header = GetInvoiceHeader(invoiceNo);
                if (header == null) throw new InvalidOperationException(L.T("This invoice no longer exists."));
                decimal refund = ComputeRefund(conn, null, invoiceNo, header, items);
                decimal outstanding = header.CustomerId.HasValue ? CustomerBalance(conn, null, header.CustomerId.Value) : 0m;
                decimal dueReduced = Math.Min(refund, Math.Max(0, outstanding));
                return new ReturnPreview { Refund = refund, DueReduced = dueReduced, CashBack = refund - dueReduced };
            }
        }

        /// <summary>
        /// Returns items from a sale: stock goes back, a RETURN invoice with negative totals is written
        /// (so summaries net out), and if the sale still has unpaid due, the refund reduces that first.
        /// </summary>
        public string ReturnItems(string invoiceNo, IList<Tuple<long, int>> items, string method, string note)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException(L.T("Nothing selected to return."));
            if (string.IsNullOrEmpty(method)) method = PayMethod.Cash;
            var now = DateTime.Now;

            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                var header = GetInvoiceHeader(conn, tx, invoiceNo);
                if (header == null || header.Type != TransactionType.Sale)
                    throw new InvalidOperationException(L.T("This invoice no longer exists."));

                var lines = GetInvoiceLines(conn, tx, invoiceNo);
                var returned = GetReturnedQuantities(conn, tx, invoiceNo);
                decimal factor = header.Subtotal > 0 ? (header.Subtotal - header.Discount) / header.Subtotal : 1m;

                decimal refund = 0, preSum = 0, costSum = 0;
                var toInsert = new List<StockTransaction>();
                foreach (var item in items)
                {
                    StockTransaction orig = null;
                    foreach (var l in lines)
                        if (l.Id == item.Item1) { orig = l; break; }
                    if (orig == null || orig.Type != TransactionType.Sale)
                        throw new InvalidOperationException(L.T("This invoice no longer exists."));

                    int already;
                    returned.TryGetValue(orig.Id, out already);
                    int qty = item.Item2;
                    if (qty <= 0) continue;
                    if (qty > orig.Quantity - already)
                        throw new InvalidOperationException(L.F("Only {0} of \"{1}\" can still be returned.",
                            L.Num(orig.Quantity - already), orig.ProductName));

                    preSum += orig.UnitPrice * qty;
                    costSum += orig.UnitCost * qty;
                    refund += Math.Round(orig.UnitPrice * qty * factor, 2, MidpointRounding.AwayFromZero);
                    toInsert.Add(new StockTransaction
                    {
                        ProductId = orig.ProductId,
                        ProductCode = orig.ProductCode,
                        ProductName = orig.ProductName,
                        Quantity = -qty,
                        UnitPrice = orig.UnitPrice,
                        UnitCost = orig.UnitCost,
                        RefTxId = orig.Id
                    });
                }
                if (toInsert.Count == 0)
                    throw new ArgumentException(L.T("Nothing selected to return."));

                decimal outstanding = header.CustomerId.HasValue ? CustomerBalance(conn, tx, header.CustomerId.Value) : 0m;
                decimal dueReduced = Math.Min(refund, Math.Max(0, outstanding));
                decimal cashBack = refund - dueReduced;

                string retNo = NextInvoiceNo(conn, tx, ReturnInvoicePrefix);
                string retNote = string.IsNullOrWhiteSpace(note) ? L.F("Return of {0}", invoiceNo) : note.Trim();
                InsertInvoice(conn, tx, retNo, TransactionType.Return, header.CustomerId,
                    -preSum, -(preSum - refund), -refund, -costSum, -cashBack, method, retNote, now);

                foreach (var l in toInsert)
                {
                    AdjustStock(conn, tx, l.ProductId.Value, -l.Quantity, now);   // Quantity is negative
                    InsertLine(conn, tx, retNo, l.ProductId.Value, l.ProductCode, l.ProductName,
                        TransactionType.Return, l.Quantity, l.UnitPrice, l.UnitCost,
                        l.UnitPrice * l.Quantity, retNote, null, null, l.RefTxId, now);
                }

                tx.Commit();
                Sync.SheetSync.Request();
                return retNo;
            }
        }

        /// <summary>Units already returned per original sale line of this invoice.</summary>
        public Dictionary<long, int> GetReturnedQuantities(string invoiceNo)
        {
            using (var conn = Database.Open())
                return GetReturnedQuantities(conn, null, invoiceNo);
        }

        private static Dictionary<long, int> GetReturnedQuantities(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo)
        {
            var map = new Dictionary<long, int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT RefTxId, -SUM(Quantity) FROM StockTransactions
WHERE Type = 'RETURN' AND RefTxId IN (SELECT Id FROM StockTransactions WHERE InvoiceNo = @inv)
GROUP BY RefTxId";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        if (!r.IsDBNull(0))
                            map[r.GetInt64(0)] = Convert.ToInt32(r.GetValue(1));
                }
            }
            return map;
        }

        private decimal ComputeRefund(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo, Invoice header, IList<Tuple<long, int>> items)
        {
            var lines = GetInvoiceLines(conn, tx, invoiceNo);
            decimal factor = header.Subtotal > 0 ? (header.Subtotal - header.Discount) / header.Subtotal : 1m;
            decimal refund = 0;
            foreach (var item in items)
            {
                foreach (var l in lines)
                    if (l.Id == item.Item1 && item.Item2 > 0)
                        refund += Math.Round(l.UnitPrice * item.Item2 * factor, 2, MidpointRounding.AwayFromZero);
            }
            return refund;
        }

        /// <summary>What the customer currently owes overall; refunds clear this before any cash is handed back.</summary>
        private static decimal CustomerBalance(SQLiteConnection conn, SQLiteTransaction tx, long customerId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT IFNULL((SELECT SUM(Total - Paid) FROM Invoices WHERE CustomerId = @v), 0)
     - IFNULL((SELECT SUM(Amount) FROM Payments WHERE CustomerId = @v), 0)";
                cmd.Parameters.AddWithValue("@v", customerId);
                return Convert.ToDecimal(cmd.ExecuteScalar());
            }
        }

        // ---------------- stock adjustment ----------------

        /// <summary>Corrects stock for damage/loss/count mistakes; recorded in history with a reason.</summary>
        public string AdjustStockLevel(long productId, int delta, string reason)
        {
            if (delta == 0)
                throw new ArgumentException(L.T("The adjustment is zero - nothing to change."));
            var now = DateTime.Now;
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                string code = null, name = null;
                int stock = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT Code, Name, Quantity FROM Products WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", productId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                            throw new InvalidOperationException(L.T("This product no longer exists."));
                        code = r.GetString(0);
                        name = r.GetString(1);
                        stock = Convert.ToInt32(r.GetValue(2));
                    }
                }
                if (stock + delta < 0)
                    throw new InsufficientStockException(name, stock, -delta);

                string invoiceNo = NextInvoiceNo(conn, tx, AdjustInvoicePrefix);
                string cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                InsertInvoice(conn, tx, invoiceNo, TransactionType.Adjust, null, 0, 0, 0, 0, 0, PayMethod.Cash, cleanReason, now);
                AdjustStock(conn, tx, productId, delta, now);
                InsertLine(conn, tx, invoiceNo, productId, code, name, TransactionType.Adjust,
                    delta, 0, 0, 0, cleanReason, null, null, null, now);

                tx.Commit();
                Sync.SheetSync.Request();
                return invoiceNo;
            }
        }

        // ---------------- reads ----------------

        public List<StockTransaction> GetInvoice(string invoiceNo)
        {
            using (var conn = Database.Open())
                return GetInvoiceLines(conn, null, invoiceNo);
        }

        public Invoice GetInvoiceHeader(string invoiceNo)
        {
            using (var conn = Database.Open())
                return GetInvoiceHeader(conn, null, invoiceNo);
        }

        private static List<StockTransaction> GetInvoiceLines(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT " + SelectColumns + " FROM StockTransactions WHERE InvoiceNo = @inv ORDER BY Id";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                return ReadAll(cmd);
            }
        }

        private static Invoice GetInvoiceHeader(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT i.InvoiceNo, i.Type, i.CustomerId, i.Subtotal, i.Discount, i.Total, i.CostTotal, i.Paid, i.Method, i.Note, i.CreatedAt, c.Name
FROM Invoices i LEFT JOIN Customers c ON c.Id = i.CustomerId
WHERE i.InvoiceNo = @inv";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new Invoice
                    {
                        InvoiceNo = r.GetString(0),
                        Type = r.GetString(1),
                        CustomerId = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                        Subtotal = Convert.ToDecimal(r.GetValue(3)),
                        Discount = Convert.ToDecimal(r.GetValue(4)),
                        Total = Convert.ToDecimal(r.GetValue(5)),
                        CostTotal = Convert.ToDecimal(r.GetValue(6)),
                        Paid = Convert.ToDecimal(r.GetValue(7)),
                        Method = r.IsDBNull(8) ? PayMethod.Cash : r.GetString(8),
                        Note = r.IsDBNull(9) ? "" : r.GetString(9),
                        CreatedAt = ProductRepository.ParseDate(r.GetValue(10)),
                        CustomerName = r.IsDBNull(11) ? "" : r.GetString(11)
                    };
                }
            }
        }

        /// <summary>
        /// Deletes a transaction line and reverses its effect on stock, recomputing the invoice header.
        /// Returns and adjustments cannot be undone (make a counter-entry instead).
        /// </summary>
        public void Undo(long transactionId)
        {
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                StockTransaction t;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT " + SelectColumns + " FROM StockTransactions WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", transactionId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return;
                        t = Map(r);
                    }
                }
                if (t.Type == TransactionType.Return || t.Type == TransactionType.Adjust)
                    throw new InvalidOperationException(L.T("Returns and adjustments cannot be undone."));
                if (Convert.ToInt64(Scalar(conn, tx, "SELECT COUNT(1) FROM StockTransactions WHERE RefTxId = @v", transactionId)) > 0)
                    throw new InvalidOperationException(L.T("Items from this line were returned. It can no longer be undone."));

                if (t.ProductId.HasValue)
                {
                    int? stock = null;
                    var v = Scalar(conn, tx, "SELECT Quantity FROM Products WHERE Id = @v", t.ProductId.Value);
                    if (v != null && v != DBNull.Value) stock = Convert.ToInt32(v);

                    if (stock.HasValue)
                    {
                        if (!t.IsSale && stock.Value < t.Quantity)
                            throw new InsufficientStockException(t.ProductName, stock.Value, t.Quantity);
                        AdjustStock(conn, tx, t.ProductId.Value, t.IsSale ? t.Quantity : -t.Quantity, DateTime.Now);
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM StockTransactions WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", transactionId);
                    cmd.ExecuteNonQuery();
                }
                Sync.SheetSync.RecordDelete(conn, tx, transactionId);

                // Keep the header's money in line with the remaining lines.
                if (!string.IsNullOrEmpty(t.InvoiceNo))
                {
                    long left = Convert.ToInt64(Scalar(conn, tx, "SELECT COUNT(1) FROM StockTransactions WHERE InvoiceNo = @v", t.InvoiceNo));
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        if (left == 0)
                        {
                            cmd.CommandText = "DELETE FROM Invoices WHERE InvoiceNo = @inv";
                        }
                        else
                        {
                            cmd.CommandText = @"
UPDATE Invoices SET
    Subtotal = (SELECT IFNULL(SUM(Total), 0) FROM StockTransactions WHERE InvoiceNo = @inv),
    CostTotal = (SELECT IFNULL(SUM(UnitCost * Quantity), 0) FROM StockTransactions WHERE InvoiceNo = @inv)
WHERE InvoiceNo = @inv;
UPDATE Invoices SET Discount = MIN(Discount, Subtotal) WHERE InvoiceNo = @inv;
UPDATE Invoices SET Total = Subtotal - Discount WHERE InvoiceNo = @inv;
UPDATE Invoices SET Paid = MIN(Paid, Total) WHERE InvoiceNo = @inv;";
                        }
                        cmd.Parameters.AddWithValue("@inv", t.InvoiceNo);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
                Sync.SheetSync.Request();
            }
        }

        public List<StockTransaction> GetTransactions(DateTime? from, string type = null, string search = null, int limit = 0, DateTime? to = null)
        {
            var sql = new StringBuilder("SELECT " + SelectColumns + " FROM StockTransactions WHERE 1=1");
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                if (from.HasValue)
                {
                    sql.Append(" AND CreatedAt >= @from");
                    cmd.Parameters.AddWithValue("@from", FormatDate(from.Value));
                }
                if (to.HasValue)
                {
                    sql.Append(" AND CreatedAt < @to");
                    cmd.Parameters.AddWithValue("@to", FormatDate(to.Value));
                }
                // Each history screen shows its related rows: sales with returns, stock-ins with adjustments.
                if (type == TransactionType.Sale)
                    sql.Append(" AND Type IN ('SALE', 'RETURN')");
                else if (type == TransactionType.StockIn)
                    sql.Append(" AND Type IN ('STOCK_IN', 'ADJUST')");
                else if (!string.IsNullOrEmpty(type))
                {
                    sql.Append(" AND Type = @type");
                    cmd.Parameters.AddWithValue("@type", type);
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql.Append(" AND (ProductCode LIKE @q OR ProductName LIKE @q OR Note LIKE @q OR InvoiceNo LIKE @q OR VendorName LIKE @q)");
                    cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
                }
                sql.Append(" ORDER BY CreatedAt DESC, Id DESC");
                if (limit > 0)
                {
                    sql.Append(" LIMIT @limit");
                    cmd.Parameters.AddWithValue("@limit", limit);
                }
                cmd.CommandText = sql.ToString();
                return ReadAll(cmd);
            }
        }

        /// <summary>
        /// Sales in [from, to): invoice count, units (net of returns), revenue and profit after
        /// discounts and returns, plus the discounts given.
        /// </summary>
        public SalesSummary GetSalesSummary(DateTime? from, DateTime? to = null)
        {
            using (var conn = Database.Open())
            {
                var s = new SalesSummary();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT IFNULL(SUM(CASE WHEN Type = 'SALE' THEN 1 ELSE 0 END), 0),
       IFNULL(SUM(Total), 0),
       IFNULL(SUM(Total - CostTotal), 0),
       IFNULL(SUM(CASE WHEN Type = 'SALE' THEN Discount ELSE 0 END), 0)
FROM Invoices WHERE Type IN ('SALE', 'RETURN')" + RangeSql(cmd, from, to);
                    using (var r = cmd.ExecuteReader())
                    {
                        r.Read();
                        s.SaleCount = Convert.ToInt32(r.GetValue(0));
                        s.Revenue = Convert.ToDecimal(r.GetValue(1));
                        s.Profit = Convert.ToDecimal(r.GetValue(2));
                        s.Discount = Convert.ToDecimal(r.GetValue(3));
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT IFNULL(SUM(Quantity), 0) FROM StockTransactions WHERE Type IN ('SALE', 'RETURN')" + RangeSql(cmd, from, to);
                    s.UnitsSold = Convert.ToInt64(cmd.ExecuteScalar());
                }
                return s;
            }
        }

        /// <summary>Stock-in count, units and purchase total since <paramref name="from"/>.</summary>
        public SalesSummary GetStockInSummary(DateTime? from, DateTime? to = null)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(DISTINCT InvoiceNo), IFNULL(SUM(Quantity), 0), IFNULL(SUM(Total), 0)
FROM StockTransactions WHERE Type = 'STOCK_IN'" + RangeSql(cmd, from, to);
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    return new SalesSummary
                    {
                        SaleCount = Convert.ToInt32(r.GetValue(0)),
                        UnitsSold = Convert.ToInt64(r.GetValue(1)),
                        Revenue = Convert.ToDecimal(r.GetValue(2)),
                        Profit = 0m
                    };
                }
            }
        }

        /// <summary>
        /// Net sales and profit per time bucket in [from, to), discounts and returns included.
        /// Bucket keys: "HH" (hour), "yyyy-MM-dd" (day) or "yyyy-MM" (month).
        /// </summary>
        public Dictionary<string, Tuple<decimal, decimal>> GetSalesSeries(DateTime from, DateTime to, string bucket)
        {
            string fmt = bucket == "hour" ? "%H" : bucket == "month" ? "%Y-%m" : "%Y-%m-%d";
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT strftime(@fmt, CreatedAt) AS k, IFNULL(SUM(Total), 0), IFNULL(SUM(Total - CostTotal), 0)
FROM Invoices
WHERE Type IN ('SALE', 'RETURN') AND CreatedAt >= @from AND CreatedAt < @to
GROUP BY k";
                cmd.Parameters.AddWithValue("@fmt", fmt);
                cmd.Parameters.AddWithValue("@from", FormatDate(from));
                cmd.Parameters.AddWithValue("@to", FormatDate(to));
                var result = new Dictionary<string, Tuple<decimal, decimal>>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (r.IsDBNull(0)) continue;
                        result[r.GetString(0)] = Tuple.Create(Convert.ToDecimal(r.GetValue(1)), Convert.ToDecimal(r.GetValue(2)));
                    }
                }
                return result;
            }
        }

        /// <summary>Best-selling products by revenue in [from, to), net of returns: name, revenue, units.</summary>
        public List<Tuple<string, decimal, long>> GetTopProducts(DateTime from, DateTime to, int count)
        {
            using (var conn = Database.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT ProductName, IFNULL(SUM(Total), 0) AS revenue, IFNULL(SUM(Quantity), 0)
FROM StockTransactions
WHERE Type IN ('SALE', 'RETURN') AND CreatedAt >= @from AND CreatedAt < @to
GROUP BY IFNULL(ProductId, ProductName)
HAVING SUM(Quantity) <> 0 OR SUM(Total) <> 0
ORDER BY revenue DESC
LIMIT @n";
                cmd.Parameters.AddWithValue("@from", FormatDate(from));
                cmd.Parameters.AddWithValue("@to", FormatDate(to));
                cmd.Parameters.AddWithValue("@n", count);
                var list = new List<Tuple<string, decimal, long>>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(Tuple.Create(r.GetString(0), Convert.ToDecimal(r.GetValue(1)), Convert.ToInt64(r.GetValue(2))));
                }
                return list;
            }
        }

        // ---------------- internals ----------------

        private static string RangeSql(SQLiteCommand cmd, DateTime? from, DateTime? to)
        {
            var sql = "";
            if (from.HasValue)
            {
                sql += " AND CreatedAt >= @from";
                cmd.Parameters.AddWithValue("@from", FormatDate(from.Value));
            }
            if (to.HasValue)
            {
                sql += " AND CreatedAt < @to";
                cmd.Parameters.AddWithValue("@to", FormatDate(to.Value));
            }
            return sql;
        }

        private static object Scalar(SQLiteConnection conn, SQLiteTransaction tx, string sql, object value)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@v", value);
                return cmd.ExecuteScalar();
            }
        }

        private static void InsertInvoice(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo, string type,
            long? customerId, decimal subtotal, decimal discount, decimal total, decimal costTotal,
            decimal paid, string method, string note, DateTime now)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO Invoices (InvoiceNo, Type, CustomerId, Subtotal, Discount, Total, CostTotal, Paid, Method, Note, CreatedAt)
VALUES (@inv, @type, @cust, @sub, @disc, @total, @cost, @paid, @method, @note, @now)";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@cust", customerId.HasValue ? (object)customerId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@sub", (double)subtotal);
                cmd.Parameters.AddWithValue("@disc", (double)discount);
                cmd.Parameters.AddWithValue("@total", (double)total);
                cmd.Parameters.AddWithValue("@cost", (double)costTotal);
                cmd.Parameters.AddWithValue("@paid", (double)paid);
                cmd.Parameters.AddWithValue("@method", method);
                cmd.Parameters.AddWithValue("@note", (object)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", FormatDate(now));
                cmd.ExecuteNonQuery();
            }
        }

        private static void InsertLine(SQLiteConnection conn, SQLiteTransaction tx, string invoiceNo, long productId,
            string code, string name, string type, int quantity, decimal unitPrice, decimal unitCost, decimal total,
            string note, long? vendorId, string vendorName, long? refTxId, DateTime now)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO StockTransactions (InvoiceNo, ProductId, ProductCode, ProductName, Type, Quantity, UnitPrice, UnitCost, Total, Note, VendorId, VendorName, RefTxId, CreatedAt)
VALUES (@inv, @pid, @code, @name, @type, @qty, @price, @cost, @total, @note, @vid, @vname, @ref, @now)";
                cmd.Parameters.AddWithValue("@inv", invoiceNo);
                cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@code", code);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@qty", quantity);
                cmd.Parameters.AddWithValue("@price", (double)unitPrice);
                cmd.Parameters.AddWithValue("@cost", (double)unitCost);
                cmd.Parameters.AddWithValue("@total", (double)total);
                cmd.Parameters.AddWithValue("@note", (object)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@vid", vendorId.HasValue ? (object)vendorId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@vname", (object)vendorName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ref", refTxId.HasValue ? (object)refTxId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@now", FormatDate(now));
                cmd.ExecuteNonQuery();
            }
        }

        private static string NextInvoiceNo(SQLiteConnection conn, SQLiteTransaction tx, string prefix)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT IFNULL(MAX(CAST(SUBSTR(InvoiceNo, @start) AS INTEGER)), 0) FROM Invoices WHERE InvoiceNo LIKE @like";
                cmd.Parameters.AddWithValue("@start", prefix.Length + 1);
                cmd.Parameters.AddWithValue("@like", prefix + "%");
                long last = Convert.ToInt64(cmd.ExecuteScalar());
                return prefix + (last + 1).ToString("000000", CultureInfo.InvariantCulture);
            }
        }

        private static void AdjustStock(SQLiteConnection conn, SQLiteTransaction tx, long productId, int delta, DateTime now)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Products SET Quantity = Quantity + @delta, UpdatedAt = @now WHERE Id = @id";
                cmd.Parameters.AddWithValue("@delta", delta);
                cmd.Parameters.AddWithValue("@now", FormatDate(now));
                cmd.Parameters.AddWithValue("@id", productId);
                cmd.ExecuteNonQuery();
            }
        }

        private static List<StockTransaction> ReadAll(SQLiteCommand cmd)
        {
            var list = new List<StockTransaction>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    list.Add(Map(r));
            }
            return list;
        }

        private static StockTransaction Map(SQLiteDataReader r)
        {
            return new StockTransaction
            {
                Id = r.GetInt64(0),
                ProductId = r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
                ProductCode = r.GetString(2),
                ProductName = r.GetString(3),
                Type = r.GetString(4),
                Quantity = Convert.ToInt32(r.GetValue(5)),
                UnitPrice = Convert.ToDecimal(r.GetValue(6)),
                Total = Convert.ToDecimal(r.GetValue(7)),
                Note = r.IsDBNull(8) ? "" : r.GetString(8),
                CreatedAt = ProductRepository.ParseDate(r.GetValue(9)),
                InvoiceNo = r.IsDBNull(10) ? "" : r.GetString(10),
                UnitCost = r.IsDBNull(11) ? 0m : Convert.ToDecimal(r.GetValue(11)),
                VendorId = r.IsDBNull(12) ? (long?)null : r.GetInt64(12),
                VendorName = r.IsDBNull(13) ? "" : r.GetString(13),
                RefTxId = r.IsDBNull(14) ? (long?)null : r.GetInt64(14)
            };
        }

        private static string FormatDate(DateTime d)
        {
            return ProductRepository.FormatDate(d);
        }

        private sealed class ProductSnapshot
        {
            public string Code;
            public string Name;
            public int Stock;
            public decimal Cost;
        }
    }
}
