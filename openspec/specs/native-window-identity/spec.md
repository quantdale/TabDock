# native-window-identity Specification

## Purpose
TBD - created by archiving change post-remediation-review-followup-2026-08-13. Update Purpose after archive.
## Requirements
### Requirement: Slow native guest mutations SHALL require process-instance identity

Hide, release, foreground handoff, delayed restore, and recovery operations
SHALL fail closed unless the live HWND still matches the captured PID, window
thread, class, executable where required, a nonzero captured process-start
identity, and the per-capture HWND token. A legacy journal entry without a
process-start identity or token SHALL be ineligible for recovery mutation and
SHALL remain classified as durable pending/manual-recovery evidence.

#### Scenario: Strong identity is required before a destructive operation

- **WHEN** release, hide, foreground handoff, delayed restore, or recovery is
  attempted for a captured guest
- **THEN** the operation SHALL require the applicable strong identity evidence
  and SHALL perform no native mutation when that evidence is unavailable

### Requirement: Identity verification SHALL distinguish mismatch from uncertainty

The identity gate SHALL expose distinct `Match`, `Mismatch`, and
`Unverifiable` outcomes. Native mutation SHALL be allowed only for `Match`.
`Mismatch` requires positive stale/recycled evidence; unavailable or throwing
required probes SHALL be `Unverifiable`, never an implicit mismatch.

#### Scenario: A strong probe is unavailable

- **WHEN** process-start, executable, class, or another required identity probe
  cannot be read
- **THEN** the operation SHALL perform no native mutation, SHALL preserve its
  recovery journal, and SHALL report a retryable recovery-pending outcome

#### Scenario: A positive mismatch is observed

- **WHEN** PID, GUI thread, class, executable, process-start, or capture token
  positively differs
- **THEN** the operation SHALL perform no native mutation and MAY clear only
  recovery evidence whose complete identity tuple matches the stale object

### Requirement: Release SHALL complete the native transaction before detaching the member

Shepherd SHALL verify identity before release mutation and SHALL return a
structured release outcome. GroupManager SHALL remove a logical member only
after release succeeds or positive stale identity is established. An
unverifiable or partially completed release SHALL retain the member binding,
preserve durable recovery evidence, and remain retryable.

#### Scenario: Pending release does not detach an external guest silently

- **WHEN** release cannot verify identity or cannot prove native finalization
- **THEN** the guest SHALL not receive a further possibly-wrong native
  mutation, the member SHALL remain represented or explicitly marked pending,
  and its journal SHALL remain available for a later retry

#### Scenario: A same-HWND different-PID replacement is refused

- **WHEN** a delayed destructive callback observes the captured HWND with a
  different PID
- **THEN** it SHALL perform no native hide, release, restore, or foreground
  mutation

#### Scenario: A same-PID different-process-instance replacement is refused

- **WHEN** the current PID and class match but the native process-start time
  differs from the captured value
- **THEN** the operation SHALL fail closed without native mutation

#### Scenario: A same-process HWND is re-captured before an old callback runs

- **WHEN** an HWND value is released and then captured into a new
  `CapturedWindow` in the same process before a callback for the old object
- **THEN** the old callback SHALL be rejected by the captured-object binding

#### Scenario: A same-process HWND is recycled before the old callback runs

- **WHEN** the original window is destroyed and Windows reuses its HWND for a
  different window in the same process, even if PID, GUI thread, class, and
  executable still match
- **THEN** the old callback SHALL be rejected by the per-capture HWND token

#### Scenario: A valid current member remains actionable

- **WHEN** all captured identity fields and the live object binding match
- **THEN** the guarded operation SHALL be admitted

### Requirement: Unverifiable hides SHALL not advance logical presentation

Hide SHALL return an explicit outcome. When identity or journal durability is
unverifiable, the caller SHALL preserve the captured member and durable
evidence and SHALL not advance an active-tab or split transition that would
leave the old guest visible beside a newly logical guest.

