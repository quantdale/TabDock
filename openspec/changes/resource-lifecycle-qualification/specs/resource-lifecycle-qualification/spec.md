## Purpose

This capability makes sustained lifecycle reliability observable and
reviewable by measuring bounded Windows GUI/process resources during safe
qualification without weakening TabDock's native or release trust boundaries.

## ADDED Requirements

### Requirement: Resource observations are identity-bound and privacy-safe

The qualification system SHALL represent each resource observation with a
sequence, phase, timestamp, process identifier and process-start identity. It
SHALL support process handles, USER objects, GDI objects, private bytes,
working set, threads, and TabDock-owned top-level window count. An unavailable
signal SHALL remain unavailable; it MUST NOT be converted to zero or omitted
from the qualification decision. Resource evidence MUST NOT contain window
titles, document names, URLs, command lines, credentials, or user file paths.

#### Scenario: Complete observation captures the supported signals

- **WHEN** a run-owned TabDock process exposes the supported Windows counters
- **THEN** the retained observation records the counters and the process-start
  identity without collecting user content or identifying window text

#### Scenario: Inaccessible observation fails closed

- **WHEN** a process exits, cannot be opened, or one supported counter cannot be
  read
- **THEN** the observation records unavailable evidence and the series cannot
  receive a resource-stability PASS

### Requirement: Resource series analysis distinguishes plateau from growth

The qualification system SHALL analyze an explicit warm-up prefix and multiple
settled checkpoints. It SHALL report deltas, peak and tail behavior, and a
deterministic trend classification that can distinguish warm-up growth,
bounded noise, transient recovery, sustained or late growth, counter reset,
invalid ordering, and unavailable evidence. A persistent native-resource
increase beyond its documented budget SHALL fail the resource result. Invalid,
missing, reordered, or cross-process-generation evidence SHALL be BLOCKED and
MUST NOT be reported as PASS.

#### Scenario: Lazy warm-up reaches a plateau

- **WHEN** resource counters rise during the configured warm-up and remain
  within budget across settled checkpoints
- **THEN** analysis reports a passing plateau result using the settled baseline

#### Scenario: One native object leaks per settled cycle

- **WHEN** USER, GDI, or process-handle counts increase persistently across
  settled checkpoints
- **THEN** analysis reports a failed resource result and identifies the leaking
  metric and growth trend

#### Scenario: Process generation changes during sampling

- **WHEN** the process-start identity changes or becomes unavailable between
  observations
- **THEN** analysis reports BLOCKED rather than combining generations or
  inferring stability from reset counters

### Requirement: Lifecycle qualification covers complementary churn profiles

The safe resource qualification command SHALL provide repeatable profiles for
group/capture membership churn, split and layout churn, picker/icon generation
churn, WinEvent routing lifecycle, bounded diagnostics artifacts, isolated
persistence/recovery artifacts, and process-generation cleanup. Each profile
SHALL report cycle count, operations, peak live state, final live state, and
remaining artifact residue. Profile failures SHALL preserve enough diagnostic
evidence to identify the profile and SHALL clean up run-owned temporary state.

#### Scenario: All safe profiles complete their cycles

- **WHEN** a bounded headless run selects all lifecycle profiles
- **THEN** each profile completes its configured cycles, returns to its bounded
  final state, and reports no unexpected temporary residue

#### Scenario: A profile assertion fails

- **WHEN** a lifecycle profile detects stale state or artifact residue
- **THEN** the run reports a non-pass resource result, retains the resource
  evidence, and still attempts cleanup of run-owned processes and temporary
  state

### Requirement: Resource evidence is retained as resource-only qualification

Each resource-aware run SHALL emit machine-readable JSON and JUnit-compatible
evidence containing schema version, source and driver identity, run identity,
measurement mode, metric definitions and budgets, snapshots or summaries,
profile outcomes, trend analysis, and a PASS/FAIL/BLOCKED result. The existing
qualification manifest SHALL bind the evidence to the same run. Synthetic or
headless resource evidence SHALL be marked as such and MUST NOT satisfy
physical-input, mixed-DPI, operating-system, signing, or human-smoke gates.
Malformed or source-mismatched evidence SHALL fail closed during validation.

#### Scenario: Headless resource evidence passes

- **WHEN** deterministic lifecycle profiles and synthetic resource series pass
- **THEN** the manifest records a resource-only synthetic PASS with its source
  and driver identities

#### Scenario: Synthetic evidence is used for a physical gate

- **WHEN** a release decision has only a synthetic resource result for a
  physical or supervised requirement
- **THEN** that requirement remains unresolved rather than being promoted to
  PASS

### Requirement: CI and extended soak execution remain safe

The ordinary CI resource gate SHALL be bounded, headless, deterministic, and
non-invasive. It MUST NOT send physical input, require an exclusive desktop,
inspect arbitrary user windows, or mutate production user state. An opt-in
extended command SHALL default to test-owned or synthetic resources, isolate
persistence state, enforce child-process and time bounds, and require the
existing supervised safety policy before any physical-input extension.

#### Scenario: Hosted CI runs the resource gate

- **WHEN** the canonical CI validation path executes its resource check
- **THEN** it completes without SendInput or arbitrary desktop interaction and
  fails the job on a resource-profile or analyzer regression

#### Scenario: Local extended soak is requested

- **WHEN** a developer requests a longer resource soak
- **THEN** the command reports source, profile, cycles, metrics, trends, and
  artifact location while cleaning only run-owned processes and isolated state
