## ADDED Requirements

### Requirement: Recovery journal schema changes SHALL be explicit

The full presentation/process-start journal from the historical v2 format and
the current thread/token generation journal SHALL have different schema
versions. The current format SHALL be v3. A read of an unsupported future
version SHALL perform no native mutation and SHALL leave the original bytes
untouched.

#### Scenario: Historical v1 is preserved

- **WHEN** startup reads the literal legacy v1 HWND/PID/executable journal
- **THEN** it SHALL perform no native rescue and SHALL preserve the exact bytes
  in durable named pending evidence

#### Scenario: Historical v2 is preserved

- **WHEN** startup reads a literal v2 journal containing presentation and
  process-start fields but no GUI-thread or generation-token fields
- **THEN** it SHALL perform no native rescue, SHALL not pretend the entry is v3,
  and SHALL preserve the exact bytes for supervised manual recovery

#### Scenario: An incomplete v3 is preserved

- **WHEN** a v3 entry is missing its nonzero thread, process-start, or
  generation-token evidence
- **THEN** the complete source journal SHALL be preserved as pending evidence
  without native mutation or destructive partial rewrite

#### Scenario: A future version is left untouched

- **WHEN** startup reads a journal with version 4 or later
- **THEN** the active file SHALL remain byte-for-byte unchanged and no native
  recovery operation SHALL be attempted

### Requirement: Current recovery identity SHALL be tri-state

For a current v3 entry, positive stale identity evidence SHALL be discarded as
safe stale cleanup, unverifiable identity SHALL remain retryable, and a full
match SHALL mutate and clear the entry only after restoration is verified.

#### Scenario: An unavailable recovery probe is retried

- **WHEN** a v3 process-start, executable, class, or token probe is unavailable
- **THEN** the entry SHALL remain in the active journal for a later retry and
  no native presentation mutation SHALL occur

#### Scenario: A matched v3 guest is restored

- **WHEN** all v3 identity evidence matches and native presentation restoration
  succeeds
- **THEN** the guest SHALL be restored, its exact capture token SHALL be
  removed, and only then may its journal entry be consumed

### Requirement: Tokenless legacy evidence SHALL require supervised recovery

Pending v1/v2 evidence SHALL have a read-only discovery operation and a
separate explicitly user-started recovery operation. Startup SHALL never
mutate a tokenless legacy HWND. Recovery SHALL enumerate live top-level
windows, require explicit candidate selection and confirmation, compare every
historical identity field present in the entry, require an explicit candidate
selection when more than one historical match exists, and refuse nonmatching
candidates.

#### Scenario: Pending discovery is read-only and identifies evidence

- **WHEN** a user runs the pending-recovery discovery command
- **THEN** it SHALL report pending-file/schema/entry counts and sanitized
  recorded-window status without changing a file or native window

#### Scenario: An unconfirmed candidate is never mutated

- **WHEN** the operator rejects a candidate, selects a mismatching candidate,
  or does not provide the exact confirmation
- **THEN** no presentation or property mutation SHALL occur and the pending
  bytes SHALL remain unchanged

#### Scenario: A new recovery generation guard is required

- **WHEN** the operator confirms a matching live target
- **THEN** recovery SHALL refuse any existing capture/recovery property, install
  a distinct ephemeral token, verify it, and revalidate it immediately before
  each placement, visibility, DWM, and token-removal mutation

### Requirement: Legacy recovery semantics SHALL reflect historical evidence

Supervised v1 recovery SHALL restore visibility only because v1 recorded no
placement or process-instance state. Supervised v2 recovery MAY restore its
recorded placement, visibility, and transition state only after all available
historical identity matches and the new generation guard is established.

#### Scenario: Explicit v1 recovery is visibility-only

- **WHEN** a user confirms a valid v1 target and the temporary generation guard
  remains valid
- **THEN** recovery SHALL call/show and verify visibility without fabricating
  geometry or maximize state

#### Scenario: Explicit v2 recovery restores recorded presentation

- **WHEN** a user confirms a valid v2 target and the temporary generation guard
  remains valid
- **THEN** recovery SHALL restore the recorded presentation fields, verifying
  each post-state and retaining evidence if any mutation fails

### Requirement: Pending entry retirement SHALL be atomic and entry-scoped

Successful recovery SHALL make an auditable durable resolution marker before
atomically removing only the resolved entry. Unresolved sibling entries and
unknown JSON fields SHALL remain intact. A cleanup failure SHALL retain the
original pending evidence and prevent repeated native recovery of the same
already-resolved entry.

#### Scenario: One entry resolves without destroying siblings

- **WHEN** entry A in a multi-entry pending file is recovered successfully
- **THEN** entries B and later SHALL remain recoverable with their original
  fields and the file SHALL not be replaced by an empty or unrelated journal

#### Scenario: Recovery failure retains evidence

- **WHEN** target selection, temporary-token installation, placement,
  visibility, DWM restoration, or token removal fails
- **THEN** the pending entry SHALL remain available and no success marker SHALL
  authorize its retirement
