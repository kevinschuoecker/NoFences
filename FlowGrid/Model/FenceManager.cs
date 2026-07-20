using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace FlowGrid.Model
{
    public class FenceManager
    {
        public static FenceManager Instance { get; } = new FenceManager();

        private const string MetaFileName = "__fence_metadata.xml";

        private readonly string basePath;

        private readonly List<FenceWindow> windows = new List<FenceWindow>();

        public IReadOnlyList<FenceWindow> Windows => windows;

        public bool AllFencesHidden { get; private set; }

        public AppSettings Settings { get; private set; } = new AppSettings();

        public FenceManager()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            basePath = Path.Combine(localAppData, "FlowGrid");

            // One-time migration from the previous app name.
            var legacyPath = Path.Combine(localAppData, "NoFences");
            if (!Directory.Exists(basePath) && Directory.Exists(legacyPath))
            {
                try
                {
                    Directory.Move(legacyPath, basePath);
                }
                catch
                {
                    // Migration failed (e.g. old app still running) - start fresh.
                }
            }

            EnsureDirectoryExists(basePath);
            LoadSettings();
        }

        private string SettingsFile => Path.Combine(basePath, "settings.xml");

        private void LoadSettings()
        {
            Settings = Util.SafeStorage.Load<AppSettings>(SettingsFile) ?? new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                Util.SafeStorage.Save(SettingsFile, Settings);
            }
            catch (Exception ex)
            {
                Util.Log.Error("Failed to save settings", ex);
            }
        }

        public void LoadFences()
        {
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                // Skip folders that are no fences (e.g. the Plugins folder lives here too).
                var metaFile = Path.Combine(dir, MetaFileName);
                if (!File.Exists(metaFile) && !File.Exists(metaFile + ".bak"))
                    continue;

                var fence = Util.SafeStorage.Load<FenceInfo>(metaFile);
                if (fence == null)
                {
                    Util.Log.Error($"Skipping unreadable fence: {dir}");
                    continue;
                }

                try
                {
                    ShowFence(fence);
                }
                catch (Exception ex)
                {
                    Util.Log.Error($"Failed to open fence '{fence.Name}' ({fence.Id})", ex);
                }
            }
        }

        public void CreateFence(string name)
        {
            CreateFence(name, 0);
        }

        public void CreateFence(string name, int fenceType)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = name,
                PosX = 100,
                PosY = 250,
                Height = fenceType == 0 ? 300 : (fenceType == 1 ? 250 : (fenceType == 4 ? 300 : 180)),
                Width = fenceType == 0 ? 300 : 280,
                FenceType = fenceType
            };

            // Sticky notes get a classic yellow-ish tint by default.
            if (fenceType == 1)
                fenceInfo.CustomColor = "#6B5900";

            UpdateFence(fenceInfo);
            ShowFence(fenceInfo);
        }

        /// <summary>
        /// First-run experience: an empty fence to play with plus a sticky note
        /// explaining the essentials, so a new user needs no manual.
        /// </summary>
        public void CreateWelcomeFences()
        {
            CreateFence("My first fence");

            var note = new FenceInfo(Guid.NewGuid())
            {
                Name = "Welcome to FlowGrid",
                FenceType = 1,
                PosX = 430,
                PosY = 250,
                Width = 360,
                Height = 300,
                CustomColor = "#6B5900",
                NoteText =
                    "Welcome! The basics:\r\n\r\n" +
                    "- Drag files from the desktop into a fence\r\n" +
                    "- Double-click an item to open it\r\n" +
                    "- Right-click a fence: colors, tabs, search, sorting, folder portals\r\n" +
                    "- Ctrl+Alt+H hides all fences at once\r\n" +
                    "- Tray icon: widgets, layout backup, autostart\r\n\r\n" +
                    "You can delete this note anytime: right-click -> Remove fence."
            };
            UpdateFence(note);
            ShowFence(note);
        }

        public void CreatePluginFence(Sdk.IFlowGridWidget widget)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = widget.Name,
                PosX = 100,
                PosY = 250,
                Height = 180,
                Width = 280,
                FenceType = 100,
                WidgetPlugin = Util.PluginManager.GetId(widget)
            };

            UpdateFence(fenceInfo);
            ShowFence(fenceInfo);
        }

        private void ShowFence(FenceInfo fenceInfo)
        {
            var window = new FenceWindow(fenceInfo);
            windows.Add(window);
            window.FormClosed += (s, e) =>
            {
                windows.Remove(window);
                // The app lives as long as at least one fence exists.
                if (windows.Count == 0)
                    System.Windows.Forms.Application.Exit();
            };
            window.Show();
            if (AllFencesHidden)
                window.Hide();
        }

        /// <summary>
        /// Quick-hide: toggles the visibility of every fence at once.
        /// </summary>
        public void ToggleAllFences()
        {
            AllFencesHidden = !AllFencesHidden;
            foreach (var window in windows)
            {
                if (AllFencesHidden)
                    window.Hide();
                else
                    window.Show();
            }
        }

        /// <summary>
        /// Adds a file to the first fence whose auto-sort patterns match it.
        /// Must be called on the UI thread.
        /// </summary>
        public void TryAutoSort(string path)
        {
            var fileName = Path.GetFileName(path);
            foreach (var window in windows)
            {
                var info = window.FenceInfo;
                if (info.FenceType != 0 || !string.IsNullOrEmpty(info.TargetFolder))
                    continue;
                if (!Util.WildcardMatcher.MatchesAny(info.AutoSortPatterns, fileName))
                    continue;
                if (info.EnumerateAllFiles().Contains(path))
                    return;

                // With tabs, auto-sorted items land in the first tab.
                var target = info.Tabs.Count > 0 ? info.Tabs[0].Files : info.Files;
                target.Add(path);
                UpdateFence(info);
                Util.DesktopIconHider.HideIfEnabled(path);
                window.NotifyItemAdded(path);
                return;
            }
        }

        public void RemoveFence(FenceInfo info)
        {
            Directory.Delete(GetFolderPath(info), true);
        }

        public void UpdateFence(FenceInfo fenceInfo)
        {
            try
            {
                var path = GetFolderPath(fenceInfo);
                EnsureDirectoryExists(path);
                Util.SafeStorage.Save(Path.Combine(path, MetaFileName), fenceInfo);
            }
            catch (Exception ex)
            {
                Util.Log.Error($"Failed to save fence '{fenceInfo.Name}' ({fenceInfo.Id})", ex);
            }
        }

        /// <summary>
        /// Writes all fences (layout + settings per fence) into a single XML file.
        /// </summary>
        public void ExportLayout(string path)
        {
            var list = windows.Select(w => w.FenceInfo).ToList();
            var serializer = new XmlSerializer(typeof(List<FenceInfo>));
            using (var writer = new StreamWriter(path))
                serializer.Serialize(writer, list);
        }

        /// <summary>
        /// Replaces all current fences with the ones from an exported layout file.
        /// </summary>
        public void ImportLayout(string path)
        {
            List<FenceInfo> imported;
            var serializer = new XmlSerializer(typeof(List<FenceInfo>));
            using (var reader = new StreamReader(path))
                imported = serializer.Deserialize(reader) as List<FenceInfo>;

            if (imported == null || imported.Count == 0)
                throw new InvalidDataException("The file contains no fences.");

            Util.DesktopIconHider.UnhideAllFenced();

            // Create the new fences first so the app never sees zero open windows
            // (closing the last window exits the application).
            var oldWindows = windows.ToList();
            foreach (var info in imported)
            {
                info.Id = Guid.NewGuid();
                UpdateFence(info);
                ShowFence(info);
            }

            foreach (var window in oldWindows)
            {
                RemoveFence(window.FenceInfo);
                window.Close();
            }

            if (Settings.HideFencedDesktopItems)
                Util.DesktopIconHider.HideAllFenced();
        }

        private void EnsureDirectoryExists(string dir)
        {
            var di = new DirectoryInfo(dir);
            if (!di.Exists)
                di.Create();
        }

        private string GetFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(basePath, fenceInfo.Id.ToString());
        }
    }
}
