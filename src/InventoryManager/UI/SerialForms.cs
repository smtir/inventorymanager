using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;
using InventoryManager.Scanner;

namespace InventoryManager.UI
{
    /// <summary>
    /// After a sale of warranty products: one box per sold unit to scan/type its serial number.
    /// Phone and USB scans both land in the focused box. Everything is optional.
    /// </summary>
    public class SerialEntryForm : Form, IScanTarget
    {
        /// <summary>The serial-entry form currently on screen (phone scans are routed to it).</summary>
        public static SerialEntryForm Current { get; private set; }

        private readonly string _invoiceNo;
        private readonly List<Tuple<SoldSerial, TextBox>> _boxes = new List<Tuple<SoldSerial, TextBox>>();

        /// <param name="items">productId, product name, quantity, warranty months</param>
        public SerialEntryForm(string invoiceNo, IList<Tuple<long, string, int, int>> items)
        {
            _invoiceNo = invoiceNo;
            Text = L.T("Serial numbers & warranty");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;

            var top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Background };
            top.Controls.Add(new Label { Text = L.T("Scan or type the serial number of each unit."), Font = Theme.Semi(12f), AutoSize = true, Location = new Point(24, 10) });
            top.Controls.Add(new Label { Text = L.T("You can skip any box - serials are optional."), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 34) });

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 12, 36, 4), BackColor = Theme.Surface, AutoScroll = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            int row = 0;
            var now = DateTime.Now;
            foreach (var item in items)
            {
                for (int unit = 1; unit <= item.Item3; unit++)
                {
                    var label = new Label
                    {
                        Text = item.Item2 + (item.Item3 > 1 ? "  (" + L.Num(unit) + "/" + L.Num(item.Item3) + ")" : "") +
                               "   ·   " + L.F("{0} months warranty", L.Num(item.Item4)),
                        AutoSize = true, Margin = new Padding(0, 9, 8, 4), UseMnemonic = false, MaximumSize = new Size(280, 0)
                    };
                    var box = new ScanTextBox { Width = 230, MaxLength = 64, Font = Theme.Num(10f), Margin = new Padding(0, 6, 0, 4) };
                    ProductsView.SetCueBanner(box, "Scan or type serial");
                    box.KeyDown += (s, e) =>
                    {
                        if (e.KeyCode == Keys.Enter)
                        {
                            e.Handled = e.SuppressKeyPress = true;
                            SelectNextControl((Control)s, true, true, true, true);
                        }
                    };
                    body.Controls.Add(label, 0, row);
                    body.Controls.Add(box, 1, row);
                    _boxes.Add(Tuple.Create(new SoldSerial
                    {
                        InvoiceNo = invoiceNo,
                        ProductId = item.Item1,
                        ProductName = item.Item2,
                        WarrantyMonths = item.Item4,
                        SoldAt = now
                    }, (TextBox)box));
                    row++;
                }
            }

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            var save = Theme.PrimaryButton("Save serials");
            var skip = Theme.SecondaryButton("Skip");
            footer.Controls.Add(save);
            footer.Controls.Add(skip);
            footer.Layout += (s, e) =>
            {
                save.Location = new Point(footer.Width - save.Width - 24, 13);
                skip.Location = new Point(save.Left - skip.Width - 8, 13);
            };

            ClientSize = new Size(600, Math.Min(540, 180 + row * 38));
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(top);
            body.BringToFront();
            AcceptButton = save;
            CancelButton = skip;
            skip.DialogResult = DialogResult.Cancel;
            save.Click += (s, e) => Save();

            Shown += (s, e) =>
            {
                Current = this;
                if (_boxes.Count > 0) _boxes[0].Item2.Focus();
            };
            FormClosed += (s, e) => { if (Current == this) Current = null; };
        }

        /// <summary>Phone scan: fill the focused (or first empty) serial box and move on.</summary>
        public ScanResult HandleScan(string code)
        {
            var target = _boxes.FirstOrDefault(b => b.Item2.Focused) ??
                         _boxes.FirstOrDefault(b => b.Item2.Text.Trim().Length == 0);
            if (target == null)
                return ScanResult.Fail(L.T("All serial boxes are already filled."));
            target.Item2.Text = code;
            SelectNextControl(target.Item2, true, true, true, true);
            return new ScanResult { Ok = true, ProductName = target.Item1.ProductName, Message = L.T("Serial captured.") };
        }

        private void Save()
        {
            var list = _boxes
                .Where(b => b.Item2.Text.Trim().Length > 0)
                .Select(b => { b.Item1.Serial = b.Item2.Text.Trim(); return b.Item1; })
                .ToList();
            if (list.Count > 0)
                new SerialRepository().Save(list);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Find a sold unit by its serial number and see its warranty state.</summary>
    public class WarrantyLookupForm : Form
    {
        private readonly SerialRepository _repo = new SerialRepository();
        private readonly TransactionRepository _tx = new TransactionRepository();
        private readonly TextBox _search;
        private readonly DataGridView _grid;
        private List<SoldSerial> _results = new List<SoldSerial>();

        public WarrantyLookupForm()
        {
            Text = L.T("Warranty lookup");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 440);
            MinimumSize = new Size(620, 340);

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            top.Controls.Add(new Label { Text = L.T("Serial number"), AutoSize = true, Location = new Point(24, 22), ForeColor = Theme.TextSecondary });
            _search = new TextBox { Location = new Point(130, 18), Width = 280, Font = Theme.Num(11f) };
            ProductsView.SetCueBanner(_search, "Scan or type a serial...");
            var find = Theme.PrimaryButton("Search");
            find.Location = new Point(420, 15);
            top.Controls.Add(_search);
            top.Controls.Add(find);

            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("Serial", "Serial", 110));
            _grid.Columns.Add(DashboardView.Col("ProductName", "Product", 140));
            _grid.Columns.Add(DashboardView.Col("InvoiceNo", "Invoice", 80));
            _grid.Columns.Add(DashboardView.Col("SoldAt", "Sold on", 80));
            _grid.Columns["SoldAt"].DefaultCellStyle.Format = "dd/MM/yyyy";
            _grid.Columns.Add(DashboardView.Col("WarrantyEnd", "Warranty until", 85));
            _grid.Columns["WarrantyEnd"].DefaultCellStyle.Format = "dd/MM/yyyy";
            _grid.Columns.Add(DashboardView.Col("Status", "Status", 110));
            _grid.Columns["Status"].DataPropertyName = null;
            _grid.CellFormatting += Grid_CellFormatting;

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Background };
            var close = Theme.PrimaryButton("Close");
            footer.Controls.Add(close);
            footer.Layout += (s, e) => close.Location = new Point(footer.Width - close.Width - 24, 11);
            close.DialogResult = DialogResult.OK;
            CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(top);
            _grid.BringToFront();

            find.Click += (s, e) => Search();
            _search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(); } };
            Shown += (s, e) => _search.Focus();
        }

        private void Search()
        {
            _results = _repo.Search(_search.Text);
            _grid.DataSource = _results;
            _grid.ClearSelection();
            if (_results.Count == 0 && _search.Text.Trim().Length > 0)
                Msg.Show(this, L.T("No sold unit found with this serial."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _results.Count) return;
            var r = _results[e.RowIndex];
            if (_grid.Columns[e.ColumnIndex].Name != "Status") return;
            if (r.WarrantyMonths <= 0)
            {
                e.Value = L.T("No warranty");
                e.CellStyle.ForeColor = Theme.TextMuted;
            }
            else if (r.InWarranty)
            {
                e.Value = L.F("{0} days left", L.Num(r.DaysLeft));
                e.CellStyle.ForeColor = Theme.Success;
                e.CellStyle.Font = Theme.SmallBold;
            }
            else
            {
                e.Value = L.T("Warranty expired");
                e.CellStyle.ForeColor = Theme.Danger;
                e.CellStyle.Font = Theme.SmallBold;
            }
            e.FormattingApplied = true;
        }
    }
}
