using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.Scanner;

namespace InventoryManager.UI
{
    public interface IRefreshable
    {
        void RefreshData();
    }

    public class MainForm : Form, IScanHost
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        private SalesView _sales;
        private SalesView _stockIn;
        private SalesView _lastPos;
        private ToolStripStatusLabel _phoneLabel;
        private ToolStripStatusLabel _sheetLabel;
        private readonly Panel _content;
        private readonly Label _headerTitle;
        private readonly ToolStripStatusLabel _statusLeft;
        private readonly Dictionary<string, Control> _views = new Dictionary<string, Control>();
        private readonly List<NavItem> _navItems = new List<NavItem>();

        public MainForm()
        {
            Text = L.T(AppInfo.ProductName);
            Font = Theme.Body;
            BackColor = Theme.Background;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 660);
            Size = new Size(1240, 780);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ---- Sidebar (white, hairline border on the right) ----
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar };
            sidebar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };

            var brand = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Sidebar };
            var brandIcon = new PictureBox
            {
                Size = new Size(36, 36),
                Location = new Point(22, 22),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (Icon != null) brandIcon.Image = new Icon(Icon, 48, 48).ToBitmap();
            var brandText = new Label
            {
                Text = L.T(AppInfo.ProductName),
                ForeColor = Theme.TextPrimary,
                Font = Theme.Brand,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(66, 16),
                Size = new Size(160, 28),
                UseMnemonic = false
            };
            var businessText = new Label
            {
                ForeColor = Theme.TextSecondary,
                Font = Theme.Small,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(67, 44),
                Size = new Size(160, 22),
                UseMnemonic = false
            };
            brand.Controls.Add(brandIcon);
            brand.Controls.Add(brandText);
            brand.Controls.Add(businessText);

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(14, 6, 14, 0),
                BackColor = Theme.Sidebar
            };

            var sideFooter = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Text = L.Digits(L.T("Version") + " " + AppInfo.Version),
                ForeColor = Theme.TextMuted,
                Font = Theme.Small,
                TextAlign = ContentAlignment.MiddleCenter
            };

            sidebar.Controls.Add(nav);
            sidebar.Controls.Add(sideFooter);
            sidebar.Controls.Add(brand);

            // ---- Header ----
            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Surface };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            _headerTitle = new Label
            {
                Font = Theme.Semi(12f),
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Location = new Point(28, 16),
                UseMnemonic = false
            };
            var dateLabel = new Label
            {
                Font = Theme.Body,
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                UseMnemonic = false
            };
            header.Controls.Add(_headerTitle);
            header.Controls.Add(dateLabel);
            Action placeDate = () => dateLabel.Location = new Point(header.Width - dateLabel.Width - 28, 18);
            header.Resize += (s, e) => placeDate();
            dateLabel.SizeChanged += (s, e) => placeDate();

            // Business name from Settings appears in the title bar and under the app name.
            Action applySettings = () =>
            {
                var business = AppSettings.BusinessName;
                var app = L.T(AppInfo.ProductName);
                Text = string.IsNullOrWhiteSpace(business) ? app : business + " - " + app;
                businessText.Text = string.IsNullOrWhiteSpace(business) ? L.T("Product & stock management") : business;
                dateLabel.Text = L.LongDate(DateTime.Now);
            };
            applySettings();
            EventHandler onSettingsChanged = (s, e) => applySettings();
            AppSettings.Changed += onSettingsChanged;
            FormClosed += (s, e) => AppSettings.Changed -= onSettingsChanged;

            // ---- Content ----
            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 24), BackColor = Theme.Background };

            // ---- Status bar ----
            var status = new StatusStrip { BackColor = Theme.Surface, SizingGrip = true, Font = Theme.Small, RenderMode = ToolStripRenderMode.System };
            _statusLeft = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextSecondary };
            var statusRight = new ToolStripStatusLabel(L.T("Database:") + " " + Database.FilePath) { ForeColor = Theme.TextMuted };
            _phoneLabel = new ToolStripStatusLabel { ForeColor = Theme.TextSecondary, IsLink = false, Margin = new Padding(0, 3, 16, 2) };
            _phoneLabel.Click += (s, e) => PhoneScannerForm.ShowFor(this);
            status.Items.Add(_statusLeft);
            _sheetLabel = new ToolStripStatusLabel { ForeColor = Theme.TextSecondary, Margin = new Padding(0, 3, 16, 2), Visible = false };
            _sheetLabel.Click += (s, e) => Navigate("Settings");
            status.Items.Add(_sheetLabel);
            status.Items.Add(_phoneLabel);
            status.Items.Add(statusRight);
            var phoneTimer = new Timer { Interval = 1000 };
            phoneTimer.Tick += (s, e) =>
            {
                UpdatePhoneLabel();
                UpdateSheetLabel();
                // Auto-relock after 10 minutes without input (only when a PIN is set).
                if (Session.PinEnabled && Session.IsOwner && (DateTime.Now - Session.LastActivity).TotalMinutes >= 10)
                    Session.Lock();
            };
            phoneTimer.Start();

            Controls.Add(_content);
            Controls.Add(header);
            Controls.Add(sidebar);
            Controls.Add(status);

            // Lock screen: positioned over the whole client area (not docked), on top of everything.
            _lockScreen = new LockScreen { Visible = false };
            Controls.Add(_lockScreen);
            Resize += (s, e) => { if (_lockScreen.Visible) _lockScreen.Bounds = ClientRectangle; };
            Activated += (s, e) => { if (_lockScreen.Visible) _lockScreen.FocusPin(); };

            // ---- Views ----
            AddView(nav, "Dashboard", "dashboard", new DashboardView(this));
            AddView(nav, "Products", "products", new ProductsView(this));
            _sales = new SalesView(this, true);
            _stockIn = new SalesView(this, false);
            _lastPos = _sales;
            AddView(nav, "Sales", "sales", _sales);
            AddView(nav, "Stock In", "stockin", _stockIn);
            AddView(nav, "Customers", "customers", new CustomersView(this));
            AddView(nav, "Vendors", "vendors", new VendorsView(this));
            nav.Controls.Add(new Panel { Height = 1, Width = 200, BackColor = Theme.Border, Margin = new Padding(8, 10, 8, 10) });
            AddView(nav, "Expenses", "expenses", new ExpensesView(this));
            AddView(nav, "Settings", "settings", new SettingsView(this));
            AddView(nav, "About", "about", new AboutView());

            _lockItem = new NavItem { Key = "__lock", Icon = "lock", Width = 208, Margin = new Padding(0, 6, 0, 2) };
            _lockItem.Click += (s, e) => Session.Lock();
            nav.Controls.Add(_lockItem);
            Session.Changed += OnSessionChanged;
            FormClosed += (s, e) => Session.Changed -= OnSessionChanged;
            Application.AddMessageFilter(new ActivityFilter());
            ApplyLockState();

            Load += (s, e) =>
            {
                Navigate("Dashboard");
                UpdatePhoneLabel();
                InventoryManager.Sync.SheetSync.Start();
                if (AppSettings.ScannerAutoStart)
                {
                    try { ScanHub.Start(this, this); }
                    catch (Exception ex) { SetStatus(L.T("Phone scanner could not start:") + " " + ex.Message.Split('\n')[0]); }
                }
            };
            FormClosing += (s, e) =>
            {
                try { DailyBackup(); } catch { /* closing must never fail on a backup problem */ }
                ScanHub.Stop();
            };
        }

        /// <summary>Once per day, on close: a silent safety backup (plus an optional copy to a chosen folder).</summary>
        private static void DailyBackup()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (Database.GetSetting("LastAutoBackupDate", "") == today) return;

            var file = Database.AutoBackup("daily");
            Database.PruneAutoBackups("daily", 30);

            var extra = (AppSettings.AutoBackupFolder ?? "").Trim();
            if (extra.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(extra);
                    File.Copy(file, Path.Combine(extra, Path.GetFileName(file)), true);
                }
                catch
                {
                    // The extra folder (USB / cloud) may be missing; the local copy still exists.
                }
            }
            Database.SetSetting("LastAutoBackupDate", today);
        }

        public void SetStatus(string text)
        {
            _statusLeft.Text = text;
        }

        // ---------------- IScanHost: where phone scans go ----------------

        /// <summary>The Sales or Stock In screen that is open, otherwise the one used last.</summary>
        private SalesView ActivePos
        {
            get { return _stockIn.IsPosActive ? _stockIn : _sales.IsPosActive ? _sales : _lastPos; }
        }

        public IScanTarget CurrentScanTarget
        {
            get
            {
                if (IsLocked)
                    return new LockedTarget();

                // Open dialogs take the barcode: serial entry first, then the product form.
                if (SerialEntryForm.Current != null && SerialEntryForm.Current.Visible)
                    return SerialEntryForm.Current;
                if (ProductEditForm.Current != null && ProductEditForm.Current.Visible)
                    return ProductEditForm.Current;

                // Another modal dialog (message box, file dialog...) is open: don't change screens under it.
                if (!IsWindowEnabled(Handle))
                    return new BusyTarget();

                var pos = ActivePos;
                if (!pos.IsPosActive) ShowPos(pos);
                return pos;
            }
        }

        public PosStatus GetPosStatus()
        {
            var pos = ActivePos;
            return new PosStatus
            {
                PosOpen = pos.IsPosActive,
                IsSale = pos.IsSale,
                CartCount = pos.CartCount,
                CartTotal = pos.CartTotal,
                Lines = pos.CartLines.Select(l => new PosLine { Id = l.ProductId, Name = l.Name, Qty = l.Quantity, Total = l.LineTotal }).ToList()
            };
        }

        /// <summary>Phone switched Sale / Stock In: open that screen (unless a dialog is open).</summary>
        public void SetPosMode(bool isSale)
        {
            var pos = isSale ? _sales : _stockIn;
            _lastPos = pos;
            if (IsWindowEnabled(Handle) && ProductEditForm.Current == null) ShowPos(pos);
        }

        public ScanResult CartAction(string action, long productId)
        {
            return ActivePos.CartAction(action, productId);
        }

        private void ShowPos(SalesView pos)
        {
            Navigate(pos == _sales ? "Sales" : "Stock In");
            pos.ShowTab(true);
        }

        private sealed class LockedTarget : IScanTarget
        {
            public ScanResult HandleScan(string code)
            {
                return ScanResult.Fail(L.T("The app is locked. Unlock it on the PC first."));
            }
        }

        private sealed class BusyTarget : IScanTarget
        {
            public ScanResult HandleScan(string code)
            {
                return ScanResult.Fail(L.T("The PC is busy (a window is open). Close it and scan again."));
            }
        }

        private void UpdateSheetLabel()
        {
            bool on = InventoryManager.Sync.SheetSync.Configured;
            _sheetLabel.Visible = on;
            if (!on) return;
            var text = InventoryManager.Sync.SheetSync.StatusText ?? "";
            if (text.Length > 70) text = text.Substring(0, 70) + "…";
            _sheetLabel.Text = "Google Sheet: " + text;
            _sheetLabel.ForeColor = InventoryManager.Sync.SheetSync.LastFailed ? Theme.Danger : Theme.TextSecondary;
        }

        private void UpdatePhoneLabel()
        {
            if (!ScanHub.IsRunning)
            {
                _phoneLabel.Text = L.T("Phone scanner: off");
                _phoneLabel.ForeColor = Theme.TextSecondary;
            }
            else if (ScanHub.PhoneConnected)
            {
                _phoneLabel.Text = L.T("Phone scanner: connected");
                _phoneLabel.ForeColor = Theme.Success;
            }
            else
            {
                _phoneLabel.Text = L.F("Phone scanner: ready (port {0})", ScanHub.Port);
                _phoneLabel.ForeColor = Theme.Accent;
            }
        }

        public void OpenSales(long productId)
        {
            ((SalesView)_views["Sales"]).PendingProductId = productId;
            Navigate("Sales");
        }

        public void Navigate(string name)
        {
            Control view;
            if (!_views.TryGetValue(name, out view)) return;

            foreach (var item in _navItems)
                item.Active = item.Key == name;

            _content.SuspendLayout();
            _content.Controls.Clear();
            view.Dock = DockStyle.Fill;
            _content.Controls.Add(view);
            _content.ResumeLayout();

            _currentView = name;
            _headerTitle.Text = L.T(name);
            SetStatus(L.T("Ready"));
            if (view == _sales || view == _stockIn) _lastPos = (SalesView)view;

            var r = view as IRefreshable;
            if (r != null) r.RefreshData();
        }

        private void AddView(FlowLayoutPanel nav, string name, string icon, Control view)
        {
            _views[name] = view;
            var item = new NavItem
            {
                Key = name,
                Icon = icon,
                Text = L.T(name),
                Width = 208,
                Margin = new Padding(0, 0, 0, 2)
            };
            item.Click += (s, e) => Navigate(name);
            _navItems.Add(item);
            nav.Controls.Add(item);
        }

        // ---------------- app lock ----------------

        private NavItem _lockItem;
        private LockScreen _lockScreen;
        private string _currentView = "Dashboard";

        private static bool IsLocked
        {
            get { return Session.PinEnabled && !Session.IsOwner; }
        }

        private void OnSessionChanged(object sender, EventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(ApplyLockState)); return; }
            ApplyLockState();
        }

        /// <summary>Locked: the lock screen covers the whole window. Unlocked: back to the same screen.</summary>
        private void ApplyLockState()
        {
            _lockItem.Visible = Session.PinEnabled;
            _lockItem.Text = L.T("Lock app");

            bool locked = IsLocked;
            // Disabled while locked, so no key press or click can reach the screens behind the lock.
            foreach (Control c in Controls)
                if (c != _lockScreen) c.Enabled = !locked;

            if (locked)
            {
                CloseOtherWindows();
                _lockScreen.Bounds = ClientRectangle;
                _lockScreen.Visible = true;
                _lockScreen.BringToFront();
                _lockScreen.Reset();
                if (IsHandleCreated) BeginInvoke(new Action(_lockScreen.FocusPin));
                return;
            }

            if (!_lockScreen.Visible) return;
            _lockScreen.Visible = false;
            Control current;
            if (_views.TryGetValue(_currentView, out current))
            {
                var r = current as IRefreshable;
                if (r != null) r.RefreshData();
                current.Focus();
            }
        }

        /// <summary>Dialogs left open (edit forms, previews, the phone QR window) would stay usable on top of the lock screen.</summary>
        private void CloseOtherWindows()
        {
            var others = new List<Form>();
            foreach (Form f in Application.OpenForms)
                if (f != this) others.Add(f);
            foreach (var f in others)
            {
                try { f.Close(); } catch { }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // While locked only the PIN box works: no screen switching or other shortcuts.
            if (IsLocked) return base.ProcessCmdKey(ref msg, keyData);

            if (Shortcuts.Is(Shortcuts.Lock, keyData))
            {
                if (Session.PinEnabled) Session.Lock();
                else SetStatus(L.T("Set a PIN first: Settings → Security."));
                return true;
            }
            foreach (var nav in new[]
            {
                Tuple.Create(Shortcuts.Dashboard, "Dashboard"), Tuple.Create(Shortcuts.Products, "Products"),
                Tuple.Create(Shortcuts.Sales, "Sales"), Tuple.Create(Shortcuts.StockIn, "Stock In"),
                Tuple.Create(Shortcuts.Customers, "Customers")
            })
            {
                if (Shortcuts.Is(nav.Item1, keyData)) { Navigate(nav.Item2); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Any key or click counts as activity for the auto-relock timer.</summary>
        private sealed class ActivityFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == 0x0100 || m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x020A)
                    Session.LastActivity = DateTime.Now;
                return false;
            }
        }
    }
}
