using System;
using System.Runtime.InteropServices;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Experimental capture backend that never reparents a guest window (see
/// docs/internal/deep-audit-2026-07-17.md, section 6). A "shepherded" guest
/// remains an unmodified top-level window for its entire captured lifetime:
/// no SetParent, no style/ex-style mutation, no DPI-message forwarding, no
/// cross-thread input attachment. Instead, the guest is positioned directly
/// over the container's content area and layered immediately above the
/// container in z-order (SetWindowPos with hwndInsertAfter = container),
/// and hidden with ShowWindow(SW_HIDE) when it is not the active tab.
///
/// Because nothing about the guest is mutated, release is symmetric and
/// simple: restore the placement snapshotted at capture time. There is no
/// style/owner/parent surgery to get wrong, no permanently-downgraded DPI
/// awareness, and no compositor invalidation from reparenting — the guest
/// renders and receives input exactly as if it were never touched.
///
/// Known v1 gaps (see the audit's section 6.4): the guest can still slip
/// behind an unrelated window for a frame during rapid alt-tabbing, and a
/// user dragging the guest by its own caption is not clamped back (unlike
/// the Reparent backend's drift watchdog) — both are accepted trade-offs for
/// this prototype, not oversights.
/// </summary>
public sealed class WindowShepherdService
{
    private readonly LoggingService _log;

    public WindowShepherdService(LoggingService log)
    {
        _log = log;
    }

    /// <summary>
    /// Captures a top-level window without reparenting or restyling it.
    /// Returns null and an error message if capture is refused (e.g. UIPI /
    /// elevation mismatch) — the same guards as the Reparent backend.
    /// </summary>
    public CapturedWindow? Capture(IntPtr hwnd, out string? error)
    {
        error = null;
        if (!NativeMethods.IsWindow(hwnd))
        {
            error = "The window no longer exists.";
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == NativeMethods.GetCurrentProcessId())
        {
            error = "Cannot capture a TabDock window.";
            return null;
        }

        if (NativeMethods.IsProcessElevated(pid, out bool targetElevated) && targetElevated)
        {
            NativeMethods.IsCurrentProcessElevated(out bool selfElevated);
            if (!selfElevated)
            {
                error = "Cannot capture an elevated window. Run TabDock as administrator or choose a non-elevated window.";
                _log.Log($"Shepherd capture blocked: elevated target 0x{hwnd.ToInt64():X} PID {pid}");
                return null;
            }
        }

        var originalPlacement = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, out originalPlacement))
        {
            _log.Log($"GetWindowPlacement failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
        }

        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT bounds);
        IntPtr originalParent = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_PARENT);
        IntPtr originalOwner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        var cw = new CapturedWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ExePath = NativeMethods.GetProcessImagePath(pid) ?? string.Empty,
            OriginalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            OriginalPlacement = originalPlacement,
            OriginalParent = originalParent,
            OriginalOwner = originalOwner,
            OriginalStyle = (long)style,
            OriginalExStyle = (long)exStyle,
            OriginalBounds = bounds,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
        };

        _log.Log($"Shepherd-captured 0x{hwnd.ToInt64():X} ({cw.OriginalTitle}) without reparenting; guest={WindowCaptureService.DescribeWindow(hwnd)}");
        return cw;
    }

    /// <summary>
    /// Positions the guest to exactly cover <paramref name="screenRect"/> and
    /// places it immediately above <paramref name="containerHwnd"/> in
    /// z-order, then shows it. Restores the guest first if it is iconic or
    /// zoomed, since either state would otherwise fight the exact-fit resize.
    /// </summary>
    public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;

        if (NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE);

        NativeMethods.SetWindowPos(
            window.Hwnd,
            containerHwnd,
            screenRect.left,
            screenRect.top,
            screenRect.Width,
            screenRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        _log.Log($"SHEPHERD[position] guest={WindowCaptureService.DescribeWindow(window.Hwnd)} rect={screenRect.left},{screenRect.top},{screenRect.Width}x{screenRect.Height}");
    }

    /// <summary>
    /// Hides an inactive shepherded guest. Safe to call on a window that is
    /// already hidden or has been destroyed.
    /// </summary>
    public void Hide(CapturedWindow window)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;
        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
        _log.Log($"SHEPHERD[hide] guest=0x{window.Hwnd.ToInt64():X}");
    }

    /// <summary>
    /// Re-asserts the guest's overlay position/z-order and gives it real
    /// foreground activation. Called when the container itself becomes the
    /// foreground window (e.g. alt-tab back, click on caption) so the guest
    /// is both visually and input-wise "in front" again. No thread-input
    /// attachment is needed: TabDock's process is genuinely the foreground
    /// process at the moment this runs, so SetForegroundWindow is legal here.
    /// </summary>
    public void BringToFront(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;

        PositionAndShow(window, containerHwnd, screenRect);
        bool fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        _log.Log($"SHEPHERD[bring-to-front] guest=0x{window.Hwnd.ToInt64():X} fg={fg}");
    }

    /// <summary>
    /// Releases a shepherded guest back to its original placement. Because
    /// nothing about the guest was mutated while docked (no style, no parent,
    /// no owner), this only needs to restore the placement snapshotted at
    /// capture — there is no style/owner/parent surgery to undo, unlike the
    /// Reparent backend's Release. When <paramref name="show"/> is false the
    /// window is left hidden (guest-initiated hide / tray-style close).
    /// </summary>
    public void Release(CapturedWindow window, bool show = true)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
        {
            _log.Log($"Shepherd release: window 0x{window.Hwnd.ToInt64():X} already gone.");
            return;
        }

        if (!show)
        {
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
            _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide)");
            return;
        }

        NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
        placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

        if (!NativeMethods.SetWindowPlacement(window.Hwnd, ref placement))
        {
            _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
            NativeMethods.SetWindowPos(
                window.Hwnd,
                NativeMethods.HWND_TOP,
                window.OriginalBounds.left,
                window.OriginalBounds.top,
                window.OriginalBounds.Width,
                window.OriginalBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        NativeMethods.ShowWindow(window.Hwnd, (int)placement.showCmd);
        NativeMethods.SetForegroundWindow(window.Hwnd);

        _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) guest={WindowCaptureService.DescribeWindow(window.Hwnd)}");
    }
}
