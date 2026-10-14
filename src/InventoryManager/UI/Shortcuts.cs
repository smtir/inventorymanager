using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using InventoryManager.Data;

namespace InventoryManager.UI
{
    /// <summary>
    /// The app's keyboard shortcuts. Every screen asks here instead of hard-coding keys, so the
    /// owner can change them in Settings. Only changed keys are stored ("id=keys;...").
    /// </summary>
    public static class Shortcuts
    {
        public sealed class Command
        {
            public string Id;
            public string Label;     // English; shown through L.T
            public Keys Default;
        }

        public const string Dashboard = "nav.dashboard";
        public const string Products = "nav.products";
        public const string Sales = "nav.sales";
        public const string StockIn = "nav.stockin";
        public const string Customers = "nav.customers";
        public const string New = "app.new";
        public const string Search = "app.search";
        public const string Refresh = "app.refresh";
        public const string Lock = "app.lock";
        public const string Scan = "pos.scan";
        public const string Discount = "pos.discount";
        public const string Checkout = "pos.checkout";

        public static readonly Command[] All =
        {
            new Command { Id = Dashboard, Label = "Open Dashboard", Default = Keys.Control | Keys.D1 },
            new Command { Id = Products, Label = "Open Products", Default = Keys.Control | Keys.D2 },
            new Command { Id = Sales, Label = "Open Sales (POS)", Default = Keys.Control | Keys.D3 },
            new Command { Id = StockIn, Label = "Open Stock In", Default = Keys.Control | Keys.D4 },
            new Command { Id = Customers, Label = "Open Customers", Default = Keys.Control | Keys.D5 },
            new Command { Id = New, Label = "Add new (product, customer, expense, vendor)", Default = Keys.Control | Keys.N },
            new Command { Id = Search, Label = "Go to the search box", Default = Keys.Control | Keys.F },
            new Command { Id = Refresh, Label = "Refresh the screen", Default = Keys.F5 },
            new Command { Id = Lock, Label = "Lock the app", Default = Keys.Control | Keys.L },
            new Command { Id = Scan, Label = "POS: go to the scan box", Default = Keys.F2 },
            new Command { Id = Discount, Label = "POS: go to the discount box", Default = Keys.F9 },
            new Command { Id = Checkout, Label = "POS: checkout / save stock-in", Default = Keys.F12 },
        };

        private static Dictionary<string, Keys> _keys;
        private static string _loadedFrom;   // the stored text _keys was built from

        /// <summary>Raised when a shortcut changes, so labels that show keys can update.</summary>
        public static event EventHandler Changed;

        public static Keys Get(string id)
        {
            EnsureLoaded();
            Keys k;
            return _keys.TryGetValue(id, out k) ? k : Keys.None;
        }

        /// <summary>True when <paramref name="keyData"/> (from ProcessCmdKey) is the key for this command.</summary>
        public static bool Is(string id, Keys keyData)
        {
            var k = Get(id);
            return k != Keys.None && k == keyData;
        }

        /// <summary>The command already using these keys, or null.</summary>
        public static Command UsedBy(Keys keys, string exceptId = null)
        {
            if (keys == Keys.None) return null;
            EnsureLoaded();
            return All.FirstOrDefault(c => c.Id != exceptId && _keys[c.Id] == keys);
        }

        /// <summary>Assigns keys (Keys.None = no shortcut). Another command with the same keys loses them.</summary>
        public static void Set(string id, Keys keys)
        {
            EnsureLoaded();
            var other = UsedBy(keys, id);
            if (other != null) _keys[other.Id] = Keys.None;
            _keys[id] = keys;
            Save();
        }

        public static void ResetAll()
        {
            _keys = All.ToDictionary(c => c.Id, c => c.Default);
            Save();
        }

