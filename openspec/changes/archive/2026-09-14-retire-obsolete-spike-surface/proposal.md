## Why

Commit `9291716` deleted the experimental `Spike/TabDock.Spike` reparent
survival project. Canonical live surfaces still treat it as present:
`openspec/specs/test-tooling-safety` still requires Spike spawn/HWND safety,
`AGENTS.md` and `docs/internal/AGENT_GUIDE.md` still place Spike in the
solution, `docs/TESTING.md` and `scripts/validate.ps1` still claim the
qualification build covers Spike, and `scripts/perf.ps1 -IncludeBuildMatrix`
still runs `dotnet build Spike\TabDock.Spike\TabDock.Spike.csproj`. That matrix
cannot succeed on current HEAD. Agents following those specs can also try to
recreate a reparent Spike, which contradicts Shepherd. The same archive
leftover class left 13 main specs with `Purpose: TBD - created by archiving…`,
and `README.md` still names the removed `PresentationOperationBudget` suite as
a hosted CI gate.

## What Changes

- Remove the three Spike-only requirements from `test-tooling-safety`. Keep
  ValidationDriver portability, isolation, shard, and input-safety
  requirements.
- Require that live canonical docs and scripts name only in-repo build
  targets (main app, UnitTests, ValidationDriver, GuineaPig, Performance).
- Stop `scripts/perf.ps1` from compiling the deleted Spike project; keep
  compiling the performance runner and remaining live projects.
- Rewrite `test-tooling-safety` Purpose and the 13 archive-placeholder
  Purpose lines so they describe the live capability instead of the archive
  stub. Historical audit documents under `docs/audits/` and
  `docs/internal/` provenance records stay as history.
- Correct the README claim that hosted CI gates split presentation through
  `PresentationOperationBudget`.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `test-tooling-safety`: remove Spike reparent/spawn/HWND requirements; add a
  requirement that live tooling and canonical docs MUST NOT present the
  deleted Spike as a current build or safety target.
- `ci-tooling`: require the optional performance build matrix to compile only
  projects that exist in the repository (solution, ValidationDriver, GuineaPig,
  performance runner) and not a deleted Spike path.

## Impact

- Specs: `openspec/specs/test-tooling-safety/spec.md`,
  `openspec/specs/ci-tooling/spec.md`, plus Purpose-only edits on the 13
  archive-placeholder specs listed in design.md.
- Scripts: `scripts/perf.ps1`, `scripts/validate.ps1` (comment/synopsis only;
  it does not currently invoke Spike).
- Canonical docs: `AGENTS.md`, `docs/TESTING.md`, `docs/internal/AGENT_GUIDE.md`,
  `README.md`.
- No product runtime behavior, Shepherd, persistence, or ValidationDriver
  scenario change. Not **BREAKING**. Historical Spike discussion in archived
  audits remains.
