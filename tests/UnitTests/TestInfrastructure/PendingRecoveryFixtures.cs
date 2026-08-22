using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.UnitTests.TestInfrastructure;

/// <summary>
/// Shared hermetic fixtures for the supervised pending-recovery workflow tests
/// (migrated from the former PendingRecoverySelfTest, Wave 4). No real HWND is
/// enumerated or mutated: <see cref="FakePendingApi"/> models candidate windows
/// and records every native mutation.
/// </summary>
internal sealed class PendingTarget
{
    public bool Exists { get; set; } = true;
    public IntPtr Hwnd { get; init; }
    public uint Pid { get; set; }
    public uint ThreadId { get; set; }
    public string Exe { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public long ProcessStartTicks { get; set; }
    public IntPtr CaptureToken { get; set; }
    public IntPtr RecoveryToken { get; set; }
    public bool Visible { get; set; }

    public static PendingTarget For(long hwnd, uint pid, uint thread, string exe, string className, long start)
        => new()
        {
            Hwnd = new IntPtr(hwnd),
            Pid = pid,
            ThreadId = thread,
            Exe = exe,
            ClassName = className,
            ProcessStartTicks = start,
        };
}

internal sealed class FakePendingApi : IPendingRecoveryNativeApi
{
    public FakePendingApi(params PendingTarget[] targets)
    {
        foreach (PendingTarget target in targets)
            Targets[target.Hwnd] = target;
    }

    public Dictionary<IntPtr, PendingTarget> Targets { get; } = new();
    public int MutationCount { get; private set; }
    public int PlacementCount { get; private set; }
    public int ShowCount { get; private set; }
    public int TransitionCount { get; private set; }
    public int RemovePropertyCount { get; private set; }
    public bool FailPlacement { get; set; }
    public bool FailShow { get; set; }
    public bool FailTransitions { get; set; }
    public bool ChangeAfterSetProperty { get; set; }
    public bool ChangeAfterPlacement { get; set; }
    public bool ChangeAfterShow { get; set; }
    public bool ChangeAfterTransitions { get; set; }

    public bool IsWindow(IntPtr hwnd) => Find(hwnd).Exists;
    public uint GetProcessId(IntPtr hwnd) => Find(hwnd).Pid;
    public uint GetWindowThreadId(IntPtr hwnd) => Find(hwnd).ThreadId;
    public string? GetProcessImagePath(uint pid) => Targets.Values.FirstOrDefault(target => target.Pid == pid)?.Exe;
    public string? GetClassName(IntPtr hwnd) => Find(hwnd).ClassName;
    public long GetProcessStartTimeUtcTicks(uint pid)
        => Targets.Values.FirstOrDefault(target => target.Pid == pid)?.ProcessStartTicks ?? 0;

    public IntPtr GetProperty(IntPtr hwnd, string propertyName)
    {
        PendingTarget target = Find(hwnd);
        return propertyName == NativeWindowIdentityApi.CaptureIdentityPropertyName
            ? target.CaptureToken
            : propertyName == PendingRecoveryService.TemporaryRecoveryPropertyName
                ? target.RecoveryToken
                : IntPtr.Zero;
    }

    public bool SetProperty(IntPtr hwnd, string propertyName, IntPtr value)
    {
        PendingTarget target = Find(hwnd);
        if (propertyName != PendingRecoveryService.TemporaryRecoveryPropertyName
            || target.RecoveryToken != IntPtr.Zero)
            return false;
        target.RecoveryToken = value;
        if (ChangeAfterSetProperty)
            ChangeGeneration(target);
        return true;
    }

    public bool RemoveProperty(IntPtr hwnd, string propertyName, IntPtr expectedValue)
    {
        PendingTarget target = Find(hwnd);
        if (propertyName != PendingRecoveryService.TemporaryRecoveryPropertyName
            || target.RecoveryToken != expectedValue)
            return false;
        RemovePropertyCount++;
        target.RecoveryToken = IntPtr.Zero;
        return true;
    }

