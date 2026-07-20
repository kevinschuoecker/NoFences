using System;
using System.IO;
using System.Xml.Serialization;

namespace FlowGrid.Util
{
    /// <summary>
    /// Crash-safe XML persistence: writes go to a temp file first and are then
    /// atomically swapped in (keeping the previous version as .bak). Reads fall
    /// back to the .bak when the main file is corrupt; corrupt files are kept
    /// aside as .corrupt-* for diagnosis instead of being overwritten.
    /// </summary>
    public static class SafeStorage
    {
        public static void Save<T>(string path, T value)
        {
            var tmp = path + ".tmp";
            var serializer = new XmlSerializer(typeof(T));
            using (var writer = new StreamWriter(tmp))
                serializer.Serialize(writer, value);

            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        /// <summary>
        /// Loads the file, falling back to its .bak. Returns null when neither
        /// can be read; the unreadable main file is preserved as .corrupt-*.
        /// </summary>
        public static T Load<T>(string path) where T : class
        {
            var result = TryRead<T>(path);
            if (result != null)
                return result;

            if (File.Exists(path))
            {
                Log.Warn($"Unreadable file, trying backup: {path}");
                PreserveCorruptFile(path);
            }

            var backup = path + ".bak";
            result = TryRead<T>(backup);
            if (result != null)
            {
                Log.Warn($"Recovered from backup: {backup}");
                return result;
            }
            return null;
        }

        private static T TryRead<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                var serializer = new XmlSerializer(typeof(T));
                using (var reader = new StreamReader(path))
                    return serializer.Deserialize(reader) as T;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read {path}", ex);
                return null;
            }
        }

        private static void PreserveCorruptFile(string path)
        {
            try
            {
                File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }
            catch
            {
                // Preservation is best-effort.
            }
        }
    }
}
