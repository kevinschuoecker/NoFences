using NoFences.Model;
using NoFences.Util;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences
{
    /// <summary>
    /// Tray icon with quick access to common actions, plus the global
    /// quick-hide hotkey (Ctrl+Alt+H) that toggles all fences at once.
    /// </summary>
    public class TrayIconHost : IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly HotkeyWindow hotkeyWindow;

        public TrayIconHost()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("New fence", null, (s, e) => FenceManager.Instance.CreateFence("New fence"));
            menu.Items.Add("Show/hide all fences  (Ctrl+Alt+H)", null, (s, e) => FenceManager.Instance.ToggleAllFences());
            menu.Items.Add("Sort desktop now", null, (s, e) => DesktopAutoSorter.ApplyRulesNow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Export layout...", null, (s, e) => ExportLayout());
            menu.Items.Add("Import layout...", null, (s, e) => ImportLayout());
            menu.Items.Add(new ToolStripSeparator());

            var hideFencedItem = new ToolStripMenuItem("Hide fenced items on desktop")
            {
                CheckOnClick = true,
                Checked = FenceManager.Instance.Settings.HideFencedDesktopItems
            };
            hideFencedItem.Click += (s, e) =>
            {
                FenceManager.Instance.Settings.HideFencedDesktopItems = hideFencedItem.Checked;
                FenceManager.Instance.SaveSettings();
                if (hideFencedItem.Checked)
                    DesktopIconHider.HideAllFenced();
                else
                    DesktopIconHider.UnhideAllFenced();
            };
            menu.Items.Add(hideFencedItem);

            var autostartItem = new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = AutostartUtil.IsEnabled
            };
            autostartItem.Click += (s, e) => AutostartUtil.SetEnabled(autostartItem.Checked);
            menu.Items.Add(autostartItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());

            notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "NoFences",
                Visible = true,
                ContextMenuStrip = menu
            };
            notifyIcon.DoubleClick += (s, e) => FenceManager.Instance.ToggleAllFences();

            hotkeyWindow = new HotkeyWindow(() => FenceManager.Instance.ToggleAllFences());
        }

        private static void ExportLayout()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "NoFences layout (*.xml)|*.xml",
                FileName = "NoFences-layout.xml",
                Title = "Export fence layout"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;
                try
                {
                    FenceManager.Instance.ExportLayout(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export failed: " + ex.Message, "NoFences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void ImportLayout()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "NoFences layout (*.xml)|*.xml",
                Title = "Import fence layout"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                if (MessageBox.Show("Importing replaces ALL current fences with the ones from the file. Continue?",
                        "Import layout", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                try
                {
                    FenceManager.Instance.ImportLayout(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Import failed: " + ex.Message, "NoFences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        public void Dispose()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            hotkeyWindow.Dispose();
        }

        private class HotkeyWindow : NativeWindow, IDisposable
        {
            private const int WM_HOTKEY = 0x0312;
            private const int HOTKEY_ID = 1;
            private const uint MOD_ALT = 0x1;
            private const uint MOD_CONTROL = 0x2;

            private readonly Action onHotkey;

            public HotkeyWindow(Action onHotkey)
            {
                this.onHotkey = onHotkey;
                CreateHandle(new CreateParams());
                RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, (uint)Keys.H);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
                {
                    onHotkey();
                    return;
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                DestroyHandle();
            }

            [DllImport("user32.dll")]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll")]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }
}
