using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
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

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void SwitchActiveWindow(CapturedWindow? oldWindow, CapturedWindow? newWindow)
    {
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
