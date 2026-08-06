using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 16. dragreorder: real-mouse drag-reorder within the strip (no crash, tabs
    //     intact) and drag-out of the container (pop-out release).
    // -------------------------------------------------------------------------
    private static void DragReorder(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "DRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "DRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Reorder: drag the RIGHTMOST tab into the left half of the LEFTMOST tab
        // (capture order follows picker Z-order, so tab order is not guaranteed;
        // GetDropIndex uses item midpoints, so the target must be left of the
        // leftmost tab's midpoint to produce a different drop index).
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabA == null || cA != 1 || tabB == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (A={cA}, B={cB}).");
        Rect rA = Uia.GetElementRect(tabA);
        Rect rB = Uia.GetElementRect(tabB);
        bool aIsRight = rA.X > rB.X;
        GuestInfo movedPig = aIsRight ? pigA : pigB;
        Rect leftRect = aIsRight ? rB : rA;
        (int sx, int sy) = Uia.Center(aIsRight ? tabA : tabB);
        long dragOff = TabDockLog.RecordLogLength(); // scope reorder analysis to THIS drag only
        Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabCount(container) == 2, "still 2 tabs after drag-reorder");
        // H2 oscillation guard: a correct (frozen-midpoint) drag produces a small,
        // bounded number of `Reordered tab` lines; the H2 bug produced hundreds of
        // A<->B flips per drag and passed the old `>= 1` check. The flip-pair check
        // (a reorder X->Y directly followed by Y->X) is the primary, machine-speed-
        // independent regression signal; the count is a generous churn ceiling.
        // Observed on a passing run: a handful of reorders (single digits); the bound
        // below carries generous headroom — confirm/tighten via the 7.2 supervised run.
        const int MaxReordersPerDrag = 20;
        (int reorderCount, int flipPairs) = TabDockLog.AnalyzeReorders(dragOff);
        ctx.Check(reorderCount >= 1, "a reorder was applied (log)");
        ctx.Check(flipPairs == 0, $"zero immediate flip-back pairs during the drag (H2 oscillation) — got {flipPairs}");
        ctx.Check(reorderCount <= MaxReordersPerDrag, $"reorder count within bound (<= {MaxReordersPerDrag}, H2 churn ceiling) — got {reorderCount}");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "both pigs alive after drag-reorder");

        // Drag-out: drag the just-moved tab well outside the container -> pop-out
        // release. Deliberately reuses the leftmost slot's screen position rather
        // than re-finding the tab via UIA: after a reorder the WPF automation
        // peers for re-inserted items go stale (observed: FindTabText count=0 for
        // several seconds while the tab was demonstrably alive).
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        Input.DragFromTo((int)(leftRect.X + leftRect.Width / 2), sy, rc.right + 150, rc.bottom + 150, 14);

        ctx.Check(Util.WaitUntil(() => IsReleased(movedPig, host), 5000), $"moved pig '{movedPig.Title}' released by drag-out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-out");
        ctx.Check(movedPig.Proc != null && !movedPig.Proc.HasExited, "moved pig alive standalone");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // dragprobe: diagnostic companion to dragreorder for the "synthetic drag
    // never reaches the WPF tab strip" failure mode (observed: app log shows
    // zero drag activity for the whole drag window while UIA-driven clicks
    // land fine). Instead of dragging blind, it first asks WindowFromPoint
    // which top-level window actually sits under the drag start point — an
    // obscuring window (e.g. an always-on-top or overlapping window owned by
    // whatever session is driving the harness) receives the drag instead, and
    // dragreorder then fails with no app-side evidence. If the tab strip is
    // obscured, the probe repositions the container programmatically to a
    // known-clear spot and retries, then runs the same reorder + drag-out
    // assertions as dragreorder, so a PASS validates the full path and a FAIL
    // names the window that stole the drag.
    // -------------------------------------------------------------------------
    private static void DragProbe(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "DPA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "DPB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        bool fg = Input.ForceForeground(container);
        GuardedProc.Log($"  DragProbe: ForceForeground(container)={fg}.");

        (int sx, int sy, Rect leftRect, GuestInfo movedPig, int lx, int ly) = FindDragGeometry(ctx, container, pigA, pigB);

        // Diagnostic 1: does the drag start point resolve to the container, or
        // is something else on top of the tab strip? An obscuring window (the
        // user's own browser sitting over the test area is the observed case)
        // receives the drag instead of TabDock, and dragreorder then fails
        // with zero app-side evidence. Informational only — the remedy below
        // (temporary topmost) doesn't disturb the obscuring window.
        bool madeTopmost = false;
        if (RootAtPoint(sx, sy) != container)
        {
            GuardedProc.Log($"  DragProbe: drag start ({sx},{sy}) is covered by {DescribeHwnd(RootAtPoint(sx, sy))} — the drag would go there, not to TabDock. Pinning the container topmost for the drag instead of touching the other window.");
            NativeMethods.SetWindowPos(container, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            madeTopmost = true;
            Thread.Sleep(300);
        }

        try
        {
            IntPtr startRoot = RootAtPoint(sx, sy);
            GuardedProc.Log($"  DragProbe: drag start ({sx},{sy}) resolves to {DescribeHwnd(startRoot)}; container is {DescribeHwnd(container)}.");

            // Diagnostic 2: plain-click the OTHER (leftmost) tab and require the
            // app to log a tab switch. This isolates drag-specific failure from
            // general mouse-delivery failure to the WPF container: if even a
            // click doesn't switch tabs, no drag scenario can run in this
            // session context at all.
            long switchesBefore = TabDockLog.CountNewLines(ctx.LogOffset, "Switched group");
            Input.ClickAt(lx, ly);
            Thread.Sleep(600);
            long switchesAfter = TabDockLog.CountNewLines(ctx.LogOffset, "Switched group");
            ctx.Check(switchesAfter > switchesBefore, "plain click on a tab produces a tab switch (mouse delivery to the tab strip)");

            // The tab-switch click shuffles z-order (the shepherd re-pairs the
            // newly active guest and foreground shuffles), so whatever pinned
            // state we had is unreliable now: re-pin topmost immediately before
            // EVERY drag and verify the start point right at mousedown.
            PinTopmostAndVerify(ctx, container, sx, sy, "reorder drag");
            madeTopmost = true;

            Input.MoveTo(sx, sy);
            Thread.Sleep(60);
            IntPtr downRoot = RootAtPoint(sx, sy);
            Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
            Thread.Sleep(600);
            GuardedProc.Log($"  DragProbe: at reorder mousedown the point resolved to {DescribeHwnd(downRoot)}.");

            ctx.Check(TabCount(container) == 2, "still 2 tabs after drag-reorder");
            ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "Reordered tab") >= 1, "a reorder was applied (log)");
            ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");

            // Same drag-out half as dragreorder, so a probe PASS covers both paths.
            NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
            int outX = (int)(leftRect.X + leftRect.Width / 2);
            PinTopmostAndVerify(ctx, container, outX, sy, "drag-out");
            Input.DragFromTo(outX, sy, rc.right + 150, rc.bottom + 150, 14);
            ctx.Check(Util.WaitUntil(() => IsReleased(movedPig, host), 5000), $"moved pig '{movedPig.Title}' released by drag-out");
            ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-out");
            ctx.Check(movedPig.Proc != null && !movedPig.Proc.HasExited, "moved pig alive standalone");
        }
        finally
        {
            if (madeTopmost)
            {
                NativeMethods.SetWindowPos(container, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }

    private static void PinTopmostAndVerify(Ctx ctx, IntPtr container, int x, int y, string what)
    {
        NativeMethods.SetWindowPos(container, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        Thread.Sleep(250);
        IntPtr root = RootAtPoint(x, y);
        if (root != container)
            GuardedProc.Log($"  DragProbe: even topmost-pinned, ({x},{y}) for the {what} resolves to {DescribeHwnd(root)} — the obscuring window is itself topmost.");
        ctx.Check(root == container, $"{what} start point resolves to the container after topmost pin");
    }

    private static (int sx, int sy, Rect leftRect, GuestInfo movedPig, int lx, int ly) FindDragGeometry(Ctx ctx, IntPtr container, GuestInfo pigA, GuestInfo pigB)
    {
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabA == null || cA != 1 || tabB == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (A={cA}, B={cB}).");
        Rect rA = Uia.GetElementRect(tabA);
        Rect rB = Uia.GetElementRect(tabB);
        bool aIsRight = rA.X > rB.X;
        (int sx, int sy) = Uia.Center(aIsRight ? tabA : tabB);
        (int lx, int ly) = Uia.Center(aIsRight ? tabB : tabA);
        return (sx, sy, aIsRight ? rB : rA, aIsRight ? pigA : pigB, lx, ly);
    }

    private static IntPtr RootAtPoint(int x, int y)
    {
        IntPtr at = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        return at == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(at, NativeMethods.GA_ROOT);
    }

    private static string DescribeHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "(no window)";
        return $"0x{hwnd.ToInt64():X} class='{NativeMethods.GetClassNameString(hwnd)}' title='{NativeMethods.GetWindowTextString(hwnd)}'";
    }

    // -------------------------------------------------------------------------
    // 17. chrometabdrag: drag a real captured Chrome window by its own
    //     client-drawn tab strip (Chrome hit-tests this as HTCAPTION, so the
    //     guest itself enters the same interactive move loop as a native
    //     title-bar drag — see dragout-by-titlebar). Verifies both halves of
    //     NoteGuestMoveSize's threshold against a real app with a custom-drawn
    //     "fake" title bar, not just a plain WinForms one: a small drag (under
    //     DragOutThresholdPx) snaps back to the host rect; a large drag pops
    //     the tab out. (Formerly an H4/H5 PR gate against the deleted Reparent
    //     backend's fill-clamp/host-background-smear bugs, both of which are
    //     structurally impossible under Shepherd — a guest is either exactly
    //     docked over the marker or fully popped out, never mid-reparented
    //     with the host's own background exposed in between.)
    // -------------------------------------------------------------------------
    private static void ChromeTabDrag(Ctx ctx, Options opt)
    {
        GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, host), 3000), "chrome docked over host at capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        double scale = NativeMethods.GetDpiForWindow(container) / 96.0;
        // Chrome's tab strip is a ~36-40px (96 DPI) band at the top of its
        // client area; this x offset clears the first tab's own close button
        // and lands on a freshly opened single-tab window's tab.
        int startX = hostRect.left + (int)(150 * scale);
        int startY = hostRect.top + (int)(18 * scale);

        // --- Small jitter (under DragOutThresholdPx=40): must snap back.
        //     Chrome's own tab strip has its own click-vs-drag threshold
        //     before it hands off to native window dragging; too small a
        //     movement (e.g. the ~12px used against a plain WinForms title
        //     bar in dragout-by-titlebar) can be absorbed as a click instead
        //     of registering as a real move at all. ---
        Input.DragFromTo(startX, startY, startX + (int)(25 * scale), startY + (int)(15 * scale), 10);
        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, host), 3000),
            "small jitter drag on Chrome's own tab strip snaps back to docked");

        // --- Real pop-out (well past the threshold), from a different start
        //     point on the same tab strip to avoid a same-pixel double-click. ---
        Thread.Sleep(700);
        int startX2 = startX + (int)(40 * scale);
        long dragOff = TabDockLog.RecordLogLength();
        Input.DragFromTo(startX2, startY, startX2 + (int)(130 * scale), startY + (int)(92 * scale), 16);

        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(chrome.Hwnd, host), 5000),
            "drag past the threshold on Chrome's own tab strip releases the tab (shown standalone, not docked)");
        ctx.Check(TabDockLog.WaitForLogLine(dragOff, "SHEPHERD[dragout]", 3000),
            "TabDock log recorded the drag-out release (SHEPHERD[dragout])");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(chrome.Proc != null && !chrome.Proc.HasExited, "chrome guest process alive after drag");
    }

    // -------------------------------------------------------------------------
    // 27. dragout-by-titlebar: verifies ContainerWindow.NoteGuestMoveSize's
    //     drag-out-by-real-titlebar hardening (DragOutThresholdPx = 40) — a
    //     real mouse drag on the shepherded guest's OWN native title bar
    //     (Shepherd never strips WS_CAPTION) must snap back on small jitter
    //     and release the tab as a pop-out once it clears the threshold.
    // -------------------------------------------------------------------------
    private static void DragOutByTitlebar(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "DOT", "--color", "red");
        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT rectBeforeCapture);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked over host right after capture");

        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT dockedRect);
        int titleX = dockedRect.left + dockedRect.Width / 3;
        int titleY = dockedRect.top + 15;
        GuardedProc.Log($"  DragOutByTitlebar: titlebar drag point ({titleX},{titleY}), docked rect {Util.FormatRect(dockedRect)}.");

        if (!Input.ForceForeground(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the docked pig to the foreground — refusing to drag blind.");

        // --- Small jitter (under DragOutThresholdPx=40): must snap back. ---
        Input.DragFromTo(titleX, titleY, titleX + 12, titleY + 8, 10);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "small jitter drag (~14px) snaps back to docked");

        // A second mouse-down at the exact same screen point shortly after the
        // first click-drag-release risks Windows treating the pair as a
        // double-click on the caption (which can toggle maximize or otherwise
        // misbehave) rather than starting a fresh drag. Settle past any
        // double-click timing window and start the second drag from a
        // different point on the same title bar (still safely clear of the
        // system-menu icon and the min/max/close buttons).
        Thread.Sleep(700);
        int titleX2 = titleX + 40;

        // --- Real pop-out (well past the threshold). ---
        long off = TabDockLog.RecordLogLength();
        const int dx = 180, dy = 150;
        Input.DragFromTo(titleX2, titleY, titleX2 + dx, titleY + dy, 14);

        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pig.Hwnd, host), 5000),
            "drag-out past the 40px threshold releases the tab (shown standalone, not docked)");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "tab removed from the strip (it was the only tab, so the container closes/empties)");
        ctx.Check(TabDockLog.ContainsNewLine(off, "SHEPHERD[dragout]"),
            "TabDock log recorded the drag-out release (SHEPHERD[dragout])");

        // NoteGuestMoveSize's drag-out release goes through the same release
        // path as every other release (Pop out via the tab strip, Close group,
        // etc.): it restores the placement snapshotted at capture time, not
        // wherever the drag happened to drop it — the drag-past-threshold is
        // only the SIGNAL that this was an intentional pop-out, not a
        // "leave it where dropped" gesture.
        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT rcAfterDrag);
        ctx.Check(Util.RectNear(rectBeforeCapture, rcAfterDrag, 4),
            $"pig restored to its original pre-capture placement (before {Util.FormatRect(rectBeforeCapture)}, after {Util.FormatRect(rcAfterDrag)})");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process still alive after drag-out (released standalone, not killed)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 37. dragreorder-then-immediate-popout: drag-reorder once among 3 tabs
    //     (same technique as the `dragreorder` scenario), then IMMEDIATELY
    //     right-click the tab now in the middle position and pop it out —
    //     targets "did a drag operation leave Mouse.Capture in a bad state
    //     that a subsequent unrelated pop-out then compounds."
    // -------------------------------------------------------------------------
    private static void DragReorderThenImmediatePopOut(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "DRPA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "DRPB", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "DRPC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        GuestInfo[] pigs = { pigA, pigB, pigC };
        var rects = new Rect[3];
        for (int i = 0; i < 3; i++)
        {
            AutomationElement? t = FindTabText(container, pigs[i].Title, out int c);
            if (t == null || c != 1)
                throw new InvalidOperationException($"Tab for '{pigs[i].Title}' not found uniquely (count={c}).");
            rects[i] = Uia.GetElementRect(t);
        }
        int rightmost = 0, leftmost = 0;
        for (int i = 1; i < 3; i++)
        {
            if (rects[i].X > rects[rightmost].X) rightmost = i;
            if (rects[i].X < rects[leftmost].X) leftmost = i;
        }

        AutomationElement? rightTab = FindTabText(container, pigs[rightmost].Title, out _);
        if (rightTab == null)
            throw new InvalidOperationException("Rightmost tab vanished before the drag could start.");
        (int sx, int sy) = Uia.Center(rightTab);
        Input.DragFromTo(sx, sy, (int)(rects[leftmost].X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabCount(container) == 3, "still 3 tabs after drag-reorder");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "Reordered tab") >= 1, "a reorder was applied (log)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");

        // Re-read positions after the reorder and pop out whichever pig is now
        // in the middle slot, with NO extra settle sleep beyond DragFromTo's own.
        var current = new List<(GuestInfo Pig, Rect Rect)>();
        foreach (GuestInfo p in pigs)
        {
            AutomationElement? t = FindTabText(container, p.Title, out int c2);
            if (t == null || c2 != 1)
                throw new InvalidOperationException($"Tab for '{p.Title}' not found uniquely after reorder (count={c2}).");
            current.Add((p, Uia.GetElementRect(t)));
        }
        current.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));
        GuestInfo middlePig = current[1].Pig;
        GuestInfo[] remaining = { current[0].Pig, current[2].Pig };

        ClickTabMenuItem(ctx, container, middlePig.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(middlePig.Hwnd, host), 5000), $"middle-position pig '{middlePig.Title}' popped out cleanly right after the reorder");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), "2 tabs remain after the immediate pop-out");

        foreach (GuestInfo p in remaining)
        {
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            AutomationElement? t = FindTabText(container, p.Title, out int c3);
            if (t == null || c3 != 1)
                throw new InvalidOperationException($"Remaining tab for '{p.Title}' not found uniquely (count={c3}).");
            (int tx, int ty) = Uia.Center(t);
            Input.ClickAt(tx, ty);
            ctx.Check(Util.WaitUntil(() => IsDocked(p.Hwnd, host), 3000), $"remaining tab '{p.Title}' is clickable/switchable after the drag+immediate-popout sequence");
        }

        ClickAddWindowButton(container);
        IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 5000);
        ctx.Check(picker != IntPtr.Zero, "'+' add-window button still opens the picker after drag-reorder + immediate pop-out");
        if (picker != IntPtr.Zero)
        {
            Input.ForceForeground(picker);
            Input.SendKey(Input.VK_ESCAPE);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000), "picker dismissed with Esc without capturing");
        }

        ctx.Check(middlePig.Proc != null && !middlePig.Proc.HasExited, "popped-out pig alive standalone");
        GuestInfo remaining0 = remaining[0];
        GuestInfo remaining1 = remaining[1];
        ctx.Check(remaining0.Proc != null && !remaining0.Proc.HasExited && remaining1.Proc != null && !remaining1.Proc.HasExited,
            "both remaining pigs alive");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }
}
