using System;
using System.IO;
using System.Text.Json.Nodes;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;
using static TabDock.UnitTests.TestInfrastructure.PendingRecoveryTestHarness;

namespace TabDock.UnitTests;

/// <summary>
/// Launcher projection coverage for pending recovery. These tests deliberately
/// use the real catalog parser and inspect the filesystem before/after the
/// projection so the banner cannot acquire an accidental mutation path.
/// </summary>
public sealed class PendingRecoveryAttentionTests
{
    [Fact]
    public void NoPendingFiles_HasNoAttention()
    {
        string root = CreateRoot();
        try
        {
            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.False(attention.HasAttention);
            Assert.Equal(0, attention.PendingFileCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OneUnresolvedFile_ProjectsOneAttentionItem()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(1, EntryV1(100, 41, "legacy.exe")));

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.True(attention.HasAttention);
            Assert.Equal(1, attention.PendingFileCount);
            Assert.Contains("1 pending recovery item", attention.SummaryText, StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MultipleUnresolvedFiles_CountEachFileOnce()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(1, EntryV1(101, 42, "one.exe")));
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending.001"),
                JournalJson(1, EntryV1(102, 43, "two.exe"), EntryV1(103, 44, "three.exe")));

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.Equal(2, attention.PendingFileCount);
            Assert.Contains("2 pending recovery items", attention.SummaryText, StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void EmptyResolvedSource_IsNotAttention()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                "{\"Version\":2,\"Entries\":[]}");

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.False(attention.HasAttention);
            Assert.Equal(0, attention.PendingFileCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void FullyResolvedEvidenceFile_IsNotAttention()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(105, 46, "resolved.exe", 4601)));

            PendingRecoveryEntry entry = PendingRecoveryService
                .Discover(root)
                .Files[0]
                .Entries[0];
            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["EntryFingerprint"] = entry.EntryFingerprint,
                        ["SchemaVersion"] = entry.Version,
                        ["ResolvedUtc"] = "2026-08-23T00:00:00+00:00",
                        ["Result"] = "presentation-restored",
                    },
                },
            };
            File.WriteAllText(path + ".recovered", ledger.ToJsonString());

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.False(attention.HasAttention);
            Assert.Equal(0, attention.PendingFileCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void CorruptEvidence_RemainsAttention()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, "{not-json");

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.True(attention.HasAttention);
            Assert.Equal(1, attention.PendingFileCount);
            Assert.True(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void UnreadableRoot_RemainsAttentionWithoutInventingAZero()
    {
        string root = CreateRoot();
        string notDirectory = Path.Combine(root, "not-a-directory");
        try
        {
            File.WriteAllText(notDirectory, "evidence");

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(notDirectory);

            Assert.True(attention.HasAttention);
            Assert.True(attention.InspectionFailed);
            Assert.Equal(0, attention.PendingFileCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void LauncherProjection_IsReadOnlyAndDoesNotSweepTemporaryFragments()
    {
        string root = CreateRoot();
        try
        {
            string pending = Path.Combine(root, "hidden-windows.json.pending");
            string staleTemporary = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
            File.WriteAllText(pending, JournalJson(1, EntryV1(104, 45, "unchanged.exe")));
            File.WriteAllText(staleTemporary, "partial evidence fragment");
            File.SetLastWriteTimeUtc(staleTemporary, DateTime.UtcNow - TimeSpan.FromHours(25));
            byte[] pendingBefore = File.ReadAllBytes(pending);
            byte[] temporaryBefore = File.ReadAllBytes(staleTemporary);

            PendingRecoveryAttention attention = PendingRecoveryService.GetLauncherAttention(root);

            Assert.True(attention.HasAttention);
            Assert.Equal(pendingBefore, File.ReadAllBytes(pending));
            Assert.Equal(temporaryBefore, File.ReadAllBytes(staleTemporary));
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-recovery-attention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
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
}
