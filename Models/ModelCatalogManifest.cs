using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PLAI.Models
{
    /// <summary>
    /// Deterministic, reviewable manifest of supported models.
    /// This is the only source of truth for the model catalog.
    /// </summary>
    public sealed class ModelCatalogManifest
    {
        [JsonPropertyName("manifestVersion")]
        public string ManifestVersion { get; set; } = "1.0";

        [JsonPropertyName("generatedFrom")]
        public string GeneratedFrom { get; set; } = string.Empty;

        [JsonPropertyName("generatedOn")]
        public string GeneratedOn { get; set; } = string.Empty;

        [JsonPropertyName("models")]
        public List<ModelCatalogManifestModel> Models { get; set; } = new();
    }

    public sealed class ModelCatalogManifestModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("computeTarget")]
        public string ComputeTarget { get; set; } = string.Empty;

        [JsonPropertyName("minRamGb")]
        public int MinRamGb { get; set; }

        [JsonPropertyName("minVramGb")]
        public int MinVramGb { get; set; }

        [JsonPropertyName("quantization")]
        public string Quantization { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
