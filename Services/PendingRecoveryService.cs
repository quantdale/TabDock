using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Read-only catalog and explicitly supervised recovery transaction for the
/// tokenless legacy pending journals. Startup never calls this service.
/// </summary>
internal static class PendingRecoveryService
{
    internal const string TemporaryRecoveryPropertyName = "TabDock.PendingRecoveryToken";
    private const string PendingFilePrefix = "hidden-windows.json.pending";
    private static long _nextRecoveryToken;

    internal static PendingRecoveryCatalog Discover(
        string? directory = null,
        IPendingRecoveryNativeApi? api = null)
    {
        string root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabDock");
        var catalog = new PendingRecoveryCatalog(root);
        string[] paths;
        try
        {
            if (File.Exists(root) && !Directory.Exists(root))
            {
                catalog.Error = "unreadable (not-a-directory)";
                return catalog;
            }
            if (!Directory.Exists(root))
                return catalog;
            paths = GetPendingPaths(root).ToArray();
        }
        catch (UnauthorizedAccessException ex)
        {
            catalog.Error = "unreadable (access-denied)";
            catalog.ErrorDetail = ex.GetType().Name;
            return catalog;
        }
        catch (IOException ex)
        {
            catalog.Error = "unreadable (io-error)";
            catalog.ErrorDetail = ex.GetType().Name;
            return catalog;
        }

        IRecoveryStatusProbe probe = api == null
            ? NativeRecoveryStatusProbe.Instance
            : new NativeRecoveryStatusProbeAdapter(api);
        for (int fileIndex = 0; fileIndex < paths.Length; fileIndex++)
        {
            string path = paths[fileIndex];
            PendingRecoveryFile file = ReadFile(path, fileIndex + 1, probe);
            catalog.Files.Add(file);
        }
        return catalog;
    }

    internal static int CountActivePendingFiles(string directory)
    {
        PendingRecoveryCatalog catalog = Discover(directory);
        return catalog.Files.Count(file => file.HasUnresolvedEvidence);
    }

