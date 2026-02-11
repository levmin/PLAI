using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using PLAI.Models;

namespace PLAI.Services
{
    internal static class EmbeddedModelCatalogLoader
    {
        private const string ResourceName = "PLAI.ModelCatalog.manifest.json";

        public static ModelCatalogManifest LoadOrThrow()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded model manifest '{ResourceName}' was not found. " +
                    "Ensure Data\\ModelCatalog\\ModelCatalog.manifest.json is marked as EmbeddedResource.");
            }

            try
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();

                var manifest = JsonSerializer.Deserialize<ModelCatalogManifest>(json);
                if (manifest is null)
                {
                    throw new InvalidOperationException("Model manifest deserialized to null.");
                }

                if (!string.Equals(manifest.ManifestVersion, "1.0", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unsupported model manifestVersion '{manifest.ManifestVersion}'. Expected '1.0'.");
                }

                if (manifest.Models is null || manifest.Models.Count == 0)
                {
                    throw new InvalidOperationException("Model manifest contains no models.");
                }

                // Basic validation (fail fast, deterministic)
                for (int i = 0; i < manifest.Models.Count; i++)
                {
                    var m = manifest.Models[i];
                    if (string.IsNullOrWhiteSpace(m.Id)) throw new InvalidOperationException($"Model[{i}] missing id.");
                    if (string.IsNullOrWhiteSpace(m.Name)) throw new InvalidOperationException($"Model[{i}] missing name.");
                    if (string.IsNullOrWhiteSpace(m.ComputeTarget)) throw new InvalidOperationException($"Model[{i}] missing computeTarget.");
                    if (m.MinRamGb < 0) throw new InvalidOperationException($"Model[{i}] has negative minRamGb.");
                    if (m.MinVramGb < 0) throw new InvalidOperationException($"Model[{i}] has negative minVramGb.");

                    var ct = m.ComputeTarget.Trim().ToLowerInvariant();
                    if (ct != "cpu" && ct != "gpu")
                    {
                        throw new InvalidOperationException($"Model[{i}] has invalid computeTarget '{m.ComputeTarget}'. Expected 'cpu' or 'gpu'.");
                    }

                    if (ct == "cpu" && m.MinVramGb != 0)
                    {
                        throw new InvalidOperationException($"Model[{i}] is cpu but minVramGb != 0.");
                    }

                    // Validate URL shape early (will throw on bad URL)
                    _ = new Uri(m.DownloadUrl, UriKind.Absolute);
                }

                return manifest;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("Failed to load embedded model manifest.", ex);
            }
        }
    }
}
