using System;
using System.Collections.Generic;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Behavioral regression coverage for the first-render capture boundary. The
/// test models the real ordering: the initial guest glue can execute before the
/// first WPF content render, and that render can invalidate native stacking;
/// the first-render boundary must therefore request one more coalesced pass.
/// </summary>
public sealed class InitialCapturePresentationTests
{
    [Fact]
    public void ActiveGuestCapturedBeforeFirstRender_ReceivesOnePostRenderPass()
    {
        var gate = new InitialPresentationReconciliationPolicy();
        var coordinator = new PresentationLayoutCoordinator();
        var pending = new Queue<Action>();
        int reconciliationPasses = 0;
        bool nativePresentationHealthy = false;

        void Request(bool ensureFinalPass = false)
            => coordinator.RequestRelayout(
                scheduleRender: callback => pending.Enqueue(callback),
                execute: () =>
                {
                    reconciliationPasses++;
                    nativePresentationHealthy = true;
                },
                ensureFinalPass);

        // SyncShepherdActiveWindow's initial pass runs during the capture call,
        // before ContentRendered. Drain that pass, then model WPF's first
        // composition changing native state after the pass has completed.
        Request();
        Assert.Single(pending);
        pending.Dequeue()();
        Assert.Equal(1, reconciliationPasses);

        // The first WPF composition is allowed to disturb the independent
        // guest/container native relationship even though the rect is unchanged.
        nativePresentationHealthy = false;

        // ContentRendered is the missing lifecycle boundary on current main.
        // Its request must be coalesced and must produce exactly one follow-up,
        // not an independent native presentation authority.
        if (gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: true))
            Request(ensureFinalPass: true);

        while (pending.Count > 0)
            pending.Dequeue()();

        Assert.Equal(2, reconciliationPasses);
        Assert.True(nativePresentationHealthy);
    }

    [Fact]
    public void ContentRenderedWhileInitialPassIsPending_LatchesOneFollowUp()
    {
        var gate = new InitialPresentationReconciliationPolicy();
        var coordinator = new PresentationLayoutCoordinator();
        var pending = new Queue<Action>();
        int reconciliationPasses = 0;

        void Request(bool ensureFinalPass = false)
            => coordinator.RequestRelayout(
                scheduleRender: callback => pending.Enqueue(callback),
                execute: () => reconciliationPasses++,
                ensureFinalPass);

        Request();
        Assert.True(gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: true));
        Request(ensureFinalPass: true);

        // The first queued frame and exactly one latched follow-up are enough;
        // the event must not create a parallel or unbounded relayout stream.
        while (pending.Count > 0)
            pending.Dequeue()();

        Assert.Equal(2, reconciliationPasses);
    }

    [Fact]
    public void FirstContentRenderedBoundary_IsConsumedOnce()
    {
        var gate = new InitialPresentationReconciliationPolicy();

        Assert.True(gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: true));
        Assert.False(gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: true));
    }

    [Fact]
    public void EmptyContainerFirstRender_DoesNotRequestGuestReconciliation()
    {
        var gate = new InitialPresentationReconciliationPolicy();

        Assert.False(gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: false));
        Assert.False(gate.ShouldReconcileAfterFirstContentRendered(hasActiveGuest: true));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void IncomingIconicGuest_RestoreIsRequiredOnlyForNormalContainer(
        bool containerMinimized,
        bool guestIconic,
        bool expected)
    {
        Assert.Equal(
            expected,
            Views.ContainerWindow.RequiresExplicitIncomingRestore(
                containerMinimized
                    ? System.Windows.WindowState.Minimized
                    : System.Windows.WindowState.Normal,
                guestIconic));
    }

    [Fact]
    public void PreMinimizedCapturedGuest_RestoreMinimizedClearsIconicState()
    {
        using var fixture = ReleaseTestFixture.Create();
        fixture.Native.SetVisible(fixture.Captured.Hwnd, visible: false);
        fixture.Native.SetIconic(fixture.Captured.Hwnd, iconic: true);

        Assert.True(fixture.Service.RestoreMinimized(fixture.Captured));
        Assert.Equal(NativeMethods.SW_RESTORE, fixture.Native.LastShowCommand);
        Assert.True(fixture.Native.IsWindowVisible(fixture.Captured.Hwnd));
        Assert.False(fixture.Native.IsIconic(fixture.Captured.Hwnd));
    }
}
