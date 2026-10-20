using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Immutable effective-product-directory authority (A02). One resolved value
/// routes every default storage role (state, backups, temps, journal, pending
/// evidence, logs, startup hygiene, diagnostic inspection) so no consumer
/// re-derives a separate known-folder path.
///
/// Qualification children select a run-owned directory with the PAIRED inputs
/// <see cref="RootEnvironmentVariable"/> and <see cref="RunIdEnvironmentVariable"/>,
/// validated against a launcher-created ownership marker inside that fresh
/// directory. The selection is an accidental-mutation guard, not
/// authentication: it binds canonical product root, run identity and exact
/// candidate identity so a stale or mismatched proof refuses to run instead of
/// silently touching production storage. Every validation failure is fail
/// closed — an invalid override NEVER falls back to the production profile,
/// and explicitly provided but unusable inputs are refused, never interpreted
/// as ordinary startup. Without any validation inputs, ordinary startup keeps
/// the normal Windows roaming-profile product directory.
///
/// Path identity for overlap/ownership decisions is established through
/// <see cref="TryVerifiedIdentity"/>: extended/device namespaces are refused,
/// and existing paths are expanded to their long form so alternate Win32
/// spellings (8.3 short names, extended prefixes) alias the same identity
/// instead of bypassing the production exclusion.
/// </summary>
internal sealed class ProductStorage
{
    internal const string RootEnvironmentVariable = "TABDOCK_VALIDATION_STATE_ROOT";
    internal const string RunIdEnvironmentVariable = "TABDOCK_VALIDATION_RUN_ID";
    internal const string MarkerFileName = "tabdock-validation-run.marker.json";
    internal const int MarkerSchemaVersion = 1;

    /// <summary>
    /// The storage roles every verified root must cover. The child probe
    /// reports this set and parent verifiers require it complete.
    /// </summary>
    internal static readonly string[] StorageRoles =
    {
        "state", "backup", "temp", "journal", "pending", "sidecar-ledger",
        "logs", "startup-hygiene", "diagnostic-inspection",
    };

    private static readonly object CurrentGate = new();
    private static ProductStorage? _current;

    // Test/qualification scope: flows only into the async context that
    // installs it, so unrelated parallel consumers can never observe it.
    private static readonly AsyncLocal<ProductStorage?> ScopedOverride = new();

    private readonly string? _effectiveDirectory;

    private ProductStorage(
        string? effectiveDirectory,
        bool isValidationRoot,
        string? runId,
        string? candidatePath,
        string? refusalReason)
    {
        _effectiveDirectory = effectiveDirectory;
        IsValidationRoot = isValidationRoot;
        RunId = runId;
        CandidatePath = candidatePath;
        RefusalReason = refusalReason;
    }

    /// <summary>Canonical product directory. Throws when the resolution was refused.</summary>
    internal string EffectiveDirectory
        => _effectiveDirectory
            ?? throw new InvalidOperationException(
                "product storage is unavailable (" + RefusalReason + "); refusing any default storage fallback");

    internal bool IsValidationRoot { get; }

    internal string? RunId { get; }

    /// <summary>Canonical candidate (process/executable) identity bound by the run marker.</summary>
    internal string? CandidatePath { get; }

    /// <summary>Null when the resolution is usable; otherwise a stable refusal code.</summary>
    internal string? RefusalReason { get; }

    internal bool IsUsable => RefusalReason == null;

    /// <summary>
    /// The process-wide authority, resolved once from the real environment.
    /// Application bootstrap calls this before any logging or service
    /// construction; unit fixtures inject explicit paths and never need it.
    /// A scoped test override is visible only inside its own async context.
    /// </summary>
    internal static ProductStorage Current
    {
        get
        {
            ProductStorage? scoped = ScopedOverride.Value;
            if (scoped != null)
                return scoped;
            lock (CurrentGate)
                return _current ??= ResolveFromEnvironment();
        }
    }

    /// <summary>
    /// Installs a scoped authority visible only to code running inside the
    /// returned scope's async context. Unrelated consumers on other
    /// execution contexts keep resolving the real environment, so tests
    /// cannot poison them with a fixture or refused root.
    /// </summary>
    internal static IDisposable OverrideForTests(ProductStorage storage)
        => new StorageOverrideScope(storage);

    private sealed class StorageOverrideScope : IDisposable
    {
        private readonly ProductStorage? _previous;
        private bool _disposed;

        public StorageOverrideScope(ProductStorage storage)
        {
            _previous = ScopedOverride.Value;
            ScopedOverride.Value = storage;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ScopedOverride.Value = _previous;
        }
    }

    internal static ProductStorage ResolveFromEnvironment(
        Func<string, string?>? environment = null,
        string? candidatePath = null,
        string? productionDirectory = null)
    {
        Func<string, string?> env = environment ?? (name => Environment.GetEnvironmentVariable(name));
        string candidate = candidatePath ?? Environment.ProcessPath ?? string.Empty;
        string production = productionDirectory ?? DefaultProductionDirectory();
        return Resolve(
            env(RootEnvironmentVariable),
            env(RunIdEnvironmentVariable),
            candidate,
            production);
    }