#### Scenario: Active-tab switch rolls back after an unverifiable hide

- **WHEN** the outgoing active guest cannot be strongly verified before its
  hide transaction
- **THEN** the incoming guest SHALL not be presented as the new active guest,
  and the old active member SHALL remain the logical active member with its
  journal available for retry

### Requirement: Hot layout identity checks SHALL remain bounded

High-frequency positioning SHALL retain HWND/PID/thread/class safeguards and
SHALL NOT allocate a managed `Process` or perform a process-start lookup on
every layout tick. A missing process-start identity SHALL fail closed on slow
paths rather than be treated as a wildcard.

#### Scenario: Hot validation does not probe process start

- **WHEN** a valid member is checked repeatedly through the hot tier
- **THEN** the identity seam SHALL report zero process-start probes

### Requirement: Destructive native mutations SHALL revalidate at generation boundaries

After a potentially slow journal or other blocking transaction, capture,
hide, release, and crash rescue SHALL perform a cheap generation revalidation
immediately before each distinct destructive native mutation. The cheap gate
SHALL use the live binding when available, `IsWindow`, the expected HWND token,
PID, GUI thread, and class; a strong executable/process-start probe SHALL not
be repeated on every hot layout frame. Only a `Match` SHALL authorize the next
native write. The final check-to-call interval remains an unavoidable ordinary
Win32 race and SHALL NOT be described as atomic.

#### Scenario: Capture refuses a recycled HWND after journal commit

- **WHEN** `JournalCapture` succeeds but the pre-token PID, thread, class,
  executable, or process-start identity no longer matches
- **THEN** `SetProp` and all DWM/presentation mutations SHALL be skipped, the
  in-memory binding SHALL be removed, and only the exact old journal record MAY
  be cleared

#### Scenario: Capture refuses DWM mutation after token installation changes

- **WHEN** the expected capture token is installed but the cheap generation
  gate fails before the first DWM mutation
- **THEN** DWM SHALL not receive a mutation for that HWND generation and the
  exact token cleanup/journal outcome SHALL be generation-scoped

#### Scenario: Hide refuses a replacement after durable JournalHide

- **WHEN** `JournalHide` completes and the cheap generation gate observes a
  mismatch or unverifiable identity
- **THEN** `ShowWindow(SW_HIDE)` SHALL not be called on the replacement; a
  mismatch MAY clear only old exact evidence, while uncertainty retains the
  journal and retryable member

#### Scenario: Release stops after a partial old-window mutation

- **WHEN** placement succeeds but the captured generation changes before
  visibility, DWM, foreground, or token-removal work
- **THEN** no subsequent native mutation SHALL target the replacement and the
  old release evidence SHALL be retained or classified only from positive
  mismatch evidence

#### Scenario: Rescue stops after placement when generation changes

- **WHEN** a v3 rescue identity matches initially but its generation token or
  cheap identity fields change after placement
- **THEN** visibility, DWM restoration, and token removal SHALL not run against
  the replacement; unverifiable evidence SHALL remain retryable

### Requirement: Picker-to-capture handoff SHALL carry strong selection evidence

The picker-to-capture handoff SHALL carry the available process-start and
window-class evidence alongside HWND, PID, and executable identity. A missing
required process-instance probe SHALL not be treated as a wildcard. The final
capture operation SHALL remain the authoritative native identity/admission gate
and SHALL fail closed before presentation mutation when identity is stale or
unverifiable.

#### Scenario: Process-start evidence is checked before picker submission

- **WHEN** a selected row's live process-start identity differs from the value
  captured at selection time
- **THEN** the row is rejected as stale and Shepherd performs no presentation
  mutation for it

#### Scenario: An inaccessible process is not admitted through the picker

- **WHEN** executable or process-start identity cannot be read for a selected
  target
- **THEN** the target is omitted or rejected with an actionable failure and no
  native capture mutation is attempted
