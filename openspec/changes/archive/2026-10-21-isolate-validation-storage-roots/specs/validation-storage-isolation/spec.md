## Purpose

Protects production user storage by selecting and proving a run-owned product directory before qualification children can log, restore, clean, recover, or persist data.

## ADDED Requirements

### Requirement: Qualification storage selection SHALL be explicit and fail closed

Qualification children SHALL accept the paired bootstrap inputs `TABDOCK_VALIDATION_STATE_ROOT` and `TABDOCK_VALIDATION_RUN_ID` only when they identify a canonical absolute run-owned product directory with a matching ownership marker. Missing paired inputs, invalid markers, unavailable directories, reparse-point components, and overlap or aliasing with production product storage SHALL be rejected before product logging, cleanup, restore, rescue, or persistence. Invalid overrides SHALL NOT fall back to production storage. Without validation inputs, ordinary startup SHALL retain the normal Windows profile product directory.

#### Scenario: Verified root contains every product storage role

- **WHEN** a qualification child starts with a valid root/run pair and ownership marker
- **THEN** state, backups, journals, pending sources/sidecars, logs, hygiene and diagnostic inspection all resolve beneath that exact product directory

#### Scenario: Invalid override cannot select the real profile

- **WHEN** either input is absent or a marker, canonical path, ownership, availability, or alias check fails
- **THEN** the child exits nonzero before product storage/native recovery work and does not create a fallback log or modify production files

#### Scenario: Environment profile variables are not proof

- **WHEN** only APPDATA or LOCALAPPDATA is changed for a child
- **THEN** tooling does not treat the child as isolated or authorize a mutating qualification launch

### Requirement: A child-observed read-only probe SHALL prove isolation before mutation

A qualification-only `--validation-storage-probe` command SHALL run the same bootstrap validation without creating logs/state, installing hooks, acquiring a mutation lease, launching UI, or performing recovery. With valid explicit inputs it SHALL emit machine-readable effective-root, run-identity and storage-role information. The parent SHALL verify that proof against its requested canonical directory and exact candidate identity before launching a mutating child; that child's bootstrap SHALL validate its own root again. Missing, mismatched or invalid proof SHALL refuse the launch.

#### Scenario: Parent rejects a different effective directory

- **WHEN** a probe exits successfully but its reported effective directory, run identity or candidate identity differs from the parent's request
- **THEN** no mutating child is started and cleanup remains limited to independently proven run-owned artifacts

#### Scenario: Probe itself is non-mutating

- **WHEN** valid and invalid root fixtures are probed with sentinel product files present
- **THEN** the fixture/production file sets, bytes and last-write times remain unchanged and no native recovery mutation occurs

### Requirement: Storage isolation SHALL preserve existing mutation and cleanup authority

A validation root SHALL NOT weaken the existing same-user product-mutation lease or input/process qualification policy. Cleanup SHALL require current run ownership and SHALL be confined to the verified temporary directory and proven launched processes. Failure to establish root/ownership proof SHALL never authorize cleanup of production or unrelated storage.

#### Scenario: Another mutation owner already exists

- **WHEN** an isolated mutating child is requested while the existing same-user mutation lease is held
- **THEN** the child respects the same contention refusal and does not introduce a directory-specific bypass lease
