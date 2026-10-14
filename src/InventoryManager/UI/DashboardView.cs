using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>
    /// Dashboard. A date filter (today / yesterday / this month / last month / custom) scopes the
    /// period cards, both charts and the transaction list; the stock cards always show the current stock.
    /// </summary>
    public class DashboardView : UserControl, IRefreshable
    {
        private static readonly Color Teal = Color.FromArgb(13, 148, 136);
        private static readonly Color Purple = Color.FromArgb(124, 58, 237);

        private readonly MainForm _main;
        private readonly ProductRepository _repo = new ProductRepository();
        private readonly TransactionRepository _transactions = new TransactionRepository();
        private readonly ExpenseRepository _expenseRepo = new ExpenseRepository();
        private readonly DateRangeBar _rangeBar;
        private bool _refreshingFromFilter;

        // Period cards
        private readonly Label _sales, _profit, _count, _units, _expenses, _net;
        private readonly Label _salesDelta, _profitDelta, _countDelta, _unitsDelta, _expensesDelta, _netDelta;
        private readonly Button _reportButton;

        // Charts
        private readonly ColumnChart _trend;
        private readonly Label _trendSubtitle;
        private readonly RankedBars _top;

        // Current stock
        private readonly Label _totalProducts, _totalUnits, _inventoryValue, _lowStock;

        // Lists
        private readonly DataGridView _recentGrid;
        private readonly DataGridView _lowGrid;

        public DashboardView(MainForm main)
        {
            _main = main;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            AutoScroll = true;
            Padding = new Padding(0, 0, 6, 8);

            // ---- Heading ----
            var heading = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            heading.Controls.Add(Theme.PageTitle("Overview"));
            heading.Controls.Add(Theme.Caption("Sales, profit and stock at a glance."));

            // ---- Filter row (one row, above everything it scopes) ----
            var filterRow = new Panel { Dock = DockStyle.Top, Height = 52 };
            _rangeBar = new DateRangeBar { Location = new Point(0, 4) };
            filterRow.Controls.Add(_rangeBar);
            _reportButton = Theme.SecondaryButton("Print report...");
            filterRow.Controls.Add(_reportButton);
            filterRow.Layout += (s, e) => _reportButton.Location = new Point(filterRow.Width - _reportButton.Width - 8, 7);
            _reportButton.Click += (s, e) =>
            {
                _rangeBar.Recompute();
                Printing.ReportPrinter.ShowReport(FindForm(), _rangeBar.Range);
            };
            _rangeBar.RangeChanged += (s, e) =>
            {
                _refreshingFromFilter = true;
                try { RefreshData(); } finally { _refreshingFromFilter = false; }
            };

            // ---- Period cards (2 rows x 3) ----
            var period = new TableLayoutPanel { Dock = DockStyle.Top, Height = 254, ColumnCount = 3, RowCount = 2, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++)
                period.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            period.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            period.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            period.Controls.Add(StatCard("Total sales", ChartColors.Series1, out _sales, out _salesDelta), 0, 0);
            period.Controls.Add(StatCard("Profit", ChartColors.Series2, out _profit, out _profitDelta), 1, 0);
            period.Controls.Add(StatCard("Number of sales", Teal, out _count, out _countDelta), 2, 0);
            period.Controls.Add(StatCard("Expenses", Color.FromArgb(217, 119, 6), out _expenses, out _expensesDelta), 0, 1);
            period.Controls.Add(StatCard("Net profit (after expenses)", Theme.Accent, out _net, out _netDelta), 1, 1);
            period.Controls.Add(StatCard("Units sold", Purple, out _units, out _unitsDelta), 2, 1);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    var cardCtl = period.GetControlFromPosition(c, r);
                    cardCtl.Margin = new Padding(0, r == 0 ? 0 : 14, c == 2 ? 0 : 14, 0);
                }

            // ---- Charts ----
            var charts = new TableLayoutPanel { Dock = DockStyle.Top, Height = 340, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            charts.Padding = new Padding(0, 14, 0, 0);

            _trend = new ColumnChart { Dock = DockStyle.Fill, EmptyText = L.T("No sales in this period") };
            _trendSubtitle = new Label();
            charts.Controls.Add(ChartCard("Sales & profit", _trendSubtitle, _trend, new Padding(0, 0, 14, 0)), 0, 0);

            _top = new RankedBars { Dock = DockStyle.Fill, EmptyText = L.T("No sales in this period") };
            var topSubtitle = new Label { Text = L.T("By sales amount") };
            charts.Controls.Add(ChartCard("Top-selling products", topSubtitle, _top, new Padding(0)), 1, 0);

            // ---- Current stock (not filtered by date) ----
            var stockLabel = new Panel { Dock = DockStyle.Top, Height = 46 };
            var stockTitle = new Label { Text = L.T("Current stock"), Font = Theme.Semi(11.5f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 18), UseMnemonic = false };
            var stockNote = new Label { Text = L.T("(not affected by the date filter)"), ForeColor = Theme.TextMuted, AutoSize = true, UseMnemonic = false };
            stockLabel.Controls.Add(stockTitle);
            stockLabel.Controls.Add(stockNote);
            stockLabel.Layout += (s, e) => stockNote.Location = new Point(stockTitle.Right + 8, 21);

            var stock = CardRow(104);
            stock.Controls.Add(StatCard("Total Products", Theme.Accent, out _totalProducts), 0, 0);
            stock.Controls.Add(StatCard("Units in Stock", Theme.Success, out _totalUnits), 1, 0);
            stock.Controls.Add(StatCard("Stock Value (at cost)", Purple, out _inventoryValue), 2, 0);
            stock.Controls.Add(StatCard("Low Stock Items", Theme.Warning, out _lowStock), 3, 0);
            LastCardFlush(stock);

            // ---- Lists ----
            var bottom = new TableLayoutPanel { Dock = DockStyle.Top, Height = 360, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 14, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

            _recentGrid = CreateGrid();
            _recentGrid.Columns.Add(Col("CreatedAt", "Time", 135));
            _recentGrid.Columns["CreatedAt"].DefaultCellStyle.Format = "dd/MM, hh:mm tt";
            _recentGrid.Columns.Add(Col("TypeDisplay", "Type", 65));
            _recentGrid.Columns.Add(Col("ProductName", "Product", 120));
            _recentGrid.Columns.Add(Col("Quantity", "Qty", 60, DataGridViewContentAlignment.MiddleRight));
            _recentGrid.Columns.Add(Col("Total", "Total", 100, DataGridViewContentAlignment.MiddleRight));
            _recentGrid.Columns.Add(Col("Profit", "Profit", 95, DataGridViewContentAlignment.MiddleRight));
            _recentGrid.CellFormatting += RecentGrid_CellFormatting;
            _recentGrid.CellDoubleClick += (s, e) =>
            {
                var t = e.RowIndex >= 0 ? _recentGrid.Rows[e.RowIndex].DataBoundItem as StockTransaction : null;
                _main.Navigate(t != null && !t.IsSale ? "Stock In" : "Sales");
            };

            _lowGrid = CreateGrid();
            _lowGrid.CellDoubleClick += (s, e) => _main.Navigate("Products");
            _lowGrid.Columns.Add(Col("Name", "Product", 140));
            _lowGrid.Columns.Add(Col("Quantity", "Qty", 50, DataGridViewContentAlignment.MiddleRight));
            _lowGrid.Columns.Add(Col("ReorderLevel", "Reorder At", 70, DataGridViewContentAlignment.MiddleRight));

            bottom.Controls.Add(GridCard("Transactions in this period", _recentGrid, new Padding(0, 0, 14, 0)), 0, 0);
            bottom.Controls.Add(GridCard("Needs Restocking", _lowGrid, new Padding(0)), 1, 0);

            // Dock=Top stacks in reverse order of adding.
            foreach (var c in new Control[] { bottom, stock, stockLabel, charts, period, filterRow, heading })
                Controls.Add(c);
        }

        /// <summary>Don't jump the page when a control inside it gets focus (keeps the view at the top).</summary>
        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }

        private static TableLayoutPanel CardRow(int height)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = height, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++)
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            return row;
        }

        /// <summary>The last card in a row has no right margin, so rows line up with the charts below.</summary>
        private static void LastCardFlush(TableLayoutPanel row)
        {
            var last = row.GetControlFromPosition(3, 0);
            if (last != null) last.Margin = new Padding(0);
        }

        private static Control ChartCard(string title, Label subtitle, Control chart, Padding margin)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = margin, Padding = new Padding(20, 14, 20, 14) };
            var head = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface };
            var t = new Label { Text = L.T(title), Font = Theme.Semi(11f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0), UseMnemonic = false };
            subtitle.ForeColor = Theme.TextSecondary;
            subtitle.Font = Theme.Small;
            subtitle.AutoSize = true;
            subtitle.UseMnemonic = false;
            subtitle.Location = new Point(1, 24);
            head.Controls.Add(t);
            head.Controls.Add(subtitle);
            card.Controls.Add(chart);
            card.Controls.Add(head);
            return card;
        }

        public void RefreshData()
        {
            bool fromNavigation = !_refreshingFromFilter;
            if (fromNavigation) AutoScrollPosition = new Point(0, 0);   // opening the dashboard starts at the top
            _rangeBar.Recompute();
            var range = _rangeBar.Range;

            // Period cards with change vs the previous period.
            bool owner = Session.IsOwner;
            _reportButton.Visible = owner;
            var cur = _transactions.GetSalesSummary(range.From, range.To);
            var prev = _transactions.GetSalesSummary(range.PrevFrom, range.PrevTo);
            decimal curExp = _expenseRepo.Total(range.From, range.To);
            decimal prevExp = _expenseRepo.Total(range.PrevFrom, range.PrevTo);
            _sales.Text = Theme.Money(cur.Revenue);
            _count.Text = L.Num(cur.SaleCount);
            _units.Text = L.Num(cur.UnitsSold);
            ShowDelta(_salesDelta, cur.Revenue, prev.Revenue, range.CompareText);
            ShowDelta(_countDelta, cur.SaleCount, prev.SaleCount, range.CompareText);
            ShowDelta(_unitsDelta, cur.UnitsSold, prev.UnitsSold, range.CompareText);
            if (owner)
            {
                _profit.Text = Theme.Money(cur.Profit);
                _profit.ForeColor = cur.Profit < 0 ? Theme.Danger : Theme.TextPrimary;
                _expenses.Text = Theme.Money(curExp);
                _expenses.ForeColor = Theme.TextPrimary;   // may still be grey from the locked state
                decimal curNet = cur.Profit - curExp, prevNet = prev.Profit - prevExp;
                _net.Text = Theme.Money(curNet);
                _net.ForeColor = curNet < 0 ? Theme.Danger : Theme.TextPrimary;
                ShowDelta(_profitDelta, cur.Profit, prev.Profit, range.CompareText);
                ShowDelta(_expensesDelta, curExp, prevExp, range.CompareText);
                ShowDelta(_netDelta, curNet, prevNet, range.CompareText);
            }
            else
            {
                // Staff mode: profit and expense figures stay the owner's business.
                foreach (var lbl in new[] { _profit, _expenses, _net })
                {
                    lbl.Text = "••••";
                    lbl.ForeColor = Theme.TextMuted;
                }
                foreach (var lbl in new[] { _profitDelta, _expensesDelta, _netDelta })
                    lbl.Text = "";
            }

            LoadTrend(range);
            _top.SetData(_transactions.GetTopProducts(range.From, range.To, 6));

            // Current stock
            var stats = _repo.GetStats();
            _totalProducts.Text = L.Num(stats.TotalProducts);
            _totalUnits.Text = L.Num(stats.TotalUnits);
            _inventoryValue.Text = owner ? Theme.Money(stats.InventoryCost) : "••••";
            _lowStock.Text = L.Num(stats.LowStockCount);

            _recentGrid.Columns["Profit"].Visible = owner;
            _recentGrid.DataSource = _transactions.GetTransactions(range.From, null, null, 100, range.To);
            _lowGrid.DataSource = _repo.GetAll(lowStockOnly: true);
            _recentGrid.ClearSelection();
            _lowGrid.ClearSelection();

            _main.SetStatus(L.F("{0} products in {1} categories  ·  stock value at sale price {2}",
                L.Num(stats.TotalProducts), L.Num(stats.CategoryCount), Theme.Money(stats.InventoryValue)));
        }

        /// <summary>Hourly for a single day, daily up to ~2 months, monthly beyond that.</summary>
        private void LoadTrend(DateRange range)
        {
            string bucket = range.Days == 1 ? "hour" : range.Days <= 62 ? "day" : "month";
            var data = _transactions.GetSalesSeries(range.From, range.To, bucket);
            var labels = new List<string>();
            var titles = new List<string>();
            var keys = new List<string>();

            if (bucket == "hour")
            {
                for (int h = 0; h < 24; h++)
                {
                    keys.Add(h.ToString("00"));
                    labels.Add(L.Digits(h.ToString("00")));
                    titles.Add(L.Digits(h.ToString("00") + ":00 – " + ((h + 1) % 24).ToString("00") + ":00"));
                }
                _trendSubtitle.Text = L.T("By hour") + "  ·  " + range.Describe();
            }
            else if (bucket == "day")
            {
                for (var d = range.From; d < range.To; d = d.AddDays(1))
                {
                    keys.Add(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    labels.Add(L.Date(d, "%d"));
                    titles.Add(L.LongDate(d));
                }
                _trendSubtitle.Text = L.T("By day") + "  ·  " + range.Describe();
            }
            else
            {
                for (var m = new DateTime(range.From.Year, range.From.Month, 1); m < range.To; m = m.AddMonths(1))
                {
                    keys.Add(m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
                    labels.Add(L.Date(m, "MMM"));
                    titles.Add(L.Date(m, "MMMM yyyy"));
                }
                _trendSubtitle.Text = L.T("By month") + "  ·  " + range.Describe();
            }

            var sales = new decimal[keys.Count];
            var profit = new decimal[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                Tuple<decimal, decimal> v;
                if (data.TryGetValue(keys[i], out v)) { sales[i] = v.Item1; profit[i] = v.Item2; }
            }
            if (Session.IsOwner)
                _trend.SetData(labels.ToArray(), titles.ToArray(),
                    new[] { L.T("Total sales"), L.T("Profit") },
                    new[] { ChartColors.Series1, ChartColors.Series2 },
                    new[] { sales, profit });
            else
                _trend.SetData(labels.ToArray(), titles.ToArray(),
                    new[] { L.T("Total sales") },
                    new[] { ChartColors.Series1 },
                    new[] { sales });
        }

        /// <summary>"+12.5% vs yesterday" in green, "−8% …" in red; no comparison when the previous period was 0.</summary>
        private static void ShowDelta(Label label, decimal current, decimal previous, string compareText)
        {
            if (previous == 0)
            {
                label.Text = current == 0 ? "" : L.T("No sales in the previous period");
                label.ForeColor = Theme.TextMuted;
                return;
            }
            decimal pct = (current - previous) / Math.Abs(previous) * 100m;
            if (Math.Abs(pct) < 0.05m)
            {
                label.Text = L.T("No change") + "  " + L.T(compareText);
                label.ForeColor = Theme.TextMuted;
                return;
            }
            var sign = pct > 0 ? "+" : "−";
            label.Text = sign + L.Num(Math.Abs(pct), 1) + "%  " + L.T(compareText);
            label.ForeColor = pct > 0 ? Theme.Success : Theme.Danger;
        }

        private void RecentGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var t = _recentGrid.Rows[e.RowIndex].DataBoundItem as StockTransaction;
            if (t == null) return;

            switch (_recentGrid.Columns[e.ColumnIndex].Name)
            {
                case "Total":
                    e.Value = Theme.Money(t.Total);
                    e.FormattingApplied = true;
                    break;
                case "Quantity":
                    e.Value = (t.IsSale ? "−" : "+") + L.Num(t.Quantity);
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = t.IsSale ? Theme.Danger : Theme.Success;
                    break;
                case "TypeDisplay":
                    e.Value = L.T(t.TypeDisplay);
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = t.IsSale ? Theme.Success : Theme.Info;
                    break;
                case "Profit":
                    e.Value = t.IsSale ? Theme.Money(t.Profit) : "";
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = t.Profit < 0 ? Theme.Danger : Teal;
                    break;
            }
        }

        internal static Control StatCard(string caption, Color accent, out Label value, out Label delta)
        {
            var card = (CardPanel)StatCard(caption, accent, out value);
            delta = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                UseMnemonic = false,
                AutoEllipsis = true
            };
            card.Controls.Add(delta);
            delta.SendToBack();   // dock below the value
            return card;
        }

        internal static Control StatCard(string caption, Color accent, out Label value)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(20, 16, 16, 12) };
            var cap = new Label
            {
                Text = L.T(caption),
                Font = Theme.Body,
                ForeColor = Theme.TextSecondary,
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(16, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                AutoEllipsis = true
            };
            // Small colored dot instead of a heavy accent bar.
            cap.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(accent))
                    e.Graphics.FillEllipse(b, 1, cap.Height / 2f - 4, 8, 8);
            };
            value = new StatValueLabel { Text = "0", Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 0) };
            card.Controls.Add(value);
            card.Controls.Add(cap);
            return card;
        }

        private Control GridCard(string title, DataGridView grid, Padding margin)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = margin, Padding = new Padding(1) };
            var header = new Label
            {
                Text = L.T(title),
                UseMnemonic = false,
                Font = Theme.BodyBold,
                ForeColor = Theme.TextPrimary,
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(16, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grid.Dock = DockStyle.Fill;
            card.Controls.Add(grid);
            card.Controls.Add(header);
            return card;
        }

        private DataGridView CreateGrid()
        {
            var g = new AppGrid { AutoGenerateColumns = false };
            Theme.StyleGrid(g);
            return g;
        }

        internal static DataGridViewTextBoxColumn Col(string property, string header, int fillWeight,
            DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleLeft)
        {
            var c = new DataGridViewTextBoxColumn
            {
                Name = property,
                DataPropertyName = property,
                HeaderText = L.T(header),
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            c.DefaultCellStyle.Alignment = align;
            c.HeaderCell.Style.Alignment = align;
            return c;
        }
    }

    /// <summary>
    /// Big single-line number for stat cards. Shrinks its font at paint time so long values
    /// (e.g. currency) always fit, and stays vertically centred.
    /// </summary>
    public class StatValueLabel : Label
    {
        public StatValueLabel()
        {
            Font = Theme.StatValue;
            ForeColor = Theme.TextPrimary;
            UseMnemonic = false;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var area = new Rectangle(Padding.Left, 0, Math.Max(1, ClientSize.Width - Padding.Horizontal), ClientSize.Height);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                                          TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            float size = Font.Size;
            Font font = null;
            // Numbers/amounts are drawn with the number font; anything else with the label's own font.
            var family = L.IsNumeric(Text) ? FontManager.Numbers : Font.FontFamily;
            var style = L.IsNumeric(Text) ? FontStyle.Bold : Font.Style;
            try
            {
                while (true)
                {
                    font = new Font(family, size, style);
                    if (size <= 9f || TextRenderer.MeasureText(e.Graphics, Text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width <= area.Width)
                        break;
                    font.Dispose();
                    size -= 1f;
                }
                TextRenderer.DrawText(e.Graphics, Text, font, area, ForeColor, flags);
            }
            finally
            {
                if (font != null) font.Dispose();
            }
        }
    }
}
