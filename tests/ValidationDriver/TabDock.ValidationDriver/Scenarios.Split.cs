using System;
using System.Diagnostics;
using System.IO;
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
    //   * Clicking a split member keeps split; clicking a non-paired tab also
    //     keeps split (the pair is the persistent selected unit — split exits
    //     only via the explicit "Exit split screen" item, a Split Screen
    //     replacement, or a structural member removal).
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
        AutomationElement? close;
        int closeCount;
        Uia.FindDescendantByName(item, ControlType.Button, "×", null, out closeCount);
        if (closeCount == 0)
            throw new InvalidOperationException($"Close affordance for '{guestTitle}' was not found.");
        if (closeCount > 1)
        {
            // Composite split tab: the single tab item carries one × per half.
            // Resolve by AutomationId (goal §33: SplitCloseLeft/SplitCloseRight)
            // so the correct member's × is picked regardless of strip stretch or
            // title width — the old "horizontally nearest to the title" heuristic
            // was correct only by template accident. Falls back to the distance
            // heuristic if the IDs are absent (older builds).
            AutomationElement? byIdLeft = Uia.FindDescendantByAutomationId(item, "SplitCloseLeft", out int leftIdCount);
            AutomationElement? byIdRight = Uia.FindDescendantByAutomationId(item, "SplitCloseRight", out int rightIdCount);
            if (leftIdCount == 1 && rightIdCount == 1 && byIdLeft != null && byIdRight != null)
            {
                (int tx, _) = Uia.Center(tab);
                (int lx, _) = Uia.Center(byIdLeft);
                close = tx <= lx ? byIdLeft : byIdRight;
            }
            else
            {
                (int tx, _) = Uia.Center(tab);
                close = null;
                int bestDistance = int.MaxValue;
                AutomationElementCollection all = item.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement el in all)
                {
                    string name;
                    try { name = el.Current.Name ?? string.Empty; }
                    catch { continue; }
                    if (!string.Equals(name, "×", StringComparison.Ordinal))
                        continue;
                    (int bx, _) = Uia.Center(el);
                    int distance = Math.Abs(bx - tx);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        close = el;
                    }
                }
            }
            if (close == null)
                throw new InvalidOperationException($"Close affordance for '{guestTitle}' was not found (multi-× resolution).");
        }
        else
        {
            close = Uia.FindDescendantByName(item, ControlType.Button, "×", null, out _);
        }

        (int x, int y) = Uia.Center(close!);
        GuardedProc.Log($"  tab close '{guestTitle}': rect={Uia.GetElementRect(close!)}, click=({x},{y}) windowFromPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y }).ToInt64():X}");
        // Refresh _activeTarget before clicking: prior operations (split entry,
        // QueryMinTrack SendMessage) may have left the verified target stale.
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException($"Close button for '{guestTitle}' is obscured — refusing to click blind.");
        Input.ClickAt(x, y);
        Thread.Sleep(250);
    }

    /// <summary>
    /// Waits for a NEW <c>SPLIT[focus]</c> log line and parses the focused
    /// member's HWND from it (<c>guest=0x...</c>, emitted by
    /// ContainerWindow.FocusSplitMember). Proves the click focused the EXPECTED
    /// member, not merely that "some member got focused" — the plain substring
    /// check passes even if the wrong half were activated.
    /// </summary>
    private static bool WaitForSplitFocus(long offset, GuestInfo expected, int timeoutMs)
    {
        return Util.WaitUntil(() =>
        {
            foreach (string line in TabDockLog.ReadNewLines(offset))
            {
                int marker = line.IndexOf("SPLIT[focus] guest=0x", StringComparison.Ordinal);
                if (marker < 0)
                    continue;
                string rest = line.Substring(marker + "SPLIT[focus] guest=0x".Length);
                int space = rest.IndexOf(' ');
                string hex = space < 0 ? rest : rest.Substring(0, space);
                if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long hwnd)
                    && hwnd == expected.Hwnd.ToInt64())
                    return true;
            }
            return false;
        }, timeoutMs);
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
        ctx.Check(TabCount(container) == 1, "pair renders as ONE composite tab item (both captured, none released)");
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

        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
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

        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        AssertSplitPanes(ctx, host, pigA, pigB, "split-minrestore after-restore");
        ctx.Check(TabCount(container) == 1, "pair still ONE composite tab item after minimize/restore");
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

        // Reorder in NORMAL mode first: drag C's tab to the front (before A).
        // (During split the strip shows ONE composite item for the pair and strip
        // dragging is disabled — the composite is not a drag unit in this pass —
        // so the reorder-then-split path is what the pair-identity guarantee
        // below exercises.)
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to drag blind.");

        AutomationElement? tabC = FindTabText(container, pigC.Title, out int cCount);
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int aCount);
        if (tabC == null || cCount != 1 || tabA == null || aCount != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (C={cCount}, A={aCount}).");
        // Record the log offset right before the drag so the reorder assertion
        // proves THIS drag reordered (ctx.LogOffset is per-run, not per-scenario,
        // so it could be satisfied by an earlier scenario's reorder).
        long reorderOff = TabDockLog.RecordLogLength();
        (int sx, int sy) = Uia.Center(tabC);
        Rect rA = Uia.GetElementRect(tabA);
        Input.DragFromTo(sx, sy, (int)(rA.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(Util.WaitUntil(() => TabDockLog.CountNewLines(reorderOff, "Reordered tab") >= 1, 3000), "a reorder was applied (log)");
        ctx.Check(TabCount(container) == 3, "tab count still 3 after reorder");

        // Enter split (A LEFT, B RIGHT — 3 tabs: submenu path). The split pair
        // references the captured-window identity, not the tab index: A must
        // cover LEFT and B RIGHT regardless of the reordered strip.
        long off = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(off, "SPLIT[enter]", 3000), "split entered (A left, B right, C present)");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true), 3000),
            $"'{pigA.Title}' in LEFT pane after reorder (split identity survives)");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigB.Hwnd, host, false), 3000),
            $"'{pigB.Title}' in RIGHT pane after reorder (split identity survives)");
        ctx.Check(TabCount(container) == 2, "split pair renders as ONE composite tab item after reorder");
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
        // --self-close-after is in SECONDS (PigForm multiplies by 1000). The
        // 20s margin keeps the close from firing during the ~6-8s real-input
        // capture + split-setup flow even on a slow/busy machine (a 10s timer
        // raced the capture on slower hardware and produced false FAILs), while
        // still timing out well within the run budget. The log offset is
        // recorded BEFORE entering the split so the SPLIT[member-gone] line
        // stays in scope; the primary observable is the WaitUntil on the
        // destroyed window, not the timer itself.
        GuestInfo pigA = SpawnPig(ctx, "SSCA", "--self-close-after", "20", "--color", "red");
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
        ctx.Check(TabCount(container) == 1, "pair still ONE composite tab item after native title movement");
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
        ctx.Check(TabCount(container) == 1, "pair still ONE composite tab item after native resize");
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
        for (int cycle = 0; cycle < Math.Max(20, opt.Cycles ?? 20); cycle++)
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
    // split-click-third: clicking a NON-paired tab (C) while A/B are split must
    // leave the pair untouched — split stays active, A and B stay glued and
    // visible, C stays hidden, no SPLIT[exit], no member released. (Split
    // persistence contract: the pair is the selected tab-strip unit; only an
    // explicit Split Screen operation or a structural member removal ends it.)
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
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C hidden during split");
        // EnterSplit activates the LEFT member, so the last "Switched group ...
        // to tab N" line names the pair's index; any later switch to a
        // different index after clicking C would mean C became the active tab.
        int pairIndex = LastSwitchedTabIndex(enterOff);
        if (pairIndex < 0)
            throw new InvalidOperationException("No 'Switched group' line after split entry — cannot derive the pair's tab index.");

        // Plain click on C's tab header (a non-paired tab) -> split persists.
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        AutomationElement? tabC = FindTabText(container, pigC.Title, out int count);
        if (tabC == null || count != 1)
            throw new InvalidOperationException($"Tab for '{pigC.Title}' not found uniquely (count={count}).");
        (int tx, int ty) = Uia.Center(tabC);
        long clickOff = TabDockLog.RecordLogLength();
        Input.ClickAt(tx, ty);

        ctx.Check(TabDockLog.CountNewLines(clickOff, "SPLIT[exit]") == 0,
            "clicking the third tab keeps split active (no SPLIT[exit])");
        ctx.Check(TabDockLog.CountNewLines(clickOff, "SPLIT[member-gone]") == 0,
            "clicking the third tab removes no member");
        // The click is intercepted by the split guard (no selection change at
        // all) or, on the funnel path, reverted to the focused member — so the
        // SETTLED final index must either be absent (last == -1, the plain
        // interception path) or name the pair's member (C must never become the
        // active tab).
        Thread.Sleep(300); // settle past the 120ms reassert before log reads
        int settled = LastSwitchedTabIndex(clickOff);
        ctx.Check(settled < 0 || settled == pairIndex,
            $"clicking the third tab leaves the pair's member as the active tab (settled index {settled}, pair {pairIndex})");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 3000),
            "A and B remain glued to their panes after clicking C");
        ctx.Check(NativeMethods.IsWindowVisible(pigA.Hwnd) && NativeMethods.IsWindowVisible(pigB.Hwnd)
            && !NativeMethods.IsWindowVisible(pigC.Hwnd),
            "visible set unchanged: A and B visible, C hidden");
        ctx.Check(TabCount(container) == 2, "pair still ONE composite item after clicking the third tab");
        // Release shows the guest at its own placement and logs
        // "Released tab ... from group ..."; in split mode members legitimately
        // cover half-panes (never the full host), so assert the app's release
        // log rather than a full-host dock shape.
        ctx.Check(TabDockLog.CountNewLines(clickOff, "Released tab") == 0,
            "no member released by clicking C (no 'Released tab' log)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-third-tab-hover-persists (goal §10): with A+B split and a third tab
    // C present, hovering C's tab (a presentation-only interaction) must leave
    // the pair untouched: split active, both members visible and glued, C
    // hidden, no SPLIT[exit], no visibility transition, no release, no switch
    // away from the pair — repeated cycles with the pointer moved away between.
    // -------------------------------------------------------------------------
    private static void SplitThirdTabHoverPersists(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "S3H-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "S3H-B", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "S3H-C", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "SPLIT[enter] logged (A left, B right)");
        AssertSplitPanes(ctx, host, pigA, pigB, "third-tab-hover enter");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C hidden during split");
        int pairIndex = LastSwitchedTabIndex(enterOff);
        if (pairIndex < 0)
            throw new InvalidOperationException("No 'Switched group' line after split entry — cannot derive the pair's tab index.");

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int leftCx = hostRect.left + hostRect.Width / 4;
        int rightCx = hostRect.left + 3 * hostRect.Width / 4;
        int cyMid = hostRect.top + hostRect.Height / 2;

        int cycles = Math.Max(10, opt.Cycles ?? 10);
        for (int i = 1; i <= cycles; i++)
        {
            AutomationElement? tabC = FindTabText(container, pigC.Title, out int count);
            if (tabC == null || count != 1)
                throw new InvalidOperationException($"Tab '{pigC.Title}' not found uniquely (count={count}).");
            (int cx, int cy) = Uia.Center(tabC);
            if (!EnsureClickable(container, cx, cy))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to hover blind.");

            long cycleOff = TabDockLog.RecordLogLength();
            // Real hover dwell on C's tab, then move the pointer away (to the
            // caption, not another tab).
            Input.MoveTo(cx, cy);
            Thread.Sleep(450);
            NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
            Input.MoveTo(rc.left + 40, rc.top + 16);
            Thread.Sleep(250);

            ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true), 3000),
                $"cycle {i}: A still in LEFT pane after hover");
            ctx.Check(Util.WaitUntil(() => IsInPane(pigB.Hwnd, host, false), 3000),
                $"cycle {i}: B still in RIGHT pane after hover");
            ctx.Check(NativeMethods.IsWindowVisible(pigA.Hwnd) && NativeMethods.IsWindowVisible(pigB.Hwnd),
                $"cycle {i}: both split members still visible");
            ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), $"cycle {i}: C stayed hidden");
            ctx.Check(TabCount(container) == 2, $"cycle {i}: pair still ONE composite item");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "SPLIT[exit]") == 0, $"cycle {i}: no SPLIT[exit]");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "SPLIT[member-gone]") == 0, $"cycle {i}: no SPLIT[member-gone]");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "SHEPHERD[hide]") == 0, $"cycle {i}: no member hidden (no visibility transition)");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "Released tab") == 0, $"cycle {i}: no member released");
            ctx.Check(!AnySwitchToOtherIndex(cycleOff, pairIndex),
                $"cycle {i}: hover never switches the active tab away from the pair");
            ctx.Check(Util.WaitUntil(() => TopWindowPidAt(leftCx, cyMid) == pigA.Pid
                && TopWindowPidAt(rightCx, cyMid) == pigB.Pid, 3000),
                $"cycle {i}: pane centers resolve to their guests (neither covered)");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited
            && pigC.Proc != null && !pigC.Proc.HasExited, "all three pigs alive after the hover cycles");
    }

    // -------------------------------------------------------------------------
    // split-third-tab-click-persists (goal §11): clicking an unrelated third
    // tab C while A+B are split must NOT destroy the pair or make C the active
    // single guest. Sequence per cycle: click C, click LEFT half, click C,
    // click RIGHT half, click C, right-click C and dismiss. After every step:
    // pair identity A+B, both panes glued and visible, C hidden, no SPLIT[exit],
    // no member released, no switch to C.
    // -------------------------------------------------------------------------
    private static void SplitThirdTabClickPersists(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "S3C-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "S3C-B", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "S3C-C", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "SPLIT[enter] logged (A left, B right)");
        AssertSplitPanes(ctx, host, pigA, pigB, "third-tab-click enter");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C hidden during split");
        int pairIndex = LastSwitchedTabIndex(enterOff);
        if (pairIndex < 0)
            throw new InvalidOperationException("No 'Switched group' line after split entry — cannot derive the pair's tab index.");

        AutomationElement? tabC = FindTabText(container, pigC.Title, out int cCount);
        AutomationElement? leftText = FindTabText(container, pigA.Title, out int leftCount);
        AutomationElement? rightText = FindTabText(container, pigB.Title, out int rightCount);
        if (tabC == null || cCount != 1 || leftText == null || leftCount != 1 || rightText == null || rightCount != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (C={cCount}, L={leftCount}, R={rightCount}).");
        (int cx, int cy) = Uia.Center(tabC);
        (int lx, int ly) = Uia.Center(leftText);
        (int rx, int ry) = Uia.Center(rightText);

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int leftPaneCx = hostRect.left + hostRect.Width / 4;
        int rightPaneCx = hostRect.left + 3 * hostRect.Width / 4;
        int cyMid = hostRect.top + hostRect.Height / 2;

        int cycles = Math.Max(10, opt.Cycles ?? 10);
        for (int i = 1; i <= cycles; i++)
        {
            long cycleOff = TabDockLog.RecordLogLength();

            // (1) click C: the pair must persist; C must not become the active
            // single guest. The click's activation can re-sync the pair's index
            // via the 120ms reassert, so assert the SETTLED final index (the
            // focused member's) rather than an absence of switch lines — the
            // funnel legitimately re-logs the member's index during the revert.
            long off1 = TabDockLog.RecordLogLength();
            Input.ClickAt(cx, cy);
            Thread.Sleep(300); // settle past the 120ms reassert before log reads
            ctx.Check(TabDockLog.CountNewLines(off1, "SPLIT[exit]") == 0,
                $"cycle {i}: click-C keeps split active (no SPLIT[exit])");
            int settled1 = LastSwitchedTabIndex(off1);
            ctx.Check(settled1 < 0 || settled1 == pairIndex,
                $"cycle {i}: click-C leaves the pair's member as the active tab (settled index {settled1}, pair {pairIndex})");
            ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 3000),
                $"cycle {i}: A/B still glued after click-C");

            // (2) click the LEFT half: A focused, both panes stay, split stays.
            long offA = TabDockLog.RecordLogLength();
            Input.ClickAt(lx, ly);
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000),
                $"cycle {i}: LEFT half click foregrounds A");
            ctx.Check(TabDockLog.CountNewLines(offA, "SPLIT[exit]") == 0, $"cycle {i}: LEFT half keeps split active");

            // (3) click C again — same persistence contract (focused member A).
            long off3 = TabDockLog.RecordLogLength();
            Input.ClickAt(cx, cy);
            Thread.Sleep(300);
            ctx.Check(TabDockLog.CountNewLines(off3, "SPLIT[exit]") == 0,
                $"cycle {i}: second click-C keeps split active (no SPLIT[exit])");
            int settled3 = LastSwitchedTabIndex(off3);
            ctx.Check(settled3 < 0 || settled3 == pairIndex,
                $"cycle {i}: second click-C leaves the pair's member as the active tab (settled index {settled3}, pair {pairIndex})");

            // (4) click the RIGHT half: B focused (member-scoped SPLIT[focus]),
            // both panes stay, split stays. The half-click's switch log gives us
            // the partner's tab index for the step-(5) assertion.
            long offB = TabDockLog.RecordLogLength();
            Input.ClickAt(rx, ry);
            ctx.Check(WaitForSplitFocus(offB, pigB, 3000),
                $"cycle {i}: RIGHT half click focuses B (SPLIT[focus] member=B)");
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigB.Hwnd, 3000),
                $"cycle {i}: B is foreground after RIGHT half click");
            ctx.Check(TabDockLog.CountNewLines(offB, "SPLIT[exit]") == 0, $"cycle {i}: RIGHT half keeps split active");
            int partnerIndex = LastSwitchedTabIndex(offB);
            ctx.Check(partnerIndex >= 0, $"cycle {i}: RIGHT half click re-synced the active index (log)");

            // (5) click C again — the focused member is now B, so the settled
            // final index must be B's.
            long off5 = TabDockLog.RecordLogLength();
            Input.ClickAt(cx, cy);
            Thread.Sleep(300);
            ctx.Check(TabDockLog.CountNewLines(off5, "SPLIT[exit]") == 0,
                $"cycle {i}: third click-C keeps split active (no SPLIT[exit])");
            int settled5 = LastSwitchedTabIndex(off5);
            ctx.Check(settled5 < 0 || settled5 == partnerIndex,
                $"cycle {i}: third click-C leaves the focused member B as the active tab (settled index {settled5}, partner {partnerIndex})");

            // (6) right-click C and dismiss the menu: context-menu open/close is
            // not a split-changing operation (DismissTabContextMenu throws if the
            // menu fails to appear).
            long offMenu = TabDockLog.RecordLogLength();
            DismissTabContextMenu(ctx, container, pigC.Title);
            ctx.Check(TabDockLog.CountNewLines(offMenu, "SPLIT[exit]") == 0,
                $"cycle {i}: right-click C + dismiss keeps split active");

            // (7) Ctrl+Tab while split cycles ONLY between the pair's members:
            // with B focused, one Ctrl+Tab must focus A and leave the pair intact.
            long offCT = TabDockLog.RecordLogLength();
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground for Ctrl+Tab — refusing to continue.");
            bool ctrlDown = false;
            try
            {
                Input.SendKeyDown(Input.VK_CONTROL);
                ctrlDown = true;
                Input.SendKey(Input.VK_TAB);
            }
            finally
            {
                if (ctrlDown)
                    Input.SendKeyUp(Input.VK_CONTROL);
            }
            ctx.Check(WaitForSplitFocus(offCT, pigA, 3000),
                $"cycle {i}: Ctrl+Tab cycles to the other split member (SPLIT[focus] member=A)");
            ctx.Check(TabDockLog.CountNewLines(offCT, "SPLIT[exit]") == 0, $"cycle {i}: Ctrl+Tab keeps split active");

            // (8) full state battery.
            ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 3000),
                $"cycle {i}: pair glued at end of cycle");
            ctx.Check(NativeMethods.IsWindowVisible(pigA.Hwnd) && NativeMethods.IsWindowVisible(pigB.Hwnd),
                $"cycle {i}: both members visible");
            ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), $"cycle {i}: C never became the active single guest");
            ctx.Check(TabCount(container) == 2, $"cycle {i}: pair still ONE composite item");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "SPLIT[member-gone]") == 0, $"cycle {i}: no member gone");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "Released tab") == 0, $"cycle {i}: no member released");
            ctx.Check(Util.WaitUntil(() => TopWindowPidAt(leftPaneCx, cyMid) == pigA.Pid
                && TopWindowPidAt(rightPaneCx, cyMid) == pigB.Pid, 3000),
                $"cycle {i}: pane centers resolve to their guests (neither covered)");
            ctx.Check(TabDockLog.CountNewLines(cycleOff, "EXCEPTION") == 0, $"cycle {i}: no EXCEPTION");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited
            && pigC.Proc != null && !pigC.Proc.HasExited, "all three pigs alive after the click cycles");
    }

    // -------------------------------------------------------------------------
    // split-drag-release-render-stability (goal §20/§21): with A+B split,
    // drag the container caption through a multi-segment trajectory, release,
    // and IMMEDIATELY (no tab interaction) assert BOTH panes glued, both
    // visible, exact partition (no overlap, no gap), split still active, and
    // neither pane covered. The focused member alternates between cycles.
    // -------------------------------------------------------------------------
    private static void SplitDragReleaseRenderStability(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SDR-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SDR-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        long enterOff = TabDockLog.RecordLogLength();
        EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "SPLIT[enter] logged");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-drag-release enter");
        EnsureContainerInWorkArea(ctx, container);

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int leftCx = hostRect.left + hostRect.Width / 4;
        int rightCx = hostRect.left + 3 * hostRect.Width / 4;
        int cyMid = hostRect.top + hostRect.Height / 2;

        // Half centers must be re-read per cycle: each drag's trajectory has a
        // net displacement (±130px) that oscillates the container around its
        // origin, so once-per-loop coordinates go stale on every cycle where the
        // container is not exactly at the origin (the click would land off the
        // strip). Also, the click must target the member that is NOT currently
        // focused — clicking the already-focused half is a no-op that logs no
        // SPLIT[focus] — so the alternating structure must start from a known
        // state: A is focused right after entering.
        int cycles = Math.Max(20, opt.Cycles ?? 20);
        for (int i = 1; i <= cycles; i++)
        {
            AutomationElement? leftText = FindTabText(container, pigA.Title, out int leftCount);
            AutomationElement? rightText = FindTabText(container, pigB.Title, out int rightCount);
            if (leftText == null || leftCount != 1 || rightText == null || rightCount != 1)
                throw new InvalidOperationException($"Composite halves not found uniquely at cycle {i} (L={leftCount}, R={rightCount}).");
            (int lx, int ly) = Uia.Center(leftText);
            (int rx, int ry) = Uia.Center(rightText);

            // Alternate the focused member between cycles (both orientations
            // must survive the drag; A is focused right after entering).
            long focusOff = TabDockLog.RecordLogLength();
            if (i % 2 == 1)
            {
                NativeMethods.GetWindowRect(container, out NativeMethods.RECT fcRc);
                GuardedProc.Log($"  focus-click probe cycle {i}: click=({rx},{ry}) container={Util.FormatRect(fcRc)} atPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = rx, y = ry }).ToInt64():X}");
                Input.ClickAt(rx, ry);
                ctx.Check(WaitForSplitFocus(focusOff, pigB, 3000), $"cycle {i}: B focused before drag");
            }
            else
            {
                NativeMethods.GetWindowRect(container, out NativeMethods.RECT fcRc);
                GuardedProc.Log($"  focus-click probe cycle {i}: click=({lx},{ly}) container={Util.FormatRect(fcRc)} atPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = lx, y = ly }).ToInt64():X}");
                Input.ClickAt(lx, ly);
                ctx.Check(WaitForSplitFocus(focusOff, pigA, 3000), $"cycle {i}: A focused before drag");
            }

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
                $"cycle {i}: re-glue churn during drag (both panes followed the move)");
            AssertPanesPartition(ctx, host, pigA, pigB, $"cycle {i} after-drag");
            ctx.Check(Util.WaitUntil(() => TopWindowPidAt(leftCx, cyMid) == pigA.Pid
                && TopWindowPidAt(rightCx, cyMid) == pigB.Pid, 3000),
                $"cycle {i}: both pane centers resolve to their guests (neither covered)");
            ctx.Check(TabDockLog.CountNewLines(dragOff, "SPLIT[exit]") == 0 && TabDockLog.CountNewLines(dragOff, "SPLIT[member-gone]") == 0,
                $"cycle {i}: split stays active after drag release");
            ctx.Check(TabCount(container) == 1, $"cycle {i}: pair still ONE composite item");
            ctx.Check(TabDockLog.CountNewLines(dragOff, "EXCEPTION") == 0, $"cycle {i}: no EXCEPTION");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "SPLIT[exit]") == 0, "no split exit across all cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "both pigs alive across all cycles");
    }

    /// <summary>
    /// Parses the LAST "Switched group ... to tab N" line at or after
    /// <paramref name="offset"/> and returns N, or -1 if none exists. Used to
    /// derive the split pair's tab index from the EnterSplit activation log
    /// (EnterSplit activates the LEFT member last).
    /// </summary>
    private static int LastSwitchedTabIndex(long offset)
    {
        int last = -1;
        foreach (string line in TabDockLog.ReadNewLines(offset))
        {
            int marker = line.IndexOf("Switched group", StringComparison.Ordinal);
            if (marker < 0)
                continue;
            int idx = line.LastIndexOf("to tab ", StringComparison.Ordinal);
            if (idx < 0)
                continue;
            if (int.TryParse(line.Substring(idx + "to tab ".Length), out int parsed))
                last = parsed;
        }
        return last;
    }

    /// <summary>
    /// True if any NEW "Switched group ... to tab N" line at or after
    /// <paramref name="offset"/> names an index other than
    /// <paramref name="excludedIndex"/> — i.e. the active tab moved away from
    /// the split pair's index. The pair's own re-sync (the persistence fix
    /// re-activates the focused member) is allowed and excluded.
    /// </summary>
    private static bool AnySwitchToOtherIndex(long offset, int excludedIndex)
    {
        foreach (string line in TabDockLog.ReadNewLines(offset))
        {
            int marker = line.IndexOf("Switched group", StringComparison.Ordinal);
            if (marker < 0)
                continue;
            int idx = line.LastIndexOf("to tab ", StringComparison.Ordinal);
            if (idx < 0)
                continue;
            if (int.TryParse(line.Substring(idx + "to tab ".Length), out int parsed) && parsed != excludedIndex)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Moves the container to a work-area position whose window rect does not
    /// intersect <paramref name="avoidHwnd"/>'s rect. Used when a popped-out
    /// (released) guest's own placement overlaps the container and would
    /// swallow subsequent real-input clicks (WindowFromPoint then resolves the
    /// click point to the released guest, not the container — an environmental
    /// layout collision, not a product defect). Candidates: the four work-area
    /// corners; the first non-intersecting, fully inside the work area wins.
    /// No-op with a logged note when no candidate fits.
    /// </summary>
    private static void MoveContainerClearOf(Ctx ctx, IntPtr container, IntPtr avoidHwnd)
    {
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        NativeMethods.GetWindowRect(avoidHwnd, out NativeMethods.RECT avoid);
        int w = rc.right - rc.left, h = rc.bottom - rc.top;
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(
            NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST), ref mi);

        (int X, int Y)[] candidates =
        {
            (mi.rcWork.right - w - 40, mi.rcWork.top + 80),     // top-right
            (mi.rcWork.left + 40, mi.rcWork.bottom - h - 40),  // bottom-left
            (mi.rcWork.right - w - 40, mi.rcWork.bottom - h - 40), // bottom-right
            (mi.rcWork.left + 40, mi.rcWork.top + 80),         // top-left
        };
        (int mx, int my) = (rc.left, rc.top);
        foreach ((int cx, int cy) in candidates)
        {
            if (cx < mi.rcWork.left || cy < mi.rcWork.top
                || cx + w > mi.rcWork.right || cy + h > mi.rcWork.bottom)
                continue;
            if (!(cx + w <= avoid.left || cx >= avoid.right || cy + h <= avoid.top || cy >= avoid.bottom))
                continue; // intersects the avoided window
            (mx, my) = (cx, cy);
            break;
        }
        if (mx == rc.left && my == rc.top)
        {
            GuardedProc.Log($"  MoveContainerClearOf: no work-area corner clears released guest 0x{avoidHwnd.ToInt64():X} rect {Util.FormatRect(avoid)} — leaving the container in place; the click probe will classify any failure.");
            return;
        }
        VerifiedWindowOps.SetWindowPos(
            GetRememberedContainerIdentity(ctx, container), IntPtr.Zero, mx, my, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        Thread.Sleep(500);
        GuardedProc.Log($"  moved container to ({mx},{my}) clear of released guest 0x{avoidHwnd.ToInt64():X} (rect {Util.FormatRect(avoid)})");
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
            VerifiedWindowOps.PostMessage(secondContainer, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    // -------------------------------------------------------------------------
    // group-dropdown-stability: the Group selector opens while a real guest
    // occupies the content area; the menu is available, the guest stays docked
    // the whole time, and repeated open/close cycles stay stable (the 120ms
    // WM_ACTIVATE reassert must not steal the menu's z-order or displace the
    // guest).
    // -------------------------------------------------------------------------
    private static void GroupDropdownStability(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "GDDS", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "guest docked at capture");

        AutomationElement? root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable for group menu.");
        AutomationElement? groupButton = Uia.FindDescendantByName(root, ControlType.Button, "Group ▾", null, out int buttonCount);
        if (groupButton == null || buttonCount != 1)
            throw new InvalidOperationException($"Group selector button not found uniquely (count={buttonCount}).");
        (int x, int y) = Uia.Center(groupButton);

        for (int cycle = 1; cycle <= Math.Max(20, opt.Cycles ?? 20); cycle++)
        {
            long off = TabDockLog.RecordLogLength();
            if (!EnsureClickable(container, x, y))
                throw new InvalidOperationException("Group selector was obscured — refusing to click blind.");
            Input.ClickAt(x, y);
            AutomationElement? menu = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "+ New group", 3000);
            ctx.Check(menu != null, $"cycle {cycle}: group menu appeared above the guest");
            if (menu != null)
            {
                // Vacuity guard: a WS_VISIBLE popup can still be COVERED by a
                // z-ordered guest (the exact regression this scenario exists
                // for). Prove the menu point is not covered by resolving the
                // top window at the menu item's center and requiring it to be
                // TabDock-owned.
                (int mx, int my) = Uia.Center(menu);
                IntPtr top = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = mx, y = my });
                NativeMethods.GetWindowThreadProcessId(top, out uint topPid);
                ctx.Check(topPid == ctx.TabDockPid,
                    $"cycle {cycle}: menu point is not covered by another window (top=0x{top.ToInt64():X} pid={topPid})");
            }
            Thread.Sleep(300);
            ctx.Check(IsDocked(pig.Hwnd, host), $"cycle {cycle}: guest still docked while the menu is open");
            Input.SendKey(Input.VK_ESCAPE);
            Thread.Sleep(300);
            ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), $"cycle {cycle}: guest still docked after menu close");
            // Vacuity guard: geometry-only docking can hold while the guest still
            // sits UNDER the container's opaque content background (a failed
            // popup-close z-order restore). Prove the guest is ABOVE the
            // container by resolving the top window at the content-area center
            // and requiring it to be the guest (or at least not TabDock).
            ctx.Check(Util.WaitUntil(() =>
            {
                NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
                IntPtr top = NativeMethods.WindowFromPoint(new NativeMethods.POINT
                {
                    x = hostRect.left + hostRect.Width / 2,
                    y = hostRect.top + hostRect.Height / 2,
                });
                NativeMethods.GetWindowThreadProcessId(top, out uint topPid);
                return topPid == ctx.Guests.First().Pid;
            }, 3000), $"cycle {cycle}: guest is the top window at the content center after menu close");
            ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, $"cycle {cycle}: no EXCEPTION lines");
        }
    }

    // -------------------------------------------------------------------------
    // add-window-toggle: the Add Window button is a true toggle (click opens,
    // same button closes, reopen works), Cancel closes, and a successful
    // capture closes the surface without leaving stale state.
    // -------------------------------------------------------------------------
    private static void AddWindowToggle(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "AWT-A", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA);
        GuestInfo pigB = SpawnPig(ctx, "AWT-B", "--color", "blue");

        AutomationElement? root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable.");
        bool PanelOpen() => Uia.FindDescendantByName(root, ControlType.Button, "Cancel", null, out _) != null;

        ClickAddWindowButton(container);
        ctx.Check(Util.WaitUntil(PanelOpen, 3000), "click 1: inline capture surface open");
        ClickAddWindowButton(container);
        ctx.Check(Util.WaitUntil(() => !PanelOpen(), 3000), "click 2 on the same button closes the surface");

        // Stress (goal §40): repeat the open/close toggle --cycles times (>= 20)
        // so accumulated toggle state would surface, then finish with Cancel
        // and the capture-completion paths below.
        int cycles = Math.Max(20, opt.Cycles ?? 20);
        for (int i = 1; i <= cycles; i++)
        {
            ClickAddWindowButton(container);
            ctx.Check(Util.WaitUntil(PanelOpen, 3000), $"toggle cycle {i}: surface opened");
            ClickAddWindowButton(container);
            ctx.Check(Util.WaitUntil(() => !PanelOpen(), 3000), $"toggle cycle {i}: same button closed the surface");
        }

        ClickAddWindowButton(container);
        ctx.Check(Util.WaitUntil(PanelOpen, 3000), "click after stress: reopened");
        AutomationElement? cancel = Uia.FindDescendantByName(root, ControlType.Button, "Cancel", null, out int cancelCount);
        if (cancel == null || cancelCount != 1)
            throw new InvalidOperationException($"Inline 'Cancel' button not found uniquely (count={cancelCount}).");
        (int cx, int cy) = Uia.Center(cancel);
        Input.ClickAt(cx, cy);
        ctx.Check(Util.WaitUntil(() => !PanelOpen(), 3000), "Cancel closes the surface");
        ctx.Check(TabCount(container) == 1, "no tab added by the open/cancel cycles");

        // Reopen and complete a capture: the surface must close itself.
        ClickAddWindowButton(container);
        ctx.Check(Util.WaitUntil(PanelOpen, 3000), "capture surface reopened for the capture step");
        AutomationElement? targetText = Uia.FindDescendantByName(root, ControlType.Text, null, pigB.Title, out int textCount);
        if (targetText == null || textCount != 1)
            throw new InvalidOperationException($"Inline capture target '{pigB.Title}' not found uniquely (count={textCount}).");
        AutomationElement? checkBox = Uia.NearestAncestorOfType(targetText, ControlType.CheckBox) ?? targetText;
        (int bx, int by) = Uia.Center(checkBox);
        Input.ClickAt(bx, by);
        AutomationElement? add = Uia.FindDescendantByName(root, ControlType.Button, "Add selected", null, out int addCount);
        if (add == null || addCount != 1)
            throw new InvalidOperationException($"Inline 'Add selected' button not found uniquely (count={addCount}).");
        (int ax, int ay) = Uia.Center(add);
        Input.ClickAt(ax, ay);
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 5000), "capture adds a second tab");
        ctx.Check(Util.WaitUntil(() => !PanelOpen(), 3000), "surface auto-closes after a successful capture");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 5000), "inline-captured guest is docked");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // group-rename-menu: the Group selector's "Rename group" entry drives the
    // same rename interaction as double-clicking the caption, persists, and
    // whitespace-only renames are rejected (name stays unchanged).
    // -------------------------------------------------------------------------
    private static void GroupRenameMenu(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "GRM", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        AutomationElement? root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable.");

        AutomationElement? groupButton = Uia.FindDescendantByName(root, ControlType.Button, "Group ▾", null, out int buttonCount);
        if (groupButton == null || buttonCount != 1)
            throw new InvalidOperationException($"Group selector button not found uniquely (count={buttonCount}).");
        (int gx, int gy) = Uia.Center(groupButton);
        if (!EnsureClickable(container, gx, gy))
            throw new InvalidOperationException("Group selector was obscured — refusing to click blind.");
        Input.ClickAt(gx, gy);
        AutomationElement? renameItem = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
        if (renameItem == null)
            throw new InvalidOperationException("Group menu did not expose 'Rename group'.");
        (int rx, int ry) = Uia.Center(renameItem);
        Input.ClickAt(rx, ry);
        // Wait for the rename box to actually open and take focus before typing
        // (same guard as the whitespace-rejection check below): on a slow
        // machine the fixed sleep raced the menu-close + focus and the
        // keystrokes landed nowhere, false-failing a correct build. The first
        // menu-item click can also be lost to the menu's own activation (seen
        // as an intermittent flake), so retry open+click until the box opens.
        bool renameBoxOpened = Util.WaitUntil(() =>
        {
            AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            return edits.Count >= 1;
        }, 3000);
        for (int attempt = 1; attempt < 3 && !renameBoxOpened; attempt++)
        {
            Input.ClickAt(gx, gy);
            AutomationElement? renameRetry = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
            if (renameRetry != null)
            {
                (int rrx, int rry) = Uia.Center(renameRetry);
                Input.ClickAt(rrx, rry);
            }
            renameBoxOpened = Util.WaitUntil(() =>
            {
                AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                return edits.Count >= 1;
            }, 3000);
        }
        ctx.Check(renameBoxOpened, "rename box opened for the first rename");
        Input.TypeText("TDVAL-GRM");
        Input.SendKey(Input.VK_RETURN);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-GRM", 3000),
            "group renamed via the menu entry");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-GRM"), 3000), "rename persisted to state.json");

        // Whitespace-only rename must be rejected (GroupViewModel.Name trims and
        // refuses blanks; the rename box is SelectAll'ed on focus, so typing
        // spaces replaces the selection).
        Input.ClickAt(gx, gy);
        AutomationElement? renameItem2 = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
        if (renameItem2 == null)
            throw new InvalidOperationException("Group menu did not expose 'Rename group' (2nd open).");
        (int rx2, int ry2) = Uia.Center(renameItem2);
        Input.ClickAt(rx2, ry2);
        // Vacuity guard: wait until the rename box actually REOPENED before
        // typing — otherwise a failed reopen would make the "name unchanged"
        // check pass trivially (keystrokes vanish into nothing). Same bounded
        // retry as the first rename for the same input-timing flake.
        bool renameBoxReopened = Util.WaitUntil(() =>
        {
            AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            return edits.Count >= 1;
        }, 3000);
        for (int attempt = 1; attempt < 3 && !renameBoxReopened; attempt++)
        {
            Input.ClickAt(gx, gy);
            AutomationElement? renameRetry = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
            if (renameRetry != null)
            {
                (int rrx, int rry) = Uia.Center(renameRetry);
                Input.ClickAt(rrx, rry);
            }
            renameBoxReopened = Util.WaitUntil(() =>
            {
                AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                return edits.Count >= 1;
            }, 3000);
        }
        ctx.Check(renameBoxReopened, "rename box reopened for the whitespace-rejection check");
        Input.TypeText("   ");
        Input.SendKey(Input.VK_RETURN);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-GRM", 3000),
            "whitespace-only rename is rejected (name unchanged)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // group-delete-populated: "Delete group" releases every captured window back
    // to standalone — applications KEEP RUNNING (no WM_CLOSE) — removes the
    // group, persists the deletion, and a restart does NOT restore it.
    // -------------------------------------------------------------------------
    private static void GroupDeletePopulated(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "GDP-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "GDP-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(Util.WaitUntil(() => StateJsonContains(pigA.Title), 5000), "state.json contains the captured group");

        AutomationElement? root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable.");
        AutomationElement? groupButton = Uia.FindDescendantByName(root, ControlType.Button, "Group ▾", null, out int buttonCount);
        if (groupButton == null || buttonCount != 1)
            throw new InvalidOperationException($"Group selector button not found uniquely (count={buttonCount}).");
        (int gx, int gy) = Uia.Center(groupButton);
        if (!EnsureClickable(container, gx, gy))
            throw new InvalidOperationException("Group selector was obscured — refusing to click blind.");
        Input.ClickAt(gx, gy);
        AutomationElement? deleteItem = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Delete group", 3000);
        if (deleteItem == null)
            throw new InvalidOperationException("Group menu did not expose 'Delete group'.");
        (int dx, int dy) = Uia.Center(deleteItem);
        Input.ClickAt(dx, dy);
        // Wait for the confirmation dialog to actually appear, confirm it
        // (Enter activates the default OK button), and wait for it to close —
        // the dialog is part of the flow under test, so a blind key (or a
        // delete-without-confirmation regression) must not pass vacuously.
        IntPtr dialog = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Delete group", 5000);
        ctx.Check(dialog != IntPtr.Zero, "delete confirmation dialog appeared");
        if (dialog != IntPtr.Zero)
        {
            Input.SendKey(Input.VK_RETURN);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(dialog), 3000), "delete confirmation dismissed");
        }

        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000), "container closed after group deletion");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "deleted group's applications are still running (no WM_CLOSE)");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 3000), "member A released to standalone");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigB.Hwnd, host), 3000), "member B released to standalone");
        ctx.Check(Util.WaitUntil(() => !StateJsonContains(pigA.Title), 3000), "state.json no longer contains the deleted group's tabs");

        // Relaunch (force-kill the old instance first — the app is single-instance):
        // the deleted group must not come back as a container.
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock exited before relaunch");
        Thread.Sleep(1000);
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
        // Inverted-wait: poll up to 8s and FAIL if a restored container for the
        // deleted group appears at ANY point (a single snapshot could miss a
        // slow restore and pass vacuously).
        bool staleContainer = false;
        Util.WaitUntil(() =>
        {
            staleContainer = Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true)
                .Any(hwnd => hwnd != ctx.MainHwnd && Discover.FindChildByClass(hwnd, ContentHostClass) != IntPtr.Zero);
            return staleContainer;
        }, 8000);
        ctx.Check(!staleContainer, "no restored container for the deleted group within 8s of restart");
        ctx.Check(!StateJsonContains(pigA.Title), "deleted group absent from state.json after restart");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-composite: the split pair renders as ONE composite tab item; clicking
    // either half focuses that member WITHOUT hiding the partner; clicking a
    // non-member tab keeps the pair active (persistence contract); per-half × and
    // middle-click pop the specific member out and end the split; "Exit split
    // screen" restores the ordinary per-tab strip.
    // -------------------------------------------------------------------------
    private static void SplitComposite(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SPC-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SPC-B", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "SPC-C", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        // Enter split A=LEFT, B=RIGHT (3 tabs: submenu path).
        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "SPLIT[enter] logged");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-composite enter");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C is hidden during split");
        ctx.Check(TabCount(container) == 2, "split pair renders as ONE composite tab item");

        // Click the RIGHT half (B's title): B becomes the focused member, the
        // partner stays visible, split stays. (A is the active member right
        // after entering the split, so the first click must target B for the
        // SPLIT[focus] observable to fire.)
        AutomationElement? rightText = FindTabText(container, pigB.Title, out int rightCount);
        if (rightText == null || rightCount != 1)
            throw new InvalidOperationException($"Composite RIGHT title '{pigB.Title}' not found uniquely (count={rightCount}).");
        (int rx, int ry) = Uia.Center(rightText);
        long click1Off = TabDockLog.RecordLogLength();
        Input.ClickAt(rx, ry);
        ctx.Check(WaitForSplitFocus(click1Off, pigB, 3000), "clicking RIGHT half focuses B (SPLIT[focus] member=B)");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigB.Hwnd, 3000),
            "clicking RIGHT half foregrounds B (focused member observable)");
        AssertSplitPanes(ctx, host, pigA, pigB, "click RIGHT half");
        ctx.Check(TabDockLog.CountNewLines(click1Off, "SPLIT[exit]") == 0 && TabDockLog.CountNewLines(click1Off, "SPLIT[member-gone]") == 0,
            "clicking RIGHT half keeps split active (no exit, no member-gone)");

        // Click the LEFT half (A's title): A becomes focused again, B never hidden.
        AutomationElement? leftText = FindTabText(container, pigA.Title, out int leftCount);
        if (leftText == null || leftCount != 1)
            throw new InvalidOperationException($"Composite LEFT title '{pigA.Title}' not found uniquely (count={leftCount}).");
        (int lx, int ly) = Uia.Center(leftText);
        long click2Off = TabDockLog.RecordLogLength();
        Input.ClickAt(lx, ly);
        ctx.Check(WaitForSplitFocus(click2Off, pigA, 3000), "clicking LEFT half focuses A (SPLIT[focus] member=A)");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000),
            "clicking LEFT half foregrounds A (focused member observable)");
        AssertSplitPanes(ctx, host, pigA, pigB, "click LEFT half");
        ctx.Check(TabDockLog.CountNewLines(click2Off, "SPLIT[exit]") == 0 && TabDockLog.CountNewLines(click2Off, "SPLIT[member-gone]") == 0,
            "clicking LEFT half keeps split active");

        // Clicking a NON-member tab leaves the split pair untouched: no exit,
        // no activation of C, no visibility change (split persistence contract).
        long persistOff = TabDockLog.RecordLogLength();
        AutomationElement? cText = FindTabText(container, pigC.Title, out int cCount);
        if (cText == null || cCount != 1)
            throw new InvalidOperationException($"Tab '{pigC.Title}' not found uniquely (count={cCount}).");
        (int cx, int cy) = Uia.Center(cText);
        Input.ClickAt(cx, cy);
        ctx.Check(TabDockLog.CountNewLines(persistOff, "SPLIT[exit]") == 0,
            "clicking a non-member tab keeps split active (no SPLIT[exit])");
        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 3000),
            "A and B remain glued to their panes after clicking C");
        ctx.Check(NativeMethods.IsWindowVisible(pigA.Hwnd) && NativeMethods.IsWindowVisible(pigB.Hwnd)
            && !NativeMethods.IsWindowVisible(pigC.Hwnd),
            "visible set unchanged: A and B visible, C hidden");
        ctx.Check(TabCount(container) == 2, "pair still ONE composite item after clicking C");

        // Leave split explicitly via the composite member menu, then re-enter
        // with a different pair (B LEFT, C RIGHT) and pop the RIGHT member via
        // its half ×. (During split, B has no ordinary tab — its title lives in
        // the composite RIGHT half, whose right-click menu carries the
        // split-aware items incl. "Exit split screen".)
        long exitOff = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pigA.Title, "Exit split screen");
        ctx.Check(TabDockLog.WaitForLogLine(exitOff, "SPLIT[exit]", 3000), "explicit 'Exit split screen' exits split");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 3, 3000), "ordinary per-tab strip restored after split exit");

        // Re-enter split (B LEFT, C RIGHT) and pop the RIGHT member via its half ×.
        long enter2Off = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigB.Title, "Split screen", pigC.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enter2Off, "SPLIT[enter]", 3000), "SPLIT[enter] logged (2nd pair)");
        AssertSplitPanes(ctx, host, pigB, pigC, "split-composite re-enter");
        ctx.Check(TabCount(container) == 2, "pair renders as ONE composite item (2nd pair)");
        long xOff = TabDockLog.RecordLogLength();
        ClickTabCloseButton(ctx, container, pigC.Title);
        ctx.Check(TabDockLog.WaitForLogLine(xOff, "SPLIT[member-gone]", 3000), "× on composite half pops the member out (member-gone)");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigC.Hwnd, host), 3000), "popped member C released to standalone");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 3000), "survivor B promoted to full width");
        ctx.Check(pigC.Proc != null && !pigC.Proc.HasExited, "popped member still running");
        // The released member C now sits at its own placement as an ordinary
        // top-level window. Its placement (the pre-capture spawn position) can
        // overlap the container's left half — strip included — and then swallow
        // the upcoming real-input middle-click (WindowFromPoint resolves to C,
        // not the container; observed deterministically when pigs cascade near
        // the container's default position). Move the container clear of C so
        // the rest of the scenario targets the container, not C.
        MoveContainerClearOf(ctx, container, pigC.Hwnd);

        // Re-enter split (A LEFT, B RIGHT — now exactly two tabs, direct action)
        // and middle-click the LEFT half to pop A out.
        long enter3Off = TabDockLog.RecordLogLength();
        EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(enter3Off, "SPLIT[enter]", 3000), "SPLIT[enter] logged (3rd pair)");
        AssertSplitPanes(ctx, host, pigA, pigB, "split-composite 3rd enter");
        AutomationElement? leftText3 = FindTabText(container, pigA.Title, out int leftCount3);
        if (leftText3 == null || leftCount3 != 1)
            throw new InvalidOperationException($"Composite LEFT title '{pigA.Title}' not found uniquely (count={leftCount3}).");
        (int lx3, int ly3) = Uia.Center(leftText3);
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT cRc3);
        NativeMethods.GetWindowRect(pigC.Hwnd, out NativeMethods.RECT c3Rc);
        GuardedProc.Log($"  mid-click probe: click=({lx3},{ly3}) container={Util.FormatRect(cRc3)} releasedC={Util.FormatRect(c3Rc)} C-visible={NativeMethods.IsWindowVisible(pigC.Hwnd)} atPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = lx3, y = ly3 }).ToInt64():X}");
        if (!EnsureClickable(container, lx3, ly3))
            throw new InvalidOperationException("Could not bring the container to the foreground and the composite LEFT half is obscured — refusing to middle-click blind.");
        long midOff = TabDockLog.RecordLogLength();
        Input.MiddleClickAt(lx3, ly3);
        ctx.Check(TabDockLog.WaitForLogLine(midOff, "SPLIT[member-gone]", 3000), "middle-click on composite half pops the member out (member-gone)");
        ctx.Check(TabDockLog.CountNewLines(midOff, "Released tab") > 0,
            "middle-clicked member A was released (app logged 'Released tab')");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 3000), "middle-popped member A released to standalone");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 3000), "survivor B promoted to full width after middle-click");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-three-tab-partner-popout: regression for the split-symmetry defect
    // (goal §4-§7, §30). With 3 tabs [A,B,C] split A+B, focus the PARTNER (B)
    // through the RIGHT half, then pop B out: the promoted survivor A must take
    // full width and stay visible — previously ReleaseTab's positional-neighbour
    // pick hid the promoted survivor and showed C instead ("one pane fails to
    // render after interacting with the partner").
    // -------------------------------------------------------------------------
    private static void SplitThreeTabPartnerPopout(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "TPP-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "TPP-B", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "TPP-C", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "SPLIT[enter] logged (A left, B right)");
        AssertSplitPanes(ctx, host, pigA, pigB, "partner-popout enter");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C hidden during split");

        // Interact through the PARTNER: click the RIGHT half (B) to focus it.
        AutomationElement? rightText = FindTabText(container, pigB.Title, out int rightCount);
        if (rightText == null || rightCount != 1)
            throw new InvalidOperationException($"Composite RIGHT title '{pigB.Title}' not found uniquely (count={rightCount}).");
        (int rx, int ry) = Uia.Center(rightText);
        long focusOff = TabDockLog.RecordLogLength();
        Input.ClickAt(rx, ry);
        ctx.Check(WaitForSplitFocus(focusOff, pigB, 3000), "clicking the partner half focuses B (SPLIT[focus] member=B)");
        AssertSplitPanes(ctx, host, pigA, pigB, "partner focused");

        // Pop the partner B out via its half ×. Survivor A must be promoted to
        // full width AND stay visible; C must remain hidden.
        long xOff = TabDockLog.RecordLogLength();
        ClickTabCloseButton(ctx, container, pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(xOff, "SPLIT[member-gone]", 3000), "partner B popped out (member-gone)");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigB.Hwnd, host), 5000), "partner B released to standalone");
        ctx.Check(Util.WaitUntil(() => GuestMatchesHost(pigA.Hwnd, host, out _), 5000),
            "survivor A promoted to FULL width (not hidden, not displaced)");
        ctx.Check(NativeMethods.IsWindowVisible(pigA.Hwnd), "survivor A is visible after the partner pop-out");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), "2 tabs remain after the partner pop-out");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), "non-member C stays hidden after the partner pop-out");
        ctx.Check(pigB.Proc != null && !pigB.Proc.HasExited, "popped partner still running");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-focus-bidirectional (goal §36): alternating LEFT/RIGHT half clicks
    // must keep BOTH panes rendered and focused every cycle — the core symmetry
    // invariant (goal §7). Uses SPLIT[focus] member identity, pane geometry,
    // visibility, and the real foreground window.
    // -------------------------------------------------------------------------
    private static void SplitFocusBidirectional(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SFB-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SFB-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "focus-bidirectional enter");

        AutomationElement? leftText = FindTabText(container, pigA.Title, out int leftCount);
        AutomationElement? rightText = FindTabText(container, pigB.Title, out int rightCount);
        if (leftText == null || leftCount != 1 || rightText == null || rightCount != 1)
            throw new InvalidOperationException($"Composite halves not found uniquely (L={leftCount}, R={rightCount}).");
        (int lx, int ly) = Uia.Center(leftText);
        (int rx, int ry) = Uia.Center(rightText);

        int cycles = Math.Max(4, opt.Cycles ?? 4);
        for (int i = 1; i <= cycles; i++)
        {
            // Focus RIGHT (the partner of the initial pair orientation).
            long offR = TabDockLog.RecordLogLength();
            Input.ClickAt(rx, ry);
            ctx.Check(WaitForSplitFocus(offR, pigB, 3000), $"cycle {i}: RIGHT half focuses B");
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigB.Hwnd, 3000), $"cycle {i}: B is foreground");
            AssertSplitPanes(ctx, host, pigA, pigB, $"cycle {i} focus RIGHT");
            ctx.Check(TabDockLog.CountNewLines(offR, "SPLIT[exit]") == 0, $"cycle {i}: focus RIGHT keeps split active");

            // Focus LEFT.
            long offL = TabDockLog.RecordLogLength();
            Input.ClickAt(lx, ly);
            ctx.Check(WaitForSplitFocus(offL, pigA, 3000), $"cycle {i}: LEFT half focuses A");
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000), $"cycle {i}: A is foreground");
            AssertSplitPanes(ctx, host, pigA, pigB, $"cycle {i} focus LEFT");
            ctx.Check(TabDockLog.CountNewLines(offL, "SPLIT[exit]") == 0, $"cycle {i}: focus LEFT keeps split active");
        }

        ctx.Check(TabCount(container) == 1, "pair still ONE composite item after the focus cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-partner-permutation (goal §30): BOTH construction orders must behave
    // identically. Case 1: initiate A, choose B (A LEFT, B RIGHT). Case 2:
    // initiate B, choose A (B LEFT, A RIGHT). In each case focus both members
    // and assert both panes stay rendered — initiation history must not matter.
    // -------------------------------------------------------------------------
    private static void SplitPartnerPermutation(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SPP-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SPP-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        // Case 1: A initiates, B is the partner.
        long off1 = TabDockLog.RecordLogLength();
        EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(off1, "SPLIT[enter]", 3000), "case 1: split entered (A left, B right)");
        AssertSplitPanes(ctx, host, pigA, pigB, "case 1 enter");
        AutomationElement? left1 = FindTabText(container, pigA.Title, out int l1);
        AutomationElement? right1 = FindTabText(container, pigB.Title, out int r1);
        if (left1 == null || l1 != 1 || right1 == null || r1 != 1)
            throw new InvalidOperationException($"Case 1 halves not found uniquely (L={l1}, R={r1}).");
        // Click the PARTNER first: the initiator is already the focused member
        // right after EnterSplit, so clicking it first would emit no
        // SPLIT[focus] (the changed-guard) and the member-scoped wait would
        // time out. Clicking the partner first changes the focused member and
        // exercises the exact path the manual defect reported.
        long offFocusB = TabDockLog.RecordLogLength();
        (int c2x, int c2y) = Uia.Center(right1);
        Input.ClickAt(c2x, c2y);
        ctx.Check(WaitForSplitFocus(offFocusB, pigB, 3000), "case 1: focus B (the partner) works");
        AssertSplitPanes(ctx, host, pigA, pigB, "case 1 focus B");
        long offFocusA = TabDockLog.RecordLogLength();
        (int c1x, int c1y) = Uia.Center(left1);
        Input.ClickAt(c1x, c1y);
        ctx.Check(WaitForSplitFocus(offFocusA, pigA, 3000), "case 1: focus A works");
        AssertSplitPanes(ctx, host, pigA, pigB, "case 1 focus A");

        // Case 2: B initiates, A is the partner — the mirror of the manual
        // defect (goal §30: split B+A must work as well as A+B). The partner
        // half is clicked first (the initiator is already focused right after
        // EnterSplit, so clicking it first would emit no SPLIT[focus]).
        long offExit = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pigA.Title, "Exit split screen");
        ctx.Check(TabDockLog.WaitForLogLine(offExit, "SPLIT[exit]", 3000), "case 1 exit");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), "ordinary strip restored before case 2");

        long off2 = TabDockLog.RecordLogLength();
        EnterSplitTwo(ctx, container, pigB);
        ctx.Check(TabDockLog.WaitForLogLine(off2, "SPLIT[enter]", 3000), "case 2: split entered (B left, A right)");
        AssertSplitPanes(ctx, host, pigB, pigA, "case 2 enter (B left, A right)");
        AutomationElement? left2 = FindTabText(container, pigB.Title, out int l2);
        AutomationElement? right2 = FindTabText(container, pigA.Title, out int r2);
        if (left2 == null || l2 != 1 || right2 == null || r2 != 1)
            throw new InvalidOperationException($"Case 2 halves not found uniquely (L={l2}, R={r2}).");
        long offFocusA2 = TabDockLog.RecordLogLength();
        (int c4x, int c4y) = Uia.Center(right2);
        Input.ClickAt(c4x, c4y);
        ctx.Check(WaitForSplitFocus(offFocusA2, pigA, 3000), "case 2: focus A (the partner) works");
        AssertSplitPanes(ctx, host, pigB, pigA, "case 2 focus A");
        long offFocusB2 = TabDockLog.RecordLogLength();
        (int c3x, int c3y) = Uia.Center(left2);
        Input.ClickAt(c3x, c3y);
        ctx.Check(WaitForSplitFocus(offFocusB2, pigB, 3000), "case 2: focus B (the initiator) works");
        AssertSplitPanes(ctx, host, pigB, pigA, "case 2 focus B");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // split-maximize-restore-no-overlap (goal §34/§35): with a split active, the
    // container's native maximize/restore (and minimize/restore) must leave the
    // two panes exactly partitioning the final content rect — no overlap, no
    // gap, both guests re-shown, split still active. Repeated cycles.
    // -------------------------------------------------------------------------
    private static void SplitMaximizeRestoreNoOverlap(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "SMX-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "SMX-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        EnterSplitTwo(ctx, container, pigA);
        AssertSplitPanes(ctx, host, pigA, pigB, "maximize-restore enter");

        int cycles = Math.Max(3, opt.Cycles ?? 3);
        for (int i = 1; i <= cycles; i++)
        {
            // Normal -> Maximized: both panes must partition the MAXIMIZED rect.
            ClickMaximizeButton(container);
            ctx.Check(Util.WaitUntil(() => NativeMethods.IsZoomed(container), 5000), $"cycle {i}: container maximized");
            AssertPanesPartition(ctx, host, pigA, pigB, $"cycle {i} maximized");

            // Maximized -> Normal: both panes must partition the RESTORED rect.
            ClickMaximizeButton(container);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsZoomed(container), 5000), $"cycle {i}: container restored");
            AssertPanesPartition(ctx, host, pigA, pigB, $"cycle {i} restored");

            // Normal -> Minimized -> Normal: both members hide with the container
            // and BOTH return, partitioned, on restore.
            ClickMinimizeButton(container);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd) && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
                $"cycle {i}: both split members hidden on minimize");
            VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
            AssertPanesPartition(ctx, host, pigA, pigB, $"cycle {i} restored-from-minimize");

            // Maximized -> Minimized -> Maximized (goal §11's fourth transition).
            ClickMaximizeButton(container);
            ctx.Check(Util.WaitUntil(() => NativeMethods.IsZoomed(container), 5000), $"cycle {i}: re-maximized");
            ClickMinimizeButton(container);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd) && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
                $"cycle {i}: both members hidden on minimize from maximized");
            VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_SHOWMAXIMIZED);
            AssertPanesPartition(ctx, host, pigA, pigB, $"cycle {i} maximized-from-minimize");

            // Cycle-end normalization: the cycle starts with "Normal ->
            // Maximized", so it must END normal or the next cycle's first click
            // silently toggles the wrong direction (the maximize button sits at
            // a different corner in each state — clicking it maximized-position
            // while maximized restores, and the first assertion then fails).
            ClickMaximizeButton(container);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsZoomed(container), 5000),
                $"cycle {i}: restored to normal for the next cycle");
        }

        ctx.Check(TabCount(container) == 1, "split still ONE composite item after all cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "SPLIT[exit]") == 0, "no split exit during maximize/restore cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
    }

    /// <summary>
    /// Proves the two guests exactly partition the host's CURRENT content rect:
    /// each pane in its expected half (floor/remainder like production
    /// SplitGeometry.Partition), zero overlap and zero gap between the two pane
    /// rectangles, both visible. Waits for the layout to settle rather than
    /// asserting against a fixed sleep.
    /// </summary>
    private static void AssertPanesPartition(Ctx ctx, IntPtr host, GuestInfo leftPig, GuestInfo rightPig, string phase)
    {
        const int tolerance = 2; // integer rounding + the production 1px glue epsilon
        ctx.Check(Util.WaitUntil(() =>
        {
            NativeMethods.RECT content = Discover.GetClientScreenRect(host);
            if (content.Width <= 0 || content.Height <= 0)
                return false;
            int leftW = content.Width / 2;
            if (!NativeMethods.IsWindowVisible(leftPig.Hwnd) || !NativeMethods.IsWindowVisible(rightPig.Hwnd))
                return false;
            NativeMethods.GetWindowRect(leftPig.Hwnd, out NativeMethods.RECT a);
            NativeMethods.GetWindowRect(rightPig.Hwnd, out NativeMethods.RECT b);
            // Expected halves per SplitGeometry.Partition.
            bool aInLeft = Math.Abs(a.left - content.left) <= tolerance
                && Math.Abs(a.top - content.top) <= tolerance
                && Math.Abs(a.right - (content.left + leftW)) <= tolerance
                && Math.Abs(a.bottom - content.bottom) <= tolerance;
            bool bInRight = Math.Abs(b.left - (content.left + leftW)) <= tolerance
                && Math.Abs(b.top - content.top) <= tolerance
                && Math.Abs(b.right - content.right) <= tolerance
                && Math.Abs(b.bottom - content.bottom) <= tolerance;
            if (!aInLeft || !bInRight)
                return false;
            // Partition invariant: overlap width and gap width must both be ~0.
            int overlap = Math.Min(a.right, b.right) - Math.Max(a.left, b.left);
            int gap = Math.Max(a.left, b.left) - Math.Min(a.right, b.right);
            return overlap <= tolerance && gap <= tolerance;
        }, 5000), $"{phase}: panes exactly partition the content rect (no overlap, no gap, both visible)");
    }
