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

                    FenceManager.Instance.LoadFences();
                    if (Application.OpenForms.Count == 0)
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
