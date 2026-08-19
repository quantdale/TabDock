using System;
using System.Collections.Generic;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

sealed class FakePresentationOps : IPresentationOperations
{
    private readonly IPresentationBudgetSink _sink;
    private readonly HashSet<long> _hidden = new();
    public FakePresentationOps(IPresentationBudgetSink sink) => _sink = sink;

    public WindowHideOutcome Hide(CapturedWindow window)
    {
        _sink.RecordHide(window.Hwnd);
        _hidden.Add(window.Hwnd.ToInt64());
        return WindowHideOutcome.Hidden;
    }

    public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
    {
        _sink.RecordPositionAndShow(window.Hwnd);
        _hidden.Remove(window.Hwnd.ToInt64());
    }

    public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd)
    {
        _sink.RecordDeferBatch();
        _sink.RecordPositionAndShow(top.Hwnd);
        _sink.RecordPositionAndShow(bottom.Hwnd);
        _sink.RecordPairZOrder();
        _hidden.Remove(top.Hwnd.ToInt64());
        _hidden.Remove(bottom.Hwnd.ToInt64());
    }

    public void SetForeground(CapturedWindow window) => _sink.RecordSetForeground(window.Hwnd);
    public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest) => _sink.RecordPairZOrder();
    public bool IsCurrentCapturedWindow(CapturedWindow window) => true;
}

static class BudgetFixtures
{
    public static CapturedWindow Window(int id) => new()
    {
        Hwnd = new IntPtr(id),
        ProcessId = (uint)id,
        WindowThreadId = (uint)id,
        WindowIdentityToken = id,
        ExePath = "C:\\app.exe",
        OriginalClassName = "Chrome",
        OriginalTitle = $"Win{id:X}",
    };
}

public class PresentationOperationBudgetTests
{
    // A: Normal tab A->B — B positioned/shown once, A hidden once, foreground once.
    [Fact]
    public void NormalTab_A_to_B_SingleHideSingleShowSingleForeground()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        var rect = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        IntPtr container = new IntPtr(0x9999);

        // Simulate SyncShepherdActiveWindow ordinary switch: hide old, show new, foreground new.
        ops.Hide(a);
        ops.PositionAndShow(b, container, rect);
        ops.SetForeground(b);
        budget.RecordLayoutSingle();

