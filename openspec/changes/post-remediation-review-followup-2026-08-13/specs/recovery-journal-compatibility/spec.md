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
