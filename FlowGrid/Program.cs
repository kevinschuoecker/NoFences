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
            //allows the context menu to be in dark mode
            //inherits from the system settings
            WindowUtil.SetPreferredAppMode(1);

            using (var mutex = new Mutex(true, "FlowGrid", out var createdNew))
            {
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
                        FenceManager.Instance.CreateFence("First fence");

                    DesktopAutoSorter.Start();

                    // Re-apply hiding in case files were added to fences while the app was not running.
                    if (FenceManager.Instance.Settings.HideFencedDesktopItems)
                        DesktopIconHider.HideAllFenced();

                    using (new TrayIconHost())
                    {
                        Application.Run();
                    }
                }
            }
        }

    }
}
