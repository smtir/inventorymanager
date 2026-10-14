using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;
using InventoryManager.Printing;

namespace InventoryManager.UI
{
    /// <summary>
    /// Prints price/barcode labels on common A4 label sheets or a 38x25mm roll.
    /// Pick how many labels of each product; the barcode is Code 128 (falls back to the product code).
    /// </summary>
    public class LabelPrintForm : Form
    {
        private class LabelItem
        {
            public Product Product;
            public int Copies;
        }

        private class LayoutPreset
        {
            public string Name;            // shown in the combo (localized)
            public string Key;             // stored in settings
            public bool Roll;
            public int Cols, Rows;         // A4 grid
            public float LabelW, LabelH;   // mm
        }

        private static readonly LayoutPreset[] Presets =
        {
            new LayoutPreset { Key = "A4_65", Name = "A4 sheet - 65 labels (38 x 21 mm)", Cols = 5, Rows = 13, LabelW = 38f, LabelH = 21.2f },
            new LayoutPreset { Key = "A4_40", Name = "A4 sheet - 40 labels (52.5 x 29.7 mm)", Cols = 4, Rows = 10, LabelW = 52.5f, LabelH = 29.7f },
            new LayoutPreset { Key = "A4_24", Name = "A4 sheet - 24 labels (70 x 37 mm)", Cols = 3, Rows = 8, LabelW = 70f, LabelH = 37f },
            new LayoutPreset { Key = "ROLL_38", Name = "Label roll - 38 x 25 mm (one per label)", Roll = true, LabelW = 38f, LabelH = 25f }
        };

        private readonly List<LabelItem> _items;
        private readonly DataGridView _grid;
        private readonly ComboBox _layout;
        private readonly CheckBox _showPrice;

