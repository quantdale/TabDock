using System;
using System.Runtime.InteropServices;

namespace TabDock.Services;

/// <summary>
/// Focus and activation helpers for reparented guests. Cross-thread input attachment
/// is owned by <see cref="Infrastructure.NativeHwndHost"/> so it can stay scoped to
/// the active tab; this helper performs the actual SetFocus and verification assuming
/// the caller has already attached the guest's input queue when required.
/// </summary>
public static class GuestActivationHelper
{
    /// <summary>
    /// Sets Win32 focus to the guest and verifies the guest thread actually reports
    /// the guest as focused. Caller must have attached input queues for cross-thread
    /// guests; same-thread guests need no attachment.
    /// </summary>
    public static void FocusGuest(IntPtr guestHwnd, LoggingService? log)
    {
        void Log(string msg)
        {
            log?.Log(msg);
        }

        if (!NativeMethods.IsWindow(guestHwnd))
        {
            Log($"INPUT[focus-skip] guest=0x{guestHwnd.ToInt64():X} reason=dead");
            return;
        }

        if (NativeMethods.IsHungAppWindow(guestHwnd))
        {
            Log($"INPUT[focus-skip] guest=0x{guestHwnd.ToInt64():X} reason=hung");
            return;
        }

        IntPtr focusResult = NativeMethods.SetFocus(guestHwnd);
        uint guestThreadId = NativeMethods.GetWindowThreadProcessId(guestHwnd, out _);
        var gti = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        bool gtiOk = NativeMethods.GetGUIThreadInfo(guestThreadId, ref gti);
        Log($"INPUT[focus] guest=0x{guestHwnd.ToInt64():X} guestThread={guestThreadId} setFocus=0x{focusResult.ToInt64():X} gtiOk={gtiOk} gtiFocus=0x{gti.hwndFocus.ToInt64():X}");
    }

    /// <summary>
    /// Sends WM_ACTIVATE/WA_ACTIVE to the guest without stealing foreground or changing focus.
    /// Used when the container itself is being activated (e.g. alt-tab back) so the guest
    /// knows it should treat itself as active again.
    /// </summary>
    public static void NotifyGuestActive(IntPtr guestHwnd, LoggingService? log)
    {
        void Log(string msg)
        {
            log?.Log(msg);
        }

        if (!NativeMethods.IsWindow(guestHwnd))
            return;

        if (NativeMethods.IsHungAppWindow(guestHwnd))
        {
            Log($"INPUT[notify-active-skip] guest=0x{guestHwnd.ToInt64():X} reason=hung");
            return;
        }

        IntPtr activateResult = NativeMethods.SendMessage(guestHwnd, NativeMethods.WM_ACTIVATE, (IntPtr)NativeMethods.WA_ACTIVE, IntPtr.Zero);
        Log($"INPUT[notify-active] guest=0x{guestHwnd.ToInt64():X} activate=0x{activateResult.ToInt64():X}");
    }
}
