using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic >=1000-mutation persistence soak for the single-writer gate.
/// Complements PersistenceSingleWriterTests with two heavier cases: a
/// 1000-submission burst whose final on-disk bytes must exactly equal a
/// freshly serialized snapshot of the final logical state, and a
/// mid-burst state-directory deletion that must still converge to the
/// complete final file (CommitJson recreates the directory). Both use the
/// same durable-write settings production uses; nothing is weakened here.
/// </summary>
[Collection("PersistenceSingleWriter")]
public class PersistenceSoakTortureTests
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

    /// <summary>
    /// The state file lives in its own subdirectory so a mid-burst deletion
    /// of the state directory never races the LoggingService's open log file.
    /// </summary>
    private static (string rootDir, string stateDir, string path, PersistenceService service) MakeService()
    {
        string rootDir = Path.Combine(Path.GetTempPath(), "tabdock-soak-" + Guid.NewGuid().ToString("N"));
        string stateDir = Path.Combine(rootDir, "state");
        Directory.CreateDirectory(stateDir);
        string path = Path.Combine(stateDir, "state.json");
        var log = new LoggingService(Path.Combine(rootDir, "logs"));
        return (rootDir, stateDir, path, new PersistenceService(log, path));
    }

    /// <summary>
    /// Builds the exact JSON PersistenceService produces for one group with
    /// one live member, using the same source-generated serializer the
    /// production write path uses (TabDockJsonContext.Default.PersistedState).
    /// </summary>
    private static string SerializeExpected(Group g)
    {
        var pg = new PersistedGroup
        {
            Id = g.Id,
            Name = g.Name,
            AccentColor = g.AccentColor,
            ActiveIndex = g.Members.Count > 0 ? g.ActiveIndex : g.PersistedActiveIndex,
        };
        foreach (CapturedWindow m in g.Members)
        {
            pg.Tabs.Add(new PersistedTab
            {
                ExePath = m.ExePath,
                OriginalTitle = m.OriginalTitle,
                CustomLabel = m.CustomLabel,
                Left = m.OriginalBounds.left,
                Top = m.OriginalBounds.top,
                Right = m.OriginalBounds.right,
                Bottom = m.OriginalBounds.bottom,
                WasMaximized = m.WasMaximized,
            });
        }

        var expectedState = new PersistedState { Version = PersistedState.CurrentVersion };
        expectedState.Groups.Add(pg);
        return JsonSerializer.Serialize(expectedState, TabDockJsonContext.Default.PersistedState);
    }

    private static void AssertNoTempFiles(string stateDir)
    {
        string[] temps = Directory.GetFiles(stateDir, "*.tmp");
        Assert.True(temps.Length == 0, $"stray .tmp files left behind: {string.Join(", ", temps)}");
    }

    [Fact]
    public async Task Soak_1000DistinctSaves_FinalBytesEqualFinalLogicalState()
    {
        var (rootDir, stateDir, path, persistence) = MakeService();
        try
        {
            const int count = 1000;
            Group finalGroup = MakeGroup(count - 1, "soak-" + (count - 1));

            for (int i = 0; i < count; i++)
            {
                // Distinct payload per submission: the counter is embedded in
                // the group name so every generation differs on disk.
                persistence.SaveAsync(new[] { i == count - 1 ? finalGroup : MakeGroup(i, "soak-" + i) });
            }

            await persistence.WhenWritesSettledAsync();

            Assert.True(File.Exists(path), "state.json missing after 1000-save soak");
            AssertNoTempFiles(stateDir);

            // Byte-compare against a freshly serialized snapshot of the FINAL
            // logical state, built through the same public serializer the
            // production write path uses. Latest-wins means exactly this
            // payload — not merely a parseable superset — may be on disk.
            string actualJson = File.ReadAllText(path);
            string expectedJson = SerializeExpected(finalGroup);
            Assert.Equal(expectedJson, actualJson);

            // A subsequent save + barrier round-trips cleanly and leaves the
            // identical durable content (same logical state => same bytes).
            persistence.SaveAsync(new[] { finalGroup });
            await persistence.WhenWritesSettledAsync();
            AssertNoTempFiles(stateDir);
            Assert.Equal(expectedJson, File.ReadAllText(path));
        }
        finally
        {
            TryCleanup(rootDir);
        }
    }

    [Fact]
    public async Task Soak_StateDirectoryDeletedMidBurst_BarrierStillProducesCompleteFinalFile()
    {
        var (rootDir, stateDir, path, persistence) = MakeService();
        try
        {
            const int count = 1000;
            const int deleteAfter = 500;
            Group finalGroup = MakeGroup(count - 1, "burst-" + (count - 1));
            bool deleted = false;

            for (int i = 0; i < count; i++)
            {
                persistence.SaveAsync(new[] { i == count - 1 ? finalGroup : MakeGroup(i, "burst-" + i) });

                if (i == deleteAfter - 1 && !deleted)
                {
                    // External deletion mid-burst, while writes are still
                    // settling. CommitJson must recreate the directory and
                    // the latest generation must still land complete. The
                    // delete itself races in-flight CommitJson file handles
                    // (primary→.bak copy), so tolerate transient sharing
                    // violations with a bounded retry — the injected fault is
                    // "directory gone at an arbitrary point", not "delete
                    // must win on the first attempt".
                    deleted = true;
                    DeleteDirectoryWithTransientRetry(stateDir);
                }
            }

            Assert.True(deleted, "mid-burst deletion never ran");

            await persistence.WhenWritesSettledAsync();

            Assert.True(File.Exists(path), "state.json missing after mid-burst directory deletion");
            AssertNoTempFiles(stateDir);

            string actualJson = File.ReadAllText(path);
            Assert.Equal(SerializeExpected(finalGroup), actualJson);

            // Post-deletion save + barrier round-trips cleanly too.
            persistence.SaveAsync(new[] { finalGroup });
            await persistence.WhenWritesSettledAsync();
            AssertNoTempFiles(stateDir);
            Assert.Equal(SerializeExpected(finalGroup), File.ReadAllText(path));
        }
        finally
        {
            TryCleanup(rootDir);
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

    private static void DeleteDirectoryWithTransientRetry(string dir)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(10);
            }
        }
    }
}
