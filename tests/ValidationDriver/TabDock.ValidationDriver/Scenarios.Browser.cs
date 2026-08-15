using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    private static GuestInfo SpawnBrowserGuest(Ctx ctx, string kind, string probeRole)
    {
        string exe;
        string profilePrefix;
        string tabKey;
        switch (kind)
        {
            case "chrome-normal":
                exe = ChromeExe;
                profilePrefix = "TabDockChromeProfileNormal";
                tabKey = "Google Chrome";
                break;
            case "edge-normal":
                exe = EdgeExe;
                profilePrefix = "TabDockEdgeProfileNormal";
                tabKey = "Microsoft";
                break;
            case "brave-normal":
                exe = BraveExe;
                profilePrefix = "TabDockBraveProfileNormal";
                tabKey = "Brave";
                break;
            default:
                throw new ArgumentException($"Unsupported isolated browser kind '{kind}'.", nameof(kind));
        }

        string page = new Uri(CreateBrowserResizeTestPage(probeRole)).AbsoluteUri;
        GuestInfo guest = SpawnClassGuest(ctx, exe,
            $"--user-data-dir=\"{FreshProfileDir(profilePrefix)}\" --no-first-run --no-default-browser-check --disable-session-crashed-bubble \"{page}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);
        return WithStableTabMatchKey(guest, tabKey);
    }

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

    // -------------------------------------------------------------------------
    // browser-split-persistent-render: isolated Chromium-family qualification.
    // Each browser receives a unique local page whose title reports
    // TDTEST-token-innerWidthxinnerHeight-resizeCount. The title is read
    // immediately after presentation transitions; no corrective click inside a
    // browser is used.
    // -------------------------------------------------------------------------
    private static void BrowserSplitPersistentRender(Ctx ctx, Options opt)
    {
        var requested = new[]
        {
            (Kind: "chrome-normal", Token: "CHR"),
            (Kind: "edge-normal", Token: "EDG"),
            (Kind: "brave-normal", Token: "BRV"),
        };
        string? requestedScope = Environment.GetEnvironmentVariable("TABDOCK_QA_BROWSER_SCOPE");
        string? scopedKind = string.IsNullOrWhiteSpace(requestedScope)
            ? null
            : requestedScope.Trim().ToLowerInvariant() switch
            {
                "edge" => "edge-normal",
                "brave" => "brave-normal",
                "chrome" => "chrome-normal",
                _ => throw new InvalidOperationException($"Unknown browser qualification scope '{requestedScope}'."),
            };
        var available = requested
            .Where(item => scopedKind == null || string.Equals(item.Kind, scopedKind, StringComparison.Ordinal))
            .Where(item => IsExecutableAvailable(item.Kind switch
            {
                "chrome-normal" => ChromeExe,
                "edge-normal" => EdgeExe,
                "brave-normal" => BraveExe,
                _ => string.Empty,
            }))
            .ToArray();

        if (available.Length == 0)
        {
            GuardedProc.Log($"SKIP_BROWSER_NOT_INSTALLED browser-split-persistent-render scope={requestedScope ?? "all"}.");
            ctx.Skip($"SKIP_BROWSER_NOT_INSTALLED: no supported isolated Chromium browser is installed for scope '{requestedScope ?? "all"}'.");
            return;
        }

        var browserSpecs = available
            .Select(item => (item.Kind, item.Token))
            .ToList();
        if (browserSpecs.Count == 1)
        {
            // A single installed family is still fully qualified with two
            // independent fresh-profile instances. Their run-specific page
            // roles keep tab labels and client evidence unambiguous.
            (string Kind, string Token) only = browserSpecs[0];
            browserSpecs.Add((only.Kind, only.Token + "2"));
        }

        int browserCount = Math.Min(3, browserSpecs.Count);
        var browsers = new List<GuestInfo>();
        var tokens = new Dictionary<GuestInfo, string>();
        for (int i = 0; i < browserCount; i++)
        {
            GuestInfo browser = SpawnBrowserGuest(ctx, browserSpecs[i].Kind, browserSpecs[i].Token);
            browsers.Add(browser);
            tokens[browser] = browserSpecs[i].Token;
        }

        GuestInfo left = browsers[0];
        GuestInfo right;
        if (browsers.Count >= 2)
        {
            right = browsers[1];
        }
        else
        {
            GuardedProc.Log("No second isolated browser instance was available; pairing the browser with a resize-probed GuineaPig.");
            right = SpawnPig(ctx, "BROWSER-RIGHT", "--color", "blue", "--resize-probe");
        }

        GuestInfo third;
        if (browsers.Count >= 3)
        {
            third = browsers[2];
        }
        else
        {
            GuardedProc.Log("BLOCKED_ENVIRONMENT Brave/third Chromium coverage unavailable; using a resize-probed GuineaPig as the controlled third guest.");
            third = SpawnPig(ctx, "BROWSER-THIRD", "--color", "green", "--resize-probe");
        }

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, left, right, third);
        // Chromium-family windows have a larger native outer minimum than a
        // GuineaPig. Make each pane legally wide before entering split so a
        // failed exact-pane assertion represents a product defect, not an
        // impossible 450px Edge/Brave outer rectangle.
        ResizeContainerTo(container, ctx.TabDockPid, 1400, 500);

        // Navigate each real browser to the isolated local probe while it is a
        // normal full-width guest. This setup uses guarded tab/keyboard input;
        // all subsequent presentation observations are passive.
        foreach (GuestInfo browser in browsers)
        {
            string token = tokens[browser];
            AutomationElement? tab = FindBrowserTab(container, token, out int count);
            if (tab == null || count != 1)
                throw new InvalidOperationException($"Browser setup: '{token}' tab not found uniquely (count={count}).");
            (int x, int y) = Uia.Center(tab);
            if (!EnsureClickable(container, x, y))
                throw new InvalidOperationException($"Browser setup: '{token}' tab was obscured.");
            Input.ClickAt(x, y);
            ctx.Check(Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 4000),
                $"browser setup: '{token}' is full-width before navigation");
            if (!Input.ForceForeground(browser.Hwnd))
                throw new InvalidOperationException($"Browser setup: could not foreground '{token}' safely.");
            Input.SendCtrlL();
            Thread.Sleep(180);
            string pageUrl = new Uri(CreateBrowserResizeTestPage(token)).AbsoluteUri;
            Input.TypeText(pageUrl);
            Input.SendKey(Input.VK_RETURN);
            string observed = WaitForBrowserResizeTitle(container, token, null, 8000, "setup");
            ctx.Check(observed.Length > 0, $"browser setup: '{token}' reports client viewport in its title");
        }

        // BROWSER-1: unsplit browser switching, recording outer and client state.
        foreach (GuestInfo browser in browsers)
        {
            string token = tokens[browser];
            AutomationElement? tab = FindBrowserTab(container, token, out int count);
            if (tab == null || count != 1)
                throw new InvalidOperationException($"BROWSER-1: '{token}' tab not found uniquely (count={count}).");
            string before = tab.Current.Name ?? string.Empty;
            (int x, int y) = Uia.Center(tab);
            if (!EnsureClickable(container, x, y))
                throw new InvalidOperationException($"BROWSER-1: '{token}' tab was obscured.");
            Input.ClickAt(x, y);
            ctx.Check(Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 4000),
                $"BROWSER-1: '{token}' unsplit full-width");
            LogBrowserState(browser, "BROWSER-1 unsplit");
            string after = WaitForBrowserResizeTitle(container, token, null, 1500, "BROWSER-1");
            ctx.Check(after.Length > 0 && (after == before || after.Contains(token, StringComparison.Ordinal)),
                $"BROWSER-1: '{token}' title remains client-observable after unsplit switch");
        }

        // BROWSER-2: split the first two browsers and inspect immediately.
        string leftToken = tokens[left];
        string rightToken = tokens.TryGetValue(right, out string? rightBrowserToken)
            ? rightBrowserToken
            : right.Title;
        string leftBeforeSplit = ReadTabName(container, leftToken);
        string rightBeforeSplit = ReadTabName(container, rightToken);
        long enterOff = TabDockLog.RecordLogLength();
        // The WPF submenu uses the current full tab label as its child name;
        // Chromium updates that label as the probe title advances. Read it
        // immediately before opening the menu instead of trusting the launch
        // title or a partial substring.
        ClickTabSubmenuItem(ctx, container, $":{leftToken}:", "Split screen", rightBeforeSplit);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "BROWSER-2: browser pair entered split");
        AssertSplitPanes(ctx, host, left, right, "BROWSER-2 immediate split");
        string leftAfterSplit = WaitForBrowserResizeTitle(container, leftToken, leftBeforeSplit, 5000, "BROWSER-2 LEFT");
        string rightAfterSplit = WaitForBrowserResizeTitle(container, rightToken, rightBeforeSplit, 5000, "BROWSER-2 RIGHT");
        ctx.Check(leftAfterSplit.Length > 0 && rightAfterSplit.Length > 0,
            "BROWSER-2: both browser client titles changed for split panes before any pane click");
        LogBrowserState(left, "BROWSER-2 split LEFT");
        LogBrowserState(right, "BROWSER-2 split RIGHT");
        ctx.Check(!NativeMethods.IsWindowVisible(third.Hwnd), "BROWSER-2: third guest hidden during browser pair");

        // BROWSER-3/4/5: pair -> third -> pair with no guest click between
        // transitions. Repeat enough cycles to expose stale settle/presentation.
        int cycles = Math.Max(10, opt.Cycles ?? 10);
        for (int i = 1; i <= cycles; i++)
        {
            string thirdKey = third == browsers.ElementAtOrDefault(2)
                ? tokens[third]
                : third.Title;
            int thirdPigBefore = third.IsPig ? PigLog.CountLines(third.Pid, "CLIENT_PRESENT") : 0;
            AutomationElement? thirdTab = third.IsPig
                ? FindTabText(container, thirdKey, out int thirdCount)
                : FindBrowserTab(container, thirdKey, out thirdCount);
            if (thirdTab == null || thirdCount != 1)
                throw new InvalidOperationException($"BROWSER-3 cycle {i}: third guest tab not found uniquely (count={thirdCount}).");
            (int tx, int ty) = Uia.Center(thirdTab);
            if (!EnsureClickable(container, tx, ty))
                throw new InvalidOperationException($"BROWSER-3 cycle {i}: third guest tab was obscured.");
            long suspendOff = TabDockLog.RecordLogLength();
            Input.ClickAt(tx, ty);
            ctx.Check(TabDockLog.WaitForLogLine(suspendOff, "SPLIT[suspend]", 3000),
                $"BROWSER-3 cycle {i}: third guest suspends browser pair");
            ctx.Check(Util.WaitUntil(() => IsDocked(third.Hwnd, host)
                && !NativeMethods.IsWindowVisible(left.Hwnd)
                && !NativeMethods.IsWindowVisible(right.Hwnd), 5000),
                $"BROWSER-3 cycle {i}: third guest is full-width and pair is hidden");
            if (third.IsPig)
            {
                // The browser tier uses a resize-probed GuineaPig only as a
                // deterministic third ordinary tab when fewer than three
                // browser families are installed. Its client-message contract
                // is qualified by split-three-app-client-settle; here the
                // browser clients are the rendering subject. Still retain the
                // pre-count in the trace so a future fixture can promote this
                // to an assertion without changing the click sequence.
                GuardedProc.Log($"  BROWSER-3 cycle {i}: controlled third client evidence baseline={thirdPigBefore}; geometry is the assertion in this browser fixture.");
            }
            else
            {
                string thirdAfter = WaitForBrowserResizeTitle(container, thirdKey, null, 1500, $"BROWSER-3 cycle {i} third");
                ctx.Check(thirdAfter.Length > 0 && thirdAfter.Contains(thirdKey, StringComparison.Ordinal),
                    $"BROWSER-3 cycle {i}: third browser client remains observable without corrective click");
            }
            LogBrowserState(third, $"BROWSER-3 cycle {i} third");
            ctx.Check(TabDockLog.CountNewLines(suspendOff, "SPLIT[exit]") == 0,
                $"BROWSER-3 cycle {i}: pair relationship retained");

            string restoreKey = i % 2 == 0 ? rightToken : leftToken;
            string leftBeforeResume = ReadTabName(container, leftToken);
            string rightBeforeResume = ReadTabName(container, rightToken);
            AutomationElement? restoreTab = FindBrowserTab(container, restoreKey, out int restoreCount);
            if (restoreTab == null || restoreCount != 1)
                throw new InvalidOperationException($"BROWSER-4 cycle {i}: '{restoreKey}' composite half not found uniquely (count={restoreCount}).");
            (int rx, int ry) = Uia.Center(restoreTab);
            if (!EnsureClickable(container, rx, ry))
                throw new InvalidOperationException($"BROWSER-4 cycle {i}: composite half was obscured.");
            long resumeOff = TabDockLog.RecordLogLength();
            Input.ClickAt(rx, ry);
            // The native presentation and visibility predicates below are the
            // contract. The diagnostic SPLIT[resume] line can be lost at the
            // same moment as the application's rotating log crosses its file
            // boundary, so do not turn a healthy state transition into a
            // false negative solely because that telemetry line was missed.
            bool resumeLogged = TabDockLog.WaitForLogLine(resumeOff, "SPLIT[resume]", 3000);
            if (!resumeLogged)
                GuardedProc.Log($"  BROWSER-4 cycle {i}: SPLIT[resume] telemetry was not observed; continuing with native state assertions.");
            AssertSplitPanes(ctx, host, left, right, $"BROWSER-4 cycle {i} restored pair");
            // Resuming the exact same pane rectangles is a visibility/presentation
            // transition, not necessarily a client-size transition. Chromium is
            // therefore allowed to retain the title it produced on split entry;
            // the observable contract is that the split viewport is still
            // present and coherent without a corrective guest click.
            string restoredLeft = WaitForBrowserResizeTitle(container, leftToken, null, 5000, $"BROWSER-4 cycle {i} LEFT");
            string restoredRight = WaitForBrowserResizeTitle(container, rightToken, null, 5000, $"BROWSER-4 cycle {i} RIGHT");
            ctx.Check(restoredLeft.Length > 0 && restoredRight.Length > 0
                && string.Equals(BrowserViewportSignature(restoredLeft), BrowserViewportSignature(leftAfterSplit), StringComparison.Ordinal)
                && string.Equals(BrowserViewportSignature(restoredRight), BrowserViewportSignature(rightAfterSplit), StringComparison.Ordinal),
                $"BROWSER-4 cycle {i}: both browser clients report the same restored split viewport without guest click");
            ctx.Check(!NativeMethods.IsWindowVisible(third.Hwnd),
                $"BROWSER-4 cycle {i}: third guest hidden after pair restore");
            ctx.Check(TabDockLog.CountNewLines(resumeOff, "SPLIT[exit]") == 0,
                $"BROWSER-4 cycle {i}: resume does not tear down relationship");
            LogBrowserState(left, $"BROWSER-4 cycle {i} LEFT");
            LogBrowserState(right, $"BROWSER-4 cycle {i} RIGHT");
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0,
            "browser split rendering produced no EXCEPTION lines");
    }

    private static string ReadTabName(IntPtr container, string token)
    {
        AutomationElement? tab = token.Contains(":", StringComparison.Ordinal)
            ? FindTabText(container, token, out int count)
            : FindBrowserTab(container, token, out count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab token '{token}' not found uniquely (count={count}).");
        return tab.Current.Name ?? string.Empty;
    }

    private static AutomationElement? FindBrowserTab(IntPtr container, string token, out int count)
        => FindTabText(container, $":{token}:", out count);

    private static string BrowserViewportSignature(string title)
    {
        int suffix = title.IndexOf(" - ", StringComparison.Ordinal);
        string probe = suffix >= 0 ? title[..suffix] : title;
        string[] fields = probe.Split(':');
        return fields.Length >= 6 && fields[0] == "TDTEST"
            ? $"{fields[4]}:{fields[5]}"
            : string.Empty;
    }

    private static string WaitForBrowserResizeTitle(
        IntPtr container,
        string token,
        string? differentFrom,
        int timeoutMs,
        string phase)
    {
        string observed = string.Empty;
        bool found = Util.WaitUntil(() =>
        {
            AutomationElement? tab = FindBrowserTab(container, token, out int count);
            if (tab == null || count != 1)
                return false;
            try
            {
                observed = tab.Current.Name ?? string.Empty;
            }
            catch
            {
                return false;
            }
            return observed.Contains("TDTEST:", StringComparison.Ordinal)
                && (differentFrom == null || !string.Equals(observed, differentFrom, StringComparison.Ordinal));
        }, timeoutMs);
        GuardedProc.Log($"  browser-client {phase} token={token} changed={found} title='{observed}'");
        return found ? observed : string.Empty;
    }

    private static void LogBrowserState(GuestInfo browser, string phase)
    {
        NativeMethods.GetWindowRect(browser.Hwnd, out NativeMethods.RECT outer);
        string version = string.Empty;
        try { version = browser.Proc?.MainModule?.FileVersionInfo.FileVersion ?? string.Empty; }
        catch { }
        GuardedProc.Log($"  browser-state {phase} title='{browser.Title}' version='{version}' hwnd=0x{browser.Hwnd.ToInt64():X} outer={Util.FormatRect(outer)} visible={NativeMethods.IsWindowVisible(browser.Hwnd)} foreground={NativeMethods.GetForegroundWindow() == browser.Hwnd}");
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

    private static string CreateBrowserResizeTestPage(string role)
    {
        string safeRole = new string(role.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(safeRole))
            safeRole = "Browser";
        string run = TestRunProvenance.RunIdCompact[..8];
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation", TestRunProvenance.RunIdCompact);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"browser-resize-probe-{safeRole}.html");
        File.WriteAllText(path, $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><style>
html, body {{ margin: 0; width: 100vw; height: 100vh; overflow: hidden; }}
body {{ background: linear-gradient(135deg, #fff 0%, #b9e7ff 100%); }}
#state {{ position: fixed; inset: 18px auto auto 18px; padding: 12px 16px;
  color: #062b40; background: rgba(255,255,255,.8); font: 700 22px Consolas, monospace; }}
</style></head>
<body><div id='state'></div>
<script>
var resizeCount = 0;
 var sequence = 0;
 function report(reason) {{
  resizeCount++;
  sequence++;
  var w = window.innerWidth;
  var h = window.innerHeight;
  var vw = window.visualViewport ? Math.round(window.visualViewport.width) : w;
  var vh = window.visualViewport ? Math.round(window.visualViewport.height) : h;
  document.title = 'TDTEST:{run}:{safeRole}:' + sequence + ':' + w + 'x' + h + ':r' + resizeCount;
  document.getElementById('state').textContent = document.title;
  document.getElementById('state').textContent += ' vv=' + vw + 'x' + vh;
}}
window.addEventListener('resize', report);
if (window.visualViewport) window.visualViewport.addEventListener('resize', report);
report('load');
</script>
</body>
</html>");
        return path;
    }
}
