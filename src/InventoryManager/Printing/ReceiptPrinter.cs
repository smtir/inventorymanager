using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using InventoryManager.Data;
using InventoryManager.Models;
using InventoryManager.UI;

namespace InventoryManager.Printing
{
    public class ReceiptLine
    {
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public List<string> Serials { get; set; }
        public decimal Total { get { return UnitPrice * Quantity; } }
    }

    public class ReceiptData
    {
        public string InvoiceNo { get; set; }
        public DateTime Date { get; set; }
        public string Note { get; set; }
        public List<ReceiptLine> Lines { get; set; }

        // Invoice-level money. Total falls back to the line sum when not set.
        public decimal Discount { get; set; }
        public decimal? TotalOverride { get; set; }
        public decimal? Paid { get; set; }
        public string Method { get; set; }
        public string CustomerName { get; set; }
        public decimal? TotalDue { get; set; }       // the customer's whole due after this invoice
        public bool IsReturn { get; set; }

        public decimal Subtotal { get { return Lines.Sum(l => l.Total); } }
        public decimal Total { get { return TotalOverride ?? (Subtotal - Discount); } }
    }

    /// <summary>
    /// Prints sale and return receipts on any Windows printer. 58 mm / 80 mm thermal printers get a
    /// narrow roll layout; A4 prints the same receipt in a column at the top of the page.
    /// </summary>
    public static class ReceiptPrinter
    {
        public static void PrintInvoice(string invoiceNo)
        {
            Print(BuildFromInvoice(invoiceNo));
        }

        /// <summary>Builds the receipt from the stored invoice (header + lines + serials).</summary>
        public static ReceiptData BuildFromInvoice(string invoiceNo)
        {
            var repo = new TransactionRepository();
            var rows = repo.GetInvoice(invoiceNo);
            if (rows.Count == 0)
                throw new InvalidOperationException(L.F("Invoice {0} was not found (it may have been undone).", invoiceNo));
            var header = repo.GetInvoiceHeader(invoiceNo);

            var serials = new SerialRepository().GetByInvoice(invoiceNo);
            bool isReturn = header != null && header.Type == TransactionType.Return;
            decimal sign = isReturn ? -1m : 1m;   // returns are stored negative; print them positive

            var data = new ReceiptData
            {
                InvoiceNo = invoiceNo,
                Date = rows[0].CreatedAt,
                Note = header != null ? header.Note : rows[0].Note,
                IsReturn = isReturn,
                Lines = rows.Select(r => new ReceiptLine
                {
                    Name = r.ProductName,
                    Quantity = (int)(sign * r.Quantity),
                    UnitPrice = r.UnitPrice,
                    Serials = serials.Where(x => x.ProductId == r.ProductId).Select(x => x.Serial).ToList()
                }).ToList()
            };
            if (header != null)
            {
                data.Discount = sign * header.Discount;
                data.TotalOverride = sign * header.Total;
                data.Paid = sign * header.Paid;
                data.Method = header.Method;
                data.CustomerName = header.CustomerName;
                if (header.CustomerId.HasValue)
                    data.TotalDue = new CustomerRepository().GetBalance(header.CustomerId.Value);
            }
            return data;
        }

        public static void PrintTest()
        {
            Print(SampleReceipt());
        }

        public static ReceiptData SampleReceipt()
        {
            return new ReceiptData
            {
                InvoiceNo = "TEST-000000",
                Date = DateTime.Now,
                Note = L.T("This is a test print"),
                Discount = 50m,
                Paid = 1300m,
                Method = PayMethod.Cash,
                Lines = new List<ReceiptLine>
                {
                    new ReceiptLine { Name = L.T("Sample product one"), Quantity = 2, UnitPrice = 150m },
                    new ReceiptLine { Name = L.T("A sample product with a much longer name"), Quantity = 1, UnitPrice = 1250m }
                }
            };
        }

        public static void Print(ReceiptData data)
        {
            using (var doc = CreateDocument(data))
                doc.Print();
        }

