using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabDock.ValidationDriver;

internal sealed record ManifestArtifactReference(
    string RelativePath,
    string Kind,
    string Sha256,
    bool Exists);

internal sealed record ImportedScenarioEvidence(
    string Scenario,
    int Attempt,
    ScenarioOutcome Outcome,
    string? JsonArtifact,
    string? JUnitArtifact,
    string? TimelineArtifact,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc);

internal sealed record ChildManifestVerification(
    string ExpectedShard,
    string? RunId,
    string? ManifestPath,
    string? ChildRelativeDirectory,
    string ManifestSha256,
    int ExitCode,
    bool Valid,
    ScenarioOutcome Outcome,
    string? FailureReason,
    IReadOnlyList<ImportedScenarioEvidence> Scenarios,
    IReadOnlyList<ManifestArtifactReference> Artifacts)
{
    public static ChildManifestVerification Invalid(
        string shard,
        int exitCode,
        string reason,
        string? manifestPath = null,
        string? childRelativeDirectory = null,
        string? manifestSha256 = null)
        => new(
            shard,
            null,
            manifestPath,
            childRelativeDirectory,
            manifestSha256 ?? "MISSING",
            exitCode,
            false,
            new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, reason),
            reason,
            Array.Empty<ImportedScenarioEvidence>(),
            Array.Empty<ManifestArtifactReference>());
}

internal sealed record ParentManifestWriteResult(
    ScenarioOutcome Outcome,
    string ManifestPath,
    IReadOnlyList<string> Errors);

/// <summary>
/// Verifies versioned ValidationDriver manifests from disk. This verifier is
/// native-free and treats child output as untrusted data; process exit codes
/// are only a cross-check against the manifest outcome.
/// </summary>
internal static class QualificationManifestVerifier
{
    public const int CurrentSchemaVersion = 2;

    public static ChildManifestVerification ImportChild(
        string childRoot,
        string parentRoot,
        string expectedShard,
        string expectedParentRunId,
        string expectedCandidateSha,
        string expectedCandidateExecutableSha,
        string expectedDriverSha,
        DateTimeOffset parentStartedUtc,
        int exitCode)
    {
        string[] runDirectories;
        try
        {
            runDirectories = Directory.Exists(childRoot)
                ? Directory.GetDirectories(childRoot)
                    .Where(directory => File.Exists(Path.Combine(directory, "run-manifest.json")))
                    .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            return ChildManifestVerification.Invalid(expectedShard, exitCode, $"child artifact directory enumeration failed: {ex.GetType().Name}");
        }

        if (runDirectories.Length != 1)
        {
            return ChildManifestVerification.Invalid(
                expectedShard,
                exitCode,
                runDirectories.Length == 0
                    ? "child manifest is missing"
                    : $"child manifest directory count is {runDirectories.Length}, expected exactly one");
        }

        string runDirectory = runDirectories[0];
        string manifestPath = Path.Combine(runDirectory, "run-manifest.json");
        string relativeChildDirectory = RelativePath(parentRoot, runDirectory);
        string manifestSha = Sha256File(manifestPath);
        var errors = new List<string>();
        JsonDocument? document = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(manifestPath);
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            FindDuplicateProperties(document.RootElement, "$", errors);
        }
        catch (Exception ex)
        {
            errors.Add($"manifest JSON is malformed: {ex.GetType().Name}");
        }

        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            errors.Add("manifest root must be an object");

        string? runId = ReadString(document?.RootElement, "runId", errors);
        int schema = ReadInt(document?.RootElement, "schemaVersion", errors);
        string? runKind = ReadString(document?.RootElement, "runKind", errors);
        string? manifestShard = ReadString(document?.RootElement, "shard", errors);
        string? parentRunId = ReadString(document?.RootElement, "parentRunId", errors);
        string? candidateSha = ReadString(document?.RootElement, "candidateSha", errors);
        string? catalogGeneration = ReadString(document?.RootElement, "catalogGeneration", errors);
        string? startedText = ReadString(document?.RootElement, "startedUtc", errors);
        string? endedText = ReadString(document?.RootElement, "endedUtc", errors);
        string? outcomeText = ReadString(document?.RootElement, "outcome", errors);
        ScenarioOutcomeKind manifestOutcomeKind = ScenarioOutcomeKind.FailHarness;
        if (outcomeText != null && !ScenarioOutcomeContract.TryParse(outcomeText, out manifestOutcomeKind))
            errors.Add($"manifest outcome '{outcomeText}' is not in the canonical vocabulary");

