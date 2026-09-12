# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — REPOSITORY CAMPAIGN COMPLETE (LOCALLY)

**Objective:** deep repository-wide engineering campaign (correctness, testing,
reliability, accessibility, performance, architecture, DX, documentation).

**Status:** all discovered locally executable workstreams are complete and
validated; two main-targeting review surfaces are pushed. Hosted CI remains
externally blocked by a GitHub Actions billing failure.

### Branch map

- `ui/refero-frontend-overhaul` (PR #13) — W1–W3 (frontend-coupled).
- `campaign/runtime-perf-and-coverage-2026-09-12` (PR #14) — W4–W5 (non-UI)
  and the superset campaign record `.agent/plans/repo-campaign-2026-09-12.md`.
  There is no source overlap between the branches beyond that plan file; keep
  the superset on integration.

### Completed workstreams

1. **W1 — runtime WPF binding errors + diagnostics (PR #13).** Opt-in
   `BindingTraceDiagnostics`; a real-app drive found 13 binding errors, all
   fixed; the drive now logs zero.
2. **W2 — accessibility/keyboard audit (PR #13).** Named capture lists,
   self-explaining disabled primary action with disabled-hover tooltips,
   removed the native content marker as a keyboard tab stop, aligned
   workspace terminology; verified by keyboard at runtime.
3. **W3 — dense tab strip (PR #13).** Eight tabs used to squeeze to 22 px with
   clipped icons and the active tab off-screen; the strip now grows by the
   scrollbar row and the active tab scrolls into view.
4. **W4 — baseline verification + startup measurement + doc accuracy
   (PR #14).** First local `--selftest all` replication (173/173 PASS);
   `perf.ps1` baseline; startup measured (~1.6 s warm shipped R2R publish vs
   ~1.8 s dev build; `PublishReadyToRun` already configured, no safe further
   optimization); new `scripts/measure-startup.ps1`; fixed stale
   `ONBOARDING.md`/`docs/TESTING.md` claims.
5. **W5 — security/robustness sweep (PR #14).** No product-side process
   execution, network, dangerous native APIs, registry writes, or polymorphic
   deserialization; recovery reads are appdata-scoped and fail closed;
   elevation guard is fail-closed. Verified negative, no change required.

### Validation (exact tips)

- PR #13 `bc7df68`: Debug/Release builds 0 warnings/0 errors; 827/827 unit
  tests both configs; `validate.ps1 -Configuration Release -Ci -Publish`
  exit 0 (38/38); `release-tooling-tests.ps1` 179/179; runtime drive logs zero
  binding errors.
- PR #14 `e2c0397`: Debug/Release builds 0 warnings/0 errors; 812/812 unit
  tests both configs (main baseline); `validate.ps1 -Release -Ci -Publish`
  exit 0 (38/38); `release-tooling-tests.ps1` 179/179; driver
  `--selftest all` 173/173 PASS.

### External / not product defects

- **Hosted CI:** GitHub Actions refuses runner allocation ("recent account
  payments have failed or your spending limit needs to be increased") for
  every run on both branches; the identical canonical gates pass locally.
- 125%/150% DPI visual cells are unavailable on this single 96-DPI host.

### Next action

Human review and merge of PR #13 and PR #14; after integration, keep the
superset campaign plan. No further locally executable workstream was found
without speculation — a future campaign should target mixed-DPI hardware
qualification or a new product direction, not the resolved items above.
