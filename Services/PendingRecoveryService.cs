using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    internal const int RecoveryTransactionSchemaVersion = 1;

    internal static class RecoveryPhase
    {
        public const string Prepared = "Prepared";
        public const string TokenInstalled = "TokenInstalled";
        public const string PlacementComplete = "PlacementComplete";
        public const string VisibilityComplete = "VisibilityComplete";
        public const string NativeRecoveryComplete = "NativeRecoveryComplete";
        public const string TokenRemoved = "TokenRemoved";
        public const string Retired = "Retired";
    }

    private enum CompletedTargetIdentityResult
    {
        Match,
        Destroyed,
        Replacement,
        Unverifiable,
    }

    private sealed class RecoveryFaultException : Exception
    {
        public RecoveryFaultException(string stage)
            : base("Injected recovery fault at " + stage)
        {
        }
    }

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
            builder.AppendLine($"status: {SanitizeConsoleDisplayValue(catalog.Error)}");
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
            builder.AppendLine($"file={SanitizeConsoleDisplayValue(file.FileName)} schema=v{file.Version} entryCount={file.Entries.Count} fileStatus={SanitizeConsoleDisplayValue(file.Status)}");
            foreach (PendingRecoveryEntry entry in file.Entries)
            {
                builder.AppendLine($"  id={SanitizeConsoleDisplayValue(entry.SessionId)} schema=v{entry.Version} status={SanitizeConsoleDisplayValue(entry.Status)} fields={SanitizeConsoleDisplayValue(entry.AvailableFields)} recordedHwnd={FormatHwnd(entry.Entry.Hwnd)}");
                builder.AppendLine($"    recoveryMode={SanitizeConsoleDisplayValue(entry.RecoveryMode)}");
            }
        }
        builder.AppendLine("next: run TabDock.exe --recover-pending from a supervised terminal to select and confirm one live target");
        return builder.ToString().TrimEnd();
    }

    internal static int RunInteractive(
        TextReader input,
        TextWriter output,
        string? directory = null,
        IPendingRecoveryNativeApi? api = null,
        IReadOnlyList<PendingRecoveryCandidate>? candidates = null,
        Func<string, bool>? faultInjector = null)
    {
        IPendingRecoveryNativeApi native = api ?? NativePendingRecoveryNativeApi.Instance;
        PendingRecoveryCatalog catalog = Discover(directory, api);
        output.WriteLine("TabDock supervised pending recovery");
        output.WriteLine("This command is user-initiated. Startup never performs tokenless legacy recovery.");
        output.Flush();
        if (catalog.Error != null)
        {
            output.WriteLine($"Pending evidence is {SanitizeConsoleDisplayValue(catalog.Error)}; no mutation was attempted.");
            return 2;
        }
        PendingRecoveryFile? unreadableFile = catalog.Files.FirstOrDefault(file =>
            file.Entries.Count == 0
            && !string.Equals(file.Status, "empty-resolved", StringComparison.Ordinal));
        if (unreadableFile != null)
        {
            output.WriteLine($"Pending evidence in {SanitizeConsoleDisplayValue(unreadableFile.FileName)} is {SanitizeConsoleDisplayValue(unreadableFile.Status)}; no mutation was attempted.");
            return 2;
        }

        // A transaction that already durably completed native recovery is a
        // disk-cleanup job, not a new supervised native operation. This is the
        // crash boundary after NativeRecoveryComplete, including the case in
        // which the exact token was removed before the process died.
        PendingRecoveryEntry[] completedEntries = catalog.Files
            .SelectMany(file => file.Entries)
            .Where(entry => entry.AlreadyResolved
                || (entry.Transaction != null
                    && IsSupportedTransaction(entry, entry.Transaction)
                    && IsNativeRecoveryComplete(entry.Transaction.Phase)))
            .ToArray();
        bool cleanupFailure = false;
        foreach (PendingRecoveryEntry completedEntry in completedEntries)
        {
            if (!completedEntry.AlreadyResolved
                && !ReconcileCompletedTransaction(completedEntry, native, out string reconciliationError))
            {
                cleanupFailure = true;
                output.WriteLine($"Interrupted transaction {SanitizeConsoleDisplayValue(completedEntry.SessionId)} needs supervised cleanup: {SanitizeConsoleDisplayValue(reconciliationError)}. Evidence was retained.");
                continue;
            }

            if (!completedEntry.AlreadyResolved
                && !MarkResolved(completedEntry, out string completedMarkerError))
            {
                cleanupFailure = true;
                output.WriteLine($"Native recovery is complete for {SanitizeConsoleDisplayValue(completedEntry.SessionId)}, but its durable resolution marker could not be written: {SanitizeConsoleDisplayValue(completedMarkerError)}. Evidence was retained.");
                continue;
            }

            if (!RetireEntry(completedEntry, out string completedRetirementError, faultInjector))
            {
                cleanupFailure = true;
                output.WriteLine($"Resolved entry {SanitizeConsoleDisplayValue(completedEntry.SessionId)} still needs disk-only retirement: {SanitizeConsoleDisplayValue(completedRetirementError)}. Evidence was retained.");
            }
        }
        if (cleanupFailure)
            return 2;

        // Re-read after disk-only cleanup so a selected sibling carries the
        // current source SHA and cannot be rejected merely because an earlier
        // completed transaction was retired in this invocation.
        if (completedEntries.Length > 0)
            catalog = Discover(directory, api);

        List<PendingRecoveryEntry> entries = catalog.Files
            .SelectMany(file => file.Entries)
            .Where(entry => !entry.AlreadyResolved)
            .ToList();
        if (entries.Count == 0)
        {
            output.WriteLine(completedEntries.Length > 0
                ? "Resolved pending entries were retired; no unresolved pending recovery entries were found."
                : "No unresolved pending recovery entries were found.");
            return 0;
        }

        output.WriteLine("Pending entries (read-only):");
        foreach (PendingRecoveryEntry entry in entries)
        {
            output.WriteLine($"  {SanitizeConsoleDisplayValue(entry.SessionId)}: schema=v{entry.Version}, status={SanitizeConsoleDisplayValue(entry.Status)}, fields={SanitizeConsoleDisplayValue(entry.AvailableFields)}, file={SanitizeConsoleDisplayValue(entry.FileName)}");
        }

        PendingRecoveryEntry? selectedEntry = SelectEntry(entries, input, output);
        if (selectedEntry == null)
            return 1;
        bool interruptedTransaction = selectedEntry.Transaction != null
            && IsSupportedTransaction(selectedEntry, selectedEntry.Transaction)
            && !IsNativeRecoveryComplete(selectedEntry.Transaction.Phase);
        if (selectedEntry.Status is not ("potentially-recoverable" or "unverifiable")
            && !interruptedTransaction)
        {
            output.WriteLine($"Entry {SanitizeConsoleDisplayValue(selectedEntry.SessionId)} is not eligible for a live recovery transaction ({SanitizeConsoleDisplayValue(selectedEntry.Status)}). Evidence was retained.");
            return 2;
        }
        if (interruptedTransaction)
            output.WriteLine($"Entry {SanitizeConsoleDisplayValue(selectedEntry.SessionId)} is an interrupted recovery transaction at phase {SanitizeConsoleDisplayValue(selectedEntry.Transaction!.Phase)}; review the exact live target before resuming.");

        IReadOnlyList<PendingRecoveryCandidate> liveCandidates = candidates ?? EnumerateCandidates();
        output.WriteLine($"Live top-level candidates: {liveCandidates.Count}");
        foreach (PendingRecoveryCandidate candidate in liveCandidates)
        {
            string title = SanitizeConsoleDisplayValue(candidate.Title);
            output.WriteLine($"  {SanitizeConsoleDisplayValue(candidate.CandidateId)}: hwnd={FormatHwnd(candidate.Hwnd)} pid={candidate.ProcessId} exe={SafeExecutableLabel(candidate.ExePath)} class={SanitizeConsoleDisplayValue(candidate.ClassName)} visible={candidate.Visible} title=\"{title}\"");
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

        if (selectedEntry.RecoveryMode == "v2-intentional-hide")
        {
            output.WriteLine("This entry says the guest intentionally hid itself. TabDock will keep it hidden and only clean interrupted TabDock presentation state.");
        }
        output.WriteLine($"Selected {SanitizeConsoleDisplayValue(selectedEntry.SessionId)} -> {SanitizeConsoleDisplayValue(selectedCandidate.CandidateId)} ({SafeExecutableLabel(selectedCandidate.ExePath)}, PID {selectedCandidate.ProcessId}, title=\"{SanitizeConsoleDisplayValue(selectedCandidate.Title)}\").");
        output.Write("Type YES to confirm this exact live target, or anything else to cancel: ");
        output.Flush();
        string? confirmation = input.ReadLine();
        if (!string.Equals(confirmation, "YES", StringComparison.Ordinal))
        {
            output.WriteLine("Recovery cancelled; no native mutation or evidence change occurred.");
            return 1;
        }

        if (!ExecuteRecovery(selectedEntry, selectedCandidate, native, out string result, faultInjector: faultInjector))
        {
            output.WriteLine("Recovery failed: " + SanitizeConsoleDisplayValue(result));
            output.WriteLine("The pending evidence was retained.");
            return 2;
        }

        if (!MarkResolved(selectedEntry, out string markerError))
        {
            output.WriteLine("The guest was recovered, but the durable resolution marker could not be written: " + SanitizeConsoleDisplayValue(markerError));
            output.WriteLine("The pending evidence remains and must be cleaned up by a later supervised invocation; native recovery will not be repeated when the marker is available.");
            return 2;
        }
        InjectFault(faultInjector, "after-resolution-marker");
        if (!RetireEntry(selectedEntry, out string retireError, faultInjector))
        {
            output.WriteLine("The guest was recovered and marked resolved, but pending-entry retirement needs a later retry: " + SanitizeConsoleDisplayValue(retireError));
            return 2;
        }

        output.WriteLine($"Recovered and retired entry {SanitizeConsoleDisplayValue(selectedEntry.SessionId)}. Unresolved sibling entries, if any, remain pending.");
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
        out string error,
        Func<IntPtr>? tokenFactory = null,
        Func<string, bool>? faultInjector = null)
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

        IntPtr currentRecoveryToken = api.GetProperty(hwnd, TemporaryRecoveryPropertyName);
        PendingRecoveryTransaction? transaction = entry.Transaction;
        if (transaction != null && !IsSupportedTransaction(entry, transaction))
        {
            error = "the durable recovery transaction is unsupported or cannot prove ownership";
            return false;
        }
        if (transaction == null)
        {
            if (currentRecoveryToken != IntPtr.Zero)
            {
                error = "foreign/unverifiable recovery token is present; it was not removed";
                return false;
            }
            WindowIdentityResult selectedIdentity = EvaluateRecoveryTarget(entry, candidate, api, out string selectedReason);
            if (selectedIdentity != WindowIdentityResult.Match)
            {
                error = selectedReason;
                return false;
            }

            IntPtr generatedToken = tokenFactory?.Invoke() ?? AllocateRecoveryToken();
            if (generatedToken == IntPtr.Zero)
            {
                error = "could not allocate a nonzero cryptographically random temporary recovery token";
                return false;
            }
            transaction = CreateTransaction(entry, candidate, generatedToken);
            if (!PersistTransaction(entry, transaction, out error))
                return false;
            entry.Transaction = transaction;
            InjectFault(faultInjector, "after-prepared");
        }
        else
        {
            if (!TransactionMatchesCandidate(entry, transaction, candidate))
            {
                error = "the selected target does not match the durable interrupted-transaction identity";
                return false;
            }
            WindowIdentityResult selectedIdentity = EvaluateRecoveryTarget(entry, candidate, api, out string selectedReason);
            if (selectedIdentity != WindowIdentityResult.Match)
            {
                error = selectedReason;
                return false;
            }
            if (IsNativeRecoveryComplete(transaction.Phase))
            {
                return ReconcileCompletedTransaction(entry, api, out error);
            }
        }

        IntPtr token = new(transaction.RecoveryToken);
        if (token == IntPtr.Zero)
        {
            error = "the durable recovery transaction contained a zero token";
            return false;
        }

        IntPtr existingRecoveryToken = api.GetProperty(hwnd, TemporaryRecoveryPropertyName);
        if (existingRecoveryToken != IntPtr.Zero && existingRecoveryToken != token)
        {
            error = "foreign/unverifiable recovery token is present; it was not removed";
            return false;
        }

        bool success = false;
        bool injectedFault = false;
        try
        {
            if (existingRecoveryToken == IntPtr.Zero)
            {
                // The transaction is already durable. Revalidate immediately
                // before the external property write and never overwrite a
                // property belonging to another recovery generation.
                if (!TryPreTokenBoundary(entry, candidate, api, out error))
                    return false;
                if (!api.SetProperty(hwnd, TemporaryRecoveryPropertyName, token)
                    || api.GetProperty(hwnd, TemporaryRecoveryPropertyName) != token)
                {
                    error = "temporary recovery token installation could not be verified";
                    return false;
                }
                InjectFault(faultInjector, "after-setprop");
            }

            if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.TokenInstalled)
                && !PersistTransactionPhase(entry, transaction, RecoveryPhase.TokenInstalled, out error))
                return false;

            if (entry.RecoveryMode == "v2-intentional-hide")
            {
                if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.NativeRecoveryComplete))
                {
                    if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                        return false;
                    if (RestoreTransitions(entry, hwnd, api, out error, restoreWhenUnrecorded: true))
                        InjectFault(faultInjector, "after-dwm");
                    else
                        return false;
                    if (!PersistTransactionPhase(entry, transaction, RecoveryPhase.NativeRecoveryComplete, out error))
                        return false;
                    InjectFault(faultInjector, "after-native-complete");
                }
            }
            else
            {
                if (!entry.IsV1
                    && PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.PlacementComplete))
                {
                    if (entry.Entry.HasOriginalPlacement)
                    {
                        if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                            return false;
                        NativeMethods.WINDOWPLACEMENT placement = CreatePlacement(entry.Entry);
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
                    InjectFault(faultInjector, "after-placement");
                    if (!PersistTransactionPhase(entry, transaction, RecoveryPhase.PlacementComplete, out error))
                        return false;
                }

                if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.VisibilityComplete))
                {
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
                    InjectFault(faultInjector, "after-visibility");
                    if (!PersistTransactionPhase(entry, transaction, RecoveryPhase.VisibilityComplete, out error))
                        return false;
                }

                if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.NativeRecoveryComplete))
                {
                    if (!TryGenerationBoundary(entry, candidate, token, api, out error))
                        return false;
                    if (!RestoreTransitions(entry, hwnd, api, out error))
                        return false;
                    InjectFault(faultInjector, "after-dwm");
                    if (!PersistTransactionPhase(entry, transaction, RecoveryPhase.NativeRecoveryComplete, out error))
                        return false;
                    InjectFault(faultInjector, "after-native-complete");
                }
            }

            if (!RemoveExactRecoveryToken(hwnd, token, api, out error))
                return false;
            InjectFault(faultInjector, "after-remove-property");
            if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.TokenRemoved)
                && !PersistTransactionPhase(entry, transaction, RecoveryPhase.TokenRemoved, out error))
                return false;
            success = true;
            return true;
        }
        catch (RecoveryFaultException)
        {
            injectedFault = true;
            throw;
        }
        finally
        {
            if (!success && !injectedFault)
            {
                // Cleanup is itself generation-scoped. If the target changed,
                // leave the exact property untouched rather than mutating a
                // replacement HWND; the property dies with that HWND.
                if (TryGenerationBoundary(entry, candidate, token, api, out _))
                    RemoveExactRecoveryToken(hwnd, token, api, out _);
            }
        }
    }

    private static bool ReconcileCompletedTransaction(
        PendingRecoveryEntry entry,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        error = string.Empty;
        PendingRecoveryTransaction? transaction = entry.Transaction;
        if (transaction == null || !IsNativeRecoveryComplete(transaction.Phase))
        {
            error = "native recovery is not durably complete";
            return false;
        }

        IntPtr hwnd = new(transaction.CandidateHwnd);
        IntPtr token = new(transaction.RecoveryToken);
        if (token == IntPtr.Zero)
        {
            error = "the durable recovery transaction contained a zero token";
            return false;
        }

        CompletedTargetIdentityResult identity = ClassifyCompletedTarget(transaction, api, out error);
        if (identity == CompletedTargetIdentityResult.Unverifiable)
            return false;

        // A destroyed or positively replaced HWND is safe to reconcile on
        // disk: native recovery was already durably complete, and any property
        // on the old HWND died with it. Never inspect/remove a property from a
        // positive replacement.
        if (identity == CompletedTargetIdentityResult.Match)
        {
            try
            {
                if (api.GetProperty(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName) != IntPtr.Zero)
                {
                    error = "a live TabDock capture token appeared on the completed target";
                    return false;
                }

                IntPtr current = api.GetProperty(hwnd, TemporaryRecoveryPropertyName);
                if (current != IntPtr.Zero && current != token)
                {
                    error = "foreign/unverifiable recovery token is present; it was not removed";
                    return false;
                }
                if (current == token && !api.RemoveProperty(hwnd, TemporaryRecoveryPropertyName, token))
                {
                    error = "the exact completed recovery token could not be removed";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"completed target property probe threw {ex.GetType().Name}";
                return false;
            }
        }

        if (entry.TransactionNeedsRebind
            && !PersistTransaction(entry, transaction, out error))
        {
            return false;
        }

        if (PhaseRank(transaction.Phase) < PhaseRank(RecoveryPhase.TokenRemoved)
            && !PersistTransactionPhase(entry, transaction, RecoveryPhase.TokenRemoved, out error))
            return false;
        return true;
    }

    private static CompletedTargetIdentityResult ClassifyCompletedTarget(
        PendingRecoveryTransaction transaction,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        try
        {
            IntPtr hwnd = new(transaction.CandidateHwnd);
            if (!api.IsWindow(hwnd))
            {
                error = "completed target HWND is destroyed";
                return CompletedTargetIdentityResult.Destroyed;
            }

            if (transaction.CandidatePid == 0
                || transaction.CandidateWindowThreadId == 0
                || string.IsNullOrWhiteSpace(transaction.CandidateExePath)
                || string.IsNullOrWhiteSpace(transaction.CandidateClassName)
                || transaction.CandidateProcessStartTimeUtcTicks == 0)
            {
                error = "completed target identity is incomplete or process-start is unavailable";
                return CompletedTargetIdentityResult.Unverifiable;
            }

            uint pid = api.GetProcessId(hwnd);
            if (pid == 0)
            {
                error = "completed target PID could not be read";
                return CompletedTargetIdentityResult.Unverifiable;
            }
            if (pid != transaction.CandidatePid)
            {
                error = "completed target PID changed; positive replacement evidence";
                return CompletedTargetIdentityResult.Replacement;
            }
            uint thread = api.GetWindowThreadId(hwnd);
            if (thread == 0)
            {
                error = "completed target GUI thread could not be read";
                return CompletedTargetIdentityResult.Unverifiable;
            }
            if (thread != transaction.CandidateWindowThreadId)
            {
                error = "completed target GUI thread changed; positive replacement evidence";
                return CompletedTargetIdentityResult.Replacement;
            }
            string? exe = api.GetProcessImagePath(pid);
            if (string.IsNullOrWhiteSpace(exe))
            {
                error = "completed target executable identity could not be read";
                return CompletedTargetIdentityResult.Unverifiable;
            }
            if (!string.Equals(exe, transaction.CandidateExePath, StringComparison.OrdinalIgnoreCase))
            {
                error = "completed target executable identity changed; positive replacement evidence";
                return CompletedTargetIdentityResult.Replacement;
            }
            string? className = api.GetClassName(hwnd);
            if (string.IsNullOrWhiteSpace(className))
            {
                error = "completed target window class could not be read";
                return CompletedTargetIdentityResult.Unverifiable;
            }
            if (!string.Equals(className, transaction.CandidateClassName, StringComparison.Ordinal))
            {
                error = "completed target window class changed; positive replacement evidence";
                return CompletedTargetIdentityResult.Replacement;
            }
            long start = api.GetProcessStartTimeUtcTicks(pid);
            if (start == 0)
            {
                error = "completed target process-start identity could not be read";
                return CompletedTargetIdentityResult.Unverifiable;
            }
            if (start != transaction.CandidateProcessStartTimeUtcTicks)
            {
                error = "completed target process-start identity changed; positive replacement evidence";
                return CompletedTargetIdentityResult.Replacement;
            }
            error = "completed target identity matched";
            return CompletedTargetIdentityResult.Match;
        }
        catch (Exception ex)
        {
            error = $"completed target identity probe threw {ex.GetType().Name}";
            return CompletedTargetIdentityResult.Unverifiable;
        }
    }

    private static bool TransactionMatchesCandidate(
        PendingRecoveryEntry entry,
        PendingRecoveryTransaction transaction,
        PendingRecoveryCandidate candidate)
        => TransactionBindsEntry(entry, transaction)
            && transaction.CandidateHwnd == candidate.Hwnd.ToInt64()
            && transaction.CandidatePid == candidate.ProcessId
            && transaction.CandidateWindowThreadId == candidate.WindowThreadId
            && string.Equals(transaction.CandidateExePath, candidate.ExePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(transaction.CandidateClassName, candidate.ClassName, StringComparison.Ordinal)
            && (transaction.CandidateProcessStartTimeUtcTicks == 0
                || transaction.CandidateProcessStartTimeUtcTicks == candidate.ProcessStartTimeUtcTicks);

    private static bool IsSupportedTransaction(
        PendingRecoveryEntry entry,
        PendingRecoveryTransaction transaction)
        => transaction.SchemaVersion == RecoveryTransactionSchemaVersion
            && TransactionBindsEntry(entry, transaction)
            && transaction.CandidateHwnd != 0
            && transaction.CandidatePid != 0
            && transaction.CandidateWindowThreadId != 0
            && !string.IsNullOrWhiteSpace(transaction.CandidateExePath)
            && !string.IsNullOrWhiteSpace(transaction.CandidateClassName)
            && transaction.RecoveryToken != 0
            && transaction.RecoveryMode == entry.RecoveryMode
            && PhaseRank(transaction.Phase) >= PhaseRank(RecoveryPhase.Prepared);

    private static bool TransactionBindsEntry(
        PendingRecoveryEntry entry,
        PendingRecoveryTransaction transaction)
        => string.Equals(transaction.SourceFileId, entry.FileName, StringComparison.Ordinal)
            && string.Equals(transaction.EntryFingerprint, entry.EntryFingerprint, StringComparison.OrdinalIgnoreCase)
            && ((string.Equals(transaction.SourceFileSha256, entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase)
                    && transaction.EntryIndex == entry.EntryIndex)
                || (entry.TransactionNeedsRebind
                    && !string.Equals(transaction.Phase, RecoveryPhase.Retired, StringComparison.Ordinal)));

    private static PendingRecoveryTransaction CreateTransaction(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IntPtr token)
        => new()
        {
            SchemaVersion = RecoveryTransactionSchemaVersion,
            SourceFileId = entry.FileName,
            SourceFileSha256 = entry.SourceFileSha256,
            EntryFingerprint = entry.EntryFingerprint,
            EntryIndex = entry.EntryIndex,
            CandidateHwnd = candidate.Hwnd.ToInt64(),
            CandidatePid = candidate.ProcessId,
            CandidateWindowThreadId = candidate.WindowThreadId,
            CandidateExePath = candidate.ExePath,
            CandidateClassName = candidate.ClassName,
            CandidateProcessStartTimeUtcTicks = candidate.ProcessStartTimeUtcTicks,
            RecoveryToken = token.ToInt64(),
            RecoveryMode = entry.RecoveryMode,
            Phase = RecoveryPhase.Prepared,
            PreparedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

    private static IntPtr AllocateRecoveryToken()
    {
        Span<byte> bytes = stackalloc byte[IntPtr.Size];
        do
        {
            RandomNumberGenerator.Fill(bytes);
            long value = IntPtr.Size == sizeof(long)
                ? BitConverter.ToInt64(bytes)
                : BitConverter.ToInt32(bytes);
            value &= long.MaxValue;
            if (value != 0)
                return new IntPtr(value);
        }
        while (true);
    }

    private static bool PersistTransaction(
        PendingRecoveryEntry entry,
        PendingRecoveryTransaction transaction,
        out string error)
    {
        error = string.Empty;
        string path = entry.FullPath + ".recovered";
        if (!TryReadLedger(path, out ResolutionLedger ledger, out error))
            return false;
        ledger.Transactions ??= new List<PendingRecoveryTransaction>();
        List<PendingRecoveryTransaction> exactMatches = ledger.Transactions
            .Where(item =>
                string.Equals(item.SourceFileId, transaction.SourceFileId, StringComparison.Ordinal)
                && string.Equals(item.SourceFileSha256, transaction.SourceFileSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.EntryFingerprint, transaction.EntryFingerprint, StringComparison.OrdinalIgnoreCase)
                && item.EntryIndex == transaction.EntryIndex)
            .ToList();
        PendingRecoveryTransaction? existing = exactMatches.Count == 1 ? exactMatches[0] : null;
        if (exactMatches.Count > 1)
        {
            error = "multiple durable recovery transactions already claim this entry";
            return false;
        }
        if (existing == null)
        {
            List<PendingRecoveryTransaction> reboundMatches = ledger.Transactions
                .Where(item =>
                    item.RecoveryToken == transaction.RecoveryToken
                    && transaction.RecoveryToken != 0
                    && string.Equals(item.SourceFileId, transaction.SourceFileId, StringComparison.Ordinal)
                    && string.Equals(item.EntryFingerprint, transaction.EntryFingerprint, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.Phase, RecoveryPhase.Retired, StringComparison.Ordinal))
                .ToList();
            if (reboundMatches.Count == 1)
                existing = reboundMatches[0];
            else if (reboundMatches.Count > 1)
            {
                error = "multiple durable recovery transactions share the rebound token";
                return false;
            }
        }
        if (existing != null && existing.RecoveryToken != transaction.RecoveryToken)
        {
            error = "a different durable recovery transaction already owns this entry";
            return false;
        }
        if (existing == null)
            ledger.Transactions.Add(transaction);
        else
        {
            existing.SchemaVersion = transaction.SchemaVersion;
            existing.SourceFileId = transaction.SourceFileId;
            existing.SourceFileSha256 = transaction.SourceFileSha256;
            existing.EntryFingerprint = transaction.EntryFingerprint;
            existing.EntryIndex = transaction.EntryIndex;
            existing.CandidateHwnd = transaction.CandidateHwnd;
            existing.CandidatePid = transaction.CandidatePid;
            existing.CandidateWindowThreadId = transaction.CandidateWindowThreadId;
            existing.CandidateExePath = transaction.CandidateExePath;
            existing.CandidateClassName = transaction.CandidateClassName;
            existing.CandidateProcessStartTimeUtcTicks = transaction.CandidateProcessStartTimeUtcTicks;
            existing.RecoveryMode = transaction.RecoveryMode;
            existing.Phase = transaction.Phase;
            existing.RecoveryToken = transaction.RecoveryToken;
            existing.PreparedUtc = transaction.PreparedUtc;
            existing.UpdatedUtc = transaction.UpdatedUtc;
        }
        try
        {
            WriteDurableJson(path, ledger);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static bool PersistTransactionPhase(
        PendingRecoveryEntry entry,
        PendingRecoveryTransaction transaction,
        string phase,
        out string error)
    {
        transaction.Phase = phase;
        transaction.UpdatedUtc = DateTimeOffset.UtcNow;
        return PersistTransaction(entry, transaction, out error);
    }

    private static int PhaseRank(string? phase)
        => phase switch
        {
            RecoveryPhase.Prepared => 0,
            RecoveryPhase.TokenInstalled => 1,
            RecoveryPhase.PlacementComplete => 2,
            RecoveryPhase.VisibilityComplete => 3,
            RecoveryPhase.NativeRecoveryComplete => 4,
            RecoveryPhase.TokenRemoved => 5,
            RecoveryPhase.Retired => 6,
            _ => -1,
        };

    private static bool IsNativeRecoveryComplete(string? phase)
        => PhaseRank(phase) >= PhaseRank(RecoveryPhase.NativeRecoveryComplete);

    private static bool RestoreTransitions(
        PendingRecoveryEntry entry,
        IntPtr hwnd,
        IPendingRecoveryNativeApi api,
        out string error,
        bool restoreWhenUnrecorded = false)
    {
        error = string.Empty;
        if (entry.IsV1 || (!entry.Entry.HasOriginalTransitionsState && !restoreWhenUnrecorded))
            return true;
        int value = entry.Entry.OriginalTransitionsDisabled ? 1 : 0;
        if (api.SetTransitionsDisabled(hwnd, value) != 0)
        {
            error = "DWM transition restoration failed";
            return false;
        }
        return true;
    }

    private static NativeMethods.WINDOWPLACEMENT CreatePlacement(HiddenWindowEntry entry)
        => new()
        {
            length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
            flags = entry.OriginalPlacementFlags,
            showCmd = unchecked((uint)entry.OriginalShowCommand),
            ptMinPosition = new NativeMethods.POINT { x = entry.OriginalMinPositionX, y = entry.OriginalMinPositionY },
            ptMaxPosition = new NativeMethods.POINT { x = entry.OriginalMaxPositionX, y = entry.OriginalMaxPositionY },
            rcNormalPosition = new NativeMethods.RECT
            {
                left = entry.OriginalNormalLeft,
                top = entry.OriginalNormalTop,
                right = entry.OriginalNormalRight,
                bottom = entry.OriginalNormalBottom,
            },
        };

    private static bool RemoveExactRecoveryToken(
        IntPtr hwnd,
        IntPtr token,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        if (api.GetProperty(hwnd, TemporaryRecoveryPropertyName) != token)
        {
            error = "temporary recovery token disappeared or changed before cleanup";
            return false;
        }
        if (!api.RemoveProperty(hwnd, TemporaryRecoveryPropertyName, token))
        {
            error = "temporary recovery token removal failed";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static void InjectFault(Func<string, bool>? faultInjector, string stage)
    {
        if (faultInjector?.Invoke(stage) == true)
            throw new RecoveryFaultException(stage);
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

    private static bool TryPreTokenBoundary(
        PendingRecoveryEntry entry,
        PendingRecoveryCandidate candidate,
        IPendingRecoveryNativeApi api,
        out string error)
    {
        if (EvaluateRecoveryTarget(entry, candidate, api, out error) != WindowIdentityResult.Match)
            return false;
        if (api.GetProperty(candidate.Hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName) != IntPtr.Zero)
        {
            error = "normal capture token appeared before recovery token installation";
            return false;
        }
        if (api.GetProperty(candidate.Hwnd, TemporaryRecoveryPropertyName) != IntPtr.Zero)
        {
            error = "foreign/unverifiable recovery token appeared before installation";
            return false;
        }
        return true;
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
            else if (entry.Transaction?.CandidateProcessStartTimeUtcTicks is long transactionStart
                && transactionStart != 0)
            {
                long start = api.GetProcessStartTimeUtcTicks(pid);
                if (start == 0)
                    return Result(WindowIdentityResult.Unverifiable, "selected process-start identity could not be read", out reason);
                if (start != transactionStart)
                    return Result(WindowIdentityResult.Mismatch, "selected process-start identity differs", out reason);
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
        output.Flush();
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
        output.Flush();
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
            string sourceSha256 = Sha256(rawBytes);
            if (!TryReadLedger(path + ".recovered", out ResolutionLedger ledger, out string ledgerError))
                return PendingRecoveryFile.Unreadable(fileName, path, fileIndex, "unreadable (recovery-ledger)", ledgerError);
            ledger.Resolutions ??= new List<PendingResolution>();
            ledger.Transactions ??= new List<PendingRecoveryTransaction>();
            var fingerprintCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement rawEntry in entriesElement.EnumerateArray())
            {
                string fingerprint = Fingerprint(rawEntry);
                fingerprintCounts[fingerprint] = fingerprintCounts.TryGetValue(fingerprint, out int count)
                    ? count + 1
                    : 1;
            }
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
                PendingResolution? resolved = FindResolution(
                    ledger,
                    fileName,
                    sourceSha256,
                    fingerprint,
                    entryIndex,
                    fingerprintCounts[fingerprint]);
                PendingRecoveryTransaction? transaction = FindTransaction(
                    ledger,
                    fileName,
                    sourceSha256,
                    fingerprint,
                    entryIndex,
                    fingerprintCounts[fingerprint],
                    out bool transactionAmbiguous,
                    out bool transactionNeedsRebind);
                if (transaction != null && transactionNeedsRebind)
                {
                    // Keep the durable old record available for the fallback
                    // lookup in PersistTransaction, while presenting the
                    // transaction to the execution path under the current
                    // source binding. The unique rebind is committed by the
                    // next durable phase write or completed-cleanup pass.
                    transaction = RebindTransaction(transaction, fileName, sourceSha256, fingerprint, entryIndex);
                }
                bool transactionSourceMatches = transaction != null
                    && string.Equals(transaction.SourceFileId, fileName, StringComparison.Ordinal)
                    && string.Equals(transaction.SourceFileSha256, sourceSha256, StringComparison.OrdinalIgnoreCase);
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
                    SourceFileSha256 = sourceSha256,
                    Transaction = transaction,
                    TransactionAmbiguous = transactionAmbiguous,
                    TransactionNeedsRebind = transactionNeedsRebind,
                    AlreadyResolved = resolved != null || (transactionSourceMatches && transaction?.Phase == RecoveryPhase.Retired),
                    Status = resolved != null
                        ? "already-resolved"
                        : transactionSourceMatches && transaction?.Phase == RecoveryPhase.Retired
                            ? "already-resolved"
                        : version > HiddenWindowJournalFile.CurrentVersion
                            ? "future-schema"
                            : transaction != null && !IsNativeRecoveryComplete(transaction.Phase)
                                ? "interrupted-transaction"
                            : transaction != null
                                ? "native-recovery-complete"
                            : probe.Classify(entry, fields),
                };
                if (transactionAmbiguous || (transaction != null && !IsSupportedTransaction(pendingEntry, transaction)))
                    pendingEntry.Status = "unverifiable-transaction";
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

    /// <summary>
    /// Converts externally-derived text to one bounded terminal display field.
    /// This removes C0/C1 controls, DEL, and Unicode line separators while
    /// retaining ordinary Unicode scalars, including emoji and CJK text.
    /// </summary>
    internal static string SanitizeConsoleDisplayValue(string? value)
    {
        const int maxLength = 96;
        string source = value ?? string.Empty;
        var builder = new StringBuilder(Math.Min(source.Length, maxLength));
        bool truncated = false;
        int limit = maxLength;

        foreach (Rune rune in source.EnumerateRunes())
        {
            int scalar = rune.Value;
            string replacement = scalar switch
            {
                '\r' or '\n' or '\t' or 0x2028 or 0x2029 => " ",
                <= 0x1F or 0x7F or (>= 0x80 and <= 0x9F) => "�",
                _ => rune.ToString(),
            };

            if (builder.Length + replacement.Length > limit)
            {
                truncated = true;
                break;
            }
            builder.Append(replacement);
        }

        if (truncated)
        {
            const string ellipsis = "…";
            if (builder.Length + ellipsis.Length > maxLength)
                builder.Length = maxLength - ellipsis.Length;
            builder.Append(ellipsis);
        }
        return builder.ToString();
    }

    internal static string SanitizeConsoleTitle(string title)
        => SanitizeConsoleDisplayValue(title);

    private static string SafeExecutableLabel(string path)
    {
        string leaf;
        try
        {
            leaf = Path.GetFileName(path ?? string.Empty);
        }
        catch
        {
            leaf = string.Empty;
        }
        return SanitizeConsoleDisplayValue(leaf);
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static PendingResolution? FindResolution(
        ResolutionLedger ledger,
        string fileName,
        string sourceSha256,
        string fingerprint,
        int entryIndex,
        int currentFingerprintCount)
    {
        PendingResolution? exact = ledger.Resolutions.FirstOrDefault(item =>
            string.Equals(item.SourceFileId, fileName, StringComparison.Ordinal)
            && string.Equals(item.SourceFileSha256, sourceSha256, StringComparison.OrdinalIgnoreCase)
            && item.EntryIndex == entryIndex
            && string.Equals(item.EntryFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        // Only the original tokenless ledger shape may use a unique
        // fingerprint without a source binding. A marker with a non-empty old
        // SHA cannot be projected onto a changed source by fingerprint alone:
        // for duplicate records that would confuse the retired entry with its
        // surviving byte-identical sibling.
        return currentFingerprintCount == 1
            ? ledger.Resolutions.FirstOrDefault(item =>
                string.IsNullOrEmpty(item.SourceFileId)
                && string.IsNullOrEmpty(item.SourceFileSha256)
                && string.Equals(item.EntryFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static PendingRecoveryTransaction? FindTransaction(
        ResolutionLedger ledger,
        string fileName,
        string sourceSha256,
        string fingerprint,
        int entryIndex,
        int currentFingerprintCount,
        out bool ambiguous,
        out bool needsRebind)
    {
        ambiguous = false;
        needsRebind = false;
        List<PendingRecoveryTransaction> exact = ledger.Transactions
            .Where(item =>
                string.Equals(item.SourceFileId, fileName, StringComparison.Ordinal)
                && string.Equals(item.SourceFileSha256, sourceSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.EntryFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                && item.EntryIndex == entryIndex
                && !string.Equals(item.Phase, RecoveryPhase.Retired, StringComparison.Ordinal))
            .ToList();
        if (exact.Count == 1)
            return exact[0];
        if (exact.Count > 1)
        {
            ambiguous = true;
            return null;
        }

        // Compatibility with the old implementation that physically removed
        // siblings: the source SHA and current index can change, but a single
        // unresolved transaction may be rebound when exactly one current entry
        // has its fingerprint. Never perform this migration for duplicated
        // current fingerprints or multiple candidate transactions.
        List<PendingRecoveryTransaction> legacy = ledger.Transactions
            .Where(item =>
                string.Equals(item.SourceFileId, fileName, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.SourceFileSha256)
                && !string.Equals(item.SourceFileSha256, sourceSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.EntryFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Phase, RecoveryPhase.Retired, StringComparison.Ordinal))
            .ToList();
        if (legacy.Count == 0)
            return null;
        if (currentFingerprintCount != 1 || legacy.Count != 1)
        {
            ambiguous = true;
            return null;
        }

        needsRebind = true;
        return legacy[0];
    }

    private static PendingRecoveryTransaction RebindTransaction(
        PendingRecoveryTransaction source,
        string sourceFileId,
        string sourceFileSha256,
        string entryFingerprint,
        int entryIndex)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            SourceFileId = sourceFileId,
            SourceFileSha256 = sourceFileSha256,
            EntryFingerprint = entryFingerprint,
            EntryIndex = entryIndex,
            CandidateHwnd = source.CandidateHwnd,
            CandidatePid = source.CandidatePid,
            CandidateWindowThreadId = source.CandidateWindowThreadId,
            CandidateExePath = source.CandidateExePath,
            CandidateClassName = source.CandidateClassName,
            CandidateProcessStartTimeUtcTicks = source.CandidateProcessStartTimeUtcTicks,
            RecoveryToken = source.RecoveryToken,
            RecoveryMode = source.RecoveryMode,
            Phase = source.Phase,
            PreparedUtc = source.PreparedUtc,
            UpdatedUtc = source.UpdatedUtc,
        };

    private static bool TryReadLedger(string path, out ResolutionLedger ledger, out string error)
    {
        ledger = new ResolutionLedger();
        error = string.Empty;
        if (!File.Exists(path))
            return true;
        try
        {
            ledger = JsonSerializer.Deserialize<ResolutionLedger>(File.ReadAllText(path))
                ?? new ResolutionLedger();
            ledger.Resolutions ??= new List<PendingResolution>();
            ledger.Transactions ??= new List<PendingRecoveryTransaction>();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static bool MarkResolved(PendingRecoveryEntry entry, out string error)
    {
        error = string.Empty;
        string path = entry.FullPath + ".recovered";
        try
        {
            if (!TryReadLedger(path, out ResolutionLedger ledger, out error))
                return false;
            ledger.Resolutions ??= new List<PendingResolution>();
            ledger.Transactions ??= new List<PendingRecoveryTransaction>();
            if (!ledger.Resolutions.Any(item =>
                    string.Equals(item.SourceFileId, entry.FileName, StringComparison.Ordinal)
                    && string.Equals(item.SourceFileSha256, entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase)
                    && item.EntryIndex == entry.EntryIndex
                    && string.Equals(item.EntryFingerprint, entry.EntryFingerprint, StringComparison.OrdinalIgnoreCase)))
            {
                ledger.Resolutions.Add(new PendingResolution
                {
                    SourceFileId = entry.FileName,
                    SourceFileSha256 = entry.SourceFileSha256,
                    EntryFingerprint = entry.EntryFingerprint,
                    EntryIndex = entry.EntryIndex,
                    SchemaVersion = entry.Version,
                    ResolvedUtc = DateTimeOffset.UtcNow,
                    Result = entry.RecoveryMode == "v2-intentional-hide"
                        ? "intentional-hide-cleanup"
                        : "presentation-restored",
                });
            }
            if (entry.Transaction != null)
            {
                entry.Transaction.Phase = RecoveryPhase.Retired;
                entry.Transaction.UpdatedUtc = DateTimeOffset.UtcNow;
                PendingRecoveryTransaction? persisted = ledger.Transactions.FirstOrDefault(item =>
                    string.Equals(item.SourceFileId, entry.FileName, StringComparison.Ordinal)
                    && item.EntryIndex == entry.EntryIndex
                    && string.Equals(item.EntryFingerprint, entry.EntryFingerprint, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.SourceFileSha256, entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase));
                if (persisted == null)
                    ledger.Transactions.Add(entry.Transaction);
                else
                    persisted.Phase = RecoveryPhase.Retired;
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

    private static bool RetireEntry(
        PendingRecoveryEntry entry,
        out string error,
        Func<string, bool>? faultInjector = null)
    {
        error = string.Empty;
        try
        {
            if (!File.Exists(entry.FullPath))
                return true;

            byte[] bytes = File.ReadAllBytes(entry.FullPath);
            if (!string.Equals(Sha256(bytes), entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "pending file changed after discovery";
                return false;
            }
            JsonNode? root = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes));
            if (root is not JsonObject rootObject
                || !TryGetJsonNodeProperty(rootObject, "Entries", out JsonNode? entriesNode)
                || entriesNode is not JsonArray entries)
            {
                error = "pending JSON entries could not be reopened";
                return false;
            }

            if (entry.EntryIndex < 0 || entry.EntryIndex >= entries.Count
                || entries[entry.EntryIndex] is not JsonNode node
                || !string.Equals(
                    Fingerprint(node),
                    entry.EntryFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "pending source no longer contains the exact discovered entry";
                return false;
            }

            if (!TryReadLedger(entry.FullPath + ".recovered", out ResolutionLedger ledger, out error))
                return false;
            ledger.Resolutions ??= new List<PendingResolution>();
            var fingerprintCounts = entries
                .OfType<JsonNode>()
                .GroupBy(Fingerprint, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            bool allResolved = true;
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index] is not JsonNode sibling)
                {
                    error = "pending source contains an unreadable sibling record";
                    return false;
                }
                string siblingFingerprint = Fingerprint(sibling);
                bool exactResolution = ledger.Resolutions.Any(item =>
                    string.Equals(item.SourceFileId, entry.FileName, StringComparison.Ordinal)
                    && string.Equals(item.SourceFileSha256, entry.SourceFileSha256, StringComparison.OrdinalIgnoreCase)
                    && item.EntryIndex == index
                    && string.Equals(item.EntryFingerprint, siblingFingerprint, StringComparison.OrdinalIgnoreCase));
                bool uniqueLegacyResolution = fingerprintCounts[siblingFingerprint] == 1
                    && ledger.Resolutions.Any(item =>
                        string.IsNullOrEmpty(item.SourceFileId)
                        && string.IsNullOrEmpty(item.SourceFileSha256)
                        && string.Equals(item.EntryFingerprint, siblingFingerprint, StringComparison.OrdinalIgnoreCase));
                if (!exactResolution && !uniqueLegacyResolution)
                {
                    allResolved = false;
                    break;
                }
            }

            // A source may also have compatibility-era transaction records
            // that no longer map to a current array position. Do not delete
            // the source while such a non-retired record lacks its own
            // durable resolution marker; retaining the bytes is safer than
            // stranding foreign or unverifiable evidence.
            if (allResolved
                && ledger.Transactions.Any(transaction =>
                    string.Equals(transaction.SourceFileId, entry.FileName, StringComparison.Ordinal)
                    && !string.Equals(transaction.Phase, RecoveryPhase.Retired, StringComparison.Ordinal)
                    && !ledger.Resolutions.Any(resolution =>
                        string.Equals(resolution.SourceFileId, transaction.SourceFileId, StringComparison.Ordinal)
                        && string.Equals(resolution.SourceFileSha256, transaction.SourceFileSha256, StringComparison.OrdinalIgnoreCase)
                        && resolution.EntryIndex == transaction.EntryIndex
                        && string.Equals(resolution.EntryFingerprint, transaction.EntryFingerprint, StringComparison.OrdinalIgnoreCase))))
            {
                allResolved = false;
            }

            // The source remains byte-for-byte immutable while any sibling is
            // unresolved. The sidecar ledger is the logical retirement state.
            // Keep the fault seam at this boundary so an interrupted retry is
            // covered without rewriting the source array.
            InjectFault(faultInjector, "during-retirement");
            if (allResolved)
            {
                File.Delete(entry.FullPath);
                InjectFault(faultInjector, "after-retirement");
            }
            return true;
        }
        catch (RecoveryFaultException)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static bool TryGetJsonNodeProperty(JsonObject root, string name, out JsonNode? value)
    {
        foreach (KeyValuePair<string, JsonNode?> property in root)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = null;
        return false;
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

    private static string Fingerprint(JsonNode node)
        => Sha256(Encoding.UTF8.GetBytes(node.ToJsonString()));

    private sealed class ResolutionLedger
    {
        public List<PendingResolution> Resolutions { get; set; } = new();
        public List<PendingRecoveryTransaction> Transactions { get; set; } = new();
    }

    private sealed class PendingResolution
    {
        public string SourceFileId { get; set; } = string.Empty;
        public string SourceFileSha256 { get; set; } = string.Empty;
        public string EntryFingerprint { get; set; } = string.Empty;
        public int EntryIndex { get; set; }
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
    public string Status { get; set; } = string.Empty;
    public bool AlreadyResolved { get; init; }
    public PendingRecoveryTransaction? Transaction { get; set; }
    public bool TransactionAmbiguous { get; init; }
    public bool TransactionNeedsRebind { get; init; }
    public bool IsV1 => Version == HiddenWindowJournalFile.LegacyMinimalVersion;
    public string RecoveryMode
        => IsV1
            ? "v1-visible"
            : Entry.DoNotRescue
                ? "v2-intentional-hide"
                : "v2-presentation";
    public string AvailableFields => Fields.ToDisplayString();
}

internal sealed class PendingRecoveryTransaction
{
    public int SchemaVersion { get; set; }
    public string SourceFileId { get; set; } = string.Empty;
    public string SourceFileSha256 { get; set; } = string.Empty;
    public string EntryFingerprint { get; set; } = string.Empty;
    public int EntryIndex { get; set; }
    public long CandidateHwnd { get; set; }
    public uint CandidatePid { get; set; }
    public uint CandidateWindowThreadId { get; set; }
    public string CandidateExePath { get; set; } = string.Empty;
    public string CandidateClassName { get; set; } = string.Empty;
    public long CandidateProcessStartTimeUtcTicks { get; set; }
    public long RecoveryToken { get; set; }
    public string RecoveryMode { get; set; } = string.Empty;
    public string Phase { get; set; } = PendingRecoveryService.RecoveryPhase.Prepared;
    public DateTimeOffset PreparedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
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
