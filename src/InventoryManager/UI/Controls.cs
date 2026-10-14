using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>
    /// Flat button with rounded corners. Colors come from the usual properties so existing code works:
    /// BackColor = fill, ForeColor = text, FlatAppearance.MouseOverBackColor = hover fill,
    /// FlatAppearance.BorderSize/BorderColor = outline.
    /// </summary>
    public class AppButton : Button
    {
        private bool _hover;
        private bool _down;

        public int Radius { get; set; }

        public AppButton()
        {
            Radius = 8;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = BackColor;
            if (Enabled && (_hover || _down) && !FlatAppearance.MouseOverBackColor.IsEmpty)
                fill = _down && !FlatAppearance.MouseDownBackColor.IsEmpty ? FlatAppearance.MouseDownBackColor : FlatAppearance.MouseOverBackColor;
            Color text = ForeColor;
            Color border = FlatAppearance.BorderSize > 0 ? FlatAppearance.BorderColor : fill;
            if (!Enabled)
            {
                fill = Theme.Blend(fill, Color.White, 0.55f);
                border = Theme.Blend(border, Color.White, 0.55f);
                text = Theme.Blend(text, Color.White, 0.45f);
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundedRect(rect, Radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var textRect = new Rectangle(Padding.Left / 2, 0, Width - Padding.Left / 2 - Padding.Right / 2, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text, flags);

            if (Focused && ShowFocusCues)
            {
                using (var p = new Pen(Theme.Blend(fill == Theme.Surface ? Theme.Accent : fill, Color.White, 0.5f), 1.5f))
                using (var path = Theme.RoundedRect(new RectangleF(2, 2, Width - 5, Height - 5), Math.Max(2, Radius - 2)))
                    g.DrawPath(p, path);
            }
        }
    }

    /// <summary>White card with rounded corners and a hairline border.</summary>
    public class CardPanel : Panel
    {
        public int Radius { get; set; }

        public CardPanel()
        {
            Radius = 10;
            BackColor = Theme.Surface;
            Padding = new Padding(16);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public Color BorderColor { get; set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
                using (var p = new Pen(BorderColor.IsEmpty ? Theme.Border : BorderColor)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>
    /// DataGridView that shows plain numbers and dates in the current language
    /// (Bangla digits / month names) unless a CellFormatting handler already formatted the value.
    /// </summary>
    public class AppGrid : DataGridView
    {
        protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
        {
            base.OnCellFormatting(e);
            if (e.Value == null) return;
            if (!e.FormattingApplied) FormatValue(e);

            // Numbers and amounts use the number font (clearer Bangla digits and ৳).
            var text = e.Value as string;
            if (text != null && L.IsNumeric(text))
                e.CellStyle.Font = Theme.NumLike(e.CellStyle.Font ?? Font);
        }

        private static void FormatValue(DataGridViewCellFormattingEventArgs e)
        {
            var v = e.Value;
            if (v is DateTime)
            {
                var pattern = string.IsNullOrEmpty(e.CellStyle.Format) ? "d MMM yyyy" : e.CellStyle.Format;
                e.Value = L.Date((DateTime)v, pattern);
                e.FormattingApplied = true;
            }
            else if (v is int || v is long || v is short)
            {
                e.Value = L.Num(Convert.ToInt64(v));
                e.FormattingApplied = true;
            }
            else if (v is decimal || v is double || v is float)
            {
                e.Value = L.Num(Convert.ToDecimal(v), 2);
                e.FormattingApplied = true;
            }
        }
    }

    /// <summary>Simple line icons drawn with GDI+ (no image files, crisp at any DPI).</summary>
    public static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color color, float stroke = 1.7f)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                Func<float, float> X = f => x + w * f;
                Func<float, float> Y = f => y + h * f;
                switch (name)
                {
                    case "dashboard":
                        foreach (var cell in new[] { new[] { 0.1f, 0.1f }, new[] { 0.56f, 0.1f }, new[] { 0.1f, 0.56f }, new[] { 0.56f, 0.56f } })
                            using (var p = Theme.RoundedRect(new RectangleF(X(cell[0]), Y(cell[1]), w * 0.34f, h * 0.34f), w * 0.07f))
                                g.DrawPath(pen, p);
                        break;
                    case "products":   // box
                        g.DrawPolygon(pen, new[] { new PointF(X(.5f), Y(.08f)), new PointF(X(.9f), Y(.28f)), new PointF(X(.9f), Y(.72f)),
                                                   new PointF(X(.5f), Y(.92f)), new PointF(X(.1f), Y(.72f)), new PointF(X(.1f), Y(.28f)) });
                        g.DrawLines(pen, new[] { new PointF(X(.1f), Y(.28f)), new PointF(X(.5f), Y(.48f)), new PointF(X(.9f), Y(.28f)) });
                        g.DrawLine(pen, X(.5f), Y(.48f), X(.5f), Y(.92f));
                        break;
                    case "sales":      // cart
                        g.DrawLines(pen, new[] { new PointF(X(.05f), Y(.12f)), new PointF(X(.2f), Y(.12f)), new PointF(X(.32f), Y(.66f)),
                                                 new PointF(X(.82f), Y(.66f)), new PointF(X(.92f), Y(.3f)), new PointF(X(.25f), Y(.3f)) });
                        g.DrawEllipse(pen, X(.33f), Y(.78f), w * .12f, h * .12f);
                        g.DrawEllipse(pen, X(.7f), Y(.78f), w * .12f, h * .12f);
                        break;
                    case "stockin":    // arrow into tray
                        g.DrawLine(pen, X(.5f), Y(.08f), X(.5f), Y(.6f));
                        g.DrawLines(pen, new[] { new PointF(X(.3f), Y(.42f)), new PointF(X(.5f), Y(.62f)), new PointF(X(.7f), Y(.42f)) });
                        g.DrawLines(pen, new[] { new PointF(X(.1f), Y(.6f)), new PointF(X(.1f), Y(.9f)), new PointF(X(.9f), Y(.9f)), new PointF(X(.9f), Y(.6f)) });
                        break;
                    case "vendors":    // truck
                        g.DrawRectangle(pen, X(.05f), Y(.25f), w * .55f, h * .45f);
                        g.DrawLines(pen, new[] { new PointF(X(.6f), Y(.38f)), new PointF(X(.8f), Y(.38f)), new PointF(X(.95f), Y(.55f)),
                                                 new PointF(X(.95f), Y(.7f)), new PointF(X(.6f), Y(.7f)) });
                        g.DrawEllipse(pen, X(.15f), Y(.7f), w * .16f, h * .16f);
                        g.DrawEllipse(pen, X(.68f), Y(.7f), w * .16f, h * .16f);
                        break;
                    case "settings":   // gear
                    {
                        float cx = X(.5f), cy = Y(.5f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * 2 * i / 8;
                            g.DrawLine(pen, cx + (float)Math.Cos(a) * w * .3f, cy + (float)Math.Sin(a) * h * .3f,
                                            cx + (float)Math.Cos(a) * w * .43f, cy + (float)Math.Sin(a) * h * .43f);
                        }
                        g.DrawEllipse(pen, cx - w * .28f, cy - h * .28f, w * .56f, h * .56f);
                        g.DrawEllipse(pen, cx - w * .1f, cy - h * .1f, w * .2f, h * .2f);
                        break;
                    }
                    case "about":      // info circle
                        g.DrawEllipse(pen, X(.08f), Y(.08f), w * .84f, h * .84f);
                        g.DrawLine(pen, X(.5f), Y(.45f), X(.5f), Y(.72f));
                        g.FillEllipse(brush, X(.5f) - stroke, Y(.3f) - stroke, stroke * 2, stroke * 2);
                        break;
                    case "customers":  // two people
                        g.DrawEllipse(pen, X(.18f), Y(.12f), w * .3f, h * .3f);
                        g.DrawArc(pen, X(.05f), Y(.5f), w * .56f, h * .62f, 180, 180);
                        g.DrawEllipse(pen, X(.58f), Y(.18f), w * .24f, h * .24f);
                        g.DrawArc(pen, X(.5f), Y(.52f), w * .46f, h * .55f, 200, 150);
                        break;
                    case "expenses":   // banknote
                        using (var pth = Theme.RoundedRect(new RectangleF(X(.05f), Y(.22f), w * .9f, h * .56f), w * .06f))
                            g.DrawPath(pen, pth);
                        g.DrawEllipse(pen, X(.38f), Y(.36f), w * .24f, h * .28f);
                        g.DrawLine(pen, X(.16f), Y(.42f), X(.16f), Y(.58f));
                        g.DrawLine(pen, X(.84f), Y(.42f), X(.84f), Y(.58f));
                        break;
                    case "lock":
                        using (var pth = Theme.RoundedRect(new RectangleF(X(.2f), Y(.45f), w * .6f, h * .45f), w * .08f))
                            g.DrawPath(pen, pth);
                        g.DrawArc(pen, X(.3f), Y(.08f), w * .4f, h * .55f, 180, 180);
                        g.FillEllipse(brush, X(.46f), Y(.6f), w * .09f, h * .09f);
                        break;
                    case "phone":
                        using (var p = Theme.RoundedRect(new RectangleF(X(.25f), Y(.05f), w * .5f, h * .9f), w * .08f))
                            g.DrawPath(pen, p);
                        g.DrawLine(pen, X(.43f), Y(.82f), X(.57f), Y(.82f));
                        break;
                }
            }
            g.SmoothingMode = old;
        }
    }

    /// <summary>Sidebar navigation item: icon + label, soft highlight when active.</summary>
    /// <summary>
    /// Wrapping button row whose preferred height matches the rows it really uses at its current width.
    /// (A plain auto-sized FlowLayoutPanel in an auto-sized table row reports a far taller height.)
    /// </summary>
    public class WrapFlowPanel : FlowLayoutPanel
    {
        private int _lastWidth = -1;

        public WrapFlowPanel()
        {
            WrapContents = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            // Docked to fill its cell, so the width it really has is what decides the rows.
            // (Table layouts measure with a throw-away width that would stack every button.)
            int width = Width;
            if (width <= 0) return base.GetPreferredSize(proposedSize);
            int avail = width - Padding.Horizontal;
            int x = 0, rowHeight = 0, total = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int w = c.Width + c.Margin.Horizontal, h = c.Height + c.Margin.Vertical;
                if (x > 0 && x + w > avail) { total += rowHeight; x = 0; rowHeight = 0; }
                x += w;
                rowHeight = Math.Max(rowHeight, h);
            }
            total += rowHeight;
            return new Size(width, total + Padding.Vertical);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // A new width can change the number of rows: re-measure up through the auto-sized
            // containers to the first fixed-size one, which hands out the space.
            if (Width != _lastWidth)
            {
                _lastWidth = Width;
                for (Control c = Parent; c != null; c = c.Parent)
                {
                    c.PerformLayout();
                    if (!c.AutoSize) break;
                }
            }
        }
    }

    public class NavItem : Control
    {
        private bool _hover;
        private bool _active;

        public string Icon { get; set; }
        public string Key { get; set; }

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Height = 44;
            Font = Theme.Nav;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Sidebar);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_active || _hover)
            {
                using (var path = Theme.RoundedRect(new RectangleF(0.5f, 2.5f, Width - 1.5f, Height - 5.5f), 8))
                using (var b = new SolidBrush(_active ? Theme.SidebarActive : Theme.SidebarHover))
                    g.FillPath(b, path);
            }
            if (_active)
            {
                using (var b = new SolidBrush(Theme.Accent))
                using (var path = Theme.RoundedRect(new RectangleF(0, 12, 3.5f, Height - 24), 1.5f))
                    g.FillPath(b, path);
            }

            var color = _active ? Theme.Accent : Theme.SidebarText;
            Icons.Draw(g, Icon, new RectangleF(16, (Height - 18) / 2f, 18, 18), color);
            TextRenderer.DrawText(g, Text, _active ? Theme.NavActive : Theme.Nav,
                new Rectangle(46, 0, Width - 50, Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
