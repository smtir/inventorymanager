using System;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>
    /// Corrects a product's stock (damage, loss, counting mistakes) with a reason,
    /// recorded in history instead of silently editing the quantity.
    /// </summary>
    public class AdjustStockForm : Form
    {
        private readonly Product _product;
        private readonly ComboBox _mode;
        private readonly NumericUpDown _qty;
        private readonly ComboBox _reason;
        private readonly Label _preview;
        private readonly Button _save;

        public AdjustStockForm(Product product)
        {
            _product = product;
            Text = L.T("Adjust stock");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 330);

            var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = product.Name, Font = Theme.Semi(12.5f), AutoSize = true, Location = new Point(24, 12), UseMnemonic = false, MaximumSize = new Size(390, 0), AutoEllipsis = true });
            header.Controls.Add(new Label { Text = L.F("Current stock: {0}", L.Num(product.Quantity)), Font = Theme.BodyBold, ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 40), UseMnemonic = false });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 14, 36, 4), BackColor = Theme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            _mode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _mode.Items.Add(L.T("Decrease stock (damage / loss)"));
            _mode.Items.Add(L.T("Increase stock"));
            _mode.Items.Add(L.T("Set the exact counted quantity"));
            _mode.SelectedIndex = 0;
            _qty = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 10000000, Minimum = 0, Value = Math.Min(1, Math.Max(0, product.Quantity)), TextAlign = HorizontalAlignment.Right, Font = Theme.Num(11f) };
            _reason = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 200 };
            _reason.Items.AddRange(new object[] { L.T("Damaged"), L.T("Lost"), L.T("Count correction"), L.T("Own use") });
            _reason.Text = L.T("Damaged");

            CustomerEditForm.Field(grid, "What happened?", _mode, 0, 0, 2, 62);
            CustomerEditForm.Field(grid, "Quantity", _qty, 0, 1, 1, 62);
            CustomerEditForm.Field(grid, "Reason *", _reason, 1, 1, 1, 62);

            _preview = new Label { Dock = DockStyle.Top, Height = 30, Font = Theme.BodyBold, Padding = new Padding(24, 2, 24, 0), UseMnemonic = false };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            _save = Theme.PrimaryButton("Save adjustment");
            var cancel = Theme.SecondaryButton("Cancel");
            footer.Controls.Add(_save);
            footer.Controls.Add(cancel);
            footer.Layout += (s, e) =>
            {
                _save.Location = new Point(footer.Width - _save.Width - 24, 13);
                cancel.Location = new Point(_save.Left - cancel.Width - 8, 13);
            };

            Controls.Add(grid);
            Controls.Add(_preview);
            Controls.Add(footer);
            Controls.Add(header);
            grid.BringToFront();
            AcceptButton = _save;
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            _save.Click += (s, e) => Save();
            _mode.SelectedIndexChanged += (s, e) => UpdatePreview();
            _qty.ValueChanged += (s, e) => UpdatePreview();
            _qty.KeyUp += (s, e) => UpdatePreview();
            UpdatePreview();
        }

        private int Delta
        {
            get
            {
                int q = (int)_qty.Value;
                switch (_mode.SelectedIndex)
                {
                    case 1: return q;
                    case 2: return q - _product.Quantity;
                    default: return -q;
                }
            }
        }

        private void UpdatePreview()
        {
            int delta = Delta;
            int after = _product.Quantity + delta;
            if (delta == 0)
            {
                _preview.Text = L.T("The adjustment is zero - nothing to change.");
                _preview.ForeColor = Theme.TextMuted;
                _save.Enabled = false;
            }
            else if (after < 0)
            {
                _preview.Text = L.T("Stock cannot go below zero.");
                _preview.ForeColor = Theme.Danger;
                _save.Enabled = false;
            }
            else
            {
                _preview.Text = L.F("Stock: {0}  →  {1}   ({2})", L.Num(_product.Quantity), L.Num(after),
                    (delta > 0 ? "+" : "−") + L.Num(Math.Abs(delta)));
                _preview.ForeColor = delta < 0 ? Theme.Warning : Theme.Success;
                _save.Enabled = true;
            }
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(_reason.Text))
            {
                Msg.Show(this, L.T("Please give a reason for the adjustment."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _reason.Focus();
                return;
            }
            try
            {
                new TransactionRepository().AdjustStockLevel(_product.Id, Delta, _reason.Text);
            }
            catch (Exception ex)
            {
                Msg.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