    internal static string FormatDiscovery(string? directory = null)
    {
        PendingRecoveryCatalog catalog = Discover(directory);
        var builder = new StringBuilder();
        builder.AppendLine("TabDock Pending Recovery");
        builder.AppendLine("readOnly: true");
        builder.AppendLine($"directory: %APPDATA%\\TabDock");
        builder.AppendLine($"pendingFileCount: {catalog.Files.Count(file => file.HasUnresolvedEvidence)}");
        builder.AppendLine($"pendingEntryCount: {catalog.Files.Sum(file => file.Entries.Count(entry => !entry.AlreadyResolved))}");
        if (catalog.Error != null)
        {
            builder.AppendLine($"status: {catalog.Error}");
            return builder.ToString().TrimEnd();
        }
        if (catalog.Files.Count == 0)
        {
            builder.AppendLine("status: absent");
            builder.AppendLine("recoveryCommand: none");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("status: entries-listed-read-only");
        foreach (PendingRecoveryFile file in catalog.Files)
        {
            builder.AppendLine($"file={file.FileName} schema=v{file.Version} entryCount={file.Entries.Count} fileStatus={file.Status}");
            foreach (PendingRecoveryEntry entry in file.Entries)
            {
                builder.AppendLine($"  id={entry.SessionId} schema=v{entry.Version} status={entry.Status} fields={entry.AvailableFields} recordedHwnd={FormatHwnd(entry.Entry.Hwnd)}");
            }
        }
        builder.AppendLine("next: run TabDock.exe --recover-pending from a supervised terminal to select and confirm one live target");
        return builder.ToString().TrimEnd();
    }

    internal static int RunInteractive(
        TextReader? input = null,
        TextWriter? output = null,
        string? directory = null,
        IPendingRecoveryNativeApi? api = null,
        IReadOnlyList<PendingRecoveryCandidate>? candidates = null)
    {
        input ??= Console.In;
        output ??= Console.Out;
        PendingRecoveryCatalog catalog = Discover(directory, api);
        output.WriteLine("TabDock supervised pending recovery");
        output.WriteLine("This command is user-initiated. Startup never performs tokenless legacy recovery.");
        if (catalog.Error != null)
        {
            output.WriteLine($"Pending evidence is {catalog.Error}; no mutation was attempted.");
            return 2;
        }

        List<PendingRecoveryEntry> entries = catalog.Files
            .SelectMany(file => file.Entries)
            .Where(entry => !entry.AlreadyResolved)
            .ToList();
        if (entries.Count == 0)
        {
            PendingRecoveryEntry[] resolvedEntries = catalog.Files
                .SelectMany(file => file.Entries)
                .Where(entry => entry.AlreadyResolved)
                .ToArray();
            bool retirementFailed = false;
            foreach (PendingRecoveryEntry resolvedEntry in resolvedEntries)
            {
                if (RetireEntry(resolvedEntry, out string retirementError))
                    continue;

                retirementFailed = true;
                output.WriteLine($"Resolved entry {resolvedEntry.SessionId} still needs disk-only retirement: {retirementError}. Evidence was retained.");
            }
            if (retirementFailed)
                return 2;
            if (resolvedEntries.Length > 0)
            {
                output.WriteLine("Resolved pending entries were retired; no unresolved pending recovery entries were found.");
                return 0;
            }
            output.WriteLine("No unresolved pending recovery entries were found.");
            return 0;
        }

        output.WriteLine("Pending entries (read-only):");
        foreach (PendingRecoveryEntry entry in entries)
        {
            output.WriteLine($"  {entry.SessionId}: schema=v{entry.Version}, status={entry.Status}, fields={entry.AvailableFields}, file={entry.FileName}");
        }

        PendingRecoveryEntry? selectedEntry = SelectEntry(entries, input, output);
        if (selectedEntry == null)
            return 1;
        if (selectedEntry.Status is not ("potentially-recoverable" or "unverifiable"))
        {
            output.WriteLine($"Entry {selectedEntry.SessionId} is not eligible for a live recovery transaction ({selectedEntry.Status}). Evidence was retained.");
            return 2;
        }

        IReadOnlyList<PendingRecoveryCandidate> liveCandidates = candidates ?? EnumerateCandidates();
        output.WriteLine($"Live top-level candidates: {liveCandidates.Count}");
        foreach (PendingRecoveryCandidate candidate in liveCandidates)
        {
            string title = SanitizeConsoleTitle(candidate.Title);
            output.WriteLine($"  {candidate.CandidateId}: hwnd={FormatHwnd(candidate.Hwnd)} pid={candidate.ProcessId} exe={Path.GetFileName(candidate.ExePath)} class={candidate.ClassName} visible={candidate.Visible} title=\"{title}\"");
        }

        List<PendingRecoveryCandidate> matching = liveCandidates
            .Where(candidate => MatchesHistoricalEvidence(selectedEntry, candidate))
            .ToList();
        if (matching.Count == 0)
        {
            output.WriteLine("No live candidate matches all historical fields available in the selected entry. No mutation was attempted.");
            return 2;
        }
        if (matching.Count > 1)
            output.WriteLine("More than one candidate matches the historical fields; an explicit candidate selection is required.");

        PendingRecoveryCandidate? selectedCandidate = SelectCandidate(liveCandidates, input, output);
        if (selectedCandidate == null)
            return 1;
        if (!MatchesHistoricalEvidence(selectedEntry, selectedCandidate))
        {
            output.WriteLine("The selected candidate does not match every available historical identity field. No mutation was attempted.");
            return 2;
        }

        output.WriteLine($"Selected {selectedEntry.SessionId} -> {selectedCandidate.CandidateId} ({Path.GetFileName(selectedCandidate.ExePath)}, PID {selectedCandidate.ProcessId}, title=\"{SanitizeConsoleTitle(selectedCandidate.Title)}\").");
        output.Write("Type YES to confirm this exact live target, or anything else to cancel: ");
        string? confirmation = input.ReadLine();
        if (!string.Equals(confirmation, "YES", StringComparison.Ordinal))
        {
            output.WriteLine("Recovery cancelled; no native mutation or evidence change occurred.");
            return 1;
        }

        IPendingRecoveryNativeApi native = api ?? NativePendingRecoveryNativeApi.Instance;
        if (!ExecuteRecovery(selectedEntry, selectedCandidate, native, out string result))
        {
            output.WriteLine("Recovery failed: " + result);
            output.WriteLine("The pending evidence was retained.");
            return 2;
        }

        if (!MarkResolved(selectedEntry, out string markerError))
        {
            output.WriteLine("The guest was recovered, but the durable resolution marker could not be written: " + markerError);
            output.WriteLine("The pending evidence remains and must be cleaned up by a later supervised invocation; native recovery will not be repeated when the marker is available.");
            return 2;
        }
        if (!RetireEntry(selectedEntry, out string retireError))
        {
            output.WriteLine("The guest was recovered and marked resolved, but pending-entry retirement needs a later retry: " + retireError);
            return 2;
        }

        output.WriteLine($"Recovered and retired entry {selectedEntry.SessionId}. Unresolved sibling entries, if any, remain pending.");
        return 0;
    }

    internal static bool MatchesHistoricalEvidence(PendingRecoveryEntry entry, PendingRecoveryCandidate candidate)
    {
        PendingRecoveryFields fields = entry.Fields;
        if (fields.HasHwnd && entry.Entry.Hwnd != candidate.Hwnd.ToInt64())
            return false;
        if (fields.HasPid && entry.Entry.Pid != candidate.ProcessId)
            return false;
        if (fields.HasExe
            && !string.Equals(entry.Entry.ExePath, candidate.ExePath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (fields.HasThread && entry.Entry.WindowThreadId != candidate.WindowThreadId)
            return false;
        if (fields.HasClass
            && !string.Equals(entry.Entry.ClassName, candidate.ClassName, StringComparison.Ordinal))
            return false;
        if (fields.HasProcessStart
            && entry.Entry.ProcessStartTimeUtcTicks != candidate.ProcessStartTimeUtcTicks)
            return false;
        return true;
    }

    internal static bool ExecuteRecovery(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        error = string.Empty;
        if (!MatchesHistoricalEvidence(entry, candidate))
        {
            error = "historical identity does not match the selected candidate";
            return false;
        }

        IntPtr hwnd = candidate.Hwnd;
        if (!api.IsWindow(hwnd))
        {
            error = "the selected HWND no longer exists";
            return false;
        }
        if (api.GetProperty(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName) != IntPtr.Zero)
        {
            error = "the selected HWND already carries a live TabDock capture token";
            return false;
        }
        if (api.GetProperty(hwnd, TemporaryRecoveryPropertyName) != IntPtr.Zero)
        {
            error = "the selected HWND already carries a pending-recovery token";
            return false;
        }

        WindowIdentityResult selectedIdentity = EvaluateRecoveryTarget(entry, candidate, api, out string selectedReason);
        if (selectedIdentity != WindowIdentityResult.Match)
        {
            error = selectedReason;
            return false;
        }

        long tokenNumber = Interlocked.Increment(ref _nextRecoveryToken);
        if (tokenNumber == 0)
        {
            error = "could not allocate a temporary recovery token";
            return false;
        }
        IntPtr token = new(tokenNumber);
        if (!api.SetProperty(hwnd, TemporaryRecoveryPropertyName, token)
            || api.GetProperty(hwnd, TemporaryRecoveryPropertyName) != token)
        {
            if (api.GetProperty(hwnd, TemporaryRecoveryPropertyName) == token)
                api.RemoveProperty(hwnd, TemporaryRecoveryPropertyName, token);
            error = "temporary recovery token installation could not be verified";
            return false;
        }

        bool success = false;
        try
        {
            if (!entry.IsV1)
            {
                if (entry.Entry.HasOriginalPlacement)
                {
                    if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                        return false;
                    NativeMethods.WINDOWPLACEMENT placement = new()
                    {
                        length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
                        flags = entry.Entry.OriginalPlacementFlags,
                        showCmd = unchecked((uint)entry.Entry.OriginalShowCommand),
                        ptMinPosition = new NativeMethods.POINT { x = entry.Entry.OriginalMinPositionX, y = entry.Entry.OriginalMinPositionY },
                        ptMaxPosition = new NativeMethods.POINT { x = entry.Entry.OriginalMaxPositionX, y = entry.Entry.OriginalMaxPositionY },
                        rcNormalPosition = new NativeMethods.RECT
                        {
                            left = entry.Entry.OriginalNormalLeft,
                            top = entry.Entry.OriginalNormalTop,
                            right = entry.Entry.OriginalNormalRight,
                            bottom = entry.Entry.OriginalNormalBottom,
                        },
                    };
                    if (!api.SetWindowPlacement(hwnd, ref placement))
                    {
                        error = "placement restoration failed";
                        return false;
                    }
                }
                else if (entry.Entry.OriginalNormalRight > entry.Entry.OriginalNormalLeft
                    && entry.Entry.OriginalNormalBottom > entry.Entry.OriginalNormalTop)
                {
                    if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                        return false;
                    if (!api.SetWindowPos(
                            hwnd,
                            IntPtr.Zero,
                            entry.Entry.OriginalNormalLeft,
                            entry.Entry.OriginalNormalTop,
                            entry.Entry.OriginalNormalRight - entry.Entry.OriginalNormalLeft,
                            entry.Entry.OriginalNormalBottom - entry.Entry.OriginalNormalTop,
                            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
                    {
                        error = "bounds restoration failed";
                        return false;
                    }
                }
            }

            if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                return false;
            int showCommand = entry.IsV1
                ? NativeMethods.SW_SHOW
                : entry.Entry.OriginallyVisible
                    ? (entry.Entry.OriginalShowCommand == 0 ? NativeMethods.SW_SHOW : entry.Entry.OriginalShowCommand)
                    : NativeMethods.SW_HIDE;
            api.ShowWindow(hwnd, showCommand);
            if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                return false;
            if (api.IsWindowVisible(hwnd) != (entry.IsV1 || entry.Entry.OriginallyVisible))
            {
                error = "visibility post-state did not match the historical contract";
                return false;
            }

            if (!entry.IsV1 && entry.Entry.HasOriginalTransitionsState)
            {
                if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                    return false;
                int transitionValue = entry.Entry.OriginalTransitionsDisabled ? 1 : 0;
                if (api.SetTransitionsDisabled(hwnd, transitionValue) != 0)
                {
                    error = "DWM transition restoration failed";
                    return false;
                }
            }

            if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                return false;
            if (!api.RemoveProperty(hwnd, TemporaryRecoveryPropertyName, token))
            {
                error = "temporary recovery token removal failed";
                return false;
            }
            success = true;
            return true;
        }
        finally
        {
            if (!success)
            {
                // Cleanup is itself generation-scoped. If the target changed,
                // leave the exact property untouched rather than mutating a
                // replacement HWND; the property dies with that HWND.
                if (TryGenerationBoundary(entry, candidate, token, api, out _))
                    api.RemoveProperty(hwnd, TemporaryRecoveryPropertyName, token);
            }
        }
    }

    private static WindowIdentityResult EvaluateRecoveryTarget(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IPendingRecoveryNativeApi api,
        out string reason)
    {
        try
        {
            if (!api.IsWindow(candidate.Hwnd))
                return Result(WindowIdentityResult.Mismatch, "the selected HWND no longer exists", out reason);
            uint pid = api.GetProcessId(candidate.Hwnd);
            if (pid == 0)
                return Result(WindowIdentityResult.Unverifiable, "live PID could not be read", out reason);
            if (pid != candidate.ProcessId)
                return Result(WindowIdentityResult.Mismatch, "PID changed after candidate selection", out reason);
            uint thread = api.GetWindowThreadId(candidate.Hwnd);
            if (thread == 0)
                return Result(WindowIdentityResult.Unverifiable, "GUI thread could not be read", out reason);
            if (thread != candidate.WindowThreadId)
                return Result(WindowIdentityResult.Mismatch, "GUI thread changed after candidate selection", out reason);
            string? exe = api.GetProcessImagePath(pid);
            if (string.IsNullOrWhiteSpace(exe))
                return Result(WindowIdentityResult.Unverifiable, "executable identity could not be read", out reason);
            if (!string.Equals(exe, candidate.ExePath, StringComparison.OrdinalIgnoreCase))
                return Result(WindowIdentityResult.Mismatch, "executable changed after candidate selection", out reason);
            string? className = api.GetClassName(candidate.Hwnd);
            if (string.IsNullOrWhiteSpace(className))
                return Result(WindowIdentityResult.Unverifiable, "window class could not be read", out reason);
            if (!string.Equals(className, candidate.ClassName, StringComparison.Ordinal))
                return Result(WindowIdentityResult.Mismatch, "window class changed after candidate selection", out reason);
            if (entry.Fields.HasProcessStart)
            {
                long start = api.GetProcessStartTimeUtcTicks(pid);
                if (start == 0)
                    return Result(WindowIdentityResult.Unverifiable, "process-start identity could not be read", out reason);
                if (start != entry.Entry.ProcessStartTimeUtcTicks)
                    return Result(WindowIdentityResult.Mismatch, "process-start identity differs", out reason);
            }
            reason = "selected target identity matched";
            return WindowIdentityResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"target identity probe threw {ex.GetType().Name}";
            return WindowIdentityResult.Unverifiable;
        }
    }

    private static bool TryGenerationBoundary(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IntPtr token,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        WindowIdentityResult result = EvaluateRecoveryGeneration(entry, candidate, token, api, out error);
        return result == WindowIdentityResult.Match;
    }

    private static WindowIdentityResult EvaluateRecoveryGeneration(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IntPtr token,
        IPendingRecoveryNativeApi api,
        out string reason)
    {
        try
        {
            if (!api.IsWindow(candidate.Hwnd))
                return Result(WindowIdentityResult.Mismatch, "HWND no longer exists", out reason);
            if (api.GetProperty(candidate.Hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName) != IntPtr.Zero)
                return Result(WindowIdentityResult.Mismatch, "normal capture token appeared", out reason);
            if (api.GetProperty(candidate.Hwnd, TemporaryRecoveryPropertyName) != token)
                return Result(WindowIdentityResult.Mismatch, "temporary recovery token differs", out reason);
            uint pid = api.GetProcessId(candidate.Hwnd);
            if (pid == 0)
                return Result(WindowIdentityResult.Unverifiable, "PID could not be read", out reason);
            if (pid != candidate.ProcessId)
                return Result(WindowIdentityResult.Mismatch, "PID differs", out reason);
            uint thread = api.GetWindowThreadId(candidate.Hwnd);
            if (thread == 0)
                return Result(WindowIdentityResult.Unverifiable, "GUI thread could not be read", out reason);
            if (thread != candidate.WindowThreadId)
                return Result(WindowIdentityResult.Mismatch, "GUI thread differs", out reason);
            string? exe = api.GetProcessImagePath(pid);
            if (string.IsNullOrWhiteSpace(exe))
                return Result(WindowIdentityResult.Unverifiable, "executable identity could not be read", out reason);
            if (!string.Equals(exe, candidate.ExePath, StringComparison.OrdinalIgnoreCase))
                return Result(WindowIdentityResult.Mismatch, "executable identity differs", out reason);
            string? className = api.GetClassName(candidate.Hwnd);
            if (string.IsNullOrWhiteSpace(className))
                return Result(WindowIdentityResult.Unverifiable, "window class identity could not be read", out reason);
            if (!string.Equals(className, candidate.ClassName, StringComparison.Ordinal))
                return Result(WindowIdentityResult.Mismatch, "window class differs", out reason);
            if (entry.Fields.HasProcessStart)
            {
                long start = api.GetProcessStartTimeUtcTicks(pid);
                if (start == 0)
                    return Result(WindowIdentityResult.Unverifiable, "process-start identity could not be read", out reason);
                if (start != entry.Entry.ProcessStartTimeUtcTicks)
                    return Result(WindowIdentityResult.Mismatch, "process-start identity differs", out reason);
            }
            reason = "temporary recovery generation matched";
            return WindowIdentityResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"generation probe threw {ex.GetType().Name}";
            return WindowIdentityResult.Unverifiable;
        }
    }

    private static WindowIdentityResult Result(WindowIdentityResult result, string message, out string reason)
    {
        reason = message;
        return result;
    }

    private static PendingRecoveryEntry? SelectEntry(
        IReadOnlyList<PendingRecoveryEntry> entries,
        TextReader input,
        TextWriter output)
    {
        output.Write("Select pending entry ID (or q): ");
        string? value = input.ReadLine();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("q", StringComparison.OrdinalIgnoreCase))
            return null;
        return entries.FirstOrDefault(entry => entry.SessionId.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static PendingRecoveryCandidate? SelectCandidate(
        IReadOnlyList<PendingRecoveryCandidate> candidates,
        TextReader input,
        TextWriter output)
    {
        output.Write("Select live candidate ID (or q): ");
        string? value = input.ReadLine();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("q", StringComparison.OrdinalIgnoreCase))
            return null;
        return candidates.FirstOrDefault(candidate => candidate.CandidateId.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static List<PendingRecoveryCandidate> EnumerateCandidates()
    {
        var candidates = new List<PendingRecoveryCandidate>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindow(hwnd))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == NativeMethods.GetCurrentProcessId())
                return true;
            uint thread = NativeMethods.GetWindowThreadProcessId(hwnd, out uint ignoredPid);
            string? exe = NativeMethods.GetProcessImagePath(pid);
            string? className = NativeMethods.GetClassNameString(hwnd);
            if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(className))
                return true;
            candidates.Add(new PendingRecoveryCandidate
            {
                CandidateId = $"C{candidates.Count + 1:D3}",
                Hwnd = hwnd,
                ProcessId = pid,
                WindowThreadId = thread,
                ExePath = exe,
                ClassName = className,
                ProcessStartTimeUtcTicks = NativeMethods.GetProcessStartTimeUtcTicks(pid),
                Title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
                Visible = NativeMethods.IsWindowVisible(hwnd),
                Iconic = NativeMethods.IsIconic(hwnd),
            });
            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    private static PendingRecoveryFile ReadFile(
        string path,
        int fileIndex,
        IRecoveryStatusProbe probe)
    {
        string fileName = Path.GetFileName(path);
        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "unreadable (access-denied)", ex.GetType().Name);
        }
        catch (IOException ex)
        {
            return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "unreadable (io-error)", ex.GetType().Name);
        }

        try
        {
            string json = new UTF8Encoding(false, true).GetString(rawBytes);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetProperty(document.RootElement, "Entries", out JsonElement entriesElement)
                || entriesElement.ValueKind != JsonValueKind.Array)
            {
                return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "malformed (entries-missing)", null);
            }

            int version = TryGetInt(document.RootElement, "Version") ?? HiddenWindowJournalFile.LegacyMinimalVersion;
            HiddenWindowJournalFile dto = JsonSerializer.Deserialize(
                json,
                TabDockJsonContext.Default.HiddenWindowJournalFile)
                ?? new HiddenWindowJournalFile();
            dto.Entries ??= new List<HiddenWindowEntry>();
            var file = new PendingRecoveryFile
            {
                FileName = fileName,
                FullPath = path,
                FileIndex = fileIndex,
                Version = version,
                Status = version > HiddenWindowJournalFile.CurrentVersion ? "future-schema" : "pending",
                SourceFileSha256 = Sha256(rawBytes),
            };
            List<PendingResolution> resolutions = ReadResolutions(path);
            int entryIndex = 0;
            foreach (JsonElement rawEntry in entriesElement.EnumerateArray())
            {
                HiddenWindowEntry entry = entryIndex < dto.Entries.Count
                    ? dto.Entries[entryIndex]
                    : new HiddenWindowEntry();
                if (version == HiddenWindowJournalFile.LegacyMinimalVersion)
                {
                    // Historical v1 only recorded guests that TabDock hid;
                    // visibility is the one recovery fact its contract proves.
                    entry.OriginallyVisible = true;
                    entry.OriginalShowCommand = NativeMethods.SW_SHOW;
                }
                string fingerprint = Fingerprint(rawEntry);
                PendingRecoveryFields fields = PendingRecoveryFields.FromJson(rawEntry, version);
                PendingResolution? resolved = resolutions.FirstOrDefault(item => item.EntryFingerprint == fingerprint);
                var pendingEntry = new PendingRecoveryEntry
                {
                    SessionId = $"P{fileIndex:D2}-E{entryIndex + 1:D3}",
                    FileName = fileName,
                    FullPath = path,
                    EntryIndex = entryIndex,
                    Version = version,
                    Entry = entry,
                    Fields = fields,
                    EntryFingerprint = fingerprint,
                    SourceFileSha256 = Sha256(rawBytes),
                    AlreadyResolved = resolved != null,
                    Status = resolved != null
                        ? "already-resolved"
                        : version > HiddenWindowJournalFile.CurrentVersion
                            ? "future-schema"
                            : probe.Classify(entry, fields),
                };
                file.Entries.Add(pendingEntry);
                entryIndex++;
            }
            if (file.Entries.Count == 0)
                file.Status = "empty-resolved";
            return file;
        }
        catch (JsonException ex)
        {
            return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "malformed (json)", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "unverifiable", ex.GetType().Name);
        }
    }

    private static IEnumerable<string> GetPendingPaths(string directory)
        => Directory.GetFiles(directory, PendingFilePrefix + "*")
            .Where(path =>
            {
                string name = Path.GetFileName(path);
                return name.StartsWith(PendingFilePrefix, StringComparison.Ordinal)
                    && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".recovered", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static int? TryGetInt(JsonElement element, string name)
        => TryGetProperty(element, name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static string FormatHwnd(long hwnd)
        => hwnd == 0 ? "0x0" : $"0x{hwnd:X}";

    private static string SanitizeConsoleTitle(string title)
    {
        string singleLine = title.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 96 ? singleLine : singleLine[..96] + "…";
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static List<PendingResolution> ReadResolutions(string pendingPath)
    {
        string path = pendingPath + ".recovered";
        if (!File.Exists(path))
            return new List<PendingResolution>();
        try
        {
            ResolutionLedger ledger = JsonSerializer.Deserialize<ResolutionLedger>(File.ReadAllText(path))
                ?? new ResolutionLedger();
            return ledger.Resolutions ?? new List<PendingResolution>();
        }
        catch
        {
            return new List<PendingResolution>();
        }
    }

    private static bool MarkResolved(PendingRecoveryEntry entry, out string error)
    {
        error = string.Empty;
        string path = entry.FullPath + ".recovered";
        try
        {
            ResolutionLedger ledger = new()
            {
                Resolutions = ReadResolutions(entry.FullPath),
            };
            if (!ledger.Resolutions.Any(item => item.EntryFingerprint == entry.EntryFingerprint))
            {
                ledger.Resolutions.Add(new PendingResolution
                {
                    EntryFingerprint = entry.EntryFingerprint,
                    SchemaVersion = entry.Version,
                    ResolvedUtc = DateTimeOffset.UtcNow,
                    Result = "presentation-restored",
                });
            }
            WriteDurableJson(path, ledger);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static bool RetireEntry(PendingRecoveryEntry entry, out string error)
    {
        error = string.Empty;
        try
        {
            byte[] bytes = File.ReadAllBytes(entry.FullPath);
            if (!string.Equals(Sha256(bytes), entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "pending file changed after discovery";
                return false;
            }
            JsonNode? root = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes));
            if (root is not JsonObject rootObject
                || rootObject["Entries"] is not JsonArray entries)
            {
                error = "pending JSON entries could not be reopened";
                return false;
            }
            int removeIndex = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is JsonNode node
                    && string.Equals(
                        Sha256(Encoding.UTF8.GetBytes(node.ToJsonString())),
                        entry.EntryFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    removeIndex = i;
                    break;
                }
            }
            if (removeIndex < 0)
            {
                // A previous cleanup attempt may already have retired the
                // entry after the resolution marker was committed.
                return true;
            }
            entries.RemoveAt(removeIndex);
            WriteDurableJson(entry.FullPath, rootObject);
            if (entries.Count == 0)
                File.Delete(entry.FullPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static void WriteDurableJson(string path, object value)
    {
        string json = value is JsonObject node
            ? node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        string tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private static string Fingerprint(JsonElement element)
    {
        JsonNode? node = JsonNode.Parse(element.GetRawText());
        string canonical = node?.ToJsonString() ?? element.GetRawText();
        return Sha256(Encoding.UTF8.GetBytes(canonical));
    }

    private sealed class ResolutionLedger
    {
        public List<PendingResolution> Resolutions { get; set; } = new();
    }

    private sealed class PendingResolution
    {
        public string EntryFingerprint { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public DateTimeOffset ResolvedUtc { get; set; }
        public string Result { get; set; } = string.Empty;
    }
}

internal sealed class PendingRecoveryCatalog
{
    public PendingRecoveryCatalog(string directory) => Directory = directory;
    public string Directory { get; }
    public List<PendingRecoveryFile> Files { get; } = new();
    public string? Error { get; set; }
    public string? ErrorDetail { get; set; }
}

internal sealed class PendingRecoveryFile
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public int FileIndex { get; init; }
    public int Version { get; init; }
    public string Status { get; set; } = string.Empty;
    public string SourceFileSha256 { get; init; } = string.Empty;
    public List<PendingRecoveryEntry> Entries { get; } = new();
    public bool HasUnresolvedEvidence
        => Entries.Any(entry => !entry.AlreadyResolved)
            || (Entries.Count == 0 && !string.Equals(Status, "empty-resolved", StringComparison.Ordinal));

    public static PendingRecoveryFile Unreadable(string fileName, string fullPath, int index, string status, string? detail)
        => new()
        {
            FileName = fileName,
            FullPath = fullPath,
            FileIndex = index,
            Version = 0,
            Status = detail == null ? status : $"{status}:{detail}",
        };
}

internal sealed class PendingRecoveryEntry
{
    public string SessionId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public int EntryIndex { get; init; }
    public int Version { get; init; }
    public HiddenWindowEntry Entry { get; init; } = new();
    public PendingRecoveryFields Fields { get; init; }
    public string EntryFingerprint { get; init; } = string.Empty;
    public string SourceFileSha256 { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool AlreadyResolved { get; init; }
    public bool IsV1 => Version == HiddenWindowJournalFile.LegacyMinimalVersion;
    public string AvailableFields => Fields.ToDisplayString();
}

internal readonly struct PendingRecoveryFields
{
    public bool HasHwnd { get; init; }
    public bool HasPid { get; init; }
    public bool HasExe { get; init; }
    public bool HasThread { get; init; }
    public bool HasClass { get; init; }
    public bool HasProcessStart { get; init; }

    public static PendingRecoveryFields FromJson(JsonElement element, int version)
        => new()
        {
            HasHwnd = Has(element, "Hwnd"),
            HasPid = Has(element, "Pid"),
            HasExe = Has(element, "ExePath"),
            HasThread = Has(element, "WindowThreadId") && version >= HiddenWindowJournalFile.CurrentVersion,
            HasClass = Has(element, "ClassName") && version >= HiddenWindowJournalFile.PresentationIdentityVersion,
            HasProcessStart = Has(element, "ProcessStartTimeUtcTicks") && version >= HiddenWindowJournalFile.PresentationIdentityVersion,
        };

    public string ToDisplayString()
    {
        var fields = new List<string>();
        if (HasHwnd) fields.Add("hwnd");
        if (HasPid) fields.Add("pid");
        if (HasExe) fields.Add("exe");
        if (HasThread) fields.Add("thread");
        if (HasClass) fields.Add("class");
        if (HasProcessStart) fields.Add("process-start");
        return fields.Count == 0 ? "none" : string.Join(',', fields);
    }

    private static bool Has(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

internal sealed class PendingRecoveryCandidate
{
    public string CandidateId { get; init; } = string.Empty;
    public IntPtr Hwnd { get; init; }
    public uint ProcessId { get; init; }
    public uint WindowThreadId { get; init; }
    public string ExePath { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public long ProcessStartTimeUtcTicks { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool Visible { get; init; }
    public bool Iconic { get; init; }
}

internal interface IPendingRecoveryNativeApi
{
    bool IsWindow(IntPtr hwnd);
    uint GetProcessId(IntPtr hwnd);
    uint GetWindowThreadId(IntPtr hwnd);
    string? GetProcessImagePath(uint pid);
    string? GetClassName(IntPtr hwnd);
    long GetProcessStartTimeUtcTicks(uint pid);
    IntPtr GetProperty(IntPtr hwnd, string propertyName);
    bool SetProperty(IntPtr hwnd, string propertyName, IntPtr value);
    bool RemoveProperty(IntPtr hwnd, string propertyName, IntPtr expectedValue);
    bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement);
    bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    bool ShowWindow(IntPtr hwnd, int command);
    bool IsWindowVisible(IntPtr hwnd);
    int SetTransitionsDisabled(IntPtr hwnd, int value);
}

internal sealed class NativePendingRecoveryNativeApi : IPendingRecoveryNativeApi
{
    public static NativePendingRecoveryNativeApi Instance { get; } = new();
    private NativePendingRecoveryNativeApi() { }

    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public uint GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public uint GetWindowThreadId(IntPtr hwnd)
        => NativeMethods.GetWindowThreadProcessId(hwnd, out _);

    public string? GetProcessImagePath(uint pid) => NativeMethods.GetProcessImagePath(pid);
    public string? GetClassName(IntPtr hwnd) => NativeMethods.GetClassNameString(hwnd);
    public long GetProcessStartTimeUtcTicks(uint pid) => NativeMethods.GetProcessStartTimeUtcTicks(pid);
    public IntPtr GetProperty(IntPtr hwnd, string propertyName) => NativeMethods.GetProp(hwnd, propertyName);
    public bool SetProperty(IntPtr hwnd, string propertyName, IntPtr value) => NativeMethods.SetProp(hwnd, propertyName, value);

    public bool RemoveProperty(IntPtr hwnd, string propertyName, IntPtr expectedValue)
    {
        if (expectedValue == IntPtr.Zero || GetProperty(hwnd, propertyName) != expectedValue)
            return false;
        return NativeMethods.RemoveProp(hwnd, propertyName) == expectedValue;
    }

    public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        => NativeMethods.SetWindowPlacement(hwnd, ref placement);

    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        => NativeMethods.SetWindowPos(hwnd, insertAfter, x, y, width, height, flags);

    public bool ShowWindow(IntPtr hwnd, int command) => NativeMethods.ShowWindow(hwnd, command);
    public bool IsWindowVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    public int SetTransitionsDisabled(IntPtr hwnd, int value)
        => NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
}

internal interface IRecoveryStatusProbe
{
    string Classify(HiddenWindowEntry entry, PendingRecoveryFields fields);
}

internal sealed class NativeRecoveryStatusProbe : IRecoveryStatusProbe
{
    public static NativeRecoveryStatusProbe Instance { get; } = new();
    private NativeRecoveryStatusProbe() { }
    public string Classify(HiddenWindowEntry entry, PendingRecoveryFields fields)
    {
        if (!fields.HasHwnd || !fields.HasPid || !fields.HasExe || entry.Hwnd == 0 || entry.Pid == 0 || string.IsNullOrWhiteSpace(entry.ExePath))
            return "unverifiable";
        IntPtr hwnd = new(entry.Hwnd);
        if (!NativeMethods.IsWindow(hwnd))
            return "clearly-gone";
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return "unverifiable";
        if (pid != entry.Pid)
            return "clearly-gone";
        string? exe = NativeMethods.GetProcessImagePath(pid);
        if (string.IsNullOrWhiteSpace(exe))
            return "unverifiable";
        if (!string.Equals(exe, entry.ExePath, StringComparison.OrdinalIgnoreCase))
            return "clearly-gone";
        if (fields.HasThread)
        {
            uint thread = NativeMethods.GetWindowThreadProcessId(hwnd, out uint ignoredPid);
            if (thread == 0)
                return "unverifiable";
            if (thread != entry.WindowThreadId)
                return "clearly-gone";
        }
        if (fields.HasClass)
        {
            string? className = NativeMethods.GetClassNameString(hwnd);
            if (string.IsNullOrWhiteSpace(className))
                return "unverifiable";
            if (!string.Equals(className, entry.ClassName, StringComparison.Ordinal))
                return "clearly-gone";
        }
        if (fields.HasProcessStart)
        {
            long start = NativeMethods.GetProcessStartTimeUtcTicks(pid);
            if (start == 0)
                return "unverifiable";
            if (start != entry.ProcessStartTimeUtcTicks)
                return "clearly-gone";
        }
        return "potentially-recoverable";
    }
}

internal sealed class NativeRecoveryStatusProbeAdapter : IRecoveryStatusProbe
{
    private readonly IPendingRecoveryNativeApi _api;
    public NativeRecoveryStatusProbeAdapter(IPendingRecoveryNativeApi api) => _api = api;
    public string Classify(HiddenWindowEntry entry, PendingRecoveryFields fields)
    {
        if (!fields.HasHwnd || !fields.HasPid || !fields.HasExe || entry.Hwnd == 0 || entry.Pid == 0 || string.IsNullOrWhiteSpace(entry.ExePath))
            return "unverifiable";
        IntPtr hwnd = new(entry.Hwnd);
        if (!_api.IsWindow(hwnd)) return "clearly-gone";
        uint pid = _api.GetProcessId(hwnd);
        if (pid == 0) return "unverifiable";
        if (pid != entry.Pid) return "clearly-gone";
        string? exe = _api.GetProcessImagePath(pid);
        if (string.IsNullOrWhiteSpace(exe)) return "unverifiable";
        if (!string.Equals(exe, entry.ExePath, StringComparison.OrdinalIgnoreCase)) return "clearly-gone";
        if (fields.HasThread)
        {
            uint thread = _api.GetWindowThreadId(hwnd);
            if (thread == 0) return "unverifiable";
            if (thread != entry.WindowThreadId) return "clearly-gone";
        }
        if (fields.HasClass)
        {
            string? className = _api.GetClassName(hwnd);
            if (string.IsNullOrWhiteSpace(className)) return "unverifiable";
            if (!string.Equals(className, entry.ClassName, StringComparison.Ordinal)) return "clearly-gone";
        }
        if (fields.HasProcessStart)
        {
            long start = _api.GetProcessStartTimeUtcTicks(pid);
            if (start == 0) return "unverifiable";
            if (start != entry.ProcessStartTimeUtcTicks) return "clearly-gone";
        }
        return "potentially-recoverable";
    }
}
