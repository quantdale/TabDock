# Historical checkpoint snapshot

Preserved during the documentation refresh on 2026-09-15. The text below is
historical session evidence, not current Git state or an instruction to resume
completed work. The residual repair was subsequently committed in `ea1ad43`.
Consult `.agent/STATE.md` for the current task.

# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — 2026-09-15 RESIDUAL MULTI-CAPTURE REPAIR

**Objective:** implement the confirmed pre-minimized incoming-guest
presentation repair from
`.agent/investigations/residual-multi-capture-black-screen-2026-09-15.md`.

**Status:** implementation complete in the working tree; not committed. The
repair restores an explicitly selected iconic guest through the existing
identity-checked Shepherd path before hiding the outgoing guest, while keeping
the passive iconic relayout guard and split authority unchanged. The broader
user-reported eventual bad-to-good sequence remains physically unverified
because supervised desktop input was unavailable.

### Validation

- Release focused presentation tests: 8/8 passed.
- Release solution tests: 838/838 passed.
- Debug solution tests: 838/838 passed.
- Release build: 0 warnings, 0 errors.
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: completed
  successfully (resource/visual synthetic gate PASS, OpenSpec 38/38, publish
  smoke PASS).
- Read-only doctor and native ABI self-test: PASS.

### Next action

Review the bounded diff and, if desired, commit only the source/test,
investigation, and state records. A supervised physical first-presentation run
is still required before claiming closure of the separate eventually-recovering
report.

## Current state — 2026-09-15 INITIAL-CAPTURE FIX AND CONSOLIDATION

**Objective:** independently diagnose and fix the initial-capture black
presentation defect, add behavior-level regression coverage, validate the
integrated result, and consolidate the repository to `main`.

**Status:** complete on `main`. The historical base is `main`/`origin/main`
`5868707`; the last substantive implementation commit is `17ebbbd`
(`fix: reconcile initial captured guest presentation`). Resolve the final
`HEAD` and `origin/main` dynamically; this file does not embed the SHA of its
own closure commit. The durable plan is
`.agent/plans/initial-capture-black-screen-2026-09-15.md` and the preservation
inventory/evidence is `.agent/investigations/initial-capture-black-screen-2026-09-15.md`.

### Completed

- Recorded branch, reachability, PR, worktree, stash, and untracked-file
  inventory before cleanup.
- Reconstructed capture → `Show`/`Loaded` → `ContentRendered` → native guest
  presentation, including single and split paths; a disposable WPF/HwndHost
  probe confirmed the first-render timing boundary.
- Reviewed PRs #15 and #16 independently. #16's first-render boundary is
  directionally correct; both existing source-text-only regression guards are
  replaced by behavior-level tests.
- Added a one-shot `InitialPresentationReconciliationPolicy` and a conditional
  `ContainerWindow` `ContentRendered` hook that re-reads the existing active
  guest authority and queues one coalesced final relayout. Shepherd identity,
  no-reparent, split, and z-order authorities remain unchanged.
- Red regression observed on the pre-fix code (2 failing assertions); the fixed
  seam now passes 4/4.
- Reviewed all baseline branches and PRs: campaign/UI tips were already in
  `main`; black-screen PRs #15 and #16 were superseded and closed; their remote
  branches were removed after unique-commit classification.
- Inspected and dropped the one redundant documentation stash. The disposable
  WPF probe was removed from `D:\Temp` after evidence capture.

### Validation so far

- `dotnet build TabDock.sln -c Release --no-restore`: 0 warnings, 0 errors.
- Focused presentation/lifecycle tests: 85/85 passed.
- Full Release solution tests: 834/834 passed.
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: exit 0 on the
  committed implementation SHA `17ebbbd`; Release builds,
  driver/GuineaPig/performance compile, 834/834 tests, resource lifecycle,
  native ABI, smokes, OpenSpec 38/38, and publish/version smoke all passed.
- Debug solution tests: 834/834 passed. Release tooling tests: 179/179
  passed. The Release build had 0 warnings and 0 errors.
- Real-input visual qualification was not run: the current CUA surface exposes
  no native apps, and `docs/TESTING.md` forbids unattended synthesized input.
  Headless synthetic lifecycle coverage passed but cannot replace that gate.
  Hosted run `34925077671` failed with both jobs reporting zero executed
  steps, an external runner/allocation failure rather than a code failure.

**Current phase:** handoff. Final dynamic Git verification must confirm the
closure commit is pushed, `main` equals `origin/main`, the working tree is
clean, and only the canonical branch/worktree remain.

---

## Prior state — 2026-09-12 CAMPAIGN INTEGRATED TO MAIN

**Objective:** the repository-wide engineering campaign
(`.agent/plans/repo-campaign-2026-09-12.md`) is complete and integrated; `main`
now carries the frontend overhaul (W1–W3), runtime/documentation work (W4),
the security sweep (W5), and the validation closure.

**Status (2026-09-14):** three OpenSpec changes archived and pushed to `main`
at `410b232`. Prior review surfaces remain at `136f91c`.
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

### Validation at integrated main

- `136f91c` (integration merge): all matrix steps exit 0; 827/827 both
  configs; `validate.ps1 -Release -Ci -Publish` exit 0 (OpenSpec 38/38,
  publish smoke); release-tooling 179/179; `--selftest all` 173/173;
  `launcher-empty-state-hint` scenario PASS.
- `d670c84` (final code SHA: tokenized views, including the container
  content void): all matrix steps exit 0; 829/829 both configs; the driver
  scenario renders launcher, picker, and container successfully. Later
  docs-only commits do not retag it.
- Per-surface exact-SHA matrices before merge: campaign `3c70ce0` (812/812),
  UI `105cb80` (827/827) — all steps exit 0 on both.

### Post-integration successor passes (2026-09-13)

1. Driver contract repair (on the UI surface before merge): launcher empty
   state located by `LauncherEmptyStateHeading`, picker retry by
   `CaptureRefresh`; scenario PASS on the real app.
2. `docs/FRONTEND.md` maintainer guide (tokens, chrome, airspace rule,
   automation-ID and binding-error contracts), linked from ARCHITECTURE.
3. Design-token cleanup: split tints and font stacks tokenized; unused
   `TdSuccessBrush`/`TdRaisedCard` removed; contract tests added for token
   usage, literal absence, and WindowChromeTheme/App.xaml palette lockstep.
4. Repository hygiene: the accidentally-named generated artifact tree
   (`AAAA…`, 44 directories of reproducible publish/packet output with no
   durable references) removed; temporary UI worktree removed; merged local
   branches deleted; personal paths and the device-account email sanitized in
   investigation records; README launcher label corrected.

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

No further locally executable workstream is identified. Remaining items are
external (Authenticode signing material, hosted CI billing/runner allocation,
mixed-DPI hardware for the blocked visual cells) or require new product
direction. Do not re-run resolved sweeps.
