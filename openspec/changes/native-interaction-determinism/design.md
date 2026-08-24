## Context

See `proposal.md` and the existing `validation-qualification`,
`test-tooling-safety`, `native-window-identity`, and `production-diagnostics`
specifications. The current driver already has useful identity revalidation,
run markers, bounded process cleanup, and JSON/JUnit output, but those concerns
are spread across `Scenarios`, `Input`, `GuardedProc`, and
`TestRunProvenance`. Production WinEvent dispatch already has a native-free
membership seam in the form of a captured-member resolver and rechecks object
identity at the UI dispatch boundary.

The design must keep the product's Shepherd model and native authorities
unchanged unless a deterministic replay or supervised reproduction proves a
defect. The driver remains a real-input harness; replay is a policy test tier,
not a simulated desktop.

## Goals / Non-Goals

**Goals:**

- Provide one typed result contract used by scenario state, JSON/JUnit,
  console summaries, manifests, rerun aggregation, and process exit mapping.
- Guard physical input with a bounded, privacy-safe desktop lease and expose
  deterministic fake-probe tests for every lease transition.
- Make ownership and adopted-window rules explicit without broadening cleanup
  authority.
- Keep a bounded, deterministic timeline that records roles and identity
  references rather than arbitrary desktop content.
- Reuse current pure split/identity policies and extract only the WinEvent
  routing decision needed to replay native event filtering and stale dispatch.
- Replace the shared wait primitive with monotonic, observable, bounded waits
  while preserving intentional product-debounce sleeps.

**Non-Goals:**

- No private Windows desktop, input virtualization, arbitrary foreign-window
  minimization/repositioning, or fake OS implementation.
- No automatic best-of-N release pass and no retries that reuse stale HWNDs.
- No broad rewrite of `WindowShepherdService`, `GuestLifecycleService`, split
  authorities, persistence, or P/Invoke declarations.
- No production telemetry, network upload, or persistence of raw user window
  titles, URLs, document text, or unredacted paths.

## Decisions

### 1. Keep a value-object outcome contract at the driver boundary

`ScenarioOutcomeKind` is the only status enum. `ScenarioOutcome` carries the
kind, stable code, reason, and whether it is a release pass. A single mapping
class owns exit codes and JUnit attributes. `Ctx` exposes explicit
`FailProduct`, `FailHarness`, `BlockEnvironment`, `BlockSupervised`,
`BlockCapability`, `SkipCapability`, and `MarkFlake` helpers; ordinary
assertions consult the lease/harness state before assigning a product failure.

Alternative rejected: extending the old enum and leaving `Skip`/`Blocked`
interpretation in three writers. That would preserve the ambiguity the change
is intended to remove.

### 2. Resolve scenario capabilities before state isolation or process launch

`ScenarioDescriptor` lists requirements such as interactive input, browser
kind, monitor topology, and signed/Stage-B prerequisites. A
`ScenarioCapabilityResolver` returns a typed decision using safe native probes
and injectable probe functions. The runner writes a preflight result without
calling `StartScenario` when a capability is absent or the environment is
blocked.

Alternative rejected: scattered `File.Exists`/`Process.Start` checks in
scenario bodies. They currently turn absent optional software into exceptions
or late product-looking failures.

### 3. Use a lease over a probe interface, not desktop seizure

`DesktopQualificationLease` captures a start snapshot and evaluates
checkpoints through `IDesktopQualificationProbe`. The production probe reads
foreground/root ownership, visible top-level windows, monitor/DPI/virtual
geometry, session/lock signals where safely available, and target-point
ownership. The fake probe supplies stable snapshots and scripted transitions.
The lease returns `Valid`, `ForeignCoverage`, `ForeignForeground`,
`IdentityChanged`, `SessionUnavailable`, or `Unverifiable`; the runner maps
these to the canonical environment/supervised outcomes.

The lease never fixes the desktop. It refuses input and captures bounded
evidence when a foreign surface invalidates the assumptions. A test-owned modal,
TabDock chrome, or exact adopted target is recognized by the ownership registry
and is not misclassified as foreign.

### 4. Extend, do not replace, TestRunProvenance