    internal static string DefaultProductionDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabDock");

    /// <summary>
    /// Pure resolution over explicit inputs. Total and side-effect free except
    /// for read-only path/marker inspection of the candidate root. Only BOTH
    /// inputs being absent selects ordinary production startup; every other
    /// provided-but-unusable input shape is refused with no fallback.
    /// </summary>
    internal static ProductStorage Resolve(
        string? rootInput,
        string? runIdInput,
        string candidatePath,
        string productionDirectory,
        Func<string, FileAttributes>? getAttributes = null)
    {
        Func<string, FileAttributes> attributes = getAttributes ?? File.GetAttributes;

        // Absence means the variable was never supplied (null). An explicitly
        // supplied blank/whitespace value is INVALID configuration and must be
        // refused, not collapsed into "no override" production startup.
        bool rootProvided = rootInput != null;
        bool runIdProvided = runIdInput != null;
        if (!rootProvided && !runIdProvided)
            return new ProductStorage(productionDirectory, isValidationRoot: false, runId: null, candidatePath: null, refusalReason: null);
        if (!rootProvided || !runIdProvided
            || string.IsNullOrWhiteSpace(rootInput)
            || string.IsNullOrWhiteSpace(runIdInput))
            return Refuse("incomplete-paired-inputs");

        string root = rootInput!.Trim();
        string runId = runIdInput!.Trim();
        // Extended/device namespaces are unsupported qualification inputs;
        // accepting them would admit alternate Win32 spellings of production.
        if (HasUnsupportedNamespace(root))
            return Refuse("root-not-canonical");
        if (!Path.IsPathFullyQualified(root))
            return Refuse("root-not-absolute");
        string canonicalRoot;
        try
        {
            canonicalRoot = TrimmedFullPath(root);
        }
        catch (Exception)
        {
            return Refuse("root-not-absolute");
        }
        // Canonical means the input already IS its fully resolved form;
        // normalizing both sides here would accept '.'/'..' style aliases.
        if (!string.Equals(
                canonicalRoot,
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
            return Refuse("root-not-canonical");
        if (runId.Length == 0 || runId.Length > 128 || runId.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            return Refuse("run-id-invalid");

        string canonicalProduction;
        try
        {
            canonicalProduction = TrimmedFullPath(productionDirectory);
        }
        catch (Exception)
        {
            canonicalProduction = productionDirectory;
        }

        // Overlap/alias exclusion compares verified filesystem identities, not
        // spellings. If identity cannot be established for an existing path,
        // fail closed instead of risking a production overlap.
        if (!TryVerifiedIdentity(canonicalRoot, out string rootIdentity)
            || !TryVerifiedIdentity(canonicalProduction, out string productionIdentity))
            return Refuse("identity-unverifiable");
        if (Overlaps(rootIdentity, productionIdentity))
            return Refuse("production-overlap");

        try
        {
            if (!Directory.Exists(canonicalRoot))
                return Refuse("root-unavailable");
            if (HasReparsePointComponent(canonicalRoot, attributes))
                return Refuse("reparse-point-component");
        }
        catch (Exception)
        {
            return Refuse("root-unavailable");
        }

        string markerPath = Path.Combine(canonicalRoot, MarkerFileName);
        string markerJson;
        try
        {
            if (!File.Exists(markerPath))
                return Refuse("marker-missing");
            if ((attributes(markerPath) & FileAttributes.ReparsePoint) != 0)
                return Refuse("reparse-point-component");
            markerJson = File.ReadAllText(markerPath);
        }
        catch (Exception)
        {
            return Refuse("marker-missing");
        }

        JsonNode? markerNode;
        try
        {
            markerNode = JsonNode.Parse(markerJson);
        }
        catch (JsonException)
        {
            return Refuse("marker-malformed");
        }
        if (markerNode is not JsonObject marker)
            return Refuse("marker-malformed");
        if (marker["SchemaVersion"] is not JsonValue schemaValue
            || !schemaValue.TryGetValue<int>(out int schemaVersion))
            return Refuse("marker-malformed");
        if (schemaVersion != MarkerSchemaVersion)
            return Refuse("marker-schema-unsupported");

        if (!TryReadString(marker, "RunId", out string? markerRunId)
            || !TryReadString(marker, "ProductRoot", out string? markerRoot)
            || !TryReadString(marker, "CandidatePath", out string? markerCandidate))
            return Refuse("marker-malformed");
        if (!string.Equals(markerRunId, runId, StringComparison.Ordinal))
            return Refuse("marker-run-id-mismatch");
        if (!TryVerifiedIdentity(markerRoot!, out string markerRootIdentity)
            || !string.Equals(markerRootIdentity, rootIdentity, StringComparison.OrdinalIgnoreCase))
            return Refuse("marker-root-mismatch");

        if (!TryVerifiedIdentity(candidatePath, out string candidateIdentity)
            || !TryVerifiedIdentity(markerCandidate!, out string markerCandidateIdentity)
            || !string.Equals(markerCandidateIdentity, candidateIdentity, StringComparison.OrdinalIgnoreCase))
            return Refuse("marker-candidate-mismatch");

        return new ProductStorage(canonicalRoot, isValidationRoot: true, runId, candidateIdentity, refusalReason: null);

        static ProductStorage Refuse(string reason) => new(null, false, null, null, reason);
    }

    /// <summary>
    /// Writes the versioned run-owned marker binding canonical product root,
    /// run identity and exact candidate identity. Qualification launchers call
    /// this when they create a fresh run-owned directory; persistent
    /// qualification evidence must exclude personal paths, so only the
    /// transient marker inside the disposable tree carries them.
    /// </summary>
    internal static void WriteRunMarker(string root, string runId, string candidatePath)
    {
        var marker = new JsonObject
        {
            ["SchemaVersion"] = MarkerSchemaVersion,
            ["RunId"] = runId,
            ["ProductRoot"] = TrimmedFullPath(root),
            ["CandidatePath"] = TrimmedFullPath(candidatePath),
            ["CreatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        File.WriteAllText(
            Path.Combine(root, MarkerFileName),
            marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Machine-readable probe report: effective root, run identity, candidate
    /// identity and covered storage roles, or a stable refusal code.
    /// </summary>
    internal string FormatProbeReport()
    {
        var report = new JsonObject
        {
            ["ProbeSchema"] = MarkerSchemaVersion,
            ["Status"] = IsUsable ? "ok" : "refused",
        };
        if (IsUsable)
        {
            report["EffectiveRoot"] = EffectiveDirectory;
            report["RunId"] = RunId;
            report["CandidatePath"] = CandidatePath;
            report["IsValidationRoot"] = IsValidationRoot;
            var roles = new JsonArray();
            foreach (string role in StorageRoles)
                roles.Add(role);
            report["Roles"] = roles;
        }
        else
        {
            report["Reason"] = RefusalReason;
        }
        return report.ToJsonString();
    }

    /// <summary>
    /// Establishes the verified filesystem identity used for overlap,
    /// ownership and marker comparisons. Extended/device namespaces are
    /// rejected outright; existing paths expand to their long form so 8.3
    /// short names and other alternate Win32 spellings converge on one
    /// identity. Identity cannot be established (false) is a fail-closed
    /// outcome: callers must refuse rather than compare spellings.
    /// </summary>
    internal static bool TryVerifiedIdentity(string path, out string identity)
    {
        identity = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string trimmed = path.Trim();
            if (HasUnsupportedNamespace(trimmed))
                return false;
            if (!Path.IsPathFullyQualified(trimmed))
                return false;
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
            if (File.Exists(full) || Directory.Exists(full))
            {
                string? longPath = GetLongPathNameOrNull(full);
                if (longPath == null)
                    return false;
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(longPath));
            }
            identity = full;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool HasUnsupportedNamespace(string path)
    {
        string trimmed = path.TrimStart();
        return trimmed.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("//?/", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("//./", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLongPathNameOrNull(string path)
    {
        StringBuilder buffer = new(4096);
        uint length = NativeMethods.GetLongPathName(path, buffer, (uint)buffer.Capacity);
        if (length == 0)
            return null;
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = NativeMethods.GetLongPathName(path, buffer, (uint)buffer.Capacity);
            if (length == 0 || length >= buffer.Capacity)
                return null;
        }
        return buffer.ToString();
    }

    private static bool TryReadString(JsonObject marker, string name, out string? value)
    {
        value = null;
        if (marker[name] is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out string? text)
            || text == null)
        {
            return false;
        }
        value = text;
        return true;
    }

    private static bool TryCanonical(string path, out string canonical)
    {
        canonical = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
                return false;
            canonical = TrimmedFullPath(path.Trim());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool HasReparsePointComponent(string path, Func<string, FileAttributes>? getAttributes = null)
    {
        Func<string, FileAttributes> attributes = getAttributes ?? File.GetAttributes;
        string current = path;
        while (true)
        {
            if ((attributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)
                || string.Equals(
                    TrimmedFullPath(parent),
                    TrimmedFullPath(current),
                    StringComparison.OrdinalIgnoreCase))
                return false;
            current = parent;
        }
    }

    private static bool Overlaps(string leftIdentity, string rightIdentity)
        => string.Equals(leftIdentity, rightIdentity, StringComparison.OrdinalIgnoreCase)
            || IsUnder(leftIdentity, rightIdentity)
            || IsUnder(rightIdentity, leftIdentity);

    private static bool IsUnder(string path, string ancestor)
    {
        string relative = Path.GetRelativePath(ancestor, path);
        return relative != "."
            && !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private static string TrimmedFullPath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
