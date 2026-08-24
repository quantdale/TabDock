## Why

ValidationDriver currently reduces distinct physical outcomes to a small
PASS/FAIL/SKIP/BLOCKED vocabulary and has no lease-backed proof that a guarded
input point still belongs to the scenario. A failure caused by a foreign
foreground window, an unavailable capability, or a harness defect can
therefore be mistaken for a product regression, while the native event
sequence needed to diagnose it is lost. This change makes qualification
evidence classifiable, bounded, privacy-safe, and replayable without
pretending that a headless replay is physical Windows qualification.

## What Changes

- Add one canonical scenario outcome contract covering product, harness,
  environment, supervised, capability, and unclassified-flake results.
- Add scenario capability descriptors and a preflight resolver that runs
  before destructive setup and distinguishes skip-capability from blocked
  environment.
- Add a bounded desktop/environment lease with start snapshots and
  input/assertion checkpoints that fail closed on foreign coverage,
  foreground changes, lock/session loss, or identity recycling.
- Make scenario ownership explicit for owned processes/windows, adopted
  external windows, foreign windows, and stale/recycled identities.
- Emit a bounded native interaction timeline and a root run manifest linking
  capabilities, environment, scenario outcomes, JSON/JUnit artifacts, and
  privacy-safe identity evidence.
- Add native-free replay fixtures for relevant WinEvent routing, lifecycle and
  identity transitions, split presentation decisions, foreground intent, and
  containment decisions, plus deterministic stress/model coverage.
- Add controlled rerun aggregation that retains first-attempt and rerun
  outcomes and never converts best-of-N into a release pass.
- Measure WinEvent membership/identity work with representative storms and
  preserve the existing semantics unless the measurement proves a safe,
  meaningful optimization.
- Update the ValidationDriver decomposition, docs, evidence ledger, and exact
  physical-qualification guidance.

## Capabilities

### New Capabilities

- `native-interaction-determinism`: bounded qualification lease, ownership
  registry, timeline/replay evidence, deterministic aggregation, and stress
  contracts for native interaction policy.

### Modified Capabilities

- `validation-qualification`: scenario outcomes, capability preflight,
  evidence manifests, lease checkpoints, and rerun semantics become explicit
  release-validation requirements.

## Impact

The main implementation surface is the `TabDock.ValidationDriver` project and
its native-free unit/self-test contracts, with the smallest viable policy seam
added around WinEvent routing/lifecycle decisions where replay requires it.
`NativeMethods.cs` remains the sole P/Invoke home. Existing Shepherd, split,
identity, persistence, and cleanup authorities remain in place. No third-party
dependencies or application reparenting architecture are introduced.
