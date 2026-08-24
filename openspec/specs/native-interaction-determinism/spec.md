# native-interaction-determinism

## Purpose

Provides deterministic, privacy-safe evidence and replay contracts for guarded
native interaction qualification when physical Windows state is volatile.

## Requirements

### Requirement: Qualification outcomes SHALL be canonical and lossless

Every scenario result SHALL use one canonical outcome vocabulary containing
`PASS`, `FAIL_PRODUCT`, `FAIL_HARNESS`, `BLOCKED_ENVIRONMENT`,
`BLOCKED_SUPERVISED`, `BLOCKED_CAPABILITY`, `SKIP_CAPABILITY`, and
`FLAKE_UNCLASSIFIED`. The same value SHALL drive the console result, process
exit mapping, JSON result, aggregate summary, and JUnit representation.

#### Scenario: A valid assertion fails

- **WHEN** prerequisites and the desktop lease are valid at the assertion
  boundary and a product invariant fails
- **THEN** the scenario is reported as `FAIL_PRODUCT`, not a generic failure

#### Scenario: A foreign window invalidates input

- **WHEN** an unregistered foreign top-level window covers the target point or
  takes the relevant foreground during a guarded step
- **THEN** no input or mutation is sent and the scenario is reported as
  `BLOCKED_ENVIRONMENT`

#### Scenario: A harness invariant fails

- **WHEN** the driver cannot prove its own selector, cleanup, evidence, or
  ownership invariant
- **THEN** the scenario is reported as `FAIL_HARNESS` and remains
  distinguishable from `FAIL_PRODUCT`

### Requirement: Capability requirements SHALL be resolved before destructive setup

Scenarios SHALL declare their required capabilities. The resolver SHALL return
runnable, capability-skippable, capability-blocked, or environment-blocked
without launching TabDock or mutating user state when prerequisites are
unavailable.

#### Scenario: An optional browser is absent

- **WHEN** a browser scenario requires an executable that is not installed
- **THEN** it reports `SKIP_CAPABILITY` without throwing or starting a product
  process

#### Scenario: Interactive input is unavailable

- **WHEN** a physical scenario requires an unlocked interactive input session
  and that condition cannot be proven
- **THEN** it reports `BLOCKED_ENVIRONMENT` before destructive setup

### Requirement: A desktop lease SHALL guard physical input and assertions

The driver SHALL capture a privacy-safe start snapshot and SHALL provide
bounded checkpoints before guarded input and authoritative physical assertions.
The lease SHALL distinguish registered TabDock/test-owned/adopted identities
from foreign or recycled HWNDs and SHALL fail closed when continuity is not
proven.

#### Scenario: An adopted external target remains unchanged

- **WHEN** a bounded operation revalidates an adopted window and its complete
  pinned identity still matches
- **THEN** the target remains usable for that operation while its process
  remains outside cleanup ownership

#### Scenario: A recycled HWND is observed

- **WHEN** an HWND value is reused by a different identity before an input or
  assertion checkpoint
- **THEN** the lease invalidates the operation and records bounded identity
  evidence without mutating the replacement

### Requirement: Run evidence SHALL be bounded, privacy-safe, and replay-linkable

Each supervised run SHALL emit one root manifest linking candidate/build
identity, environment and capability snapshots, scenario outcomes, JSON/JUnit
artifacts, ownership diagnostics, and bounded native-interaction timeline
artifacts. The manifest SHALL omit arbitrary user titles, URLs, document text,
and unredacted user paths.

#### Scenario: A scenario fails after a native event storm

- **WHEN** the run completes or aborts after a guarded scenario step
- **THEN** the manifest links a bounded timeline sufficient to identify the
  first recorded invariant divergence

#### Scenario: The timeline reaches capacity

- **WHEN** more events are recorded than the configured evidence bound
- **THEN** oldest entries are evicted deterministically, sequence numbers remain
  monotonic, and export remains privacy-safe

### Requirement: Native-free replay SHALL preserve relevant policy decisions

Replay fixtures SHALL accept an initial logical state, relevant identities,
ordered synthetic native events, and probe outcomes, then expose expected
membership, lifecycle/presentation state, and native intents/refusals without
creating desktop windows or invoking destructive native mutation.

#### Scenario: A stale lifecycle event arrives after release

- **WHEN** a destroy, hide, show, or name-change event references an identity
  generation that is no longer current
- **THEN** replay refuses the stale transition and preserves the current
  logical state

#### Scenario: A split member disappears during a transition

- **WHEN** a member-death event is replayed while a pair is presented or dormant
- **THEN** replay applies the existing split policy for survivor promotion or
  dormant preservation without swapping member identity by index

### Requirement: Reruns SHALL retain first-attempt classification

Aggregated reports SHALL retain first-attempt and rerun outcomes separately. An
environment-invalid attempt followed by a pass MAY be reported as
`first=BLOCKED_ENVIRONMENT, rerun=PASS`, while a valid-environment product
failure followed by a pass SHALL remain `FLAKE_UNCLASSIFIED` until understood.

#### Scenario: Best-of-N would hide a valid failure

- **WHEN** a valid first attempt reports `FAIL_PRODUCT` and a later attempt
  reports `PASS`
- **THEN** the aggregate is not promoted to release PASS and retains the valid
  failure and rerun outcome
