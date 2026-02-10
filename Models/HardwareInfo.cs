namespace PLAI.Models
{
    // Simple DTO describing hardware information. No behavior, no OS calls here.
    public record HardwareInfo
    {
        // System RAM in bytes
        public long RamBytes { get; init; }

        // System RAM in gigabytes (for convenience). No logic to keep both in sync.
        public double RamGb { get; init; }

        // Whether the system has a discrete GPU
        public bool HasDiscreteGpu { get; init; }

        // GPU VRAM in bytes
        public long VramBytes { get; init; }

        // GPU VRAM in gigabytes (0 if none/unknown)
        public double VramGb { get; init; }

        // Whether the VRAM value is known (true) or unknown/estimated (false)
        public bool IsVramKnown { get; init; }
        
        // Whether the RAM value is known (true) or unknown/failed-to-detect (false)
        public bool IsRamKnown { get; init; }
    }
}
