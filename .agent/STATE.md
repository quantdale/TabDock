# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — 2026-09-12 CAMPAIGN INTEGRATED TO MAIN

**Objective:** the repository-wide engineering campaign
(`.agent/plans/repo-campaign-2026-09-12.md`) is complete and integrated; `main`
now carries the frontend overhaul (W1–W3), runtime/documentation work (W4),
the security sweep (W5), and the validation closure.

**Status (2026-09-13):** both review surfaces merged to `main` at `136f91c`
(UI `105cb80` + campaign `0178390`); the plan-file conflict was resolved to the
superset record. PRs #13 and #14 are `MERGED` (GitHub detected the integration
merge commit). Hosted CI remains externally blocked (jobs fail with 0 steps
executed — Actions billing/runner allocation, not a source failure).

### Integrated workstreams

- W1 — runtime WPF binding-error elimination + opt-in
  `BindingTraceDiagnostics`; a full real-app drive logs 0 binding errors.
- W2 — accessibility/keyboard audit: named capture lists, self-explaining
  disabled primary action with disabled-hover tooltips, no dead content-marker
  tab stop, workspace terminology.
- W3 — dense tab strip grows by the scrollbar row and scrolls the active tab
  into view.
- W4 — baseline verification and startup measurement (`--selftest all`
  173/173; `scripts/measure-startup.ps1`; `ONBOARDING.md`/`docs/TESTING.md`
  drift fixed).
- W5 — security/robustness sweep: verified negative, no change required.
- Driver contract repair: launcher empty state is located by the stable
  `LauncherEmptyStateHeading` automation id and the picker scroll retry by
  `CaptureRefresh`; `launcher-empty-state-hint` PASS on the real app
  (the redesign's visible-copy change had broken the old lookup).

### Validation at integrated main `136f91c`

- Debug/Release builds 0 warnings; 827/827 unit Debug + 827/827 Release;
  `validate.ps1 -Release -Ci -Publish` exit 0 (OpenSpec 38/38, publish smoke);
  release-tooling 179/179; `--selftest all` 173/173; launcher scenario PASS.
- Per-surface exact-SHA matrices before merge: campaign `3c70ce0` (812/812),
  UI `105cb80` (827/827) — all steps exit 0 on both.

### Qualification status (external gates unchanged)

- Prior physical qualification candidate remains `fbc4d92` (`1.1.0`
  qualification-only); signing `NOT_CONFIGURED`; production eligibility
  `BLOCKED_EXTERNAL`.
- Environment limits: single 96-DPI monitor; mixed/high-DPI visual cells and
  supervised human gates remain `BLOCKED_CAPABILITY`/`BLOCKED_ENVIRONMENT`.

### Durable records

- Superset campaign plan: `.agent/plans/repo-campaign-2026-09-12.md`
- Frontend overhaul plan: `.agent/plans/refero-frontend-overhaul-2026-09-12.md`
- Prior hardening evidence: `.agent/investigations/` and
  `openspec/changes/archive/`.

### Next action

Continue bounded successor passes discovered after integration (maintainer
documentation for the frontend design system, review-finding drift cleanup,
repository hygiene). Remaining known limitations are external — signing
material, hosted CI billing, mixed-DPI hardware — or require new product
direction; do not re-run resolved sweeps.
