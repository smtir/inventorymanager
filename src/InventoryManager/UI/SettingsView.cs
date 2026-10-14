using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Drawing.Printing;
using InventoryManager.Data;
using InventoryManager.Printing;
using InventoryManager.Scanner;

namespace InventoryManager.UI
{
    public class SettingsView : UserControl, IRefreshable
    {
        private readonly MainForm _main;
        private readonly TextBox _businessName;
        private readonly TextBox _businessAddress;
        private readonly TextBox _businessPhone;
        private readonly TextBox _currency;
        private readonly Label _currencyPreview;
        private readonly NumericUpDown _defaultReorder;
        private readonly CheckBox _loadSamples;
        private readonly TextBox _autoBackupFolder;
        private Label _pinStatus;
        private Button _setPinBtn;
        private Button _removePinBtn;
        private readonly List<ShortcutBox> _shortcutBoxes = new List<ShortcutBox>();
        private Label _shortcutMessage;
        private readonly NumericUpDown _scannerPort;
        private readonly CheckBox _scannerAutoStart;
        private readonly Label _scannerStatus;
        private readonly ComboBox _printer;
        private readonly ComboBox _paper;
        private readonly CheckBox _autoPrint;
        private readonly ComboBox _language;
        private readonly TextBox _sheetUrl;
        private readonly CheckBox _sheetEnabled;
        private readonly Label _sheetStatus;
        private static string DefaultPrinterText { get { return L.T("(Windows default printer)"); } }

        public SettingsView(MainForm main)
        {
            _main = main;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            AutoScroll = true;

            // Single scrolling column of cards.
            var column = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(0, 0),
                Margin = new Padding(0)
            };

            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            heading.Controls.Add(Theme.PageTitle("Settings"));
            heading.Controls.Add(Theme.Caption("Business details, preferences and data management."));
            column.Controls.Add(heading);

            // ---- Language ----
            _language = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _language.Items.AddRange(new object[] { "বাংলা (Bangla)", "English" });
            var languageBody = FormGrid(
                Row("Language / ভাষা", _language, "The app restarts to apply a new language."));
            column.Controls.Add(Card("Language", "Interface language, digits and dates.", languageBody));

            // ---- Business info ----
            _businessName = new TextBox { Width = 420, MaxLength = 100 };
            _businessPhone = new TextBox { Width = 420, MaxLength = 50 };
            _businessAddress = new TextBox { Width = 420, Height = 60, Multiline = true, MaxLength = 300, ScrollBars = ScrollBars.Vertical };
            var businessBody = FormGrid(
                Row("Business name", _businessName),
                Row("Phone", _businessPhone),
                Row("Address", _businessAddress));
            column.Controls.Add(Card("Business Information", "Shown in the window title and the header.", businessBody));

            // ---- Currency & defaults ----
            _currency = new TextBox { Width = 120, MaxLength = 8 };
            _currencyPreview = new Label { AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(12, 6, 0, 0) };
            var currencyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            currencyRow.Controls.Add(_currency);
            currencyRow.Controls.Add(_currencyPreview);
            _defaultReorder = new NumericUpDown { Width = 120, Maximum = 1000000, TextAlign = HorizontalAlignment.Right };
            var defaultsBody = FormGrid(
                Row("Currency symbol", currencyRow, "e.g. Tk, BDT, $, Rs"),
                Row("Default reorder level", _defaultReorder, "Pre-filled when adding a new product"));
            column.Controls.Add(Card("Currency & Defaults", null, defaultsBody));

            // ---- Phone scanner ----
            _scannerPort = new NumericUpDown { Width = 120, Minimum = 1024, Maximum = 65535, TextAlign = HorizontalAlignment.Right };
            _scannerAutoStart = new CheckBox { Text = L.T("Start the phone scanner automatically when the app opens"), AutoSize = true };
            _scannerStatus = new Label { AutoSize = true, Font = Theme.BodyBold };
            var connectPhone = Theme.PrimaryButton("Connect Phone...");
            var scannerBody = FormGrid(
                Row("Status", _scannerStatus),
                Row("Port", _scannerPort, "Default 8443. Change only if another program uses it."),
                Row("", _scannerAutoStart),
                Row("", connectPhone));
            column.Controls.Add(Card("Phone Barcode Scanner", "Use an Android phone on the same Wi-Fi to scan barcodes into the POS.", scannerBody));

