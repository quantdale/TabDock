using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Models;
using TabDock.ViewModels;
using TabDock.Views;

namespace TabDock.Services;

public enum DiagnosticCommandKind
{
    None,
    Version,
    Doctor,
    SupportBundle,
    SelfTest,
    PendingRecovery,
    RecoverPending,
}

public sealed class DiagnosticCommandRequest
{
    public DiagnosticCommandKind Kind { get; init; }
    public string? OutputPath { get; init; }
}

/// <summary>Small dependency-free parser for the supported diagnostic commands.</summary>
public static class DiagnosticCommandLine
{
    public static bool TryParse(IEnumerable<string> args, out DiagnosticCommandRequest request, out string? error)
    {
        string[] values = args.ToArray();
        error = null;
        request = new DiagnosticCommandRequest();
        if (values.Length == 0)
            return false;

        DiagnosticCommandKind kind = values[0].ToLowerInvariant() switch
        {
            "--version" => DiagnosticCommandKind.Version,
            "--doctor" => DiagnosticCommandKind.Doctor,
            "--support-bundle" => DiagnosticCommandKind.SupportBundle,
            "--selftest-diagnostics" => DiagnosticCommandKind.SelfTest,
            "--pending-recovery" => DiagnosticCommandKind.PendingRecovery,
            "--recover-pending" => DiagnosticCommandKind.RecoverPending,
            _ => DiagnosticCommandKind.None,
        };
        if (kind == DiagnosticCommandKind.None)
            return false;

        string? output = null;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= values.Length || string.IsNullOrWhiteSpace(values[i]))
                {
                    error = "--output requires a path";
                    return true;
                }
                output = values[i];
            }
            else
            {
                error = $"unrecognized diagnostic option '{values[i]}'";
                return true;
            }
        }
        request = new DiagnosticCommandRequest { Kind = kind, OutputPath = output };
        return true;
    }

    public static bool IsDiagnosticCommand(IEnumerable<string> args)
        => TryParse(args, out _, out _);

    public static int Run(IEnumerable<string> args)
    {
        if (!TryParse(args, out DiagnosticCommandRequest request, out string? error))
            return 0;
        if (error != null)
        {
            Write("diagnostic command failed: " + error);
            return 2;
        }
        return Run(request);
    }

    public static int Run(DiagnosticCommandRequest request)
    {
        try
        {
            switch (request.Kind)
            {
                case DiagnosticCommandKind.Version:
                    Write(DiagnosticReportService.FormatVersion(BuildIdentity.Capture(includeHash: true)));
                    return 0;
                case DiagnosticCommandKind.Doctor:
                    string report = DiagnosticReportService.FormatDoctor(DiagnosticReportService.CreateReport(includeHash: true));
                    if (string.IsNullOrWhiteSpace(request.OutputPath))
                        Write(report);
                    else
                    {
                        File.WriteAllText(Path.GetFullPath(request.OutputPath), report);
                        Write($"doctor report: {Path.GetFullPath(request.OutputPath)}");
                    }
                    return 0;
                case DiagnosticCommandKind.SupportBundle:
                    string bundle = DiagnosticReportService.ExportBundle(request.OutputPath);
                    Write($"support bundle: {bundle}");
                    return 0;
                case DiagnosticCommandKind.SelfTest:
                    (int checks, int failures) = DiagnosticSelfTest.Run();
                    Write($"SELFTEST[diagnostics] checks={checks} failures={failures} result={(failures == 0 ? "PASS" : "FAIL")}");
                    if (PersistenceSelfTest.LastAccessDeniedFixtureStatus is string aclStatus && aclStatus != "pass")
                        Write($"SELFTEST[diagnostics] persistence-acl-fixture={aclStatus}");
                    return failures == 0 ? 0 : 1;
                case DiagnosticCommandKind.PendingRecovery:
                    string discovery = PendingRecoveryService.FormatDiscovery();
                    if (string.IsNullOrWhiteSpace(request.OutputPath))
                        Write(discovery);
                    else
                    {
                        File.WriteAllText(Path.GetFullPath(request.OutputPath), discovery);
                        Write($"pending recovery report: {Path.GetFullPath(request.OutputPath)}");
                    }
                    return 0;
                case DiagnosticCommandKind.RecoverPending:
                    if (!ProductMutationLease.TryAcquire(out ProductMutationLease? recoveryLease))
                    {
                        Write("Supervised recovery refused: another TabDock mutation owner is running. Exit the running TabDock instance before supervised recovery.");
                        return 3;
                    }
                    using (recoveryLease!)
                    {
                        if (!ConsoleSession.TryCreate(out ConsoleSession? session, out string? consoleError))
                        {
                            Write("Supervised recovery requires a console or redirected standard input/output: " + consoleError);
                            return 2;
                        }
                        ConsoleSession interactiveSession = session!;
                        using (interactiveSession)
                            return PendingRecoveryService.RunInteractive(interactiveSession.Input, interactiveSession.Output);
                    }
                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Write($"diagnostic command failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void Write(string text)
    {
        bool attached = false;
        try
        {
            attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        }
        catch { }
        try
        {
            Console.WriteLine(text);
        }
        catch (IOException)
        {
            System.Diagnostics.Debug.WriteLine(text);
        }
        finally
        {
            if (attached)
            {
                try { NativeMethods.FreeConsole(); } catch { }
            }
        }
    }
}

internal static class DiagnosticSelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition) failures++;
        }

        Check(BuildIdentity.ParseCommitHash("1.2.3+abcdef0123456789") == "abcdef0123456789");
        Check(BuildIdentity.ParseCommitHash("1.2.3") == null);
        Check(BuildIdentity.ParseSemanticVersion("1.2.3+abcdef", new Version(9, 9)) == "1.2.3");
        Check(BuildIdentity.ParseSemanticVersion(null, new Version(9, 9)) == "9.9");

        // Windows product normalization: a Windows 11 build must never be
        // mislabeled solely because the registry ProductName says Windows 10,
        // and a real Windows 10 build must remain identifiable as Windows 10.
        // The raw registry evidence stays available for forensics.
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Pro", "19045") == "Windows 10 Pro");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Pro", "22631") == "Windows 11 Pro");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 11 Pro", "22631") == "Windows 11 Pro");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Enterprise LTSC", "26100") == "Windows 11 Enterprise LTSC");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Pro", "21996") == "Windows 10 Pro");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName(null, "22631") == "Windows 11 (build 22631; raw ProductName: unavailable)");
        Check(DiagnosticEnvironmentService.GetWindowsProductFamily("22631", "Windows 10 Pro") == "Windows 11");
        Check(DiagnosticEnvironmentService.GetWindowsProductFamily("19045", "Windows 10 Pro") == "Windows 10 or earlier");
        Check(DiagnosticEnvironmentService.GetWindowsProductFamily(null, "Windows 10 Pro") == "Windows 10 (raw registry evidence)");
        Check(DiagnosticEnvironmentService.GetWindowsProductFamily(null, null) == "unavailable");

        Check(DiagnosticCommandLine.TryParse(new[] { "--version" }, out DiagnosticCommandRequest version, out _)
            && version.Kind == DiagnosticCommandKind.Version);
        Check(DiagnosticCommandLine.TryParse(new[] { "--doctor", "--output", "report.txt" }, out DiagnosticCommandRequest doctor, out _)
            && doctor.Kind == DiagnosticCommandKind.Doctor && doctor.OutputPath == "report.txt");
        Check(DiagnosticCommandLine.TryParse(new[] { "--doctor", "--bad" }, out _, out string? parserError)
            && parserError != null);
        Check(DiagnosticCommandLine.TryParse(new[] { "--pending-recovery" }, out DiagnosticCommandRequest pending, out _)
            && pending.Kind == DiagnosticCommandKind.PendingRecovery);
        Check(DiagnosticCommandLine.TryParse(new[] { "--recover-pending" }, out DiagnosticCommandRequest recover, out _)
            && recover.Kind == DiagnosticCommandKind.RecoverPending);

        var trace = new DiagnosticTrace(3);
        long first = trace.Record("one");
        long second = trace.Record("two");
        trace.Record("three");
        trace.Record("four");
        IReadOnlyList<TabDock.Models.DiagnosticEventRecord> events = trace.Snapshot();
        Check(second == first + 1);
        Check(events.Count == 3 && events[0].Kind == "two" && events[^1].Kind == "four");
        Check(events[0].Sequence < events[^1].Sequence);
        var callerData = new Dictionary<string, string> { ["key"] = "before" };
        var defensiveTrace = new DiagnosticTrace(2);
        defensiveTrace.Record("defensive", data: callerData);
        callerData["key"] = "after";
        IReadOnlyList<DiagnosticEventRecord> defensiveSnapshot = defensiveTrace.Snapshot();
        Check(defensiveSnapshot[0].Data["key"] == "before");
        defensiveSnapshot[0].Data["key"] = "mutated-snapshot";
        Check(defensiveTrace.Snapshot()[0].Data["key"] == "before");
        defensiveTrace.Clear();
        Check(defensiveTrace.Snapshot().Count == 0);
        Check(DiagnosticEnvironmentService.HashTitle("secret") != "secret");
        Check(DiagnosticEnvironmentService.ClassifyJsonText("{\"Version\":1,\"Groups\":[]}", isState: true) == "valid");
        Check(DiagnosticEnvironmentService.ClassifyJsonText("not-json", isState: true).StartsWith("corrupt", StringComparison.Ordinal));
        Check(ConsoleSessionSelfTest.UsesScopedStreams());
        Check(ProductMutationLeaseSelfTest.UserScopedNameRules());
        Check(ProductMutationLeaseSelfTest.AccessControlRulesAreUserScoped());
        Check(ProductMutationLeaseSelfTest.ExclusiveAndReusable());
        Check(ProductMutationLeaseSelfTest.AccessDeniedAndConstructionFailuresFailClosed());
        Check(ProductMutationLeaseSelfTest.DiagnosticCommandsRemainLeaseIndependent());
        Check(ProductMutationLeaseSelfTest.DifferentUserScopedLeasesCanCoexist());
        Check(DeferredWindowPositionSelfTest.ChangedHandlesAreChained());
        Check(DeferredWindowPositionSelfTest.FailedDeferAbandonsWithoutEnd());
        Check(DeferredWindowPositionSelfTest.StaleGuestIsNotQueuedAndValidHdwpIsClosed());
        Check(DeferredWindowPositionSelfTest.ValidatorRunsBeforeEachQueue());
        Guid selectedGroupId = Guid.NewGuid();
        var pickerOptions = new[]
        {
            new CapturePickerViewModel.GroupOption(Guid.Empty, "<New group>"),
            new CapturePickerViewModel.GroupOption(selectedGroupId, "Existing"),
        };
        Check(CapturePickerViewModel.SelectGroupAfterRefresh(pickerOptions, selectedGroupId)?.Id == selectedGroupId);
        Check(CapturePickerViewModel.SelectGroupAfterRefresh(pickerOptions, Guid.NewGuid())?.Id == Guid.Empty);
        Check(CapturePickerSelfTest.BackgroundIconResolutionIsGenerationSafe());
        Check(WinEventMonitorSelfTest.FailedInstallUnwindsAndFailsClosed());
        Check(WinEventMonitorSelfTest.DesktopReorderDropsUncapturedAndRejectsStaleDispatch());
        Check(SessionEndingPolicySelfTest.TeardownIsOneWayAndIdempotent());
        Check(MinTrackProbeSelfTest.InitializesEveryField());
        Check(WindowShepherdService.MinTrackProbeTimeoutMilliseconds <= 100);
        Check(WindowIdentitySelfTest.CoversIdentityTiers());
        (int captureChecks, int captureFailures) = CaptureBoundarySelfTest.Run();
        checks += captureChecks;
        failures += captureFailures;
        (int releaseChecks, int releaseFailures) = WindowReleaseSelfTest.Run();
        checks += releaseChecks;
        failures += releaseFailures;
        Check(MonitorDpiSelfTest.CoversProbeAndConversionSeam());
        Check(ShowWindowSemanticsSelfTest.CoversPostStateSemantics());
        Check(NativeInteropSelfTest.PlacementContractIsStable());
        Check(NativeInteropSelfTest.PlacementRoundTripThroughUser32());
        Check(ContainerGeometrySelfTest.UsesContainingMonitorWorkArea());
        (int journalChecks, int journalFailures) = RecoveryJournalSelfTest.Run();
        checks += journalChecks;
        failures += journalFailures;
        (int persistenceChecks, int persistenceFailures) = PersistenceSelfTest.Run();
        checks += persistenceChecks;
        failures += persistenceFailures;
        (int privacyChecks, int privacyFailures) = DiagnosticPrivacySelfTest.Run();
        checks += privacyChecks;
        failures += privacyFailures;
        (int pendingChecks, int pendingFailures) = PendingRecoverySelfTest.Run();
        checks += pendingChecks;
        failures += pendingFailures;
        var concurrent = new DiagnosticTrace(128);
        Parallel.For(0, 512, i => concurrent.Record("concurrent"));
        Check(concurrent.Snapshot().Count == 128);
        Check(concurrent.Snapshot().Zip(concurrent.Snapshot().Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        return (checks, failures);
    }
}

