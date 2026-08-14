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
/// Identity outcome for a graceful close request after a guest has been
/// released from Shepherd ownership. The capture registry and capture token
/// are intentionally not part of this post-release proof.
/// </summary>
internal enum ReleasedWindowCloseTargetResult
{
    Match,
    Destroyed,
    Replaced,
    Unverifiable,
}

/// <summary>
/// Immutable native identity carried from the captured state across a release
/// transaction so a later WM_CLOSE cannot rely on a detached CapturedWindow.
/// </summary>
internal readonly struct ReleasedWindowCloseTarget
{
    public ReleasedWindowCloseTarget(
        IntPtr hwnd,
        uint processId,
        uint windowThreadId,
        string exePath,
        string className,
        long processStartTimeUtcTicks)
    {
        Hwnd = hwnd;
        ProcessId = processId;
        WindowThreadId = windowThreadId;
        ExePath = exePath;
        ClassName = className;
        ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
    }

    public IntPtr Hwnd { get; }
    public uint ProcessId { get; }
    public uint WindowThreadId { get; }
    public string ExePath { get; }
    public string ClassName { get; }
    public long ProcessStartTimeUtcTicks { get; }

    public static ReleasedWindowCloseTarget FromCaptured(CapturedWindow window)
        => new(
            window.Hwnd,
            window.ProcessId,
            window.WindowThreadId,
            window.ExePath,
            window.OriginalClassName,
            window.ProcessStartTimeUtcTicks);
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

    /// <summary>
    /// Evaluates the strong identity fields before the per-capture HWND token
    /// has been installed. This is the capture journal-to-token boundary: the
    /// token cannot be required yet, but every other strong field must still
    /// match immediately before SetProp.
    /// </summary>
    public static WindowIdentityResult EvaluateBeforeCaptureToken(
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

            reason = "all pre-token identity evidence matched";
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

    /// <summary>
    /// Verifies a target after Shepherd has released it. This deliberately
    /// does not require the captured-object binding or the per-capture token:
    /// successful release removes both ownership signals. The process-start
    /// probe remains mandatory because PID reuse must not authorize WM_CLOSE.
    /// </summary>
    public static ReleasedWindowCloseTargetResult VerifyReleasedCloseTarget(
        ReleasedWindowCloseTarget target,
        IWindowIdentityNativeApi api,
        out string reason)
    {
        try
        {
            if (target.Hwnd == IntPtr.Zero)
                return Result(ReleasedWindowCloseTargetResult.Destroyed, "released HWND is zero", out reason);
            if (!api.IsWindow(target.Hwnd))
                return Result(ReleasedWindowCloseTargetResult.Destroyed, "released HWND no longer exists", out reason);
            if (target.ProcessId == 0 || target.WindowThreadId == 0
                || string.IsNullOrWhiteSpace(target.ExePath)
                || string.IsNullOrWhiteSpace(target.ClassName)
                || target.ProcessStartTimeUtcTicks == 0)
            {
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "released target identity is incomplete", out reason);
            }

            // A successfully released guest must no longer carry TabDock's
            // capture generation marker. A token that remains is ownership or
            // foreign state we cannot safely interpret as a released target.
            if (api.GetCaptureIdentityToken(target.Hwnd) != IntPtr.Zero)
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "capture token remains after release", out reason);

            WindowProcessIdentity current = api.GetProcessIdentity(target.Hwnd);
            if (current.ProcessId == 0 || current.ThreadId == 0)
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "released target PID/thread could not be read", out reason);
            if (current.ProcessId != target.ProcessId)
                return Result(ReleasedWindowCloseTargetResult.Replaced, "released target PID changed", out reason);
            if (current.ThreadId != target.WindowThreadId)
                return Result(ReleasedWindowCloseTargetResult.Replaced, "released target GUI thread changed", out reason);

            string? currentClass = api.GetClassName(target.Hwnd);
            if (string.IsNullOrWhiteSpace(currentClass))
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "released target class could not be read", out reason);
            if (!string.Equals(currentClass, target.ClassName, StringComparison.Ordinal))
                return Result(ReleasedWindowCloseTargetResult.Replaced, "released target class changed", out reason);

            string? currentExe = api.GetProcessImagePath(current.ProcessId);
            if (string.IsNullOrWhiteSpace(currentExe))
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "released target executable could not be read", out reason);
            if (!string.Equals(currentExe, target.ExePath, StringComparison.OrdinalIgnoreCase))
                return Result(ReleasedWindowCloseTargetResult.Replaced, "released target executable changed", out reason);

            long currentStart = api.GetProcessStartTimeUtcTicks(current.ProcessId);
            if (currentStart == 0)
                return Result(ReleasedWindowCloseTargetResult.Unverifiable, "released target process-start identity could not be read", out reason);
            if (currentStart != target.ProcessStartTimeUtcTicks)
                return Result(ReleasedWindowCloseTargetResult.Replaced, "released target process-start identity changed", out reason);

            reason = "released target identity matched";
            return ReleasedWindowCloseTargetResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"released target identity probe threw {ex.GetType().Name}";
            return ReleasedWindowCloseTargetResult.Unverifiable;
        }

        static ReleasedWindowCloseTargetResult Result(
            ReleasedWindowCloseTargetResult result,
            string message,
            out string resultReason)
        {
            resultReason = message;
            return result;
        }
    }
}
