using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>Customers and the due ledger (বাকি খাতা): balances, payments, per-customer history.</summary>
    public class CustomersView : UserControl, IRefreshable
    {
        private readonly MainForm _main;
        private readonly CustomerRepository _repo = new CustomerRepository();
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _count;
        private readonly Button _receive;
        private readonly Button _ledger;
        private readonly Button _edit;
        private readonly Button _delete;
        private readonly Timer _searchDelay;

        public CustomersView(MainForm main)
        {
            _main = main;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            heading.Controls.Add(Theme.PageTitle("Customers & dues"));
            _count = Theme.Caption("");
            heading.Controls.Add(_count);

            var actions = new WrapFlowPanel
            {
                AutoSize = true, WrapContents = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(24, 8, 0, 0)
            };
            var add = Theme.PrimaryButton("+  Add Customer");
            _receive = Theme.PrimaryButton("Receive payment");
            _receive.BackColor = Theme.Success;
            _receive.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 128, 61);
            _receive.FlatAppearance.MouseDownBackColor = _receive.FlatAppearance.MouseOverBackColor;
            _ledger = Theme.SecondaryButton("Ledger");
            _edit = Theme.SecondaryButton("Edit");
            _delete = Theme.DangerButton("Delete");
            actions.Controls.AddRange(new Control[] { _delete, _edit, _ledger, _receive, add });

            titleRow.Controls.Add(heading, 0, 0);
            titleRow.Controls.Add(actions, 1, 0);

            var filterCard = new CardPanel { Dock = DockStyle.Fill, Height = 64, Padding = new Padding(16, 14, 16, 12), Margin = new Padding(0, 0, 0, 12) };
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };
            filters.Controls.Add(new Label { Text = L.T("Search"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 9, 8, 0) });
            _search = new TextBox { Width = 300, Font = Theme.Body, Margin = new Padding(0, 4, 0, 0) };
            ProductsView.SetCueBanner(_search, "Name, phone or address...");
            filters.Controls.Add(_search);
            filterCard.Controls.Add(filters);

            var gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("Name", "Customer", 140));
            _grid.Columns.Add(DashboardView.Col("Phone", "Phone", 90));
            _grid.Columns.Add(DashboardView.Col("Due", "Due", 80, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("LastActivity", "Last activity", 95));
            _grid.Columns["LastActivity"].DefaultCellStyle.Format = "dd/MM/yy";
            _grid.Columns.Add(DashboardView.Col("Address", "Address", 150));
            gridCard.Controls.Add(_grid);

            layout.Controls.Add(titleRow, 0, 0);
            layout.Controls.Add(filterCard, 0, 1);
            layout.Controls.Add(gridCard, 0, 2);
            Controls.Add(layout);

            add.Click += (s, e) => AddCustomer();
            _receive.Click += (s, e) => ReceivePayment();
            _ledger.Click += (s, e) => ShowLedger();
            _edit.Click += (s, e) => EditSelected();
            _delete.Click += (s, e) => DeleteSelected();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowLedger(); };
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; } };
            _grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var c = _grid.Rows[e.RowIndex].DataBoundItem as Customer;
                if (c == null) return;
                if (_grid.Columns[e.ColumnIndex].Name == "Due")
                {
                    e.Value = Theme.Money(c.Due);
                    e.FormattingApplied = true;
                    e.CellStyle.Font = Theme.Num(10f, true);
                    e.CellStyle.ForeColor = c.Due > 0.004m ? Theme.Danger : Theme.TextSecondary;
                }
            };
            _searchDelay = new Timer { Interval = 300 };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); LoadList(0); };
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Shortcuts.Is(Shortcuts.New, keyData)) { AddCustomer(); return true; }
            if (_search != null && Shortcuts.Is(Shortcuts.Search, keyData)) { _search.Focus(); _search.SelectAll(); return true; }
            if (Shortcuts.Is(Shortcuts.Refresh, keyData)) { RefreshData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void RefreshData()
        {
            LoadList(0);
        }

        private Customer Selected
        {
            get { return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as Customer : null; }
        }

        private void LoadList(long selectId)
        {
            if (selectId == 0 && Selected != null) selectId = Selected.Id;
            var list = _repo.GetAll(_search.Text);
            _grid.DataSource = list;
            _grid.ClearSelection();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (((Customer)row.DataBoundItem).Id == selectId)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
            var totalDue = list.Sum(c => Math.Max(0, c.Due));
            _count.Text = L.F("{0} customer(s)", L.Num(list.Count)) + "  ·  " + L.F("total due {0}", Theme.Money(totalDue));
            _main.SetStatus(L.F("{0} customer(s)", L.Num(list.Count)));
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = Selected != null;
            _receive.Enabled = _ledger.Enabled = _edit.Enabled = _delete.Enabled = has;
            _delete.Visible = Session.IsOwner;
        }

        private void AddCustomer()
        {
            using (var dlg = new CustomerEditForm(null))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadList(dlg.Customer.Id);
                    _main.SetStatus(L.F("Customer \"{0}\" added.", dlg.Customer.Name));
                }
            }
        }

        private void EditSelected()
        {
            var c = Selected;
            if (c == null) return;
            using (var dlg = new CustomerEditForm(_repo.GetById(c.Id) ?? c))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    LoadList(c.Id);
            }
        }

        private void ReceivePayment()
        {
            var c = Selected;
            if (c == null) return;
            using (var dlg = new ReceivePaymentForm(_repo.GetById(c.Id) ?? c))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadList(c.Id);
                    _main.SetStatus(L.F("Payment received. New due of \"{0}\": {1}", c.Name, Theme.Money(dlg.NewBalance)));
                }
            }
        }

        private void ShowLedger()
        {
            var c = Selected;
            if (c == null) return;
            using (var dlg = new CustomerLedgerForm(_repo.GetById(c.Id) ?? c))
            {
                dlg.ShowDialog(FindForm());
            }
            LoadList(c.Id);
        }

        private void DeleteSelected()
        {
            var c = Selected;
            if (c == null || !Session.IsOwner) return;
            if (Msg.Show(FindForm(), L.F("Delete customer \"{0}\"? Their sales history is kept.", c.Name),
                    "Delete Customer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                _repo.Delete(c.Id);
                LoadList(0);
            }
            catch (InvalidOperationException ex)
            {
                Msg.Show(FindForm(), ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Add / edit a customer. Also used by the POS quick-add.</summary>
    public class CustomerEditForm : Form
    {
        private readonly CustomerRepository _repo = new CustomerRepository();
        private readonly bool _isNew;
        private readonly ErrorProvider _errors;
        private readonly TextBox _name;
        private readonly TextBox _phone;
        private readonly TextBox _address;
        private readonly TextBox _notes;

        public Customer Customer { get; private set; }

        public CustomerEditForm(Customer customer)
        {
            _isNew = customer == null;
            Customer = customer ?? new Customer();

            Text = L.T(_isNew ? "Add Customer" : "Edit Customer");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 390);
            _errors = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink, ContainerControl = this };

            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = L.T(_isNew ? "New Customer" : "Edit Customer"), Font = Theme.Semi(13f), AutoSize = true, Location = new Point(24, 14) });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 14, 36, 8), BackColor = Theme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            _name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
            _phone = new TextBox { Dock = DockStyle.Fill, MaxLength = 40 };
            _address = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 300, ScrollBars = ScrollBars.Vertical };
            _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 500, ScrollBars = ScrollBars.Vertical };

            Field(grid, "Customer name *", _name, 0, 0, 1, 62);
            Field(grid, "Phone", _phone, 1, 0, 1, 62);
            Field(grid, "Address", _address, 0, 1, 2, 78);
            Field(grid, "Notes", _notes, 0, 2, 2, 78);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
            var save = Theme.PrimaryButton(_isNew ? "Add Customer" : "Save Changes");
            var cancel = Theme.SecondaryButton("Cancel");
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

            _name.Text = Customer.Name;
            _phone.Text = Customer.Phone;
            _address.Text = Customer.Address;
            _notes.Text = Customer.Notes;
            Shown += (s, e) => _name.Focus();
        }

        internal static void Field(TableLayoutPanel grid, string label, Control input, int col, int row, int span, int height)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Height = height, Margin = new Padding(0, 0, 12, 4) };
            cell.Controls.Add(input);
            cell.Controls.Add(new Label { Text = L.T(label), Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextSecondary, Font = Theme.Small });
            input.Font = Theme.Body;
            grid.Controls.Add(cell, col, row);
            if (span > 1) grid.SetColumnSpan(cell, span);
        }

        private void Save()
        {
            _errors.Clear();
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _errors.SetError(_name, L.T("Customer name is required."));
                _name.Focus();
                return;
            }
            Customer.Name = _name.Text.Trim();
            Customer.Phone = _phone.Text.Trim();
            Customer.Address = _address.Text.Trim();
            Customer.Notes = _notes.Text.Trim();
            if (_isNew) _repo.Insert(Customer);
            else _repo.Update(Customer);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Collects money against a customer's due (টাকা জমা).</summary>
    public class ReceivePaymentForm : Form
    {
        private readonly CustomerRepository _repo = new CustomerRepository();
        private readonly Customer _customer;
        private readonly NumericUpDown _amount;
        private readonly ComboBox _method;
        private readonly TextBox _note;

        public decimal NewBalance { get; private set; }

        public ReceivePaymentForm(Customer customer)
        {
            _customer = customer;
            Text = L.T("Receive payment");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 300);

            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = customer.Name, Font = Theme.Semi(13f), AutoSize = true, Location = new Point(24, 12), UseMnemonic = false });
            header.Controls.Add(new Label
            {
                Text = L.F("Current due: {0}", Theme.Money(customer.Due)),
                ForeColor = customer.Due > 0 ? Theme.Danger : Theme.TextSecondary,
                Font = Theme.BodyBold,
                AutoSize = true,
                Location = new Point(26, 42),
                UseMnemonic = false
            });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 14, 36, 8), BackColor = Theme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _amount = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Font = Theme.Num(11f) };
            if (customer.Due > 0) _amount.Value = Math.Min(_amount.Maximum, customer.Due);
            _method = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in PayMethod.All) _method.Items.Add(PayMethod.Display(m));
            _method.SelectedIndex = 0;
            _note = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };

            CustomerEditForm.Field(grid, "Amount *", _amount, 0, 0, 1, 62);
            CustomerEditForm.Field(grid, "Method", _method, 1, 0, 1, 62);
            CustomerEditForm.Field(grid, "Note", _note, 0, 1, 2, 62);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
            var save = Theme.PrimaryButton("Receive payment");
            save.BackColor = Theme.Success;
            save.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 128, 61);
            save.FlatAppearance.MouseDownBackColor = save.FlatAppearance.MouseOverBackColor;
            var cancel = Theme.SecondaryButton("Cancel");
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
            Shown += (s, e) => { _amount.Focus(); _amount.Select(0, _amount.Text.Length); };
        }

        private void Save()
        {
            if (_amount.Value <= 0)
            {
                Msg.Show(this, L.T("Please enter a valid amount."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            NewBalance = _repo.AddPayment(_customer.Id, _amount.Value, PayMethod.All[_method.SelectedIndex], _note.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>A customer's full ledger with a printable due slip.</summary>
    public class CustomerLedgerForm : Form
    {
        public CustomerLedgerForm(Customer customer)
        {
            Text = L.T("Ledger") + " - " + customer.Name;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 480);
            MinimumSize = new Size(560, 360);

            var entries = new CustomerRepository().GetLedger(customer.Id);

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = customer.DisplayName, Font = Theme.Semi(13f), AutoSize = true, Location = new Point(24, 10), UseMnemonic = false });
            header.Controls.Add(new Label
            {
                Text = L.F("Current due: {0}", Theme.Money(customer.Due)),
                ForeColor = customer.Due > 0 ? Theme.Danger : Theme.Success,
                Font = Theme.BodyBold,
                AutoSize = true,
                Location = new Point(26, 38),
                UseMnemonic = false
            });

            var grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(grid);
            grid.Columns.Add(DashboardView.Col("Date", "Date & Time", 95));
            grid.Columns["Date"].DefaultCellStyle.Format = "dd/MM/yy, hh:mm tt";
            grid.Columns.Add(DashboardView.Col("Kind", "Type", 55));
            grid.Columns.Add(DashboardView.Col("Reference", "Invoice", 75));
            grid.Columns.Add(DashboardView.Col("Charge", "Bill", 70, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(DashboardView.Col("Credit", "Paid", 70, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(DashboardView.Col("Balance", "Due", 70, DataGridViewContentAlignment.MiddleRight));
            grid.DataSource = entries;
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= entries.Count) return;
                var row = entries[e.RowIndex];
                switch (grid.Columns[e.ColumnIndex].Name)
                {
                    case "Kind":
                        e.Value = L.T(row.Kind == "PAYMENT" ? "Payment" : row.Kind == "RETURN" ? "Return" : "Sale");
                        e.FormattingApplied = true;
                        e.CellStyle.ForeColor = row.Kind == "PAYMENT" ? Theme.Success : row.Kind == "RETURN" ? Theme.Info : Theme.TextPrimary;
                        break;
                    case "Charge":
                        e.Value = row.Charge == 0 ? "" : Theme.Money(row.Charge);
                        e.FormattingApplied = true;
                        break;
                    case "Credit":
                        e.Value = row.Credit == 0 ? "" : Theme.Money(row.Credit);
                        e.FormattingApplied = true;
                        e.CellStyle.ForeColor = Theme.Success;
                        break;
                    case "Balance":
                        e.Value = Theme.Money(row.Balance);
                        e.FormattingApplied = true;
                        e.CellStyle.Font = Theme.Num(10f, true);
                        e.CellStyle.ForeColor = row.Balance > 0.004m ? Theme.Danger : Theme.TextSecondary;
                        break;
                }
            };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            var print = Theme.SecondaryButton("Print due slip");
            var close = Theme.PrimaryButton("Close");
            footer.Controls.Add(print);
            footer.Controls.Add(close);
            footer.Layout += (s, e) =>
            {
                close.Location = new Point(footer.Width - close.Width - 24, 13);
                print.Location = new Point(close.Left - print.Width - 8, 13);
            };
            close.DialogResult = DialogResult.OK;
            CancelButton = close;
            print.Click += (s, e) =>
            {
                try
                {
                    Printing.ReceiptPrinter.PrintDueSlip(customer, entries);
                }
                catch (Exception ex)
                {
                    Msg.Show(this, L.T("Could not print.") + "\n\n" + ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(header);
        }
    }
}
