using System;

namespace TabDock.Models;

/// <summary>
/// The presentation mode of a split relationship. A relationship can remain
/// defined while its two members are not the visible set.
/// </summary>
public enum SplitPresentationMode
{
    None,
    Pair,
    SingleGuest,
}

/// <summary>Outcome of the guarded native work for a presentation transition.</summary>
public enum SplitNativeTransitionOutcome
{
    Succeeded,
    RecoveryPending,
    IdentityMismatch,
    ShowFailed,
}

/// <summary>
/// Small, native-free state contract for the split relationship. The strings
/// stand for stable captured-member identities; production callers use HWND/
/// CapturedWindow references around this policy, while deterministic tests can
/// exercise the state machine without creating windows or sending input.
/// </summary>
public readonly record struct SplitPresentationState(
    string? Left,
    string? Right,
    bool PairPresented,
    string? ActiveGuest,
    long Generation)
{
    public bool RelationshipDefined => Left is not null && Right is not null;

    public SplitPresentationMode Mode => !RelationshipDefined
        ? SplitPresentationMode.None
        : PairPresented
            ? SplitPresentationMode.Pair
            : SplitPresentationMode.SingleGuest;

    public bool IsMember(string? identity)
        => identity is not null && (string.Equals(identity, Left, StringComparison.Ordinal)
            || string.Equals(identity, Right, StringComparison.Ordinal));
}

/// <summary>
/// Pure transition policy shared by the application and its deterministic
/// qualification contract. It only decides logical authority; callers remain
/// responsible for journal-safe native execution and must apply the desired
/// state only after that execution succeeds.
/// </summary>
public static class SplitPresentationPolicy
{
    public static SplitPresentationState NoPair(string? activeGuest = null, long generation = 0)
        => new(null, null, false, activeGuest, generation);

    public static SplitPresentationState DefinePair(
        string left,
        string right,
        string? focusedMember = null,
        long generation = 0)
    {
        if (string.IsNullOrWhiteSpace(left))
            throw new ArgumentException("A split pair needs a left member.", nameof(left));
        if (string.IsNullOrWhiteSpace(right))
            throw new ArgumentException("A split pair needs a right member.", nameof(right));
        if (string.Equals(left, right, StringComparison.Ordinal))
            throw new ArgumentException("A split pair needs two distinct members.");

        string focus = string.Equals(focusedMember, right, StringComparison.Ordinal) ? right : left;
        return new SplitPresentationState(left, right, true, focus, generation + 1);
    }

    public static SplitPresentationState SelectNonMember(
        SplitPresentationState current,
        string guest)
    {
        if (string.IsNullOrWhiteSpace(guest) || !current.RelationshipDefined || current.IsMember(guest))
            return current;
        return current with { PairPresented = false, ActiveGuest = guest, Generation = current.Generation + 1 };
    }

    /// <summary>
    /// Presents <paramref name="guest"/> as the single active guest in the
    /// standalone (no relationship) or dormant modes. This is the ordinary
    /// tab-switch authority: the same NoPair/dormant shape either way, with
    /// the presentation epoch advanced so a queued settle can never alias an
    /// older single-guest world. A PRESENTED pair is never changed here —
    /// suspension goes through <see cref="SelectNonMember"/> after its guarded
    /// member hides succeed — and a null/blank guest clears the active guest
    /// (teardown / empty group).
    /// </summary>
    public static SplitPresentationState SelectGuest(SplitPresentationState current, string? guest)
    {
        if (current.RelationshipDefined && current.PairPresented)
            return current; // fail-closed: presented pairs suspend via SelectNonMember
        return current with { PairPresented = false, ActiveGuest = guest, Generation = current.Generation + 1 };
    }

    public static SplitPresentationState SelectMember(
        SplitPresentationState current,
        string member)
    {
        if (!current.RelationshipDefined || !current.IsMember(member))
            return current;
        return current with { PairPresented = true, ActiveGuest = member, Generation = current.Generation + 1 };
    }

    /// <summary>
    /// Explicit exit removes only the relationship. A dormant non-member stays
    /// active; an active presented pair member remains the ordinary survivor.
    /// <paramref name="preferredSurvivor"/> lets the caller honor an explicit
    /// "exit keeping THIS member active" request: when it names one of the two
    /// members it wins over the current active guest; otherwise the active
    /// guest (dormant non-member included) is kept.
    /// </summary>
    public static SplitPresentationState ExplicitExit(
        SplitPresentationState current,
        string? preferredSurvivor = null)
    {
        if (!current.RelationshipDefined)
            return current;
        string? survivor = preferredSurvivor != null && current.IsMember(preferredSurvivor)
            ? preferredSurvivor
            : current.ActiveGuest;
        return NoPair(survivor, current.Generation + 1);
    }

    /// <summary>
    /// Switches the focused member of a defined pair WITHOUT ending or changing
    /// the presentation mode and without bumping the generation: a z-top focus
    /// switch inside a live pair is not a presentation-world transition (the
    /// settle callback re-reads the foreground at fire time).
    /// </summary>
    public static SplitPresentationState FocusMember(SplitPresentationState current, string member)
    {
        if (!current.RelationshipDefined
            || !current.IsMember(member)
            || string.Equals(current.ActiveGuest, member, StringComparison.Ordinal))
            return current;
        return current with { ActiveGuest = member };
    }

    /// <summary>
    /// Structural invalidation always removes the relationship. The current
    /// non-member remains active when dormant; if the removed member was active,
    /// the surviving member is the deterministic ordinary survivor.
    /// </summary>
    public static SplitPresentationState RemoveMember(
        SplitPresentationState current,
        string removed)
    {
        if (!current.RelationshipDefined || !current.IsMember(removed))
            return current;

        string? survivor = string.Equals(current.ActiveGuest, removed, StringComparison.Ordinal)
            ? (string.Equals(current.Left, removed, StringComparison.Ordinal) ? current.Right : current.Left)
            : current.ActiveGuest;
        return NoPair(survivor, current.Generation + 1);
    }

    /// <summary>
    /// User-requested reconfiguration replaces the old relationship only after
    /// its native transition has succeeded.
    /// </summary>
    public static SplitPresentationState Reconfigure(
        SplitPresentationState current,
        string left,
        string right)
        => DefinePair(left, right, left, current.Generation);

    /// <summary>Recovery uncertainty leaves the old authoritative presentation intact.</summary>
    public static SplitPresentationState ResolveNativeTransition(
        SplitPresentationState authoritative,
        SplitPresentationState desired,
        SplitNativeTransitionOutcome outcome)
        => outcome == SplitNativeTransitionOutcome.Succeeded ? desired : authoritative;

    /// <summary>
    /// A queued settle is valid only for the currently presented generation.
    /// Dormant relationships must never be resurrected by a stale callback.
    /// </summary>
    public static bool IsCurrentSettle(
        SplitPresentationState current,
        long queuedGeneration)
        => current.RelationshipDefined
            && current.PairPresented
            && current.Generation == queuedGeneration;
}
