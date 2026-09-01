using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabDock.Services;

namespace TabDock.ValidationDriver;


internal enum PhysicalCellOutcome
{
    RUNNABLE,
    BLOCKED_CAPABILITY,
    BLOCKED_ENVIRONMENT,
    BLOCKED_SUPERVISED,
}

internal sealed record PhysicalDisplayStateProtocolStep(
    int Order,
    string Id,
    string Requirement,
    bool InputAllowed,
    bool RestorationProofRequired);

internal static class PhysicalDisplayStateProtocol
{
    public const string Authority = "operator-controlled-display-state";
    public const string MutationPolicy =
        "No registry DPI hacks, blind Display Settings clicks, unsupported display mutation, or automated monitor hot-unplug.";

    public static IReadOnlyList<string> ProhibitedOperations { get; } =
        new[]
        {
            "registry-dpi-mutation",
            "blind-display-settings-clicks",
            "unsupported-display-mutation",
            "automated-monitor-hot-unplug",
        };

    public static IReadOnlyList<PhysicalDisplayStateProtocolStep> Steps { get; } =
        new[]
        {
            new PhysicalDisplayStateProtocolStep(1, "record-baseline", "record the current physical topology before any temporary preparation", false, true),
            new PhysicalDisplayStateProtocolStep(2, "prove-idle", "prove no scenario input or destructive run is active", false, true),
            new PhysicalDisplayStateProtocolStep(3, "check-requested-cell", "determine whether the requested topology already exists", false, true),
            new PhysicalDisplayStateProtocolStep(4, "operator-prepare", "require explicit supervised operator preparation when the cell is absent", false, true),
            new PhysicalDisplayStateProtocolStep(5, "reread-topology", "capture the native topology and effective DPI after preparation", false, true),
            new PhysicalDisplayStateProtocolStep(6, "verify-cell", "prove the observed topology matches the requested physical cell", false, true),
            new PhysicalDisplayStateProtocolStep(7, "run-bounded-cell", "allow only the bounded intended cell after all identity and lease gates pass", true, true),
            new PhysicalDisplayStateProtocolStep(8, "restore-baseline", "restore the original operator display configuration when it was changed", false, true),
            new PhysicalDisplayStateProtocolStep(9, "reread-restored-topology", "capture native topology again after cleanup", false, true),
            new PhysicalDisplayStateProtocolStep(10, "prove-restoration", "prove the post-run topology is equivalent to the baseline", false, true),
        };

    public static bool InputMayBegin(
        PhysicalCellOutcome outcome,
        bool supervisionConfirmed,
        bool topologyVerified,
        bool leaseActive)
        => outcome == PhysicalCellOutcome.RUNNABLE
            && supervisionConfirmed
            && topologyVerified
            && leaseActive;
}

internal readonly record struct QualificationRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsPositive => Right > Left && Bottom > Top;

    public bool Contains(QualificationRect other)
        => other.Left >= Left
            && other.Top >= Top
            && other.Right <= Right
            && other.Bottom <= Bottom;

    public void Validate(string name)
    {
        if (!IsPositive)
            throw new ArgumentException($"{name} must have positive dimensions.", name);
    }
}

internal readonly record struct TaskbarDelta(int Left, int Top, int Right, int Bottom)
{
    public static TaskbarDelta From(QualificationRect bounds, QualificationRect workArea)
        => new(
            workArea.Left - bounds.Left,
            workArea.Top - bounds.Top,
            bounds.Right - workArea.Right,
            bounds.Bottom - workArea.Bottom);
}

