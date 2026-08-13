## ADDED Requirements

### Requirement: Validation artifacts SHALL be configurable and discoverable
ValidationDriver SHALL support explicit configuration, RID, TabDock path, and
GuineaPig path options, with deterministic discovery for standard Debug and
Release outputs. Help text and docs SHALL describe the actual resolution.

#### Scenario: A Release driver uses Release artifacts
- **WHEN** the driver is invoked with `--configuration Release`
- **THEN** it resolves the Release TabDock and GuineaPig outputs without source edits

### Requirement: Every registered scenario SHALL belong to a named shard
The runner SHALL reject or report any scenario not assigned to a known category,
and shard execution SHALL retain the existing guarded spawn and identity rules.

#### Scenario: A shard has bounded safety
- **WHEN** a named shard runs
- **THEN** its scenarios execute under the existing per-scenario and per-run caps and cleanup guarantees

#### Scenario: A growing scenario family is decomposed before its budget is exceeded
- **WHEN** one logical scenario family would exceed the fixed driver budget as
  a single process
- **THEN** its registered scenarios are assigned to multiple named sub-shards,
  each retaining the existing per-process spawn and time limits, and `--list`
  exposes every sub-shard and assignment

#### Scenario: A transient popup does not block a safe target transition
- **WHEN** a prior verified input target is a short-lived validation popup and
  that popup closes before the next click
- **THEN** the driver independently revalidates the current point root against
  its registered process-start, PID, executable, class, and HWND identity
  scope, permits the transition only when those checks pass, and never uses the
  destroyed popup as a reason to target an unregistered window

### Requirement: Validation state isolation SHALL include the backup file
Before a supervised scenario, ValidationDriver SHALL isolate both
`%APPDATA%\TabDock\state.json` and `state.json.bak`, because the product is
required to recover a valid backup when the primary is missing. Cleanup SHALL
restore both files, including the originally-absent case, without touching
unrelated user state.

#### Scenario: A stale backup cannot repopulate an empty scenario
- **WHEN** the user's primary is isolated and a stale valid backup exists
- **THEN** the scenario starts with no persisted groups and cleanup restores the
  original primary and backup files
