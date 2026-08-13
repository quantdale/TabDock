using System;

namespace TabDock.ValidationDriver;

/// <summary>
/// Identity-gated Win32 window operations used by the validation harness.
/// Coordinate input is guarded in <see cref="Input"/>; this class covers the
/// direct message/visibility/geometry operations that scenarios must perform
/// during setup and cleanup.
/// </summary>
internal static class VerifiedWindowOps
{
    public static bool PostMessage(WindowIdentity expected, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!Discover.MatchesIdentity(expected))
        {
            GuardedProc.Log($"WARNING: refusing PostMessage to stale/unverified HWND 0x{expected.Hwnd.ToInt64():X}.");
            return false;
        }

        bool ok = NativeMethods.PostMessage(expected.Hwnd, message, wParam, lParam);
        if (!ok)
            GuardedProc.Log($"WARNING: PostMessage to verified HWND 0x{expected.Hwnd.ToInt64():X} failed: {NativeMethods.FormatLastError()}");
        return ok;
    }

    public static bool PostMessage(IntPtr hwnd, uint expectedProcessId, uint message, IntPtr wParam, IntPtr lParam)
    {
        return TryCaptureForProcess(hwnd, expectedProcessId, out WindowIdentity identity)
            && PostMessage(identity, message, wParam, lParam);
    }

    public static bool ShowWindow(WindowIdentity expected, int command)
    {
        if (!Discover.MatchesIdentity(expected))
        {
            GuardedProc.Log($"WARNING: refusing ShowWindow on stale/unverified HWND 0x{expected.Hwnd.ToInt64():X}.");
            return false;
        }

        // ShowWindow's BOOL is the window's previous visibility, not an
        // operation result. Safety is determined from the identity re-read;
        // callers that need a presentation assertion also poll the resulting
        // native state (for example IsIconic after restore/minimize).
        bool previouslyVisible = NativeMethods.ShowWindow(expected.Hwnd, command);
        _ = previouslyVisible;
        return Discover.MatchesIdentity(expected);
    }

    public static bool ShowWindow(IntPtr hwnd, uint expectedProcessId, int command)
    {
        return TryCaptureForProcess(hwnd, expectedProcessId, out WindowIdentity identity)
            && ShowWindow(identity, command);
    }

    public static bool SetWindowPos(
        WindowIdentity expected,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags,
        WindowIdentity? insertAfterIdentity = null)
    {
        if (!Discover.MatchesIdentity(expected))
        {
            GuardedProc.Log($"WARNING: refusing SetWindowPos on stale/unverified HWND 0x{expected.Hwnd.ToInt64():X}.");
            return false;
        }

        bool insertAfterNeedsIdentity = insertAfter != IntPtr.Zero
            && insertAfter != NativeMethods.HWND_TOP
            && insertAfter != NativeMethods.HWND_TOPMOST
            && insertAfter != NativeMethods.HWND_NOTOPMOST;
        if (insertAfterNeedsIdentity
            && (insertAfterIdentity is not WindowIdentity after
                || !Discover.MatchesIdentity(after)))
        {
            GuardedProc.Log($"WARNING: refusing SetWindowPos for 0x{expected.Hwnd.ToInt64():X}; insert-after HWND was not identity verified.");
            return false;
        }

        bool ok = NativeMethods.SetWindowPos(expected.Hwnd, insertAfter, x, y, width, height, flags);
        if (!ok)
            GuardedProc.Log($"WARNING: SetWindowPos to verified HWND 0x{expected.Hwnd.ToInt64():X} failed: {NativeMethods.FormatLastError()}");
        return ok && Discover.MatchesIdentity(expected);
    }

    private static bool TryCaptureForProcess(IntPtr hwnd, uint expectedProcessId, out WindowIdentity identity)
    {
        if (!Discover.TryCaptureIdentity(hwnd, out identity) || identity.ProcessId != expectedProcessId)
        {
            GuardedProc.Log($"WARNING: refusing operation on HWND 0x{hwnd.ToInt64():X}; expected PID {expectedProcessId}.");
            identity = default;
            return false;
        }
        return true;
    }
}
