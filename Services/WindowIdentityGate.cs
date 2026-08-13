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
/// Outcome of a native identity verification. A mismatch is positive evidence
/// that the HWND is no longer the captured object; an unverifiable result means
/// the required evidence could not be obtained and must never be treated as a
/// stale identity.
/// </summary>
internal enum WindowIdentityResult
{
    Match,
    Mismatch,
    Unverifiable,
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
    bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken);
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

    public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
    {
        if (expectedToken == IntPtr.Zero || GetCaptureIdentityToken(hwnd) != expectedToken)
            return false;
        return NativeMethods.RemoveProp(hwnd, CaptureIdentityPropertyName) == expectedToken;
    }
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
    public static bool IsCaptureTokenAvailable(IntPtr hwnd, IWindowIdentityNativeApi api)
        => api.GetCaptureIdentityToken(hwnd) == IntPtr.Zero;

    public static WindowIdentityResult Evaluate(
        CapturedWindow captured,
        IWindowIdentityNativeApi api,
        bool verifyExecutable,
        bool verifyProcessInstance,
        out string reason)
    {
        try
        {
            if (!api.IsWindow(captured.Hwnd))
                return Mismatch("HWND no longer exists", out reason);

            if (captured.WindowIdentityToken == 0)
                return Unverifiable("captured HWND token is unavailable", out reason);

            IntPtr currentToken = api.GetCaptureIdentityToken(captured.Hwnd);
            if (currentToken != new IntPtr(captured.WindowIdentityToken))
                return Mismatch("HWND capture token differs", out reason);

            if (captured.ProcessId == 0 || captured.WindowThreadId == 0)
                return Unverifiable("captured process/thread identity is unavailable", out reason);

            WindowProcessIdentity current = api.GetProcessIdentity(captured.Hwnd);
            if (current.ProcessId == 0 || current.ThreadId == 0)
                return Unverifiable("live process/thread identity could not be read", out reason);
            if (current.ProcessId != captured.ProcessId)
                return Mismatch("process ID differs", out reason);
            if (current.ThreadId != captured.WindowThreadId)
                return Mismatch("GUI thread ID differs", out reason);

            if (string.IsNullOrWhiteSpace(captured.OriginalClassName))
                return Unverifiable("captured window class is unavailable", out reason);
            string? currentClass = api.GetClassName(captured.Hwnd);
            if (string.IsNullOrWhiteSpace(currentClass))
                return Unverifiable("window class could not be read", out reason);
            if (!string.Equals(currentClass, captured.OriginalClassName, StringComparison.Ordinal))
                return Mismatch("window class differs", out reason);

            if (verifyExecutable)
            {
                if (string.IsNullOrWhiteSpace(captured.ExePath))
                    return Unverifiable("captured executable identity is unavailable", out reason);
                string? currentExe = api.GetProcessImagePath(current.ProcessId);
                if (string.IsNullOrWhiteSpace(currentExe))
                    return Unverifiable("executable identity could not be read", out reason);
                if (!string.Equals(currentExe, captured.ExePath, StringComparison.OrdinalIgnoreCase))
                    return Mismatch("executable identity differs", out reason);
            }

            if (verifyProcessInstance)
            {
                if (captured.ProcessStartTimeUtcTicks == 0)
                    return Unverifiable("captured process-start identity is unavailable", out reason);
                long currentStart = api.GetProcessStartTimeUtcTicks(current.ProcessId);
                if (currentStart == 0)
                    return Unverifiable("process-start identity could not be read", out reason);
                if (currentStart != captured.ProcessStartTimeUtcTicks)
                    return Mismatch("process-start identity differs", out reason);
            }

            reason = "all required identity evidence matched";
            return WindowIdentityResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"identity probe threw {ex.GetType().Name}";
            return WindowIdentityResult.Unverifiable;
        }

        static WindowIdentityResult Mismatch(string message, out string resultReason)
        {
            resultReason = message;
            return WindowIdentityResult.Mismatch;
        }

        static WindowIdentityResult Unverifiable(string message, out string resultReason)
        {
            resultReason = message;
            return WindowIdentityResult.Unverifiable;
        }
    }

    public static bool Matches(
        CapturedWindow captured,
        IWindowIdentityNativeApi api,
        bool verifyExecutable,
        bool verifyProcessInstance)
        => Evaluate(captured, api, verifyExecutable, verifyProcessInstance, out _)
            == WindowIdentityResult.Match;
}