            // ---- Receipt ----
            _printer = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
            _paper = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            _paper.Items.AddRange(new object[] { "58mm", "80mm", "A4" });
            _autoPrint = new CheckBox { Text = L.T("Print a receipt automatically after each sale"), AutoSize = true };
            var testPrint = Theme.SecondaryButton("Test Print");
            var preview = Theme.SecondaryButton("Preview");
            var receiptButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            receiptButtons.Controls.Add(preview);
            receiptButtons.Controls.Add(testPrint);
            var receiptBody = FormGrid(
                Row("Printer", _printer),
                Row("Paper", _paper, "58mm / 80mm for thermal receipt printers"),
                Row("", _autoPrint),
                Row("", receiptButtons));
            column.Controls.Add(Card("Receipt", "Printed after checkout in the POS. Business details above appear on the receipt.", receiptBody));

            // ---- Google Sheet sync ----
            _sheetStatus = new Label { AutoSize = true, Font = Theme.BodyBold, MaximumSize = new Size(520, 0), UseMnemonic = false };
            var sheetSteps = new Label
            {
                Text = L.Digits(L.T("1.  Create a new Google Sheet (sheets.google.com).\n" +
                       "2.  Click Copy script below. In the Sheet open Extensions → Apps Script, delete what is there, paste, and press Save (Ctrl+S).\n" +
                       "3.  Give permission once: in the function list at the top choose \"authorize\" and click Run. Click Review permissions, pick your Google account, then Advanced → Go to (unsafe) → Allow. Google shows this warning for every personal script.\n" +
                       "4.  Click Deploy → New deployment → Web app. Set \"Execute as\" to \"Me\" and \"Who has access\" to \"Anyone\", then click Deploy.\n" +
                       "5.  Copy the Web app link (it ends with /exec), paste it below and click Test connection.\n\n" +
                       "If you change the script later, use Deploy → Manage deployments → Edit → Version: New version → Deploy, so the link stays the same.")),
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = Theme.TextSecondary,
                UseMnemonic = false
            };
            var copyScript = Theme.SecondaryButton("Copy script");
            _sheetUrl = new TextBox { Width = 520, Font = Theme.Make(9.5f) };
            ProductsView.SetCueBanner(_sheetUrl, "https://script.google.com/macros/s/.../exec");
            _sheetEnabled = new CheckBox { Text = L.T("Sync automatically to the Google Sheet"), AutoSize = true };
            var testSheet = Theme.SecondaryButton("Test connection");
            var syncNow = Theme.PrimaryButton("Sync now");
            var resend = Theme.SecondaryButton("Re-send everything");
            var sheetButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            sheetButtons.Controls.AddRange(new Control[] { syncNow, testSheet, resend });
            var sheetBody = FormGrid(
                Row("Status", _sheetStatus),
                Row("Setup", sheetSteps),
                Row("", copyScript),
                Row("Web app link", _sheetUrl),
                Row("", _sheetEnabled),
                Row("", sheetButtons));
            column.Controls.Add(Card("Google Sheet sync", "A live copy of products, sales, stock-ins and daily totals in your Google Sheet (one-way: app → Sheet).", sheetBody));