internal sealed record PhysicalMonitorSnapshot(
    string MonitorId,
    QualificationRect Bounds,
    QualificationRect WorkArea,
    bool IsPrimary,
    uint EffectiveDpi,
    int ScalePercent,
    string RelativePlacement,
    TaskbarDelta TaskbarDelta)
{
    public void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(MonitorId)
            || string.IsNullOrWhiteSpace(RelativePlacement)
            || EffectiveDpi == 0
            || ScalePercent <= 0)
        {
            throw new ArgumentException($"{name} identity or scale is incomplete.", name);
        }
        Bounds.Validate($"{name}.bounds");
        WorkArea.Validate($"{name}.workArea");
        if (!Bounds.Contains(WorkArea))
            throw new ArgumentException($"{name}.workArea leaves monitor bounds.", name);
        if (!TaskbarDelta.Equals(TaskbarDelta.From(Bounds, WorkArea)))
            throw new ArgumentException($"{name}.taskbarDelta disagrees with monitor/work rectangles.", name);
        int expectedScale = checked((int)Math.Round(
            EffectiveDpi * 100.0 / NativeMethods.USER_DEFAULT_SCREEN_DPI,
            MidpointRounding.AwayFromZero));
        if (ScalePercent != expectedScale)
            throw new ArgumentException($"{name}.scalePercent disagrees with effective DPI.", name);
    }
}

