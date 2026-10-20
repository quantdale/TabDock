## Context

See proposal.md and audit A10. Discover defaults cleanupTemporaryFiles to true; FormatDiscovery and diagnostic inspection inherit that behavior, while launcher attention explicitly disables it. Eligible stale-temp deletion is hygiene, not discovery or a native recovery requirement.

## Goals / Non-Goals

**Goals:** Read-only defaults and call sites; explicit coordinated hygiene; unchanged evidence bytes/file set during inspection.

**Non-Goals:** No new evidence-retirement policy, automatic tokenless recovery, or filesystem maintenance loop.

## Decisions

1. Remove hygiene as a discovery side effect. Prefer a read-only discovery API without a mutating boolean to merely changing the boolean default: future callers should not accidentally opt into cleanup under an inspection name.
2. Retain the current bounded age/pending-prefix/temp-shape eligibility policy in an explicit hygiene operation. Invoke it only from an existing mutating startup or supervised-recovery path after ProductMutationLease acquisition and after storage-root resolution. Read-only diagnostics remain lease-independent.
3. Migrate all discovery callers, including pending CLI, doctor, support export, attention projection, and internal count helpers. Completed-entry/sidecar retirement stays in the mutating recovery workflow; discovery never performs it.
4. Compare file sets, bytes and last-write times before/after inspection against disposable directories containing old/new temp fragments, valid pending sources, ledgers, malformed evidence, and unrelated files. Do not require stable access times, which filesystem reads can update. Recording native adapters must observe zero writes.
5. Test the actual command/export paths only using A02's verified isolated root; use explicit directory fixtures for the immediate default-discovery regression.

## Risks / Trade-offs

- Stale temp fragments can remain longer if only diagnostics run → intentional non-mutation; next coordinated mutating session handles hygiene.
- Moving cleanup before root/lease proof repeats the safety bug → enforce ordering at its explicit call site and test lease refusal/root mismatch.
- A10 and A02 share discovery/bootstrap paths → serialize integration and rerun both sentinel suites.

## Migration Plan

Add failing read-only artifact-preservation tests, separate the API, and wire existing mutating hygiene. No evidence format migration; existing stale fragments are not automatically removed by an inspection upgrade. Rollback must not reintroduce mutating discovery defaults.
