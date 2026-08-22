using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TabDock.Services;

public enum DiagnosticCommandKind
{
    None,
    Version,
    Doctor,
    SupportBundle,
    SelfTest,
    SelfTestNativeAbi,
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
            "--selftest-native-abi" => DiagnosticCommandKind.SelfTestNativeAbi,
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
                    return failures == 0 ? 0 : 1;
                case DiagnosticCommandKind.SelfTestNativeAbi:
                    bool contractOk = NativeInteropSelfTest.PlacementContractIsStable();
                    bool roundTripOk = NativeInteropSelfTest.PlacementRoundTripThroughUser32();
                    Write(NativeInteropSelfTest.PlacementEnvironmentReport());
                    bool nativeAbiOk = contractOk && roundTripOk;
                    Write($"SELFTEST[native-abi] placementContract={(contractOk ? "PASS" : "FAIL")} placementRoundTrip={(roundTripOk ? "PASS" : "FAIL")} result={(nativeAbiOk ? "PASS" : "FAIL")}");
                    return nativeAbiOk ? 0 : 1;
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

        Check(NativeInteropSelfTest.PlacementContractIsStable());
        Check(NativeInteropSelfTest.PlacementRoundTripThroughUser32());
        return (checks, failures);
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
/// need. They run against REAL user32 on whatever machine executes them
/// (hosted CI runners included), so every qualification run produces
/// environment-specific ABI evidence; --selftest-native-abi additionally
/// prints that evidence as a report.
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
            if (NativeMethods.SetWindowPlacement(hwnd, ref uninitialized))
                return false;

            // The SDK header documents sizeof(WINDOWPLACEMENT) = 60 bytes on
            // x64 (trailing rcDevice), but the empirically-validated runtime
            // contract is 44 bytes; a length-60 buffer must be rejected. If a
            // Windows build ever accepts 60, this fails loudly and the
            // structure-size decision must be revisited with that evidence
            // (a compatibility wrapper would be the safe response, not a
            // silent global size change).
            var sdkSized = new NativeMethods.WINDOWPLACEMENT { length = 60 };
            return !NativeMethods.SetWindowPlacement(hwnd, ref sdkSized);
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }

    /// <summary>
    /// Environment evidence for the placement ABI, emitted by
    /// --selftest-native-abi: the OS identity of THIS machine plus the
    /// concrete accepted length and get/set behavior observed from real
    /// user32. The report records what was observed so a qualification run
    /// can be attributed to an OS build; it is evidence for the compatibility
    /// matrix (docs/release/compatibility-matrix.md), never a claim about
    /// untested Windows versions.
    /// </summary>
    public static string PlacementEnvironmentReport()
    {
        var environment = DiagnosticEnvironmentService.CaptureWindows();
        IntPtr hwnd = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            "TabDock.NativeAbiReport",
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
        {
            return string.Join(Environment.NewLine,
                "WINDOWPLACEMENT environment report: probe window unavailable; only OS identity recorded",
                $"  os: {environment.ProductFamily} (build {environment.Build}, {environment.OsVersion})");
        }
        try
        {
            uint length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
            var getProbe = new NativeMethods.WINDOWPLACEMENT { length = length };
            bool getOk = NativeMethods.GetWindowPlacement(hwnd, ref getProbe);
            var setProbe = new NativeMethods.WINDOWPLACEMENT
            {
                length = length,
                ptMinPosition = new NativeMethods.POINT { x = -32000, y = -32000 },
                ptMaxPosition = new NativeMethods.POINT { x = -1, y = -1 },
                rcNormalPosition = new NativeMethods.RECT { left = 120, top = 130, right = 620, bottom = 530 },
                showCmd = NativeMethods.SW_SHOWNORMAL,
            };
            bool setOk = NativeMethods.SetWindowPlacement(hwnd, ref setProbe);
            var set60Probe = new NativeMethods.WINDOWPLACEMENT { length = 60 };
            bool set60Accepted = NativeMethods.SetWindowPlacement(hwnd, ref set60Probe);
            return string.Join(Environment.NewLine,
                "WINDOWPLACEMENT environment report:",
                $"  os: {environment.ProductFamily} (build {environment.Build}, {environment.OsVersion})",
                "  sdkDocumentedSize: 60 (SDK header includes trailing rcDevice)",
                $"  runtimeAcceptedLength: {length}",
                $"  GetWindowPlacement({length}): {(getOk ? $"OK (native writes accepted length back as {getProbe.length})" : "FAILED")}",
                $"  SetWindowPlacement({length}): {(setOk ? "OK" : "FAILED")}",
                $"  SetWindowPlacement(60): {(set60Accepted ? "ACCEPTED (contract changed - investigate before trusting the 44-byte contract)" : "REJECTED (expected on the current supported Windows builds)")}");
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }
}