        /// <summary>Why these keys can't be a shortcut, or null when they can.</summary>
        public static string Problem(Keys keys)
        {
            var code = keys & Keys.KeyCode;
            bool ctrl = (keys & Keys.Control) != 0, alt = (keys & Keys.Alt) != 0;
            bool function = code >= Keys.F1 && code <= Keys.F24;
            if (!function && !ctrl && !alt)
                return L.T("Use a function key (F1-F12), or hold Ctrl or Alt with another key.");
            if (alt && code == Keys.F4)
                return L.T("Alt+F4 closes the window in Windows.");
            if (ctrl && !alt && (code == Keys.C || code == Keys.V || code == Keys.X || code == Keys.A || code == Keys.Z || code == Keys.Y))
                return L.T("Ctrl+C, V, X, A, Z and Y are used for copy, paste and undo.");
            return null;
        }

        /// <summary>"Ctrl+Shift+F5". Key names stay in English, like the labels on the keyboard.</summary>
        public static string Display(Keys keys)
        {
            if (keys == Keys.None) return L.T("(none)");
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            parts.Add(KeyName(keys & Keys.KeyCode));
            return string.Join("+", parts);
        }

        private static string KeyName(Keys code)
        {
            if (code >= Keys.D0 && code <= Keys.D9) return ((int)(code - Keys.D0)).ToString(CultureInfo.InvariantCulture);
            if (code >= Keys.NumPad0 && code <= Keys.NumPad9) return "Num " + (int)(code - Keys.NumPad0);
            switch (code)
            {
                case Keys.Delete: return "Del";
                case Keys.Insert: return "Ins";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                default: return code.ToString();
            }
        }

        private static void EnsureLoaded()
        {
            // Re-read when the stored text changed underneath us (e.g. after restoring a backup).
            var stored = AppSettings.Shortcuts ?? "";
            if (_keys != null && _loadedFrom == stored) return;
            var keys = All.ToDictionary(c => c.Id, c => c.Default);
            foreach (var pair in stored.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = pair.Split('=');
                int value;
                if (bits.Length == 2 && keys.ContainsKey(bits[0]) &&
                    int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    keys[bits[0]] = (Keys)value;
            }
            _keys = keys;
            _loadedFrom = stored;
        }

        private static void Save()
        {
            AppSettings.Shortcuts = string.Join(";", All
                .Where(c => _keys[c.Id] != c.Default)
                .Select(c => c.Id + "=" + ((int)_keys[c.Id]).ToString(CultureInfo.InvariantCulture)));
            _loadedFrom = AppSettings.Shortcuts;
            AppSettings.Save();
            OnChanged();
        }

        private static void OnChanged()
        {
            var handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Settings box that records a key combination: click it and press the new keys.
    /// Backspace / Delete removes the shortcut, Esc leaves it unchanged, Tab moves on.
    /// </summary>
    public sealed class ShortcutBox : TextBox
    {
        public string CommandId { get; set; }

        /// <summary>The user pressed a combination (Keys.None = remove the shortcut).</summary>
        public event Action<ShortcutBox, Keys> KeysPressed;

        public ShortcutBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
            TextAlign = HorizontalAlignment.Center;
            Width = 180;
            BackColor = Theme.Surface;
            Font = Theme.BodyBold;
            Enter += (s, e) => { BackColor = Color.FromArgb(224, 242, 241); Text = L.T("Press the new keys..."); };
            Leave += (s, e) => { BackColor = Theme.Surface; ShowCurrent(); };
        }

        public void ShowCurrent()
        {
            Text = Shortcuts.Display(Shortcuts.Get(CommandId));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
                return base.ProcessCmdKey(ref msg, keyData);
            // Ctrl, Shift, Alt and Win on their own: wait for the real key.
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
                return true;
            if (keyData == Keys.Escape) { ShowCurrent(); return true; }
            var handler = KeysPressed;
            if (handler != null)
                handler(this, keyData == Keys.Back || keyData == Keys.Delete ? Keys.None : keyData);
            if (Focused) Text = Shortcuts.Display(Shortcuts.Get(CommandId));
            return true;
        }
    }
}