internal static class ShowWindowSemanticsSelfTest
{
    public static bool CoversPostStateSemantics()
    {
        // The first argument models ShowWindow's previous-visibility BOOL.
        // It is deliberately false for hidden/minimized restore and must not
        // turn a successful post-state into a failure.
        bool hiddenRestore = ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: true, iconicAfter: false, zoomedAfter: false);
        bool minimizedRestore = ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: true, visibleAfter: true, iconicAfter: false, zoomedAfter: false);
        bool visibleRestore = ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: true, visibleAfter: true, iconicAfter: false, zoomedAfter: false);
        bool hiddenNormalStillHidden = !ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: false, iconicAfter: false, zoomedAfter: false);
        bool stillIconic = !ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: true, iconicAfter: true, zoomedAfter: false);
        bool stillZoomed = !ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: true, iconicAfter: false, zoomedAfter: true);
        bool hide = ShowWindowSemantics.VisibilitySucceeded(
            previouslyVisible: true, visibleAfter: false, expectedVisible: false);
        bool releaseShow = ShowWindowSemantics.VisibilitySucceeded(
            previouslyVisible: false, visibleAfter: true, expectedVisible: true);
        bool intentionalHide = ShowWindowSemantics.VisibilitySucceeded(
            previouslyVisible: true, visibleAfter: false, expectedVisible: false);
        bool failedVisibility = !ShowWindowSemantics.VisibilitySucceeded(
            previouslyVisible: false, visibleAfter: false, expectedVisible: true);
        var positioningFailuresLogged = new HashSet<IntPtr>();
        if (!hiddenRestore)
            positioningFailuresLogged.Add(new IntPtr(1));
        bool genuineFailureWasRecorded = stillIconic
            && positioningFailuresLogged.Add(new IntPtr(1));
        bool benignFalseDidNotConsumeFailureSlot = genuineFailureWasRecorded;

        return hiddenRestore && minimizedRestore && visibleRestore
            && hiddenNormalStillHidden && stillIconic && stillZoomed
            && hide && releaseShow && intentionalHide && failedVisibility
            && benignFalseDidNotConsumeFailureSlot;
    }
}

