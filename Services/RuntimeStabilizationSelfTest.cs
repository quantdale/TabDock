using System;
using System.IO;
using System.Linq;
using TabDock.Models;
using TabDock.Views;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the live-runtime stabilization campaign:
/// no synchronous durable I/O on ordinary tab switching (#3), zero redundant
/// recovery-journal commits across ordinary hides after a durable capture (#4),
/// and relative (not strict-adjacency) split z-order health (#7).
///
/// These run inside the production self-test gate (validate.ps1) so a
/// regression in the real runtime path fails CI exactly like a unit test.
/// </summary>
internal static class RuntimeStabilizationSelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition) failures++;
        }

        Check(JournaledCapture_OrdinaryHidesAreZeroCommit());
        Check(JournaledCapture_IntentionalHideInvalidatesRescue());
        Check(ActiveSelection_SameIndexIsNoOpAndNoSynchronousStateWrite());
        Check(ZOrder_RelativeOrderIgnoresInterveningHelperWindows());
        return (checks, failures);
    }

    /// <summary>
    /// #4: a normal capture durably commits the complete rescue entry before the
    /// first presentation mutation. From then on, 100 ordinary SW_HIDE tab
    /// switches must NOT rewrite and fsync that identical entry — zero
    /// additional durable journal commits — while the rescue entry remains on
    /// disk so a hard-kill recovery still works.
    /// </summary>
    private static bool JournaledCapture_OrdinaryHidesAreZeroCommit()
    {
        int commits = 0;
        int alreadyDurable = 0;
        using WindowReleaseSelfTest.TestFixture fixture = WindowReleaseSelfTest.TestFixture.Create(
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

        // 100 ordinary hides -> zero additional durable journal commits.
        bool zeroCommits = commits == 0;
        bool allAlreadyDurable = alreadyDurable == ordinaryHides;

        // The rescue entry must still be on disk (hard-kill recovery works).
        bool rescueStillRecoverable = fixture.ReadEntries().Count == 1;

        return zeroCommits && allAlreadyDurable && rescueStillRecoverable;
    }

    /// <summary>
    /// #4 (bookkeeping integrity): an intentional hide (release / explicit close)
    /// invalidates the rescue-known flag, so a future hide must re-establish
    /// durable rescue intent rather than silently skipping the commit.
    /// </summary>
    private static bool JournaledCapture_IntentionalHideInvalidatesRescue()
    {
        int commits = 0;
        using WindowReleaseSelfTest.TestFixture fixture = WindowReleaseSelfTest.TestFixture.Create(
            sequencingHook: (stage, _) => { if (stage == "JournalHide.committed") commits++; });

        fixture.Service.MarkJournalCaptureCompleteForTesting(fixture.Captured);

        // Ordinary hide: skipped (already durable).
        fixture.Service.Hide(fixture.Captured);
        int afterOrdinary = commits;

        // Intentional hide marker invalidates the rescue-known state.
        fixture.Service.MarkIntentionalHideForTesting(fixture.Captured);

        // A subsequent hide now must re-establish durable rescue intent.
        fixture.Service.Hide(fixture.Captured);
        int afterInvalidated = commits;

        return afterOrdinary == 0 && afterInvalidated == 1;
    }

    /// <summary>
    /// #3: active-tab selection is a hot-path presentation preference, not a
    /// crash-safety boundary. The same-index selection must be a true no-op, and
    /// a different-index selection must NOT synchronously write state.json on the
    /// input/click path (it is debounced instead).
    /// </summary>
    private static bool ActiveSelection_SameIndexIsNoOpAndNoSynchronousStateWrite()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "tabdock-stab-selftest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var log = new LoggingService(Path.Combine(Path.GetTempPath(), "tabdock-stab-selftest-logs"));
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

            // Same-index selection: must be a no-op (no log, no write, no state change).
            manager.SwitchActiveTab(group, 1);
            bool sameIndexNoOp = group.ActiveIndex == 1;

            // Different-index selection: state.json must NOT be written synchronously.
            manager.SwitchActiveTab(group, 2);
            bool switched = group.ActiveIndex == 2;
            bool noSynchronousWrite = !File.Exists(statePath);

            return sameIndexNoOp && switched && noSynchronousWrite;
        }
        finally
        {
            try { if (File.Exists(statePath)) File.Delete(statePath); } catch { }
        }
    }

    /// <summary>
    /// #7: split z-order health uses relative order, so IME / helper / overlay
    /// HWNDs between the two panes must NOT make the pair look "out of order".
    /// </summary>
    private static bool ZOrder_RelativeOrderIgnoresInterveningHelperWindows()
    {
        // Chain (top -> bottom): paneTop, imeHelper, overlay, paneBottom.
        IntPtr top = new IntPtr(0x10);
        IntPtr ime = new IntPtr(0x20);
        IntPtr overlay = new IntPtr(0x30);
        IntPtr bottom = new IntPtr(0x40);
        IntPtr garbage = new IntPtr(0x50);
        var next = new System.Collections.Generic.Dictionary<IntPtr, IntPtr>
        {
            [top] = ime, [ime] = overlay, [overlay] = bottom, [bottom] = garbage, [garbage] = IntPtr.Zero,
        };
        Func<IntPtr, IntPtr> getNext = h => next.TryGetValue(h, out IntPtr n) ? n : IntPtr.Zero;

        bool topAboveBottom = ZOrder.IsOrderedAbove(top, bottom, getNext);
        bool bottomAboveTop = ZOrder.IsOrderedAbove(bottom, top, getNext);
        bool selfNotAbove = !ZOrder.IsOrderedAbove(top, top, getNext);
        bool zeroSafe = !ZOrder.IsOrderedAbove(IntPtr.Zero, bottom, getNext);
        return topAboveBottom && !bottomAboveTop && selfNotAbove && zeroSafe;
    }
}
