using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Immutable, detached logical evidence. Serializing at capture time prevents
/// later UI/model mutation from changing the archive's snapshot.
/// </summary>
public sealed class LogicalSnapshotCapture
{
    private readonly string _json;
    private readonly string[] _issues;
    public string Status { get; }
    public IReadOnlyList<string> Issues => Array.AsReadOnly(_issues);

    private LogicalSnapshotCapture(string status, IEnumerable<LogicalPresentationSnapshot> snapshots, IEnumerable<string> issues)
    {
        Status = status;
        _json = JsonSerializer.Serialize(snapshots);
        _issues = issues.ToArray();
    }

    public static LogicalSnapshotCapture NoProvider { get; } =
        new("unavailable", Array.Empty<LogicalPresentationSnapshot>(), new[] { "logical snapshot unavailable (no-live-provider)" });

    public static LogicalSnapshotCapture Unavailable() =>
        new("unavailable", Array.Empty<LogicalPresentationSnapshot>(), new[] { "logical snapshot unavailable (collection-failed)" });

    /// <summary>Run on the owning dispatcher. Failures stay in this capture, not a later trace.</summary>
    public static LogicalSnapshotCapture Collect(IEnumerable<Func<LogicalPresentationSnapshot>> sources)
    {
        var snapshots = new List<LogicalPresentationSnapshot>();
        var issues = new List<string>();
        foreach (Func<LogicalPresentationSnapshot> source in sources)
        {
            try
            {
                // Copy each result immediately, even if another provider fails.
                string json = JsonSerializer.Serialize(source());
                snapshots.Add(JsonSerializer.Deserialize<LogicalPresentationSnapshot>(json)!);
            }
            catch
            {
                // Exception text may contain paths, titles or credentials.
                issues.Add("logical snapshot unavailable (container-collection-failed)");
            }
        }
        string status = issues.Count > 0 ? snapshots.Count > 0 ? "partial" : "unavailable"
            : snapshots.Count == 0 ? "empty" : "available";
        return new LogicalSnapshotCapture(status, snapshots, issues);
    }

    internal List<LogicalPresentationSnapshot> CopySnapshots() =>
        JsonSerializer.Deserialize<List<LogicalPresentationSnapshot>>(_json)!;
}
