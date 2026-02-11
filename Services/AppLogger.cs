using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PLAI.Services
{
    /// <summary>
    /// Minimal, bounded, append-only diagnostic logging (v1.1).
    /// - Location: LocalState/log.txt (package-local storage)
    /// - Format: ISO-8601 UTC timestamp | LEVEL | message
    /// - Max size: ~1 MB (simple overwrite/truncate when exceeded)
    /// Logging must never crash the app and must not block the UI thread.
    /// </summary>
    public static class AppLogger
    {
        private const long MaxBytes = 1024 * 1024; // ~1 MB
        private static readonly SemaphoreSlim s_gate = new(1, 1);

        public static void Info(string message) => Enqueue("INFO", message);
        public static void Warn(string message) => Enqueue("WARN", message);
        public static void Error(string message) => Enqueue("ERROR", message);

        private static void Enqueue(string level, string message)
        {
            // Fire-and-forget. Never block callers and never throw.
            try
            {
                _ = Task.Run(() => WriteAsync(level, message));
            }
            catch
            {
                // swallow
            }
        }

        private static async Task WriteAsync(string level, string message)
        {
            try
            {
                await s_gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var logPath = ModelStoragePaths.GetLogFilePath();

                    // Ensure parent directory exists (LocalState root always exists; still be defensive).
                    var parent = Path.GetDirectoryName(logPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    // Simple size limiting: if file exceeds max, truncate before writing next entry.
                    try
                    {
                        var fi = new FileInfo(logPath);
                        if (fi.Exists && fi.Length > MaxBytes)
                        {
                            // Overwrite/truncate deterministically.
                            using var trunc = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                            // Write nothing; file is now empty.
                        }
                    }
                    catch
                    {
                        // If size check/truncate fails, continue best-effort.
                    }

                    var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                    var line = $"{ts} | {level} | {message}{Environment.NewLine}";
                    var bytes = Encoding.UTF8.GetBytes(line);

                    await using var fs = new FileStream(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        bufferSize: 4096,
                        options: FileOptions.Asynchronous);

                    await fs.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    try { s_gate.Release(); } catch { }
                }
            }
            catch
            {
                // swallow all logging failures
            }
        }
    }
}