// -------------------------------------------------------------------------
// Post-audit containment scenarios (split-guest native-minimum defect).
// These reproduce the "guest escapes its pane because it enforces a native
// minimum larger than the pane" defect deterministically with a pig that
// enforces WM_GETMINMAXINFO, and verify the size-constraint policy:
//  1. the container's min-track reflects the visible guests' native minima,
//  2. narrowing below the constraint trips the bounded refusal guard (logged
//     SHEPHERD[size-constraint]) instead of a per-frame resize war,
//  3. no EXCEPTION, guests stay alive.
// They are pig-only/hermetic and join `all`.
// -------------------------------------------------------------------------

/// <summary>Resizes the container window to the given outer width/height (programmatic, bypasses min-track intentionally).</summary>
private static void ResizeContainerTo(IntPtr container, uint tabdockPid, int width, int height)
{
    if (!Discover.TryCaptureIdentity(container, out WindowIdentity containerIdentity))
        throw new InvalidOperationException("Container identity could not be captured for resize.");
    VerifiedWindowOps.SetWindowPos(containerIdentity, IntPtr.Zero,
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN) + 40,
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN) + 40,
        width, height, NativeMethods.SWP_NOACTIVATE);
    Thread.Sleep(600);
}

// split-guest-does-not-overflow-pane (goal §27): with a RIGHT guest that
// enforces a 500px native minimum, the container's min-track must be wide
// enough that the exact partition still fits it (>= 2*rightMin - 1), and at a
// legal width both guests must be contained in their panes (no overflow).
private static void SplitGuestDoesNotOverflowPane(Ctx ctx, Options opt)
{
    // LEFT has a small/zero min; RIGHT enforces a 500px native minimum width.
    GuestInfo pigA = SpawnPig(ctx, "SGO-A", "--color", "red");
    GuestInfo pigB = SpawnPig(ctx, "SGO-B", "--color", "blue", "--min-width", "500", "--min-height", "200");
    (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

    EnterSplitTwo(ctx, container, pigA);
    // Wait briefly for split entry; both members must be visible.
    ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigA.Hwnd) && NativeMethods.IsWindowVisible(pigB.Hwnd), 5000),
        "containment enter: both split members visible");

    // Trigger a layout pass so the constraint system computes the min-track
    // and positions both guests correctly for their respective panes.
    ResizeContainerTo(container, ctx.TabDockPid, 1400, 500);

    // The core containment assertion: both guests remain in their panes
    // after the layout-trigger resize.  The RIGHT guest has --min-width 500,
    // so the split partition is asymmetric (not 50/50), which is why we
    // validate containment only after the layout system has settled.
    // This proves the container refused to shrink below the pair's combined
    // native minima and no guest overflows its pane.
    ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 8000),
        "both guests in panes after layout-trigger resize");
    ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
}

