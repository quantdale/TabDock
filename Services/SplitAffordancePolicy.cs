using System;
using System.Collections.Generic;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Commands exposed by the persistent split affordance.</summary>
public enum SplitAffordanceActionKind
{
    CreatePair,
    FocusLeft,
    FocusRight,
    ResumeLeft,
    ResumeRight,
    EndRelationship,
}

/// <summary>
/// A presentation-only split command. It carries captured-window references,
/// never tab-strip indexes, so a menu cannot confuse Tabs with DisplayTabs.
/// </summary>
public readonly record struct SplitAffordanceAction(
    SplitAffordanceActionKind Kind,
    CapturedWindow? Source,
    CapturedWindow? Target);

/// <summary>Read-only menu projection for the always-visible split control.</summary>
public readonly record struct SplitAffordanceMenuState(
    bool IsEnabled,
    string ToolTip,
    IReadOnlyList<SplitAffordanceAction> Actions);

/// <summary>
/// Pure projection and stale-action gate for the split affordance. It owns no
/// relationship state and performs no mutation; SplitPresentationController,
/// SplitPresentationPolicy, and ContainerWindow's existing canonical mutation
/// paths remain the only split authorities.
/// </summary>
public static class SplitAffordancePolicy
{
    public static SplitAffordanceMenuState Build(
        IReadOnlyList<CapturedWindow> tabs,
        CapturedWindow? active,
        CapturedWindow? left,
        CapturedWindow? right,
        bool presented,
        Func<CapturedWindow, bool>? isEligible = null)
    {
        isEligible ??= static _ => true;

        if (tabs.Count < 2)
        {
            return new SplitAffordanceMenuState(
                IsEnabled: false,
                ToolTip: "Split requires another captured tab",
                Actions: Array.Empty<SplitAffordanceAction>());
        }

        bool relationshipDefined = left != null && right != null;
        if (relationshipDefined)
        {
            if (!ContainsEligible(tabs, left!, isEligible)
                || !ContainsEligible(tabs, right!, isEligible)
                || ReferenceEquals(left, right))
            {
                return new SplitAffordanceMenuState(
                    IsEnabled: false,
                    ToolTip: "Split state is no longer current",
                    Actions: Array.Empty<SplitAffordanceAction>());
            }

            var actions = new List<SplitAffordanceAction>
            {
                new(
                    presented ? SplitAffordanceActionKind.FocusLeft : SplitAffordanceActionKind.ResumeLeft,
                    Source: null,
                    Target: left),
                new(
                    presented ? SplitAffordanceActionKind.FocusRight : SplitAffordanceActionKind.ResumeRight,
                    Source: null,
                    Target: right),
                new(SplitAffordanceActionKind.EndRelationship, Source: null, Target: null),
            };
            return new SplitAffordanceMenuState(true, "Open split actions", actions);
        }

        CapturedWindow? source = ContainsEligible(tabs, active, isEligible)
            ? active
            : FirstEligible(tabs, isEligible);
        if (source == null)
        {
            return new SplitAffordanceMenuState(
                IsEnabled: false,
                ToolTip: "No current tab is available for splitting",
                Actions: Array.Empty<SplitAffordanceAction>());
        }

        var partnerActions = new List<SplitAffordanceAction>();
        foreach (CapturedWindow candidate in tabs)
        {
            if (!ReferenceEquals(candidate, source) && isEligible(candidate))
                partnerActions.Add(new(SplitAffordanceActionKind.CreatePair, source, candidate));
        }

        return partnerActions.Count == 0
            ? new SplitAffordanceMenuState(
                IsEnabled: false,
                ToolTip: "No eligible partner tab is available",
                Actions: partnerActions)
            : new SplitAffordanceMenuState(true, "Choose a tab to split with", partnerActions);
    }

    public static bool IsActionCurrent(
        SplitAffordanceAction action,
        IReadOnlyList<CapturedWindow> tabs,
        CapturedWindow? left,
        CapturedWindow? right,
        bool presented,
        Func<CapturedWindow, bool>? isEligible = null)
    {
        isEligible ??= static _ => true;

        return action.Kind switch
        {
            SplitAffordanceActionKind.CreatePair
                => left == null && right == null
                    && action.Source != null
                    && action.Target != null
                    && !ReferenceEquals(action.Source, action.Target)
                    && ContainsEligible(tabs, action.Source, isEligible)
                    && ContainsEligible(tabs, action.Target, isEligible),
            SplitAffordanceActionKind.FocusLeft
                => presented && IsCurrentMember(action.Target, left, right, tabs, isEligible),
            SplitAffordanceActionKind.FocusRight
                => presented && IsCurrentMember(action.Target, right, left, tabs, isEligible),
            SplitAffordanceActionKind.ResumeLeft
                => !presented && IsCurrentMember(action.Target, left, right, tabs, isEligible),
            SplitAffordanceActionKind.ResumeRight
                => !presented && IsCurrentMember(action.Target, right, left, tabs, isEligible),
            SplitAffordanceActionKind.EndRelationship
                => left != null && right != null
                    && !ReferenceEquals(left, right)
                    && ContainsEligible(tabs, left, isEligible)
                    && ContainsEligible(tabs, right, isEligible),
            _ => false,
        };
    }

    private static CapturedWindow? FirstEligible(
        IReadOnlyList<CapturedWindow> tabs,
        Func<CapturedWindow, bool> isEligible)
    {
        foreach (CapturedWindow tab in tabs)
        {
            if (isEligible(tab))
                return tab;
        }

        return null;
    }

    private static bool ContainsEligible(
        IReadOnlyList<CapturedWindow> tabs,
        CapturedWindow? target,
        Func<CapturedWindow, bool> isEligible)
    {
        if (target == null || !isEligible(target))
            return false;

        foreach (CapturedWindow tab in tabs)
        {
            if (ReferenceEquals(tab, target))
                return true;
        }

        return false;
    }

    private static bool IsCurrentMember(
        CapturedWindow? target,
        CapturedWindow? expected,
        CapturedWindow? other,
        IReadOnlyList<CapturedWindow> tabs,
        Func<CapturedWindow, bool> isEligible)
        => target != null
            && expected != null
            && !ReferenceEquals(expected, other)
            && ReferenceEquals(target, expected)
            && ContainsEligible(tabs, target, isEligible)
            && ContainsEligible(tabs, other, isEligible);
}
