using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;
using static TabDock.UnitTests.TestInfrastructure.PendingRecoveryTestHarness;

namespace TabDock.UnitTests;

/// <summary>
/// A10 regression coverage: every inspection entry point (default discovery,
/// the pending CLI report, launcher attention, count projections) must be
/// strictly read-only over recovery storage, including aged pending-prefix
/// temp fragments. Eligible stale-temp hygiene exists only as an explicit
/// mutating API for lease-held recovery/startup paths.
/// </summary>
public class PendingRecoveryInspectionNonMutationTests
{
    private sealed record FixtureFile(byte[] Bytes, DateTime LastWriteTimeUtc);

    private static Dictionary<string, FixtureFile> Snapshot(string root)
        => Directory.EnumerateFiles(root)
            .ToDictionary(
                path => Path.GetFileName(path),
                path => new FixtureFile(File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)),
                StringComparer.OrdinalIgnoreCase);

    private static void AssertTreeUnchanged(Dictionary<string, FixtureFile> before, string root)
    {
        Dictionary<string, FixtureFile> after = Snapshot(root);
        Assert.Equal(
            before.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            after.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        foreach ((string name, FixtureFile file) in before)
        {
            Assert.Equal(file.Bytes, after[name].Bytes);
            Assert.Equal(file.LastWriteTimeUtc, after[name].LastWriteTimeUtc);
        }
    }

    /// <summary>
    /// Complete evidence tree: valid pending source, aged and fresh
    /// pending-prefix temp fragments, a sidecar ledger whose source is alive,
    /// malformed pending evidence and unrelated files.
    /// </summary>
    private static void WriteFixture(string root)
    {
        File.WriteAllText(
            Path.Combine(root, "hidden-windows.json.pending"),
            JournalJson(2, EntryV2(1550, 176, "fixture.exe", 17601)));

        string agedTmp = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
        File.WriteAllText(agedTmp, "{ partial write");
        File.SetLastWriteTimeUtc(agedTmp, DateTime.UtcNow - TimeSpan.FromHours(25));

        File.WriteAllText(Path.Combine(root, "hidden-windows.json.pending.002.recovered.tmp"), "{ partial write");

        File.WriteAllText(
            Path.Combine(root, "hidden-windows.json.pending.recovered"),
            "{\"Resolutions\":[],\"Transactions\":[]}");

        File.WriteAllText(Path.Combine(root, "hidden-windows.json.pending.003"), "not-json");

        File.WriteAllText(Path.Combine(root, "state.json"), "{\"Version\":2,\"Groups\":[]}");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "user notes");
    }

    [Fact]
    public void DefaultDiscovery_PreservesAgedTemporaryFragmentAndAllEvidence()
    {
        string root = CreateRoot();
        try
        {
            WriteFixture(root);
            var api = new FakePendingApi();
            Dictionary<string, FixtureFile> before = Snapshot(root);

            // Default discovery: no read-only opt-in, exactly what doctor and
            // the pending CLI report invoke today.
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);

            Assert.Null(catalog.Error);
            AssertTreeUnchanged(before, root);
            Assert.Equal(0, api.MutationCount);
            Assert.Equal(0, api.RemovePropertyCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void FullInspectionSurface_PreservesFixtureTreeAndPerformsNoNativeRecoveryMutation()
    {
        string root = CreateRoot();
        try
        {
            WriteFixture(root);
            var api = new FakePendingApi();
            Dictionary<string, FixtureFile> before = Snapshot(root);

            // Every read-only caller: default discovery (doctor, support
            // export), the pending CLI report, launcher attention projection
            // and the internal active-count helper.
            PendingRecoveryService.Discover(root, api);
            string report = PendingRecoveryService.FormatDiscovery(root);
            Assert.Contains("readOnly: true", report, StringComparison.Ordinal);
            PendingRecoveryService.GetLauncherAttention(root);
            PendingRecoveryService.CountActivePendingFiles(root);

            AssertTreeUnchanged(before, root);
            Assert.Equal(0, api.MutationCount);
            Assert.Equal(0, api.PlacementCount);
            Assert.Equal(0, api.ShowCount);
            Assert.Equal(0, api.TransitionCount);
            Assert.Equal(0, api.RemovePropertyCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void RepeatedInspection_RemainsReadOnlyAcrossCalls()
    {
        string root = CreateRoot();
        try
        {
            WriteFixture(root);
            var api = new FakePendingApi();
            PendingRecoveryService.Discover(root, api);
            Dictionary<string, FixtureFile> before = Snapshot(root);

            for (int i = 0; i < 3; i++)
                PendingRecoveryService.Discover(root, api);

            AssertTreeUnchanged(before, root);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ExplicitHygiene_DeletesOnlyEligibleAgedPendingTemporaryFragments()
    {
        string root = CreateRoot();
        try
        {
            WriteFixture(root);
            Dictionary<string, FixtureFile> before = Snapshot(root);

            PendingRecoveryService.RunTemporaryFileHygiene(root);

            string agedTmp = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
            Assert.False(File.Exists(agedTmp));
            // Everything outside the bounded age/name eligibility policy is
            // byte-for-byte retained: fresh temps, pending sources, sidecar
            // ledgers, malformed evidence and unrelated files.
            foreach ((string name, FixtureFile file) in before)
            {
                if (string.Equals(name, Path.GetFileName(agedTmp), StringComparison.OrdinalIgnoreCase))
                    continue;
                Assert.True(File.Exists(Path.Combine(root, name)), name + " must be retained");
                Assert.Equal(file.Bytes, File.ReadAllBytes(Path.Combine(root, name)));
            }
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ExplicitHygiene_MissingOrUnreadableRootPerformsNoFallbackCleanup()
    {
        string root = CreateRoot();
        string parent = Path.GetDirectoryName(root)!;
        try
        {
            WriteFixture(root);
            string agedTmp = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
            Dictionary<string, FixtureFile> before = Snapshot(root);

            // A refused/unavailable root must not trigger cleanup anywhere:
            // the explicit hygiene call degrades without deletion and there is
            // no discovery fallback to clean behind it.
            PendingRecoveryService.RunTemporaryFileHygiene(Path.Combine(parent, "missing-" + Guid.NewGuid().ToString("N")));
            PendingRecoveryService.RunTemporaryFileHygiene(Path.Combine(root, "notes.txt"));

            AssertTreeUnchanged(before, root);
            Assert.True(File.Exists(agedTmp));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void SupervisedRecoverySession_RunsExplicitHygieneOnItsMutatingPath()
    {
        string root = CreateRoot();
        try
        {
            string agedTmp = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
            File.WriteAllText(agedTmp, "{ partial write");
            File.SetLastWriteTimeUtc(agedTmp, DateTime.UtcNow - TimeSpan.FromHours(25));

            // The supervised recovery session is the lease-held mutating path
            // where eligible temp hygiene now belongs (the caller acquires the
            // product-mutation lease before invoking it).
            var output = new StringWriter();
            PendingRecoveryService.RunInteractive(new StringReader(string.Empty), output, root, new FakePendingApi());

            Assert.False(File.Exists(agedTmp), "the mutating supervised session performs eligible temp hygiene");
        }
        finally { DeleteRoot(root); }
    }
}
