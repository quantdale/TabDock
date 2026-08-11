using System;

namespace TabDock.Models;

/// <summary>
/// Identity captured when a user selects a window in the picker. The HWND is
/// only a handle value; the process/class/title/executable tuple is rechecked
/// immediately before capture so a recycled handle cannot target a different
/// application than the one the user selected.
/// </summary>
public sealed record WindowCaptureTarget(
    IntPtr Hwnd,
    uint ProcessId,
    string ClassName,
    string Title,
    string ExePath);
