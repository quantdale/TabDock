# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — REPOSITORY CAMPAIGN ACTIVE

**Objective:** deep repository-wide engineering campaign (correctness, testing,
reliability, accessibility, performance, architecture, DX, documentation).

**Plan:** `.agent/plans/repo-campaign-2026-09-12.md` (source of truth;
workstreams W1–W4 with status).

**Branch policy:** frontend-coupled work continues on
`ui/refero-frontend-overhaul` (PR #13, main-targeting); non-UI work will use a
separate main-based branch.

### Completed this session

1. **W1 — runtime WPF binding-error elimination + binding diagnostics.**
   Added opt-in `Infrastructure/BindingTraceDiagnostics`
   (`TABDOCK_TRACE_BINDINGS=<log path>`), drove the real Release app through
   launcher, picker, inline panel, container, menus, and split under trace:
   13 binding errors found, all fixed (11 = inline panel resolving against the
   container's `GroupViewModel` before its own view-model was injected;
   2 = theme `ListBoxItem` alignment bindings evaluated before each list's
   container style). Re-run logs **0 binding errors**.
   Tests added: `BindingTraceDiagnosticsTests` (2),
   `FrontendDesignContractTests` guards (2).

2. **W2 — accessibility/keyboard runtime audit.** Named the capture lists,
   added `GroupSelectedHelpText` + disabled-hover tooltips (admission first,
   then selection), dropped the native content marker as a keyboard tab stop,
   and aligned tooltip workspace terminology. Verified at runtime: Space
   toggles a focused row and enables the primary action, Escape closes the
   picker, the marker is gone from the tab-stop set.
   Tests added: help-text unit coverage, accessibility contract test.
   Full suite: 826/826 Debug+Release.

### Prior completed work (frontend overhaul, PR #13)

Runtime visual qualification and eight rendering fixes: DWM dark chrome,
templated system-themed controls (ComboBox/ScrollBar/MenuItem/ToolTip),
airspace-safe empty-workspace popup, hover states, non-shifting focus. PR #13
head is `2e3f885` plus this campaign's commits; hosted CI remains blocked by a
GitHub Actions billing failure (external).

### Externally blocked (not product defects)

- Hosted CI runner allocation: GitHub Actions billing/spending limit.
- 125%/150% DPI cells: single 96-DPI monitor on this host.
- Pending-recovery and capture-admission-blocked runtime states require
  manipulated journal/health state; structurally covered by tests.

### Next action

W3 — dense tab-strip rendering with many tabs at narrow widths, then select
 the highest-value non-UI workstream (W4) on a main-based branch.

Update this file at each workstream completion, validation milestone, or
blocker, and before final handoff.
