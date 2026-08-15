using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>Identity captured for a window before a later operation targets it.</summary>
internal readonly record struct WindowIdentity(
    IntPtr Hwnd,
    uint ProcessId,
    uint WindowThreadId,
    string ClassName,
    string Title,
    string ExePath,
    long ProcessStartTimeUtcTicks);

/// <summary>Win32 EnumWindows / EnumChildWindows discovery helpers.</summary>
internal static class Discover
{
    /// <summary>
    /// Captures the process/thread/class/title/start-time identity used by the
    /// driver's safety checks. A zero/invalid HWND never becomes actionable.
    /// </summary>
    public static bool TryCaptureIdentity(IntPtr hwnd, out WindowIdentity identity)
    {
        // Process image/start-time queries are independent native calls. A
        // live window can therefore briefly produce an incomplete snapshot
        // while another process is starting/stopping or the OS is servicing a
        // burst of identity probes. Re-read the complete identity a bounded
        // number of times; this never admits a partial result and remains
        // fail-closed when the HWND genuinely disappears or changes owner.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (TryCaptureIdentityOnce(hwnd, out identity))
                return true;
            if (attempt < 2)
                Thread.Sleep(2);
        }

        identity = default;
        return false;
    }

    private static bool TryCaptureIdentityOnce(IntPtr hwnd, out WindowIdentity identity)
    {
        identity = default;
        if (!NativeMethods.IsWindow(hwnd))
            return false;

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        string? className = NativeMethods.GetClassNameString(hwnd);
        string? title = NativeMethods.GetWindowTextString(hwnd);
        string? exePath = pid == 0 ? null : NativeMethods.GetProcessImagePath(pid);
        if (pid == 0 || threadId == 0 || string.IsNullOrEmpty(className)
            || title == null || string.IsNullOrWhiteSpace(exePath))
            return false;

        long processStartTicks = TryGetProcessStartTimeUtcTicks(pid);
        if (processStartTicks == 0)
            return false;

        identity = new WindowIdentity(hwnd, pid, threadId, className, title, exePath, processStartTicks);
        return true;
    }

    /// <summary>
    /// Re-reads all identity fields immediately before a window operation.
    /// HWND validity alone is insufficient because Windows may recycle an HWND
    /// between discovery and cleanup/input.
    /// </summary>
    public static bool MatchesIdentity(WindowIdentity expected)
    {
        if (!TryCaptureIdentity(expected.Hwnd, out WindowIdentity current))
            return false;

        return current.ProcessId == expected.ProcessId
            && current.WindowThreadId == expected.WindowThreadId
            && string.Equals(current.ClassName, expected.ClassName, StringComparison.Ordinal)
            && string.Equals(current.Title, expected.Title, StringComparison.Ordinal)
            && string.Equals(current.ExePath, expected.ExePath, StringComparison.OrdinalIgnoreCase)
            && current.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks;
    }

    /// <summary>
    /// Reads the process-start identity used to distinguish a recycled PID.
    /// This uses the same native FILETIME seam as production. A zero result is
    /// unavailable and causes identity capture to fail closed before the
    /// driver can send input or mutate a window.
    /// </summary>
    public static long TryGetProcessStartTimeUtcTicks(uint pid)
        => NativeMethods.GetProcessStartTimeUtcTicks(pid);

    public static List<IntPtr> GetTopLevelWindowsByPid(uint pid, bool visibleOnly)
    {
        var list = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (visibleOnly && !NativeMethods.IsWindowVisible(hwnd))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint p);
            if (p == pid)
                list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static IntPtr FindTopLevelWindow(uint pid, Func<string, bool> titlePredicate)
    {
        foreach (IntPtr hwnd in GetTopLevelWindowsByPid(pid, visibleOnly: true))
        {
            string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            if (titlePredicate(title))
                return hwnd;
        }
        return IntPtr.Zero;
    }

    public static IntPtr WaitForTopLevelWindow(uint pid, Func<string, bool> titlePredicate, int timeoutMs)
    {
        IntPtr found = IntPtr.Zero;
        Util.WaitUntil(() => (found = FindTopLevelWindow(pid, titlePredicate)) != IntPtr.Zero, timeoutMs, 150);
        return found;
    }

    /// <summary>Finds a descendant window by class name (EnumChildWindows walks the whole subtree).</summary>
    public static IntPtr FindChildByClass(IntPtr parent, string className)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(parent, (hwnd, lParam) =>
        {
            if (string.Equals(NativeMethods.GetClassNameString(hwnd), className, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Visible top-level windows of a class; used to diff before/after spawning class-matched guests.</summary>
    public static HashSet<IntPtr> FindWindowsByClass(string className)
    {
        var set = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            var cn = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, cn, cn.Capacity);
            if (cn.ToString().Equals(className, StringComparison.OrdinalIgnoreCase))
                set.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    /// <summary>New visible titled window of the class that was not in <paramref name="existingWindows"/>.</summary>
    public static IntPtr FindNewWindowByClass(string className, HashSet<IntPtr> existingWindows)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || existingWindows.Contains(hwnd))
                return true;

            var cn = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, cn, cn.Capacity);
            if (!cn.ToString().Equals(className, StringComparison.OrdinalIgnoreCase))
                return true;

            string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            if (title.Length == 0)
                return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Finds a Win32 MessageBox (#32770 dialog) belonging to the pid, optionally by caption substring.</summary>
    public static IntPtr FindMessageBox(uint pid, string? titleContains)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            if (!string.Equals(NativeMethods.GetClassNameString(hwnd), "#32770", StringComparison.Ordinal))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint p);
            if (p != pid)
                return true;
            if (titleContains != null)
            {
                string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
                if (title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)
                    return true;
            }
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Finds a direct/descendant child window whose window text equals any of the candidates.</summary>
    public static IntPtr FindChildWindowByText(IntPtr parent, string[] textCandidates)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(parent, (hwnd, lParam) =>
        {
            string text = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            foreach (string candidate in textCandidates)
            {
                if (string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    found = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The window's client area expressed in screen coordinates.</summary>
    public static NativeMethods.RECT GetClientScreenRect(IntPtr hwnd)
    {
        NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT rc);
        var pt = new NativeMethods.POINT { x = rc.left, y = rc.top };
        NativeMethods.ClientToScreen(hwnd, ref pt);
        return new NativeMethods.RECT
        {
            left = pt.x,
            top = pt.y,
            right = pt.x + rc.Width,
            bottom = pt.y + rc.Height,
        };
    }
}
