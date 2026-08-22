using System;
using System.Collections.Generic;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Owns the pane-containment REFUSAL history (Wave 3C): which captured guest
/// last refused which pane rect because its native minimum exceeds the pane
/// (e.g. a browser sidebar opened). The DECISION stays in the pure
/// <see cref="PaneContainmentPolicy"/>; this class is only the stateful store
/// feeding it, extracted from <c>ContainerWindow</c> so the presentation
/// layer holds no second copy of the concept.
///
/// Identity: entries are keyed by CapturedWindow REFERENCE, never by a raw
/// HWND value — a recycled HWND value can therefore never inherit a prior
/// occupant's refusal. Safety envelope: entries exist only for guests that
/// were recently PRESENTED (marking happens only inside layout passes over the
/// current active guest / split members), and every visible-set, geometry,
/// DPI, or topology boundary clears the table via <see cref="InvalidateAll"/>,
/// so strong references here never outlive a presented relationship.
///
/// The hidden-guest invariant (3591ee3) is preserved by construction: this
/// class stores rects only; visibility is sampled by the CALLER at decision
/// time and handed to the policy. A hidden guest is NEVER suppressed even
/// against an identical recorded refusal, so container minimize/restore always
/// re-shows.
/// </summary>
public sealed class PaneContainmentCoordinator
{
    private readonly Dictionary<CapturedWindow, NativeMethods.RECT> _refusedPaneByGuest = new();
    private readonly Action<string>? _log;

    public PaneContainmentCoordinator(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// Records that <paramref name="guest"/> refused <paramref name="rect"/>.
    /// Repeated identical refusals are deduped so one persistent
    /// non-compliance produces one diagnostic, not a stream.
    /// </summary>
    public void MarkRefusingPane(CapturedWindow guest, NativeMethods.RECT rect)
    {
        if (_refusedPaneByGuest.TryGetValue(guest, out NativeMethods.RECT prior)
            && PaneContainmentPolicy.IsExactSameRect(prior, rect))
            return; // already recorded this exact refusal
        _refusedPaneByGuest[guest] = rect;
        _log?.Invoke($"SHEPHERD[size-constraint] guest=0x{guest.Hwnd.ToInt64():X} refused pane {rect.left},{rect.top},{rect.Width}x{rect.Height}; guest cannot fit the assigned pane (native minimum).");
    }

    /// <summary>Clears the refusal record for <paramref name="guest"/> (re-glue succeeded or rect changed).</summary>
    public void ClearRefusingPane(CapturedWindow guest)
        => _refusedPaneByGuest.Remove(guest);

    /// <summary>
    /// Clears ALL refusals. The invalidation categories driven by callers:
    /// container geometry changed (WM_EXITSIZEMOVE), minimum-constraint
    /// periodic refresh, active guest changed, split entered / suspended /
    /// resumed / exited, split member removed, guest move/size ended (+ its
    /// render-priority final pass), DPI changed (WM_DPICHANGED), display
    /// topology changed (WM_DISPLAYCHANGE), and container teardown.
    /// </summary>
    public void InvalidateAll()
        => _refusedPaneByGuest.Clear();

    /// <summary>
    /// True when <paramref name="guest"/>'s recorded refusal covers
    /// <paramref name="requestedRect"/> within the glue epsilon AND the
    /// caller-sampled current visibility allows suppression per
    /// <see cref="PaneContainmentPolicy.ShouldSuppressRepositioning"/>.
    /// </summary>
    public bool ShouldSuppressRepositioning(
        CapturedWindow guest,
        bool guestCurrentlyVisible,
        NativeMethods.RECT requestedRect)
        => _refusedPaneByGuest.TryGetValue(guest, out NativeMethods.RECT refused)
            && PaneContainmentPolicy.ShouldSuppressRepositioning(guestCurrentlyVisible, refused, requestedRect);

    /// <summary>Deterministic test/diagnostic probe: is a refusal recorded for this exact guest reference?</summary>
    public bool HasRefusal(CapturedWindow guest) => _refusedPaneByGuest.ContainsKey(guest);
}
