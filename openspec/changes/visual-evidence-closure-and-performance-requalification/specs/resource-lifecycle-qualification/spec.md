# resource-lifecycle-qualification delta — visual overhead comparison

## MODIFIED Requirements

### Requirement: Resource observations are identity-bound and privacy-safe

The qualification system SHALL represent each resource observation with a
sequence, phase, timestamp, process identifier and process-start identity. It
SHALL support process handles, USER objects, GDI objects, HBITMAP/HDC lifetime,
file handles, private bytes, working set, threads/workers, timers, and
TabDock-owned top-level window count. An unavailable signal SHALL remain
unavailable; it MUST NOT be converted to zero or omitted from the
qualification decision. Resource evidence MUST NOT contain window titles,
document names, URLs, command lines, credentials, or user file paths.
Visual-mode observations SHALL identify disabled/checkpoint/flight mode and
shall not include raw image content in resource records.

#### Scenario: Visual resources are observed without user content

- **WHEN** a visual-enabled or disabled run is compared with its paired
  baseline
- **THEN** observations retain process/resource identity, visual mode, and
  bounded counters without collecting titles, URLs, credentials, or raw image
  pixels

#### Scenario: An inaccessible counter is encountered

- **WHEN** a required resource counter cannot be read or process generation
  changes
- **THEN** the observation remains unavailable and the resource result blocks
  rather than converting the missing value to zero

### Requirement: Resource series analysis distinguishes plateau from growth

The qualification system SHALL analyze an explicit warm-up prefix and multiple
settled checkpoints. It SHALL report deltas, peak and tail behavior, and a
deterministic trend classification that distinguishes warm-up growth, bounded
noise, transient recovery, sustained or late growth, counter reset, invalid
ordering, and unavailable evidence. It SHALL compare visual-enabled cells to
paired visual-disabled baselines for the same candidate, scenario, and
machine/topology classification. Persistent native-resource or visual-artifact
growth beyond measured documented budgets SHALL fail the resource result.
Invalid, missing, reordered, cross-process-generation, or incomparable
evidence SHALL be BLOCKED and MUST NOT be reported as PASS.

#### Scenario: Visual work returns to the paired plateau

- **WHEN** checkpoint or flight evidence completes and cleanup finishes
- **THEN** settled handles, GDI/USER objects, private bytes, working set,
  threads, timers, file handles, ring memory, and artifact residue remain
  within the selected measured delta from the disabled baseline

#### Scenario: Healthy flight history is discarded

- **WHEN** a flight-enabled transition completes without a flush trigger
- **THEN** rolling frames are released, retained visual bytes/artifacts remain
  within policy, and no visual worker/timer/file residue survives

### Requirement: Lifecycle qualification covers complementary churn profiles

The safe resource qualification command SHALL provide repeatable profiles for
group/capture membership churn, split and layout churn, picker/icon generation
churn, WinEvent routing lifecycle, bounded diagnostics artifacts,
isolated persistence/recovery artifacts, process-generation cleanup, and
visual capture/encode/contact/packet/flight lifecycle. Each profile SHALL
report cycle count, operations, peak live state, final live state, visual mode
when applicable, and remaining artifact/resource residue. Profile failures
SHALL preserve enough diagnostic evidence to identify the profile and SHALL
clean up run-owned processes and temporary state.

#### Scenario: Visual lifecycle profiles complete

- **WHEN** the bounded resource command executes visual-disabled and selected
  visual-enabled profiles
- **THEN** each profile reports its mode/cycles/costs, returns to its bounded
  final state, and reports no unexpected temporary, native, worker, timer, or
  artifact residue

### Requirement: Resource evidence is retained as resource-only qualification

Each resource-aware run SHALL emit machine-readable JSON and JUnit-compatible
evidence containing schema version, source and driver identity, run identity,
measurement mode, metric definitions and budgets, snapshots or summaries,
profile outcomes, trend analysis, and a PASS/FAIL/BLOCKED result. Visual
comparison reports SHALL additionally identify the candidate, scenario/mode
cells, sample counts/statistics, selected measured budgets, baseline, and
synthetic/physical classification. The existing qualification manifest SHALL
bind the evidence to the same run. Synthetic or headless resource evidence
SHALL be marked as such and MUST NOT satisfy physical-input, mixed-DPI,
operating-system, signing, or human-smoke gates. Malformed, unavailable,
source-mismatched, or budget-unproven evidence SHALL fail closed.

#### Scenario: Disabled and enabled evidence is compared

- **WHEN** a paired visual resource qualification completes
- **THEN** its report contains raw/aggregate statistics and measured deltas for
  disabled, checkpoint, and flight cells, with budget provenance and no
  unsupported claim of physical visual acceptance

### Requirement: CI and extended soak execution remain safe

The ordinary CI resource gate SHALL be bounded, headless, deterministic,
non-invasive, model-free, and screen-capture-free. It MUST NOT send physical
input, require an exclusive desktop, inspect arbitrary user windows, mutate
production user state, or create visual artifacts from the physical desktop.
An opt-in extended command SHALL default to test-owned or synthetic resources,
isolate persistence state, enforce child-process/time/artifact bounds, and
require the existing supervised safety policy before any physical-input or
real-capture extension.

#### Scenario: Hosted CI runs visual resource checks

- **WHEN** the canonical CI validation path executes visual resource
  regression checks
- **THEN** it uses synthetic/in-memory visual fixtures, proves disabled-mode
  zero work and cleanup bounds, and fails on regression without screen capture,
  model inference, or network access

#### Scenario: Local extended visual soak is requested

- **WHEN** a developer explicitly requests enabled visual measurement
- **THEN** the command reports source, scenario, mode, cycles, samples,
  metrics, trends, resource deltas, and artifact location while cleaning only
  run-owned processes and isolated state