The existing process-start/executable/ancestry/marker checks remain the source
of truth. Each process/window record gains an explicit ownership kind:
`OWNED_PROCESS`, `OWNED_WINDOW`, `ADOPTED_EXTERNAL_WINDOW`, `FOREIGN`, or
`STALE_RECYCLED`. Adoption is a window-only record; it cannot create a process
record or enter the cleanup list. Stable identity revalidation transitions a
record to stale and removes its input eligibility without reusing the HWND.

Alternative rejected: a second registry in the lease. The lease asks the
existing registry for role/ownership decisions and records only a bounded
snapshot, preventing divergent cleanup semantics.

### 5. Record a bounded in-memory interaction timeline and one run manifest

`NativeInteractionTimeline` stores a fixed-capacity ring of records with a
monotonic sequence, UTC timestamp, elapsed milliseconds, event kind, role-based
identity references, and sanitized key/value data. It serializes deterministically
and evicts oldest entries. `QualificationResultWriter` writes per-scenario
timeline links and a root manifest containing candidate/build identity,
environment/capabilities, outcome counts, and artifact links. All raw titles
and paths pass existing redaction rules or are omitted.

Alternative rejected: dumping the full desktop or relying only on the existing
free-form log. The former violates privacy and bounds; the latter cannot
reconstruct first divergence deterministically.

### 6. Replay policy at the native boundary

Add a small pure `WinEventRoutingPolicy` used by `WinEventMonitor` to decide
whether a callback is irrelevant, a direct captured event, or a desktop
reorder tied to a callback-time captured foreground. The existing monitor
still owns hook installation, posting, and current-object revalidation. Tests
feed ordered policy inputs and use current `SplitPresentationPolicy` and
`WindowIdentityGate` seams for lifecycle/identity/split fixtures. No replay
fixture calls USER32 or mutates a real HWND.

Alternative rejected: splitting `WindowShepherdService` or introducing a
mock Win32 object graph. The policy boundary gives deterministic coverage of
the high-value decisions while preserving real native authority.

### 7. Measure before changing WinEvent lookup behavior

The monitor's resolver seam is instrumented in tests for callback and dispatch
counts under bounded event storms. The expected two resolves for a queued
captured event are treated separately: callback-time admission and
dispatch-time stale-object revalidation. If representative storms show no
meaningful redundant work, the optimization is rejected and the measured
counts are documented. Any accepted change requires equivalence tests first.

### 8. Reruns are an aggregate record, never a success shortcut

`ScenarioAttempt` and `ScenarioAggregate` are pure records. A blocked first
attempt followed by pass is retained as two outcomes; valid failure followed
by pass becomes `FLAKE_UNCLASSIFIED`; only a single valid PASS is release-pass.
This keeps investigation useful without weakening release gates.

## Risks / Trade-offs

- [Risk] A strict non-PASS exit for capability skips could make blanket local
  runs noisy. → Keep capability decisions visible in manifests and provide
  explicit scenario/shard selection; never relabel them as product failures.
- [Risk] Native desktop probing can itself fail on locked/UIPI sessions. →
  Return `Unverifiable`/blocked with bounded evidence and inject the probe in
  deterministic tests; never guess safe ownership.
- [Risk] Timeline recording could add hot-path overhead. → Record only
  selected events, use a fixed ring, and keep WinEvent production tracing
  behind the existing selected-event filter.
- [Risk] Changing `Ctx.Check` classification could obscure a genuine product
  failure if the lease is stale. → Preserve the assertion and lease reason in
  evidence, and classify the environment first as required by the contract.
- [Risk] Driver decomposition can accidentally make scenario discovery opaque.
  → Keep the existing explicit scenario switch/arrays and extract only shared
  operations with direct names and focused tests.

## Migration Plan

1. Add the typed outcome/capability/lease/timeline/replay value objects and
   native-free tests while retaining compatibility shims for current callers.
2. Migrate `Ctx`, writers, program aggregation, and provenance incrementally;
   old status strings are accepted only at the serialization boundary during
   the wave and then removed.
3. Add lease checkpoints to guarded input and authoritative assertion helpers,
   then migrate scenario capability checks and repeated waits.
4. Add replay fixtures, measurement tests, stress/model tests, and evidence
   manifest aggregation.
5. Update docs/OpenSpec validation and run the full deterministic gate.

Rollback is a source-level revert of the campaign commits; no product state
schema or user data migration is introduced. Physical runs continue to use
the existing conservative cleanup and input guards throughout the migration.
