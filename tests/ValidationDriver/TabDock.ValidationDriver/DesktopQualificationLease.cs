using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TabDock.Services;

namespace TabDock.ValidationDriver;

internal enum DesktopLeaseState
{
    Unstarted,
    Active,
    Invalidated,
    Closed,
}

internal enum DesktopLeaseCheckpointKind
{
    Valid,
    ForeignCoverage,
    ForeignForeground,
    TestOwnedOverlay,
    IdentityChanged,
    Unverifiable,
    SessionUnavailable,
}

/// <summary>Privacy-safe identity for a visible top-level window observation.</summary>
internal readonly record struct DesktopWindowObservation(
    IntPtr Hwnd,
    string IdentityKey,
    RunOwnershipKind Ownership,
    string Role,
    bool Visible,
    bool IdentityAvailable)
{
    public string HwndCode => Hwnd == IntPtr.Zero ? "0x0" : $"0x{Hwnd.ToInt64():X}";
}

internal readonly record struct DesktopMonitorObservation(
    int Left,
    int Top,
    int Right,
    int Bottom,
    uint Dpi)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Snapshot taken before a physical scenario starts mutating state.</summary>
internal sealed record DesktopQualificationSnapshot(
    DesktopWindowObservation Foreground,
    IReadOnlyList<DesktopWindowObservation> VisibleTestWindows,
    IReadOnlyList<DesktopMonitorObservation> Monitors,
    int VirtualLeft,
    int VirtualTop,
    int VirtualWidth,
    int VirtualHeight,
    bool InteractiveSessionAvailable,
    bool WorkstationLockedKnown,
    bool WorkstationLocked,
    string InputDesktop,
    string TabDockCandidateIdentity,
    string TestRunnerIdentity,
    PhysicalTopologySnapshot? Topology = null)
{
    public bool MultiMonitorAvailable => Monitors.Count > 1;
    public bool MixedDpiAvailable => Monitors.Select(monitor => monitor.Dpi)
        .Where(dpi => dpi != 0)
        .Distinct()
        .Count() > 1;
}

internal readonly record struct DesktopLeaseCheckpoint(
    DesktopLeaseCheckpointKind Kind,
    string Operation,
    string Reason,
    DesktopWindowObservation Foreground,
    DesktopWindowObservation? Point)
{
    public bool IsValid => Kind == DesktopLeaseCheckpointKind.Valid;
}

/// <summary>Native seam for deterministic lease state-machine tests.</summary>
internal interface IDesktopQualificationProbe
{
    DesktopQualificationSnapshot Capture();
    DesktopWindowObservation ObserveForeground();
    DesktopWindowObservation ObservePoint(int x, int y);
}

/// <summary>
/// Bounded continuity guard for physical SendInput scenarios. It records the
/// environment but never attempts to control a foreign window or restore a
/// foreign foreground window. Once a continuity proof fails the lease remains
/// invalid for the rest of that scenario.
/// </summary>
internal sealed class DesktopQualificationLease : IDisposable
{
    private sealed record TargetBinding(IntPtr Hwnd, string IdentityKey, RunOwnershipKind Ownership, string Role);

    private readonly IDesktopQualificationProbe _probe;
    private readonly NativeInteractionTimeline _timeline;
    private readonly object _sync = new();
    private readonly Dictionary<IntPtr, TargetBinding> _targets = new();
    private DesktopLeaseState _state = DesktopLeaseState.Unstarted;
    private DesktopQualificationSnapshot? _snapshot;
    private DesktopQualificationSnapshot? _restoredSnapshot;
    private string? _lastFailureReason;

    public DesktopQualificationLease(
        IDesktopQualificationProbe probe,
        NativeInteractionTimeline timeline)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public static DesktopQualificationLease CreateNative(
        NativeInteractionTimeline timeline,
        PhysicalTopologyCaptureMetadata? metadata = null)
        => new(new NativeDesktopQualificationProbe(metadata), timeline);

    public DesktopLeaseState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public bool IsValid => State == DesktopLeaseState.Active;

    public string? LastFailureReason
    {
        get
        {
            lock (_sync)
                return _lastFailureReason;
        }
    }

