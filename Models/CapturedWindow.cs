using System;

namespace TabDock.Models;

/// <summary>
/// A top-level window shepherded into a TabDock group (see
/// Services/WindowShepherdService.cs). The guest's window identity is never
/// mutated — no SetParent, no style/ex-style change, no owner change — only
/// reversible placement, z-order, and visibility changes are applied. DWM
/// transition attributes are observed only by diagnostics and are never
/// mutated by production capture. This only needs to snapshot enough to restore its
/// on-screen placement and activation state on release, not undo any surgery.
/// </summary>
public sealed class CapturedWindow
{
    public IntPtr Hwnd { get; set; }

    public uint ProcessId { get; set; }

    public string ExePath { get; set; } = string.Empty;

    /// <summary>
    /// Class name captured with the HWND identity. This is intentionally kept
    /// separate from persisted tab metadata so immediate cleanup can refuse
    /// to touch a recycled HWND.
    /// </summary>
    public string OriginalClassName { get; set; } = string.Empty;

    public string OriginalTitle { get; set; } = string.Empty;

    public string CustomLabel { get; set; } = string.Empty;

    /// <summary>Snapshot of the guest's placement at capture time; used to restore exact window state on release.</summary>
    public NativeMethods.WINDOWPLACEMENT OriginalPlacement { get; set; }

    /// <summary>
    /// Whether <see cref="OriginalPlacement"/> came from a successful
    /// GetWindowPlacement call. When false the struct may contain only the
    /// caller-initialized length or partial native output; Release must not
    /// apply it — it restores <see cref="OriginalBounds"/> and shows the guest
    /// with SW_SHOW instead.
    /// </summary>
    public bool HasValidPlacement { get; set; }

    public NativeMethods.RECT OriginalBounds { get; set; }

    /// <summary>Tracks whether the guest was maximized at capture; restored to that state when released.</summary>
    public bool WasMaximized { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(CustomLabel) ? OriginalTitle : CustomLabel;

    public override string ToString() => $"{DisplayLabel} (0x{Hwnd.ToInt64():X})";
}
