namespace InventoryManager.Models
{
    /// <summary>One line in the POS cart (sale or stock-in) before checkout.</summary>
    public class CartLine
    {
        public long ProductId { get; set; }
        public string Code { get; set; }
        public string Barcode { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        /// <summary>Purchase rate per unit (sale carts: for the profit preview).</summary>
        public decimal UnitCost { get; set; }

        /// <summary>Stock on hand when the line was last refreshed (for warnings only).</summary>
        public int Stock { get; set; }

        public decimal LineTotal
        {
            get { return UnitPrice * Quantity; }
        }

        public decimal LineProfit
        {
            get { return (UnitPrice - UnitCost) * Quantity; }
        }
    }
}
