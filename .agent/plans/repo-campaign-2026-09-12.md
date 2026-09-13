# Plan: repository-wide engineering campaign (2026-09-12)

**Status:** active — final validation matrix complete on both review surfaces;
integration into `main` is the remaining step
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

### W5 — Security/robustness hazard sweep (COMPLETE, this branch)

Proportionate review of the product assembly for process execution, network
surface, dangerous native APIs, deserialization, registry writes, and
path/user-input handling. Evidence:

- No `Process.Start`, shell execution, `HttpClient`/`WebClient`/`Socket`,
  `WriteProcessMemory`/`VirtualAllocEx`/`CreateRemoteThread`, registry writes,
  or reflection-based polymorphic deserialization anywhere in the product
  (all process-spawning hits are ValidationDriver harness code).
- State and journal JSON use the source-generated `TabDockJsonContext`;
  the recovery resolution ledger reads only from the appdata recovery
  directory and handles malformed input by failing closed with an error.
- The `--doctor`/`--support-bundle`/diagnostic-hotkey exports write only to
  user-directed or timestamped paths and are redacted in logs; support-bundle
  privacy is additionally gated by tests and the validate.ps1 privacy smoke.
- The elevation guard is explicitly fail-closed (an indeterminate elevation
  probe blocks capture unless TabDock itself is elevated).

No actionable finding; no code change required. Recorded as a verified
negative so a future session does not re-run the same sweep by default.

### Final validation matrix (COMPLETE)

Exact-SHA gates on the final commit of each review surface — all steps
`exit=0` (logs `tabdock-matrix-*`):

- `campaign/runtime-perf-and-coverage-2026-09-12` @ `3c70ce0`: Debug/Release
  builds 0 warnings; 812/812 unit Debug + 812/812 unit Release (inside
  `validate.ps1`); `validate.ps1 -Release -Ci -Publish` exit 0 (OpenSpec 38/38,
  publish smoke); release-tooling 179/179. Pushed to `origin`.
- `ui/refero-frontend-overhaul` @ `105cb80` (PR #13): Debug/Release builds
  0 warnings; 827/827 unit Debug + 827/827 unit Release; `validate.ps1
  -Release -Ci -Publish` exit 0; release-tooling 179/179. Pushed to `origin`;
  PR #13 head updated.

The original matrix run at `4c0675d` was green on those same gates, but the
review pass found the launcher redesign had broken the driver's
`launcher-empty-state-hint` scenario (it matched the removed visible copy);
the scenario is `includeInAll`, so the supervised scenario lane would have
failed. Repaired at `105cb80`: the heading carries a stable
`LauncherEmptyStateHeading` automation id located by the driver, and the
picker scroll retry now locates `CaptureRefresh` by id (its exact `"Refresh"`
name lookup never matched even on `main`). Verified on the real app:
`launcher-empty-state-hint` PASS (UiAutomationRead lane).

Hosted CI remains externally blocked: every run stops before any step
(`build` and `native-abi-evidence` fail with 0 steps executed — Actions
billing/runner allocation, not a source failure). The local exact-SHA gates
above are the available qualification. Integration of both surfaces into
`main` follows (plan-file conflict resolved to this superset version).

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