    public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
    {
        MutationCount++;
        PlacementCount++;
        if (ChangeAfterPlacement)
            ChangeGeneration(Find(hwnd));
        return !FailPlacement;
    }

    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
    {
        MutationCount++;
        PlacementCount++;
        return !FailPlacement;
    }

    public bool ShowWindow(IntPtr hwnd, int command)
    {
        MutationCount++;
        ShowCount++;
        PendingTarget target = Find(hwnd);
        bool previous = target.Visible;
        if (FailShow)
            return previous;
        target.Visible = command != NativeMethods.SW_HIDE;
        if (ChangeAfterShow)
            ChangeGeneration(target);
        return previous;
    }

    public bool IsWindowVisible(IntPtr hwnd) => Find(hwnd).Visible;

    public int SetTransitionsDisabled(IntPtr hwnd, int value)
    {
        MutationCount++;
        TransitionCount++;
        if (ChangeAfterTransitions)
            ChangeGeneration(Find(hwnd));
        return FailTransitions ? -1 : 0;
    }

    private PendingTarget Find(IntPtr hwnd)
        => Targets.TryGetValue(hwnd, out PendingTarget? target)
            ? target
            : throw new InvalidOperationException("unknown test HWND");

    private static void ChangeGeneration(PendingTarget target)
        => target.RecoveryToken = new IntPtr(0x7FFF);
}

internal static class PendingRecoveryTestHarness
{
    /// <summary>Runs ExecuteRecovery expecting the injected fault; returns true iff it threw.</summary>
    public static bool FaultAfterStage(
        PendingRecoveryEntry entry,
        FakePendingApi api,
        string stage,
        long token)
    {
        try
        {
            PendingRecoveryService.ExecuteRecovery(
                entry,
                CandidateFor(entry, "C001"),
                api,
                out _,
                tokenFactory: () => new IntPtr(token),
                faultInjector: value => value == stage);
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
        {
            return true;
        }
    }

    public static int RunInteractiveFor(PendingRecoveryEntry entry, FakePendingApi api, string root)
    {
        PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
        using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
        using var output = new StringWriter();
        return PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });
    }

    public static int RunInteractiveWithFault(PendingRecoveryEntry entry, FakePendingApi api, string root, string stage)
    {
        PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
        using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
        using var output = new StringWriter();
        return PendingRecoveryService.RunInteractive(
            input,
            output,
            root,
            api,
            new[] { candidate },
            faultInjector: value => value == stage);
    }

