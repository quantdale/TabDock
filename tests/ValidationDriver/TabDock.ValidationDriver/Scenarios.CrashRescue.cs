using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 28. crashkill-rescue: verifies WindowShepherdService.RescueOrphanedWindows
    //     (called once at startup in App.xaml.cs, before GroupManager.RestoreState)
    //     and the %APPDATA%\TabDock\hidden-windows.json crash journal. The
    //     headline Shepherd-vs-Reparent improvement: since nothing is ever
    //     reparented, BOTH guest processes/windows survive a force-kill of
    //     TabDock outright (unlike the old backend's WS_CHILD-destroyed-
    //     with-its-parent limitation) — this only has to bring the hidden
    //     (inactive-tab) one back into view.
    // -------------------------------------------------------------------------
    private static void CrashKillRescue(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CKRA", "--color", "blue");
        GuestInfo pigB = SpawnPig(ctx, "CKRB", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        GuestInfo dockedPig = IsDocked(pigA.Hwnd, host) ? pigA : pigB;
        GuestInfo hiddenPig = dockedPig == pigA ? pigB : pigA;
        ctx.Check(IsDocked(dockedPig.Hwnd, host), $"'{dockedPig.Title}' is the docked/active tab after capture");
        ctx.Check(IsReleasedAndHidden(hiddenPig.Hwnd), $"'{hiddenPig.Title}' is the hidden inactive tab after capture");

        GuardedProc.Log("  Force-killing TabDock (Process.Kill, no graceful shutdown) with a hidden captured tab.");
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);

        ctx.Check(dockedPig.Proc != null && !dockedPig.Proc.HasExited, "docked pig's process survived the force-kill");
        ctx.Check(hiddenPig.Proc != null && !hiddenPig.Proc.HasExited, "hidden pig's process survived the force-kill (Shepherd never reparented it)");
        ctx.Check(NativeMethods.IsWindow(hiddenPig.Hwnd) && !NativeMethods.IsWindowVisible(hiddenPig.Hwnd),
            "hidden pig's HWND still exists but stays hidden immediately after the kill (orphaned, awaiting rescue)");
        ctx.Check(NativeMethods.IsWindow(dockedPig.Hwnd) && NativeMethods.IsWindowVisible(dockedPig.Hwnd),
            "docked pig's HWND still exists and is visible (nothing is repositioning it now, but nothing destroyed it either)");

        long relaunchOffset = TabDockLog.RecordLogLength();
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched (MainWindow up)");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);

        ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, "SHEPHERD[rescue]", 10000),
            "TabDock log gained a SHEPHERD[rescue] line on the relaunch");
        ctx.Check(TabDockLog.ContainsNewLine(relaunchOffset, $"0x{hiddenPig.Hwnd.ToInt64():X}"),
            "the rescue log specifically names the previously-hidden pig's HWND");
        int rescuedCount = TabDockLog.CountNewLines(relaunchOffset, "previously-hidden window(s) restored");
        ctx.Check(rescuedCount >= 1, $"rescue count-summary line appeared (found {rescuedCount})");

        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(hiddenPig.Hwnd), 5000),
            "previously-hidden pig is visible again after the relaunch rescue");
    }

    // -------------------------------------------------------------------------
    // 28b. crashkill-rapidswitch-rescue: regression guard for the synchronous
    //      capture-session journal. Fires a rapid, no-settle-time burst of tab
    //      switches (no wait between clicks, unlike instant-tabswitch's
    //      poll-until-docked loop) and force-kills immediately after the last
    //      one, before any later UI work can run. Verifies the pig left hidden
    //      by the final switch is still rescued: the full session journal
    //      writes synchronously before each presentation mutation, and a hard
    //      kill (TerminateProcess) allows no exit handler to fill that gap.
    // -------------------------------------------------------------------------
    private static void CrashKillRapidSwitchRescue(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CKRSA", "--color", "blue");
        GuestInfo pigB = SpawnPig(ctx, "CKRSB", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        GuestInfo activeGuest = IsDocked(pigA.Hwnd, host) ? pigA : pigB;
        GuestInfo otherGuest = ReferenceEquals(activeGuest, pigA) ? pigB : pigA;

        const int switchCount = 6;
        for (int i = 1; i <= switchCount; i++)
        {
            AutomationElement? otherTab = FindTabText(container, otherGuest.Title, out int count);
            if (otherTab == null || count != 1)
                throw new InvalidOperationException($"switch {i}: tab for '{otherGuest.Title}' not found uniquely (count={count}).");
            (int tx, int ty) = Uia.Center(otherTab);
            Input.ClickAt(tx, ty);
            // No settle wait, deliberately: the point is to force-kill during
            // rapid semantic transitions, not after a quiet period.

            (activeGuest, otherGuest) = (otherGuest, activeGuest);
        }

        // The pig left inactive by the final switch is the one JournalHide must
        // have already durably written before we kill — with no dependency on
        // settle time or any exit-handler flush.
        GuestInfo hiddenPig = otherGuest;
        GuestInfo dockedPig = activeGuest;

        GuardedProc.Log("  Force-killing TabDock immediately after a rapid, no-settle-time tab-switch burst.");
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);

        ctx.Check(dockedPig.Proc != null && !dockedPig.Proc.HasExited, "docked pig's process survived the force-kill");
        ctx.Check(hiddenPig.Proc != null && !hiddenPig.Proc.HasExited, "hidden pig's process survived the force-kill");

        long relaunchOffset = TabDockLog.RecordLogLength();
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched (MainWindow up)");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);

        ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, "SHEPHERD[rescue]", 10000),
            "TabDock log gained a SHEPHERD[rescue] line on the relaunch");
        ctx.Check(TabDockLog.ContainsNewLine(relaunchOffset, $"0x{hiddenPig.Hwnd.ToInt64():X}"),
            "the rescue log specifically names the pig left hidden by the final rapid switch");

        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(hiddenPig.Hwnd), 5000),
            "the pig hidden by the last rapid, no-settle-time switch is visible again after the relaunch rescue");
    }

    // -------------------------------------------------------------------------
    // 28c. crashkill-selfhide-not-rescued: regression guard for the
    //      intentional-hide contract — a stale journal entry is only safe to
    //      replay when the guest was meant to be visible. Release's
    //      guest-initiated-hide path
    //      (tray-style close) is the opposite: the guest ends up intentionally
    //      hidden, so a stale "hidden" entry surviving a crash would be
    //      indistinguishable from a real orphan and get incorrectly
    //      un-hidden by RescueOrphanedWindows. The fix writes a durable
    //      DoNotRescue marker before completing that path. This scenario sets up the
    //      exact precondition (a real JournalHide entry already on disk from
    //      an earlier inactive-tab hide, then switches back to active so its
    //      capture-session entry is still present) and self-hides immediately,
    //      force-killing right after. The full-session journal may legitimately
    //      rescue the other still-captured guest, but the intentional-hide
    //      marker must consume the target entry without restoring it.
    // -------------------------------------------------------------------------
    private static void CrashKillSelfHideNotRescued(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CKSHA", "--hide-on-close", "--color", "blue");
        GuestInfo pigB = SpawnPig(ctx, "CKSHB", "--hide-on-close", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Whichever pig capture left inactive already has a real JournalHide
        // entry on disk (Hide() runs on every non-active captured member as
        // part of normal group formation — the same precondition
        // crashkill-rescue itself relies on).
        GuestInfo dockedPig = IsDocked(pigA.Hwnd, host) ? pigA : pigB;
        GuestInfo targetPig = ReferenceEquals(dockedPig, pigA) ? pigB : pigA;
        ctx.Check(!NativeMethods.IsWindowVisible(targetPig.Hwnd), $"'{targetPig.Title}' starts hidden (inactive tab, JournalHide already on disk)");

        // Switch to targetPig: PositionAndShow clears the old hidden-tab entry
        // synchronously while the full capture-session entry remains active.
        AutomationElement? targetTab = FindTabText(container, targetPig.Title, out int count);
        if (targetTab == null || count != 1)
            throw new InvalidOperationException($"tab for '{targetPig.Title}' not found uniquely (count={count}).");
        (int tx, int ty) = Uia.Center(targetTab);
        Input.ClickAt(tx, ty);
        ctx.Check(Util.WaitUntil(() => IsDocked(targetPig.Hwnd, host), 3000), $"switched to '{targetPig.Title}' (full capture-session journal remains durable)");

        // Immediately trigger guest-initiated self-hide (WM_CLOSE via the tab's
        // context menu; --hide-on-close makes the pig hide instead of exit) —
        // no settle wait beyond what confirming the switch above required.
        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, targetPig.Title, "Close window");
        ctx.Check(TabDockLog.WaitForLogLine(off, "hid itself (tray-style close)", 5000),
            $"TabDock log gained 'hid itself (tray-style close)' for '{targetPig.Title}'");

        GuardedProc.Log($"  Force-killing TabDock immediately after '{targetPig.Title}' self-hid.");
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);

        ctx.Check(dockedPig.Proc != null && !dockedPig.Proc.HasExited, "the other pig's process survived the force-kill");
        ctx.Check(targetPig.Proc != null && !targetPig.Proc.HasExited, "the self-hidden pig's process survived the force-kill");
        ctx.Check(NativeMethods.IsWindow(targetPig.Hwnd) && !NativeMethods.IsWindowVisible(targetPig.Hwnd),
            "the self-hidden pig stays hidden immediately after the kill");

        long relaunchOffset = TabDockLog.RecordLogLength();
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched (MainWindow up)");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);

        Thread.Sleep(1500); // let RescueOrphanedWindows run (or correctly no-op) and settle

        ctx.Check(!TabDockLog.ContainsNewLine(relaunchOffset, $"SHEPHERD[rescue] restored guest 0x{targetPig.Hwnd.ToInt64():X}"),
            "relaunch did not restore the intentionally self-hidden pig (other capture-session entries may be rescued)");
        ctx.Check(!NativeMethods.IsWindowVisible(targetPig.Hwnd),
            "the self-hidden pig was NOT incorrectly resurrected by rescue after the relaunch");
    }

    // -------------------------------------------------------------------------
    // 39. crashkill-during-active-drag: force-kills TabDock while a real
    //     OS-level mouse-button-down state is still active past the tab-strip
    //     drag threshold (a true "kill mid-native-drag" cannot be done from
    //     outside the process — Input.DragFromTo's down/move/up sequence is
    //     synchronous — so the button is held via Input.PressLeftButtonHeld
    //     instead of released), then relaunches (mirroring crashkill-rescue's
    //     relaunch block) and checks both guests survived and the app comes
    //     back up cleanly with no stuck state.
    // -------------------------------------------------------------------------
    private static void CrashKillDuringActiveDrag(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CKDA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "CKDB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to drag blind.");
        AutomationElement? tab = FindTabText(container, pigA.Title, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{pigA.Title}' not found uniquely (count={count}).");
        (int sx, int sy) = Uia.Center(tab);

        long off = TabDockLog.RecordLogLength();
        try
        {
            // Hold the button down past TabsListBox_MouseMove's DragThreshold
            // (4px), so Mouse.Capture(TabsListBox) is genuinely acquired
            // (ContainerWindow.xaml.cs), then force-kill TabDock while that
            // real button-down state is still active.
            Input.PressLeftButtonHeld(sx, sy);
            Input.MoveWhileHeld(sx + 15, sy + 10);
            Input.MoveWhileHeld(sx + 30, sy + 15);
            Thread.Sleep(200);

            GuardedProc.Log("  Force-killing TabDock (Process.Kill) while a tab-strip drag is theoretically still in progress (mouse button physically held down).");
            ctx.TabDock.Kill();
            ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed mid-drag");
        }
        finally
        {
            // Always release the real OS-level button state ourselves — an
            // unreleased button-down would corrupt every later click in this run.
            Input.ReleaseLeftButtonHeld();
        }
        Thread.Sleep(500);

        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited, "pigA process survived the force-kill");
        ctx.Check(pigB.Proc != null && !pigB.Proc.HasExited, "pigB process survived the force-kill");

        // Relaunch, mirroring crashkill-rescue's exact relaunch block.
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched cleanly (MainWindow up) after a kill mid-drag");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
        ctx.Check(Util.WaitUntil(() => !ctx.TabDock.HasExited, 2000), "relaunched TabDock stays up (no immediate crash from any stuck drag/capture state)");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, "no EXCEPTION lines around the kill/relaunch");
    }

    private static void CrashKillMaximizedRecovery(Ctx ctx, Options opt)
        => CrashKillOriginalWindowState(ctx, "CKMAX", NativeMethods.SW_SHOWMAXIMIZED, expectedMaximized: true, expectedMinimized: false);

    private static void CrashKillMinimizedRecovery(Ctx ctx, Options opt)
        => CrashKillOriginalWindowState(ctx, "CKMIN", NativeMethods.SW_SHOWMINIMIZED, expectedMaximized: false, expectedMinimized: true);

    private static void CrashKillSplitRescue(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CKSPL-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "CKSPL-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        EnterSplitTwo(ctx, container, pigA);
        ctx.Check(Util.WaitUntil(() => IsInPane(pigA.Hwnd, host, left: true)
            && IsInPane(pigB.Hwnd, host, left: false), 5000),
            "both guests entered exact split panes before hard kill");

        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed with split pair active");
        Thread.Sleep(700);
        ctx.Check(!pigA.Proc!.HasExited && !pigB.Proc!.HasExited, "both split guest processes survived force-kill");

        long relaunchOffset = TabDockLog.RecordLogLength();
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched after split recovery kill");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
        ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, $"SHEPHERD[rescue] restored guest 0x{pigA.Hwnd.ToInt64():X}", 10000),
            "split guest A received full-state rescue");
        ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, $"SHEPHERD[rescue] restored guest 0x{pigB.Hwnd.ToInt64():X}", 10000),
            "split guest B received full-state rescue");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigA.Hwnd)
            && NativeMethods.IsWindowVisible(pigB.Hwnd), 5000),
            "both split guests are visible after rescue");
    }

    /// <summary>
    /// Verifies the v2 capture-session journal restores an originally maximized
    /// or minimized guest after an abrupt TabDock termination. Capture itself
    /// temporarily restores iconic/zoomed guests for exact docking; the journal
    /// must restore the user's original presentation state on the next launch.
    /// </summary>
    private static void CrashKillOriginalWindowState(
        Ctx ctx,
        string tag,
        int showCommand,
        bool expectedMaximized,
        bool expectedMinimized)
    {
        GuestInfo pig = SpawnPig(ctx, tag, "--color", "blue");
        if (!VerifiedWindowOps.ShowWindow(pig.Hwnd, pig.Pid, showCommand))
            throw new InvalidOperationException($"Could not set original {tag} presentation state safely.");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsZoomed(pig.Hwnd) == expectedMaximized
            && NativeMethods.IsIconic(pig.Hwnd) == expectedMinimized, 3000),
            $"guest entered original {tag} state before capture");

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 4000), "guest docked before hard kill");
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed for original-state recovery");
        Thread.Sleep(700);
        ctx.Check(!pig.Proc!.HasExited, "guest process survived original-state recovery kill");

        long relaunchOffset = TabDockLog.RecordLogLength();
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched for original-state recovery");
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
        ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, $"SHEPHERD[rescue] restored guest 0x{pig.Hwnd.ToInt64():X}", 10000),
            "relaunch rescue restored the full-state journal entry");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsZoomed(pig.Hwnd) == expectedMaximized
            && NativeMethods.IsIconic(pig.Hwnd) == expectedMinimized
            && NativeMethods.IsWindowVisible(pig.Hwnd), 5000),
            $"guest returned to original {tag} placement/state after rescue");
        ctx.Check(TabDockLog.CountNewLines(relaunchOffset, "EXCEPTION") == 0, "no EXCEPTION lines during original-state rescue");
    }
}
