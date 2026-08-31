# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — USER-REPORTED PRESENTATION INTEGRITY

**Objective:** resolve the four user-reported presentation failures (chrome occlusion via accent/rename/split/+ panel, off-center title, guest maximize/fullscreen/monitor escape, unreliable top-layer) with an evidence-first, bounded, Shepherd-preserving implementation and demonstrable regression coverage.

**Plan:** `openspec/changes/2026-08-31-user-reported-presentation-integrity/` (proposal, design, tasks)

**Status:** implementation and deterministic qualification complete; physical supervised qualification honestly blocked; ready to commit and push on `main`.

### Current phase

- Orientation: complete — Git state dynamically resolved (`main` at `e4787c7769c333bd750582d74692da0a573c1727`, clean), `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, canonical specs, and active OpenSpec change read before editing.
- Investigation: complete — durable record at `.agent/investigations/presentation-integrity-2026-08-31.md` with proven vs rejected hypotheses, report classification, and evidence plan. Static source analysis proved: (a) `BeginChromePopup` + `RaiseContainerForChrome(HWND_TOP)` on opaque container + broad `IsContainerChromeInteractionActive` suppression blanks guest; (b) `WinEventMonitor` omitted `EVENT_OBJECT_LOCATIONCHANGE` leaving no signal for guest geometry/zoom changes; (c) caption `Auto|*|Auto…` left-aligns title. No supervised SendInput issued (no lease) — physical reproduction honestly blocked.
- Regression harness: complete — 18 new deterministic cases: `GuestPresentationDriftPolicyTests` (10), `CaptionCenteringTests` (2), `PresentationChromeIntegrityTests` (6); updated `WinEventMonitorTests` (8-hook cycle); harness adds `guest-maximize-contained` (`core-lifecycle`, 128 dispatchable total, generation unchanged) plus existing `group-dropdown-stability` etc. point-ownership/client-render guards.
- Implementation: complete — caption `* Auto *` true-center (`Views/ContainerWindow.xaml`); depth-scoped popup tracking (`_popupChromeDepth`, `CHROME[popup-open]`/`CHROME[popup-closed-restore-request]`) without opaque container raise; inline capture composes via shrunken `GetContentAreaScreenRect`; visual vs foreground predicates split (`PairZOrderBehindGuest` and layout now gate only on `_closePromptOpen`); filtered `EVENT_OBJECT_LOCATIONCHANGE` hook + per-HWND coalescing (`RepairKind.LocationDrift`) + pure `GuestPresentationDriftPolicy` → `ReconcilePresentationDrift` via existing Shepherd authority with refusal guard.
- Validation: complete — `dotnet build TabDock.sln -c Release` 0 warnings; `dotnet test TabDock.UnitTests -c Release` **725/725 PASS** (prev 707 +18); `dotnet build ValidationDriver -c Release` PASS; `--list` 128 dispatchable, shards within budgets; `group-dropdown-stability`, `contextmenu-render-stability`, etc. deterministic gates still pass; physical `all` run honestly `BLOCKED_ENVIRONMENT` without supervised lease.
- Reconciliation: complete — `docs/ARCHITECTURE.md` (GuestPresentationDriftPolicy, LOCATIONCHANGE coalescing, `* Auto *` caption), `docs/TESTING.md` (new deterministic suites + `guest-maximize-contained`), tasks file reconciled to actual depth+LOCATIONCHANGE+pure policy, investigation closed.

### Mainline checkpoint

- Session start `origin/main` was `e4787c7769c333bd750582d74692da0a573c1727` (verified `git rev-parse HEAD` and `origin/main` identical).
- Worktree was clean at start; final SHA to be resolved dynamically after commit and push.
- Previous campaign `resource-lifecycle-hardening` remains archived; this campaign is a direct `main` development (no permanent staging branch).
- Strict OpenSpec validation currently 34/34 canonical specs (before sync of new delta); active change `2026-08-31-user-reported-presentation-integrity` holds 2 delta specs (`presentation-integrity`, `ui-ux-hardening` delta) to be synced after merge.

### Completed work (this campaign)

- `Views/ContainerWindow.xaml` — `* Auto *` caption true-center, title/rename `HorizontalAlignment Center` `MaxWidth 220` trimming without covering side controls.
- `Views/ContainerWindow.xaml.cs` — removed container raise from `BeginChromePopup`, added `_popupChromeDepth` depth counter, narrowed `PairZOrderBehindGuest`/`LayoutSplitPanes`/`LayoutShepherdActiveWindow` visual guards to `_closePromptOpen` only, made inline capture compose without raise, added `ReconcilePresentationDrift`.
- `Services/WinEventMonitor.cs` — added `_hookLocationChange`, `WindowLocationChanged`, `EVENT_OBJECT_LOCATIONCHANGE` install/unhook, `HasInstalledHooks`/`HasAllHooks` include it, `Raise` dispatch, `IsDiagnosticEvent`/`EventName` include it.
- `Services/GuestLifecycleService.cs` — added `RepairKind.LocationDrift`, `WindowLocationChanged` subscription, `OnLocationChanged` → `QueueRepair(LocationDrift)`, `ProcessPendingRepair` handles it via `container.ReconcilePresentationDrift`.
- `Services/GuestPresentationDriftPolicy.cs` — new pure decision (zoom / geometryMismatch / notVisibleButShouldBe) with 10 deterministic cases.
- `tests/UnitTests/GuestPresentationDriftPolicyTests.cs` (10), `CaptionCenteringTests.cs` (2), `PresentationChromeIntegrityTests.cs` (6) — updated `WinEventMonitorTests` FakeApi to 8-hook cycle.
- `tests/ValidationDriver/TabDock.ValidationDriver/ScenarioCatalog.cs` — added `guest-maximize-contained` (`core-lifecycle`, `includeInAll: true`).
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs` — implemented `GuestMaximizeContained` (synthetic `SW_SHOWMAXIMIZED` → `LOCATIONCHANGE` → `SHEPHERD[drift-reconcile]` check).
- `docs/ARCHITECTURE.md` / `docs/TESTING.md` — presentation-integrity architecture and qualification notes.
- `.agent/investigations/presentation-integrity-2026-08-31.md` — closed with proven/rejected verdicts, bounded evidence, remaining risks.
- `openspec/changes/2026-08-31-user-reported-presentation-integrity/tasks.md` — all 0.x–6.x checked with evidence.

