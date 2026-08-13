using System;
using System.Collections.Generic;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>PID and GUI-thread identity returned by one HWND probe.</summary>
internal readonly struct WindowProcessIdentity
{
    public WindowProcessIdentity(uint processId, uint threadId)
    {
        ProcessId = processId;
        ThreadId = threadId;
    }

    public uint ProcessId { get; }
    public uint ThreadId { get; }
}

/// <summary>
/// Native identity seam for the Shepherd mutation gate. The deterministic
/// diagnostics self-test injects this interface; production uses the adapter
/// below. Process-start probes are deliberately separate from the hot tier.
/// </summary>
internal interface IWindowIdentityNativeApi
{
    IntPtr GetCaptureIdentityToken(IntPtr hwnd);
    bool IsWindow(IntPtr hwnd);
    WindowProcessIdentity GetProcessIdentity(IntPtr hwnd);
    string? GetProcessImagePath(uint pid);
    string? GetClassName(IntPtr hwnd);
    long GetProcessStartTimeUtcTicks(uint pid);
}

internal sealed class NativeWindowIdentityApi : IWindowIdentityNativeApi
{
    internal const string CaptureIdentityPropertyName = "TabDock.CapturedWindowToken";

    public static NativeWindowIdentityApi Instance { get; } = new();

    private NativeWindowIdentityApi() { }

    public IntPtr GetCaptureIdentityToken(IntPtr hwnd)
        => NativeMethods.GetProp(hwnd, CaptureIdentityPropertyName);

    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return new WindowProcessIdentity(processId, threadId);
    }

    public string? GetProcessImagePath(uint pid) => NativeMethods.GetProcessImagePath(pid);

    public string? GetClassName(IntPtr hwnd) => NativeMethods.GetClassNameString(hwnd);

    public long GetProcessStartTimeUtcTicks(uint pid)
        => NativeMethods.GetProcessStartTimeUtcTicks(pid);
}

/// <summary>
/// Keeps the current captured object bound to an HWND value. HWNDs can be
/// recycled even within the same process, so an old delayed callback must not
/// become valid merely because the replacement has the same native handle.
/// </summary>
internal sealed class WindowIdentityBinding
{
    private readonly Dictionary<IntPtr, CapturedWindow> _items = new();

    public void Bind(CapturedWindow window) => _items[window.Hwnd] = window;

    public bool ContainsHwnd(IntPtr hwnd) => _items.ContainsKey(hwnd);

    public bool IsCurrent(CapturedWindow window)
        => _items.TryGetValue(window.Hwnd, out CapturedWindow? current)
            && ReferenceEquals(current, window);

    public void Unbind(CapturedWindow window)
    {
        if (IsCurrent(window))
            _items.Remove(window.Hwnd);
    }
}

/// <summary>
/// Applies the identity fields appropriate to a Shepherd mutation tier.
/// </summary>
internal static class WindowIdentityGate
{
    public static bool Matches(
        CapturedWindow captured,
        IWindowIdentityNativeApi api,
        bool verifyExecutable,
        bool verifyProcessInstance)
    {
        if (!api.IsWindow(captured.Hwnd))
            return false;

        if (captured.WindowIdentityToken == 0
            || api.GetCaptureIdentityToken(captured.Hwnd) != new IntPtr(captured.WindowIdentityToken))
        {
            return false;
        }

        WindowProcessIdentity current = api.GetProcessIdentity(captured.Hwnd);
        if (current.ProcessId == 0
            || captured.ProcessId == 0
            || current.ProcessId != captured.ProcessId
            || captured.WindowThreadId == 0
            || current.ThreadId != captured.WindowThreadId
            || string.IsNullOrWhiteSpace(captured.OriginalClassName)
            || !string.Equals(api.GetClassName(captured.Hwnd), captured.OriginalClassName, StringComparison.Ordinal))
        {
            return false;
        }

        if (verifyExecutable)
        {
            string? currentExe = api.GetProcessImagePath(current.ProcessId);
            if (string.IsNullOrWhiteSpace(captured.ExePath)
                || !string.Equals(currentExe, captured.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (verifyProcessInstance)
        {
            if (captured.ProcessStartTimeUtcTicks == 0
                || api.GetProcessStartTimeUtcTicks(current.ProcessId) != captured.ProcessStartTimeUtcTicks)
            {
                return false;
            }
        }

        return true;
    }
}
