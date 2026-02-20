using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PLAI.Models;

namespace PLAI.Services
{
    /// <summary>
    /// Deterministic model download and storage behavior.
    /// v1 contract preserved: Models/{id}/model.onnx must exist and be non-empty.
    /// v1.2 adds multi-file download for Hugging Face folder links (ONNX + sidecars).
    /// </summary>
    public sealed class ModelDownloadService
    {
        private static readonly HttpClient s_http = CreateHttpClient();
        private readonly HuggingFaceTreeClient _hfTreeClient = new HuggingFaceTreeClient(s_http);

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.Timeout = Timeout.InfiniteTimeSpan;
            try
            {
                // Hugging Face is more reliable with a UA.
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PLAI/1.2");
            }
            catch { }
            return http;
        }

        public bool IsModelComplete(string modelId)
        {
            try
            {
                var folder = ModelStoragePaths.GetModelFolderPath(modelId);
                var finalPath = ModelStoragePaths.GetFinalModelPath(modelId);

                if (!Directory.Exists(folder)) return false;
                var fi = new FileInfo(finalPath);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool IsModelReadyForInference(string modelId)
        {
            try
            {
                var folder = ModelStoragePaths.GetModelFolderPath(modelId);
                if (!Directory.Exists(folder)) return false;

                // Minimal readiness for ORT GenAI: genai_config.json + at least one ONNX file.
                var genaiConfig = Path.Combine(folder, "genai_config.json");
                if (!File.Exists(genaiConfig)) return false;

                var onnxFiles = Directory.GetFiles(folder, "*.onnx", SearchOption.AllDirectories);
                if (onnxFiles.Length == 0) return false;

                // Preserve v1 completeness definition.
                return IsModelComplete(modelId);
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> EnsureModelDownloadedAsync(ModelDescriptor model, CancellationToken cancellationToken)
            => EnsureModelDownloadedAsync(model, progress: null, cancellationToken);

        /// <summary>
        /// Ensures the selected model is present on disk.
        /// Returns true if the model is complete after the call, false otherwise (failure/cancel).
        /// On failure/cancel, partial artifacts are cleaned up.
        /// </summary>
        public async Task<bool> EnsureModelDownloadedAsync(
            ModelDescriptor model,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(model.Id)) throw new ArgumentException("Model id is required.", nameof(model));

            if (IsModelReadyForInference(model.Id))
            {
                TryCleanupTempDownloads(model.Id);
                try { AppLogger.Info($"Download completed successfully (id {model.Id})"); } catch { }
                return true;
            }

            try { AppLogger.Info($"Download started (id {model.Id})"); } catch { }

            var modelFolder = ModelStoragePaths.GetModelFolderPath(model.Id);

            try
            {
                Directory.CreateDirectory(modelFolder);

                progress?.Report(new ModelDownloadProgress("Listing"));

                var filesToDownload = await GetFilesToDownloadAsync(model.DownloadUri, cancellationToken).ConfigureAwait(false);
                if (filesToDownload.Count == 0)
                {
                    throw new IOException("No downloadable files were discovered for the selected model.");
                }

                long? totalBytes = filesToDownload.All(f => f.SizeBytes.HasValue)
                    ? filesToDownload.Sum(f => f.SizeBytes!.Value)
                    : null;

                long totalDownloaded = 0;

                foreach (var file in filesToDownload)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var localPath = GetSafeLocalPath(modelFolder, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? modelFolder);

                    var tempPath = localPath + ".download";

                    // Ensure no stale partial artifacts.
                    TryDeleteFile(tempPath);

                    // Deterministic: overwrite any prior file by re-downloading.
                    // (Model folders are only expected to exist when incomplete.)
                    TryDeleteFile(localPath);

                    var resolveUrl = HuggingFaceTreeClient.BuildResolveUrl(file.RepoId, file.Revision, file.PathInRepo);

                    progress?.Report(new ModelDownloadProgress(
                        stage: "Downloading",
                        currentFile: file.RelativePath,
                        currentFileBytesDownloaded: 0,
                        currentFileTotalBytes: file.SizeBytes,
                        totalBytesDownloaded: totalDownloaded,
                        totalBytesToDownload: totalBytes));

                    var downloadedThisFile = await DownloadFileAsync(resolveUrl, tempPath, progress, file, totalBytes, totalDownloaded, cancellationToken)
                        .ConfigureAwait(false);

                    totalDownloaded += downloadedThisFile;

                    // Atomic promote.
                    File.Move(tempPath, localPath, overwrite: true);

                    // Minimal check.
                    var fi = new FileInfo(localPath);
                    if (!fi.Exists || fi.Length <= 0)
                    {
                        throw new IOException($"Downloaded file is missing or empty: {file.RelativePath}");
                    }
                }

                EnsureModelAliasExists(model.Id);

                return true;
            }
            catch (OperationCanceledException)
            {
                try { AppLogger.Warn($"Download cancelled (id {model.Id})"); } catch { }
                CleanupIncompleteModelArtifacts(model.Id);
                return false;
            }
            catch (Exception ex)
            {
                try { AppLogger.Error($"Download failed (id {model.Id}): {ex}"); } catch { }
                CleanupIncompleteModelArtifacts(model.Id);
                return false;
            }
        }

        public void CleanupIncompleteModelArtifacts(string modelId)
        {
            try
            {
                var folder = ModelStoragePaths.GetModelFolderPath(modelId);
                if (!Directory.Exists(folder)) return;

                // Remove temp downloads anywhere under the folder.
                foreach (var f in Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories))
                {
                    TryDeleteFile(f);
                }

                // If the v1 final alias is missing, treat the model folder as incomplete and remove it.
                if (!IsModelComplete(modelId))
                {
                    try { Directory.Delete(folder, recursive: true); } catch { }
                    return;
                }

                // Otherwise, only remove now-empty directories.
                TryDeleteEmptyDirectories(folder);
            }
            catch
            {
                // swallow
            }
        }

        private void TryCleanupTempDownloads(string modelId)
        {
            try
            {
                var folder = ModelStoragePaths.GetModelFolderPath(modelId);
                if (!Directory.Exists(folder)) return;

                foreach (var f in Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories))
                {
                    TryDeleteFile(f);
                }

                TryDeleteEmptyDirectories(folder);
            }
            catch { }
        }

