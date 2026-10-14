using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.UI;

namespace InventoryManager.Printing
{
    /// <summary>
    /// A4 business report for a date range: sales, discounts, cost, gross profit,
    /// expenses by category, net profit, stock-in, top products and outstanding dues.
    /// </summary>
    public static class ReportPrinter
    {
        private abstract class Row
        {
            public abstract float Draw(Graphics g, Fonts f, RectangleF area, float y, bool measureOnly);
        }

        private sealed class Fonts : IDisposable
        {
            public readonly Font Title = new Font(FontManager.Regular, 16f, FontStyle.Bold);
            public readonly Font Section = new Font(FontManager.SemiBold, 11.5f);
            public readonly Font Regular = new Font(FontManager.Regular, 10f);
            public readonly Font Bold = new Font(FontManager.SemiBold, 10f);
            public readonly Font Num = new Font(FontManager.Numbers, 10f);
            public readonly Font NumBold = new Font(FontManager.Numbers, 10.5f, FontStyle.Bold);
            public readonly Font Small = new Font(FontManager.Regular, 8.5f);
            public void Dispose()
            {
                Title.Dispose(); Section.Dispose(); Regular.Dispose();
                Bold.Dispose(); Num.Dispose(); NumBold.Dispose(); Small.Dispose();
            }
        }

        private sealed class TextRow : Row
        {
            public string Text; public int Kind;   // 0 title, 1 subtitle, 2 section, 3 note
            public override float Draw(Graphics g, Fonts f, RectangleF area, float y, bool measureOnly)
            {
                Font font = Kind == 0 ? f.Title : Kind == 2 ? f.Section : Kind == 3 ? f.Small : f.Regular;
                float top = Kind == 2 ? 14f : 2f;
                var size = g.MeasureString(Text, font, (int)area.Width);
                if (!measureOnly)
                {
                    g.DrawString(Text, font, Brushes.Black, new RectangleF(area.X, y + top, area.Width, size.Height + 2));
                    if (Kind == 2)
                        g.DrawLine(Pens.Black, area.X, y + top + size.Height + 4, area.Right, y + top + size.Height + 4);
                }
                return top + size.Height + (Kind == 2 ? 10f : 2f);
            }
        }

        private sealed class PairRow : Row
        {
            public string Label, Mid, Value; public bool Bold; public bool Indent;
            public override float Draw(Graphics g, Fonts f, RectangleF area, float y, bool measureOnly)
            {
                float h = g.MeasureString("Ag", f.Regular).Height + 4;
                if (!measureOnly)
                {
                    float x = area.X + (Indent ? 18 : 0);
                    var right = new StringFormat { Alignment = StringAlignment.Far };
                    g.DrawString(Label, Bold ? f.Bold : f.Regular, Brushes.Black,
                        new RectangleF(x, y + 2, area.Width * 0.55f - (x - area.X), h));
                    if (!string.IsNullOrEmpty(Mid))
                        g.DrawString(Mid, f.Num, Brushes.Black,
                            new RectangleF(area.X + area.Width * 0.55f, y + 2, area.Width * 0.2f, h), right);
                    g.DrawString(Value, Bold ? f.NumBold : f.Num, Brushes.Black,
                        new RectangleF(area.X, y + 2, area.Width, h), right);
                }
                return h;
            }
        }

        private sealed class GapRow : Row
        {
            public float Height = 8;
            public override float Draw(Graphics g, Fonts f, RectangleF area, float y, bool measureOnly) { return Height; }
        }

        /// <summary>Builds the document and shows the standard print preview (printing happens from there).</summary>
        public static void ShowReport(IWin32Window owner, DateRange range)
        {
            var doc = CreateDocument(range);
            var dlg = new PrintPreviewDialog
            {
                Document = doc,
                Width = 980,
                Height = 760,
                StartPosition = FormStartPosition.CenterParent,
                UseAntiAlias = true,
                Text = L.T("Report preview")
            };
            using (dlg)
                dlg.ShowDialog(owner);
            doc.Dispose();
        }

        /// <summary>Public so tests can render the report to an image without a printer.</summary>
        public static PrintDocument CreateDocument(DateRange range)
        {
            var rows = BuildRows(range);
            var doc = new PrintDocument();
            doc.DocumentName = L.T("Business report") + " " + L.ShortDate(DateTime.Now);
            int index = 0;
            doc.BeginPrint += (s, e) => index = 0;
            doc.PrintPage += (s, e) =>
            {
                using (var fonts = new Fonts())
                {
                    var g = e.Graphics;
                    var area = new RectangleF(e.MarginBounds.X, e.MarginBounds.Y, e.MarginBounds.Width, e.MarginBounds.Height);
                    float y = area.Y;
                    while (index < rows.Count)
                    {
                        float h = rows[index].Draw(g, fonts, area, y, true);
                        if (y + h > area.Bottom && y > area.Y) break;   // next page (always place at least one row)
                        rows[index].Draw(g, fonts, area, y, false);
                        y += h;
                        index++;
                    }
                    e.HasMorePages = index < rows.Count;
                }
            };
            return doc;
        }

