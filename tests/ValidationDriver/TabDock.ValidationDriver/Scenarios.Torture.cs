using System;
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
}
