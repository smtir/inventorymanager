using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>
    /// Restock order list: every low-stock product grouped by vendor, with a suggested
    /// order quantity the user can edit, printable per vendor on A4.
    /// </summary>
    public class PurchaseOrderForm : Form
    {
        private class OrderLine
        {
            public Product Product;
            public int OrderQty;
            public string VendorKey { get { return string.IsNullOrWhiteSpace(Product.VendorName) ? "" : Product.VendorName; } }
        }

        private readonly List<OrderLine> _lines;
        private readonly Dictionary<string, Vendor> _vendors;
        private readonly DataGridView _grid;

        public PurchaseOrderForm()
        {
            Text = L.T("Order list (restock)");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 540);
            MinimumSize = new Size(700, 400);

            _lines = new ProductRepository().GetAll(lowStockOnly: true)
                .Select(p => new OrderLine { Product = p, OrderQty = Math.Max(p.ReorderLevel * 2 - p.Quantity, 1) })
                .OrderBy(l => l.VendorKey)
                .ThenBy(l => l.Product.Name)
                .ToList();
            _vendors = new VendorRepository().GetAll()
                .GroupBy(v => v.Name ?? "")
                .ToDictionary(gr => gr.Key, gr => gr.First());

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            top.Controls.Add(new Label
            {
                Text = L.F("{0} products are at or below their reorder level.", L.Num(_lines.Count)),
                Font = Theme.Semi(12f), AutoSize = true, Location = new Point(24, 10)
            });
            top.Controls.Add(new Label
            {
                Text = L.T("Adjust the order quantities, then print the list to take to your vendors."),
                ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 36)
            });

            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2 };
            Theme.StyleGrid(_grid);
            _grid.ReadOnly = false;
            var colVendor = DashboardView.Col("Vendor", "Vendor", 110);
            colVendor.ReadOnly = true;
            var colCode = DashboardView.Col("Code", "Code", 60);
            colCode.ReadOnly = true;
            var colName = DashboardView.Col("Name", "Product", 170);
            colName.ReadOnly = true;
            var colQty = DashboardView.Col("Quantity", "In stock", 65, DataGridViewContentAlignment.MiddleRight);
            colQty.ReadOnly = true;
            var colReorder = DashboardView.Col("ReorderLevel", "Reorder At", 65, DataGridViewContentAlignment.MiddleRight);
            colReorder.ReadOnly = true;
            var colOrder = new DataGridViewTextBoxColumn
            {
                Name = "OrderQty",
                HeaderText = L.T("Order qty"),
                FillWeight = 70,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colOrder.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colOrder.DefaultCellStyle.Font = Theme.Num(10f, true);
            _grid.Columns.AddRange(colVendor, colCode, colName, colQty, colReorder, colOrder);
            _grid.CellParsing += Grid_CellParsing;
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            FillGrid();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            var print = Theme.PrimaryButton("Preview & print...");
            var close = Theme.SecondaryButton("Close");
            footer.Controls.Add(print);
            footer.Controls.Add(close);
            footer.Layout += (s, e) =>
            {
                print.Location = new Point(footer.Width - print.Width - 24, 13);
                close.Location = new Point(print.Left - close.Width - 8, 13);
            };

            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(top);
            _grid.BringToFront();
            CancelButton = close;
            close.DialogResult = DialogResult.Cancel;
            print.Click += (s, e) => PreviewAndPrint();
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();
            foreach (var l in _lines)
            {
                int row = _grid.Rows.Add(
                    l.VendorKey.Length == 0 ? L.T("(no vendor)") : l.VendorKey,
                    l.Product.Code, l.Product.Name,
                    L.Num(l.Product.Quantity), L.Num(l.Product.ReorderLevel),
                    L.Num(l.OrderQty));
                _grid.Rows[row].Tag = l;
            }
            _grid.ClearSelection();
        }

        private void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].Name != "OrderQty") return;
            var line = _grid.Rows[e.RowIndex].Tag as OrderLine;
            if (line == null) return;
            int n;
            int.TryParse(L.ToAscii((e.Value ?? "").ToString()).Trim(), out n);
            line.OrderQty = Math.Max(0, Math.Min(1000000, n));
            e.Value = L.Num(line.OrderQty);
            e.ParsingApplied = true;
        }

        private void PreviewAndPrint()
        {
            _grid.EndEdit();
            var lines = _lines.Where(l => l.OrderQty > 0).ToList();
            if (lines.Count == 0)
            {
                Msg.Show(this, L.T("Nothing to order - all quantities are zero."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var doc = CreateDocument(lines, _vendors);
            var dlg = new PrintPreviewDialog
            {
                Document = doc,
                Width = 980,
                Height = 760,
                StartPosition = FormStartPosition.CenterParent,
                UseAntiAlias = true
            };
            using (dlg)
                dlg.ShowDialog(this);
            doc.Dispose();
        }

        private static PrintDocument CreateDocument(List<OrderLine> lines, Dictionary<string, Vendor> vendors)
        {
            var doc = new PrintDocument();
            doc.DocumentName = L.T("Order list (restock)");
            int index = 0;
            string lastVendor = null;
            doc.BeginPrint += (s, e) => { index = 0; lastVendor = null; };
            doc.PrintPage += (s, e) =>
            {
                var g = e.Graphics;
                var area = e.MarginBounds;
                float y = area.Top;
                using (var title = new Font(FontManager.Regular, 15f, FontStyle.Bold))
                using (var section = new Font(FontManager.SemiBold, 11.5f))
                using (var regular = new Font(FontManager.Regular, 10f))
                using (var bold = new Font(FontManager.SemiBold, 10f))
                using (var num = new Font(FontManager.Numbers, 10f))
                using (var small = new Font(FontManager.Regular, 8.5f))
                {
                    var right = new StringFormat { Alignment = StringAlignment.Far };
                    if (index == 0)
                    {
                        string business = string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName;
                        g.DrawString(business + "  ·  " + L.T("Order list (restock)"), title, Brushes.Black, area.Left, y);
                        y += g.MeasureString("Ag", title).Height + 2;
                        g.DrawString(L.ShortDate(DateTime.Now) + (string.IsNullOrWhiteSpace(AppSettings.BusinessPhone) ? "" : "   ·   " + L.T("Phone:") + " " + AppSettings.BusinessPhone),
                            regular, Brushes.Black, area.Left, y);
                        y += g.MeasureString("Ag", regular).Height + 10;
                    }

                    float rowH = g.MeasureString("Ag", regular).Height + 5;
                    while (index < lines.Count)
                    {
                        var l = lines[index];
                        string vendorName = l.VendorKey.Length == 0 ? L.T("(no vendor)") : l.VendorKey;

                        // Vendor section header when the vendor changes.
                        if (vendorName != lastVendor)
                        {
                            if (y + rowH * 4 > area.Bottom) break;   // keep header with at least two rows
                            y += 10;
                            Vendor v;
                            string contact = "";
                            if (l.VendorKey.Length > 0 && vendors.TryGetValue(l.VendorKey, out v))
                            {
                                var parts = new List<string>();
                                if (!string.IsNullOrWhiteSpace(v.ContactPerson)) parts.Add(v.ContactPerson);
                                if (!string.IsNullOrWhiteSpace(v.Phone)) parts.Add(v.Phone);
                                if (!string.IsNullOrWhiteSpace(v.Address)) parts.Add(v.Address);
                                contact = string.Join("  ·  ", parts);
                            }
                            g.DrawString(vendorName, section, Brushes.Black, area.Left, y);
                            y += g.MeasureString("Ag", section).Height + 1;
                            if (contact.Length > 0)
                            {
                                g.DrawString(contact, small, Brushes.Black, area.Left, y);
                                y += g.MeasureString("Ag", small).Height + 1;
                            }
                            g.DrawLine(Pens.Black, area.Left, y + 2, area.Right, y + 2);
                            y += 7;
                            // Column heads (wide right-aligned rects so Bangla headings don't wrap)
                            var rightNoWrap = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
                            g.DrawString(L.T("Code"), bold, Brushes.Black, area.Left, y);
                            g.DrawString(L.T("Product"), bold, Brushes.Black, area.Left + 70, y);
                            g.DrawString(L.T("In stock"), bold, Brushes.Black, new RectangleF(area.Right - 340, y, 200, rowH), rightNoWrap);
                            g.DrawString(L.T("Order qty"), bold, Brushes.Black, new RectangleF(area.Right - 180, y, 180, rowH), rightNoWrap);
                            y += rowH;
                            lastVendor = vendorName;
                        }

                        if (y + rowH > area.Bottom) break;
                        g.DrawString(l.Product.Code, num, Brushes.Black, area.Left, y);
                        g.DrawString(l.Product.Name, regular, Brushes.Black,
                            new RectangleF(area.Left + 70, y, area.Width - 70 - 240, rowH),
                            new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                        g.DrawString(L.Num(l.Product.Quantity), num, Brushes.Black, new RectangleF(area.Right - 220, y, 80, rowH), right);
                        g.DrawString(L.Num(l.OrderQty), num, Brushes.Black, new RectangleF(area.Right - 80, y, 80, rowH), right);
                        y += rowH;
                        index++;
                    }

                    e.HasMorePages = index < lines.Count;
                    if (!e.HasMorePages)
                    {
                        // Signature line for the vendor copy.
                        float sy = Math.Min(area.Bottom - 20, y + 50);
                        g.DrawLine(Pens.Black, area.Left, sy, area.Left + 180, sy);
                        g.DrawString(L.T("Signature"), small, Brushes.Black, area.Left, sy + 3);
                    }
                }
            };
            return doc;
        }
    }
}
