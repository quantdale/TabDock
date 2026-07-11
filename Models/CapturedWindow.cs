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

    public long OriginalStyle { get; set; }

    public long OriginalExStyle { get; set; }

    public NativeMethods.RECT OriginalBounds { get; set; }

    public bool WasMaximized { get; set; }

    public bool RenderHealth { get; set; } = true;

    public string DisplayLabel => string.IsNullOrWhiteSpace(CustomLabel) ? OriginalTitle : CustomLabel;

    public override string ToString() => $"{DisplayLabel} (0x{Hwnd.ToInt64():X})";
}
