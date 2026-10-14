using System;

namespace InventoryManager.Models
{
    /// <summary>Payment methods, stored as English codes; displayed through L.T.</summary>
    public static class PayMethod
    {
        public const string Cash = "CASH";
        public const string Bkash = "BKASH";
        public const string Nagad = "NAGAD";
        public const string Card = "CARD";

        public static readonly string[] All = { Cash, Bkash, Nagad, Card };

        public static string Display(string code)
        {
            switch (code)
            {
                case Bkash: return L.T("bKash");
                case Nagad: return L.T("Nagad");
                case Card: return L.T("Card");
                default: return L.T("Cash");
            }
        }
    }

    /// <summary>
    /// One checkout: the money-level facts shared by all its lines.
    /// For RETURN invoices the money columns are negative, so sums over invoices net out naturally.
    /// </summary>
    public class Invoice
    {
        public string InvoiceNo { get; set; }
        public string Type { get; set; }           // TransactionType values
        public long? CustomerId { get; set; }
        public string CustomerName { get; set; }   // joined for display
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }         // Subtotal - Discount
        public decimal CostTotal { get; set; }
        public decimal Paid { get; set; }
        public string Method { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }

        public decimal Due
        {
            get { return Total - Paid; }
        }

        public decimal Profit
        {
            get { return Total - CostTotal; }
        }
    }
}