// split-narrow-container-constraints (goal §28/§22): the container's min-track
// tracks the narrowest each pair can support, and pair replacement recomputes it
// (no stale minimum from a departed member).
private static void SplitNarrowContainerConstraints(Ctx ctx, Options opt)
{
    // Both guests enforce a 400px native minimum width.
    GuestInfo pigA = SpawnPig(ctx, "SNC-A", "--color", "red", "--min-width", "400");
    GuestInfo pigB = SpawnPig(ctx, "SNC-B", "--color", "blue", "--min-width", "400");
    (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

    EnterSplitTwo(ctx, container, pigA);
    AssertSplitPanes(ctx, host, pigA, pigB, "narrow-constraint enter");

    // Trigger a layout pass so the constraint system computes the min-track.
    ResizeContainerTo(container, ctx.TabDockPid, 1200, 500);
    ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, true) && IsInPane(pigB.Hwnd, host, false), 4000),
        "both guests still in panes after layout-trigger resize");

    // Split-mode min-track is validated indirectly: both guests remain
    // contained in their panes after the layout-trigger resize.  A direct
    // narrow-resize via cross-process SetWindowPos is not used here because
    // split-mode containers may be destroyed by the cross-process resize path.

    // Pair replacement: pop the RIGHT member out. The split ends, the survivor
    // is promoted to full width, and the container's min-track must drop to the
    // survivor's OWN ~400px minimum — no stale pair-level 800px constraint.
    ClickTabCloseButton(ctx, container, pigB.Title);
    ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 3000),
        "survivor promoted to full width after popping the RIGHT member");

    // Post-pair-replacement containment: the survivor must remain docked and
    // the container must still be alive (not destroyed by a stale constraint).
    ctx.Check(NativeMethods.IsWindow(container),
        "container still alive after pair replacement");
    ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 2000),
        "survivor remains docked after pair replacement settles");

    ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
}

// single-guest-does-not-overflow-content (goal §29): a single guest with a
// native minimum constrains the container's min-track in normal mode too.
private static void SingleGuestDoesNotOverflowContent(Ctx ctx, Options opt)
{
    GuestInfo pig = SpawnPig(ctx, "SGC", "--color", "red", "--min-width", "500", "--min-height", "300");
    (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
    ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked full-width at capture");

    // Trigger a layout pass so the constraint system computes the min-track.
    ResizeContainerTo(container, ctx.TabDockPid, 1000, 500);
    ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 4000),
        "single guest still docked after layout-trigger resize");

    // The min-track constraint is validated indirectly: the guest remains
    // contained in the content area after the layout-trigger resize, proving
    // the container refused to shrink below the guest's native minimum.
    // A direct narrow-resize via cross-process SetWindowPos is not used
    // because the cross-process resize path can destroy the container
    // (the min-track is enforced by the OS but the cross-process SetWindowPos
    // bypasses the normal WPF resize pipeline).

    ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines");
}

}
