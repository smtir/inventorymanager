using System;

namespace InventoryManager.Models
{
    public class Product
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Barcode { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public decimal PurchasePrice { get; set; }
        public long? VendorId { get; set; }
        public string VendorName { get; set; }
        public int Quantity { get; set; }
        public int ReorderLevel { get; set; }
        public int WarrantyMonths { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public decimal StockValue
        {
            get { return Price * Quantity; }
        }

        /// <summary>Profit made on one unit at the current prices.</summary>
        public decimal ProfitPerUnit
        {
            get { return Price - PurchasePrice; }
        }

        public string DisplayName
        {
            get { return Code + "  —  " + Name; }
        }

        public bool IsLowStock
        {
            get { return Quantity <= ReorderLevel; }
        }
    }

    public class InventoryStats
    {
        public int TotalProducts { get; set; }
        public long TotalUnits { get; set; }
        public decimal InventoryValue { get; set; }
        public int LowStockCount { get; set; }
        public int CategoryCount { get; set; }
        public decimal InventoryCost { get; set; }
    }
}
