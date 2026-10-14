using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>Chart colours: validated categorical slots (blue, orange) on the white card surface.</summary>
    public static class ChartColors
    {
        public static readonly Color Series1 = Color.FromArgb(0x2a, 0x78, 0xd6);   // blue   - sales
        public static readonly Color Series2 = Color.FromArgb(0xeb, 0x68, 0x34);   // orange - profit
        public static readonly Color Grid = Color.FromArgb(0xec, 0xed, 0xef);
        public static readonly Color Baseline = Color.FromArgb(0xc9, 0xcc, 0xd1);
        public static readonly Color Muted = Color.FromArgb(0x89, 0x87, 0x81);
        public static readonly Color HoverBand = Color.FromArgb(0xf3, 0xf4, 0xf6);
    }

    /// <summary>
    /// Grouped column chart on one value axis (all series share the same unit), with legend,
    /// hairline grid and a hover readout listing every series for the hovered bucket.
    /// </summary>
    public class ColumnChart : Control
    {
        private string[] _labels = new string[0];
        private string[] _titles = new string[0];
        private decimal[][] _series = new decimal[0][];
        private string[] _names = new string[0];
        private Color[] _colors = new Color[0];
        private int _hover = -1;
        private RectangleF _plot;
        private float _slot;

        public string EmptyText { get; set; }

        public ColumnChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.Small;
        }

        /// <param name="labels">short x-axis label per bucket</param>
        /// <param name="titles">full bucket name for the hover readout</param>
        public void SetData(string[] labels, string[] titles, string[] seriesNames, Color[] colors, decimal[][] series)
        {
            _labels = labels;
            _titles = titles;
            _names = seriesNames;
            _colors = colors;
            _series = series;
            _hover = -1;
            Invalidate();
        }

        private bool HasData
        {
            get { return _labels.Length > 0 && _series.Any(s => s.Any(v => v != 0)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Legend (two or more series): swatch + name, text in ink, not series colour.
            float lx = 0;
            for (int s = 0; s < _names.Length && _names.Length > 1; s++)
            {
                using (var b = new SolidBrush(_colors[s]))
                using (var p = Theme.RoundedRect(new RectangleF(lx, 7, 10, 10), 2))
                    g.FillPath(b, p);
                var size = TextRenderer.MeasureText(g, _names[s], Theme.Small);
                TextRenderer.DrawText(g, _names[s], Theme.Small, new Point((int)lx + 14, 3), Theme.TextSecondary);
                lx += 14 + size.Width + 16;
            }

            if (!HasData)
            {
                TextRenderer.DrawText(g, EmptyText ?? "", Theme.Body, ClientRectangle, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // Scale: one axis from a clean minimum (0, or negative if profit is negative) to a clean maximum.
            decimal max = _series.SelectMany(x => x).DefaultIfEmpty(0).Max();
            decimal min = Math.Min(0, _series.SelectMany(x => x).DefaultIfEmpty(0).Min());
            decimal step = NiceStep(Math.Max(max, -min) == 0 ? 1 : (max - min) / 4m);
            decimal top = Math.Ceiling(max / step) * step;
            decimal bottom = Math.Floor(min / step) * step;
            if (top == bottom) top = bottom + step;

            var tickFont = Theme.Num(8.5f);
            int axisW = 0;
            for (decimal v = bottom; v <= top; v += step)
                axisW = Math.Max(axisW, TextRenderer.MeasureText(g, L.Num(v, 0), tickFont).Width);

            _plot = new RectangleF(axisW + 10, 42, Width - axisW - 14, Height - 42 - 26);
            if (_plot.Width < 20 || _plot.Height < 20) return;
            Func<decimal, float> Y = v => _plot.Bottom - (float)((v - bottom) / (top - bottom)) * _plot.Height;

            // Gridlines + tick labels (hairline, solid, recessive).
            using (var grid = new Pen(ChartColors.Grid))
            {
                for (decimal v = bottom; v <= top; v += step)
                {
                    float y = Y(v);
                    g.DrawLine(grid, _plot.Left, y, _plot.Right, y);
                    var txt = L.Num(v, 0);
                    var sz = TextRenderer.MeasureText(g, txt, tickFont);
                    TextRenderer.DrawText(g, txt, tickFont, new Point(axisW + 4 - sz.Width, (int)(y - sz.Height / 2f)), ChartColors.Muted);
                }
            }

            int n = _labels.Length;
            _slot = _plot.Width / n;
            int count = _series.Length;
            const float gap = 2f;
            float barW = Math.Min(24f, Math.Max(2f, (_slot * 0.72f - gap * (count - 1)) / count));
            float groupW = barW * count + gap * (count - 1);

            // Hover band behind the hovered bucket.
            if (_hover >= 0 && _hover < n)
                using (var b = new SolidBrush(ChartColors.HoverBand))
                    g.FillRectangle(b, _plot.Left + _slot * _hover, _plot.Top, _slot, _plot.Height);

            float zero = Y(0);
            for (int i = 0; i < n; i++)
            {
                float x0 = _plot.Left + _slot * i + (_slot - groupW) / 2f;
                for (int s = 0; s < count; s++)
                {
                    decimal v = _series[s][i];
                    if (v == 0) continue;
                    float y = Y(v);
                    var rect = v > 0
                        ? new RectangleF(x0 + s * (barW + gap), y, barW, zero - y)
                        : new RectangleF(x0 + s * (barW + gap), zero, barW, y - zero);
                    using (var b = new SolidBrush(_colors[s]))
                    using (var path = ColumnPath(rect, v > 0))
                        g.FillPath(b, path);
                }
            }

            // Baseline at zero.
            using (var pen = new Pen(ChartColors.Baseline))
                g.DrawLine(pen, _plot.Left, zero, _plot.Right, zero);

            // X labels: skip some so they never overlap.
            var labelFont = Theme.Num(8.5f);
            int maxW = _labels.Select(l => TextRenderer.MeasureText(g, l, labelFont).Width).DefaultIfEmpty(0).Max();
            int every = Math.Max(1, (int)Math.Ceiling((maxW + 10) / _slot));
            for (int i = 0; i < n; i += every)
            {
                var sz = TextRenderer.MeasureText(g, _labels[i], labelFont);
                float cx = _plot.Left + _slot * i + _slot / 2f;
                TextRenderer.DrawText(g, _labels[i], labelFont, new Point((int)(cx - sz.Width / 2f), (int)_plot.Bottom + 6), ChartColors.Muted);
            }

            if (_hover >= 0 && _hover < n) DrawReadout(g, _hover);
        }

        /// <summary>4px rounded data-end, square at the baseline.</summary>
        private static GraphicsPath ColumnPath(RectangleF r, bool up)
        {
            var path = new GraphicsPath();
            float rad = Math.Min(4f, Math.Min(r.Width / 2f, r.Height));
            if (rad < 1f) { path.AddRectangle(r); return path; }
            float d = rad * 2;
            if (up)
            {
                path.AddLine(r.Left, r.Bottom, r.Left, r.Top + rad);
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddLine(r.Right, r.Top + rad, r.Right, r.Bottom);
            }
            else
            {
                path.AddLine(r.Left, r.Top, r.Right, r.Top);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            }
            path.CloseFigure();
            return path;
        }

        /// <summary>Hover readout: bucket name, then each series as line key + value (strong) + name.</summary>
        private void DrawReadout(Graphics g, int i)
        {
            var valueFont = Theme.Num(10f, true);
            var rows = new List<Tuple<string, string, Color>>();
            for (int s = 0; s < _series.Length; s++)
                rows.Add(Tuple.Create(L.Money(_series[s][i]), _names[s], _colors[s]));

            int w = TextRenderer.MeasureText(g, _titles[i], Theme.Small).Width + 24;
            foreach (var r in rows)
                w = Math.Max(w, 30 + TextRenderer.MeasureText(g, r.Item1, valueFont).Width + 8 + TextRenderer.MeasureText(g, r.Item2, Theme.Small).Width + 14);
            int h = 30 + rows.Count * 24 + 6;

            float cx = _plot.Left + _slot * i + _slot / 2f;
            float x = cx + 14;
            if (x + w > Width - 2) x = cx - 14 - w;
            if (x < 2) x = 2;
            float y = _plot.Top + 4;

            var box = new RectangleF(x, y, w, h);
            using (var shadow = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
            using (var p = Theme.RoundedRect(new RectangleF(box.X + 1, box.Y + 2, box.Width, box.Height), 8))
                g.FillPath(shadow, p);
            using (var b = new SolidBrush(Color.White))
            using (var pen = new Pen(Theme.Border))
            using (var p = Theme.RoundedRect(box, 8))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, _titles[i], Theme.Small, new Point((int)x + 12, (int)y + 8), Theme.TextSecondary);
            int ry = (int)y + 32;
            foreach (var r in rows)
            {
                using (var pen = new Pen(r.Item3, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, x + 13, ry + 10, x + 25, ry + 10);
                var vs = TextRenderer.MeasureText(g, r.Item1, valueFont);
                TextRenderer.DrawText(g, r.Item1, valueFont, new Point((int)x + 32, ry), Theme.TextPrimary);
                TextRenderer.DrawText(g, r.Item2, Theme.Small, new Point((int)x + 32 + vs.Width + 4, ry + 2), Theme.TextSecondary);
                ry += 24;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = -1;
            if (HasData && _slot > 0 && e.X >= _plot.Left && e.X <= _plot.Right && e.Y >= _plot.Top - 10 && e.Y <= _plot.Bottom + 24)
                idx = Math.Min(_labels.Length - 1, (int)((e.X - _plot.Left) / _slot));
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        /// <summary>Rounds a raw step up to 1, 2, 2.5 or 5 x 10^n so ticks are clean numbers.</summary>
        private static decimal NiceStep(decimal raw)
        {
            if (raw <= 0) return 1;
            double exp = Math.Floor(Math.Log10((double)raw));
            decimal pow = (decimal)Math.Pow(10, exp);
            decimal f = raw / pow;
            decimal nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5m ? 2.5m : f <= 5 ? 5 : 10;
            return Math.Max(1, nice * pow);
        }
    }

    /// <summary>Ranked horizontal bars (single series, so no legend): name + value, thin bar beneath.</summary>
    public class RankedBars : Control
    {
        private List<Tuple<string, decimal, long>> _items = new List<Tuple<string, decimal, long>>();
        private int _hover = -1;
        private const int RowH = 46;

        public string EmptyText { get; set; }
        public Color BarColor { get; set; }

        public RankedBars()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            BarColor = ChartColors.Series1;
        }

        /// <param name="items">name, revenue, units</param>
        public void SetData(List<Tuple<string, decimal, long>> items)
        {
            _items = items ?? new List<Tuple<string, decimal, long>>();
            _hover = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText ?? "", Theme.Body, ClientRectangle, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            decimal max = Math.Max(1, _items.Max(i => i.Item2));
            var valueFont = Theme.Num(9.5f, true);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                int y = i * RowH + 4;
                if (i == _hover)
                    using (var b = new SolidBrush(ChartColors.HoverBand))
                    using (var p = Theme.RoundedRect(new RectangleF(0, y - 2, Width - 1, RowH - 4), 6))
                        g.FillPath(b, p);

                // Value at the right; on hover it also shows the units sold.
                var value = L.Money(it.Item2);
                if (i == _hover) value = L.F("{0} units", L.Num(it.Item3)) + "  ·  " + value;
                var vs = TextRenderer.MeasureText(g, value, i == _hover ? Theme.Num(9.5f) : valueFont);
                TextRenderer.DrawText(g, value, i == _hover ? Theme.Num(9.5f) : valueFont, new Point(Width - 6 - vs.Width, y + 2), Theme.TextPrimary);
                TextRenderer.DrawText(g, it.Item1, Theme.Body, new Rectangle(6, y, Width - vs.Width - 20, 24), Theme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                // Track + bar (8px, rounded end).
                float trackW = Width - 12;
                var track = new RectangleF(6, y + 28, trackW, 8);
                using (var b = new SolidBrush(ChartColors.HoverBand))
                using (var p = Theme.RoundedRect(track, 4))
                    g.FillPath(b, p);
                float w = Math.Max(8f, (float)(it.Item2 / max) * trackW);
                using (var b = new SolidBrush(BarColor))
                using (var p = Theme.RoundedRect(new RectangleF(6, y + 28, w, 8), 4))
                    g.FillPath(b, p);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = e.Y / RowH;
            if (idx >= _items.Count) idx = -1;
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }
    }
}
