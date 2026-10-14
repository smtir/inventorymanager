using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>Asks for the owner PIN (or a new PIN). Accepts English or Bangla digits.</summary>
    public class PinDialog : Form
    {
        private readonly TextBox _pin;
        private readonly Label _error;
        private readonly Func<string, string> _validate;

        public string Pin { get; private set; }

        /// <param name="validate">Returns an error text to show, or null to accept and close.</param>
        private PinDialog(string title, string prompt, Func<string, string> validate)
        {
            _validate = validate;
            Text = title;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(360, 190);

            var label = new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(320, 0), Location = new Point(24, 18), ForeColor = Theme.TextPrimary, UseMnemonic = false };
            _pin = new TextBox
            {
                Location = new Point(24, 56),
                Width = 312,
                Font = Theme.Num(16f, true),
                TextAlign = HorizontalAlignment.Center,
                UseSystemPasswordChar = true,
                MaxLength = 12
            };
            _error = new Label { AutoSize = true, ForeColor = Theme.Danger, Location = new Point(24, 96), UseMnemonic = false };

            var ok = Theme.PrimaryButton("OK");
            var cancel = Theme.SecondaryButton("Cancel");
            ok.Location = new Point(ClientSize.Width - ok.Width - 24, 136);
            cancel.Location = new Point(ok.Left - cancel.Width - 8, 136);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (s, e) => Submit();
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { label, _pin, _error, ok, cancel });
            Shown += (s, e) => _pin.Focus();
        }

        private void Submit()
        {
            var error = _validate(_pin.Text);
            if (error != null)
            {
                _error.Text = error;
                _pin.SelectAll();
                _pin.Focus();
                return;
            }
            Pin = _pin.Text;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Asks for the current owner PIN; true when unlocked.</summary>
        public static bool AskUnlock(IWin32Window owner)
        {
            using (var dlg = new PinDialog(L.T("Owner unlock"), L.T("Enter the owner PIN:"),
                pin => Session.Unlock(pin) ? null : L.T("Wrong PIN. Try again.")))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        /// <summary>Verifies the current PIN without changing the lock state (for Settings changes).</summary>
        public static bool AskVerify(IWin32Window owner)
        {
            using (var dlg = new PinDialog(L.T("Owner PIN"), L.T("Enter the current owner PIN:"),
                pin => Session.Verify(pin) ? null : L.T("Wrong PIN. Try again.")))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        /// <summary>Asks for a new PIN (typed twice); returns it or null.</summary>
        public static string AskNewPin(IWin32Window owner)
        {
            string first = null;
            using (var dlg = new PinDialog(L.T("Set owner PIN"), L.T("Choose a PIN of at least 4 digits:"),
                pin =>
                {
                    var p = L.ToAscii((pin ?? "").Trim());
                    if (p.Length < 4) return L.T("Use at least 4 digits.");
                    foreach (var ch in p)
                        if (ch < '0' || ch > '9') return L.T("Digits only, please.");
                    first = pin;
                    return null;
                }))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
            }
            using (var dlg = new PinDialog(L.T("Set owner PIN"), L.T("Type the same PIN again:"),
                pin => L.ToAscii(pin.Trim()) == L.ToAscii(first.Trim()) ? null : L.T("The PINs do not match.")))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK ? first : null;
            }
        }
    }
}
