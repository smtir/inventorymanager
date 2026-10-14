using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>
    /// Central place for colors, fonts and control styling ("light &amp; airy" theme).
    /// Text passed to the helpers below is translated automatically (see L.T).
    /// </summary>
    public static class Theme
    {
        // ---- Palette ----
        public static readonly Color Background = Color.FromArgb(245, 246, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color BorderSoft = Color.FromArgb(240, 241, 243);
        public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
        public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 175);

        public static readonly Color Accent = Color.FromArgb(15, 118, 110);        // deep teal
        public static readonly Color AccentHover = Color.FromArgb(17, 94, 89);
        public static readonly Color AccentSoft = Color.FromArgb(231, 243, 241);

        public static readonly Color Success = Color.FromArgb(5, 150, 105);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerHover = Color.FromArgb(185, 28, 28);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color WarningRow = Color.FromArgb(255, 251, 235);
        public static readonly Color Info = Color.FromArgb(37, 99, 235);
        public static readonly Color Purple = Color.FromArgb(124, 58, 237);

        // Sidebar (white, light)
        public static readonly Color Sidebar = Color.White;
        public static readonly Color SidebarHover = Color.FromArgb(243, 244, 246);
        public static readonly Color SidebarActive = AccentSoft;
        public static readonly Color SidebarText = Color.FromArgb(75, 85, 99);

        // Grid
        public static readonly Color GridAltRow = Color.White;
        public static readonly Color GridSelection = Color.FromArgb(238, 246, 245);
        public static readonly Color GridLine = Color.FromArgb(241, 242, 244);

        // ---- Fonts (bundled Hind Siliguri, see FontManager) ----
        public static readonly Font Body;
        public static readonly Font BodyBold;
        public static readonly Font Small;
        public static readonly Font SmallBold;
        public static readonly Font Title;
        public static readonly Font Subtitle;
        public static readonly Font StatValue;
        public static readonly Font Nav;
        public static readonly Font NavActive;
        public static readonly Font Brand;

        static Theme()
        {
            FontManager.Load();
            Body = Make(10f);
            BodyBold = Semi(10f);
            Small = Make(9f);
            SmallBold = Semi(9f);
            Title = Semi(17f);
            Subtitle = Make(11.5f);
            StatValue = Semi(19f);
            Nav = Make(10.5f);
            NavActive = Semi(10.5f);
            Brand = Semi(12f);
        }

        /// <summary>Regular (or bold) font at the given size.</summary>
        public static Font Make(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(FontManager.Regular, size, style);
        }

        /// <summary>Semi-bold font: used for headings, buttons and emphasis.</summary>
        public static Font Semi(float size)
        {
            return new Font(FontManager.SemiBold, size, FontStyle.Regular);
        }

        /// <summary>Medium-weight font.</summary>
        public static Font Medium(float size)
        {
            return new Font(FontManager.Medium, size, FontStyle.Regular);
        }

        // ---- Number font (digits, amounts) ----
        private static readonly System.Collections.Generic.Dictionary<string, Font> NumCache =
            new System.Collections.Generic.Dictionary<string, Font>();

        /// <summary>Font for numbers and amounts (cached). Bold uses the semi-bold digit design.</summary>
        public static Font Num(float size, bool bold = false)
        {
            var key = size.ToString(System.Globalization.CultureInfo.InvariantCulture) + (bold ? "b" : "r");
            Font f;
            if (!NumCache.TryGetValue(key, out f))
            {
                f = new Font(FontManager.Numbers, size, bold ? FontStyle.Bold : FontStyle.Regular);
                NumCache[key] = f;
            }
            return f;
        }

        /// <summary>The number-font equivalent of a text font (same size; bold if the text font is semi-bold/bold).</summary>
        public static Font NumLike(Font textFont)
        {
            bool bold = textFont.Bold || textFont.FontFamily.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0
                                      || textFont.FontFamily.Name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0;
            return Num(textFont.Size, bold);
        }

        /// <summary>Gives every NumericUpDown under <paramref name="root"/> the number font.</summary>
        public static void ApplyNumberFonts(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is NumericUpDown) c.Font = NumLike(c.Font);
                if (c.HasChildren) ApplyNumberFonts(c);
            }
        }

        // ---- Money / currency (Bangla digits and ৳ in Bangla mode) ----
        public static string CurrencySymbol { get { return L.CurrencySymbol; } }

        public static string Money(decimal value) { return L.Money(value); }

        // ---- Buttons ----
        public static Button PrimaryButton(string text)
        {
            return MakeButton(text, Accent, AccentHover, Color.White, Color.Empty);
        }

        public static Button DangerButton(string text)
        {
            return MakeButton(text, Danger, DangerHover, Color.White, Color.Empty);
        }

        public static Button SecondaryButton(string text)
        {
            return MakeButton(text, Surface, Color.FromArgb(249, 250, 251), TextPrimary, Border);
        }

        private static Button MakeButton(string text, Color back, Color hover, Color fore, Color border)
        {
            var b = new AppButton
            {
                Text = L.T(text),
                BackColor = back,
                ForeColor = fore,
                Font = BodyBold,
                Height = 36,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(88, 36),
                Padding = new Padding(14, 0, 14, 0),
                Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            b.FlatAppearance.BorderSize = border.IsEmpty ? 0 : 1;
            b.FlatAppearance.BorderColor = border.IsEmpty ? back : border;
            return b;
        }

        // ---- Grids ----
        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = GridLine;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 44;
            grid.RowTemplate.Height = 40;
            grid.Font = Body;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = SmallBold;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;

            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = GridSelection;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = GridAltRow;

            // Thin line under the header row.
            grid.CellPainting += (s, e) =>
            {
                if (e.RowIndex != -1) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                e.Handled = true;
            };

            // Reduce flicker on scroll/resize (DoubleBuffered is protected).
            typeof(DataGridView)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(grid, true, null);
        }

        // ---- Text ----
        public static Label PageTitle(string text)
        {
            return new Label
            {
                Text = L.T(text),
                Font = Title,
                ForeColor = TextPrimary,
                UseMnemonic = false,
                AutoSize = true,
                Margin = new Padding(0)
            };
        }

        public static Label Caption(string text)
        {
            return new Label
            {
                Text = L.T(text),
                Font = Body,
                ForeColor = TextSecondary,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, 2, 0, 0)
            };
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (radius <= 0 || r.Width < d || r.Height < d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color a, Color b, float amountOfB)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amountOfB),
                (int)(a.G + (b.G - a.G) * amountOfB),
                (int)(a.B + (b.B - a.B) * amountOfB));
        }
    }
}
