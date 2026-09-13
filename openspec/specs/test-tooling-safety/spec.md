# test-tooling-safety

## Purpose
ValidationDriver ownership, spawn, isolation, shard, and input-identity safety for real-input qualification. The experimental reparent Spike is not a live target.
## Requirements
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

### Requirement: Live tooling SHALL NOT present the deleted Spike as current
Canonical live agent instructions, the testing playbook, the agent guide
solution map, the qualification-script synopsis, and the optional performance
build matrix SHALL describe only projects that exist in the repository: the
main application, UnitTests, ValidationDriver, GuineaPig, and the
compile-only Performance harness. They SHALL NOT list `Spike/TabDock.Spike`
as a solution member, a qualification build target, or a live safety
obligation. Guarded-spawn documentation MAY retain the historical Spike
incident as provenance without instructing operators to build or run Spike.
Historical audit records MAY mention Spike as past tense.

#### Scenario: Qualification docs match the solution
- **WHEN** an operator reads `AGENTS.md`, `docs/TESTING.md`, or the
  `validate.ps1` synopsis
- **THEN** those texts do not claim that the solution or canonical
  qualification build compiles Spike

#### Scenario: Performance matrix does not compile a missing Spike project
- **WHEN** `scripts/perf.ps1` is invoked with its build-matrix switch
- **THEN** it restores and compiles only in-repo projects and does not invoke
  `dotnet build` on `Spike\TabDock.Spike\TabDock.Spike.csproj`

#### Scenario: Test-tooling spec no longer obligates a reparent Spike
- **WHEN** an implementer applies `test-tooling-safety`
- **THEN** no remaining requirement asks them to spawn, reparent, or
  HWND-validate a Spike process
