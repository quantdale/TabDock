using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TabDock.Services;

namespace TabDock.ValidationDriver;

/// <summary>
/// A02 storage-isolation proof protocol for process qualification. The
/// launcher creates a fresh run-owned product directory with a versioned
/// ownership marker binding canonical product root, run identity and exact
/// candidate identity, then obtains a read-only child-observed probe report
/// from the exact candidate before any mutating child is started. Proof is
/// exact-match and fail closed: the complete supported proof contract
/// (schema, types, validation-root status, storage-role set and identity
/// binding) must hold, and anything missing, malformed, stale or mismatched
/// refuses the mutating launch. Cleanup revalidates the same complete marker
/// immediately before deletion — a lost or ambiguous marker means the tree is
/// retained for review, never recursively deleted. APPDATA-only environment
/// overrides are never treated as isolation.
/// </summary>
internal static class ValidationStorageProof
{
    internal const string RootEnvironmentVariable = ProductStorage.RootEnvironmentVariable;
    internal const string RunIdEnvironmentVariable = ProductStorage.RunIdEnvironmentVariable;

    /// <summary>Stable refusal codes shared by soak, smoke and self-tests.</summary>
    internal static class Refusal
    {
        public const string ProbeFailed = "probe-failed";
        public const string ProofMissing = "proof-missing";
        public const string ProofMalformed = "proof-malformed";
        public const string ProofRefused = "proof-refused";
        public const string ProofSchemaMismatch = "proof-schema-mismatch";
        public const string ProofContractInvalid = "proof-contract-invalid";
        public const string RootMismatch = "root-mismatch";
        public const string RunIdMismatch = "run-id-mismatch";
        public const string CandidateMismatch = "candidate-mismatch";
        public const string MarkerOwnershipLost = "marker-ownership-lost";
    }

    /// <summary>Creates a fresh run-owned product directory and its ownership marker.</summary>
    internal static string CreateRunOwnedRoot(string parentRoot, string runId, string candidatePath)
    {
        string root = Path.Combine(parentRoot, "validation-product");
        if (Directory.Exists(root))
            throw new InvalidOperationException("validation root already exists; refusing to reuse a run-owned directory");
        Directory.CreateDirectory(root);
        ProductStorage.WriteRunMarker(root, runId, candidatePath);
        return Path.GetFullPath(root);
    }

    /// <summary>
    /// Runs the read-only child probe against the exact candidate and verifies
    /// its proof. Returns null on success, otherwise a stable refusal code.
    /// </summary>
    internal static string? VerifyChildProof(
        string tabDockExe,
        string runOwnedRoot,
        string runId,
        string candidatePath,
        int timeoutMs = 30_000,
        string probeArguments = "--validation-storage-probe")
    {
        if (!TryRunProbe(tabDockExe, runOwnedRoot, runId, candidatePath, out string probeOutput, timeoutMs, probeArguments))
            return Refusal.ProbeFailed;
        return VerifyProof(proofJson: probeOutput, runOwnedRoot, runId, candidatePath);
    }