/// <summary>
/// Deterministic regression protection for the WINDOWPLACEMENT interop
/// contract (see the NativeMethods.WINDOWPLACEMENT documentation). Modern
/// Windows 10/11 user32 accepts only a 44-byte structure with
/// length = 44: the SDK header's trailing RECT rcDevice is never populated,
/// and SetWindowPlacement rejects length = 60 with ERROR_INVALID_PARAMETER.
/// Both tests fail loudly if a supported Windows build ever changes that
/// contract, which is exactly the signal a placement-restore regression would
/// need.
/// </summary>
internal static class NativeInteropSelfTest
{
    public static bool PlacementContractIsStable()
    {
        // 44 bytes on both x86 and x64 (4+4+4+8+8+16); rcDevice is
        // intentionally absent. All offsets are identical on x86 and x64
        // because every member is a 4-byte-aligned int.
        bool sizeOk = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() == 44;
        bool offsetsOk =
            Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("length").ToInt32() == 0
            && Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("flags").ToInt32() == 4
            && Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("showCmd").ToInt32() == 8
            && Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("ptMinPosition").ToInt32() == 12
            && Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("ptMaxPosition").ToInt32() == 20
            && Marshal.OffsetOf<NativeMethods.WINDOWPLACEMENT>("rcNormalPosition").ToInt32() == 28;
        return sizeOk && offsetsOk;
    }