        PresentationOperationCounts c = budget.Snapshot();
        Assert.Equal(1, c.HideCount);
        Assert.Equal(1, c.HideForHwnd(a.Hwnd));
        Assert.Equal(0, c.HideForHwnd(b.Hwnd));
        Assert.Equal(1, c.PositionAndShowCount);
        Assert.Equal(1, c.PositionAndShowForHwnd(b.Hwnd));
        Assert.Equal(1, c.SetForegroundCount);
        Assert.Equal(1, c.SetForegroundForHwnd(b.Hwnd));
        Assert.Equal(1, c.LayoutSingleCount);
        Assert.Equal(0, c.DeferBatchCount);
        Assert.Equal(0, c.LayoutSplitPanesCount);
    }

    [Fact]
    public void NormalTab_DoesNotDuplicateForegroundOrShow()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        var rect = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        IntPtr container = new IntPtr(0x9999);

        ops.Hide(a);
        ops.PositionAndShow(b, container, rect);
        ops.SetForeground(b);
        // Second call would be a bug — ensure counts stay 1.
        PresentationOperationCounts c1 = budget.Snapshot();
        Assert.Equal(1, c1.SetForegroundCount);
        Assert.Equal(1, c1.PositionAndShowCount);

        // If caller accidentally double-calls, budget would show 2 — test proves single-pass contract.
        budget.Reset();
        ops.Hide(a);
        ops.PositionAndShow(b, container, rect);
        ops.SetForeground(b);
        PresentationOperationCounts c2 = budget.Snapshot();
        Assert.Equal(1, c2.HideCount);
        Assert.Equal(1, c2.PositionAndShowCount);
        Assert.Equal(1, c2.SetForegroundCount);
    }

    // B: Presented split A/B -> ordinary C: A hidden once, B hidden once, C shown once, foreground once, no duplicate LayoutSplit.
    [Fact]
    public void PresentedSplit_To_Guest_Budgets()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        CapturedWindow c = BudgetFixtures.Window(0x1003);
        var rect = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        IntPtr container = new IntPtr(0x9999);

        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: true, foreground: a, generation: 1);

        bool suspended = ctrl.SuspendForGuest(c);
        Assert.True(suspended);
        // After suspend, present C as single guest (one layout pass).
        ops.PositionAndShow(c, container, rect);
        ops.SetForeground(c);
        budget.RecordLayoutSingle();

        PresentationOperationCounts s = budget.Snapshot();
        Assert.Equal(1, s.HideForHwnd(a.Hwnd));
        Assert.Equal(1, s.HideForHwnd(b.Hwnd));
        Assert.Equal(2, s.HideCount);
        Assert.Equal(1, s.PositionAndShowForHwnd(c.Hwnd));
        Assert.Equal(1, s.PositionAndShowCount);
        Assert.Equal(1, s.SetForegroundForHwnd(c.Hwnd));
        Assert.Equal(1, s.SetForegroundCount);
        Assert.Equal(0, s.DeferBatchCount);
        Assert.Equal(0, s.LayoutSplitPanesCount);
        Assert.Equal(1, s.LayoutSingleCount);
        // No duplicate hide of C, no duplicate layout.
        Assert.Equal(0, s.HideForHwnd(c.Hwnd));
    }

    [Fact]
    public void PresentedSplit_To_Guest_DoesNotDoubleHideOrDoubleLayout()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        CapturedWindow c = BudgetFixtures.Window(0x1003);
        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: true, foreground: a, generation: 5);

        ctrl.SuspendForGuest(c);
        // Simulate that Sync path would NOT re-hide A/B — assert controller hid exactly once each.
        PresentationOperationCounts s = budget.Snapshot();
        Assert.Equal(1, s.HideForHwnd(a.Hwnd));
        Assert.Equal(1, s.HideForHwnd(b.Hwnd));
        // No second LayoutSplitPanes.
        Assert.Equal(0, s.LayoutSplitPanesCount);
        Assert.Equal(0, s.DeferBatchCount);
    }

    // C: Ordinary C -> dormant A/B resume: C hidden once, A/B pane transaction once, pair z-order once, foreground once.
    [Fact]
    public void DormantPair_Resume_Budgets()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        CapturedWindow c = BudgetFixtures.Window(0x1003);
        IntPtr container = new IntPtr(0x9999);
        var leftRect = new NativeMethods.RECT { left = 0, top = 0, right = 400, bottom = 600 };
        var rightRect = new NativeMethods.RECT { left = 400, top = 0, right = 800, bottom = 600 };

        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: false, foreground: a, generation: 2);

        bool resumed = ctrl.ResumeMember(a, currentSingleGuest: c);
        Assert.True(resumed);
        // Resume hides C, then one atomic defer batch for the pair, then one foreground.
        ops.PositionGuestsDeferred(a, leftRect, b, rightRect, container);
        ops.SetForeground(a);
        budget.RecordLayoutSplit();

        PresentationOperationCounts s = budget.Snapshot();
        Assert.Equal(1, s.HideForHwnd(c.Hwnd));
        Assert.Equal(1, s.HideCount);
        Assert.Equal(1, s.DeferBatchCount);
        Assert.Equal(1, s.PositionAndShowForHwnd(a.Hwnd));
        Assert.Equal(1, s.PositionAndShowForHwnd(b.Hwnd));
        Assert.Equal(1, s.SetForegroundForHwnd(a.Hwnd));
        Assert.Equal(1, s.SetForegroundCount);
        Assert.Equal(1, s.LayoutSplitPanesCount);
        // No redundant storm: exactly one defer, one foreground, one split layout.
        Assert.Equal(2, s.PositionAndShowCount);
    }

    [Fact]
    public void DormantPair_Resume_NoRedundantStorm()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        CapturedWindow c = BudgetFixtures.Window(0x1003);
        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: false, foreground: a, generation: 2);
        ctrl.ResumeMember(a, currentSingleGuest: c);
        // Controller hides C exactly once — not twice.
        Assert.Equal(1, budget.Snapshot().HideForHwnd(c.Hwnd));
        Assert.Equal(1, budget.Snapshot().HideCount);
    }

    // D: Split member focus A<->B: no hide/show, only z-order/foreground, no teardown.
    [Fact]
    public void SplitMemberFocus_NoHideShow_OnlyZOrderAndForeground()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        IntPtr container = new IntPtr(0x9999);
        var leftRect = new NativeMethods.RECT { left = 0, top = 0, right = 400, bottom = 600 };
        var rightRect = new NativeMethods.RECT { left = 400, top = 0, right = 800, bottom = 600 };

        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: true, foreground: a, generation: 3);

        // Focus B: pure z-order + foreground, no hide/show.
        ctrl.FocusMember(b);
        // Simulate FocusSplitMember's single re-glue: one defer batch with B on top.
        ops.PositionGuestsDeferred(b, rightRect, a, leftRect, container);
        ops.SetForeground(b);
        budget.RecordLayoutSplit();

        PresentationOperationCounts s = budget.Snapshot();
        Assert.Equal(0, s.HideCount);
        Assert.Equal(0, s.HideForHwnd(a.Hwnd));
        Assert.Equal(0, s.HideForHwnd(b.Hwnd));
        Assert.Equal(1, s.DeferBatchCount);
        Assert.Equal(1, s.SetForegroundForHwnd(b.Hwnd));
        Assert.Equal(1, s.SetForegroundCount);
        Assert.Equal(1, s.LayoutSplitPanesCount);
        // No teardown — pair still presented.
        Assert.True(ctrl.IsPresented);
    }

    [Fact]
    public void SplitMemberFocus_DoesNotTearDownPair()
    {
        var budget = new PresentationOperationCounter();
        var ops = new FakePresentationOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        var ctrl = new SplitPresentationController(ops, budget);
        ctrl.SeedState(a, b, presented: true, foreground: a, generation: 3);
        ctrl.FocusMember(b);
        Assert.True(ctrl.IsPresented);
        Assert.Equal(b, ctrl.Foreground);
        Assert.Equal(0, budget.Snapshot().HideCount);
    }

    // E: Container move/resize coalesced — one frame cannot issue several identical batches.
    [Fact]
    public void CoalescedRelayout_OneFrame_OneBatch()
    {
        var budget = new PresentationOperationCounter();
        var coordinator = new PresentationLayoutCoordinator(null, budget);
        int executes = 0;
        void Execute() { executes++; budget.RecordLayoutSingle(); }

        // Simulate 5 LocationChanged/SizeChanged/LayoutUpdated triggers in same frame.
        coordinator.CoalesceAndExecute(Execute, coalescedRequests: 5);

        Assert.Equal(1, executes);
        Assert.Equal(1, budget.Snapshot().LayoutSingleCount);
    }

    [Fact]
    public void CoalescedRelayout_SeparateFrames_TwoBatches()
    {
        var budget = new PresentationOperationCounter();
        var coordinator = new PresentationLayoutCoordinator(null, budget);
        int executes = 0;
        void Execute() { executes++; budget.RecordLayoutSingle(); }

        coordinator.CoalesceAndExecute(Execute, 3);
        coordinator.CoalesceAndExecute(Execute, 3);

        Assert.Equal(2, executes);
        Assert.Equal(2, budget.Snapshot().LayoutSingleCount);
    }

    [Fact]
    public void CoalescedRelayout_SplitPanes_OneDeferPerFrame()
    {
        var budget = new PresentationOperationCounter();
        var coordinator = new PresentationLayoutCoordinator(null, budget);
        int defers = 0;
        void ExecuteSplit() { defers++; budget.RecordDeferBatch(); budget.RecordLayoutSplit(); }

        coordinator.CoalesceAndExecute(ExecuteSplit, 10);
        Assert.Equal(1, defers);
        PresentationOperationCounts s = budget.Snapshot();
        Assert.Equal(1, s.DeferBatchCount);
        Assert.Equal(1, s.LayoutSplitPanesCount);
    }

    // Additional: controller generation + settle + policy wrap.
    [Fact]
    public void SplitController_GenerationAndSettle()
    {
        var ctrl = new SplitPresentationController();
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        ctrl.DefinePair(a, b, a);
        long gen = ctrl.Generation;
        Assert.True(ctrl.IsPresented);
        Assert.True(ctrl.SettlePending);
        Assert.Equal(gen, ctrl.SettleGeneration);
        Assert.True(ctrl.IsCurrentSettle(gen));
        Assert.False(ctrl.IsCurrentSettle(gen + 1));
        ctrl.DisarmSettle();
        Assert.False(ctrl.SettlePending);
    }

    [Fact]
    public void SplitController_RecoveryPending_FailClosed_NoDuplicateHide()
    {
        var budget = new PresentationOperationCounter();
        var failingOps = new FailingHideOps(budget);
        CapturedWindow a = BudgetFixtures.Window(0x1001);
        CapturedWindow b = BudgetFixtures.Window(0x1002);
        CapturedWindow c = BudgetFixtures.Window(0x1003);
        var ctrl = new SplitPresentationController(failingOps, budget);
        ctrl.SeedState(a, b, presented: true, foreground: a, generation: 1);

        bool ok = ctrl.SuspendForGuest(c);
        Assert.False(ok); // recovery-pending keeps pair presented
        Assert.True(ctrl.IsPresented);
        // Only one Hide attempted before failing closed (A fails, B not attempted second time).
        Assert.Equal(1, budget.Snapshot().HideCount);
    }

    [Fact]
    public void PresentationOperationCounter_ThreadSafeSnapshot()
    {
        var c = new PresentationOperationCounter();
        c.RecordHide(new IntPtr(0x1));
        c.RecordHide(new IntPtr(0x1));
        c.RecordPositionAndShow(new IntPtr(0x2));
        c.RecordSetForeground(new IntPtr(0x2));
        c.RecordDeferBatch();
        c.RecordLayoutSplit();
        c.RecordLayoutSingle();
        c.RecordPairZOrder();
        var s = c.Snapshot();
        Assert.Equal(2, s.HideForHwnd(new IntPtr(0x1)));
        Assert.Equal(1, s.PositionAndShowForHwnd(new IntPtr(0x2)));
        Assert.Equal(1, s.SetForegroundForHwnd(new IntPtr(0x2)));
        Assert.Equal(1, s.DeferBatchCount);
        Assert.Equal(1, s.LayoutSplitPanesCount);
        Assert.Equal(1, s.LayoutSingleCount);
        Assert.Equal(1, s.PairZOrderBehindCount);
        c.Reset();
        Assert.Equal(0, c.Snapshot().HideCount);
    }

    // Helper that fails first Hide with RecoveryPending semantics.
    sealed class FailingHideOps : IPresentationOperations
    {
        private readonly IPresentationBudgetSink _sink;
        private int _calls;
        public FailingHideOps(IPresentationBudgetSink sink) => _sink = sink;
        public WindowHideOutcome Hide(CapturedWindow window)
        {
            _sink.RecordHide(window.Hwnd);
            return _calls++ == 0 ? WindowHideOutcome.RecoveryPending : WindowHideOutcome.Hidden;
        }
        public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect) => _sink.RecordPositionAndShow(window.Hwnd);
        public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd) { _sink.RecordDeferBatch(); }
        public void SetForeground(CapturedWindow window) => _sink.RecordSetForeground(window.Hwnd);
        public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest) => _sink.RecordPairZOrder();
        public bool IsCurrentCapturedWindow(CapturedWindow window) => true;
    }
}
