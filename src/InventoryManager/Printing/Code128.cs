using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace InventoryManager.Printing
{
    /// <summary>
    /// Self-contained Code 128 encoder (subsets B and C) for printing barcode labels.
    /// No dependencies: builds the module widths and draws plain black bars with GDI.
    /// </summary>
    public static class Code128
    {
        // Standard Code 128 pattern table: values 0..105 are 6 widths (bar,space,... = 11 modules),
        // 106 is the stop pattern (7 widths = 13 modules).
        internal static readonly string[] Patterns =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
        };

        private const int StartB = 104, StartC = 105, CodeB = 100, CodeC = 99, Stop = 106;

        /// <summary>True when the text can be encoded (printable ASCII 32..126).</summary>
        public static bool CanEncode(string text)
        {
            return !string.IsNullOrEmpty(text) && text.All(ch => ch >= 32 && ch <= 126);
        }

        /// <summary>
        /// Symbol values including start code and checksum (no stop), using subset C for
        /// runs of 4+ digits (or an all-digit even-length string). Exposed for tests.
        /// </summary>
        internal static List<int> Encode(string text)
        {
            if (!CanEncode(text)) throw new ArgumentException("Not encodable as Code 128.");
            var values = new List<int>();
            int i = 0;
            bool inC;

            Func<int, int> digitRun = start =>
            {
                int n = 0;
                while (start + n < text.Length && char.IsDigit(text[start + n])) n++;
                return n;
            };

            int firstRun = digitRun(0);
            inC = firstRun >= 4 || (firstRun == text.Length && firstRun >= 2 && firstRun % 2 == 0);
            values.Add(inC ? StartC : StartB);

            while (i < text.Length)
            {
                if (inC)
                {
                    int run = digitRun(i);
                    if (run >= 2)
                    {
                        // Encode an even number of digits as pairs.
                        int pairs = run / 2;
                        for (int p = 0; p < pairs; p++, i += 2)
                            values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                        if (i >= text.Length) break;
                    }
                    values.Add(CodeB);
                    inC = false;
                }
                else
                {
                    int run = digitRun(i);
                    // Switch to C when it pays off: 4+ digits mid-string, or an even run ending the string.
                    if (run >= 4 || (run >= 2 && run % 2 == 0 && i + run == text.Length && run >= 4))
                    {
                        if (run % 2 == 1) { values.Add(text[i] - '0' + 16); i++; }   // odd leading digit stays in B
                        values.Add(CodeC);
                        inC = true;
                        continue;
                    }
                    values.Add(text[i] - ' ');
                    i++;
                }
            }

            int checksum = values[0];
            for (int k = 1; k < values.Count; k++)
                checksum += values[k] * k;
            values.Add(checksum % 103);
            return values;
        }

        /// <summary>Alternating bar/space module widths, starting with a bar (stop included, no quiet zone).</summary>
        public static int[] ModuleWidths(string text)
        {
            var values = Encode(text);
            var widths = new List<int>();
            foreach (var v in values)
                widths.AddRange(Patterns[v].Select(ch => ch - '0'));
            widths.AddRange(Patterns[Stop].Select(ch => ch - '0'));
            return widths.ToArray();
        }

        /// <summary>
        /// Draws the barcode centred in <paramref name="area"/> with proper quiet zones.
        /// Returns false (and draws nothing) when the text doesn't fit readably.
        /// </summary>
        public static bool Draw(Graphics g, string text, RectangleF area)
        {
            if (!CanEncode(text)) return false;
            var widths = ModuleWidths(text);
            int totalModules = widths.Sum() + 20;           // 10 quiet modules each side
            float module = area.Width / totalModules;
            if (module <= 0.01f) return false;

            float x = area.X + 10 * module;
            var old = g.PixelOffsetMode;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            bool bar = true;
            foreach (var w in widths)
            {
                float wd = w * module;
                if (bar)
                    g.FillRectangle(Brushes.Black, x, area.Y, wd, area.Height);
                x += wd;
                bar = !bar;
            }
            g.PixelOffsetMode = old;
            return true;
        }
    }
}
