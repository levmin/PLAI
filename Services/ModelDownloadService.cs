using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PLAI.Models;

namespace PLAI.Services
{
    /// <summary>
    /// Deterministic model download and storage behavior (v1).
    /// Stores a single file per model: Models/{id}/model.onnx
    /// Uses atomic download via temporary file model.onnx.download.
    /// </summary>
    public sealed class ModelDownloadService
    {
        private static readonly HttpClient s_http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            // Keep deterministic / conservative defaults.
            http.Timeout = Timeout.InfiniteTimeSpan;
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

        /// <summary>
        /// Ensures the selected model is present on disk according to the v1 contract.
        /// Returns true if the model is complete after the call, false otherwise (failure/cancel).
        /// </summary>
        public async Task<bool> EnsureModelDownloadedAsync(ModelDescriptor model, CancellationToken cancellationToken)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(model.Id)) throw new ArgumentException("Model id is required.", nameof(model));

            // If already complete, nothing to do.
            if (IsModelComplete(model.Id))
            {
                // Opportunistic cleanup of stray temp file, if any.
                TryDeleteTempAndEmptyFolder(model.Id);
                return true;
            }

            var modelFolder = ModelStoragePaths.GetModelFolderPath(model.Id);
            var finalPath = ModelStoragePaths.GetFinalModelPath(model.Id);
            var tempPath = ModelStoragePaths.GetTempModelPath(model.Id);

            try
            {
                Directory.CreateDirectory(modelFolder);

                // Ensure no partial final file remains (should not normally happen).
                TryDeleteFile(finalPath);

                // Clean any previous temp file.
                TryDeleteFile(tempPath);

                var onnxUri = ResolveOnnxDownloadUri(model.DownloadUri);

                using var request = new HttpRequestMessage(HttpMethod.Get, onnxUri);

                using var response = await s_http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = new FileStream(
                                 tempPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 1024 * 128,
                                 options: FileOptions.SequentialScan))
                {
                    await httpStream.CopyToAsync(fileStream, bufferSize: 1024 * 128, cancellationToken).ConfigureAwait(false);
                    await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // Validate minimal completeness requirement: file exists and size > 0.
                var tempFi = new FileInfo(tempPath);
                if (!tempFi.Exists || tempFi.Length <= 0)
                {
                    throw new IOException("Downloaded file is missing or empty.");
                }

                // Atomic promote: rename temp -> final
                File.Move(tempPath, finalPath, overwrite: true);

                // Final must exist and be non-empty.
                var finalFi = new FileInfo(finalPath);
                if (!finalFi.Exists || finalFi.Length <= 0)
                {
                    throw new IOException("Final model file is missing or empty after rename.");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                // Cancel: required cleanup + false
                TryDeleteFile(tempPath);
                TryDeleteEmptyFolder(modelFolder);
                return false;
            }
            catch
            {
                // Failure: required cleanup + false
                TryDeleteFile(tempPath);
                TryDeleteEmptyFolder(modelFolder);
                return false;
            }
        }

        /// <summary>
        /// Startup recovery cleanup: deletes stale temp download files and removes empty model folders.
        /// Does not attempt partial recovery of incomplete models.
        /// </summary>
        public void CleanupIncompleteModelArtifacts(string modelId)
        {
            try
            {
                TryDeleteTempAndEmptyFolder(modelId);
            }
            catch
            {
                // swallow
            }
        }

        private static void TryDeleteTempAndEmptyFolder(string modelId)
        {
            var folder = ModelStoragePaths.GetModelFolderPath(modelId);
            var temp = ModelStoragePaths.GetTempModelPath(modelId);

            TryDeleteFile(temp);

            // If the final model file is missing, and folder is now empty, remove it.
            TryDeleteEmptyFolder(folder);
        }

        private static void TryDeleteEmptyFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                var entries = Directory.GetFileSystemEntries(folderPath);
                if (entries.Length == 0)
                {
                    Directory.Delete(folderPath, recursive: false);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static Uri ResolveOnnxDownloadUri(Uri manifestDownloadUri)
        {
            if (manifestDownloadUri is null) throw new ArgumentNullException(nameof(manifestDownloadUri));

            var s = manifestDownloadUri.ToString();

            // If already a direct .onnx link, use as-is.
            if (s.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return manifestDownloadUri;
            }

            // Hugging Face folder link pattern from the spreadsheet:
            // https://huggingface.co/{repo}/tree/{revision}/{path}
            // Convert to resolve and append model.onnx
            if (s.Contains("huggingface.co", StringComparison.OrdinalIgnoreCase) &&
                s.Contains("/tree/", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Replace("/tree/", "/resolve/", StringComparison.OrdinalIgnoreCase);
                if (!s.EndsWith("/", StringComparison.Ordinal))
                {
                    s += "/";
                }

                s += ModelStoragePaths.ModelFileName;
                return new Uri(s, UriKind.Absolute);
            }

            // Generic: treat as a folder URL and append model.onnx
            if (!s.EndsWith("/", StringComparison.Ordinal))
            {
                s += "/";
            }

            s += ModelStoragePaths.ModelFileName;
            return new Uri(s, UriKind.Absolute);
        }
    }
}
