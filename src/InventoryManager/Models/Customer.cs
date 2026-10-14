using System;

namespace InventoryManager.Models
{
    /// <summary>A customer in the due ledger (বাকি খাতা).</summary>
    public class Customer
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        // Calculated
        public decimal Due { get; set; }
        public DateTime? LastActivity { get; set; }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Phone) ? Name : Name + "  (" + Phone + ")"; }
        }
    }

    /// <summary>One row of a customer's ledger: an invoice (charge/credit) or a payment.</summary>
    public class LedgerEntry
    {
        public DateTime Date { get; set; }
        public string Kind { get; set; }        // "SALE", "RETURN", "PAYMENT"
        public string Reference { get; set; }   // invoice no, or payment note
        public decimal Charge { get; set; }     // added to the due (invoice total)
        public decimal Credit { get; set; }     // paid at sale / collected later / refunded
        public decimal Balance { get; set; }    // running due after this row
    }
}
