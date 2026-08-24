using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class ResourceLifecycleQualificationTests
{
    [Fact]
    public void AllHeadlessProfilesReturnToBoundedState()
    {
        string root = TemporaryRoot();
        try
        {
            var results = ResourceLifecycleProfiles.Run(
                "all",
                cycles: 64,
                seed: 20260824,
                temporaryRoot: root);

            Assert.Equal(8, results.Count);
            Assert.All(results, result =>
            {
                Assert.True(result.Passed, result.FailureReason);
                Assert.Equal(0, result.FinalLiveItems);
                Assert.Equal(0, result.RemainingArtifacts);
            });
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void ResourceArtifactWriterEmitsBoundedPrivacySafeEvidence()
    {
        string root = TemporaryRoot();
        try
        {
            var snapshots = Enumerable.Range(0, 8)
                .Select(index => new ResourceSnapshot(
                    index + 1,
                    index < 3 ? "warmup" : "settled",
                    DateTimeOffset.UnixEpoch.AddSeconds(index),
                    new ResourceProcessIdentity(42, 1),
                    10,
                    20,
                    30,
                    40,
                    50,
                    6,
                    1))
                .ToArray();
            ResourceSeriesAnalysis series = ResourceSeriesAnalyzer.Analyze("writer", snapshots);
            var artifact = new ResourceStabilityRunArtifact(
                1,
                "resource-stability",
                "run-id",
                new string('a', 40),
                new string('b', 64),
                true,
                "headless deterministic fixtures",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(8),
                64,
                "all",
                ResourceStabilityArtifactWriter.Definitions(new ResourceStabilityOptions()),
                Array.Empty<ResourceProfileResult>(),
                series,
                snapshots,
                "PASS",
                null);

            string jsonPath = ResourceStabilityArtifactWriter.Write(artifact, root);
            string junitPath = ResourceStabilityArtifactWriter.WriteJUnit(artifact, root);
            string json = File.ReadAllText(jsonPath);
            string junit = File.ReadAllText(junitPath);

            Assert.True(series.Passed);
            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(junitPath));
            Assert.Contains("resource-stability", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SyntheticMeasurements", json, StringComparison.Ordinal);
            Assert.Contains("testsuite", junit, StringComparison.Ordinal);
            Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);

            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal("resource-stability", document.RootElement.GetProperty("ArtifactKind").GetString());
            Assert.Equal("PASS", document.RootElement.GetProperty("Outcome").GetString());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string TemporaryRoot()
        => Path.Combine(Path.GetTempPath(), "TabDock-resource-unit-" + Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