    /// <summary>
    /// Native get/set round trip on a window this test creates and destroys
    /// itself. The window is never shown, so the probe produces no desktop
    /// artifacts, sends no input, and never touches an existing window.
    /// The zero-length rejection proves the native function reads length from
    /// the caller's buffer — the by-reference parameter semantics that keep
    /// the caller's initialization authoritative.
    /// </summary>
    public static bool PlacementRoundTripThroughUser32()
    {
        IntPtr hwnd = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            "TabDock.NativeAbiSelfTest",
            0,
            10,
            10,
            320,
            200,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            uint length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

            // Caller-initialized length is the documented precondition; it
            // must reach user32 because the structure is passed by ref.
            var initial = new NativeMethods.WINDOWPLACEMENT { length = length };
            if (!NativeMethods.GetWindowPlacement(hwnd, ref initial))
                return false;
            if (initial.length != 44)
                return false; // native reports the accepted buffer size
            if (initial.rcNormalPosition.right <= initial.rcNormalPosition.left
                || initial.rcNormalPosition.bottom <= initial.rcNormalPosition.top)
                return false; // the created window rect must be reflected

            var target = new NativeMethods.WINDOWPLACEMENT
            {
                length = length,
                ptMinPosition = new NativeMethods.POINT { x = -32000, y = -32000 },
                ptMaxPosition = new NativeMethods.POINT { x = -1, y = -1 },
                rcNormalPosition = new NativeMethods.RECT { left = 120, top = 130, right = 620, bottom = 530 },
                showCmd = NativeMethods.SW_SHOWNORMAL,
            };
            if (!NativeMethods.SetWindowPlacement(hwnd, ref target))
                return false;

            var readBack = new NativeMethods.WINDOWPLACEMENT { length = length };
            if (!NativeMethods.GetWindowPlacement(hwnd, ref readBack))
                return false;
            if (readBack.showCmd != NativeMethods.SW_SHOWNORMAL)
                return false;
            if (readBack.rcNormalPosition.left != 120 || readBack.rcNormalPosition.top != 130
                || readBack.rcNormalPosition.right != 620 || readBack.rcNormalPosition.bottom != 530)
                return false;

            // An uninitialized (length = 0) structure must be rejected: the
            // by-reference length contract is what makes the call succeed.
            var uninitialized = new NativeMethods.WINDOWPLACEMENT();
            return !NativeMethods.SetWindowPlacement(hwnd, ref uninitialized);
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }
}

