using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;
using static TabDock.UnitTests.TestInfrastructure.PendingRecoveryTestHarness;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former PendingRecoverySelfTest (Wave 4): supervised
/// recovery execution — identity matching, per-mode presentation restoration,
/// failure retention, generation guards, terminal-safe console output, and the
/// abandon path.
/// </summary>
public class PendingRecoveryExecutionTests
{
    [Fact]
    public void UserCancellation_PerformsNoMutation()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(600, 81, "cancel.exe", 8101)));
            var api = new FakePendingApi(PendingTarget.For(600, 81, 1081, "cancel.exe", "Modern", 8101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nNO\n");
            using var output = new StringWriter();

            int result = PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });

            Assert.Equal(1, result);
            Assert.Equal(0, api.MutationCount);
            Assert.True(File.Exists(entry.FullPath));
            Assert.False(File.Exists(entry.FullPath + ".recovered"));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void V1Recovery_IsVisibilityOnly()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(null, EntryV1(700, 91, "v1.exe")));
            var api = new FakePendingApi(PendingTarget.For(700, 91, 1091, "v1.exe", "Legacy", 0));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);

            Assert.True(recovered);
            Assert.Equal(0, api.PlacementCount);
            Assert.Equal(1, api.ShowCount);
            Assert.Equal(0, api.TransitionCount);
            Assert.Equal(1, api.RemovePropertyCount);
            Assert.True(api.Targets[new IntPtr(700)].Visible);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void V2Recovery_RestoresFullPresentation()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(800, 101, "v2.exe", 10101)));
            var api = new FakePendingApi(PendingTarget.For(800, 101, 1101, "v2.exe", "Modern", 10101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);

            Assert.True(recovered);
            Assert.Equal(1, api.PlacementCount);
            Assert.Equal(1, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
            Assert.Equal(1, api.RemovePropertyCount);
            Assert.True(api.Targets[new IntPtr(800)].Visible);
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData("placement")]
    [InlineData("show")]
    [InlineData("transitions")]
    public void RecoveryFailure_AtEachStage_RetainsEvidenceWithoutToken(string failingStage)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(900, 111, "failure.exe", 11101)));
            var api = new FakePendingApi(PendingTarget.For(900, 111, 1111, "failure.exe", "Modern", 11101))
            {
                FailPlacement = failingStage == "placement",
                FailShow = failingStage == "show",
                FailTransitions = failingStage == "transitions",
            };
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);

            Assert.False(recovered);
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".recovered"));
            Assert.DoesNotContain("presentation-restored", File.ReadAllText(path + ".recovered"), StringComparison.Ordinal);
            Assert.Equal(IntPtr.Zero, api.Targets[new IntPtr(900)].RecoveryToken);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void GenerationChange_AtAnyLaterStage_StopsRemainingMutations()
    {
        Assert.True(
            RunGenerationCase(changeAfterSet: true, expectedPlacement: 0, expectedShow: 0, expectedTransitions: 0),
            "change after SetProperty must stop every later mutation");
        Assert.True(
            RunGenerationCase(changeAfterPlacement: true, expectedPlacement: 1, expectedShow: 0, expectedTransitions: 0),
            "change after placement must stop show/transitions/cleanup");
        Assert.True(
            RunGenerationCase(changeAfterShow: true, expectedPlacement: 1, expectedShow: 1, expectedTransitions: 0),
            "change after show must stop transitions/cleanup");
        Assert.True(
            RunGenerationCase(changeAfterTransitions: true, expectedPlacement: 1, expectedShow: 1, expectedTransitions: 1),
            "change after transitions must stop cleanup");

        static bool RunGenerationCase(
            bool changeAfterSet = false,
            bool changeAfterPlacement = false,
            bool changeAfterShow = false,
            bool changeAfterTransitions = false,
            int expectedPlacement = 0,
            int expectedShow = 0,
            int expectedTransitions = 0)
        {
            string root = CreateRoot();
            try
            {
                string path = Path.Combine(root, "hidden-windows.json.pending");
                File.WriteAllText(path, JournalJson(2, EntryV2(1000, 121, "race.exe", 12101)));
                var api = new FakePendingApi(PendingTarget.For(1000, 121, 1121, "race.exe", "Modern", 12101))
                {
                    ChangeAfterSetProperty = changeAfterSet,
                    ChangeAfterPlacement = changeAfterPlacement,
                    ChangeAfterShow = changeAfterShow,
                    ChangeAfterTransitions = changeAfterTransitions,
                };
                PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
                bool recovered = PendingRecoveryService.ExecuteRecovery(
                    entry, CandidateFor(entry, "C001"), api, out _);
                return !recovered
                    && api.PlacementCount == expectedPlacement
                    && api.ShowCount == expectedShow
                    && api.TransitionCount == expectedTransitions
                    && api.RemovePropertyCount == 0
                    && File.Exists(path);
            }
            finally { DeleteRoot(root); }
        }
    }

    [Fact]
    public void ExistingCaptureOrRecoveryTokens_RefuseRecovery()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1200, 141, "token.exe", 14101)));
            var api = new FakePendingApi(PendingTarget.For(1200, 141, 1141, "token.exe", "Modern", 14101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");

            api.Targets[new IntPtr(1200)].CaptureToken = new IntPtr(7001);
            bool captureRefused = !PendingRecoveryService.ExecuteRecovery(entry, candidate, api, out _)
                && api.MutationCount == 0;

            api.Targets[new IntPtr(1200)].CaptureToken = IntPtr.Zero;
            api.Targets[new IntPtr(1200)].RecoveryToken = new IntPtr(7002);
            bool recoveryRefused = !PendingRecoveryService.ExecuteRecovery(entry, candidate, api, out _)
                && api.MutationCount == 0;

            Assert.True(captureRefused);
            Assert.True(recoveryRefused);
            Assert.True(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void DoNotRescue_NeverResurrectsTheGuest()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1300, 151, "intentional.exe", 15101, doNotRescue: true)));
            var api = new FakePendingApi(PendingTarget.For(1300, 151, 1151, "intentional.exe", "Modern", 15101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool recovered = PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _);

            // Only the historically required transition-state cleanup runs; the
            // guest is never shown or repositioned.
            Assert.True(recovered);
            Assert.Equal("v2-intentional-hide", entry.RecoveryMode);
            Assert.Equal(0, api.PlacementCount);
            Assert.Equal(0, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
            Assert.Equal(1, api.RemovePropertyCount);
            Assert.False(api.Targets[new IntPtr(1300)].Visible);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void DoNotRescue_WithoutRecordedTransitionState_StillCleansDwm()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1310, 152, "intentional-unrecorded.exe", 15201, doNotRescue: true, hasTransitions: false)));
            var api = new FakePendingApi(PendingTarget.For(1310, 152, 1152, "intentional-unrecorded.exe", "Modern", 15201));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool recovered = PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _);

            Assert.True(recovered);
            Assert.Equal(0, api.PlacementCount);
            Assert.Equal(0, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
            Assert.False(api.Targets[new IntPtr(1310)].Visible);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void RandomRecoveryToken_IsDurableAndNonzero()
    {
        long first = RunAndReadRecoveryToken(1470, 169, "random-a.exe", 16901);
        long second = RunAndReadRecoveryToken(1471, 170, "random-b.exe", 17001);

        Assert.NotEqual(0, first);
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);

        static long RunAndReadRecoveryToken(long hwnd, uint pid, string exe, long start)
        {
            string root = CreateRoot();
            try
            {
                string path = Path.Combine(root, "hidden-windows.json.pending");
                File.WriteAllText(path, JournalJson(2, EntryV2(hwnd, pid, exe, start)));
                var api = new FakePendingApi(PendingTarget.For(hwnd, pid, pid + 1000, exe, "Modern", start));
                PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
                if (!PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _))
                    return 0;
                JsonNode? rootNode = JsonNode.Parse(File.ReadAllText(path + ".recovered"));
                return rootNode?["Transactions"]?[0]?["RecoveryToken"]?.GetValue<long>() ?? 0;
            }
            finally { DeleteRoot(root); }
        }
    }

    [Fact]
    public void SanitizeConsoleTitle_StripsControlCharactersAndBoundsLength()
    {
        string title = "\u001B[31mRED\u001B]0;spoof\u0007\r\n\t\0\u007F\u0085\u2028\u2029 ordinary 😀 中文 "
            + new string('x', 140);

        string sanitized = PendingRecoveryService.SanitizeConsoleTitle(title);

        Assert.True(sanitized.Length <= 96);
        Assert.All(sanitized, character => Assert.False(
            character == '\u001B'
            || character == '\r'
            || character == '\n'
            || character == '\0'
            || character == '\u007F'
            || (character >= '\u0080' && character <= '\u009F')
            || character == '\u2028'
            || character == '\u2029',
            $"sanitized title must not contain control character U+{((int)character):X4}"));
        Assert.Contains("RED", sanitized, StringComparison.Ordinal);
        Assert.Contains("😀", sanitized, StringComparison.Ordinal);
        Assert.Contains("中文", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveConsoleOutput_IsTerminalSafeForHostileFields()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            string hostileExe = "C:\\Apps\\bad\u001B[31m.exe\r\n";
            string hostileClass = "Class\u001B]0;spoof\u0007\t\u0085";
            JsonObject entryJson = EntryV2(1480, 190, hostileExe, 19001);
            entryJson["ClassName"] = hostileClass;
            File.WriteAllText(path, JournalJson(2, entryJson));
            var api = new FakePendingApi(PendingTarget.For(1480, 190, 1190, hostileExe, hostileClass, 19001));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = new()
            {
                CandidateId = "C001",
                Hwnd = new IntPtr(1480),
                ProcessId = 190,
                WindowThreadId = 1190,
                ExePath = hostileExe,
                ClassName = hostileClass,
                ProcessStartTimeUtcTicks = 19001,
                Title = "title\u001B[2J\u001B]52;c;secret\u0007\r\n\t\u0080\u007F\u2028\u2029 😀 中文",
            };
            using var output = new StringWriter();
            using var input = new StringReader("P01-E001\nC001\nNO\n");

            int result = PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });

            string rendered = output.ToString();
            string displayFields = rendered.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            Assert.Equal(1, result);
            Assert.All(displayFields.EnumerateRunes(), rune => Assert.True(
                rune.Value > 0x1F
                && rune.Value != 0x7F
                && (rune.Value < 0x80 || rune.Value > 0x9F)
                && rune.Value != 0x2028
                && rune.Value != 0x2029,
                $"rendered console field must be terminal-safe (found U+{rune.Value:X4})"));
            Assert.Contains("😀", rendered, StringComparison.Ordinal);
            Assert.Contains("中文", rendered, StringComparison.Ordinal);
            Assert.True(File.Exists(path));
            Assert.Equal(0, api.MutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void AbandonPath_LiveTargetRefuses_VerifiablyGoneTargetIsDiscardedDurably()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(PendingTarget.For(1570, 179, 1179, "abandon.exe", "Modern", 17901));
            File.WriteAllText(path, JournalJson(2, EntryV2(1570, 179, "abandon.exe", 17901)));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.True(FaultAfterStage(entry, api, "after-setprop", 0x5801), "fixture must prepare an interrupted transaction");

            // A live target refuses abandonment.
            using (var liveInput = new StringReader("abandon P01-E001\n"))
            using (var liveOutput = new StringWriter())
            {
                int liveResult = PendingRecoveryService.RunInteractive(
                    liveInput, liveOutput, root, api, Array.Empty<PendingRecoveryCandidate>());
                Assert.Equal(2, liveResult);
                Assert.True(File.Exists(path));
            }

            // A verifiably destroyed target may be discarded, with zero native
            // mutations and a durable abandoned-resolution record.
            api.Targets[new IntPtr(1570)].Exists = false;
            int mutationsBefore = api.MutationCount;
            using var input = new StringReader("abandon P01-E001\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, Array.Empty<PendingRecoveryCandidate>());
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();

            Assert.Equal(0, result);
            Assert.False(File.Exists(path));
            Assert.Equal(mutationsBefore, api.MutationCount);
            Assert.Equal(
                "abandoned-target-gone",
                ledger["Resolutions"]!.AsArray().Single()!.AsObject()["Result"]?.GetValue<string>());
            Assert.Equal(
                PendingRecoveryService.RecoveryPhase.Retired,
                ledger["Transactions"]!.AsArray().Single()!.AsObject()["Phase"]?.GetValue<string>());
        }
        finally { DeleteRoot(root); }
    }
}
