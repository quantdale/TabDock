using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // Product Trust & Interaction supervised scenarios
    // -------------------------------------------------------------------------

    /// <summary>
    /// Exercises the real global PageUp/PageDown path from a captured guest,
    /// the owning container, a presented split, and a dormant split. It then
    /// proves that an unrelated foreground guest remains untouched. The driver
    /// sends only guarded real input; unit policy tests cover the stale/recycled
    /// HWND branches that cannot be safely manufactured by a desktop run.
    /// </summary>
    private static void GlobalTabNavigation(Ctx ctx, Options opt)
    {
        GuestInfo a = SpawnPig(ctx, "GTNA", "--color", "red");
        GuestInfo b = SpawnPig(ctx, "GTNB", "--color", "blue");
        GuestInfo c = SpawnPig(ctx, "GTNC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, a, b, c);

        ClickTabByTitle(ctx, container, a.Title);
        ctx.Check(Util.WaitUntil(() => IsDocked(a.Hwnd, host), 3000), "global navigation starts with A active");

        // The picker admits selections in candidate-enumeration order, not
        // checkbox order (physically observed [c,b,a] on 2026-08-23), so the
        // strip neighbors must be derived from the LIVE strip instead of
        // assuming capture order.
        GuestInfo[] stripOrder = TabStripOrder(container, a, b, c);
        int indexOfA = Array.IndexOf(stripOrder, a);
        int indexOfB = Array.IndexOf(stripOrder, b);
        GuestInfo nextOfA = stripOrder[(indexOfA + 1) % stripOrder.Length];

        bool sent = Input.SendHotkeyCtrlAltPageTo(a.Hwnd, previous: false);
        ctx.Check(sent, "Ctrl+Alt+PageDown sent from a captured guest");
        ctx.Check(Util.WaitUntil(() => IsDocked(nextOfA.Hwnd, host), 3000),
            $"PageDown from captured guest selects the next tab ({nextOfA.Title})");

        sent = Input.SendHotkeyCtrlAltPageTo(nextOfA.Hwnd, previous: true);
        ctx.Check(sent, "Ctrl+Alt+PageUp sent from a captured guest");
        ctx.Check(Util.WaitUntil(() => IsDocked(a.Hwnd, host), 3000),
            "PageUp from captured guest selects the previous tab");

        // Hand foreground to the TabDock process through a REAL strip click:
        // Windows denies programmatic foreground theft to background
        // processes, so the harness must activate like a user. Active becomes
        // nextOfA; the global operation must behave identically from there.
        ClickTabByTitle(ctx, container, nextOfA.Title);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == container, 3000),
            "owning container holds foreground after a real strip click");
        GuestInfo secondNext = stripOrder[(Array.IndexOf(stripOrder, nextOfA) + 1) % stripOrder.Length];
        sent = Input.SendHotkeyCtrlAltPageTo(container, previous: false);
        ctx.Check(sent, "Ctrl+Alt+PageDown sent from the owning container");
        ctx.Check(Util.WaitUntil(() => IsDocked(secondNext.Hwnd, host), 3000),
            "PageDown from the owning container uses the same navigation operation");

        ClickTabByTitle(ctx, container, a.Title);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == container, 3000),
            "container holds foreground before split menu interaction");
        long splitOffset = TabDockLog.RecordLogLength();
        // Three tabs are admitted here, so "Split screen" is the >=3-tab partner
        // submenu (Scenarios.Split.cs header): choosing 'Split with B' makes A
        // LEFT and B RIGHT. The two-tab EnterSplitTwo direct action is invalid
        // in this state.
        ClickTabSubmenuItem(ctx, container, a.Title, "Split screen", b.Title);
        ctx.Check(TabDockLog.WaitForLogLine(splitOffset, "SPLIT[enter]", 3000),
            "global navigation scenario entered the presented split");
        AssertSplitPanes(ctx, host, a, b, "global navigation presented split");

        sent = Input.SendHotkeyCtrlAltPageTo(a.Hwnd, previous: false);
        ctx.Check(sent, "PageDown sent from the presented split's LEFT guest");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == b.Hwnd, 3000),
            "global PageDown focuses the presented split's RIGHT member");
        AssertSplitPanes(ctx, host, a, b, "global navigation presented split after PageDown");

        ClickTabByTitle(ctx, container, c.Title);
        ctx.Check(Util.WaitUntil(() => IsDocked(c.Hwnd, host), 3000),
            "ordinary tab selection makes the split relationship dormant");
        sent = Input.SendHotkeyCtrlAltPageTo(c.Hwnd, previous: false);
        ctx.Check(sent, "PageDown sent from a dormant split's unrelated active guest");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(a.Hwnd)
            && NativeMethods.IsWindowVisible(b.Hwnd), 3000),
            "global navigation from dormant state resumes the existing pair");
        AssertSplitPanes(ctx, host, a, b, "global navigation dormant split resumed");

        GuestInfo unrelated = SpawnPig(ctx, "GTN-UNRELATED", "--color", "white");
        if (!Input.ForceForegroundRoot(unrelated.Hwnd))
            throw new InvalidOperationException("Could not foreground unrelated guest safely.");
        IntPtr before = NativeMethods.GetForegroundWindow();
        sent = Input.SendHotkeyCtrlAltPageTo(unrelated.Hwnd, previous: false);
        ctx.Check(sent, "global shortcut sent while an unrelated foreground window was active");
        Thread.Sleep(350);
        ctx.Check(NativeMethods.GetForegroundWindow() == before,
            "unrelated foreground application is a strict no-op for global tab navigation");
        ctx.Check(NativeMethods.IsWindowVisible(a.Hwnd) && NativeMethods.IsWindowVisible(b.Hwnd),
            "unrelated foreground no-op does not disturb the presented split");
    }

    /// <summary>
    /// Drives the always-visible Split button through disabled, create,
    /// presented focus/end, and dormant resume/show states using UIA only for
    /// read/locate operations and guarded real mouse clicks for actions.
    /// </summary>
    private static void SplitAffordance(Ctx ctx, Options opt)
    {
        GuestInfo a = SpawnPig(ctx, "SFA", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, a);

        AutomationElement splitButton = FindSplitButton(container);
        ctx.Check(!splitButton.Current.IsEnabled,
            "persistent Split affordance is visible but disabled with one tab");
        ctx.Check(splitButton.Current.HelpText?.IndexOf("another", StringComparison.OrdinalIgnoreCase) >= 0,
            "disabled Split affordance explains that another tab is required");

        GuestInfo b = SpawnPig(ctx, "SFB", "--color", "blue");
        CaptureIntoExistingGroupViaAddButton(ctx, container, host, b);
        ClickTabByTitle(ctx, container, a.Title);
        splitButton = FindSplitButton(container);
        ctx.Check(splitButton.Current.IsEnabled,
            "Split affordance becomes enabled after a second live tab is admitted");

        ClickSplitButton(ctx, container);
        AutomationElement? create = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Split with " + b.Title, 5000);
        ctx.Check(create != null, "Split affordance lists the eligible partner by title");
        if (create == null || !TryClickVerifiedPopupItem(ctx, container, create))
            throw new InvalidOperationException("Split partner menu item was not safely clickable.");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(a.Hwnd)
            && NativeMethods.IsWindowVisible(b.Hwnd), 3000),
            "Split affordance creates the pair through the existing split path");
        AssertSplitPanes(ctx, host, a, b, "Split affordance presented pair");

        ClickSplitButton(ctx, container);
        AssertSplitActionExists(ctx, "SplitAction_FocusLeft", "presented split exposes Focus LEFT");
        AssertSplitActionExists(ctx, "SplitAction_FocusRight", "presented split exposes Focus RIGHT");
        AutomationElement? end = Uia.FindMenuItemOnDesktopByAutomationId(ctx.TabDockPid, "SplitAction_EndRelationship", 5000);
        ctx.Check(end != null, "presented split exposes End split");
        if (end == null || !TryClickVerifiedPopupItem(ctx, container, end))
            throw new InvalidOperationException("End split action was not safely clickable.");
        ctx.Check(Util.WaitUntil(() => !(NativeMethods.IsWindowVisible(a.Hwnd)
            && NativeMethods.IsWindowVisible(b.Hwnd)), 3000),
            "End split returns to ordinary one-guest presentation");

        GuestInfo c = SpawnPig(ctx, "SFC", "--color", "green");
        CaptureIntoExistingGroupViaAddButton(ctx, container, host, c);
        ClickTabByTitle(ctx, container, a.Title);
        ClickSplitButton(ctx, container);
        AutomationElement? createAgain = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Split with " + b.Title, 5000);
        if (createAgain == null || !TryClickVerifiedPopupItem(ctx, container, createAgain))
            throw new InvalidOperationException("Could not safely recreate the split pair.");
        AssertSplitPanes(ctx, host, a, b, "Split affordance pair before dormant transition");

        ClickTabByTitle(ctx, container, c.Title);
        ctx.Check(Util.WaitUntil(() => IsDocked(c.Hwnd, host), 3000),
            "ordinary unrelated tab selection leaves the pair dormant");
        ClickSplitButton(ctx, container);
        AssertSplitActionExists(ctx, "SplitAction_ResumeLeft", "dormant split exposes Resume/show LEFT");
        AssertSplitActionExists(ctx, "SplitAction_ResumeRight", "dormant split exposes Resume/show RIGHT");
        AutomationElement? resume = Uia.FindMenuItemOnDesktopByAutomationId(ctx.TabDockPid, "SplitAction_ResumeLeft", 5000);
        if (resume == null || !TryClickVerifiedPopupItem(ctx, container, resume))
            throw new InvalidOperationException("Resume/show split action was not safely clickable.");
        AssertSplitPanes(ctx, host, a, b, "Split affordance resumed dormant pair");

        ClickSplitButton(ctx, container);
        end = Uia.FindMenuItemOnDesktopByAutomationId(ctx.TabDockPid, "SplitAction_EndRelationship", 5000);
        if (end == null || !TryClickVerifiedPopupItem(ctx, container, end))
            throw new InvalidOperationException("Final End split action was not safely clickable.");
    }

    /// <summary>
    /// The blocked-admission state requires a deliberately unavailable durable
    /// journal. The driver refuses to manufacture that condition by touching a
    /// user's AppData or mutating permissions. On a qualifying blocked-storage
    /// desktop it verifies the UIA status/control projection; otherwise it emits
    /// BLOCKED_ENVIRONMENT with the exact rerun command.
    /// </summary>
    private static void CaptureAdmissionBlocked(Ctx ctx, Options opt)
    {
        AutomationElement? main = Uia.FromHwnd(ctx.MainHwnd);
        AutomationElement? status = main == null
            ? null
            : Uia.FindDescendantByAutomationId(main, "CaptureAdmissionStatus", out _);
        if (status == null || status.Current.IsOffscreen || status.Current.BoundingRectangle.IsEmpty)
        {
            ctx.Block("BLOCKED_ENVIRONMENT: durable journal failure was not safely inducible on this desktop; rerun after launching with a supervised unavailable AppData journal and use the positional capture-admission-blocked scenario command documented in docs/TESTING.md.");
            return;
        }

        AutomationElement? capture = main == null
            ? null
            : Uia.FindDescendantByAutomationId(main, "LauncherCaptureButton", out _);
        ctx.Check(capture != null && !capture.Current.IsEnabled,
            "blocked admission disables the launcher Capture button");
        ctx.Check(status.Current.Name?.Length > 0, "blocked admission exposes a human-readable reason");
    }

    private static AutomationElement FindSplitButton(IntPtr container)
    {
        AutomationElement root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA root unavailable.");
        AutomationElement? button = Uia.FindDescendantByAutomationId(root, "SplitAffordance", out int count);
        return button != null && count == 1
            ? button
            : throw new InvalidOperationException($"Split affordance was not uniquely exposed (count={count}).");
    }

    private static void ClickSplitButton(Ctx ctx, IntPtr container)
    {
        AutomationElement button = FindSplitButton(container);
        (int x, int y) = Uia.Center(button);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Split affordance was obscured — refusing to click blind.");
        Input.ClickAt(x, y);
        Thread.Sleep(250);
    }

    private static void AssertSplitActionExists(Ctx ctx, string automationId, string assertion)
    {
        AutomationElement? item = Uia.FindMenuItemOnDesktopByAutomationId(ctx.TabDockPid, automationId, 5000);
        ctx.Check(item != null, assertion);
    }

    private static void ClickTabByTitle(Ctx ctx, IntPtr container, string title)
    {
        AutomationElement? tab = FindTabText(container, title, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab '{title}' was not uniquely located (count={count}).");
        (int x, int y) = Uia.Center(tab);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException($"Tab '{title}' was obscured — refusing to click blind.");
        Input.ClickAt(x, y);
        Thread.Sleep(250);
    }
}