internal static class MinTrackProbeSelfTest
{
    public static bool InitializesEveryField()
    {
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MINMAXINFO>());
        try
        {
            // Poison the allocation first so the test proves the helper writes
            // the complete structure rather than relying on allocator zeroing.
            for (int i = 0; i < System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MINMAXINFO>(); i++)
                System.Runtime.InteropServices.Marshal.WriteByte(buffer, i, 0xA5);

            WindowShepherdService.InitializeMinTrackProbeBuffer(buffer);
            NativeMethods.MINMAXINFO value = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(buffer);
            return value.ptReserved.x == 0 && value.ptReserved.y == 0
                && value.ptMaxSize.x == 0 && value.ptMaxSize.y == 0
                && value.ptMaxPosition.x == 0 && value.ptMaxPosition.y == 0
                && value.ptMinTrackSize.x == 0 && value.ptMinTrackSize.y == 0
                && value.ptMaxTrackSize.x == 0 && value.ptMaxTrackSize.y == 0;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }
}

internal static class ContainerGeometrySelfTest
{
    public static bool UsesContainingMonitorWorkArea()
    {
        var monitor = new NativeMethods.MONITORINFO
        {
            rcMonitor = new NativeMethods.RECT { left = 1920, top = -300, right = 3840, bottom = 1140 },
            rcWork = new NativeMethods.RECT { left = 1920, top = -260, right = 3840, bottom = 1100 },
        };
        var minMax = new NativeMethods.MINMAXINFO
        {
            ptMaxPosition = new NativeMethods.POINT { x = -1, y = -1 },
            ptMaxSize = new NativeMethods.POINT { x = -1, y = -1 },
        };
        ContainerWindow.ApplyMonitorMaximizeBounds(monitor, ref minMax);
        return minMax.ptMaxPosition.x == 0
            && minMax.ptMaxPosition.y == 40
            && minMax.ptMaxSize.x == 1920
            && minMax.ptMaxSize.y == 1360;
    }
}

