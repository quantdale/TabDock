using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using TabDock.Services;

namespace TabDock.ValidationDriver;

/// <summary>
/// Physical qualification of native guest presentation gestures. These scenarios
/// deliberately use real caption clicks and real Windows-logo chords; direct
/// ShowWindow/PostMessage calls are observation or cleanup only, never the action
/// under test.
/// </summary>
internal static partial class Scenarios
{
    private static void GuestCaptionMaximizeContained(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "GCM", "--pulse", "--resize-probe", "--color", "red",
            "--min-width", "200", "--min-height", "150");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.ExpectedState = "A physically clicked GuineaPig caption maximize is observed as native maximize input and is reconciled to the same captured pane without a tab click.";
        RunGuestCaptionCycles(ctx, pig, container, host, "GuineaPig", Math.Min(Math.Max(opt.Cycles ?? 2, 2), 3), requirePigMessageEvidence: true);
    }

    private static void NotepadCaptionMaximizeContained(Ctx ctx, Options opt)
    {
        GuestInfo notepad = SpawnNotepad(ctx);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, notepad);
        ctx.ExpectedState = "A physically clicked Notepad caption maximize remains the same captured guest and is restored to its assigned pane without a tab click.";
        RunGuestCaptionCycles(ctx, notepad, container, host, "Notepad", Math.Min(Math.Max(opt.Cycles ?? 2, 2), 3), requirePigMessageEvidence: false);
    }

    private static void GuestWinUpContained(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "GWU", "--pulse", "--resize-probe", "--color", "blue",
            "--min-width", "200", "--min-height", "150");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.ExpectedState = "A real Win+Up against the captured guest is observed as native maximize input, then Win+Down or Shepherd reconciliation restores the same pane.";

        int cycles = Math.Min(Math.Max(opt.Cycles ?? 2, 2), 3);
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            string phase = $"Win+Up cycle {cycle}/{cycles}";
            bool CaptureVisual(string suffix, VisualCheckpointPhase checkpointPhase, string expectation)
            {
                if (!TryGetVisualMonitorBinding(ctx, pig.Hwnd, out VisualTopologyBinding? monitorBinding))
                {
                    ctx.BlockEnvironment($"{phase}: visual checkpoint '{suffix}' could not resolve the guest's observed physical monitor.");
                    return false;
                }
                return CapturePhysicalVisual(
                    ctx,
                    $"gwu-cycle-{cycle}-{suffix}",
                    checkpointPhase,
                    expectation,
                    new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                    monitorBinding);
            }

            if (!CaptureVisual(
                    "baseline",
                    VisualCheckpointPhase.BASELINE,
                    "Win+Up baseline shows the same captured guest over its assigned host pane."))
                return;

            if (!AssertGuestPresentation(ctx, pig, container, host, phase + " baseline"))
                return;

            if (!CaptureVisual(
                    "before-winup",
                    VisualCheckpointPhase.BEFORE_ACTION,
                    "The captured guest is identity-proven and ready for real Win+Up input."))
                return;

            long off = TabDockLog.RecordLogLength();
            int sysMaxBefore = PigLog.CountLines(pig.Pid, "WM_SYSCOMMAND wParam=0xF030");
            if (!Input.SendWinUpTo(pig.Hwnd))
            {
                ctx.BlockEnvironment($"{phase}: exact guest foreground/lease proof refused the real Win+Up");
                return;
            }

            bool nativeMax = WaitForNativeMaximizeEvidence(pig, sysMaxBefore, 2500, out string maxEvidence);
            GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] Win+Up evidence={maxEvidence}");
            if (!nativeMax)
            {
                ctx.BlockEnvironment($"{phase}: Win+Up was dispatched only after identity proof, but no native maximize state/message was observable");
                return;
            }
            ctx.Check(nativeMax, $"{phase}: real Win+Up produced native maximize evidence ({maxEvidence})");
            if (!CaptureVisual(
                    "after-winup",
                    VisualCheckpointPhase.AFTER_ACTION_IMMEDIATE,
                    "Real Win+Up produced native maximize evidence before restoration."))
                return;


            bool restoredByShepherd = Util.WaitUntil(
                () => !NativeMethods.IsZoomed(pig.Hwnd) && IsDocked(pig.Hwnd, host),
                3500,
                40);
            bool sentRestore = false;
            if (!restoredByShepherd && NativeMethods.IsZoomed(pig.Hwnd))
            {
                sentRestore = Input.SendWinDownTo(pig.Hwnd);
                if (sentRestore)
                    restoredByShepherd = Util.WaitUntil(
                        () => !NativeMethods.IsZoomed(pig.Hwnd) && IsDocked(pig.Hwnd, host),
                        2500,
                        40);
            }
            GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] restore shepherd={restoredByShepherd} Win+Down={sentRestore} drift={TabDockLog.CountNewLines(off, "SHEPHERD[drift-reconcile]")}");
            if (!restoredByShepherd)
            {
                ctx.Check(false, $"{phase}: guest restored to its assigned pane after Win+Up (Win+Down sent={sentRestore})");
                return;
            }

            ctx.Check(!NativeMethods.IsZoomed(pig.Hwnd), $"{phase}: guest is not zoomed after restore");
            if (!AssertGuestPresentation(ctx, pig, container, host, phase + " restored"))
                return;
            if (!CaptureVisual(
                    "after-restore",
                    VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                    "Win+Down or Shepherd reconciliation restored the same captured guest pane."))
                return;

            ctx.Check(TabDockLog.CountNewLines(off, "SHEPHERD[drift-reconcile]") > 0
                || TabDockLog.CountNewLines(off, "SHEPHERD[position]") > 0,
                $"{phase}: TabDock recorded native-presentation reconciliation without a tab click");
            ctx.Check(TabCount(container) == 1, $"{phase}: captured tab count remained one");
            ctx.Check(PigLog.CountLines(pig.Pid, "CLIENT_PRESENT") > 0,
                $"{phase}: GuineaPig client-render evidence remained live");
        }
    }

    private static void DualMonitorMixedDpiTransfer(Ctx ctx, Options opt)
    {
        List<DpiMonitor> monitors = EnumerateDpiMonitors();
        DpiMonitor? primary = monitors.FirstOrDefault(m => IsPrimaryMonitor(m));
        DpiMonitor? secondary = monitors.FirstOrDefault(m => !IsPrimaryMonitor(m));
        if (primary == null || secondary == null)
        {
            ctx.SkipCapability("dual-monitor-mixed-dpi-transfer: two physical monitors are required");
            return;
        }
        if (primary.Dpi == 0 || secondary.Dpi == 0)
        {
            ctx.BlockEnvironment("dual-monitor-mixed-dpi-transfer: a monitor DPI probe failed");
            return;
        }
        if (primary.Dpi == secondary.Dpi)
        {
            ctx.SkipCapability($"dual-monitor-mixed-dpi-transfer: monitors are not mixed-DPI ({primary.Dpi} and {secondary.Dpi})");
            return;
        }
        if (!TryGetPhysicalMonitor(ctx, primary, out PhysicalMonitorSnapshot? primarySnapshot)
            || !TryGetPhysicalMonitor(ctx, secondary, out PhysicalMonitorSnapshot? secondarySnapshot)
            || primarySnapshot == null
            || secondarySnapshot == null)
        {
            ctx.BlockEnvironment("dual-monitor-mixed-dpi-transfer: observed monitors could not be bound to the preflight topology snapshot");
            return;
        }

        GuestInfo pig = SpawnPig(ctx, "DMT", "--pulse", "--resize-probe", "--color", "green",
            "--min-width", "200", "--min-height", "150");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.ExpectedState = "The captured guest and container remain paired while the container crosses both physical monitors and real Win+Shift+Arrow transfer attempts are reconciled without a silent roam.";
        GuardedProc.Log($"  PHYSICAL_TOPOLOGY monitors primary={primary.Describe()} secondary={secondary.Describe()}");

        if (NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST) != primary.Handle
            && !MoveContainerToMonitor(ctx, container, pig.Hwnd, primary, "container to primary before transfer"))
        {
            return;
        }

        bool CaptureTransition(
            string id,
            VisualCheckpointPhase phase,
            string expectation,
            PhysicalMonitorSnapshot captureMonitor,
            PhysicalMonitorSnapshot sourceMonitor,
            PhysicalMonitorSnapshot destinationMonitor,
            bool includeContainer)
        {
            VisualTopologyBinding? binding = ctx.VisualTopologyFor(
                includeContainer ? captureMonitor.MonitorId : null,
                includeContainer ? (int)captureMonitor.EffectiveDpi : null,
                sourceMonitor.MonitorId,
                (int)sourceMonitor.EffectiveDpi,
                destinationMonitor.MonitorId,
                (int)destinationMonitor.EffectiveDpi);
            IReadOnlyList<VisualCaptureScope> scopes = includeContainer
                ? new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) }
                : new[] { ctx.VisualGuestScope(pig) };
            return CapturePhysicalVisual(ctx, id, phase, expectation, scopes, binding);
        }

        if (!CaptureTransition(
                "dmt-baseline-primary",
                VisualCheckpointPhase.BASELINE,
                "Primary-monitor baseline shows the captured guest and container paired before either transfer direction.",
                primarySnapshot,
                primarySnapshot,
                secondarySnapshot,
                includeContainer: true))
            return;
        if (!AssertGuestPresentation(ctx, pig, container, host, "dual-monitor baseline"))
            return;

        bool secondaryIsRight = secondary.Bounds.left > primary.Bounds.left;
        if (!CaptureTransition(
                "dmt-before-primary-to-secondary",
                VisualCheckpointPhase.BEFORE_ACTION,
                "The captured guest is ready for a real Win+Shift+Arrow transfer attempt from the primary monitor to the secondary monitor.",
                primarySnapshot,
                primarySnapshot,
                secondarySnapshot,
                includeContainer: true))
            return;
        bool sentTowardSecondary = secondaryIsRight
            ? Input.SendWinShiftRightTo(pig.Hwnd)
            : Input.SendWinShiftLeftTo(pig.Hwnd);
        if (!sentTowardSecondary)
        {
            ctx.BlockEnvironment("dual-monitor-mixed-dpi-transfer: exact foreground/lease proof refused Win+Shift+Arrow toward the secondary monitor");
            return;
        }
        if (!CaptureTransition(
                "dmt-after-primary-to-secondary",
                VisualCheckpointPhase.AFTER_ACTION_IMMEDIATE,
                "The primary-to-secondary Win+Shift+Arrow attempt is represented by a topology-bound guest frame before reconciliation.",
                primarySnapshot,
                primarySnapshot,
                secondarySnapshot,
                includeContainer: false))
            return;
        bool recontainedAfterGuestTransfer = Util.WaitUntil(
            () => IsDocked(pig.Hwnd, host)
                && NativeMethods.MonitorFromWindow(pig.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
                    == NativeMethods.MonitorFromWindow(host, NativeMethods.MONITOR_DEFAULTTONEAREST),
            3500,
            40);
        GuardedProc.Log($"  PHYSICAL_TOPOLOGY guest-transfer-toward-secondary sent=True recontained={recontainedAfterGuestTransfer} drift={TabDockLog.CountNewLines(ctx.LogOffset, "SHEPHERD[drift-reconcile]")}");
        ctx.Check(recontainedAfterGuestTransfer,
            "real Win+Shift+Arrow toward the secondary monitor did not leave the captured guest roaming away from its host");
        if (!recontainedAfterGuestTransfer)
            return;
        if (!AssertGuestPresentation(ctx, pig, container, host, "dual-monitor after guest transfer toward secondary"))
            return;

        if (!MoveContainerToMonitor(ctx, container, pig.Hwnd, secondary, "container to secondary"))
            return;
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3500, 40),
            "guest settled back over the secondary host after monitor placement");
        if (!CaptureTransition(
                "dmt-after-container-secondary",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The container and captured guest remain paired after moving to the secondary monitor and inherit its effective DPI.",
                secondarySnapshot,
                primarySnapshot,
                secondarySnapshot,
                includeContainer: true))
            return;
        if (!AssertGuestPresentation(ctx, pig, container, host, "dual-monitor secondary transfer"))
            return;
        ctx.Check(NativeMethods.GetDpiForWindow(container) == secondary.Dpi,
            $"container DPI follows the secondary monitor ({NativeMethods.GetDpiForWindow(container)} == {secondary.Dpi})");

        if (!CaptureTransition(
                "dmt-before-secondary-to-primary",
                VisualCheckpointPhase.BEFORE_ACTION,
                "The captured guest is ready for the reverse Win+Shift+Arrow transfer attempt from the secondary monitor to the primary monitor.",
                secondarySnapshot,
                secondarySnapshot,
                primarySnapshot,
                includeContainer: true))
            return;
        bool sentTowardPrimary = secondaryIsRight
            ? Input.SendWinShiftLeftTo(pig.Hwnd)
            : Input.SendWinShiftRightTo(pig.Hwnd);
        if (!sentTowardPrimary)
        {
            ctx.BlockEnvironment("dual-monitor-mixed-dpi-transfer: secondary monitor has no safe exact guest activation point for the reverse Win+Shift+Arrow attempt");
            return;
        }
        if (!CaptureTransition(
                "dmt-after-secondary-to-primary",
                VisualCheckpointPhase.AFTER_ACTION_IMMEDIATE,
                "The secondary-to-primary Win+Shift+Arrow attempt is represented by a topology-bound guest frame before reconciliation.",
                secondarySnapshot,
                secondarySnapshot,
                primarySnapshot,
                includeContainer: false))
            return;
        bool recontainedAfterReverse = Util.WaitUntil(
            () => IsDocked(pig.Hwnd, host)
                && NativeMethods.MonitorFromWindow(pig.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
                    == NativeMethods.MonitorFromWindow(host, NativeMethods.MONITOR_DEFAULTTONEAREST),
            3500,
            40);
        GuardedProc.Log($"  PHYSICAL_TOPOLOGY guest-transfer-toward-primary sent=True recontained={recontainedAfterReverse} drift={TabDockLog.CountNewLines(ctx.LogOffset, "SHEPHERD[drift-reconcile]")}");
        ctx.Check(recontainedAfterReverse,
            "real reverse Win+Shift+Arrow did not leave the captured guest roaming away from its host");
        if (!recontainedAfterReverse)
            return;
        if (!AssertGuestPresentation(ctx, pig, container, host, "dual-monitor secondary after reverse guest transfer"))
            return;

        if (!MoveContainerToMonitor(ctx, container, pig.Hwnd, primary, "container back to primary"))
            return;
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3500, 40),
            "guest settled back over the primary host after monitor placement");
        if (!CaptureTransition(
                "dmt-final-primary",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The reverse transition ends with the captured guest and container paired on the primary monitor.",
                primarySnapshot,
                secondarySnapshot,
                primarySnapshot,
                includeContainer: true))
            return;
        if (!AssertGuestPresentation(ctx, pig, container, host, "dual-monitor primary return"))
            return;
        ctx.Check(NativeMethods.GetDpiForWindow(container) == primary.Dpi,
            $"container DPI follows the primary monitor ({NativeMethods.GetDpiForWindow(container)} == {primary.Dpi})");
        ctx.Check(TabCount(container) == 1, "dual-monitor transfer retained the single captured tab");
        ctx.Check(PigLog.CountLines(pig.Pid, "CLIENT_PRESENT") > 0,
            "dual-monitor transfer retained live GuineaPig client rendering");
    }


    private static void TopmostGuestInteraction(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "TOP", "--topmost", "--text-box", "--pulse", "--color", "purple");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.ExpectedState = "A controlled GuineaPig launched in the topmost z-order band remains a real top-level window above its content pane while the container's group menu, rename editor, and direct guest input remain usable.";
        GuardedProc.Log($"  PHYSICAL_TOPMOST setup: GuineaPig requested its topmost state before capture; capture and all later input use stable identities.");
        Thread.Sleep(300);
        if (!AssertGuestPresentation(ctx, pig, container, host, "topmost baseline"))

            return;
        if (!TryGetVisualMonitorBinding(ctx, container, out VisualTopologyBinding? topmostBinding))
        {
            ctx.BlockEnvironment("topmost-guest-interaction: baseline visual evidence could not bind to the observed monitor");
            return;
        }
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-baseline",
                VisualCheckpointPhase.BASELINE,
                "A controlled topmost guest remains a real top-level window over its captured host pane.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;

        uint guestExStyle = unchecked((uint)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_EXSTYLE).ToInt64());
        uint containerExStyle = unchecked((uint)NativeMethods.GetWindowLongPtr(container, NativeMethods.GWL_EXSTYLE).ToInt64());
        ctx.Check((guestExStyle & NativeMethods.WS_EX_TOPMOST) != 0,
            $"captured GuineaPig retains WS_EX_TOPMOST (exStyle=0x{guestExStyle:X})");
        ctx.Check((containerExStyle & NativeMethods.WS_EX_TOPMOST) == 0,
            $"container remains in the normal z-order band (exStyle=0x{containerExStyle:X})");

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        int contentX = hostRect.left + hostRect.Width / 2;
        int contentY = hostRect.top + hostRect.Height / 2;
        if (!EnsureClickable(pig.Hwnd, contentX, contentY))
        {
            ctx.BlockEnvironment("topmost-guest-interaction: captured topmost guest content point was not safely foregroundable");
            return;
        }
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-before-input",
                VisualCheckpointPhase.BEFORE_ACTION,
                "The topmost guest and normal-band container are ready for identity-proven direct input.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;

        Input.ClickAt(contentX, contentY);
        Input.TypeText("TOP-INPUT");
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "TEXTBOX text='TOP-INPUT'", 3000),
            "topmost guest accepted direct typed input at the content center");
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-after-input",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The topmost guest accepted direct typed input without losing its captured presentation.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;


        AutomationElement root = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("topmost interaction: container UIA root unavailable.");
        AutomationElement? groupButton = Uia.FindDescendantByAutomationId(root, "GroupSelector", out int groupButtonCount);
        if (groupButton == null || groupButtonCount != 1)
            throw new InvalidOperationException($"topmost interaction: GroupSelector not found uniquely (count={groupButtonCount}).");
        (int groupX, int groupY) = Uia.Center(groupButton);
        if (!EnsureClickable(container, groupX, groupY))
        {
            ctx.BlockEnvironment("topmost-guest-interaction: container group button was covered by a foreign window");
            return;
        }
        Input.ClickAt(groupX, groupY);
        AutomationElement? newGroup = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "+ New group", 3000);
        ctx.Check(newGroup != null, "group menu opened above a topmost guest");
        if (newGroup == null)
            return;
        (int menuX, int menuY) = Uia.Center(newGroup);
        IntPtr menuRoot = RootAtPoint(menuX, menuY);
        NativeMethods.GetWindowThreadProcessId(menuRoot, out uint menuPid);
        ctx.Check(menuPid == ctx.TabDockPid,
            $"group menu item is topmost and owned by TabDock (root=0x{menuRoot.ToInt64():X} pid={menuPid})");
        // The menu is intentionally observed in a container screen-composition
        // scope: WPF Popup can expose its UIA subtree through the owning
        // window even while the actual popup HWND is transient.
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-group-menu",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The TabDock group menu is visibly usable above the controlled topmost guest.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;

        Input.SendKey(Input.VK_ESCAPE);
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000),
            "topmost guest remains docked after closing the group menu");
        guestExStyle = unchecked((uint)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_EXSTYLE).ToInt64());
        ctx.Check((guestExStyle & NativeMethods.WS_EX_TOPMOST) != 0,
            "topmost guest style remains set after group menu close");

        Input.ClickAt(groupX, groupY);
        AutomationElement? renameItem = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, "Rename group", 3000);
        if (renameItem == null)
        {
            ctx.Check(false, "group menu exposed Rename group while a topmost guest was captured");
            return;
        }
        (int renameX, int renameY) = Uia.Center(renameItem);
        IntPtr renameRoot = RootAtPoint(renameX, renameY);
        NativeMethods.GetWindowThreadProcessId(renameRoot, out uint renamePid);
        ctx.Check(renamePid == ctx.TabDockPid,
            $"rename menu item is owned by TabDock above the topmost guest (root=0x{renameRoot.ToInt64():X})");
        Input.ClickAt(renameX, renameY);
        bool renameOpened = Util.WaitUntil(() =>
        {
            AutomationElement? liveRoot = Uia.FromHwnd(container);
            return liveRoot != null
                && Uia.FindDescendantByName(liveRoot, ControlType.Edit, "Rename workspace", null, out _) != null;
        }, 3000);
        ctx.Check(renameOpened, "rename editor opened while the captured guest remained topmost");
        if (renameOpened)
        {
            Input.TypeText("TOP-Renamed");
            Input.SendKey(Input.VK_RETURN);
            ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TOP-Renamed", 3000),
                "topmost interaction renamed the group through the real menu/editor path");
        }
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-after-rename",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The topmost guest remains visible while the real group rename path commits.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;

        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3000),
            "topmost guest remains docked after the rename editor closes");

        GuestInfo unrelated = SpawnPig(ctx, "TOP-EXT", "--color", "white");
        if (!Input.ForceForegroundRoot(unrelated.Hwnd))
        {
            ctx.BlockEnvironment("topmost-guest-interaction: unrelated foreground steal could not be established safely");
            return;
        }
        ctx.Check(RootAtPointOf(NativeMethods.GetForegroundWindow()) == unrelated.Hwnd,
            "unrelated guest owns foreground without TabDock forcing itself topmost");

        if (!EnsureClickable(pig.Hwnd, contentX, contentY))
        {
            ctx.BlockEnvironment("topmost-guest-interaction: captured guest could not be reactivated after foreground steal");
            return;
        }
        Input.ClickAt(contentX, contentY);
        bool controlDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_CONTROL);
            controlDown = true;
            Input.SendKey(Input.VK_A);
        }
        finally
        {
            if (controlDown)
                Input.SendKeyUp(Input.VK_CONTROL);
        }
        Input.TypeText("TOP-INPUT-2");
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "TEXTBOX text='TOP-INPUT-2'", 3000),
            "topmost guest accepted input again after foreground was stolen");
        if (!AssertGuestPresentation(ctx, pig, container, host, "topmost after menu-rename-foreground"))
            return;
        if (!CapturePhysicalVisual(
                ctx,
                "topmost-final-input",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The topmost guest accepts direct input again after an unrelated window temporarily owns the foreground.",
                new[] { ctx.VisualGuestScope(pig), ctx.VisualContainerScope(container) },
                topmostBinding))
            return;

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0,
            "no EXCEPTION lines across topmost guest interactions");
    }

    private static void LocationChangeControlledLoad(Ctx ctx, Options opt)
    {
        GuestInfo captured = SpawnPig(ctx, "LCL", "--pulse", "--resize-probe", "--color", "orange",
            "--min-width", "200", "--min-height", "150");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, captured);
        ctx.ExpectedState = "A controlled native LOCATIONCHANGE load produces captured-member callbacks, membership probes, posts, coalesced repairs, and bounded SetWindowPos reconciliation while an unrelated window produces rejection-only traffic and TabDock remains responsive.";
        if (!AssertGuestPresentation(ctx, captured, container, host, "location-load baseline"))
            return;

        if (!Input.ForceForeground(container))
        {
            ctx.BlockEnvironment("locationchange-controlled-load: could not foreground the diagnostic container before the metrics snapshot");
            return;
        }
        long beforeMetricsOffset = TabDockLog.RecordLogLength();
        Input.SendHotkeyCtrlAltShiftD();
        if (!TabDockLog.WaitForLogLine(beforeMetricsOffset, "WINEVENT[metrics]", 20000)
            || !TryReadLastWinEventMetrics(beforeMetricsOffset, out WinEventMetricSample beforeMetrics))
        {
            ctx.BlockEnvironment("locationchange-controlled-load: diagnostic metrics snapshot was not observable after the real support-bundle hotkey");
            return;
        }

        GuestInfo unrelated = SpawnPig(ctx, "LCL-UNRELATED", "--resize-probe", "--color", "gray");
        if (unrelated.Identity is not WindowIdentity unrelatedIdentity
            || !NativeMethods.GetWindowRect(unrelated.Hwnd, out NativeMethods.RECT unrelatedRect))
        {
            ctx.BlockEnvironment("locationchange-controlled-load: unrelated test-owned window identity/rect was unavailable");
            return;
        }
        long unrelatedOffset = TabDockLog.RecordLogLength();
        for (int i = 0; i < 18; i++)
        {
            int x = 40 + (i % 6) * 120;
            int y = 40 + (i % 3) * 50;
            if (!VerifiedWindowOps.SetWindowPos(
                    unrelatedIdentity,
                    NativeMethods.HWND_TOP,
                    x,
                    y,
                    unrelatedRect.Width,
                    unrelatedRect.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
            {
                ctx.BlockEnvironment($"locationchange-controlled-load: unrelated SetWindowPos probe failed at iteration {i + 1}");
                return;
            }
        }
        Thread.Sleep(450);
        int unrelatedRepairs = TabDockLog.CountNewLines(unrelatedOffset, "SHEPHERD[");
        GuardedProc.Log($"  PHYSICAL_LOCATIONCHANGE unrelated load iterations=18 shepherdRepairs={unrelatedRepairs} (expected 0)");
        ctx.Check(unrelatedRepairs == 0,
            $"unrelated LOCATIONCHANGE load caused no Shepherd repair ({unrelatedRepairs} repair lines)");

        if (captured.Identity is not WindowIdentity capturedIdentity)
        {
            ctx.BlockEnvironment("locationchange-controlled-load: captured guest identity was unavailable before controlled load");
            return;
        }
        long capturedOffset = TabDockLog.RecordLogLength();
        NativeMethods.GetWindowRect(captured.Hwnd, out NativeMethods.RECT capturedRect);
        for (int i = 0; i < 12; i++)
        {
            NativeMethods.GetWindowRect(captured.Hwnd, out NativeMethods.RECT current);
            int dx = i % 2 == 0 ? 18 : -18;
            int dy = i % 3 == 0 ? 10 : -10;
            if (!VerifiedWindowOps.SetWindowPos(
                    capturedIdentity,
                    NativeMethods.HWND_TOP,
                    current.left + dx,
                    current.top + dy,
                    current.Width > 0 ? current.Width : capturedRect.Width,
                    current.Height > 0 ? current.Height : capturedRect.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
            {
                ctx.BlockEnvironment($"locationchange-controlled-load: captured SetWindowPos probe failed at iteration {i + 1}");
                return;
            }
            Thread.Sleep(55);
        }

        NativeMethods.RECT finalGuestRect = default;
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 3500)
        {
            if (IsDocked(captured.Hwnd, host)
                && NativeMethods.GetWindowRect(captured.Hwnd, out finalGuestRect))
            {
                break;
            }
            Thread.Sleep(40);
        }
        settle.Stop();
        int capturedRepairs = TabDockLog.CountNewLines(capturedOffset, "SHEPHERD[");
        int driftRepairs = TabDockLog.CountNewLines(capturedOffset, "SHEPHERD[drift-reconcile]");
        int positionRepairs = TabDockLog.CountNewLines(capturedOffset, "SHEPHERD[position]");
        GuardedProc.Log($"  PHYSICAL_LOCATIONCHANGE captured load iterations=12 repairs={capturedRepairs} driftReconcile={driftRepairs} position={positionRepairs} settleMs={settle.ElapsedMilliseconds} final={Util.FormatRect(finalGuestRect)}");
        ctx.Check(capturedRepairs > 0, "captured LOCATIONCHANGE load produced Shepherd repair evidence");
        ctx.Check(driftRepairs > 0, "captured LOCATIONCHANGE load produced drift-reconcile evidence");
        ctx.Check(positionRepairs > 0, "captured LOCATIONCHANGE load produced SetWindowPos position evidence");
        ctx.Check(Util.WaitUntil(() => IsDocked(captured.Hwnd, host), 3500),
            "captured guest returned to its assigned pane after the controlled location load");

        if (!Input.ForceForeground(container))
        {
            ctx.BlockEnvironment("locationchange-controlled-load: could not foreground the container for the post-load diagnostic snapshot");
            return;
        }
        long afterMetricsOffset = TabDockLog.RecordLogLength();
        Input.SendHotkeyCtrlAltShiftD();
        if (!TabDockLog.WaitForLogLine(afterMetricsOffset, "WINEVENT[metrics]", 20000)
            || !TryReadLastWinEventMetrics(afterMetricsOffset, out WinEventMetricSample afterMetrics))
        {
            ctx.BlockEnvironment("locationchange-controlled-load: post-load diagnostic metrics snapshot was not observable");
            return;
        }
        WinEventMetricSample delta = afterMetrics - beforeMetrics;
        GuardedProc.Log($"  PHYSICAL_LOCATIONCHANGE metrics delta callbacks={delta.Callbacks} rejected={delta.Rejected} membership={delta.Membership} dispatch={delta.Dispatch} posts={delta.Posts} stale={delta.Stale} lifecycle={delta.Lifecycle}");
        ctx.Check(delta.Callbacks > 0, "controlled load produced WinEvent callbacks");
        ctx.Check(delta.Rejected > 0, "unrelated load produced fail-closed WinEvent rejections");
        ctx.Check(delta.Membership > 0 && delta.Membership >= delta.Dispatch,
            $"captured/desktop callback membership probes are present (membership={delta.Membership}, dispatch={delta.Dispatch})");
        ctx.Check(delta.Dispatch > 0 && delta.Posts > 0 && delta.Lifecycle > 0,
            "captured load produced dispatch revalidation, UI posts, and lifecycle callbacks");
        ctx.Check(delta.Posts <= delta.Callbacks,
            $"posted captured events remain bounded by callbacks (posts={delta.Posts}, callbacks={delta.Callbacks})");
        ctx.Check(capturedRepairs <= delta.Callbacks,
            $"captured repairs remain bounded by the callback stream (repairs={capturedRepairs}, callbacks={delta.Callbacks})");

        var responsiveness = Stopwatch.StartNew();
        AutomationElement? liveRoot = Uia.FromHwnd(container);
        responsiveness.Stop();
        ctx.Check(liveRoot != null && responsiveness.ElapsedMilliseconds < 1500,
            $"container UIA remained responsive after LOCATIONCHANGE load ({responsiveness.ElapsedMilliseconds}ms < 1500ms)");
        ctx.Check(captured.Proc != null && !captured.Proc.HasExited, "captured GuineaPig remained alive after controlled load");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0,
            "no EXCEPTION lines across controlled LOCATIONCHANGE load");
    }

    private static bool TryReadLastWinEventMetrics(long offset, out WinEventMetricSample metrics)
    {
        metrics = default;
        string? last = TabDockLog.ReadNewLines(offset)
            .LastOrDefault(line => line.Contains("WINEVENT[metrics]", StringComparison.OrdinalIgnoreCase));
        if (last == null)
            return false;

        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in last.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = token.IndexOf('=');
            if (equals <= 0
                || !long.TryParse(token[(equals + 1)..], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out long value))
            {
                continue;
            }
            values[token[..equals]] = value;
        }
        if (!TryMetric(values, "callbacks", out long callbacks)
            || !TryMetric(values, "rejected", out long rejected)
            || !TryMetric(values, "membership", out long membership)
            || !TryMetric(values, "dispatch", out long dispatch)
            || !TryMetric(values, "posts", out long posts)
            || !TryMetric(values, "stale", out long stale)
            || !TryMetric(values, "lifecycle", out long lifecycle))
        {
            return false;
        }
        metrics = new WinEventMetricSample(callbacks, rejected, membership, dispatch, posts, stale, lifecycle);
        return true;
    }

    private static bool TryMetric(IReadOnlyDictionary<string, long> values, string name, out long value)
        => values.TryGetValue(name, out value);

    private readonly record struct WinEventMetricSample(
        long Callbacks,
        long Rejected,
        long Membership,
        long Dispatch,
        long Posts,
        long Stale,
        long Lifecycle)
    {
        public static WinEventMetricSample operator -(WinEventMetricSample left, WinEventMetricSample right)
            => new(
                left.Callbacks - right.Callbacks,
                left.Rejected - right.Rejected,
                left.Membership - right.Membership,
                left.Dispatch - right.Dispatch,
                left.Posts - right.Posts,
                left.Stale - right.Stale,
                left.Lifecycle - right.Lifecycle);
    }

    private static void TitleCenteringPhysicalMeasurement(Ctx ctx, Options opt)
    {
        List<DpiMonitor> monitors = EnumerateDpiMonitors();
        DpiMonitor? primary = monitors.FirstOrDefault(m => IsPrimaryMonitor(m));
        DpiMonitor? secondary = monitors.FirstOrDefault(m => !IsPrimaryMonitor(m));
        if (primary == null || secondary == null)
        {
            ctx.SkipCapability("title-centering-physical-measurement: two physical monitors are required");
            return;
        }
        if (primary.Dpi == 0 || secondary.Dpi == 0)
        {
            ctx.BlockEnvironment("title-centering-physical-measurement: a monitor DPI probe failed");
            return;
        }
        if (primary.Dpi == secondary.Dpi)
        {
            ctx.SkipCapability($"title-centering-physical-measurement: monitors are not mixed-DPI ({primary.Dpi} and {secondary.Dpi})");
            return;
        }
        if (!TryGetPhysicalMonitor(ctx, primary, out PhysicalMonitorSnapshot? primarySnapshot)
            || !TryGetPhysicalMonitor(ctx, secondary, out PhysicalMonitorSnapshot? secondarySnapshot)
            || primarySnapshot == null
            || secondarySnapshot == null)
        {
            ctx.BlockEnvironment("title-centering-physical-measurement: observed monitors could not be bound to the preflight topology snapshot");
            return;
        }

        GuestInfo pig = SpawnPig(ctx, "TCM", "--pulse", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.ExpectedState = "The visible workspace title remains centered on the physical container midpoint for short, medium, and long names at narrow, default, and wide container widths on both mixed-DPI monitors.";
        var titleCases = new[]
        {
            (Key: "short", Name: "TDVAL-C"),
            (Key: "medium", Name: "TDVAL-Center-Medium"),
            (Key: "long", Name: "TDVAL-Center-" + new string('W', 80)),
        };
        string[] widthKinds = { "narrow", "default", "wide" };
        string currentName = "Group";

        bool ResizeForTitle(DpiMonitor target, string widthKind, int defaultWidth)
        {
            if (!NativeMethods.GetWindowRect(container, out NativeMethods.RECT current))
            {
                ctx.Check(false, $"title centering {target.Handle:X} {widthKind}: container rect was unavailable before resize");
                return false;
            }
            int availableWidth = target.Work.Width - 80;
            if (availableWidth < 320)
            {
                ctx.SkipCapability($"title-centering-physical-measurement: monitor 0x{target.Handle.ToInt64():X} work area is too narrow for the bounded width matrix");
                return false;
            }
            int safeDefaultWidth = Math.Clamp(defaultWidth, 320, availableWidth);
            int width = widthKind switch
            {
                "narrow" => Math.Max(320, Math.Min(safeDefaultWidth, availableWidth / 2)),
                "wide" => Math.Min(availableWidth, Math.Max(safeDefaultWidth, availableWidth * 3 / 4)),
                _ => safeDefaultWidth,
            };
            int x = target.Work.left + Math.Max(40, (target.Work.Width - width) / 2);
            x = Math.Clamp(x, target.Work.left + 20, target.Work.right - width - 20);
            int y = Math.Clamp(
                current.top,
                target.Work.top + 20,
                Math.Max(target.Work.top + 20, target.Work.bottom - current.Height - 20));
            if (!VerifiedWindowOps.SetWindowPos(
                    GetRememberedContainerIdentity(ctx, container),
                    NativeMethods.HWND_TOP,
                    x,
                    y,
                    width,
                    current.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
            {
                ctx.Check(false, $"title centering {target.Handle:X} {widthKind}: verified resize failed");
                return false;
            }
            bool resized = Util.WaitUntil(
                () => NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST) == target.Handle
                    && NativeMethods.GetWindowRect(container, out NativeMethods.RECT resizedRect)
                    && resizedRect.Width == width,
                3500,
                40);
            ctx.Check(resized, $"title centering {target.Handle:X} {widthKind}: container reached bounded width {width}px");
            return resized;
        }

        if (!NativeMethods.GetWindowRect(container, out NativeMethods.RECT initialRect))
        {
            ctx.BlockEnvironment("title-centering-physical-measurement: initial container rect unavailable");
            return;
        }
        int initialWidth = initialRect.Width;

        (DpiMonitor Monitor, PhysicalMonitorSnapshot Snapshot)[] targetCases =
        {
            (primary, primarySnapshot),
            (secondary, secondarySnapshot),
        };
        foreach ((DpiMonitor target, PhysicalMonitorSnapshot targetSnapshot) in targetCases)
        {
            if (NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST) != target.Handle
                && !MoveContainerToMonitor(ctx, container, pig.Hwnd, target, $"title measurement to {targetSnapshot.MonitorId}"))
            {
                return;
            }
            bool guestSettledAfterMonitorPlacement = Util.WaitUntil(
                () => IsDocked(pig.Hwnd, host),
                3500,
                40);
            ctx.Check(
                guestSettledAfterMonitorPlacement,
                $"title centering {targetSnapshot.MonitorId} baseline: captured guest settled after monitor placement");
            if (!guestSettledAfterMonitorPlacement)
                return;
            if (!AssertGuestPresentation(ctx, pig, container, host, $"title centering {targetSnapshot.MonitorId} baseline"))
                return;
            int defaultWidth = initialWidth;
            foreach ((string titleKey, string titleName) in titleCases)
            {
                if (!string.Equals(currentName, titleName, StringComparison.Ordinal)
                    && !RenameGroupForMeasurement(ctx, container, currentName, titleName))
                {
                    ctx.BlockEnvironment($"title-centering-physical-measurement: UIA rename to {titleKey} was not safely actionable");
                    return;
                }
                currentName = titleName;
                foreach (string widthKind in widthKinds)
                {
                    if (!ResizeForTitle(target, widthKind, defaultWidth))
                        return;
                    ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 3500, 40),
                        $"title centering {targetSnapshot.MonitorId} {titleKey} {widthKind}: captured guest remains docked");
                    VisualTopologyBinding? binding = ctx.VisualTopologyFor(
                        targetSnapshot.MonitorId,
                        (int)targetSnapshot.EffectiveDpi);
                    if (!CapturePhysicalVisual(
                            ctx,
                            $"title-{targetSnapshot.MonitorId}-{titleKey}-{widthKind}",
                            VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                            $"The {titleKey} workspace title is centered at the physical midpoint at {widthKind} width on {targetSnapshot.MonitorId}.",
                            new[] { ctx.VisualContainerScope(container), ctx.VisualGuestScope(pig) },
                            binding))
                        return;
                    if (!MeasureTitleCenter(
                            ctx,
                            container,
                            titleName,
                            target,
                            $"{targetSnapshot.MonitorId} {titleKey} {widthKind}"))
                        return;
                }
            }
        }

        if (!string.Equals(currentName, "Group", StringComparison.Ordinal)
            && !RenameGroupForMeasurement(ctx, container, currentName, "Group"))
        {
            ctx.BlockEnvironment("title-centering-physical-measurement: final UIA rename back to Group was not safely actionable");
            return;
        }
        if (!MoveContainerToMonitor(ctx, container, pig.Hwnd, primary, "title measurement final primary"))
            return;
        bool finalGuestSettled = Util.WaitUntil(
            () => IsDocked(pig.Hwnd, host),
            3500,
            40);
        ctx.Check(finalGuestSettled, "title centering final primary: captured guest settled after monitor placement");
        if (!finalGuestSettled
            || !AssertGuestPresentation(ctx, pig, container, host, "title centering final primary"))
            return;
        ctx.Check(TabCount(container) == 1, "title-centering measurement retained the single captured tab");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0,
            "no EXCEPTION lines across title-centering measurements");
    }

    private static bool RenameGroupForMeasurement(Ctx ctx, IntPtr container, string oldName, string newName)
    {
        AutomationElement? root = Uia.FromHwnd(container);
        int titleCount = 0;
        AutomationElement? title = root == null
            ? null
            : Uia.FindDescendantByName(root, ControlType.Text, oldName, null, out titleCount);
        if (title == null || titleCount != 1)
        {
            ctx.Check(false, $"title rename '{oldName}' found one UIA Text title (count={titleCount})");
            return false;
        }
        (int x, int y) = Uia.Center(title);
        if (!EnsureClickable(container, x, y))
            return false;
        Input.DoubleClickAt(x, y);
        bool editorOpened = Util.WaitUntil(() =>
        {
            AutomationElement? liveRoot = Uia.FromHwnd(container);
            return liveRoot != null
                && Uia.FindDescendantByName(liveRoot, ControlType.Edit, "Rename workspace", null, out _) != null;
        }, 3000);
        if (!editorOpened)
        {
            ctx.Check(false, $"title rename '{oldName}' opened the UIA Rename workspace editor");
            return false;
        }
        bool controlDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_CONTROL);
            controlDown = true;
            Input.SendKey(Input.VK_A);
        }
        finally
        {
            if (controlDown)
                Input.SendKeyUp(Input.VK_CONTROL);
        }
        Input.TypeText(newName);
        Input.SendKey(Input.VK_RETURN);
        bool renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == newName, 3000);
        ctx.Check(renamed, $"title rename committed '{newName}'");
        return renamed;
    }

    private static bool MeasureTitleCenter(Ctx ctx, IntPtr container, string name, DpiMonitor monitor, string phase)
    {
        AutomationElement? root = Uia.FromHwnd(container);
        int titleCount = 0;
        AutomationElement? title = root == null
            ? null
            : Uia.FindDescendantByName(root, ControlType.Text, name, null, out titleCount);
        if (title == null || titleCount != 1)
        {
            ctx.Check(false, $"{phase}: UIA title '{name}' found uniquely (count={titleCount})");
            return false;
        }
        Rect titleRect = Uia.GetElementRect(title);
        if (!NativeMethods.GetWindowRect(container, out NativeMethods.RECT containerRect))
        {
            ctx.Check(false, $"{phase}: container rect available for title-center measurement");
            return false;
        }
        double titleCenter = titleRect.X + titleRect.Width / 2.0;
        double windowCenter = containerRect.left + containerRect.Width / 2.0;
        double error = Math.Abs(titleCenter - windowCenter);
        GuardedProc.Log($"  PHYSICAL_TITLE_CENTER phase={phase} monitor=0x{monitor.Handle.ToInt64():X} dpi={monitor.Dpi} nameLength={name.Length} titleRect={titleRect} container={Util.FormatRect(containerRect)} centerErrorPx={error:F2}");
        AppendObservedState(ctx, $"title-center {phase}: dpi={monitor.Dpi} titleRect={titleRect} container={Util.FormatRect(containerRect)} centerErrorPx={error:F2}");
        ctx.Check(titleRect.Width > 0 && titleRect.Height > 0,
            $"{phase}: title UIA rect is non-empty ({titleRect})");
        ctx.Check(error <= 3.0,
            $"{phase}: title midpoint is within 3 physical pixels of the container midpoint (error={error:F2}px)");
        ctx.Check(NativeMethods.GetDpiForWindow(container) == monitor.Dpi,
            $"{phase}: container reports the target monitor DPI ({NativeMethods.GetDpiForWindow(container)} == {monitor.Dpi})");
        return ctx.Pass;
    }
    private static bool IsPrimaryMonitor(DpiMonitor monitor)
        => (monitor.Bounds.left == 0 && monitor.Bounds.top == 0)
            || monitor.Work.left == 0 && monitor.Work.top == 0
                && monitor.Bounds.left == 0;
    private static bool CapturePhysicalVisual(
        Ctx ctx,
        string id,
        VisualCheckpointPhase phase,
        string expectation,
        IReadOnlyList<VisualCaptureScope> scopes,
        VisualTopologyBinding? binding = null)
    {
        if (ctx.Visual is null || ctx.VisualPolicy.Level is VisualEvidenceLevel.NONE or VisualEvidenceLevel.FAILURE_ONLY)
        {
            ctx.BlockCapability($"{ctx.Name}: physical checkpoint '{id}' requires checkpoint visual evidence.");
            return false;
        }
        binding ??= ctx.VisualTopologyFor();
        if (binding is null)
        {
            ctx.BlockEnvironment($"{ctx.Name}: physical checkpoint '{id}' could not bind to the observed topology.");
            return false;
        }

        VisualCheckpointResult result = ctx.VisualCheckpoint(new VisualCheckpointRequest(
            id,
            phase,
            expectation,
            scopes,
            VisualCaptureRequiredness.REQUIRED,
            IncludeInReview: true,
            TopologyBinding: binding));
        return result.Captured;
    }

    private static bool TryGetPhysicalMonitor(
        Ctx ctx,
        DpiMonitor observed,
        out PhysicalMonitorSnapshot? snapshot)
    {
        snapshot = null;
        if (ctx.Capabilities?.Topology is not { } topology)
            return false;

        snapshot = topology.Monitors.FirstOrDefault(m =>
            m.Bounds.Left == observed.Bounds.left
            && m.Bounds.Top == observed.Bounds.top
            && m.Bounds.Right == observed.Bounds.right
            && m.Bounds.Bottom == observed.Bounds.bottom
            && m.WorkArea.Left == observed.Work.left
            && m.WorkArea.Top == observed.Work.top
            && m.WorkArea.Right == observed.Work.right
            && m.WorkArea.Bottom == observed.Work.bottom
            && m.EffectiveDpi == observed.Dpi);
        return snapshot != null;
    }

    private static bool TryGetVisualMonitorBinding(
        Ctx ctx,
        IntPtr hwnd,
        out VisualTopologyBinding? binding)
    {
        binding = null;
        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        DpiMonitor? observed = EnumerateDpiMonitors().FirstOrDefault(m => m.Handle == handle);
        if (observed == null
            || !TryGetPhysicalMonitor(ctx, observed, out PhysicalMonitorSnapshot? snapshot)
            || snapshot == null)
        {
            return false;
        }

        binding = ctx.VisualTopologyFor(snapshot.MonitorId, (int)snapshot.EffectiveDpi);
        return binding != null;
    }

    private static bool MoveContainerToMonitor(Ctx ctx, IntPtr container, IntPtr guest, DpiMonitor target, string phase)
    {
        if (!NativeMethods.GetWindowRect(container, out NativeMethods.RECT current))
        {
            ctx.Check(false, $"{phase}: container rect was unavailable before test-owned monitor placement");
            return false;
        }
        int x = target.Work.left + Math.Max(40, (target.Work.Width - current.Width) / 2);

        int y = target.Work.top + Math.Max(40, (target.Work.Height - current.Height) / 2);
        GuardedProc.Log($"  PHYSICAL_TOPOLOGY {phase}: test-owned SetWindowPos target=({x},{y}) size={current.Width}x{current.Height}; monitor={target.Handle.ToInt64():X}");
        if (!VerifiedWindowOps.SetWindowPos(
                GetRememberedContainerIdentity(ctx, container),
                NativeMethods.HWND_TOP,
                x,
                y,
                current.Width,
                current.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            ctx.Check(false, $"{phase}: verified container SetWindowPos failed");
            return false;
        }
        bool moved = Util.WaitUntil(
            () => NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST) == target.Handle
                && NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST)
                    == NativeMethods.MonitorFromWindow(guest, NativeMethods.MONITOR_DEFAULTTONEAREST),
            3500,
            40);
        ctx.Check(moved, $"{phase}: container reached monitor 0x{target.Handle.ToInt64():X} with its captured guest");
        return moved;
    }


    private static void RunGuestCaptionCycles(
        Ctx ctx,
        GuestInfo guest,
        IntPtr container,
        IntPtr host,
        string guestLabel,
        int cycles,
        bool requirePigMessageEvidence)
    {
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            string phase = $"{guestLabel} caption cycle {cycle}/{cycles}";
            string visualPrefix = guest.IsPig ? "guineapig" : "notepad";
            bool CaptureVisual(string suffix, VisualCheckpointPhase checkpointPhase, string expectation)
            {
                if (!TryGetVisualMonitorBinding(ctx, guest.Hwnd, out VisualTopologyBinding? monitorBinding))
                {
                    ctx.BlockEnvironment($"{phase}: visual checkpoint '{suffix}' could not resolve the guest's observed physical monitor.");
                    return false;
                }
                return CapturePhysicalVisual(
                    ctx,
                    $"{visualPrefix}-caption-cycle-{cycle}-{suffix}",
                    checkpointPhase,
                    expectation,
                    new[] { ctx.VisualGuestScope(guest), ctx.VisualContainerScope(container) },
                    monitorBinding);
            }

            if (!CaptureVisual(
                    "baseline",
                    VisualCheckpointPhase.BASELINE,
                    $"{guestLabel} baseline remains the same captured guest in its assigned pane."))
                return;

            if (!AssertGuestPresentation(ctx, guest, container, host, phase + " baseline"))
                return;

            if (!TryGetGuestCaptionButtonPoint(guest, new[] { "Maximize" }, out int maxX, out int maxY, out string pointEvidence))
            {
                ctx.BlockCapability($"{phase}: the guest exposed no identity-proven native maximize caption point ({pointEvidence})");
                return;
            }
            long off = TabDockLog.RecordLogLength();
            int sysMaxBefore = guest.IsPig ? PigLog.CountLines(guest.Pid, "WM_SYSCOMMAND wParam=0xF030") : 0;
            GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] maximize point=({maxX},{maxY}) {pointEvidence}");
            if (!EnsureClickable(guest.Hwnd, maxX, maxY))
            {
                ctx.BlockEnvironment($"{phase}: native maximize point became covered or failed exact foreground/lease proof");
                return;
            }
            if (!CaptureVisual(
                    "before-maximize",
                    VisualCheckpointPhase.BEFORE_ACTION,
                    $"{guestLabel} maximize caption is identity-proven and ready for the real click."))
                return;

            Input.ClickAt(maxX, maxY);

            bool nativeMax = WaitForNativeMaximizeEvidence(guest, sysMaxBefore, 2500, out string maxEvidence);
            GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] native maximize evidence={maxEvidence}");
            if (!nativeMax)
            {
                ctx.BlockEnvironment($"{phase}: physically clicked, identity-proven caption point produced no observable native maximize state/message");
                return;
            }
            if (requirePigMessageEvidence)
                ctx.Check(PigLog.CountLines(guest.Pid, "WM_SYSCOMMAND wParam=0xF030") > sysMaxBefore,
                    $"{phase}: GuineaPig received the native SC_MAXIMIZE message after the real caption click");
            ctx.Check(nativeMax, $"{phase}: caption click produced native maximize evidence ({maxEvidence})");
            if (!CaptureVisual(
                    "after-maximize",
                    VisualCheckpointPhase.AFTER_ACTION_IMMEDIATE,
                    $"{guestLabel} real caption maximize produced observable native presentation before reconciliation."))
                return;


            bool contained = Util.WaitUntil(
                () => !NativeMethods.IsZoomed(guest.Hwnd) && IsDocked(guest.Hwnd, host),
                3500,
                40);
            bool sentRestore = false;
            if (!contained && NativeMethods.IsZoomed(guest.Hwnd))
            {
                if (!TryGetGuestCaptionButtonPoint(guest, new[] { "Restore", "Maximize" }, out int restoreX, out int restoreY, out string restoreEvidence))
                {
                    ctx.Check(false, $"{phase}: native restore caption point remained unavailable ({restoreEvidence})");
                    return;
                }
                GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] explicit restore point=({restoreX},{restoreY}) {restoreEvidence}");
                if (!EnsureClickable(guest.Hwnd, restoreX, restoreY))
                {
                    ctx.BlockEnvironment($"{phase}: native restore point became covered or failed exact foreground/lease proof");
                    return;
                }
                Input.ClickAt(restoreX, restoreY);
                sentRestore = true;
                contained = Util.WaitUntil(
                    () => !NativeMethods.IsZoomed(guest.Hwnd) && IsDocked(guest.Hwnd, host),
                    2500,
                    40);
            }

            GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] contained={contained} explicitRestore={sentRestore} drift={TabDockLog.CountNewLines(off, "SHEPHERD[drift-reconcile]")}");
            if (!contained)
            {
                ctx.Check(false, $"{phase}: guest returned to its assigned pane after native maximize (explicit restore={sentRestore})");
                return;
            }
            ctx.Check(!NativeMethods.IsZoomed(guest.Hwnd), $"{phase}: guest is not zoomed after native restore/reconciliation");
            if (!AssertGuestPresentation(ctx, guest, container, host, phase + " restored"))
                return;
            if (!CaptureVisual(
                    "after-restore",
                    VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                    $"{guestLabel} restored to the same captured pane after native presentation reconciliation."))
                return;

            ctx.Check(TabDockLog.CountNewLines(off, "SHEPHERD[drift-reconcile]") > 0
                || TabDockLog.CountNewLines(off, "SHEPHERD[position]") > 0,
                $"{phase}: TabDock recorded native-presentation reconciliation without a corrective tab click");
            ctx.Check(TabCount(container) == 1, $"{phase}: captured tab count remained one");
            if (guest.IsPig)
                ctx.Check(PigLog.CountLines(guest.Pid, "CLIENT_PRESENT") > 0,
                    $"{phase}: GuineaPig client-render evidence remained live");
        }
    }

    /// <summary>
    /// Reads the standard non-client caption button through UIA when available,
    /// then uses only DPI-derived native-frame candidates. Every candidate is
    /// accepted only when WindowFromPoint/GA_ROOT resolves to the exact pinned
    /// guest identity; no blind caption coordinate is ever clicked.
    /// </summary>
    private static bool TryGetGuestCaptionButtonPoint(
        GuestInfo guest,
        IReadOnlyList<string> names,
        out int x,
        out int y,
        out string evidence)
    {
        x = 0;
        y = 0;
        evidence = string.Empty;
        if (!TryGetLiveGuestIdentity(guest, out WindowIdentity identity, out string identityReason)
            || !NativeMethods.IsWindowVisible(guest.Hwnd)
            || !NativeMethods.IsWindowEnabled(guest.Hwnd)
            || !NativeMethods.GetWindowRect(guest.Hwnd, out NativeMethods.RECT rect))
        {
            evidence = string.IsNullOrEmpty(identityReason) ? "guest-window-not-visible-or-rect-unavailable" : identityReason;
            return false;
        }

        uint style = unchecked((uint)NativeMethods.GetWindowLongPtr(guest.Hwnd, NativeMethods.GWL_STYLE).ToInt64());
        if ((style & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION
            || (style & NativeMethods.WS_MAXIMIZEBOX) == 0)
        {
            evidence = $"style=0x{style:X} lacks WS_CAPTION/WS_MAXIMIZEBOX";
            return false;
        }

        AutomationElement? root = Uia.FromHwnd(guest.Hwnd);
        if (root != null)
        {
            foreach (string name in names)
            {
                AutomationElement? button = Uia.FindDescendantByName(root, ControlType.Button, name, null, out int count);
                if (button == null || count != 1)
                    continue;
                Rect buttonRect = Uia.GetElementRect(button);
                int candidateX = (int)Math.Round(buttonRect.X + buttonRect.Width / 2.0);
                int candidateY = (int)Math.Round(buttonRect.Y + buttonRect.Height / 2.0);
                if (IsNativeCaptionCandidate(identity, rect, candidateX, candidateY)
                    && RootAtPoint(candidateX, candidateY) == guest.Hwnd)
                {
                    x = candidateX;
                    y = candidateY;
                    evidence = $"UIA name={name} count=1 rect={buttonRect.X:F0},{buttonRect.Y:F0},{buttonRect.Width:F0}x{buttonRect.Height:F0}";
                    return true;
                }
            }
        }

        uint dpi = NativeMethods.GetDpiForWindow(guest.Hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        int systemButton = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSIZE);
        int systemCaption = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSIZE);
        var widths = new[]
        {
            systemButton,
            (int)Math.Round(46 * scale),
            (int)Math.Round(30 * scale),
        }.Where(value => value >= 10).Distinct().ToArray();
        var heights = new[]
        {
            systemCaption,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCAPTION),
            (int)Math.Round(32 * scale),
            (int)Math.Round(46 * scale),
        }.Where(value => value >= 10).Distinct().ToArray();
        foreach (int width in widths)
        {
            foreach (int height in heights)
            {
                int candidateX = rect.right - (int)Math.Round(width * 1.5);
                int candidateY = rect.top + height / 2;
                if (!IsNativeCaptionCandidate(identity, rect, candidateX, candidateY)
                    || RootAtPoint(candidateX, candidateY) != guest.Hwnd)
                    continue;
                x = candidateX;
                y = candidateY;
                evidence = $"native-frame metrics button={width}px caption={height}px dpi={dpi} style=0x{style:X}";
                return true;
            }
        }

        evidence = $"no exact-root caption candidate; rect={Util.FormatRect(rect)} dpi={dpi} style=0x{style:X}";
        return false;
    }

    private static bool IsNativeCaptionCandidate(WindowIdentity identity, NativeMethods.RECT rect, int x, int y)
    {
        if (x <= rect.left || x >= rect.right || y <= rect.top || y >= rect.bottom)
            return false;
        if (!Discover.TryCaptureIdentity(identity.Hwnd, out WindowIdentity current)
            || !SameStableWindowIdentity(identity, current)
            || !TestRunProvenance.TryValidateWindow(current, out _))
            return false;
        return y - rect.top <= 70 && x >= rect.right - Math.Max(90, rect.Width / 4);
    }

    private static bool WaitForNativeMaximizeEvidence(GuestInfo guest, int priorPigSysMaximize, int timeoutMs, out string evidence)
    {
        var sw = Stopwatch.StartNew();
        bool sawZoomed = false;
        uint sawShowCmd = 0;
        int sawMessageCount = priorPigSysMaximize;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (NativeMethods.IsZoomed(guest.Hwnd))
                sawZoomed = true;
            if (NativeMethods.IsWindow(guest.Hwnd))
            {
                var placement = new NativeMethods.WINDOWPLACEMENT
                {
                    length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
                };
                if (NativeMethods.GetWindowPlacement(guest.Hwnd, ref placement))
                    sawShowCmd = Math.Max(sawShowCmd, placement.showCmd);
            }
            if (guest.IsPig)
                sawMessageCount = Math.Max(sawMessageCount, PigLog.CountLines(guest.Pid, "WM_SYSCOMMAND wParam=0xF030"));
            if (sawZoomed || sawShowCmd == NativeMethods.SW_SHOWMAXIMIZED || sawMessageCount > priorPigSysMaximize)
            {
                evidence = $"zoomed={sawZoomed} showCmd={sawShowCmd} pigSC_MAXIMIZE={sawMessageCount - priorPigSysMaximize} elapsedMs={sw.ElapsedMilliseconds}";
                return true;
            }
            Thread.Sleep(20);
        }
        evidence = $"zoomed={sawZoomed} showCmd={sawShowCmd} pigSC_MAXIMIZE={sawMessageCount - priorPigSysMaximize} elapsedMs={sw.ElapsedMilliseconds}";
        return false;
    }

    /// <summary>Logs all native presentation fields required to classify a physical gesture.</summary>
    private static bool AssertGuestPresentation(
        Ctx ctx,
        GuestInfo guest,
        IntPtr container,
        IntPtr host,
        string phase,
        bool allowBrowserOwnedSurface = false)
    {
        bool identity = TryGetLiveGuestIdentity(guest, out WindowIdentity current, out string identityReason);
        bool window = NativeMethods.IsWindow(guest.Hwnd) && NativeMethods.IsWindowVisible(guest.Hwnd);
        bool parentless = window && NativeMethods.GetParent(guest.Hwnd) == IntPtr.Zero;
        bool docked = window && IsDocked(guest.Hwnd, host);
        bool sameTab = TabCount(container) == 1;
        IntPtr guestMonitor = window ? NativeMethods.MonitorFromWindow(guest.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
        IntPtr hostMonitor = NativeMethods.MonitorFromWindow(host, NativeMethods.MONITOR_DEFAULTTONEAREST);
        bool sameMonitor = guestMonitor != IntPtr.Zero && guestMonitor == hostMonitor;
        NativeMethods.RECT guestRect = default;
        bool haveRects = window && NativeMethods.GetWindowRect(guest.Hwnd, out guestRect)
            && NativeMethods.GetWindowRect(container, out _)
            && NativeMethods.GetClientRect(host, out _);
        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int centerX = hostClient.left + hostClient.Width / 2;
        int centerY = hostClient.top + hostClient.Height / 2;
        IntPtr pointRoot = window ? RootAtPoint(centerX, centerY) : IntPtr.Zero;
        IntPtr foregroundRoot = RootAtPointOf(NativeMethods.GetForegroundWindow());
        IntPtr previousZ = window ? NativeMethods.GetWindow(guest.Hwnd, NativeMethods.GW_HWNDPREV) : IntPtr.Zero;
        uint dpi = window ? NativeMethods.GetDpiForWindow(guest.Hwnd) : 0;
        uint style = window ? unchecked((uint)NativeMethods.GetWindowLongPtr(guest.Hwnd, NativeMethods.GWL_STYLE).ToInt64()) : 0;
        uint exStyle = window ? unchecked((uint)NativeMethods.GetWindowLongPtr(guest.Hwnd, NativeMethods.GWL_EXSTYLE).ToInt64()) : 0;
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
        };
        bool placementRead = window && NativeMethods.GetWindowPlacement(guest.Hwnd, ref placement);
        string state = $"identity={identity} identityReason={identityReason} window={window} parentless={parentless} docked={docked} tabs={sameTab} "
            + $"guestRect={(haveRects ? Util.FormatRect(guestRect) : "<none>")} hostClient={Util.FormatRect(hostClient)} "
            + $"guestMonitor=0x{guestMonitor.ToInt64():X} hostMonitor=0x{hostMonitor.ToInt64():X} sameMonitor={sameMonitor} dpi={dpi} "
            + $"style=0x{style:X} exStyle=0x{exStyle:X} zoomed={(window && NativeMethods.IsZoomed(guest.Hwnd))} "
            + $"showCmd={(placementRead ? placement.showCmd : 0)} foregroundRoot=0x{foregroundRoot.ToInt64():X} "
            + $"pointRoot=0x{pointRoot.ToInt64():X} previousZ=0x{previousZ.ToInt64():X}";
        GuardedProc.Log($"  PHYSICAL_PRESENTATION[{phase}] {state}");
        AppendObservedState(ctx, phase + ": " + state);

        WindowIdentity covered = default;
        bool hasCoveredIdentity = pointRoot != IntPtr.Zero
            && pointRoot != guest.Hwnd
            && Discover.TryCaptureIdentity(pointRoot, out covered);
        bool coveredOwned = hasCoveredIdentity
            && TestRunProvenance.TryValidateWindow(covered, out _);
        bool browserOwnedSurface = allowBrowserOwnedSurface
            && identity
            && coveredOwned
            && covered.ProcessId == current.ProcessId
            && covered.ProcessStartTimeUtcTicks == current.ProcessStartTimeUtcTicks
            && string.Equals(covered.ExePath, current.ExePath, StringComparison.OrdinalIgnoreCase)
            && TestRunProvenance.WindowRole(pointRoot).EndsWith(".DynamicSurface", StringComparison.Ordinal);
        bool pointOwned = pointRoot == guest.Hwnd || browserOwnedSurface;
        bool foreignPoint = hasCoveredIdentity && !coveredOwned;
        if (foreignPoint)
        {
            ctx.BlockEnvironment($"{phase}: content center is covered by a foreign root 0x{pointRoot.ToInt64():X}; no product conclusion");
            return false;
        }
        ctx.Check(identity, $"{phase}: guest strong identity remains stable ({identityReason})");
        ctx.Check(window, $"{phase}: guest remains live and visible");
        ctx.Check(parentless, $"{phase}: guest remains an independent top-level window (no reparent)");
        ctx.Check(docked && haveRects, $"{phase}: guest remains assigned to the host content rect");
        ctx.Check(sameTab, $"{phase}: the same single captured tab remains in the container");
        ctx.Check(sameMonitor, $"{phase}: guest and assigned host remain on monitor 0x{guestMonitor.ToInt64():X}");
        ctx.Check(pointOwned, browserOwnedSurface
            ? $"{phase}: WindowFromPoint at host center resolves to the captured guest's registered browser surface"
            : $"{phase}: WindowFromPoint at host center resolves to the captured guest");
        return ctx.Pass;
    }

    private static bool TryGetLiveGuestIdentity(GuestInfo guest, out WindowIdentity current, out string reason)
    {
        current = default;
        reason = string.Empty;
        if (guest.Identity is not WindowIdentity expected)
        {
            reason = "guest-identity-not-pinned";
            return false;
        }
        if (!Discover.TryCaptureIdentity(guest.Hwnd, out current))
        {
            reason = "guest-identity-read-failed";
            return false;
        }
        if (!SameStableWindowIdentity(expected, current))
        {
            reason = "guest-stable-identity-mismatch";
            return false;
        }
        if (!TestRunProvenance.TryValidateWindow(current, out reason))
            return false;
        return true;
    }

    private static bool SameStableWindowIdentity(WindowIdentity expected, WindowIdentity actual)
        => expected.Hwnd == actual.Hwnd
            && expected.ProcessId == actual.ProcessId
            && expected.WindowThreadId == actual.WindowThreadId
            && expected.ProcessStartTimeUtcTicks == actual.ProcessStartTimeUtcTicks
            && string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal)
            && string.Equals(expected.ExePath, actual.ExePath, StringComparison.OrdinalIgnoreCase);

    private static IntPtr RootAtPointOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private readonly record struct NativeGuestSnapshot(
        bool IdentityValid,
        NativeMethods.RECT Outer,
        bool HasOuter,
        uint Style,
        uint ExStyle,
        bool Zoomed,
        uint ShowCommand,
        IntPtr Monitor,
        uint Dpi,
        NativeMethods.RECT MonitorBounds,
        NativeMethods.RECT MonitorWork,
        IntPtr ForegroundRoot,
        IntPtr PointRoot,
        IntPtr PreviousZ,
        IntPtr Parent,
        string Title);

    private static void BrowserFullscreenContained(Ctx ctx, Options opt)
    {
        string[] supported = { "chrome-normal", "edge-normal", "brave-normal" };
        if (!supported.Contains(opt.Guest, StringComparer.OrdinalIgnoreCase))
        {
            ctx.SkipCapability($"browser-fullscreen-contained: supported physical F11 matrix is Chrome, Edge, and Brave; '{opt.Guest}' is outside that matrix");
            return;
        }

        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser);
        ctx.ExpectedState = $"A real F11 request on isolated {opt.Guest} is observed as browser-owned borderless fullscreen; Shepherd then exits that browser mode through one identity-checked browser-F11 request and recontains the same guest without a tab click.";
        if (!AssertGuestPresentation(ctx, browser, container, host, $"{opt.Guest} F11 baseline", allowBrowserOwnedSurface: true))
            return;

        // Restricted visual baseline for 19.1 — run-owned isolated profile, so TEST_OWNED host+guest scope is privacy-safe.
        // Visual is best-effort for this cycle; native containment remains the authoritative outcome.
        VisualTopologyBinding? baselineBinding = null;
        bool hasBaselineBinding = TryGetVisualMonitorBinding(ctx, container, out baselineBinding);
        if (hasBaselineBinding && baselineBinding != null)
        {
            CapturePhysicalVisual(ctx, $"{opt.Guest}-f11-baseline", VisualCheckpointPhase.BASELINE,
                $"Restricted baseline before F11 on isolated {opt.Guest}.",
                new[] { ctx.VisualGuestScope(browser), ctx.VisualContainerScope(container) }, baselineBinding);
        }

        int cycles = Math.Min(Math.Max(opt.Cycles ?? 2, 2), 3);
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            string phase = $"{opt.Guest} F11 cycle {cycle}/{cycles}";
            NativeGuestSnapshot before = ReadNativeGuestSnapshot(browser, host, phase + " before");
            AppendObservedState(ctx, phase + " before: " + DescribeNativeGuestSnapshot(before));
            if (hasBaselineBinding && baselineBinding != null)
            {
                CapturePhysicalVisual(ctx, $"{opt.Guest}-f11-{cycle}-before", VisualCheckpointPhase.BEFORE_ACTION,
                    $"Restricted {phase} before F11.",
                    new[] { ctx.VisualGuestScope(browser), ctx.VisualContainerScope(container) }, baselineBinding);
            }
            long actionOff = TabDockLog.RecordLogLength();
            if (!Input.SendF11To(browser.Hwnd))
            {
                ctx.BlockEnvironment($"{phase}: exact browser foreground/lease proof refused real F11 input");
                return;
            }

            bool transitionObserved = WaitForBrowserNativeTransition(
                browser,
                host,
                before,
                actionOff,
                3500,
                out NativeGuestSnapshot transitioned);
            int driftCount = TabDockLog.CountNewLines(actionOff, "SHEPHERD[drift-reconcile]");
            GuardedProc.Log($"  PHYSICAL_BROWSER[{phase}] transition observed={transitionObserved} drift={driftCount} before={DescribeNativeGuestSnapshot(before)} transitioned={DescribeNativeGuestSnapshot(transitioned)}");
            AppendObservedState(ctx, phase + " transitioned: " + DescribeNativeGuestSnapshot(transitioned));
            ctx.Check(transitionObserved, $"{phase}: real F11 changed browser native presentation before Shepherd repair ({DescribeNativeGuestSnapshot(transitioned)})");
            if (!transitionObserved)
                return;

            if (hasBaselineBinding && baselineBinding != null)
            {
                CapturePhysicalVisual(ctx, $"{opt.Guest}-f11-{cycle}-fullscreen", VisualCheckpointPhase.AFTER_ACTION_IMMEDIATE,
                    $"Restricted {phase} browser borderless fullscreen (style 0x{transitioned.Style:X}, outer {Util.FormatRect(transitioned.Outer)}).",
                    new[] { ctx.VisualGuestScope(browser), ctx.VisualContainerScope(container) }, baselineBinding);
            }

            bool contained = Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 3500);
            ctx.Check(contained, $"{phase}: Shepherd exited browser F11 through its identity-checked repair and returned the same browser to its assigned pane");
            if (!contained)
                return;

            uint style = unchecked((uint)NativeMethods.GetWindowLongPtr(browser.Hwnd, NativeMethods.GWL_STYLE).ToInt64());
            bool framed = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
            ctx.Check(framed, $"{phase}: browser left borderless F11 presentation before pane containment");
            if (!AssertGuestPresentation(ctx, browser, container, host, phase + " after-reconcile", allowBrowserOwnedSurface: true))
                return;
            AssertBrowserRender(ctx, browser, host, phase + " after-reconcile");
            if (hasBaselineBinding && baselineBinding != null)
            {
                CapturePhysicalVisual(ctx, $"{opt.Guest}-f11-{cycle}-after", VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                    $"Restricted {phase} after Shepherd recontained the browser.",
                    new[] { ctx.VisualGuestScope(browser), ctx.VisualContainerScope(container) }, baselineBinding);
            }
            ctx.Check(TabCount(container) == 1, $"{phase}: no tab click was needed and the captured tab count remained one");
            ctx.Check(TabDockLog.CountNewLines(actionOff, "SHEPHERD[presentation-restore-request]") == 1,
                $"{phase}: one identity-checked browser F11 repair request coalesced the native presentation drift");
            ctx.Check(TabDockLog.CountNewLines(actionOff, "Released tab") == 0,
                $"{phase}: F11 did not release the browser tab");
        }
    }

    private static bool WaitForBrowserNativeTransition(
        GuestInfo browser,
        IntPtr host,
        NativeGuestSnapshot before,
        long logOffset,
        int timeoutMs,
        out NativeGuestSnapshot latest)
    {
        latest = before;
        NativeGuestSnapshot observed = before;
        bool changed = Util.WaitUntil(() =>
        {
            observed = ReadNativeGuestSnapshot(browser, host, "transition-poll");
            return NativeGuestStateChanged(before, observed)
                || TabDockLog.CountNewLines(logOffset, "SHEPHERD[drift-reconcile]") > 0;
        }, timeoutMs, 30);
        latest = observed;
        return changed;
    }

    private static bool NativeGuestStateChanged(NativeGuestSnapshot before, NativeGuestSnapshot after)
        => before.Outer.left != after.Outer.left
            || before.Outer.top != after.Outer.top
            || before.Outer.right != after.Outer.right
            || before.Outer.bottom != after.Outer.bottom
            || before.Style != after.Style
            || before.ExStyle != after.ExStyle
            || before.Zoomed != after.Zoomed
            || before.ShowCommand != after.ShowCommand
            || before.Monitor != after.Monitor
            || !string.Equals(before.Title, after.Title, StringComparison.Ordinal);

    private static NativeGuestSnapshot ReadNativeGuestSnapshot(GuestInfo guest, IntPtr host, string phase)
    {
        bool identity = TryGetLiveGuestIdentity(guest, out _, out _);
        bool hasOuter = NativeMethods.GetWindowRect(guest.Hwnd, out NativeMethods.RECT outer);
        uint style = hasOuter
            ? unchecked((uint)NativeMethods.GetWindowLongPtr(guest.Hwnd, NativeMethods.GWL_STYLE).ToInt64())
            : 0;
        uint exStyle = hasOuter
            ? unchecked((uint)NativeMethods.GetWindowLongPtr(guest.Hwnd, NativeMethods.GWL_EXSTYLE).ToInt64())
            : 0;
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
        };
        bool placementRead = hasOuter && NativeMethods.GetWindowPlacement(guest.Hwnd, ref placement);
        IntPtr monitor = hasOuter
            ? NativeMethods.MonitorFromWindow(guest.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        bool monitorRead = monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref monitorInfo);
        uint dpi = monitor == IntPtr.Zero ? 0 : MonitorDpiService.GetEffectiveDpi(monitor);
        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        IntPtr pointRoot = RootAtPoint(hostClient.left + hostClient.Width / 2, hostClient.top + hostClient.Height / 2);
        if (hasOuter)
        {
            IntPtr owner = NativeMethods.GetWindow(guest.Hwnd, NativeMethods.GW_OWNER);
            IntPtr popup = NativeMethods.GetWindow(guest.Hwnd, NativeMethods.GW_ENABLEDPOPUP);
            GuardedProc.Log($"  PHYSICAL_BROWSER_SURFACE[{phase}] guest=0x{guest.Hwnd.ToInt64():X} owner=0x{owner.ToInt64():X} enabledPopup=0x{popup.ToInt64():X}");
        }
        NativeGuestSnapshot snapshot = new(
            identity,
            outer,
            hasOuter,
            style,
            exStyle,
            hasOuter && NativeMethods.IsZoomed(guest.Hwnd),
            placementRead ? placement.showCmd : 0,
            monitor,
            dpi,
            monitorRead ? monitorInfo.rcMonitor : default,
            monitorRead ? monitorInfo.rcWork : default,
            RootAtPointOf(NativeMethods.GetForegroundWindow()),
            pointRoot,
            hasOuter ? NativeMethods.GetWindow(guest.Hwnd, NativeMethods.GW_HWNDPREV) : IntPtr.Zero,
            hasOuter ? NativeMethods.GetParent(guest.Hwnd) : IntPtr.Zero,
            NativeMethods.GetWindowTextString(guest.Hwnd) ?? string.Empty);
        GuardedProc.Log($"  PHYSICAL_BROWSER_STATE[{phase}] {DescribeNativeGuestSnapshot(snapshot)}");
        return snapshot;
    }

    private static string DescribeNativeGuestSnapshot(NativeGuestSnapshot snapshot)
        => $"identity={snapshot.IdentityValid} outer={(snapshot.HasOuter ? Util.FormatRect(snapshot.Outer) : "<none>")} "
            + $"style=0x{snapshot.Style:X} exStyle=0x{snapshot.ExStyle:X} zoomed={snapshot.Zoomed} showCmd={snapshot.ShowCommand} "
            + $"monitor=0x{snapshot.Monitor.ToInt64():X} dpi={snapshot.Dpi} bounds={Util.FormatRect(snapshot.MonitorBounds)} work={Util.FormatRect(snapshot.MonitorWork)} "
            + $"foregroundRoot=0x{snapshot.ForegroundRoot.ToInt64():X} pointRoot=0x{snapshot.PointRoot.ToInt64():X} "
            + $"previousZ=0x{snapshot.PreviousZ.ToInt64():X} parent=0x{snapshot.Parent.ToInt64():X} title='{snapshot.Title}'";

    private static void AssertBrowserRender(Ctx ctx, GuestInfo browser, IntPtr host, string phase)
    {
        int[]? frame = Pixels.CaptureHostScreenArea(host);
        double brightness = frame == null ? -1 : Pixels.ComputeAvgBrightness(frame);
        ctx.Check(frame != null && brightness > 1.0, $"{phase}: browser content remained visibly rendered (brightness={brightness:F2})");
    }

    private static void AppendObservedState(Ctx ctx, string line)
    {
        ctx.ObservedState = string.IsNullOrEmpty(ctx.ObservedState)
            ? line
            : ctx.ObservedState + Environment.NewLine + line;
    }
}
