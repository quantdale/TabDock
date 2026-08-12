using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TabDock.Services;

public enum DiagnosticCommandKind
{
    None,
    Version,
    Doctor,
    SupportBundle,
    SelfTest,
    GeometrySelfTest,
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
            "--selftest-geometry" => DiagnosticCommandKind.GeometrySelfTest,
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
                case DiagnosticCommandKind.GeometrySelfTest:
                    (int geometryChecks, int geometryFailures) = SplitGeometry.RunSelfTest(Write);
                    Write($"SELFTEST[geometry] checks={geometryChecks} failures={geometryFailures} result={(geometryFailures == 0 ? "PASS" : "FAIL")}");
                    return geometryFailures == 0 ? 0 : 1;
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
        Check(DiagnosticCommandLine.TryParse(new[] { "--selftest-geometry" }, out DiagnosticCommandRequest geometry, out _)
            && geometry.Kind == DiagnosticCommandKind.GeometrySelfTest);

        Check(typeof(NativeMethods).GetMethod(nameof(NativeMethods.DeferWindowPos))?.ReturnType == typeof(IntPtr));
        var placementMethod = typeof(NativeMethods).GetMethod(nameof(NativeMethods.GetWindowPlacement));
        var placementParameters = placementMethod?.GetParameters();
        Check(placementParameters?.Length == 2
            && placementParameters[1].ParameterType == typeof(NativeMethods.WINDOWPLACEMENT).MakeByRefType());
        Check(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() == 60
            && typeof(NativeMethods.WINDOWPLACEMENT).GetField(nameof(NativeMethods.WINDOWPLACEMENT.rcDevice)) != null);
        Check(typeof(NativeMethods).GetMethod(nameof(NativeMethods.ShowWindow))?.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute), inherit: false)
            is System.Runtime.InteropServices.DllImportAttribute[] { Length: 1 } showWindowImport
            && !showWindowImport[0].SetLastError);
        Check(WindowShepherdService.MatchesStableCaptureIdentity(
            expectedPid: 42,
            currentPid: 42,
            expectedExePath: @"C:\Program Files\Guest\guest.exe",
            currentExePath: @"c:\program files\guest\guest.exe",
            expectedClassName: "GuestClass",
            currentClassName: "GuestClass"));
        const string initialTitle = "Document - before";
        const string finalTitle = "Document - after";
        Check(!string.Equals(initialTitle, finalTitle, StringComparison.Ordinal)
            && WindowShepherdService.MatchesStableCaptureIdentity(
                expectedPid: 42,
                currentPid: 42,
                expectedExePath: @"C:\Program Files\Guest\guest.exe",
                currentExePath: @"c:\program files\guest\guest.exe",
                expectedClassName: "GuestClass",
                currentClassName: "GuestClass"));

        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Home", "26200") == "Windows 11 Home");
        Check(DiagnosticEnvironmentService.NormalizeWindowsProductName("Windows 10 Pro", "19045") == "Windows 10 Pro");
        Check(DiagnosticEnvironmentService.GetWindowsProductFamily("26200", "Windows 10 Home") == "Windows 11");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            string embeddedAppData = $"[2026-08-12 22:00:00.000] STARTUP[cleanup] removed stale temp file: {appData.ToUpperInvariant()}\\TabDock\\state.json.tmp";
            string redacted = DiagnosticEnvironmentService.RedactPath(embeddedAppData);
            Check(redacted.Contains("%APPDATA%", StringComparison.Ordinal)
                && !redacted.Contains(appData, StringComparison.OrdinalIgnoreCase)
                && !redacted.Contains("%USERPROFILE%\\AppData", StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            string redactedProfile = DiagnosticEnvironmentService.RedactPath($"path={userProfile.ToUpperInvariant()}\\Documents\\file.txt");
            Check(redactedProfile.Contains("%USERPROFILE%", StringComparison.Ordinal)
                && !redactedProfile.Contains(userProfile, StringComparison.OrdinalIgnoreCase));
        }
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string redactedLocal = DiagnosticEnvironmentService.RedactPath($"local={localAppData.ToUpperInvariant()}\\TabDock\\cache.bin");
            Check(redactedLocal.Contains("%LOCALAPPDATA%", StringComparison.Ordinal)
                && !redactedLocal.Contains(localAppData, StringComparison.OrdinalIgnoreCase));
        }

        Check(SingleInstanceGuard.BuildMutexName("S-1-5-21-100-200-300-400") != SingleInstanceGuard.BuildMutexName("S-1-5-21-100-200-300-401")
            && SingleInstanceGuard.BuildMutexName("S-1-5-21-100-200-300-400").StartsWith(@"Global\TabDock-", StringComparison.Ordinal));

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
        var concurrent = new DiagnosticTrace(128);
        Parallel.For(0, 512, i => concurrent.Record("concurrent"));
        Check(concurrent.Snapshot().Count == 128);
        Check(concurrent.Snapshot().Zip(concurrent.Snapshot().Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        return (checks, failures);
    }
}
