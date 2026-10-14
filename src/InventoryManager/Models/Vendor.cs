using System;

namespace InventoryManager.Models
{
    /// <summary>A supplier that goods are purchased from.</summary>
    public class Vendor
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string ContactPerson { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        // Calculated
        public int ProductCount { get; set; }
        public decimal TotalPurchased { get; set; }
    }
}