    public DesktopQualificationSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    public DesktopQualificationSnapshot? RestoredSnapshot
    {
        get
        {
            lock (_sync)
                return _restoredSnapshot;
        }
    }

    public void Start()
    {
        DesktopQualificationSnapshot snapshot;
        try
        {
            snapshot = _probe.Capture();
        }
        catch (Exception ex)
        {
            Invalidate("desktop snapshot failed: " + ex.GetType().Name,
                DesktopLeaseCheckpointKind.Unverifiable);
            return;
        }

        lock (_sync)
        {
            if (_state != DesktopLeaseState.Unstarted)
                return;
            _snapshot = snapshot;
            _state = snapshot.InteractiveSessionAvailable
                && !(snapshot.WorkstationLockedKnown && snapshot.WorkstationLocked)
                ? DesktopLeaseState.Active
                : DesktopLeaseState.Invalidated;
            if (_state == DesktopLeaseState.Invalidated)
            {
                _lastFailureReason = snapshot.InteractiveSessionAvailable
                    ? "workstation is locked"
                    : "interactive session is unavailable";
            }
        }

        _timeline.Record(
            _state == DesktopLeaseState.Active ? "environment-lease-started" : "environment-lease-invalid",
            data: new Dictionary<string, string>
            {
                ["interactive"] = snapshot.InteractiveSessionAvailable.ToString(),
                ["locked"] = snapshot.WorkstationLocked.ToString(),
                ["monitors"] = snapshot.Monitors.Count.ToString(),
            });
    }

    public void RegisterTarget(WindowIdentity identity, string role)
    {
        RunOwnershipKind ownership = TestRunProvenance.GetOwnership(identity);
        RegisterTarget(identity.Hwnd, IdentityKey(identity), ownership, role);
    }

