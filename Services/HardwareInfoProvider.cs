using System;
using PLAI.Models;
using PLAI.Interop;

namespace PLAI.Services
{
    // Provider that attempts to detect RAM via a single Win32 call, but never throws.
    public class HardwareInfoProvider : IHardwareInfoProvider
    {
        public HardwareInfo GetHardwareInfo()
        {
            try
            {
                var totalPhys = MemoryStatus.TryGetTotalPhysicalMemoryBytes();

                if (totalPhys.HasValue)
                {
                    // Convert bytes to gigabytes with round-to-nearest GiB rule
                    double gb = totalPhys.Value / (1024.0 * 1024.0 * 1024.0);
                    double roundedGb = Math.Round(gb);

                    try { AppLogger.Info($"Detected RAM {roundedGb} GB"); } catch { }

                    // Attempt to detect GPU adapters and VRAM
                    try
                    {
                        var (dxgiInit, foundNonSoftware, maxDedicated) = DxgiInterop.TryGetMaxDedicatedVideoMemoryBytes();

                        double vramGb = 0.0;
                        bool isVramKnown = false;

                        if (dxgiInit && foundNonSoftware)
                        {
                            // Round to nearest GiB
                            vramGb = System.Math.Round(maxDedicated / (1024.0 * 1024.0 * 1024.0));
                            isVramKnown = true;
                            try { AppLogger.Info($"GPU present: true, VRAM {vramGb} GB"); } catch { }
                        }

                        return new HardwareInfo
                        {
                            RamBytes = (long)totalPhys.Value,
                            RamGb = roundedGb,
                            HasDiscreteGpu = foundNonSoftware,
                            VramBytes = (long)maxDedicated,
                            VramGb = vramGb,
                            IsVramKnown = isVramKnown,
                            IsRamKnown = true
                        };
                    }
                    catch
                    {
                        try { AppLogger.Info("GPU present: false, VRAM unknown"); } catch { }
                        return new HardwareInfo
                        {
                            RamBytes = (long)totalPhys.Value,
                            RamGb = roundedGb,
                            HasDiscreteGpu = false,
                            // GPU detection failed; treat as no discrete GPU known.
                            
                            VramBytes = 0,
                            VramGb = 0.0,
                            IsVramKnown = false,
                            IsRamKnown = true
                        };
                    }
                }
            }
            catch
            {
                // swallow and fall through to unknown result
            }

            try { AppLogger.Warn("Hardware detection returned unknown values"); } catch { }

            // Fallback: unknown/zero values
            return new HardwareInfo
            {
                RamBytes = 0,
                RamGb = 0.0,
                HasDiscreteGpu = false,
                VramBytes = 0,
                VramGb = 0.0,
                IsVramKnown = false,
                IsRamKnown = false
            };
        }
    }
}
