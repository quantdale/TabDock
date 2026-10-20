using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic interleaving coverage for audit finding A04. The unchanged-save
/// optimization used to compare the caller's snapshot with the last completed
/// disk content before claiming a request generation, so returning to the last
/// saved state while a different async write was in flight silently kept the
/// stale async snapshot. Each case holds the older write at an injected I/O
/// boundary and releases it from the test coordinator, never from the blocked
/// writer.
/// </summary>
[Collection("PersistenceSingleWriter")]
public sealed class PersistenceLatestRequestWinsTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;
    private readonly string _statePath;

    public PersistenceLatestRequestWinsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-latest-wins-" + Guid.NewGuid().ToString("N"));
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
        catch
        {
        }
    }

    private static CapturedWindow MakeWindow(int i) => new()
    {
        Hwnd = new IntPtr(0x4000 + i),
        ProcessId = 4000u + (uint)i,
        WindowThreadId = 5000u + (uint)i,
        WindowIdentityToken = 6000 + i,
        ExePath = $"latest{i}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = "Latest " + i,
    };

    private static Group MakeGroup(int i, string name)
    {
        var g = new Group { Name = name };
        g.Members.Add(MakeWindow(i));
        return g;
    }

    private PersistenceService CreateService(
        Func<string, FileAttributes>? getAttributes = null,
        Action<string, byte[]>? writeDurableBytes = null,
        Action<string, string>? writeDurableText = null,
        Action<string, string>? atomicMove = null)
        => new(_log, _statePath,
            getAttributes ?? (path => File.GetAttributes(path)),
            path => File.ReadAllText(path),
            null,
            writeDurableBytes,
            writeDurableText,
            atomicMove);

    private static bool ContainsName(string path, string name)
        => File.Exists(path)
           && File.ReadAllText(path).Contains("\"" + name + "\"", StringComparison.Ordinal);

    [Fact]
    public async Task SynchronousReturnToA_SupersedesInFlightB()
    {
        using var bEntered = new ManualResetEventSlim(false);
        using var releaseB = new ManualResetEventSlim(false);
        using var aAdmitted = new ManualResetEventSlim(false);
        int signalAdmission = 0;

        var persistence = CreateService(
            getAttributes: path =>
            {
                if (Volatile.Read(ref signalAdmission) == 1)
                    aAdmitted.Set();
                return File.GetAttributes(path);
            },
            writeDurableText: (path, contents) =>
            {
                if (contents.Contains("\"in-flight-B\"", StringComparison.Ordinal))
                {
                    bEntered.Set();
                    if (!releaseB.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("coordinator did not release the in-flight async write");
                }
                File.WriteAllText(path, contents);
            });

        var groupA = MakeGroup(0, "saved-A");
        var groupB = MakeGroup(1, "in-flight-B");
        persistence.Save(new[] { groupA });
        persistence.SaveAsync(new[] { groupB });
        Assert.True(bEntered.Wait(TimeSpan.FromSeconds(10)), "async B never entered its write transaction");

        Volatile.Write(ref signalAdmission, 1);
        Task syncA = Task.Run(() => persistence.Save(new[] { groupA }));
        Assert.True(aAdmitted.Wait(TimeSpan.FromSeconds(10)), "synchronous A was suppressed before admission");
        Volatile.Write(ref signalAdmission, 0);

        releaseB.Set();
        await syncA.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(ContainsName(_statePath, "saved-A"), "a synchronous return to the last saved state must supersede in-flight B");
        Assert.False(ContainsName(_statePath, "in-flight-B"));
    }

    [Fact]
    public async Task AsynchronousReturnToA_SettlesToA()
    {
        using var bEntered = new ManualResetEventSlim(false);
        using var releaseB = new ManualResetEventSlim(false);
        using var aAdmitted = new ManualResetEventSlim(false);
        int signalAdmission = 0;

        var persistence = CreateService(
            getAttributes: path =>
            {
                if (Volatile.Read(ref signalAdmission) == 1)
                    aAdmitted.Set();
                return File.GetAttributes(path);
            },
            writeDurableText: (path, contents) =>
            {
                if (contents.Contains("\"in-flight-B\"", StringComparison.Ordinal))
                {
                    bEntered.Set();
                    if (!releaseB.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("coordinator did not release the in-flight async write");
                }
                File.WriteAllText(path, contents);
            });

        var groupA = MakeGroup(0, "saved-A");
        var groupB = MakeGroup(1, "in-flight-B");
        persistence.Save(new[] { groupA });
        persistence.SaveAsync(new[] { groupB });
        Assert.True(bEntered.Wait(TimeSpan.FromSeconds(10)), "async B never entered its write transaction");

        Volatile.Write(ref signalAdmission, 1);
        persistence.SaveAsync(new[] { groupA });
        Volatile.Write(ref signalAdmission, 0);
        Assert.True(aAdmitted.IsSet, "asynchronous A was suppressed before admission");

        releaseB.Set();
        await persistence.WhenWritesSettledAsync();

        Assert.True(ContainsName(_statePath, "saved-A"), "the latest admitted async request must leave A on disk");
        Assert.False(ContainsName(_statePath, "in-flight-B"));
        Assert.False(File.Exists(_statePath + ".tmp"), "stray .tmp left behind");
    }

    [Fact]
    public async Task RequestHeldBeforeAdmission_DoesNotBlockLaterAdmittedSave()
    {
        using var bBuilding = new ManualResetEventSlim(false);
        using var releaseBuild = new ManualResetEventSlim(false);
        int holdB = 0;

        var persistence = CreateService(getAttributes: path =>
        {
            if (Volatile.Read(ref holdB) == 1)
            {
                bBuilding.Set();
                if (!releaseBuild.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("coordinator did not release the pre-admission build");
            }
            return File.GetAttributes(path);
        });

        persistence.Save(new[] { MakeGroup(0, "saved-A") });

        Volatile.Write(ref holdB, 1);
        Task buildB = Task.Run(() => persistence.SaveAsync(new[] { MakeGroup(1, "later-B") }));
        Assert.True(bBuilding.Wait(TimeSpan.FromSeconds(10)), "B never reached its pre-admission snapshot build");
        Volatile.Write(ref holdB, 0);

        persistence.Save(new[] { MakeGroup(2, "later-C") });
        Assert.True(ContainsName(_statePath, "later-C"));

        releaseBuild.Set();
        await buildB.WaitAsync(TimeSpan.FromSeconds(10));
        await persistence.WhenWritesSettledAsync();

        Assert.True(ContainsName(_statePath, "later-B"), "the request admitted last must become the latest durable snapshot");
    }

    [Fact]
    public async Task ReturnToAThenC_SettlesToLatestAdmittedC()
    {
        using var bEntered = new ManualResetEventSlim(false);
        using var releaseB = new ManualResetEventSlim(false);

        var persistence = CreateService(writeDurableText: (path, contents) =>
        {
            if (contents.Contains("\"in-flight-B\"", StringComparison.Ordinal))
            {
                bEntered.Set();
                if (!releaseB.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("coordinator did not release the in-flight async write");
            }
            File.WriteAllText(path, contents);
        });

        var groupA = MakeGroup(0, "saved-A");
        persistence.Save(new[] { groupA });
        persistence.SaveAsync(new[] { MakeGroup(1, "in-flight-B") });
        Assert.True(bEntered.Wait(TimeSpan.FromSeconds(10)), "async B never entered its write transaction");

        persistence.SaveAsync(new[] { groupA });
        persistence.SaveAsync(new[] { MakeGroup(2, "final-C") });

        releaseB.Set();
        await persistence.WhenWritesSettledAsync();

        Assert.True(ContainsName(_statePath, "final-C"), "the last admitted request must win the settled sequence");
        Assert.False(ContainsName(_statePath, "in-flight-B"));
        Assert.False(ContainsName(_statePath, "saved-A"));
    }

    [Fact]
    public async Task IdleUnchangedRequest_DoesNotReplacePrimaryOrBackup()
    {
        int moves = 0;
        var persistence = CreateService(atomicMove: (sourcePath, destinationPath) =>
        {
            Interlocked.Increment(ref moves);
            File.Move(sourcePath, destinationPath, overwrite: true);
        });

        persistence.Save(new[] { MakeGroup(9, "seed") });
        var groups = new[] { MakeGroup(0, "idle-A") };
        persistence.Save(groups);
        byte[] primary = File.ReadAllBytes(_statePath);
        byte[] backup = File.ReadAllBytes(_statePath + ".bak");
        int movesAfterInitial = Volatile.Read(ref moves);

        persistence.Save(groups);
        persistence.SaveAsync(groups);
        await persistence.WhenWritesSettledAsync();

        Assert.Equal(movesAfterInitial, Volatile.Read(ref moves));
        Assert.Equal(primary, File.ReadAllBytes(_statePath));
        Assert.Equal(backup, File.ReadAllBytes(_statePath + ".bak"));
    }

    /// <summary>
    /// A04 failure/retry contract: after a failed final primary install, the
    /// SAME snapshot must be retried once the fault is removed. The fixture
    /// reuses one Group instance with a fixed Id across both attempts and
    /// captures the exact intended primary JSON per attempt, so the two
    /// attempts are required to be byte-identical. A prematurely advanced
    /// completed-content marker would make the identical retry disappear
    /// through unchanged-content suppression; the install-attempt count and
    /// installed bytes below fail in that case.
    /// </summary>
    [Fact]
    public async Task LatestSnapshot_RetriesAfterFailedFinalWrite()
    {
        int failPrimaryInstall = 0;
        int primaryInstallAttempts = 0;
        var intendedPrimaryJson = new List<string>();
        Guid retryId = new("0f9a2b1c-4d5e-4f60-8a71-2b3c4d5e6f70");
        var retryGroup = MakeGroup(1, "retry-B");
        retryGroup.Id = retryId;

        var persistence = CreateService(
            writeDurableText: (path, contents) =>
            {
                lock (intendedPrimaryJson)
                    intendedPrimaryJson.Add(contents);
                File.WriteAllText(path, contents);
            },
            atomicMove: (sourcePath, destinationPath) =>
            {
                if (destinationPath == _statePath)
                {
                    Interlocked.Increment(ref primaryInstallAttempts);
                    if (Interlocked.Exchange(ref failPrimaryInstall, 0) == 1)
                        throw new IOException("injected final primary install failure");
                }
                File.Move(sourcePath, destinationPath, overwrite: true);
            });

        // Two prior complete saves so both a primary and a backup snapshot
        // exist before the faulted attempt.
        persistence.Save(new[] { MakeGroup(0, "saved-seed") });
        persistence.Save(new[] { MakeGroup(0, "saved-A") });
        byte[] previousPrimary = File.ReadAllBytes(_statePath);
        Assert.Equal(2, Volatile.Read(ref primaryInstallAttempts));

        Interlocked.Exchange(ref failPrimaryInstall, 1);
        persistence.SaveAsync(new[] { retryGroup });
        await persistence.WhenWritesSettledAsync();

        // The failed attempt must not advance the durable snapshot or leave
        // partial evidence: primary stays byte-identical and the backup holds
        // a complete copy of the previous state.
        Assert.Equal(previousPrimary, File.ReadAllBytes(_statePath));
        Assert.Equal(previousPrimary, File.ReadAllBytes(_statePath + ".bak"));
        Assert.Equal(3, Volatile.Read(ref primaryInstallAttempts));
        lock (intendedPrimaryJson)
        {
            Assert.Equal(3, intendedPrimaryJson.Count);
            Assert.Contains("retry-B", intendedPrimaryJson[2], StringComparison.Ordinal);
            Assert.Contains(retryId.ToString("D"), intendedPrimaryJson[2], StringComparison.OrdinalIgnoreCase);
        }

        persistence.SaveAsync(new[] { retryGroup });
        await persistence.WhenWritesSettledAsync();

        lock (intendedPrimaryJson)
        {
            Assert.Equal(4, intendedPrimaryJson.Count);
            // Identical intended snapshot across failure/retry: same object,
            // same fixed Id, byte-identical serialized JSON.
            Assert.Equal(intendedPrimaryJson[2], intendedPrimaryJson[3]);
        }
        // The identical retry performed a real install instead of vanishing
        // through unchanged-content suppression.
        Assert.Equal(4, Volatile.Read(ref primaryInstallAttempts));
        string installed = File.ReadAllText(_statePath);
        lock (intendedPrimaryJson)
            Assert.Equal(intendedPrimaryJson[3], installed);
        Assert.Contains("retry-B", installed, StringComparison.Ordinal);
        Assert.DoesNotContain("saved-A", installed, StringComparison.Ordinal);
        // The retry's backup stage preserved the previous complete state.
        Assert.Equal(previousPrimary, File.ReadAllBytes(_statePath + ".bak"));
        Assert.False(File.Exists(_statePath + ".tmp"));
        Assert.False(File.Exists(_statePath + ".bak.tmp"));
    }
}
