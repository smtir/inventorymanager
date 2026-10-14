using System;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>Vendor (supplier) list: add, edit, delete, search.</summary>
    public class VendorsView : UserControl, IRefreshable
    {
        private readonly MainForm _main;
        private readonly VendorRepository _repo = new VendorRepository();
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _count;
        private readonly Button _edit;
        private readonly Button _delete;
        private readonly Timer _searchDelay;

        public VendorsView(MainForm main)
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
            heading.Controls.Add(Theme.PageTitle("Vendors"));
            _count = Theme.Caption("");
            heading.Controls.Add(_count);

            var actions = new WrapFlowPanel
            {
                AutoSize = true, WrapContents = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(24, 8, 0, 0)
            };
            var add = Theme.PrimaryButton("+  Add Vendor");
            _edit = Theme.SecondaryButton("Edit");
            _delete = Theme.DangerButton("Delete");
            _delete.Margin = new Padding(0);
            actions.Controls.AddRange(new Control[] { _delete, _edit, add });
            titleRow.Controls.Add(heading, 0, 0);
            titleRow.Controls.Add(actions, 1, 0);

            var filterCard = new CardPanel { Dock = DockStyle.Fill, Height = 64, Padding = new Padding(16, 14, 16, 12), Margin = new Padding(0, 0, 0, 12) };
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };
            filters.Controls.Add(new Label { Text = L.T("Search"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 9, 8, 0) });
            _search = new TextBox { Width = 300, Font = Theme.Body, Margin = new Padding(0, 4, 0, 0) };
            ProductsView.SetCueBanner(_search, "Name, contact, phone or address...");
            filters.Controls.Add(_search);
            filterCard.Controls.Add(filters);

            var gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("Name", "Vendor", 150));
            _grid.Columns.Add(DashboardView.Col("ContactPerson", "Contact Person", 110));
            _grid.Columns.Add(DashboardView.Col("Phone", "Phone", 95));
            _grid.Columns.Add(DashboardView.Col("Address", "Address", 170));
            _grid.Columns.Add(DashboardView.Col("ProductCount", "Products", 60, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("TotalPurchased", "Total Purchased", 100, DataGridViewContentAlignment.MiddleRight));
            gridCard.Controls.Add(_grid);

            layout.Controls.Add(titleRow, 0, 0);
            layout.Controls.Add(filterCard, 0, 1);
            layout.Controls.Add(gridCard, 0, 2);
            Controls.Add(layout);

            add.Click += (s, e) => AddVendor();
            _edit.Click += (s, e) => EditSelected();
            _delete.Click += (s, e) => DeleteSelected();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; } };
            _grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "TotalPurchased")
                {
                    e.Value = Theme.Money((decimal)e.Value);
                    e.FormattingApplied = true;
                }
            };
            _searchDelay = new Timer { Interval = 300 };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); LoadList(0); };
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Shortcuts.Is(Shortcuts.New, keyData)) { AddVendor(); return true; }
            if (_search != null && Shortcuts.Is(Shortcuts.Search, keyData)) { _search.Focus(); _search.SelectAll(); return true; }
            if (Shortcuts.Is(Shortcuts.Refresh, keyData)) { RefreshData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void RefreshData()
        {
            LoadList(0);
        }

        private Vendor Selected
        {
            get { return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as Vendor : null; }
        }

        private void LoadList(long selectId)
        {
            if (selectId == 0 && Selected != null) selectId = Selected.Id;
            var list = _repo.GetAll(_search.Text);
            _grid.DataSource = list;
            _grid.ClearSelection();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (((Vendor)row.DataBoundItem).Id == selectId)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
            _count.Text = L.F(list.Count == 1 ? "{0} vendor  ·  suppliers you purchase stock from" : "{0} vendors  ·  suppliers you purchase stock from", L.Num(list.Count));
            _main.SetStatus(L.F("{0} vendor(s)", L.Num(list.Count)));
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _edit.Enabled = _delete.Enabled = Selected != null;
            _delete.BackColor = Selected != null ? Theme.Danger : Color.FromArgb(252, 165, 165);
        }

        private void AddVendor()
        {
            using (var dlg = new VendorEditForm(null))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadList(dlg.Vendor.Id);
                    _main.SetStatus(L.F("Vendor \"{0}\" added.", dlg.Vendor.Name));
                }
            }
        }

        private void EditSelected()
        {
            var v = Selected;
            if (v == null) return;
            using (var dlg = new VendorEditForm(_repo.GetById(v.Id) ?? v))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadList(v.Id);
                    _main.SetStatus(L.F("Vendor \"{0}\" updated.", dlg.Vendor.Name));
                }
            }
        }

        private void DeleteSelected()
        {
            var v = Selected;
            if (v == null) return;
            var msg = L.F("Delete vendor \"{0}\"?", v.Name);
            if (v.ProductCount > 0)
                msg += "\n\n" + L.F("{0} product(s) have this as their vendor; they will have no vendor. Purchase history keeps the vendor name.", L.Num(v.ProductCount));
            if (Msg.Show(FindForm(), msg, "Delete Vendor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _repo.Delete(v.Id);
            LoadList(0);
            _main.SetStatus(L.F("Vendor \"{0}\" deleted.", v.Name));
        }
    }

    /// <summary>Add / edit a vendor.</summary>
    public class VendorEditForm : Form
    {
        private readonly VendorRepository _repo = new VendorRepository();
        private readonly bool _isNew;
        private readonly ErrorProvider _errors;
        private readonly TextBox _name;
        private readonly TextBox _contact;
        private readonly TextBox _phone;
        private readonly TextBox _address;
        private readonly TextBox _notes;

        public Vendor Vendor { get; private set; }

        public VendorEditForm(Vendor vendor)
        {
            _isNew = vendor == null;
            Vendor = vendor ?? new Vendor();

            Text = L.T(_isNew ? "Add Vendor" : "Edit Vendor");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 480);
            _errors = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink, ContainerControl = this };

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = L.T(_isNew ? "New Vendor" : "Edit Vendor"), Font = Theme.Semi(13f), AutoSize = true, Location = new Point(24, 10) });
            header.Controls.Add(new Label { Text = L.T("Supplier you purchase stock from."), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(26, 38) });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 16, 36, 8), BackColor = Theme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
            _contact = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
            _phone = new TextBox { Dock = DockStyle.Fill, MaxLength = 60 };
            _address = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 300, ScrollBars = ScrollBars.Vertical };
            _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 500, ScrollBars = ScrollBars.Vertical };

            Field(grid, "Vendor / Company Name *", _name, 0, 0, 2, 62);
            Field(grid, "Contact Person", _contact, 0, 1, 1, 62);
            Field(grid, "Phone", _phone, 1, 1, 1, 62);
            Field(grid, "Address", _address, 0, 2, 2, 86);
            Field(grid, "Notes", _notes, 0, 3, 2, 86);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
            var save = Theme.PrimaryButton(_isNew ? "Add Vendor" : "Save Changes");
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

            _name.Text = Vendor.Name;
            _contact.Text = Vendor.ContactPerson;
            _phone.Text = Vendor.Phone;
            _address.Text = Vendor.Address;
            _notes.Text = Vendor.Notes;
            Shown += (s, e) => _name.Focus();
        }

        private static void Field(TableLayoutPanel grid, string label, Control input, int col, int row, int span, int height)
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
                _errors.SetError(_name, L.T("Vendor name is required."));
                _name.Focus();
                return;
            }

            Vendor.Name = _name.Text.Trim();
            Vendor.ContactPerson = _contact.Text.Trim();
            Vendor.Phone = _phone.Text.Trim();
            Vendor.Address = _address.Text.Trim();
            Vendor.Notes = _notes.Text.Trim();
            if (_isNew) _repo.Insert(Vendor);
            else _repo.Update(Vendor);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
