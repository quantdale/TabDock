using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.Infrastructure;

/// <summary>
/// A WPF HwndHost that creates a child HWND to act as the container for captured windows.
/// On tab switch it hides the previous child and shows the new one; on WM_SIZE it re-lays-out
/// the active captured window.
/// </summary>
public class NativeHwndHost : HwndHost
{
    private const string WindowClass = "TabDockContentHost";
    private static readonly NativeMethods.WndProc s_wndProc = new(HostWndProc);
    private static readonly Dictionary<IntPtr, NativeHwndHost> s_hosts = new();
    private static bool s_classRegistered;

    private IntPtr _hwnd;
    private WindowCaptureService? _service;
    private LoggingService? _log;

    // Scoped cross-thread input attachment. The active guest's input thread is attached
    // to the host (WPF UI) thread for as long as the guest is the visible tab and the
    // container is active. Detaching happens on tab switch, container deactivation,
    // container close, guest hang/death, and application exit. This keeps keyboard focus
    // reliably on the guest without the momentary attach/detach pattern that let focus
    // drift back to the host.
    private uint _attachedGuestThreadId;
    private uint _attachedHostThreadId;
    private bool _isThreadInputAttached;

    // Guards the WM_KILLFOCUS handler against the focus change it is itself
    // about to cause: SetFocus(guest), called from AttachActiveGuest below,
    // synchronously delivers WM_KILLFOCUS to whichever window currently holds
    // focus (the host, once queues are attached) BEFORE SetFocus returns. Left
    // unguarded, that re-entrant WM_KILLFOCUS immediately detached the
    // attachment AttachActiveGuest had just established, and AttachThreadInput's
    // detach resets both threads' focus state — observed live as
    // "focus=0x0" moments after "attach" in the rotating log, i.e. no window on
    // either thread had keyboard focus. This flag makes that specific
    // self-triggered detach a no-op while leaving WM_KILLFOCUS from a genuine
    // external focus change (alt-tab, click elsewhere) fully handled.
    private bool _suppressKillFocusDetach;

