using System;
using System.Collections.Generic;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Regression coverage for the minimize/restore stale-refusal invariant fixed
/// in 3591ee3 and locked behind the Wave-0B seam:
///
///   visible guest + same refused rect  => suppression allowed
///   hidden  guest + same refused rect  => suppression MUST NOT prevent
///                                         PositionAndShow / PositionGuestsDeferred
///
/// These tests guard the actual decision function
/// (<see cref="PaneContainmentPolicy.ShouldSuppressRepositioning"/>) plus the
/// record/clear lifecycle the presentation layer drives, so a future change
/// that drops the visibility gate (or widens "same rect") fails here.
/// </summary>
public class PaneContainmentPolicyTests
{
    private static NativeMethods.RECT Rect(int l, int t, int r, int b) => new() { left = l, top = t, right = r, bottom = b };

    /// <summary>
    /// Mirrors the production call-site decision shape: storage lookup feeds
    /// the policy together with the guest's current visibility.
    /// </summary>
    private static bool SuppressWouldApply(
        Dictionary<long, NativeMethods.RECT> cache, long hwndKey, bool visible, NativeMethods.RECT requested)
        => cache.TryGetValue(hwndKey, out NativeMethods.RECT refused)
            && PaneContainmentPolicy.ShouldSuppressRepositioning(visible, refused, requested);

    // ---- single guest ------------------------------------------------------

    [Fact]
    public void SingleGuest_VisibleAtSameRefusedRect_SuppressionAllowed()
    {
        var cache = new Dictionary<long, NativeMethods.RECT> { [1001] = Rect(10, 10, 500, 400) };
        Assert.True(SuppressWouldApply(cache, 1001, visible: true, Rect(10, 10, 500, 400)));
        // Glue epsilon: a 1px-different requested rect is still "the same" refusal.
        Assert.True(SuppressWouldApply(cache, 1001, visible: true, Rect(11, 10, 501, 400)));
    }

    [Fact]
    public void SingleGuest_HiddenAtSameRefusedRect_SuppressionMustNotApply()
    {
        var cache = new Dictionary<long, NativeMethods.RECT> { [1001] = Rect(10, 10, 500, 400) };
        // The restore case: container minimized hid the guest; the restored pane
        // matches the stale refusal — the fresh position attempt must proceed.
        Assert.False(SuppressWouldApply(cache, 1001, visible: false, Rect(10, 10, 500, 400)));
    }

    [Fact]
    public void SingleGuest_ChangedRect_SuppressionDoesNotApply()
    {
        var cache = new Dictionary<long, NativeMethods.RECT> { [1001] = Rect(10, 10, 500, 400) };
        Assert.False(SuppressWouldApply(cache, 1001, visible: true, Rect(10, 10, 700, 400))); // grown past epsilon
    }

    [Fact]
    public void SingleGuest_RefusalCleared_RepositionProceeds()
    {
        var cache = new Dictionary<long, NativeMethods.RECT> { [1001] = Rect(10, 10, 500, 400) };
        cache.Remove(1001); // re-glue succeeded / compliance restored
        Assert.False(SuppressWouldApply(cache, 1001, visible: true, Rect(10, 10, 500, 400)));
    }

