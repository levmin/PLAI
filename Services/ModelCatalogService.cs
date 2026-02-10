using System;
using System.Collections.Generic;
using PLAI.Models;

namespace PLAI.Services
{
    // Hardcoded in-memory catalog for demonstration purposes
    public class ModelCatalogService
    {
        private readonly List<ModelDescriptor> _models = new()
        {
            new ModelDescriptor
            {
                Name = "cpu-small",
                SizeGb = 0.8,
                IsGpuCapable = false,
                Quantization = "fp32",
                MinRamGb = 4.0,
                MinVramGb = null,
                DownloadUri = new Uri("https://example.com/models/cpu-small")
            },
            new ModelDescriptor
            {
                Name = "gpu-moderate",
                SizeGb = 2.4,
                IsGpuCapable = true,
                Quantization = "int8",
                MinRamGb = 8.0,
                MinVramGb = 4.0,
                DownloadUri = new Uri("https://example.com/models/gpu-moderate")
            },
            new ModelDescriptor
            {
                Name = "gpu-highmem",
                SizeGb = 12.0,
                IsGpuCapable = true,
                Quantization = "int8",
                MinRamGb = 16.0,
                MinVramGb = 12.0,
                DownloadUri = new Uri("https://example.com/models/gpu-highmem")
            }
        };

        public IReadOnlyList<ModelDescriptor> GetAllModels()
        {
            return _models.AsReadOnly();
        }
    }
}
