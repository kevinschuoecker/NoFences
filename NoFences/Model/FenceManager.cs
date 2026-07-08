using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace NoFences.Model
{
    public class FenceManager
    {
        public static FenceManager Instance { get; } = new FenceManager();

        private const string MetaFileName = "__fence_metadata.xml";

        private readonly string basePath;

        private readonly List<FenceWindow> windows = new List<FenceWindow>();

        public IReadOnlyList<FenceWindow> Windows => windows;

        public bool AllFencesHidden { get; private set; }

        public FenceManager()
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoFences");
            EnsureDirectoryExists(basePath);
        }

        public void LoadFences()
        {
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                var metaFile = Path.Combine(dir, MetaFileName);
                var serializer = new XmlSerializer(typeof(FenceInfo));
                var reader = new StreamReader(metaFile);
                var fence = serializer.Deserialize(reader) as FenceInfo;
                reader.Close();

                ShowFence(fence);
            }
        }

        public void CreateFence(string name)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = name,
                PosX = 100,
                PosY = 250,
                Height = 300,
                Width = 300
            };

            UpdateFence(fenceInfo);
            ShowFence(fenceInfo);
        }

        private void ShowFence(FenceInfo fenceInfo)
        {
            var window = new FenceWindow(fenceInfo);
            windows.Add(window);
            window.FormClosed += (s, e) => windows.Remove(window);
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
                if (!string.IsNullOrEmpty(info.TargetFolder))
                    continue;
                if (!Util.WildcardMatcher.MatchesAny(info.AutoSortPatterns, fileName))
                    continue;
                if (info.Files.Contains(path))
                    return;

                info.Files.Add(path);
                UpdateFence(info);
                window.Invalidate();
                return;
            }
        }

        public void RemoveFence(FenceInfo info)
        {
            Directory.Delete(GetFolderPath(info), true);
        }

        public void UpdateFence(FenceInfo fenceInfo)
        {
            var path = GetFolderPath(fenceInfo);
            EnsureDirectoryExists(path);

            var metaFile = Path.Combine(path, MetaFileName);
            var serializer = new XmlSerializer(typeof(FenceInfo));
            var writer = new StreamWriter(metaFile);
            serializer.Serialize(writer, fenceInfo);
            writer.Close();
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
