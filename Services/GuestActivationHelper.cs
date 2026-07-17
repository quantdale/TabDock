using System;
using System.Runtime.InteropServices;

namespace TabDock.Services;

/// <summary>
/// Routes focus and activation to a reparented guest window. Captured guests run on
/// their own thread (and often in another process), so setting focus to them requires
/// a temporary AttachThreadInput call; without it Chromium/Electron guests may stop
/// consuming user input after being reparented into the TabDock content host.
/// </summary>
public static class GuestActivationHelper
{
    /// <summary>
    /// Activates the reparented guest: brings the container to the foreground, attaches
    /// the guest's input thread to TabDock's UI thread, sets focus on the guest, detaches,
    /// and sends WM_ACTIVATE/WA_ACTIVE. If the guest is hung the attach/focus sequence is
    /// skipped to avoid freezing TabDock's UI.
    /// </summary>
    public static void ActivateGuest(IntPtr guestHwnd, IntPtr containerHwnd, LoggingService? log)
    {
        void Log(string msg)
        {
            log?.Log(msg);
        }

        if (!NativeMethods.IsWindow(guestHwnd))
        {
            Log($"INPUT[activate-skip] guest=0x{guestHwnd.ToInt64():X} reason=dead");
            return;
        }

        if (!NativeMethods.IsWindow(containerHwnd))
        {
            Log($"INPUT[activate-skip] guest=0x{guestHwnd.ToInt64():X} reason=no-container");
            return;
        }

        // Bring the container to the foreground so the guest can legally receive focus.
        bool fg = NativeMethods.SetForegroundWindow(containerHwnd);
        if (!fg)
        {
            Log($"INPUT[activate-skip] guest=0x{guestHwnd.ToInt64():X} reason=container-not-foreground");
            return;
        }

        if (NativeMethods.IsHungAppWindow(guestHwnd))
        {
            Log($"INPUT[activate-skip] guest=0x{guestHwnd.ToInt64():X} reason=hung");
            return;
        }

        uint guestThreadId = NativeMethods.GetWindowThreadProcessId(guestHwnd, out _);
        uint hostThreadId = NativeMethods.GetWindowThreadProcessId(containerHwnd, out _);

        if (guestThreadId == 0 || hostThreadId == 0)
        {
            Log($"INPUT[activate-skip] guest=0x{guestHwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId} reason=no-thread");
            return;
        }

        if (guestThreadId == hostThreadId)
        {
            // Same thread: no attach needed, just set focus directly.
            IntPtr focusResult = NativeMethods.SetFocus(guestHwnd);
            IntPtr activateResult = NativeMethods.SendMessage(guestHwnd, NativeMethods.WM_ACTIVATE, (IntPtr)NativeMethods.WA_ACTIVE, IntPtr.Zero);
            Log($"INPUT[activate-same-thread] guest=0x{guestHwnd.ToInt64():X} focus=0x{focusResult.ToInt64():X} activate=0x{activateResult.ToInt64():X}");
            return;
        }

        bool attached = false;
        try
        {
            attached = NativeMethods.AttachThreadInput(guestThreadId, hostThreadId, true);
            if (!attached)
            {
                Log($"INPUT[activate-failed] guest=0x{guestHwnd.ToInt64():X} error=AttachThreadInput failed: {NativeMethods.FormatLastError()}");
                return;
            }

            IntPtr focusResult = NativeMethods.SetFocus(guestHwnd);
            IntPtr activateResult = NativeMethods.SendMessage(guestHwnd, NativeMethods.WM_ACTIVATE, (IntPtr)NativeMethods.WA_ACTIVE, IntPtr.Zero);
            Log($"INPUT[activate] guest=0x{guestHwnd.ToInt64():X} guestThread={guestThreadId} hostThread={hostThreadId} focus=0x{focusResult.ToInt64():X} activate=0x{activateResult.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Log($"INPUT[activate-exception] guest=0x{guestHwnd.ToInt64():X} {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Unconditional detach: keep this scoped even if SetFocus threw or the guest died mid-sequence.
            if (attached)
            {
                NativeMethods.AttachThreadInput(guestThreadId, hostThreadId, false);
            }
        }
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
