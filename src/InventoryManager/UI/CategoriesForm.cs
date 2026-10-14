using System;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.UI
{
    /// <summary>Add, rename and delete product categories.</summary>
    public class CategoriesForm : Form
    {
        private readonly CategoryRepository _repo = new CategoryRepository();
        private readonly DataGridView _grid;
        private readonly TextBox _name;
        private readonly Button _rename;
        private readonly Button _delete;

        /// <summary>True when anything was changed (so the caller can refresh).</summary>
        public bool Changed { get; private set; }

        public CategoriesForm()
        {
            Text = L.T("Categories");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 520);

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            header.Controls.Add(new Label { Text = L.T("Product Categories"), Font = Theme.Semi(13f), AutoSize = true, Location = new Point(20, 10) });
            header.Controls.Add(new Label { Text = L.T("Add, rename or delete categories used to group products."), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(22, 38) });

            var addBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 56, ColumnCount = 2, Padding = new Padding(20, 12, 20, 8), BackColor = Theme.Surface };
            addBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _name = new TextBox { Dock = DockStyle.Fill, Font = Theme.Body, MaxLength = 60, Margin = new Padding(0, 4, 8, 0) };
            ProductsView.SetCueBanner(_name, "New category name");
            var add = Theme.PrimaryButton("Add");
            add.Margin = new Padding(0);
            addBar.Controls.Add(_name, 0, 0);
            addBar.Controls.Add(add, 1, 0);

            var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 4, 20, 8), BackColor = Theme.Surface };
            _grid = new AppGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false, BorderStyle = BorderStyle.FixedSingle };
            Theme.StyleGrid(_grid);
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.Columns.Add(DashboardView.Col("Name", "Category", 200));
            _grid.Columns.Add(DashboardView.Col("ProductCount", "Products", 70, DataGridViewContentAlignment.MiddleRight));
            gridPanel.Controls.Add(_grid);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            _rename = Theme.SecondaryButton("Rename");
            _delete = Theme.DangerButton("Delete");
            var close = Theme.PrimaryButton("Close");
            footer.Controls.AddRange(new Control[] { _rename, _delete, close });
            footer.Layout += (s, e) =>
            {
                _rename.Location = new Point(20, 13);
                _delete.Location = new Point(_rename.Right + 8, 13);
                close.Location = new Point(footer.Width - close.Width - 20, 13);
            };

            Controls.Add(gridPanel);
            Controls.Add(addBar);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = close;
            close.DialogResult = DialogResult.OK;

            add.Click += (s, e) => AddCategory();
            _name.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddCategory(); } };
            _rename.Click += (s, e) => RenameSelected();
            _delete.Click += (s, e) => DeleteSelected();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) RenameSelected(); };
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; } };
            _grid.SelectionChanged += (s, e) => UpdateButtons();

            LoadList(null);
            Shown += (s, e) => _name.Focus();
        }

        private Category Selected
        {
            get { return _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].DataBoundItem as Category : null; }
        }

        private void LoadList(string select)
        {
            var list = _repo.GetAll();
            _grid.DataSource = list;
            _grid.ClearSelection();
            if (select != null)
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (string.Equals(((Category)row.DataBoundItem).Name, select, StringComparison.CurrentCultureIgnoreCase))
                    {
                        row.Selected = true;
                        _grid.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _rename.Enabled = _delete.Enabled = Selected != null;
        }

        private void AddCategory()
        {
            try
            {
                var name = _name.Text.Trim();
                _repo.Add(name);
                Changed = true;
                _name.Clear();
                LoadList(name);
                _name.Focus();
            }
            catch (Exception ex)
            {
                Msg.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
            }
        }

        private void RenameSelected()
        {
            var c = Selected;
            if (c == null) return;
            var newName = InputDialog.Show(this, L.T("Rename Category"), L.F("New name for \"{0}\":", c.Name), c.Name);
            if (newName == null || newName.Trim() == c.Name) return;
            try
            {
                _repo.Rename(c.Id, newName);
                Changed = true;
                LoadList(newName.Trim());
            }
            catch (Exception ex)
            {
                Msg.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelected()
        {
            var c = Selected;
            if (c == null) return;
            var msg = c.ProductCount > 0
                ? L.F("Delete category \"{0}\"?\n\n{1} product(s) use it and will become uncategorized. The products themselves are not deleted.", c.Name, L.Num(c.ProductCount))
                : L.F("Delete category \"{0}\"?", c.Name);
            if (Msg.Show(this, msg, "Delete Category", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _repo.Delete(c.Id);
            Changed = true;
            LoadList(null);
        }
    }

    /// <summary>Minimal one-line text prompt.</summary>
    public static class InputDialog
    {
        public static string Show(IWin32Window owner, string title, string prompt, string value)
        {
            using (var f = new Form
            {
                Text = title, Font = Theme.Body, BackColor = Theme.Surface, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent,
                AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(380, 140)
            })
            {
                var label = new Label { Text = prompt, AutoSize = true, Location = new Point(16, 16), UseMnemonic = false };
                var box = new TextBox { Text = value, Location = new Point(16, 42), Width = 348, MaxLength = 60 };
                var ok = Theme.PrimaryButton("OK");
                var cancel = Theme.SecondaryButton("Cancel");
                ok.Location = new Point(380 - 16 - 90, 88);
                cancel.Location = new Point(ok.Left - 8 - 90, 88);
                ok.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { label, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
