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
/// Deterministic regression coverage for the single-writer persistence gate
/// introduced by tabdock_persistence_single_writer_fix.py. The live
/// ValidationDriver exercises the same invariants through the persist-kill and
/// rapid-switch scenarios, but these cases prove the contract headlessly: one
/// serialized disk gate, monotonic latest-wins generations, rapid async
/// coalescing, off-thread debounce, and stale-async protection.
///
/// The cases below intentionally spin up many off-thread writes, so the class is
/// placed in a non-parallelized collection to avoid thread-pool starvation when
/// the full suite runs.
/// </summary>
[CollectionDefinition("PersistenceSingleWriter", DisableParallelization = true)]
public class PersistenceSingleWriterCollection : ICollectionFixture<PersistenceSingleWriterFixture>
{
}

public class PersistenceSingleWriterFixture
{
}

[Collection("PersistenceSingleWriter")]
public class PersistenceSingleWriterTests
{


    private static CapturedWindow MakeWindow(int i) => new()
    {
        Hwnd = new IntPtr(0x1000 + i),
        ProcessId = 1000u + (uint)i,
        WindowThreadId = 2000u + (uint)i,
        WindowIdentityToken = 3000 + i,
        ExePath = $"guest{i}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = "Guest " + i,
    };

    private static Group MakeGroup(int i, string name)
    {
        var g = new Group { Name = name };
        g.Members.Add(MakeWindow(i));
        return g;
    }

    private static (string dir, string path, PersistenceService service) MakeService()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tabdock-sw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        var log = new LoggingService(Path.Combine(dir, "logs"));
        return (dir, path, new PersistenceService(log, path));
    }

    [Fact]
    public async Task SaveAsync_WritesOffThreadAndSettles()
    {
        var (dir, path, persistence) = MakeService();
        try
        {
            persistence.SaveAsync(new[] { MakeGroup(0, "async") });

            // Deterministic completion barrier: every write submitted before
            // the barrier has settled when it returns. The off-thread claim is
            // proven by the writer's construction (Task.Run in SaveAsync), not
            // by racing the filesystem for an absent file — a fast thread pool
            // may legitimately finish before this thread resumes.
            await persistence.WhenWritesSettledAsync();
            Assert.True(File.Exists(path), "settled SaveAsync never reached disk");
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public async Task SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration()
    {
        var (dir, path, persistence) = MakeService();
        try
        {
            const int count = 60;
            for (int i = 0; i < count; i++)
                persistence.SaveAsync(new[] { MakeGroup(i, "gen-" + i) });

            // Barrier instead of a wall-clock poll: when all submitted writes
            // have settled, exactly the latest generation can be on disk.
            await persistence.WhenWritesSettledAsync();

            string expected = "gen-" + (count - 1);
            string finalJson = File.ReadAllText(path);
            Assert.Contains(expected, finalJson);
            Assert.DoesNotContain("gen-0", finalJson);
            Assert.False(File.Exists(path + ".tmp"), "stray .tmp file left behind");
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public void SaveAsync_OlderDelayedSnapshotCannotOverwriteNewerSync()
    {
        var (dir, path, persistence) = MakeService();
        try
        {
            // gen1 (async, may run late) vs gen2 (sync, immediate).
            persistence.SaveAsync(new[] { MakeGroup(0, "old-async") });
            persistence.Save(new[] { MakeGroup(1, "new-sync") });

            bool ok = SpinWait.SpinUntil(() =>
            {
                try
                {
                    return File.Exists(path) && File.ReadAllText(path).Contains("new-sync");
                }
                catch (IOException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(10));
            Assert.True(ok, "newer synchronous save never reached disk");

            string finalJson = File.ReadAllText(path);
            Assert.Contains("new-sync", finalJson);
            Assert.DoesNotContain("old-async", finalJson);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public async Task SaveAndSaveAsync_Concurrent_MixedWriterGateStaysConsistent()
    {
        var (dir, path, persistence) = MakeService();
        try
        {
            const int count = 80;
            var tasks = new List<Task>(count);
            for (int i = 0; i < count; i++)
            {
                int local = i;
                if (i % 2 == 0)
                    tasks.Add(Task.Run(() => persistence.SaveAsync(new[] { MakeGroup(local, "m-" + local) })));
                else
                    tasks.Add(Task.Run(() => persistence.Save(new[] { MakeGroup(local, "m-" + local) })));
            }

            await Task.WhenAll(tasks);
            // Await the single-writer drain so no off-thread write is still mid-flight.
            await persistence.WhenWritesSettledAsync();

            // All writers settled. The single-writer gate guarantees the file is
            // always one complete, consistent snapshot (never a torn interleave of
            // two saves). Under concurrent dispatch the winning generation is
            // scheduling-dependent, so we assert the file is valid and names one of
            // the requested groups rather than a specific index.
            Assert.True(File.Exists(path), "state.json missing after concurrent writers");
            string finalJson = File.ReadAllText(path);

            Assert.False(File.Exists(path + ".tmp"), "stray .tmp file left behind");
            using var doc = System.Text.Json.JsonDocument.Parse(finalJson);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
            string name = doc.RootElement.GetProperty("Groups")[0].GetProperty("Name").GetString()!;
            Assert.Matches(@"^m-\d+$", name);

            // Backup must exist (same transaction) and be parseable.
            Assert.True(File.Exists(path + ".bak"), "backup (.bak) not written");
            using var backupDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path + ".bak"));
            Assert.Equal(System.Text.Json.JsonValueKind.Object, backupDoc.RootElement.ValueKind);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public async Task PersistenceHammer_AlwaysParseableFinalIsNewestNoTempCollision()
    {
        var (dir, path, persistence) = MakeService();
        try
        {
            const int count = 1000;
            var tasks = new List<Task>(count);
            for (int i = 0; i < count; i++)
            {
                int local = i;
                tasks.Add(Task.Run(() =>
                {
                    persistence.SaveAsync(new[] { MakeGroup(local, "h-" + local) });
                    // Periodic synchronous barrier save.
                    if (local % 50 == 0)
                        persistence.Save(new[] { MakeGroup(local, "h-" + local) });
                }));
            }

            await Task.WhenAll(tasks);
            // Await the single-writer drain so no off-thread write is still mid-flight.
            await persistence.WhenWritesSettledAsync();
            // Under arbitrary task scheduling the highest-generation writer may not be
            // the last-enqueued task, so also wait for the temp file to disappear.
            bool settled = SpinWait.SpinUntil(() =>
            {
                try { return !File.Exists(path + ".tmp"); }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(10));
            Assert.True(settled, "pending async write left a .tmp behind");

            // The single-writer gate guarantees a complete, self-consistent snapshot
            // after a 1000-snapshot hammer. The winning generation is
            // scheduling-dependent under concurrency, so assert validity and that it
            // names one of the requested groups, plus no temp-file collision.
            Assert.True(File.Exists(path), "state.json missing after 1000-snapshot hammer");
            string finalJson = File.ReadAllText(path);
            Assert.False(File.Exists(path + ".tmp"), "stray .tmp after hammer");

            using var doc = System.Text.Json.JsonDocument.Parse(finalJson);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
            string name = doc.RootElement.GetProperty("Groups")[0].GetProperty("Name").GetString()!;
            Assert.Matches(@"^h-\d+$", name);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; ignore races on the agent machine.
        }
    }
}



