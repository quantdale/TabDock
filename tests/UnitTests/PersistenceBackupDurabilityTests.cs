using System;
using System.Collections.Generic;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic fault-injection coverage for the staged backup transaction in
/// PersistenceService.CommitJson. The previous implementation replaced the live
/// state.json.bak with File.Copy(overwrite:true), so a failure or power loss
/// mid-copy destroyed the previous known-good backup before the new primary was
/// installed. These tests drive every failure boundary through injected
/// delegates (read primary bytes / flush durable temp / atomic move) and prove,
/// per boundary: the previous backup survives, the primary is untouched, no
/// false saved-content advancement happens, and a retry completes the whole
/// transaction. No real crash or timing is involved.
/// </summary>
public sealed class PersistenceBackupDurabilityTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;
    private readonly string _statePath;

    public PersistenceBackupDurabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-bak-durability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new LoggingService(Path.Combine(_root, "logs"));
        _statePath = Path.Combine(_root, "state.json");
    }

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private static CapturedWindow MakeWindow(int i) => new()
    {
        Hwnd = new IntPtr(0x2000 + i),
        ProcessId = 2000u + (uint)i,
        WindowThreadId = 3000u + (uint)i,
        WindowIdentityToken = 4000 + i,
        ExePath = $"durability{i}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = "Durability " + i,
    };

    private static Group MakeGroup(string name)
    {
        var g = new Group { Name = name };
        g.Members.Add(MakeWindow(0));
        return g;
    }

    private PersistenceService CreateService(
        Func<string, byte[]>? readAllBytes = null,
        Action<string, byte[]>? writeDurableBytes = null,
        Action<string, string>? writeDurableText = null,
        Action<string, string>? atomicMove = null)
        => new(_log, _statePath, path => File.GetAttributes(path), path => File.ReadAllText(path),
            readAllBytes, writeDurableBytes, writeDurableText, atomicMove);

    private static bool ContainsName(string path, string name)
        => File.ReadAllText(path).Contains("\"" + name + "\"", StringComparison.Ordinal);

    [Fact]
    public void BackupStagingReadFailure_PreservesPreviousBackupAndPrimary_AndRetriesClean()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));
        bool failing = true;
        var persistence = CreateService(readAllBytes: path =>
        {
            if (failing && path == _statePath)
                throw new IOException("injected staging read failure");
            return File.ReadAllBytes(path);
        });

        persistence.Save(new[] { MakeGroup("v2") });

        Assert.True(ContainsName(_statePath, "v1"), "primary must be untouched after a failed backup stage");
        Assert.True(ContainsName(_statePath + ".bak", "b0"), "previous known-good backup must survive a failed backup stage");
        Assert.False(File.Exists(_statePath + ".bak.tmp"), "a read failure must not leave a candidate behind");
        Assert.False(File.Exists(_statePath + ".tmp"));

        failing = false;
        persistence.Save(new[] { MakeGroup("v2") });

        Assert.True(ContainsName(_statePath, "v2"), "retry must complete the save");
        Assert.True(ContainsName(_statePath + ".bak", "v1"), "retry must install a fresh coherent backup of the previous primary");
        Assert.False(File.Exists(_statePath + ".bak.tmp"));
    }

    [Fact]
    public void BackupCandidateFlushFailure_PreservesPreviousBackup_TruncatesStaleCandidateOnRetry()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));
        // A leftover fragment from an earlier crashed attempt must never be
        // readable as state and must be truncated by the next staging write.
        File.WriteAllText(_statePath + ".bak.tmp", "{ stale fragment");
        int failuresLeft = 1;
        var persistence = CreateService(writeDurableBytes: (path, bytes) =>
        {
            if (path.EndsWith(".bak.tmp", StringComparison.OrdinalIgnoreCase) && failuresLeft > 0)
            {
                failuresLeft--;
                throw new IOException("injected backup candidate flush failure");
            }
            WriteDurableBytesDefault(path, bytes);
        });

        persistence.Save(new[] { MakeGroup("v2") });

        Assert.True(ContainsName(_statePath, "v1"));
        Assert.True(ContainsName(_statePath + ".bak", "b0"), "flush failure must not damage the live backup");

        persistence.Save(new[] { MakeGroup("v3") });

        Assert.True(ContainsName(_statePath, "v3"));
        Assert.True(ContainsName(_statePath + ".bak", "v1"), "successful retry installs the previous primary as backup");
        Assert.False(File.Exists(_statePath + ".bak.tmp"), "stale candidate is consumed by a successful transaction");
        Assert.False(LoadMentionsStaleFragment());
    }

    [Fact]
    public void BackupInstallFailure_PreservesPreviousBackupAndPrimary_AndRetriesClean()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));
        int failuresLeft = 1;
        var persistence = CreateService(atomicMove: (sourcePath, destinationPath) =>
        {
            if (destinationPath == _statePath + ".bak" && failuresLeft > 0)
            {
                failuresLeft--;
                throw new IOException("injected backup install failure");
            }
            File.Move(sourcePath, destinationPath, overwrite: true);
        });

        persistence.Save(new[] { MakeGroup("v2") });

        Assert.True(ContainsName(_statePath, "v1"));
        Assert.True(ContainsName(_statePath + ".bak", "b0"), "failed atomic replacement leaves the previous backup usable");

        persistence.Save(new[] { MakeGroup("v3") });

        Assert.True(ContainsName(_statePath, "v3"));
        Assert.True(ContainsName(_statePath + ".bak", "v1"));
        Assert.False(File.Exists(_statePath + ".bak.tmp"));
    }

    [Fact]
    public void PrimaryTempWriteFailure_AfterBackupStage_LeavesCoherentPairAndRetries()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));
        int failuresLeft = 1;
        var persistence = CreateService(writeDurableText: (path, contents) =>
        {
            if (path == _statePath + ".tmp" && failuresLeft > 0)
            {
                failuresLeft--;
                throw new IOException("injected primary temp write failure");
            }
            File.WriteAllText(path, contents);
        });

        persistence.Save(new[] { MakeGroup("v2") });

        // The backup stage already committed: it now holds a copy of the
        // previous primary. That pair is coherent — nothing was lost.
        Assert.True(ContainsName(_statePath, "v1"), "primary untouched by its own temp-write failure");
        Assert.True(ContainsName(_statePath + ".bak", "v1"), "installed backup holds the previous primary");

        persistence.Save(new[] { MakeGroup("v3") });

        Assert.True(ContainsName(_statePath, "v3"));
        Assert.True(ContainsName(_statePath + ".bak", "v1"));
        Assert.False(File.Exists(_statePath + ".tmp"));
    }

    [Fact]
    public void PrimaryInstallFailure_IgnoresStaleTempOnLoad_AndRetriesClean()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));
        int failuresLeft = 1;
        var persistence = CreateService(atomicMove: (sourcePath, destinationPath) =>
        {
            if (destinationPath == _statePath && failuresLeft > 0)
            {
                failuresLeft--;
                File.WriteAllText(sourcePath, "{\"Groups\":[],\"Version\":2}");
                throw new IOException("injected primary install failure");
            }
            File.Move(sourcePath, destinationPath, overwrite: true);
        });

        persistence.Save(new[] { MakeGroup("v2") });

        Assert.True(ContainsName(_statePath, "v1"), "atomic install failure keeps the previous primary authoritative");
        Assert.True(File.Exists(_statePath + ".tmp"), "the interrupted attempt leaves its temp artifact behind");
        Assert.True(ContainsName(_statePath + ".bak", "v1"));

        // The stale temp fragment must not become authoritative on load.
        var reader = new PersistenceService(_log, _statePath);
        List<Group> loaded = reader.Load();
        Assert.Single(loaded);
        Assert.Equal("v1", loaded[0].Name);

        persistence.Save(new[] { MakeGroup("v3") });

        Assert.True(ContainsName(_statePath, "v3"));
        Assert.False(File.Exists(_statePath + ".tmp"), "successful install consumes the temp path");
    }

    [Fact]
    public void MissingPrimary_DoesNotDestroyExistingValidBackup()
    {
        File.WriteAllText(_statePath + ".bak", StateJson("b0"));

        var persistence = CreateService();
        persistence.Save(new[] { MakeGroup("v1") });

        Assert.True(ContainsName(_statePath, "v1"), "save writes a fresh primary");
        Assert.True(ContainsName(_statePath + ".bak", "b0"), "a missing primary must skip the backup stage, preserving the valid backup");
        Assert.False(File.Exists(_statePath + ".bak.tmp"));
    }

    [Fact]
    public void FailedSave_DoesNotAdvanceSavedContentMarker_UnchangedOptimizationStaysCorrect()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        int failuresLeft = 1;
        int moves = 0;
        var persistence = CreateService(atomicMove: (sourcePath, destinationPath) =>
        {
            if (destinationPath == _statePath && failuresLeft > 0)
            {
                failuresLeft--;
                throw new IOException("injected primary install failure");
            }
            moves++;
            File.Move(sourcePath, destinationPath, overwrite: true);
        });

        // One identical immutable snapshot for every attempt: the unchanged-save
        // optimization compares exact serialized bytes.
        var groups = new[] { MakeGroup("v2") };

        persistence.Save(groups);
        Assert.True(ContainsName(_statePath, "v1"));

        // If the failed save had falsely advanced the saved-content marker, an
        // identical retry would be skipped by the unchanged-save optimization
        // and the file would still say v1.
        persistence.Save(groups);
        Assert.True(ContainsName(_statePath, "v2"), "identical retry after a failed save must still reach disk");

        // Once the marker genuinely matches disk, an identical save skips I/O.
        int movesAfterSuccess = moves;
        Assert.True(movesAfterSuccess > 0);
        persistence.Save(groups);
        Assert.Equal(movesAfterSuccess, moves);
        Assert.True(ContainsName(_statePath, "v2"));
    }

    [Fact]
    public async Task LatestWinsGeneration_Intact_AcrossFailedSaves()
    {
        File.WriteAllText(_statePath, StateJson("v1"));
        int failOnce = 1;
        var persistence = CreateService(atomicMove: (sourcePath, destinationPath) =>
        {
            if (destinationPath == _statePath && System.Threading.Interlocked.Exchange(ref failOnce, 0) == 1)
                throw new IOException("injected first-generation install failure");
            File.Move(sourcePath, destinationPath, overwrite: true);
        });

        persistence.SaveAsync(new[] { MakeGroup("async-a") });
        await persistence.WhenWritesSettledAsync();
        persistence.SaveAsync(new[] { MakeGroup("async-b") });
        await persistence.WhenWritesSettledAsync();

        Assert.True(ContainsName(_statePath, "async-b"), "latest generation must win even after an earlier failure");
        Assert.False(File.Exists(_statePath + ".tmp"));
        Assert.True(ContainsName(_statePath + ".bak", "v1"), "backup holds the pre-campaign primary from the winning transaction");
    }

    private static string StateJson(string name)
        => "{\"Version\":2,\"Groups\":[{\"Id\":\""
           + Guid.NewGuid().ToString("D")
           + "\",\"Name\":\"" + name + "\",\"Tabs\":[{\"ExePath\":\"x.exe\"}]}]}";

    private static void WriteDurableBytesDefault(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private bool LoadMentionsStaleFragment()
    {
        var reader = new PersistenceService(_log, _statePath);
        foreach (Group group in reader.Load())
        {
            if (group.Name.Contains("stale", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
