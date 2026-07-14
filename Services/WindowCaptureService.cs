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
        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        // Capture the guest's baseline DPI while it is still a standalone top-level
        // window on its original monitor. Retained for diagnostics only; no reverse
        // DPI message is sent on release.
        uint originalDpi = _dpi.GetDpi(hwnd);

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

        Layout(hwnd, hostHwnd, "capture");

        var cw = new CapturedWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ExePath = _icons.GetProcessImagePath(pid) ?? string.Empty,
            OriginalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            OriginalPlacement = originalPlacement,
            OriginalParent = originalParent,
            OriginalStyle = (long)style,
            OriginalExStyle = (long)exStyle,
            OriginalBounds = bounds,
            OriginalDpi = originalDpi,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
        };

        // Chrome/Electron guests do not receive WM_DPICHANGED across the SetParent
        // boundary, so tell the guest the host monitor's DPI explicitly.
        NotifyDpiChanged(cw, hostHwnd);

        // Disable maximize/restore for the hosted guest. Frame-stripping removes
        // the system maximize box, but custom-drawn captions still dispatch these
        // commands internally.
        cw.SubclassProc = GuestSubclassProc;
        bool subclassed = NativeMethods.SetWindowSubclass(hwnd, cw.SubclassProc, IntPtr.Zero, IntPtr.Zero);
        _log.Log($"DIAG[subclass-top] hwnd=0x{hwnd.ToInt64():X} result={(subclassed ? "installed" : "failed")} error={(subclassed ? "none" : NativeMethods.FormatLastError())}");
        if (!subclassed)
        {
            _log.Log($"SetWindowSubclass failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}; falling back to WinEvent zoom clamp.");
        }

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
        int width = Math.Max(rc.Width, 1);
        int height = Math.Max(rc.Height, 1);

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
            _log.Log($"LAYOUT[{reason}] hostClient={width}x{height} guest={DescribeWindow(hwnd)}");
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
    /// Notifies a captured guest that the host monitor DPI differs from its
    /// current DPI by sending WM_DPICHANGED with the host client rect mapped to
    /// screen. Chrome/Electron do not receive DPI changes across the SetParent
    /// process boundary, so this forward message is required for correct scaling.
    /// </summary>
    public void NotifyDpiChanged(CapturedWindow window, IntPtr hostHwnd)
    {
        if (!NativeMethods.IsWindow(window.Hwnd) || !NativeMethods.IsWindow(hostHwnd))
            return;

        uint hostDpi = _dpi.GetDpi(hostHwnd);
        uint guestDpi = _dpi.GetDpi(window.Hwnd);
        if (hostDpi == guestDpi)
            return;

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
        _log.Log($"LAYOUT[dpi-forward] hostDpi={hostDpi} guestDpi={guestDpi} rect={suggested.left},{suggested.top},{suggested.Width}x{suggested.Height} result={(sent ? "sent" : "failed")} guest={DescribeWindow(window.Hwnd)}");

    }

    /// <summary>
    /// Subclass procedure installed on captured guests to disable maximize and
    /// restore while they are hosted. Custom-frame apps (Chrome, Electron, Edge)
    /// still dispatch these system commands from their internal caption buttons,
    /// so dropping SC_MAXIMIZE/SC_RESTORE prevents them from breaking the host
    /// layout. Other messages are passed through to DefSubclassProc.
    /// </summary>
    private static IntPtr GuestSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_SYSCOMMAND)
        {
            uint cmd = (uint)(wParam.ToInt64() & 0xFFF0);
            if (cmd == NativeMethods.SC_MAXIMIZE || cmd == NativeMethods.SC_RESTORE)
                return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
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

        // Remove the guest subclass before reparenting so comctl32 detaches the
        // callback cleanly while the HWND is still our child.
        NativeMethods.RemoveWindowSubclass(window.Hwnd, IntPtr.Zero);

        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);

        // Restore parent first.
        NativeMethods.SetParent(window.Hwnd, window.OriginalParent);

        // Restore styles.
        NativeMethods.SetWindowLongPtr(window.Hwnd, NativeMethods.GWL_STYLE, (nint)window.OriginalStyle);
        NativeMethods.SetWindowLongPtr(window.Hwnd, NativeMethods.GWL_EXSTYLE, (nint)window.OriginalExStyle);

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

        _log.Log($"Released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) originalDpi={window.OriginalDpi}");
    }

    public void ReleaseAndShow(CapturedWindow window)
    {
        Release(window);
    }
}
