using System;
using System.Collections.Generic;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former RuntimeStabilizationSelfTest (Wave 4): live-runtime
/// stabilization contracts — no synchronous durable I/O on ordinary tab
/// switching, zero redundant recovery-journal commits across ordinary hides
/// after a durable capture, and relative (not strict-adjacency) split z-order
/// health.
/// </summary>
public class RuntimeStabilizationTests
{
    [Fact]
    public void OrdinaryHidesAfterJournaledCapture_PerformZeroAdditionalDurableCommits()
    {
        int commits = 0;
        int alreadyDurable = 0;
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, _) =>
            {
                if (stage == "JournalHide.committed") commits++;
                else if (stage == "JournalHide.already-durable") alreadyDurable++;
            });

        // Capture is already durably journaled for this token (the overwhelmingly
        // common production case). Mark it without replaying the native capture.
        fixture.Service.MarkJournalCaptureCompleteForTesting(fixture.Captured);

        const int ordinaryHides = 100;
        for (int i = 0; i < ordinaryHides; i++)
            fixture.Service.Hide(fixture.Captured);

        // 100 ordinary hides -> zero additional durable journal commits, and the
        // rescue entry must still be on disk (hard-kill recovery works).
        Assert.Equal(0, commits);
        Assert.Equal(ordinaryHides, alreadyDurable);
        Assert.Single(fixture.ReadEntries());
    }

    [Fact]
    public void IntentionalHide_InvalidatesRescueKnownStateSoNextHideRecommits()
    {
        int commits = 0;
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(
            sequencingHook: (stage, _) => { if (stage == "JournalHide.committed") commits++; });

        fixture.Service.MarkJournalCaptureCompleteForTesting(fixture.Captured);

        // Ordinary hide: skipped (already durable).
        fixture.Service.Hide(fixture.Captured);
        Assert.Equal(0, commits);

        // Intentional hide marker invalidates the rescue-known state; a
        // subsequent hide must re-establish durable rescue intent.
        fixture.Service.MarkIntentionalHideForTesting(fixture.Captured);
        fixture.Service.Hide(fixture.Captured);

        Assert.Equal(1, commits);
    }

    [Fact]
    public void SwitchActiveTab_SameIndexIsNoOp_AndDifferentIndexNeverWritesSynchronously()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "tabdock-stab-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var log = new LoggingService(Path.Combine(Path.GetTempPath(), "tabdock-stab-test-logs"));
            var shepherd = new WindowShepherdService(log);
            var persistence = new PersistenceService(log, statePath);
            var manager = new GroupManager(shepherd, persistence, log);

            var group = new Group();
            for (int i = 0; i < 3; i++)
            {
                group.Members.Add(new CapturedWindow
                {
                    Hwnd = new IntPtr(0x5000 + i),
                    ProcessId = 5000u + (uint)i,
                    WindowThreadId = 6000u + (uint)i,
                    WindowIdentityToken = 7000 + i,
                    ExePath = $"guest{i}.exe",
                    OriginalClassName = "Pig",
                    OriginalTitle = "Guest",
                });
            }
            group.ActiveIndex = 1;

            // Same-index selection: must be a true no-op.
            manager.SwitchActiveTab(group, 1);
            Assert.Equal(1, group.ActiveIndex);

            // Different-index selection: active-tab preference is hot-path
            // presentation state and must NOT synchronously write state.json.
            manager.SwitchActiveTab(group, 2);
            Assert.Equal(2, group.ActiveIndex);
            Assert.False(File.Exists(statePath));
        }
        finally
        {
            try { if (File.Exists(statePath)) File.Delete(statePath); } catch { }
        }
    }

    [Fact]
    public void ZOrder_RelativeOrder_IgnoresInterveningHelperWindows()
    {
        // Chain (top -> bottom): paneTop, imeHelper, overlay, paneBottom.
        IntPtr top = new IntPtr(0x10);
        IntPtr ime = new IntPtr(0x20);
        IntPtr overlay = new IntPtr(0x30);
        IntPtr bottom = new IntPtr(0x40);
        IntPtr garbage = new IntPtr(0x50);
        var next = new Dictionary<IntPtr, IntPtr>
        {
            [top] = ime, [ime] = overlay, [overlay] = bottom, [bottom] = garbage, [garbage] = IntPtr.Zero,
        };
        Func<IntPtr, IntPtr> getNext = h => next.TryGetValue(h, out IntPtr n) ? n : IntPtr.Zero;

        Assert.True(ZOrder.IsOrderedAbove(top, bottom, getNext));
        Assert.False(ZOrder.IsOrderedAbove(bottom, top, getNext));
        Assert.False(ZOrder.IsOrderedAbove(top, top, getNext));
        Assert.False(ZOrder.IsOrderedAbove(IntPtr.Zero, bottom, getNext));
    }
}
