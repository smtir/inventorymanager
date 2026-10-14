using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>
    /// Returns items from a sale invoice: pick quantities per line, see the refund split
    /// (due reduced first, rest in cash), then confirm.
    /// </summary>
    public class ReturnForm : Form
    {
        private readonly TransactionRepository _repo = new TransactionRepository();
        private readonly string _invoiceNo;
        private readonly List<Tuple<StockTransaction, NumericUpDown>> _rows = new List<Tuple<StockTransaction, NumericUpDown>>();
        private readonly Label _summary;
        private readonly ComboBox _method;
        private readonly TextBox _note;
        private readonly CheckBox _print;
        private readonly Button _save;

        public string ReturnInvoiceNo { get; private set; }
        public bool PrintSlip { get; private set; }

        public ReturnForm(string invoiceNo)
        {
            _invoiceNo = invoiceNo;
            Text = L.T("Return items") + " - " + invoiceNo;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;

            var header = _repo.GetInvoiceHeader(invoiceNo);
            var lines = _repo.GetInvoice(invoiceNo).Where(t => t.Type == TransactionType.Sale).ToList();
            var returned = _repo.GetReturnedQuantities(invoiceNo);

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            top.Controls.Add(new Label
            {
                Text = L.F("Invoice {0}  ·  {1}", invoiceNo, header != null ? L.DateTimeText(header.CreatedAt) : ""),
                Font = Theme.Semi(12f), AutoSize = true, Location = new Point(24, 10), UseMnemonic = false
            });
            top.Controls.Add(new Label
            {
                Text = (header != null && !string.IsNullOrEmpty(header.CustomerName) ? L.T("Customer:") + " " + header.CustomerName + "   ·   " : "") +
                       L.T("Choose how many of each item came back."),
                ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 38), UseMnemonic = false
            });

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(24, 12, 36, 4), BackColor = Theme.Surface, AutoScroll = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            int rowIndex = 0;
            foreach (var t in lines)
            {
                int already;
                returned.TryGetValue(t.Id, out already);
                int max = t.Quantity - already;
                var name = new Label
                {
                    Text = t.ProductName, AutoSize = true, Margin = new Padding(0, 8, 8, 4),
                    UseMnemonic = false, MaximumSize = new Size(300, 0)
                };
                var info = new Label
                {
                    Text = L.F("sold {0}", L.Num(t.Quantity)) + (already > 0 ? "  ·  " + L.F("returned {0}", L.Num(already)) : ""),
                    AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Small, Margin = new Padding(0, 11, 8, 4)
                };
                var qty = new NumericUpDown
                {
                    Width = 80, Minimum = 0, Maximum = Math.Max(0, max), Value = 0,
                    TextAlign = HorizontalAlignment.Right, Font = Theme.Num(10f), Margin = new Padding(0, 6, 0, 4),
                    Enabled = max > 0
                };
                qty.ValueChanged += (s, e) => UpdatePreview();
                body.Controls.Add(name, 0, rowIndex);
                body.Controls.Add(info, 1, rowIndex);
                body.Controls.Add(qty, 2, rowIndex);
                _rows.Add(Tuple.Create(t, qty));
                rowIndex++;
            }

            _summary = new Label { Dock = DockStyle.Bottom, Height = 44, Font = Theme.BodyBold, ForeColor = Theme.TextPrimary, Padding = new Padding(24, 4, 24, 0), UseMnemonic = false };

            var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(20, 2, 0, 0), BackColor = Theme.Surface, WrapContents = false };
            options.Controls.Add(new Label { Text = L.T("Refund by"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(4, 9, 8, 0) });
            _method = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in PayMethod.All) _method.Items.Add(PayMethod.Display(m));
            _method.SelectedIndex = 0;
            options.Controls.Add(_method);
            options.Controls.Add(new Label { Text = L.T("Reason"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(16, 9, 8, 0) });
            _note = new TextBox { Width = 180, MaxLength = 200, Margin = new Padding(0, 5, 0, 0) };
            options.Controls.Add(_note);
            _print = new CheckBox { Text = L.T("Print return slip"), AutoSize = true, Margin = new Padding(16, 8, 0, 0) };
            options.Controls.Add(_print);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            _save = Theme.DangerButton("Return items");
            var cancel = Theme.SecondaryButton("Cancel");
            footer.Controls.Add(_save);
            footer.Controls.Add(cancel);
            footer.Layout += (s, e) =>
            {
                _save.Location = new Point(footer.Width - _save.Width - 24, 13);
                cancel.Location = new Point(_save.Left - cancel.Width - 8, 13);
            };

            ClientSize = new Size(720, Math.Min(560, 250 + rowIndex * 40));
            Controls.Add(body);
            Controls.Add(options);
            Controls.Add(_summary);
            Controls.Add(footer);
            Controls.Add(top);
            body.BringToFront();
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            _save.Click += (s, e) => Save();
            UpdatePreview();
        }

        private List<Tuple<long, int>> SelectedItems()
        {
            return _rows.Where(r => r.Item2.Value > 0)
                        .Select(r => Tuple.Create(r.Item1.Id, (int)r.Item2.Value))
                        .ToList();
        }

        private void UpdatePreview()
        {
            var items = SelectedItems();
            if (items.Count == 0)
            {
                _summary.Text = L.T("Nothing selected yet.");
                _summary.ForeColor = Theme.TextSecondary;
                _save.Enabled = false;
                return;
            }
            try
            {
                var p = _repo.PreviewReturn(_invoiceNo, items);
                _summary.Text = p.DueReduced > 0.004m
                    ? L.F("Refund {0}:  {1} reduced from due, {2} given back", Theme.Money(p.Refund), Theme.Money(p.DueReduced), Theme.Money(p.CashBack))
                    : L.F("Refund {0} given back to the customer", Theme.Money(p.Refund));
                _summary.ForeColor = Theme.TextPrimary;
                _save.Enabled = true;
            }
            catch (Exception ex)
            {
                _summary.Text = ex.Message;
                _summary.ForeColor = Theme.Danger;
                _save.Enabled = false;
            }
        }

        private void Save()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;
            try
            {
                ReturnInvoiceNo = _repo.ReturnItems(_invoiceNo, items, PayMethod.All[_method.SelectedIndex], _note.Text);
            }
            catch (Exception ex)
            {
                Msg.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdatePreview();
                return;
            }
            PrintSlip = _print.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