        if (schema != CurrentSchemaVersion)
            errors.Add($"schemaVersion={schema}, expected {CurrentSchemaVersion}");
        if (!string.Equals(runKind, "shard", StringComparison.Ordinal))
            errors.Add($"runKind='{runKind ?? "<missing>"}', expected shard");
        if (!string.Equals(manifestShard, expectedShard, StringComparison.Ordinal))
            errors.Add($"shard='{manifestShard ?? "<missing>"}', expected '{expectedShard}'");
        if (!string.Equals(parentRunId, expectedParentRunId, StringComparison.Ordinal))
            errors.Add("parent run identity does not match the all-run parent");
        if (!string.Equals(candidateSha, expectedCandidateSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("candidate source SHA does not match the parent candidate");
        if (!string.Equals(catalogGeneration, ScenarioCatalog.Generation, StringComparison.Ordinal))
            errors.Add("scenario catalog generation does not match the current driver");

        JsonElement executableSha = Child(document?.RootElement, "executableSha256");
        string? candidateExecutableSha = ReadString(executableSha, "candidate", errors);
        if (!string.Equals(candidateExecutableSha, expectedCandidateExecutableSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("candidate executable SHA does not match the parent candidate");

        JsonElement driverIdentity = Child(document?.RootElement, "driverIdentity");
        string? driverSha = ReadString(driverIdentity, "sha256", errors);
        if (!string.Equals(driverSha, expectedDriverSha, StringComparison.OrdinalIgnoreCase))
            errors.Add("driver executable SHA does not match the parent driver");

        DateTimeOffset started = ReadTimestamp(startedText, "startedUtc", errors);
        DateTimeOffset ended = ReadTimestamp(endedText, "endedUtc", errors);
        if (ended < started)
            errors.Add("endedUtc precedes startedUtc");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (started > now.AddMinutes(5) || ended > now.AddMinutes(5))
            errors.Add("manifest timestamp is materially in the future");
        if (started < parentStartedUtc.AddMinutes(-5))
            errors.Add("manifest timestamp is stale relative to the parent run");

        var scenarios = new List<ImportedScenarioEvidence>();
        JsonElement scenarioArray = Child(document?.RootElement, "scenarios");
        if (scenarioArray.ValueKind != JsonValueKind.Array)
        {
            errors.Add("scenarios must be an array");
        }
        else
        {
            foreach (JsonElement scenarioElement in scenarioArray.EnumerateArray())
            {
                string? scenario = ReadString(scenarioElement, "scenario", errors);
                int attempt = ReadInt(scenarioElement, "attempt", errors);
                string? result = ReadString(scenarioElement, "result", errors);
                DateTimeOffset scenarioStarted = ReadTimestamp(
                    ReadString(scenarioElement, "startedUtc", errors), "scenario.startedUtc", errors);
                DateTimeOffset scenarioEnded = ReadTimestamp(
                    ReadString(scenarioElement, "endedUtc", errors), "scenario.endedUtc", errors);
                if (scenarioEnded < scenarioStarted)
                    errors.Add($"scenario '{scenario ?? "<missing>"}' ends before it starts");
                if (string.IsNullOrWhiteSpace(scenario) || !ScenarioOutcomeContract.TryParse(result ?? string.Empty, out ScenarioOutcomeKind resultKind))
                {
                    errors.Add("scenario entry has an invalid ID or result");
                    continue;
                }

                JsonElement? reasonElement = TryGet(scenarioElement, "reason");
                string? reason = reasonElement is { ValueKind: JsonValueKind.String }
                    ? reasonElement.Value.GetString()
                    : null;
                scenarios.Add(new ImportedScenarioEvidence(
                    scenario,
                    attempt,
                    new ScenarioOutcome(resultKind, reason),
                    ReadString(scenarioElement, "jsonArtifact", errors),
                    ReadString(scenarioElement, "junitArtifact", errors),
                    ReadString(scenarioElement, "timelineArtifact", errors),
                    scenarioStarted,
                    scenarioEnded));
            }
        }

        IReadOnlyList<string> expectedScenarios = ScenarioCatalog.GetShardScenarios(expectedShard);
        HashSet<string> actualScenarioIds = scenarios.Select(item => item.Scenario).ToHashSet(StringComparer.Ordinal);
        HashSet<string> expectedScenarioIds = expectedScenarios.ToHashSet(StringComparer.Ordinal);
        foreach (string missing in expectedScenarioIds.Except(actualScenarioIds, StringComparer.Ordinal))
            errors.Add($"child shard is missing catalog scenario '{missing}'");
        foreach (string unexpected in actualScenarioIds.Except(expectedScenarioIds, StringComparer.Ordinal))
            errors.Add($"child shard contains scenario '{unexpected}' not assigned to '{expectedShard}'");
        var attemptKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ImportedScenarioEvidence scenario in scenarios)
        {
            if (scenario.Attempt < 1 || !attemptKeys.Add(scenario.Scenario + "#" + scenario.Attempt.ToString(CultureInfo.InvariantCulture)))
                errors.Add($"duplicate or invalid attempt for scenario '{scenario.Scenario}'");
        }

        IReadOnlyList<ManifestArtifactReference> artifacts = ReadAndVerifyArtifacts(
            document?.RootElement,
            runDirectory,
            scenarios,
            errors);

        ScenarioOutcome derivedOutcome = DeriveOutcome(scenarios, errors);
        if (!string.Equals(ScenarioOutcomeContract.Code(derivedOutcome.Kind), outcomeText, StringComparison.Ordinal))
            errors.Add($"manifest outcome '{outcomeText}' disagrees with scenario aggregation '{derivedOutcome.Code}'");
        if (exitCode != ScenarioOutcomeContract.ExitCode(manifestOutcomeKind))
            errors.Add($"child exit code {exitCode} disagrees with manifest outcome '{outcomeText}'");

        document?.Dispose();
        bool valid = errors.Count == 0;
        ScenarioOutcome outcome = valid
            ? derivedOutcome
            : new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, string.Join("; ", errors));
        return new ChildManifestVerification(
            expectedShard,
            runId,
            manifestPath,
            relativeChildDirectory,
            manifestSha,
            exitCode,
            valid,
            outcome,
            valid ? null : string.Join("; ", errors),
            scenarios,
            artifacts);
    }

    public static bool VerifyParent(string parentManifestPath, out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(parentManifestPath));
            FindDuplicateProperties(document.RootElement, "$", failures);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                failures.Add("parent manifest root must be an object");
            if (ReadInt(document.RootElement, "schemaVersion", failures) != CurrentSchemaVersion)
                failures.Add("parent schema version is unsupported");
            if (!string.Equals(ReadString(document.RootElement, "runKind", failures), "all", StringComparison.Ordinal))
                failures.Add("parent run kind is not all");
            if (!string.Equals(ReadString(document.RootElement, "catalogGeneration", failures), ScenarioCatalog.Generation, StringComparison.Ordinal))
                failures.Add("parent catalog generation is not current");
            string? parentRunId = ReadString(document.RootElement, "runId", failures);
            string? candidateSha = ReadString(document.RootElement, "candidateSha", failures);
            DateTimeOffset parentStartedUtc = ReadTimestamp(
                ReadString(document.RootElement, "startedUtc", failures), "startedUtc", failures);
            JsonElement executableSha = Child(document.RootElement, "executableSha256");
            string? candidateExecutableSha = ReadString(executableSha, "candidate", failures);
            JsonElement driverIdentity = Child(document.RootElement, "driverIdentity");
            string? driverSha = ReadString(driverIdentity, "sha256", failures);
            JsonElement childManifests = Child(document.RootElement, "childManifests");
            if (childManifests.ValueKind != JsonValueKind.Array)
                failures.Add("parent childManifests must be an array");
            else
            {
                HashSet<string> shards = new(StringComparer.Ordinal);
                string parentRoot = Path.GetDirectoryName(parentManifestPath) ?? string.Empty;
                foreach (JsonElement child in childManifests.EnumerateArray())
                {
                    string? shard = ReadString(child, "shard", failures);
                    bool verified = ReadBool(child, "verified", failures);
                    if (shard != null && !shards.Add(shard))
                        failures.Add($"parent contains duplicate shard '{shard}'");
                    if (!verified)
                        failures.Add($"parent contains unverified shard '{shard ?? "<missing>"}'");
                    string? path = ReadString(child, "manifestPath", failures);
                    string? expectedManifestSha = ReadString(child, "manifestSha256", failures);
                    string? childRunId = ReadString(child, "runId", failures);
                    string? outcomeText = ReadString(child, "outcome", failures);
                    int exitCode = ReadInt(child, "exitCode", failures);
                    string? childPath = null;
                    string pathError = "path is missing";
                    bool pathResolved = path != null
                        && TryResolveRelative(parentManifestPath, path, out childPath, out pathError);
                    if (!pathResolved)
                        failures.Add($"child manifest path invalid: {pathError}");
                    else if (!File.Exists(childPath))
                        failures.Add($"child manifest missing: {path}");
                    else
                    {
                        string actualManifestSha = Sha256File(childPath!);
                        if (!string.Equals(expectedManifestSha, actualManifestSha, StringComparison.OrdinalIgnoreCase))
                            failures.Add($"child manifest hash mismatch for '{path}'");

                        string? childRunDirectory = Path.GetDirectoryName(childPath);
                        string? childRoot = childRunDirectory == null
                            ? null
                            : Directory.GetParent(childRunDirectory)?.FullName;
                        if (shard != null
                            && parentRunId != null
                            && candidateSha != null
                            && candidateExecutableSha != null
                            && driverSha != null
                            && childRoot != null)
                        {
                            ChildManifestVerification imported = ImportChild(
                                childRoot,
                                parentRoot,
                                shard,
                                parentRunId,
                                candidateSha,
                                candidateExecutableSha,
                                driverSha,
                                parentStartedUtc,
                                exitCode);
                            if (!imported.Valid)
                                failures.Add($"child manifest '{path}' failed re-verification: {imported.FailureReason}");
                            if (!string.Equals(imported.RunId, childRunId, StringComparison.Ordinal))
                                failures.Add($"child manifest run identity disagrees for '{path}'");
                            if (!string.Equals(imported.Outcome.Code, outcomeText, StringComparison.Ordinal))
                                failures.Add($"child manifest outcome disagrees for '{path}'");
                            if (imported.Valid != verified)
                                failures.Add($"child manifest verification flag disagrees for '{path}'");
                        }
                    }
                }

                foreach (string expected in ScenarioCatalog.OrchestratedShardNames)
                {
                    if (!shards.Contains(expected))
                        failures.Add($"parent is missing declared shard '{expected}'");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"parent manifest parse failed: {ex.GetType().Name}");
        }

        errors = failures;
        return failures.Count == 0;
    }

