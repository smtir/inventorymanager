using System;

namespace InventoryManager.Models
{
    /// <summary>A serial number recorded for one sold unit, with its warranty window.</summary>
    public class SoldSerial
    {
        public long Id { get; set; }
        public string InvoiceNo { get; set; }
        public long? ProductId { get; set; }
        public string ProductName { get; set; }
        public string Serial { get; set; }
        public int WarrantyMonths { get; set; }
        public DateTime SoldAt { get; set; }

        public DateTime WarrantyEnd
        {
            get { return SoldAt.Date.AddMonths(WarrantyMonths); }
        }

        public bool InWarranty
        {
            get { return WarrantyMonths > 0 && DateTime.Today <= WarrantyEnd; }
        }

        public int DaysLeft
        {
            get { return Math.Max(0, (int)(WarrantyEnd - DateTime.Today).TotalDays); }
        }
    }
}
