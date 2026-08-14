using System;

namespace TabDock.Services;

/// <summary>
/// Pure policy seam for the capture-time DPI contract. A nonzero awareness
/// context is a known classification; a nonzero monitor DPI is required before
/// TabDock admits any guest into its physical-coordinate Shepherd model.
/// </summary>
internal static class DpiCapturePolicy
{
    public const int DpiAwarenessUnaware = 0;
    public const int DpiAwarenessSystem = 1;
    public const int DpiAwarenessPerMonitor = 2;

    public static bool IsKnownAwareness(int awareness)
        => awareness is DpiAwarenessUnaware or DpiAwarenessSystem or DpiAwarenessPerMonitor;

    public static bool HasKnownAwarenessAndMonitorDpi(int awareness, uint effectiveDpi)
        => IsKnownAwareness(awareness) && effectiveDpi != 0;

    public static bool ShouldScaleUnawareMinimum(
        int awareness,
        uint effectiveDpi)
        => HasKnownAwarenessAndMonitorDpi(awareness, effectiveDpi)
            && awareness == DpiAwarenessUnaware;
}
