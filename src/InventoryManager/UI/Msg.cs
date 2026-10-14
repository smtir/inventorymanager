using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>
    /// Drop-in replacement for MessageBox.Show that uses the app's font and theme
    /// (Windows' own message box cannot show the bundled Bangla font). Button labels are translated;
    /// the message text should already be translated by the caller.
    /// </summary>
    public static class Msg
    {
        public static DialogResult Show(string text, string title)
        {
            return Show(null, text, title, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, title, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text)
        {
            return Show(owner, text, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string title)
        {
            return Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons)
        {
            return Show(owner, text, title, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, text, title, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var dlg = new MessageDialog(text, title, buttons, icon, defaultButton))
            {
                if (owner == null)
                {
                    dlg.StartPosition = FormStartPosition.CenterScreen;
                    return dlg.ShowDialog();
                }
                return dlg.ShowDialog(owner);
            }
        }

        private sealed class MessageDialog : Form
        {
            private readonly MessageBoxIcon _icon;

            public MessageDialog(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
            {
                _icon = icon;
                Text = L.T(string.IsNullOrEmpty(title) ? AppInfo.ProductName : title);
                Font = Theme.Body;
                BackColor = Theme.Surface;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ShowIcon = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoScaleMode = AutoScaleMode.Dpi;

                int left = icon == MessageBoxIcon.None ? 24 : 76;
                const int width = 460;
                var heading = new Label
                {
                    Text = Text,
                    Font = Theme.Semi(12f),
                    ForeColor = Theme.TextPrimary,
                    AutoSize = true,
                    MaximumSize = new Size(width - left - 24, 0),
                    Location = new Point(left, 22),
                    UseMnemonic = false
                };
                var body = new Label
                {
                    Text = text ?? "",
                    ForeColor = Theme.TextSecondary,
                    AutoSize = true,
                    MaximumSize = new Size(width - left - 24, 0),
                    UseMnemonic = false
                };
                Controls.Add(heading);
                Controls.Add(body);
                body.Location = new Point(left, heading.Bottom + 6);

                var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Background };
                var flow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 16, 0), WrapContents = false };
                footer.Controls.Add(flow);

                Button[] made;
                switch (buttons)
                {
                    case MessageBoxButtons.YesNo:
                        made = new[] { Make("Yes", DialogResult.Yes, icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Stop), Make("No", DialogResult.No, false) };
                        CancelButton = made[1];
                        break;
                    case MessageBoxButtons.YesNoCancel:
                        made = new[] { Make("Yes", DialogResult.Yes, false), Make("No", DialogResult.No, false), Make("Cancel", DialogResult.Cancel, false) };
                        CancelButton = made[2];
                        break;
                    case MessageBoxButtons.OKCancel:
                        made = new[] { Make("OK", DialogResult.OK, false), Make("Cancel", DialogResult.Cancel, false) };
                        CancelButton = made[1];
                        break;
                    default:
                        made = new[] { Make("OK", DialogResult.OK, false) };
                        CancelButton = made[0];
                        break;
                }
                // Right-to-left flow: add last button first so the order reads Yes, No.
                for (int i = made.Length - 1; i >= 0; i--)
                    flow.Controls.Add(made[i]);

                int def = defaultButton == MessageBoxDefaultButton.Button2 ? 1 : defaultButton == MessageBoxDefaultButton.Button3 ? 2 : 0;
                def = Math.Min(def, made.Length - 1);
                AcceptButton = made[def];
                Shown += (s, e) => made[def].Focus();

                Controls.Add(footer);
                int contentHeight = Math.Max(body.Bottom, icon == MessageBoxIcon.None ? 0 : 70) + 24;
                ClientSize = new Size(width, contentHeight + footer.Height);
            }

            private static Button Make(string text, DialogResult result, bool danger)
            {
                Button b = result == DialogResult.No || result == DialogResult.Cancel
                    ? Theme.SecondaryButton(text)
                    : danger ? Theme.DangerButton(text) : Theme.PrimaryButton(text);
                b.DialogResult = result;
                b.MinimumSize = new Size(96, 36);
                b.Margin = new Padding(8, 0, 0, 0);
                return b;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (_icon == MessageBoxIcon.None) return;

                Color color;
                string glyph;
                switch (_icon)
                {
                    case MessageBoxIcon.Warning: color = Theme.Warning; glyph = "!"; break;
                    case MessageBoxIcon.Error: color = Theme.Danger; glyph = "×"; break;     // also Stop/Hand
                    case MessageBoxIcon.Question: color = Theme.Accent; glyph = "?"; break;
                    default: color = Theme.Accent; glyph = "i"; break;                         // Information/Asterisk
                }

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var circle = new Rectangle(24, 22, 36, 36);
                using (var b = new SolidBrush(Theme.Blend(color, Color.White, 0.85f))) g.FillEllipse(b, circle);
                using (var f = Theme.Make(15f, FontStyle.Bold))
                    TextRenderer.DrawText(g, glyph, f, circle, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
