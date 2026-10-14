using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>Shop expenses: rent, bills, salary... Totals feed the dashboard's net profit.</summary>
    public class ExpensesView : UserControl, IRefreshable
    {
        private readonly MainForm _main;
        private readonly ExpenseRepository _repo = new ExpenseRepository();
        private readonly DateRangeBar _rangeBar;
        private readonly DataGridView _grid;
        private readonly Label _count;
        private readonly Button _edit;
        private readonly Button _delete;

        public ExpensesView(MainForm main)
        {
            _main = main;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            heading.Controls.Add(Theme.PageTitle("Expenses"));
            _count = Theme.Caption("");
            heading.Controls.Add(_count);

            var actions = new WrapFlowPanel
            {
                AutoSize = true, WrapContents = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(24, 8, 0, 0)
            };
            var add = Theme.PrimaryButton("+  Add Expense");
            _edit = Theme.SecondaryButton("Edit");
            _delete = Theme.DangerButton("Delete");
            actions.Controls.AddRange(new Control[] { _delete, _edit, add });

            titleRow.Controls.Add(heading, 0, 0);
            titleRow.Controls.Add(actions, 1, 0);

            var filterRow = new Panel { Dock = DockStyle.Fill, Height = 52 };
            _rangeBar = new DateRangeBar { Location = new Point(0, 4) };
            _rangeBar.Select(RangePreset.ThisMonth);
            filterRow.Controls.Add(_rangeBar);
            _rangeBar.RangeChanged += (s, e) => LoadList(0);

            var gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 8, 0, 0) };
            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.Columns.Add(DashboardView.Col("Date", "Date", 80));
            _grid.Columns["Date"].DefaultCellStyle.Format = "dd/MM/yyyy";
            _grid.Columns.Add(DashboardView.Col("Category", "Category", 110));
            _grid.Columns.Add(DashboardView.Col("Amount", "Amount", 90, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(DashboardView.Col("Note", "Note", 220));
            gridCard.Controls.Add(_grid);

            layout.Controls.Add(titleRow, 0, 0);
            layout.Controls.Add(filterRow, 0, 1);
            layout.Controls.Add(gridCard, 0, 2);
            Controls.Add(layout);

            add.Click += (s, e) => AddExpense();
            _edit.Click += (s, e) => EditSelected();
            _delete.Click += (s, e) => DeleteSelected();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _grid.SelectionChanged += (s, e) => { _edit.Enabled = _delete.Enabled = Selected != null; };
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; } };
            _grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (_grid.Columns[e.ColumnIndex].Name == "Amount")
                {
                    e.Value = Theme.Money(((Expense)_grid.Rows[e.RowIndex].DataBoundItem).Amount);
                    e.FormattingApplied = true;
                    e.CellStyle.Font = Theme.Num(10f, true);
                }
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Shortcuts.Is(Shortcuts.New, keyData)) { AddExpense(); return true; }
            if (Shortcuts.Is(Shortcuts.Refresh, keyData)) { RefreshData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void RefreshData()
        {
            _rangeBar.Recompute();
            LoadList(0);
        }

        private Expense Selected
        {
            get { return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as Expense : null; }
        }

        private void LoadList(long selectId)
        {
            if (selectId == 0 && Selected != null) selectId = Selected.Id;
            var range = _rangeBar.Range;
            var list = _repo.GetAll(range.From, range.To);
            _grid.DataSource = list;
            _grid.ClearSelection();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (((Expense)row.DataBoundItem).Id == selectId)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
            _count.Text = L.F("{0} expense(s)  ·  total {1}", L.Num(list.Count), Theme.Money(list.Sum(x => x.Amount)));
            _edit.Enabled = _delete.Enabled = Selected != null;
        }

        private void AddExpense()
        {
            using (var dlg = new ExpenseEditForm(null, _repo))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadList(dlg.Expense.Id);
                    _main.SetStatus(L.T("Expense saved."));
                }
            }
        }

        private void EditSelected()
        {
            var x = Selected;
            if (x == null) return;
            using (var dlg = new ExpenseEditForm(x, _repo))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    LoadList(x.Id);
            }
        }

        private void DeleteSelected()
        {
            var x = Selected;
            if (x == null) return;
            if (Msg.Show(FindForm(), L.F("Delete this expense of {0}?", Theme.Money(x.Amount)),
                    AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _repo.Delete(x.Id);
            LoadList(0);
        }
    }

    /// <summary>Add / edit one expense.</summary>
    public class ExpenseEditForm : Form
    {
        private readonly ExpenseRepository _repo;
        private readonly bool _isNew;
        private readonly DateTimePicker _date;
        private readonly ComboBox _category;
        private readonly NumericUpDown _amount;
        private readonly TextBox _note;

        public Expense Expense { get; private set; }

        public ExpenseEditForm(Expense expense, ExpenseRepository repo)
        {
            _repo = repo;
            _isNew = expense == null;
            Expense = expense ?? new Expense { Date = DateTime.Today };

            Text = L.T(_isNew ? "Add Expense" : "Edit Expense");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 400);

            var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = L.T(_isNew ? "New Expense" : "Edit Expense"), Font = Theme.Semi(13f), AutoSize = true, Location = new Point(24, 12) });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 14, 36, 8), BackColor = Theme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowCount = 3;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _date = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Font = Theme.Num(10f) };
            _amount = new NumericUpDown { Dock = DockStyle.Fill, DecimalPlaces = 2, Maximum = 100000000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Font = Theme.Num(11f) };
            _category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 60 };
            _category.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _category.AutoCompleteSource = AutoCompleteSource.ListItems;
            foreach (var c in repo.Categories()) _category.Items.Add(c);
            _note = new TextBox { Dock = DockStyle.Fill, MaxLength = 300, Multiline = true, ScrollBars = ScrollBars.Vertical };

            CustomerEditForm.Field(grid, "Date", _date, 0, 0, 1, 62);
            CustomerEditForm.Field(grid, "Amount *", _amount, 1, 0, 1, 62);
            CustomerEditForm.Field(grid, "Category", _category, 0, 1, 2, 62);
            CustomerEditForm.Field(grid, "Note", _note, 0, 2, 2, 110);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
            var save = Theme.PrimaryButton(_isNew ? "Add Expense" : "Save Changes");
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
            grid.BringToFront();   // fill only the space between header and footer
            AcceptButton = save;
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            save.Click += (s, e) => Save();

            _date.Value = Expense.Date == DateTime.MinValue ? DateTime.Today : Expense.Date;
            _amount.Value = Math.Max(_amount.Minimum, Math.Min(_amount.Maximum, Expense.Amount));
            _category.Text = Expense.Category;
            _note.Text = Expense.Note;
            Shown += (s, e) => _amount.Focus();
        }

        private void Save()
        {
            if (_amount.Value <= 0)
            {
                Msg.Show(this, L.T("Please enter a valid amount."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Expense.Date = _date.Value.Date;
            Expense.Category = _category.Text.Trim();
            Expense.Amount = _amount.Value;
            Expense.Note = _note.Text.Trim();
            if (_isNew) _repo.Insert(Expense);
            else _repo.Update(Expense);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
