using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

/// <summary>
/// Startup z-order regression scenarios for the TabDock startup-hide defect
/// (a restored/opened group can land hidden behind an already-existing desktop
/// window when the group initially overlaps it). The product fix adds a one-shot
/// STARTUP[reconcile] that raises restored containers to the top of the normal
/// z-order band via HWND_TOP + SWP_NOACTIVATE (z-order only, never foreground).
///
/// These methods live in their own partial part of <see cref="Scenarios"/> so the
/// large Scenarios.cs body is untouched; they call the same private helpers
/// (SpawnPig, CaptureIntoGroup, StateJsonContains, RememberContainer, IsDocked, ...).
/// </summary>
internal static partial class Scenarios
{
    /// <summary>Shared: build a persisted group, rename it to <paramref name="groupTitle"/>,
    /// wait for the debounced state.json save, then kill TabDock and the leftover pig so the
    /// group restores EMPTY on relaunch. Mirrors restored-group-survives-member-reclose.</summary>
    private static void BuildPersistedGroupThenKill(Ctx ctx, string pigTag, string color, string groupTitle)
    {
        GuestInfo pig = SpawnPig(ctx, pigTag, "--color", color);
        (IntPtr container, _) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => StateJsonContains(pig.Title), 5000),
            "state.json contains the captured tab's title (debounced save)");

        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int capCount);
        if (caption == null || capCount != 1)
            throw new InvalidOperationException($"Container caption 'Group' not found uniquely (count={capCount}).");
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to rename blind.");
        (int cx, int cy) = Uia.Center(caption);
        bool renamed = false;
        for (int attempt = 0; attempt < 3 && !renamed; attempt++)
        {
            Input.DoubleClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText(groupTitle);
            Input.SendKey(Input.VK_RETURN);
            renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == groupTitle, 2000);
        }
        ctx.Check(renamed, $"group renamed to {groupTitle}");
        ctx.Check(Util.WaitUntil(() => StateJsonContains(groupTitle), 3000), "state.json contains the group rename");

        // Kill TabDock, then the leftover pig, so the persisted group restores EMPTY
        // (its member window is gone) — the exact path the startup-hide defect buries.
        ctx.TabDock.Kill();
        ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
        Thread.Sleep(1000);
        if (pig.Proc != null && !pig.Proc.HasExited)
        {
            pig.Proc.Kill(entireProcessTree: true);
            Util.WaitUntil(() => pig.Proc.HasExited, 5000);
        }
    }

    /// <summary>Relaunch a fresh TabDock manually (no StartScenario) and update ctx. Tracks the
    /// new process via SpawnGuarded so Cleanup kills it. Returns the post-relaunch log offset.</summary>
    private static void RelaunchTabDockManual(Ctx ctx, out long logOffset)
    {
        logOffset = TabDockLog.RecordLogLength();
        ctx.TabDock = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDockPid = (uint)ctx.TabDock.Id;
        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        if (ctx.MainHwnd != IntPtr.Zero)
            RememberMainWindow(ctx);
    }

    private static NativeMethods.RECT PrimaryWorkAreaRect()
    {
        IntPtr mon = NativeMethods.MonitorFromWindow(IntPtr.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(mon, ref mi);
        return mi.rcWork;
    }

    private static void ExpandToWorkArea(IntPtr hwnd)
    {
        NativeMethods.RECT rc = PrimaryWorkAreaRect();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, rc.left, rc.top, rc.Width, rc.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    private static uint PidOfWindow(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>Replicates WindowShepherdService.IsContainerBelowGuest: walks GW_HWNDPREV (upward)
    /// from the container; true if the guest is reached (guest sits above the container).</summary>
    private static bool IsContainerBelowGuest(IntPtr containerHwnd, IntPtr guestHwnd)
    {
        IntPtr cur = NativeMethods.GetWindow(containerHwnd, NativeMethods.GW_HWNDPREV);
        while (cur != IntPtr.Zero)
        {
            if (cur == guestHwnd)
                return true;
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
        }
        return false;
    }

    /// <summary>True if <paramref name="container"/> sits ABOVE <paramref name="blocker"/>: walk
    /// GW_HWNDNEXT (downward) from the container and confirm we reach the blocker.</summary>
    private static bool ContainerAboveInZOrder(IntPtr container, IntPtr blocker)
    {
        IntPtr cur = NativeMethods.GetWindow(container, NativeMethods.GW_HWNDNEXT);
        while (cur != IntPtr.Zero)
        {
            if (cur == blocker)
                return true;
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDNEXT);
        }
        return false;
    }

    /// <summary>
    /// THE regression/reproduction for the startup-hide defect. A ValidationDriver-spawned
    /// TabDock is a background process with no foreground permission, so a freshly Show()n
    /// top-level window lands directly BENEATH the current foreground window. If a blocker
    /// covers the primary work area and is foreground when TabDock relaunches with a persisted
    /// EMPTY group, the pre-fix container is parked beneath the blocker (buried); the fix raises
    /// it above via HWND_TOP + SWP_NOACTIVATE. Proven with native WindowFromPoint + z-order walk.
    /// </summary>
    private static void StartupGroupNotHiddenBehindExistingWindow(Ctx ctx, Options opt)
    {
        BuildPersistedGroupThenKill(ctx, "SUG", "red", "TDVAL-SUG");

        // Phase 2: blocker covering the work area, foreground BEFORE relaunch.
        GuestInfo blocker = SpawnPig(ctx, "BLK", "--color", "gray");
        ExpandToWorkArea(blocker.Hwnd);
        ctx.Check(Input.ForceForeground(blocker.Hwnd), "blocker covering the work area forced to foreground before TabDock relaunch");

        RelaunchTabDockManual(ctx, out long off);
        ctx.Check(TabDockLog.WaitForLogLine(off, "TabDock startup complete.", 20000), "TabDock logged 'TabDock startup complete.' after relaunch");
        IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-SUG", 20000);
        ctx.Check(restored != IntPtr.Zero, "restored empty container 'TDVAL-SUG' opened after relaunch");
        if (restored != IntPtr.Zero)
            RememberContainer(ctx, restored);

        ctx.Check(NativeMethods.IsWindowVisible(restored), "restored container is visible");

        NativeMethods.GetWindowRect(restored, out NativeMethods.RECT rc);
        NativeMethods.POINT center = new NativeMethods.POINT { x = rc.left + rc.Width / 2, y = rc.top + rc.Height / 2 };
        IntPtr hit = NativeMethods.WindowFromPoint(center);
        uint hitPid = PidOfWindow(hit);
        ctx.Check(hitPid == ctx.TabDockPid && hitPid != blocker.Pid,
            $"WindowFromPoint at container center resolves to TabDock PID (hit 0x{hit.ToInt64():X} pid {hitPid}), not the blocker (pid {blocker.Pid})");

        ctx.Check(ContainerAboveInZOrder(restored, blocker.Hwnd),
            "restored container is ABOVE the blocker in z-order (GW_HWNDNEXT walk reaches the blocker)");
    }

    /// <summary>
    /// GUARD: must PASS both before and after the fix. It only catches a WRONG fix that calls
    /// Activate()/SetForegroundWindow on startup. Same setup as the regression (restored container
    /// up, blocker foreground), then we re-force the blocker to the foreground and assert TabDock
    /// never re-takes it. No log-line assertions — native GetForegroundWindow + PID only.
    /// </summary>
    private static void StartupDoesNotStealForegroundAfterExternalActivation(Ctx ctx, Options opt)
    {
        BuildPersistedGroupThenKill(ctx, "SNF", "red", "TDVAL-SNF");

        GuestInfo blocker = SpawnPig(ctx, "SNB", "--color", "gray");
        ExpandToWorkArea(blocker.Hwnd);
        ctx.Check(Input.ForceForeground(blocker.Hwnd), "blocker forced to foreground before relaunch");

        RelaunchTabDockManual(ctx, out long off);
        ctx.Check(TabDockLog.WaitForLogLine(off, "TabDock startup complete.", 20000), "TabDock logged 'TabDock startup complete.' after relaunch");
        IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-SNF", 20000);
        ctx.Check(restored != IntPtr.Zero, "restored container 'TDVAL-SNF' opened after relaunch");
        if (restored != IntPtr.Zero)
            RememberContainer(ctx, restored);

        ctx.Check(Input.ForceForeground(blocker.Hwnd), "blocker re-forced to foreground after startup");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == blocker.Hwnd, 3000),
            "GetForegroundWindow == blocker after forcing it");

        // A wrong fix (Activate/SetForegroundWindow at startup) would re-take foreground within this window.
        Thread.Sleep(1500);
        IntPtr fgNow = NativeMethods.GetForegroundWindow();
        uint fgPid = PidOfWindow(fgNow);
        ctx.Check(fgNow == blocker.Hwnd && fgPid == blocker.Pid,
            $"foreground is STILL the blocker after 1.5s (TabDock did not steal it; fg 0x{fgNow.ToInt64():X} pid {fgPid})");
    }

    /// <summary>
    /// GUARD for the shared z-order/Shepherd invariants (single live TabDock, pig-only, joins
    /// AllOrder). Spawn a blocker covering the work area and foreground it, capture a live pig, and
    /// assert the startup fix did not disturb PositionAndShow/PairZOrderBehindCore: (a) the container
    /// sits BELOW its active guest; (b) WindowFromPoint at the container caption resolves to TabDock,
    /// not the blocker; (c) WindowFromPoint at the content-host center resolves to the guest (on top).
    /// </summary>
    private static void StartupLocalStackAboveUnrelatedWhenGuestPresent(Ctx ctx, Options opt)
    {
        GuestInfo blocker = SpawnPig(ctx, "LSB", "--color", "gray");
        ExpandToWorkArea(blocker.Hwnd);
        ctx.Check(Input.ForceForeground(blocker.Hwnd), "blocker covering the work area forced to foreground");

        GuestInfo pig = SpawnPig(ctx, "LSG", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000), "pig docked over its content host");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(container), 2000), "container visible");

        ctx.Check(IsContainerBelowGuest(container, pig.Hwnd),
            "container sits BELOW its active guest (upward GW_HWNDPREV walk reaches the guest)");

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        NativeMethods.POINT capPt = new NativeMethods.POINT { x = rc.left + rc.Width / 2, y = rc.top + 16 };
        IntPtr capHit = NativeMethods.WindowFromPoint(capPt);
        uint capPid = PidOfWindow(capHit);
        ctx.Check(capPid == ctx.TabDockPid && capPid != blocker.Pid,
            $"WindowFromPoint at the container caption resolves to TabDock PID (hit pid {capPid}), not the blocker");

        IntPtr contentHost = Discover.FindChildByClass(container, ContentHostClass);
        ctx.Check(contentHost != IntPtr.Zero, "content host found for the container");
        if (contentHost != IntPtr.Zero)
        {
            NativeMethods.GetWindowRect(contentHost, out NativeMethods.RECT hrc);
            NativeMethods.POINT ctrPt = new NativeMethods.POINT { x = hrc.left + hrc.Width / 2, y = hrc.top + hrc.Height / 2 };
            IntPtr ctrHit = NativeMethods.WindowFromPoint(ctrPt);
            uint ctrPid = PidOfWindow(ctrHit);
            ctx.Check(ctrPid == pig.Pid && ctrPid != blocker.Pid,
                $"WindowFromPoint at the content-host center resolves to the guest (pid {ctrPid}), not the blocker");
        }
    }
}
