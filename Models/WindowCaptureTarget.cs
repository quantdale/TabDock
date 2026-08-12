using System;

namespace TabDock.Models;

/// <summary>
/// Identity captured when a user selects a window in the picker. The HWND is
/// only a handle value; the process/class/executable identity is rechecked
/// immediately before capture so a recycled handle cannot target a different
/// application than the one the user selected. <see cref="Title"/> is retained
/// as picker metadata and is intentionally not an identity gate because normal
/// applications change their titles during capture.
/// </summary>
public sealed record WindowCaptureTarget(
    IntPtr Hwnd,
    uint ProcessId,
    string ClassName,
    string Title,
    string ExePath);