        /// <summary>Builds the print document (used for printing and for on-screen preview).</summary>
        public static PrintDocument CreateDocument(ReceiptData data)
        {
            var doc = new PrintDocument();
            try
            {
                doc.DocumentName = "Receipt " + data.InvoiceNo;
                if (!string.IsNullOrWhiteSpace(AppSettings.ReceiptPrinter))
                {
                    doc.PrinterSettings.PrinterName = AppSettings.ReceiptPrinter;
                    if (!doc.PrinterSettings.IsValid)
                        throw new InvalidOperationException("Printer \"" + AppSettings.ReceiptPrinter + "\" was not found.");
                }
                else if (!doc.PrinterSettings.IsValid)
                {
                    throw new InvalidOperationException(L.T("No default printer is installed."));
                }

                var paper = AppSettings.ReceiptPaper ?? "80mm";
                int widthHundredths = paper == "58mm" ? 228 : paper == "80mm" ? 315 : 0;   // 1/100 inch
                float fontSize = paper == "58mm" ? 7.5f : paper == "80mm" ? 8.5f : 10f;

                if (widthHundredths > 0)
                {
                    // Roll paper: custom size tall enough for the content.
                    int serialLines = data.Lines.Sum(l => l.Serials != null ? l.Serials.Count : 0);
                    int lines = 20 + data.Lines.Count * 2 + serialLines + (string.IsNullOrWhiteSpace(data.Note) ? 0 : 2);
                    int height = (int)(lines * fontSize * (L.Bangla ? 1.75f : 1.45f)) + 40;
                    doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", widthHundredths, height);
                    doc.DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
                }
                else
                {
                    doc.DefaultPageSettings.Margins = new Margins(60, 60, 60, 60);
                }

                doc.PrintPage += (s, e) => Draw(e, data, fontSize, widthHundredths == 0);
                return doc;
            }
            catch
            {
                doc.Dispose();
                throw;
            }
        }

        /// <summary>Prints a customer's due slip: the latest ledger rows plus the current balance.</summary>
        public static void PrintDueSlip(Customer customer, IList<LedgerEntry> entries)
        {
            var recent = entries.Count > 12 ? entries.Skip(entries.Count - 12).ToList() : new List<LedgerEntry>(entries);
            using (var doc = new PrintDocument())
            {
                doc.DocumentName = "Due slip " + customer.Name;
                if (!string.IsNullOrWhiteSpace(AppSettings.ReceiptPrinter))
                {
                    doc.PrinterSettings.PrinterName = AppSettings.ReceiptPrinter;
                    if (!doc.PrinterSettings.IsValid)
                        throw new InvalidOperationException("Printer \"" + AppSettings.ReceiptPrinter + "\" was not found.");
                }
                else if (!doc.PrinterSettings.IsValid)
                {
                    throw new InvalidOperationException(L.T("No default printer is installed."));
                }

                var paper = AppSettings.ReceiptPaper ?? "80mm";
                int widthHundredths = paper == "58mm" ? 228 : paper == "80mm" ? 315 : 0;
                float fontSize = paper == "58mm" ? 7.5f : paper == "80mm" ? 8.5f : 10f;
                if (widthHundredths > 0)
                {
                    int lines = 14 + recent.Count * 2;
                    int height = (int)(lines * fontSize * (L.Bangla ? 1.75f : 1.45f)) + 40;
                    doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", widthHundredths, height);
                    doc.DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
                }
                else
                {
                    doc.DefaultPageSettings.Margins = new Margins(60, 60, 60, 60);
                }
                doc.PrintPage += (s2, e2) => DrawDueSlip(e2, customer, recent, fontSize, widthHundredths == 0);
                doc.Print();
            }
        }

