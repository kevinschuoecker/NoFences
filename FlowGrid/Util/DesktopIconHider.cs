using FlowGrid.Model;
using System;
using System.IO;

namespace FlowGrid.Util
{
    /// <summary>
    /// Hides/unhides fenced files on the desktop via the Hidden file attribute,
    /// so the desktop only shows what is not already inside a fence.
    /// </summary>
    public static class DesktopIconHider
    {
        private static bool Enabled => FenceManager.Instance.Settings.HideFencedDesktopItems;

        private static bool IsOnDesktop(string path)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var parent = Path.GetDirectoryName(path);
            return string.Equals(parent, desktop, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Hides a fenced item on the desktop, if the feature is enabled.</summary>
        public static void HideIfEnabled(string path)
        {
            if (Enabled)
                SetHidden(path, true);
        }

        /// <summary>Makes an item visible on the desktop again (e.g. after removing it from a fence).</summary>
        public static void Unhide(string path)
        {
            SetHidden(path, false);
        }

        /// <summary>Hides every desktop file currently referenced by any fence.</summary>
        public static void HideAllFenced()
        {
            ForAllFencedItems(p => SetHidden(p, true));
        }

        /// <summary>Restores visibility of every desktop file referenced by any fence.</summary>
        public static void UnhideAllFenced()
        {
            ForAllFencedItems(p => SetHidden(p, false));
        }

        private static void ForAllFencedItems(Action<string> action)
        {
            foreach (var window in FenceManager.Instance.Windows)
                foreach (var file in window.FenceInfo.EnumerateAllFiles())
                    action(file);
        }

        private static void SetHidden(string path, bool hidden)
        {
            try
            {
                if (!IsOnDesktop(path) || (!File.Exists(path) && !Directory.Exists(path)))
                    return;

                var attributes = File.GetAttributes(path);
                var newAttributes = hidden
                    ? attributes | FileAttributes.Hidden
                    : attributes & ~FileAttributes.Hidden;
                if (newAttributes != attributes)
                    File.SetAttributes(path, newAttributes);
            }
            catch
            {
                // Access denied or file vanished - nothing sensible to do.
            }
        }
    }
}
