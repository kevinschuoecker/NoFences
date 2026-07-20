using System.Drawing;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using System.IO;
using FlowGrid.Win32;
using FlowGrid.Util;

namespace FlowGrid.Model
{
    public class FenceEntry
    {
        public string Path { get; }

        public EntryType Type { get; }

        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        private FenceEntry(string path, EntryType type)
        {
            Path = path;
            Type = type;
        }

        public static FenceEntry FromPath(string path)
        {
            if (File.Exists(path))
                return new FenceEntry(path, EntryType.File);
            else if (Directory.Exists(path))
                return new FenceEntry(path, EntryType.Folder);
            else return null;
        }

        /// <summary>
        /// Creates an entry whose type is already known (e.g. from a directory
        /// enumeration), skipping the two filesystem existence checks of
        /// <see cref="FromPath"/> - important in hot paths like portal rendering.
        /// </summary>
        public static FenceEntry FromKnownType(string path, EntryType type)
        {
            return new FenceEntry(path, type);
        }

        public Icon ExtractIcon(ThumbnailProvider thumbnailProvider, int size)
        {
            if (Type == EntryType.File && thumbnailProvider.IsSupported(Path))
                return thumbnailProvider.GenerateThumbnail(Path, size);

            var shellIcon = IconUtil.GetShellIcon(Path, size);
            if (shellIcon != null)
                return shellIcon;

            if (Type == EntryType.File)
                return Icon.ExtractAssociatedIcon(Path);

            return IconUtil.FolderLarge;
        }

        public void Open()
        {
            Task.Run(() =>
            {
                // start asynchronously
                try
                {
                    if (Type == EntryType.File)
                        Process.Start(Path);
                    else if (Type == EntryType.Folder)
                        Process.Start("explorer.exe", Path);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to start: {e}");
                }
            });
        }
    }
}
