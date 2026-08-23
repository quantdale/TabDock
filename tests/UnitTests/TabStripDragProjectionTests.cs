using System;
using System.Collections.Generic;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless matrix for the pure tab-strip drag projection: mapping a drop
/// position over VISIBLE strip slots to an authoritative Tabs insertion index.
/// The composite slot resolves through its LEFT member's live index, ordinary
/// slots through their own tab — never positional arithmetic, so the mapping
/// stays correct for pairs at any strip position with arbitrary non-members
/// around them. With no composite the result is identical to the historical
/// midpoint formula (anchor == display index), pinning unchanged no-split
/// behavior and the H2-compatible snapshot contract (counts decide
/// resnapshotting; reorders change no counts).
/// </summary>
public class TabStripDragProjectionTests
{
    private static TabViewModel Tab(string name)
        => new(new Models.CapturedWindow { ExePath = name });

    /// <summary>
    /// A live Tabs collection plus the anchor resolver the view passes to the
    /// projection: TabViewModel → its index; composite → Left's index;
    /// anything else unresolved.
    /// </summary>
    private sealed class Strip
    {
        public List<TabViewModel> TabsList { get; } = new();

        public int? AnchorOf(object item) => item switch
        {
            TabViewModel t => Index(t),
            SplitCompositeViewModel c => c.Left != null ? Index(c.Left) : null,
            _ => null,
        };

        private int Index(TabViewModel t) => TabsList.IndexOf(t);
    }

    private static TabStripDragProjection.DragSlot Slot(double mid, object item)
        => new(mid, item);

