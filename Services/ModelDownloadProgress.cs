using System;

namespace PLAI.Services
{
    public sealed class ModelDownloadProgress
    {
        public string Stage { get; }
        public string? CurrentFile { get; }
        public long? CurrentFileBytesDownloaded { get; }
        public long? CurrentFileTotalBytes { get; }
        public long? TotalBytesDownloaded { get; }
        public long? TotalBytesToDownload { get; }

        public ModelDownloadProgress(
            string stage,
            string? currentFile = null,
            long? currentFileBytesDownloaded = null,
            long? currentFileTotalBytes = null,
            long? totalBytesDownloaded = null,
            long? totalBytesToDownload = null)
        {
            Stage = stage ?? string.Empty;
            CurrentFile = currentFile;
            CurrentFileBytesDownloaded = currentFileBytesDownloaded;
            CurrentFileTotalBytes = currentFileTotalBytes;
            TotalBytesDownloaded = totalBytesDownloaded;
            TotalBytesToDownload = totalBytesToDownload;
        }

        public double? GetOverallFraction()
        {
            if (TotalBytesDownloaded.HasValue && TotalBytesToDownload.HasValue && TotalBytesToDownload.Value > 0)
            {
                return Math.Clamp((double)TotalBytesDownloaded.Value / TotalBytesToDownload.Value, 0.0, 1.0);
            }

            return null;
        }
    }
}
