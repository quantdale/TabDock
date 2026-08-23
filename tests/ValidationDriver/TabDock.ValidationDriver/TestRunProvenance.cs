using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

/// <summary>
/// Provenance for one supervised validation-driver invocation. The registry is
/// deliberately stricter than a PID allow-list: a process is identified by its
/// PID, executable path, and process-start identity, while browser descendants
/// additionally need a provable relationship to the launcher for this run.
/// </summary>
internal static class TestRunProvenance
{
    private sealed class ProcessRecord
    {
        public required ProcessIdentity Identity { get; init; }
        public required string Role { get; init; }
        public uint LaunchRootPid { get; init; }
        public IReadOnlyList<uint> Ancestry { get; init; } = Array.Empty<uint>();
    }

    private sealed class WindowRecord
    {
        public required WindowIdentity Identity { get; init; }
        public required string Role { get; init; }

        /// <summary>
        /// True for a pre-existing external window (e.g. Windows 11 Notepad's
        /// single-instance broker opening the spawned temp file as a tab in an
        /// already-running instance) that this run explicitly adopted after its
        /// full stable identity was pinned. Adopted windows are valid INPUT
        /// TARGETS while their identity matches, but their process is never
        /// tracked or killed by cleanup.
        /// </summary>
        public bool AdoptedExternal { get; init; }
    }

    internal readonly record struct ProcessIdentity(
        uint ProcessId,
        long ProcessStartTimeUtcTicks,
        string ExePath);

    private static readonly Dictionary<uint, ProcessRecord> Processes = new();
    private static readonly Dictionary<IntPtr, WindowRecord> Windows = new();
    private static readonly object Sync = new();
    private static Guid _runId;
    private static string _markerName = string.Empty;
    private static IntPtr _markerValue;
    private static int _diagnosticSequence;

    public static string CurrentScenario { get; private set; } = "startup";
    public static string RunId => _runId.ToString("D");
    public static string RunIdCompact => _runId.ToString("N");
    public static string MarkerName => _markerName;
    public static IntPtr MarkerValue => _markerValue;

