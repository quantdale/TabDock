using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace TabDock.ValidationDriver;

internal sealed record ResourceMetricDefinitionEvidence(
    ResourceMetric Metric,
    long MaxFinalDelta,
    long MaxTailDelta,
    double MaxPositiveSlopePerCycle);

internal sealed record ResourceStabilityRunArtifact(
    int SchemaVersion,
    string ArtifactKind,
    string RunId,
    string SourceSha,
    string DriverSha,
    bool SyntheticMeasurements,
    string MeasurementTarget,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    int CycleCount,
    string ProfileSelection,
    IReadOnlyList<ResourceMetricDefinitionEvidence> MetricDefinitions,
    IReadOnlyList<ResourceProfileResult> Profiles,
    ResourceSeriesAnalysis ProcessSeries,
    IReadOnlyList<ResourceSnapshot> Snapshots,
    string Outcome,
    string? FailureReason,
    VisualMeasurementReport? VisualMeasurements = null);

internal static class ResourceStabilityArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(
        ResourceStabilityRunArtifact artifact,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "resource-stability.json");
        string json = JsonSerializer.Serialize(artifact, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    public static string WriteJUnit(
        ResourceStabilityRunArtifact artifact,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "resource-stability.junit.xml");
        string failure = artifact.FailureReason ?? "resource-stability gate failed";
        int failures = string.Equals(artifact.Outcome, "PASS", StringComparison.Ordinal) ? 0 : 1;
        string escaped = System.Security.SecurityElement.Escape(failure) ?? "resource-stability gate failed";
        string failureElement = failures == 0
            ? string.Empty
            : $"<failure message=\"{escaped}\" />";
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + $"<testsuite name=\"TabDock.ResourceStability\" tests=\"1\" failures=\"{failures}\" skipped=\"0\">\n"
            + "  <testcase classname=\"TabDock.ValidationDriver.ResourceStability\" name=\"resource-stability\">\n"
            + $"    {failureElement}\n"
            + "  </testcase>\n"
            + "</testsuite>\n";
        File.WriteAllText(path, xml);
        return path;
    }

    public static IReadOnlyList<ResourceMetricDefinitionEvidence> Definitions(
        ResourceStabilityOptions options)
        => options.Budgets
            .OrderBy(pair => pair.Key)
            .Select(pair => new ResourceMetricDefinitionEvidence(
                pair.Key,
                pair.Value.MaxFinalDelta,
                pair.Value.MaxTailDelta,
                pair.Value.MaxPositiveSlopePerCycle))
            .ToArray();
}
