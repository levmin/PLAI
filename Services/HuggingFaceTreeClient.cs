using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PLAI.Services
{
    internal readonly record struct HuggingFaceTreeLocation(string RepoId, string Revision, string BasePath);

    internal readonly record struct HuggingFaceFileEntry(
        string RepoId,
        string Revision,
        string PathInRepo,
        string RelativePath,
        long? SizeBytes);

    internal sealed class HuggingFaceTreeClient
    {
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public HuggingFaceTreeClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public static bool TryParseTreeUrl(Uri treeUrl, out HuggingFaceTreeLocation location)
        {
            location = default;
            if (treeUrl is null) return false;
            if (!treeUrl.Host.EndsWith("huggingface.co", StringComparison.OrdinalIgnoreCase)) return false;

            // Expected:
            // https://huggingface.co/{org}/{repo}/tree/{revision}/{path...}
            var segments = treeUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 4) return false;

            var org = segments[0];
            var repo = segments[1];
            if (!string.Equals(segments[2], "tree", StringComparison.OrdinalIgnoreCase)) return false;

            var revision = segments[3];
            var basePath = segments.Length > 4 ? string.Join('/', segments.Skip(4)) : string.Empty;

            location = new HuggingFaceTreeLocation($"{org}/{repo}", revision, basePath);
            return true;
        }

        public async Task<IReadOnlyList<HuggingFaceFileEntry>> ListFilesAsync(Uri treeUrl, CancellationToken cancellationToken)
        {
            if (!TryParseTreeUrl(treeUrl, out var loc))
            {
                throw new ArgumentException("Unsupported Hugging Face tree URL.", nameof(treeUrl));
            }

            var apiUrl = BuildTreeApiUrl(loc);

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var items = await JsonSerializer.DeserializeAsync<List<TreeItem>>(stream, s_json, cancellationToken)
                .ConfigureAwait(false) ?? new List<TreeItem>();

            var results = new List<HuggingFaceFileEntry>(items.Count);

            foreach (var item in items)
            {
                if (!IsFileItem(item.Type)) continue;

                var pathInRepo = item.Path ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pathInRepo)) continue;

                var relative = GetRelativePath(loc.BasePath, pathInRepo);
                results.Add(new HuggingFaceFileEntry(loc.RepoId, loc.Revision, pathInRepo, relative, item.Size));
            }

            // Deterministic ordering.
            return results.OrderBy(r => r.RelativePath, StringComparer.Ordinal).ToList();
        }

        public static Uri BuildResolveUrl(string repoId, string revision, string pathInRepo)
        {
            if (string.IsNullOrWhiteSpace(repoId)) throw new ArgumentException("Repo id is required.", nameof(repoId));
            if (string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Revision is required.", nameof(revision));
            if (string.IsNullOrWhiteSpace(pathInRepo)) throw new ArgumentException("Path is required.", nameof(pathInRepo));

            var escapedRepo = repoId.Trim('/');
            var escapedRevision = Uri.EscapeDataString(revision);
            var escapedPath = EscapePath(pathInRepo);

            var url = $"https://huggingface.co/{escapedRepo}/resolve/{escapedRevision}/{escapedPath}";
            return new Uri(url, UriKind.Absolute);
        }

        private static Uri BuildTreeApiUrl(HuggingFaceTreeLocation loc)
        {
            // IMPORTANT: Hugging Face expects repo id in the path as "org/repo".
            // Do NOT escape the slash as %2F (many servers won't decode it in path segments).
            var repoEsc = EscapeRepoId(loc.RepoId);
            var revEsc = Uri.EscapeDataString(loc.Revision);
            var basePathEsc = EscapePath(loc.BasePath);

            var url = $"https://huggingface.co/api/models/{repoEsc}/tree/{revEsc}";
            if (!string.IsNullOrEmpty(basePathEsc))
            {
                url += "/" + basePathEsc;
            }

            // 'recursive' and 'expand' are used by huggingface_hub clients.
            url += "?recursive=true&expand=true";
            return new Uri(url, UriKind.Absolute);
        }

        private static string EscapePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return string.Join('/', parts.Select(Uri.EscapeDataString));
        }

        private static string EscapeRepoId(string repoId)
        {
            // repoId is "org/repo".
            // Escape each segment but preserve the slash.
            if (string.IsNullOrWhiteSpace(repoId)) return string.Empty;
            var parts = repoId.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return string.Join('/', parts.Select(Uri.EscapeDataString));
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(basePath)) return fullPath;

            basePath = basePath.Trim('/');
            if (fullPath.StartsWith(basePath + "/", StringComparison.Ordinal))
            {
                return fullPath.Substring(basePath.Length + 1);
            }

            // Some responses may already be relative.
            return fullPath;
        }

        private static bool IsFileItem(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "blob", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class TreeItem
        {
            [JsonPropertyName("path")]
            public string? Path { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("size")]
            public long? Size { get; set; }
        }
    }
}
