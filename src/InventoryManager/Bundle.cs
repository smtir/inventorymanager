using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace InventoryManager
{
    /// <summary>
    /// Single-file support. The DLLs, the native SQLite engines and the fonts travel inside the exe as
    /// resources named "bundle/&lt;path&gt;". On start they are unpacked once per build into
    /// %LocalAppData%\InventoryManager\runtime\&lt;build id&gt; and loaded from there.
    /// When the files already sit next to the exe (folder deployment), nothing is unpacked.
    /// </summary>
    internal static class Bundle
    {
        private const string Prefix = "bundle/";

        /// <summary>Where the unpacked files live, or null when running from a full folder.</summary>
        public static string Folder { get; private set; }

        public static void Init()
        {
            var asm = typeof(Bundle).Assembly;
            var names = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
            if (names.Length == 0) return;

            string exeDir = Path.GetDirectoryName(asm.Location);
            if (File.Exists(Path.Combine(exeDir, "System.Data.SQLite.dll"))) return;

            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InventoryManager", "runtime");
            Folder = Path.Combine(root, asm.ManifestModule.ModuleVersionId.ToString("N"));

            foreach (var name in names)
            {
                string target = Path.Combine(Folder, name.Substring(Prefix.Length).Replace('/', Path.DirectorySeparatorChar));
                using (var source = asm.GetManifestResourceStream(name))
                {
                    if (source == null) continue;
                    if (File.Exists(target) && new FileInfo(target).Length == source.Length) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var output = File.Create(temp))
                        source.CopyTo(output);
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(temp, target);
                    }
                    catch (IOException)
                    {
                        // Another copy of the app unpacked the same file a moment ago.
                        TryDelete(temp);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        TryDelete(temp);
                    }
                }
            }

            // System.Data.SQLite looks for x86\ / x64\SQLite.Interop.dll under this folder.
            Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", Folder);
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            RemoveOldBuilds(root);
        }

        private static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(Folder, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        /// <summary>Files unpacked by earlier versions of the app are no longer needed.</summary>
        private static void RemoveOldBuilds(string root)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (string.Equals(dir, Folder, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
