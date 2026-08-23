using System;
using System.Collections.Generic;

namespace TabDock.Services;

/// <summary>
/// Pure mapping from a snapshotted tab-strip drag geometry to an authoritative
/// <see cref="ViewModels.GroupViewModel"/> Tabs insertion index.
///
/// The strip ListBox is bound to DisplayTabs, the presentation projection: with
/// a split pair defined (presented OR dormant) the LEFT member's slot renders
/// as one composite item and the RIGHT member's ordinary item is suppressed, so
/// visible slots and authoritative Tabs indexes diverge. Drop targeting must
/// therefore resolve boundaries through slot IDENTITY, never positional
/// arithmetic: the boundary before visible slot k means "immediately before
/// that slot's representative member in authoritative Tabs order" — the Left
/// member for a composite slot, the tab itself otherwise.
///
/// With no composite present every anchor equals its own display index, so the
/// mapping reduces exactly to the historical midpoint formula and no-split
/// behavior is byte-preserved. Anchors are resolved LIVE from stored item
/// references at drop time, so intermediate in-drag reorders keep the mapping
/// correct WITHOUT resnapshotting geometry (the H2 anti-oscillation rule).
/// </summary>
internal static class TabStripDragProjection
{
    /// <summary>One visible strip slot: snapshotted midpoint plus the strip item it rendered.</summary>
    internal readonly struct DragSlot
    {
        public DragSlot(double midpointX, object item)
        {
            MidpointX = midpointX;
            Item = item;
        }

        public double MidpointX { get; }

        /// <summary>
        /// The ListBoxItem DataContext: a <see cref="ViewModels.TabViewModel"/>
        /// for an ordinary member, a <see cref="ViewModels.SplitCompositeViewModel"/>
        /// for the pair slot.</summary>
        public object Item { get; }
    }

    /// <summary>
    /// Resolves the authoritative Tabs index a drop at <paramref name="pointerX"/>
    /// means, given slots ordered by ascending midpoint. The first slot whose
    /// midpoint exceeds the pointer defines the insertion boundary; past the
    /// last slot resolves to <paramref name="tabsCount"/> (the caller's move
    /// clamps to the end). Returns null when nothing can be resolved — an empty
    /// snapshot or an unresolvable anchor (fail-closed: no reorder this move).
    /// </summary>
    internal static int? ResolveDropTargetIndex(
        IReadOnlyList<DragSlot> slots,
        double pointerX,
        Func<object, int?> resolveAnchor,
        int tabsCount)
    {
        if (slots == null || slots.Count == 0 || resolveAnchor == null)
            return null;

        foreach (DragSlot slot in slots)
        {
            if (pointerX < slot.MidpointX)
            {
                int? anchor = resolveAnchor(slot.Item);
                return anchor >= 0 ? anchor : null;
            }
        }
        return tabsCount;
    }
}
