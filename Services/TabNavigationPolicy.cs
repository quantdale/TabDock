using System;
using System.Collections.Generic;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Pure decision authority for the container's Ctrl+Tab / Ctrl+Shift+Tab tab
/// navigation. Extracted from <c>ContainerWindow_PreviewKeyDown</c> so the
/// actual navigation decision is deterministically testable without a WPF
/// fixture.
/// </summary>
/// <remarks>
/// The decision NEVER produces a presentation-space index: it returns the
/// authoritative target <see cref="CapturedWindow"/> itself, which the view
/// resolves back to its tab and applies through the canonical activation
/// paths (<c>SetActiveTab</c> / <see cref="SplitPresentationController"/>
/// member focus). This structurally forecloses reintroducing a raw Tabs-space
/// index write against the DisplayTabs-bound ListBox — the split composite
/// makes those two index spaces diverge for as long as it exists (see
/// GroupViewModelDisplayTabsTests), so any decision expressed as an index is
/// wrong by construction.
///
/// The rules reproduce the pre-extraction handler exactly:
/// - fewer than two tabs → NotNavigable;
/// - presented pair → cycle between the two members only (the partner of the
///   focused member; when no member is focused this anchors on LEFT);
/// - otherwise (no pair or dormant) → ordinary cycling over the full Tabs
///   order with wraparound at both ends, anchoring at the first tab when no
///   active tab exists.
/// Dormant-member selection intentionally flows through ActivateTab: resuming
/// the pair is owned by the active-tab change path (SyncShepherdActiveWindow),
/// not by keyboard navigation.
/// </remarks>
public static class TabNavigationPolicy
{
    public enum NavigationKind
    {
        /// <summary>Fewer than two tabs — the view must not handle the key.</summary>
        NotNavigable,

        /// <summary>Focus the partner member of the presented split pair.</summary>
        FocusSplitMember,

        /// <summary>Make the target tab the ordinary active tab.</summary>
        ActivateTab,
    }

    public readonly record struct Decision(NavigationKind Kind, CapturedWindow? Target);

    public static Decision ResolveCtrlTab(
        IReadOnlyList<CapturedWindow> tabsInOrder,
        CapturedWindow? activeTab,
        bool backward,
        bool splitPresented,
        CapturedWindow? splitLeft,
        CapturedWindow? splitRight,
        CapturedWindow? splitForeground)
    {
        int count = tabsInOrder.Count;
        if (count <= 1)
            return new Decision(NavigationKind.NotNavigable, null);

        if (splitPresented)
        {
            // The presented pair is the selected unit: cycle between its two
            // members only. Partner selection is identity-based, mirroring the
            // original lookup: anchor on LEFT when nothing is focused.
            CapturedWindow? other = ReferenceEquals(splitForeground, splitLeft)
                ? splitRight
                : splitLeft;
            return new Decision(NavigationKind.FocusSplitMember, other);
        }

        int current = activeTab != null ? IndexOf(tabsInOrder, activeTab) : 0;
        if (current < 0)
            current = 0;

        int next = backward
            ? (current - 1 + count) % count
            : (current + 1) % count;

        return new Decision(NavigationKind.ActivateTab, tabsInOrder[next]);
    }

    private static int IndexOf(IReadOnlyList<CapturedWindow> list, CapturedWindow item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return i;
        }
        return -1;
    }
}
