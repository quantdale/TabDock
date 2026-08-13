using System;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the monitor-DPI injection seam. The native
/// helper remains a real-display qualification; conversion policy is tested
/// here without depending on a particular monitor matrix.
/// </summary>
internal static class MonitorDpiSelfTest
{
    public static bool CoversProbeAndConversionSeam()
    {
        var probe = new FakeProbe { Dpi = 144 };
        bool targetDpiIsConsumed = probe.GetEffectiveDpi(new IntPtr(7)) == 144;
        bool conversionUsesProbeDpi = SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))) == 750;

        probe.Dpi = 0;
        bool failedProbeIsUnavailable = probe.GetEffectiveDpi(new IntPtr(7)) == 0
            && SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))) == 500;

        return targetDpiIsConsumed && conversionUsesProbeDpi && failedProbeIsUnavailable;
    }

    private sealed class FakeProbe : IMonitorDpiProbe
    {
        public uint Dpi { get; set; }

        public uint GetEffectiveDpi(IntPtr monitor) => monitor == IntPtr.Zero ? 0 : Dpi;
    }
}
