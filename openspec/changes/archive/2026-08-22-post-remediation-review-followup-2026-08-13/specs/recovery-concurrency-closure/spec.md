# Recovery/concurrency closure

## ADDED Requirements

### Requirement: Supervised recovery has a durable resumable transaction
`--recover-pending` SHALL persist a versioned transaction record containing a
safe source-file identifier, source SHA-256, entry fingerprint, selected HWND
identity, recovery mode, and a cryptographically random nonzero recovery token
before installing `TabDock.PendingRecoveryToken` or mutating the guest.
Transaction progress SHALL be durable across the prepared, token-installed,
presentation, native-complete, token-removed, and retired boundaries.

#### Scenario: A hard kill before token installation is restartable
- **WHEN** the process dies after the prepared record is durable but before
  `SetProp`
- **THEN** the pending evidence remains, no native presentation mutation has
  occurred, and a later supervised invocation can revalidate and resume after
  explicit confirmation

#### Scenario: A hard kill after native completion does not repeat native work
- **WHEN** the process dies after the native-complete marker is durable but
  before token cleanup or pending-entry retirement
- **THEN** the next invocation verifies/removes only the exact owned token and
  performs disk cleanup without repeating placement, visibility, or DWM work

#### Scenario: Foreign recovery ownership fails closed
- **WHEN** a candidate has a nonzero recovery property but no matching durable
  transaction proves the exact source, target, and token
- **THEN** recovery refuses and leaves the foreign property and pending bytes
  untouched

### Requirement: Mutating recovery shares product mutation ownership
The normal TabDock process and `--recover-pending` SHALL use the same named
product-mutation lease for the complete mutating lifetime. A second normal
process or recovery process SHALL refuse before discovery or native/file
mutation. Read-only diagnostics SHALL remain usable without this lease.

#### Scenario: Normal TabDock and supervised recovery are mutually exclusive
- **WHEN** either a normal TabDock instance or a supervised recovery command
  owns `Global\\TabDock`
- **THEN** the other mutating operation exits nonzero with an instruction to
  close the active TabDock mutation owner

#### Scenario: An abandoned owner is recoverable
- **WHEN** Windows reports the mutation mutex owner abandoned
- **THEN** the next waiter acquires ownership and continues under the normal
  evidence and identity safety gates

### Requirement: Interactive recovery has a real WinExe console contract
The WinExe recovery command SHALL use one scoped console session for the whole
interactive transaction. It SHALL use redirected standard streams when
available, otherwise attach once to the launching parent console, rebind
managed streams, flush prompts, and free only a console attached by that
scope. With neither console nor redirected input it SHALL fail nonzero without
blocking or starting the WPF UI.

#### Scenario: Redirected process recovery exits without pending evidence
- **WHEN** the built executable is launched with isolated `APPDATA`, redirected
  standard streams, and EOF input
- **THEN** it emits the no-pending result and exits successfully without WPF
  startup, hooks, or an interactive hang

### Requirement: Historical intentional-hide evidence is not resurrected
Supervised recovery of a literal v2 entry with `DoNotRescue=true` SHALL retain
the intentional-hide meaning: it SHALL not call `ShowWindow` or restore
placement, and may restore only the historical DWM transition state before
retiring the entry after successful identity validation. The discovery and
confirmation output SHALL identify this as intentional-hide cleanup.

#### Scenario: DoNotRescue remains hidden
- **WHEN** a selected v2 entry contains `DoNotRescue=true`
- **THEN** the guest is never shown or repositioned, interrupted presentation
  state is cleaned safely, and unresolved evidence remains on any failure

### Requirement: Deferred positioning validates each safe queue boundary
Split deferred positioning SHALL validate each guest generation immediately
before its `DeferWindowPos` call. A failed native `DeferWindowPos` SHALL follow
the documented no-`EndDeferWindowPos` path. If a later generation validator
fails while a valid HDWP exists, the helper SHALL close that valid batch with
`EndDeferWindowPos` and SHALL not run a stale-guest fallback.

#### Scenario: A stale partner is never queued
- **WHEN** the partner generation changes after the top guest was queued but
  before the partner queue operation
- **THEN** the partner is not passed to `DeferWindowPos`, the valid HDWP is
  closed according to its native lifecycle, and no stale fallback touches it

### Requirement: Canonical journal clear semantics are synchronous
The canonical hidden-window journal contract SHALL match production:
`JournalHide`, intentional-hide writes, and an actual matching `JournalClear`
are synchronous; a clear with no matching entry SHALL perform no disk write;
`FlushJournal` SHALL remain an idempotent lifecycle boundary; no pending 300ms
clear timer is part of correctness.

#### Scenario: Empty clear is a no-write fast path
- **WHEN** `JournalClear` is called with no matching cached entry
- **THEN** it returns without a disk write, while a matching clear writes
  synchronously exactly once

### Requirement: Supervised local titles are terminal-safe
Candidate titles shown only to the supervised recovery operator SHALL be
normalized to one bounded line, with C0/C1 controls, ESC, DEL, and Unicode line
separators removed or replaced. Ordinary Unicode SHALL remain readable, and
support-bundle/doctor privacy redaction SHALL remain unchanged.

#### Scenario: ANSI and control injection is neutralized
- **WHEN** an external title contains ANSI/OSC sequences, C0/C1 controls,
  line separators, emoji, or CJK text
- **THEN** no raw terminal control or line break is emitted, the output stays
  within its maximum length, and ordinary Unicode remains identifiable