    public static string ArtifactDirectory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_ARTIFACT_ROOT");
            string root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Path.GetTempPath(), "TabDock-Validation", "runs")
                : configured;
            return Path.Combine(root, RunIdCompact);
        }
    }

    public static void BeginRun()
    {
        lock (Sync)
        {
            _runId = Guid.NewGuid();
            _markerName = $"TabDock.Validation.{RunIdCompact}";
            _markerValue = CreateMarkerValue();
            _diagnosticSequence = 0;
            Directory.CreateDirectory(ArtifactDirectory);
            BeginScenarioLocked("startup");
        }
    }

    public static void BeginScenario(string scenario)
    {
        lock (Sync)
        {
            if (_runId == Guid.Empty)
            {
                _runId = Guid.NewGuid();
                _markerName = $"TabDock.Validation.{RunIdCompact}";
                _markerValue = CreateMarkerValue();
            }
            Directory.CreateDirectory(ArtifactDirectory);
            BeginScenarioLocked(scenario);
        }
    }

    private static void BeginScenarioLocked(string scenario)
    {
        CurrentScenario = string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario;
        Processes.Clear();
        Windows.Clear();
        RegisterCurrentProcessLocked();
    }

    private static void RegisterCurrentProcessLocked()
    {
        uint pid = NativeMethods.CurrentProcessId;
        if (TryReadProcessIdentity(pid, out ProcessIdentity identity))
        {
            Processes[pid] = new ProcessRecord
            {
                Identity = identity,
                Role = "ValidationDriver",
                LaunchRootPid = pid,
                Ancestry = GetProcessAncestry(pid),
            };
        }
    }

    public static bool RegisterLaunchedProcess(
        System.Diagnostics.Process process,
        string role,
        out string reason)
    {
        if (process == null)
        {
            reason = "process-null";
            return false;
        }

        return RegisterProcess(
            (uint)process.Id,
            role,
            NativeMethods.CurrentProcessId,
            expectedExecutable: null,
            requireDescendant: false,
            out reason);
    }

    /// <summary>
    /// Registers a process that owns a discovered browser window. The process
    /// must be the launcher or a descendant of the launcher and its executable
    /// must match the expected browser executable. This rejects an already-open
    /// personal browser even when its executable name is identical.
    /// </summary>
    public static bool RegisterDescendantProcess(
        uint processId,
        string role,
        uint launcherProcessId,
        string expectedExecutable,
        out string reason)
    {
        return RegisterProcess(
            processId,
            role,
            launcherProcessId,
            expectedExecutable,
            requireDescendant: true,
            out reason);
    }

    private static bool RegisterProcess(
        uint processId,
        string role,
        uint launchRootPid,
        string? expectedExecutable,
        bool requireDescendant,
        out string reason)
    {
        reason = string.Empty;
        if (!TryReadProcessIdentity(processId, out ProcessIdentity identity))
        {
            reason = "process-identity-unavailable";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedExecutable)
            && !ExecutableMatches(identity.ExePath, expectedExecutable))
        {
            reason = $"executable-mismatch expected={Path.GetFileName(expectedExecutable)} actual={Path.GetFileName(identity.ExePath)}";
            return false;
        }

        IReadOnlyList<uint> ancestry = GetProcessAncestry(processId);
        if (requireDescendant
            && processId != launchRootPid
            && !ancestry.Contains(launchRootPid))
        {
            reason = $"ancestry-unproven launcher={launchRootPid}";
            return false;
        }

        lock (Sync)
        {
            Processes[processId] = new ProcessRecord
            {
                Identity = identity,
                Role = string.IsNullOrWhiteSpace(role) ? "Unknown" : role,
                LaunchRootPid = launchRootPid,
                Ancestry = ancestry,
            };
        }
        return true;
    }

    public static bool TryRegisterWindow(WindowIdentity identity, string role, out string reason)
    {
        reason = string.Empty;
        if (!TryValidateProcess(identity.ProcessId, identity.ExePath, identity.ProcessStartTimeUtcTicks, out reason))
            return false;

        lock (Sync)
        {
            if (Windows.TryGetValue(identity.Hwnd, out WindowRecord? previous)
                && !SameStableIdentity(previous.Identity, identity))
            {
                reason = "hwnd-registration-identity-changed";
                return false;
            }
        }

        if (!NativeMethods.SetProp(identity.Hwnd, _markerName, _markerValue))
        {
            reason = $"marker-install-failed win32={MarshalLastError()}";
            return false;
        }

        lock (Sync)
        {
            Windows[identity.Hwnd] = new WindowRecord
            {
                Identity = identity,
                Role = string.IsNullOrWhiteSpace(role) ? ProcessRole(identity.ProcessId) : role,
            };
        }
        return true;
    }

    /// <summary>
    /// Explicitly adopts a pre-existing external window as a bounded input
    /// target: the window's complete stable identity (HWND, pid, process start,
    /// executable, class, title) is pinned NOW and re-verified before every
    /// input, so any recycle/retitle refuses fail-closed. The owning process is
    /// deliberately NOT registered — cleanup must never kill a user process.
    /// This exists for documented broker flows such as Windows 11 Notepad
    /// opening a spawned file as a tab inside an already-running instance.
    /// </summary>
    public static bool TryAdoptExternalWindow(WindowIdentity identity, string role, out string reason)
    {
        reason = string.Empty;
        lock (Sync)
        {
            if (Windows.TryGetValue(identity.Hwnd, out WindowRecord? previous))
            {
                if (!SameStableIdentity(previous.Identity, identity))
                    reason = "adopt-hwnd-identity-changed";
                else
                    return true;
                return false;
            }
            Windows[identity.Hwnd] = new WindowRecord
            {
                Identity = identity,
                Role = string.IsNullOrWhiteSpace(role) ? "ExternalWindow" : role,
                AdoptedExternal = true,
            };
        }
        return true;
    }

    /// <summary>
    /// Validates a window immediately before input. A registered HWND requires
    /// its run marker and every stable identity field. An unregistered dynamic
    /// surface is admitted only after its owning process has already been
    /// proven to this run; it is then marked and registered.
    /// </summary>
    public static bool TryValidateWindow(WindowIdentity current, out string reason)
    {
        reason = string.Empty;
        if (current.ProcessId == NativeMethods.CurrentProcessId)
        {
            // The validation console is not an implicit input target.  A
            // point that resolves to the driver's own console must still have
            // gone through the same explicit HWND registration/marker path;
            // otherwise a covering shell window could be mistaken for a safe
            // test target merely because it shares the driver's PID.
            lock (Sync)
            {
                if (Windows.TryGetValue(current.Hwnd, out WindowRecord? ownWindow)
                    && SameStableIdentity(ownWindow.Identity, current)
                    && NativeMethods.GetProp(current.Hwnd, _markerName) == _markerValue)
                    return true;
            }
            reason = "validation-driver-window-not-registered";
            return false;
        }

        if (!TryValidateProcess(current.ProcessId, current.ExePath, current.ProcessStartTimeUtcTicks, out reason))
        {
            // Adopted external windows are the ONE exception: their owning
            // process is intentionally untracked (never spawned by this run),
            // so acceptance rests entirely on the pinned stable identity.
            lock (Sync)
            {
                if (Windows.TryGetValue(current.Hwnd, out WindowRecord? adopted)
                    && adopted.AdoptedExternal)
                {
                    if (SameStableIdentity(adopted.Identity, current))
                        return true;
                    reason = "adopted-external-window-identity-mismatch";
                }
            }
            return false;
        }

        WindowRecord? registered;
        lock (Sync)
            Windows.TryGetValue(current.Hwnd, out registered);

        if (registered != null)
        {
            if (!SameStableIdentity(registered.Identity, current))
            {
                reason = "registered-hwnd-stable-identity-mismatch";
                return false;
            }
            if (NativeMethods.GetProp(current.Hwnd, _markerName) != _markerValue)
            {
                reason = "registered-hwnd-run-marker-missing-or-mismatched";
                return false;
            }
            return true;
        }

        ProcessRecord record;
        lock (Sync)
        {
            if (!Processes.TryGetValue(current.ProcessId, out record!))
            {
                reason = "process-not-registered";
                return false;
            }
        }

        bool dynamicAllowed = ProvenanceContract.DynamicWindowAllowed(
            record.Role,
            HasRegisteredOwner(current.Hwnd, current.ProcessId));
        if (!dynamicAllowed)
        {
            reason = "unregistered-window-not-owned-by-test-run";
            return false;
        }

        if (!NativeMethods.SetProp(current.Hwnd, _markerName, _markerValue))
        {
            reason = $"dynamic-marker-install-failed win32={MarshalLastError()}";
            return false;
        }
        lock (Sync)
        {
            Windows[current.Hwnd] = new WindowRecord
            {
                Identity = current,
                Role = record.Role + ".DynamicSurface",
            };
        }
        return true;
    }

    public static bool IsProcessInScope(uint processId)
    {
        lock (Sync)
            return processId == NativeMethods.CurrentProcessId || Processes.ContainsKey(processId);
    }

    public static string ProcessRole(uint processId)
    {
        lock (Sync)
            return Processes.TryGetValue(processId, out ProcessRecord? record) ? record.Role : "Unknown";
    }

    public static string WindowRole(IntPtr hwnd)
    {
        lock (Sync)
            return Windows.TryGetValue(hwnd, out WindowRecord? record) ? record.Role : ProcessRoleForWindow(hwnd);
    }

    private static string ProcessRoleForWindow(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return ProcessRole(pid);
    }

    public static string SafeIdentity(WindowIdentity identity)
    {
        string role = WindowRole(identity.Hwnd);
        string title = SafeTitle(identity.Title, role);
        return $"hwnd=0x{identity.Hwnd.ToInt64():X} pid={identity.ProcessId} tid={identity.WindowThreadId} class='{identity.ClassName}' title='{title}' exe='{Path.GetFileName(identity.ExePath)}' start={identity.ProcessStartTimeUtcTicks} role={role}";
    }

    public static string SafeTitle(WindowIdentity identity)
        => SafeTitle(identity.Title, WindowRole(identity.Hwnd));

    public static string RedactPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile)
            && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
            return "%USERPROFILE%" + path.Substring(userProfile.Length);
        return path;
    }

    public static IReadOnlyList<uint> GetProcessAncestry(uint processId)
    {
        var parentMap = new Dictionary<uint, uint>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return Array.Empty<uint>();
        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PROCESSENTRY32>(),
            };
            if (NativeMethods.Process32First(snapshot, ref entry))
            {
                do
                {
                    parentMap[entry.th32ProcessID] = entry.th32ParentProcessID;
                }
                while (NativeMethods.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        var result = new List<uint>();
        uint current = processId;
        for (int i = 0; i < 64 && parentMap.TryGetValue(current, out uint parent) && parent != 0; i++)
        {
            result.Add(parent);
            current = parent;
        }
        return result;
    }

    public static string NextDiagnosticFileName(string prefix)
    {
        int sequence = System.Threading.Interlocked.Increment(ref _diagnosticSequence);
        return Path.Combine(ArtifactDirectory, $"{prefix}-{sequence:D4}.json");
    }

    public static IReadOnlyDictionary<string, object?> ScopeSummary()
    {
        var processes = new List<object>();
        lock (Sync)
        {
            foreach (ProcessRecord record in Processes.Values.OrderBy(p => p.Identity.ProcessId))
            {
                processes.Add(new
                {
                    pid = record.Identity.ProcessId,
                    startTimeUtcTicks = record.Identity.ProcessStartTimeUtcTicks,
                    executable = RedactPath(record.Identity.ExePath),
                    role = record.Role,
                    launchRootPid = record.LaunchRootPid,
                    ancestry = record.Ancestry,
                });
            }
        }
        return new Dictionary<string, object?>
        {
            ["runId"] = RunId,
            ["markerProperty"] = _markerName,
            ["processes"] = processes,
        };
    }

    private static bool TryValidateProcess(uint processId, string executable, long startTicks, out string reason)
    {
        lock (Sync)
        {
            if (!Processes.TryGetValue(processId, out ProcessRecord? expected))
            {
                reason = "process-not-registered";
                return false;
            }
            var actual = new ProcessIdentity(processId, startTicks, executable);
            if (expected.Identity.ProcessStartTimeUtcTicks != startTicks)
            {
                reason = "process-start-identity-mismatch";
                return false;
            }
            if (!ProvenanceContract.ProcessIdentityMatches(expected.Identity, actual, PathsEqual))
            {
                reason = "process-executable-identity-mismatch";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool HasRegisteredOwner(IntPtr hwnd, uint processId)
    {
        IntPtr owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        for (int i = 0; i < 16 && owner != IntPtr.Zero; i++)
        {
            lock (Sync)
            {
                if (Windows.TryGetValue(owner, out WindowRecord? record)
                    && record.Identity.ProcessId == processId
                    && NativeMethods.GetProp(owner, _markerName) == _markerValue)
                    return true;
            }
            owner = NativeMethods.GetWindow(owner, NativeMethods.GW_OWNER);
        }

        IntPtr parent = NativeMethods.GetParent(hwnd);
        for (int i = 0; i < 16 && parent != IntPtr.Zero; i++)
        {
            lock (Sync)
            {
                if (Windows.TryGetValue(parent, out WindowRecord? record)
                    && record.Identity.ProcessId == processId
                    && NativeMethods.GetProp(parent, _markerName) == _markerValue)
                    return true;
            }
            parent = NativeMethods.GetParent(parent);
        }
        return false;
    }

    private static bool TryReadProcessIdentity(uint processId, out ProcessIdentity identity)
    {
        string? exe = NativeMethods.GetProcessImagePath(processId);
        long start = Discover.TryGetProcessStartTimeUtcTicks(processId);
        if (string.IsNullOrWhiteSpace(exe) || start == 0)
        {
            identity = default;
            return false;
        }
        identity = new ProcessIdentity(processId, start, exe);
        return true;
    }

    internal static bool SameStableIdentity(WindowIdentity a, WindowIdentity b)
    {
        return ProvenanceContract.WindowIdentityMatches(a, b, PathsEqual);
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool ExecutableMatches(string actual, string expected)
    {
        if (File.Exists(expected))
            return PathsEqual(actual, expected);
        return string.Equals(Path.GetFileName(actual), Path.GetFileName(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeTitle(string title, string role)
    {
        return role.StartsWith("GuineaPig", StringComparison.Ordinal)
            || role.StartsWith("Browser", StringComparison.Ordinal)
            || title.StartsWith("TDTEST:", StringComparison.Ordinal)
            || title.StartsWith("TDVAL-", StringComparison.Ordinal)
            || role.StartsWith("TabDock", StringComparison.Ordinal);
    }

    private static string SafeTitle(string title, string role)
        => IsSafeTitle(title, role)
            ? title
            : $"<redacted len={title.Length} hash={ShortHash(title)}>";

    private static string ShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(hash.AsSpan(0, 6));
    }

    private static IntPtr CreateMarkerValue()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        long value = BitConverter.ToInt64(bytes);
        return new IntPtr(value == 0 ? 1 : value);
    }

    private static int MarshalLastError()
        => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}

/// <summary>
/// Native-free parts of the run-provenance contract. The live registry above
/// supplies the HWND marker and process ancestry; these rules make the
/// identity decisions independently testable without creating or targeting a
/// desktop window.
/// </summary>
internal static class ProvenanceContract
{
    internal static bool ProcessIdentityMatches(
        TestRunProvenance.ProcessIdentity expected,
        TestRunProvenance.ProcessIdentity actual,
        Func<string, string, bool> pathComparer)
        => expected.ProcessId == actual.ProcessId
            && expected.ProcessStartTimeUtcTicks == actual.ProcessStartTimeUtcTicks
            && pathComparer(expected.ExePath, actual.ExePath);

    internal static bool WindowIdentityMatches(
        WindowIdentity expected,
        WindowIdentity actual,
        Func<string, string, bool> pathComparer)
        => expected.Hwnd == actual.Hwnd
            && expected.ProcessId == actual.ProcessId
            && expected.WindowThreadId == actual.WindowThreadId
            && expected.ProcessStartTimeUtcTicks == actual.ProcessStartTimeUtcTicks
            && string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal)
            && pathComparer(expected.ExePath, actual.ExePath);

    internal static bool DynamicWindowAllowed(string processRole, bool hasRegisteredOwner)
        => processRole.StartsWith("Browser", StringComparison.Ordinal)
            || (processRole.StartsWith("TabDock", StringComparison.Ordinal) && hasRegisteredOwner);

    internal static bool AcceptWindowEvidence(
        bool processRegistered,
        bool processStartMatches,
        bool executableMatches,
        bool ancestryMatches,
        bool windowIdentityMatches,
        bool runMarkerMatches,
        bool isRegisteredWindow,
        bool hasRegisteredOwner,
        string processRole)
    {
        if (!processRegistered || !processStartMatches || !executableMatches)
            return false;
        if (isRegisteredWindow)
            return windowIdentityMatches && runMarkerMatches;
        if (!runMarkerMatches)
            return false;
        if (processRole.StartsWith("Browser", StringComparison.Ordinal))
            return ancestryMatches;
        return processRole.StartsWith("TabDock", StringComparison.Ordinal) && hasRegisteredOwner;
    }
}

/// <summary>Privacy-safe identity evidence emitted when the guard refuses input.</summary>
internal static class IdentityDiagnostics
{
    internal static bool HasActionableReason(string reason)
        => !string.IsNullOrWhiteSpace(reason);

    public static void RecordPointFailure(
        int x,
        int y,
        IntPtr expectedTarget,
        string reason)
    {
        IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr root = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
        IntPtr rootOwner = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOTOWNER);
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        IntPtr foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (foregroundRoot == IntPtr.Zero)
            foregroundRoot = foreground;
        string safeReason = TestRunProvenance.RedactPath(string.IsNullOrWhiteSpace(reason) ? "identity-proof-unavailable" : reason);
        WindowIdentity? observed = root != IntPtr.Zero && Discover.TryCaptureIdentity(root, out WindowIdentity current)
            ? current
            : null;

        var record = new Dictionary<string, object?>
        {
            ["runId"] = TestRunProvenance.RunId,
            ["scenario"] = TestRunProvenance.CurrentScenario,
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            ["reason"] = safeReason,
            ["targetScreenPoint"] = new { x, y },
            ["windowFromPoint"] = Hwnd(atPoint),
            ["gaRoot"] = Hwnd(root),
            ["gaRootOwner"] = Hwnd(rootOwner),
            ["foregroundWindow"] = Hwnd(foreground),
            ["foregroundRoot"] = Hwnd(foregroundRoot),
            ["parentChain"] = HwndChain(root, owner: false),
            ["ownerChain"] = HwndChain(root, owner: true),
            ["observed"] = observed.HasValue ? DescribeWindow(observed.Value) : null,
            ["expectedTarget"] = DescribeExpected(expectedTarget),
            ["uia"] = DescribeUia(x, y),
            ["scope"] = TestRunProvenance.ScopeSummary(),
        };

        string path = TestRunProvenance.NextDiagnosticFileName("identity-failure");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            GuardedProc.Log($"IDENTITY_SCOPE_DIAGNOSTIC runId={TestRunProvenance.RunIdCompact} scenario={TestRunProvenance.CurrentScenario} reason={safeReason} point=({x},{y}) root={Hwnd(root)} artifact=<validation-artifact>/{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"IDENTITY_SCOPE_DIAGNOSTIC_WRITE_FAILED runId={TestRunProvenance.RunIdCompact} reason={ex.GetType().Name}");
        }
    }

    private static object? DescribeExpected(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && Discover.TryCaptureIdentity(hwnd, out WindowIdentity identity)
            ? DescribeWindow(identity)
            : new { hwnd = Hwnd(hwnd), state = "unavailable" };
    }

    private static object DescribeWindow(WindowIdentity identity)
    {
        nint style = NativeMethods.GetWindowLongPtr(identity.Hwnd, -16);
        nint exStyle = NativeMethods.GetWindowLongPtr(identity.Hwnd, -20);
        return new
        {
            hwnd = Hwnd(identity.Hwnd),
            pid = identity.ProcessId,
            tid = identity.WindowThreadId,
            executable = TestRunProvenance.RedactPath(identity.ExePath),
            executableName = Path.GetFileName(identity.ExePath),
            processStartTimeUtcTicks = identity.ProcessStartTimeUtcTicks,
            windowClass = identity.ClassName,
            title = TestRunProvenance.SafeTitle(identity),
            visible = NativeMethods.IsWindowVisible(identity.Hwnd),
            enabled = NativeMethods.IsWindowEnabled(identity.Hwnd),
            outerRectangle = TryDescribeOuterRect(identity.Hwnd),
            clientRectangle = TryDescribeClientRect(identity.Hwnd),
            style = $"0x{style.ToInt64():X}",
            exStyle = $"0x{exStyle.ToInt64():X}",
            runMarker = NativeMethods.GetProp(identity.Hwnd, TestRunProvenance.MarkerName) == TestRunProvenance.MarkerValue,
            role = TestRunProvenance.WindowRole(identity.Hwnd),
            processAncestry = TestRunProvenance.GetProcessAncestry(identity.ProcessId),
        };
    }

    private static object? TryDescribeOuterRect(IntPtr hwnd)
    {
        return NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)
            ? new { left = rect.left, top = rect.top, width = rect.Width, height = rect.Height }
            : null;
    }

    private static object? TryDescribeClientRect(IntPtr hwnd)
    {
        return NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT rect)
            ? new { left = rect.left, top = rect.top, width = rect.Width, height = rect.Height }
            : null;
    }

    private static object DescribeUia(int x, int y)
    {
        try
        {
            AutomationElement element = AutomationElement.FromPoint(new Point(x, y));
            int processId = element.Current.ProcessId;
            string name = element.Current.Name ?? string.Empty;
            bool safe = TestRunProvenance.IsProcessInScope((uint)processId);
            return new
            {
                available = true,
                processId,
                automationId = element.Current.AutomationId ?? string.Empty,
                controlType = element.Current.ControlType?.ProgrammaticName ?? string.Empty,
                name = safe ? name : $"<redacted len={name.Length}>",
                boundingRectangle = Rect(element.Current.BoundingRectangle),
            };
        }
        catch (Exception ex)
        {
            return new { available = false, error = ex.GetType().Name };
        }
    }

    private static object[] HwndChain(IntPtr hwnd, bool owner)
    {
        var result = new List<object>();
        IntPtr current = owner
            ? NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER)
            : NativeMethods.GetParent(hwnd);
        for (int i = 0; i < 16 && current != IntPtr.Zero; i++)
        {
            if (Discover.TryCaptureIdentity(current, out WindowIdentity identity))
            {
                result.Add(new
                {
                    hwnd = Hwnd(current),
                    pid = identity.ProcessId,
                    tid = identity.WindowThreadId,
                    className = identity.ClassName,
                    role = TestRunProvenance.WindowRole(current),
                });
            }
            else
            {
                result.Add(new { hwnd = Hwnd(current), state = "unavailable" });
            }
            current = owner
                ? NativeMethods.GetWindow(current, NativeMethods.GW_OWNER)
                : NativeMethods.GetParent(current);
        }
        return result.ToArray();
    }

    private static object Rect(Rect rect)
        => new { left = rect.Left, top = rect.Top, width = rect.Width, height = rect.Height };

    private static string Hwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";
}
