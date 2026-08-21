using System;

namespace TabDock.Models;

/// <summary>
/// A top-level window shepherded into a TabDock group (see
/// Services/WindowShepherdService.cs). The guest's hierarchy/style/owner
/// identity is never mutated — no SetParent, no style/ex-style change, no
/// owner change. A reversible private HWND token is used only to reject
/// same-process handle recycling. Presentation changes are limited to
/// placement, z-order, visibility, and DWM transition suppression. This only
/// needs to snapshot enough to restore its on-screen placement and activation
/// state on release, not undo any surgery.
/// </summary>
public sealed class CapturedWindow
{
    public IntPtr Hwnd { get; set; }

    public uint ProcessId { get; set; }

    /// <summary>GUI thread that owned the HWND at capture time.</summary>
    public uint WindowThreadId { get; set; }

    /// <summary>
    /// Nonzero per-capture token stored as a reversible HWND property. It
    /// distinguishes same-process HWND recycling, which PID/thread/class/exe
    /// checks alone cannot prove is the original window instance.
    /// </summary>
    public long WindowIdentityToken { get; set; }

    /// <summary>
    /// Nonzero one-shot HWND-instance proof installed while capture identity
    /// was strongly proven. Unlike <see cref="WindowIdentityToken"/> it
    /// deliberately survives release so the destructive close-group path can
    /// prove the exact window instance before posting WM_CLOSE.
    /// </summary>
    public long ReleasedCloseNonce { get; set; }

    /// <summary>Required process-instance identity used by mutation gates and crash rescue.</summary>
    public long ProcessStartTimeUtcTicks { get; set; }

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
    /// GetWindowPlacement call. When false the struct is zeroed (showCmd ==
    /// SW_HIDE) and Release must not apply it — it restores
    /// <see cref="OriginalBounds"/> and shows the guest with SW_SHOW instead.
    /// </summary>
    public bool HasValidPlacement { get; set; }

    public NativeMethods.RECT OriginalBounds { get; set; }

    /// <summary>Tracks whether the guest was maximized at capture; restored to that state when released.</summary>
    public bool WasMaximized { get; set; }

    /// <summary>Whether the guest was visible before TabDock began shepherding it.</summary>
    public bool OriginallyVisible { get; set; }

    /// <summary>Whether DWM transition suppression was already enabled before capture.</summary>
    public bool HasOriginalTransitionsState { get; set; }

    public bool OriginalTransitionsDisabled { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(CustomLabel) ? OriginalTitle : CustomLabel;

    public override string ToString() => $"{DisplayLabel} (0x{Hwnd.ToInt64():X})";
}
