using System;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 31. reattach-thenclick-othertab: regression guard for the
    //     Mouse.Capture(TabsListBox)-left-stale bug fixed by
    //     ContainerWindow.ViewModel_PropertyChanged calling EndDrag() before
    //     SyncShepherdActiveWindow(). Pops a tab out and recaptures it back
    //     into the SAME group (via the container's own "+" button, which
    //     auto-preselects that group — see CaptureIntoExistingGroupViaAddButton),
    //     then exercises every header control the original report implicated:
    //     another tab, the "+" button itself, minimize, and rename.
    // -------------------------------------------------------------------------
    private static void ReattachThenClickOtherTab(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "RTA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "RTB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        ClickTabMenuItem(ctx, container, pigA.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 5000), "pigA released by Pop out");
        ctx.Check(NativeMethods.IsWindow(container), "container still open (pigB's tab remains)");

        CaptureIntoExistingGroupViaAddButton(ctx, container, host, pigA);
        ctx.Check(CountOpenContainers(ctx) == 1, "exactly one container is open after the reattach (no second group was created)");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), "2 tabs again after recapturing pigA back into the same group");

        // The root-cause regression check: click ANOTHER tab in the group
        // right after the reattach. ForceForeground can legitimately fail
        // here — WindowShepherdService.Release just explicitly foregrounded
        // pigA, and Windows' foreground-lock heuristic then blocks this
        // background process from immediately reclaiming it — so fall back
        // to EnsureClickable's point-obscured check, matching what a real
        // click from a human user would experience.
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabB == null || cB != 1)
            throw new InvalidOperationException($"Tab for '{pigB.Title}' not found uniquely (count={cB}).");
        (int tbx, int tby) = Uia.Center(tabB);
        if (!EnsureClickable(container, tbx, tby))
            throw new InvalidOperationException("Could not bring the container to the foreground and tab B is obscured — refusing to click blind.");
        Input.ClickAt(tbx, tby);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 3000),
            "clicking tab B after the reattach worked (no stale Mouse.Capture swallowing the click)");

        // The "+" add-window header button must still open the INLINE capture
        // surface after the reattach (the standalone "Capture windows" picker
        // window is the launcher/hotkey fallback only); close it with the
        // documented second-click toggle without completing a capture.
        ClickAddWindowButton(container);
        AutomationElement? panelRoot = Uia.FromHwnd(container);
        bool panelOpened = panelRoot != null && Util.WaitUntil(() =>
            Uia.FindDescendantByName(panelRoot, ControlType.Button, "Add selected", null, out _) != null, 5000);
        ctx.Check(panelOpened, "'+' add-window button opened the inline capture surface after the reattach");
        Thread.Sleep(300);
        ClickAddWindowButton(container);
        bool panelClosed = Util.WaitUntil(() =>
        {
            AutomationElement? r = Uia.FromHwnd(container);
            if (r == null)
                return false;
            return Uia.FindDescendantByName(r, ControlType.Button, "Add selected", null, out _) == null;
        }, 3000);
        ctx.Check(panelClosed, "inline capture surface dismissed with the second '+' click");

        // Rename through the Group header while the freshly reattached shell
        // is in its stable visible state. The same menu path is exercised here
        // after reattach; the native minimize/restore below is then kept as a
        // separate cleanup transition so stale post-restore UIA rectangles do
        // not turn this test into an unsafe coordinate guess.
        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? groupButton = Uia.FindDescendantByName(containerEl, ControlType.Button, "Group ▾", null, out int groupButtonCount);
        ctx.Check(groupButton != null && groupButtonCount == 1,
            $"Group header button found uniquely after reattach (count={groupButtonCount})");
        if (groupButton == null || groupButtonCount != 1)
            throw new InvalidOperationException("Group header button was not available after reattach.");
        (int groupX, int groupY) = Uia.Center(groupButton);
        if (!EnsureClickable(container, groupX, groupY))
            throw new InvalidOperationException("Could not bring the container to the foreground and its Group header was obscured — refusing to click blind.");
        Input.ClickAt(groupX, groupY);
        AutomationElement? renameItem = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
        if (renameItem == null)
            throw new InvalidOperationException("Group menu did not expose 'Rename group' after reattach.");
        (int renameX, int renameY) = Uia.Center(renameItem);
        Input.ClickAt(renameX, renameY);
        bool renameBoxOpened = Util.WaitUntil(() =>
        {
            AutomationElement? currentRoot = Uia.FromHwnd(container);
            return currentRoot != null && Uia.FindFirstOfType(currentRoot, ControlType.Edit) != null;
        }, 3000);
        ctx.Check(renameBoxOpened, "rename box opened after the reattach");
        if (renameBoxOpened)
        {
            Input.TypeText("TDVAL-Reattached");
            Input.SendKey(Input.VK_RETURN);
        }
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Reattached", 2000),
            "rename after the reattach worked (container title changed)");

        // Minimize / restore.
        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container), 3000), "minimize button minimized the container after the reattach");
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container), 3000), "container restored (test cleanup step, not the restore gesture itself)");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive throughout");
    }

    // -------------------------------------------------------------------------
    // 32. reattach-repeated-cycles: same regression target as
    //     reattach-thenclick-othertab, but the pop-out/recapture cycle runs
    //     --cycles times (default 3) on the SAME guest before the final
    //     header-control verification — targets stale drag/click state that
    //     might only accumulate across MULTIPLE cycles rather than surface on
    //     the very first one.
    // -------------------------------------------------------------------------
    private static void ReattachRepeatedCycles(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "RCA", "--color", "green");
        GuestInfo pigB = SpawnPig(ctx, "RCB", "--color", "white");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        int cycles = Math.Max(3, opt.Cycles ?? 3);
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            ClickTabMenuItem(ctx, container, pigB.Title, "Pop out");
            ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigB.Hwnd, host), 5000), $"cycle {cycle}: pigB released by Pop out");
            ctx.Check(NativeMethods.IsWindow(container), $"cycle {cycle}: container still open (pigA's tab remains)");

            CaptureIntoExistingGroupViaAddButton(ctx, container, host, pigB);
            ctx.Check(CountOpenContainers(ctx) == 1, $"cycle {cycle}: exactly one container is open (no second group was created)");
            ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), $"cycle {cycle}: 2 tabs again after recapture");
        }

        // Final verification: the same header-control regression checks as
        // reattach-thenclick-othertab, abbreviated.
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        if (tabA == null || cA != 1)
            throw new InvalidOperationException($"Tab for '{pigA.Title}' not found uniquely (count={cA}).");
        (int tax, int tay) = Uia.Center(tabA);
        if (!EnsureClickable(container, tax, tay))
            throw new InvalidOperationException("Could not bring the container to the foreground and tab A is obscured — refusing to click blind.");
        Input.ClickAt(tax, tay);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 3000),
            $"clicking the OTHER tab after {cycles} reattach cycles worked (no accumulated stale Mouse.Capture)");

        // Inline capture surface must still open (and toggle-close) after the
        // requested reattach cycles; the standalone picker is the launcher
        // fallback only.
        ClickAddWindowButton(container);
        AutomationElement? panelRoot = Uia.FromHwnd(container);
        bool panelOpened = panelRoot != null && Util.WaitUntil(() =>
            Uia.FindDescendantByName(panelRoot, ControlType.Button, "Add selected", null, out _) != null, 5000);
        ctx.Check(panelOpened, $"'+' add-window button still opens the inline capture surface after {cycles} reattach cycles");
        Thread.Sleep(300);
        ClickAddWindowButton(container);
        ctx.Check(Util.WaitUntil(() =>
        {
            AutomationElement? r = Uia.FromHwnd(container);
            if (r == null)
                return false;
            return Uia.FindDescendantByName(r, ControlType.Button, "Add selected", null, out _) == null;
        }, 3000), $"inline capture surface dismissed with the second '+' click after {cycles} cycles");

        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container), 3000), $"minimize still works after {cycles} reattach cycles");
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container), 3000), "container restored");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, $"no EXCEPTION lines across all {cycles} reattach cycles");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive throughout");
    }
}
