using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using InventoryManager.Data;
using InventoryManager.UI;

namespace InventoryManager
{
    public static class AppInfo
    {
        public const string ProductName = "Inventory Manager";

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return string.Format("{0}.{1}.{2}", v.Major, v.Minor, v.Build);
            }
        }
    }

    internal static class Program
    {
        private const string RestartArg = "--restart";

        [STAThread]
        private static void Main(string[] args)
        {
            // Unpack the bundled DLLs before any code that needs them is compiled (see Run below).
            try
            {
                Bundle.Init();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Inventory Manager could not prepare its files:\n\n" + ex.Message,
                    AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Run(args);
        }

        // Kept separate from Main so SQLite & co. are only resolved after Bundle.Init has run.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Only one instance at a time, so two windows never write the same database.
            using (var mutex = new Mutex(false, "InventoryManager_SingleInstance_7F4F5A61"))
            {
                bool owned;
                try
                {
                    // After a language change the old window may still be closing: wait for it briefly.
                    owned = mutex.WaitOne(args.Contains(RestartArg) ? 10000 : 0, false);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    TryLoadSettingsForMessage();
                    Msg.Show(L.T("Inventory Manager is already running."), AppInfo.ProductName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => ShowError(e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

                    try
                    {
                        Database.Initialize();
                    }
                    catch (Exception ex)
                    {
                        Msg.Show(L.T("Could not open the database.") + "\n\n" + ex.Message, AppInfo.ProductName,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Application.Run(new MainForm());
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>Closes the app and starts it again (used after changing the language).</summary>
        public static void Restart()
        {
            Process.Start(Application.ExecutablePath, RestartArg);
            Application.Exit();
        }

        private static void TryLoadSettingsForMessage()
        {
            try { Database.Initialize(); } catch { }
        }

        private static void ShowError(Exception ex)
        {
            Msg.Show(L.T("An unexpected error occurred:") + "\n\n" + (ex != null ? ex.Message : L.T("Unknown error")),
                AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
