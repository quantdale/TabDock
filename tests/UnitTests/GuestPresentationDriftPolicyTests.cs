using System;
using Xunit;
using TabDock.Services;

namespace TabDock.UnitTests;

public sealed class GuestPresentationDriftPolicyTests
{
    private static NativeMethods.RECT R(int l, int t, int r, int b) => new() { left = l, top = t, right = r, bottom = b };

    [Fact]
    public void ZoomedVisibleGuestNeedsReconciliation()
    {
        var assigned = R(0, 0, 800, 600);
        var observed = R(0, 0, 1920, 1080); // fullscreen monitor size, zoomed
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: true, isIconic: false, isVisible: true, shouldBeVisible: true);
        Assert.True(eval.NeedsReconciliation);
        Assert.Equal(GuestPresentationDriftPolicy.DriftKind.Zoomed, eval.Kind);
    }

    [Fact]
    public void GeometryMismatchNeedsReconciliation()
    {
        var assigned = R(100, 100, 500, 400);
        var observed = R(0, 0, 800, 600); // guest moved to monitor 1
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: false, isVisible: true, shouldBeVisible: true);
        Assert.True(eval.NeedsReconciliation);
        Assert.Equal(GuestPresentationDriftPolicy.DriftKind.GeometryMismatch, eval.Kind);
    }

    [Fact]
    public void ExactGeometryNoDrift()
    {
        var assigned = R(10, 20, 810, 620);
        var observed = R(10, 20, 810, 620);
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: false, isVisible: true, shouldBeVisible: true);
        Assert.False(eval.NeedsReconciliation);
        Assert.Equal(GuestPresentationDriftPolicy.DriftKind.None, eval.Kind);
    }

    [Fact]
    public void EpsilonWithinIsHealthy()
    {
        var assigned = R(0, 0, 800, 600);
        var observed = R(1, 0, 801, 600); // 1px epsilon is allowed
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: false, isVisible: true, shouldBeVisible: true);
        Assert.False(eval.NeedsReconciliation);
    }

    [Fact]
    public void IconicGuestDoesNotRequestDrift()
    {
        var assigned = R(0, 0, 800, 600);
        var observed = R(0, 0, 800, 600);
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: true, isVisible: true, shouldBeVisible: true);
        Assert.False(eval.NeedsReconciliation);
    }

    [Fact]
    public void HiddenWhenShouldBeVisibleNeedsReconciliation()
    {
        var assigned = R(0, 0, 800, 600);
        var observed = R(0, 0, 800, 600);
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: false, isVisible: false, shouldBeVisible: true);
        Assert.True(eval.NeedsReconciliation);
        Assert.Equal(GuestPresentationDriftPolicy.DriftKind.NotVisibleButShouldBe, eval.Kind);
    }

    [Fact]
    public void NotShouldBeVisibleNeverRequests()
    {
        var assigned = R(0, 0, 800, 600);
        var observed = R(0, 0, 1920, 1080);
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: true, isIconic: false, isVisible: false, shouldBeVisible: false);
        Assert.False(eval.NeedsReconciliation);
    }

    [Fact]
    public void PairNeedsReconciliationIfEitherMemberDrifts()
    {
        var leftAssigned = R(0, 0, 400, 600);
        var rightAssigned = R(400, 0, 800, 600);
        var leftObserved = R(0, 0, 400, 600);
        var rightObserved = R(0, 0, 1920, 1080); // right zoomed
        bool needs = GuestPresentationDriftPolicy.NeedsPairReconciliation(
            leftAssigned, rightAssigned, leftObserved, rightObserved,
            leftZoomed: false, rightZoomed: true, leftVisible: true, rightVisible: true,
            leftIconic: false, rightIconic: false, shouldBeVisible: true);
        Assert.True(needs);
    }

    [Fact]
    public void PairNoDriftWhenBothHealthy()
    {
        var leftAssigned = R(0, 0, 400, 600);
        var rightAssigned = R(400, 0, 800, 600);
        var leftObserved = R(0, 0, 400, 600);
        var rightObserved = R(400, 0, 800, 600);
        bool needs = GuestPresentationDriftPolicy.NeedsPairReconciliation(
            leftAssigned, rightAssigned, leftObserved, rightObserved,
            leftZoomed: false, rightZoomed: false, leftVisible: true, rightVisible: true,
            leftIconic: false, rightIconic: false, shouldBeVisible: true);
        Assert.False(needs);
    }

    [Fact]
    public void ZeroAssignedDoesNotMistakenlyRequest()
    {
        var assigned = R(0, 0, 0, 0);
        var observed = R(100, 100, 200, 200);
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: false, isIconic: false, isVisible: true, shouldBeVisible: true);
        Assert.False(eval.NeedsReconciliation);
    }
}
