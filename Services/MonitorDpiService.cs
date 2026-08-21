using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TabDock.Services;

/// <summary>Injectable effective-DPI seam for capture, diagnostics, and tests.</summary>
internal interface IMonitorDpiProbe
{
    uint GetEffectiveDpi(IntPtr monitor);
}

/// <summary>Native lifecycle seam for the PMv2 helper-window probe.</summary>
internal interface IMonitorDpiNativeApi
{
    IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    IntPtr GetThreadDpiAwarenessContext();
    bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);
    bool GetMonitorInfo(IntPtr monitor, ref NativeMethods.MONITORINFO info);
    IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter);
    IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    uint GetDpiForWindow(IntPtr hwnd);
    bool DestroyWindow(IntPtr hwnd);
    string FormatLastError();
}

internal sealed class NativeMonitorDpiNativeApi : IMonitorDpiNativeApi
{
    public static NativeMonitorDpiNativeApi Instance { get; } = new();

    private NativeMonitorDpiNativeApi() { }

    public IntPtr SetThreadDpiAwarenessContext(IntPtr context)
        => NativeMethods.SetThreadDpiAwarenessContext(context);

    public IntPtr GetThreadDpiAwarenessContext()
        => NativeMethods.GetThreadDpiAwarenessContext();

    public bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right)
        => NativeMethods.AreDpiAwarenessContextsEqual(left, right);

    public bool GetMonitorInfo(IntPtr monitor, ref NativeMethods.MONITORINFO info)
        => NativeMethods.GetMonitorInfo(monitor, ref info);

    public IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter)
        => NativeMethods.CreateWindowEx(
            exStyle, className, title, style, x, y, width, height,
            parent, menu, instance, parameter);

    public IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd)
        => NativeMethods.GetWindowDpiAwarenessContext(hwnd);

    public IntPtr MonitorFromWindow(IntPtr hwnd, uint flags)
        => NativeMethods.MonitorFromWindow(hwnd, flags);

    public uint GetDpiForWindow(IntPtr hwnd)
        => NativeMethods.GetDpiForWindow(hwnd);

    public bool DestroyWindow(IntPtr hwnd)
        => NativeMethods.DestroyWindow(hwnd);

    public string FormatLastError()
        => NativeMethods.FormatLastError();
}

/// <summary>
/// Contract-correct monitor DPI probe for a PerMonitorV2 process. Microsoft
/// documents GetDpiForMonitor as not DPI-aware and says not to call it from a
/// per-monitor-aware thread. Instead, this creates a hidden PMv2 top-level
/// helper at the target monitor and asks GetDpiForWindow for that HWND's DPI.
/// </summary>
internal sealed class NativeMonitorDpiProbe : IMonitorDpiProbe
{
    public static NativeMonitorDpiProbe Instance { get; } = new(NativeMonitorDpiNativeApi.Instance);

    private readonly IMonitorDpiNativeApi _api;

    internal NativeMonitorDpiProbe(IMonitorDpiNativeApi api)
    {
        _api = api;
    }

    public uint GetEffectiveDpi(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
            return 0;

        IntPtr previousContext = _api.SetThreadDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorV2);
        if (previousContext == IntPtr.Zero)
            return 0;

        IntPtr helper = IntPtr.Zero;
        try
        {
            if (!_api.AreDpiAwarenessContextsEqual(
                    _api.GetThreadDpiAwarenessContext(),
                    NativeMethods.DpiAwarenessContextPerMonitorV2))
                return 0;

            var info = new NativeMethods.MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
            };
            if (!_api.GetMonitorInfo(monitor, ref info))
                return 0;

            if (!MonitorDpiGeometry.TryGetCenter(in info.rcMonitor, out int x, out int y))
                return 0;
            helper = _api.CreateWindowEx(
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

            if (!_api.AreDpiAwarenessContextsEqual(
                    _api.GetWindowDpiAwarenessContext(helper),
                    NativeMethods.DpiAwarenessContextPerMonitorV2))
            {
                return 0;
            }

            if (_api.MonitorFromWindow(helper, NativeMethods.MONITOR_DEFAULTTONEAREST) != monitor)
                return 0;

            uint dpi = _api.GetDpiForWindow(helper);
            return dpi == 0 ? 0 : dpi;
        }
        finally
        {
            try
            {
                if (helper != IntPtr.Zero && !_api.DestroyWindow(helper))
                {
                    string error = _api.FormatLastError();
                    Debug.WriteLine($"TabDock PMv2 DPI helper DestroyWindow failed for 0x{helper.ToInt64():X}: {error}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TabDock PMv2 DPI helper DestroyWindow threw: {ex.GetType().Name}");
            }
            finally
            {
                try
                {
                    if (_api.SetThreadDpiAwarenessContext(previousContext) == IntPtr.Zero)
                        Debug.WriteLine("TabDock PMv2 DPI helper could not restore the previous thread DPI context.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TabDock PMv2 DPI helper could not restore the previous thread DPI context: {ex.GetType().Name}");
                }
            }
        }
    }
}

/// <summary>
/// Overflow-safe monitor-center calculation used before creating the hidden
/// helper. Win32 monitor coordinates may be negative; malformed or
/// non-positive rectangles are unavailable rather than passed to USER32.
/// </summary>
internal static class MonitorDpiGeometry
{
    public static bool TryGetCenter(in NativeMethods.RECT rect, out int x, out int y)
    {
        long width = (long)rect.right - rect.left;
        long height = (long)rect.bottom - rect.top;
        if (width <= 0 || height <= 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        long centerX = (long)rect.left + (width / 2);
        long centerY = (long)rect.top + (height / 2);
        if (centerX < int.MinValue || centerX > int.MaxValue
            || centerY < int.MinValue || centerY > int.MaxValue)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = (int)centerX;
        y = (int)centerY;
        return true;
    }
}

internal static class MonitorDpiService
{
    private static readonly CachedMonitorDpiProbe CachedProbe = new(NativeMonitorDpiProbe.Instance);

    public static uint GetEffectiveDpi(IntPtr monitor)
        => CachedProbe.GetEffectiveDpi(monitor);

    /// <summary>Drops every cached monitor DPI (display topology changed).</summary>
    public static void InvalidateDpiCache()
        => CachedProbe.Invalidate();

    /// <summary>Drops the cached DPI for one monitor (its DPI changed).</summary>
    public static void InvalidateDpiCache(IntPtr monitor)
        => CachedProbe.Invalidate(monitor);
}

/// <summary>
/// Per-monitor cache over the expensive PMv2 helper-window probe. A hit
/// avoids the SetThreadDpiAwarenessContext + CreateWindowEx/DestroyWindow
/// round trip entirely. Entries are only stored for successful probes, so a
/// transient failure retries on the next query; callers must invalidate on
/// WM_DPICHANGED (one monitor) and WM_DISPLAYCHANGE / topology changes (all).
/// UI-thread confined like every other display-state consumer.
/// </summary>
internal sealed class CachedMonitorDpiProbe : IMonitorDpiProbe
{
    private readonly IMonitorDpiProbe _inner;
    private readonly Dictionary<IntPtr, uint> _cache = new();

    public CachedMonitorDpiProbe(IMonitorDpiProbe inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public uint GetEffectiveDpi(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
            return 0;
        if (_cache.TryGetValue(monitor, out uint cached))
            return cached;
        uint dpi = _inner.GetEffectiveDpi(monitor);
        if (dpi != 0)
            _cache[monitor] = dpi;
        return dpi;
    }

    public void Invalidate() => _cache.Clear();

    public void Invalidate(IntPtr monitor) => _cache.Remove(monitor);
}
