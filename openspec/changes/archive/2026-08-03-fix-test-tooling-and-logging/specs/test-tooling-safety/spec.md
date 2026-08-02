# test-tooling-safety delta — fix-test-tooling-and-logging (new capability)

## ADDED Requirements

### Requirement: The Spike only reparents a window it spawned itself
`FindCmdWindow` SHALL verify, via `GetWindowThreadProcessId`, that a candidate console window belongs to the `cmd.exe` process the orchestrator spawned before returning it. A window that merely matches class or title (e.g. a pre-existing console the user owns) SHALL be skipped and the retry loop continued.

#### Scenario: A pre-existing console window is never touched
- **WHEN** the Spike runs while the user already has a console (or a window titled "...cmd.exe") open
- **THEN** that window is never `SetParent`'d, restyled, hidden, or killed by the Spike — only the orchestrator's own spawned `cmd.exe` window is used

### Requirement: Every Spike process spawn routes through the guarded-spawn pattern
All `Process.Start` call sites in the Spike — including the internal `taskkill` — SHALL go through `SpawnGuarded` (spawn cap, tracking, `KillAllTracked` on exit/timeout), per `docs/internal/guarded-spawn-pattern.md`.

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
