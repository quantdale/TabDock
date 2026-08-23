using System;
using System.Collections.Generic;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic verification of the relayout coalescing contract from the
/// live-runtime stabilization campaign:
///   - idle + ensureFinalPass:true  -> exactly ONE pass (no redundant second frame)
///   - idle + ensureFinalPass:false -> exactly ONE pass
///   - already pending + ensureFinalPass:true -> existing pass + exactly ONE follow-up
///   - already pending + ensureFinalPass:false -> existing pass only
/// </summary>
public class RequestRelayoutFinalPassTests
{
    private static PresentationLayoutCoordinator CreateCoordinator()
        => new PresentationLayoutCoordinator();

    [Fact]
    public void Idle_EnsureFinalPass_True_ExecutesExactlyOnce()
    {
        var coordinator = CreateCoordinator();
        int executes = 0;
        coordinator.RequestRelayout(cb => cb(), () => executes++, ensureFinalPass: true);
        Assert.Equal(1, executes);
    }

    [Fact]
    public void Idle_EnsureFinalPass_False_ExecutesExactlyOnce()
    {
        var coordinator = CreateCoordinator();
        int executes = 0;
        coordinator.RequestRelayout(cb => cb(), () => executes++);
        Assert.Equal(1, executes);
    }

    [Fact]
    public void Pending_Then_EnsureFinalPass_True_ExecutesExistingPlusOneFollowUp()
    {
        var coordinator = CreateCoordinator();
        int executes = 0;
        var pending = new Queue<Action>();
        // First request: deferred schedule (not yet run) -> pending.
        coordinator.RequestRelayout(cb => pending.Enqueue(cb), () => executes++);
        // Second request while pending, with ensureFinalPass -> latch follow-up only.
        coordinator.RequestRelayout(cb => pending.Enqueue(cb), () => executes++, ensureFinalPass: true);

        while (pending.Count > 0)
            pending.Dequeue()();

        Assert.Equal(2, executes);
    }

    [Fact]
    public void Pending_Then_EnsureFinalPass_False_ExecutesOnlyExistingPass()
    {
        var coordinator = CreateCoordinator();
        int executes = 0;
        var pending = new Queue<Action>();
        coordinator.RequestRelayout(cb => pending.Enqueue(cb), () => executes++);
        coordinator.RequestRelayout(cb => pending.Enqueue(cb), () => executes++, ensureFinalPass: false);

        while (pending.Count > 0)
            pending.Dequeue()();

        Assert.Equal(1, executes);
    }

    // The former UnchangedLayoutUpdated_ProducesNoRelayout fact here was a
    // tautology: byte-identical to Idle_EnsureFinalPass_True above and never
    // touching the dirty-check it claimed to cover. The real LayoutUpdated
    // content-rect decision now lives behind
    // PaneContainmentPolicy.ShouldRequestRelayoutForContentRect and is covered
    // exhaustively in PaneContainmentPolicyTests (first observation, unchanged,
    // epsilon edges, degenerate rects, cache-poisoning resistance).
}
