using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    public class ProductsView : UserControl, IRefreshable
    {
        private static string AllCategories { get { return L.T("All categories"); } }

        private readonly MainForm _main;
        private readonly ProductRepository _repo = new ProductRepository();
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly ComboBox _category;
        private readonly CheckBox _lowOnly;
        private readonly Button _editButton;
        private readonly Button _deleteButton;
        private readonly Button _sellButton;
        private readonly Button _adjustButton;
        private readonly Label _countLabel;
        private readonly Timer _searchDelay;
        private bool _loadingFilters;

        public ProductsView(MainForm main)
        {
            _main = main;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- Title row with action buttons ----
            var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            heading.Controls.Add(Theme.PageTitle("Products"));
            _countLabel = Theme.Caption("");
            heading.Controls.Add(_countLabel);

            var actions = new WrapFlowPanel
            {
                AutoSize = true, WrapContents = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(24, 8, 0, 0)
            };
            var addButton = Theme.PrimaryButton("+  Add Product");
            _sellButton = Theme.SecondaryButton("Sell");
            _adjustButton = Theme.SecondaryButton("Adjust stock");
            var labelsButton = Theme.SecondaryButton("Print labels");
            var orderButton = Theme.SecondaryButton("Order list");
            var categoriesButton = Theme.SecondaryButton("Categories");
            _editButton = Theme.SecondaryButton("Edit");
            _deleteButton = Theme.DangerButton("Delete");
            var exportButton = Theme.SecondaryButton("Export CSV");
            var refreshButton = Theme.SecondaryButton("Refresh");
            refreshButton.Margin = new Padding(0);
            actions.Controls.AddRange(new Control[] { refreshButton, exportButton, orderButton, labelsButton, categoriesButton, _adjustButton, _deleteButton, _editButton, _sellButton, addButton });

            titleRow.Controls.Add(heading, 0, 0);
            titleRow.Controls.Add(actions, 1, 0);

            // ---- Filter bar ----
            var filterCard = new CardPanel { Dock = DockStyle.Fill, Height = 64, Padding = new Padding(16, 14, 16, 12), Margin = new Padding(0, 0, 0, 12) };
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };

            var searchLabel = new Label { Text = L.T("Search"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 9, 8, 0) };
            _search = new TextBox { Width = 280, Font = Theme.Body, Margin = new Padding(0, 4, 24, 0) };
            SetCueBanner(_search, "Code, barcode, name, category...");

            var catLabel = new Label { Text = L.T("Category"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 9, 8, 0) };
            _category = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Margin = new Padding(0, 4, 24, 0) };

            _lowOnly = new CheckBox { Text = L.T("Low stock only"), AutoSize = true, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 8, 0, 0) };

            filters.Controls.AddRange(new Control[] { searchLabel, _search, catLabel, _category, _lowOnly });
            filterCard.Controls.Add(filters);

            // ---- Grid ----
            var gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("Code", "Code", 60));
            _grid.Columns.Add(DashboardView.Col("Barcode", "Barcode", 90));
            _grid.Columns.Add(DashboardView.Col("Name", "Product Name", 150));
            _grid.Columns.Add(DashboardView.Col("Category", "Category", 85));
            _grid.Columns.Add(DashboardView.Col("VendorName", "Vendor", 95));
            _grid.Columns.Add(DashboardView.Col("PurchasePrice", "Purchase", 80, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("Price", "Sale Price", 80, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("ProfitPerUnit", "Profit/Unit", 75, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("Quantity", "Qty", 50, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("StockValue", "Stock Value", 90, DataGridViewContentAlignment.MiddleRight));
            foreach (DataGridViewColumn c in _grid.Columns)
                c.SortMode = DataGridViewColumnSortMode.Programmatic;
            gridCard.Controls.Add(_grid);

            layout.Controls.Add(titleRow, 0, 0);
            layout.Controls.Add(filterCard, 0, 1);
            layout.Controls.Add(gridCard, 0, 2);
            Controls.Add(layout);

            // ---- Events ----
            _searchDelay = new Timer { Interval = 300 };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); LoadProducts(); };
            _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
            _category.SelectedIndexChanged += (s, e) => { if (!_loadingFilters) LoadProducts(); };
            _lowOnly.CheckedChanged += (s, e) => LoadProducts();

            addButton.Click += (s, e) => AddProduct();
            _editButton.Click += (s, e) => EditSelected();
            _deleteButton.Click += (s, e) => DeleteSelected();
            exportButton.Click += (s, e) => ExportCsv();
            _sellButton.Click += (s, e) => { var p = SelectedProduct; if (p != null) _main.OpenSales(p.Id); };
            _adjustButton.Click += (s, e) => AdjustSelected();
            labelsButton.Click += (s, e) =>
            {
                using (var dlg = new LabelPrintForm(SelectedProduct))
                    dlg.ShowDialog(FindForm());
            };
            orderButton.Click += (s, e) =>
            {
                using (var dlg = new PurchaseOrderForm())
                    dlg.ShowDialog(FindForm());
            };
            categoriesButton.Click += (s, e) =>
            {
                using (var dlg = new CategoriesForm())
                {
                    dlg.ShowDialog(FindForm());
                    if (dlg.Changed) RefreshData();
                }
            };
            refreshButton.Click += (s, e) => RefreshData();

            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            _grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = true; }
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Shortcuts.Is(Shortcuts.New, keyData)) { AddProduct(); return true; }
            if (Shortcuts.Is(Shortcuts.Search, keyData)) { _search.Focus(); _search.SelectAll(); return true; }
            if (Shortcuts.Is(Shortcuts.Refresh, keyData)) { RefreshData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void RefreshData()
        {
            // Staff mode hides money the owner wants private.
            bool owner = Session.IsOwner;
            _grid.Columns["PurchasePrice"].Visible = owner;
            _grid.Columns["ProfitPerUnit"].Visible = owner;
            _deleteButton.Visible = owner;
            _adjustButton.Visible = owner;

            LoadCategories();
            LoadProducts();
            _grid.Focus();
        }

        private void AdjustSelected()
        {
            var p = SelectedProduct;
            if (p == null) return;
            var fresh = _repo.GetById(p.Id);
            if (fresh == null) { LoadProducts(); return; }
            using (var dlg = new AdjustStockForm(fresh))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadProducts(p.Id);
                    _main.SetStatus(L.F("Stock of \"{0}\" adjusted.", fresh.Name));
                }
            }
        }

        private void LoadCategories()
        {
            _loadingFilters = true;
            var current = _category.SelectedItem as string;
            _category.Items.Clear();
            _category.Items.Add(AllCategories);
            foreach (var c in _repo.GetCategories())
                _category.Items.Add(c);
            int idx = current != null ? _category.Items.IndexOf(current) : 0;
            _category.SelectedIndex = idx >= 0 ? idx : 0;
            _loadingFilters = false;
        }

        private string _sortColumn = "Name";
        private bool _sortAscending = true;

        private void LoadProducts(long selectId = 0)
        {
            if (selectId == 0 && SelectedProduct != null)
                selectId = SelectedProduct.Id;

            var cat = _category.SelectedIndex > 0 ? (string)_category.SelectedItem : null;
            var products = Sort(_repo.GetAll(_search.Text, cat, _lowOnly.Checked));

            _grid.DataSource = products;

            foreach (DataGridViewColumn c in _grid.Columns)
                c.HeaderCell.SortGlyphDirection = SortOrder.None;
            _grid.Columns[_sortColumn].HeaderCell.SortGlyphDirection = _sortAscending ? SortOrder.Ascending : SortOrder.Descending;

            _grid.ClearSelection();
            if (selectId > 0)
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (((Product)row.DataBoundItem).Id == selectId)
                    {
                        row.Selected = true;
                        _grid.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }

            int lowCount = products.Count(p => p.IsLowStock);
            _countLabel.Text = L.F(products.Count == 1 ? "{0} product shown" : "{0} products shown", L.Num(products.Count)) +
                (lowCount > 0 ? "  ·  " + L.F("{0} low on stock", L.Num(lowCount)) : "");
            _main.SetStatus(L.F("{0} record(s)", L.Num(products.Count)));
            UpdateButtons();
        }

        private List<Product> Sort(List<Product> list)
        {
            Func<Product, object> key;
            switch (_sortColumn)
            {
                case "Code": key = p => p.Code; break;
                case "Barcode": key = p => p.Barcode; break;
                case "Category": key = p => p.Category; break;
                case "Price": key = p => p.Price; break;
                case "Quantity": key = p => p.Quantity; break;
                case "VendorName": key = p => p.VendorName; break;
                case "PurchasePrice": key = p => p.PurchasePrice; break;
                case "ProfitPerUnit": key = p => p.ProfitPerUnit; break;
                case "StockValue": key = p => p.StockValue; break;
                default: key = p => p.Name; break;
            }
            var comparer = Comparer<object>.Create((a, b) =>
            {
                var sa = a as string;
                var sb = b as string;
                if (sa != null || sb != null)
                    return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                return Comparer<object>.Default.Compare(a, b);
            });
            return (_sortAscending ? list.OrderBy(key, comparer) : list.OrderByDescending(key, comparer)).ToList();
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var name = _grid.Columns[e.ColumnIndex].Name;
            _sortAscending = name == _sortColumn ? !_sortAscending : true;
            _sortColumn = name;
            LoadProducts();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var p = _grid.Rows[e.RowIndex].DataBoundItem as Product;
            if (p == null) return;

            var col = _grid.Columns[e.ColumnIndex].Name;
            if (col == "ProfitPerUnit" && p.PurchasePrice > 0)
                e.CellStyle.ForeColor = p.ProfitPerUnit < 0 ? Theme.Danger : Color.FromArgb(13, 148, 136);
            if (col == "ProfitPerUnit" && p.PurchasePrice <= 0)
            {
                e.Value = "-";
                e.FormattingApplied = true;
            }
            else if (col == "Price" || col == "StockValue" || col == "PurchasePrice" || col == "ProfitPerUnit")
            {
                e.Value = Theme.Money((decimal)e.Value);
                e.FormattingApplied = true;
            }

            if (p.IsLowStock)
            {
                e.CellStyle.BackColor = Theme.WarningRow;
                if (col == "Quantity")
                {
                    e.CellStyle.ForeColor = Theme.Warning;
                    e.CellStyle.Font = Theme.BodyBold;
                }
            }
        }

        private Product SelectedProduct
        {
            get
            {
                return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as Product : null;
            }
        }

        private void UpdateButtons()
        {
            bool has = SelectedProduct != null;
            _editButton.Enabled = has;
            _deleteButton.Enabled = has;
            _sellButton.Enabled = has;
            _adjustButton.Enabled = has;
            _deleteButton.BackColor = has ? Theme.Danger : Color.FromArgb(252, 165, 165);
        }

        private void AddProduct()
        {
            using (var dlg = new ProductEditForm(null, _repo.GetCategories()))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadCategories();
                    LoadProducts(dlg.Product.Id);
                    _main.SetStatus(L.F("Product \"{0}\" added.", dlg.Product.Name));
                }
            }
        }

        private void EditSelected()
        {
            var selected = SelectedProduct;
            if (selected == null) return;

            var fresh = _repo.GetById(selected.Id);
            if (fresh == null)
            {
                Msg.Show(FindForm(), L.T("This product no longer exists."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadProducts();
                return;
            }

            using (var dlg = new ProductEditForm(fresh, _repo.GetCategories()))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadCategories();
                    LoadProducts(fresh.Id);
                    _main.SetStatus(L.F("Product \"{0}\" updated.", fresh.Name));
                }
            }
        }

        private void DeleteSelected()
        {
            var p = SelectedProduct;
            if (p == null) return;

            var answer = Msg.Show(FindForm(),
                L.F("Delete product \"{0}\" ({1})?\n\nThis cannot be undone.", p.Name, p.Code),
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            int index = _grid.SelectedRows[0].Index;
            _repo.Delete(p.Id);
            LoadCategories();
            LoadProducts(-1);

            if (_grid.Rows.Count > 0)
            {
                var row = _grid.Rows[Math.Min(index, _grid.Rows.Count - 1)];
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
            }
            _main.SetStatus(L.F("Product \"{0}\" deleted.", p.Name));
        }

        private void ExportCsv()
        {
            var products = _grid.DataSource as List<Product>;
            if (products == null || products.Count == 0)
            {
                Msg.Show(FindForm(), L.T("There are no products to export."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Filter = L.T("CSV files (*.csv)|*.csv"),
                FileName = "products_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv",
                Title = L.T("Export Products")
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine("Code,Barcode,Name,Category,Vendor,Purchase Rate,Sale Price,Profit per Unit,Quantity,Reorder Level,Stock Value,Description,Last Updated");
                foreach (var p in products)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(p.Code), CsvDigits(p.Barcode), Csv(p.Name), Csv(p.Category), Csv(p.VendorName),
                        p.PurchasePrice.ToString("0.00", CultureInfo.InvariantCulture),
                        p.Price.ToString("0.00", CultureInfo.InvariantCulture),
                        p.ProfitPerUnit.ToString("0.00", CultureInfo.InvariantCulture),
                        p.Quantity.ToString(CultureInfo.InvariantCulture),
                        p.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                        p.StockValue.ToString("0.00", CultureInfo.InvariantCulture),
                        Csv(p.Description),
                        p.UpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    }));
                }
                // UTF-8 with BOM so Excel opens non-ASCII text correctly.
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                _main.SetStatus(L.F("Exported {0} products to {1}", L.Num(products.Count), dlg.FileName));
            }
        }

        /// <summary>Long digit strings (barcodes) as ="..." so Excel keeps them as text, not 8.9E+12.</summary>
        internal static string CsvDigits(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return "=\"" + value.Replace("\"", "") + "\"";
        }

        internal static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        internal static void SetCueBanner(TextBox box, string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            var cue = L.T(text);
            box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
        }
    }
}
