using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    public class AboutView : UserControl
    {
        private const string DeveloperName = "Tawhidul Islam";
        private const string DeveloperTitle = "Senior Backend Engineer";
        private const string DeveloperEmail = "smtir.dev@gmail.com";
        private const string DeveloperWebsite = "https://tawhid.playvai.com";

        public AboutView()
        {
            BackColor = Theme.Background;
            DoubleBuffered = true;
            AutoScroll = true;

            var card = new CardPanel { Size = new Size(560, 470), Padding = new Padding(0) };

            // Banner
            var banner = new Panel { Dock = DockStyle.Top, Height = 140, BackColor = Theme.Sidebar };
            banner.Paint += (s, e) =>
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    banner.ClientRectangle, Theme.Accent, Theme.AccentHover, 20f))
                    e.Graphics.FillRectangle(brush, banner.ClientRectangle);
            };
            var logo = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point(32, 38),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) logo.Image = new Icon(icon, 64, 64).ToBitmap();
            }
            catch { }
            var appName = new Label
            {
                Text = L.T(AppInfo.ProductName),
                Font = Theme.Semi(20f),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(110, 40)
            };
            var version = new Label
            {
                Text = L.Digits(L.T("Version") + " " + AppInfo.Version) + "  ·  " + L.T("Product & stock management"),
                ForeColor = Color.FromArgb(203, 213, 225),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(113, 82)
            };
            banner.Controls.AddRange(new Control[] { logo, appName, version });

            // Body
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(32, 24, 32, 16),
                BackColor = Theme.Surface
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var heading = new Label
            {
                Text = L.T("DEVELOPED BY"),
                Font = Theme.Small,
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            var name = new Label
            {
                Text = L.T(DeveloperName),
                Font = Theme.Semi(15f),
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            var title = new Label
            {
                Text = L.T(DeveloperTitle),
                Font = Theme.Subtitle,
                ForeColor = Theme.Accent,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 18)
            };

            body.Controls.Add(heading, 0, 0); body.SetColumnSpan(heading, 2);
            body.Controls.Add(name, 0, 1); body.SetColumnSpan(name, 2);
            body.Controls.Add(title, 0, 2); body.SetColumnSpan(title, 2);

            var website = new LinkLabel
            {
                Text = DeveloperWebsite.Replace("https://", ""),
                AutoSize = true,
                Font = Theme.BodyBold,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            website.LinkClicked += (s, e) =>
            {
                try { Process.Start(DeveloperWebsite); }
                catch { Clipboard.SetText(DeveloperWebsite); Msg.Show(L.T("Website address copied to clipboard."), AppInfo.ProductName); }
            };
            AddRow(body, 3, "Website", website);

            var email = new LinkLabel
            {
                Text = DeveloperEmail,
                AutoSize = true,
                Font = Theme.BodyBold,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            email.LinkClicked += (s, e) =>
            {
                try { Process.Start("mailto:" + DeveloperEmail); }
                catch { Clipboard.SetText(DeveloperEmail); Msg.Show(L.T("Email address copied to clipboard."), AppInfo.ProductName); }
            };
            AddRow(body, 4, "Email", email);

            var copyright = new Label
            {
                Text = "© " + L.Digits(DateTime.Now.Year.ToString()) + " " + L.T(DeveloperName) + (L.Bangla ? "। " : ". ") + L.T("All rights reserved."),
                ForeColor = Theme.TextSecondary,
                Font = Theme.Small,
                AutoSize = true,
                Margin = new Padding(0, 24, 0, 0)
            };
            body.Controls.Add(copyright, 0, 5); body.SetColumnSpan(copyright, 2);

            card.Controls.Add(body);
            card.Controls.Add(banner);
            Controls.Add(card);

            // Keep the card centred horizontally.
            Resize += (s, e) => card.Location = new Point(Math.Max(0, (ClientSize.Width - card.Width) / 2), 8);
        }

        private static void AddRow(TableLayoutPanel body, int row, string label, Control value)
        {
            var lbl = new Label { Text = L.T(label), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 6, 0, 6) };
            value.Margin = new Padding(0, 6, 0, 6);
            body.Controls.Add(lbl, 0, row);
            body.Controls.Add(value, 1, row);
        }
    }
}
