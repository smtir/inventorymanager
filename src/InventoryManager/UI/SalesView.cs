using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;
using InventoryManager.Printing;
using InventoryManager.Scanner;

namespace InventoryManager.UI
{
    /// <summary>
    /// One screen per mode:
    ///   Sales (POS)  - scan/search products into a cart and check out; history with profit.
    ///   Stock In     - receive goods from a vendor at a purchase cost; purchase history.
    /// Receives barcodes from the phone scanner (IScanTarget), USB scanners and the keyboard.
    /// </summary>
    public class SalesView : UserControl, IRefreshable, IScanTarget
    {
        private static readonly string[] Periods = { "Today", "Last 7 days", "Last 30 days", "This month", "All time" };
        private static readonly Color SaleHover = Color.FromArgb(21, 128, 61);
        private static string NoVendor { get { return L.T("(no vendor)"); } }

        private readonly MainForm _main;
        private readonly bool _isSale;
        private readonly ProductRepository _products = new ProductRepository();
        private readonly TransactionRepository _transactions = new TransactionRepository();
        private readonly VendorRepository _vendors = new VendorRepository();

        // Header / tabs
        private readonly Button _tabPos;
        private readonly Button _tabHistory;
        private readonly Panel _posPage;
        private readonly Panel _historyPage;
        private readonly Label _phoneStatus;

        // POS / receive
        private readonly ComboBox _vendor;
        private readonly TextBox _scan;
        private readonly Label _notice;
        private readonly Timer _noticeTimer;
        private readonly DataGridView _cartGrid;
        private readonly BindingList<CartLine> _cart = new BindingList<CartLine>();
        private readonly Stack<long> _scanHistory = new Stack<long>();
        private readonly Label _itemsLabel;
        private readonly StatValueLabel _totalLabel;
        private readonly Label _profitLabel;
        private readonly TextBox _note;
        private readonly CheckBox _printReceipt;
        private readonly Button _checkoutButton;
        private readonly Label _keysHint;
        private readonly Label _lastInvoice;
        private readonly LinkLabel _reprintLast;
        private string _lastInvoiceNo;
        private readonly CustomerRepository _customerRepo = new CustomerRepository();
        private ComboBox _customer;          // sale only
        private NumericUpDown _discount;     // sale only
        private NumericUpDown _paid;         // sale only
        private ComboBox _method;            // sale only
        private Label _dueLabel;             // sale only
        private Button _returnButton;        // history, sale only
        private bool _paidTouched;
        private bool _settingPaid;
        private List<Customer> _customerList = new List<Customer>();
        private bool _keyHooked;
        private List<Product> _productCache = new List<Product>();
        private Dictionary<string, Product> _byDisplayName = new Dictionary<string, Product>(StringComparer.CurrentCultureIgnoreCase);

        // History
        private readonly Label _stat1;
        private readonly Label _stat2;
        private readonly Label _stat3;
        private readonly Label _stat4;
        private readonly ComboBox _period;
        private readonly TextBox _search;
        private readonly DataGridView _grid;
        private readonly Button _undoButton;
        private readonly Button _reprintButton;
        private readonly Timer _searchDelay;

        /// <summary>Set before navigating here to add a product to the cart (e.g. from the Products screen).</summary>
        public long PendingProductId { get; set; }

