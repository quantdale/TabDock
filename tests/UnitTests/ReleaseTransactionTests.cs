using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former WindowReleaseSelfTest (Wave 4): deterministic
/// coverage for the release/hide transaction's identity boundary. The counting
/// fakes prove that an unverifiable or mismatched strong probe never mutates
/// the possibly-wrong HWND, while journal retention/rejection stays exact.
/// </summary>
public class ReleaseTransactionTests
{
    [Fact]
    public void Release_ValidStrongIdentity_ReleasesAndClearsJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.Released, result);
        Assert.True(fixture.Native.MutationCount > 0);
        Assert.Empty(fixture.ReadEntries());
        Assert.Equal(IntPtr.Zero, fixture.Identity.CaptureToken);
    }

    [Fact]
    public void Hide_ValidIdentity_UsesJournalBoundaryWithoutTransitionMutation()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        WindowHideOutcome result = fixture.Service.Hide(fixture.Captured);
        Assert.Equal(WindowHideOutcome.Hidden, result);
        Assert.Equal(1, fixture.Native.ShowWindowCount);
        Assert.Equal(0, fixture.Native.TransitionCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Hide_JournalCommitGenerationMismatch_NeverCallsShowWindow()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, identity) =>
            {
                if (stage == "JournalHide.committed")
                    identity.ReplaceGeneration();
            });
        WindowHideOutcome result = fixture.Service.Hide(fixture.Captured);
        Assert.Equal(WindowHideOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.ShowWindowCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Hide_BoundaryGenerationMismatch_NeverCallsShowWindow()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, identity) =>
            {
                if (stage == "hide-after-journal.before")
                    identity.ReplaceGeneration();
            });
        WindowHideOutcome result = fixture.Service.Hide(fixture.Captured);
        Assert.Equal(WindowHideOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.ShowWindowCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Hide_BoundaryUnverifiable_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, identity) =>
            {
                if (stage == "hide-after-journal.before")
                    identity.ThrowOnClassProbe = true;
            });
        WindowHideOutcome result = fixture.Service.Hide(fixture.Captured);
        Assert.Equal(WindowHideOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.ShowWindowCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Release_DefinitePidMismatch_DoesNotMutate()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.Identity = new WindowProcessIdentity(99, fixture.Captured.WindowThreadId);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Release_DefiniteTokenMismatch_DoesNotMutate()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.CaptureToken = new IntPtr(2002);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Release_DefiniteProcessStartMismatch_DoesNotMutate()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ProcessStartTicks = 202;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Release_UnavailableProcessStart_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Release_ExecutableProbeFailure_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ThrowOnExecutableProbe = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Release_NativeVerificationException_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ThrowOnClassProbe = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Release_DefiniteExecutableMismatch_DoesNotMutate()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ExePath = "replacement.exe";
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Release_SameHwndChangedMetadata_CannotClearReplacementEntry()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        HiddenWindowEntry replacement = new()
        {
            Hwnd = fixture.Entry.Hwnd,
            Pid = fixture.Entry.Pid,
            WindowThreadId = fixture.Entry.WindowThreadId,
            WindowIdentityToken = fixture.Entry.WindowIdentityToken,
            ExePath = "replacement.exe",
            ClassName = fixture.Entry.ClassName,
            ProcessStartTimeUtcTicks = fixture.Entry.ProcessStartTimeUtcTicks,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };
        fixture.WriteEntries(replacement);
        fixture.Identity.ExePath = "replacement.exe";
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        List<HiddenWindowEntry> remaining = fixture.ReadEntries();
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        HiddenWindowEntry remainingEntry = Assert.Single(remaining);
        Assert.Equal("replacement.exe", remainingEntry.ExePath);
    }

    [Fact]
    public void Release_OldSameHwndJournal_CannotClearReplacementEntry()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        HiddenWindowEntry replacement = new()
        {
            Hwnd = fixture.Entry.Hwnd,
            Pid = fixture.Entry.Pid,
            WindowThreadId = fixture.Entry.WindowThreadId,
            WindowIdentityToken = 2002,
            ExePath = "replacement.exe",
            ClassName = fixture.Entry.ClassName,
            ProcessStartTimeUtcTicks = 202,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };
        fixture.WriteEntries(fixture.Entry, replacement);
        fixture.Identity.CaptureToken = new IntPtr(2002);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        List<HiddenWindowEntry> remaining = fixture.ReadEntries();
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        HiddenWindowEntry remainingEntry = Assert.Single(remaining);
        Assert.Equal(2002, remainingEntry.WindowIdentityToken);
        Assert.Equal("replacement.exe", remainingEntry.ExePath);
    }

    [Fact]
    public void Release_TokenRemovalFailure_FailsClosedForFutureCapture()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.FailTokenRemoval = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Single(fixture.ReadEntries());
        Assert.Equal(new IntPtr(fixture.Captured.WindowIdentityToken), fixture.Identity.CaptureToken);
        Assert.False(WindowIdentityGate.IsCaptureTokenAvailable(fixture.Captured.Hwnd, fixture.Identity));
    }

    [Fact]
    public void Release_UnverifiableHiddenGuest_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Captured.OriginallyVisible = false;
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: false);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void Release_UnverifiableVisibleGuest_PreservesJournal()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: true);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
        Assert.Equal(0, fixture.Native.MutationCount);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void IntentionalHide_JournalClearFailsAfterOwnTokenRemoval_RecoveryPendingAndGuestReshow()
    {
        // SG-1 regression: once THIS transaction has removed the capture token,
        // a failed JournalClear must NOT be misread as "target gone or
        // recycled" at the finalization boundaries. The live token is absent
        // by our own hand; identity strength there comes from HWND/PID/thread/
        // class. The only correct outcome is RecoveryPending with the guest
        // re-shown and ownership retained.
        FileStream? journalLock = null;
        ReleaseTestFixture? holder = null;
        try
        {
            ReleaseTestFixture fixture = ReleaseTestFixture.Create(sequencingHook: (stage, _) =>
            {
                if (stage == "release-intentional-hide-before-token-removal.before" && journalLock == null)
                {
                    // Hold the journal file exclusively so the marker write
                    // (already durable) succeeds but the later clear's atomic
                    // move onto the locked destination throws.
                    journalLock = new FileStream(
                        holder!.JournalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
            });
            holder = fixture;
            using (fixture)
            {
                WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: false);

                // Release the exclusive hold before reading the journal back.
                journalLock.Dispose();
                journalLock = null;

                Assert.Equal(WindowReleaseOutcome.RecoveryPending, result);
                // SW_HIDE (the intentional hide) plus the recovery re-show.
                Assert.Equal(2, fixture.Native.ShowWindowCount);
                Assert.Equal(IntPtr.Zero, fixture.Identity.CaptureToken);
                HiddenWindowEntry retained = Assert.Single(fixture.ReadEntries());
                Assert.True(retained.DoNotRescue);
            }
        }
        finally
        {
            journalLock?.Dispose();
        }
    }

    [Fact]
    public void IntentionalHide_GenuineRecycleAtFinalization_StillStaleNotPending()
    {
        // The SG-1 fix must not weaken recycle protection: if the HWND really
        // dies between hide and finalization, the pre-token boundary still
        // yields Mismatch and the release stays TargetGoneOrRecycled.
        FileStream? journalLock = null;
        ReleaseTestFixture? holder = null;
        try
        {
            ReleaseTestFixture fixture = ReleaseTestFixture.Create(sequencingHook: (stage, identity) =>
            {
                if (stage == "release-intentional-hide-before-token-removal.before" && journalLock == null)
                {
                    journalLock = new FileStream(
                        holder!.JournalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                if (stage == "release-intentional-hide-finalization-before-show.before")
                    identity.IsWindowAlive = false;
            });
            holder = fixture;
            using (fixture)
            {
                WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: false);
                Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
            }
        }
        finally
        {
            journalLock?.Dispose();
        }
    }

    [Fact]
    public void EmergencyRelease_ContinuesPastPendingMember()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(twoEntries: true);
        fixture.IdentityForSecond!.ProcessStartTicks = 0;
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var groups = new GroupManager(fixture.Service, persistence, fixture.Log);
        var group = groups.CreateGroup("release-tests");
        group.Members.Add(fixture.Captured);
        group.Members.Add(fixture.CapturedSecond!);

        groups.EmergencyReleaseAll();

        Assert.Same(fixture.CapturedSecond, Assert.Single(group.Members));
        Assert.True(fixture.Native.MutationCount > 0);
        HiddenWindowEntry remainingEntry = Assert.Single(fixture.ReadEntries());
        Assert.Equal(fixture.CapturedSecond!.WindowIdentityToken, remainingEntry.WindowIdentityToken);
    }

    [Fact]
    public void CloseGroup_RetainsPendingMember()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(twoEntries: true);
        fixture.IdentityForSecond!.ProcessStartTicks = 0;
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var groups = new GroupManager(fixture.Service, persistence, fixture.Log);
        var group = groups.CreateGroup("close-tests");
        group.Members.Add(fixture.Captured);
        group.Members.Add(fixture.CapturedSecond!);

        bool closed = groups.CloseGroup(group);

        Assert.False(closed);
        Assert.Contains(group, groups.Groups);
        Assert.Same(fixture.CapturedSecond, Assert.Single(group.Members));
        Assert.Single(fixture.ReadEntries());
        Assert.True(fixture.Native.MutationCount > 0);
    }

    [Fact]
    public void Release_LaterRetry_CompletesPreviouslyUnverifiableRelease()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome first = fixture.Service.Release(fixture.Captured);
        fixture.Identity.ProcessStartTicks = fixture.Captured.ProcessStartTimeUtcTicks;
        WindowReleaseOutcome second = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.RecoveryPending, first);
        Assert.Equal(WindowReleaseOutcome.Released, second);
        Assert.Empty(fixture.ReadEntries());
        Assert.True(fixture.Native.MutationCount > 0);
    }

    [Fact]
    public void Release_GenerationChangeBeforePlacement_NeverMutates()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, identity) =>
            {
                if (stage == "release-before-placement.before")
                    identity.ReplaceGeneration();
            });
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(0, fixture.Native.PlacementCount);
        Assert.Equal(0, fixture.Native.ShowWindowCount);
        Assert.Equal(0, fixture.Native.TransitionCount);
    }

    [Fact]
    public void Release_GenerationChangeBetweenPlacementAndVisibility_StopsSequence()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Native.AfterPlacement = fixture.Identity.ReplaceGeneration;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(1, fixture.Native.PlacementCount);
        Assert.Equal(0, fixture.Native.ShowWindowCount);
        Assert.Equal(0, fixture.Native.TransitionCount);
    }

    [Fact]
    public void Release_GenerationChangeBeforeTransitions_StopsSequence()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, identity) =>
            {
                if (stage == "release-before-transitions.before")
                    identity.ReplaceGeneration();
            });
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(1, fixture.Native.PlacementCount);
        Assert.Equal(1, fixture.Native.ShowWindowCount);
        Assert.Equal(1, fixture.Native.ForegroundCount);
        Assert.Equal(0, fixture.Native.TransitionCount);
    }

    [Fact]
    public void Release_GenerationChangeBeforeTokenRemoval_StopsCleanup()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        fixture.Native.AfterTransitions = fixture.Identity.ReplaceGeneration;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, result);
        Assert.Equal(1, fixture.Native.PlacementCount);
        Assert.Equal(1, fixture.Native.ShowWindowCount);
        Assert.Equal(1, fixture.Native.TransitionCount);
        Assert.Equal(0, fixture.Identity.TokenRemovalCount);
        Assert.Empty(fixture.ReadEntries());
    }

    [Fact]
    public void Hide_RegistersExpectedHideProvenance()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        var provenance = new GuestHideProvenance();
        fixture.Service.HideProvenance = provenance;

        WindowHideOutcome result = fixture.Service.Hide(fixture.Captured);

        Assert.Equal(WindowHideOutcome.Hidden, result);
        Assert.True(provenance.HasExpectedHide(fixture.Captured.Hwnd));
        Assert.True(provenance.TryConsumeExpectedHide(
            fixture.Captured.Hwnd,
            fixture.Captured,
            unchecked((uint)Environment.TickCount),
            out string operation));
        Assert.Equal("shepherd-hide", operation);
    }

    [Fact]
    public void Release_ForgetsStaleHideProvenance()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        var provenance = new GuestHideProvenance();
        fixture.Service.HideProvenance = provenance;
        provenance.RegisterExpectedHide(
            fixture.Captured,
            "stale-expectation",
            unchecked((uint)Environment.TickCount));

        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);

        Assert.Equal(WindowReleaseOutcome.Released, result);
        Assert.False(provenance.HasExpectedHide(fixture.Captured.Hwnd));
    }

    // ---- released-close target identity (nonce-guarded destructive close) ----

    [Fact]
    public void VerifyReleasedCloseTarget_ExactMatchConsumesNonceOneShot()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);

        Assert.Equal(
            ReleasedWindowCloseTargetResult.Match,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));

        // One-shot semantics: the successful proof consumed the nonce, so a
        // replayed verification must fail closed instead of re-authorizing.
        Assert.NotEqual(
            ReleasedWindowCloseTargetResult.Match,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));
    }

    [Fact]
    public void VerifyReleasedCloseTarget_SameProcessRecycleWithoutNonce_IsUnverifiable()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);
        WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _); // consume the nonce

        // A same-process destroy/recreate of a same-class window on the same
        // GUI thread passes every process-scoped field but carries no nonce.
        Assert.Equal(
            ReleasedWindowCloseTargetResult.Unverifiable,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));
    }

    [Theory]
    [InlineData("WrongNonce")]
    [InlineData("DifferentPid")]
    [InlineData("DifferentThread")]
    [InlineData("DifferentExecutable")]
    [InlineData("DifferentClass")]
    [InlineData("DifferentProcessInstance")]
    public void VerifyReleasedCloseTarget_ReplacementEvidence_IsRejected(string caseName)
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);

        switch (caseName)
        {
            case "WrongNonce":
                fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce + 1);
                break;
            case "DifferentPid":
                fixture.Identity.Identity = new WindowProcessIdentity(99, fixture.Captured.WindowThreadId);
                break;
            case "DifferentThread":
                fixture.Identity.Identity = new WindowProcessIdentity(fixture.Captured.ProcessId, 9999);
                break;
            case "DifferentExecutable":
                fixture.Identity.ExePath = "replacement.exe";
                break;
            case "DifferentClass":
                fixture.Identity.ClassName = "ReplacementClass";
                break;
            case "DifferentProcessInstance":
                fixture.Identity.ProcessStartTicks = fixture.Captured.ProcessStartTimeUtcTicks + 1;
                break;
        }

        Assert.Equal(
            ReleasedWindowCloseTargetResult.Replaced,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));
    }

    [Fact]
    public void VerifyReleasedCloseTarget_MissingTargetNonce_FailsClosed()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);
        var noncelessTarget = new ReleasedWindowCloseTarget(
            target.Hwnd,
            target.ProcessId,
            target.WindowThreadId,
            target.ExePath,
            target.ClassName,
            target.ProcessStartTimeUtcTicks,
            0);

        Assert.Equal(
            ReleasedWindowCloseTargetResult.Unverifiable,
            WindowIdentityGate.VerifyReleasedCloseTarget(noncelessTarget, fixture.Identity, out _));
    }

    [Fact]
    public void VerifyReleasedCloseTarget_MissingProcessInstance_FailsClosed()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);
        fixture.Identity.ProcessStartTicks = 0;

        Assert.Equal(
            ReleasedWindowCloseTargetResult.Unverifiable,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));
    }

    [Fact]
    public void VerifyReleasedCloseTarget_DestroyedWindow_IsBenignDestroyed()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        ReleasedWindowCloseTarget target = ReleasedWindowCloseTarget.FromCaptured(fixture.Captured);
        fixture.Identity.CaptureToken = IntPtr.Zero;
        fixture.Identity.ReleasedCloseNonce = new IntPtr(fixture.Captured.ReleasedCloseNonce);
        fixture.Identity.IsWindowAlive = false;

        Assert.Equal(
            ReleasedWindowCloseTargetResult.Destroyed,
            WindowIdentityGate.VerifyReleasedCloseTarget(target, fixture.Identity, out _));
    }
}
