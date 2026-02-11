namespace PLAI.Models
{
    // Data Transfer Object describing a model - no behavior, no validation
    public class ModelDescriptor
    {
        // Stable synthetic id persisted as-is.
        public string Id { get; set; } = default!;

        // Human-readable name for display.
        public string Name { get; set; } = default!;

        public double SizeGb { get; set; }

        public bool IsGpuCapable { get; set; }

        public string Quantization { get; set; } = default!;

        public double MinRamGb { get; set; }

        public double? MinVramGb { get; set; }

        public System.Uri DownloadUri { get; set; } = default!;
    }
}