internal sealed record PhysicalTopologySnapshot(
    int SchemaVersion,
    string Generation,
    bool SyntheticTopology,
    PhysicalTopologyProvenance Provenance,
    DateTimeOffset ObservedUtc,
    string CandidateSha,
    string CandidateExecutableSha,
    string DriverSha,
    string RunId,
    string Scenario,
    int Attempt,
    QualificationRect VirtualScreen,
    string PrimaryMonitorId,
    IReadOnlyList<PhysicalMonitorSnapshot> Monitors,
    string SnapshotId)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentGeneration = "physical-topology-snapshot-2026-09-01-v1";

    public bool MixedDpiAvailable
        => Monitors.Select(monitor => monitor.EffectiveDpi).Distinct().Count() > 1;

    public bool NegativeXAvailable
        => Monitors.Any(monitor => monitor.Bounds.Left < 0);

    public bool NegativeYAvailable
        => Monitors.Any(monitor => monitor.Bounds.Top < 0);

    public bool StaggeredAvailable
        => Monitors.Any(monitor => monitor.RelativePlacement.Contains("staggered", StringComparison.Ordinal));

    public IReadOnlyList<uint> DpiValues
        => Monitors.Select(monitor => monitor.EffectiveDpi).Distinct().OrderBy(value => value).ToArray();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || !string.Equals(Generation, CurrentGeneration, StringComparison.Ordinal)
            || !Enum.IsDefined(Provenance)
            || ObservedUtc == default
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(CandidateExecutableSha)
            || string.IsNullOrWhiteSpace(DriverSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(Scenario)
            || Attempt < 1
            || string.IsNullOrWhiteSpace(PrimaryMonitorId)
            || string.IsNullOrWhiteSpace(SnapshotId)
            || !IsSha256(SnapshotId)
            || Monitors is null
            || Monitors.Count == 0)
        {
            throw new ArgumentException("physical topology snapshot identity or collections are invalid.");
        }

        VirtualScreen.Validate(nameof(VirtualScreen));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int primaryCount = 0;
        foreach (PhysicalMonitorSnapshot monitor in Monitors)
        {
            monitor.Validate($"monitor[{monitor.MonitorId}]");
            if (!ids.Add(monitor.MonitorId))
                throw new ArgumentException("physical topology monitor IDs are not unique.");
            if (!VirtualScreen.Contains(monitor.Bounds))
                throw new ArgumentException($"monitor {monitor.MonitorId} leaves the virtual screen.");
            if (monitor.IsPrimary)
                primaryCount++;
        }
        if (!ids.Contains(PrimaryMonitorId) || primaryCount != 1
            || !Monitors.Single(monitor => monitor.IsPrimary).MonitorId.Equals(
                PrimaryMonitorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("physical topology primary identity is contradictory.");
        }
        for (int i = 0; i < Monitors.Count; i++)
        {
            for (int j = i + 1; j < Monitors.Count; j++)
            {
                if (IntersectionArea(Monitors[i].Bounds, Monitors[j].Bounds) > 0)
                    throw new ArgumentException("physical topology monitor bounds overlap.");
            }
        }
        string expectedId = ComputeSnapshotId(this);
        if (!string.Equals(expectedId, SnapshotId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("physical topology snapshot hash disagrees with its contents.");
    }

    public static PhysicalTopologySnapshot Create(
        DateTimeOffset observedUtc,
        string candidateSha,
        string candidateExecutableSha,
        string driverSha,
        string runId,
        string scenario,
        int attempt,
        QualificationRect virtualScreen,
        string primaryMonitorId,
        IReadOnlyList<PhysicalMonitorSnapshot> monitors,
        PhysicalTopologyProvenance provenance = PhysicalTopologyProvenance.Observed,
        bool syntheticTopology = false)
    {
        var snapshot = new PhysicalTopologySnapshot(
            CurrentSchemaVersion,
            CurrentGeneration,
            syntheticTopology,
            provenance,
            observedUtc,
            candidateSha,
            candidateExecutableSha,
            driverSha,
            runId,
            scenario,
            attempt,
            virtualScreen,
            primaryMonitorId,
            monitors.ToArray(),
            string.Empty);
        return snapshot with { SnapshotId = ComputeSnapshotId(snapshot) };
    }

    public static string ComputeSnapshotId(PhysicalTopologySnapshot snapshot)
    {
        var canonical = new
        {
            schemaVersion = snapshot.SchemaVersion,
            generation = snapshot.Generation,
            syntheticTopology = snapshot.SyntheticTopology,
            virtualScreen = snapshot.VirtualScreen,
            primaryMonitorId = snapshot.PrimaryMonitorId,
            monitors = snapshot.Monitors.Select(monitor => new
            {
                monitor.MonitorId,
                monitor.Bounds,
                monitor.WorkArea,
                monitor.IsPrimary,
                monitor.EffectiveDpi,
                monitor.ScalePercent,
                monitor.RelativePlacement,
                monitor.TaskbarDelta,
            }).ToArray(),
        };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool EquivalentTopology(
        PhysicalTopologySnapshot expected,
        PhysicalTopologySnapshot actual,
        out string reason)
    {
        reason = string.Empty;
        try
        {
            expected.Validate();
            actual.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
        if (!expected.VirtualScreen.Equals(actual.VirtualScreen))
            reason = "virtual screen changed";
        else if (!string.Equals(expected.PrimaryMonitorId, actual.PrimaryMonitorId, StringComparison.Ordinal))
            reason = "primary monitor changed";
        else if (expected.Monitors.Count != actual.Monitors.Count)
            reason = "monitor count changed";
        else
        {
            foreach (PhysicalMonitorSnapshot expectedMonitor in expected.Monitors)
            {
                PhysicalMonitorSnapshot? actualMonitor = actual.Monitors.FirstOrDefault(
                    monitor => string.Equals(monitor.MonitorId, expectedMonitor.MonitorId, StringComparison.Ordinal));
                if (actualMonitor == null)
                {
                    reason = $"monitor {expectedMonitor.MonitorId} disappeared or was reordered";
                    break;
                }
                if (!expectedMonitor.Equals(actualMonitor))
                {
                    reason = $"monitor {expectedMonitor.MonitorId} geometry, work area, primary, or DPI changed";
                    break;
                }
            }
        }
        return reason.Length == 0;
    }

    private static int IntersectionArea(QualificationRect left, QualificationRect right)
    {
        int width = Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
        int height = Math.Max(0, Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Top, right.Top));
        return checked(width * height);
    }

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length == 64
            && value.All(Uri.IsHexDigit);
}

internal sealed record PhysicalTopologyCaptureMetadata(
    string CandidateSha,
    string CandidateExecutableSha,
    string DriverSha,
    string RunId,
    string Scenario,
    int Attempt,
    PhysicalTopologyProvenance Provenance = PhysicalTopologyProvenance.Observed,
    bool SyntheticTopology = false);

internal sealed record PhysicalTopologyCaptureResult(
    PhysicalTopologySnapshot? Snapshot,
    string? Failure)
{
    public bool Succeeded => Snapshot is not null && Failure is null;
}

internal static class PhysicalTopologyProbe
{
    public static PhysicalTopologyCaptureResult CaptureNative(PhysicalTopologyCaptureMetadata metadata)
    {
        var native = new List<NativeMonitor>();
        try
        {
            bool enumerated = NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr monitor, IntPtr _, ref NativeMethods.RECT callbackRect, IntPtr _) =>
                {
                    var info = new NativeMethods.MONITORINFO
                    {
                        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
                    };
                    if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                        return false;
                    if (info.rcMonitor.left != callbackRect.left
                        || info.rcMonitor.top != callbackRect.top
                        || info.rcMonitor.right != callbackRect.right
                        || info.rcMonitor.bottom != callbackRect.bottom)
                    {
                        return false;
                    }
                    uint dpi;
                    try
                    {
                        dpi = MonitorDpiService.GetEffectiveDpi(monitor);
                    }
                    catch
                    {
                        dpi = 0;
                    }
                    if (dpi == 0)
                        return false;
                    native.Add(new NativeMonitor(
                        monitor,
                        ToRect(info.rcMonitor),
                        ToRect(info.rcWork),
                        (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                        dpi));
                    return true;
                },
                IntPtr.Zero);
            if (!enumerated)
                return new PhysicalTopologyCaptureResult(null, "native monitor enumeration or DPI probe failed");
            if (native.Count == 0)
                return new PhysicalTopologyCaptureResult(null, "no native display monitors were observed");

            int virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (virtualWidth <= 0 || virtualHeight <= 0)
                return new PhysicalTopologyCaptureResult(null, "native virtual-screen metrics are unavailable");

            NativeMonitor[] ordered = native
                .OrderBy(item => item.Bounds.Top)
                .ThenBy(item => item.Bounds.Left)
                .ThenBy(item => item.Bounds.Right)
                .ThenBy(item => item.Bounds.Bottom)
                .ToArray();
            if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Bounds == pair.Second.Bounds))
                return new PhysicalTopologyCaptureResult(null, "native monitor geometry is not uniquely identifiable");
            if (ordered.Count(item => item.IsPrimary) != 1)
                return new PhysicalTopologyCaptureResult(null, "native primary monitor identity is unavailable or contradictory");

            QualificationRect virtualScreen = new(
                virtualLeft,
                virtualTop,
                checked(virtualLeft + virtualWidth),
                checked(virtualTop + virtualHeight));
            NativeMonitor primary = ordered.Single(item => item.IsPrimary);
            var monitors = new List<PhysicalMonitorSnapshot>(ordered.Length);
            for (int index = 0; index < ordered.Length; index++)
            {
                NativeMonitor item = ordered[index];
                string id = $"monitor-{index + 1:D3}";
                monitors.Add(new PhysicalMonitorSnapshot(
                    id,
                    item.Bounds,
                    item.WorkArea,
                    item.IsPrimary,
                    item.Dpi,
                    checked((int)Math.Round(item.Dpi * 100.0 / NativeMethods.USER_DEFAULT_SCREEN_DPI,
                        MidpointRounding.AwayFromZero)),
                    RelativePlacement(item.Bounds, primary.Bounds, item.IsPrimary),
                    TaskbarDelta.From(item.Bounds, item.WorkArea)));
            }

            var snapshot = PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                metadata.CandidateSha,
                metadata.CandidateExecutableSha,
                metadata.DriverSha,
                metadata.RunId,
                metadata.Scenario,
                metadata.Attempt,
                virtualScreen,
                monitors.Single(item => item.IsPrimary).MonitorId,
                monitors,
                metadata.Provenance,
                metadata.SyntheticTopology);
            snapshot.Validate();
            return new PhysicalTopologyCaptureResult(snapshot, null);
        }
        catch (Exception ex)
        {
            return new PhysicalTopologyCaptureResult(null, $"native topology probe failed: {ex.GetType().Name}");
        }
    }

    private static QualificationRect ToRect(NativeMethods.RECT rect)
        => new(rect.left, rect.top, rect.right, rect.bottom);

    private static string RelativePlacement(
        QualificationRect bounds,
        QualificationRect primary,
        bool isPrimary)
    {
        if (isPrimary)
            return "primary";
        var labels = new List<string>();
        if (bounds.Right <= primary.Left)
            labels.Add("left");
        if (bounds.Left >= primary.Right)
            labels.Add("right");
        if (bounds.Bottom <= primary.Top)
            labels.Add("above");
        if (bounds.Top >= primary.Bottom)
            labels.Add("below");
        if (bounds.Top != primary.Top && bounds.Left != primary.Left)
            labels.Add("staggered");
        return labels.Count == 0 ? "overlapping-axis" : string.Join("+", labels);
    }

    private sealed record NativeMonitor(
        IntPtr Handle,
        QualificationRect Bounds,
        QualificationRect WorkArea,
        bool IsPrimary,
        uint Dpi);
}

