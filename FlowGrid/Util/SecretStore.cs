using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FlowGrid.Util
{
    /// <summary>
    /// DPAPI-based secret storage for widget plugins. Each secret is encrypted
    /// with the current Windows user's key and stored as its own file under
    /// %LOCALAPPDATA%\FlowGrid\Secrets - never in plain text, never part of
    /// layout exports.
    /// </summary>
    public static class SecretStore
    {
        private static string Directory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowGrid", "Secrets");

        public static string Get(string key)
        {
            try
            {
                var file = FileFor(key);
                if (!File.Exists(file))
                    return null;
                var encrypted = File.ReadAllBytes(file);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        public static void Set(string key, string value)
        {
            try
            {
                var file = FileFor(key);
                if (string.IsNullOrEmpty(value))
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    return;
                }
                System.IO.Directory.CreateDirectory(Directory);
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(file, encrypted);
            }
            catch
            {
                // Storage failure - the caller sees the secret as absent.
            }
        }

        private static string FileFor(string key)
        {
            var name = new StringBuilder();
            foreach (var c in key)
                name.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            return Path.Combine(Directory, name + ".bin");
        }
    }
}
