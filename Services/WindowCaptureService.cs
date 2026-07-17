using System;
using System.Runtime.InteropServices;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Captures, lays out, and releases external windows into/out of TabDock host containers.
/// </summary>
public sealed class WindowCaptureService
{
    private readonly LoggingService _log;
    private readonly IconService _icons;
    private readonly DpiService _dpi;

    public WindowCaptureService(LoggingService log, IconService icons, DpiService dpi)
    {
        _log = log;
        _icons = icons;
        _dpi = dpi;
    }

    /// <summary>
    /// Captures a top-level window and reparents it into the supplied host HWND.
    /// Returns null and an error message if the capture fails (e.g. UIPI / elevation mismatch).
    /// </summary>
    public CapturedWindow? Capture(IntPtr hwnd, IntPtr hostHwnd, out string? error)
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

        // Elevation / UIPI guard.
        if (NativeMethods.IsProcessElevated(pid, out bool targetElevated) && targetElevated)
        {
            NativeMethods.IsCurrentProcessElevated(out bool selfElevated);
            if (!selfElevated)
            {
                error = "Cannot capture an elevated window. Run TabDock as administrator or choose a non-elevated window.";
                _log.Log($"Capture blocked: elevated target 0x{hwnd.ToInt64():X} PID {pid}");
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

        // Capture the guest's baseline DPI and awareness context while it is still a
        // standalone top-level window on its original monitor. These are used to
        // reconcile the guest's logical coordinate system with the host monitor.
        uint originalDpi = _dpi.GetDpi(hwnd);
        IntPtr originalAwareness = _dpi.GetAwarenessContext(hwnd);

        // Diagnostic snapshot before reparenting (class, DPI awareness, geometry).
        LogGuestDiagnostics(hwnd, "pre-capture");

        // Preserve the original placement (including maximized state) so it can be
        // restored accurately on release. Do not restore the window here; that
        // would overwrite the rcNormalPosition / ptMaxPosition data we need.

        IntPtr previousParent = NativeMethods.SetParent(hwnd, hostHwnd);
        if (previousParent == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            error = err == NativeMethods.ERROR_ACCESS_DENIED
                ? "Access denied. The target window may be elevated or protected by UIPI."
                : $"SetParent failed: {NativeMethods.FormatLastError()}";
            _log.Log($"SetParent failed for 0x{hwnd.ToInt64():X}: {error}");
            return null;
        }

        nint newStyle = (nint)(((long)style & ~(long)(
            NativeMethods.WS_POPUP |
            NativeMethods.WS_CAPTION |
            NativeMethods.WS_THICKFRAME |
            NativeMethods.WS_MINIMIZEBOX |
            NativeMethods.WS_MAXIMIZEBOX |
            NativeMethods.WS_MAXIMIZE |
            NativeMethods.WS_SYSMENU)) |
            (long)(NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS));

        nint newExStyle = (nint)(((long)exStyle & ~(long)(
            NativeMethods.WS_EX_WINDOWEDGE |
            NativeMethods.WS_EX_CLIENTEDGE |
            NativeMethods.WS_EX_DLGMODALFRAME)));

        if (NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, newStyle) == 0 && Marshal.GetLastWin32Error() != 0)
        {
            _log.Log($"SetWindowLongPtr GWL_STYLE failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
        }

        if (NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, newExStyle) == 0 && Marshal.GetLastWin32Error() != 0)
        {
            _log.Log($"SetWindowLongPtr GWL_EXSTYLE failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
        }

        var cw = new CapturedWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ExePath = _icons.GetProcessImagePath(pid) ?? string.Empty,
            OriginalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            OriginalPlacement = originalPlacement,
            OriginalParent = originalParent,
            OriginalOwner = originalOwner,
            OriginalStyle = (long)style,
            OriginalExStyle = (long)exStyle,
            OriginalBounds = bounds,
            OriginalDpi = originalDpi,
            OriginalAwarenessContext = originalAwareness,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
        };

        // Chrome/Electron/Edge guests do not receive WM_DPICHANGED across the SetParent
        // boundary, so tell a Per-Monitor-V2 guest the host monitor's DPI explicitly
        // before sizing it. The Layout call below then applies the guest's logical
        // scale to the host physical client rect.
        NotifyDpiChanged(cw, hostHwnd);

        Layout(hwnd, hostHwnd, "capture");

        // Focus/activation is now handled by NativeHwndHost.SwitchActiveWindow when
        // the captured window becomes the active tab, which keeps the cross-thread
        // input attachment scoped to the visible tab instead of momentary.

        // Maximize/restore for the hosted guest is prevented by the WinEvent-driven
        // zoom clamp (ContainerWindow's drift watchdog + MOVESIZEEND reclamp), not by
        // subclassing: comctl32's SetWindowSubclass cannot subclass a window owned by
        // another process, so it failed on every real capture (confirmed in the
        // rotating log) and was pure overhead. See docs/internal/deep-audit-2026-07-17.md F8.

        // Diagnostic snapshot after reparenting and style changes.
        LogGuestDiagnostics(hwnd, "post-capture");

        // The render-health check (and the auto-release of unhealthy tabs) is
        // driven by ContainerWindow.AddCapturedWindow, which owns the view-model
        // state a release has to update. A duplicate check here could only log.
        _log.Log($"Captured 0x{hwnd.ToInt64():X} ({cw.OriginalTitle}) into host 0x{hostHwnd.ToInt64():X}");
        return cw;
    }

