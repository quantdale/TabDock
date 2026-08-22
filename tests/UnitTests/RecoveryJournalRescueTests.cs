using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former RecoveryJournalSelfTest (Wave 4): deterministic
/// coverage for recovery schema compatibility and identity-safe rescue. The v1
/// and v2 fixtures are literal historical JSON shapes rather than current DTO
/// instances, so a new field cannot accidentally make a legacy fixture look
/// current.
/// </summary>
public class RecoveryJournalRescueTests
{
    [Fact]
    public void LegacyV1Journal_IsPreservedAsPendingEvidenceWithoutMutation()
    {
        const string legacyV1 = "{\"Entries\":[{\"Hwnd\":1,\"Pid\":11,\"ExePath\":\"guest-11.exe\"}]}";
        byte[] legacyBytes = Encoding.UTF8.GetBytes(legacyV1);
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllBytes(journalPath, legacyBytes);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            byte[] pending = File.ReadAllBytes(journalPath + ".pending");
            Assert.True(pending.SequenceEqual(legacyBytes), "pending sidecar must preserve the exact original bytes");
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void LegacyV2Journal_IsPreservedAsPendingEvidenceWithoutMutation()
    {
        // Exact c6b119a/f1dc7ab journal shape: Version 2, no WindowThreadId and
        // no WindowIdentityToken.
        const string legacyV2 = "{\n  \"Version\": 2,\n  \"Entries\": [\n    {\n      \"Hwnd\": 1,\n      \"Pid\": 11,\n      \"ExePath\": \"guest-11.exe\",\n      \"ClassName\": \"Pig\",\n      \"ProcessStartTimeUtcTicks\": 101,\n      \"OriginallyVisible\": true,\n      \"HasOriginalPlacement\": true,\n      \"OriginalPlacementFlags\": 0,\n      \"OriginalShowCommand\": 5,\n      \"OriginalMinPositionX\": 0,\n      \"OriginalMinPositionY\": 0,\n      \"OriginalMaxPositionX\": 0,\n      \"OriginalMaxPositionY\": 0,\n      \"OriginalNormalLeft\": 0,\n      \"OriginalNormalTop\": 0,\n      \"OriginalNormalRight\": 400,\n      \"OriginalNormalBottom\": 300,\n      \"HasOriginalTransitionsState\": false,\n      \"OriginalTransitionsDisabled\": false,\n      \"DoNotRescue\": false\n    }\n  ]\n}";
        byte[] legacyBytes = Encoding.UTF8.GetBytes(legacyV2);
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllBytes(journalPath, legacyBytes);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            byte[] pending = File.ReadAllBytes(journalPath + ".pending");
            Assert.True(pending.SequenceEqual(legacyBytes), "pending sidecar must preserve the exact original bytes");
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ValidV3Journal_RescuesTheGuestAndClearsTheToken()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Contains(new IntPtr(1), api.Shown);
            Assert.Equal(IntPtr.Zero, api.CaptureTokens[new IntPtr(1)]);
            Assert.True(api.NativeMutationCount > 0);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void FutureVersionJournal_IsUntouched()
    {
        const string future = "{\"Version\":4,\"Entries\":[]}";
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllText(journalPath, future);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.True(File.Exists(journalPath));
            Assert.Equal(future, File.ReadAllText(journalPath));
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MalformedJournal_IsQuarantined()
    {
        const string malformed = "{not-json";
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllText(journalPath, malformed);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, new FakeRecoveryApi());

            Assert.False(File.Exists(journalPath));
            Assert.Single(Directory.GetFiles(root, "hidden-windows.json.corrupt.*"));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void V3EntryMissingToken_IsPreservedWithoutMutation()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            HiddenWindowEntry entry = Entry(1, 11, 101, 0);
            string json = JsonSerializer.Serialize(
                new HiddenWindowJournalFile { Version = 3, Entries = new List<HiddenWindowEntry> { entry } },
                TabDockJsonContext.Default.HiddenWindowJournalFile);
            File.WriteAllText(journalPath, json);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(json, File.ReadAllText(journalPath + ".pending"));
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MixedV3Evidence_ValidAndTokenless_ArePreservedTogether()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            HiddenWindowEntry valid = Entry(1, 11, 101, 1001);
            HiddenWindowEntry missingToken = Entry(2, 22, 202, 0);
            string json = JsonSerializer.Serialize(
                new HiddenWindowJournalFile { Version = 3, Entries = new List<HiddenWindowEntry> { valid, missingToken } },
                TabDockJsonContext.Default.HiddenWindowJournalFile);
            File.WriteAllText(journalPath, json);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(json, File.ReadAllText(journalPath + ".pending"));
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void V3TokenMismatch_IsStaleWithoutMutation()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi();
            api.Identity[new IntPtr(1)] = (11, 1101, "guest-11.exe", "Pig", 101, 2002);
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(0, api.NativeMutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void StartProbeUnavailable_RetriesLaterAndCompletes()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi { StartProbeUnavailable = true };
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);
            Assert.True(File.Exists(journalPath));
            Assert.Equal(0, api.NativeMutationCount);

            api.StartProbeUnavailable = false;
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Contains(new IntPtr(1), api.Shown);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void IdentityChangeAfterPlacement_StopsRescueBeforeVisibility()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi { ChangeTokenAfterPlacement = true };
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(1, api.PlacementMutationCount);
            Assert.Equal(0, api.VisibilityMutationCount);
            Assert.Equal(0, api.TransitionMutationCount);
            Assert.Equal(0, api.TokenRemovalCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void IdentityChangeAfterVisibility_StopsDwmRescue()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi { ChangeTokenAfterShow = true };
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(1, api.PlacementMutationCount);
            Assert.Equal(1, api.VisibilityMutationCount);
            Assert.Equal(0, api.TransitionMutationCount);
            Assert.Equal(0, api.TokenRemovalCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void IdentityChangeBeforeTokenRemoval_StopsCleanup()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            WriteV3(journalPath, Entry(1, 11, 101, 1001));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var api = new FakeRecoveryApi { ChangeTokenAfterTransitions = true };
            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);

            Assert.False(File.Exists(journalPath));
            Assert.Equal(1, api.PlacementMutationCount);
            Assert.Equal(1, api.VisibilityMutationCount);
            Assert.Equal(1, api.TransitionMutationCount);
            Assert.Equal(0, api.TokenRemovalCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void LegacyRead_CannotBeDowngradedToLegacySchemaOnReleaseMismatch()
    {
        const string legacyV2 = "{\"Version\":2,\"Entries\":[{\"Hwnd\":1,\"Pid\":11,\"ExePath\":\"guest-11.exe\",\"ClassName\":\"Pig\",\"ProcessStartTimeUtcTicks\":101}]}";
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllText(journalPath, legacyV2);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var identity = ShepherdFakeIdentityApi.For(1, 11, 101, 1001);
            identity.Identity = new WindowProcessIdentity(11, 20);
            var service = new WindowShepherdService(log, journalPath, identity, new FakeMonitorDpiProbe(), new ShepherdFakeReleaseApi());
            var captured = Captured(1, 11, 20, 1001, 101);
            service.BindCapturedWindowForTesting(captured);
            identity.CaptureToken = new IntPtr(2002);

            WindowReleaseOutcome outcome = service.Release(captured);

            Assert.Equal(WindowReleaseOutcome.TargetGoneOrRecycled, outcome);
            Assert.Equal(legacyV2, File.ReadAllText(journalPath));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void HideAgainstLegacyJournal_CannotDowngradeSchema()
    {
        const string legacyV2 = "{\"Version\":2,\"Entries\":[{\"Hwnd\":1,\"Pid\":11,\"ExePath\":\"guest-11.exe\",\"ClassName\":\"Pig\",\"ProcessStartTimeUtcTicks\":101}]}";
        string root = CreateRoot(out string journalPath);
        try
        {
            File.WriteAllText(journalPath, legacyV2);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var identity = ShepherdFakeIdentityApi.For(1, 11, 101, 1001);
            identity.Identity = new WindowProcessIdentity(11, 20);
            var service = new WindowShepherdService(log, journalPath, identity, new FakeMonitorDpiProbe(), new ShepherdFakeReleaseApi());
            var captured = Captured(1, 11, 20, 1001, 101);
            service.BindCapturedWindowForTesting(captured);

            WindowHideOutcome outcome = service.Hide(captured);

            Assert.Equal(WindowHideOutcome.RecoveryPending, outcome);
            Assert.Equal(legacyV2, File.ReadAllText(journalPath));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void UnverifiableHide_PreservesV3Journal()
    {
        string root = CreateRoot(out string journalPath);
        try
        {
            HiddenWindowEntry entry = Entry(1, 11, 101, 1001);
            string json = JsonSerializer.Serialize(
                new HiddenWindowJournalFile { Version = 3, Entries = new List<HiddenWindowEntry> { entry } },
                TabDockJsonContext.Default.HiddenWindowJournalFile);
            File.WriteAllText(journalPath, json);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var identity = ShepherdFakeIdentityApi.For(1, 11, 101, 1001);
            identity.Identity = new WindowProcessIdentity(11, 20);
            identity.ProcessStartTicks = 0; // strong start evidence unavailable -> fail closed
            var service = new WindowShepherdService(log, journalPath, identity, new FakeMonitorDpiProbe(), new ShepherdFakeReleaseApi());
            var captured = Captured(1, 11, 20, 1001, 101);
            service.BindCapturedWindowForTesting(captured);

            WindowHideOutcome outcome = service.Hide(captured);

            Assert.Equal(WindowHideOutcome.RecoveryPending, outcome);
            Assert.Equal(json, File.ReadAllText(journalPath));
        }
        finally { DeleteRoot(root); }
    }

    private static void WriteV3(string path, HiddenWindowEntry entry)
    {
        string json = JsonSerializer.Serialize(
            new HiddenWindowJournalFile { Version = 3, Entries = new List<HiddenWindowEntry> { entry } },
            TabDockJsonContext.Default.HiddenWindowJournalFile);
        File.WriteAllText(path, json);
    }

    private static HiddenWindowEntry Entry(long hwnd, uint pid, long startTicks, long identityToken)
        => new()
        {
            Hwnd = hwnd,
            Pid = pid,
            WindowThreadId = pid + 1000,
            WindowIdentityToken = identityToken,
            ExePath = $"guest-{pid}.exe",
            ClassName = "Pig",
            ProcessStartTimeUtcTicks = startTicks,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };

    private static CapturedWindow Captured(long hwnd, uint pid, uint threadId, long token, long startTicks)
        => new()
        {
            Hwnd = new IntPtr(hwnd),
            ProcessId = pid,
            WindowThreadId = threadId,
            WindowIdentityToken = token,
            ProcessStartTimeUtcTicks = startTicks,
            ExePath = $"guest-{pid}.exe",
            OriginalClassName = "Pig",
            OriginalTitle = "Guest",
            OriginallyVisible = true,
            HasValidPlacement = true,
            OriginalPlacement = new NativeMethods.WINDOWPLACEMENT
            {
                showCmd = NativeMethods.SW_SHOW,
                rcNormalPosition = new NativeMethods.RECT { right = 400, bottom = 300 },
            },
            OriginalBounds = new NativeMethods.RECT { right = 400, bottom = 300 },
        };

    private static string CreateRoot(out string journalPath)
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-journal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        journalPath = Path.Combine(root, "hidden-windows.json");
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    private sealed class FakeRecoveryApi : IRecoveryNativeApi
    {
        public bool StartProbeUnavailable { get; set; }
        public bool ChangeTokenAfterPlacement { get; set; }
        public bool ChangeTokenAfterShow { get; set; }
        public bool ChangeTokenAfterTransitions { get; set; }
        public Dictionary<IntPtr, (uint Pid, uint ThreadId, string Exe, string ClassName, long StartTicks, long Token)> Identity { get; } = new()
        {
            [new IntPtr(1)] = (11, 1011, "guest-11.exe", "Pig", 101, 1001),
            [new IntPtr(2)] = (22, 1022, "guest-22.exe", "Pig", 202, 1002),
        };
        public HashSet<IntPtr> Shown { get; } = new();
        public int NativeMutationCount { get; private set; }
        public int PlacementMutationCount { get; private set; }
        public int VisibilityMutationCount { get; private set; }
        public int TransitionMutationCount { get; private set; }
        public int TokenRemovalCount { get; private set; }
        public Dictionary<IntPtr, IntPtr> CaptureTokens => Identity.ToDictionary(
            pair => pair.Key,
            pair => new IntPtr(pair.Value.Token));

        public bool IsWindow(IntPtr hwnd) => Identity.ContainsKey(hwnd);
        public uint GetProcessId(IntPtr hwnd) => Identity[hwnd].Pid;
        public uint GetWindowThreadId(IntPtr hwnd) => Identity[hwnd].ThreadId;
        public string? GetProcessImagePath(uint pid) => Identity.Values.FirstOrDefault(x => x.Pid == pid).Exe;
        public string? GetClassName(IntPtr hwnd) => Identity[hwnd].ClassName;
        public long GetProcessStartTimeUtcTicks(uint pid)
            => StartProbeUnavailable ? 0 : Identity.Values.FirstOrDefault(x => x.Pid == pid).StartTicks;
        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) => new IntPtr(Identity[hwnd].Token);
        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
        {
            TokenRemovalCount++;
            if (GetCaptureIdentityToken(hwnd) != expectedToken)
                return false;
            (uint pid, uint threadId, string exe, string className, long startTicks, long _) = Identity[hwnd];
            Identity[hwnd] = (pid, threadId, exe, className, startTicks, 0);
            return true;
        }
        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        {
            NativeMutationCount++;
            PlacementMutationCount++;
            if (ChangeTokenAfterPlacement)
                ReplaceIdentity(hwnd);
            return true;
        }
        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            NativeMutationCount++;
            PlacementMutationCount++;
            if (ChangeTokenAfterPlacement)
                ReplaceIdentity(hwnd);
            return true;
        }
        public bool ShowWindow(IntPtr hwnd, int command)
        {
            NativeMutationCount++;
            VisibilityMutationCount++;
            bool previouslyVisible = Shown.Contains(hwnd);
            if (command == NativeMethods.SW_HIDE)
                Shown.Remove(hwnd);
            else
                Shown.Add(hwnd);
            if (ChangeTokenAfterShow)
                ReplaceIdentity(hwnd);
            return previouslyVisible;
        }
        public bool IsWindowVisible(IntPtr hwnd) => Shown.Contains(hwnd);
        public int SetTransitionsDisabled(IntPtr hwnd, int value)
        {
            NativeMutationCount++;
            TransitionMutationCount++;
            if (ChangeTokenAfterTransitions)
                ReplaceIdentity(hwnd);
            return 0;
        }

        private void ReplaceIdentity(IntPtr hwnd)
        {
            (uint pid, uint threadId, string exe, string className, long startTicks, long _) = Identity[hwnd];
            Identity[hwnd] = (pid, threadId, exe, className, startTicks, 2002);
        }
    }
}

