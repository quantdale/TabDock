using System;

namespace TabDock.Models;

/// <summary>
/// Identity captured when a user selects a window in the picker. The HWND is
/// only a handle value; the process/thread/class/executable/process-start
/// identity is rechecked immediately before capture so a recycled handle or
/// reused PID cannot target a different application instance than the one the
/// user selected. Title remains mutable display metadata.
/// </summary>
public sealed record WindowCaptureTarget(
    IntPtr Hwnd,
    uint ProcessId,
    string ClassName,
    string Title,
    string ExePath)
{
    /// <summary>
    /// GUI thread identity captured by the picker when it can be read. The
    /// final native admission gate treats a missing value as unverifiable.
    /// </summary>
    public uint WindowThreadId { get; init; }

    /// <summary>
    /// Process creation identity captured by the picker. PID reuse without a
    /// matching process instance must never inherit a checked row.
    /// </summary>
    public long ProcessStartTimeUtcTicks { get; init; }
}
