using Microsoft.Win32;
using System.Windows.Forms;

namespace FlowGrid.Util
{
    /// <summary>
    /// Manages the "start with Windows" entry in the current user's Run key.
    /// </summary>
    public static class AutostartUtil
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FlowGrid";

        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key?.GetValue(ValueName) as string == GetCommand();
                }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                // Clean up the entry from before the rename.
                key.DeleteValue("NoFences", false);

                if (enabled)
                    key.SetValue(ValueName, GetCommand());
                else
                    key.DeleteValue(ValueName, false);
            }
        }

        private static string GetCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
        }
    }
}