            // Save button for the two sections above.
            var saveBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 20) };
            var save = Theme.PrimaryButton("Save Settings");
            save.MinimumSize = new Size(140, 34);
            var revert = Theme.SecondaryButton("Revert");
            saveBar.Controls.Add(save);
            saveBar.Controls.Add(revert);
            column.Controls.Add(saveBar);

            // ---- Backup & restore ----
            var backupBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            backupBody.Controls.Add(new Label
            {
                Text = L.T("Save a copy of all products, sales and settings to a file. Keep it on a USB drive or cloud folder.\n" +
                       "A backup is also made automatically once a day when you close the app (the last 30 days are kept).\n" +
                       "Restoring replaces ALL current data with the backup (a safety copy is made first)."),
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 12)
            });
            var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            folderRow.Controls.Add(new Label { Text = L.T("Daily auto-backup also copies to"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 8, 8, 0) });
            _autoBackupFolder = new TextBox { Width = 300, Font = Theme.Body, Margin = new Padding(0, 4, 0, 0) };
            ProductsView.SetCueBanner(_autoBackupFolder, "(optional, e.g. a USB or OneDrive folder)");
            folderRow.Controls.Add(_autoBackupFolder);
            var browseBackup = Theme.SecondaryButton("Browse...");
            browseBackup.Margin = new Padding(6, 1, 0, 0);
            browseBackup.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _autoBackupFolder.Text = dlg.SelectedPath;
                }
            };
            folderRow.Controls.Add(browseBackup);
            backupBody.Controls.Add(folderRow);

            var backupButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var backupNow = Theme.PrimaryButton("Back Up Now...");
            var restore = Theme.SecondaryButton("Restore from Backup...");
            var openFolder = Theme.SecondaryButton("Open Data Folder");
            backupButtons.Controls.AddRange(new Control[] { backupNow, restore, openFolder });
            backupBody.Controls.Add(backupButtons);
            column.Controls.Add(Card("Backup & Restore", null, backupBody));

            // ---- Security (app lock PIN) ----
            var securityBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            securityBody.Controls.Add(new Label
            {
                Text = L.T("With a PIN, the app can be locked: the whole window is covered by a lock screen and nothing\n" +
                       "can be seen or changed until the PIN is entered. It locks when the app opens, when you press\n" +
                       "Lock app at the bottom of the sidebar (or its keyboard shortcut), and after 10 minutes without use."),
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 10)
            });
            _pinStatus = new Label { AutoSize = true, Font = Theme.BodyBold, Margin = new Padding(0, 0, 0, 10) };
            securityBody.Controls.Add(_pinStatus);
            var pinButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            _setPinBtn = Theme.PrimaryButton("Set PIN...");
            _removePinBtn = Theme.SecondaryButton("Remove PIN...");
            pinButtons.Controls.AddRange(new Control[] { _setPinBtn, _removePinBtn });
            securityBody.Controls.Add(pinButtons);
            column.Controls.Add(Card("Security (app lock)", null, securityBody));

            // ---- Keyboard shortcuts ----
            var keysBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            keysBody.Controls.Add(new Label
            {
                Text = L.T("Click a box and press the new keys. Backspace removes a shortcut, Esc keeps the old one.\n" +
                       "Use a function key (F1-F12), or hold Ctrl or Alt with another key."),
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 10)
            });
            var keysGrid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0) };
            keysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            keysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var command in Shortcuts.All)
            {
                var box = new ShortcutBox { CommandId = command.Id, Margin = new Padding(0, 0, 0, 6) };
                box.ShowCurrent();
                box.KeysPressed += OnShortcutPressed;
                _shortcutBoxes.Add(box);
                keysGrid.Controls.Add(new Label { Text = L.T(command.Label), AutoSize = true, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 5, 12, 6), UseMnemonic = false });
                keysGrid.Controls.Add(box);
            }
            keysBody.Controls.Add(keysGrid);
            keysBody.Controls.Add(new Label
            {
                Text = L.T("Always available: Del removes the selected line or item, Enter opens it, Tab moves between boxes."),
                AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.TextSecondary, Font = Theme.Small, Margin = new Padding(0, 6, 0, 8)
            });
            _shortcutMessage = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Font = Theme.BodyBold, Margin = new Padding(0, 0, 0, 8), UseMnemonic = false, Visible = false };
            keysBody.Controls.Add(_shortcutMessage);
            var resetKeys = Theme.SecondaryButton("Reset to defaults");
            resetKeys.Margin = new Padding(0);
            resetKeys.Click += (s, e) =>
            {
                Shortcuts.ResetAll();
                foreach (var b in _shortcutBoxes) b.ShowCurrent();
                ShowShortcutMessage(L.T("Default shortcuts restored."), false);
            };
            keysBody.Controls.Add(resetKeys);
            column.Controls.Add(Card("Keyboard shortcuts", null, keysBody));

            // ---- Reset ----
            var resetBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            resetBody.Controls.Add(new Label
            {
                Text = L.T("Permanently delete all products, categories, vendors, sales and stock-in history. Settings are kept.\n" +
                       "A safety backup is saved automatically before anything is deleted."),
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 10)
            });
            _loadSamples = new CheckBox { Text = L.T("Load sample products after reset"), AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            resetBody.Controls.Add(_loadSamples);
            var reset = Theme.DangerButton("Reset All Data...");
            resetBody.Controls.Add(reset);
            var resetCard = Card("Reset Data", null, resetBody);
            resetCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(254, 202, 202)))
                    e.Graphics.DrawRectangle(pen, 0, 0, resetCard.Width - 1, resetCard.Height - 1);
            };
            column.Controls.Add(resetCard);

            Controls.Add(column);

            // ---- Events ----
            _currency.TextChanged += (s, e) => UpdatePreview();
            save.Click += (s, e) => SaveSettings();
            revert.Click += (s, e) => { LoadValues(); _main.SetStatus(L.T("Changes reverted.")); };
            backupNow.Click += (s, e) => BackupNow();
            restore.Click += (s, e) => Restore();
            openFolder.Click += (s, e) => { try { Process.Start("explorer.exe", Database.DataFolder); } catch { } };
            reset.Click += (s, e) => ResetData();
            _setPinBtn.Click += (s, e) => SetOrChangePin();
            _removePinBtn.Click += (s, e) => RemovePin();
            copyScript.Click += (s, e) => CopySheetScript();
            testSheet.Click += (s, e) => TestSheet();
            syncNow.Click += (s, e) => { if (ApplySheetFields(true)) { Sync.SheetSync.Request(); _main.SetStatus(L.T("Syncing...")); } };
            resend.Click += (s, e) =>
            {
                if (!ApplySheetFields(true)) return;
                Sync.SheetSync.RequestFull();
                _main.SetStatus(L.T("Re-sending everything to the Google Sheet..."));
            };
            var sheetTimer = new Timer { Interval = 1500 };
            sheetTimer.Tick += (s, e) => { if (Visible) UpdateSheetStatus(); };
            sheetTimer.Start();
            Theme.ApplyNumberFonts(this);
            connectPhone.Click += (s, e) => PhoneScannerForm.ShowFor(_main);
            testPrint.Click += (s, e) => TestPrint();
            preview.Click += (s, e) => PreviewReceipt();
            var statusTimer = new Timer { Interval = 1000 };
            statusTimer.Tick += (s, e) => { if (Visible) UpdateScannerStatus(); };
            statusTimer.Start();
        }

        public void RefreshData()
        {
            LoadValues();
        }

        private void LoadValues()
        {
            _businessName.Text = AppSettings.BusinessName;
            _businessPhone.Text = AppSettings.BusinessPhone;
            _businessAddress.Text = AppSettings.BusinessAddress;
            _currency.Text = AppSettings.CurrencySymbol;
            _defaultReorder.Value = Math.Max(_defaultReorder.Minimum, Math.Min(_defaultReorder.Maximum, AppSettings.DefaultReorderLevel));
            UpdatePreview();

            _language.SelectedIndex = AppSettings.Language == "en" ? 1 : 0;
            _sheetUrl.Text = AppSettings.SheetUrl ?? "";
            _sheetEnabled.Checked = AppSettings.SheetSyncEnabled;
            UpdateSheetStatus();
            _scannerPort.Value = Math.Max(_scannerPort.Minimum, Math.Min(_scannerPort.Maximum, AppSettings.ScannerPort));
            _scannerAutoStart.Checked = AppSettings.ScannerAutoStart;
            UpdateScannerStatus();

            _printer.Items.Clear();
            _printer.Items.Add(DefaultPrinterText);
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                    _printer.Items.Add(name);
            }
            catch { }
            int pi = string.IsNullOrEmpty(AppSettings.ReceiptPrinter) ? 0 : _printer.Items.IndexOf(AppSettings.ReceiptPrinter);
            if (pi < 0)
            {
                _printer.Items.Add(AppSettings.ReceiptPrinter + NotFoundSuffix);
                pi = _printer.Items.Count - 1;
            }
            _printer.SelectedIndex = pi;
            int paper = _paper.Items.IndexOf(AppSettings.ReceiptPaper ?? "80mm");
            _paper.SelectedIndex = paper >= 0 ? paper : 1;
            _autoPrint.Checked = AppSettings.ReceiptAutoPrint;
            _autoBackupFolder.Text = AppSettings.AutoBackupFolder ?? "";
            UpdatePinStatus();
        }

        private void UpdatePreview()
        {
            var symbol = _currency.Text.Trim();
            _currencyPreview.Text = L.T("Preview:") + "  " + (symbol.Length == 0 ? "" : (L.Bangla && (symbol == "Tk" || symbol == "BDT") ? "৳" : symbol) + " ") + L.Num(1250m, 2);
        }

        private void SaveSettings()
        {
            AppSettings.BusinessName = _businessName.Text.Trim();
            AppSettings.BusinessPhone = _businessPhone.Text.Trim();
            AppSettings.BusinessAddress = _businessAddress.Text.Trim();
            AppSettings.CurrencySymbol = _currency.Text.Trim();
            AppSettings.DefaultReorderLevel = (int)_defaultReorder.Value;
            ApplyReceiptFields();
            ApplySheetFields(false);
            AppSettings.AutoBackupFolder = _autoBackupFolder.Text.Trim();
            bool portChanged = AppSettings.ScannerPort != (int)_scannerPort.Value;
            AppSettings.ScannerPort = (int)_scannerPort.Value;
            AppSettings.ScannerAutoStart = _scannerAutoStart.Checked;
            AppSettings.Save();

            if (portChanged && ScanHub.IsRunning)
            {
                ScanHub.Stop();
                try { ScanHub.Start(_main, _main); }
                catch (Exception ex) { Msg.Show(FindForm(), ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            _main.SetStatus(L.T("Settings saved."));

            var newLanguage = _language.SelectedIndex == 1 ? "en" : "bn";
            if (newLanguage != AppSettings.Language)
            {
                AppSettings.Language = newLanguage;
                AppSettings.Save();
                if (Msg.Show(FindForm(), L.T("The language has been changed. Restart the app now to apply it?"), AppInfo.ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Program.Restart();
                }
                return;
            }
            Msg.Show(FindForm(), L.T("Settings saved."), AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string NotFoundSuffix { get { return " " + L.T("(not found)"); } }

        private void ApplyReceiptFields()
        {
            var printer = _printer.SelectedItem as string;
            if (printer == null || printer == DefaultPrinterText) AppSettings.ReceiptPrinter = "";
            else if (!printer.EndsWith(NotFoundSuffix)) AppSettings.ReceiptPrinter = printer;
            AppSettings.ReceiptPaper = (_paper.SelectedItem as string) ?? "80mm";
            AppSettings.ReceiptAutoPrint = _autoPrint.Checked;
        }

        private void TestPrint()
        {
            // Print with what is selected on screen, even if not saved yet; then restore.
            var savedPrinter = AppSettings.ReceiptPrinter;
            var savedPaper = AppSettings.ReceiptPaper;
            var savedAuto = AppSettings.ReceiptAutoPrint;
            ApplyReceiptFields();
            try
            {
                ReceiptPrinter.PrintTest();
                _main.SetStatus(L.T("Test receipt sent to the printer."));
            }
            catch (Exception ex)
            {
                Msg.Show(FindForm(), L.T("Could not print.") + "\n\n" + ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                AppSettings.ReceiptPrinter = savedPrinter;
                AppSettings.ReceiptPaper = savedPaper;
                AppSettings.ReceiptAutoPrint = savedAuto;
            }
        }

        private void PreviewReceipt()
        {
            var savedPrinter = AppSettings.ReceiptPrinter;
            var savedPaper = AppSettings.ReceiptPaper;
            var savedAuto = AppSettings.ReceiptAutoPrint;
            ApplyReceiptFields();
            try
            {
                using (var doc = ReceiptPrinter.CreateDocument(ReceiptPrinter.SampleReceipt()))
                using (var dlg = new PrintPreviewDialog { Document = doc, Width = 520, Height = 720, StartPosition = FormStartPosition.CenterParent, ShowIcon = false })
                    dlg.ShowDialog(FindForm());
            }
            catch (Exception ex)
            {
                Msg.Show(FindForm(), L.T("Could not preview.") + "\n\n" + ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                AppSettings.ReceiptPrinter = savedPrinter;
                AppSettings.ReceiptPaper = savedPaper;
                AppSettings.ReceiptAutoPrint = savedAuto;
            }
        }

        // ---------------- Google Sheet ----------------

        private void CopySheetScript()
        {
            try
            {
                Clipboard.SetText(Sync.SheetSync.ScriptSource());
                Msg.Show(FindForm(), L.T("The script was copied. In your Google Sheet open Extensions → Apps Script, delete what is there, paste (Ctrl+V) and click Deploy."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Msg.Show(FindForm(), ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Stores the link + switch. With <paramref name="requireLink"/>, warns when the link is missing or wrong.</summary>
        private bool ApplySheetFields(bool requireLink)
        {
            var url = _sheetUrl.Text.Trim();
            if (requireLink && !Sync.SheetSync.IsValidUrl(url))
            {
                Msg.Show(FindForm(), L.T("Please paste the Web app link from Apps Script. It starts with https://script.google.com/ and ends with /exec."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _sheetUrl.Focus();
                return false;
            }
            if (requireLink) _sheetEnabled.Checked = true;
            bool changed = AppSettings.SheetUrl != url || AppSettings.SheetSyncEnabled != _sheetEnabled.Checked;
            AppSettings.SheetUrl = url;
            AppSettings.SheetSyncEnabled = _sheetEnabled.Checked && Sync.SheetSync.IsValidUrl(url);
            if (changed && requireLink) AppSettings.Save();
            return true;
        }

        private void TestSheet()
        {
            if (!ApplySheetFields(true)) return;
            Cursor = Cursors.WaitCursor;
            string error;
            try { error = Sync.SheetSync.Test(_sheetUrl.Text); }
            finally { Cursor = Cursors.Default; }

            if (error == null)
            {
                Sync.SheetSync.Request();
                Msg.Show(FindForm(), L.T("Connected to the Google Sheet. Your data is being sent now and will stay up to date automatically."),
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Msg.Show(FindForm(), L.T("Could not reach the Google Sheet.") + "\n\n" + error, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateSheetStatus()
        {
            if (!Sync.SheetSync.Configured)
            {
                _sheetStatus.Text = L.T("Off");
                _sheetStatus.ForeColor = Theme.TextSecondary;
                return;
            }
            _sheetStatus.Text = Sync.SheetSync.StatusText ?? "";
            _sheetStatus.ForeColor = Sync.SheetSync.LastFailed ? Theme.Danger : Sync.SheetSync.Busy ? Theme.Accent : Theme.Success;
        }

        private void UpdateScannerStatus()
        {
            if (!ScanHub.IsRunning)
            {
                _scannerStatus.Text = L.T("Stopped");
                _scannerStatus.ForeColor = Theme.TextSecondary;
            }
            else if (ScanHub.PhoneConnected)
            {
                _scannerStatus.Text = L.T("Running  -  phone connected");
                _scannerStatus.ForeColor = Theme.Success;
            }
            else
            {
                _scannerStatus.Text = L.F("Running on port {0}  -  waiting for phone", ScanHub.Port);
                _scannerStatus.ForeColor = Theme.Accent;
            }
        }

        private void BackupNow()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = L.T("Back Up Database"),
                Filter = L.T("Inventory backup (*.db)|*.db|All files (*.*)|*.*"),
                FileName = "InventoryBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".db"
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Cursor = Cursors.WaitCursor;
                try
                {
                    Database.BackupTo(dlg.FileName);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
                _main.SetStatus(L.F("Backup saved to {0}", dlg.FileName));
                Msg.Show(FindForm(), L.T("Backup saved successfully:") + "\n\n" + dlg.FileName, AppInfo.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void Restore()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = L.T("Restore from Backup"),
                Filter = L.T("Inventory backup (*.db)|*.db|All files (*.*)|*.*"),
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(Database.FilePath), StringComparison.OrdinalIgnoreCase))
                {
                    Msg.Show(FindForm(), L.T("That is the live database file. Please pick a backup file."), AppInfo.ProductName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var answer = Msg.Show(FindForm(),
                    L.T("Restore from this backup?") + "\n\n" + dlg.FileName + "\n\n" + L.T("ALL current products, sales and settings will be replaced."),
                    "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;

                Cursor = Cursors.WaitCursor;
                string safety;
                try
                {
                    safety = Database.AutoBackup("before_restore");
                    Database.RestoreFrom(dlg.FileName);
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    Msg.Show(FindForm(), L.T("Restore failed. Your current data was not changed.") + "\n\n" + ex.Message, AppInfo.ProductName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Cursor = Cursors.Default;

                LoadValues();
                _main.SetStatus(L.F("Data restored from {0}", dlg.FileName));
                Msg.Show(FindForm(), L.T("Data restored successfully.") + "\n\n" + L.T("A copy of your previous data was saved to:") + "\n" + safety,
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetData()
        {
            var answer = Msg.Show(FindForm(),
                L.T("This will permanently delete ALL products, categories, vendors and sales / stock-in history.\n\nAre you sure you want to continue?"),
                "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            answer = Msg.Show(FindForm(),
                L.T("Last chance: delete everything now?"),
                "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            Cursor = Cursors.WaitCursor;
            string safety;
            try
            {
                safety = Database.AutoBackup("before_reset");
                Database.ResetData(_loadSamples.Checked);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            _main.SetStatus(L.T("All data has been reset."));
            Msg.Show(FindForm(), L.T("All data has been reset.") + "\n\n" + L.T("A safety backup was saved to:") + "\n" + safety,
                AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------- Layout helpers ----------------

        private void UpdatePinStatus()
        {
            bool on = Session.PinEnabled;
            _pinStatus.Text = on
                ? L.T("A PIN is set. The app opens locked and locks itself after 10 minutes without use.")
                : L.T("No PIN set - anyone who opens the app can use it.");
            _pinStatus.ForeColor = on ? Theme.Success : Theme.TextSecondary;
            _setPinBtn.Text = L.T(on ? "Change PIN..." : "Set PIN...");
            _removePinBtn.Visible = on;
        }

        private void SetOrChangePin()
        {
            if (Session.PinEnabled && !PinDialog.AskVerify(FindForm())) return;
            var pin = PinDialog.AskNewPin(FindForm());
            if (pin == null) return;
            Session.SetPin(pin);
            UpdatePinStatus();
            _main.SetStatus(L.T("PIN saved."));
        }

        private void RemovePin()
        {
            if (!PinDialog.AskVerify(FindForm())) return;
            Session.RemovePin();
            UpdatePinStatus();
            _main.SetStatus(L.T("PIN removed."));
        }

        private void OnShortcutPressed(ShortcutBox box, Keys keys)
        {
            var command = Shortcuts.All.First(c => c.Id == box.CommandId);
            if (keys != Keys.None)
            {
                var problem = Shortcuts.Problem(keys);
                if (problem != null)
                {
                    ShowShortcutMessage(L.F("{0} can't be used. {1}", Shortcuts.Display(keys), problem), true);
                    return;
                }
            }
            var previousOwner = Shortcuts.UsedBy(keys, command.Id);
            Shortcuts.Set(command.Id, keys);
            foreach (var b in _shortcutBoxes)
                if (b != box) b.ShowCurrent();

            if (keys == Keys.None)
                ShowShortcutMessage(L.F("Shortcut removed from \"{0}\".", L.T(command.Label)), false);
            else if (previousOwner != null)
                ShowShortcutMessage(L.F("{0} now does \"{1}\". It was removed from \"{2}\".", Shortcuts.Display(keys), L.T(command.Label), L.T(previousOwner.Label)), false);
            else
                ShowShortcutMessage(L.F("{0} now does \"{1}\".", Shortcuts.Display(keys), L.T(command.Label)), false);
        }

        private void ShowShortcutMessage(string text, bool error)
        {
            _shortcutMessage.Text = text;
            _shortcutMessage.Visible = text.Length > 0;
            _shortcutMessage.ForeColor = error ? Theme.Danger : Theme.Success;
        }

        private static Control Card(string title, string subtitle, Control body)
        {
            var card = new CardPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 18, 24, 20),
                Margin = new Padding(0, 0, 0, 16),
                MinimumSize = new Size(720, 0)
            };
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Location = new Point(24, 18),
                BackColor = Theme.Surface
            };
            stack.Controls.Add(new Label { Text = L.T(title), UseMnemonic = false, Font = Theme.Semi(11.5f), AutoSize = true, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 0, 0, 2) });
            if (!string.IsNullOrEmpty(subtitle))
                stack.Controls.Add(new Label { Text = L.T(subtitle), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 0) });
            body.Margin = new Padding(0, 14, 0, 0);
            stack.Controls.Add(body);
            card.Controls.Add(stack);
            return card;
        }

        private static TableLayoutPanel FormGrid(params Control[][] rows)
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Surface };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var r in rows)
            {
                t.Controls.Add(r[0]);
                t.Controls.Add(r[1]);
            }
            return t;
        }

        private static Control[] Row(string label, Control input, string hint = null)
        {
            input.Font = Theme.Body;
            input.Margin = new Padding(0, 0, 0, 10);
            var lbl = new Label { Text = L.T(label), AutoSize = true, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 4, 12, 10) };
            if (hint == null) return new[] { lbl, input };

            var withHint = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            input.Margin = new Padding(0);
            withHint.Controls.Add(input);
            withHint.Controls.Add(new Label { Text = L.T(hint), AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Small, Margin = new Padding(0, 3, 0, 0) });
            return new[] { lbl, (Control)withHint };
        }
    }
}
