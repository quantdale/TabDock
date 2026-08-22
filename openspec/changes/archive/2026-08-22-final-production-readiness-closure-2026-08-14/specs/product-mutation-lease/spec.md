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

### Requirement: Product mutation ownership uses an explicit least-privilege DACL
The named mutation mutex SHALL be created or opened through the Windows ACL
API with a protected DACL owned by the current canonical user SID. The current
SID SHALL receive the `Synchronize`, `Modify`, and `ReadPermissions` rights
needed to wait, release, and verify the lease. The generated DACL SHALL NOT
grant mutation access to Everyone, World, Authenticated Users, inherited
principals, or unrelated local users. An existing object with denied access or
an unexpected owner/DACL SHALL fail closed, and acquisition SHALL never fall
back to an unsecured named-mutex constructor.

#### Scenario: Current user receives only the required lease rights
- **WHEN** TabDock builds the product mutation security descriptor
- **THEN** the protected DACL contains one explicit allow rule for the current SID with the required wait/release/read-permissions rights and no broad principal grant

#### Scenario: Foreign or pre-created object fails closed
- **WHEN** a same-name object is pre-created with denied access or a security descriptor that does not match the expected owner and DACL
- **THEN** TabDock refuses mutation-lease acquisition without weakening, replacing, or destroying that object

#### Scenario: Same-user elevated processes retain the logical lease
- **WHEN** normal and elevated TabDock processes run under the same Windows SID, regardless of session
- **THEN** they use the same `Global` lease name and the explicit SID rule permits normal mutex coordination without an integrity-level-specific name

### Requirement: Read-only commands remain lease-independent
Read-only diagnostics and pending-evidence discovery SHALL remain usable while
another process holds the per-user mutation lease.

#### Scenario: Diagnostics are not blocked by mutation ownership
- **WHEN** a normal TabDock process owns the product mutation lease
- **THEN** `--version`, `--doctor`, `--pending-recovery`, and `--support-bundle` retain their read-only behavior
