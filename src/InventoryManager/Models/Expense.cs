using System;

namespace InventoryManager.Models
{
    /// <summary>A shop expense (rent, electricity, salary...), counted against profit.</summary>
    public class Expense
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public string Category { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
