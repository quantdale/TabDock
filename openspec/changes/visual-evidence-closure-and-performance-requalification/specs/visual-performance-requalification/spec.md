# visual-performance-requalification

## ADDED Requirements

### Requirement: Visual overhead measurements SHALL be repeated, identity-bound, and distributional

The visual qualification system SHALL measure visual-disabled, checkpoint,
checkpoint-plus-packet, flight-healthy-discard, and flight-failure-flush modes
across representative presentation-sensitive scenarios. Every sample SHALL
record the exact candidate SHA, run ID, scenario, attempt, mode, policy,
configuration, image dimensions, capture method/scope where applicable,
machine/topology classification, and measurement units. Reports SHALL retain
sample count, median, p95 when the sample count supports it, maximum, and
bounded raw/aggregate evidence. A campaign SHALL NOT derive a release budget
from an unlabelled single trivial capture.

#### Scenario: Paired modes use the same candidate

- **WHEN** enabled and disabled measurements are collected for one scenario
- **THEN** each sample identifies the same exact candidate and comparable
  machine/topology conditions, and the report distinguishes mode overhead from
  scenario variance

#### Scenario: An under-sampled distribution is reported

- **WHEN** a measurement cell has too few samples for a stable p95
- **THEN** the report states the limitation and does not present an invented or
  unlabeled p95 as a release budget

### Requirement: Disabled visual evidence SHALL perform no visual work

When visual evidence is disabled, ValidationDriver SHALL produce zero visual
capture requests, successful or failed visual captures, PNG encodes, retained
visual bytes, visual artifacts, contact-sheet work, packet work, visual worker
or timer activity, and artifact growth attributable to visual evidence. Any
unavoidable policy-branch/check overhead MAY be nonzero but SHALL be measured
and reported separately. Missing native observations SHALL remain unavailable
and SHALL NOT be converted to zero to manufacture a pass.

#### Scenario: Headless CI runs with visual evidence disabled

- **WHEN** the ordinary CI/resource path executes with visual evidence `none`
- **THEN** no desktop capture, PNG, contact-sheet, packet, visual worker, or
  visual artifact operation occurs, and only separately labelled control
  overhead may remain

#### Scenario: Disabled-mode invariant regresses

- **WHEN** a disabled run increments a visual-work counter or leaves visual
  artifact/resource residue
- **THEN** the deterministic gate fails and does not downgrade the finding to
  a warning

### Requirement: Enabled visual modes SHALL expose bounded operation costs

Checkpoint and flight measurements SHALL separately record capture latency,
PNG encode latency and size, temporary/final filesystem write and hash cost,
manifest cost, contact-sheet cost, packet/instruction cost, retained frame
count/bytes, peak ring memory, allocation/peak-memory deltas, CPU cost where
practical, and flush/discard cost. Measurements SHALL cover at least rename,
split, inline capture, maximize/fullscreen, title centering, and one controlled
topmost or high-risk transition scenario. Raw PNG evidence SHALL remain
immutable while derived work is measured independently.

#### Scenario: Representative checkpoint costs are measured

- **WHEN** a checkpoint-enabled scenario reaches each configured phase
- **THEN** the report identifies the capture method/dimensions, latency,
  encoding/write/hash costs, retained bytes, and derived packet/contact costs
  without combining unlike operations into one opaque total

#### Scenario: Flight failure flush is measured

- **WHEN** a bounded flight recorder flushes after a suspicious or failed
  transition
- **THEN** the report records cadence, ring occupancy/evictions, ordered flush
  count/bytes/time, trigger-frame cost, and post-flush cleanup

### Requirement: Visual budgets SHALL be derived from measured distributions

Visual artifact, latency, memory, CPU, allocation, native-resource, and
lifecycle budgets SHALL be selected only after the campaign has recorded the
measurement evidence from representative cells. Each budget SHALL identify its
source candidate, sample count, statistic, safety margin, outliers, units, and
whether it is a hard ceiling, regression gate, or diagnostic warning. Guessed
constants SHALL NOT silently become release thresholds.

#### Scenario: A measured budget is published

- **WHEN** the measurement report is reviewed for budget selection
- **THEN** every selected limit has a traceable distribution and explicit
  rationale, and deterministic tests exercise the resulting limit

#### Scenario: A budget lacks evidence

- **WHEN** a proposed visual limit has no representative measured source
- **THEN** the limit remains provisional/non-gating and release qualification
  does not claim that the performance requirement is proven

### Requirement: Visual resource cleanup SHALL be compared with the non-visual baseline

Visual-enabled and visual-disabled runs SHALL use the existing run-owned
resource qualification boundary and SHALL compare process handles, USER/GDI
objects, HBITMAP/HDC lifetime, file handles, private bytes, working set,
threads/workers, timers, TabDock-owned windows, artifact residue, and ring
memory before and after cleanup. Healthy flight history SHALL be discarded by
policy; failure history SHALL flush only within configured bounds. Cancellation,
timeout, capture failure, encode failure, packet/contact failure, and process
abort SHALL stop recorder work and close/remove run-owned resources.

#### Scenario: Visual work returns to the baseline plateau

- **WHEN** a visual-enabled scenario completes and cleanup finishes
- **THEN** observed resources return within the selected measured delta from
  the paired disabled baseline, with no surviving visual worker/timer/file or
  unbounded artifact residue

#### Scenario: A required resource observation is unavailable

- **WHEN** a supported resource counter cannot be read or process generation
  changes during comparison
- **THEN** the resource result is blocked/unavailable rather than treated as a
  zero-cost or passing observation

### Requirement: Ordinary CI SHALL remain model-free and screen-capture-free

The deterministic CI path SHALL exercise synthetic visual frames, PNG,
manifest, packet/result, tamper, stale, collection, derived-failure, disabled-
mode, budget, and cleanup invariants without capturing the physical desktop or
invoking a multimodal model. Real capture and capable-agent review SHALL
remain supervised evidence, explicitly labelled synthetic versus physical.

#### Scenario: CI runs the visual regression matrix

- **WHEN** the canonical CI-safe validation command executes
- **THEN** it verifies bounded visual contracts with synthetic/in-memory
  fixtures, produces no real screenshots, invokes no model/network service,
  and fails closed on a visual contract regression

#### Scenario: Synthetic performance evidence is used for a physical gate

- **WHEN** deterministic fixture measurements pass without a supervised desktop
- **THEN** they are retained as synthetic resource evidence and the physical
  visual gate remains pending or blocked