### Validation and evidence rules (retained)

- Build + unit-test gates must be 0 warnings / 0 errors and fully passing before commit.
- Synthetic/headless PASS cannot satisfy supervised physical input, mixed-DPI, Windows-version, signing, or human smoke gates.
- No guarded `SendInput` or blind desktop automation without a proven exclusive supervised lease. Autonomous runs use pure seams or test-owned processes/windows and clean up in `finally`.
- Keep generated artifacts, logs, caches, machine paths, credentials, and secrets out of Git.

### Known external blockers retained (honestly)

- Supervised physical real-input repetitions for `rename`, `group-rename-menu`, `group-dropdown-stability` (20 cycles), `contextmenu-render-stability` (20), `chrome-click-render-stability` (8), `capture-inline-ui`, `guest-maximize-contained` real title-bar click, `guest-fullscreen` (F11), `guest-monitor-transfer` (2 monitors, Win+Shift+Arrow), `topmost-guest-chrome-integrity` (WS_EX_TOPMOST), mixed-DPI hardware, real Windows 10 x64, independent Windows 11, approved production signing, final human smoke — all remain `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT`/`SKIP_CAPABILITY` without lease/hardware. Deterministic policy tests + synthetic maximize scenario provide strongest controlled evidence.
- No candidate binary executed by report importer; imported evidence hash-verified as data only.

### Next action

Commit the presentation-integrity implementation (detailed evidence summary), push `origin/main`, prove identical SHAs and clean worktree with `git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, then hand off. Keep all supervised/hardware/signing gates honestly blocked. Do not create another commit merely to record the push.
