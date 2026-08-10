using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TabDock.Services;

/// <summary>
/// Bounded environment fingerprint for customer/test reports (goal §16). One
/// startup block plus one per-container line make friend-machine failures
/// diagnosable (OS version/build, .NET runtime, bitness, monitor layout,
/// per-container DPI/monitor/geometry) without logging anything per frame and
/// without collecting personally sensitive information.
/// </summary>
public static class EnvironmentFingerprint
{
    private static readonly string s_platform = BuildPlatform();

    /// <summary>
    /// OS version/build + .NET runtime + process bitness, computed once.
    /// </summary>
    public static string Platform => s_platform;

    private static string BuildPlatform()
    {
        try
        {
            // Environment.OSVersion reports the OS version/build; the runtime
            // description covers .NET runtime + RID. Bitness is cheap.
            return $"os={Environment.OSVersion.Version} desc='{RuntimeInformation.OSDescription}' runtime='{RuntimeInformation.FrameworkDescription}' bitness={(Environment.Is64BitProcess ? 64 : 32)}";
        }
        catch (Exception ex)
        {
            return $"platform-description failed: {ex.Message}";
        }
    }

    /// <summary>
    /// One line per monitor: bounds, work area, primary flag. Uses
    /// EnumDisplayMonitors so all monitors (including those at negative
    /// coordinates) are covered, never just the primary.
    /// </summary>
    public static string DescribeMonitors()
    {
        try
        {
            int count = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);
            var lines = new List<string> { $"monitorCount={count} (SM_CMONITORS, incl. pseudo-monitors)" };
            bool enumOk = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT monitorRect, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                string detail = "?";
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    bool primary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    detail = $"bounds={mi.rcMonitor.left},{mi.rcMonitor.top},{mi.rcMonitor.Width}x{mi.rcMonitor.Height} " +
                             $"work={mi.rcWork.left},{mi.rcWork.top},{mi.rcWork.Width}x{mi.rcWork.Height} primary={primary}";
                }
                lines.Add($"monitor=0x{hMonitor.ToInt64():X} {detail}");
                return true;
            }, IntPtr.Zero);
            if (!enumOk)
                lines.Add($"enumFailed={NativeMethods.FormatLastError()}");
            return string.Join(" | ", lines);
        }
        catch (Exception ex)
        {
            return $"monitor-description failed: {ex.Message}";
        }
    }

    /// <summary>
    /// The monitor a window currently sits on (bounds + work area + primary
    /// flag) plus its DPI, used by the per-container fingerprint line.
    /// </summary>
    public static string DescribeWindowMonitor(IntPtr hwnd)
    {
        try
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return "monitor=none";
            var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
                return $"monitor=0x{monitor.ToInt64():X} info-failed";
            bool primary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
            uint dpi = NativeMethods.GetDpiForWindow(hwnd);
            return $"monitor=0x{monitor.ToInt64():X} bounds={mi.rcMonitor.left},{mi.rcMonitor.top},{mi.rcMonitor.Width}x{mi.rcMonitor.Height} " +
                   $"work={mi.rcWork.left},{mi.rcWork.top},{mi.rcWork.Width}x{mi.rcWork.Height} primary={primary} dpi={dpi}";
        }
        catch (Exception ex)
        {
            return $"monitor-description failed: {ex.Message}";
        }
    }
}
