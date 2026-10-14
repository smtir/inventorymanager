using System;
using System.Globalization;
using System.Text;
using InventoryManager.Data;

namespace InventoryManager
{
    /// <summary>
    /// Localization helpers. All UI text is written in English in code and translated here
    /// (see Strings.cs). In Bangla mode numbers and dates are shown with Bangla digits and month names;
    /// product codes, barcodes and invoice numbers stay in English digits so scanning still matches.
    /// </summary>
    public static class L
    {
        private static readonly char[] BanglaDigits = { '০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯' };
        private static DateTimeFormatInfo _bnDates;

        /// <summary>True when the interface language is Bangla (the default).</summary>
        public static bool Bangla
        {
            get { return !string.Equals(AppSettings.Language, "en", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Translates an English UI string. Unknown strings are returned unchanged.</summary>
        public static string T(string english)
        {
            if (english == null || !Bangla) return english;
            string bn;
            return Strings.Bangla.TryGetValue(english, out bn) ? bn : english;
        }

        /// <summary>Translates a format string, then fills it in. Format numbers with Num/Money first.</summary>
        public static string F(string englishFormat, params object[] args)
        {
            return string.Format(T(englishFormat), args);
        }

        /// <summary>Whole number with thousands separators, e.g. 1,250 or ১,২৫০.</summary>
        public static string Num(long value)
        {
            return Digits(value.ToString("N0", CultureInfo.InvariantCulture));
        }

        public static string Num(decimal value, int decimals)
        {
            return Digits(value.ToString("N" + decimals, CultureInfo.InvariantCulture));
        }

        /// <summary>Money with the currency symbol, e.g. "৳ ১,২৫০.০০" or "Tk 1,250.00".</summary>
        public static string Money(decimal value)
        {
            var symbol = CurrencySymbol;
            var amount = Num(value, 2);
            return symbol.Length == 0 ? amount : symbol + " " + amount;
        }

        /// <summary>The configured symbol; "Tk"/"BDT"/"Taka" shows as ৳ in Bangla mode.</summary>
        public static string CurrencySymbol
        {
            get
            {
                var s = (AppSettings.CurrencySymbol ?? "").Trim();
                if (Bangla && (s.Equals("Tk", StringComparison.OrdinalIgnoreCase) ||
                               s.Equals("BDT", StringComparison.OrdinalIgnoreCase) ||
                               s.Equals("Taka", StringComparison.OrdinalIgnoreCase)))
                    return "৳";
                return s;
            }
        }

        /// <summary>
        /// True when the text is just a number or an amount (digits, separators, sign, %, currency symbol),
        /// so it can be drawn with the number font.
        /// </summary>
        public static bool IsNumeric(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var symbol = CurrencySymbol;
            if (symbol.Length > 0) s = s.Replace(symbol, "");
            s = s.Replace("৳", "").Replace("Tk", "");
            bool digit = false;
            foreach (var ch in s)
            {
                if (char.IsDigit(ch)) { digit = true; continue; }
                if (" ,.:/-+−%()\u00A0".IndexOf(ch) < 0) return false;
            }
            return digit;
        }

        /// <summary>Converts 0-9 to Bangla digits (Bangla mode only).</summary>
        public static string Digits(string s)
        {
            if (!Bangla || string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(ch >= '0' && ch <= '9' ? BanglaDigits[ch - '0'] : ch);
            return sb.ToString();
        }

        /// <summary>Converts Bangla digits typed or shown on screen back to 0-9 (for parsing).</summary>
        public static string ToAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(ch >= '০' && ch <= '৯' ? (char)('0' + (ch - '০')) : ch);
            return sb.ToString();
        }

        /// <summary>Formats a date with a .NET pattern, using Bangla month/day names and digits in Bangla mode.</summary>
        public static string Date(DateTime d, string pattern)
        {
            if (!Bangla)
                return d.ToString(pattern, CultureInfo.InvariantCulture);
            return Digits(d.ToString(pattern, BanglaDates));
        }

        /// <summary>e.g. "14 Oct 2026" / "১৪ অক্টোবর ২০২৬".</summary>
        public static string ShortDate(DateTime d) { return Date(d, "d MMM yyyy"); }

        /// <summary>e.g. "Wednesday, 14 October 2026" / "বুধবার, ১৪ অক্টোবর ২০২৬".</summary>
        public static string LongDate(DateTime d) { return Date(d, "dddd, d MMMM yyyy"); }

        /// <summary>e.g. "14 Oct 2026, 01:45 PM".</summary>
        public static string DateTimeText(DateTime d) { return Date(d, "d MMM yyyy, hh:mm tt"); }

        private static DateTimeFormatInfo BanglaDates
        {
            get
            {
                if (_bnDates != null) return _bnDates;
                var f = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
                var months = new[]
                {
                    "জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন",
                    "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর", ""
                };
                var days = new[] { "রবিবার", "সোমবার", "মঙ্গলবার", "বুধবার", "বৃহস্পতিবার", "শুক্রবার", "শনিবার" };
                f.MonthNames = months;
                f.AbbreviatedMonthNames = months;
                f.MonthGenitiveNames = months;
                f.AbbreviatedMonthGenitiveNames = months;
                f.DayNames = days;
                f.AbbreviatedDayNames = days;
                f.AMDesignator = "পূর্বাহ্ণ";
                f.PMDesignator = "অপরাহ্ণ";
                _bnDates = f;
                return f;
            }
        }
    }
}
