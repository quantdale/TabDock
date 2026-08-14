using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        Check(WinEventMonitorSelfTest.FailedInstallUnwindsAndFailsClosed());
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
