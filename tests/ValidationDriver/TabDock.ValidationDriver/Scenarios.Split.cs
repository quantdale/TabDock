using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // Split-screen scenarios (vertical LEFT/RIGHT split of exactly two captured
    // guests), driven from the tab context menu. Overview of the feature under
    // test (Views/ContainerWindow.xaml.cs):
    //   * Right-click a tab -> the initiating tab becomes the LEFT pane.
    //   * 1 tab            -> "Split screen" is present but DISABLED.
    //   * exactly 2 tabs   -> "Split screen" is a direct action that auto-selects
    //                          the sole other tab as RIGHT.
    //   * >= 3 tabs        -> "Split screen" is a submenu of candidate partner
    //                          tabs (excluding the initiating tab); choosing one
    //                          puts initiating=LEFT, chosen=RIGHT.
    //   * When split is active the menu also offers "Exit split screen" -> returns
    //     to single-visible-guest, hides the departing member, releases nothing.
    //   * Clicking a split member keeps split; clicking a non-paired tab exits split.
    // The app logs SPLIT[enter] / SPLIT[exit] / SPLIT[replace] / SPLIT[member-gone]
    // (all present in committed source).
    //
    // Pane geometry contract: the content-area marker HWND (class TabDockContentHost)
    // gives the full host client screen rect via Discover.GetClientScreenRect(host).
    // LEFT pane  = {host.Left, host.Top, host.Left + host.Width/2, host.Bottom}
    // RIGHT pane = {host.Left + host.Width/2, host.Top, host.Right, host.Bottom}
    // Each guest must cover its pane within Util.RectNear(..., 4). This is the ONLY
    // correct membership assertion — never GetParent (Shepherd never reparents).
    // -------------------------------------------------------------------------

    /// <summary>
    /// True when <paramref name="guest"/> is a visible top-level window covering its
    /// LEFT (or RIGHT) half of the host's content area within the 4px tolerance. This
    /// is the pane-membership assertion for the split feature; it must NOT use
    /// GetParent (Shepherd keeps guests as independent top-level windows).
    /// </summary>
    private static bool IsInPane(IntPtr guest, IntPtr host, bool left)
    {
        if (!NativeMethods.IsWindow(guest) || !NativeMethods.IsWindowVisible(guest))
            return false;

        NativeMethods.GetWindowRect(guest, out NativeMethods.RECT guestRect);
        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int leftW = hostRect.Width / 2;
        NativeMethods.RECT expected = left
            ? new NativeMethods.RECT
            {
                left = hostRect.left,
                top = hostRect.top,
                right = hostRect.left + leftW,
                bottom = hostRect.bottom,
            }
            : new NativeMethods.RECT
            {
                left = hostRect.left + leftW,
                top = hostRect.top,
                right = hostRect.right,
                bottom = hostRect.bottom,
            };
        return Util.RectNear(guestRect, expected, 4);
    }

    /// <summary>
    /// Right-clicks a tab (by guest title) and real-clicks the named item in the
    /// SPLIT-SUBMENU that the parent menu item expands to. With three or more tabs the
    /// "Split screen" entry is a submenu whose children are the candidate partner tab
    /// titles; WPF submenus open on hover, so we right-click the tab, hover the parent
    /// ("Split screen"), then real-click the child named <paramref name="childName"/>.
    /// The expanded submenu is a separate top-level popup owned by the pid, so the
    /// child is found with the same desktop search as the parent menu.
    /// </summary>
    private static void ClickTabSubmenuItem(Ctx ctx, IntPtr container, string guestTitle, string parentName, string childName)
    {
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");

        (int tx, int ty) = Uia.Center(tab);
        if (!EnsureClickable(container, tx, ty))
            throw new InvalidOperationException("Could not bring the container to the foreground and the tab is obscured — refusing to click blind.");
        Input.RightClickAt(tx, ty);

        AutomationElement? parent = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, parentName, 3000);
        if (parent == null)
            throw new InvalidOperationException($"Context menu item '{parentName}' did not appear within 3s.");

        // Hover the parent to expand the submenu (WPF submenus open on mouse-over).
        (int px, int py) = Uia.Center(parent);
        Input.MoveTo(px, py);
        Thread.Sleep(600);

        AutomationElement? child = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, childName, 3000);
        if (child == null)
            throw new InvalidOperationException($"Submenu item '{childName}' did not appear within 3s after expanding '{parentName}'.");
        (int cx, int cy) = Uia.Center(child);
        Input.ClickAt(cx, cy);
        Thread.Sleep(300);
    }

    /// <summary>
    /// Right-clicks <paramref name="leftPig"/>'s tab and clicks the direct "Split
    /// screen" action (valid only for a group of exactly two tabs, where the sole
    /// other tab is auto-selected as RIGHT). Returns the log offset recorded just
    /// before the menu click so callers can scope SPLIT[enter] assertions.
    /// </summary>
    private static long EnterSplitTwo(Ctx ctx, IntPtr container, GuestInfo leftPig)
    {
        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, leftPig.Title, "Split screen");
        return off;
    }

    /// <summary>Asserts both <paramref name="leftPig"/> (LEFT) and <paramref name="rightPig"/> (RIGHT) have entered their panes.</summary>
    private static void AssertSplitPanes(Ctx ctx, IntPtr host, GuestInfo leftPig, GuestInfo rightPig, string phase)
    {
        ctx.Check(Util.WaitUntil(() => IsInPane(leftPig.Hwnd, host, true), 3000),
            $"{phase}: '{leftPig.Title}' in LEFT pane");
        ctx.Check(Util.WaitUntil(() => IsInPane(rightPig.Hwnd, host, false), 3000),
            $"{phase}: '{rightPig.Title}' in RIGHT pane");
        ctx.Check(NativeMethods.IsWindowVisible(leftPig.Hwnd) && NativeMethods.IsWindowVisible(rightPig.Hwnd),
            $"{phase}: both split members visible");
    }

    private static void DismissTabContextMenu(Ctx ctx, IntPtr container, string guestTitle)
    {
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");
        (int x, int y) = Uia.Center(tab);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Tab was not clickable — refusing to right-click blind.");
        Input.RightClickAt(x, y);
        if (Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Pop out", 3000) == null)
            throw new InvalidOperationException("Tab context menu did not appear.");
        Input.SendKey(Input.VK_ESCAPE);
        Thread.Sleep(180);
    }

    private static void ClickTabCloseButton(Ctx ctx, IntPtr container, string guestTitle)
    {
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");
        AutomationElement? item = Uia.NearestAncestorOfType(tab, ControlType.ListItem);
        if (item == null)
            throw new InvalidOperationException($"Tab item for '{guestTitle}' was not found.");
        AutomationElement? close = Uia.FindDescendantByName(item, ControlType.Button, "×", null, out int closeCount);
        if (close == null || closeCount != 1)
            throw new InvalidOperationException($"Close affordance for '{guestTitle}' was not found uniquely (count={closeCount}).");
        (int x, int y) = Uia.Center(close);
        GuardedProc.Log($"  tab close '{guestTitle}': rect={Uia.GetElementRect(close)}, click=({x},{y}) windowFromPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y }).ToInt64():X}");
        Input.ClickAt(x, y);
        Thread.Sleep(250);
    }

    // -------------------------------------------------------------------------
    // split-single-disabled: with one captured tab the "Split screen" item exists
    // but is DISABLED; dismissing the menu leaves the guest docked full-width and
    // produces no SPLIT[ log line at all.
    // -------------------------------------------------------------------------
    private static void SplitSingleDisabled(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SSD", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked full-width at capture");

        long off = TabDockLog.RecordLogLength();
        AutomationElement? tab = FindTabText(container, pig.Title, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{pig.Title}' not found uniquely (count={count}).");
        (int tx, int ty) = Uia.Center(tab);
        if (!EnsureClickable(container, tx, ty))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        Input.RightClickAt(tx, ty);

        bool? enabled = Uia.IsMenuItemEnabled(ctx.TabDockPid, "Split screen");
        ctx.Check(enabled == false, "'Split screen' menu item is DISABLED with a single tab");
        if (enabled == null)
            throw new InvalidOperationException("'Split screen' menu item did not appear within the enabled-check window.");

        // Dismiss the menu without invoking anything.
        Input.SendKey(Input.VK_ESCAPE);
        Thread.Sleep(300);

        ctx.Check(IsDocked(pig.Hwnd, host), "guest still docked full-width after dismissing the menu");
        ctx.Check(TabDockLog.CountNewLines(off, "SPLIT[") == 0, "no SPLIT[ log line appeared (no split entered)");
    }

    // -------------------------------------------------------------------------
    // split-two-auto: exactly two tabs; the direct "Split screen" action auto-selects
    // the sole other tab as RIGHT. Both stay visible in their panes, tab count 2.
    // -------------------------------------------------------------------------
    private static void SplitTwoAuto(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "STA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "STB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        long off = EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[enter]", 3000), "SPLIT[enter] logged");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-two-auto");
        ctx.Check(TabCount(container) == 2, "tab count still 2 (both captured, none released)");
    }

    // -------------------------------------------------------------------------
    // split-select-partner: three tabs; the submenu partner picker puts the chosen
    // third tab (C) in the RIGHT pane and hides the unselected B.
    // -------------------------------------------------------------------------
    private static void SplitSelectPartner(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SSPA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SSPB", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "SSPC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long off = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigC.Title);
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[enter]", 3000), "SPLIT[enter] logged");
        AssertSplitPanes(ctx, host, pigA, pigC, "split-select-partner");
        ctx.Check(IsReleasedAndHidden(pigB.Hwnd), $"'{pigB.Title}' hidden (non-member of the split pair)");
    }

    // -------------------------------------------------------------------------
    // split-exit: "Exit split screen" returns to single-visible-guest: one member
    // full-width, the other hidden, tab count still 2, neither released.
    // -------------------------------------------------------------------------
    private static void SplitExit(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SEXA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SEXB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        long enterOff = EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "split entered");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-exit enter");

        long exitOff = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pigA.Title, "Exit split screen");
        ctx.Check(TabDockLog.WaitForLogLine(exitOff, "SPLIT[exit]", 3000), "SPLIT[exit] logged");

        bool aFull = IsDocked(pigA.Hwnd, host);
        bool bFull = IsDocked(pigB.Hwnd, host);
        ctx.Check(aFull != bFull, "exactly one guest is full-width after 'Exit split screen'");
        ctx.Check((aFull && IsReleasedAndHidden(pigB.Hwnd)) || (bFull && IsReleasedAndHidden(pigA.Hwnd)),
            "the non-surviving member is hidden after exit split");
        ctx.Check(!IsReleasedAndShown(pigA.Hwnd, host) && !IsReleasedAndShown(pigB.Hwnd, host),
            "neither guest was released by 'Exit split screen' (membership preserved)");
        ctx.Check(TabCount(container) == 2, "tab count still 2 after exit split");
    }

    // -------------------------------------------------------------------------
    // split-resize: maximizing then restoring the container keeps both members glued
    // to their (recomputed) panes.
    // -------------------------------------------------------------------------
    private static void SplitResize(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SRZA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SRZB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-resize enter");

        ClickMaximizeButton(container);
        Thread.Sleep(1500);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-resize after-maximize");

        NativeMethods.ShowWindow(container, NativeMethods.SW_RESTORE);
        Thread.Sleep(1500);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-resize after-restore");
    }

    // -------------------------------------------------------------------------
    // split-move: dragging the container's caption bar by a small delta keeps both
    // members glued to their panes. NOTE: the harness's real-input caption drag is the
    // least deterministic of these scenarios (WindowChrome caption hit-testing under
    // synthetic input); implemented as best-effort and flagged for the reviewer.
    // -------------------------------------------------------------------------
    private static void SplitMove(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SMVA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SMVB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-move enter");

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        int sx = rc.left + rc.Width / 2;
        int sy = rc.top + 16; // within the WindowChrome CaptionHeight=32 caption region
        if (!EnsureClickable(container, sx, sy))
            throw new InvalidOperationException("Could not bring the container to the foreground and its caption is obscured — refusing to drag blind.");
        GuardedProc.Log($"  split-move: dragging container caption from ({sx},{sy}) by (+40,+30).");
        Input.DragFromTo(sx, sy, sx + 40, sy + 30, 12);
        Thread.Sleep(800);

        // Assert the container actually moved (otherwise the "panes still glued"
        // check below would pass vacuously on a no-op drag).
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcAfter);
        ctx.Check(Math.Abs(rcAfter.left - rc.left) > 4 || Math.Abs(rcAfter.top - rc.top) > 4,
            "container actually moved during the caption drag");

        AssertSplitPanes(ctx, host, pigA, pigB, "split-move after-move");
    }

    // -------------------------------------------------------------------------
    // split-minrestore: minimizing the container hides BOTH members; restoring shows
    // both back in their panes, split still active.
    // -------------------------------------------------------------------------
    private static void SplitMinRestore(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SMRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SMRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-minrestore enter");

        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd) && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
            "both split members hidden after the container minimize");

        NativeMethods.ShowWindow(container, NativeMethods.SW_RESTORE);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-minrestore after-restore");
        ctx.Check(TabCount(container) == 2, "tab count still 2 after minimize/restore");
    }

    // -------------------------------------------------------------------------
    // split-reorder: reordering tabs (3rd pig C present, split A/B) must not change
    // the split pair identity — A stays LEFT and B stays RIGHT regardless of tab order.
    // -------------------------------------------------------------------------
    private static void SplitReorder(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SRDA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SRDB", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "SRDC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long off = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[enter]", 3000), "split entered (A left, B right, C present)");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-reorder enter");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to drag blind.");

        // Reorder: drag the RIGHTMOST of the two members' tabs into the left half of
        // the LEFTMOST one (same technique as the dragreorder scenario).
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabA == null || cA != 1 || tabB == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (A={cA}, B={cB}).");
        Rect rA = Uia.GetElementRect(tabA);
        Rect rB = Uia.GetElementRect(tabB);
        bool aIsRight = rA.X > rB.X;
        Rect leftRect = aIsRight ? rB : rA;
        // Record the log offset right before the drag so the reorder assertion
        // proves THIS drag reordered (ctx.LogOffset is per-run, not per-scenario,
        // so it could be satisfied by an earlier scenario's reorder).
        long reorderOff = TabDockLog.RecordLogLength();
        (int sx, int sy) = Uia.Center(aIsRight ? tabA : tabB);
        Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabDockLog.CountNewLines(reorderOff, "Reordered tab") >= 1, "a reorder was applied (log)");
        ctx.Check(TabCount(container) == 3, "tab count still 3 after reorder");

        // The split pair references the captured-window identity, not the tab index:
        // A must still cover LEFT, B still RIGHT after the tab order changed.
        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true), 3000),
            $"'{pigA.Title}' STILL in LEFT pane after reorder (split identity survives)");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigB.Hwnd, host, false), 3000),
            $"'{pigB.Title}' STILL in RIGHT pane after reorder (split identity survives)");
    }

    // -------------------------------------------------------------------------
    // split-popout-left: popping the LEFT member out releases it and promotes the
    // RIGHT member to full-width; the split terminates (SPLIT[member-gone]).
    // -------------------------------------------------------------------------
    private static void SplitPopoutLeft(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SPLA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SPLB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-popout-left enter");

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pigA.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 5000),
            $"'{pigA.Title}' (LEFT) popped out and shown standalone");
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigB.Hwnd, host, out _), 3000),
            $"'{pigB.Title}' (RIGHT) is full-width after the LEFT member popped out");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[member-gone]", 3000), "SPLIT[member-gone] logged");
        ctx.Check(TabCount(container) == 1, "1 tab remains after the LEFT member popped out");
    }

    // -------------------------------------------------------------------------
    // split-popout-right: popping the RIGHT member out releases it and promotes the
    // LEFT member to full-width; the split terminates.
    // -------------------------------------------------------------------------
    private static void SplitPopoutRight(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SPRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SPRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-popout-right enter");

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pigB.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigB.Hwnd, host), 5000),
            $"'{pigB.Title}' (RIGHT) popped out and shown standalone");
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigA.Hwnd, host, out _), 3000),
            $"'{pigA.Title}' (LEFT) is full-width after the RIGHT member popped out");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[member-gone]", 3000), "SPLIT[member-gone] logged");
        ctx.Check(TabCount(container) == 1, "1 tab remains after the RIGHT member popped out");
    }

    // -------------------------------------------------------------------------
    // split-selfclose: a split member self-closes (--self-close-after, in SECONDS);
    // its window is destroyed, the split terminates, and the surviving member takes
    // full width with no stale split state.
    // -------------------------------------------------------------------------
    private static void SplitSelfClose(Ctx ctx, Options opt)
    {
        // --self-close-after is in SECONDS (PigForm multiplies by 1000). 10s keeps the
        // close from firing during the ~6s real-input capture + split-setup flow while
        // still timing out well within the run budget. The log offset is recorded
        // BEFORE entering the split so the SPLIT[member-gone] line stays in scope.
        GuestInfo pigA = SpawnPig(ctx, "SSCA", "--self-close-after", "10", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SSCB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        long off = TabDockLog.RecordLogLength();
        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-selfclose enter");

        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(pigA.Hwnd), 20000),
            $"'{pigA.Title}' window destroyed by self-close");
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigB.Hwnd, host, out _), 5000),
            $"'{pigB.Title}' is full-width after the LEFT member self-closed");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[member-gone]", 3000), "SPLIT[member-gone] logged");
        ctx.Check(pigB.Proc != null && !pigB.Proc.HasExited, "surviving member process alive after self-close");
    }

    // -------------------------------------------------------------------------
    // split-native-move-reassert: a real title-bar drag on a split member is
    // re-glued to its pane and does not release the tab.
    // -------------------------------------------------------------------------
    private static void SplitNativeMoveReassert(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "STDA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "STDB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-native-move-reassert enter");

        NativeMethods.GetWindowRect(pigA.Hwnd, out NativeMethods.RECT aRect);
        int titleX = aRect.left + aRect.Width / 3;
        int titleY = aRect.top + 15;
        if (!Input.ForceForeground(pigA.Hwnd))
            throw new InvalidOperationException("Could not bring the split member to the foreground — refusing to drag blind.");
        Thread.Sleep(700); // settle past any double-click timing window

        long off = TabDockLog.RecordLogLength();
        Input.DragFromTo(titleX, titleY, titleX + 180, titleY + 150, 14);

        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true), 5000),
            $"'{pigA.Title}' is re-glued to LEFT after native title movement");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigB.Hwnd, host, false), 3000),
            $"'{pigB.Title}' remains in RIGHT after native title movement");
        ctx.Check(TabCount(container) == 2, "both tabs remain captured after native title movement");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SHEPHERD[re-glue]", 3000), "SHEPHERD[re-glue] logged");
        ctx.Check(TabDockLog.CountNewLines(off, "SPLIT[member-gone]") == 0, "split remains active");
    }

    // split-native-resize-reassert: a native edge resize cannot leave a split
    // member outside its assigned pane.
    private static void SplitNativeResizeReassert(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SNRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SNRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-native-resize-reassert enter");

        NativeMethods.GetWindowRect(pigA.Hwnd, out NativeMethods.RECT aRect);
        if (!Input.ForceForeground(pigA.Hwnd))
            throw new InvalidOperationException("Could not foreground the left split member — refusing to resize blind.");
        long off = TabDockLog.RecordLogLength();
        Input.DragFromTo(aRect.right - 2, aRect.top + aRect.Height / 2,
            aRect.right + 80, aRect.top + aRect.Height / 2, 12);

        AssertSplitPanes(ctx, host, pigA, pigB, "split-native-resize-reassert after-resize");
        ctx.Check(TabCount(container) == 2, "both tabs remain captured after native resize");
        ctx.Check(TabDockLog.WaitForLogLine(off, "SHEPHERD[re-glue]", 3000), "resize re-glue logged");
    }

    private static void SplitContextMenuRenderStability(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SCSA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SCSB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-contextmenu-render-stability enter");

        for (int cycle = 0; cycle < 5; cycle++)
        {
            DismissTabContextMenu(ctx, container, pigA.Title);
            AssertSplitPanes(ctx, host, pigA, pigB, $"split context cycle {cycle + 1} after LEFT menu");
            DismissTabContextMenu(ctx, container, pigB.Title);
            AssertSplitPanes(ctx, host, pigA, pigB, $"split context cycle {cycle + 1} after RIGHT menu");
        }
    }

    private static void ContextMenuRenderStability(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CMS", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        for (int cycle = 0; cycle < 6; cycle++)
        {
            DismissTabContextMenu(ctx, container, pig.Title);
            ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host) && NativeMethods.IsWindowVisible(pig.Hwnd), 2000),
                $"context cycle {cycle + 1}: guest remains visible and docked");
        }
    }

    private static void ChromeClickRenderStability(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CCS", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        AutomationElement? tab = FindTabText(container, pig.Title, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException("Tab not found for chrome stability scenario.");
        (int x, int y) = Uia.Center(tab);
        for (int cycle = 0; cycle < 8; cycle++)
        {
            Input.ClickAt(x, y);
            ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host) && NativeMethods.IsWindowVisible(pig.Hwnd), 2000),
                $"chrome/tab click cycle {cycle + 1}: guest remains rendered/docked");
        }
    }

    private static void TabCloseButtonPopout(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "XPO", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ClickTabCloseButton(ctx, container, pig.Title);
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pig.Hwnd, host), 5000),
            "tab X pops the guest out and leaves it visible");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "tab X leaves the external process alive");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "last tab X empties/closes the container");
    }

    private static void TabMiddleClickPopout(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "MPOA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "MPOB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        AutomationElement? tab = FindTabText(container, pigA.Title, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException("Middle-click target tab not found uniquely.");
        (int x, int y) = Uia.Center(tab);
        Input.MiddleClickAt(x, y);
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 5000),
            "middle-click pops the tab out visibly");
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigB.Hwnd, host, out _), 3000),
            "the surviving tab expands to the full content rect");
        ctx.Check(TabCount(container) == 1, "one tab remains after middle-click pop-out");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited, "middle-click leaves the external process alive");
    }

    private static void SplitCloseButtonLeft(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "XSA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "XSB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-closebutton-left enter");
        ClickTabCloseButton(ctx, container, pigA.Title);
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigB.Hwnd, host, out _), 5000),
            "RIGHT survivor expands after LEFT tab X");
        ctx.Check(TabCount(container) == 1, "LEFT tab X leaves one captured tab");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited, "LEFT tab X leaves the process alive");
    }

    private static void SplitCloseButtonRight(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "XRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "XRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-closebutton-right enter");
        ClickTabCloseButton(ctx, container, pigB.Title);
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigA.Hwnd, host, out _), 5000),
            "LEFT survivor expands after RIGHT tab X");
        ctx.Check(TabCount(container) == 1, "RIGHT tab X leaves one captured tab");
        ctx.Check(pigB.Proc != null && !pigB.Proc.HasExited, "RIGHT tab X leaves the process alive");
    }

    // -------------------------------------------------------------------------
    // split-click-third: clicking a NON-paired tab (C) while A/B are split exits the
    // split and makes C the single full-width guest; A and B are hidden.
    // -------------------------------------------------------------------------
    private static void SplitClickThird(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SCTA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SCTB", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "SCTC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "split entered (A/B, C present)");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-click-third enter");

        // Plain click on C's tab header (a non-paired tab) -> exits split.
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        AutomationElement? tabC = FindTabText(container, pigC.Title, out int count);
        if (tabC == null || count != 1)
            throw new InvalidOperationException($"Tab for '{pigC.Title}' not found uniquely (count={count}).");
        (int tx, int ty) = Uia.Center(tabC);
        long clickOff = TabDockLog.RecordLogLength();
        Input.ClickAt(tx, ty);

        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigC.Hwnd, host, out _), 3000),
            $"'{pigC.Title}' is full-width after clicking it (split exited)");
        ctx.Check(TabDockLog.WaitForLogLine(clickOff, "SPLIT[exit]", 3000), "SPLIT[exit] logged on non-paired tab click");
        ctx.Check(IsReleasedAndHidden(pigA.Hwnd) && IsReleasedAndHidden(pigB.Hwnd),
            "both former split members hidden after clicking the non-paired tab");
        ctx.Check(TabCount(container) == 3, "tab count still 3 after clicking the third tab");
    }

    // -------------------------------------------------------------------------
    // split-directclick: both members carry a --text-box; clicking directly into each
    // member's own pane and typing must deliver input to THAT member, and both must
    // remain glued in their panes (input does not disturb the split).
    // -------------------------------------------------------------------------
    private static void SplitDirectClick(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SDCA", "--color", "red", "--text-box");
        GuestInfo pigB = SpawnPig(ctx, "SDCB", "--color", "blue", "--text-box");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-directclick enter");

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int cy = hostRect.top + hostRect.Height / 2;
        int leftCx = hostRect.left + hostRect.Width / 4;      // center of the LEFT pane
        int rightCx = hostRect.left + 3 * hostRect.Width / 4; // center of the RIGHT pane

        // Click into A's (left) text box and type.
        Input.ClickAt(leftCx, cy);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000),
            "'A' became the real foreground window from the direct click into the LEFT pane");
        Thread.Sleep(300);
        Input.TypeText("LEFTA");
        ctx.Check(PigLog.WaitForPigLine(pigA.Pid, "TEXTBOX text='LEFTA'", 3000),
            $"'A' text box received 'LEFTA' (input delivered to the LEFT pane member)");

        // Click into B's (right) text box and type.
        Input.ClickAt(rightCx, cy);
        Thread.Sleep(300);
        Input.TypeText("RIGHTB");
        ctx.Check(PigLog.WaitForPigLine(pigB.Pid, "TEXTBOX text='RIGHTB'", 3000),
            $"'B' text box received 'RIGHTB' (input delivered to the RIGHT pane member)");

        AssertSplitPanes(ctx, host, pigA, pigB, "split-directclick after-input");
    }

    // -------------------------------------------------------------------------
    // split-repeat-cycles: repeatedly enter split (both panes), exit split (one
    // full-width, the other hidden), asserting no EXCEPTION and no stale split state
    // per cycle.
    // -------------------------------------------------------------------------
    private static void SplitRepeatCycles(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 5;
        GuestInfo pigA = SpawnPig(ctx, "SRCA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SRCB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- split-repeat-cycles {cycle}/{cycles} ---");
            long cycOff = TabDockLog.RecordLogLength();

            long enterOff = EnterSplitTwo(ctx, container, pigA);
            ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), $"cycle {cycle}: SPLIT[enter] logged");
            AssertSplitPanes(ctx, host, pigA, pigB, $"cycle {cycle} enter");

            long exitOff = TabDockLog.RecordLogLength();
            ClickTabMenuItem(ctx, container, pigA.Title, "Exit split screen");
            ctx.Check(TabDockLog.WaitForLogLine(exitOff, "SPLIT[exit]", 3000), $"cycle {cycle}: SPLIT[exit] logged");
            ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host) || IsDocked(pigB.Hwnd, host), 3000),
                $"cycle {cycle}: one guest full-width after exit split");
            ctx.Check(TabCount(container) == 2, $"cycle {cycle}: tab count still 2");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "EXCEPTION") == 0, $"cycle {cycle}: no EXCEPTION lines since cycle start");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines across all cycles");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "both pigs alive across all cycles");
    }

    private static void CaptureInlineUi(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CIUA", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA);
        GuestInfo pigB = SpawnPig(ctx, "CIUB", "--color", "blue");

        ClickAddWindowButton(container);
        Thread.Sleep(400);
        ctx.Check(Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true)
            .All(hwnd => NativeMethods.GetWindowTextString(hwnd) != "Capture windows"),
            "Add App opens capture inline without a CapturePickerWindow");

        AutomationElement? root = Uia.FromHwnd(container);
        if (root == null)
            throw new InvalidOperationException("Container UIA root unavailable while inline capture is open.");
        AutomationElement? targetText = Uia.FindDescendantByName(root, ControlType.Text, null, pigB.Title, out int textCount);
        if (targetText == null || textCount != 1)
            throw new InvalidOperationException($"Inline capture target '{pigB.Title}' not found uniquely (count={textCount}).");
        AutomationElement? checkBox = Uia.NearestAncestorOfType(targetText, ControlType.CheckBox) ?? targetText;
        (int cx, int cy) = Uia.Center(checkBox);
        Input.ClickAt(cx, cy);

        AutomationElement? add = Uia.FindDescendantByName(root, ControlType.Button, "Add selected", null, out int addCount);
        if (add == null || addCount != 1)
            throw new InvalidOperationException($"Inline 'Add selected' button not found uniquely (count={addCount}).");
        (int ax, int ay) = Uia.Center(add);
        Input.ClickAt(ax, ay);

        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 5000), "inline capture adds the selected guest as a second tab");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 5000), "inline-captured guest is docked in the content rect");
    }

    private static void GroupCreateInline(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "GCIN", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        AutomationElement? root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable for group menu.");
        AutomationElement? groupButton = Uia.FindDescendantByName(root, ControlType.Button, "Group ▾", null, out int buttonCount);
        if (groupButton == null || buttonCount != 1)
            throw new InvalidOperationException($"Group selector button not found uniquely (count={buttonCount}).");

        (int x, int y) = Uia.Center(groupButton);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Group selector was obscured — refusing to click blind.");
        Input.ClickAt(x, y);
        AutomationElement? newGroup = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "+ New group", 3000);
        if (newGroup == null)
            throw new InvalidOperationException("Group menu did not expose '+ New group'.");
        (int nx, int ny) = Uia.Center(newGroup);
        Input.ClickAt(nx, ny);

        IntPtr secondContainer = IntPtr.Zero;
        ctx.Check(Util.WaitUntil(() =>
        {
            foreach (IntPtr hwnd in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
            {
                if (hwnd != container && Discover.FindChildByClass(hwnd, ContentHostClass) != IntPtr.Zero)
                {
                    secondContainer = hwnd;
                    return true;
                }
            }
            return false;
        }, 5000), "new group opens a second tabbed shell from the in-window group menu");
        ctx.Check(!NativeMethods.IsWindowVisible(ctx.MainHwnd),
            "launcher is hidden once a tabbed shell exists");
        ctx.Check(NativeMethods.IsWindowVisible(pig.Hwnd) && IsDocked(pig.Hwnd, host),
            "creating a group does not disturb the existing guest");

        if (secondContainer != IntPtr.Zero && NativeMethods.IsWindow(secondContainer))
            NativeMethods.PostMessage(secondContainer, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
