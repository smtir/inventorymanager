using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    public enum RangePreset { Today, Yesterday, ThisMonth, LastMonth, Custom }

    /// <summary>A date range [From, To) plus the matching "previous period" for comparisons.</summary>
    public class DateRange
    {
        public RangePreset Preset { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }            // exclusive
        public DateTime PrevFrom { get; set; }
        public DateTime PrevTo { get; set; }
        public string CompareText { get; set; }     // e.g. "vs yesterday"

        public int Days { get { return Math.Max(1, (int)Math.Round((To - From).TotalDays)); } }

        /// <summary>Human-readable range, e.g. "১ অক্টোবর – ১৪ অক্টোবর ২০২৬".</summary>
        public string Describe()
        {
            var last = To.AddDays(-1);
            if (Days == 1) return L.LongDate(From);
            if (From.Year == last.Year) return L.Date(From, "d MMMM") + " – " + L.Date(last, "d MMMM yyyy");
            return L.ShortDate(From) + " – " + L.ShortDate(last);
        }

        public static DateRange For(RangePreset preset, DateTime customFrom, DateTime customTo)
        {
            var today = DateTime.Today;
            var month = new DateTime(today.Year, today.Month, 1);
            var r = new DateRange { Preset = preset };
            switch (preset)
            {
                case RangePreset.Today:
                    r.From = today; r.To = today.AddDays(1);
                    r.PrevFrom = today.AddDays(-1); r.PrevTo = today;
                    r.CompareText = "vs yesterday";
                    break;
                case RangePreset.Yesterday:
                    r.From = today.AddDays(-1); r.To = today;
                    r.PrevFrom = today.AddDays(-2); r.PrevTo = today.AddDays(-1);
                    r.CompareText = "vs the day before";
                    break;
                case RangePreset.ThisMonth:
                    r.From = month; r.To = month.AddMonths(1);
                    r.PrevFrom = month.AddMonths(-1); r.PrevTo = month;
                    r.CompareText = "vs last month";
                    break;
                case RangePreset.LastMonth:
                    r.From = month.AddMonths(-1); r.To = month;
                    r.PrevFrom = month.AddMonths(-2); r.PrevTo = month.AddMonths(-1);
                    r.CompareText = "vs the month before";
                    break;
                default:
                    var a = customFrom.Date;
                    var b = customTo.Date;
                    if (b < a) { var t = a; a = b; b = t; }
                    r.From = a; r.To = b.AddDays(1);
                    var len = r.To - r.From;
                    r.PrevFrom = r.From - len; r.PrevTo = r.From;
                    r.CompareText = "vs previous period";
                    break;
            }
            return r;
        }
    }

    /// <summary>
    /// Date filter row: presets first (Today, Yesterday, This month, Last month), then a custom range.
    /// Raises RangeChanged; everything on the dashboard re-renders against the same range.
    /// </summary>
    public class DateRangeBar : FlowLayoutPanel
    {
        private readonly Dictionary<RangePreset, Button> _buttons = new Dictionary<RangePreset, Button>();
        private readonly FlowLayoutPanel _custom;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly Label _rangeText;

        public DateRange Range { get; private set; }
        public event EventHandler RangeChanged;

        public DateRangeBar()
        {
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(0);
            BackColor = Theme.Background;

            var group = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 14, 0), BackColor = Theme.Background };
            foreach (var p in new[]
            {
                Tuple.Create(RangePreset.Today, "Today"),
                Tuple.Create(RangePreset.Yesterday, "Yesterday"),
                Tuple.Create(RangePreset.ThisMonth, "This month"),
                Tuple.Create(RangePreset.LastMonth, "Last month"),
                Tuple.Create(RangePreset.Custom, "Custom range")
            })
            {
                var preset = p.Item1;
                var b = new AppButton
                {
                    Text = L.T(p.Item2),
                    Font = Theme.BodyBold,
                    AutoSize = true,
                    MinimumSize = new Size(76, 34),
                    Height = 34,
                    Padding = new Padding(12, 0, 12, 0),
                    Margin = new Padding(0, 0, 6, 0)
                };
                b.FlatAppearance.BorderSize = 1;
                b.Click += (s, e) => Select(preset);
                _buttons[preset] = b;
                group.Controls.Add(b);
            }

            _from = Picker(DateTime.Today.AddDays(-6));
            _to = Picker(DateTime.Today);
            var apply = Theme.PrimaryButton("Apply");
            apply.MinimumSize = new Size(70, 32);
            apply.Height = 32;
            apply.Margin = new Padding(6, 1, 0, 0);
            _custom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Visible = false, Margin = new Padding(0, 0, 14, 0), BackColor = Theme.Background };
            _custom.Controls.Add(new Label { Text = L.T("From"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 8, 6, 0) });
            _custom.Controls.Add(_from);
            _custom.Controls.Add(new Label { Text = L.T("To"), AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(10, 8, 6, 0) });
            _custom.Controls.Add(_to);
            _custom.Controls.Add(apply);
            apply.Click += (s, e) => Select(RangePreset.Custom);

            _rangeText = new Label { AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 8, 0, 0), UseMnemonic = false };

            Controls.Add(group);
            Controls.Add(_custom);
            Controls.Add(_rangeText);

            Range = DateRange.For(RangePreset.Today, _from.Value, _to.Value);
            Highlight(RangePreset.Today);
            _rangeText.Text = Range.Describe();
        }

        private static DateTimePicker Picker(DateTime value)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Width = 128,
                Value = value,
                Font = Theme.Num(10f),
                MaxDate = DateTime.Today.AddYears(1),
                Margin = new Padding(0, 3, 0, 0)
            };
        }

        public void Select(RangePreset preset)
        {
            _custom.Visible = preset == RangePreset.Custom;
            Highlight(preset);
            Range = DateRange.For(preset, _from.Value, _to.Value);
            _rangeText.Text = Range.Describe();
            var handler = RangeChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>Refreshes "today"-relative presets (e.g. after midnight) without changing the selection.</summary>
        public void Recompute()
        {
            Range = DateRange.For(Range.Preset, _from.Value, _to.Value);
            _rangeText.Text = Range.Describe();
        }

        private void Highlight(RangePreset active)
        {
            foreach (var kv in _buttons)
            {
                bool on = kv.Key == active;
                var b = kv.Value;
                b.BackColor = on ? Theme.AccentSoft : Theme.Surface;
                b.ForeColor = on ? Theme.Accent : Theme.TextSecondary;
                b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
                b.FlatAppearance.MouseOverBackColor = on ? Theme.AccentSoft : Theme.Background;
                b.FlatAppearance.MouseDownBackColor = b.FlatAppearance.MouseOverBackColor;
                b.Invalidate();
            }
        }
    }
}
