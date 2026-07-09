using FlowGrid.Model;
using System;
using System.IO;
using System.Threading;

namespace FlowGrid.Util
{
    /// <summary>
    /// Watches the desktop for new files and adds them to fences whose
    /// auto-sort patterns match ("rules", like Fences' automation).
    /// </summary>
    public static class DesktopAutoSorter
    {
        private static FileSystemWatcher watcher;
        private static SynchronizationContext syncContext;

        public static void Start()
        {
            syncContext = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop))
                return;

            watcher = new FileSystemWatcher(desktop)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            watcher.Created += (s, e) => OnDesktopItem(e.FullPath);
            watcher.Renamed += (s, e) => OnDesktopItem(e.FullPath);
        }

        private static void OnDesktopItem(string path)
        {
            syncContext.Post(_ => FenceManager.Instance.TryAutoSort(path), null);
        }

        /// <summary>
        /// Applies the auto-sort rules of all fences to the files currently on the desktop.
        /// Must be called on the UI thread.
        /// </summary>
        public static void ApplyRulesNow()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop))
                return;

            foreach (var entry in Directory.EnumerateFileSystemEntries(desktop))
                FenceManager.Instance.TryAutoSort(entry);
        }
    }
}
