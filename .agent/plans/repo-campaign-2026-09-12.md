# Plan: repository-wide engineering campaign (2026-09-12)

**Status:** active
**Owner/session:** OpenCode goal continuation (overnight campaign)
**Ground truth:** `git branch --show-current`, `git rev-parse HEAD`, `git status`
(dynamic; never embed the self-referential SHA here)

## Branch split (integration note)

The campaign runs on two review surfaces:

- `ui/refero-frontend-overhaul` (PR #13) carries W1–W3, the frontend-coupled
  workstreams, plus its copy of this plan.
- `campaign/runtime-perf-and-coverage-2026-09-12` (this branch, based on `main`)
  carries W4–W5, the non-UI workstreams.

This file is the superset record. When the branches are integrated, keep this
superset version. There is no source-file overlap between the branches beyond
this plan file.

## Objective

Deep, evidence-driven campaign across the repository: establish truth, build a
prioritized backlog, then implement, test, verify, document, and commit verified
improvements. Prefer root causes, measured claims, and truthful incompleteness.

## Repository assessment (evidence)

- Mature Windows/WPF product with an unusually strong qualification surface:
  `scripts/validate.ps1` (audited restore, Release builds, headless unit gate,
  resource-lifecycle gate, native-ABI self-test, doctor/pending-recovery/
  bundle-privacy smokes, OpenSpec validation, publish smoke), 812 unit tests on
  `main`, a real-input ValidationDriver (173/173 deterministic self-tests), and
  38 canonical OpenSpec specs.
- No `TODO/FIXME/HACK` markers in application code; the 2026-08-22 improvement
  review's dead-code findings (budget sink, duplicate refusal state, dead
  shims) and the Wave 4 self-test migration are already resolved; Wave 3 closed
  the split-presentation ownership findings; `docs/ARCHITECTURE.md` matches the
  current design.
- Hosted CI is externally blocked by a GitHub Actions billing failure (runs
  stop before any step); the identical canonical gates run locally.

## Workstreams

### W1 — Runtime WPF binding-error elimination + binding diagnostics (COMPLETE, PR #13)

Opt-in `Infrastructure/BindingTraceDiagnostics`; a real-app drive logged 13
binding errors, all fixed (inline capture panel DataContext, theme ListBoxItem
alignment bindings). Full drive now logs **0 binding errors**. Tests added.

### W2 — Accessibility/keyboard runtime audit (COMPLETE, PR #13)

Named the capture lists, added `GroupSelectedHelpText` with disabled-hover
tooltips, removed the native content marker as a keyboard tab stop, aligned
workspace terminology. Verified at runtime by keyboard. Tests added.

### W3 — Dense tab-strip rendering (COMPLETE, PR #13)

Eight tabs squeezed to 22 px with clipped icons and the active tab off-screen;
the strip now grows by the scrollbar row (`MinHeight`) and the active tab
scrolls into view. Contract guard added.

### W4 — Baseline verification, startup measurement, and doc accuracy (COMPLETE, this branch)

- First local replication of the CI deterministic step: `--selftest all`
  = 173/173 PASS with a passing run manifest.
- Performance baseline via `scripts/perf.ps1` (picker refresh warm 1.8 ms,
  persistence changed-save 4.0 ms, diagnostic-trace 100k 38.7 ms).
- Startup measured with temporary instrumentation (reverted):
  `MainWindow.InitializeComponent` ~334 ms and `Show()` ~680 ms dominate;
  `PublishReadyToRun=true` is already configured, so the shipped artifact is
  already the faster path (dev build ~1.8 s warm vs R2R publish ~1.6 s warm).
  No safe, non-speculative startup optimization remained, so none was made.
- Added `scripts/measure-startup.ps1` (safe, non-gating, re-runnable).
- Fixed live documentation drift: `ONBOARDING.md` non-existent test project
  path and non-canonical validate invocation; `docs/TESTING.md` obsolete
  self-test description; documented the new startup measurement.
- Gate: Debug/Release 0 warnings/0 errors; 812/812 both configs;
  `validate.ps1 -Release -Ci -Publish` exit 0 (38/38); release tooling 179/179.

### W5 — Security/robustness hazard sweep (NEXT, this branch)

Proportionate review of process execution, filesystem paths, network surface,
and dangerous API usage in the product; fix anything concretely actionable and
record evidence. Then final campaign validation matrix and report.

## Constraints

- Preserve the Shepherd/no-reparent architecture, automation IDs, and native
  presentation contracts.
- No speculative refactors of already-resolved structural findings.
- Do not commit QA harnesses, captures, or generated artifacts.

## Validation gates per workstream

`dotnet build TabDock.sln -c Debug|Release`, unit tests Debug+Release,
`scripts/validate.ps1 -Configuration Release -Ci -Publish`,
`scripts/release-tooling-tests.ps1`, plus any runtime audit applicable to the
workstream.

## Terminal conditions

Campaign ends only when locally executable work is exhausted (Condition A),
remaining work is externally blocked (Condition B), or further changes would be
speculative (Condition C) — recorded here and in `.agent/STATE.md`.