        public SalesView(MainForm main, bool isSale)
        {
            _main = main;
            _isSale = isSale;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            Color accent = isSale ? Theme.Success : Theme.Accent;

            // ================= Header =================
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 2, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            heading.Controls.Add(Theme.PageTitle(isSale ? "Sales (POS)" : "Stock In"));
            heading.Controls.Add(Theme.Caption(isSale
                ? "Scan or search products, then check out. Stock goes down automatically."
                : "Receive goods from a vendor: scan items, enter purchase cost, then save. Stock goes up."));

            var headerRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, 6, 0, 0) };
            _phoneStatus = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 10, 8, 0) };
            var phoneButton = Theme.SecondaryButton("Phone Scanner");
            phoneButton.Margin = new Padding(0, 0, 16, 0);
            _tabPos = TabButton(isSale ? "POS" : "Receive");
            _tabHistory = TabButton("History");
            headerRight.Controls.AddRange(new Control[] { _phoneStatus, phoneButton, _tabPos, _tabHistory });

            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(headerRight, 1, 0);

            var pages = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
            _posPage = new Panel { Dock = DockStyle.Fill };
            _historyPage = new Panel { Dock = DockStyle.Fill, Visible = false };
            pages.Controls.Add(_historyPage);
            pages.Controls.Add(_posPage);

            Controls.Add(pages);
            Controls.Add(header);

            // ================= POS / receive page =================
            var pos = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            pos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 12, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var scanCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0) };
            var scanLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface };
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scanLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            scanLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var topBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            if (!isSale)
            {
                topBar.Controls.Add(new Label { Text = L.T("Vendor"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 9, 8, 0) });
                _vendor = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Margin = new Padding(0, 4, 8, 0) };
                topBar.Controls.Add(_vendor);
                var manageVendors = new LinkLabel { Text = L.T("Manage vendors"), AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(0, 9, 16, 0) };
                manageVendors.LinkClicked += (s, e) => _main.Navigate("Vendors");
                topBar.Controls.Add(manageVendors);
            }
            _keysHint = new Label { AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Small, Margin = new Padding(0, 10, 0, 0) };
            topBar.Controls.Add(_keysHint);
            scanLayout.Controls.Add(topBar, 0, 0);
            scanLayout.SetColumnSpan(topBar, 2);

            _scan = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = Theme.Make(14f),
                Margin = new Padding(0, 6, 8, 0),
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.CustomSource
            };
            ProductsView.SetCueBanner(_scan, "Scan a barcode, or type a name / code + Enter");
            var addButton = Theme.PrimaryButton("Add");
            addButton.BackColor = accent;
            addButton.Margin = new Padding(0, 6, 0, 0);
            addButton.MinimumSize = new Size(80, 34);
            scanLayout.Controls.Add(_scan, 0, 1);
            scanLayout.Controls.Add(addButton, 1, 1);
            scanCard.Controls.Add(scanLayout);

            _notice = new Label { Dock = DockStyle.Fill, Font = Theme.BodyBold, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0), UseMnemonic = false, AutoEllipsis = true };
            _noticeTimer = new Timer { Interval = 5000 };
            _noticeTimer.Tick += (s, e) => { _noticeTimer.Stop(); _notice.Text = ""; };

            // Cart grid
            var cartCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            _cartGrid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_cartGrid);
            _cartGrid.ReadOnly = false;
            _cartGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _cartGrid.Columns.Add(DashboardView.Col("No", "#", 18));
            _cartGrid.Columns.Add(DashboardView.Col("Code", "Code", 50));
            _cartGrid.Columns.Add(DashboardView.Col("Name", "Product", 150));
            _cartGrid.Columns.Add(DashboardView.Col("Stock", "In Stock", 45, DataGridViewContentAlignment.MiddleRight));
            _cartGrid.Columns.Add(DashboardView.Col("UnitPrice", isSale ? "Unit Price" : "Unit Cost", 65, DataGridViewContentAlignment.MiddleRight));
            _cartGrid.Columns.Add(DashboardView.Col("Quantity", "Qty", 40, DataGridViewContentAlignment.MiddleRight));
            _cartGrid.Columns.Add(DashboardView.Col("LineTotal", "Total", 70, DataGridViewContentAlignment.MiddleRight));
            _cartGrid.Columns["No"].DataPropertyName = null;
            var remove = new DataGridViewButtonColumn
            {
                Name = "Remove", HeaderText = "", Text = L.T("Remove"), UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat, FillWeight = 40
            };
            remove.DefaultCellStyle.ForeColor = Theme.Danger;
            remove.DefaultCellStyle.SelectionForeColor = Theme.Danger;
            _cartGrid.Columns.Add(remove);
            foreach (DataGridViewColumn c in _cartGrid.Columns)
                c.ReadOnly = c.Name != "UnitPrice" && c.Name != "Quantity";

            // Fixed widths for the numeric columns; the product name takes the remaining space.
            var widths = new Dictionary<string, int>
            {
                { "No", 36 }, { "Code", 68 }, { "Stock", 62 }, { "UnitPrice", 104 }, { "Quantity", 72 }, { "LineTotal", 112 }, { "Remove", 72 }
            };
            foreach (var w in widths)
            {
                var col = _cartGrid.Columns[w.Key];
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width = w.Value;
            }
            _cartGrid.Columns["Name"].MinimumWidth = 90;
            _cartGrid.Columns["No"].DefaultCellStyle.ForeColor = Theme.TextSecondary;
            _cartGrid.Columns["Remove"].DefaultCellStyle.Padding = new Padding(6, 5, 10, 5);
            _cartGrid.Columns["UnitPrice"].DefaultCellStyle.BackColor = Color.FromArgb(239, 246, 255);
            _cartGrid.Columns["Quantity"].DefaultCellStyle.BackColor = Color.FromArgb(239, 246, 255);
            _cartGrid.Columns["Quantity"].DefaultCellStyle.Font = Theme.BodyBold;
            _cartGrid.DataSource = _cart;
            cartCard.Controls.Add(_cartGrid);

            left.Controls.Add(scanCard, 0, 0);
            left.Controls.Add(_notice, 0, 1);
            left.Controls.Add(cartCard, 0, 2);

            // Summary / checkout panel
            var summaryCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), Margin = new Padding(0) };
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Surface };
            var caption = new Label { Text = L.T(isSale ? "CURRENT SALE" : "GOODS RECEIVED"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            _itemsLabel = new Label { Text = L.T("0 items"), ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            _totalLabel = new StatValueLabel { Text = Theme.Money(0), Height = 56, Margin = new Padding(0, 0, 0, 0) };
            _totalLabel.Font = Theme.Semi(24f);
            _totalLabel.ForeColor = accent;
            _profitLabel = new Label { AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false, Visible = isSale };
            // Customer, discount and payment (sale mode only).
            var customerLabel = new Label { Text = L.T("Customer"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 2), Visible = isSale };
            var customerRow = new TableLayoutPanel { Height = 32, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8), Visible = isSale, BackColor = Theme.Surface };
            customerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            customerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _customer = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Margin = new Padding(0, 2, 4, 0) };
            var addCustomer = Theme.SecondaryButton("+");
            addCustomer.MinimumSize = new Size(32, 28);
            addCustomer.AutoSize = false;
            addCustomer.Size = new Size(32, 28);
            addCustomer.Margin = new Padding(0, 2, 0, 0);
            addCustomer.Padding = new Padding(0);
            customerRow.Controls.Add(_customer, 0, 0);
            customerRow.Controls.Add(addCustomer, 1, 0);

            var payRow = new TableLayoutPanel { Height = 52, ColumnCount = 2, Margin = new Padding(0, 0, 0, 2), Visible = isSale, BackColor = Theme.Surface };
            payRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            payRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            payRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            payRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            payRow.Controls.Add(new Label { Text = L.T("Discount"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0) }, 0, 0);
            payRow.Controls.Add(new Label { Text = L.T("Method"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(4, 0, 0, 0) }, 1, 0);
            _discount = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Font = Theme.Num(10f), Margin = new Padding(0, 2, 4, 0) };
            _method = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Margin = new Padding(4, 2, 0, 0) };
            foreach (var m in PayMethod.All) _method.Items.Add(PayMethod.Display(m));
            _method.SelectedIndex = 0;
            payRow.Controls.Add(_discount, 0, 1);
            payRow.Controls.Add(_method, 1, 1);

            var paidRow = new TableLayoutPanel { Height = 52, ColumnCount = 2, Margin = new Padding(0, 0, 0, 2), Visible = isSale, BackColor = Theme.Surface };
            paidRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            paidRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            paidRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            paidRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            paidRow.Controls.Add(new Label { Text = L.T("Paid now"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0) }, 0, 0);
            _paid = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Font = Theme.Num(10f), Margin = new Padding(0, 2, 4, 0) };
            _dueLabel = new Label { Dock = DockStyle.Fill, Font = Theme.Num(10f, true), ForeColor = Theme.Danger, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 2, 0, 0), UseMnemonic = false };
            paidRow.Controls.Add(_paid, 0, 1);
            paidRow.Controls.Add(_dueLabel, 1, 1);

            var noteLabel = new Label { Text = L.T(isSale ? "Note (optional)" : "Note / supplier invoice no. (optional)"), Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            _note = new TextBox { Multiline = true, Height = 54, MaxLength = 200, Font = Theme.Body, Margin = new Padding(0, 0, 0, 10) };
            _printReceipt = new CheckBox { Text = L.T("Print receipt"), AutoSize = true, Checked = AppSettings.ReceiptAutoPrint, Margin = new Padding(0, 0, 0, 12), Visible = isSale };
            _checkoutButton = Theme.PrimaryButton(isSale ? "Checkout" : "Save Stock In");
            _checkoutButton.BackColor = accent;
            UpdateKeyLabels();
            Shortcuts.Changed += OnShortcutsChanged;
            Disposed += (s, e) => Shortcuts.Changed -= OnShortcutsChanged;
            _checkoutButton.FlatAppearance.MouseOverBackColor = isSale ? SaleHover : Theme.AccentHover;
            _checkoutButton.FlatAppearance.MouseDownBackColor = isSale ? SaleHover : Theme.AccentHover;
            _checkoutButton.Height = 52;
            _checkoutButton.MinimumSize = new Size(0, 52);
            _checkoutButton.AutoSize = false;
            _checkoutButton.Font = Theme.Semi(12f);
            _checkoutButton.Margin = new Padding(0, 0, 0, 8);
            var clearButton = Theme.SecondaryButton("Clear Cart");
            clearButton.AutoSize = false;
            clearButton.Margin = new Padding(0, 0, 0, 16);
            _lastInvoice = new Label { AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Small, Margin = new Padding(0, 0, 0, 2), UseMnemonic = false, MaximumSize = new Size(240, 0) };
            _reprintLast = new LinkLabel { Text = L.T("Reprint receipt"), AutoSize = true, Visible = false, LinkColor = Theme.Accent, Font = Theme.Small, Margin = new Padding(0) };

            foreach (var c in new Control[] { caption, _itemsLabel, _totalLabel, _profitLabel, customerLabel, customerRow, payRow, paidRow, noteLabel, _note, _printReceipt, _checkoutButton, clearButton, _lastInvoice, _reprintLast })
            {
                summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                if (c is TextBox || c is Button || c is StatValueLabel) c.Dock = DockStyle.Fill;
                summary.Controls.Add(c);
            }
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            summaryCard.Controls.Add(summary);

            pos.Controls.Add(left, 0, 0);
            pos.Controls.Add(summaryCard, 1, 0);
            _posPage.Controls.Add(pos);

            // ================= History page =================
            var history = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            history.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            history.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            int statCount = isSale ? 4 : 3;
            var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = statCount, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            for (int i = 0; i < statCount; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / statCount));
            if (isSale)
            {
                stats.Controls.Add(DashboardView.StatCard("Sales (invoices)", Theme.Success, out _stat1), 0, 0);
                stats.Controls.Add(DashboardView.StatCard("Units Sold", Theme.Accent, out _stat2), 1, 0);
                stats.Controls.Add(DashboardView.StatCard("Revenue", Color.FromArgb(124, 58, 237), out _stat3), 2, 0);
                stats.Controls.Add(DashboardView.StatCard("Profit", Color.FromArgb(13, 148, 136), out _stat4), 3, 0);
            }
            else
            {
                stats.Controls.Add(DashboardView.StatCard("Stock-ins", Theme.Accent, out _stat1), 0, 0);
                stats.Controls.Add(DashboardView.StatCard("Units Received", Theme.Success, out _stat2), 1, 0);
                stats.Controls.Add(DashboardView.StatCard("Purchase Total", Color.FromArgb(124, 58, 237), out _stat3), 2, 0);
            }

            var historyCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            // Two rows (filters, then actions) so the search box never gets squeezed by the buttons.
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface, Padding = new Padding(16, 12, 16, 8) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _period = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Margin = new Padding(0, 4, 8, 0) };
            _period.Items.AddRange(Periods.Select(x => (object)L.T(x)).ToArray());
            _period.SelectedIndex = 0;
            _search = new TextBox { Width = 340, Font = Theme.Body, Margin = new Padding(0, 4, 8, 0) };
            ProductsView.SetCueBanner(_search, isSale ? "Search product, invoice or note..." : "Search product, vendor, invoice...");
            filters.Controls.AddRange(new Control[] { _period, _search });

            var historyActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            _returnButton = Theme.SecondaryButton("Return items");
            _returnButton.Visible = isSale;
            _reprintButton = Theme.SecondaryButton("Reprint Receipt");
            _reprintButton.Visible = isSale;
            var warrantyButton = Theme.SecondaryButton("Warranty lookup");
            warrantyButton.Visible = isSale;
            _undoButton = Theme.SecondaryButton("Undo Selected");
            var exportButton = Theme.SecondaryButton("Export CSV");
            exportButton.Margin = new Padding(0);
            historyActions.Controls.AddRange(new Control[] { _returnButton, _reprintButton, warrantyButton, _undoButton, exportButton });
            warrantyButton.Click += (s, e) => { using (var dlg = new WarrantyLookupForm()) dlg.ShowDialog(FindForm()); };
            _returnButton.Click += (s, e) => ReturnSelected();

            toolbar.Controls.Add(filters, 0, 0);
            toolbar.Controls.Add(historyActions, 0, 1);

            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("CreatedAt", "Date & Time", 115));
            _grid.Columns["CreatedAt"].DefaultCellStyle.Format = "dd/MM/yy, hh:mm tt";
            _grid.Columns.Add(DashboardView.Col("InvoiceNo", "Invoice", 85));
            _grid.Columns.Add(DashboardView.Col("TypeDisplay", "Type", 55));
            if (!isSale) _grid.Columns.Add(DashboardView.Col("VendorName", "Vendor", 100));
            _grid.Columns.Add(DashboardView.Col("ProductName", "Product", 130));
            _grid.Columns.Add(DashboardView.Col("Quantity", "Qty", 45, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("UnitPrice", isSale ? "Unit Price" : "Unit Cost", 70, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("Total", "Total", 80, DataGridViewContentAlignment.MiddleRight));
            if (isSale) _grid.Columns.Add(DashboardView.Col("Profit", "Profit", 75, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("Note", "Note", 90));

            historyCard.Controls.Add(_grid);
            historyCard.Controls.Add(toolbar);
            history.Controls.Add(stats, 0, 0);
            history.Controls.Add(historyCard, 0, 1);
            _historyPage.Controls.Add(history);

            // ================= Events =================
            _tabPos.Click += (s, e) => ShowTab(true);
            _tabHistory.Click += (s, e) => ShowTab(false);
            phoneButton.Click += (s, e) => PhoneScannerForm.ShowFor(_main);

            var phoneTimer = new Timer { Interval = 1000 };
            phoneTimer.Tick += (s, e) => { if (Visible) UpdatePhoneStatus(); };
            phoneTimer.Start();

            _scan.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ProcessScanBox();
                }
            };
            addButton.Click += (s, e) => ProcessScanBox();

            _cartGrid.CellFormatting += CartGrid_CellFormatting;
            _cartGrid.CellParsing += CartGrid_CellParsing;
            _cartGrid.CellValidating += CartGrid_CellValidating;
            _cartGrid.CellEndEdit += (s, e) => UpdateSummary();
            _cartGrid.DataError += (s, e) => { e.ThrowException = false; Notice(L.T("Please enter a valid number."), false); };
            _cartGrid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && _cartGrid.Columns[e.ColumnIndex].Name == "Remove")
                    RemoveLine(e.RowIndex);
            };
            _cartGrid.CellClick += (s, e) =>
            {
                // One click on Qty or Price starts editing.
                if (e.RowIndex < 0) return;
                var name = _cartGrid.Columns[e.ColumnIndex].Name;
                if (name == "Quantity" || name == "UnitPrice") _cartGrid.BeginEdit(true);
            };
            _cartGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !_cartGrid.IsCurrentCellInEditMode && _cartGrid.CurrentRow != null)
                {
                    RemoveLine(_cartGrid.CurrentRow.Index);
                    e.Handled = true;
                }
            };

            _checkoutButton.Click += (s, e) => Checkout();
            addCustomer.Click += (s, e) => QuickAddCustomer();
            _discount.ValueChanged += (s, e) => UpdateSummary();
            _discount.KeyUp += (s, e) => UpdateSummary();
            _paid.ValueChanged += (s, e) => { if (!_settingPaid) _paidTouched = true; UpdateSummary(); };
            clearButton.Click += (s, e) => ClearCart(true);
            _reprintLast.LinkClicked += (s, e) => { if (_lastInvoiceNo != null) PrintReceipt(_lastInvoiceNo); };

            _searchDelay = new Timer { Interval = 300 };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); LoadHistory(); };
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
            _period.SelectedIndexChanged += (s, e) => LoadHistory();
            _undoButton.Click += (s, e) => UndoSelected();
            _reprintButton.Click += (s, e) => ReprintSelected();
            exportButton.Click += (s, e) => ExportCsv();
            _grid.SelectionChanged += (s, e) => UpdateHistoryButtons();
            _grid.CellFormatting += HistoryGrid_CellFormatting;
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { UndoSelected(); e.Handled = true; } };

            AppSettings.Changed += OnSettingsChanged;
            Disposed += (s, e) => AppSettings.Changed -= OnSettingsChanged;
            ParentChanged += (s, e) => HookFormKeyPress();

            StyleTab(_tabPos, true);
            StyleTab(_tabHistory, false);
            UpdateSummary();
        }

        // =====================================================================
        // Public surface (MainForm / ScanHub)
        // =====================================================================

        public bool IsSale { get { return _isSale; } }

        private void OnShortcutsChanged(object sender, EventArgs e)
        {
            UpdateKeyLabels();
        }

        /// <summary>The hint line and the checkout button show the current (changeable) shortcut keys.</summary>
        private void UpdateKeyLabels()
        {
            string scan = Shortcuts.Display(Shortcuts.Get(Shortcuts.Scan));
            string checkout = Shortcuts.Display(Shortcuts.Get(Shortcuts.Checkout));
            _keysHint.Text = _isSale
                ? L.F("{0} scan box  ·  {1} checkout  ·  Del remove line", scan, checkout)
                : L.F("{0} scan box  ·  {1} save  ·  Del remove line", scan, checkout);
            _checkoutButton.Text = Shortcuts.Get(Shortcuts.Checkout) == Keys.None
                ? L.T(_isSale ? "Checkout" : "Save Stock In")
                : L.F(_isSale ? "Checkout  ({0})" : "Save Stock In  ({0})", checkout);
        }

        /// <summary>True when this screen is on screen with its POS/Receive tab showing.</summary>
        public bool IsPosActive
        {
            get { return Visible && Parent != null && _posPage.Visible; }
        }

        public int CartCount { get { return _cart.Sum(l => l.Quantity); } }

        public decimal CartSubtotal { get { return _cart.Sum(l => l.LineTotal); } }

        private decimal CurrentDiscount
        {
            get { return _isSale && _discount != null ? Math.Min(_discount.Value, CartSubtotal) : 0m; }
        }

        public decimal CartTotal { get { return CartSubtotal - CurrentDiscount; } }

        public IList<CartLine> CartLines { get { return _cart.ToList(); } }

        public void RefreshData()
        {
            RefreshProducts();
            if (_vendor != null) LoadVendors();
            if (_isSale) LoadCustomers(SelectedCustomerId);
            ApplyPermissions();
            if (PendingProductId != 0)
            {
                var p = _productCache.FirstOrDefault(x => x.Id == PendingProductId);
                PendingProductId = 0;
                if (p != null)
                {
                    ShowTab(true);
                    AddToCart(p);
                }
            }
            if (_historyPage.Visible) LoadHistory();
            UpdatePhoneStatus();
            if (_posPage.Visible) FocusScan();
        }

        public void ShowTab(bool pos)
        {
            _posPage.Visible = pos;
            _historyPage.Visible = !pos;
            StyleTab(_tabPos, pos);
            StyleTab(_tabHistory, !pos);
            if (pos) FocusScan();
            else LoadHistory();
        }

        /// <summary>Barcode from the phone scanner (called on the UI thread; never shows modal dialogs).</summary>
        public ScanResult HandleScan(string code)
        {
            if (!_posPage.Visible) ShowTab(true);

            var p = _products.FindByScan(code);
            if (p != null)
                return AddToCart(p);

            if (_isSale)
            {
                Notice(L.F("Unknown barcode: {0}. Add it in Products or receive it in Stock In.", code), false);
                return ScanResult.Fail(L.T("Unknown barcode. Not found in products."));
            }

            // Stock In with a new barcode: open "Add Product" after replying to the phone.
            BeginInvoke(new Action(() => CreateProductFor(code)));
            return new ScanResult { Ok = true, ProductName = L.F("New barcode: {0}", code), Message = L.T("Enter the product details on the PC; it will then be added to the list.") };
        }

        /// <summary>
        /// Cart change requested from the phone: "undo" (last scan), "inc", "dec", "remove" (a product) or "clear".
        /// </summary>
        public ScanResult CartAction(string action, long productId)
        {
            if (_cartGrid.IsCurrentCellInEditMode) _cartGrid.CancelEdit();
            switch (action)
            {
                case "clear":
                    if (_cart.Count == 0) return new ScanResult { Ok = true, Message = L.T("The list is already empty.") };
                    ClearCart(false);
                    Notice(L.T("Cart cleared from the phone."), true);
                    return new ScanResult { Ok = true, Message = L.T("All scanned items removed.") };

                case "undo":
                    while (_scanHistory.Count > 0)
                    {
                        var id = _scanHistory.Pop();
                        int i = IndexOf(id);
                        if (i < 0) continue;     // that line was already removed
                        var name = _cart[i].Name;
                        ChangeQuantity(i, -1);
                        Notice(L.F("Last scan undone: {0}", name), true);
                        return new ScanResult { Ok = true, ProductName = name, Message = L.T("Last scan undone.") };
                    }
                    return ScanResult.Fail(L.T("Nothing to undo."));

                case "inc":
                case "dec":
                case "remove":
                {
                    int i = IndexOf(productId);
                    if (i < 0) return ScanResult.Fail(L.T("That item is no longer in the list."));
                    var name = _cart[i].Name;
                    if (action == "remove")
                    {
                        RemoveLine(i);
                        return new ScanResult { Ok = true, ProductName = name, Message = L.T("Removed.") };
                    }
                    if (action == "inc" && _isSale && _cart[i].Quantity + 1 > _cart[i].Stock)
                        return ScanResult.Fail(L.F("Only {0} in stock.", L.Num(_cart[i].Stock)));
                    ChangeQuantity(i, action == "inc" ? 1 : -1);
                    return new ScanResult { Ok = true, ProductName = name, Message = L.T("Quantity updated.") };
                }
            }
            return ScanResult.Fail(L.T("Unknown action."));
        }

        // =====================================================================
        // Cart
        // =====================================================================

        private int IndexOf(long productId)
        {
            for (int i = 0; i < _cart.Count; i++)
                if (_cart[i].ProductId == productId) return i;
            return -1;
        }

        private void ChangeQuantity(int index, int delta)
        {
            var line = _cart[index];
            line.Quantity += delta;
            if (line.Quantity <= 0)
            {
                _cart.RemoveAt(index);
            }
            else
            {
                _cart.ResetItem(index);
                SelectCartRow(index);
            }
            UpdateSummary();
        }

        private ScanResult AddToCart(Product p, int qty = 1)
        {
            int index = IndexOf(p.Id);
            int newQty = (index >= 0 ? _cart[index].Quantity : 0) + qty;
            if (_isSale && newQty > p.Quantity)
            {
                var msg = p.Quantity <= 0
                    ? L.F("\"{0}\" is out of stock.", p.Name)
                    : L.F("Only {0} of \"{1}\" in stock.", L.Num(p.Quantity), p.Name);
                Notice(msg, false);
                SystemSounds.Exclamation.Play();
                return new ScanResult { Ok = false, ProductName = p.Name, Message = msg, Quantity = index >= 0 ? _cart[index].Quantity : 0 };
            }

            CartLine line;
            if (index >= 0)
            {
                line = _cart[index];
                line.Quantity = newQty;
                line.Stock = p.Quantity;
                _cart.ResetItem(index);
            }
            else
            {
                line = new CartLine
                {
                    ProductId = p.Id,
                    Code = p.Code,
                    Barcode = p.Barcode,
                    Name = p.Name,
                    Quantity = qty,
                    UnitPrice = _isSale ? p.Price : p.PurchasePrice,
                    UnitCost = p.PurchasePrice,
                    Stock = p.Quantity
                };
                _cart.Add(line);
                index = _cart.Count - 1;

                // Stock In: pre-select the product's usual vendor if none is chosen yet.
                if (!_isSale && p.VendorId.HasValue && _vendor.SelectedIndex <= 0)
                    SelectVendor(p.VendorId.Value);
            }
            for (int i = 0; i < qty; i++) _scanHistory.Push(p.Id);

            SelectCartRow(index);
            UpdateSummary();
            Notice(L.F("Added: {0}  (qty {1})", p.Name, L.Num(line.Quantity)), true);
            return new ScanResult
            {
                Ok = true,
                ProductName = p.Name,
                Price = line.UnitPrice,
                Quantity = line.Quantity,
                Message = L.T(_isSale ? "Added to sale" : "Added to stock-in")
            };
        }

        private void RemoveLine(int index)
        {
            if (index < 0 || index >= _cart.Count) return;
            if (_cartGrid.IsCurrentCellInEditMode) _cartGrid.CancelEdit();
            var name = _cart[index].Name;
            _cart.RemoveAt(index);
            UpdateSummary();
            Notice(L.F("Removed: {0}", name), true);
            FocusScan();
        }

        private void ClearCart(bool ask)
        {
            if (_cart.Count == 0) return;
            if (ask && Msg.Show(FindForm(), L.T("Remove all items from the list?"), AppInfo.ProductName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (_cartGrid.IsCurrentCellInEditMode) _cartGrid.CancelEdit();
            _cart.Clear();
            _scanHistory.Clear();
            UpdateSummary();
            FocusScan();
        }

        private void SelectCartRow(int index)
        {
            if (index < 0 || index >= _cartGrid.Rows.Count) return;
            _cartGrid.ClearSelection();
            _cartGrid.Rows[index].Selected = true;
            _cartGrid.CurrentCell = _cartGrid.Rows[index].Cells["Name"];
            _cartGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 3);
        }

        private void UpdateSummary()
        {
            int units = CartCount;
            int lines = _cart.Count;
            _itemsLabel.Text = lines == 0
                ? L.T(_isSale ? "Cart is empty. Scan a product to begin." : "Nothing yet. Scan items as they arrive.")
                : L.F(lines == 1 ? "{0} product" : "{0} products", L.Num(lines)) + "  ·  " + L.F(units == 1 ? "{0} unit" : "{0} units", L.Num(units));
            _totalLabel.Text = Theme.Money(CartTotal);
            if (_isSale)
            {
                var profit = _cart.Sum(l => l.LineProfit) - CurrentDiscount;
                _profitLabel.Text = lines == 0 ? "" : L.F("Profit on this sale:  {0}", Theme.Money(profit));
                _profitLabel.ForeColor = profit < 0 ? Theme.Danger : Color.FromArgb(13, 148, 136);

                var totalNow = CartTotal;
                if (!_paidTouched)
                {
                    _settingPaid = true;
                    _paid.Value = Math.Max(_paid.Minimum, Math.Min(_paid.Maximum, totalNow));
                    _settingPaid = false;
                }
                if (_paid.Value > totalNow && _paidTouched)
                {
                    _settingPaid = true;
                    _paid.Value = totalNow;
                    _settingPaid = false;
                }
                var due = Math.Max(0, totalNow - _paid.Value);
                _dueLabel.Text = due > 0.004m ? L.F("Due {0}", Theme.Money(due)) : "";
            }
            _checkoutButton.Enabled = lines > 0;
            _cartGrid.Invalidate();
        }

        private void RefreshProducts()
        {
            _productCache = _products.GetAll();
            _byDisplayName = new Dictionary<string, Product>(StringComparer.CurrentCultureIgnoreCase);
            var source = new AutoCompleteStringCollection();
            foreach (var p in _productCache)
            {
                _byDisplayName[p.DisplayName] = p;
                source.Add(p.DisplayName);
            }
            _scan.AutoCompleteCustomSource = source;

            // Keep stock numbers current; drop lines whose product was deleted.
            for (int i = _cart.Count - 1; i >= 0; i--)
            {
                var p = _productCache.FirstOrDefault(x => x.Id == _cart[i].ProductId);
                if (p == null) { _cart.RemoveAt(i); continue; }
                _cart[i].Stock = p.Quantity;
                _cart[i].Name = p.Name;
                _cart[i].Code = p.Code;
                _cart[i].UnitCost = p.PurchasePrice;
                _cart.ResetItem(i);
            }
            UpdateSummary();
        }

        // ---------------- Vendors (Stock In) ----------------

        private void LoadVendors()
        {
            var current = SelectedVendorId;
            _vendor.Items.Clear();
            _vendor.Items.Add(NoVendor);
            foreach (var v in _vendors.GetAll())
                _vendor.Items.Add(new VendorItem(v));
            _vendor.SelectedIndex = 0;
            if (current.HasValue) SelectVendor(current.Value);
        }

        private void SelectVendor(long id)
        {
            for (int i = 1; i < _vendor.Items.Count; i++)
            {
                if (((VendorItem)_vendor.Items[i]).Vendor.Id == id)
                {
                    _vendor.SelectedIndex = i;
                    return;
                }
            }
        }

        private long? SelectedVendorId
        {
            get
            {
                var item = _vendor != null ? _vendor.SelectedItem as VendorItem : null;
                return item != null ? item.Vendor.Id : (long?)null;
            }
        }

        private sealed class VendorItem
        {
            public readonly Vendor Vendor;
            public VendorItem(Vendor v) { Vendor = v; }
            public override string ToString() { return Vendor.Name; }
        }

        // ---------------- Scan box (keyboard / USB scanner) ----------------

        private void ProcessScanBox()
        {
            var text = _scan.Text.Trim();
            if (text.Length == 0) { FocusScan(); return; }

            Product p;
            if (!_byDisplayName.TryGetValue(text, out p))
                p = _products.FindByScan(text);

            if (p == null)
            {
                var matches = _productCache.Where(x =>
                    x.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    x.Code.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(15).ToList();

                if (matches.Count == 1)
                {
                    p = matches[0];
                }
                else if (matches.Count > 1)
                {
                    ShowPicker(matches);
                    return;
                }
                else
                {
                    _scan.Clear();
                    if (_isSale)
                    {
                        Notice(L.F("No product found for \"{0}\".", text), false);
                        SystemSounds.Exclamation.Play();
                        FocusScan();
                    }
                    else
                    {
                        CreateProductFor(text);
                    }
                    return;
                }
            }

            _scan.Clear();
            AddToCart(p);
            FocusScan();
        }

        private void ShowPicker(List<Product> matches)
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            foreach (var m in matches)
            {
                var product = m;
                var price = _isSale ? m.Price : m.PurchasePrice;
                var item = new ToolStripMenuItem(L.F("{0}   ({1})   {2}   ·   stock {3}", m.Name, m.Code, Theme.Money(price), L.Num(m.Quantity)));
                item.Click += (s, e) => { _scan.Clear(); AddToCart(product); FocusScan(); };
                menu.Items.Add(item);
            }
            menu.Closed += (s, e) => BeginInvoke(new Action(() => menu.Dispose()));
            menu.Show(_scan, new Point(0, _scan.Height));
        }

        /// <summary>Unknown code during Stock In: open Add Product with the barcode (or name) filled in.</summary>
        private void CreateProductFor(string code)
        {
            if (ProductEditForm.Current != null) return;   // a product form is already open

            bool looksLikeBarcode = code.All(ch => !char.IsWhiteSpace(ch)) && code.Count(char.IsDigit) >= code.Length / 2;
            using (var dlg = new ProductEditForm(null, _products.GetCategories(),
                       looksLikeBarcode ? code : null, looksLikeBarcode ? null : code, SelectedVendorId))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    RefreshProducts();
                    AddToCart(dlg.Product);
                }
            }
            FocusScan();
        }

        private void FocusScan()
        {
            if (_posPage.Visible && _scan.CanFocus) _scan.Focus();
        }

        /// <summary>
        /// USB scanners "type" the barcode. If a key arrives while focus is on a non-text control of this screen,
        /// send it to the scan box so the scan is not lost.
        /// </summary>
        private void HookFormKeyPress()
        {
            var form = FindForm();
            if (form == null || _keyHooked) return;
            _keyHooked = true;
            form.KeyPress += (s, e) =>
            {
                if (!IsPosActive || char.IsControl(e.KeyChar)) return;
                var focused = DeepestActiveControl(form);
                if (focused == _scan || focused is TextBoxBase || focused is NumericUpDown || focused is ComboBox ||
                    (focused != null && focused.Parent is DataGridView) || _cartGrid.IsCurrentCellInEditMode)
                    return;
                if (focused != null && !Contains(focused)) return;   // focus is outside this screen

                _scan.Focus();
                _scan.AppendText(e.KeyChar.ToString());
                e.Handled = true;
            };
        }

        private static Control DeepestActiveControl(ContainerControl root)
        {
            Control c = root.ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null)
                c = ((ContainerControl)c).ActiveControl;
            return c;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Shortcuts.Is(Shortcuts.Scan, keyData))
            {
                ShowTab(true);
                _scan.SelectAll();
                return true;
            }
            if (Shortcuts.Is(Shortcuts.Checkout, keyData) && _posPage.Visible)
            {
                Checkout();
                return true;
            }
            if (Shortcuts.Is(Shortcuts.Discount, keyData) && _posPage.Visible && _discount != null && _discount.Visible)
            {
                _discount.Focus();
                _discount.Select(0, _discount.Text.Length);
                return true;
            }
            if (Shortcuts.Is(Shortcuts.Refresh, keyData))
            {
                RefreshData();
                return true;
            }
            if (Shortcuts.Is(Shortcuts.Search, keyData) && _historyPage.Visible)
            {
                _search.Focus();
                _search.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- Cart grid editing ----------------

        private void CartGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _cart.Count) return;
            var line = _cart[e.RowIndex];
            switch (_cartGrid.Columns[e.ColumnIndex].Name)
            {
                case "No":
                    e.Value = L.Num(e.RowIndex + 1);
                    e.FormattingApplied = true;
                    break;
                case "UnitPrice":
                    e.Value = Theme.Money(line.UnitPrice);
                    e.FormattingApplied = true;
                    if (_isSale && line.UnitPrice < line.UnitCost)
                        e.CellStyle.ForeColor = Theme.Danger;   // selling below purchase rate
                    break;
                case "LineTotal":
                    e.Value = Theme.Money(line.LineTotal);
                    e.FormattingApplied = true;
                    e.CellStyle.Font = Theme.BodyBold;
                    break;
                case "Stock":
                    if (_isSale && line.Stock - line.Quantity <= 0)
                        e.CellStyle.ForeColor = Theme.Warning;
                    break;
            }
        }

        private void CartGrid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            var name = _cartGrid.Columns[e.ColumnIndex].Name;
            var text = Convert.ToString(e.Value);
            decimal d;
            if (name == "UnitPrice" && TryParseNumber(text, out d)) { e.Value = d; e.ParsingApplied = true; }
            else if (name == "Quantity" && TryParseNumber(text, out d)) { e.Value = (int)Math.Round(d); e.ParsingApplied = true; }
        }

        private void CartGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (!_cartGrid.IsCurrentCellInEditMode || e.RowIndex >= _cart.Count) return;
            var name = _cartGrid.Columns[e.ColumnIndex].Name;
            var text = Convert.ToString(e.FormattedValue);
            decimal d;

            if (name == "Quantity")
            {
                if (!TryParseNumber(text, out d) || d < 1 || d > 10000000)
                {
                    Notice(L.T("Quantity must be a whole number of 1 or more."), false);
                    e.Cancel = true;
                    return;
                }
                var line = _cart[e.RowIndex];
                if (_isSale && d > line.Stock)
                {
                    Notice(L.F("Only {0} of \"{1}\" in stock.", L.Num(line.Stock), line.Name), false);
                    e.Cancel = true;
                }
            }
            else if (name == "UnitPrice")
            {
                if (!TryParseNumber(text, out d) || d < 0 || d > 100000000)
                {
                    Notice(L.T("Please enter a valid price."), false);
                    e.Cancel = true;
                }
            }
        }

        private static bool TryParseNumber(string text, out decimal value)
        {
            text = L.ToAscii(text ?? "").Replace(Theme.CurrencySymbol, "").Replace("৳", "").Replace("Tk", "").Replace(",", "").Trim();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
                   decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        // ---------------- Checkout ----------------

        private void Checkout()
        {
            if (_cartGrid.IsCurrentCellInEditMode && !_cartGrid.EndEdit()) return;
            if (_cart.Count == 0)
            {
                Notice(L.T(_isSale ? "The cart is empty. Scan a product first." : "Nothing to save. Scan the received items first."), false);
                FocusScan();
                return;
            }

            if (!_isSale && !SelectedVendorId.HasValue && _vendor.Items.Count > 1 &&
                Msg.Show(FindForm(), L.T("No vendor is selected. Save this stock-in without a vendor?"), AppInfo.ProductName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _vendor.Focus();
                _vendor.DroppedDown = true;
                return;
            }

            var type = _isSale ? TransactionType.Sale : TransactionType.StockIn;
            long? customerId = SelectedCustomerId;
            decimal discount = CurrentDiscount;
            var total = CartTotal;
            decimal? paid = _isSale ? (decimal?)Math.Min(_paid.Value, total) : null;
            string method = _isSale ? PayMethod.All[Math.Max(0, _method.SelectedIndex)] : PayMethod.Cash;
            decimal dueNow = _isSale ? Math.Max(0, total - paid.Value) : 0m;
            if (_isSale && dueNow > 0.004m && !customerId.HasValue)
            {
                Msg.Show(FindForm(), L.T("Selling on due needs a customer. Pick a customer first."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _customer.Focus();
                _customer.DroppedDown = true;
                return;
            }

            var profit = _cart.Sum(l => l.LineProfit) - discount;
            int units = CartCount;
            var warrantyLines = !_isSale ? null : _cart
                .Select(l => new { Line = l, Product = _productCache.FirstOrDefault(x => x.Id == l.ProductId) })
                .Where(x => x.Product != null && x.Product.WarrantyMonths > 0)
                .Select(x => Tuple.Create(x.Line.ProductId, x.Line.Name, x.Line.Quantity, x.Product.WarrantyMonths))
                .ToList();
            string invoice;
            try
            {
                invoice = _transactions.Checkout(_cart.ToList(), type, _note.Text, _isSale ? null : SelectedVendorId,
                    discount, customerId, paid, method);
            }
            catch (InsufficientStockException ex)
            {
                RefreshProducts();
                Msg.Show(FindForm(), ex.Message + "\n\n" + L.T("Stock numbers in the cart have been refreshed. Please adjust and try again."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (InvalidOperationException ex)
            {
                RefreshProducts();
                Msg.Show(FindForm(), ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cart.Clear();
            _scanHistory.Clear();
            _note.Clear();
            if (_isSale)
            {
                _discount.Value = 0;
                _paidTouched = false;
                if (_customer.Items.Count > 0) _customer.SelectedIndex = 0;
            }
            _lastInvoiceNo = invoice;
            _lastInvoice.Text = _isSale
                ? L.F("Last: {0}  ·  {1}  ·  profit {2}", invoice, Theme.Money(total), Theme.Money(profit)) +
                  (dueNow > 0.004m ? "  ·  " + L.F("due {0}", Theme.Money(dueNow)) : "")
                : L.F("Last: {0}  ·  {1} units  ·  {2}", invoice, L.Num(units), Theme.Money(total));
            _reprintLast.Visible = _isSale;
            Notice(_isSale ? L.F("Sale {0} completed: {1}", invoice, Theme.Money(total))
                           : L.F("Stock-in {0} saved: {1} units added", invoice, L.Num(units)), true);
            _main.SetStatus(L.F(_isSale ? "Sale {0} recorded." : "Stock-in {0} saved.", invoice));

            RefreshProducts();
            if (warrantyLines != null && warrantyLines.Count > 0)
            {
                using (var dlg = new SerialEntryForm(invoice, warrantyLines))
                    dlg.ShowDialog(FindForm());
            }
            if (_isSale && _printReceipt.Checked)
                PrintReceipt(invoice);
            FocusScan();
        }

        // ---------------- Customers (sale mode) ----------------

        private long? SelectedCustomerId
        {
            get
            {
                if (!_isSale || _customer == null) return null;
                int i = _customer.SelectedIndex - 1;
                return i >= 0 && i < _customerList.Count ? _customerList[i].Id : (long?)null;
            }
        }

        private void LoadCustomers(long? keepId)
        {
            _customerList = _customerRepo.GetAll();
            _customer.BeginUpdate();
            _customer.Items.Clear();
            _customer.Items.Add(L.T("(walk-in customer)"));
            foreach (var c in _customerList)
                _customer.Items.Add(c.Due > 0.004m ? c.DisplayName + "  ·  " + Theme.Money(c.Due) : c.DisplayName);
            _customer.SelectedIndex = 0;
            if (keepId.HasValue)
            {
                for (int i = 0; i < _customerList.Count; i++)
                    if (_customerList[i].Id == keepId.Value) { _customer.SelectedIndex = i + 1; break; }
            }
            _customer.EndUpdate();
        }

        private void QuickAddCustomer()
        {
            using (var dlg = new CustomerEditForm(null))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    LoadCustomers(dlg.Customer.Id);
            }
        }

        /// <summary>Hides money and destructive actions from staff when a PIN is set.</summary>
        private void ApplyPermissions()
        {
            bool owner = Session.IsOwner;
            if (_isSale)
            {
                _profitLabel.Visible = owner;
                if (_grid.Columns.Contains("Profit")) _grid.Columns["Profit"].Visible = owner;
            }
            _undoButton.Visible = owner;
            if (_returnButton != null) _returnButton.Visible = owner && _isSale;
        }

        private void PrintReceipt(string invoiceNo)
        {
            try
            {
                ReceiptPrinter.PrintInvoice(invoiceNo);
            }
            catch (Exception ex)
            {
                Msg.Show(FindForm(), L.T("Could not print the receipt.") + "\n\n" + ex.Message + "\n\n" + L.T("Check the printer in Settings → Receipt."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- Notices / status ----------------

        private void Notice(string text, bool ok)
        {
            _notice.Text = (ok ? "•  " : "!  ") + text;
            _notice.ForeColor = ok ? Theme.Success : Theme.Danger;
            _noticeTimer.Stop();
            _noticeTimer.Start();
        }

        private void UpdatePhoneStatus()
        {
            if (!ScanHub.IsRunning)
            {
                _phoneStatus.Text = L.T("Phone scanner off");
                _phoneStatus.ForeColor = Theme.TextSecondary;
            }
            else if (ScanHub.PhoneConnected)
            {
                _phoneStatus.Text = L.T("• Phone connected");
                _phoneStatus.ForeColor = Theme.Success;
            }
            else
            {
                _phoneStatus.Text = L.T("Phone scanner ready");
                _phoneStatus.ForeColor = Theme.Accent;
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            _printReceipt.Checked = AppSettings.ReceiptAutoPrint;
            UpdateSummary();
        }

        // =====================================================================
        // History tab
        // =====================================================================

        private DateTime? PeriodStart()
        {
            var today = DateTime.Today;
            switch (_period.SelectedIndex)
            {
                case 0: return today;
                case 1: return today.AddDays(-6);
                case 2: return today.AddDays(-29);
                case 3: return new DateTime(today.Year, today.Month, 1);
                default: return null;
            }
        }

        private void LoadHistory()
        {
            var from = PeriodStart();
            var type = _isSale ? TransactionType.Sale : TransactionType.StockIn;

            _grid.DataSource = _transactions.GetTransactions(from, type, _search.Text);
            _grid.ClearSelection();
            UpdateHistoryButtons();

            var s = _isSale ? _transactions.GetSalesSummary(from) : _transactions.GetStockInSummary(from);
            _stat1.Text = L.Num(s.SaleCount);
            _stat2.Text = L.Num(s.UnitsSold);
            _stat3.Text = Theme.Money(s.Revenue);
            if (_stat4 != null) _stat4.Text = Session.IsOwner ? Theme.Money(s.Profit) : "••••";
        }

        private StockTransaction SelectedTransaction
        {
            get { return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as StockTransaction : null; }
        }

        private void UpdateHistoryButtons()
        {
            var t = SelectedTransaction;
            _undoButton.Enabled = t != null && (t.Type == TransactionType.Sale || t.Type == TransactionType.StockIn);
            _reprintButton.Enabled = t != null && !string.IsNullOrEmpty(t.InvoiceNo) &&
                (t.Type == TransactionType.Sale || t.Type == TransactionType.Return);
            if (_returnButton != null)
                _returnButton.Enabled = t != null && t.Type == TransactionType.Sale && !string.IsNullOrEmpty(t.InvoiceNo);
        }

        private void ReturnSelected()
        {
            var t = SelectedTransaction;
            if (t == null || t.Type != TransactionType.Sale || string.IsNullOrEmpty(t.InvoiceNo)) return;
            using (var dlg = new ReturnForm(t.InvoiceNo))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    RefreshProducts();
                    LoadHistory();
                    _main.SetStatus(L.F("Return {0} saved.", dlg.ReturnInvoiceNo));
                    if (dlg.PrintSlip) PrintReceipt(dlg.ReturnInvoiceNo);
                }
            }
        }

        private void HistoryGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var t = _grid.Rows[e.RowIndex].DataBoundItem as StockTransaction;
            if (t == null) return;

            switch (_grid.Columns[e.ColumnIndex].Name)
            {
                case "UnitPrice":
                case "Total":
                    e.Value = Theme.Money((decimal)e.Value);
                    e.FormattingApplied = true;
                    break;
                case "TypeDisplay":
                    e.Value = L.T(t.TypeDisplay);
                    e.FormattingApplied = true;
                    e.CellStyle.Font = Theme.SmallBold;
                    e.CellStyle.ForeColor =
                        t.Type == TransactionType.Return ? Theme.Danger :
                        t.Type == TransactionType.Adjust ? Theme.Warning :
                        t.IsSale ? Theme.Success : Theme.Info;
                    break;
                case "Profit":
                    e.Value = Theme.Money(t.Profit);
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = t.Profit < 0 ? Theme.Danger : Color.FromArgb(13, 148, 136);
                    e.CellStyle.Font = Theme.BodyBold;
                    break;
                case "Quantity":
                {
                    int q = t.Quantity;
                    switch (t.Type)
                    {
                        case TransactionType.Return:
                            e.Value = "+" + L.Num(-q);
                            e.CellStyle.ForeColor = Theme.Success;
                            break;
                        case TransactionType.Adjust:
                            e.Value = (q > 0 ? "+" : "−") + L.Num(Math.Abs(q));
                            e.CellStyle.ForeColor = q > 0 ? Theme.Success : Theme.Warning;
                            break;
                        case TransactionType.StockIn:
                            e.Value = "+" + L.Num(q);
                            e.CellStyle.ForeColor = Theme.Success;
                            break;
                        default:
                            e.Value = "−" + L.Num(q);
                            e.CellStyle.ForeColor = Theme.Danger;
                            break;
                    }
                    e.FormattingApplied = true;
                    break;
                }
            }
        }

        private void ReprintSelected()
        {
            var t = SelectedTransaction;
            if (t != null && t.IsSale && !string.IsNullOrEmpty(t.InvoiceNo))
                PrintReceipt(t.InvoiceNo);
        }

        private void UndoSelected()
        {
            var t = SelectedTransaction;
            if (t == null) return;

            string effect = t.IsSale
                ? L.F("{0} unit(s) will be returned to stock.", L.Num(t.Quantity))
                : L.F("{0} unit(s) will be removed from stock.", L.Num(t.Quantity));
            var answer = Msg.Show(FindForm(),
                L.F(t.IsSale ? "Undo this sale of \"{0}\" ({1})?\n\n{2}" : "Undo this stock-in of \"{0}\" ({1})?\n\n{2}",
                    t.ProductName, L.DateTimeText(t.CreatedAt), effect),
                "Confirm Undo", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                _transactions.Undo(t.Id);
            }
            catch (InsufficientStockException ex)
            {
                Msg.Show(FindForm(), L.T("Cannot undo this stock-in because some of those units were already sold.") + "\n\n" + ex.Message,
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RefreshProducts();
            LoadHistory();
            _main.SetStatus(L.F("{0} of \"{1}\" undone.", L.T(t.TypeDisplay), t.ProductName));
        }

        private void ExportCsv()
        {
            var list = _grid.DataSource as List<StockTransaction>;
            if (list == null || list.Count == 0)
            {
                Msg.Show(FindForm(), L.T("There is nothing to export."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Filter = L.T("CSV files (*.csv)|*.csv"),
                FileName = (_isSale ? "sales_" : "stock_in_") + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv",
                Title = L.T("Export")
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine(_isSale
                    ? "Date,Invoice,Code,Product,Quantity,Unit Price,Total,Unit Cost,Profit,Note"
                    : "Date,Invoice,Vendor,Code,Product,Quantity,Unit Cost,Total,Note");
                foreach (var t in list)
                {
                    var cells = new List<string>
                    {
                        t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ProductsView.Csv(t.InvoiceNo)
                    };
                    if (!_isSale) cells.Add(ProductsView.Csv(t.VendorName));
                    cells.Add(ProductsView.Csv(t.ProductCode));
                    cells.Add(ProductsView.Csv(t.ProductName));
                    cells.Add(t.Quantity.ToString(CultureInfo.InvariantCulture));
                    cells.Add(t.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture));
                    cells.Add(t.Total.ToString("0.00", CultureInfo.InvariantCulture));
                    if (_isSale)
                    {
                        cells.Add(t.UnitCost.ToString("0.00", CultureInfo.InvariantCulture));
                        cells.Add(t.Profit.ToString("0.00", CultureInfo.InvariantCulture));
                    }
                    cells.Add(ProductsView.Csv(t.Note));
                    sb.AppendLine(string.Join(",", cells));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                _main.SetStatus(L.F("Exported {0} rows to {1}", L.Num(list.Count), dlg.FileName));
            }
        }

        // =====================================================================
        // Small UI helpers
        // =====================================================================

        private static Button TabButton(string text)
        {
            var b = new AppButton
            {
                Text = L.T(text),
                FlatStyle = FlatStyle.Flat,
                Font = Theme.BodyBold,
                Size = new Size(104, 36),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Margin = new Padding(0)
            };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private static void StyleTab(Button b, bool active)
        {
            b.BackColor = active ? Theme.AccentSoft : Theme.Surface;
            b.ForeColor = active ? Theme.Accent : Theme.TextSecondary;
            b.FlatAppearance.BorderColor = active ? Theme.AccentSoft : Theme.Border;
            b.FlatAppearance.MouseOverBackColor = active ? Theme.AccentSoft : Theme.Background;
            b.FlatAppearance.BorderSize = 1;
        }
    }
}
