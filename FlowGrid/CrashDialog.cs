using FlowGrid.Util;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FlowGrid
{
    /// <summary>
    /// Shown when an unhandled exception reaches the global handlers. Gives the
    /// user a choice to continue or exit, with quick access to the log folder.
    /// Storm-guarded: after repeated crashes in a short window it stops asking
    /// and just logs, so a permanently failing paint loop cannot spam dialogs.
    /// </summary>
    public class CrashDialog : Form
    {
        private static int recentCrashes;
        private static DateTime firstRecentCrash = DateTime.MinValue;
        private static bool dialogOpen;

        /// <summary>Logs the exception and, when appropriate, asks the user how to proceed.</summary>
        /// <returns>true to continue running, false to exit.</returns>
        public static bool HandleException(Exception exception, bool canContinue)
        {
            Log.Error("Unhandled exception", exception);

            // Storm guard: 3+ crashes within a minute -> keep running silently, just log.
            if (DateTime.Now - firstRecentCrash > TimeSpan.FromMinutes(1))
            {
                firstRecentCrash = DateTime.Now;
                recentCrashes = 0;
            }
            recentCrashes++;
            if (dialogOpen || (recentCrashes > 3 && canContinue))
                return true;

            dialogOpen = true;
            try
            {
                using (var dialog = new CrashDialog(exception, canContinue))
                    return dialog.ShowDialog() == DialogResult.OK && canContinue;
            }
            catch
            {
                return canContinue;
            }
            finally
            {
                dialogOpen = false;
            }
        }

        private CrashDialog(Exception exception, bool canContinue)
        {
            Text = "FlowGrid - unexpected error";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(460, 190);
            Font = new Font("Segoe UI", 9f);

            var message = new Label
            {
                Text = canContinue
                    ? "FlowGrid ran into an unexpected error. You can keep working; if problems persist, please restart the app and check the logs."
                    : "FlowGrid ran into a fatal error and needs to close. Details have been written to the log.",
                Location = new Point(12, 12),
                Size = new Size(436, 50)
            };

            var details = new TextBox
            {
                Text = exception?.GetType().Name + ": " + exception?.Message,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 66),
                Size = new Size(436, 70)
            };

            var logsButton = new Button
            {
                Text = "Open logs",
                Location = new Point(12, 148),
                Size = new Size(100, 30)
            };
            logsButton.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", Log.LogDirectory); } catch { }
            };

            var continueButton = new Button
            {
                Text = "Continue",
                DialogResult = DialogResult.OK,
                Location = new Point(238, 148),
                Size = new Size(100, 30),
                Enabled = canContinue
            };

            var exitButton = new Button
            {
                Text = "Exit FlowGrid",
                DialogResult = DialogResult.Cancel,
                Location = new Point(344, 148),
                Size = new Size(104, 30)
            };

            Controls.Add(message);
            Controls.Add(details);
            Controls.Add(logsButton);
            Controls.Add(continueButton);
            Controls.Add(exitButton);
            AcceptButton = canContinue ? continueButton : exitButton;
        }
    }
}
