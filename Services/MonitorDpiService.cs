using System;

namespace TabDock.Services;

/// <summary>Injectable effective-DPI seam for capture, diagnostics, and tests.</summary>
internal interface IMonitorDpiProbe
{
    uint GetEffectiveDpi(IntPtr monitor);
}

/// <summary>
/// Contract-correct monitor DPI probe for a PerMonitorV2 process. Microsoft
/// documents GetDpiForMonitor as not DPI-aware and says not to call it from a
/// per-monitor-aware thread. Instead, this creates a hidden PMv2 top-level
/// helper at the target monitor and asks GetDpiForWindow for that HWND's DPI.
/// </summary>
internal sealed class NativeMonitorDpiProbe : IMonitorDpiProbe
{
    public static NativeMonitorDpiProbe Instance { get; } = new();

    private NativeMonitorDpiProbe() { }

    public uint GetEffectiveDpi(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
            return 0;

        IntPtr previousContext = NativeMethods.SetThreadDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorV2);
        if (previousContext == IntPtr.Zero)
            return 0;

        IntPtr helper = IntPtr.Zero;
        try
        {
            if (!NativeMethods.AreDpiAwarenessContextsEqual(
                    NativeMethods.GetThreadDpiAwarenessContext(),
                    NativeMethods.DpiAwarenessContextPerMonitorV2))
                return 0;

            var info = new NativeMethods.MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return 0;

            int x = info.rcMonitor.left + (info.rcMonitor.Width / 2);
            int y = info.rcMonitor.top + (info.rcMonitor.Height / 2);
            helper = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                "STATIC",
                string.Empty,
                NativeMethods.WS_POPUP,
                x,
                y,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.GetModuleHandle(null),
                IntPtr.Zero);
            if (helper == IntPtr.Zero)
                return 0;

            if (!NativeMethods.AreDpiAwarenessContextsEqual(
                    NativeMethods.GetWindowDpiAwarenessContext(helper),
                    NativeMethods.DpiAwarenessContextPerMonitorV2))
            {
                return 0;
            }

            if (NativeMethods.MonitorFromWindow(helper, NativeMethods.MONITOR_DEFAULTTONEAREST) != monitor)
                return 0;

            uint dpi = NativeMethods.GetDpiForWindow(helper);
            return dpi == 0 ? 0 : dpi;
        }
        finally
        {
            if (helper != IntPtr.Zero)
                NativeMethods.DestroyWindow(helper);
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }
}

internal static class MonitorDpiService
{
    public static uint GetEffectiveDpi(IntPtr monitor)
        => NativeMonitorDpiProbe.Instance.GetEffectiveDpi(monitor);
}
