using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace TabDock.ValidationDriver;

/// <summary>
/// Writes the common scenario result contract used by every interactive tier.
/// Values are role/identity/geometry evidence; arbitrary desktop titles and
/// URLs are never copied into the result artifact.
/// </summary>
internal static class QualificationResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void WriteDeterministic(string suite, IReadOnlyList<AssertionEvidence> assertions)
    {
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        int failed = assertions.Count(a => !a.Passed);
        var result = new
        {
            runId = TestRunProvenance.RunId,
            scenario = $"deterministic-{suite}",
            iteration = 1,
            startedUtc = (DateTimeOffset?)null,
            endedUtc = DateTimeOffset.UtcNow,
            candidateSha = CandidateSha(),
            applicationVersion = ApplicationVersion(),
            environment = EnvironmentFingerprint(),
            result = failed == 0 ? "PASS" : "FAIL",
            failureReason = failed == 0 ? null : $"{failed} deterministic contract assertion(s) failed",
            expectedState = "all selected native-free split and provenance contracts pass",
            observedState = $"passed={assertions.Count - failed} failed={failed} total={assertions.Count}",
            splitRelationshipMembers = Array.Empty<string>(),
            splitPairPresented = (bool?)null,
            activeGuest = (string?)null,
            visibleHwndSet = Array.Empty<object>(),
            foregroundHwnd = "0x0",
            guestRectangles = Array.Empty<object>(),
            paneRectangles = Array.Empty<object>(),
            clientRenderingEvidence = Array.Empty<object>(),
            testIdentities = TestRunProvenance.ScopeSummary(),
            assertions,
            diagnosticLogOffset = 0L,
            traceArtifacts = new[] { "<validation-artifact>/deterministic-selftest.json" },
        };
        string stem = SafeFileName($"deterministic-{suite}");
        File.WriteAllText(Path.Combine(root, $"{stem}.json"), JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        WriteDeterministicJUnit(root, stem, suite, assertions, failed);
        GuardedProc.Log($"RESULT_JSON scenario=deterministic-{suite} status={(failed == 0 ? "PASS" : "FAIL")} artifact=<validation-artifact>/{stem}.json");
    }

    public static void WriteScenario(Ctx ctx)
    {
        ctx.FinishedUtc ??= DateTimeOffset.UtcNow;
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        SplitEvidence split = ReadSplitEvidence(ctx);
        string[] relationshipMembers = ctx.LiveSplitRelationshipMembers ?? split.Members;
        bool pairPresented = ctx.LiveSplitPairPresented ?? split.Presented;
        string? activeGuest = ctx.LiveActiveGuest ?? split.ActiveGuest;

        var result = new
        {
            runId = TestRunProvenance.RunId,
            scenario = ctx.Name,
            iteration = 1,
            startedUtc = ctx.StartedUtc,
            endedUtc = ctx.FinishedUtc,
            candidateSha = CandidateSha(),
            applicationVersion = ApplicationVersion(),
            environment = EnvironmentFingerprint(),
            result = ctx.Status.ToString().ToUpperInvariant(),
            failureReason = ctx.FailureReasons.Count == 0 ? null : string.Join("; ", ctx.FailureReasons),
            expectedState = ctx.ExpectedState,
            observedState = ctx.ObservedState,
            splitRelationshipMembers = relationshipMembers,
            splitPairPresented = pairPresented,
            activeGuest = activeGuest,
            visibleHwndSet = ctx.LiveVisibleHwndSet ?? VisibleGuests(ctx),
            foregroundHwnd = ctx.LiveForegroundHwnd ?? Hwnd(NativeMethods.GetForegroundWindow()),
            guestRectangles = ctx.LiveGuestRectangles ?? GuestGeometry(ctx),
            paneRectangles = ctx.LivePaneRectangles ?? PaneGeometry(ctx, split),
            clientRenderingEvidence = ctx.LiveClientRenderingEvidence ?? GuestGeometry(ctx),
            testIdentities = TestRunProvenance.ScopeSummary(),
            assertions = ctx.Assertions,
            diagnosticLogOffset = ctx.LogOffset,
            traceArtifacts = new[] { $"<validation-artifact>/{Path.GetFileName(TestRunProvenance.ArtifactDirectory)}" },
        };

        string stem = SafeFileName(ctx.Name);
        string jsonPath = Path.Combine(root, $"{stem}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        WriteJUnit(root, stem, ctx);
        GuardedProc.Log($"RESULT_JSON scenario={ctx.Name} status={ctx.Status.ToString().ToUpperInvariant()} artifact=<validation-artifact>/{Path.GetFileName(jsonPath)}");
    }

    public static void CaptureLiveEvidence(Ctx ctx)
    {
        try
        {
            SplitEvidence split = ReadSplitEvidence(ctx);
            ctx.LiveSplitRelationshipMembers = split.Members;
            ctx.LiveSplitPairPresented = split.Presented;
            ctx.LiveActiveGuest = split.ActiveGuest;
            ctx.LiveVisibleHwndSet = VisibleGuests(ctx);
            ctx.LiveForegroundHwnd = Hwnd(NativeMethods.GetForegroundWindow());
            ctx.LiveGuestRectangles = GuestGeometry(ctx);
            ctx.LivePaneRectangles = PaneGeometry(ctx, split);
            ctx.LiveClientRenderingEvidence = GuestGeometry(ctx);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  Result evidence snapshot unavailable before cleanup: {ex.GetType().Name}.");
        }
    }

    private sealed record SplitEvidence(string[] Members, bool Presented, string? ActiveGuest);

    /// <summary>
    /// Reconstructs the final logical split projection from the app's bounded
    /// SPLIT telemetry. This is diagnostic evidence only; pass/fail assertions
    /// remain the live geometry/UIA checks in the scenarios themselves.
    /// </summary>
    private static SplitEvidence ReadSplitEvidence(Ctx ctx)
    {
        string? left = null;
        string? right = null;
        string? active = null;
        bool presented = false;

        foreach (string line in TabDockLog.ReadNewLines(ctx.LogOffset))
        {
            if (line.Contains("SPLIT[replace]", StringComparison.Ordinal))
            {
                left = null;
                right = null;
                active = null;
                presented = false;
                continue;
            }

            Match enter = Regex.Match(line, @"SPLIT\[enter\] left=0x([0-9A-Fa-f]+) right=0x([0-9A-Fa-f]+)");
            if (enter.Success)
            {
                left = Hwnd(ParseHex(enter.Groups[1].Value));
                right = Hwnd(ParseHex(enter.Groups[2].Value));
                active = left;
                presented = true;
                continue;
            }

            Match suspend = Regex.Match(line, @"SPLIT\[(?:suspend|single)\] guest=0x([0-9A-Fa-f]+)");
            if (suspend.Success)
            {
                active = Hwnd(ParseHex(suspend.Groups[1].Value));
                presented = false;
                continue;
            }

            Match resume = Regex.Match(line, @"SPLIT\[resume\].*focused=0x([0-9A-Fa-f]+)");
            if (resume.Success)
            {
                active = Hwnd(ParseHex(resume.Groups[1].Value));
                presented = true;
                continue;
            }

            Match focus = Regex.Match(line, @"SPLIT\[focus\] guest=0x([0-9A-Fa-f]+)");
            if (focus.Success)
            {
                active = Hwnd(ParseHex(focus.Groups[1].Value));
                presented = true;
                continue;
            }

            if (line.Contains("SPLIT[exit]", StringComparison.Ordinal)
                || line.Contains("SPLIT[member-gone]", StringComparison.Ordinal))
            {
                left = null;
                right = null;
                active = null;
                presented = false;
            }
        }

        return new SplitEvidence(
            left != null && right != null ? new[] { left, right } : Array.Empty<string>(),
            left != null && right != null && presented,
            active);
    }

    private static IntPtr ParseHex(string value)
    {
        return long.TryParse(value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out long parsed)
            ? new IntPtr(parsed)
            : IntPtr.Zero;
    }

    private static object[] PaneGeometry(Ctx ctx, SplitEvidence split)
    {
        if (!split.Presented || split.Members.Length != 2)
            return Array.Empty<object>();

        var result = new List<object>();
        string[] sides = { "left", "right" };
        for (int i = 0; i < split.Members.Length; i++)
        {
            IntPtr hwnd = ParseHwnd(split.Members[i]);
            GuestInfo? guest = ctx.Guests.FirstOrDefault(item => item.Hwnd == hwnd);
            if (guest == null || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                continue;
            result.Add(new { side = sides[i], hwnd = Hwnd(hwnd), rect = Rect(rect), role = guest.Role });
        }
        return result.ToArray();
    }

    private static IntPtr ParseHwnd(string value)
    {
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return ParseHex(digits);
    }

    private static string ResultRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_RESULT_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? TestRunProvenance.ArtifactDirectory
            : Path.GetFullPath(configured);
    }

    private static object EnvironmentFingerprint()
        => new
        {
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            screen = new
            {
                width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
                height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN),
            },
        };

    private static object[] VisibleGuests(Ctx ctx)
        => ctx.Guests.Select(g => new
        {
            role = g.Role,
            pid = g.Pid,
            hwnd = Hwnd(g.Hwnd),
            visible = g.Hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(g.Hwnd),
            alive = g.Proc is { HasExited: false },
        }).ToArray();

    private static object[] GuestGeometry(Ctx ctx)
    {
        var evidence = new List<object>();
        foreach (GuestInfo guest in ctx.Guests)
        {
            NativeMethods.RECT outer = default;
            NativeMethods.RECT client = default;
            bool hasOuter = guest.Hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(guest.Hwnd, out outer);
            bool hasClient = guest.Hwnd != IntPtr.Zero && NativeMethods.GetClientRect(guest.Hwnd, out client);
            int resizeEvidence = guest.IsPig ? PigLog.CountLines(guest.Pid, "CLIENT_PRESENT") : 0;
            evidence.Add(new
            {
                role = guest.Role,
                hwnd = Hwnd(guest.Hwnd),
                outer = hasOuter ? Rect(outer) : null,
                client = hasClient ? Rect(client) : null,
                resizeEvidence,
                visible = guest.Hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(guest.Hwnd),
            });
        }
        return evidence.ToArray();
    }

    private static void WriteJUnit(string root, string stem, Ctx ctx)
    {
        string path = Path.Combine(root, $"{stem}.junit.xml");
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(path, settings);
        int failures = ctx.Status == QualificationStatus.Fail ? 1 : 0;
        int skipped = ctx.Status is QualificationStatus.Skip or QualificationStatus.Blocked ? 1 : 0;
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "TabDock.SplitQualification");
        writer.WriteAttributeString("tests", "1");
        writer.WriteAttributeString("failures", failures.ToString());
        writer.WriteAttributeString("skipped", skipped.ToString());
        writer.WriteStartElement("testcase");
        writer.WriteAttributeString("classname", "TabDock.ValidationDriver");
        writer.WriteAttributeString("name", ctx.Name);
        if (ctx.Status == QualificationStatus.Fail)
        {
            writer.WriteStartElement("failure");
            writer.WriteAttributeString("message", string.Join("; ", ctx.FailureReasons));
            writer.WriteEndElement();
        }
        else if (skipped != 0)
        {
            writer.WriteStartElement("skipped");
            writer.WriteAttributeString("message", string.Join("; ", ctx.FailureReasons));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteDeterministicJUnit(
        string root,
        string stem,
        string suite,
        IReadOnlyList<AssertionEvidence> assertions,
        int failed)
    {
        string path = Path.Combine(root, $"{stem}.junit.xml");
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(path, settings);
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "TabDock.SplitQualification.Deterministic");
        writer.WriteAttributeString("tests", assertions.Count.ToString());
        writer.WriteAttributeString("failures", failed.ToString());
        writer.WriteAttributeString("skipped", "0");
        foreach (AssertionEvidence assertion in assertions)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("classname", "TabDock.ValidationDriver.Deterministic");
            writer.WriteAttributeString("name", assertion.Name);
            if (!assertion.Passed)
            {
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message", "deterministic contract assertion failed");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string CandidateSha()
    {
        string? fromCi = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(fromCi))
            return fromCi;
        try
        {
            string? root = FindRepoRoot();
            if (root == null)
                return "unknown";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "rev-parse", "HEAD" },
            });
            if (process == null)
                return "unknown";
            string value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return value.Length == 40 ? value : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ApplicationVersion()
    {
        try
        {
            string? version = FileVersionInfo.GetVersionInfo(Scenarios.TabDockExe).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static object Rect(NativeMethods.RECT rect)
        => new { left = rect.left, top = rect.top, width = rect.Width, height = rect.Height };

    private static string Hwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        return builder.Length == 0 ? "scenario" : builder.ToString();
    }
}
