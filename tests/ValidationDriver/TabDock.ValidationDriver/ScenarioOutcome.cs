using System;
using System.Collections.Generic;
using System.Linq;

namespace TabDock.ValidationDriver;

/// <summary>
/// Canonical qualification result categories. These values are serialized in
/// uppercase form and are the only scenario status vocabulary.
/// </summary>
internal enum ScenarioOutcomeKind
{
    Pass,
    FailProduct,
    FailHarness,
    BlockedEnvironment,
    BlockedSupervised,
    BlockedCapability,
    SkipCapability,
    FlakeUnclassified,
}

/// <summary>A typed scenario outcome with a stable result code and reason.</summary>
internal readonly record struct ScenarioOutcome(ScenarioOutcomeKind Kind, string? Reason = null)
{
    public string Code => ScenarioOutcomeContract.Code(Kind);

    public bool IsReleasePass => Kind == ScenarioOutcomeKind.Pass;

    public static ScenarioOutcome Pass => new(ScenarioOutcomeKind.Pass);
}

/// <summary>
/// Owns the single mapping between outcome values and all external result
/// formats. Keeping this mapping here prevents console, JSON, JUnit, and exit
/// code semantics from drifting apart.
/// </summary>
internal static class ScenarioOutcomeContract
{
    public static string Code(ScenarioOutcomeKind kind)
        => kind switch
        {
            ScenarioOutcomeKind.Pass => "PASS",
            ScenarioOutcomeKind.FailProduct => "FAIL_PRODUCT",
            ScenarioOutcomeKind.FailHarness => "FAIL_HARNESS",
            ScenarioOutcomeKind.BlockedEnvironment => "BLOCKED_ENVIRONMENT",
            ScenarioOutcomeKind.BlockedSupervised => "BLOCKED_SUPERVISED",
            ScenarioOutcomeKind.BlockedCapability => "BLOCKED_CAPABILITY",
            ScenarioOutcomeKind.SkipCapability => "SKIP_CAPABILITY",
            ScenarioOutcomeKind.FlakeUnclassified => "FLAKE_UNCLASSIFIED",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>
    /// Maps an individual scenario outcome to a stable process result. Zero is
    /// reserved for a true PASS; blocked/skipped outcomes are intentionally
    /// nonzero so a release report cannot silently treat them as green.
    /// </summary>
    public static int ExitCode(ScenarioOutcomeKind kind)
        => kind switch
        {
            ScenarioOutcomeKind.Pass => 0,
            ScenarioOutcomeKind.SkipCapability => 10,
            ScenarioOutcomeKind.BlockedCapability => 11,
            ScenarioOutcomeKind.BlockedEnvironment => 12,
            ScenarioOutcomeKind.BlockedSupervised => 13,
            ScenarioOutcomeKind.FailProduct => 20,
            ScenarioOutcomeKind.FailHarness => 21,
            ScenarioOutcomeKind.FlakeUnclassified => 22,
            _ => 23,
        };

    /// <summary>Rehydrates a child-process result for shard aggregation.</summary>
    public static ScenarioOutcomeKind FromExitCode(int exitCode)
        => exitCode switch
        {
            0 => ScenarioOutcomeKind.Pass,
            10 => ScenarioOutcomeKind.SkipCapability,
            11 => ScenarioOutcomeKind.BlockedCapability,
            12 => ScenarioOutcomeKind.BlockedEnvironment,
            13 => ScenarioOutcomeKind.BlockedSupervised,
            20 => ScenarioOutcomeKind.FailProduct,
            21 => ScenarioOutcomeKind.FailHarness,
            22 => ScenarioOutcomeKind.FlakeUnclassified,
            _ => ScenarioOutcomeKind.FailHarness,
        };

    /// <summary>Returns the JUnit failure and skipped counts for one outcome.</summary>
    public static (int Failures, int Skipped) JUnitCounts(ScenarioOutcomeKind kind)
        => kind switch
        {
            ScenarioOutcomeKind.FailProduct
                or ScenarioOutcomeKind.FailHarness
                or ScenarioOutcomeKind.FlakeUnclassified => (1, 0),
            ScenarioOutcomeKind.Pass => (0, 0),
            _ => (0, 1),
        };

    /// <summary>Returns true only for an outcome eligible to count as release PASS.</summary>
    public static bool IsReleasePass(ScenarioOutcomeKind kind)
        => kind == ScenarioOutcomeKind.Pass;

    /// <summary>
    /// Deterministically reduces attempt outcomes by severity. A valid product
    /// or harness failure is never hidden by a later pass; a flake remains
    /// visible even when the later attempt succeeds.
    /// </summary>
    public static ScenarioOutcome Aggregate(IEnumerable<ScenarioOutcome> outcomes)
    {
        ScenarioOutcome[] items = outcomes.ToArray();
        if (items.Length == 0)
            return ScenarioOutcome.Pass;

        ScenarioOutcome[] failures = items.Where(item =>
            item.Kind is ScenarioOutcomeKind.FailProduct
                or ScenarioOutcomeKind.FailHarness
                or ScenarioOutcomeKind.FlakeUnclassified).ToArray();
        if (failures.Length != 0)
            return failures[0];

        ScenarioOutcome[] blocked = items.Where(item =>
            item.Kind is ScenarioOutcomeKind.BlockedEnvironment
                or ScenarioOutcomeKind.BlockedSupervised
                or ScenarioOutcomeKind.BlockedCapability).ToArray();
        if (blocked.Length != 0)
            return blocked[0];

        ScenarioOutcome[] skipped = items.Where(item => item.Kind == ScenarioOutcomeKind.SkipCapability).ToArray();
        return skipped.Length == 0 ? ScenarioOutcome.Pass : skipped[0];
    }

    public static string AggregateCode(IEnumerable<ScenarioOutcome> outcomes)
        => Aggregate(outcomes).Code;
}

/// <summary>Records one attempt and its later investigation rerun.</summary>
internal sealed record ScenarioAttempt(string Scenario, int Attempt, ScenarioOutcome Outcome);

/// <summary>Pure rerun aggregation that never implements best-of-N success.</summary>
internal sealed record ScenarioAggregate(
    string Scenario,
    IReadOnlyList<ScenarioAttempt> Attempts)
{
    public ScenarioOutcome FinalOutcome
    {
        get
        {
            if (Attempts.Count == 0)
                return ScenarioOutcome.Pass;

            ScenarioAttempt first = Attempts[0];
            if (first.Outcome.Kind is ScenarioOutcomeKind.FailProduct or ScenarioOutcomeKind.FailHarness)
            {
                if (Attempts.Skip(1).Any(attempt => attempt.Outcome.Kind == ScenarioOutcomeKind.Pass))
                    return new ScenarioOutcome(ScenarioOutcomeKind.FlakeUnclassified,
                        $"first={first.Outcome.Code}, rerun=PASS");
            }

            return ScenarioOutcomeContract.Aggregate(Attempts.Select(attempt => attempt.Outcome));
        }
    }
}