internal static class CapturePickerSelfTest
{
    public static bool BackgroundIconResolutionIsGenerationSafe()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-picker-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstExtractionStarted = new ManualResetEventSlim();
        var releaseFirstExtraction = new ManualResetEventSlim();
        int extractionCount = 0;
        int sourceGeneration = 0;
        DrawingImage oldIcon = FrozenImage();
        DrawingImage currentIcon = FrozenImage();

        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, path =>
            {
                int call = Interlocked.Increment(ref extractionCount);
                if (call == 1)
                {
                    firstExtractionStarted.Set();
                    releaseFirstExtraction.Wait(TimeSpan.FromSeconds(2));
                    return oldIcon;
                }
                return currentIcon;
            });

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
            {
                string path = Volatile.Read(ref sourceGeneration) == 0
                    ? @"C:\Perf\Old.exe"
                    : @"C:\Perf\Current.exe";
                return new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x101), 101, "Perf", "First", path),
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x102), 102, "Perf", "Second", path),
                };
            }

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            if (picker.Windows.Count != 2
                || picker.Windows[0].Title != "First"
                || picker.Windows[1].Title != "Second"
                || picker.Windows.Any(row => row.Icon != null)
                || !firstExtractionStarted.Wait(TimeSpan.FromSeconds(2)))
            {
                return false;
            }

            // Invalidate refresh N while its old executable is still being
            // extracted. Refresh N+1 has a different path and must win.
            Volatile.Write(ref sourceGeneration, 1);
            picker.Refresh();
            if (!picker.IconResolutionCompletion.Wait(TimeSpan.FromSeconds(2)))
                return false;
            if (!PumpUntil(() => picker.Windows.All(row => ReferenceEquals(row.Icon, currentIcon)), 2000))
                return false;

            releaseFirstExtraction.Set();
            if (!picker.IconResolutionCompletion.Wait(TimeSpan.FromSeconds(2)))
                return false;

            int callsAfterColdRefresh = Volatile.Read(ref extractionCount);
            picker.Refresh();
            bool cachedRowsAreImmediate = picker.IconResolutionCompletion.IsCompleted
                && picker.Windows.All(row => ReferenceEquals(row.Icon, currentIcon))
                && Volatile.Read(ref extractionCount) == callsAfterColdRefresh;

            var failingIcons = new IconService(log, _ => throw new InvalidOperationException("test icon failure"));
            using var failingPicker = new CapturePickerViewModel(
                manager,
                failingIcons,
                log,
                () => new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x103), 103, "Perf", "Failure", @"C:\Perf\Failure.exe"),
                });
            bool failureDoesNotBreakRefresh = failingPicker.IconResolutionCompletion.Wait(TimeSpan.FromSeconds(2));
            return cachedRowsAreImmediate && failureDoesNotBreakRefresh;
        }
        finally
        {
            releaseFirstExtraction.Set();
            firstExtractionStarted.Dispose();
            releaseFirstExtraction.Dispose();
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static DrawingImage FrozenImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        if (condition())
            return true;

        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        Stopwatch stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                frame.Continue = false;
            }
        }, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }
}

