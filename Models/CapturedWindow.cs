using System;

namespace TabDock.Models;

/// <summary>
/// A captured top-level window that has been reparented into a TabDock group.
/// This class snapshots the original state so the window can be released later.
/// </summary>
public sealed class CapturedWindow
{
    public IntPtr Hwnd { get; set; }

    public uint ProcessId { get; set; }

    public string ExePath { get; set; } = string.Empty;

    public string OriginalTitle { get; set; } = string.Empty;

    public string CustomLabel { get; set; } = string.Empty;

    public NativeMethods.WINDOWPLACEMENT OriginalPlacement { get; set; }

    public IntPtr OriginalParent { get; set; }

    /// <summary>
    /// The original owner window of a top-level guest (GW_OWNER), if any. Captured
    /// before reparenting so it can be restored after release.
    /// </summary>
    public IntPtr OriginalOwner { get; set; }

    public long OriginalStyle { get; set; }

    public long OriginalExStyle { get; set; }

    public NativeMethods.RECT OriginalBounds { get; set; }

    /// <summary>
    /// The guest's original DPI before capture, captured while it was still a
    /// standalone top-level window on its original monitor. Retained for
    /// diagnostics; the forward DPI message uses the host monitor DPI.
    /// </summary>
    public uint OriginalDpi { get; set; }

    /// <summary>
    /// The guest's DPI awareness context before capture. Used to decide whether
    /// a synthetic WM_DPICHANGED is appropriate (only Per-Monitor-V2 guests
    /// handle it meaningfully).
    /// </summary>
    public IntPtr OriginalAwarenessContext { get; set; }

    public bool WasMaximized { get; set; }

    public bool RenderHealth { get; set; } = true;

    public string DisplayLabel => string.IsNullOrWhiteSpace(CustomLabel) ? OriginalTitle : CustomLabel;

    public override string ToString() => $"{DisplayLabel} (0x{Hwnd.ToInt64():X})";
}
