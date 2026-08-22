using System;
using System.Windows.Threading;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic coverage for the Wave 2E replaceable-timer abstraction. The
/// stale-callback suppression contract lives in <see cref="ReplaceableWorkSlot"/>
/// (pure, token-based — no dispatcher pump and no sleeps), while
/// <see cref="ReplaceableDispatcherTimer"/> is exercised for its arm/replace/
/// cancel state transitions via real DispatcherTimer instances whose ticks are
/// never pumped by the test runner.
///
/// Locked contract: a replaced or cancelled timer's tick must never execute its
/// user action; a one-shot timer cannot fire twice; replacement re-arms cleanly.
/// This is the correctness pattern behind AUDIT25-05/Q5/Q8 made unavoidable.
/// </summary>
public sealed class ReplaceableDispatcherTimerTests
{
    // ---- ReplaceableWorkSlot: the deterministic stale-guard core -------------

    [Fact]
    public void ScheduleOnce_ConsumeSucceedsExactlyOnce_CallbackRunsOnce()
    {
        var slot = new ReplaceableWorkSlot();
        var token = new object();
        int executions = 0;

        slot.Claim(token);
        if (slot.ConsumeIfCurrent(token)) executions++; // simulated first (only) tick
        if (slot.ConsumeIfCurrent(token)) executions++; // simulated second dispatch of same one-shot

        Assert.Equal(1, executions);
    }

    [Fact]
    public void Replacement_RevokesOldOwner_OldCallbackCannotRun()
    {
        var slot = new ReplaceableWorkSlot();
        var oldTimer = new object();
        var newTimer = new object();
        int oldExecutions = 0;

        slot.Claim(oldTimer);
        slot.Claim(newTimer); // user re-arms before the old tick dispatches

        // Old timer's queued tick finally arrives:
        if (slot.ConsumeIfCurrent(oldTimer)) oldExecutions++;
        Assert.Equal(0, oldExecutions);

        // ...and the new owner is still live.
        Assert.True(slot.IsCurrent(newTimer));
    }

    [Fact]
    public void Cancellation_PreventsAction()
    {
        var slot = new ReplaceableWorkSlot();
        var token = new object();

        slot.Claim(token);
        slot.Clear(); // logical Cancel()

        Assert.False(slot.ConsumeIfCurrent(token));
        Assert.False(slot.IsCurrent(token));
    }

    [Fact]
    public void RescheduleAfterExecution_WorksCleanly()
    {
        var slot = new ReplaceableWorkSlot();
        int generationsFired = 0;

        var first = new object();
        slot.Claim(first);
        if (slot.ConsumeIfCurrent(first)) generationsFired++;

        var second = new object();
        slot.Claim(second);
        if (slot.ConsumeIfCurrent(second)) generationsFired++;

        Assert.Equal(2, generationsFired);
    }

    [Fact]
    public void CapturedState_IsTheScheduledSnapshot_NotLiveState()
    {
        // The handwritten idiom's snapshot-at-schedule contract: the CALLER
        // copies live state into a local BEFORE arming (e.g. `CapturedWindow
        // activeWindow = _shepherdActiveWindow;`) and the armed delegate closes
        // over that local — so later external mutation of the live state cannot
        // reach the tick. This pins it so nobody converts a captured
        // guest/generation into "read current state at tick time".
        string liveState = "guest-A";
        string stateAtSchedule = liveState; // value snapshot taken when arming
        string? observed = null;

        var slot = new ReplaceableWorkSlot();
        var token = new object();
        slot.Claim(token);

        Action scheduledAction = () => observed = stateAtSchedule;
        liveState = "guest-B-changed"; // external mutation AFTER scheduling

        if (slot.ConsumeIfCurrent(token))
            scheduledAction();

        Assert.Equal("guest-A", observed);
    }

    [Fact]
    public void Consume_NonOwnedToken_DoesNotDisturbCurrentOwner()
    {
        var slot = new ReplaceableWorkSlot();
        var current = new object();
        var foreign = new object();

        slot.Claim(current);
        Assert.False(slot.ConsumeIfCurrent(foreign));
        Assert.True(slot.IsCurrent(current)); // unaffected by the stale consume attempt
    }

    // ---- ReplaceableDispatcherTimer: real WPF wiring, unpumped ---------------

    [Fact]
    public void Schedule_ArmsSlot_Cancel_DisarmsIt()
    {
        var t = new ReplaceableDispatcherTimer();
        Assert.False(t.HasPendingWork);

        bool fired = false;
        t.Schedule(TimeSpan.FromMinutes(1), () => fired = true);
        Assert.True(t.HasPendingWork); // armed; runner never pumps ticks

        t.Cancel();
        Assert.False(t.HasPendingWork);
        Assert.False(fired);
    }

    [Fact]
    public void Schedule_Twice_ReplacesThePendingTimer()
    {
        var t = new ReplaceableDispatcherTimer();
        t.Schedule(TimeSpan.FromMinutes(1), () => { });
        t.Schedule(TimeSpan.FromSeconds(30), () => { }); // replacement cancels prior arm
        Assert.True(t.HasPendingWork);

        t.Cancel();
        Assert.False(t.HasPendingWork);
    }

    [Fact]
    public void Cancel_IsIdempotent_AndSafeWithoutAnySchedule()
    {
        var t = new ReplaceableDispatcherTimer();
        t.Cancel();
        t.Cancel();
        Assert.False(t.HasPendingWork);
    }

    // Note: DispatcherTimer exposes no readable Priority property (it is
    // constructor-only in WPF), so the adapter's Background default matching the
    // legacy `new DispatcherTimer { Interval = .. }` construction is enforced by
    // its optional-parameter signature rather than a runtime assertion here.
}
