using System;
using System.Collections.Generic;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Wave 3C lifecycle coverage for the pane-refusal STATE owner. The decision
/// invariant itself (visible + same rect ⇒ suppress; hidden ⇒ NEVER) is owned
/// by <see cref="PaneContainmentPolicy"/> and covered in
/// PaneContainmentPolicyTests; these tests pin what the storage owner adds:
///
///  - reference-keyed identity: a recycled HWND VALUE can never inherit a
///    prior occupant's refusal (two distinct CapturedWindows sharing one HWND
///    value do not collide);
///  - mark dedupe (one diagnostic per distinct refusal);
///  - clear-guest / clear-all semantics used by every invalidation boundary;
///  - visibility is caller-sampled and handed to the policy at decision time,
///    so a recorded refusal alone can never suppress anything.
/// </summary>
public class PaneContainmentCoordinatorTests
{
    private static NativeMethods.RECT Rect(int l, int t, int r, int b)
        => new() { left = l, top = t, right = r, bottom = b };

    private static CapturedWindow Guest(long hwndValue, long token)
        => new()
        {
            Hwnd = new IntPtr(hwndValue),
            ProcessId = 10,
            WindowThreadId = 20,
            WindowIdentityToken = token,
            ExePath = "C:\\app.exe",
            OriginalClassName = "Pig",
        };

    private static readonly NativeMethods.RECT Pane = Rect(0, 0, 400, 600);

    [Fact]
    public void MarkThenQuery_SuppressesVisibleGuestAtSameRect_WithinEpsilon()
    {
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow guest = Guest(1001, token: 1);
        coordinator.MarkRefusingPane(guest, Pane);

        Assert.True(coordinator.HasRefusal(guest));
        Assert.True(coordinator.ShouldSuppressRepositioning(guest, guestCurrentlyVisible: true, requestedRect: Rect(0, 0, 400, 600)));
        Assert.True(coordinator.ShouldSuppressRepositioning(guest, guestCurrentlyVisible: true, requestedRect: Rect(1, 0, 401, 600))); // epsilon
        Assert.False(coordinator.ShouldSuppressRepositioning(guest, guestCurrentlyVisible: true, requestedRect: Rect(0, 0, 700, 600))); // changed
    }

    [Fact]
    public void HiddenGuest_IsNeverSuppressed_EvenAgainstIdenticalRecordedRefusal()
    {
        // THE minimize/restore guarantee (3591ee3): the restore path re-shows.
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow guest = Guest(1001, token: 1);
        coordinator.MarkRefusingPane(guest, Pane);

        Assert.False(coordinator.ShouldSuppressRepositioning(guest, guestCurrentlyVisible: false, requestedRect: Pane));
    }

    [Fact]
    public void ReferenceIdentity_TwoGuestsSharingAnHwndValue_NeverCollide()
    {
        // A released guest's HWND value is recycled for an unrelated window
        // that TabDock then captures: the NEW member must not inherit the OLD
        // occupant's refusal (the raw-HWND-keyed dictionary could).
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow oldOccupant = Guest(1001, token: 111);
        CapturedWindow recycledNewcomer = Guest(1001, token: 222); // same HWND VALUE

        coordinator.MarkRefusingPane(oldOccupant, Pane);

        Assert.True(coordinator.HasRefusal(oldOccupant));
        Assert.False(coordinator.HasRefusal(recycledNewcomer));
        Assert.False(coordinator.ShouldSuppressRepositioning(recycledNewcomer, guestCurrentlyVisible: true, requestedRect: Pane));

        coordinator.ClearRefusingPane(oldOccupant);
        Assert.False(coordinator.HasRefusal(oldOccupant));
    }

    [Fact]
    public void ClearRefusingPane_RemovesOnlyThatGuest()
    {
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow left = Guest(2001, token: 1);
        CapturedWindow right = Guest(2002, token: 2);
        coordinator.MarkRefusingPane(left, Rect(0, 0, 400, 600));
        coordinator.MarkRefusingPane(right, Rect(400, 0, 800, 600));

        coordinator.ClearRefusingPane(left);

        Assert.False(coordinator.HasRefusal(left));
        Assert.True(coordinator.HasRefusal(right));
    }

    [Fact]
    public void InvalidateAll_ClearsEveryBoundaryCategory_AtOnce()
    {
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow a = Guest(3001, token: 1);
        CapturedWindow b = Guest(3002, token: 2);
        coordinator.MarkRefusingPane(a, Pane);
        coordinator.MarkRefusingPane(b, Pane);

        // One call covers all classified events: geometry change, min-refresh,
        // active-guest change, split enter/suspend/resume/exit, member removal,
        // move/size end, DPI change, topology change, teardown.
        coordinator.InvalidateAll();

        Assert.False(coordinator.HasRefusal(a));
        Assert.False(coordinator.HasRefusal(b));
        Assert.False(coordinator.ShouldSuppressRepositioning(a, guestCurrentlyVisible: true, requestedRect: Pane));
    }

    [Fact]
    public void MarkDedupe_ExactDuplicateLogsOnce_ChangedOrEpsilonShiftedRectLogsAgain()
    {
        var diagnostics = new List<string>();
        var coordinator = new PaneContainmentCoordinator(diagnostics.Add);
        CapturedWindow guest = Guest(4001, token: 1);

        coordinator.MarkRefusingPane(guest, Pane);
        coordinator.MarkRefusingPane(guest, Rect(0, 0, 400, 600)); // exact duplicate: no new diagnostic

        string message = Assert.Single(diagnostics);
        Assert.Contains("SHEPHERD[size-constraint]", message);
        Assert.Contains("refused pane", message);

        // Dedupe is EXACT-rect (PaneContainmentPolicy.IsExactSameRect), the
        // same rule the view used before Wave 3C: one persistent non-compliance
        // produces one diagnostic. A rect shift — even within the glue epsilon
        // — is a genuinely different refusal and is recorded again.
        coordinator.MarkRefusingPane(guest, Rect(1, 0, 401, 600));
        Assert.Equal(2, diagnostics.Count);

        coordinator.MarkRefusingPane(guest, Rect(0, 0, 900, 600));
        Assert.Equal(3, diagnostics.Count);
    }

    [Fact]
    public void SuppressionDecision_AlwaysMatchesDirectPolicyEvaluation()
    {
        // Cross-check: the owner is pure storage + policy delegation; it never
        // invents its own suppression rule.
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow guest = Guest(5001, token: 1);
        coordinator.MarkRefusingPane(guest, Pane);

        foreach (bool visible in new[] { true, false })
        {
            foreach (var requested in new[] { Pane, Rect(1, 1, 401, 601), Rect(2, 0, 402, 600) })
            {
                bool viaOwner = coordinator.ShouldSuppressRepositioning(guest, visible, requested);
                bool direct = PaneContainmentPolicy.MatchesWithinEpsilon(Pane, requested)
                    && PaneContainmentPolicy.ShouldSuppressRepositioning(visible, Pane, requested);
                Assert.Equal(direct, viaOwner);
            }
        }
    }

    [Fact]
    public void NoRefusalRecorded_SuppressionNeverApplies()
    {
        var coordinator = new PaneContainmentCoordinator();
        CapturedWindow guest = Guest(6001, token: 1);
        Assert.False(coordinator.HasRefusal(guest));
        Assert.False(coordinator.ShouldSuppressRepositioning(guest, guestCurrentlyVisible: true, requestedRect: Pane));
    }
}
