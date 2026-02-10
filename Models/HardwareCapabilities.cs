namespace PLAI.Models
{
    // Pure value object describing available hardware capabilities.
    // No OS calls, no behavior.
    public class HardwareCapabilities
    {
        // Available system RAM in gigabytes
        public double AvailableRamGb { get; set; }

        // Available GPU VRAM in gigabytes. Use 0 to indicate none or unknown.
        public double AvailableVramGb { get; set; }
    }
}
