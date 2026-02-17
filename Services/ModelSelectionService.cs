using System.Collections.Generic;
using PLAI.Models;

namespace PLAI.Services
{
    /// <summary>
    /// Pure, deterministic selection function for choosing the best model given hardware capabilities.
    /// No side effects, no LINQ.
    /// </summary>
    public static class ModelSelectionService
    {
        // TEMPORARY PERFORMANCE OVERRIDE (user-requested):
        // Always pick Phi-3.5-mini CPU INT4 AWQ regardless of detected hardware.
        // Remove this once performance work / GPU EP is in place.
        private const string ForcedModelId = "phi-3_5-mini-instruct-cpu-int4-awq";

        /// <summary>
        /// Selects the best model from the provided list according to the algorithm:
        /// Filter: MinRamGb <= AvailableRamGb && (MinVramGb == null || MinVramGb <= AvailableVramGb)
        /// Rank by: (MinVramGb ?? 0) desc, MinRamGb desc, SizeGb desc
        /// Returns the first best match or null if none.
        /// </summary>
        public static ModelDescriptor? ChooseBestModel(HardwareCapabilities capabilities, IReadOnlyList<ModelDescriptor> models)
        {
            if (capabilities is null || models is null)
            {
                return null;
            }

            // TEMPORARY OVERRIDE: forced model selection.
            // Deterministic and side-effect-free.
            foreach (var m in models)
            {
                if (m is not null && string.Equals(m.Id, ForcedModelId, System.StringComparison.Ordinal))
                {
                    return m;
                }
            }

            ModelDescriptor? best = null;

            foreach (var model in models)
            {
                // Filter
                if (model.MinRamGb > capabilities.AvailableRamGb)
                {
                    continue;
                }

                if (model.MinVramGb.HasValue && model.MinVramGb.Value > capabilities.AvailableVramGb)
                {
                    continue;
                }

                // Compute ranking keys
                double modelVramKey = model.MinVramGb ?? 0.0;
                double modelRamKey = model.MinRamGb;
                double modelSizeKey = model.SizeGb;

                if (best is null)
                {
                    best = model;
                    continue;
                }

                double bestVramKey = best.MinVramGb ?? 0.0;
                double bestRamKey = best.MinRamGb;
                double bestSizeKey = best.SizeGb;

                // Compare lexicographically according to the spec
                if (modelVramKey > bestVramKey)
                {
                    best = model;
                    continue;
                }

                if (modelVramKey < bestVramKey)
                {
                    continue;
                }

                // vram keys equal, compare ram
                if (modelRamKey > bestRamKey)
                {
                    best = model;
                    continue;
                }

                if (modelRamKey < bestRamKey)
                {
                    continue;
                }

                // ram keys equal, compare size
                if (modelSizeKey > bestSizeKey)
                {
                    best = model;
                    continue;
                }

                // if equal or smaller, keep existing best (preserve first)
            }

            return best;
        }
    }
}
