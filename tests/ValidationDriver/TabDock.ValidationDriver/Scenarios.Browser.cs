using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 20. browser-lifecycle --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.1): real reparent lifecycle (launch/
    //     attach/detach) plus the H4 hide->show smear check with a real browser.
    // -------------------------------------------------------------------------
    private static void BrowserLifecycle(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        long capOff = TabDockLog.RecordLogLength();
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser);
        ctx.Check(Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 3000), "guest docked over host content area at capture (Shepherd positioning)");
        ctx.Check(TabDockLog.WaitForLogLine(capOff, "SHEPHERD[position]", 3000), "TabDock log gained a SHEPHERD[position] line for the guest at capture");
        ctx.Check(GuestMatchesHost(browser.Hwnd, host, out string geoCap), $"guest rect == host client rect at capture ({geoCap})");

        (double bBefore, _) = SampleHost(host);
        ctx.Check(bBefore > 1.0, $"host bright with browser visible before hide ({bBefore:F2})");

        // Force a hide->show cycle of the guest within its own host by
        // minimizing/restoring the container itself (mirrors minrestore).
        // Record the log offset BEFORE the cycle: the restore triggers a
        // WM_ACTIVATE -> BringToFront churn at an unbounded delay, and the
        // settle-wait below must be able to see it start.
        long churnOff = TabDockLog.RecordLogLength();
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the browser container to the foreground — refusing to click blind.");
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_MINIMIZE);
        Thread.Sleep(600);
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        Thread.Sleep(800);

        (double bAfter, _) = SampleHost(host);
        ctx.Check(bAfter > 1.0, $"host bright again after minimize->restore hide/show cycle, i.e. no H4 smear residue ({bAfter:F2})");
        ctx.Check(GuestMatchesHost(browser.Hwnd, host, out string geoAfter), $"guest still fills host after hide/show ({geoAfter})");

        // The restore re-asserts the active guest's foreground via the container's
        // WM_ACTIVATE -> BringToFront churn (SHEPHERD[bring-to-front] lines), whose
        // start time is unbounded, and that churn CLOSES a context menu opened while
        // it is active (see SelfMinimizeTimerVsTeardown). First wait for the churn to
        // have started AND gone quiet — but the churn fires in sporadic waves with
        // multi-second gaps between them, so a single quiet window can land between
        // waves and the very next reassert eats the click. Treat the wait as
        // best-effort and RETRY the pop-out: each attempt re-asserts the container's
        // foreground and re-opens the menu, and once the last wave has died out an
        // attempt lands cleanly.
        bool released = false;
        for (int attempt = 1; attempt <= 3 && !released; attempt++)
        {
            if (attempt > 1)
            {
                GuardedProc.Log($"  Pop-out attempt {attempt}: re-asserting foreground and retrying.");
                if (!Input.ForceForeground(container))
                    GuardedProc.Log("  Pop-out retry: ForceForeground failed — trying the menu click anyway.");
            }
            else if (!TabDockLog.WaitForChurnToSettle(churnOff, "SHEPHERD[bring-to-front]", 1200, 12000))
            {
                throw new InvalidOperationException("The guest reassert churn never started-and-settled after minimize->restore — refusing to open the Pop out menu into live churn.");
            }
            try
            {
                ClickTabMenuItem(ctx, container, browser.EffectiveTabMatchKey, "Pop out");
            }
            catch (InvalidOperationException ex)
            {
                GuardedProc.Log($"  Pop-out attempt {attempt} failed to open/click the menu: {ex.Message}");
                continue;
            }
            released = Util.WaitUntil(() => IsReleased(browser, host), 5000);
            if (!released)
                GuardedProc.Log($"  Pop-out attempt {attempt} did not release the browser (menu likely eaten by churn); retrying.");
        }
        ctx.Check(released, "browser released by Pop out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // 21. browser-tabswitch-hidesafety --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.2): the existing tabswitch-hidesafety
    //     gate, with one of the three tabs a real browser instead of all pigs.
    // -------------------------------------------------------------------------
    private static void BrowserTabSwitchHideSafety(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pigB = SpawnPig(ctx, "BTB", "--color", "blue");
        GuestInfo pigG = SpawnPig(ctx, "BTG", "--color", "green");
        GuestInfo[] guests = { browser, pigB, pigG };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture (1 real browser + 2 pigs)");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // H4 render check across tab switches: drive the browser to a deterministic
        // local test page (white background + blinking black square) so per-switch
        // brightness and inter-frame variance can be hard-asserted — no live URL,
        // no browser-theme dependence (the white background dominates).
        string renderUrl = new Uri(CreateBrowserRenderTestPage()).AbsoluteUri;
        AutomationElement? browserTab = FindTabText(container, browser.EffectiveTabMatchKey, out int browserTabCount);
        if (browserTab == null || browserTabCount != 1)
            throw new InvalidOperationException($"Browser tab for '{browser.EffectiveTabMatchKey}' not found uniquely (count={browserTabCount}).");
        (int btx, int bty) = Uia.Center(browserTab);
        Input.ClickAt(btx, bty);
        ctx.Check(Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 3000), "browser is the active (docked) tab before navigation");
        if (!Input.ForceForeground(browser.Hwnd))
            throw new InvalidOperationException("Could not bring the browser to the foreground — refusing to type blind.");
        Input.SendCtrlL();
        Thread.Sleep(300);
        Input.TypeText(renderUrl);
        Input.SendKey(Input.VK_RETURN);
        Thread.Sleep(2000); // let the page load

        bool everyClickOk = true;
        for (int i = 0; i < 24; i++)
        {
            int idx = i % guests.Length;
            AutomationElement? tab = FindTabText(container, guests[idx].EffectiveTabMatchKey, out int count);
            if (tab == null || count != 1)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab for '{guests[idx].Title}' found uniquely (count={count})");
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

            // H4: after every switch TO the browser, hard-assert it is live-rendering.
            // PrintWindow (PW_RENDERFULLCONTENT) reads the guest's own back-buffer,
            // so on-screen occlusion cannot fake a black frame. Floors are the ones
            // proven by maximize-repro/realapp-multi-render: brightness > 1.0 (a
            // black/blank frame fails) and inter-frame variance > 0.005 (the test
            // page's blinking square guarantees visible change between frames).
            if (guests[idx] == browser)
            {
                ctx.Check(Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 3000),
                    $"switch {i + 1}/24: browser docked over host after switching to it");
                // The blink's 500ms setInterval can be throttled to >= 1s when the
                // guest is momentarily not the OS-foreground window (Chrome's
                // background-timer throttling), so two 600ms-apart captures can
                // land in the same blink phase and read as byte-identical (variance
                // 0.0000). That is the documented sampling-timing flake class, not a
                // rendering regression — resample with a fresh pair before failing.
                int[]? f0 = Pixels.CaptureWindowViaPrintWindow(browser.Hwnd);
                Thread.Sleep(600);
                int[]? f1 = Pixels.CaptureWindowViaPrintWindow(browser.Hwnd);
                double var = f0 == null || f1 == null ? -1 : Pixels.ComputeAvgFrameDiff(f0, f1);
                for (int s = 0; s < 3 && var <= 0.005; s++)
                {
                    GuardedProc.Log($"  switch {i + 1}/24: H4 variance sample {s + 1} flat (var={var:F4}) — resampling.");
                    Thread.Sleep(700);
                    f0 = Pixels.CaptureWindowViaPrintWindow(browser.Hwnd);
                    Thread.Sleep(600);
                    f1 = Pixels.CaptureWindowViaPrintWindow(browser.Hwnd);
                    var = f0 == null || f1 == null ? -1 : Pixels.ComputeAvgFrameDiff(f0, f1);
                }
                if (f0 == null || f1 == null)
                {
                    ctx.Check(false, $"switch {i + 1}/24: PrintWindow capture of the browser returned null");
                }
                else
                {
                    double bright = Pixels.ComputeAvgBrightness(f1);
                    ctx.Check(bright > 1.0, $"switch {i + 1}/24: browser frame bright (brightness={bright:F2} > 1.0, H4 liveness floor)");
                    ctx.Check(var > 0.005, $"switch {i + 1}/24: browser frame has live content (variance={var:F4} > 0.005, H4 liveness floor)");
                }
            }
        }
        if (everyClickOk)
            ctx.Check(true, "tab count stayed 3 after every one of the 24 clicks (real browser included)");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "hid itself") == 0, "ZERO 'hid itself' lines in TabDock log");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "destroyed") == 0, "ZERO 'destroyed' lines in TabDock log");
        ctx.Check(browser.Proc != null && !browser.Proc.HasExited
                && (IsDocked(browser.Hwnd, host) || IsReleasedAndHidden(browser.Hwnd)),
            $"real browser '{browser.Title}' alive and still captured (docked over host or hidden inactive tab) after 24 switches");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // 22. browser-dragreorder --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.3): H2's TabDock-tab-strip drag-reorder,
    //     with a real browser as one of the two tabs (dragreorder uses pigs only).
    // -------------------------------------------------------------------------
    private static void BrowserDragReorder(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pig = SpawnPig(ctx, "BDR", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser, pig);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture (1 real browser + 1 pig)");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        AutomationElement? tabBrowser = FindTabText(container, browser.EffectiveTabMatchKey, out int cA);
        AutomationElement? tabPig = FindTabText(container, pig.EffectiveTabMatchKey, out int cB);
        if (tabBrowser == null || cA != 1 || tabPig == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (browser={cA}, pig={cB}).");
        Rect rBrowser = Uia.GetElementRect(tabBrowser);
        Rect rPig = Uia.GetElementRect(tabPig);
        bool browserIsRight = rBrowser.X > rPig.X;
        GuestInfo movedGuest = browserIsRight ? browser : pig;
        Rect leftRect = browserIsRight ? rPig : rBrowser;
        (int sx, int sy) = Uia.Center(browserIsRight ? tabBrowser : tabPig);
        long dragOff = TabDockLog.RecordLogLength(); // scope reorder analysis to THIS drag only
        Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabCount(container) == 2, "still 2 tabs after drag-reorder");
        // H2 oscillation guard (see dragreorder): zero flip-back pairs + a bounded
        // reorder count. The flip-pair check is the primary signal; the count is a churn ceiling.
        const int MaxReordersPerDrag = 20;
        (int reorderCount, int flipPairs) = TabDockLog.AnalyzeReorders(dragOff);
        ctx.Check(reorderCount >= 1, "a reorder was applied (log)");
        ctx.Check(flipPairs == 0, $"zero immediate flip-back pairs during the drag (H2 oscillation) — got {flipPairs}");
        ctx.Check(reorderCount <= MaxReordersPerDrag, $"reorder count within bound (<= {MaxReordersPerDrag}, H2 churn ceiling) — got {reorderCount}");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");
        ctx.Check(browser.Proc != null && !browser.Proc.HasExited && pig.Proc != null && !pig.Proc.HasExited,
            "both the real browser and the pig are alive after drag-reorder");

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        Input.DragFromTo((int)(leftRect.X + leftRect.Width / 2), sy, rc.right + 150, rc.bottom + 150, 14);
        ctx.Check(Util.WaitUntil(() => IsReleased(movedGuest, host), 5000), $"moved guest '{movedGuest.Title}' released by drag-out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-out");
        ctx.Check(movedGuest.Proc != null && !movedGuest.Proc.HasExited, "moved guest alive standalone");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // 23. browser-multi (docs/internal/TEST_PLAN.md 5.4): Chrome + Edge as two
    //     simultaneous tabs in one container (Firefox omitted — not installed
    //     on this dev machine; add "firefox-normal" to the guest list below if
    //     it becomes available, per TEST_PLAN.md section 4/6).
    // -------------------------------------------------------------------------
    private static void BrowserMulti(Ctx ctx, Options opt)
    {
        GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
        GuestInfo edge = SpawnGuest(ctx, "edge-normal");
        GuestInfo[] guests = { chrome, edge };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);
        ctx.Check(TabCount(container) == 2, "2 tabs after simultaneous multi-browser capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        foreach (GuestInfo g in guests)
        {
            AutomationElement? tab = FindTabText(container, g.EffectiveTabMatchKey, out int count);
            ctx.Check(tab != null && count == 1, $"tab for '{g.Title}' found uniquely (count={count})");
            if (tab == null || count != 1)
                continue;
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(400);
            ctx.Check(TabCount(container) == 2, $"tab count still 2 after switching to '{g.Title}'");
        }

        foreach (GuestInfo g in guests)
        {
            ctx.Check(g.Proc != null && !g.Proc.HasExited
                    && (IsDocked(g.Hwnd, host) || IsReleasedAndHidden(g.Hwnd)),
                $"'{g.Title}' alive and still captured (docked over host or hidden inactive tab) after the multi-browser switch pass");
        }
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    // -------------------------------------------------------------------------
    // 24. browser-soak --guest {chrome-normal|edge-normal|firefox-normal} --cycles N
    //     (docs/internal/TEST_PLAN.md 5.6): a SCOPED PROXY for long-running
    //     stability — N tab-switch cycles (default 30, several minutes) with a
    //     periodic health check, not a true multi-hour/day soak. See
    //     KNOWN_ISSUES.md for the honest scope note.
    // -------------------------------------------------------------------------
    private static void BrowserSoak(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 30;
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pig = SpawnPig(ctx, "SOAK", "--pulse", "--color", "white");
        GuestInfo[] guests = { browser, pig };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        for (int i = 0; i < cycles; i++)
        {
            GuestInfo target = guests[i % guests.Length];
            AutomationElement? tab = FindTabText(container, target.Title, out int count);
            if (tab == null || count != 1)
            {
                ctx.Check(false, $"cycle {i + 1}/{cycles}: tab for '{target.Title}' found uniquely (count={count})");
                break;
            }
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(300);

            if (i % 5 == 4)
            {
                bool ok = TabCount(container) == 2
                    && browser.Proc != null && !browser.Proc.HasExited
                    && pig.Proc != null && !pig.Proc.HasExited
                    && TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0;
                ctx.Check(ok, $"health check at cycle {i + 1}/{cycles}: 2 tabs, both guests alive, no EXCEPTION lines");
                if (!ok)
                    break;
            }
        }

        ctx.Check(browser.Proc != null && !browser.Proc.HasExited, $"real browser '{browser.Title}' survived {cycles} switch cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines across the whole soak run");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphaned guest windows survive the scenario");
    }

    /// <summary>
    /// Deterministic local page for the H4 Chromium live-render check: a white
    /// page with a black square that blinks on a 500ms cycle. Brightness stays
    /// high (white background) while the blinking square guarantees inter-frame
    /// variance &gt; 0 — a black/frozen PrintWindow capture fails both floors.
    /// </summary>
    private static string CreateBrowserRenderTestPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "browser-render-test.html");
        File.WriteAllText(path, @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><style>
body { margin: 0; width: 100vw; height: 100vh; background: white; }
#blink { position: fixed; top: 20px; left: 20px; width: 120px; height: 120px; background: black; }
</style></head>
<body>
<div id='blink'></div>
<script>
setInterval(function() {
    var el = document.getElementById('blink');
    el.style.opacity = el.style.opacity === '0' ? '1' : '0';
}, 500);
</script>
</body>
</html>");
        return path;
    }
}
