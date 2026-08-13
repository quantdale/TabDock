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
        int dragOutX = (int)(leftRect.X + leftRect.Width / 2);
        if (!EnsureClickable(container, dragOutX, sy))
            throw new InvalidOperationException("Could not bring the container to the foreground before drag-out; refusing to drag blind.");
        Input.DragFromTo(dragOutX, sy, rc.right + 150, rc.bottom + 150, 14);

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
        if (!fg)
            throw new InvalidOperationException("Could not bring the drag-probe container to a verified foreground state.");

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
            if (!VerifiedWindowOps.SetWindowPos(
                    GetRememberedContainerIdentity(ctx, container),
                    NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
                throw new InvalidOperationException("Container identity changed before the topmost drag probe.");
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
                VerifiedWindowOps.SetWindowPos(
                    GetRememberedContainerIdentity(ctx, container),
                    NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }

    private static void PinTopmostAndVerify(Ctx ctx, IntPtr container, int x, int y, string what)
    {
        if (!VerifiedWindowOps.SetWindowPos(
                GetRememberedContainerIdentity(ctx, container),
                NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
            throw new InvalidOperationException("Container identity changed before the topmost pin.");
        Thread.Sleep(250);
        IntPtr root = RootAtPoint(x, y);
        if (root != container)
            GuardedProc.Log($"  DragProbe: even topmost-pinned, ({x},{y}) for the {what} resolves to {DescribeHwnd(root)} — the obscuring window is itself topmost.");
        ctx.Check(root == container, $"{what} start point resolves to the container after topmost pin");
        if (root == container && !EnsureClickable(container, x, y))
            throw new InvalidOperationException($"Could not bring the container to the foreground for the {what}; refusing to drag blind.");
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
    //     NoteGuestMoveSize re-glue behavior against a real app with a custom-drawn
    //     "fake" title bar, not just a plain WinForms one: both small and large
    //     native movements snap back to the host rect and keep the tab captured.
    //     (Formerly an H4/H5 PR gate against the deleted Reparent
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

        // --- Small jitter: must snap back.
        //     Chrome's own tab strip has its own click-vs-drag threshold
        //     before it hands off to native window dragging; too small a
        //     movement (e.g. the ~12px used against a plain WinForms title
        //     bar in dragout-by-titlebar) can be absorbed as a click instead
        //     of registering as a real move at all. ---
        Input.DragFromTo(startX, startY, startX + (int)(25 * scale), startY + (int)(15 * scale), 10);
        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, host), 3000),
            "small jitter drag on Chrome's own tab strip snaps back to docked");

        // --- Large movement is still re-glued, from a different start point on
        //     the same tab strip to avoid a same-pixel double-click. ---
        Thread.Sleep(700);
        int startX2 = startX + (int)(40 * scale);
        long dragOff = TabDockLog.RecordLogLength();
        Input.DragFromTo(startX2, startY, startX2 + (int)(130 * scale), startY + (int)(92 * scale), 16);

        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, host), 5000),
            "large movement on Chrome's own tab strip is re-glued to the host");
        ctx.Check(TabCount(container) == 1, "Chrome remains captured after native movement");
        ctx.Check(TabDockLog.WaitForLogLine(dragOff, "SHEPHERD[re-glue]", 3000),
            "TabDock logged the native movement re-glue");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(chrome.Proc != null && !chrome.Proc.HasExited, "chrome guest process alive after drag");
    }

    // -------------------------------------------------------------------------
    // 27. dragout-by-titlebar: native title-bar movement is always re-glued.
    //     Explicit Pop out is the only release gesture; a captured guest remains
    //     a tab even when its untouched native frame is dragged a long distance.
    // -------------------------------------------------------------------------
    private static void DragOutByTitlebar(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "DOT", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked over host right after capture");

        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT dockedRect);
        int titleX = dockedRect.left + dockedRect.Width / 3;
        int titleY = dockedRect.top + 15;
        GuardedProc.Log($"  DragOutByTitlebar: titlebar drag point ({titleX},{titleY}), docked rect {Util.FormatRect(dockedRect)}.");

        if (!Input.ForceForeground(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the docked pig to the foreground — refusing to drag blind.");

        // --- Small movement: must snap back. ---
        Input.DragFromTo(titleX, titleY, titleX + 12, titleY + 8, 10);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "small native title movement snaps back to docked");

        // A second mouse-down at the exact same screen point shortly after the
        // first click-drag-release risks Windows treating the pair as a
        // double-click on the caption (which can toggle maximize or otherwise
        // misbehave) rather than starting a fresh drag. Settle past any
        // double-click timing window and start the second drag from a
        // different point on the same title bar (still safely clear of the
        // system-menu icon and the min/max/close buttons).
        Thread.Sleep(700);
        int titleX2 = titleX + 40;

        // --- Large movement: it is still not a pop-out. ---
        long off = TabDockLog.RecordLogLength();
        const int dx = 180, dy = 150;
        Input.DragFromTo(titleX2, titleY, titleX2 + dx, titleY + dy, 14);

        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000),
            "large native title movement is re-glued to the content rect");
        ctx.Check(TabCount(container) == 1, "tab remains captured after native title movement");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SHEPHERD[re-glue]", 3000),
            "TabDock logged the native move/size re-glue");
        ctx.Check(TabDockLog.CountNewLines(off, "SHEPHERD[dragout]") == 0,
            "native title movement does not log a pop-out dragout");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process still alive after native movement");
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
            AutomationElement? t = FindTabText(container, p.Title, out int c3);
            if (t == null || c3 != 1)
                throw new InvalidOperationException($"Remaining tab for '{p.Title}' not found uniquely (count={c3}).");
            (int tx, int ty) = Uia.Center(t);
            if (!EnsureClickable(container, tx, ty))
                throw new InvalidOperationException("Could not bring the container to the foreground before clicking a remaining tab; refusing to click blind.");
            Input.ClickAt(tx, ty);
            ctx.Check(Util.WaitUntil(() => IsDocked(p.Hwnd, host), 3000), $"remaining tab '{p.Title}' is clickable/switchable after the drag+immediate-popout sequence");
        }

        ClickAddWindowButton(container);
        AutomationElement? containerRoot = Uia.FromHwnd(container);
        bool panelOpened = containerRoot != null && Util.WaitUntil(() =>
            Uia.FindDescendantByName(containerRoot, ControlType.Button, "Add selected", null, out _) != null, 5000);
        ctx.Check(panelOpened, "'+' add-window button still opens the inline capture surface after drag-reorder + immediate pop-out");
        if (panelOpened)
        {
            ClickAddWindowButton(container);
            ctx.Check(Util.WaitUntil(() =>
                Uia.FindDescendantByName(containerRoot!, ControlType.Button, "Add selected", null, out _) == null, 3000),
                "inline capture surface dismissed with the second '+' click without capturing");
        }

        ctx.Check(middlePig.Proc != null && !middlePig.Proc.HasExited, "popped-out pig alive standalone");
        GuestInfo remaining0 = remaining[0];
        GuestInfo remaining1 = remaining[1];
        ctx.Check(remaining0.Proc != null && !remaining0.Proc.HasExited && remaining1.Proc != null && !remaining1.Proc.HasExited,
            "both remaining pigs alive");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // drag-release-render-stability (goal §19/§21): with ONE captured guest
    // visible (normal mode), drag the CONTAINER's caption through a
    // multi-segment trajectory (right, down, left, up, diagonal, return — many
    // intermediate WM_WINDOWPOSCHANGED events), release, then IMMEDIATELY —
    // without ANY tab interaction — assert the guest is still visible, live,
    // glued to the full content rect, and the TOP window at the content
    // center (a covered-but-correctly-sized guest must FAIL). A test that
    // switches tabs before asserting would be invalid: tab switching itself
    // repairs the post-drag blanking defect this scenario guards.
    // -------------------------------------------------------------------------
    private static void DragReleaseRenderStability(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "DRS-A", "--color", "red", "--pulse");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "guest docked full-width at capture");
        EnsureContainerInWorkArea(ctx, container);

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int centerX = hostRect.left + hostRect.Width / 2;
        int centerY = hostRect.top + hostRect.Height / 2;

        int cycles = Math.Max(20, opt.Cycles ?? 20);
        for (int i = 1; i <= cycles; i++)
        {
            NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcBefore);
            int sx = rcBefore.left + rcBefore.Width / 2 + (i % 2 == 1 ? -60 : 60);
            int sy = rcBefore.top + 16; // WindowChrome CaptionHeight=32 band
            (int[] xs, int[] ys) = BuildDragTrajectory(container, sx, sy, mirrored: i % 2 == 1);
            if (!EnsureClickable(container, sx, sy))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to drag blind.");

            long dragOff = TabDockLog.RecordLogLength();
            Input.DragPolyline(xs, ys, stepsPerSegment: 8);
            Thread.Sleep(250); // Render-priority final reconciliation

            NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcAfter);
            ctx.Check(Math.Abs(rcAfter.left - rcBefore.left) > 8 || Math.Abs(rcAfter.top - rcBefore.top) > 8,
                $"cycle {i}: container actually moved (Δ=({rcAfter.left - rcBefore.left},{rcAfter.top - rcBefore.top}))");
            ctx.Check(TabDockLog.CountNewLines(dragOff, "SHEPHERD[position]") >= 2,
                $"cycle {i}: multiple re-glue events during the drag (multi-segment trajectory)");
            ctx.Check(NativeMethods.IsWindowVisible(pig.Hwnd), $"cycle {i}: guest visible immediately after release");
            ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000),
                $"cycle {i}: guest glued to the full content rect");
            ctx.Check(Util.WaitUntil(() => TopWindowPidAt(centerX, centerY) == pig.Pid, 3000),
                $"cycle {i}: guest is the TOP window at the content center (not covered)");
            ctx.Check(TabCount(container) == 1, $"cycle {i}: still one tab (no switch, no pop-out)");
            ctx.Check(TabDockLog.CountNewLines(dragOff, "EXCEPTION") == 0, $"cycle {i}: no EXCEPTION");

            if (i % 5 == 0)
            {
                // Deeper liveness probe without any foreground/input side
                // effect. The pig toggles its background every 500ms, so two
                // captures at a fixed offset can land on the same phase and
                // diff ~0 (false FAIL). Capture three frames 400ms apart and
                // require ANY adjacent pair to differ: over any 800ms span the
                // pig toggles at least once, and the 400ms gap is shorter than
                // the 500ms toggle period, so at most one toggle falls between
                // adjacent frames — at least one adjacent pair must straddle a
                // toggle. Live pixels must also be red-dominant (the container
                // background is neutral gray, not red).
                int[]? f0 = Pixels.CaptureHostScreenArea(host);
                Thread.Sleep(400);
                int[]? f1 = Pixels.CaptureHostScreenArea(host);
                Thread.Sleep(400);
                int[]? f2 = Pixels.CaptureHostScreenArea(host);
                double d01 = f0 != null && f1 != null ? Pixels.ComputeAvgFrameDiff(f0, f1) : 0;
                double d12 = f1 != null && f2 != null ? Pixels.ComputeAvgFrameDiff(f1, f2) : 0;
                ctx.Check(f0 != null && f1 != null && f2 != null && (d01 > 0.005 || d12 > 0.005),
                    $"cycle {i}: guest content is LIVE (pulse variance on screen)");
                ctx.Check(f2 != null && f2.Length > 0 && Pixels.DominantChannel(f2) == 'r',
                    $"cycle {i}: RED guest pixels on screen (not the container background)");
            }
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines across all cycles");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "guest process alive across all cycles");
    }

    /// <summary>PID of the top-level window owning the topmost window at (x, y), or 0.</summary>
    private static uint TopWindowPidAt(int x, int y)
    {
        IntPtr top = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        if (top == IntPtr.Zero)
            return 0;
        IntPtr root = NativeMethods.GetAncestor(top, NativeMethods.GA_ROOT);
        NativeMethods.GetWindowThreadProcessId(root, out uint pid);
        return pid;
    }

    /// <summary>
    /// Positions the container once at a work-area inset if it does not fit
    /// with >= 120px margin on every side (the drag trajectories drift and
    /// must never exit the work area mid-gesture).
    /// </summary>
    private static void EnsureContainerInWorkArea(Ctx ctx, IntPtr container)
    {
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(
            NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST), ref mi);
        bool fits = rc.left >= mi.rcWork.left + 120 && rc.right <= mi.rcWork.right - 120
            && rc.top >= mi.rcWork.top + 120 && rc.bottom <= mi.rcWork.bottom - 120;
        if (fits)
            return;
        VerifiedWindowOps.SetWindowPos(
            GetRememberedContainerIdentity(ctx, container), IntPtr.Zero,
            mi.rcWork.left + 80, mi.rcWork.top + 80, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        Thread.Sleep(500);
        // Vacuity guard: the drag trajectory (max excursion +160/+100px) and
        // the pane-center probes must stay inside the work area. On a monitor
        // narrower than the container this fails with a clear message and is
        // classified as an environmental limitation, not a product defect.
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc2);
        ctx.Check(rc2.right <= mi.rcWork.right - 40 && rc2.bottom <= mi.rcWork.bottom - 40,
            "container fits the monitor work area for the drag trajectory (monitor too small otherwise)");
    }

    /// <summary>
    /// Six waypoints (absolute screen positions) for one caption drag: right,
    /// down, left, up, diagonal, return. Odd cycles mirror the offsets on
    /// BOTH axes so consecutive cycles oscillate around a stable origin
    /// (net displacement zero per pair) instead of drifting off screen;
    /// every waypoint is clamped into the monitor work area.
    /// </summary>
    private static (int[] Xs, int[] Ys) BuildDragTrajectory(IntPtr container, int sx, int sy, bool mirrored)
    {
        int m = mirrored ? -1 : 1;
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(
            NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST), ref mi);
        int xMin = mi.rcWork.left + 40, xMax = mi.rcWork.right - 40;
        int yMin = mi.rcWork.top + 40, yMax = mi.rcWork.bottom - 40;

        int[] offsetsX = { 160, 160, 40, 40, 130, 30 };
        int[] offsetsY = { 0, 100, 100, 30, 70, 10 };
        var xs = new int[6];
        var ys = new int[6];
        for (int i = 0; i < 6; i++)
        {
            xs[i] = Math.Clamp(sx + m * offsetsX[i], xMin, xMax);
            ys[i] = Math.Clamp(sy + m * offsetsY[i], yMin, yMax);
        }
        return (xs, ys);
    }


}
