using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // Supervised real-input closure pass: three independent top-level apps in
    // one group. This deliberately uses the same UIA discovery and SendInput
    // helpers as the manual validation runs, while keeping the sequence
    // repeatable and ensuring cleanup cannot leave real apps behind.
    private static void ThreeAppTorture(Ctx ctx, Options opt)
    {
        if (!IsExecutableAvailable(ChromeExe))
        {
            ctx.Skip("SKIP_BROWSER_NOT_INSTALLED: three-app-torture requires Google Chrome for its browser leg.");
            return;
        }
        GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
        GuestInfo edge = SpawnGuest(ctx, "edge-normal");
        GuestInfo terminal = SpawnGuest(ctx, "wt");
        RefreshGuestTitle(chrome);
        RefreshGuestTitle(edge);
        RefreshGuestTitle(terminal);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome, edge, terminal);
        GuestInfo external = SpawnNotepad(ctx);
        GuestInfo[] apps = { chrome, edge, terminal };
        ctx.Check(TabCount(container) == 3, "three real applications captured into one group");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not foreground the three-app container — refusing to click blind.");

        // Normal switching, repeated direct activation, and context-menu popup
        // layering for every real guest.
        ClickTabAndCheck(ctx, container, host, chrome);
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            DismissTabContextMenu(ctx, container, chrome.EffectiveTabMatchKey);
            ctx.Check(TabCount(container) == 3, $"right-click menu cycle {cycle} leaves all three tabs intact");
        }
        foreach (GuestInfo app in apps)
        {
            ClickTabAndCheck(ctx, container, host, app);
        }
        ClickTabAndCheck(ctx, container, host, chrome);
        DirectActivateAfterExternalSteal(ctx, container, host, chrome, external.Hwnd);

        // Exercise actual browser chrome, not only the rendered page surface.
        ClickTabAndCheck(ctx, container, host, chrome);
        NativeMethods.GetWindowRect(chrome.Hwnd, out NativeMethods.RECT chromeRect);
        Input.ClickAt(chromeRect.left + 240, chromeRect.top + 45);
        Input.SendCtrlL();
        Input.TypeText("https://time.is");
        Input.SendKey(Input.VK_RETURN);
        Thread.Sleep(900);
        ctx.Check(NativeMethods.GetForegroundWindow() == chrome.Hwnd,
            "Chrome remains foreground after address-bar interaction");

        // Three-tab split: Chrome on LEFT and Terminal on RIGHT, while Edge
        // remains captured but hidden. Click both panes and repeat the direct
        // activation transition while split is active.
        ClickTabSubmenuItem(ctx, container, chrome.EffectiveTabMatchKey, "Split screen", terminal.Title);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(chrome.Hwnd) && NativeMethods.IsWindowVisible(terminal.Hwnd), 3000),
            "Chrome and Terminal are both visible after entering split mode");
        ctx.Check(IsReleasedAndHidden(edge.Hwnd), "Edge remains captured and hidden as the non-split tab");

        NativeMethods.RECT splitRect = Discover.GetClientScreenRect(host);
        Input.ClickAt(splitRect.left + splitRect.Width / 4, splitRect.top + splitRect.Height / 2);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == chrome.Hwnd, 1500),
            "direct click returns focus to the LEFT Chrome pane");
        Input.ClickAt(splitRect.left + 3 * splitRect.Width / 4, splitRect.top + splitRect.Height / 2);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == terminal.Hwnd, 1500),
            "direct click returns focus to the RIGHT Terminal pane");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(chrome.Hwnd) && NativeMethods.IsWindowVisible(terminal.Hwnd), 3000),
            "both split guests remain visible after direct focus returns");

        // Native container move/resize and minimize/restore while split.
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT beforeMove);
        Input.DragFromTo(beforeMove.left + 180, beforeMove.top + 16,
            beforeMove.left + 220, beforeMove.top + 46, 12);
        Thread.Sleep(700);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(chrome.Hwnd) && NativeMethods.IsWindowVisible(terminal.Hwnd), 3000),
            "both split guests remain visible after a native container move");
        ClickMaximizeButton(container);
        Thread.Sleep(700);
        ClickMaximizeButton(container);
        Thread.Sleep(700);
        ClickMinimizeButton(container);
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(chrome.Hwnd) && !NativeMethods.IsWindowVisible(terminal.Hwnd), 3000),
            "both split guests hide with the minimized container");
        VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(chrome.Hwnd) && NativeMethods.IsWindowVisible(terminal.Hwnd), 3000),
            "both split guests restore after minimize");

        // Pop out by middle-click, re-capture, then pop out another app with X
        // and re-capture it. This also creates two groups for a final group
        // switching check without changing application ownership or styles.
        ClickTabMenuItem(ctx, container, chrome.EffectiveTabMatchKey, "Exit split screen");
        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, host) || IsDocked(edge.Hwnd, host), 3000),
            "normal stack restored after exiting split");
        AutomationElement? chromeTab = FindTabText(container, chrome.EffectiveTabMatchKey, out int chromeCount);
        if (chromeTab == null || chromeCount != 1)
            throw new InvalidOperationException("Chrome tab not found for middle-click pop-out.");
        (int cx, int cy) = Uia.Center(chromeTab);
        Input.MiddleClickAt(cx, cy);
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(chrome.Hwnd, host), 5000),
            "Chrome middle-click pop-out leaves it visible");
        RefreshGuestTitle(chrome);
        (IntPtr chromeContainer, IntPtr chromeHost) = CaptureIntoGroup(ctx, chrome);
        ctx.Check(Util.WaitUntil(() => IsDocked(chrome.Hwnd, chromeHost), 3000),
            "Chrome re-captures into a new group");

        RefreshGuestTitle(edge);
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the torture container to the foreground.");
        ClickTabCloseButton(ctx, container, edge.EffectiveTabMatchKey);
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(edge.Hwnd, host), 5000),
            "Edge X pop-out leaves it visible");
        (IntPtr edgeContainer, IntPtr edgeHost) = CaptureIntoGroup(ctx, edge);
        ctx.Check(Util.WaitUntil(() => IsDocked(edge.Hwnd, edgeHost), 3000),
            "Edge re-captures after X pop-out");

        if (!Input.ForceForeground(chromeContainer) || !Input.ForceForeground(edgeContainer))
            throw new InvalidOperationException("Could not re-verify the final torture containers before foreground switching.");
        ctx.Check(NativeMethods.GetForegroundWindow() == edge.Hwnd || NativeMethods.GetForegroundWindow() == edgeContainer,
            "final group switch leaves the Edge group active");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0,
            "no TabDock exceptions across the three-app torture sequence");
    }

    private static void ClickTabAndCheck(Ctx ctx, IntPtr container, IntPtr host, GuestInfo app)
    {
        AutomationElement? tab = FindTabText(container, app.EffectiveTabMatchKey, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{app.Title}' not found uniquely (count={count}).");
        (int x, int y) = Uia.Center(tab);
        Input.ClickAt(x, y);
        ctx.Check(Util.WaitUntil(() => IsDocked(app.Hwnd, host), 3000),
            $"normal tab switch to '{app.Title}' keeps the guest docked");
    }

    private static void RefreshGuestTitle(GuestInfo app)
    {
        string? current = NativeMethods.GetWindowTextString(app.Hwnd);
        if (!string.IsNullOrWhiteSpace(current))
        {
            app.Title = current;
            if (app.IsPig)
                app.TabMatchKey = current;
            if (Discover.TryCaptureIdentity(app.Hwnd, out WindowIdentity identity)
                && identity.ProcessId == app.Pid)
            {
                app.Identity = identity;
                Input.RegisterIdentity(identity);
            }
        }
    }

    private static void DirectActivateAfterExternalSteal(Ctx ctx, IntPtr container, IntPtr host, GuestInfo app, IntPtr external)
    {
        NativeMethods.RECT rect = Discover.GetClientScreenRect(host);
        DirectActivateAfterExternalSteal(ctx, container, host, app, external, rect.left + rect.Width / 2, rect.top + rect.Height / 2);
    }

    private static void DirectActivateAfterExternalSteal(Ctx ctx, IntPtr container, IntPtr host, GuestInfo app, IntPtr external, int x, int y)
    {
        if (external == IntPtr.Zero || external == app.Hwnd || external == container)
            throw new InvalidOperationException("The validation console has no safe external HWND for the direct-activation step.");
        if (!Input.ForceForegroundRoot(external) || NativeMethods.GetForegroundWindow() != external)
            throw new InvalidOperationException("Could not establish the external foreground steal.");
        if (!Discover.TryCaptureIdentity(external, out WindowIdentity externalIdentity))
            throw new InvalidOperationException("External window failed identity verification before restore.");
        if (!VerifiedWindowOps.ShowWindow(externalIdentity, NativeMethods.SW_RESTORE))
            throw new InvalidOperationException("External window changed during restore; refusing to continue.");
        NativeMethods.GetWindowRect(external, out NativeMethods.RECT externalRect);
        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualRight = virtualLeft + NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualBottom = virtualTop + NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int[] candidateX =
        {
            virtualLeft + 10,
            Math.Max(virtualLeft, virtualRight - externalRect.Width - 10),
            hostRect.right + 20,
            hostRect.left - externalRect.Width - 20,
        };
        int[] candidateY =
        {
            virtualTop + 10,
            Math.Max(virtualTop, virtualBottom - externalRect.Height - 10),
            hostRect.top,
        };
        bool pointFree = false;
        foreach (int candidateXValue in candidateX)
        {
            foreach (int candidateYValue in candidateY)
            {
                int placedX = Math.Clamp(candidateXValue, virtualLeft, virtualRight - externalRect.Width);
                int placedY = Math.Clamp(candidateYValue, virtualTop, virtualBottom - externalRect.Height);
                if (!Discover.TryCaptureIdentity(external, out externalIdentity)
                    || !Discover.TryCaptureIdentity(app.Hwnd, out WindowIdentity appIdentity))
                    throw new InvalidOperationException("A direct-activation target changed identity; refusing to reposition it.");
                VerifiedWindowOps.SetWindowPos(externalIdentity, IntPtr.Zero, placedX, placedY, 0, 0,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE,
                    externalIdentity);
                VerifiedWindowOps.SetWindowPos(externalIdentity, app.Hwnd, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE,
                    appIdentity);
                if (IsPointOnWindow(app.Hwnd, x, y))
                {
                    pointFree = true;
                    break;
                }
            }
            if (pointFree)
                break;
        }
        if (!pointFree)
        {
            NativeMethods.GetWindowRect(app.Hwnd, out NativeMethods.RECT appRect);
            int[] offsets = { 0, -appRect.Width / 4, appRect.Width / 4, -appRect.Width / 8, appRect.Width / 8 };
            int[] verticalOffsets = { 0, -appRect.Height / 4, appRect.Height / 4, -appRect.Height / 8, appRect.Height / 8 };
            foreach (int dx in offsets)
            {
                foreach (int dy in verticalOffsets)
                {
                    int pointX = Math.Clamp(x + dx, appRect.left + 20, appRect.right - 20);
                    int pointY = Math.Clamp(y + dy, appRect.top + 40, appRect.bottom - 20);
                    if (IsPointOnWindow(app.Hwnd, pointX, pointY))
                    {
                        x = pointX;
                        y = pointY;
                        pointFree = true;
                        break;
                    }
                }
                if (pointFree)
                    break;
            }
        }
        if (!pointFree)
            throw new InvalidOperationException($"External window still obscures every direct-click point for '{app.Title}'.");
        Input.ClickAt(x, y);
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == app.Hwnd, 1500),
            $"direct click foregrounds '{app.Title}' after an external steal");
        ctx.Check(Util.WaitUntil(() => IsDocked(app.Hwnd, host) || IsInPane(app.Hwnd, host, true) || IsInPane(app.Hwnd, host, false), 1500),
            $"'{app.Title}' remains locally glued after direct activation");
    }

    private static bool IsPointOnWindow(IntPtr target, int x, int y)
    {
        IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr rootAtPoint = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
        return rootAtPoint == target;
    }

    // -------------------------------------------------------------------------
    // R22 qualification torture harness (hermetic, pig-only soaks). All six
    // bodies live here; registration is in Scenarios.cs (RunScenario switch,
    // AllOrder) and categorization in GetScenarioShard. Every assertion goes
    // through ctx.Check and every wait through Util.WaitUntil /
    // TabDockLog.WaitForLogLine so the per-scenario GuardedProc budget bounds
    // the run. Asserted log substrings are limited to strings verified in
    // committed product source (WindowShepherdService, GuestLifecycleService,
    // GroupManager, ContainerWindow*) plus the GuineaPig's own LIFECYCLE log.
    // -------------------------------------------------------------------------

    /// <summary>Real-clicks the center of the uniquely-named tab for a guest.</summary>
    private static void ClickTabCenter(IntPtr container, string guestTitle)
    {
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");
        (int x, int y) = Uia.Center(tab);
        Input.ClickAt(x, y);
    }

    /// <summary>
    /// The shared every-10th-cycle invariant set for the tab-switch torture
    /// scenarios: the active guest is docked, the foreground ROOT is the active
    /// guest (Shepherd keeps guests top-level, so the root — not a child — is
    /// the meaningful comparison), no tray-style hide was misclassified, and
    /// TabDock logged no exception.
    /// </summary>
    private static void AssertTortureTabSwitchInvariants(Ctx ctx, IntPtr host, GuestInfo active, long off, string label)
    {
        ctx.Check(Util.WaitUntil(() => IsDocked(active.Hwnd, host), 3000),
            $"{label}: active guest '{active.Title}' is docked");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT) == active.Hwnd, 2000),
            $"{label}: foreground root is the active guest");
        ctx.Check(TabDockLog.CountNewLines(off, "hid itself (tray-style close)") == 0,
            $"{label}: zero 'hid itself (tray-style close)' lines");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0,
            $"{label}: zero EXCEPTION lines");
    }

    /// <summary>
    /// Shared tail assertions for the tab-switch torture scenarios: tab count
    /// unchanged, zero releases, and every SHEPHERD-initiated hide carries its
    /// provenance match ("hide matched TabDock's shepherd-hide", emitted by
    /// GuestLifecycleService via GuestHideProvenance.TryConsumeExpectedHide).
    /// </summary>
    private static void AssertTortureTabSwitchTail(Ctx ctx, IntPtr container, long off, int expectedTabs, string label)
    {
        ctx.Check(TabCount(container) == expectedTabs, $"{label}: tab count unchanged after all cycles");
        ctx.Check(TabDockLog.CountNewLines(off, "Released tab") == 0, $"{label}: zero 'Released tab' lines");
        int shepherdHides = TabDockLog.CountNewLines(off, "SHEPHERD[hide]");
        int matchedHides = TabDockLog.CountNewLines(off, "hide matched TabDock's shepherd-hide");
        GuardedProc.Log($"  {label}: SHEPHERD[hide]={shepherdHides} hide-matched={matchedHides}");
        ctx.Check(matchedHides >= shepherdHides,
            $"{label}: every SHEPHERD[hide] line is provenance-matched ({matchedHides} >= {shepherdHides})");
    }

    // -------------------------------------------------------------------------
    // torture-tabswitch-rapid: two pigs in one group, then N rapid alternating
    // tab activations (default 100, hard cap 150). Every 10th cycle re-asserts
    // the active-guest contract; the tail proves no tab was lost to a
    // misclassified hide.
    // -------------------------------------------------------------------------
    private static void TortureTabSwitchRapid(Ctx ctx, Options opt)
    {
        int cycles = Math.Min(opt.Cycles ?? 100, 150);
        GuestInfo pigA = SpawnPig(ctx, "TRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "TRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "rapid: 2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        GuestInfo[] pigs = { pigA, pigB };
        long off = TabDockLog.RecordLogLength();
        for (int i = 0; i < cycles; i++)
        {
            GuestInfo active = pigs[i % 2];
            ClickTabCenter(container, active.Title);
            Thread.Sleep(250);
            if ((i + 1) % 10 == 0)
                AssertTortureTabSwitchInvariants(ctx, host, active, off, $"rapid cycle {i + 1}/{cycles}");
        }

        AssertTortureTabSwitchTail(ctx, container, off, 2, "rapid");
    }

    // -------------------------------------------------------------------------
    // torture-tabswitch-random: four pigs, 50 pseudo-random tab switches drawn
    // from a FIXED seed (20260822) so any failure reproduces bit-for-bit; each
    // chosen index is echoed to the driver log with the seed. Same invariant
    // set as torture-tabswitch-rapid, plus the final active tab must be the
    // last chosen index.
    // -------------------------------------------------------------------------
    private static void TortureTabSwitchRandom(Ctx ctx, Options opt)
    {
        const int sequences = 50;
        const int seed = 20260822;
        var rng = new Random(seed);
        GuestInfo[] pigs =
        {
            SpawnPig(ctx, "TRS-A", "--color", "red"),
            SpawnPig(ctx, "TRS-B", "--color", "blue"),
            SpawnPig(ctx, "TRS-C", "--color", "green"),
            SpawnPig(ctx, "TRS-D", "--color", "yellow"),
        };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigs);
        ctx.Check(TabCount(container) == 4, "random: 4 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        long off = TabDockLog.RecordLogLength();
        int lastIdx = -1;
        for (int i = 0; i < sequences; i++)
        {
            lastIdx = rng.Next(pigs.Length);
            GuardedProc.Log($"  random switch {i + 1}/{sequences}: seed={seed} -> tab {lastIdx} '{pigs[lastIdx].Title}'");
            ClickTabCenter(container, pigs[lastIdx].Title);
            Thread.Sleep(250);
            if ((i + 1) % 10 == 0)
                AssertTortureTabSwitchInvariants(ctx, host, pigs[lastIdx], off, $"random cycle {i + 1}/{sequences}");
        }

        AssertTortureTabSwitchTail(ctx, container, off, 4, "random");

        GuestInfo last = pigs[lastIdx];
        ctx.Check(Util.WaitUntil(() => IsDocked(last.Hwnd, host), 3000)
            && NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT) == last.Hwnd,
            $"random: final active tab is the last chosen index ({lastIdx}, '{last.Title}')");
    }

    /// <summary>
    /// Waits for the member-loss evidence TabDock actually emits when a split
    /// member's process dies: either SPLIT[member-gone]
    /// (ContainerWindow.xaml.cs) or the WinEvent-driven
    /// "destroyed; removing its tab." (GuestLifecycleService). Never asserts
    /// WHICH of the two fires — only that the relationship reacted.
    /// </summary>
    private static bool WaitForMemberGoneEvidence(long offset, int timeoutMs)
    {
        return Util.WaitUntil(() =>
            TabDockLog.ContainsNewLine(offset, "SPLIT[member-gone]")
            || TabDockLog.ContainsNewLine(offset, "destroyed; removing its tab."), timeoutMs);
    }

    // -------------------------------------------------------------------------
    // torture-split-member-destroy: three phases against one four-pig group.
    //   (a) A|B presented; kill PRESENTED LEFT member A -> B promoted visible
    //       full-width, container interactive, zero EXCEPTION.
    //   (b) B|C presented; suspend by clicking third captured tab D; kill
    //       DORMANT member B -> no ghost pane (the dead member's pane region
    //       resolves only to real TabDock/guest windows), C still usable, and
    //       the relationship produced member-gone or exit evidence.
    //   (c) C|D presented again; kill PRESENTED LEFT member C -> D promoted
    //       visible full-width.
    // -------------------------------------------------------------------------
    private static void TortureSplitMemberDestroy(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "TSD-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "TSD-B", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "TSD-C", "--color", "green");
        GuestInfo pigD = SpawnPig(ctx, "TSD-D", "--color", "yellow");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC, pigD);
        ctx.Check(TabCount(container) == 4, "member-destroy: 4 tabs after capture");

        // --- Phase (a): kill the PRESENTED left member. ---
        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, "Split screen", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "phase (a): A|B entered split");
        AssertSplitPanes(ctx, host, pigA, pigB, "phase (a) enter");

        long killOff = TabDockLog.RecordLogLength();
        GuardedProc.Log("  phase (a): killing PRESENTED LEFT member A (GuardedProc-tracked Process.Kill).");
        pigA.Proc!.Kill();
        ctx.Check(Util.WaitUntil(() => pigA.Proc!.HasExited, 5000), "phase (a): pig A process exited");
        ctx.Check(WaitForMemberGoneEvidence(killOff, 8000),
            "phase (a): TabDock logged member-gone or destroyed-removal evidence for A");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigB.Hwnd) && IsDocked(pigB.Hwnd, host), 5000),
            "phase (a): B promoted visible full-width");
        ctx.Check(Input.ForceForeground(container), "phase (a): container still interactive (foregroundable)");
        ctx.Check(TabDockLog.CountNewLines(killOff, "EXCEPTION") == 0, "phase (a): zero EXCEPTION lines");

        // --- Phase (b): suspend by clicking a third captured tab, then kill the DORMANT member. ---
        long enterOff2 = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigB.Title, "Split screen", pigC.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff2, "SPLIT[enter]", 3000), "phase (b): B|C entered split");
        AssertSplitPanes(ctx, host, pigB, pigC, "phase (b) enter");

        long suspOff = TabDockLog.RecordLogLength();
        ClickTabCenter(container, pigD.Title);
        ctx.Check(TabDockLog.WaitForLogLine(suspOff, "SPLIT[suspend]", 3000),
            "phase (b): clicking third captured tab D suspended the pair");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigD.Hwnd) && IsDocked(pigD.Hwnd, host), 3000),
            "phase (b): D presented full-width while the pair is suspended");

        long killOff2 = TabDockLog.RecordLogLength();
        GuardedProc.Log("  phase (b): killing DORMANT member B (GuardedProc-tracked Process.Kill).");
        pigB.Proc!.Kill();
        ctx.Check(Util.WaitUntil(() => pigB.Proc!.HasExited, 5000), "phase (b): pig B process exited");
        ctx.Check(WaitForMemberGoneEvidence(killOff2, 8000)
            || TabDockLog.ContainsNewLine(killOff2, "SPLIT[exit]"),
            "phase (b): relationship safely cleared or dormant (member-gone / destroyed-removal / exit evidence)");
        // No ghost pane: the point at the center of the dead member's former
        // LEFT pane must resolve to a REAL window (host content root, the
        // container frame, the launcher, or a surviving guest) — never an
        // orphaned pane surface.
        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int paneProbeX = hostRect.left + hostRect.Width / 4;
        int paneProbeY = hostRect.top + hostRect.Height / 2;
        IntPtr rootAtPane = NativeMethods.GetAncestor(
            NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = paneProbeX, y = paneProbeY }), NativeMethods.GA_ROOT);
        bool knownRoot = rootAtPane == host || rootAtPane == container || rootAtPane == ctx.MainHwnd
            || rootAtPane == pigC.Hwnd || rootAtPane == pigD.Hwnd;
        GuardedProc.Log($"  phase (b): pane probe ({paneProbeX},{paneProbeY}) -> root 0x{rootAtPane.ToInt64():X}");
        ctx.Check(knownRoot, "phase (b): dormant member's pane resolves only to real guests/TabDock windows (no ghost pane)");
        ClickTabCenter(container, pigC.Title);
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigC.Hwnd) && IsDocked(pigC.Hwnd, host), 3000),
            "phase (b): C still usable (presented full-width on selection)");
        ctx.Check(TabDockLog.CountNewLines(killOff2, "EXCEPTION") == 0, "phase (b): zero EXCEPTION lines");

        // --- Phase (c): re-enter C|D, then kill the PRESENTED left member. ---
        // Only C and D remain alive: the two-tab direct 'Split screen' action
        // (auto-pairing) is the correct UI path here; the partner submenu is
        // for groups with 3+ tabs.
        long enterOff3 = EnterSplitTwo(ctx, container, pigC);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff3, "SPLIT[enter]", 3000), "phase (c): C|D entered split");
        AssertSplitPanes(ctx, host, pigC, pigD, "phase (c) enter");

        long killOff3 = TabDockLog.RecordLogLength();
        GuardedProc.Log("  phase (c): killing PRESENTED LEFT member C (GuardedProc-tracked Process.Kill).");
        pigC.Proc!.Kill();
        ctx.Check(Util.WaitUntil(() => pigC.Proc!.HasExited, 5000), "phase (c): pig C process exited");
        ctx.Check(WaitForMemberGoneEvidence(killOff3, 8000),
            "phase (c): TabDock logged member-gone or destroyed-removal evidence for C");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(pigD.Hwnd) && IsDocked(pigD.Hwnd, host), 6000),
            "phase (c): D promoted visible full-width");
        ctx.Check(TabDockLog.CountNewLines(killOff3, "EXCEPTION") == 0, "phase (c): zero EXCEPTION lines");
    }

    /// <summary>
    /// Registers the GuineaPig's second same-process top-level window
    /// ('&lt;base&gt;-W2', created by the '--extra-windows 1' launch flag) as a
    /// first-class guest so capture, cleanup, and orphan checks treat it like
    /// any other spawned window. It shares the primary's Process/Pid/pig log.
    /// </summary>
    private static GuestInfo AttachSecondPigWindow(Ctx ctx, GuestInfo primary)
    {
        var g = new GuestInfo
        {
            Proc = primary.Proc,
            Pid = primary.Pid,
            Title = primary.Title + "-W2",
            IsPig = true,
            Role = primary.Role + "-W2",
        };
        g.Hwnd = Discover.WaitForTopLevelWindow(g.Pid, t => t == g.Title, 15000);
        if (g.Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Pig second window '{g.Title}' did not appear within 15s.");
        RememberGuestWindow(g);
        ctx.Guests.Add(g);
        GuardedProc.Log($"  Pig second window '{g.Title}' HWND 0x{g.Hwnd.ToInt64():X} (same process {g.Pid}).");
        return g;
    }

    /// <summary>Posts WM_CLOSE to the container and waits for the Yes/No/Cancel close-group prompt.</summary>
    private static IntPtr OpenCloseGroupPrompt(Ctx ctx, IntPtr container)
    {
        VerifiedWindowOps.PostMessage(container, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg = IntPtr.Zero;
        Util.WaitUntil(() => (dlg = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        return dlg;
    }

    /// <summary>Real-clicks the first found of the given texts on a MessageBox.</summary>
    private static void ClickMessageBoxButton(Ctx ctx, IntPtr dlg, params string[] texts)
    {
        IntPtr btn = Discover.FindChildWindowByText(dlg, texts);
        if (btn == IntPtr.Zero)
            throw new InvalidOperationException($"Message box button '{texts[texts.Length - 1]}' not found.");
        if (!Input.ForceForeground(dlg))
            throw new InvalidOperationException("Could not bring the prompt to the foreground; refusing to click.");
        NativeMethods.GetWindowRect(btn, out NativeMethods.RECT rc);
        Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
    }

    // -------------------------------------------------------------------------
    // torture-closegroup-same-process: one pig process launched with
    // '--extra-windows 1' yields TWO same-class same-process top-level windows
    // ('<base>' and '<base>-W2'); both are captured into ONE group. Then all
    // three answers of the native close-group prompt are exercised against
    // fresh captures: YES closes (exits) both windows; NO releases both
    // standalone without any WM_CLOSE reaching the pig; CANCEL leaves the
    // group untouched. One TabDock instance serves all three phases; total
    // spawns = 1 TabDock + 3 pig processes (one per phase) <= 4.
    // -------------------------------------------------------------------------
    private static void TortureCloseGroupSameProcess(Ctx ctx, Options opt)
    {
        // --- Phase 1: YES closes both same-process windows. ---
        GuestInfo pig1 = SpawnPig(ctx, "TGC", "--color", "red", "--extra-windows", "1");
        GuestInfo w2a = AttachSecondPigWindow(ctx, pig1);
        (IntPtr container1, IntPtr _) = CaptureIntoGroupExact(ctx, pig1, w2a);
        ctx.Check(TabCount(container1) == 2, "phase 1: both same-process windows captured into one group");
        long off1 = TabDockLog.RecordLogLength();

        IntPtr dlg1 = OpenCloseGroupPrompt(ctx, container1);
        ctx.Check(dlg1 != IntPtr.Zero, "phase 1: close-group prompt appeared on WM_CLOSE");
        if (dlg1 != IntPtr.Zero)
            ClickMessageBoxButton(ctx, dlg1, "&Yes", "Yes");
        ctx.Check(PigLog.WaitForPigLine(pig1.Pid, "WM_CLOSE", 5000), "phase 1: pig log gained WM_CLOSE");
        ctx.Check(PigLog.WaitForPigLine(pig1.Pid, "LIFECYCLE FormClosed", 5000), "phase 1: pig log gained FormClosed");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container1), 5000), "phase 1: container closed");
        ctx.Check(Util.WaitUntil(() => pig1.Proc!.HasExited, 5000), "phase 1: pig process exited (both windows closed)");
        ctx.Check(TabDockLog.CountNewLines(off1, "Closed group ") > 0, "phase 1: TabDock logged 'Closed group '");
        ctx.Check(TabDockLog.CountNewLines(off1, "EXCEPTION") == 0, "phase 1: zero EXCEPTION lines");

        // --- Phase 2: NO releases both windows standalone, sending nothing. ---
        GuestInfo pig2 = SpawnPig(ctx, "TGN", "--color", "blue", "--extra-windows", "1");
        GuestInfo w2b = AttachSecondPigWindow(ctx, pig2);
        (IntPtr container2, IntPtr host2) = CaptureIntoGroupExact(ctx, pig2, w2b);
        ctx.Check(TabCount(container2) == 2, "phase 2: both same-process windows captured into one group");
        int wmCloseBefore2 = PigLog.CountLines(pig2.Pid, "WM_CLOSE");
        long off2 = TabDockLog.RecordLogLength();

        IntPtr dlg2 = OpenCloseGroupPrompt(ctx, container2);
        ctx.Check(dlg2 != IntPtr.Zero, "phase 2: close-group prompt appeared on WM_CLOSE");
        if (dlg2 != IntPtr.Zero)
            ClickMessageBoxButton(ctx, dlg2, "&No", "No");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container2), 5000), "phase 2: container closed");
        ctx.Check(PigLog.CountLines(pig2.Pid, "WM_CLOSE") == wmCloseBefore2,
            "phase 2: neither window received a new WM_CLOSE (shared same-process pig log)");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pig2.Hwnd, host2) && IsReleasedAndShown(w2b.Hwnd, host2), 5000),
            "phase 2: both windows alive standalone after No");
        ctx.Check(!pig2.Proc!.HasExited, "phase 2: pig process survived the No answer");
        ctx.Check(TabDockLog.CountNewLines(off2, "EXCEPTION") == 0, "phase 2: zero EXCEPTION lines");

        // --- Phase 3: CANCEL leaves everything untouched. ---
        GuestInfo pig3 = SpawnPig(ctx, "TCC", "--color", "green", "--extra-windows", "1");
        GuestInfo w2c = AttachSecondPigWindow(ctx, pig3);
        (IntPtr container3, IntPtr host3) = CaptureIntoGroupExact(ctx, pig3, w2c);
        ctx.Check(TabCount(container3) == 2, "phase 3: both same-process windows captured into one group");
        long off3 = TabDockLog.RecordLogLength();

        IntPtr dlg3 = OpenCloseGroupPrompt(ctx, container3);
        ctx.Check(dlg3 != IntPtr.Zero, "phase 3: close-group prompt appeared on WM_CLOSE");
        if (dlg3 != IntPtr.Zero)
        {
            ClickMessageBoxButton(ctx, dlg3, "Cancel");
            Util.WaitUntil(() => !NativeMethods.IsWindow(dlg3), 3000);
        }
        Thread.Sleep(400);
        ctx.Check(NativeMethods.IsWindow(container3), "phase 3: container still open after Cancel");
        ctx.Check(TabCount(container3) == 2, "phase 3: both tabs still present after Cancel");
        ctx.Check(TabDockLog.CountNewLines(off3, "Released tab") == 0, "phase 3: zero 'Released tab' lines after Cancel");
        ctx.Check(!pig3.Proc!.HasExited && NativeMethods.IsWindow(pig3.Hwnd) && NativeMethods.IsWindow(w2c.Hwnd),
            "phase 3: both windows alive after Cancel");
        ctx.Check((IsDocked(pig3.Hwnd, host3) || IsReleasedAndHidden(pig3.Hwnd))
            && (IsDocked(w2c.Hwnd, host3) || IsReleasedAndHidden(w2c.Hwnd)),
            "phase 3: both windows still captured after Cancel");
        ctx.Check(TabDockLog.CountNewLines(off3, "EXCEPTION") == 0, "phase 3: zero EXCEPTION lines");
    }

    // -------------------------------------------------------------------------
    // torture-minrestore-soak: A|B split, then N container minimize/restore
    // cycles (default 50, cap 80). Each cycle asserts the exact pane partition
    // (IsInPane both members), zero SPLIT[exit], zero 'Released tab', zero
    // EXCEPTION; every 10th cycle additionally clicks each half and requires
    // the matching SPLIT[focus] diagnostic for that member's HWND.
    // -------------------------------------------------------------------------
    private static void TortureMinRestoreSoak(Ctx ctx, Options opt)
    {
        int cycles = Math.Min(opt.Cycles ?? 50, 80);
        GuestInfo pigA = SpawnPig(ctx, "TMR-A", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "TMR-B", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        long enterOff = EnterSplitTwo(ctx, container, pigA);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, "SPLIT[enter]", 3000), "soak: A|B entered split");
        AssertSplitPanes(ctx, host, pigA, pigB, "soak enter");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            long cycOff = TabDockLog.RecordLogLength();
            ClickMinimizeButton(container);
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd) && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
                $"cycle {cycle}/{cycles}: both members hidden with the minimized container");
            VerifiedWindowOps.ShowWindow(container, ctx.TabDockPid, NativeMethods.SW_RESTORE);
            AssertSplitPanes(ctx, host, pigA, pigB, $"cycle {cycle} restore");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "SPLIT[exit]") == 0, $"cycle {cycle}: zero SPLIT[exit] lines");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "Released tab") == 0, $"cycle {cycle}: zero 'Released tab' lines");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "EXCEPTION") == 0, $"cycle {cycle}: zero EXCEPTION lines");

            if (cycle % 10 == 0)
            {
                long focusOff = TabDockLog.RecordLogLength();
                NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
                Input.ClickAt(hostRect.left + hostRect.Width / 4, hostRect.top + hostRect.Height / 2);
                ctx.Check(WaitForSplitFocus(focusOff, pigA, 3000),
                    $"cycle {cycle}: LEFT half click emitted SPLIT[focus] for '{pigA.Title}'");
                Input.ClickAt(hostRect.left + 3 * hostRect.Width / 4, hostRect.top + hostRect.Height / 2);
                ctx.Check(WaitForSplitFocus(focusOff, pigB, 3000),
                    $"cycle {cycle}: RIGHT half click emitted SPLIT[focus] for '{pigB.Title}'");
            }
        }

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "soak: no EXCEPTION lines across all cycles");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "soak: both pigs alive across all cycles");
    }

    // -------------------------------------------------------------------------
    // torture-crash-restart-soak: mirrors persist-kill per cycle — capture the
    // pig, note its pre-capture rect, hard-kill TabDock (Process.Kill, no
    // graceful shutdown), relaunch fresh, require the rescue journal line
    // ("SHEPHERD[rescue] restored guest 0x<hwnd>") and the pig visible
    // standalone near its pre-capture placement, then recapture next cycle.
    // Spawn math: 1 initial TabDock + 1 reused pig + N relaunches <= the
    // 12-spawn-per-scenario cap => N <= 10, so the effective cycle cap is 10
    // regardless of --cycles (default request 20, spec cap 25, both clamped).
    // -------------------------------------------------------------------------
    private static void TortureCrashRestartSoak(Ctx ctx, Options opt)
    {
        int cycles = Math.Min(opt.Cycles ?? 20, 10);
        GuestInfo pig = SpawnPig(ctx, "TCR", "--color", "blue");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- torture-crash-restart-soak {cycle}/{cycles} ---");
            NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT preCapture);
            (_, IntPtr host) = CaptureIntoGroup(ctx, pig);
            ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 4000),
                $"cycle {cycle}: pig docked before the hard kill");

            ctx.TabDock.Kill();
            ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), $"cycle {cycle}: TabDock force-killed");
            Thread.Sleep(700);
            ctx.Check(!pig.Proc!.HasExited, $"cycle {cycle}: pig process survived the TabDock hard kill");

            long relaunchOffset = TabDockLog.RecordLogLength();
            Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
            });
            ctx.TabDock = td2;
            ctx.TabDockPid = (uint)td2.Id;
            TestRunProvenance.RegisterLaunchedProcess(td2, "TabDockUnderTest", out _);
            // The restored group's container can hide the launcher within ~50ms
            // (see PersistKill), so wait for ANY visible pid window first, then
            // locate the 'TabDock'-titled launcher regardless of visibility.
            ctx.MainHwnd = IntPtr.Zero;
            Util.WaitUntil(() => Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true).Count > 0, 20000);
            foreach (IntPtr hwnd in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: false))
            {
                if (string.Equals(NativeMethods.GetWindowTextString(hwnd), "TabDock", StringComparison.Ordinal))
                {
                    ctx.MainHwnd = hwnd;
                    break;
                }
            }
            ctx.Check(ctx.MainHwnd != IntPtr.Zero, $"cycle {cycle}: relaunched TabDock launcher located");
            if (ctx.MainHwnd != IntPtr.Zero)
                RememberMainWindow(ctx);

            ctx.Check(TabDockLog.WaitForLogLine(relaunchOffset, $"SHEPHERD[rescue] restored guest 0x{pig.Hwnd.ToInt64():X}", 10000),
                $"cycle {cycle}: rescue journal restored the pig");
            ctx.Check(Util.WaitUntil(() =>
            {
                NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT now);
                return NativeMethods.IsWindowVisible(pig.Hwnd) && Util.RectNear(now, preCapture, 40);
            }, 8000),
                $"cycle {cycle}: pig visible standalone near its pre-capture placement ({Util.FormatRect(preCapture)})");
            ctx.Check(TabDockLog.CountNewLines(relaunchOffset, "EXCEPTION") == 0,
                $"cycle {cycle}: zero EXCEPTION lines during relaunch/rescue");

            // Close the restored EMPTY group container so the launcher is
            // visible again for the next cycle's capture hotkey (an empty
            // container closes silently; only populated ones prompt).
            IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Group", 10000);
            if (restored != IntPtr.Zero)
            {
                RememberContainer(ctx, restored);
                VerifiedWindowOps.PostMessage(restored, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                bool closed = Util.WaitUntil(() => !NativeMethods.IsWindow(restored), 5000);
                ctx.Check(closed, $"cycle {cycle}: restored empty container closed cleanly for the next cycle");
            }
            Thread.Sleep(800); // let the shell settle before the next recapture
        }

        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "crash-restart: pig survived all crash/restart cycles");
    }
}
