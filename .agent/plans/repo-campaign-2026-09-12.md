# Plan: repository-wide engineering campaign (2026-09-12)

**Status:** active
**Owner/session:** OpenCode goal continuation (overnight campaign)
**Ground truth:** `git branch --show-current`, `git rev-parse HEAD`, `git status` (dynamic; never embed the self-referential SHA here)

## Objective

Deep, evidence-driven campaign across the repository: establish truth, build a
prioritized backlog, then implement, test, verify, document, and commit verified
improvements. Prefer root causes, measured claims, and truthful incompleteness.

## Repository assessment (evidence)

- Mature Windows/WPF product with an unusually strong qualification surface:
  `scripts/validate.ps1` (audited restore, Release builds, headless unit gate,
  resource-lifecycle gate, native-ABI self-test, doctor/pending-recovery/
  bundle-privacy smokes, OpenSpec validation, publish smoke), 820+ unit tests,
  a real-input ValidationDriver, and 38 canonical OpenSpec specs.
- The frontend overhaul (PR #13, branch `ui/refero-frontend-overhaul`) is
  implemented and visually qualified; this campaign continues it with runtime
  behavioral audits rather than re-reviewing pixels.
- No `TODO/FIXME/HACK` markers in application code; `KNOWN_ISSUES.md` and
  `docs/internal/` hold historical, resolved findings.
- Hosted CI is externally blocked: GitHub Actions refuses to allocate runners
  ("recent account payments have failed or your spending limit needs to be
  increased"). The identical canonical gates run locally.
- Environment limits: one 96-DPI monitor; real-input validation needs a
  supervised interactive desktop, which is available but shared with the user.

## Workstreams

### W1 — Runtime WPF binding-error elimination + binding diagnostics (COMPLETE)

Evidence: added `Infrastructure/BindingTraceDiagnostics` (opt-in
`TABDOCK_TRACE_BINDINGS=<log path>`), drove the real Release app through
launcher, picker (search, zero-result, dropdown), inline capture panel,
container tabs, workspace menu, and split under trace.

- Found 13 binding errors per run.
- 11x: the inline capture panel's bindings resolved against the container's
  `GroupViewModel` before the panel's `CapturePickerViewModel` was injected.
  Fixed with an explicit `DataContext="{x:Null}"` contract on the panel.
- 2x: theme `ListBoxItem` alignment bindings (`FindAncestor ItemsControl`)
  evaluated before each list's `ItemContainerStyle`. Fixed with explicit
  app-level `ListBoxItem` alignment defaults in `App.xaml`.
- Verified: full drive now logs **0 binding errors**.
- Tests: `BindingTraceDiagnosticsTests` (2), `FrontendDesignContractTests`
  additions (inline-panel context contract, app-level list defaults).
- Reproducible audit: `.visual-validation-runs/frontend-qa/run-binding-audit.ps1`
  (local, gitignored).

### W2 — Accessibility/keyboard runtime audit (NEXT)

Against `openspec/specs/accessibility-keyboard-completeness`: enumerate every
interactive element in launcher/picker/container with UIA; verify names, stable
IDs, help text, disabled states, focus reachability, Enter/Space activation,
Escape behavior, and logical tab order; fix gaps; add contract tests.

### W3 — Dense tab-strip rendering

Reproduce tab-strip overflow with many tabs at narrow widths; the 42px strip
plus the custom 11px horizontal scrollbar may not coexist cleanly.

### W4 — Non-UI robustness/coverage/performance (branch off `main`)

To be selected from measurement: startup/picker/capture latency baselines,
identity-gate and persistence edge coverage, and any measured hotspot. Kept on
a separate main-based branch so PR #13 stays a focused review surface.

## Constraints

- Preserve the Shepherd/no-reparent architecture, automation IDs, and native
  presentation contracts. Frontend-coupled work continues on PR #13's branch;
  non-UI work goes to a main-targeting branch.
- Do not commit QA harnesses, captures, or generated artifacts.

## Validation gates per workstream

`dotnet build TabDock.sln -c Debug|Release`, unit tests Debug+Release,
`scripts/validate.ps1 -Configuration Release -Ci -Publish`,
`scripts/release-tooling-tests.ps1`, plus the runtime audit applicable to the
workstream.

## Terminal conditions

Campaign ends only when locally executable work is exhausted (Condition A),
remaining work is externally blocked (Condition B), or further changes would be
speculative (Condition C) — recorded here and in `.agent/STATE.md`.