        private static List<Row> BuildRows(DateRange range)
        {
            var tx = new TransactionRepository();
            var expenses = new ExpenseRepository();
            var customers = new CustomerRepository();

            var s = tx.GetSalesSummary(range.From, range.To);
            decimal cogs = s.Revenue - s.Profit;
            decimal expTotal = expenses.Total(range.From, range.To);
            var expCats = expenses.TotalsByCategory(range.From, range.To);
            decimal net = s.Profit - expTotal;
            var stockIn = tx.GetStockInSummary(range.From, range.To);
            var top = tx.GetTopProducts(range.From, range.To, 10);
            var allCustomers = customers.GetAll();
            decimal dueTotal = allCustomers.Where(c => c.Due > 0).Sum(c => c.Due);
            int dueCount = allCustomers.Count(c => c.Due > 0);

            string business = string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName;
            var rows = new List<Row>
            {
                new TextRow { Text = business, Kind = 0 },
                new TextRow { Text = (string.IsNullOrWhiteSpace(AppSettings.BusinessAddress) ? "" : AppSettings.BusinessAddress + "   ") +
                                     (string.IsNullOrWhiteSpace(AppSettings.BusinessPhone) ? "" : L.T("Phone:") + " " + AppSettings.BusinessPhone), Kind = 1 },
                new TextRow { Text = L.F("Business report  ·  {0}  ({1} – {2})", range.Describe(),
                                     L.ShortDate(range.From), L.ShortDate(range.To.AddDays(-1))), Kind = 1 },

                new TextRow { Text = L.T("Sales & profit"), Kind = 2 },
                new PairRow { Label = L.T("Number of sales"), Value = L.Num(s.SaleCount) },
                new PairRow { Label = L.T("Units sold"), Value = L.Num(s.UnitsSold) },
                new PairRow { Label = L.T("Total sales (after discounts & returns)"), Value = Theme.Money(s.Revenue) },
                new PairRow { Label = L.T("Discounts given"), Value = Theme.Money(s.Discount) },
                new PairRow { Label = L.T("Cost of goods sold"), Value = Theme.Money(cogs) },
                new PairRow { Label = L.T("Gross profit"), Value = Theme.Money(s.Profit), Bold = true },

                new TextRow { Text = L.T("Expenses"), Kind = 2 }
            };
            foreach (var c in expCats)
                rows.Add(new PairRow { Label = string.IsNullOrWhiteSpace(c.Item1) ? L.T("(no category)") : c.Item1, Value = Theme.Money(c.Item2), Indent = true });
            if (expCats.Count == 0)
                rows.Add(new PairRow { Label = L.T("(no expenses in this period)"), Value = Theme.Money(0) });
            rows.Add(new PairRow { Label = L.T("Total expenses"), Value = Theme.Money(expTotal), Bold = true });
            rows.Add(new GapRow());
            rows.Add(new PairRow { Label = L.T("NET PROFIT (gross profit − expenses)"), Value = Theme.Money(net), Bold = true });

            rows.Add(new TextRow { Text = L.T("Stock purchased in this period"), Kind = 2 });
            rows.Add(new PairRow { Label = L.T("Stock-in entries"), Value = L.Num(stockIn.SaleCount) });
            rows.Add(new PairRow { Label = L.T("Units received"), Value = L.Num(stockIn.UnitsSold) });
            rows.Add(new PairRow { Label = L.T("Purchase total"), Value = Theme.Money(stockIn.Revenue), Bold = true });

            rows.Add(new TextRow { Text = L.T("Top-selling products"), Kind = 2 });
            if (top.Count == 0)
                rows.Add(new TextRow { Text = L.T("No sales in this period"), Kind = 3 });
            else
            {
                rows.Add(new PairRow { Label = L.T("Product"), Mid = L.T("Qty"), Value = L.T("Sales"), Bold = true });
                foreach (var t in top)
                    rows.Add(new PairRow { Label = t.Item1, Mid = L.Num(t.Item3), Value = Theme.Money(t.Item2) });
            }

            rows.Add(new TextRow { Text = L.T("Customer dues (today)"), Kind = 2 });
            rows.Add(new PairRow { Label = L.T("Customers with due"), Value = L.Num(dueCount) });
            rows.Add(new PairRow { Label = L.T("Total outstanding"), Value = Theme.Money(dueTotal), Bold = true });

            rows.Add(new GapRow { Height = 16 });
            rows.Add(new TextRow { Text = L.F("Printed {0} · {1}", L.ShortDate(DateTime.Now) + " " + L.Date(DateTime.Now, "hh:mm tt"), L.T(AppInfo.ProductName)), Kind = 3 });
            return rows;
        }
    }
}