    [Fact]
    public void EpsilonBoundary_OnePixelIsSameRefusal_TwoPixelsDiffer()
    {
        var refused = Rect(0, 0, 300, 200);
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(refused, Rect(1, 1, 301, 201)));
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(refused, Rect(2, 0, 302, 202)));
    }

    // ---- Wave 2D: the epsilon authority's full edge contract ----------------
    // Every ContainerWindow ±1px comparison routes through MatchesWithinEpsilon,
    // so its per-edge behavior is now load-bearing for layout equivalence too.

    [Fact]
    public void Epsilon_ExactMatch_IsTrue()
    {
        var a = Rect(-1920, -500, 300, 200);
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(a, a));
    }

    [Fact]
    public void Epsilon_EachEdgeIndependently_ToleratesOnePixel()
    {
        var baseRect = Rect(100, 100, 400, 300);
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(101, 100, 400, 300))); // left +1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(99, 100, 400, 300)));  // left -1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 101, 400, 300))); // top +1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 99, 400, 300)));  // top -1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 401, 300))); // right +1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 399, 300))); // right -1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 400, 301))); // bottom +1
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 400, 299))); // bottom -1
    }

    [Fact]
    public void Epsilon_TwoPixelsOnAnyEdge_IsRejected()
    {
        var baseRect = Rect(100, 100, 400, 300);
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(102, 100, 400, 300))); // left +2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(98, 100, 400, 300)));  // left -2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 102, 400, 300))); // top +2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 98, 400, 300)));  // top -2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 402, 300))); // right +2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 398, 300))); // right -2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 400, 302))); // bottom +2
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(100, 100, 400, 298))); // bottom -2
    }

    [Fact]
    public void Epsilon_OnePixelOnEachEdgeSimultaneously_IsTolerated()
    {
        var baseRect = Rect(100, 100, 400, 300);
        // All four edges off by one in mixed directions at once: the glue
        // epsilon is per-edge, not a total-difference budget.
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(99, 101, 399, 301)));
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(101, 99, 401, 299)));
    }

    [Fact]
    public void Epsilon_NegativeScreenCoordinates_BehaveIdentically()
    {
        // Left-of-primary monitor geometry (negative origins are normal on
        // multi-monitor desktops).
        var baseRect = Rect(-1920, -1080, -1600, -800);
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(-1919, -1079, -1599, -799)));
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect(-1918, -1080, -1600, -800)));
    }

    [Fact]
    public void Epsilon_LargePositiveCoordinates_BehaveIdentically()
    {
        // Far-right/bottom monitor geometry; no overflow or magnitude effects.
        long big = 2_000_000_000;
        var baseRect = Rect((int)big, (int)big, (int)(big + 800), (int)(big + 600));
        Assert.True(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect((int)big + 1, (int)big - 1, (int)(big + 801), (int)(big + 599))));
        Assert.False(PaneContainmentPolicy.MatchesWithinEpsilon(baseRect, Rect((int)big + 2, (int)big, (int)(big + 800), (int)(big + 600))));
    }

    // ---- presented split pair ----------------------------------------------

    private static readonly NativeMethods.RECT LeftPane = Rect(0, 0, 400, 600);
    private static readonly NativeMethods.RECT RightPane = Rect(400, 0, 800, 600);

    [Fact]
    public void Split_LeftHiddenRightRefusing_OnlyRightSuppresses()
    {
        var cache = new Dictionary<long, NativeMethods.RECT>
        {
            [2001] = LeftPane,   // left recorded refusal...
            [2002] = RightPane,  // right recorded refusal
        };

        bool topSuppressed = SuppressWouldApply(cache, 2001, visible: false, LeftPane);
        bool bottomSuppressed = SuppressWouldApply(cache, 2002, visible: true, RightPane);

        Assert.False(topSuppressed);   // hidden left must be re-positioned on restore
        Assert.True(bottomSuppressed); // visible refuser keeps its bounded skip
        Assert.False(topSuppressed && bottomSuppressed);
    }

    [Fact]
    public void Split_BothHidden_NeitherSuppresses()
    {
        var cache = new Dictionary<long, NativeMethods.RECT>
        {
            [2001] = LeftPane,
            [2002] = RightPane,
        };

        bool anySuppressed =
            SuppressWouldApply(cache, 2001, visible: false, LeftPane)
            || SuppressWouldApply(cache, 2002, visible: false, RightPane);

        // Restore path: PositionGuestsDeferred MUST run for both panes.
        Assert.False(anySuppressed);
    }

    [Fact]
    public void Split_VisibleRefuserSuppresses_PairZOrderStillAppliedByCaller()
    {
        var cache = new Dictionary<long, NativeMethods.RECT> { [2002] = RightPane };
        bool suppressed = SuppressWouldApply(cache, 2002, visible: true, RightPane);
        // Caller contract (LayoutSplitPanes): when either side suppresses it
        // still pins the container below the panes via PairZOrderBehind before
        // skipping the geometry write — only the WRITE is skipped.
        Assert.True(suppressed);
    }

    [Fact]
    public void Split_NoRefusalRecords_PositioningProceeds()
    {
        var cache = new Dictionary<long, NativeMethods.RECT>();
        Assert.False(SuppressWouldApply(cache, 2001, visible: true, LeftPane));
        Assert.False(SuppressWouldApply(cache, 2002, visible: true, RightPane));
    }

    // ---- record lifecycle ---------------------------------------------------

    [Fact]
    public void RecordLifecycle_MarkThenRectChangeReevaluates()
    {
        var cache = new Dictionary<long, NativeMethods.RECT>();
        long key = 3001;
        var first = Rect(10, 10, 500, 400);

        // First pass: no refusal -> positioned; observed differs -> marked.
        Assert.False(SuppressWouldApply(cache, key, visible: true, first));
        cache[key] = first;

        // Second pass at same rect while visible -> bounded skip applies.
        Assert.True(SuppressWouldApply(cache, key, visible: true, first));

        // Container grows: rect changed -> refusal re-evaluated away.
        var grown = Rect(10, 10, 700, 400);
        Assert.False(SuppressWouldApply(cache, key, visible: true, grown));

        // Mark dedupe: recording the identical refusal again changes nothing.
        if (!cache.TryGetValue(key, out var prior) || !PaneContainmentPolicy.IsExactSameRect(prior, first))
            cache[key] = first;
        Assert.Equal(first, cache[key]);
    }
}
