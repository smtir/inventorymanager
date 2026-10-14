using System;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Scanner;
using QRCoder;

namespace InventoryManager.UI
{
    /// <summary>
    /// Shows the QR code the phone scans to open the scanner page, plus connection status.
    /// Non-modal, so scans keep flowing to the POS while it is open.
    /// </summary>
    public class PhoneScannerForm : Form
    {
        private static PhoneScannerForm _instance;
        private static string NoNetwork { get { return L.T("No network found"); } }

        private readonly MainForm _main;
        private readonly PictureBox _qr;
        private readonly ComboBox _address;
        private readonly TextBox _url;
        private readonly Label _status;
        private readonly Button _startStop;
        private readonly Timer _timer;

        public static void ShowFor(MainForm main)
        {
            if (_instance == null || _instance.IsDisposed)
                _instance = new PhoneScannerForm(main);
            if (!_instance.Visible) _instance.Show(main);
            _instance.Activate();
            _instance.EnsureRunning();
        }

        private PhoneScannerForm(MainForm main)
        {
            _main = main;
            Text = L.T("Connect Phone Scanner");
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 600);
            Icon = main.Icon;

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            header.Controls.Add(new Label
            {
                Text = L.T("Use your Android phone as a barcode scanner"),
                Font = Theme.Semi(13f),
                AutoSize = true, Location = new Point(24, 10), ForeColor = Theme.TextPrimary
            });
            header.Controls.Add(new Label
            {
                Text = L.T("The phone and this PC must be on the same Wi-Fi / network. Nothing needs to be installed on the phone."),
                AutoSize = true, Location = new Point(26, 38), ForeColor = Theme.TextSecondary
            });

            _qr = new PictureBox
            {
                Location = new Point(24, 84),
                Size = new Size(286, 286),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var steps = new Label
            {
                Location = new Point(330, 84),
                Size = new Size(430, 290),
                ForeColor = Theme.TextPrimary,
                Text =
                    L.Digits(L.T("1.  Scan this QR code with the phone camera (or Google Lens)\n" +
                    "     and open the link in Chrome.\n\n" +
                    "2.  Chrome will warn \"Your connection is not private\".\n" +
                    "     Tap Advanced, then Proceed. (This is your own PC; safe.)\n\n" +
                    "3.  Tap Start Scanning and allow camera access.\n\n" +
                    "4.  Scan products. They appear in the POS cart on this PC.\n" +
                    "     Switch Sale / Stock In on the phone or here."))
            };

            var addressLabel = new Label { Text = L.T("PC address"), Location = new Point(330, 380), AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.Small };
            _address = new ComboBox { Location = new Point(330, 402), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _url = new TextBox { Location = new Point(520, 402), Width = 240, ReadOnly = true, BackColor = Theme.Surface };

            _status = new Label { Location = new Point(330, 438), Size = new Size(430, 28), Font = Theme.BodyBold, UseMnemonic = false };

            var note = new Label
            {
                Location = new Point(24, 478),
                Size = new Size(736, 52),
                ForeColor = Theme.TextSecondary,
                Font = Theme.Small,
                Text = L.T("If Windows Firewall asks, allow Inventory Manager on Private networks. If the phone cannot connect, " +
                       "try the other address in the list, or check that the phone is not on mobile data / a guest Wi-Fi.")
            };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
            var close = Theme.PrimaryButton("Close");
            _startStop = Theme.SecondaryButton("Stop Scanner");
            var reset = Theme.SecondaryButton("Reset Pairing");
            var copy = Theme.SecondaryButton("Copy Link");
            footer.Controls.AddRange(new Control[] { close, _startStop, reset, copy });
            footer.Layout += (s, e) =>
            {
                close.Location = new Point(footer.Width - close.Width - 24, 13);
                _startStop.Location = new Point(close.Left - _startStop.Width - 8, 13);
                reset.Location = new Point(24, 13);
                copy.Location = new Point(reset.Right + 8, 13);
            };

            Controls.AddRange(new Control[] { _qr, steps, addressLabel, _address, _url, _status, note });
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = close;

            close.Click += (s, e) => Hide();
            _startStop.Click += (s, e) =>
            {
                if (ScanHub.IsRunning) ScanHub.Stop();
                else EnsureRunning();
                UpdateView();
            };
            reset.Click += (s, e) =>
            {
                if (Msg.Show(this, L.T("Create a new QR code? Phones paired with the old one will have to scan again."),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    ScanHub.ResetPairing();
                    UpdateQr();
                }
            };
            copy.Click += (s, e) => { if (_url.Text.Length > 0) Clipboard.SetText(_url.Text); };
            _address.SelectedIndexChanged += (s, e) => UpdateQr();

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => UpdateView();
            VisibleChanged += (s, e) => { if (Visible) _timer.Start(); else _timer.Stop(); };
            FormClosing += (s, e) =>
            {
                // Keep the instance (and the server) alive; just hide.
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        private void EnsureRunning()
        {
            if (!ScanHub.IsRunning)
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    ScanHub.Start(_main, _main);
                }
                catch (Exception ex)
                {
                    Msg.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            LoadAddresses();
            UpdateView();
        }

        private void LoadAddresses()
        {
            var current = _address.SelectedItem as string;
            _address.Items.Clear();
            foreach (var ip in ScanHub.GetLocalAddresses())
                _address.Items.Add(ip);
            if (_address.Items.Count == 0)
            {
                _address.Items.Add(NoNetwork);
                _address.Enabled = false;
            }
            else
            {
                _address.Enabled = _address.Items.Count > 1;
            }
            int idx = current != null ? _address.Items.IndexOf(current) : -1;
            _address.SelectedIndex = idx >= 0 ? idx : 0;
            UpdateQr();
        }

        private void UpdateQr()
        {
            var ip = _address.SelectedItem as string;
            if (ip == null || !_address.Enabled && _address.Items.Count == 1 && ip == NoNetwork)
            {
                _url.Text = "";
                _qr.Image = null;
                return;
            }

            var url = ScanHub.UrlFor(ip);
            _url.Text = url;
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M))
            using (var code = new QRCode(data))
            {
                var old = _qr.Image;
                _qr.Image = code.GetGraphic(10, Color.Black, Color.White, true);
                if (old != null) old.Dispose();
            }
        }

        private void UpdateView()
        {
            _startStop.Text = L.T(ScanHub.IsRunning ? "Stop Scanner" : "Start Scanner");
            _qr.Enabled = ScanHub.IsRunning;

            if (!ScanHub.IsRunning)
            {
                _status.Text = L.T("Scanner is stopped.");
                _status.ForeColor = Theme.Danger;
            }
            else if (ScanHub.PhoneConnected)
            {
                _status.Text = L.F("Connected: {0}", ScanHub.LastDevice ?? L.T("phone"));
                _status.ForeColor = Theme.Success;
            }
            else
            {
                _status.Text = L.T("Waiting for the phone to connect...");
                _status.ForeColor = Theme.Accent;
            }
        }
    }
}