        private static void TryDeleteEmptyDirectories(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;

                // Depth-first.
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length == 0)
                        {
                            Directory.Delete(dir, recursive: false);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task<IReadOnlyList<HuggingFaceFileEntry>> GetFilesToDownloadAsync(Uri manifestDownloadUri, CancellationToken cancellationToken)
        {
            if (manifestDownloadUri is null) throw new ArgumentNullException(nameof(manifestDownloadUri));

            // Support deterministic Hugging Face folder links from the manifest.
            if (HuggingFaceTreeClient.TryParseTreeUrl(manifestDownloadUri, out _))
            {
                return await _hfTreeClient.ListFilesAsync(manifestDownloadUri, cancellationToken).ConfigureAwait(false);
            }

            throw new ArgumentException("DownloadUri must be a Hugging Face folder (tree) URL.", nameof(manifestDownloadUri));
        }

        private static async Task<long> DownloadFileAsync(
            Uri url,
            string tempPath,
            IProgress<ModelDownloadProgress>? progress,
            HuggingFaceFileEntry file,
            long? totalBytes,
            long totalDownloadedBeforeFile,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await s_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            var fileTotal = file.SizeBytes ?? contentLength;

            long downloaded = 0;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                options: FileOptions.SequentialScan);

            var buffer = new byte[1024 * 128];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read <= 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;

                progress?.Report(new ModelDownloadProgress(
                    stage: "Downloading",
                    currentFile: file.RelativePath,
                    currentFileBytesDownloaded: downloaded,
                    currentFileTotalBytes: fileTotal,
                    totalBytesDownloaded: totalDownloadedBeforeFile + downloaded,
                    totalBytesToDownload: totalBytes));
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            return downloaded;
        }

        private static string GetSafeLocalPath(string modelFolder, string relativePath)
        {
            // relativePath comes from the Hugging Face API and may contain forward slashes.
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

            var fullRoot = Path.GetFullPath(modelFolder) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(modelFolder, relativePath));

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Unsafe path in download list.");
            }

            return fullPath;
        }

        private static void EnsureModelAliasExists(string modelId)
        {
            var folder = ModelStoragePaths.GetModelFolderPath(modelId);
            var aliasPath = ModelStoragePaths.GetFinalModelPath(modelId);

            if (File.Exists(aliasPath))
            {
                var fi = new FileInfo(aliasPath);
                if (fi.Length > 0) return;
                TryDeleteFile(aliasPath);
            }

            var preferred = Path.Combine(folder, ModelStoragePaths.ModelFileName);
            if (File.Exists(preferred))
            {
                // If the file exists (perhaps downloaded via its original name), treat it as the alias.
                // (No-op: preferred == alias)
                return;
            }

            var candidates = Directory.GetFiles(folder, "*.onnx", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .Where(f => f.Exists && f.Length > 0)
                .OrderByDescending(f => f.Length)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new IOException("No ONNX model file was downloaded.");
            }

            var primary = candidates[0].FullName;

            // Prefer a hard link to avoid duplication, fall back to copy.
            if (!TryCreateHardLink(aliasPath, primary))
            {
                // As a last resort, copy to satisfy the v1 contract.
                // Do NOT move/rename: GenAI config files may reference the original filename.
                File.Copy(primary, aliasPath, overwrite: true);
            }

            var aliasFi = new FileInfo(aliasPath);
            if (!aliasFi.Exists || aliasFi.Length <= 0)
            {
                throw new IOException("Model alias file is missing or empty after finalization.");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // swallow
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        private static bool TryCreateHardLink(string linkPath, string existingPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath) ?? Path.GetDirectoryName(existingPath) ?? ".");

                // If a prior alias exists, remove it.
                TryDeleteFile(linkPath);

                return CreateHardLink(linkPath, existingPath, IntPtr.Zero);
            }
            catch
            {
                return false;
            }
        }
    }
}
