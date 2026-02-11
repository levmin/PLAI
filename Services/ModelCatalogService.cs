using System;
using System.Collections.Generic;
using PLAI.Models;

namespace PLAI.Services
{
    /// <summary>
    /// Loads the model catalog from the embedded JSON manifest.
    /// This is the sole authoritative source of models.
    /// </summary>
    public class ModelCatalogService
    {
        private readonly List<ModelDescriptor> _models;

        public ModelCatalogService()
        {
            var manifest = EmbeddedModelCatalogLoader.LoadOrThrow();
            _models = new List<ModelDescriptor>(manifest.Models.Count);

            foreach (var m in manifest.Models)
            {
                var isGpu = string.Equals(m.ComputeTarget, "gpu", StringComparison.OrdinalIgnoreCase);

                _models.Add(new ModelDescriptor
                {
                    Id = m.Id,
                    ComputeTarget = m.ComputeTarget,
                    Name = m.Name,

                    // The spreadsheet "Size" column is ignored by design.
                    // Keep a constant value so selection semantics remain vram/ram-only.
                    SizeGb = 0.0,

                    IsGpuCapable = isGpu,
                    Quantization = m.Quantization,
                    MinRamGb = m.MinRamGb,
                    MinVramGb = m.MinVramGb,
                    DownloadUri = new Uri(m.DownloadUrl, UriKind.Absolute)
                });
            }
        }

        public IReadOnlyList<ModelDescriptor> GetAllModels()
        {
            return _models.AsReadOnly();
        }
    }
}