internal static class WinEventMonitorSelfTest
{
    public static bool FailedInstallUnwindsAndFailsClosed()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-winevent-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            var api = new FakeApi(failOnHookInAttempt: 4);
            using var log = new LoggingService(root);
            using var monitor = new WinEventMonitor(_ => false, _ => null, log, api);
            bool started = monitor.Start();
            return !started && !monitor.IsRunning && api.SetCount >= 7 && api.UnhookCount >= 3;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    public static bool DesktopReorderDropsUncapturedAndRejectsStaleDispatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-winevent-reorder-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            var context = new RecordingContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var api = new EventApi();
            IntPtr desktop = new(0xD); // test-only sentinel, not a native HWND
            IntPtr foreground = new(0x9999);
            var members = new Dictionary<IntPtr, CapturedWindow?>();
            var memberA = new CapturedWindow { Hwnd = foreground };
            var memberB = new CapturedWindow { Hwnd = foreground };

            using var log = new LoggingService(root);
            using var monitor = new WinEventMonitor(
                hwnd => members.TryGetValue(hwnd, out CapturedWindow? member) && member != null,
                hwnd => members.TryGetValue(hwnd, out CapturedWindow? member) ? member : null,
                log,
                api,
                () => desktop,
                () => foreground);

            int dispatched = 0;
            monitor.WindowZOrderChanged += (_, _) => dispatched++;
            if (!monitor.Start())
                return false;

            int callbackTraceCount = CountReorderTrace("callback", foreground);
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            bool uncapturedDropped = context.PostCount == 0
                && CountReorderTrace("callback", foreground) == callbackTraceCount;

            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            if (context.PostCount != 1)
                return false;
            context.DispatchNext();
            if (dispatched != 1)
                return false;

            // Release the captured object before the queued UI hop.
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            members.Remove(foreground);
            context.DispatchNext();
            if (dispatched != 1)
                return false;

            // Recycle the same numeric HWND to a different CapturedWindow.
            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            members[foreground] = memberB;
            context.DispatchNext();
            if (dispatched != 1)
                return false;

            // The original object still resolves and dispatches normally.
            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            context.DispatchNext();
            bool relevantTracePreserved = CountReorderTrace("callback", foreground) >= callbackTraceCount + 3
                && CountReorderTrace("dispatch", foreground) >= 2;
            return uncapturedDropped && dispatched == 2 && relevantTracePreserved;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static int CountReorderTrace(string phase, IntPtr hwnd)
        => DiagnosticRuntime.Trace.Snapshot().Count(eventRecord =>
            eventRecord.Kind == "EVENT_OBJECT_REORDER." + phase
            && eventRecord.GuestHwnd == hwnd.ToInt64());

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly Queue<Action> _pending = new();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCount++;
            _pending.Enqueue(() => callback(state));
        }

        public void DispatchNext()
        {
            if (_pending.Count == 0)
                throw new InvalidOperationException("Expected a posted WinEvent dispatch.");
            _pending.Dequeue()();
        }
    }

    private sealed class EventApi : IWinEventHookApi
    {
        private NativeMethods.WinEventProc? _callback;
        private int _nextHook;

        public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        {
            _callback = callback;
            return new IntPtr(++_nextHook);
        }

        public bool Unhook(IntPtr hook) => true;

        public void Raise(IntPtr hwnd, uint eventType, int idObject, int idChild)
            => _callback?.Invoke(IntPtr.Zero, eventType, hwnd, idObject, idChild, 0, 1);
    }

    private sealed class FakeApi : IWinEventHookApi
    {
        private readonly int _failOnHookInAttempt;
        private int _hookInAttempt;

        public FakeApi(int failOnHookInAttempt)
        {
            _failOnHookInAttempt = failOnHookInAttempt;
        }

        public int SetCount { get; private set; }
        public int UnhookCount { get; private set; }

        public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        {
            SetCount++;
            _hookInAttempt++;
            if (_hookInAttempt == 7)
                _hookInAttempt = 0;
            return _hookInAttempt == _failOnHookInAttempt
                ? IntPtr.Zero
                : new IntPtr(SetCount);
        }

        public bool Unhook(IntPtr hook)
        {
            UnhookCount++;
            return true;
        }
    }
}