    public void Layout(CapturedWindow window, IntPtr hostHwnd, string reason = "layout")
    {
        Layout(window.Hwnd, hostHwnd, reason);
    }

    public void Layout(IntPtr hwnd, IntPtr hostHwnd, string reason = "layout")
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        // An iconic child cannot be sized into place; SetWindowPos would move
        // its minimized stub and the tab would render black.
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);

        // Reconcile the guest's logical coordinate system with the host monitor's
        // physical pixels. A guest that runs under a different DPI awareness
        // context (system-aware or DPI-unaware) interprets SetWindowPos sizes in
        // its own logical pixels; Windows then scales the rendered output to the
        // monitor DPI. Without this scale factor the guest would overflow or
        // underflow the host. Per-Monitor-V2 guests on the same monitor use a 1:1
        // scale and continue to fill the host exactly.
        double scale = _dpi.GetGuestToHostScaleFactor(hwnd, hostHwnd);
        int width = Math.Max((int)(rc.Width * scale), 1);
        int height = Math.Max((int)(rc.Height * scale), 1);

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            width,
            height,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);

        // Per-resize-tick lines would churn the 1 MB rotating log (finding L6);
        // every other layout trigger is rare and diagnostically valuable.
        if (reason != "wmsize")
            _log.Log($"LAYOUT[{reason}] hostClient={rc.Width}x{rc.Height} scale={scale:F3} guestSize={width}x{height} guest={DescribeWindow(hwnd)}");
    }

    /// <summary>
    /// One-token diagnostic description of a window's rect and state.
    /// </summary>
    public static string DescribeWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return $"0x{hwnd.ToInt64():X}(dead)";
        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r);
        return $"0x{hwnd.ToInt64():X}(rect={r.left},{r.top},{r.Width}x{r.Height} iconic={NativeMethods.IsIconic(hwnd)} zoomed={NativeMethods.IsZoomed(hwnd)} visible={NativeMethods.IsWindowVisible(hwnd)})";
    }

    /// <summary>
    /// Notifies a Per-Monitor-V2 captured guest that the host monitor DPI differs
    /// from the DPI it had before capture. Chrome/Electron/Edge do not receive
    /// WM_DPICHANGED across the SetParent process boundary, so this forward message
    /// is required for correct scaling when the guest originated on a monitor with
    /// a different DPI. System-aware and DPI-unaware guests do not handle
    /// WM_DPICHANGED meaningfully, so the message is skipped for them; their
    /// scaling is reconciled in Layout instead.
    /// </summary>
    public void NotifyDpiChanged(CapturedWindow window, IntPtr hostHwnd)
    {
        if (!NativeMethods.IsWindow(window.Hwnd) || !NativeMethods.IsWindow(hostHwnd))
            return;

        uint hostDpi = _dpi.GetDpi(hostHwnd);
        if (window.OriginalDpi == hostDpi)
            return;

        // WM_DPICHANGED is only meaningful for Per-Monitor-V2 guests.
        if (!_dpi.IsPerMonitorV2(window.Hwnd) &&
            !_dpi.IsPerMonitorAware(window.Hwnd))
        {
            _log.Log($"LAYOUT[dpi-forward] skipped: guest 0x{window.Hwnd.ToInt64():X} is not Per-Monitor aware (originalDpi={window.OriginalDpi} hostDpi={hostDpi})");
            return;
        }

        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);
        var pt = new NativeMethods.POINT { x = rc.left, y = rc.top };
        NativeMethods.ClientToScreen(hostHwnd, ref pt);
        var suggested = new NativeMethods.RECT
        {
            left = pt.x,
            top = pt.y,
            right = pt.x + rc.Width,
            bottom = pt.y + rc.Height,
        };

        bool sent = NativeMethods.TrySendDpiChanged(window.Hwnd, hostDpi, suggested);
        _log.Log($"LAYOUT[dpi-forward] originalDpi={window.OriginalDpi} hostDpi={hostDpi} rect={suggested.left},{suggested.top},{suggested.Width}x{suggested.Height} result={(sent ? "sent" : "failed")} guest={DescribeWindow(window.Hwnd)}");

    }

    /// <summary>
    /// Logs the guest's class name, DPI, awareness context, and geometry so Edge vs
    /// Chrome divergence can be diagnosed from the rotating log.
    /// </summary>
    private void LogGuestDiagnostics(IntPtr hwnd, string phase)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        string className = NativeMethods.GetClassNameString(hwnd) ?? "(unknown)";
        uint dpi = _dpi.GetDpi(hwnd);
        IntPtr context = _dpi.GetAwarenessContext(hwnd);
        string contextName = _dpi.DescribeAwarenessContext(context);
        bool isWrapper = IsWrapperClass(className);

        _log.Log($"DIAG[{phase}] hwnd=0x{hwnd.ToInt64():X} class={className} wrapper={isWrapper} dpi={dpi} awareness={contextName} {DescribeWindow(hwnd)}");
    }

    private static bool IsWrapperClass(string? className)
    {
        return className != null &&
            (className.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) ||
             className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Releases a captured window back to its original parent and restores its original styles/bounds.
    /// When <paramref name="show"/> is false the window's ownership, styles and geometry are restored
    /// but it is left hidden and not foregrounded — used when the guest hid itself (tray-style close)
    /// and must not be forced back on screen or focus-stolen.
    /// </summary>
    public void Release(CapturedWindow window, bool show = true)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
        {
            _log.Log($"Release: window 0x{window.Hwnd.ToInt64():X} already gone.");
            return;
        }

        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);

        bool wasTopLevel = (window.OriginalStyle & (long)NativeMethods.WS_CHILD) == 0;
        IntPtr targetParent = wasTopLevel ? IntPtr.Zero : window.OriginalParent;

        // Restore styles before reparenting so the window carries the correct
        // WS_CHILD / WS_POPUP bits for its new parent.
        NativeMethods.SetWindowLongPtr(window.Hwnd, NativeMethods.GWL_STYLE, (nint)window.OriginalStyle);
        NativeMethods.SetWindowLongPtr(window.Hwnd, NativeMethods.GWL_EXSTYLE, (nint)window.OriginalExStyle);

        // Restore parent. Top-level guests are detached (NULL) rather than re-parented
        // to the desktop window, which recreates a genuine top-level HWND.
        NativeMethods.SetParent(window.Hwnd, targetParent);

        // Restore the original owner for top-level windows (GW_OWNER is held in
        // GWLP_HWNDPARENT when the window is not WS_CHILD).
        if (wasTopLevel && window.OriginalOwner != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(window.Hwnd, NativeMethods.GWLP_HWNDPARENT, (nint)window.OriginalOwner);
        }

        // Recalculate the frame with the restored top-level styles before applying
        // placement, otherwise maximized bounds can end up stuck at the child size.
        NativeMethods.SetWindowPos(
            window.Hwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE);

        // Force DWM to recompute the non-client area now that the window is a
        // top-level frame again.
        NativeMethods.RedrawWindow(
            window.Hwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RDW_FRAME |
            NativeMethods.RDW_INVALIDATE |
            NativeMethods.RDW_ERASE |
            NativeMethods.RDW_ALLCHILDREN);

        NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
        placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

        if (!show)
        {
            // Restore geometry while keeping the window hidden. WINDOWPLACEMENT
            // accepts SW_HIDE: the normal/max rects are applied but the window
            // does not become visible, so a tray app can re-show itself later
            // as a normal top-level window at its original position.
            var hiddenPlacement = placement;
            hiddenPlacement.showCmd = NativeMethods.SW_HIDE;
            hiddenPlacement.flags = 0;
            if (!NativeMethods.SetWindowPlacement(window.Hwnd, ref hiddenPlacement))
            {
                _log.Log($"SetWindowPlacement (hidden) failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
                NativeMethods.SetWindowPos(
                    window.Hwnd,
                    NativeMethods.HWND_TOP,
                    window.OriginalBounds.left,
                    window.OriginalBounds.top,
                    window.OriginalBounds.Width,
                    window.OriginalBounds.Height,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            _log.Log($"Released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide) originalDpi={window.OriginalDpi}");
            return;
        }

        if (window.WasMaximized)
        {
            // Restore the normal position first, then explicitly maximize. Passing
            // showCmd=SW_SHOWMAXIMIZED directly to SetWindowPlacement can leave the
            // window at its last child-window size on some apps.
            var normalPlacement = placement;
            normalPlacement.showCmd = NativeMethods.SW_SHOWNORMAL;
            normalPlacement.flags = 0;
            NativeMethods.SetWindowPlacement(window.Hwnd, ref normalPlacement);
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_SHOWMAXIMIZED);
        }
        else
        {
            if (!NativeMethods.SetWindowPlacement(window.Hwnd, ref placement))
            {
                _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
                // Fallback to raw bounds.
                NativeMethods.SetWindowPos(
                    window.Hwnd,
                    NativeMethods.HWND_TOP,
                    window.OriginalBounds.left,
                    window.OriginalBounds.top,
                    window.OriginalBounds.Width,
                    window.OriginalBounds.Height,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
            }

            NativeMethods.ShowWindow(window.Hwnd, (int)placement.showCmd);
        }

        if (show)
        {
            // A released window can remain unresponsive because it never received
            // activation after being reparented back to the desktop. Ensure it is
            // visible, foregrounded, and explicitly told to activate.
            if (!NativeMethods.IsWindowVisible(window.Hwnd))
                NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_SHOW);

            bool fg = NativeMethods.SetForegroundWindow(window.Hwnd);
            IntPtr activateResult = NativeMethods.SendMessage(window.Hwnd, NativeMethods.WM_ACTIVATE, (IntPtr)NativeMethods.WA_ACTIVE, IntPtr.Zero);
            _log.Log($"LAYOUT[release-activate] fg={fg} activate=0x{activateResult.ToInt64():X} guest={DescribeWindow(window.Hwnd)}");
        }

        if (show)
        {
            nint releasedStyle = NativeMethods.GetWindowLongPtr(window.Hwnd, NativeMethods.GWL_STYLE);
            nint releasedExStyle = NativeMethods.GetWindowLongPtr(window.Hwnd, NativeMethods.GWL_EXSTYLE);
            IntPtr releasedParent = NativeMethods.GetParent(window.Hwnd);
            IntPtr releasedOwner = NativeMethods.GetWindow(window.Hwnd, NativeMethods.GW_OWNER);
            _log.Log($"Released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) parent=0x{releasedParent.ToInt64():X} owner=0x{releasedOwner.ToInt64():X} style=0x{releasedStyle.ToInt64():X} exStyle=0x{releasedExStyle.ToInt64():X} originalDpi={window.OriginalDpi} guest={DescribeWindow(window.Hwnd)}");

            // Checkpoint 2: Electron/Chromium-specific nudge. Isolated so it can be
            // removed if the real repro (ChatGPT Classic header gap) does not pass.
            if (IsChromiumWindow(window.Hwnd))
            {
                TryNudgeChromiumCompositor(window.Hwnd);
            }
        }
        else
        {
            _log.Log($"Released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide) originalDpi={window.OriginalDpi}");
        }
    }

    public void ReleaseAndShow(CapturedWindow window)
    {
        Release(window);
    }

    /// <summary>
    /// Returns true for Chromium/Electron guests whose window class follows the
    /// Chrome_WidgetWin pattern. Used only for the isolated Checkpoint 2 nudge.
    /// </summary>
    private static bool IsChromiumWindow(IntPtr hwnd)
    {
        string? className = NativeMethods.GetClassNameString(hwnd);
        return className != null &&
            className.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Forces a Chromium/Electron compositor surface rebuild by minimizing and
    /// restoring the released top-level window. This is intentionally isolated so
    /// it can be deleted if Checkpoint 2 (ChatGPT Classic header-gap repro) fails.
    /// </summary>
    private static void TryNudgeChromiumCompositor(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
    }
}
