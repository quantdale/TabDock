using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

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
        // The driver process can regain the desktop foreground during the
        // startup settle delay (for example when the hosting terminal paints a
        // prompt). Re-establish the verified TabDock target immediately before
        // sending the held-key sequence; keyboard input must still fail closed
        // if this activation cannot be proven.
        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring the launcher to the foreground before the hotkey hold; refusing to send input.");

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
                // The first hotkey opens the picker and legitimately moves the
                // foreground to that new TabDock-owned window. Subsequent
                // simulated repeat taps must target the currently foreground
                // registered TabDock window, otherwise the safety gate would
                // correctly reject input aimed at the old launcher HWND.
                IntPtr inputTarget = ctx.MainHwnd;
                foreach (IntPtr candidate in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
                {
                    if (IsCapturePickerTitle(NativeMethods.GetWindowTextString(candidate)))
                    {
                        inputTarget = candidate;
                        break;
                    }
                }
                if (!Input.ForceForeground(inputTarget))
                    throw new InvalidOperationException("Could not bring the current hotkey target to the foreground; refusing to send input.");

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
            if (IsCapturePickerTitle(t))
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
            if (IsCapturePickerTitle(t))
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
        AutomationElement? container2Root = Uia.FromHwnd(container2);
        AutomationElement? container1Root = Uia.FromHwnd(container1);
        bool panelOpened = container2Root != null && Util.WaitUntil(() =>
            Uia.FindDescendantByName(container2Root, ControlType.Button, "Add selected", null, out _) != null, 6000);
        ctx.Check(panelOpened, "inline capture surface appeared from container 2's own '+' button");
        bool otherPanelOpened = container1Root != null
            && Uia.FindDescendantByName(container1Root, ControlType.Button, "Add selected", null, out _) != null;
        ctx.Check(!otherPanelOpened, "container 1 did not receive container 2's inline capture surface");
        if (panelOpened)
            ClickAddWindowButton(container2); // documented second-click toggle dismisses it

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
        AutomationElement? containerRoot = Uia.FromHwnd(container);
        bool panelOpened = containerRoot != null && Util.WaitUntil(() =>
            Uia.FindDescendantByName(containerRoot, ControlType.Button, "Add selected", null, out _) != null, 6000);
        ctx.Check(panelOpened, "inline capture surface still appears from the container's own '+' button after the launcher is closed");
        if (panelOpened)
            ClickAddWindowButton(container); // documented second-click toggle dismisses it

        ctx.Check(NativeMethods.IsWindowEnabled(container), "container still enabled AFTER the picker closes");
        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive at scenario end");
        ctx.Check(IsDocked(pig.Hwnd, host) || IsReleasedAndHidden(pig.Hwnd), "pig still captured");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }
}
