using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InventoryManager.UI
{
    /// <summary>
    /// Loads the bundled fonts (IM Siliguri for text, IM Numbers for figures; SIL Open Font License) from the Fonts folder.
    /// Registered twice: with GDI (so WinForms controls, which draw via TextRenderer, can use it) and
    /// in a PrivateFontCollection (so Font objects can be created from it). Falls back to system fonts.
    /// </summary>
    public static class FontManager
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);
        private const uint FR_PRIVATE = 0x10;

        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        private static bool _loaded;

        public static FontFamily Regular { get; private set; }
        public static FontFamily Medium { get; private set; }
        public static FontFamily SemiBold { get; private set; }

        /// <summary>"IM Numbers": clear Bangla/Latin digits and ৳ (see Fonts\IMNumbers-LICENSE.txt).</summary>
        public static FontFamily Numbers { get; private set; }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            var dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Fonts");
            if (!Directory.Exists(dir) && Bundle.Folder != null)
                dir = Path.Combine(Bundle.Folder, "Fonts");   // single-file exe: fonts were unpacked here
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.ttf"))
                    {
                        AddFontResourceEx(file, FR_PRIVATE, IntPtr.Zero);
                        Collection.AddFontFile(file);
                    }
                }
            }
            catch
            {
                // Fall through to system fonts.
            }

            // IM Siliguri = Hind Siliguri with clearer Bangla digits (see Fonts\IMSiliguri-README.txt).
            Regular = Find("IM Siliguri") ?? Find("Hind Siliguri") ?? SystemFallback();
            Medium = Find("IM Siliguri Medium") ?? Find("Hind Siliguri Medium") ?? Regular;
            SemiBold = Find("IM Siliguri SemiBold") ?? Find("Hind Siliguri SemiBold") ?? Regular;
            Numbers = Find("IM Numbers") ?? Regular;
        }

        private static FontFamily Find(string name)
        {
            return Collection.Families.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Windows 8/10 have Nirmala UI (good Bangla); Windows 7 has Vrinda.</summary>
        private static FontFamily SystemFallback()
        {
            foreach (var name in new[] { "Nirmala UI", "Vrinda", "Segoe UI" })
            {
                try
                {
                    var family = new FontFamily(name);
                    if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                        return family;
                }
                catch (ArgumentException)
                {
                }
            }
            return SystemFonts.MessageBoxFont.FontFamily;
        }
    }
}