internal static class PhysicalTopologyGate
{
    public static IReadOnlyList<string> ValidatePhysicalSnapshot(
        PhysicalTopologySnapshot? snapshot,
        PhysicalTopologyCaptureMetadata expected,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        var failures = new List<string>();
        if (snapshot is null)
            return new[] { "topology snapshot is unavailable" };
        if (snapshot.SyntheticTopology)
            failures.Add("synthetic topology cannot satisfy a physical gate");
        try
        {
            snapshot.Validate();
        }
        catch (ArgumentException ex)
        {
            failures.Add(ex.Message);
        }
        if (snapshot.Provenance is not PhysicalTopologyProvenance.Observed
            and not PhysicalTopologyProvenance.OperatorPrepared)
        {
            failures.Add("topology provenance is not physical");
        }
        if (!string.Equals(snapshot.CandidateSha, expected.CandidateSha, StringComparison.Ordinal))
            failures.Add("topology candidate source identity does not match");
        if (!string.Equals(snapshot.CandidateExecutableSha, expected.CandidateExecutableSha, StringComparison.OrdinalIgnoreCase))
            failures.Add("topology candidate executable identity does not match");
        if (!string.Equals(snapshot.DriverSha, expected.DriverSha, StringComparison.OrdinalIgnoreCase))
            failures.Add("topology validation-driver identity does not match");
        if (!string.Equals(snapshot.RunId, expected.RunId, StringComparison.Ordinal)
            || !string.Equals(snapshot.Scenario, expected.Scenario, StringComparison.Ordinal)
            || snapshot.Attempt != expected.Attempt)
        {
            failures.Add("topology run/scenario/attempt identity does not match");
        }
        if (snapshot.ObservedUtc > now || now - snapshot.ObservedUtc > maximumAge)
            failures.Add("topology snapshot is stale or timestamped in the future");
        if (!IsSha(snapshot.CandidateSha, 40))
            failures.Add("topology candidate source SHA is unavailable or malformed");
        if (!IsSha(snapshot.CandidateExecutableSha, 64))
            failures.Add("topology candidate executable SHA is unavailable or malformed");
        if (!IsSha(snapshot.DriverSha, 64))
            failures.Add("topology validation-driver SHA is unavailable or malformed");
        return failures;
    }

