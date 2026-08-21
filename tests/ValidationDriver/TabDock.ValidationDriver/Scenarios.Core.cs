using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 1. rename
    // -------------------------------------------------------------------------
    private static void Rename(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "REN", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
        ctx.Check(caption != null && count == 1, $"caption title TextBlock 'Group' found uniquely (count={count})");
        if (caption == null || count != 1)
            return;

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcBefore);
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        // Retry with a clickability gate: ForceForeground succeeding does not
        // guarantee the caption point is unobstructed, and a covering top-level
        // window silently swallows both the double-click and the typed text
        // (observed on 2026-08-03: rename failed while an overlapping window
        // covered the caption; the same step passed on attempt 1 in the same
        // run as soon as the desktop was clear).
        bool renamed = false;
        for (int attempt = 0; attempt < 3 && !renamed; attempt++)
        {
            if (attempt > 0)
                if (!Input.ForceForeground(container))
                    throw new InvalidOperationException("Could not bring the container to the foreground; refusing to retry rename.");
            AutomationElement? cap = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int cnt);
            if (cap == null || cnt != 1)
                break;
            (int cx, int cy) = Uia.Center(cap);
            if (FindObstructingWindow(container, cx, cy) != IntPtr.Zero)
                continue;
            Input.DoubleClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText("TDVAL-Renamed");
            Input.SendKey(Input.VK_RETURN);
            renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Renamed", 2000);
        }

        ctx.Check(renamed, "container window text became 'TDVAL-Renamed' within 2s");
        ctx.Check(!NativeMethods.IsZoomed(container), "double-click did not maximize the container");
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcAfter);
        ctx.Check(Util.RectNear(rcBefore, rcAfter, 0),
            $"container rect unchanged (before {Util.FormatRect(rcBefore)}, after {Util.FormatRect(rcAfter)})");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-Renamed"), 3000),
            "state.json contains 'TDVAL-Renamed' without exiting TabDock");
    }

    // -------------------------------------------------------------------------
    // 2. popout
    // -------------------------------------------------------------------------
    private static void PopOut(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "POP", "--color", "green");

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        ClickTabMenuItem(ctx, container, pig.Title, "Pop out");

        // Shepherd never mutates parent/style (WindowShepherdService.cs is explicit
        // that WS_CAPTION stripping was deliberately not implemented), so the old
        // parent-restored/WS_CAPTION-restored checks tested a mutation that no
        // longer happens; released-and-shown-at-its-own-placement is the only
        // meaningful signal left.
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pig.Hwnd, host), 3000),
            "pig released within 3s (shown at its own placement, not docked over host)");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive");
        ctx.Check(!PigLog.ContainsLine(pig.Pid, "WM_CLOSE"), "pig log has NO WM_CLOSE");
        // Popping out the only tab must close the now-empty container outright,
        // not just clear its tab strip (finding L11 — previously it was left open
        // indefinitely). Strict "closed", not the old "empty or closed" tolerance.
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 3000),
            "container closed (last tab popped out)");
        // ...and the emptied group must not persist forever either (finding L12 —
        // 18 residual empty groups were observed accumulating this way on one
        // machine, each reopening an empty container at every future launch).
        ctx.Check(Util.WaitUntil(() => !StateJsonContains(pig.Title), 3000),
            "state.json no longer references the popped-out tab (group removed, not just the window closed)");
    }

    // -------------------------------------------------------------------------
    // 3. closewin
    // -------------------------------------------------------------------------
    private static void CloseWin(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CW", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig.Title, "Close window");

        // Poll every 100ms until the HWND dies. Under Shepherd the guest is ALWAYS
        // a top-level window while captured, so "never becomes top-level" no
        // longer means anything; the invariant that still matters is that it
        // never gets shown away from the host (i.e. released-and-shown) mid-
        // teardown — it must stay either docked over the host or hidden right up
        // until the HWND is destroyed.
        bool becameReleasedAndShown = false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000 && NativeMethods.IsWindow(pig.Hwnd))
        {
            if (IsReleasedAndShown(pig.Hwnd, host))
            {
                becameReleasedAndShown = true;
            }
            Thread.Sleep(100);
        }
        ctx.Check(!becameReleasedAndShown, "pig NEVER shown away from the host while closing (stayed docked or hidden)");

        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "WM_CLOSE", 3000), "pig log contains WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => pig.Proc!.HasExited, 5000), "pig process exited within 5s");
        // A closing window may hide-then-destroy (default WinForms sequence) or
        // destroy directly; both drive the same teardown. Accept either.
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off, "hid itself"), 5000),
            "TabDock log shows the tab was torn down (destroy or hide path)");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000),
            "container closed (it was the only tab)");
    }

    // -------------------------------------------------------------------------
    // 4. closewin-hide
    // -------------------------------------------------------------------------
    private static void CloseWinHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CWH", "--hide-on-close", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig.Title, "Close window");
        AssertHiddenRelease(ctx, pig, container, off);
    }

    // -------------------------------------------------------------------------
    // 5. selfclose
    // -------------------------------------------------------------------------
    private static void SelfClose(Ctx ctx, Options opt)
    {
        // 7s (not 4s) so the timer cannot fire while the real-input capture flow (~5s) is still running.
        GuestInfo pig = SpawnPig(ctx, "SC", "--self-close-after", "7", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        long off = TabDockLog.RecordLogLength();

        Thread.Sleep(8000);

        ctx.Check(TabDockLog.ContainsNewLine(off, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off, "hid itself"),
            "TabDock log shows the tab was torn down (destroy or hide path)");
        ctx.Check(pig.Proc != null && pig.Proc.HasExited, "pig process exited by itself");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 3000),
            "container closed (last tab)");
        // Note: a persisted group restored from the user's state.json opens its own
        // empty "Group" container at startup, so we verify THIS scenario's container
        // is gone (above) rather than asserting no "Group" window exists at all.
    }

    // -------------------------------------------------------------------------
    // 6. selfhide
    // -------------------------------------------------------------------------
    private static void SelfHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SH", "--hide-on-close", "--close-button", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        AutomationElement pigEl = Uia.FromHwnd(pig.Hwnd)
            ?? throw new InvalidOperationException("Pig UIA element unavailable.");
        AutomationElement? closeBtn = Uia.FindDescendantByName(pigEl, ControlType.Button, "X-CLOSE", null, out int count);
        if (closeBtn == null || count != 1)
            throw new InvalidOperationException($"X-CLOSE button not found uniquely in pig (count={count}).");

        if (!Input.ForceForegroundRoot(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the captured pig to the foreground — refusing to click blind.");
        (int bx, int by) = Uia.Center(closeBtn);
        Input.ClickAt(bx, by);
        AssertHiddenRelease(ctx, pig, container, off);
    }

    // -------------------------------------------------------------------------
    // 7. selfminhide
    // -------------------------------------------------------------------------
    private static void SelfMinHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SMH", "--minimize-then-hide-on-close", "--close-button", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        AutomationElement pigEl = Uia.FromHwnd(pig.Hwnd)
            ?? throw new InvalidOperationException("Pig UIA element unavailable.");
        AutomationElement? closeBtn = Uia.FindDescendantByName(pigEl, ControlType.Button, "X-CLOSE", null, out int count);
        if (closeBtn == null || count != 1)
            throw new InvalidOperationException($"X-CLOSE button not found uniquely in pig (count={count}).");

        if (!Input.ForceForegroundRoot(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the captured pig to the foreground — refusing to click blind.");
        (int bx, int by) = Uia.Center(closeBtn);
        Input.ClickAt(bx, by);

        ctx.Check(TabDockLog.WaitForLogLine(off, "hid itself", 6000), "TabDock log gained 'hid itself'");
        Thread.Sleep(2500); // give any restore loop time to manifest
        int restores = TabDockLog.CountNewLines(off, "minimized; restoring");
        ctx.Check(restores <= 1, $"no restore loop (got {restores} 'minimized; restoring' line(s), max 1 allowed)");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "tab removed (container empty or closed)");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive");
        ctx.Check(!NativeMethods.IsWindowVisible(pig.Hwnd), "pig hidden");
        ctx.Check(IsReleasedAndHidden(pig.Hwnd), "pig released and hidden (guest-initiated hide, not shown)");
    }

    // -------------------------------------------------------------------------
    // 8. tabswitch-hidesafety (CRITICAL)
    // -------------------------------------------------------------------------
    private static void TabSwitchHideSafety(Ctx ctx, Options opt)
    {
        GuestInfo[] pigs =
        {
            SpawnPig(ctx, "RED", "--color", "red"),
            SpawnPig(ctx, "BLUE", "--color", "blue"),
            SpawnPig(ctx, "GREEN", "--color", "green"),
        };
        char[] channelByIdx = { 'r', 'b', 'g' };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigs);

        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        bool everyClickOk = true;
        int lastIdx = -1;
        for (int i = 0; i < 24; i++)
        {
            int idx = i % 3;
            AutomationElement? tab = FindTabText(container, pigs[idx].Title, out int count);
            if (tab == null || count != 1)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab for '{pigs[idx].Title}' found uniquely (count={count})");
                break;
            }
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(250);

            int tabs = TabCount(container);
            if (tabs != 3)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab count still 3 (got {tabs})");
                break;
            }
            lastIdx = idx;
        }
        if (everyClickOk)
            ctx.Check(true, "tab count stayed 3 after every one of the 24 clicks");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "hid itself") == 0, "ZERO 'hid itself' lines in TabDock log");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "destroyed") == 0, "ZERO 'destroyed' lines in TabDock log");

        for (int i = 0; i < pigs.Length; i++)
        {
            bool alive = pigs[i].Proc != null && !pigs[i].Proc!.HasExited;
            bool captured = IsDocked(pigs[i].Hwnd, host) || IsReleasedAndHidden(pigs[i].Hwnd);
            ctx.Check(alive && captured, $"pig '{pigs[i].Title}' alive and still captured (docked over host or hidden inactive tab)");
        }

        if (lastIdx >= 0)
        {
            Thread.Sleep(400);
            Input.ForceForegroundRoot(host);
            int[]? frame = Pixels.CaptureHostScreenArea(host);
            char dominant = frame != null ? Pixels.DominantChannel(frame) : '?';
            ctx.Check(frame != null && dominant == channelByIdx[lastIdx],
                $"host dominant channel '{dominant}' matches last-clicked pig color channel '{channelByIdx[lastIdx]}'");
        }

        if (ctx.Pass)
            GuardedProc.Log("  HIDE-SAFETY: no tab was removed by tab-switch-induced hide");
    }

    // -------------------------------------------------------------------------
    // 9. minrestore
    // -------------------------------------------------------------------------
    private static void MinRestore(Ctx ctx, Options opt)
    {
        // 7s (not 3s) so the timer cannot fire while the real-input capture flow (~5s) is still running.
        GuestInfo pig = SpawnPig(ctx, "MR", "--color", "white", "--self-minimize-after", "7");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        long off = TabDockLog.RecordLogLength();

        Thread.Sleep(8000);

        ctx.Check(TabDockLog.ContainsNewLine(off, "minimized; restoring it inside its tab."),
            "TabDock log gained 'minimized; restoring it inside its tab.'");
        ctx.Check(!NativeMethods.IsIconic(pig.Hwnd), "pig not iconic after restore");
        ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geo), $"pig rect equals host client area within 4px ({geo})");
        Input.ForceForegroundRoot(host);
        int[]? frame = Pixels.CaptureHostScreenArea(host);
        double brightness = frame != null ? Pixels.ComputeAvgBrightness(frame) : -1;
        ctx.Check(brightness > 1.0, $"host brightness > 1.0 ({brightness:F2})");
    }

    // -------------------------------------------------------------------------
    // 10. maximize-repro (also the diagnostic: completes all cycles and dumps everything even on FAIL)
    // -------------------------------------------------------------------------
    private static void MaximizeRepro(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 3;
        GuestInfo guest = SpawnGuest(ctx, opt.Guest);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guest);

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- maximize-repro cycle {cycle}/{cycles} (guest={opt.Guest}) ---");
            long cycOff = TabDockLog.RecordLogLength();

            (double bBase, double vBase) = SampleHost(host);
            GuardedProc.Log($"  cycle {cycle}: baseline brightness={bBase:F2} variance={vBase:F4}");

            ClickMaximizeButton(container);
            Thread.Sleep(1500);
            (double bMax, double vMax) = SampleHost(host);
            // The pig's --pulse toggles its background every 500 ms and the two
            // SampleHost frames are ~1500 ms apart (3 pulse periods), so the
            // frames can land on the same phase and read variance=0 with a
            // perfectly live window (observed: brightness 228, variance 0.0000).
            // Re-sample at a different phase before treating it as frozen.
            for (int retry = 0; retry < 3 && vMax <= 0.005; retry++)
            {
                Thread.Sleep(700);
                (bMax, vMax) = SampleHost(host);
            }
            DumpGeometry(ctx, container, host, guest, $"cycle{cycle} after-maximize");
            bool geoMaxOk = GuestMatchesHost(guest.Hwnd, host, out string geoMax);
            GuardedProc.Log($"  cycle {cycle}: brightnessAfterMax={bMax:F2} varianceAfterMax={vMax:F4}");

            // Restore: recompute the button position from the NEW (maximized) rect.
            ClickMaximizeButton(container);
            Thread.Sleep(1500);
            (double bRest, double vRest) = SampleHost(host);
            for (int retry = 0; retry < 3 && vRest <= 0.005; retry++)
            {
                Thread.Sleep(700);
                (bRest, vRest) = SampleHost(host);
            }
            DumpGeometry(ctx, container, host, guest, $"cycle{cycle} after-restore");
            bool geoRestOk = GuestMatchesHost(guest.Hwnd, host, out string geoRest);
            GuardedProc.Log($"  cycle {cycle}: brightnessAfterRestore={bRest:F2} varianceAfterRestore={vRest:F4}");

            string newLines = TabDockLog.DumpNewLines(cycOff);
            GuardedProc.Log($"  cycle {cycle}: new TabDock log lines (MAXCLICK/STATE/SHEPHERD instrumentation):");
            Console.WriteLine(newLines.Length > 0 ? newLines : "  (none)");
            Console.Out.Flush();

            ctx.Check(bMax > 1.0, $"cycle {cycle}: brightness > 1.0 after maximize ({bMax:F2})");
            ctx.Check(vMax > 0.005, $"cycle {cycle}: variance > 0.005 after maximize ({vMax:F4})");
            ctx.Check(geoMaxOk, $"cycle {cycle}: guest rect equals host client rect within 4px after maximize ({geoMax})");
            ctx.Check(bRest > 1.0, $"cycle {cycle}: brightness > 1.0 after restore ({bRest:F2})");
            ctx.Check(vRest > 0.005, $"cycle {cycle}: variance > 0.005 after restore ({vRest:F4})");
            ctx.Check(geoRestOk, $"cycle {cycle}: guest rect equals host client rect within 4px after restore ({geoRest})");
        }
    }

    // -------------------------------------------------------------------------
    // 11. repeat-cycles
    // -------------------------------------------------------------------------
    private static void RepeatCycles(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 5;
        GuestInfo pig = SpawnPig(ctx, "CYC", "--color", "blue");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- repeat-cycles {cycle}/{cycles} ---");
            long cycOff = TabDockLog.RecordLogLength();

            (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

            ClickMaximizeButton(container);
            Thread.Sleep(1200);
            ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geoMax),
                $"cycle {cycle}: geometry OK after maximize ({geoMax})");

            ClickMaximizeButton(container);
            Thread.Sleep(1200);
            ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geoRest),
                $"cycle {cycle}: geometry OK after restore ({geoRest})");

            ClickTabMenuItem(ctx, container, pig.Title, "Pop out");
            ctx.Check(Util.WaitUntil(() => IsReleased(pig, host), 5000), $"cycle {cycle}: pig released by Pop out");
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
                $"cycle {cycle}: container closed/empty after Pop out");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "EXCEPTION") == 0,
                $"cycle {cycle}: no EXCEPTION lines in TabDock log");

            Thread.Sleep(800); // let the released pig settle before recapturing
        }

        // A persisted group restored from state.json keeps its own empty "Group"
        // container open, so assert no orphaned TDVAL guest windows rather than
        // "no Group window at all".
        ctx.Check(NoOrphanPigWindows(ctx), "final tab state correct: no orphan TDVAL guest windows");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig survived all cycles");
    }

    // -------------------------------------------------------------------------
    // 12. crossfeature
    // -------------------------------------------------------------------------
    private static void CrossFeature(Ctx ctx, Options opt)
    {
        GuestInfo pig1 = SpawnPig(ctx, "XF1", "--pulse", "--color", "white");
        GuestInfo pig2 = SpawnPig(ctx, "XF2", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig1, pig2);

        // Step 1: rename (scenario-1 steps).
        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
        ctx.Check(caption != null && count == 1, $"step rename: caption 'Group' found uniquely (count={count})");
        if (caption != null && count == 1)
        {
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            (int cx, int cy) = Uia.Center(caption);
            bool renamed = false;
            for (int attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                if (FindObstructingWindow(container, cx, cy) != IntPtr.Zero)
                    continue;
                Input.DoubleClickAt(cx, cy);
                Thread.Sleep(300);
                Input.TypeText("TDVAL-Renamed");
                Input.SendKey(Input.VK_RETURN);
                renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Renamed", 2000);
            }
            ctx.Check(renamed, "step rename: container title became 'TDVAL-Renamed'");
        }

        // Step 2: Close window on tab 2 (scenario-3 steps).
        long off2 = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig2.Title, "Close window");
        ctx.Check(PigLog.WaitForPigLine(pig2.Pid, "WM_CLOSE", 3000), "step closewin: pig2 log contains WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => pig2.Proc!.HasExited, 5000), "step closewin: pig2 exited within 5s");
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off2, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off2, "hid itself"), 5000),
            "step closewin: tab torn down (destroy or hide path)");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 1, 3000), "step closewin: one tab remains");

        // Step 3: maximize + restore (scenario-10 single cycle).
        ClickMaximizeButton(container);
        Thread.Sleep(1500);
        (double bMax, double vMax) = SampleHost(host);
        ctx.Check(bMax > 1.0, $"step maximize: brightness > 1.0 ({bMax:F2})");
        ctx.Check(vMax > 0.005, $"step maximize: variance > 0.005 ({vMax:F4})");
        ctx.Check(GuestMatchesHost(pig1.Hwnd, host, out string geoMax), $"step maximize: geometry OK ({geoMax})");

        ClickMaximizeButton(container);
        Thread.Sleep(1500);
        (double bRest, double vRest) = SampleHost(host);
        ctx.Check(bRest > 1.0, $"step restore: brightness > 1.0 ({bRest:F2})");
        ctx.Check(vRest > 0.005, $"step restore: variance > 0.005 ({vRest:F4})");
        ctx.Check(GuestMatchesHost(pig1.Hwnd, host, out string geoRest), $"step restore: geometry OK ({geoRest})");

        // Step 4: pop out the remaining pig — the container should end up empty/closed.
        ClickTabMenuItem(ctx, container, pig1.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleased(pig1, host), 5000), "step popout: pig1 released");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "step popout: container empty/closed");

        // Final checks.
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphan TDVAL windows on the desktop");
        ctx.Check(NativeMethods.IsWindow(ctx.MainHwnd), "TabDock MainWindow still alive/responsive");
    }

    // -------------------------------------------------------------------------
    // 14. hotkey-afterclose (H3): close the launcher with a group still open,
    //     then the global hotkey AND the container '+' button must still open
    //     the picker instead of crashing or doing nothing.
    // -------------------------------------------------------------------------
    private static void HotkeyAfterClose(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "HK", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        // A populated container intentionally hides the launcher. If it is
        // still visible (for example, a future shell policy changes that
        // presentation), close it through its real caption button; otherwise
        // the already-hidden state is the equivalent post-close condition.
        if (NativeMethods.IsWindowVisible(ctx.MainHwnd))
        {
            if (!Input.ForceForeground(ctx.MainHwnd))
                throw new InvalidOperationException("Could not bring the launcher to the foreground — refusing to click blind.");
            NativeMethods.GetWindowRect(ctx.MainHwnd, out NativeMethods.RECT rc);
            double scale = NativeMethods.GetDpiForWindow(ctx.MainHwnd) / 96.0;
            Input.ClickAt(rc.right - (int)(23 * scale), rc.top + (int)(16 * scale));
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(ctx.MainHwnd), 3000),
                "launcher closed after real X click");
        }
        else
        {
            ctx.Check(true, "launcher already hidden while populated container is open (documented design)");
        }
        Thread.Sleep(500);
        ctx.Check(!ctx.TabDock.HasExited, "TabDock still alive (open container keeps the app running)");

        int cycles = opt.Cycles ?? 3;
        for (int i = 1; i <= cycles && !ctx.TabDock.HasExited; i++)
        {
            long off = TabDockLog.RecordLogLength();
            // Dismissing the prior picker legitimately changes the live
            // foreground target. Re-establish the still-verified container
            // before sending the next global hotkey; never send it based on a
            // destroyed picker HWND or a stale launcher target.
            if (!Input.ForceForeground(container))
            {
                // Windows may retain the picker/driver foreground lock after
                // Esc closes the prior picker. A real click on the unobscured
                // container caption is the safe activation fallback; the
                // point is independently checked before the click and the
                // click itself establishes the new verified target.
                NativeMethods.GetWindowRect(container, out NativeMethods.RECT containerRect);
                int activateX = containerRect.left + 100;
                int activateY = containerRect.top + 16;
                if (!EnsureClickable(container, activateX, activateY))
                    throw new InvalidOperationException("Could not bring the live container to the foreground and its caption is obscured; refusing to send the next hotkey.");
                Input.ClickAt(activateX, activateY);
            }
            Input.SendHotkeyCtrlAltG();
            IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 6000);
            bool hotkeySeen = TabDockLog.ContainsNewLine(off, "hotkey Ctrl+Alt+G pressed");
            ctx.Check(picker != IntPtr.Zero,
                $"cycle {i}: picker appeared after hotkey with launcher closed (hotkey log line seen={hotkeySeen})");
            if (picker == IntPtr.Zero)
                break;
            Thread.Sleep(300);
            if (!Input.ForceForeground(picker))
                throw new InvalidOperationException("Could not bring the picker to the foreground; refusing to send Esc.");
            Input.SendKey(Input.VK_ESCAPE); // picker Cancel is IsCancel=True
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000),
                $"cycle {i}: picker dismissed with Esc");
            ctx.Check(!ctx.TabDock.HasExited, $"cycle {i}: TabDock alive after hotkey cycle");
            Thread.Sleep(300);
        }

        // The container's '+' (add window) button funnels through the same
        // ShowCapturePicker path and must also survive the launcher being closed.
        if (!ctx.TabDock.HasExited && NativeMethods.IsWindow(container))
        {
            AutomationElement? containerEl = Uia.FromHwnd(container);
            AutomationElement? addBtn = containerEl == null
                ? null
                : Uia.FindDescendantByName(containerEl, ControlType.Button, "", null, out _);
            ctx.Check(addBtn != null, "container '+' button located via UIA");
            if (addBtn != null)
            {
                // Compute the point before attempting foreground; if
                // ForceForeground fails, fall back to the point-obscured check
                // (same pattern as ClickAddWindowButton) — a real click at an
                // unobstructed point grants foreground via click-to-activate.
                (int ax, int ay) = Uia.Center(addBtn);
                if (!EnsureClickable(container, ax, ay))
                    throw new InvalidOperationException("Could not bring the container to the foreground and its '+' button is obscured — refusing to click blind.");
                Input.ClickAt(ax, ay);
                // The container's "+" opens the INLINE capture surface (the
                // standalone "Capture windows" picker is the launcher/hotkey
                // fallback only), which must still work with the launcher
                // closed; dismiss it with the documented second-click toggle.
                AutomationElement? panelRoot = Uia.FromHwnd(container);
                bool panelOpened = panelRoot != null && Util.WaitUntil(() =>
                    Uia.FindDescendantByName(panelRoot, ControlType.Button, "Add selected", null, out _) != null, 6000);
                ctx.Check(panelOpened, "inline capture surface appeared from container '+' with launcher closed");
                if (panelOpened)
                {
                    Thread.Sleep(300);
                    ClickAddWindowButton(container);
                    Util.WaitUntil(() =>
                    {
                        AutomationElement? r = Uia.FromHwnd(container);
                        if (r == null)
                            return false;
                        return Uia.FindDescendantByName(r, ControlType.Button, "Add selected", null, out _) == null;
                    }, 3000);
                }
            }
        }

        Thread.Sleep(500);
        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive at scenario end (no dispatcher crash)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        if (!ctx.TabDock.HasExited)
        {
            ctx.Check(IsDocked(pig.Hwnd, host), "pig still docked over host at scenario end");
        }
    }

    // -------------------------------------------------------------------------
    // 15. persist-kill (M5): capture + rename must reach state.json without a
    //     clean exit; a force-kill must not lose it; relaunch restores the group;
    //     a later save with the group still empty must not wipe tab metadata.
    //     (StartScenario/Cleanup snapshot and restore the user's state.json.)
    // -------------------------------------------------------------------------
    private static void PersistKill(Ctx ctx, Options opt)
    {
        {
            GuestInfo pig = SpawnPig(ctx, "PK", "--color", "blue");
            (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

            // 1) Durable semantic save: the capture must reach state.json with no rename/exit.
            ctx.Check(Util.WaitUntil(() => StateJsonContains(pig.Title), 5000),
                "state.json contains the captured tab's title within 5s of capture (durable semantic save)");

            // 2) Rename so the restored group is positively identifiable after relaunch.
            AutomationElement containerEl = Uia.FromHwnd(container)
                ?? throw new InvalidOperationException("Container UIA element unavailable.");
            AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
            if (caption == null || count != 1)
                throw new InvalidOperationException($"Container caption 'Group' not found uniquely (count={count}).");
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            (int cx, int cy) = Uia.Center(caption);
            bool renamed = false;
            for (int attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                Input.DoubleClickAt(cx, cy);
                Thread.Sleep(300);
                Input.TypeText("TDVAL-PKGRP");
                Input.SendKey(Input.VK_RETURN);
                renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-PKGRP", 2000);
            }
            ctx.Check(renamed, "group renamed to TDVAL-PKGRP");
            ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-PKGRP"), 3000),
                "state.json contains the group rename");

            // 3) Force-kill TabDock: no shutdown handler runs; the file must already be durable.
            GuardedProc.Log("  Force-killing TabDock (Process.Kill, no graceful shutdown).");
            ctx.TabDock.Kill();
            ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
            Thread.Sleep(1000);
            ctx.Check(StateJsonContains("TDVAL-PKGRP") && StateJsonContains(pig.Title),
                "state.json survived the force-kill with group name and tab metadata");
            // The captured pig HWND dies with the host window tree (documented limitation) — not asserted.

            // 4) Relaunch: the group must come back as a named (empty) container shell.
            Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
            });
            ctx.TabDock = td2;
            ctx.TabDockPid = (uint)td2.Id;
            TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
            // The restored group's container opens during startup and HIDES the
            // launcher within ~50ms, so a visible "TabDock"-titled window may
            // never exist at poll time — wait for ANY visible top-level window
            // of the process instead (liveness), then check the restored
            // container by its renamed title below.
            ctx.MainHwnd = IntPtr.Zero;
            bool relaunched = Util.WaitUntil(() =>
                Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true).Count > 0, 20000);
            ctx.Check(relaunched, "TabDock relaunched (visible window up)");
            IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-PKGRP", 10000);
            ctx.Check(restored != IntPtr.Zero, "restored container 'TDVAL-PKGRP' opened after relaunch");
            if (restored != IntPtr.Zero)
                RememberContainer(ctx, restored);

            // 5) A clean-exit save with the group still empty must NOT wipe the
            //    persisted tab metadata (layout intent).
            Thread.Sleep(1000);
            ctx.Check(CloseAllWindowsUntilExit(ctx.TabDockPid, ctx.TabDock, 8000),
                "relaunched TabDock exited cleanly (close waves include the reappearing launcher)");
            ctx.Check(StateJsonContains("TDVAL-PKGRP"), "group name survived the clean-exit save");
            ctx.Check(StateJsonContains(pig.Title),
                "persisted tab metadata survived a save with the group empty (not wiped)");
        }
    }

    // -------------------------------------------------------------------------
    // 19. closegroupprompt: the container's Closing handler shows a Yes/No/
    //     Cancel MessageBox when it still has tabs (ContainerWindow.xaml.cs
    //     ContainerWindow_Closing). Cancel must leave the container and its
    //     tabs untouched; Yes must actually close (exit) every captured guest,
    //     not just release it — the one path no other scenario exercises,
    //     since cleanup's own MessageBox handling always clicks "No".
    // -------------------------------------------------------------------------
    private static void CloseGroupPrompt(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CGA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "CGB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        // --- Cancel: container and both tabs must be completely unaffected. ---
        VerifiedWindowOps.PostMessage(container, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg1 = Discover.FindMessageBox(ctx.TabDockPid, "Close group");
        Util.WaitUntil(() => (dlg1 = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        ctx.Check(dlg1 != IntPtr.Zero, "Close-group prompt appeared on WM_CLOSE with tabs present");
        if (dlg1 != IntPtr.Zero)
        {
            IntPtr cancelBtn = Discover.FindChildWindowByText(dlg1, new[] { "Cancel" });
            ctx.Check(cancelBtn != IntPtr.Zero, "prompt has a Cancel button");
            if (cancelBtn != IntPtr.Zero)
            {
                if (!Input.ForceForeground(dlg1))
                    throw new InvalidOperationException("Could not bring the cancel prompt to the foreground; refusing to click.");
                NativeMethods.GetWindowRect(cancelBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
            Util.WaitUntil(() => !NativeMethods.IsWindow(dlg1), 3000);
        }
        Thread.Sleep(400);
        ctx.Check(NativeMethods.IsWindow(container), "Cancel: container still open");
        ctx.Check(TabCount(container) == 2, "Cancel: both tabs still present");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "Cancel: both pigs still alive");
        ctx.Check((IsDocked(pigA.Hwnd, host) || IsReleasedAndHidden(pigA.Hwnd))
                && (IsDocked(pigB.Hwnd, host) || IsReleasedAndHidden(pigB.Hwnd)),
            "Cancel: both pigs still captured (docked over host or hidden inactive tab)");

        // --- Yes: must actually close (exit) both captured guests. ---
        long off = TabDockLog.RecordLogLength();
        VerifiedWindowOps.PostMessage(container, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg2 = IntPtr.Zero;
        Util.WaitUntil(() => (dlg2 = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        ctx.Check(dlg2 != IntPtr.Zero, "Close-group prompt appeared again on second WM_CLOSE");
        if (dlg2 != IntPtr.Zero)
        {
            IntPtr yesBtn = Discover.FindChildWindowByText(dlg2, new[] { "&Yes", "Yes" });
            ctx.Check(yesBtn != IntPtr.Zero, "prompt has a Yes button");
            if (yesBtn != IntPtr.Zero)
            {
                if (!Input.ForceForeground(dlg2))
                    throw new InvalidOperationException("Could not bring the yes prompt to the foreground; refusing to click.");
                NativeMethods.GetWindowRect(yesBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
        }
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000), "Yes: container closed");
        ctx.Check(Util.WaitUntil(() => pigA.Proc!.HasExited, 5000), "Yes: pigA actually exited (not just released)");
        ctx.Check(Util.WaitUntil(() => pigB.Proc!.HasExited, 5000), "Yes: pigB actually exited (not just released)");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, "no EXCEPTION lines across the whole prompt sequence");
    }

    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // 19b. exitpopulated (M6): the launcher's "Exit" button (bound to
    //     ExitCommand -> App.OnExitRequested -> Application.Shutdown) must shut
    //     the whole app down cleanly. The launcher is HIDDEN while a container
    //     is open (documented design — docs/ARCHITECTURE.md: "The launcher is
    //     hidden while a container is open and remains only as the no-group/
    //     global-hotkey fallback"), so the reachable exit flow is: close the
    //     populated container via its × -> "Close group" prompt -> Yes -> the
    //     launcher reappears -> Exit. The original M6 stall (ContainerWindow_
    //     Closing's Yes/No/Cancel modal firing during Shutdown with nobody left
    //     to answer, stalling into a zombie) is guarded by IsAppShuttingDown in
    //     both App.OnExitRequested and Application_SessionEnding; this scenario
    //     verifies the end-to-end exit path and asserts no stranded MessageBox.
    // -------------------------------------------------------------------------
    private static void ExitPopulated(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "EXP", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        // Design contract: the launcher hides while a container is open (the
        // old scenario premise — launcher Exit with a populated group open —
        // is unreachable by design since the launcher-hide landed).
        ctx.Check(!NativeMethods.IsWindowVisible(ctx.MainHwnd),
            "launcher hidden while the populated container is open (documented design)");

        // Close the populated container with a REAL CLICK on its caption
        // close button (CaptionButtonStyle index 0 = Close). A posted WM_CLOSE
        // raises the prompt without any input-grant: the modal cannot take the
        // foreground under Windows' foreground lock, and the docked guest
        // (paired above the container) then covers the dialog's buttons
        // (observed live: WindowFromPoint at the Yes button resolves to the
        // guest). A real click grants the app foreground rights first, so the
        // dialog appears on top, exactly as for a human user.
        (int closeX, int closeY) = CaptionButtonCenterFromRight(container, 0);
        if (!EnsureClickable(container, closeX, closeY))
            throw new InvalidOperationException("Could not bring the container to the foreground and its close button is obscured — refusing to click blind.");
        long off = TabDockLog.RecordLogLength();
        Input.ClickAt(closeX, closeY);
        IntPtr dlg = IntPtr.Zero;
        Util.WaitUntil(() => (dlg = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        ctx.Check(dlg != IntPtr.Zero, "Close-group prompt appeared on caption close with tabs present");
        if (dlg == IntPtr.Zero)
            return;
        // The first click on a just-shown modal can be consumed by the
        // activation itself (observed live), so retry the Yes click until the
        // dialog actually closes (bounded, no infinite loop).
        IntPtr yesBtn = Discover.FindChildWindowByText(dlg, new[] { "&Yes", "Yes" });
        ctx.Check(yesBtn != IntPtr.Zero, "prompt has a Yes button");
        bool yesAccepted = false;
        for (int attempt = 0; attempt < 3 && !yesAccepted; attempt++)
        {
            IntPtr curDlg = Discover.FindMessageBox(ctx.TabDockPid, "Close group");
            if (curDlg == IntPtr.Zero)
                break;
            IntPtr curYes = Discover.FindChildWindowByText(curDlg, new[] { "&Yes", "Yes" });
            if (curYes == IntPtr.Zero || !Input.ForceForeground(curDlg))
            {
                Thread.Sleep(300);
                continue;
            }
            NativeMethods.GetWindowRect(curYes, out NativeMethods.RECT rc);
            Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            yesAccepted = Util.WaitUntil(() => !NativeMethods.IsWindow(curDlg), 2000);
        }
        ctx.Check(yesAccepted, "Yes click dismissed the Close-group prompt");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000), "Yes: container closed");
        ctx.Check(Util.WaitUntil(() => pig.Proc!.HasExited, 5000), "Yes: pig actually exited (not just released)");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, "no EXCEPTION lines across the prompt sequence");

        // The launcher reappears once the last container is gone, and its Exit
        // button now shuts the app down with no container left to prompt over.
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(ctx.MainHwnd), 5000),
            "launcher reappears after the last container closes");

        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring the launcher to the foreground — refusing to click blind.");

        AutomationElement? mainEl = Uia.FromHwnd(ctx.MainHwnd);
        AutomationElement? exitBtn = mainEl == null
            ? null
            : Uia.FindDescendantByName(mainEl, ControlType.Button, "Exit", null, out int count);
        ctx.Check(exitBtn != null, "launcher Exit button located via UIA");
        if (exitBtn == null)
            return;

        // The launcher just reappeared; UIA can retain the pre-hide button
        // rectangle for a dispatcher turn. Derive a fresh point from the live
        // native frame (the Exit button is 80 DIP wide with a 16 DIP right and
        // bottom margin) and still require the point to resolve to the verified
        // launcher before every real click.
        int ex = 0;
        int ey = 0;
        bool exited = false;
        for (int attempt = 0; attempt < 3 && !exited; attempt++)
        {
            NativeMethods.GetWindowRect(ctx.MainHwnd, out NativeMethods.RECT mainRect);
            double mainScale = NativeMethods.GetDpiForWindow(ctx.MainHwnd) / 96.0;
            ex = mainRect.right - (int)(56 * mainScale);
            ey = mainRect.bottom - (int)(32 * mainScale);
            if (!EnsureClickable(ctx.MainHwnd, ex, ey))
            {
                Thread.Sleep(300);
                continue;
            }
            Input.ClickAt(ex, ey);
            exited = Util.WaitUntil(() => ctx.TabDock.HasExited, 5000);
        }
        IntPtr strandedDialog = exited ? IntPtr.Zero : Discover.FindMessageBox(ctx.TabDockPid, null);
        ctx.Check(exited, "TabDock process exited within 15s of clicking Exit (no blocking modal)");
        ctx.Check(strandedDialog == IntPtr.Zero, "no stranded MessageBox left open blocking shutdown");
    }
    // H6 regression: minimizing a populated container via its OWN minimize button
    // must retain every tab; on restore the active guest is re-docked. The H6 bug
    // misclassified the active guest's minimize-hide as a tray-close and released
    // its tab hidden. Pig-only, hermetic → joins `all`.
    // -------------------------------------------------------------------------
    private static void ContainerMinimizeRetainsTabs(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CMA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "CMB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        long off = TabDockLog.RecordLogLength();
        // Real click on the container's own minimize button. The container was
        // JUST created and its WM_ACTIVATE -> 120ms BringToFront reassert
        // (ContainerWindow.WndProc) can race the click: if the shepherd puts the
        // docked guest on top between ForceForeground and the click, the first
        // caption click is consumed as an activation instead of reaching the
        // button. Retry once after re-asserting foreground — a real user's
        // second click on the now-active window hits the button.
        ClickMinimizeButton(container);
        if (!Util.WaitUntil(() => NativeMethods.IsIconic(container), 1500))
        {
            GuardedProc.Log("  container not minimized after first caption click (foreground reassert race) — re-asserting foreground and clicking again.");
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not re-verify the container before retrying minimize.");
            Thread.Sleep(250);
            ClickMinimizeButton(container);
        }
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container), 3000), "container minimized after clicking its minimize button");
        Thread.Sleep(500);
        ctx.Check(TabCount(container) == 2, "2 tabs still present while the container is minimized");

        // Restoring is not the path that regressed (minimizing was), so a plain
        // ShowWindow(SW_RESTORE) suffices for the restore half.
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container), 3000), "container restored (no longer minimized)");
        Thread.Sleep(800);

        ctx.Check(TabCount(container) == 2, "2 tabs retained after minimize/restore (H6 regression)");
        bool aDocked = IsDocked(pigA.Hwnd, host);
        bool bDocked = IsDocked(pigB.Hwnd, host);
        bool aHidden = IsReleasedAndHidden(pigA.Hwnd);
        bool bHidden = IsReleasedAndHidden(pigB.Hwnd);
        ctx.Check((aDocked && bHidden) || (bDocked && aHidden),
            "after restore exactly one guest is docked over the host and the other is hidden (tab set intact)");
        ctx.Check(TabDockLog.CountNewLines(off, "hid itself (tray-style close)") == 0,
            "zero 'hid itself (tray-style close)' release lines from the container minimize (H6 regression)");
        ctx.Check(TabDockLog.CountNewLines(off, "destroyed; removing its tab") == 0,
            "zero 'destroyed; removing its tab' lines from the container minimize");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // Popping out an INACTIVE tab must not disturb the active tab (GroupViewModel
    // fix). 3 pigs in one group; make the rightmost tab (index 2) active; pop out
    // the LEFTMOST (inactive) tab via the context menu; assert the active tab is
    // unchanged and its guest is still docked.
    // -------------------------------------------------------------------------
    private static void PopOutInactiveKeepsActive(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "POA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "POB", "--color", "green");
        GuestInfo pigC = SpawnPig(ctx, "POC", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Find the rightmost (index 2) and leftmost (index 0) tabs by their rects.
        GuestInfo? rightmost = null;
        double rightmostX = double.MinValue;
        GuestInfo? leftmost = null;
        double leftmostX = double.MaxValue;
        foreach (GuestInfo g in new[] { pigA, pigB, pigC })
        {
            AutomationElement? t = FindTabText(container, g.Title, out int c);
            if (t == null || c != 1)
                throw new InvalidOperationException($"Tab for '{g.Title}' not found uniquely (count={c}).");
            double x = Uia.GetElementRect(t).X;
            if (x > rightmostX) { rightmostX = x; rightmost = g; }
            if (x < leftmostX) { leftmostX = x; leftmost = g; }
        }
        if (rightmost == null || leftmost == null)
            throw new InvalidOperationException("Could not determine tab positions.");

        // Make the rightmost tab (index 2) active by real-clicking it.
        AutomationElement? tabActive = FindTabText(container, rightmost.Title, out _);
        (int tx, int ty) = Uia.Center(tabActive!);
        Input.ClickAt(tx, ty);
        Thread.Sleep(500);
        ctx.Check(Util.WaitUntil(() => IsDocked(rightmost.Hwnd, host), 3000),
            $"rightmost tab ('{rightmost.Title}') is active (docked) after clicking it");

        // Pop out the LEFTMOST (inactive) tab.
        ClickTabMenuItem(ctx, container, leftmost.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(leftmost.Hwnd, host), 5000), "inactive leftmost tab popped out (released standalone)");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 2, 3000), "container now holds 2 tabs");

        ctx.Check(IsDocked(rightmost.Hwnd, host),
            $"active tab unchanged — the former rightmost tab ('{rightmost.Title}') is still docked over host");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // An already-captured window cannot be captured twice: the picker excludes
    // windows already in a group (CapturePickerViewModel.Refresh), so reopening
    // the picker must NOT offer the captured pig's title, and the group is
    // unchanged after dismissal.
    // -------------------------------------------------------------------------
    private static void DoubleCaptureRefused(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "DC", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(TabCount(container) == 1, "1 tab after capture");
        int tabsBefore = TabCount(container);

        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring the launcher to the foreground — refusing to click blind.");
        Thread.Sleep(300);
        Input.SendHotkeyCtrlAltG();
        IntPtr pickerHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        if (pickerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("'Capture windows' picker did not appear within 10s.");
        AutomationElement? picker = Uia.FromHwnd(pickerHwnd);
        if (picker == null)
            throw new InvalidOperationException("Picker HWND found but UIA FromHandle failed.");
        if (!Input.ForceForeground(pickerHwnd))
            throw new InvalidOperationException("Could not bring the capture picker to the foreground — refusing to click blind.");
        Thread.Sleep(800);

        // The captured pig's title must be absent from the picker's window list.
        int titleMatches = 0;
        Uia.FindDescendantByName(picker, ControlType.Text, null, pig.Title, out titleMatches);
        ctx.Check(titleMatches == 0,
            $"already-captured pig '{pig.Title}' is NOT offered by the reopened picker (double-capture guard)");

        Input.SendKey(Input.VK_ESCAPE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(pickerHwnd), 3000), "picker dismissed with Esc");
        ctx.Check(TabCount(container) == tabsBefore, "group unchanged after the reopened picker was dismissed");
        ctx.Check(IsDocked(pig.Hwnd, host), "captured pig still docked over host (group intact)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // A persisted non-zero active-tab index must survive restore and the first
    // post-restore save (PersistenceService.Save: ActiveIndex = Members.Count > 0
    // ? ActiveIndex : PersistedActiveIndex). Extends the persist-kill pattern.
    // -------------------------------------------------------------------------
    private static void PersistActiveTabIndex(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "ATIA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "ATIB", "--color", "green");
        GuestInfo pigC = SpawnPig(ctx, "ATIC", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Rename the group so the restored shell is positively identifiable.
        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int capCount);
        if (caption == null || capCount != 1)
            throw new InvalidOperationException($"Container caption 'Group' not found uniquely (count={capCount}).");
        (int cx, int cy) = Uia.Center(caption);
        bool renamed = false;
        for (int attempt = 0; attempt < 3 && !renamed; attempt++)
        {
            Input.DoubleClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText("TDVAL-ATIIDX");
            Input.SendKey(Input.VK_RETURN);
            renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-ATIIDX", 2000);
        }
        ctx.Check(renamed, "group renamed to TDVAL-ATIIDX");

        // Make the rightmost tab (index 2) active — the active index is now > 0.
        GuestInfo? rightmost = null;
        double rightmostX = double.MinValue;
        foreach (GuestInfo g in new[] { pigA, pigB, pigC })
        {
            AutomationElement? t = FindTabText(container, g.Title, out int c);
            if (t == null || c != 1)
                throw new InvalidOperationException($"Tab for '{g.Title}' not found uniquely (count={c}).");
            double x = Uia.GetElementRect(t).X;
            if (x > rightmostX) { rightmostX = x; rightmost = g; }
        }
        if (rightmost == null)
            throw new InvalidOperationException("Could not determine the rightmost tab.");
        AutomationElement? tabActive = FindTabText(container, rightmost.Title, out _);
        (int tx, int ty) = Uia.Center(tabActive!);
        Input.ClickAt(tx, ty);
        Thread.Sleep(500);
        ctx.Check(Util.WaitUntil(() => IsDocked(rightmost.Hwnd, host), 3000),
            $"rightmost tab active (index 2) — docked guest is '{rightmost.Title}'");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("\"ActiveIndex\": 2"), 5000),
            "state.json recorded active-tab index 2 (durable semantic save)");

        // Force-kill, relaunch, and let the first post-restore save run: the index
        // must NOT be reset to 0.
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);
        Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDock = td2;
        ctx.TabDockPid = (uint)td2.Id;
        TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
        // The restored group's container opens during startup and HIDES the
        // launcher within ~50ms, so a visible "TabDock"-titled window may never
        // exist at poll time (observed live as an intermittent flake) — wait
        // for ANY visible top-level window of the process instead (liveness),
        // then check the restored container by its renamed title below.
        ctx.MainHwnd = IntPtr.Zero;
        bool relaunched = Util.WaitUntil(() =>
            Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true).Count > 0, 20000);
        ctx.Check(relaunched, "TabDock relaunched (visible window up)");
        IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-ATIIDX", 10000);
        ctx.Check(restored != IntPtr.Zero, "restored container 'TDVAL-ATIIDX' opened after relaunch");
        if (restored != IntPtr.Zero)
            RememberContainer(ctx, restored);

        Thread.Sleep(1000); // let the restored empty shell settle
        ctx.Check(CloseAllWindowsUntilExit(ctx.TabDockPid, ctx.TabDock, 8000),
            "relaunched TabDock exited cleanly (close waves include the reappearing launcher)");
        ctx.Check(StateJsonContains("TDVAL-ATIIDX"), "group name survived the clean-exit save");
        ctx.Check(StateJsonContains("\"ActiveIndex\": 2"),
            "active-tab index 2 survived the first post-restore save (not reset to 0)");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // RemoveDeadMember guard (App.xaml.cs): a restored group's persisted layout
    // (name + original tab metadata) must survive its re-captured live member
    // being destroyed (WM_CLOSE) or tray-hidden (--hide-on-close) — the path
    // persist-kill's empty-shell clean exit never exercises. Each teardown kind
    // is a phase; a kill+relaunch between re-opens the emptied shell's container.
    // -------------------------------------------------------------------------
    private static void RestoredGroupSurvivesMemberReclose(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "RS", "--color", "red");
        (IntPtr container1, _) = CaptureIntoGroup(ctx, pigA);
        ctx.Check(Util.WaitUntil(() => StateJsonContains(pigA.Title), 5000),
            "state.json contains the captured tab's title (durable semantic save)");

        AutomationElement containerEl = Uia.FromHwnd(container1)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int capCount);
        if (caption == null || capCount != 1)
            throw new InvalidOperationException($"Container caption 'Group' not found uniquely (count={capCount}).");
        if (!Input.ForceForeground(container1))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        (int cx, int cy) = Uia.Center(caption);
        bool renamed = false;
        for (int attempt = 0; attempt < 3 && !renamed; attempt++)
        {
            Input.DoubleClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText("TDVAL-RSMR");
            Input.SendKey(Input.VK_RETURN);
            renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container1) == "TDVAL-RSMR", 2000);
        }
        ctx.Check(renamed, "group renamed to TDVAL-RSMR");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-RSMR"), 3000), "state.json contains the group rename");

        Process Relaunch() => GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });

        // Phase 2a: re-capture a pig into the restored shell, then DESTROY it.
        GuardedProc.Log("  Force-killing TabDock (Process.Kill, no graceful shutdown).");
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);
        ctx.TabDock = Relaunch();
        ctx.TabDockPid = (uint)ctx.TabDock.Id;
        TestRunProvenance.RegisterLaunchedProcess(ctx.TabDock, "TabDockUnderTest", out _);
        // The restored group's container opens during startup and HIDES the
        // launcher within ~50ms, so a visible "TabDock"-titled window may never
        // exist at poll time (observed live as an intermittent flake) — wait
        // for ANY visible top-level window of the process instead (liveness),
        // then check the restored container by its renamed title below.
        ctx.MainHwnd = IntPtr.Zero;
        bool relaunched = Util.WaitUntil(() =>
            Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true).Count > 0, 20000);
        ctx.Check(relaunched, "TabDock relaunched (visible window up)");
        IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-RSMR", 10000);
        ctx.Check(restored != IntPtr.Zero, "restored container 'TDVAL-RSMR' opened after relaunch");
        if (restored != IntPtr.Zero)
            RememberContainer(ctx, restored);
        IntPtr restoredHost = IntPtr.Zero;
        Util.WaitUntil(() => (restoredHost = Discover.FindChildByClass(restored, ContentHostClass)) != IntPtr.Zero, 5000, 150);

        GuestInfo pigB = SpawnPig(ctx, "RS2", "--color", "green");
        CaptureIntoExistingGroupViaAddButton(ctx, restored, restoredHost, pigB);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigB.Hwnd, restoredHost), 5000),
            $"pig '{pigB.Title}' re-captured into the restored shell");
        ctx.Check(TabCount(restored) == 1, "restored shell holds 1 live tab");

        if (pigB.Identity is WindowIdentity pigBIdentity)
            VerifiedWindowOps.PostMessage(pigBIdentity, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        ctx.Check(Util.WaitUntil(() => pigB.Proc!.HasExited, 5000), "re-captured pig exited after WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-RSMR"), 5000),
            "group name survived the re-captured member being destroyed (RemoveDeadMember guard)");
        ctx.Check(Util.WaitUntil(() => StateJsonContains(pigA.Title), 5000),
            "original tab metadata survived the re-captured member being destroyed (not wiped)");

        // Phase 2b: relaunch again, re-capture, tray-hide via --hide-on-close.
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed (phase 2b)");
        Thread.Sleep(1000);
        ctx.TabDock = Relaunch();
        ctx.TabDockPid = (uint)ctx.TabDock.Id;
        TestRunProvenance.RegisterLaunchedProcess(ctx.TabDock, "TabDockUnderTest", out _);
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
        IntPtr restored2 = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-RSMR", 10000);
        ctx.Check(restored2 != IntPtr.Zero, "restored container 'TDVAL-RSMR' reopened after second relaunch");
        if (restored2 != IntPtr.Zero)
            RememberContainer(ctx, restored2);
        IntPtr restoredHost2 = IntPtr.Zero;
        Util.WaitUntil(() => (restoredHost2 = Discover.FindChildByClass(restored2, ContentHostClass)) != IntPtr.Zero, 5000, 150);

        GuestInfo pigC = SpawnPig(ctx, "RS3", "--color", "blue", "--hide-on-close", "--close-button");
        CaptureIntoExistingGroupViaAddButton(ctx, restored2, restoredHost2, pigC);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigC.Hwnd, restoredHost2) || IsReleasedAndHidden(pigC.Hwnd), 5000),
            $"pig '{pigC.Title}' re-captured into the restored shell (phase 2b)");
        AutomationElement? tabC = FindTabText(restored2, pigC.Title, out int tabCount);
        if (tabC == null || tabCount != 1)
            throw new InvalidOperationException($"Tab for '{pigC.Title}' not found uniquely (count={tabCount}).");
        (int txc, int tyc) = Uia.Center(tabC);
        Input.ClickAt(txc, tyc);
        Thread.Sleep(500);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigC.Hwnd, restoredHost2), 3000), "pigC is the active (docked) tab");

        AutomationElement pigEl = Uia.FromHwnd(pigC.Hwnd) ?? throw new InvalidOperationException("Pig UIA element unavailable.");
        AutomationElement? closeBtn = Uia.FindDescendantByName(pigEl, ControlType.Button, "X-CLOSE", null, out int closeCount);
        if (closeBtn == null || closeCount != 1)
            throw new InvalidOperationException($"X-CLOSE button not found uniquely in pig (count={closeCount}).");
        if (!Input.ForceForegroundRoot(pigC.Hwnd))
            throw new InvalidOperationException("Could not bring the captured pig to the foreground — refusing to click blind.");
        (int bx, int by) = Uia.Center(closeBtn);
        Input.ClickAt(bx, by);
        ctx.Check(Util.WaitUntil(() => IsReleasedAndHidden(pigC.Hwnd), 5000), "pigC hidden (tray-style close)");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-RSMR"), 5000),
            "group name survived the re-captured member being tray-hidden (RemoveDeadMember guard)");
        ctx.Check(Util.WaitUntil(() => StateJsonContains(pigA.Title), 5000),
            "original tab metadata survived the re-captured member being tray-hidden (not wiped)");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // Self-minimize restore-timer guard: a captured guest minimized via its OWN
    // native title-bar minimize button arms a 200ms deferred restore check
    // (ContainerWindow.RestoreMinimizedWindow). The "release before the check
    // fires" variant is unreachable with real input (the harness needs seconds
    // to interact against a 200ms timer — see the amended spec's NOTES), so the
    // scenario exercises the restore-first branch: the still-captured guest is
    // restored inside its tab, then released via container close, and the guard
    // (ContainerWindow.xaml.cs stops the timers and nulls _shepherdActiveWindow
    // on close) must keep the released guest at its pre-capture placement —
    // not repositioned by any restore/reassert machinery tied to the now-defunct
    // container.
    // -------------------------------------------------------------------------
    private static void SelfMinimizeTimerVsTeardown(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SMT", "--color", "red");
        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT preCaptureRect);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked over host right after capture");

        // Minimize the guest via its OWN native minimize button (real input at
        // the guest's caption chrome) — arms the 200ms RestoreMinimizedWindow
        // check. With real input the release below cannot beat that timer (the
        // harness needs seconds to find and click the tab's menu), so the
        // restore legitimately wins on every machine; the stale-timer guard is
        // still exercised because the restore's own re-layout churn must not
        // reposition the guest after its tab is gone.
        ClickNativeMinimizeButton(pig.Hwnd);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(pig.Hwnd), 3000),
            "pig minimized via its own native minimize button");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(pig.Hwnd) && IsDocked(pig.Hwnd, host), 5000),
            "app restored the self-minimized guest inside its tab within 5s");
        ctx.Check(TabCount(container) == 1, "still 1 tab after self-minimize + restore");

        // Release via container close. The restore re-asserts the active guest's
        // foreground repeatedly for a second or two (container WM_ACTIVATE ->
        // BringToFront churn) and that churn CLOSES a context menu opened while
        // it is active (proven: a menu opened at T and a bring-to-front at T+80ms
        // closed it at T+230ms). The churn's start time after the restore is
        // unbounded (observed ~1.5s in run8, ~8.4s in run9 — after an 8s settle
        // poll had given up), so no fixed wait can reliably precede it; popping
        // out via the context menu is therefore not a dependable release here.
        // The amended spec's release step explicitly allows "or its container is
        // closed": WM_CLOSE -> the modal "Close group" prompt -> "No" (release
        // windows back to standalone) is churn-robust — the modal disables the
        // container so no further WM_ACTIVATE reaches it (the reassert loop
        // dies), and a modal dialog does not auto-close on focus loss the way a
        // popup menu does.
        VerifiedWindowOps.PostMessage(container, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg = IntPtr.Zero;
        var promptSw = Stopwatch.StartNew();
        bool noClicked = false;
        while (!noClicked && promptSw.ElapsedMilliseconds < 5000)
        {
            if (!NativeMethods.IsWindow(container))
                break;
            dlg = Discover.FindMessageBox(ctx.TabDockPid, "Close group");
            if (dlg == IntPtr.Zero)
            {
                Thread.Sleep(200);
                continue;
            }
            IntPtr noBtn = Discover.FindChildWindowByText(dlg, new[] { "&No", "No" });
            if (noBtn == IntPtr.Zero)
            {
                Thread.Sleep(200);
                continue;
            }
            if (!Input.ForceForeground(dlg))
                throw new InvalidOperationException("Could not bring the close prompt to the foreground; refusing to click.");
            NativeMethods.GetWindowRect(noBtn, out NativeMethods.RECT rc);
            Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            noClicked = true;
            Thread.Sleep(400);
        }
        ctx.Check(noClicked, "Close-group prompt 'No' (release to standalone) was clicked");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000),
            "container closed after 'No' on Close-group prompt (guest released)");

        // Wait PAST the 200ms restore-check delay with generous headroom.
        Thread.Sleep(600);

        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT rcNow);
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process still alive after teardown");
        ctx.Check(NativeMethods.IsWindowVisible(pig.Hwnd) && !NativeMethods.IsIconic(pig.Hwnd),
            "released pig is visible and NOT iconic after the restore-check delay would have fired");
        ctx.Check(Util.RectNear(preCaptureRect, rcNow, 10),
            $"released pig still at its pre-capture placement — not repositioned by a stale restore timer (before {Util.FormatRect(preCaptureRect)}, now {Util.FormatRect(rcNow)})");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // The launcher's "No groups yet" empty-state hint must be visible with zero
    // groups and hidden once a group exists (MainWindow.xaml DataTrigger on
    // Groups.Count). Pure UIA read — no input is ever sent to the hint element.
    // -------------------------------------------------------------------------
    private static void LauncherEmptyStateHint(Ctx ctx, Options opt)
    {
        AutomationElement? mainEl = Uia.FromHwnd(ctx.MainHwnd);
        ctx.Check(mainEl != null, "launcher MainWindow UIA element available");
        int hintCount = 0;
        AutomationElement? hint = mainEl == null
            ? null
            : Uia.FindDescendantByName(mainEl, ControlType.Text, null, "No groups yet", out hintCount);
        ctx.Check(hint != null && hintCount == 1, $"launcher empty-state hint 'No groups yet' found uniquely (count={hintCount})");

        bool hintVisible = false;
        if (hint != null)
        {
            try
            {
                hintVisible = !hint.Current.IsOffscreen
                    && !hint.Current.BoundingRectangle.IsEmpty
                    && hint.Current.BoundingRectangle.Width > 0
                    && hint.Current.BoundingRectangle.Height > 0;
            }
            catch (Exception ex)
            {
                GuardedProc.Log($"  LauncherEmptyStateHint: reading hint UIA state threw: {ex.Message}");
            }
        }
        ctx.Check(hintVisible, "empty-state hint is visible (not offscreen/collapsed) with zero groups");

        // Capture a pig into a new group; the hint must disappear.
        GuestInfo pig = SpawnPig(ctx, "LEH", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(TabCount(container) == 1, "1 tab after capture");

        AutomationElement? mainEl2 = Uia.FromHwnd(ctx.MainHwnd);
        int hintCount2 = 0;
        AutomationElement? hint2 = mainEl2 == null
            ? null
            : Uia.FindDescendantByName(mainEl2, ControlType.Text, null, "No groups yet", out hintCount2);
        bool hintGone = false;
        try
        {
            hintGone = hint2 == null || hintCount2 == 0
                || hint2.Current.IsOffscreen
                || hint2.Current.BoundingRectangle.IsEmpty
                || hint2.Current.BoundingRectangle.Width == 0
                || hint2.Current.BoundingRectangle.Height == 0;
        }
        catch (Exception ex)
        {
            hintGone = true;
            GuardedProc.Log($"  LauncherEmptyStateHint: hint2 UIA read threw: {ex.Message}");
        }
        ctx.Check(hintGone, "empty-state hint is no longer visible once a group exists");
        ctx.Check(IsDocked(pig.Hwnd, host), "captured pig docked over host");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // 26. directclick-foreground-pairing: verifies
    //     ContainerWindow.PairZOrderBehindGuest (wired in App.xaml.cs's
    //     WindowForegroundChanged handler) — when the user clicks the guest
    //     DIRECTLY (never touching TabDock's own tab-strip/title-bar UI),
    //     keyboard input must still work and the container must re-pair
    //     immediately behind the guest in z-order.
    // -------------------------------------------------------------------------
    private static void DirectClickForegroundPairing(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "DCFP", "--color", "blue", "--text-box");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked over host right after capture");

        // Steal foreground with a genuinely external app TabDock never
        // captured — the driver's own console window, falling back to a
        // throwaway Notepad (never the pig captured above).
        IntPtr externalHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (externalHwnd == IntPtr.Zero)
        {
            GuestInfo externalNotepad = SpawnNotepad(ctx);
            externalHwnd = externalNotepad.Hwnd;
        }
        ctx.Check(Input.ForceForegroundRoot(externalHwnd), "external window accepted foreground steal");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == externalHwnd, 1500),
            "the intended external HWND is foreground before the direct click");

        // The real assertion under test: click the pig's own docked content
        // area DIRECTLY — deliberately NOT via Input.ForceForeground/
        // ForceForegroundRoot and NOT via the tab strip. Windows' own
        // click-to-activate must hand real foreground to the pig's HWND from
        // raw SendInput alone, exactly like a human clicking the visible
        // content of a docked tab.
        NativeMethods.RECT dockedRect = Discover.GetClientScreenRect(host);
        int cx = dockedRect.left + dockedRect.Width / 2;
        int cy = dockedRect.top + dockedRect.Height / 2;
        // A foreground steal must not also obscure the guest's click point;
        // otherwise the raw click correctly activates the unrelated window and
        // the scenario never reaches the product transition under test. Move
        // the external window without changing its z-order or activation.
        NativeMethods.GetWindowRect(externalHwnd, out NativeMethods.RECT externalRect);
        int virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualRight = virtualLeft + NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualBottom = virtualTop + NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int externalX = dockedRect.right + 20;
        int externalY = dockedRect.top;
        if (externalX + externalRect.Width > virtualRight)
            externalX = dockedRect.left - externalRect.Width - 20;
        if (externalX < virtualLeft)
            externalX = virtualLeft;
        if (externalY + externalRect.Height > virtualBottom)
            externalY = Math.Max(virtualTop, dockedRect.top - externalRect.Height - 20);
        if (!Discover.TryCaptureIdentity(externalHwnd, out WindowIdentity externalIdentity))
            throw new InvalidOperationException("External foreground window failed identity verification; refusing to reposition it.");
        VerifiedWindowOps.SetWindowPos(externalIdentity, IntPtr.Zero, externalX, externalY,
            0, 0, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        // Preserve the real external-foreground state while making the
        // observed guest -> external -> container gap deterministic. This is
        // the exact bad state captured in the original failure evidence.
        if (pig.Identity is not WindowIdentity pigIdentity)
            throw new InvalidOperationException("Pig identity was lost before z-order setup; refusing to manipulate an unverified HWND.");
        VerifiedWindowOps.SetWindowPos(externalIdentity, pig.Hwnd, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE,
            pigIdentity);
        ctx.Check(NextVisibleWindow(pig.Hwnd) == externalHwnd,
            "external foreground steal leaves the unrelated HWND between guest and container");
        if (FindObstructingWindow(pig.Hwnd, cx, cy) != IntPtr.Zero)
            throw new InvalidOperationException("The direct-click point is obstructed; refusing to test the wrong HWND.");
        GuardedProc.Log($"  DirectClickForegroundPairing: clicking pig content directly at ({cx},{cy}) — no ForceForeground helper used.");
        var clickClock = Stopwatch.StartNew();
        Input.ClickAt(cx, cy);

        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pig.Hwnd, 1500),
            "pig became the real foreground window from the direct click alone");
        // Windows inserts invisible per-thread IME helper windows (MSCTFIME UI,
        // Default IME) into the z-order next to whatever window a thread just
        // touched — harmless and unrelated to PairZOrderBehindGuest, but they
        // sit between the pig and the container in a raw GW_HWNDNEXT walk.
        // Skip invisible windows so this checks the next REAL window, not the
        // literal next HWND.
        ctx.Check(Util.WaitUntil(() => NextVisibleWindow(pig.Hwnd) == container, 1500),
            "container re-paired immediately behind the guest in z-order (PairZOrderBehindGuest)");
        ctx.Check(clickClock.ElapsedMilliseconds <= 1500,
            $"guest/container repair completed within the bounded direct-click window ({clickClock.ElapsedMilliseconds} ms)");

        const string typed = "DCFPTEST";
        Input.TypeText(typed);
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, $"TEXTBOX text='{typed}'", 3000),
            $"pig text box received '{typed}' with zero re-click beyond the one direct click on its content");

        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive throughout");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 30. instant-tabswitch: tab switching under Shepherd must be instantaneous
    //     (WindowShepherdService.Capture disables DWM transitions and
    //     ContainerWindow.SyncShepherdActiveWindow shows-before-hides, both
    //     added this session) — never a visible/timed fade. Measures real
    //     wall-clock click-to-docked latency with a Stopwatch (not
    //     Util.WaitUntil's coarser polling) across 3 consecutive round-trip
    //     switches.
    // -------------------------------------------------------------------------
    private static void InstantTabSwitch(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "ITSA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "ITSB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        GuestInfo activeGuest = IsDocked(pigA.Hwnd, host) ? pigA : pigB;
        GuestInfo otherGuest = ReferenceEquals(activeGuest, pigA) ? pigB : pigA;

        for (int i = 1; i <= 3; i++)
        {
            AutomationElement? otherTab = FindTabText(container, otherGuest.Title, out int count);
            if (otherTab == null || count != 1)
                throw new InvalidOperationException($"switch {i}: tab for '{otherGuest.Title}' not found uniquely (count={count}).");
            (int tx, int ty) = Uia.Center(otherTab);

            long off = TabDockLog.RecordLogLength();
            var sw = Stopwatch.StartNew();
            Input.ClickAt(tx, ty);
            bool becameDocked = false;
            while (sw.ElapsedMilliseconds < 2000)
            {
                if (IsDocked(otherGuest.Hwnd, host))
                {
                    becameDocked = true;
                    break;
                }
                Thread.Sleep(2);
            }
            sw.Stop();

            ctx.Check(becameDocked, $"switch {i}: '{otherGuest.Title}' became docked");
            ctx.Check(sw.ElapsedMilliseconds < 400,
                $"switch {i}: click-to-docked elapsed {sw.ElapsedMilliseconds}ms (< 400ms — a fade transition would be far slower; instant show/hide should land well under this)");
            ctx.Check(TabDockLog.WaitForLogLine(off, "SHEPHERD[position]", 2000), $"switch {i}: TabDock log gained SHEPHERD[position] promptly");

            GuestInfo tmp = activeGuest;
            activeGuest = otherGuest;
            otherGuest = tmp;
        }

        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive after 3 switches");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 35. rename-edge-cases: empty-string rename, a 200+ char rename (with a
    //     state.json round-trip check), and Escape-must-preserve-the-original-
    //     name — none of these must crash or wedge the container.
    // -------------------------------------------------------------------------
    private static void RenameEdgeCases(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "REC", "--color", "teal");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int c1);
        ctx.Check(caption != null && c1 == 1, $"caption 'Group' found uniquely before edge-case renames (count={c1})");
        if (caption == null || c1 != 1)
            return;

        void ClickCaption()
        {
            (int px, int py) = Uia.Center(caption);
            if (!EnsureClickable(container, px, py))
                throw new InvalidOperationException("Could not bring the container to the foreground and its caption is obscured — refusing to click blind.");
            Input.DoubleClickAt(px, py);
            Thread.Sleep(300);
        }

        void SelectAll()
        {
            bool ctrlDown = false;
            try
            {
                Input.SendKeyDown(Input.VK_CONTROL);
                ctrlDown = true;
                Input.SendKey(Input.VK_A);
            }
            finally
            {
                if (ctrlDown)
                    Input.SendKeyUp(Input.VK_CONTROL);
            }
        }

        // --- Empty string: Ctrl+A, Delete, Enter must not crash the app. ---
        ClickCaption();
        SelectAll();
        Input.SendKey(Input.VK_DELETE);
        Input.SendKey(Input.VK_RETURN);
        Thread.Sleep(300);

        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive after renaming to an empty string");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after the empty-string rename");
        ctx.Check(NativeMethods.IsWindowEnabled(container), "container still enabled/responsive after the empty-string rename");
        GuardedProc.Log($"  rename-edge-cases: window title after empty-string rename = '{NativeMethods.GetWindowTextString(container) ?? "<null>"}' " +
            "(no specific fallback value is asserted — only survival and continued responsiveness).");

        // A follow-up NORMAL rename must still succeed (the box wasn't left in
        // a broken state by the empty-string edit).
        ClickCaption();
        SelectAll();
        Input.TypeText("TDVAL-AfterEmpty");
        Input.SendKey(Input.VK_RETURN);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-AfterEmpty", 2000),
            "a normal rename after the empty-string edit still works");

        // --- Very long string (200+ chars). ---
        string longName = "TDVAL-" + new string('X', 200);
        ClickCaption();
        SelectAll();
        Input.TypeText(longName);
        Input.SendKey(Input.VK_RETURN);
        Thread.Sleep(300);

        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive after a 200+ char rename");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after the long-string rename");
        ctx.Check(NativeMethods.IsWindowEnabled(container), "container still enabled/responsive after the long-string rename");
        ctx.Check(Util.WaitUntil(() => StateJsonContains(longName), 5000), "state.json round-trips the 200+ char group name (durable semantic save)");

        // --- Escape must preserve the name from BEFORE this edit, not commit it. ---
        ClickCaption();
        SelectAll();
        Input.TypeText("TDVAL-ShouldNotCommit");
        Input.SendKey(Input.VK_ESCAPE);
        Thread.Sleep(300);
        ctx.Check(NativeMethods.GetWindowTextString(container) == longName,
            "Escape preserved the pre-edit (200+ char) name instead of committing the abandoned edit");

        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig alive throughout all rename edge cases");
    }

    // -------------------------------------------------------------------------
    // 36. multi-group-independent-interaction: 3 separate single-tab groups
    //     open simultaneously must stay fully independent — each stays
    //     enabled/responsive and each one's minimize/restore must not disturb
    //     the other two's IsWindowEnabled/IsDocked state.
    // -------------------------------------------------------------------------
    private static void MultiGroupIndependentInteraction(Ctx ctx, Options opt)
    {
        GuestInfo pig1 = SpawnPig(ctx, "MG1", "--color", "red");
        (IntPtr container1, IntPtr host1) = CaptureIntoGroup(ctx, pig1);
        GuestInfo pig2 = SpawnPig(ctx, "MG2", "--color", "blue");
        (IntPtr container2, IntPtr host2) = CaptureIntoGroup(ctx, pig2);
        GuestInfo pig3 = SpawnPig(ctx, "MG3", "--color", "green");
        (IntPtr container3, IntPtr host3) = CaptureIntoGroup(ctx, pig3);

        ctx.Check(container1 != container2 && container2 != container3 && container1 != container3, "3 distinct containers created");

        IntPtr[] containers = { container1, container2, container3 };
        IntPtr[] hosts = { host1, host2, host3 };
        GuestInfo[] pigs = { pig1, pig2, pig3 };

        foreach (IntPtr c in containers)
            ctx.Check(NativeMethods.IsWindowEnabled(c), $"container 0x{c.ToInt64():X} enabled with all 3 groups open");

        // Trivial single-tab clicks: each container's own (only) tab stays docked.
        for (int i = 0; i < 3; i++)
        {
            AutomationElement? tab = FindTabText(containers[i], pigs[i].Title, out int count);
            if (tab == null || count != 1)
                throw new InvalidOperationException($"Tab for '{pigs[i].Title}' not found uniquely (count={count}).");
            (int tx, int ty) = Uia.Center(tab);
            if (!EnsureClickable(containers[i], tx, ty))
                throw new InvalidOperationException($"Could not bring container {i + 1} to the foreground and its tab is obscured — refusing to click blind.");
            Input.ClickAt(tx, ty);
            ctx.Check(Util.WaitUntil(() => IsDocked(pigs[i].Hwnd, hosts[i]), 3000), $"container {i + 1}: tab click keeps its only tab docked");
        }

        // Minimize container 2; verify 1 and 3 are unaffected.
        ClickMinimizeButton(container2);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container2), 3000), "container 2 minimized");
        ctx.Check(NativeMethods.IsWindowEnabled(container1) && IsDocked(pig1.Hwnd, host1), "container 1 unaffected by container 2's minimize");
        ctx.Check(NativeMethods.IsWindowEnabled(container3) && IsDocked(pig3.Hwnd, host3), "container 3 unaffected by container 2's minimize");
        VerifiedWindowOps.ShowWindow(container2, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container2), 3000), "container 2 restored");
        ctx.Check(Util.WaitUntil(() => IsDocked(pig2.Hwnd, host2), 3000), "container 2's tab docked again after restore");

        // Minimize container 1; verify 2 and 3 are unaffected.
        ClickMinimizeButton(container1);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container1), 3000), "container 1 minimized");
        ctx.Check(NativeMethods.IsWindowEnabled(container2) && IsDocked(pig2.Hwnd, host2), "container 2 unaffected by container 1's minimize");
        ctx.Check(NativeMethods.IsWindowEnabled(container3) && IsDocked(pig3.Hwnd, host3), "container 3 unaffected by container 1's minimize");
        VerifiedWindowOps.ShowWindow(container1, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container1), 3000), "container 1 restored");

        // Minimize container 3; verify 1 and 2 are unaffected.
        ClickMinimizeButton(container3);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsIconic(container3), 3000), "container 3 minimized");
        ctx.Check(NativeMethods.IsWindowEnabled(container1) && NativeMethods.IsWindowEnabled(container2), "containers 1 and 2 unaffected by container 3's minimize");
        VerifiedWindowOps.ShowWindow(container3, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsIconic(container3), 3000), "container 3 restored");

        ctx.Check(pig1.Proc != null && !pig1.Proc.HasExited && pig2.Proc != null && !pig2.Proc.HasExited && pig3.Proc != null && !pig3.Proc.HasExited,
            "all three pigs alive throughout");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 40. dwm-transitions-disabled-on-capture: WindowShepherdService.Capture
    //     calls DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED, true)
    //     on every captured guest, restored to false on release. Empirically
    //     tests (at run time, since this environment cannot run the app ahead
    //     of writing this code) whether DwmGetWindowAttribute can read that
    //     value back at all; if not, falls back to the documented observable
    //     side effect (no per-switch animation tax across repeated switches).
    // -------------------------------------------------------------------------
    private static void DwmTransitionsDisabledOnCapture(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "DWMA", "--color", "orange");
        GuestInfo pigB = SpawnPig(ctx, "DWMB", "--color", "purple");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        int hrGet = NativeMethods.DwmGetWindowAttribute(pigA.Hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, out bool disabledWhileCaptured, sizeof(uint));
        bool readable = hrGet == 0;
        GuardedProc.Log($"  dwm-transitions-disabled-on-capture: DwmGetWindowAttribute(TRANSITIONS_FORCEDISABLED) hr=0x{hrGet:X} value={disabledWhileCaptured} readable={readable} " +
            "(empirical check — this DWM attribute is not documented as guaranteed gettable).");
        if (readable)
        {
            ctx.Check(disabledWhileCaptured, "DWMWA_TRANSITIONS_FORCEDISABLED reads back true (disabled) while the guest is captured");
        }
        else
        {
            GuardedProc.Log("  dwm-transitions-disabled-on-capture: attribute not readable back (DwmGetWindowAttribute failed); falling back to the observable-timing assertion only.");
        }

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabA == null || cA != 1 || tabB == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (A={cA}, B={cB}).");
        (int ax, int ay) = Uia.Center(tabA);
        (int bx, int by) = Uia.Center(tabB);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++)
        {
            Input.ClickAt(bx, by);
            Util.WaitUntil(() => IsDocked(pigB.Hwnd, host), 1000, 20);
            Input.ClickAt(ax, ay);
            Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 1000, 20);
        }
        sw.Stop();
        GuardedProc.Log($"  dwm-transitions-disabled-on-capture: 3 round-trip switches took {sw.ElapsedMilliseconds}ms total.");
        ctx.Check(sw.ElapsedMilliseconds < 1500, $"3 round-trip tab switches complete in under 1.5s total ({sw.ElapsedMilliseconds}ms) — no per-switch animation tax accumulates");

        ClickTabMenuItem(ctx, container, pigA.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pigA.Hwnd, host), 5000), "pigA released by Pop out");

        if (readable)
        {
            int hrGetAfter = NativeMethods.DwmGetWindowAttribute(pigA.Hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, out bool disabledAfterRelease, sizeof(uint));
            ctx.Check(hrGetAfter == 0 && !disabledAfterRelease, "DWMWA_TRANSITIONS_FORCEDISABLED reads back false (re-enabled) after release (WindowShepherdService.Release restores it)");
        }

        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited, "both pigs alive throughout");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }
}
