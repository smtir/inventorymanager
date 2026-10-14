using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;
using InventoryManager.Scanner;

namespace InventoryManager.UI
{
    /// <summary>
    /// Modal dialog used for both adding and editing a product.
    /// While open it receives phone scans (IScanTarget) into the Barcode field.
    /// </summary>
    public class ProductEditForm : Form, IScanTarget
    {
        /// <summary>The product form currently on screen, if any (scans are routed to it).</summary>
        public static ProductEditForm Current { get; private set; }

        private readonly ProductRepository _repo = new ProductRepository();
        private readonly bool _isNew;
        private readonly ErrorProvider _errors;
        private readonly TextBox _code;
        private readonly TextBox _barcode;
        private readonly TextBox _name;
        private readonly ComboBox _category;
        private readonly NumericUpDown _price;
        private readonly NumericUpDown _cost;
        private readonly ComboBox _vendor;
        private readonly Label _profit;
        private readonly NumericUpDown _quantity;
        private readonly NumericUpDown _reorder;
        private readonly NumericUpDown _warranty;
        private readonly TextBox _description;

        public Product Product { get; private set; }

        public ProductEditForm(Product product, IEnumerable<string> categories, string initialBarcode = null,
            string initialName = null, long? initialVendorId = null)
        {
            _isNew = product == null;
            Product = product ?? new Product { ReorderLevel = AppSettings.DefaultReorderLevel };

            Text = L.T(_isNew ? "Add Product" : "Edit Product");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 730);

