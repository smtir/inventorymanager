using System;

namespace InventoryManager.Models
{
    public static class TransactionType
    {
        public const string Sale = "SALE";
        public const string StockIn = "STOCK_IN";
        public const string Return = "RETURN";       // lines carry negative Quantity/Total
        public const string Adjust = "ADJUST";       // signed Quantity, zero money
    }

    public class StockTransaction
    {
        public long Id { get; set; }
        public string InvoiceNo { get; set; }
        public long? ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string Type { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        /// <summary>Purchase cost per unit at the time (for sales: the product's purchase rate when sold).</summary>
        public decimal UnitCost { get; set; }
        public long? VendorId { get; set; }
        public string VendorName { get; set; }

        /// <summary>For RETURN lines: the Id of the original sale line.</summary>
        public long? RefTxId { get; set; }
        public decimal Total { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Profit on a sale line; negative on a return line; 0 otherwise.</summary>
        public decimal Profit
        {
            get { return IsSale || Type == TransactionType.Return ? Total - UnitCost * Quantity : 0m; }
        }

        public bool IsSale
        {
            get { return Type == TransactionType.Sale; }
        }

        public string TypeDisplay
        {
            get
            {
                switch (Type)
                {
                    case TransactionType.Return: return "Return";
                    case TransactionType.Adjust: return "Adjustment";
                    case TransactionType.StockIn: return "Stock In";
                    default: return "Sale";
                }
            }
        }
    }

    public class SalesSummary
    {
        public int SaleCount { get; set; }
        public long UnitsSold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
        public decimal Discount { get; set; }
    }
}
