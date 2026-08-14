## Purpose

Defines exclusive product mutation ownership for one Windows user across that
user's terminal/RDP sessions without allowing an unrelated user's TabDock
state to block or interfere with it.

## ADDED Requirements

### Requirement: Product mutation ownership is per-user and cross-session
The normal TabDock process and supervised mutating recovery SHALL use one
named mutex in the `Global` namespace whose name is derived from the current
Windows user's canonical SID. Processes for the same SID SHALL contend across
sessions; different SIDs SHALL use different logical leases.

#### Scenario: Same user contends across sessions
- **WHEN** two mutating TabDock processes run under the same Windows SID in different sessions
- **THEN** only one acquires the product mutation lease

#### Scenario: Different users do not contend
- **WHEN** two mutating TabDock processes run under different Windows SIDs
- **THEN** each user's lease name is different and neither blocks the other user's independent state

### Requirement: User identity failure and unsafe names fail closed
If the current SID cannot be read or canonicalized, mutating startup SHALL
fail without acquiring an unscoped fallback mutex. SID-derived names SHALL
reject malformed or unsafe input and SHALL not use raw username text as the
security boundary.

#### Scenario: Missing SID does not fall back to a machine-global lease
- **WHEN** current-user identity lookup fails
- **THEN** lease acquisition returns failure without creating `Global\\TabDock` or another predictable unscoped name

#### Scenario: Abandoned ownership remains recoverable
- **WHEN** the previous same-user owner terminates while holding the lease
- **THEN** the next same-user waiter receives ownership through normal abandoned-mutex semantics and can continue

### Requirement: Read-only commands remain lease-independent
Read-only diagnostics and pending-evidence discovery SHALL remain usable while
another process holds the per-user mutation lease.

#### Scenario: Diagnostics are not blocked by mutation ownership
- **WHEN** a normal TabDock process owns the product mutation lease
- **THEN** `--version`, `--doctor`, `--pending-recovery`, and `--support-bundle` retain their read-only behavior