    /// <summary>Deterministic-test admission overload; no native calls.</summary>
    internal void RegisterTarget(
        IntPtr hwnd,
        string identityKey,
        RunOwnershipKind ownership,
        string role)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(identityKey))
            return;

        lock (_sync)
        {
            if (_state == DesktopLeaseState.Closed)
                return;
            if (_targets.TryGetValue(hwnd, out TargetBinding? previous)
                && !string.Equals(previous.IdentityKey, identityKey, StringComparison.Ordinal))
            {
                InvalidateLocked("lease target identity changed before input", DesktopLeaseCheckpointKind.IdentityChanged);
                return;
            }
            _targets[hwnd] = new TargetBinding(hwnd, identityKey, ownership, SafeRole(role));
        }

        _timeline.Record(
            "window-target-registered",
            SafeRole(role),
            hwnd,
            new Dictionary<string, string>
            {
                ["ownership"] = ownership.ToString().ToUpperInvariant(),
                ["identity"] = identityKey,
            });
    }

    /// <summary>
    /// Checks the point or foreground immediately before a guarded operation.
    /// When an expected target is provided, the observed identity must match
    /// the pinned target exactly. Without one, the observation still must be a
    /// run-owned or explicitly adopted input target.
    /// </summary>
    public DesktopLeaseCheckpoint Checkpoint(
        string operation,
        WindowIdentity? expectedTarget = null,
        int? x = null,
        int? y = null,
        bool requireForeground = false)
    {
        if (!IsValid)
            return InvalidCheckpoint(operation, DesktopLeaseCheckpointKind.SessionUnavailable,
                LastFailureReason ?? "desktop lease is not active");

        DesktopWindowObservation foreground = SafeObserveForeground();
        DesktopWindowObservation? point = x.HasValue && y.HasValue
            ? SafeObservePoint(x.Value, y.Value)
            : null;
        DesktopWindowObservation observed = point ?? foreground;

        if (expectedTarget.HasValue)
        {
            WindowIdentity expected = expectedTarget.Value;
            RunOwnershipKind expectedOwnership = TestRunProvenance.GetOwnership(expected);
            if (!ProvenanceContract.InputAllowed(expectedOwnership))
            {
                return InvalidateCheckpoint(operation, DesktopLeaseCheckpointKind.IdentityChanged,
                    $"expected target ownership is {expectedOwnership}", foreground, point);
            }

            string expectedKey = IdentityKey(expected);
            RegisterTarget(expected.Hwnd, expectedKey, expectedOwnership,
                TestRunProvenance.WindowRole(expected.Hwnd));
            if (!observed.IdentityAvailable)
            {
                return InvalidateCheckpoint(operation, DesktopLeaseCheckpointKind.Unverifiable,
                    "observed target identity is unavailable", foreground, point);
            }
            if (observed.Hwnd != expected.Hwnd
                || !string.Equals(observed.IdentityKey, expectedKey, StringComparison.Ordinal))
            {
                DesktopLeaseCheckpointKind mismatch = observed.Ownership == RunOwnershipKind.Foreign
                    ? (point.HasValue
                        ? DesktopLeaseCheckpointKind.ForeignCoverage
                        : DesktopLeaseCheckpointKind.ForeignForeground)
                    : observed.Ownership == RunOwnershipKind.StaleRecycled
                        ? DesktopLeaseCheckpointKind.IdentityChanged
                        : DesktopLeaseCheckpointKind.TestOwnedOverlay;
                return InvalidateCheckpoint(operation, mismatch,
                    DescribeMismatch(expected, observed, point.HasValue), foreground, point);
            }
        }
        else if (!observed.IdentityAvailable)
        {
            return InvalidateCheckpoint(operation, DesktopLeaseCheckpointKind.Unverifiable,
                "observed window identity is unavailable", foreground, point);
        }
        else if (!ProvenanceContract.InputAllowed(observed.Ownership))
        {
            DesktopLeaseCheckpointKind kind = observed.Ownership == RunOwnershipKind.Foreign
                ? (point.HasValue
                    ? DesktopLeaseCheckpointKind.ForeignCoverage
                    : DesktopLeaseCheckpointKind.ForeignForeground)
                : DesktopLeaseCheckpointKind.IdentityChanged;
            return InvalidateCheckpoint(operation, kind,
                $"observed {observed.Role} is {observed.Ownership}", foreground, point);
        }

        if (requireForeground)
        {
            if (!foreground.IdentityAvailable)
            {
                return InvalidateCheckpoint(operation, DesktopLeaseCheckpointKind.Unverifiable,
                    "foreground identity is unavailable", foreground, point);
            }
            if (expectedTarget.HasValue
                && (foreground.Hwnd != expectedTarget.Value.Hwnd
                    || !string.Equals(foreground.IdentityKey, IdentityKey(expectedTarget.Value), StringComparison.Ordinal)))
            {
                DesktopLeaseCheckpointKind kind = foreground.Ownership == RunOwnershipKind.Foreign
                    ? DesktopLeaseCheckpointKind.ForeignForeground
                    : DesktopLeaseCheckpointKind.TestOwnedOverlay;
                return InvalidateCheckpoint(operation, kind,
                    "foreground is not the expected target", foreground, point);
            }
            if (!ProvenanceContract.InputAllowed(foreground.Ownership))
            {
                return InvalidateCheckpoint(operation,
                    foreground.Ownership == RunOwnershipKind.Foreign
                        ? DesktopLeaseCheckpointKind.ForeignForeground
                        : DesktopLeaseCheckpointKind.IdentityChanged,
                    $"foreground {foreground.Role} is {foreground.Ownership}", foreground, point);
            }
        }

        var valid = new DesktopLeaseCheckpoint(
            DesktopLeaseCheckpointKind.Valid,
            SafeOperation(operation),
            "ok",
            foreground,
            point);
        _timeline.Record(
            "environment-lease-checkpoint",
            observed.Role,
            observed.Hwnd,
            new Dictionary<string, string>
            {
                ["operation"] = valid.Operation,
                ["result"] = "VALID",
                ["observedOwnership"] = observed.Ownership.ToString().ToUpperInvariant(),
                ["point"] = point.HasValue ? "true" : "false",
                ["x"] = x?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ["y"] = y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ["requireForeground"] = requireForeground.ToString(),
                ["expectedHwnd"] = expectedTarget.HasValue
                    ? (expectedTarget.Value.Hwnd == IntPtr.Zero ? "0x0" : $"0x{expectedTarget.Value.Hwnd.ToInt64():X}")
                    : "",
            });
        return valid;
    }

    /// <summary>Records a guarded interaction without exposing arbitrary desktop text.</summary>
    internal void RecordInteraction(
        string type,
        string role = "Unknown",
        IntPtr hwnd = default,
        IReadOnlyDictionary<string, string>? data = null)
        => _timeline.Record(type, role, hwnd, data);

    public void Close()
    {
        lock (_sync)
        {
            if (_state != DesktopLeaseState.Closed)
                _state = DesktopLeaseState.Closed;
        }
        _timeline.Record("environment-lease-closed");
    }

    /// <summary>
    /// Proves that the lease-start topology still matches the topology admitted
    /// during preflight. A drift is an environment block before app launch or
    /// guarded input.
    /// </summary>
    public bool VerifyTopology(PhysicalTopologySnapshot expected, out string reason)
    {
        reason = string.Empty;
        if (!IsValid)
        {
            reason = LastFailureReason ?? "desktop qualification lease is not active";
            return false;
        }

        DesktopQualificationSnapshot? current = Snapshot;
        if (current?.Topology is null)
        {
            reason = "desktop topology at lease start is unavailable";
            Invalidate(reason, DesktopLeaseCheckpointKind.Unverifiable);
            return false;
        }

        if (!PhysicalTopologySnapshot.EquivalentTopology(
                expected,
                current.Topology,
                out reason))
        {
            reason = "topology changed between preflight and input: " + reason;
            Invalidate(reason, DesktopLeaseCheckpointKind.Unverifiable);
            _timeline.Record("environment-topology-preinput-mismatch",
                data: new Dictionary<string, string>
                {
                    ["reason"] = reason,
                    ["expectedSnapshotId"] = expected.SnapshotId,
                    ["observedSnapshotId"] = current.Topology.SnapshotId,
                });
            return false;
        }

        _timeline.Record("environment-topology-preinput-verified",
            data: new Dictionary<string, string>
            {
                ["snapshotId"] = current.Topology.SnapshotId,
            });
        return true;
    }

    /// <summary>
    /// Captures the native topology after cleanup and proves it is equivalent
    /// to the pre-input observation. A missing or changed topology permanently
    /// invalidates this lease; callers must stop further physical input.
    /// </summary>
    public bool VerifyRestored()
    {
        if (!IsValid)
            return false;

        DesktopQualificationSnapshot? baseline = Snapshot;
        if (baseline?.Topology is null)
        {
            Invalidate("desktop topology restoration baseline is unavailable",
                DesktopLeaseCheckpointKind.Unverifiable);
            return false;
        }

        DesktopQualificationSnapshot current;
        try
        {
            current = _probe.Capture();
        }
        catch (Exception ex)
        {
            Invalidate($"desktop topology restoration probe failed: {ex.GetType().Name}",
                DesktopLeaseCheckpointKind.Unverifiable);
            return false;
        }

        lock (_sync)
            _restoredSnapshot = current;

        if (current.Topology is null)
        {
            Invalidate("desktop topology restoration observation is unavailable",
                DesktopLeaseCheckpointKind.Unverifiable);
            return false;
        }

        if (!PhysicalTopologySnapshot.EquivalentTopology(
                baseline.Topology,
                current.Topology,
                out string reason))
        {
            Invalidate("desktop topology was not restored: " + reason,
                DesktopLeaseCheckpointKind.Unverifiable);
            _timeline.Record("environment-topology-restore-mismatch",
                data: new Dictionary<string, string>
                {
                    ["reason"] = reason,
                    ["expectedSnapshotId"] = baseline.Topology.SnapshotId,
                    ["observedSnapshotId"] = current.Topology.SnapshotId,
                });
            return false;
        }

        _timeline.Record("environment-topology-restored",
            data: new Dictionary<string, string>
            {
                ["snapshotId"] = baseline.Topology.SnapshotId,
            });
        return true;
    }

    public void Dispose() => Close();

    private DesktopWindowObservation SafeObserveForeground()
    {
        try { return _probe.ObserveForeground(); }
        catch (Exception ex)
        {
            _timeline.Record("environment-probe-error", data: new Dictionary<string, string>
            {
                ["kind"] = "foreground",
                ["exception"] = ex.GetType().Name,
            });
            return new DesktopWindowObservation(IntPtr.Zero, "", RunOwnershipKind.StaleRecycled,
                "Unknown", false, false);
        }
    }

    private DesktopWindowObservation SafeObservePoint(int x, int y)
    {
        try { return _probe.ObservePoint(x, y); }
        catch (Exception ex)
        {
            _timeline.Record("environment-probe-error", data: new Dictionary<string, string>
            {
                ["kind"] = "point",
                ["exception"] = ex.GetType().Name,
            });
            return new DesktopWindowObservation(IntPtr.Zero, "", RunOwnershipKind.StaleRecycled,
                "Unknown", false, false);
        }
    }

    private DesktopLeaseCheckpoint InvalidateCheckpoint(
        string operation,
        DesktopLeaseCheckpointKind kind,
        string reason,
        DesktopWindowObservation foreground,
        DesktopWindowObservation? point)
    {
        Invalidate(reason, kind);
        var result = new DesktopLeaseCheckpoint(kind, SafeOperation(operation), reason, foreground, point);
        _timeline.Record(
            "environment-lease-invalidated",
            point?.Role ?? foreground.Role,
            point?.Hwnd ?? foreground.Hwnd,
            new Dictionary<string, string>
            {
                ["operation"] = result.Operation,
                ["classification"] = kind.ToString().ToUpperInvariant(),
                ["reason"] = reason,
            });
        return result;
    }

    private DesktopLeaseCheckpoint InvalidCheckpoint(
        string operation,
        DesktopLeaseCheckpointKind kind,
        string reason)
    {
        var result = new DesktopLeaseCheckpoint(kind, SafeOperation(operation), reason,
            new DesktopWindowObservation(IntPtr.Zero, "", RunOwnershipKind.StaleRecycled, "Unknown", false, false),
            null);
        _timeline.Record(
            "environment-lease-checkpoint-refused",
            data: new Dictionary<string, string>
            {
                ["operation"] = result.Operation,
                ["classification"] = kind.ToString().ToUpperInvariant(),
            });
        return result;
    }

    private void Invalidate(string reason, DesktopLeaseCheckpointKind kind)
    {
        lock (_sync)
        {
            if (_state == DesktopLeaseState.Closed)
                return;
            InvalidateLocked(reason, kind);
        }
    }

    private void InvalidateLocked(string reason, DesktopLeaseCheckpointKind kind)
    {
        _state = DesktopLeaseState.Invalidated;
        _lastFailureReason ??= reason;
    }

    private static string DescribeMismatch(WindowIdentity expected, DesktopWindowObservation observed, bool point)
        => $"{(point ? "point" : "foreground")} resolved to {observed.HwndCode}/{observed.Role}; expected 0x{expected.Hwnd.ToInt64():X}";

    internal static string IdentityKey(WindowIdentity identity)
        => $"hwnd=0x{identity.Hwnd.ToInt64():X};pid={identity.ProcessId};tid={identity.WindowThreadId};class={identity.ClassName};exe={Path.GetFileName(identity.ExePath)};start={identity.ProcessStartTimeUtcTicks}";

    private static string SafeOperation(string operation)
        => string.IsNullOrWhiteSpace(operation) ? "unspecified" : operation.Trim()[..Math.Min(operation.Trim().Length, 96)];

    private static string SafeRole(string role)
        => string.IsNullOrWhiteSpace(role) ? "Unknown" : role.Trim()[..Math.Min(role.Trim().Length, 96)];
}

