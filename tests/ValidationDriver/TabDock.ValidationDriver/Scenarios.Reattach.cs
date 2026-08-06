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

        // The "+" add-window header button must still open the picker; cancel
        // without completing a capture.
        ClickAddWindowButton(container);
        IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 5000);
        ctx.Check(picker != IntPtr.Zero, "'+' add-window button opened the picker after the reattach");
        if (picker != IntPtr.Zero)
        {
            Input.ForceForeground(picker);
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000), "picker dismissed with Esc without capturing");
        }

        // Minimize / restore.
        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container), 3000), "minimize button minimized the container after the reattach");
        NativeMethods.ShowWindow(container, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container), 3000), "container restored (test cleanup step, not the restore gesture itself)");

        // Rename (mirrors the `rename` scenario's exact pattern).
        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int capCount);
        ctx.Check(caption != null && capCount == 1, $"caption title TextBlock 'Group' found uniquely after reattach (count={capCount})");
        if (caption != null && capCount == 1)
        {
            (int cx, int cy) = Uia.Center(caption);
            if (!EnsureClickable(container, cx, cy))
                throw new InvalidOperationException("Could not bring the container to the foreground and its caption is obscured — refusing to click blind.");
            Input.DoubleClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText("TDVAL-Reattached");
            Input.SendKey(Input.VK_RETURN);
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Reattached", 2000),
                "rename after the reattach worked (container title changed)");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive throughout");
    }

    // -------------------------------------------------------------------------
    // 32. reattach-repeated-cycles: same regression target as
    //     reattach-thenclick-othertab, but the pop-out/recapture cycle runs 3x
    //     on the SAME guest before the final header-control verification —
    //     targets stale drag/click state that might only accumulate across
    //     MULTIPLE cycles rather than surface on the very first one.
    // -------------------------------------------------------------------------
    private static void ReattachRepeatedCycles(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "RCA", "--color", "green");
        GuestInfo pigB = SpawnPig(ctx, "RCB", "--color", "white");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        const int cycles = 3;
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
            "clicking the OTHER tab after 3 reattach cycles worked (no accumulated stale Mouse.Capture)");

        ClickAddWindowButton(container);
        IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 5000);
        ctx.Check(picker != IntPtr.Zero, "'+' add-window button still opens the picker after 3 reattach cycles");
        if (picker != IntPtr.Zero)
        {
            Input.ForceForeground(picker);
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000), "picker dismissed with Esc without capturing");
        }

        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container), 3000), "minimize still works after 3 reattach cycles");
        NativeMethods.ShowWindow(container, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container), 3000), "container restored");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines across all 3 reattach cycles");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive throughout");
    }
}
