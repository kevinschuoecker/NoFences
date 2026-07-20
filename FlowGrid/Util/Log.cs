using System;
using System.IO;
using System.Text;

namespace FlowGrid.Util
{
    /// <summary>
    /// Minimal, thread-safe file logger. One file per day under
    /// %LOCALAPPDATA%\FlowGrid\Logs, files older than 14 days are pruned at
    /// startup. Logging must never throw and never block the UI noticeably.
    /// </summary>
    public static class Log
    {
        private static readonly object sync = new object();

        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowGrid", "Logs");

        /// <summary>Creates the log directory, prunes old files and writes the session header.</summary>
        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                foreach (var file in Directory.EnumerateFiles(LogDirectory, "flowgrid-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14))
                            File.Delete(file);
                    }
                    catch
                    {
                        // Pruning is best-effort.
                    }
                }
            }
            catch
            {
                // No log directory - Write() will keep silently failing, app still works.
            }

            var version = typeof(Log).Assembly.GetName().Version;
            Info($"===== FlowGrid {version} starting | OS {Environment.OSVersion} | .NET {Environment.Version} | 64bit={Environment.Is64BitProcess} =====");
        }

        public static void Info(string message) => Write("INFO ", message);

        public static void Warn(string message) => Write("WARN ", message);

        public static void Error(string message, Exception exception = null)
        {
            var text = message;
            if (exception != null)
                text += Environment.NewLine + exception;
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append("] ")
                    .AppendLine(message)
                    .ToString();
                var file = Path.Combine(LogDirectory, "flowgrid-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                lock (sync)
                {
                    File.AppendAllText(file, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