    private static ScenarioOutcome DeriveOutcome(
        IReadOnlyList<ImportedScenarioEvidence> scenarios,
        List<string> errors)
    {
        if (scenarios.Count == 0)
        {
            errors.Add("child manifest contains no scenario attempts");
            return new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, "no scenario result was recorded");
        }

        var finals = scenarios
            .GroupBy(item => item.Scenario, StringComparer.Ordinal)
            .Select(group => new ScenarioAggregate(
                group.Key,
                group.OrderBy(item => item.Attempt)
                    .Select(item => new ScenarioAttempt(item.Scenario, item.Attempt, item.Outcome))
                    .ToArray()).FinalOutcome)
            .ToArray();
        return ScenarioOutcomeContract.Aggregate(finals);
    }

    private static IReadOnlyList<ManifestArtifactReference> ReadAndVerifyArtifacts(
        JsonElement? root,
        string runDirectory,
        IReadOnlyList<ImportedScenarioEvidence> scenarios,
        List<string> errors)
    {
        var references = new List<ManifestArtifactReference>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        JsonElement index = Child(root, "artifactIndex");
        if (index.ValueKind != JsonValueKind.Array)
        {
            errors.Add("artifactIndex must be an array");
            return references;
        }

        foreach (JsonElement element in index.EnumerateArray())
        {
            string? rawPath = ReadString(element, "relativePath", errors);
            string? kind = ReadString(element, "kind", errors);
            string? expectedHash = ReadString(element, "sha256", errors);
            bool exists = ReadBool(element, "exists", errors);
            if (!TryNormalizeRelativePath(rawPath, out string? normalized, out string pathError))
            {
                errors.Add($"artifact path invalid: {pathError}");
                continue;
            }
            if (!paths.Add(normalized!))
            {
                errors.Add($"artifact path is duplicated: {normalized}");
                continue;
            }

            if (!TryResolveRelative(runDirectory, normalized!, out string? fullPath, out pathError))
            {
                errors.Add($"artifact path escapes child root: {pathError}");
                continue;
            }
            bool actualExists = File.Exists(fullPath);
            string actualHash = actualExists ? Sha256File(fullPath!) : "MISSING";
            if (exists != actualExists || !string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                errors.Add($"artifact hash/existence mismatch for '{normalized}'");
            references.Add(new ManifestArtifactReference(normalized!, kind ?? "unknown", expectedHash ?? "MISSING", exists));
        }

        foreach (ImportedScenarioEvidence scenario in scenarios)
        {
            RequireReferencedArtifact(scenario.JsonArtifact, "scenario-result", paths, errors);
            RequireReferencedArtifact(scenario.JUnitArtifact, "junit", paths, errors);
            RequireReferencedArtifact(scenario.TimelineArtifact, "timeline", paths, errors);
        }
        return references;
    }

    private static void RequireReferencedArtifact(
        string? rawPath,
        string kind,
        HashSet<string> paths,
        List<string> errors)
    {
        if (!TryNormalizeRelativePath(rawPath, out string? normalized, out string error))
        {
            errors.Add($"{kind} artifact reference invalid: {error}");
            return;
        }
        if (!paths.Contains(normalized!))
            errors.Add($"{kind} artifact '{normalized}' is not in artifactIndex");
    }

    private static JsonElement Child(JsonElement? element, string property)
    {
        if (element is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty(property, out JsonElement child))
            return child;
        return default;
    }

    private static JsonElement? TryGet(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
            ? value
            : null;

    private static string? ReadString(JsonElement? element, string property, List<string> errors)
    {
        JsonElement child = Child(element, property);
        if (child.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{property} must be a string");
            return null;
        }
        return child.GetString();
    }

    private static int ReadInt(JsonElement? element, string property, List<string> errors)
    {
        JsonElement child = Child(element, property);
        if (child.ValueKind != JsonValueKind.Number || !child.TryGetInt32(out int value))
        {
            errors.Add($"{property} must be an integer");
            return -1;
        }
        return value;
    }

    private static bool ReadBool(JsonElement element, string property, List<string> errors)
    {
        JsonElement child = Child(element, property);
        if (child.ValueKind != JsonValueKind.True && child.ValueKind != JsonValueKind.False)
        {
            errors.Add($"{property} must be a boolean");
            return false;
        }
        return child.GetBoolean();
    }

    private static DateTimeOffset ReadTimestamp(string? value, string property, List<string> errors)
    {
        if (value == null || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp))
        {
            errors.Add($"{property} must be an ISO-8601 timestamp");
            return DateTimeOffset.MinValue;
        }
        return timestamp;
    }

    private static void FindDuplicateProperties(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    errors.Add($"duplicate JSON property '{path}.{property.Name}'");
                FindDuplicateProperties(property.Value, path + "." + property.Name, errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement child in element.EnumerateArray())
                FindDuplicateProperties(child, path + "[" + (index++).ToString(CultureInfo.InvariantCulture) + "]", errors);
        }
    }

    public static bool TryNormalizeRelativePath(string? rawPath, out string? normalized, out string error)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "path is empty";
            return false;
        }

        string candidate = rawPath.Replace('\\', '/');
        if (candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.Contains(":", StringComparison.Ordinal))
        {
            error = "path is absolute";
            return false;
        }

        string[] segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            error = "path contains an empty or traversal segment";
            return false;
        }
        normalized = string.Join('/', segments);
        error = string.Empty;
        return true;
    }

    private static bool TryResolveRelative(
        string rootOrManifestPath,
        string relativePath,
        out string? fullPath,
        out string error)
    {
        string root = File.Exists(rootOrManifestPath)
            ? Path.GetDirectoryName(rootOrManifestPath) ?? string.Empty
            : rootOrManifestPath;
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            fullPath = null;
            error = "resolved path leaves the bundle root";
            return false;
        }
        fullPath = candidate;
        error = string.Empty;
        return true;
    }

    public static string RelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Sha256File(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

/// <summary>Writes the parent evidence record for a bounded all-run.</summary>
internal static class QualificationParentManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ParentManifestWriteResult Write(
        string parentRoot,
        string parentRunId,
        DateTimeOffset startedUtc,
        string candidateSha,
        string candidateExecutableSha,
        string driverSha,
        IReadOnlyList<string> expectedShards,
        IReadOnlyList<ChildManifestVerification> imports)
    {
        var errors = new List<string>();
        foreach (string expected in expectedShards)
        {
            if (!imports.Any(item => string.Equals(item.ExpectedShard, expected, StringComparison.Ordinal)))
                errors.Add($"parent is missing declared shard '{expected}'");
        }

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ChildManifestVerification import in imports.Where(item => item.Valid))
        {
            foreach (string scenario in import.Scenarios.Select(item => item.Scenario).Distinct(StringComparer.Ordinal))
            {
                if (owners.TryGetValue(scenario, out string? owner))
                    errors.Add($"scenario '{scenario}' appears in shards '{owner}' and '{import.ExpectedShard}'");
                else
                    owners[scenario] = import.ExpectedShard;
            }
        }

        var finalScenarios = imports
            .Where(item => item.Valid)
            .SelectMany(item => item.Scenarios)
            .GroupBy(item => item.Scenario, StringComparer.Ordinal)
            .Select(group => new
            {
                scenario = group.Key,
                shard = imports.First(item => item.Scenarios.Any(scenario => scenario.Scenario == group.Key)).ExpectedShard,
                first = group.OrderBy(item => item.Attempt).First().Outcome.Code,
                final = new ScenarioAggregate(
                    group.Key,
                    group.OrderBy(item => item.Attempt)
                        .Select(item => new ScenarioAttempt(item.Scenario, item.Attempt, item.Outcome))
                        .ToArray()).FinalOutcome.Code,
                attempts = group.OrderBy(item => item.Attempt).Select(item => new
                {
                    attempt = item.Attempt,
                    result = item.Outcome.Code,
                    reason = item.Outcome.Reason,
                }).ToArray(),
            })
            .OrderBy(item => item.scenario, StringComparer.Ordinal)
            .ToArray();

        var shardOutcomes = imports.Select(item =>
            item.Valid
                ? item.Outcome
                : new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, item.FailureReason ?? "child manifest invalid"))
            .ToArray();
        ScenarioOutcome outcome = ScenarioOutcomeContract.Aggregate(shardOutcomes);
        if (errors.Count != 0)
            outcome = new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, string.Join("; ", errors));

        var aggregateCounts = Enum.GetValues<ScenarioOutcomeKind>()
            .ToDictionary(
                kind => ScenarioOutcomeContract.Code(kind),
                kind => finalScenarios.Count(item => string.Equals(item.final, ScenarioOutcomeContract.Code(kind), StringComparison.Ordinal)),
                StringComparer.Ordinal);
        var shardCounts = Enum.GetValues<ScenarioOutcomeKind>()
            .ToDictionary(
                kind => ScenarioOutcomeContract.Code(kind),
                kind => shardOutcomes.Count(item => item.Kind == kind),
                StringComparer.Ordinal);
        var attemptCounts = Enum.GetValues<ScenarioOutcomeKind>()
            .ToDictionary(
                kind => ScenarioOutcomeContract.Code(kind),
                kind => imports.SelectMany(item => item.Scenarios).Count(item => item.Outcome.Kind == kind),
                StringComparer.Ordinal);

        var artifactIndex = new List<object>();
        var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
        var childRecords = new List<object>();
        foreach (ChildManifestVerification import in imports)
        {
            string manifestPath = import.ManifestPath == null
                ? $"children/{import.ExpectedShard}/<missing>/run-manifest.json"
                : QualificationManifestVerifier.RelativePath(parentRoot, import.ManifestPath);
            childRecords.Add(new
            {
                shard = import.ExpectedShard,
                runId = import.RunId,
                verified = import.Valid,
                outcome = import.Outcome.Code,
                reason = import.FailureReason,
                exitCode = import.ExitCode,
                manifestPath,
                manifestSha256 = import.ManifestSha256,
                scenarioCount = import.Scenarios.Select(item => item.Scenario).Distinct(StringComparer.Ordinal).Count(),
                attemptCount = import.Scenarios.Count,
            });
            if (import.ManifestPath != null)
                AddArtifact(manifestPath, "child-manifest", import.ManifestSha256, true);
            if (import.ChildRelativeDirectory != null)
            {
                foreach (ManifestArtifactReference artifact in import.Artifacts)
                {
                    string parentPath = import.ChildRelativeDirectory + "/" + artifact.RelativePath;
                    AddArtifact(parentPath, artifact.Kind, artifact.Sha256, artifact.Exists);
                }
            }
        }

        var scenarios = imports
            .Where(item => item.Valid)
            .SelectMany(import => import.Scenarios.Select(scenario => new
            {
                shard = import.ExpectedShard,
                childManifestPath = QualificationManifestVerifier.RelativePath(parentRoot, import.ManifestPath!),
                scenario = scenario.Scenario,
                attempt = scenario.Attempt,
                result = scenario.Outcome.Code,
                reason = scenario.Outcome.Reason,
                jsonArtifact = import.ChildRelativeDirectory + "/" + scenario.JsonArtifact,
                junitArtifact = import.ChildRelativeDirectory + "/" + scenario.JUnitArtifact,
                timelineArtifact = import.ChildRelativeDirectory + "/" + scenario.TimelineArtifact,
                startedUtc = scenario.StartedUtc,
                endedUtc = scenario.EndedUtc,
            }))
            .OrderBy(item => item.scenario, StringComparer.Ordinal)
            .ThenBy(item => item.attempt)
            .ToArray();

        var manifest = new
        {
            schemaVersion = QualificationManifestVerifier.CurrentSchemaVersion,
            runKind = "all",
            runId = parentRunId,
            parentRunId = (string?)null,
            manifestRelativePath = "run-manifest.json",
            catalogGeneration = ScenarioCatalog.Generation,
            candidateSha,
            startedUtc,
            endedUtc = DateTimeOffset.UtcNow,
            environment = new
            {
                os = Environment.OSVersion.VersionString,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            },
            outcome = outcome.Code,
            outcomeReason = outcome.Reason,
            aggregateCounts,
            shardOutcomeCounts = shardCounts,
            attemptCounts,
            executableSha256 = new
            {
                candidate = candidateExecutableSha,
                test = driverSha,
            },
            driverIdentity = new
            {
                fileName = "TabDock.ValidationDriver",
                sha256 = driverSha,
            },
            expectedShards,
            childManifests = childRecords.ToArray(),
            scenarios,
            scenarioAggregates = finalScenarios,
            artifactIndex = artifactIndex.ToArray(),
            syntheticTopology = false,
        };

        string manifestPathOutput = Path.Combine(parentRoot, "run-manifest.json");
        Directory.CreateDirectory(parentRoot);
        File.WriteAllText(manifestPathOutput, JsonSerializer.Serialize(manifest, JsonOptions));
        return new ParentManifestWriteResult(outcome, manifestPathOutput, errors);

        void AddArtifact(string relativePath, string kind, string sha256, bool exists)
        {
            if (!artifactPaths.Add(relativePath))
                return;
            artifactIndex.Add(new { relativePath, kind, sha256, exists });
        }
    }
}