    [Fact]
    public void NoSplit_MatchesTheHistoricalMidpointFormulaExactly()
    {
        var a = Tab("A"); var b = Tab("B"); var c = Tab("C");
        var strip = new Strip { TabsList = { a, b, c } };
        var slots = new[] { Slot(50, a), Slot(150, b), Slot(250, c) };

        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 10, strip.AnchorOf, 3));
        Assert.Equal(1, TabStripDragProjection.ResolveDropTargetIndex(slots, 100, strip.AnchorOf, 3));
        Assert.Equal(2, TabStripDragProjection.ResolveDropTargetIndex(slots, 200, strip.AnchorOf, 3));
        Assert.Equal(3, TabStripDragProjection.ResolveDropTargetIndex(slots, 999, strip.AnchorOf, 3));
        // Exact boundary semantics preserved: strictly-less-than comparison.
        Assert.Equal(1, TabStripDragProjection.ResolveDropTargetIndex(slots, 50, strip.AnchorOf, 3));
    }

    [Fact]
    public void PairFirst_NonMembersMapThroughTheCompositeWithoutPlusOneShortcuts()
    {
        var a = Tab("A"); var b = Tab("B"); var c = Tab("C"); var d = Tab("D");
        var strip = new Strip { TabsList = { a, b, c, d } };
        // DisplayTabs: [composite(A|B)] C D   → visible slots only.
        var composite = new SplitCompositeViewModel(a, b);
        var slots = new[] { Slot(100, composite), Slot(200, c), Slot(300, d) };

        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 40, strip.AnchorOf, 4));  // before A|B
        Assert.Equal(2, TabStripDragProjection.ResolveDropTargetIndex(slots, 150, strip.AnchorOf, 4)); // between pair and C
        Assert.Equal(3, TabStripDragProjection.ResolveDropTargetIndex(slots, 250, strip.AnchorOf, 4)); // between C and D
        Assert.Equal(4, TabStripDragProjection.ResolveDropTargetIndex(slots, 900, strip.AnchorOf, 4)); // past end
    }

    [Fact]
    public void PairMiddle_AnchorsStayCorrectWithMembersSurroundedByNonMembers()
    {
        // Authoritative order C A B D → display order C [A|B] D.
        var a = Tab("A"); var b = Tab("B"); var c = Tab("C"); var d = Tab("D");
        var strip = new Strip { TabsList = { c, a, b, d } };
        var composite = new SplitCompositeViewModel(a, b);
        var slots = new[] { Slot(50, c), Slot(150, composite), Slot(250, d) };

        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 20, strip.AnchorOf, 4));  // before C
        Assert.Equal(1, TabStripDragProjection.ResolveDropTargetIndex(slots, 80, strip.AnchorOf, 4));  // between C and pair
        Assert.Equal(3, TabStripDragProjection.ResolveDropTargetIndex(slots, 180, strip.AnchorOf, 4)); // between pair and D
        Assert.Equal(4, TabStripDragProjection.ResolveDropTargetIndex(slots, 400, strip.AnchorOf, 4));
    }

    [Fact]
    public void LeadingAndTrailingNonMembers_NeverRelyOnPositionalArithmetic()
    {
        // Authoritative E A B F: composite sits between two non-members and is
        // NOT at indexes 0/1 — the historical +1 shortcut would misresolve here.
        var a = Tab("A"); var b = Tab("B"); var e = Tab("E"); var f = Tab("F");
        var strip = new Strip { TabsList = { e, a, b, f } };
        var slots = new[]
        {
            Slot(25, e),
            Slot(125, new SplitCompositeViewModel(a, b)),
            Slot(225, f),
        };

        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 10, strip.AnchorOf, 4));  // before E
        Assert.Equal(1, TabStripDragProjection.ResolveDropTargetIndex(slots, 60, strip.AnchorOf, 4));  // between E and the pair (before A)
        Assert.Equal(3, TabStripDragProjection.ResolveDropTargetIndex(slots, 160, strip.AnchorOf, 4)); // between pair and F
        Assert.Equal(4, TabStripDragProjection.ResolveDropTargetIndex(slots, 400, strip.AnchorOf, 4));

        // Live re-resolution after an in-drag reorder that moved E behind the
        // pair (authoritative now A B E F): the SAME stored slot references map
        // through their NEW anchors — no geometry resnapshot involved.
        strip.TabsList.Remove(e);
        strip.TabsList.Insert(2, e);
        Assert.Equal(2, TabStripDragProjection.ResolveDropTargetIndex(slots, 10, strip.AnchorOf, 4));  // before E slot -> now anchors E at index 2
        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 60, strip.AnchorOf, 4));  // before pair -> A now at index 0
        Assert.Equal(3, TabStripDragProjection.ResolveDropTargetIndex(slots, 160, strip.AnchorOf, 4)); // before F, unchanged
    }

    [Fact]
    public void CompositeSlot_IsABoundaryRegion_ButNeverResolvesToItsSuppressedRightMember()
    {
        var a = Tab("A"); var b = Tab("B");
        var strip = new Strip { TabsList = { a, b } };
        var slots = new[] { Slot(100, new SplitCompositeViewModel(a, b)) };

        Assert.Equal(0, TabStripDragProjection.ResolveDropTargetIndex(slots, 90, strip.AnchorOf, 2));
        Assert.Equal(2, TabStripDragProjection.ResolveDropTargetIndex(slots, 110, strip.AnchorOf, 2));
    }

    [Fact]
    public void UnresolvableAnchor_FailsClosed()
    {
        var slots = new[] { Slot(100, "not-a-strip-item") };
        Assert.Null(TabStripDragProjection.ResolveDropTargetIndex(slots, 10, _ => null, 1));
        Assert.Null(TabStripDragProjection.ResolveDropTargetIndex(slots, 10, item => -1, 1));
    }

    [Fact]
    public void EmptySlots_ReturnNull()
    {
        Assert.Null(TabStripDragProjection.ResolveDropTargetIndex(
            Array.Empty<TabStripDragProjection.DragSlot>(), 100, _ => 0, 3));
    }

    [Fact]
    public void ReorderShapeChangesNoCounts_SoSnapshotsSurviveAndH2CannotReform()
    {
        // The view resnapshots only when a collection COUNT changes. Model the
        // exact ObservableCollection.Move(old,new) primitive ReorderTabs uses
        // (RemoveAt then Insert into the reduced list) and prove both counts
        // stay invariant even while a pair keeps DisplayTabs shorter: a reorder
        // can therefore never reach the resnapshot path, so the H2 oscillation
        // feedback loop (recompute-after-move → flip-back) cannot re-form.
        var tabs = new List<TabViewModel> { Tab("A"), Tab("B"), Tab("C"), Tab("D") };
        var display = new List<object> { "composite", tabs[2], tabs[3] }; // dormant pair projection

        TabViewModel moved = tabs[0];
        tabs.RemoveAt(0);
        tabs.Insert(3, moved);
        object movedDisplay = display[0];
        display.RemoveAt(0);
        display.Insert(display.Count, movedDisplay);

        Assert.Equal(4, tabs.Count);
        Assert.Equal(3, display.Count);
    }
}
