## ADDED Requirements

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

## REMOVED Requirements

### Requirement: The Spike only reparents a window it spawned itself
**Reason:** The experimental `Spike/TabDock.Spike` project was deleted in
`9291716`. TabDock's only capture backend is Shepherd; no in-repo tool may
reparent a guest. Keeping this requirement would force recreation of a
reparent spike.
**Migration:** Do not restore Spike. Window-ownership and guarded-spawn rules
for live tooling remain on ValidationDriver (portability, fresh browser
profiles, shard budgets, state isolation, and input-identity proofs). The
historical incident stays documented in
`docs/internal/guarded-spawn-pattern.md`.

### Requirement: Every Spike process spawn routes through the guarded-spawn pattern
**Reason:** There is no Spike process and no Spike `Process.Start` call site.
**Migration:** ValidationDriver spawn/cleanup continues to use the existing
guarded process wrapper. Do not add a new Spike orchestrator.

### Requirement: Spike child modes validate their HWND/PID arguments
**Reason:** The `--host` and `--checker` Spike entry points no longer exist.
**Migration:** No replacement entry point. ValidationDriver already proves
HWND/PID/executable ownership before sending input.