    /// <summary>
    /// Pure proof verification of the COMPLETE supported contract: probe
    /// schema, field types, explicit validation-root status, the required
    /// storage-role set, and the exact root/run/candidate identity binding.
    /// Unknown schemas, incomplete proofs and getter-type mismatches are
    /// stable non-authorizing refusals, never exceptions.
    /// </summary>
    internal static string? VerifyProof(string? proofJson, string runOwnedRoot, string runId, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(proofJson))
            return Refusal.ProofMissing;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(proofJson);
        }
        catch (JsonException)
        {
            return Refusal.ProofMalformed;
        }
        if (node is not JsonObject proof)
            return Refusal.ProofMalformed;

        if (proof["ProbeSchema"] is not JsonValue schemaValue
            || !schemaValue.TryGetValue<int>(out int schemaVersion))
            return Refusal.ProofSchemaMismatch;
        if (schemaVersion != ProductStorage.MarkerSchemaVersion)
            return Refusal.ProofSchemaMismatch;

        if (proof["Status"] is not JsonValue statusValue
            || !statusValue.TryGetValue<string>(out string? status)
            || status == null)
            return Refusal.ProofMalformed;
        if (!string.Equals(status, "ok", StringComparison.Ordinal))
            return Refusal.ProofRefused;

        if (proof["IsValidationRoot"] is not JsonValue validationValue
            || !validationValue.TryGetValue<bool>(out bool isValidationRoot)
            || !isValidationRoot)
            return Refusal.ProofContractInvalid;

        if (proof["Roles"] is not JsonArray rolesArray)
            return Refusal.ProofContractInvalid;
        var reportedRoles = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? roleNode in rolesArray)
        {
            if (roleNode is not JsonValue roleValue
                || !roleValue.TryGetValue<string>(out string? role)
                || role == null)
                return Refusal.ProofContractInvalid;
            reportedRoles.Add(role);
        }
        foreach (string required in ProductStorage.StorageRoles)
        {
            if (!reportedRoles.Contains(required))
                return Refusal.ProofContractInvalid;
        }

        if (!TryReadString(proof, "EffectiveRoot", out string? effectiveRoot)
            || !TryReadString(proof, "RunId", out string? proofRunId)
            || !TryReadString(proof, "CandidatePath", out string? proofCandidate))
            return Refusal.ProofMalformed;

        if (!ProductStorage.TryVerifiedIdentity(effectiveRoot!, out string reportedRootIdentity)
            || !ProductStorage.TryVerifiedIdentity(runOwnedRoot, out string expectedRootIdentity)
            || !string.Equals(reportedRootIdentity, expectedRootIdentity, StringComparison.OrdinalIgnoreCase))
            return Refusal.RootMismatch;
        if (!string.Equals(proofRunId, runId, StringComparison.Ordinal))
            return Refusal.RunIdMismatch;
        if (!ProductStorage.TryVerifiedIdentity(proofCandidate!, out string reportedCandidateIdentity)
            || !ProductStorage.TryVerifiedIdentity(candidatePath, out string expectedCandidateIdentity)
            || !string.Equals(reportedCandidateIdentity, expectedCandidateIdentity, StringComparison.OrdinalIgnoreCase))
            return Refusal.CandidateMismatch;
        return null;
    }

    /// <summary>
    /// Run-owned cleanup: recursive deletion is authorized only while the
    /// COMPLETE supported ownership marker still binds this exact root, run
    /// and candidate identity. Incomplete, unsupported, copied, wrong-root or
    /// wrong-candidate markers all mean ownership is unproven: the tree is
    /// retained for review and the cleanup scope is never broadened.
    /// </summary>
    internal static bool TryCleanupRunOwnedRoot(
        string runOwnedRoot,
        string runId,
        string candidatePath,
        out string? refusalReason)
    {
        refusalReason = null;
        string markerPath = Path.Combine(runOwnedRoot, ProductStorage.MarkerFileName);
        try
        {
            if (!File.Exists(markerPath)
                || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
            {
                refusalReason = Refusal.MarkerOwnershipLost;
                return false;
            }
            JsonNode? node = JsonNode.Parse(File.ReadAllText(markerPath));
            if (node is not JsonObject marker
                || marker["SchemaVersion"] is not JsonValue schemaValue
                || !schemaValue.TryGetValue<int>(out int schemaVersion)
                || schemaVersion != ProductStorage.MarkerSchemaVersion
                || !TryReadString(marker, "RunId", out string? markerRunId)
                || !TryReadString(marker, "ProductRoot", out string? markerRoot)
                || !TryReadString(marker, "CandidatePath", out string? markerCandidate))
            {
                refusalReason = Refusal.MarkerOwnershipLost;
                return false;
            }
            if (!string.Equals(markerRunId, runId, StringComparison.Ordinal)
                || !ProductStorage.TryVerifiedIdentity(markerRoot!, out string markerRootIdentity)
                || !ProductStorage.TryVerifiedIdentity(runOwnedRoot, out string rootIdentity)
                || !string.Equals(markerRootIdentity, rootIdentity, StringComparison.OrdinalIgnoreCase)
                || !ProductStorage.TryVerifiedIdentity(markerCandidate!, out string markerCandidateIdentity)
                || !ProductStorage.TryVerifiedIdentity(candidatePath, out string candidateIdentity)
                || !string.Equals(markerCandidateIdentity, candidateIdentity, StringComparison.OrdinalIgnoreCase))
            {
                refusalReason = Refusal.MarkerOwnershipLost;
                return false;
            }
            // R1: the complete marker proves what was RECORDED, not where the
            // bytes live now. A component-level reparse check on the deletion
            // path (same policy as bootstrap) refuses a replaced ancestor
            // junction instead of following it into relocated evidence.
            if (ProductStorage.HasReparsePointComponent(runOwnedRoot))
            {
                refusalReason = Refusal.MarkerOwnershipLost;
                return false;
            }
            Directory.Delete(runOwnedRoot, recursive: true);
            return true;
        }
        catch (Exception)
        {
            refusalReason = Refusal.MarkerOwnershipLost;
            return false;
        }
    }

    private static bool TryReadString(JsonObject json, string name, out string? value)
    {
        value = null;
        if (json[name] is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out string? text)
            || text == null)
        {
            return false;
        }
        value = text;
        return true;
    }

    /// <summary>
    /// Bounded child probe: both pipes drain concurrently under ONE deadline.
    /// Serial synchronous draining before the wait (the previous shape) let a
    /// child holding a pipe open block far beyond the budget and deadlocked
    /// when the unread pipe filled. On timeout or error only the launched
    /// child is terminated and reaped.
    /// </summary>
    private static bool TryRunProbe(
        string tabDockExe,
        string runOwnedRoot,
        string runId,
        string candidatePath,
        out string probeOutput,
        int timeoutMs,
        string probeArguments)
    {
        probeOutput = string.Empty;
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo(tabDockExe)
            {
                Arguments = probeArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(tabDockExe) ?? Environment.CurrentDirectory,
            };
            startInfo.Environment[RootEnvironmentVariable] = runOwnedRoot;
            startInfo.Environment[RunIdEnvironmentVariable] = runId;
            process = Process.Start(startInfo);
            if (process == null)
                return false;

            var deadline = Stopwatch.StartNew();
            TimeSpan budget = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)Math.Max(1, (budget - deadline.Elapsed).TotalMilliseconds)))
            {
                TryTerminateAndReap(process);
                return false;
            }

            // The child exited; its pipes are closing. Bound the drain too: an
            // inherited handle could otherwise keep a pipe open indefinitely.
            int remainingDrainMs = (int)Math.Max(1, (budget - deadline.Elapsed).TotalMilliseconds + 1000);
            if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, remainingDrainMs))
            {
                TryTerminateAndReap(process);
                return false;
            }
            // R3: a success-looking payload from a process that reported
            // failure must never authorize the mutating launch.
            if (process.ExitCode != 0)
                return false;
            probeOutput = stdoutTask.Result;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>Terminates and reaps only the launched child on timeout/error.</summary>
    private static void TryTerminateAndReap(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: false);
        }
        catch (Exception)
        {
            // Already exited or unkillable; reaping below is best effort.
        }
        try
        {
            process.WaitForExit(5_000);
        }
        catch (Exception)
        {
        }
    }
}
