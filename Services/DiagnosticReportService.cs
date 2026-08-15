using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Builds human-readable and JSON support reports without app startup.</summary>
public static class DiagnosticReportService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static DiagnosticReport CreateReport(bool includeHash)
    {
        var report = new DiagnosticReport
        {
            Build = BuildIdentity.Capture(includeHash),
            Windows = DiagnosticEnvironmentService.CaptureWindows(),
            Monitors = DiagnosticEnvironmentService.CaptureMonitors(),
            DisplayAdapters = DiagnosticEnvironmentService.CaptureDisplayAdapters(),
            Persistence = DiagnosticEnvironmentService.InspectPersistence(),
            Trace = DiagnosticRuntime.Trace.Snapshot().ToList(),
            RecentLog = DiagnosticEnvironmentService.ReadSanitizedRecentLogText(),
        };
        report.Build.ExecutablePath = DiagnosticEnvironmentService.RedactPath(report.Build.ExecutablePath);

        try
        {
            report.LogicalPresentations = DiagnosticRuntime.LogicalSnapshotProvider?.Invoke()?.ToList() ?? new List<LogicalPresentationSnapshot>();
        }
        catch (Exception ex)
        {
            report.Issues.Add("logical snapshot unavailable (" + Classify(ex) + ")");
        }

        try
        {
            var native = new NativeSnapshotService(report.Monitors);
            report.NativeWindows = native.CaptureTabDockWindows(report.LogicalPresentations);
            foreach (LogicalPresentationSnapshot logical in report.LogicalPresentations)
            {
                foreach (DiagnosticMemberSnapshot member in logical.Members)
                {
                    if (member.Hwnd == 0 || report.NativeWindows.Any(window => window.Hwnd == member.Hwnd))
                        continue;
                    report.NativeWindows.Add(native.CaptureWindow(new IntPtr(member.Hwnd), logical, "captured-guest"));
                }
            }
            report.TabDockProcesses = native.CaptureTabDockProcesses(report.NativeWindows);
        }
        catch (Exception ex)
        {
            report.Issues.Add("native snapshot unavailable (" + Classify(ex) + ")");
        }
        return report;
    }

    public static string FormatVersion(BuildIdentityInfo identity)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{identity.ProductName} {identity.SemanticVersion}");
        builder.AppendLine($"commit: {identity.CommitHash}");
        builder.AppendLine($"configuration: {identity.BuildConfiguration}");
        builder.AppendLine($"runtime: {identity.RuntimeIdentifier}");
        builder.AppendLine($"buildTimestampUtc: {identity.BuildTimestampUtc}");
        builder.AppendLine($"informationalVersion: {identity.InformationalVersion}");
        builder.AppendLine($"executable: {identity.ExecutablePath}");
        builder.AppendLine($"fileVersion: {identity.ExecutableFileVersion}");
        builder.AppendLine($"processArchitecture: {identity.ProcessArchitecture}");
        builder.AppendLine($"osArchitecture: {identity.OsArchitecture}");
        builder.AppendLine($"deployment: {identity.DeploymentModel}");
        builder.AppendLine($"sha256: {identity.ExecutableSha256}");
        return DiagnosticEnvironmentService.SanitizeText(builder.ToString().TrimEnd());
    }

    public static string FormatDoctor(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TabDock Doctor");
        builder.AppendLine($"generatedUtc: {report.GeneratedUtc}");
        builder.AppendLine("readOnly: true");
        builder.AppendLine();
        builder.AppendLine("[build]");
        builder.AppendLine($"product: {report.Build.ProductName}");
        builder.AppendLine($"version: {report.Build.SemanticVersion}");
        builder.AppendLine($"commit: {report.Build.CommitHash}");
        builder.AppendLine($"configuration: {report.Build.BuildConfiguration}");
        builder.AppendLine($"runtimeIdentifier: {report.Build.RuntimeIdentifier}");
        builder.AppendLine($"buildTimestampUtc: {report.Build.BuildTimestampUtc}");
        builder.AppendLine($"informationalVersion: {report.Build.InformationalVersion}");
        builder.AppendLine($"executable: {report.Build.ExecutablePath}");
        builder.AppendLine($"fileVersion: {report.Build.ExecutableFileVersion}");
        builder.AppendLine($"sha256: {report.Build.ExecutableSha256}");
        builder.AppendLine($"processArchitecture: {report.Build.ProcessArchitecture}");
        builder.AppendLine($"osArchitecture: {report.Build.OsArchitecture}");
        builder.AppendLine($"runtimeDescription: {report.Build.RuntimeDescription}");
        builder.AppendLine($"deployment: {report.Build.DeploymentModel}");
        builder.AppendLine();
        builder.AppendLine("[windows]");
        builder.AppendLine($"product: {report.Windows.ProductName}");
        builder.AppendLine($"family: {report.Windows.ProductFamily}");
        builder.AppendLine($"rawProductName: {report.Windows.RawProductName}");
        builder.AppendLine($"displayVersion: {report.Windows.DisplayVersion}");
        builder.AppendLine($"build: {report.Windows.Build}");
        builder.AppendLine($"revision: {report.Windows.Revision}");
        builder.AppendLine($"osVersion: {report.Windows.OsVersion}");
        builder.AppendLine($"runtime: {report.Windows.Runtime}");
        builder.AppendLine($"architecture: process={report.Windows.ProcessArchitecture}, os={report.Windows.OsArchitecture}");
        builder.AppendLine($"elevation: {report.Windows.ElevationStatus}");
        builder.AppendLine($"sessionId: {report.Windows.SessionId}");
        builder.AppendLine();
        builder.AppendLine("[monitors]");
        foreach (MonitorSnapshot monitor in report.Monitors)
            builder.AppendLine($"index={monitor.Index} handle={monitor.MonitorHandle} primary={monitor.Primary} bounds={FormatRect(monitor.Bounds)} work={FormatRect(monitor.WorkArea)} dpi={monitor.EffectiveDpiX}x{monitor.EffectiveDpiY} scale={monitor.ScalePercent} orientation={monitor.Orientation} status={monitor.Status}");
        builder.AppendLine();
        builder.AppendLine("[display-adapters]");
        foreach (DisplayAdapterSnapshot adapter in report.DisplayAdapters)
            builder.AppendLine($"index={adapter.Index} name={adapter.Name} description={adapter.Description} deviceId={adapter.DeviceId} driverVersion={adapter.DriverVersion} status={adapter.Status}");
        builder.AppendLine();
        builder.AppendLine("[persistence]");
        builder.AppendLine($"state={report.Persistence.StateStatus} schemaVersion={report.Persistence.SchemaVersion?.ToString() ?? "unavailable"} groupCount={report.Persistence.GroupCount} persistedMemberMetadataCount={report.Persistence.PersistedMemberMetadataCount}");
        builder.AppendLine($"journal={report.Persistence.JournalStatus} journalEntryCount={report.Persistence.JournalEntryCount?.ToString() ?? "unavailable"} logExists={report.Persistence.LogExists}");
        builder.AppendLine($"pendingJournal={report.Persistence.PendingJournalStatus} pendingJournalFileCount={report.Persistence.PendingJournalFileCount}");
        if (report.Persistence.PendingJournalFileCount > 0)
        {
            builder.AppendLine("pendingRecovery=run TabDock.exe --pending-recovery (read-only), then TabDock.exe --recover-pending from a supervised terminal to select one live target");
            builder.AppendLine("pendingRecoveryPolicy=legacy tokenless entries are never mutated automatically and unresolved evidence is retained");
        }
        builder.AppendLine();
        builder.AppendLine("[tabdock-processes]");
        if (report.TabDockProcesses.Count == 0)
            builder.AppendLine("none");
        foreach (TabDockProcessSnapshot process in report.TabDockProcesses)
            builder.AppendLine($"pid={process.ProcessId} exe={process.ExecutableName} path={process.ExecutablePath} startUtc={process.StartTimeUtc} elevation={process.Elevation} session={process.SessionId} mainHwnd={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(process.MainHwnd))} visible={process.MainHwndVisible} iconic={process.MainHwndIconic} status={process.Status}");
        builder.AppendLine();
        builder.AppendLine("[native-windows]");
        if (report.NativeWindows.Count == 0)
            builder.AppendLine("none");
        foreach (NativeWindowSnapshot window in report.NativeWindows)
        {
            builder.AppendLine($"role={window.Role} hwnd={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(window.Hwnd))} pid={window.ProcessId} exe={window.ProcessName} class={window.WindowClass} titleLength={window.TitleLength} titleSha256={window.TitleSha256} rect={FormatRect(window.Rect)} clientRectScreen={FormatRect(window.ClientRectScreen)} visible={window.Visible} iconic={window.Iconic} zoomed={window.Zoomed} foreground={window.Foreground} topmost={window.Topmost} cloaked={window.Cloaked} owner={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(window.OwnerHwnd))} prev={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(window.PreviousZOrderHwnd))} next={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(window.NextZOrderHwnd))} monitor={window.Monitor} dpi={window.EffectiveDpi} elevation={window.Elevation} status={window.Status}");
            foreach (WindowPointProbe probe in window.PointProbes)
                builder.AppendLine($"  probe={probe.Name} point={probe.X},{probe.Y} returned={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(probe.ReturnedHwnd))} pid={probe.ReturnedPid} class={probe.ReturnedClass} process={probe.ReturnedProcess} status={probe.Status}");
        }
        builder.AppendLine();
        builder.AppendLine("[logical-presentations]");
        if (report.LogicalPresentations.Count == 0)
            builder.AppendLine("none (command-line doctor observes native state and persisted metadata; use the in-product hotkey export for live logical state)");
        foreach (LogicalPresentationSnapshot logical in report.LogicalPresentations)
        {
            builder.AppendLine($"groupId={logical.GroupId} container={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(logical.ContainerHwnd))} visible={logical.ContainerVisible} windowState={logical.WindowState} minimized={logical.Minimized} maximized={logical.Maximized} activeMember={logical.ActiveMemberKey ?? "none"} activeGuest={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(logical.ActiveGuestHwnd))} splitActive={logical.SplitActive} splitPresented={logical.SplitPresented} splitLeft={logical.SplitLeftMemberKey ?? "none"}/{DiagnosticEnvironmentService.FormatHwnd(new IntPtr(logical.SplitLeftHwnd))} splitRight={logical.SplitRightMemberKey ?? "none"}/{DiagnosticEnvironmentService.FormatHwnd(new IntPtr(logical.SplitRightHwnd))} splitForeground={logical.SplitForegroundMemberKey ?? "none"}/{DiagnosticEnvironmentService.FormatHwnd(new IntPtr(logical.SplitForegroundHwnd))} chromeInteractionActive={logical.ChromeInteractionActive} monitor={logical.Monitor}");
            foreach (DiagnosticMemberSnapshot member in logical.Members)
                builder.AppendLine($"  member={member.MemberKey} hwnd={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(member.Hwnd))} pid={member.ProcessId} exe={member.ExecutableName} class={member.WindowClass} visible={member.Visible} iconic={member.Iconic} zoomed={member.Zoomed} expectedPane={FormatRect(member.ExpectedPaneRect)}");
        }
        builder.AppendLine();
        builder.AppendLine("[trace]");
        builder.AppendLine($"capacity={DiagnosticRuntime.Trace.Capacity} retained={report.Trace.Count}");
        foreach (DiagnosticEventRecord trace in report.Trace)
            builder.AppendLine($"seq={trace.Sequence} t={trace.TimestampUtc} kind={trace.Kind} group={trace.GroupId ?? "none"} container={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(trace.ContainerHwnd))} guest={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(trace.GuestHwnd))} foreground={DiagnosticEnvironmentService.FormatHwnd(new IntPtr(trace.ForegroundHwnd))} action={trace.Action ?? "none"} result={trace.Result ?? "none"} data={FormatData(trace.Data)}");
        builder.AppendLine();
        builder.AppendLine("[recent-log-sanitized]");
        builder.AppendLine(report.RecentLog);
        if (report.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[issues]");
            foreach (string issue in report.Issues)
                builder.AppendLine(issue);
        }
        return DiagnosticEnvironmentService.SanitizeText(builder.ToString().TrimEnd());
    }

    public static string ToJson(DiagnosticReport report)
        => DiagnosticEnvironmentService.SanitizeJsonText(JsonSerializer.Serialize(report, s_jsonOptions));

    public static string ExportBundle(string? outputPath)
    {
        string path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Environment.CurrentDirectory, $"TabDock-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip")
            : Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // Publish only a completed archive. Writing directly to the final
        // desktop path lets a file watcher observe a non-empty but still-open
        // ZIP and makes consumers race the producer. The temporary name is
        // unique so an earlier export in the same timestamp second cannot be
        // mistaken for this one; the final move happens after both the stream
        // and ZipArchive have closed.
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            DiagnosticReport report = CreateReport(includeHash: true);
            string doctor = FormatDoctor(report);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                AddEntry(archive, "version.txt", FormatVersion(report.Build));
                AddEntry(archive, "doctor.txt", doctor);
                AddEntry(archive, "environment.json", ToJson(report));
                AddEntry(archive, "environment.txt", FormatEnvironment(report));
                AddEntry(archive, "state-summary.json", JsonSerializer.Serialize(report.Persistence, s_jsonOptions));
                AddEntry(archive, "hwnd-snapshot.json", JsonSerializer.Serialize(report.NativeWindows, s_jsonOptions));
                AddEntry(archive, "logical-snapshot.json", JsonSerializer.Serialize(report.LogicalPresentations, s_jsonOptions));
                AddEntry(archive, "trace.jsonl", string.Join(Environment.NewLine, report.Trace.Select(e => JsonSerializer.Serialize(e, s_jsonOptions))) + Environment.NewLine);
                AddEntry(archive, "recent-log.txt", report.RecentLog);
            }

            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The primary export exception, if any, remains authoritative;
                // a locked temporary artifact is safer to leave than to retry
                // destructively from a diagnostics path.
            }
            catch (UnauthorizedAccessException)
            {
                // Same fail-closed cleanup rule for externally held files.
            }
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        string sanitized = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticEnvironmentService.SanitizeJsonText(content)
            : name.Equals("trace.jsonl", StringComparison.OrdinalIgnoreCase)
                ? string.Join(Environment.NewLine, content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(DiagnosticEnvironmentService.SanitizeJsonText))
                : DiagnosticEnvironmentService.SanitizeText(content);
        writer.Write(sanitized);
    }

    private static string FormatRect(DiagnosticRect? rect)
        => rect == null ? "unavailable" : $"{rect.Left},{rect.Top},{rect.Width}x{rect.Height}";

    private static string FormatEnvironment(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        string windowsLabel = report.Windows.ProductName;
        string rawSuffix = string.Equals(report.Windows.ProductName, report.Windows.RawProductName, StringComparison.Ordinal)
            ? string.Empty
            : $" raw={report.Windows.RawProductName}";
        builder.AppendLine($"windows={windowsLabel} family={report.Windows.ProductFamily} {report.Windows.DisplayVersion} build={report.Windows.Build}.{report.Windows.Revision}{rawSuffix}");
        builder.AppendLine($"runtime={report.Windows.Runtime} processArchitecture={report.Windows.ProcessArchitecture} osArchitecture={report.Windows.OsArchitecture}");
        builder.AppendLine($"elevation={report.Windows.ElevationStatus} sessionId={report.Windows.SessionId}");
        foreach (MonitorSnapshot monitor in report.Monitors)
            builder.AppendLine($"monitor index={monitor.Index} handle={monitor.MonitorHandle} primary={monitor.Primary} bounds={FormatRect(monitor.Bounds)} work={FormatRect(monitor.WorkArea)} dpi={monitor.EffectiveDpiX}x{monitor.EffectiveDpiY} scale={monitor.ScalePercent} orientation={monitor.Orientation}");
        foreach (DisplayAdapterSnapshot adapter in report.DisplayAdapters)
            builder.AppendLine($"displayAdapter index={adapter.Index} description={adapter.Description} driverVersion={adapter.DriverVersion}");
        return DiagnosticEnvironmentService.SanitizeText(builder.ToString().TrimEnd());
    }

    private static string FormatData(IReadOnlyDictionary<string, string> data)
        => data.Count == 0 ? "none" : string.Join(",", data.Select(kv => kv.Key + "=" + kv.Value));

    private static string Classify(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => "access-denied",
            IOException => "io-error",
            _ => "probe-failed",
        };
}
