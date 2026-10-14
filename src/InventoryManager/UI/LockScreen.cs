using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using InventoryManager.Data;

namespace InventoryManager.UI
{
    /// <summary>
    /// Covers the whole main window while the app is locked: nothing behind it can be seen or used
    /// until the owner PIN is entered. After 5 wrong tries, input pauses for 30 seconds.
    /// </summary>
    public class LockScreen : Control
    {
        private const int MaxTries = 5;
        private const int PauseSeconds = 30;

        private readonly CardPanel _card;
        private readonly Label _title;
        private readonly Label _clock;
        private readonly Label _date;
        private readonly TextBox _pin;
        private readonly Button _unlock;
        private readonly Label _error;
        private readonly Timer _timer;
        private int _wrongTries;
        private DateTime _pausedUntil = DateTime.MinValue;
        private bool _showingPause;

        public LockScreen()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Theme.Body;

            _clock = new Label { AutoSize = true, Font = Theme.Num(40f, true), ForeColor = Color.White, BackColor = Color.Transparent };
            _date = new Label { AutoSize = true, Font = Theme.Semi(13f), ForeColor = Color.FromArgb(220, 240, 237), BackColor = Color.Transparent };

            _card = new CardPanel { Size = new Size(400, 330), Padding = new Padding(36, 28, 36, 24) };
            var icon = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try
            {
                var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null) icon.Image = new Icon(appIcon, 64, 64).ToBitmap();
            }
            catch { }
            _title = new Label { AutoSize = false, Height = 34, Font = Theme.Semi(15f), ForeColor = Theme.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false };
            var subtitle = new Label
            {
                Text = L.T("The app is locked. Enter the PIN to continue."),
                AutoSize = false, Height = 24, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleCenter
            };
            _pin = new TextBox
            {
                Width = 220,
                Font = Theme.Num(18f, true),
                TextAlign = HorizontalAlignment.Center,
                UseSystemPasswordChar = true,
                MaxLength = 12
            };
            _unlock = Theme.PrimaryButton("Unlock");
            _unlock.MinimumSize = new Size(220, 40);
            _error = new Label { AutoSize = false, Height = 40, ForeColor = Theme.Danger, TextAlign = ContentAlignment.TopCenter, UseMnemonic = false };

            _card.Controls.AddRange(new Control[] { icon, _title, subtitle, _pin, _unlock, _error });
            _card.Layout += (s, e) =>
            {
                int w = _card.ClientSize.Width;
                icon.Location = new Point((w - icon.Width) / 2, 26);
                _title.SetBounds(16, icon.Bottom + 8, w - 32, _title.Height);
                subtitle.SetBounds(16, _title.Bottom, w - 32, subtitle.Height);
                _pin.Location = new Point((w - _pin.Width) / 2, subtitle.Bottom + 18);
                _unlock.Location = new Point((w - _unlock.Width) / 2, _pin.Bottom + 12);
                _error.SetBounds(16, _unlock.Bottom + 10, w - 32, _error.Height);
            };

            Controls.Add(_clock);
            Controls.Add(_date);
            Controls.Add(_card);

            _unlock.Click += (s, e) => TryUnlock();
            _pin.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; TryUnlock(); }
            };
            _pin.TextChanged += (s, e) => { if (_error.Text.Length > 0 && DateTime.Now >= _pausedUntil) _error.Text = ""; };

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => Tick();
            VisibleChanged += (s, e) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        }

        /// <summary>Clears the box and puts the cursor in it (called each time the app locks).</summary>
        public void Reset()
        {
            _title.Text = string.IsNullOrWhiteSpace(AppSettings.BusinessName) ? L.T(AppInfo.ProductName) : AppSettings.BusinessName;
            _pin.Text = "";
            if (DateTime.Now >= _pausedUntil) _error.Text = "";
            Tick();
            FocusPin();
        }

        public void FocusPin()
        {
            if (_pin.Enabled) { _pin.Focus(); _pin.SelectAll(); }
        }

        private void TryUnlock()
        {
            if (DateTime.Now < _pausedUntil) return;
            if (_pin.Text.Trim().Length == 0) { FocusPin(); return; }
            if (Session.Unlock(_pin.Text))
            {
                _wrongTries = 0;
                _pin.Text = "";
                _error.Text = "";
                return;   // MainForm hides this screen on Session.Changed
            }
            _wrongTries++;
            _pin.Text = "";
            if (_wrongTries >= MaxTries)
            {
                _wrongTries = 0;
                _pausedUntil = DateTime.Now.AddSeconds(PauseSeconds);
                Tick();
            }
            else
            {
                _error.Text = L.T("Wrong PIN. Try again.");
                FocusPin();
            }
        }

        private void Tick()
        {
            var now = DateTime.Now;
            _clock.Text = L.Date(now, "hh:mm");
            _date.Text = L.Date(now, "tt") + "  ·  " + L.LongDate(now);
            bool paused = now < _pausedUntil;
            _pin.Enabled = _unlock.Enabled = !paused;
            if (paused)
            {
                _showingPause = true;
                _error.Text = L.F("Too many wrong tries. Try again in {0} seconds.", L.Num((int)Math.Ceiling((_pausedUntil - now).TotalSeconds)));
            }
            else if (_showingPause)
            {
                _showingPause = false;
                _error.Text = "";
                FocusPin();
            }
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_card == null) return;   // layout requested while the constructor is still running
            int cx = Width / 2;
            int blockHeight = _clock.Height + _date.Height + 24 + _card.Height;
            int top = Math.Max(16, (Height - blockHeight) / 2);
            _clock.Location = new Point(cx - _clock.Width / 2, top);
            _date.Location = new Point(cx - _date.Width / 2, _clock.Bottom);
            _card.Location = new Point(cx - _card.Width / 2, _date.Bottom + 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new LinearGradientBrush(ClientRectangle.Width > 0 && ClientRectangle.Height > 0 ? ClientRectangle : new Rectangle(0, 0, 1, 1),
                       Theme.Accent, Theme.AccentHover, 35f))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
