using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // Holding Ctrl+Alt+G (~2 s, simulating keyboard auto-repeat of 'G' with the
    // modifiers held) must open exactly ONE capture picker — MOD_NOREPEAT
    // (HotkeyService.Register) suppresses the WM_HOTKEY repeats that used to
    // stack one picker per repeat. Pig-independent.
    // -------------------------------------------------------------------------
    private static void HotkeyHoldSinglePicker(Ctx ctx, Options opt)
    {
        // SendInput does not auto-repeat (that comes from the keyboard hardware),
        // so simulate a held key the way it behaves at the hotkey registration:
        // Ctrl+Alt down, then a rapid series of G down/up taps for ~2 s — each tap
        // would have re-fired WM_HOTKEY without MOD_NOREPEAT.
        bool ctrlDown = false;
        bool altDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_CONTROL);
            ctrlDown = true;
            Input.SendKeyDown(Input.VK_MENU);
            altDown = true;
            var hold = Stopwatch.StartNew();
            while (hold.ElapsedMilliseconds < 2000)
            {
                bool gDown = false;
                try
                {
                    Input.SendKeyDown(Input.VK_G);
                    gDown = true;
                }
                finally
                {
                    if (gDown)
                        Input.SendKeyUp(Input.VK_G);
                }
                Thread.Sleep(120);
            }
        }
        finally
        {
            if (altDown)
                Input.SendKeyUp(Input.VK_MENU);
            if (ctrlDown)
                Input.SendKeyUp(Input.VK_CONTROL);
        }
        Thread.Sleep(800); // settle any queued opens

        var pickers = new List<IntPtr>();
        foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
        {
            string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
            if (t == "Capture windows")
                pickers.Add(h);
        }
        ctx.Check(pickers.Count == 1, $"holding Ctrl+Alt+G opened exactly one capture picker (got {pickers.Count})");

        if (pickers.Count == 1)
        {
            if (!Input.ForceForeground(pickers[0]))
                throw new InvalidOperationException("Could not bring the picker to the foreground — refusing to click blind.");
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(pickers[0]), 3000), "picker dismissed with Esc");
        }
        foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
        {
            string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
            if (t == "Capture windows")
                ctx.Check(false, "a capture picker is still open after Esc dismissal — expected zero");
        }
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 33. picker-owner-is-requesting-container: regression guard for
    //     App.ShowCapturePicker's requestingWindow resolution — a container's
    //     own "+" button must own the picker it opens, never a DIFFERENT
    //     container and never the main launcher.
    // -------------------------------------------------------------------------
    private static void PickerOwnerIsRequestingContainer(Ctx ctx, Options opt)
    {
        GuestInfo pig1 = SpawnPig(ctx, "OWN1", "--color", "blue");
        (IntPtr container1, IntPtr host1) = CaptureIntoGroup(ctx, pig1);
        GuestInfo pig2 = SpawnPig(ctx, "OWN2", "--color", "red");
        (IntPtr container2, IntPtr host2) = CaptureIntoGroup(ctx, pig2);
        ctx.Check(container1 != container2, "two distinct containers were created");

        ClickAddWindowButton(container2);
        IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        ctx.Check(picker != IntPtr.Zero, "picker appeared from container 2's own '+' button");
        if (picker != IntPtr.Zero)
        {
            IntPtr owner = NativeMethods.GetWindow(picker, NativeMethods.GW_OWNER);
            ctx.Check(owner == container2, $"picker's Win32 owner is container 2 (0x{container2.ToInt64():X}) (got 0x{owner.ToInt64():X})");
            ctx.Check(owner != container1, "picker owner is NOT container 1");
            ctx.Check(owner != ctx.MainHwnd, "picker owner is NOT the main launcher");

            if (!Input.ForceForeground(picker))
                throw new InvalidOperationException("Could not bring picker to the foreground; refusing to send Esc.");
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000), "picker dismissed with Esc without capturing");
        }

        ctx.Check(IsDocked(pig1.Hwnd, host1) || IsReleasedAndHidden(pig1.Hwnd), "pig1 still captured");
        ctx.Check(IsDocked(pig2.Hwnd, host2) || IsReleasedAndHidden(pig2.Hwnd), "pig2 still captured");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 34. picker-owner-falls-back-when-container-closed: closing the main
    //     launcher must not break a container's own "+" button — the picker
    //     must still appear, and the container must stay enabled/responsive
    //     both before and after the picker is shown and dismissed.
    // -------------------------------------------------------------------------
    private static void PickerOwnerFallsBackWhenContainerClosed(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "FB", "--color", "purple");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring the launcher to the foreground — refusing to click blind.");
        if (!Discover.TryCaptureIdentity(ctx.MainHwnd, out WindowIdentity mainIdentity))
            throw new InvalidOperationException("Launcher identity changed; refusing to close an unverified HWND.");
        VerifiedWindowOps.PostMessage(mainIdentity, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(ctx.MainHwnd), 3000), "launcher closed");
        Thread.Sleep(500);
        ctx.Check(!ctx.TabDock.HasExited, "TabDock still alive (populated container keeps the app running)");
        ctx.Check(NativeMethods.IsWindowEnabled(container), "container enabled BEFORE opening the picker (launcher closed)");

        ClickAddWindowButton(container);
        IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        ctx.Check(picker != IntPtr.Zero, "picker still appears from the container's own '+' button after the launcher is closed");
        if (picker != IntPtr.Zero)
        {
            IntPtr owner = NativeMethods.GetWindow(picker, NativeMethods.GW_OWNER);
            ctx.Check(owner == container, $"picker owner resolves to the requesting container itself with the launcher gone (got 0x{owner.ToInt64():X})");

            if (!Input.ForceForeground(picker))
                throw new InvalidOperationException("Could not bring picker to the foreground; refusing to send Esc.");
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000), "picker dismissed with Esc");
        }

        ctx.Check(NativeMethods.IsWindowEnabled(container), "container still enabled AFTER the picker closes");
        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive at scenario end");
        ctx.Check(IsDocked(pig.Hwnd, host) || IsReleasedAndHidden(pig.Hwnd), "pig still captured");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }
}