        public LabelPrintForm(Product preselect)
        {
            Text = L.T("Print barcode labels");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(680, 400);

            _items = new ProductRepository().GetAll()
                .Select(p => new LabelItem { Product = p, Copies = preselect != null && p.Id == preselect.Id ? Math.Max(1, p.Quantity) : 0 })
                .OrderByDescending(i => i.Copies > 0)
                .ThenBy(i => i.Product.Name)
                .ToList();

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            top.Controls.Add(new Label { Text = L.T("Choose how many labels to print for each product."), Font = Theme.Semi(12f), AutoSize = true, Location = new Point(24, 10) });
            top.Controls.Add(new Label { Text = L.T("Products without a barcode use their product code instead."), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 36) });

            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2 };
            Theme.StyleGrid(_grid);
            _grid.ReadOnly = false;
            var colName = DashboardView.Col("Name", "Product", 180);
            colName.ReadOnly = true;
            var colCode = DashboardView.Col("Code", "Code", 70);
            colCode.ReadOnly = true;
            var colBarcode = DashboardView.Col("Barcode", "Barcode", 110);
            colBarcode.ReadOnly = true;
            var colPrice = DashboardView.Col("Price", "Sale Price", 80, DataGridViewContentAlignment.MiddleRight);
            colPrice.ReadOnly = true;
            var colCopies = new DataGridViewTextBoxColumn
            {
                Name = "Copies",
                HeaderText = L.T("Labels"),
                FillWeight = 60,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colCopies.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colCopies.DefaultCellStyle.Font = Theme.Num(10f, true);
            _grid.Columns.AddRange(colName, colCode, colBarcode, colPrice, colCopies);
            _grid.VirtualMode = false;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellParsing += Grid_CellParsing;
            _grid.CellValueNeeded += (s, e) => { };
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.RowCount = 0;
            FillGrid();

            var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(20, 6, 0, 0), BackColor = Theme.Surface, WrapContents = false };
            options.Controls.Add(new Label { Text = L.T("Label sheet"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(4, 9, 8, 0) });
            _layout = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in Presets) _layout.Items.Add(L.T(p.Name));
            string saved = AppSettings.LabelLayout ?? "";
            int idx = Array.FindIndex(Presets, p => p.Key == saved);
            _layout.SelectedIndex = idx >= 0 ? idx : 0;
            options.Controls.Add(_layout);
            _showPrice = new CheckBox { Text = L.T("Include sale price"), AutoSize = true, Checked = true, Margin = new Padding(16, 8, 0, 0) };
            options.Controls.Add(_showPrice);

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
            Controls.Add(options);
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
            foreach (var item in _items)
            {
                int row = _grid.Rows.Add(item.Product.Name, item.Product.Code, item.Product.Barcode,
                    Theme.Money(item.Product.Price), item.Copies == 0 ? "" : L.Num(item.Copies));
                _grid.Rows[row].Tag = item;
            }
            _grid.ClearSelection();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e) { }

        private void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].Name != "Copies") return;
            var item = _grid.Rows[e.RowIndex].Tag as LabelItem;
            if (item == null) return;
            int n;
            int.TryParse(L.ToAscii((e.Value ?? "").ToString()).Trim(), out n);
            item.Copies = Math.Max(0, Math.Min(5000, n));
            e.Value = item.Copies == 0 ? "" : L.Num(item.Copies);
            e.ParsingApplied = true;
        }

        private void PreviewAndPrint()
        {
            _grid.EndEdit();
            var labels = new List<Product>();
            foreach (var item in _items)
                for (int i = 0; i < item.Copies; i++)
                    labels.Add(item.Product);
            if (labels.Count == 0)
            {
                Msg.Show(this, L.T("Set at least one label count first."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var preset = Presets[_layout.SelectedIndex];
            AppSettings.LabelLayout = preset.Key;
            AppSettings.Save();

            var doc = CreateDocument(labels, preset, _showPrice.Checked);
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

        /// <summary>Public so tests can render label pages to an image.</summary>
        public static PrintDocument CreateDocument(IList<Product> labels, int presetIndex, bool showPrice)
        {
            return CreateDocument(labels, Presets[presetIndex], showPrice);
        }

        private static PrintDocument CreateDocument(IList<Product> labels, LayoutPreset preset, bool showPrice)
        {
            var doc = new PrintDocument();
            doc.DocumentName = L.T("Barcode labels");
            int index = 0;
            doc.BeginPrint += (s, e) => index = 0;
            if (preset.Roll)
            {
                doc.DefaultPageSettings.Margins = new Margins(2, 2, 2, 2);
                doc.DefaultPageSettings.PaperSize = new PaperSize("Label",
                    (int)Math.Round(preset.LabelW / 25.4 * 100), (int)Math.Round(preset.LabelH / 25.4 * 100));
            }
            else
            {
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            }
            doc.PrintPage += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                if (preset.Roll)
                {
                    DrawLabel(g, new RectangleF(2, 2, e.PageBounds.Width - 4, e.PageBounds.Height - 4), labels[index], showPrice);
                    index++;
                }
                else
                {
                    // Center the label grid on the A4 page (PageBounds is in 1/100 inch).
                    float lw = (float)(preset.LabelW / 25.4 * 100), lh = (float)(preset.LabelH / 25.4 * 100);
                    float left = (e.PageBounds.Width - preset.Cols * lw) / 2f;
                    float topM = (e.PageBounds.Height - preset.Rows * lh) / 2f;
                    for (int r = 0; r < preset.Rows && index < labels.Count; r++)
                        for (int c = 0; c < preset.Cols && index < labels.Count; c++, index++)
                            DrawLabel(g, new RectangleF(left + c * lw, topM + r * lh, lw, lh), labels[index], showPrice);
                }
                e.HasMorePages = index < labels.Count;
            };
            return doc;
        }

        private static void DrawLabel(Graphics g, RectangleF cell, Product p, bool showPrice)
        {
            // Inner padding so the label survives slight printer misalignment.
            var r = new RectangleF(cell.X + 4, cell.Y + 3, cell.Width - 8, cell.Height - 6);
            string code = !string.IsNullOrWhiteSpace(p.Barcode) ? p.Barcode.Trim() : (p.Code ?? "").Trim();
            float nameH = r.Height * 0.24f;
            float priceH = showPrice ? r.Height * 0.22f : 0f;
            float digitsH = r.Height * 0.18f;
            float barH = r.Height - nameH - priceH - digitsH;

            using (var nameFont = FitFont(g, p.Name, FontManager.Regular, r.Width, nameH))
            using (var digitFont = FitFont(g, code, FontManager.Numbers, r.Width * 0.9f, digitsH))
            {
                var center = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(p.Name, nameFont, Brushes.Black, new RectangleF(r.X, r.Y, r.Width, nameH), center);

                bool drawn = Code128.CanEncode(code) && code.Length > 0 &&
                             Code128.Draw(g, code, new RectangleF(r.X, r.Y + nameH, r.Width, barH - 1));
                if (!drawn)
                    g.DrawString("—", nameFont, Brushes.Black, new RectangleF(r.X, r.Y + nameH + barH / 3, r.Width, nameH), center);

                g.DrawString(L.Digits(code), digitFont, Brushes.Black, new RectangleF(r.X, r.Y + nameH + barH, r.Width, digitsH), center);

                if (showPrice)
                {
                    using (var priceFont = FitFont(g, Theme.Money(p.Price), FontManager.Numbers, r.Width, priceH, FontStyle.Bold))
                        g.DrawString(Theme.Money(p.Price), priceFont, Brushes.Black,
                            new RectangleF(r.X, r.Y + nameH + barH + digitsH, r.Width, priceH), center);
                }
            }
        }

        /// <summary>Largest font (capped) whose text fits the given box.</summary>
        private static Font FitFont(Graphics g, string text, FontFamily family, float maxWidth, float maxHeight, FontStyle style = FontStyle.Regular)
        {
            float size = Math.Max(4f, Math.Min(12f, maxHeight * 0.62f));
            var font = new Font(family, size, style);
            while (size > 4f && g.MeasureString(text ?? "", font).Width > maxWidth)
            {
                font.Dispose();
                size -= 0.5f;
                font = new Font(family, size, style);
            }
            return font;
        }
    }
}