    public static void RemoveFirstPendingEntry(string path)
    {
        JsonObject rootObject = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        rootObject["Entries"]!.AsArray().RemoveAt(0);
        File.WriteAllText(path, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void RewritePendingSource(string path)
    {
        JsonObject rootObject = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        rootObject["unknown-root-field"] = "rewritten-source";
        File.WriteAllText(path, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool SetLedgerTransactionPhase(string path, string phase)
    {
        try
        {
            JsonObject rootObject = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (rootObject["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject transaction)
            {
                return false;
            }

            transaction["Phase"] = phase;
            File.WriteAllText(path, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DuplicateLedgerTransaction(string path, string secondSourceSha, int secondEntryIndex)
    {
        try
        {
            JsonObject rootObject = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (rootObject["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject first)
            {
                return false;
            }

            JsonObject second = JsonNode.Parse(first.ToJsonString())!.AsObject();
            second["SourceFileSha256"] = secondSourceSha;
            second["EntryIndex"] = secondEntryIndex;
            transactions.Add(second);
            File.WriteAllText(path, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool LedgerHasSingleRetiredCurrentTransaction(
        string path,
        string sourceSha256,
        int entryIndex,
        string entryFingerprint,
        long recoveryToken)
    {
        try
        {
            JsonObject rootObject = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (rootObject["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject transaction)
            {
                return false;
            }

            return transaction["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.Retired
                && string.Equals(
                    transaction["SourceFileSha256"]?.GetValue<string>(),
                    sourceSha256,
                    StringComparison.OrdinalIgnoreCase)
                && transaction["EntryIndex"]?.GetValue<int>() == entryIndex
                && string.Equals(
                    transaction["EntryFingerprint"]?.GetValue<string>(),
                    entryFingerprint,
                    StringComparison.OrdinalIgnoreCase)
                && transaction["RecoveryToken"]?.GetValue<long>() == recoveryToken;
        }
        catch
        {
            return false;
        }
    }

    public static string JournalJson(int? version, params JsonObject[] entries)
        => JournalJson(version, sourceInstanceId: null, entries);

    public static string JournalJson(int? version, string? sourceInstanceId, params JsonObject[] entries)
    {
        var root = new JsonObject
        {
            ["unknown-root-field"] = "preserve-me",
        };
        if (version.HasValue)
            root["Version"] = version.Value;
        if (sourceInstanceId != null)
            root["SourceInstanceId"] = sourceInstanceId;
        var array = new JsonArray();
        foreach (JsonObject entry in entries)
            array.Add(entry);
        root["Entries"] = array;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static JsonObject EntryV1(long hwnd, uint pid, string exe)
        => new()
        {
            ["Hwnd"] = hwnd,
            ["Pid"] = pid,
            ["ExePath"] = exe,
        };

    public static JsonObject EntryV2(
        long hwnd,
        uint pid,
        string exe,
        long start,
        bool doNotRescue = false,
        bool hasTransitions = true)
        => new()
        {
            ["Hwnd"] = hwnd,
            ["Pid"] = pid,
            ["ExePath"] = exe,
            ["ClassName"] = "Modern",
            ["ProcessStartTimeUtcTicks"] = start,
            ["OriginallyVisible"] = true,
            ["HasOriginalPlacement"] = true,
            ["OriginalPlacementFlags"] = 0,
            ["OriginalShowCommand"] = NativeMethods.SW_SHOW,
            ["OriginalNormalLeft"] = 10,
            ["OriginalNormalTop"] = 20,
            ["OriginalNormalRight"] = 410,
            ["OriginalNormalBottom"] = 320,
            ["HasOriginalTransitionsState"] = hasTransitions,
            ["OriginalTransitionsDisabled"] = false,
            ["DoNotRescue"] = doNotRescue,
        };

    public static PendingRecoveryCandidate CandidateFor(PendingRecoveryEntry entry, string id)
        => new()
        {
            CandidateId = id,
            Hwnd = new IntPtr(entry.Entry.Hwnd),
            ProcessId = entry.Entry.Pid,
            WindowThreadId = entry.Entry.Pid + 1000,
            ExePath = entry.Entry.ExePath,
            ClassName = string.IsNullOrWhiteSpace(entry.Entry.ClassName) ? "Legacy" : entry.Entry.ClassName,
            ProcessStartTimeUtcTicks = entry.Entry.ProcessStartTimeUtcTicks,
            Title = "local test title",
        };

    public static PendingRecoveryCandidate CopyCandidate(
        PendingRecoveryCandidate source,
        string? id = null,
        IntPtr? hwnd = null,
        uint? processId = null,
        string? exePath = null,
        long? processStart = null)
        => new()
        {
            CandidateId = id ?? source.CandidateId,
            Hwnd = hwnd ?? source.Hwnd,
            ProcessId = processId ?? source.ProcessId,
            WindowThreadId = source.WindowThreadId,
            ExePath = exePath ?? source.ExePath,
            ClassName = source.ClassName,
            ProcessStartTimeUtcTicks = processStart ?? source.ProcessStartTimeUtcTicks,
            Title = source.Title,
            Visible = source.Visible,
            Iconic = source.Iconic,
        };

    public static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-pending-recovery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
    }
}
