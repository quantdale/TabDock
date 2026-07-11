using System;

namespace TabDock.Services;

/// <summary>
/// DPI helpers and awareness-context checks for mixed-DPI monitor setups.
/// </summary>
public sealed class DpiService
{
    public uint GetDpi(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return NativeMethods.GetDpiForSystem();
        return NativeMethods.GetDpiForWindow(hwnd);
    }

    public double GetScaleFactor(IntPtr hwnd)
    {
        uint dpi = GetDpi(hwnd);
        return dpi / 96.0;
    }

    public IntPtr GetAwarenessContext(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return NativeMethods.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE;
        return NativeMethods.GetWindowDpiAwarenessContext(hwnd);
    }

    public bool HaveDifferentAwarenessContexts(IntPtr hwndA, IntPtr hwndB)
    {
        if (hwndA == IntPtr.Zero || hwndB == IntPtr.Zero)
            return false;

        IntPtr ctxA = NativeMethods.GetWindowDpiAwarenessContext(hwndA);
        IntPtr ctxB = NativeMethods.GetWindowDpiAwarenessContext(hwndB);
        return !NativeMethods.AreDpiAwarenessContextsEqual(ctxA, ctxB);
    }

    public string DescribeAwarenessContext(IntPtr context)
    {
        if (context == NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE)
            return "Unaware";
        if (context == NativeMethods.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE)
            return "SystemAware";
        if (context == NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE)
            return "PerMonitor";
        if (context == NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
            return "PerMonitorV2";
        if (context == NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED)
            return "UnawareGdiScaled";
        return $"Unknown({context.ToInt64()})";
    }
}
