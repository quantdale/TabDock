# test-tooling-safety

## Purpose
Captures the ownership-validation, guarded-spawn compliance, and portability semantics of the Spike and ValidationDriver test tooling.
## Requirements
### Requirement: The Spike only reparents a window it spawned itself
`FindCmdWindow` SHALL verify, via `GetWindowThreadProcessId`, that a candidate console window belongs to the `cmd.exe` process the orchestrator spawned before returning it. A window that merely matches class or title (e.g. a pre-existing console the user owns) SHALL be skipped and the retry loop continued.

#### Scenario: A pre-existing console window is never touched
- **WHEN** the Spike runs while the user already has a console (or a window titled "...cmd.exe") open
- **THEN** that window is never `SetParent`'d, restyled, hidden, or killed by the Spike - only the orchestrator's own spawned `cmd.exe` window is used

### Requirement: Every Spike process spawn routes through the guarded-spawn pattern
All `Process.Start` call sites in the Spike - including the internal `taskkill` - SHALL go through `SpawnGuarded` (spawn cap, tracking, `KillAllTracked` on exit/timeout), per `docs/internal/guarded-spawn-pattern.md`.

#### Scenario: A failed Spike run kills every process it started
- **WHEN** the Spike aborts on timeout, failure, or Ctrl+C after spawning processes
- **THEN** every spawned process, including the `taskkill` helper, was counted against the cap and is tracked and killed by the guardrails

### Requirement: Spike child modes validate their HWND/PID arguments
The `--host` and `--checker` entry points SHALL validate that a parsed HWND is a real window (`IsWindow`) of the expected class and owning process before any `SetParent`/restyle, and SHALL exit with a clear error on malformed arguments instead of an unhandled `FormatException`.

#### Scenario: A bogus --host HWND is refused
- **WHEN** the Spike is invoked as `--host <hwnd>` where `<hwnd>` is not a window owned by the expected process
- **THEN** it exits with a clear error and performs no reparenting or restyling

### Requirement: The ValidationDriver is portable across machines
The driver SHALL resolve `TabDockExe`/`PigExe` relative to its own assembly location and SHALL locate browsers by probing well-known install paths / PATH, rather than hardcoded absolute paths under a specific developer's machine.

#### Scenario: The driver runs on a fresh machine
- **WHEN** the ValidationDriver is built and run on a machine other than the original development box
- **THEN** it locates TabDock, the guinea-pig app, and an available browser without source edits

### Requirement: Browser-driven scenarios use fresh profiles
Every scenario launching Chrome/Edge SHALL use a per-run unique `--user-data-dir` (the `FreshProfileDir` pattern), so a profile left locked or "crashed" by a previous force-killed run cannot surface a "Restore pages?" bubble that breaks window matching.

#### Scenario: No scenario reuses a fixed profile directory
- **WHEN** any browser scenario runs twice in a row, with the first run force-killed
- **THEN** the second run starts with a clean, unique profile and window matching is unaffected by the prior run's crash state

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
