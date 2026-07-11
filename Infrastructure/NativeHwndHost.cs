using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
            _service.Layout(newWindow, _hwnd);
            NativeMethods.ShowWindow(newWindow.Hwnd, NativeMethods.SW_SHOW);
        }
    }

    private void LayoutActiveWindow()
    {
        if (_hwnd == IntPtr.Zero || _service == null)
            return;

        CapturedWindow? cw = ActiveWindow;
        if (cw != null)
            _service.Layout(cw, _hwnd);
    }

    public IntPtr HostWindowHandle => _hwnd;
}
