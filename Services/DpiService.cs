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

    /// <summary>
    /// Returns the ratio of the guest window's logical DPI to the host monitor's
    /// physical DPI. Use this to convert host physical pixels into guest logical
    /// pixels before calling SetWindowPos.
    /// </summary>
    public double GetGuestToHostScaleFactor(IntPtr guestHwnd, IntPtr hostHwnd)
    {
        uint guestDpi = GetDpi(guestHwnd);
        uint hostDpi = GetDpi(hostHwnd);
        if (guestDpi == 0 || hostDpi == 0)
            return 1.0;
        return (double)guestDpi / hostDpi;
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

    public bool IsUnaware(IntPtr hwnd)
    {
        return NativeMethods.AreDpiAwarenessContextsEqual(
            GetAwarenessContext(hwnd),
            NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE);
    }

    public bool IsSystemAware(IntPtr hwnd)
    {
        return NativeMethods.AreDpiAwarenessContextsEqual(
            GetAwarenessContext(hwnd),
            NativeMethods.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE);
    }

    public bool IsPerMonitorAware(IntPtr hwnd)
    {
        return NativeMethods.AreDpiAwarenessContextsEqual(
            GetAwarenessContext(hwnd),
            NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
    }

    public bool IsPerMonitorV2(IntPtr hwnd)
    {
        return NativeMethods.AreDpiAwarenessContextsEqual(
            GetAwarenessContext(hwnd),
            NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    public bool IsUnawareGdiScaled(IntPtr hwnd)
    {
        return NativeMethods.AreDpiAwarenessContextsEqual(
            GetAwarenessContext(hwnd),
            NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED);
    }

    /// <summary>
    /// Describes a DPI awareness context returned by GetWindowDpiAwarenessContext.
    /// These are opaque handles, so direct equality with the sentinel constants is
    /// unreliable; use AreDpiAwarenessContextsEqual for comparison.
    /// </summary>
    public string DescribeAwarenessContext(IntPtr context)
    {
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE))
            return "Unaware";
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE))
            return "SystemAware";
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            return "PerMonitor";
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            return "PerMonitorV2";
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED))
            return "UnawareGdiScaled";
        return $"Unknown({context.ToInt64()})";
    }
}