    public static readonly DependencyProperty ActiveWindowProperty = DependencyProperty.Register(
        nameof(ActiveWindow),
        typeof(CapturedWindow),
        typeof(NativeHwndHost),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.None, OnActiveWindowChanged));

    public CapturedWindow? ActiveWindow
    {
        get => (CapturedWindow?)GetValue(ActiveWindowProperty);
        set => SetValue(ActiveWindowProperty, value);
    }

    public WindowCaptureService? Service
    {
        get => _service;
        set => _service = value;
    }

    public LoggingService? Logger
    {
        get => _log;
        set => _log = value;
    }

    /// <summary>
    /// Override HwndHost's default focus-within check so WPF does not yank Win32
    /// focus back to a WPF element (the container caption/tab strip) when we
    /// forward focus to the captured guest. Treat the guest subtree as "within"
    /// the host while WPF focus is on this host or while the guest currently
    /// holds Win32 focus.
    /// </summary>
    protected override bool HasFocusWithinCore()
    {
        CapturedWindow? cw = ActiveWindow;
        if (cw != null && NativeMethods.IsWindow(cw.Hwnd))
        {
            // Treat the captured guest subtree as always having focus within this
            // host while it is the active tab. This stops WPF from pulling Win32
            // focus back to the container caption/tab strip when we forward focus
            // to the guest.
            return true;
        }
        return base.HasFocusWithinCore();
    }

    /// <summary>
    /// Attaches the active guest's input thread to the host thread. Safe to call
    /// repeatedly: it is a no-op if the same guest is already attached.
    /// </summary>
    public void AttachActiveGuest()
    {
        CapturedWindow? cw = ActiveWindow;
        if (cw == null || cw.Hwnd == IntPtr.Zero || !NativeMethods.IsWindow(cw.Hwnd))
        {
            _log?.Log("INPUT[attach-skip] reason=no-active-window");
            return;
        }

        if (NativeMethods.IsHungAppWindow(cw.Hwnd))
        {
            _log?.Log($"INPUT[attach-skip] guest=0x{cw.Hwnd.ToInt64():X} reason=hung");
            return;
        }

        uint guestThreadId = NativeMethods.GetWindowThreadProcessId(cw.Hwnd, out _);
        uint hostThreadId = NativeMethods.GetWindowThreadProcessId(_hwnd, out _);
        if (guestThreadId == 0 || hostThreadId == 0)
        {
            _log?.Log($"INPUT[attach-skip] guest=0x{cw.Hwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId} reason=no-thread");
            return;
        }

        if (guestThreadId == hostThreadId)
        {
            // Same thread: no attach needed, but make sure focus is on the guest.
            _log?.Log($"INPUT[attach-same-thread] guest=0x{cw.Hwnd.ToInt64():X}");
            FocusActiveGuestGuarded();
            return;
        }

        if (_isThreadInputAttached &&
            _attachedGuestThreadId == guestThreadId &&
            _attachedHostThreadId == hostThreadId)
        {
            _log?.Log($"INPUT[attach-noop] guest=0x{cw.Hwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId}");
            return;
        }

        // If attached to a different guest, detach first to avoid chaining attachments.
        DetachActiveGuest();

        bool attached = NativeMethods.AttachThreadInput(guestThreadId, hostThreadId, true);
        if (attached)
        {
            _isThreadInputAttached = true;
            _attachedGuestThreadId = guestThreadId;
            _attachedHostThreadId = hostThreadId;
            _log?.Log($"INPUT[attach] guest=0x{cw.Hwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId}");
            FocusActiveGuestGuarded();
        }
        else
        {
            _log?.Log($"INPUT[attach-failed] guest=0x{cw.Hwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId} error={NativeMethods.FormatLastError()}");
        }
    }

    /// <summary>
    /// Detaches the currently attached guest's input thread from the host thread.
    /// Safe to call when not attached; logs the resulting focus state for diagnosis.
    /// </summary>
    public void DetachActiveGuest()
    {
        if (!_isThreadInputAttached)
            return;

        uint guestThreadId = _attachedGuestThreadId;
        uint hostThreadId = _attachedHostThreadId;
        bool detached = NativeMethods.AttachThreadInput(guestThreadId, hostThreadId, false);
        IntPtr focusAfter = NativeMethods.GetFocus();
        _log?.Log($"INPUT[detach] guestThread={guestThreadId} hostThread={hostThreadId} detached={detached} focus=0x{focusAfter.ToInt64():X}");

        _isThreadInputAttached = false;
        _attachedGuestThreadId = 0;
        _attachedHostThreadId = 0;
    }

    /// <summary>
    /// Detaches only if the active guest is hung or no longer a window. Called from
    /// the drift watchdog so a permanently stuck guest does not keep TabDock's input
    /// queue chained to it.
    /// </summary>
    public void DetachIfGuestHung()
    {
        if (!_isThreadInputAttached)
            return;

        CapturedWindow? cw = ActiveWindow;
        if (cw == null || cw.Hwnd == IntPtr.Zero || !NativeMethods.IsWindow(cw.Hwnd) || NativeMethods.IsHungAppWindow(cw.Hwnd))
        {
            string reason = cw == null ? "null" : (!NativeMethods.IsWindow(cw.Hwnd) ? "dead" : "hung");
            _log?.Log($"INPUT[detach-hung] guest=0x{(cw?.Hwnd ?? IntPtr.Zero).ToInt64():X} reason={reason}");
            DetachActiveGuest();
        }
    }

    /// <summary>
    /// Sets Win32 focus to the active guest and verifies the guest thread actually
    /// reports the guest as focused. Caller must have already attached input queues
    /// when cross-thread; this method also works for same-thread guests.
    /// </summary>
    public void FocusActiveGuest()
    {
        CapturedWindow? cw = ActiveWindow;
        if (cw == null || cw.Hwnd == IntPtr.Zero || !NativeMethods.IsWindow(cw.Hwnd))
            return;

        if (NativeMethods.IsHungAppWindow(cw.Hwnd))
        {
            _log?.Log($"INPUT[focus-skip] guest=0x{cw.Hwnd.ToInt64():X} reason=hung");
            return;
        }

        IntPtr focusResult = NativeMethods.SetFocus(cw.Hwnd);
        uint guestThreadId = NativeMethods.GetWindowThreadProcessId(cw.Hwnd, out _);
        var gti = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        bool gtiOk = NativeMethods.GetGUIThreadInfo(guestThreadId, ref gti);
        _log?.Log($"INPUT[focus] guest=0x{cw.Hwnd.ToInt64():X} guestThread={guestThreadId} setFocus=0x{focusResult.ToInt64():X} gtiOk={gtiOk} gtiFocus=0x{gti.hwndFocus.ToInt64():X}");

        // Chromium/Electron guests require an explicit WM_ACTIVATE notification in
        // addition to SetFocus before they will consume keyboard input reliably.
        GuestActivationHelper.NotifyGuestActive(cw.Hwnd, _log);
    }

    /// <summary>
    /// Calls <see cref="FocusActiveGuest"/> with the re-entrant WM_KILLFOCUS
    /// detach suppressed for its duration (see <see cref="_suppressKillFocusDetach"/>).
    /// </summary>
    private void FocusActiveGuestGuarded()
    {
        _suppressKillFocusDetach = true;
        try
        {
            FocusActiveGuest();
        }
        finally
        {
            _suppressKillFocusDetach = false;
        }
    }

    /// <summary>
    /// Detaches every host that still has an active guest attached. Called on
    /// application exit/crash paths so cross-thread input attachments are not left
    /// dangling after TabDock's UI thread terminates.
    /// </summary>
    public static void DetachAllGuests()
    {
        NativeHwndHost[] hosts;
        lock (s_hosts)
        {
            hosts = new NativeHwndHost[s_hosts.Count];
            s_hosts.Values.CopyTo(hosts, 0);
        }

        foreach (NativeHwndHost host in hosts)
        {
            try
            {
                host.DetachActiveGuest();
            }
            catch
            {
                // Best-effort emergency cleanup; never throw during shutdown.
            }
        }
    }

    private static void OnActiveWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NativeHwndHost host)
            host.SwitchActiveWindow(e.OldValue as CapturedWindow, e.NewValue as CapturedWindow);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (!s_classRegistered)
        {
            IntPtr hInstance = NativeMethods.GetModuleHandle(null);
            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = hInstance,
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW),
                // Without a class brush, DefWindowProc paints nothing on
                // WM_ERASEBKGND and any host region not covered by the guest
                // child keeps stale pixels (smearing when a guest moves within
                // the host). Matches the WPF ContentBorder background #1E1E1E.
                // A brush handed to RegisterClassEx is owned by the system for
                // the class's lifetime — it must never be DeleteObject'd.
                hbrBackground = NativeMethods.CreateSolidBrush(0x001E1E1E),
                lpszClassName = WindowClass,
            };

            if (NativeMethods.RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {NativeMethods.FormatLastError()}");
            }
            s_classRegistered = true;
        }

        _hwnd = NativeMethods.CreateWindowEx(
            0,
            WindowClass,
            "TabDock Content Host",
            NativeMethods.WS_CHILD | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS | NativeMethods.WS_VISIBLE,
            0,
            0,
            100,
            100,
            hwndParent.Handle,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {NativeMethods.FormatLastError()}");

        lock (s_hosts)
            s_hosts[_hwnd] = this;

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // Detach before destroying the host so the guest is not left chained to a
        // thread whose window is about to disappear.
        DetachActiveGuest();

        lock (s_hosts)
            s_hosts.Remove(hwnd.Handle);

        NativeMethods.DestroyWindow(hwnd.Handle);
    }

    private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_SIZE)
        {
            NativeHwndHost? host;
            lock (s_hosts)
                s_hosts.TryGetValue(hWnd, out host);
            host?.LayoutActiveWindow();
        }
        else if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            // Activate the clicked child window (the captured guest) rather than the
            // host itself. This lets Chromium/Electron receive activation on the first
            // click in the content area instead of staying input-dead.
            NativeHwndHost? host;
            lock (s_hosts)
                s_hosts.TryGetValue(hWnd, out host);
            if (host?.ActiveWindow != null && NativeMethods.IsWindow(host.ActiveWindow.Hwnd))
            {
                return (IntPtr)NativeMethods.MA_ACTIVATE;
            }
        }
        else if (msg == NativeMethods.WM_SETFOCUS)
        {
            // The host HWND received Win32 focus. Attach to the active guest and
            // forward focus there; the attachment stays in place so subsequent
            // keyboard input continues to route to the guest.
            NativeHwndHost? host;
            lock (s_hosts)
                s_hosts.TryGetValue(hWnd, out host);

            host?._log?.Log($"INPUT[host-wmsetfocus] host=0x{hWnd.ToInt64():X} active=0x{(host?.ActiveWindow?.Hwnd ?? IntPtr.Zero).ToInt64():X}");
            host?.AttachActiveGuest();
            return IntPtr.Zero;
        }
        else if (msg == NativeMethods.WM_KILLFOCUS)
        {
            // The host is losing Win32 focus; detach so we do not keep the guest's
            // thread chained to a host that is no longer the foreground window.
            NativeHwndHost? host;
            lock (s_hosts)
                s_hosts.TryGetValue(hWnd, out host);

            if (host != null && host._suppressKillFocusDetach)
            {
                // This WM_KILLFOCUS was raised by our own SetFocus(guest) call
                // inside AttachActiveGuest, not by an external focus change.
                // Detaching here would tear down the attachment that call just
                // established. See _suppressKillFocusDetach.
                host._log?.Log($"INPUT[host-wmkillfocus-suppressed] host=0x{hWnd.ToInt64():X}");
                return IntPtr.Zero;
            }

            host?._log?.Log($"INPUT[host-wmkillfocus] host=0x{hWnd.ToInt64():X}");
            host?.DetachActiveGuest();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void SwitchActiveWindow(CapturedWindow? oldWindow, CapturedWindow? newWindow)
    {
        // Detach from the old guest before hiding it. ActiveWindow has already
        // changed to newWindow at this point, but stored attachment state still
        // describes the old guest until AttachActiveGuest runs below.
        DetachActiveGuest();

        // Hide the previously active window, but only if it is still our child.
        if (oldWindow != null && oldWindow.Hwnd != IntPtr.Zero && NativeMethods.IsWindow(oldWindow.Hwnd))
        {
            try
            {
                if (NativeMethods.GetParent(oldWindow.Hwnd) == _hwnd)
                    NativeMethods.ShowWindow(oldWindow.Hwnd, NativeMethods.SW_HIDE);
            }
            catch
            {
                // Best effort; the window may have been destroyed or reparented.
            }
        }

        if (newWindow != null && _service != null && _hwnd != IntPtr.Zero &&
            NativeMethods.IsWindow(newWindow.Hwnd))
        {
            // SW_SHOW does not un-minimize an iconic window, which would leave
            // the content area black. Restore first, then lay out and show.
            if (NativeMethods.IsIconic(newWindow.Hwnd))
                NativeMethods.ShowWindow(newWindow.Hwnd, NativeMethods.SW_RESTORE);
            _service.Layout(newWindow, _hwnd, "switch");
            NativeMethods.ShowWindow(newWindow.Hwnd, NativeMethods.SW_SHOW);

            // Establish persistent cross-thread attachment and focus for the newly
            // shown guest. This replaces the temporary attach/detach pattern that
            // allowed focus to revert to the host immediately after SetFocus.
            AttachActiveGuest();
        }
    }

    private void LayoutActiveWindow()
    {
        if (_hwnd == IntPtr.Zero || _service == null)
            return;

        CapturedWindow? cw = ActiveWindow;
        if (cw != null)
            _service.Layout(cw, _hwnd, "wmsize");
    }

    public IntPtr HostWindowHandle => _hwnd;

    /// <summary>
    /// Sizes the host HWND in physical pixels so it matches the WPF element bounds
    /// on high-DPI monitors. The base HwndHost positions the window; this override
    /// corrects the size to device pixels, which is what captured Win32 guests expect.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        if (_hwnd != IntPtr.Zero)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Max(1, Math.Round(finalSize.Width * dpi.DpiScaleX));
            int height = (int)Math.Max(1, Math.Round(finalSize.Height * dpi.DpiScaleY));

            NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT rc);
            if (rc.Width != width || rc.Height != height)
            {
                NativeMethods.SetWindowPos(
                    _hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            }
        }
        return result;
    }

    /// <summary>
    /// Re-lays out the active guest when the host moves between monitors with
    /// different scale factors. DPI is reconciled by sizing the host HWND in
    /// physical pixels (ArrangeOverride); no cross-process WM_DPICHANGED is sent
    /// because synthetic DPI messages leave Chrome/Electron guests unresponsive
    /// after release.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        LayoutActiveWindow();
    }
}