        private static void DrawDueSlip(PrintPageEventArgs e, Customer customer, List<LedgerEntry> entries, float fontSize, bool a4)
        {
            var g = e.Graphics;
            var area = e.MarginBounds;
            float width = a4 ? Math.Min(area.Width, 380) : area.Width;
            float left = area.Left;
            float y = area.Top;

            using (var regular = new Font(FontManager.Regular, fontSize))
            using (var bold = new Font(FontManager.SemiBold, fontSize))
            using (var title = new Font(FontManager.Regular, fontSize + 3, FontStyle.Bold))
            using (var num = new Font(FontManager.Numbers, fontSize))
            using (var numBig = new Font(FontManager.Numbers, fontSize + 2, FontStyle.Bold))
            using (var pen = new Pen(Color.Black, 0.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            {
                var center = new StringFormat { Alignment = StringAlignment.Center };
                var right = new StringFormat { Alignment = StringAlignment.Far };
                var wrap = new StringFormat { Trimming = StringTrimming.Word };

                Action<string, Font, StringFormat> line = (text, font, fmt) =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var size = g.MeasureString(text, font, (int)width, fmt);
                    g.DrawString(text, font, Brushes.Black, new RectangleF(left, y, width, size.Height + 1), fmt);
                    y += size.Height;
                };
                Action separator = () =>
                {
                    y += fontSize * 0.4f;
                    g.DrawLine(pen, left, y, left + width, y);
                    y += fontSize * 0.6f;
                };
                Action<string, string> pair = (l, r) =>
                {
                    float h = g.MeasureString("Ag", regular).Height;
                    g.DrawString(l, L.IsNumeric(l) ? num : regular, Brushes.Black, new RectangleF(left, y, width * 0.62f, h), wrap);
                    g.DrawString(r, num, Brushes.Black, new RectangleF(left, y, width, h), right);
                    y += h;
                };

                string business = string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName;
                line(business, title, center);
                if (!string.IsNullOrWhiteSpace(AppSettings.BusinessPhone))
                    line(L.T("Phone:") + " " + AppSettings.BusinessPhone, regular, center);
                separator();
                line(L.T("DUE SLIP"), bold, center);
                line(L.T("Customer:") + " " + customer.Name + (string.IsNullOrEmpty(customer.Phone) ? "" : " · " + customer.Phone), regular, wrap);
                line(L.ShortDate(DateTime.Now) + ", " + L.Date(DateTime.Now, "hh:mm tt"), regular, wrap);
                separator();

                foreach (var row in entries)
                {
                    string what = L.T(row.Kind == "PAYMENT" ? "Payment" : row.Kind == "RETURN" ? "Return" : "Sale");
                    string amount = row.Kind == "PAYMENT"
                        ? "- " + Theme.Money(row.Credit)
                        : Theme.Money(row.Charge - row.Credit);
                    pair(L.Date(row.Date, "dd/MM/yy") + "  " + what + (string.IsNullOrEmpty(row.Reference) ? "" : " " + row.Reference), amount);
                }
                separator();
                float h2 = g.MeasureString("Ag", numBig).Height;
                g.DrawString(L.T("Total due"), bold, Brushes.Black, new RectangleF(left, y, width * 0.6f, h2), wrap);
                g.DrawString(Theme.Money(customer.Due), numBig, Brushes.Black, new RectangleF(left, y, width, h2), right);
                y += h2;
            }
            e.HasMorePages = false;
        }

        private static void Draw(PrintPageEventArgs e, ReceiptData data, float fontSize, bool a4)
        {
            var g = e.Graphics;
            var area = e.MarginBounds;
            float width = a4 ? Math.Min(area.Width, 380) : area.Width;
            float left = area.Left;
            float y = area.Top;

            using (var regular = new Font(FontManager.Regular, fontSize))
            using (var bold = new Font(FontManager.SemiBold, fontSize))
            using (var title = new Font(FontManager.Regular, fontSize + 3, FontStyle.Bold))
            using (var big = new Font(FontManager.Regular, fontSize + 2, FontStyle.Bold))
            using (var num = new Font(FontManager.Numbers, fontSize))
            using (var numBig = new Font(FontManager.Numbers, fontSize + 2, FontStyle.Bold))
            using (var pen = new Pen(Color.Black, 0.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            {
                var center = new StringFormat { Alignment = StringAlignment.Center };
                var right = new StringFormat { Alignment = StringAlignment.Far };
                var wrap = new StringFormat { Trimming = StringTrimming.Word };

                Action<string, Font, StringFormat> line = (text, font, fmt) =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var size = g.MeasureString(text, font, (int)width, fmt);
                    g.DrawString(text, font, Brushes.Black, new RectangleF(left, y, width, size.Height + 1), fmt);
                    y += size.Height;
                };
                Action separator = () =>
                {
                    y += fontSize * 0.4f;
                    g.DrawLine(pen, left, y, left + width, y);
                    y += fontSize * 0.6f;
                };
                Action<string, string, Font, Font> pairWith = (l, r, font, rightFont) =>
                {
                    float h = Math.Max(g.MeasureString("Ag", font).Height, g.MeasureString("0", rightFont).Height);
                    g.DrawString(l, L.IsNumeric(l) ? rightFont : font, Brushes.Black, new RectangleF(left, y, width * 0.6f, h), wrap);
                    g.DrawString(r, rightFont, Brushes.Black, new RectangleF(left, y, width, h), right);
                    y += h;
                };
                Action<string, string, Font> pair = (l, r, font) => pairWith(l, r, font, font == big ? numBig : num);

                string business = string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName;
                line(business, title, center);
                line(AppSettings.BusinessAddress, regular, center);
                if (!string.IsNullOrWhiteSpace(AppSettings.BusinessPhone))
                    line(L.T("Phone:") + " " + AppSettings.BusinessPhone, regular, center);
                separator();

                if (data.IsReturn)
                    line(L.T("RETURN RECEIPT"), bold, center);
                pair(L.T("Invoice:") + " " + data.InvoiceNo, "", bold);
                pair(L.ShortDate(data.Date), L.Date(data.Date, "hh:mm tt"), regular);
                if (!string.IsNullOrWhiteSpace(data.CustomerName))
                    line(L.T("Customer:") + " " + data.CustomerName, regular, wrap);
                separator();

                foreach (var l in data.Lines)
                {
                    line(l.Name, regular, wrap);
                    pairWith("   " + L.Num(l.Quantity) + " x " + Theme.Money(l.UnitPrice), Theme.Money(l.Total), num, num);
                    if (l.Serials != null)
                        foreach (var sn in l.Serials)
                            line("   SN: " + sn, regular, wrap);
                }
                separator();

                int units = data.Lines.Sum(l => l.Quantity);
                pair(L.T("Items:") + " " + L.Num(units), "", regular);
                if (data.Discount > 0.004m)
                {
                    pair(L.T("Subtotal"), Theme.Money(data.Subtotal), regular);
                    pair(L.T("Discount"), "- " + Theme.Money(data.Discount), regular);
                }

                if (data.IsReturn)
                {
                    pair(L.T("Refund"), Theme.Money(data.Total), big);
                    decimal cash = data.Paid ?? data.Total;
                    if (data.Total - cash > 0.004m)
                    {
                        pair(L.T("Reduced from due"), Theme.Money(data.Total - cash), regular);
                        pair(L.F("Cash returned ({0})", PayMethod.Display(data.Method)), Theme.Money(cash), regular);
                    }
                }
                else
                {
                    pair(L.T("TOTAL"), Theme.Money(data.Total), big);
                    if (data.Paid.HasValue)
                    {
                        pair(L.F("Paid ({0})", PayMethod.Display(data.Method)), Theme.Money(data.Paid.Value), regular);
                        if (data.Total - data.Paid.Value > 0.004m)
                            pair(L.T("Due this invoice"), Theme.Money(data.Total - data.Paid.Value), bold);
                    }
                }
                if (data.TotalDue.HasValue && Math.Abs(data.TotalDue.Value) > 0.004m)
                    pair(L.T("Total due"), Theme.Money(data.TotalDue.Value), bold);
                separator();

                if (!string.IsNullOrWhiteSpace(data.Note))
                    line(L.T("Note:") + " " + data.Note, regular, wrap);
                y += fontSize * 0.5f;
                line(L.T("Thank you for your purchase!"), bold, center);
            }
            e.HasMorePages = false;
        }
    }
}