            _errors = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink, ContainerControl = this };

            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Background };
            header.Controls.Add(new Label
            {
                Text = L.T(_isNew ? "New Product" : "Edit Product"),
                Font = Theme.Semi(13f),
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Location = new Point(24, 12)
            });
            header.Controls.Add(new Label
            {
                Text = L.T(_isNew ? "Fill in the details below. Fields marked * are required." : "Update the product details and save."),
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Location = new Point(26, 40)
            });

            // Fields
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(24, 20, 36, 8),
                BackColor = Theme.Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _code = new TextBox { Dock = DockStyle.Fill, MaxLength = 30, CharacterCasing = CharacterCasing.Upper };
            _name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
            _barcode = new ScanTextBox { Dock = DockStyle.Fill, MaxLength = 64 };
            ProductsView.SetCueBanner(_barcode, "Scan or type barcode");
            _category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 60 };
            _category.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _category.AutoCompleteSource = AutoCompleteSource.ListItems;
            foreach (var c in categories) _category.Items.Add(c);

            _price = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
            _cost = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
            _vendor = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _vendor.Items.Add(L.T("(none)"));
            foreach (var v in new VendorRepository().GetAll()) _vendor.Items.Add(new VendorChoice(v));
            _profit = new Label { Dock = DockStyle.Fill, Height = 34, Font = Theme.BodyBold, TextAlign = ContentAlignment.TopLeft, UseMnemonic = false, Margin = new Padding(0, 0, 12, 6) };
            _quantity = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 10000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
            _reorder = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 10000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
            _warranty = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 120, TextAlign = HorizontalAlignment.Right };
            _description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 1000, Height = 80 };

            AddField(grid, "Product Code *", _code, 0, 0);
            AddField(grid, "Category", _category, 1, 0);
            AddField(grid, "Product Name *", _name, 0, 1, 2);
            AddField(grid, L.T("Purchase Rate") + " (" + Theme.CurrencySymbol + ")", _cost, 0, 2);
            AddField(grid, L.T("Sale Price") + " (" + Theme.CurrencySymbol + ") *", _price, 1, 2);
            grid.Controls.Add(_profit, 0, 3);
            grid.SetColumnSpan(_profit, 2);
            AddField(grid, "Quantity in Stock", _quantity, 0, 4);
            AddField(grid, "Reorder Level", _reorder, 1, 4);
            AddField(grid, "Barcode", _barcode, 0, 5);
            AddField(grid, "Vendor", _vendor, 1, 5);
            AddField(grid, "Warranty (months, 0 = none)", _warranty, 0, 6);
            AddField(grid, "Description", _description, 0, 7, 2);

            // Footer
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
            var save = Theme.PrimaryButton(_isNew ? "Add Product" : "Save Changes");
            var cancel = Theme.SecondaryButton("Cancel");
            save.Anchor = cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Layout += (s, e) =>
            {
                save.Location = new Point(footer.Width - save.Width - 24, 15);
                cancel.Location = new Point(save.Left - cancel.Width - 8, 15);
            };

            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = save;
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            save.Click += (s, e) => Save();

            // Load values
            _code.Text = Product.Code;
            _name.Text = Product.Name;
            _category.Text = Product.Category;
            _price.Value = Clamp(Product.Price, _price);
            _quantity.Value = Clamp(Product.Quantity, _quantity);
            _reorder.Value = Clamp(Product.ReorderLevel, _reorder);
            _description.Text = Product.Description;
            _barcode.Text = Product.Barcode;
            _cost.Value = Clamp(Product.PurchasePrice, _cost);
            _warranty.Value = Math.Max(_warranty.Minimum, Math.Min(_warranty.Maximum, Product.WarrantyMonths));
            if (!_isNew)
            {
                // Stock of an existing product changes through Stock In / Adjust stock, so history stays true.
                _quantity.Enabled = false;
            }
            if (!Session.IsOwner)
            {
                _cost.Parent.Visible = false;
                _profit.Visible = false;
            }
            SelectVendor(Product.VendorId ?? initialVendorId);
            _cost.ValueChanged += (s, e) => UpdateProfit();
            _price.ValueChanged += (s, e) => UpdateProfit();
            _cost.KeyUp += (s, e) => UpdateProfit();
            _price.KeyUp += (s, e) => UpdateProfit();
            UpdateProfit();

            if (_isNew) _code.Text = SuggestCode();
            if (!string.IsNullOrWhiteSpace(initialBarcode)) _barcode.Text = initialBarcode.Trim();
            if (!string.IsNullOrWhiteSpace(initialName)) _name.Text = initialName.Trim();

            // A USB scanner ends with Enter: move on instead of saving the form.
            _barcode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    SelectNextControl(_barcode, true, true, true, true);
                }
            };

            Shown += (s, e) =>
            {
                Current = this;
                (_isNew ? (Control)_name : _code).Focus();
            };
            FormClosed += (s, e) => { if (Current == this) Current = null; };
            Theme.ApplyNumberFonts(this);
        }

        private void SelectVendor(long? id)
        {
            _vendor.SelectedIndex = 0;
            if (!id.HasValue) return;
            for (int i = 1; i < _vendor.Items.Count; i++)
                if (((VendorChoice)_vendor.Items[i]).Vendor.Id == id.Value) { _vendor.SelectedIndex = i; return; }
        }

        /// <summary>Shows profit per unit and margin while the user types prices.</summary>
        private void UpdateProfit()
        {
            decimal cost = ReadNumber(_cost), price = ReadNumber(_price);
            if (cost <= 0)
            {
                _profit.Text = L.T("Enter the purchase rate to see the profit per unit.");
                _profit.ForeColor = Theme.TextSecondary;
                return;
            }
            var profit = price - cost;
            var margin = price > 0 ? profit / price * 100m : 0m;
            _profit.Text = L.F("Profit per unit:  {0}   ({1}% margin)", Theme.Money(profit), L.Digits(margin.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
            _profit.ForeColor = profit < 0 ? Theme.Danger : Color.FromArgb(13, 148, 136);
        }

        private static decimal ReadNumber(NumericUpDown n)
        {
            decimal v;
            return decimal.TryParse(n.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out v) ? v : n.Value;
        }

        private sealed class VendorChoice
        {
            public readonly Vendor Vendor;
            public VendorChoice(Vendor v) { Vendor = v; }
            public override string ToString() { return Vendor.Name; }
        }

        /// <summary>Phone scan while this form is open: put the code in the Barcode field.</summary>
        public ScanResult HandleScan(string code)
        {
            _barcode.Text = code;
            _errors.SetError(_barcode, L.T(""));
            _barcode.BackColor = Color.FromArgb(220, 252, 231);
            var t = new Timer { Interval = 700 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!_barcode.IsDisposed) _barcode.BackColor = SystemColors.Window; };
            t.Start();
            return new ScanResult
            {
                Ok = true,
                ProductName = string.IsNullOrWhiteSpace(_name.Text) ? L.T("Barcode captured") : _name.Text.Trim(),
                Message = L.T("Barcode set on the product form on the PC.")
            };
        }

        private static void AddField(TableLayoutPanel grid, string label, Control input, int col, int row, int colSpan = 1)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Height = input is TextBox && ((TextBox)input).Multiline ? 112 : 62, Margin = new Padding(0, 0, 12, 4) };
            var lbl = new Label { Text = L.T(label), Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextSecondary, Font = Theme.Small };
            cell.Controls.Add(input);
            cell.Controls.Add(lbl);
            input.Font = Theme.Body;
            grid.Controls.Add(cell, col, row);
            if (colSpan > 1) grid.SetColumnSpan(cell, colSpan);
        }

        private static decimal Clamp(decimal v, NumericUpDown n)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        private string SuggestCode()
        {
            for (int i = 1001; i < 1000000; i++)
            {
                var code = "P-" + i;
                if (!_repo.CodeExists(code)) return code;
            }
            return "";
        }

        private bool ValidateInputs()
        {
            _errors.Clear();
            bool ok = true;
            Control first = null;

            if (string.IsNullOrWhiteSpace(_code.Text))
            {
                _errors.SetError(_code, L.T("Product code is required."));
                ok = false; first = first ?? _code;
            }
            else if (_repo.CodeExists(_code.Text, Product.Id))
            {
                _errors.SetError(_code, L.T("This code is already used by another product."));
                ok = false; first = first ?? _code;
            }

            if (_repo.BarcodeExists(_barcode.Text, Product.Id))
            {
                _errors.SetError(_barcode, L.T("This barcode is already used by another product."));
                ok = false; first = first ?? _barcode;
            }

            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _errors.SetError(_name, L.T("Product name is required."));
                ok = false; first = first ?? _name;
            }

            if (_price.Value <= 0)
            {
                _errors.SetError(_price, L.T("Price must be greater than zero."));
                ok = false; first = first ?? _price;
            }

            if (first != null) first.Focus();
            return ok;
        }

        private void Save()
        {
            if (!ValidateInputs()) return;

            Product.Code = _code.Text.Trim();
            Product.Barcode = _barcode.Text.Trim();
            Product.PurchasePrice = Session.IsOwner ? _cost.Value : Product.PurchasePrice;
            Product.WarrantyMonths = (int)_warranty.Value;
            var vendor = _vendor.SelectedItem as VendorChoice;
            Product.VendorId = vendor != null ? vendor.Vendor.Id : (long?)null;
            Product.VendorName = vendor != null ? vendor.Vendor.Name : "";
            Product.Name = _name.Text.Trim();
            Product.Category = _category.Text.Trim();
            Product.Price = _price.Value;
            Product.Quantity = (int)_quantity.Value;
            Product.ReorderLevel = (int)_reorder.Value;
            Product.Description = _description.Text.Trim();

            try
            {
                if (_isNew) _repo.Insert(Product);
                else _repo.Update(Product);
            }
            catch (DuplicateCodeException ex)
            {
                _errors.SetError(_code, ex.Message);
                _code.Focus();
                return;
            }
            catch (DuplicateBarcodeException ex)
            {
                _errors.SetError(_barcode, ex.Message);
                _barcode.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>TextBox that receives Enter itself (instead of triggering the form's default button).</summary>
    public class ScanTextBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            return (keyData & Keys.KeyCode) == Keys.Enter || base.IsInputKey(keyData);
        }
    }
}