internal static class DeferredWindowPositionSelfTest
{
    public static bool ChangedHandlesAreChained()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });
        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(api, Entries());
        return result == DeferredWindowPositionResult.Applied
            && api.DeferInputs.Count == 3
            && api.DeferInputs[0] == new IntPtr(0x11)
            && api.DeferInputs[1] == new IntPtr(0x22)
            && api.DeferInputs[2] == new IntPtr(0x33)
            && api.EndInput == new IntPtr(0x44);
    }

    public static bool FailedDeferAbandonsWithoutEnd()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), IntPtr.Zero, new IntPtr(0x44) });
        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(api, Entries());
        return result == DeferredWindowPositionResult.DeferFailed
            && api.DeferInputs.Count == 2
            && api.DeferInputs[0] == new IntPtr(0x11)
            && api.DeferInputs[1] == new IntPtr(0x22)
            && api.EndInput == IntPtr.Zero;
    }

    public static bool StaleGuestIsNotQueuedAndValidHdwpIsClosed()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });
        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(
            api,
            Entries(),
            beforeDefer: index => index != 0);
        return result == DeferredWindowPositionResult.ValidationFailed
            && api.DeferInputs.Count == 0
            && api.EndInput == new IntPtr(0x11);
    }

    public static bool ValidatorRunsBeforeEachQueue()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });
        var calls = new List<int>();
        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(
            api,
            Entries(),
            beforeDefer: index =>
            {
                calls.Add(index);
                return index != 1;
            });
        return result == DeferredWindowPositionResult.ValidationFailed
            && calls.SequenceEqual(new[] { 0, 1 })
            && api.DeferInputs.Count == 1
            && api.EndInput == new IntPtr(0x22);
    }

    private static IReadOnlyList<DeferredWindowPositionEntry> Entries()
    {
        return new[]
        {
            new DeferredWindowPositionEntry(new IntPtr(0x101), IntPtr.Zero, 1, 2, 3, 4, 5),
            new DeferredWindowPositionEntry(new IntPtr(0x102), IntPtr.Zero, 6, 7, 8, 9, 10),
            new DeferredWindowPositionEntry(new IntPtr(0x103), IntPtr.Zero, 11, 12, 13, 14, 15),
        };
    }

    private sealed class FakeApi : IDeferredWindowPositionApi
    {
        private readonly IReadOnlyList<IntPtr> _returns;
        private int _deferIndex;

        public FakeApi(IReadOnlyList<IntPtr> returns)
        {
            _returns = returns;
        }

        public List<IntPtr> DeferInputs { get; } = new();
        public IntPtr EndInput { get; private set; }

        public IntPtr Begin(int windowCount) => new(0x11);

        public IntPtr Defer(IntPtr hdwp, IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            DeferInputs.Add(hdwp);
            return _returns[_deferIndex++];
        }

        public bool End(IntPtr hdwp)
        {
            EndInput = hdwp;
            return true;
        }
    }
}
