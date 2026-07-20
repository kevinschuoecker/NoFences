using FlowGrid.Model;
using FlowGrid.Util;
using System;
using System.Threading;
using System.Windows.Forms;
using FlowGrid.Win32;

namespace FlowGrid
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Log.Initialize();
            InstallGlobalExceptionHandlers();

            //allows the context menu to be in dark mode
            //inherits from the system settings
            WindowUtil.SetPreferredAppMode(1);

            using (var mutex = new Mutex(true, "FlowGrid", out var createdNew))
            {
                if (!createdNew)
                {
                    Log.Info("Another instance is already running - exiting.");
                    return;
                }
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // Plugins must be available before fences load, since fences may host plugin widgets.
                    PluginManager.LoadPlugins();

                    FenceManager.Instance.LoadFences();
                    // Don't use Application.OpenForms here: the fade-in animation sets
                    // Opacity, which recreates window handles and silently drops forms
                    // from OpenForms - making it report 0 even with fences loaded.
                    if (FenceManager.Instance.Windows.Count == 0)
                        FenceManager.Instance.CreateWelcomeFences();

                    DesktopAutoSorter.Start();

                    // Re-apply hiding in case files were added to fences while the app was not running.
                    if (FenceManager.Instance.Settings.HideFencedDesktopItems)
                        DesktopIconHider.HideAllFenced();

                    Log.Info($"Startup complete: {FenceManager.Instance.Windows.Count} fences, {PluginManager.Widgets.Count} plugin widgets.");

                    using (new TrayIconHost())
                    {
                        Application.Run();
                    }

                    Log.Info("Shutdown.");
                }
            }
        }

        private static void InstallGlobalExceptionHandlers()
        {
            // UI thread exceptions: ask the user, allow continuing.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                if (!CrashDialog.HandleException(e.Exception, canContinue: true))
                    Application.Exit();
            };

            // Non-UI thread exceptions are fatal for the process - inform, then die cleanly.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                CrashDialog.HandleException(e.ExceptionObject as Exception, canContinue: false);
            };

            // Faulted tasks nobody awaited: log only, never kill the app.
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }
    }
}
