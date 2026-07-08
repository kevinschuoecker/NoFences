using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoFences.Util
{
    public class ThumbnailProvider
    {
        // Supported .NET images as per https://docs.microsoft.com/en-us/dotnet/api/system.drawing.image.fromfile
        private static readonly string[] SupportedExtensions =
        {
            ".bmp",
            ".gif",
            ".jpg",
            ".jpeg",
            ".png",
            ".tiff",
            ".tif"
        };

        private class ThumbnailState
        {
            public Icon icon;
        }

        // Only allow 4 concurrent images to be decoded to try and prevent OOM errors
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(4);
        private readonly IDictionary<string, ThumbnailState> iconCache = new Dictionary<string, ThumbnailState>();
        public event EventHandler IconThumbnailLoaded;

        public bool IsSupported(string path)
        {
            return SupportedExtensions.Any(ext => path.EndsWith(ext));
        }

        public Icon GenerateThumbnail(string path, int size)
        {
            var key = path + "|" + size;
            if (!iconCache.ContainsKey(key))
            {
                return SubmitGeneratorTask(key, path, size).icon;
            }
            else
            {
                return iconCache[key].icon;
            }
        }

        private ThumbnailState SubmitGeneratorTask(string key, string path, int size)
        {
            var state = new ThumbnailState() { icon = Icon.ExtractAssociatedIcon(path) };
            iconCache[key] = state;

            Task.Run(() =>
            {
                semaphore.Wait();
                try
                {
                    using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                    {
                        using (var img = Image.FromStream(ms))
                        {
                            var thumb = (Bitmap)img.GetThumbnailImage(size, size, () => false, IntPtr.Zero);
                            var icon = Icon.FromHandle(thumb.GetHicon());
                            state.icon = icon;
                            IconThumbnailLoaded?.Invoke(this, new EventArgs());
                        }
                    }
                }
                catch
                {
                    // Keep the fallback icon if the image cannot be decoded.
                }
                finally
                {
                    semaphore.Release();
                }
            });
            return state;
        }

    }
}
