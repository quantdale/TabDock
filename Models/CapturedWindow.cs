using System;

namespace TabDock.Models;

/// <summary>
/// A top-level window shepherded into a TabDock group (see
/// Services/WindowShepherdService.cs). Nothing about the guest is ever
/// mutated — no SetParent, no style/ex-style change, no owner change — so
/// this only needs to snapshot enough to restore its on-screen placement and
/// activation state on release, not undo any surgery.
/// </summary>
public sealed class CapturedWindow
{
    public IntPtr Hwnd { get; set; }

    public uint ProcessId { get; set; }

    public string ExePath { get; set; } = string.Empty;

    public string OriginalTitle { get; set; } = string.Empty;

    public string CustomLabel { get; set; } = string.Empty;

    public NativeMethods.WINDOWPLACEMENT OriginalPlacement { get; set; }

    public NativeMethods.RECT OriginalBounds { get; set; }

    public bool WasMaximized { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(CustomLabel) ? OriginalTitle : CustomLabel;

    public override string ToString() => $"{DisplayLabel} (0x{Hwnd.ToInt64():X})";
}