/// <summary>Live Windows probe used only by supervised physical runs.</summary>
internal sealed class NativeDesktopQualificationProbe : IDesktopQualificationProbe
{
    private readonly PhysicalTopologyCaptureMetadata? _metadata;

    public NativeDesktopQualificationProbe(PhysicalTopologyCaptureMetadata? metadata = null)
    {
        _metadata = metadata;
    }

    public DesktopQualificationSnapshot Capture()
    {
        DesktopWindowObservation foreground = ObserveForeground();
        var windows = new List<DesktopWindowObservation>();
        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd)
                    || !Discover.TryCaptureIdentity(hwnd, out WindowIdentity identity))
                    return true;

                RunOwnershipKind ownership = TestRunProvenance.GetOwnership(identity);
                if (ownership != RunOwnershipKind.Foreign)
                {
                    windows.Add(new DesktopWindowObservation(
                        hwnd,
                        DesktopQualificationLease.IdentityKey(identity),
                        ownership,
                        TestRunProvenance.WindowRole(hwnd),
                        true,
                        true));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            windows.Clear();
        }

        PhysicalTopologyCaptureMetadata metadata = _metadata ?? new(
            QualificationResultWriter.CandidateSha(),
            QualificationResultWriter.Sha256File(Scenarios.TabDockExe),
            QualificationResultWriter.DriverIdentitySha256(),
            TestRunProvenance.RunId,
            TestRunProvenance.CurrentScenario,
            1);
        PhysicalTopologyCaptureResult topologyResult = PhysicalTopologyProbe.CaptureNative(metadata);
        PhysicalTopologySnapshot? topology = topologyResult.Snapshot;
        IReadOnlyList<DesktopMonitorObservation> monitors = topology?.Monitors
            .Select(monitor => new DesktopMonitorObservation(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitor.Bounds.Right,
                monitor.Bounds.Bottom,
                monitor.EffectiveDpi))
            .ToArray()
            ?? Array.Empty<DesktopMonitorObservation>();

        int virtualLeft = topology?.VirtualScreen.Left ?? SafeMetric(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualTop = topology?.VirtualScreen.Top ?? SafeMetric(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualWidth = topology?.VirtualScreen.Width ?? SafeMetric(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualHeight = topology?.VirtualScreen.Height ?? SafeMetric(NativeMethods.SM_CYVIRTUALSCREEN);
        bool interactive = Environment.UserInteractive;
        bool lockedKnown = true;
        bool locked = foreground.Hwnd == IntPtr.Zero;
        return new DesktopQualificationSnapshot(
            foreground,
            windows.OrderBy(window => window.Hwnd.ToInt64()).ToArray(),
            monitors,
            virtualLeft,
            virtualTop,
            virtualWidth,
            virtualHeight,
            interactive,
            lockedKnown,
            locked,
            interactive ? "not-queried" : "unavailable",
            foreground.Role.StartsWith("TabDock", StringComparison.Ordinal)
                ? foreground.IdentityKey
                : "not-started",
            RunnerIdentity(),
            topology);
    }

    public DesktopWindowObservation ObserveForeground()
        => Observe(NativeMethods.GetForegroundWindow());

    public DesktopWindowObservation ObservePoint(int x, int y)
        => Observe(NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y }));

    private static DesktopWindowObservation Observe(IntPtr hwnd)
    {
        IntPtr root = hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        if (root == IntPtr.Zero)
            return new DesktopWindowObservation(IntPtr.Zero, "", RunOwnershipKind.Foreign, "None", false, false);

        if (!Discover.TryCaptureIdentity(root, out WindowIdentity identity))
        {
            return new DesktopWindowObservation(root, "", RunOwnershipKind.StaleRecycled,
                "Unknown", SafeVisible(root), false);
        }

        return new DesktopWindowObservation(
            root,
            DesktopQualificationLease.IdentityKey(identity),
            TestRunProvenance.GetOwnership(identity),
            TestRunProvenance.WindowRole(root),
            SafeVisible(root),
            true);
    }

    private static bool SafeVisible(IntPtr hwnd)
    {
        try { return NativeMethods.IsWindowVisible(hwnd); } catch { return false; }
    }

    private static int SafeMetric(int metric)
    {
        try { return NativeMethods.GetSystemMetrics(metric); } catch { return 0; }
    }

    private static string RunnerIdentity()
    {
        uint pid = NativeMethods.CurrentProcessId;
        long start = Discover.TryGetProcessStartTimeUtcTicks(pid);
        return $"pid={pid};start={start}";
    }
}
