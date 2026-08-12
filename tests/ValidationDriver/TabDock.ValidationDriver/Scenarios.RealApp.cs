using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 18. realapp (real Codex / ChatGPT Classic): fill + maximize/restore +
    //     hide-on-close, against the user's OWN already-running app instance.
    //     Never spawned, never killed by this driver (GuestInfo.DoNotKill) —
    //     "Close window" is expected to hide it back to tray (its normal
    //     X-click behavior), exactly like a real user closing the tab, not
    //     terminate it. Not part of AllOrder/"all" — must be invoked by name.
    // -------------------------------------------------------------------------
    private static void RealAppFillMaxHide(Ctx ctx, Options opt)
    {
        GuestInfo app = SpawnGuest(ctx, opt.Guest);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, app);

        // "Fill": click into the window and type a short, clearly-marked
        // throwaway line WITHOUT pressing Enter/Send. This exercises real
        // rendering/layout with real on-screen content without submitting
        // anything to a live account/session — deliberately conservative.
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        Input.ClickAt(hostRect.left + hostRect.Width / 2, hostRect.top + hostRect.Height - 60);
        Thread.Sleep(300);
        Input.TypeText("TDVAL test fill - please ignore, not sent");
        Thread.Sleep(300);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoFill), $"guest still fills host after fill ({geoFill})");

        // Maximize / restore cycle. Only one monitor is available in this
        // environment, so the checklist's "2nd monitor" clause cannot be
        // exercised here — noted, not silently skipped.
        ClickMaximizeButton(container);
        Thread.Sleep(1200);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoMax), $"geometry OK after maximize ({geoMax})");
        ClickMaximizeButton(container);
        Thread.Sleep(1200);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoRest), $"geometry OK after restore ({geoRest})");

        // Hide-on-close: "Close window" from the tab menu should hide the
        // real app back to tray (never terminate it) — its normal X-click
        // behavior, exercised through TabDock's teardown path instead.
        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, app.Title, "Close window");
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off, "hid itself")
                || TabDockLog.ContainsNewLine(off, "destroyed; removing its tab"), 8000),
            "TabDock log shows the tab was torn down (hide or destroy path)");
        Thread.Sleep(1500);
        ctx.Check(app.Proc != null && !app.Proc.HasExited, "real app process still alive after close (hidden, not terminated)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");

        GuardedProc.Log($"  Real app '{app.Title}' left in its normal hidden/tray state (never captured, never killed by cleanup).");
    }

    // -------------------------------------------------------------------------
    // 29. realapp-multi-render: replaces the old (now-deleted) CaptureReleaseTest
    //     project's unique value — real rendered-pixel verification — adapted
    //     to Shepherd's stronger guarantee: since a shepherded guest is NEVER
    //     reparented or restyled, release must restore BYTE-IDENTICAL
    //     placement/style/exstyle/parent, not just "close enough". Verifies
    //     rendering via PrintWindow directly on the guest's own HWND rather
    //     than Pixels.CaptureHostScreenArea's screen-region BitBlt — see
    //     Pixels.CaptureWindowViaPrintWindow's doc comment for why.
    // -------------------------------------------------------------------------
    private static void RealAppMultiRender(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "RAMR", "--color", "blue");

        var placementBefore = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        NativeMethods.GetWindowPlacement(pig.Hwnd, ref placementBefore);
        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT rectBefore);
        long styleBefore = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE);
        long exstyleBefore = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr parentBefore = NativeMethods.GetParent(pig.Hwnd);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000), "pig docked over host after capture");

        Input.ForceForegroundRoot(host);
        Thread.Sleep(500);
        int[]? dockedFrame = Pixels.CaptureWindowViaPrintWindow(pig.Hwnd);
        double dockedBrightness = dockedFrame != null ? Pixels.ComputeAvgBrightness(dockedFrame) : -1;
        char dockedDominant = dockedFrame != null ? Pixels.DominantChannel(dockedFrame) : '?';
        ctx.Check(dockedFrame != null && dockedBrightness > 1.0,
            $"PrintWindow capture of the docked guest is not black (brightness={dockedBrightness:F2})");
        ctx.Check(dockedDominant == 'b', $"PrintWindow capture shows the pig's own blue content (dominant channel='{dockedDominant}')");

        ClickTabMenuItem(ctx, container, pig.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleasedAndShown(pig.Hwnd, host), 5000), "pig released and shown at its own placement");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 3000), "container closed (last tab popped out)");

        var placementAfter = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        NativeMethods.GetWindowPlacement(pig.Hwnd, ref placementAfter);
        NativeMethods.GetWindowRect(pig.Hwnd, out NativeMethods.RECT rectAfter);
        long styleAfter = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE);
        long exstyleAfter = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr parentAfter = NativeMethods.GetParent(pig.Hwnd);

        ctx.Check(Util.RectNear(rectBefore, rectAfter, 0),
            $"GetWindowRect byte-identical after release (before {Util.FormatRect(rectBefore)}, after {Util.FormatRect(rectAfter)})");
        ctx.Check(Util.RectNear(placementBefore.rcNormalPosition, placementAfter.rcNormalPosition, 0),
            "WINDOWPLACEMENT.rcNormalPosition byte-identical after release");
        ctx.Check(placementBefore.showCmd == placementAfter.showCmd,
            $"WINDOWPLACEMENT.showCmd unchanged (before={placementBefore.showCmd}, after={placementAfter.showCmd})");
        ctx.Check(styleBefore == styleAfter, $"GWL_STYLE bits byte-identical (before=0x{styleBefore:X}, after=0x{styleAfter:X})");
        ctx.Check(exstyleBefore == exstyleAfter, $"GWL_EXSTYLE bits byte-identical (before=0x{exstyleBefore:X}, after=0x{exstyleAfter:X})");
        ctx.Check(parentBefore == IntPtr.Zero && parentAfter == IntPtr.Zero,
            $"parent is IntPtr.Zero both before and after (never reparented) (before=0x{parentBefore.ToInt64():X}, after=0x{parentAfter.ToInt64():X})");

        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive throughout capture and release");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");

        // Best-effort secondary coverage: a real GPU-accelerated app (Chrome),
        // verified via the same PrintWindow-based method. Deliberately NOT a
        // hard ctx.Check — real-browser capture in an unattended run is
        // flakier (profile/session state, first-paint timing) than the
        // deterministic pig, so a failure here only logs a warning and never
        // fails the scenario.
        try
        {
            GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
            (IntPtr chromeContainer, IntPtr chromeHost) = CaptureIntoGroup(ctx, chrome);
            Util.WaitUntil(() => IsDocked(chrome.Hwnd, chromeHost), 3000);
            Input.ForceForegroundRoot(chromeHost);
            Thread.Sleep(800);

            int[]? chromeFrame = Pixels.CaptureWindowViaPrintWindow(chrome.Hwnd);
            double chromeBrightness = chromeFrame != null ? Pixels.ComputeAvgBrightness(chromeFrame) : -1;
            if (chromeFrame != null && chromeBrightness > 1.0)
                GuardedProc.Log($"  realapp-multi-render (best-effort): Chrome PrintWindow capture rendered correctly (brightness={chromeBrightness:F2}).");
            else
                GuardedProc.Log($"  WARNING (best-effort, not a hard failure): Chrome PrintWindow capture looked black/empty (brightness={chromeBrightness:F2}).");

            ClickTabMenuItem(ctx, chromeContainer, chrome.EffectiveTabMatchKey, "Pop out");
            Util.WaitUntil(() => IsReleased(chrome, chromeHost), 5000);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  WARNING (best-effort, not a hard failure): real-app (Chrome) PrintWindow coverage threw: {ex.Message}");
        }
    }
}