    public static bool IsPhysicalEligible(PhysicalTopologySnapshot? snapshot)
        => snapshot is not null
            && !snapshot.SyntheticTopology
            && (snapshot.Provenance is PhysicalTopologyProvenance.Observed
                or PhysicalTopologyProvenance.OperatorPrepared);

    private static bool IsSha(string? value, int length)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length == length
            && value.All(Uri.IsHexDigit);
}

internal sealed record PhysicalQualificationCell(
    string Id,
    string Scenario,
    string TopologyClass,
    int? RequiredDpi = null,
    int? SourceDpi = null,
    int? DestinationDpi = null,
    bool RequiresMultiMonitor = false,
    bool RequiresMixedDpi = false,
    bool RequiresSupervision = true);

internal sealed record PhysicalQualificationPlanRow(
    PhysicalQualificationCell Cell,
    PhysicalCellOutcome Outcome,
    string? Reason,
    string? TopologySnapshotId,
    string? SourceMonitorId,
    string? DestinationMonitorId);

internal static class PhysicalQualificationPlan
{
    public static IReadOnlyList<PhysicalQualificationCell> Cells { get; } =
        new[]
        {
            new PhysicalQualificationCell("topology-negative-x", "dual-monitor-mixed-dpi-transfer", "negative-x", RequiresMultiMonitor: true),
            new PhysicalQualificationCell("topology-negative-y", "dual-monitor-mixed-dpi-transfer", "negative-y", RequiresMultiMonitor: true),
            new PhysicalQualificationCell("topology-staggered", "dual-monitor-mixed-dpi-transfer", "staggered", RequiresMultiMonitor: true),
            new PhysicalQualificationCell("topology-asymmetric-work-areas", "dual-monitor-mixed-dpi-transfer", "asymmetric-work-areas", RequiresMultiMonitor: true),
            new PhysicalQualificationCell("topology-odd-dimensions", "dual-monitor-mixed-dpi-transfer", "odd-dimensions"),
            new PhysicalQualificationCell("topology-narrow-work-area", "dual-monitor-mixed-dpi-transfer", "narrow-work-area"),
            new PhysicalQualificationCell("topology-large-coordinates", "dual-monitor-mixed-dpi-transfer", "large-coordinates"),
            new PhysicalQualificationCell("dpi-150-percent", "dual-monitor-mixed-dpi-transfer", "dpi-class", RequiredDpi: 144),
            new PhysicalQualificationCell("dpi-175-percent", "dual-monitor-mixed-dpi-transfer", "dpi-class", RequiredDpi: 168),
            new PhysicalQualificationCell("dpi-200-percent", "dual-monitor-mixed-dpi-transfer", "dpi-class", RequiredDpi: 192),
            new PhysicalQualificationCell("transition-96-to-144", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 96, DestinationDpi: 144, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-144-to-96", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 144, DestinationDpi: 96, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-96-to-168", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 96, DestinationDpi: 168, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-168-to-96", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 168, DestinationDpi: 96, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-96-to-192", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 96, DestinationDpi: 192, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-192-to-96", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 192, DestinationDpi: 96, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-120-to-144", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 120, DestinationDpi: 144, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-144-to-120", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 144, DestinationDpi: 120, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-120-to-168", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 120, DestinationDpi: 168, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-168-to-120", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 168, DestinationDpi: 120, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-120-to-192", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 120, DestinationDpi: 192, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("transition-192-to-120", "dual-monitor-mixed-dpi-transfer", "dpi-transition",  SourceDpi: 192, DestinationDpi: 120, RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-short-narrow", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-short-default", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-short-wide", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-medium-narrow", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-medium-default", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-medium-wide", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-long-narrow", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-long-default", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("title-long-wide", "title-centering-physical-measurement", "title", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("maximize-restore-after-transfer", "guest-caption-maximize-contained", "maximize-restore", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("win-up-after-transfer", "guest-win-up-contained", "win-up", RequiresMultiMonitor: true, RequiresMixedDpi: true),
            new PhysicalQualificationCell("controlled-topmost", "topmost-guest-interaction", "topmost", RequiresMultiMonitor: true),
            new PhysicalQualificationCell("single-split-containment", "split-two-auto", "single-split"),
        };

    public static IReadOnlyList<PhysicalQualificationPlanRow> BuildRows(
        ScenarioCapabilitySnapshot snapshot,
        PhysicalTopologyCaptureMetadata expected,
        bool supervisionConfirmed,
        DateTimeOffset now)
    {
        IReadOnlyList<string> baseFailures = PhysicalTopologyGate.ValidatePhysicalSnapshot(
            snapshot.Topology,
            expected,
            now,
            TimeSpan.FromMinutes(5));
        return Cells.Select(cell => BuildRow(cell, snapshot.Topology, baseFailures, supervisionConfirmed)).ToArray();
    }

    private static PhysicalQualificationPlanRow BuildRow(
        PhysicalQualificationCell cell,
        PhysicalTopologySnapshot? topology,
        IReadOnlyList<string> baseFailures,
        bool supervisionConfirmed)
    {
        if (cell.RequiresSupervision && !supervisionConfirmed)
        {
            return new PhysicalQualificationPlanRow(
                cell,
                PhysicalCellOutcome.BLOCKED_SUPERVISED,
                "supervised operator confirmation is required before input",
                topology?.SnapshotId,
                null,
                null);
        }

        if (baseFailures.Count != 0)
        {
            PhysicalCellOutcome baseOutcome = baseFailures.Any(IsCapabilityFailure)
                ? PhysicalCellOutcome.BLOCKED_CAPABILITY
                : PhysicalCellOutcome.BLOCKED_ENVIRONMENT;
            return new PhysicalQualificationPlanRow(
                cell,
                baseOutcome,
                string.Join("; ", baseFailures),
                topology?.SnapshotId,
                null,
                null);
        }

        if (topology is null)
        {
            return new PhysicalQualificationPlanRow(
                cell,
                PhysicalCellOutcome.BLOCKED_ENVIRONMENT,
                "physical topology snapshot is unavailable",
                null,
                null,
                null);
        }

        var failures = new List<string>();
        if (cell.RequiresMultiMonitor && topology.Monitors.Count < 2)
            failures.Add("multi-monitor topology is unavailable");
        if (cell.RequiresMixedDpi && !topology.MixedDpiAvailable)
            failures.Add("mixed-DPI topology is unavailable");

        switch (cell.TopologyClass)
        {
            case "negative-x" when !topology.NegativeXAvailable:
                failures.Add("negative-X monitor is unavailable");
                break;
            case "negative-y" when !topology.NegativeYAvailable:
                failures.Add("negative-Y monitor is unavailable");
                break;
            case "staggered" when !topology.StaggeredAvailable:
                failures.Add("staggered monitor placement is unavailable");
                break;
            case "asymmetric-work-areas" when !HasAsymmetricWorkAreas(topology):
                failures.Add("asymmetric monitor work areas are unavailable");
                break;
            case "odd-dimensions" when !topology.Monitors.Any(HasOddDimension):
                failures.Add("odd monitor dimensions are unavailable");
                break;
            case "narrow-work-area" when !topology.Monitors.Any(
                monitor => monitor.WorkArea.Width <= 640 || monitor.WorkArea.Height <= 480):
                failures.Add("narrow monitor work area is unavailable");
                break;
            case "large-coordinates" when !topology.Monitors.Any(HasLargeCoordinate):
                failures.Add("large monitor coordinates are unavailable");
                break;
        }

        PhysicalMonitorSnapshot? source = null;
        PhysicalMonitorSnapshot? destination = null;
        if (cell.RequiredDpi is int requiredDpi
            && !topology.Monitors.Any(monitor => monitor.EffectiveDpi == requiredDpi))
        {
            failures.Add($"required effective DPI {requiredDpi} is unavailable");
        }
        if (cell.SourceDpi is int sourceDpi)
        {
            source = topology.Monitors.FirstOrDefault(monitor => monitor.EffectiveDpi == sourceDpi);
            if (source is null)
                failures.Add($"source effective DPI {sourceDpi} is unavailable");
        }
        if (cell.DestinationDpi is int destinationDpi)
        {
            destination = topology.Monitors.FirstOrDefault(monitor => monitor.EffectiveDpi == destinationDpi);
            if (destination is null)
                failures.Add($"destination effective DPI {destinationDpi} is unavailable");
        }

        PhysicalCellOutcome outcome = failures.Count == 0
            ? PhysicalCellOutcome.RUNNABLE
            : PhysicalCellOutcome.BLOCKED_CAPABILITY;
        return new PhysicalQualificationPlanRow(
            cell,
            outcome,
            failures.Count == 0 ? null : string.Join("; ", failures),
            topology.SnapshotId,
            source?.MonitorId,
            destination?.MonitorId);
    }

    private static bool HasAsymmetricWorkAreas(PhysicalTopologySnapshot topology)
        => topology.Monitors.Select(monitor => monitor.WorkArea).Distinct().Count() > 1
            && topology.Monitors.Any(monitor => monitor.Bounds != monitor.WorkArea);

    private static bool HasOddDimension(PhysicalMonitorSnapshot monitor)
        => (monitor.Bounds.Width & 1) != 0
            || (monitor.Bounds.Height & 1) != 0
            || (monitor.WorkArea.Width & 1) != 0
            || (monitor.WorkArea.Height & 1) != 0;

    private static bool HasLargeCoordinate(PhysicalMonitorSnapshot monitor)
        => new[]
        {
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Right,
            monitor.Bounds.Bottom,
        }.Any(value => Math.Abs((long)value) > 8192);

    private static bool IsCapabilityFailure(string failure)
        => failure.Contains("candidate", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("SHA", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("synthetic", StringComparison.OrdinalIgnoreCase);
}
