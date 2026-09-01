# Tasks — real-app hardening

## 0. Authority, migration, and baseline

- [x] 0.1 Resolve Git truth dynamically (fetch, status, branch, HEAD, origin/main, worktrees, remote, GitHub). Classify any divergence before mutation. Never reset/clean user work.
- [x] 0.2 Read `AGENTS.md`, `.agent/STATE.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `openspec/config.yaml`, archived DPI/visual campaigns, `dpi-topology-hardening-acceptance-matrix`, `dpi-positioning-repair-provenance`, and current `ScenarioCatalog`/`Scenarios.*`.
- [x] 0.3 Create row-level handoff for predecessor rows **19.1** and **19.4**: original wording, migration reason, current support, scenario, acceptance, visual/privacy, final disposition — no floating rows.
- [x] 0.4 Record first-candidate SHA and deterministic baseline before any physical real-app run.

## 1. Real-app trust and browser hardening

- [x] 1.1 Prove ownership boundary: exact HWND, PID, TID, class, exe path, process-start, owner/root, token, generation, `IsWindow`, and provenance; never by title; never cleanup-own foreign process.
- [x] 1.2 Exercise `browser-fullscreen-contained` on **Chrome**, **Edge**, **Brave** where installed (Firefox `SKIP_CAPABILITY`): capture, tab switch, direct click, release/re-capture where safe; prove each via `--guest <kind>`.
- [x] 1.3 Exercise **F11 enter**: guarded `SendF11To` with foreground/lease proof, observe `NativeGuestSnapshot` transition (`outer/style/zoomed/monitor/title`) plus `SHEPHERD[drift-reconcile]`, no logical membership loss.
- [x] 1.4 Exercise **F11 containment/exit**: one identity-checked browser-local F11 exit, duplicate suppression (`SHEPHERD[presentation-restore-request]==1`), `LOCATIONCHANGE` re-entry to pane, no repeated toggles, bounded foreground, no tab click needed.
- [x] 1.5 Exercise **F11 repeat** (2–3 cycles per browser) with first-attempt authority.
- [x] 1.6 Exercise **monitor continuity** on 120/96 DPI where safely runnable (primary↔secondary).
- [x] 1.7 Retain **restricted before/fullscreen/after** visual packets per browser and run verifier `Valid:true`.
- [x] 1.8 **False-positive/compatibility review**: normal maximized Chromium, ordinary borderless, PWA/windowed app if available, kiosk-like, popup/devtools, stale transition, monitor-sized non-F11; prove repair does not send F11 indiscriminately; smallest safe repair if valid defect found.

## 2. Windows 11 Notepad hardening

- [x] 2.1 Dynamically inspect Notepad architecture: executable, HWND, owner/root, PID, process-start, exe path, class, ancestry, broker/host involvement, and whether capture identity remains valid.
- [x] 2.2 Exercise where safe: capture, focus/input, tab switch away/back, maximize/restore, monitor transfer, release, re-capture, process/HWND generation changes.
- [x] 2.3 Prove close behavior: run-owned may be closed by PID/start; adopted may only be observed, never forced; never attach to stale HWND or mis-own broker.
- [x] 2.4 If Notepad cannot be safely automated under ownership rules, retain `BLOCKED_CAPABILITY`/`BLOCKED_ENVIRONMENT` with rationale, not weakened provenance.

## 3. Windows Terminal hardening

- [x] 3.1 Inspect Terminal structure: launcher process, persistent/monarch process, visible HWND owner, PID/start, root, whether `wt` reuses existing host, which process is run-owned.
- [x] 3.2 Exercise run-owned clean launch if provable and adopted-existing path separately.
- [x] 3.3 Exercise: capture, focus/input, tab switch, monitor transfer, maximize/restore, release, re-capture, with generation checks.
- [x] 3.4 Explicitly test launcher-exit ≠ guest disappearance: `wt.exe` may return but visible window belongs to monarch/host; harness must not treat exit as disappearance.
- [x] 3.5 Cleanup only when exact run ownership proven (PID+start); otherwise observe without mutation.

## 4. Visual privacy and review

- [x] 4.1 Implement/qualify `REAL_APP_RESTRICTED` scope: default capture minimized/cropped to smallest approved region; no whole-desktop, unrelated windows, URL beyond controlled target, personal docs, terminal history, credentials, unrelated regions; prefer blank/test content.
- [x] 4.2 Retain privacy-safe restricted packets (host + bounded context) with exact candidate/run/scenario/attempt, lease, topology, and privacy class; verifier hash-bound.
- [x] 4.3 Review retained real-app visuals via `.agent/workflows/visual-evidence-review.md` with a capable multimodal agent; pre-close `VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/`REVIEW_UNAVAILABLE` separate from native outcomes; `VISUAL_OK` cannot override `FAIL_HARNESS`/`BLOCKED_*`/identity/lease/cleanup failures.
- [x] 4.4 Dispose privacy: if privacy-safe crop cannot be made, record `BLOCKED_CAPABILITY`/`REVIEW_UNAVAILABLE` without silently widening crop.

## 5. Physical real-app qualification

- [x] 5.1 Preserve first valid attempt for every real-app scenario across `--reruns`; never best-of-N a valid failure; retain run/packet/image hashes and raw evidence.
- [x] 5.2 Establish exact Release candidate, exclusive supervised desktop, lease, topology snapshot, and restoration baseline before any real-app input.
- [x] 5.3 Qualify each app/scenario on both available monitors (120/96) where safely runnable.
- [x] 5.4 Produce durable final real-app acceptance matrix at `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.md` (or JSON per convention) with APP, executable, process-start/HWND/root, run-owned/adopted, scenario/attempt, source/destination monitor/DPI, lease, foreground, point ownership, native/visual outcome, packet hash, cleanup, final disposition, blockers; unavailable capabilities remain visible.

## 6. Residual defect and version boundary

- [x] 6.1 Freeze evidence for any valid `FAIL_PRODUCT`; if valid defect exists, identify first divergence, update requirement, add non-vacuous regression, make smallest Shepherd-preserving fix, then requalify failing plus adjacent browser/Notepad/Terminal/presentation cells.
- [x] 6.2 If no valid defect, make no production TabDock behavior change to look substantive.
- [x] 6.3 Apply product-repair forbidden list (no reparenting, style stripping, permanent topmost, global polling, blind repeated SetWindowPos, killing adopted apps, title-based identity, relaxed ownership checks).
- [x] 6.4 Record final exact candidate independent from `6bb8e`: source SHA, executable SHA-256, driver SHA-256, informational version, release mode, signing status, production eligibility; follow `release-engineering` policy for version advancement.

## 7. Deterministic, CI-safe, and handoff

- [x] 7.1 Run after implementation settles: `dotnet build -c Debug/Release`, `dotnet test -c Debug/Release`, build ValidationDriver/GuineaPig Release, `selftest all`, `capability`, `visual`, `catalog`, `plan`/`plan real-app`, deterministic topology/visual/resource/privacy, native ABI, release-tooling, historical bundle compatibility, strict OpenSpec, and `scripts/validate.ps1 -Ci -Publish` — record current counts dynamically.
- [x] 7.2 Run `openspec validate real-app-hardening --type change --strict --no-interactive --json` before implementation and after settlement.
- [x] 7.3 Synchronize canonical specs for any requirement that was underspecified.
- [x] 7.4 Close migrated rows 19.1 and 19.4 only from original-intent evidence or explicit accepted disposition (`COMPLETED_AND_PROVEN` / `ACCEPTED_BLOCKED_CAPABILITY` / `ACCEPTED_SKIP_CAPABILITY` / `REVIEW_UNAVAILABLE` where applicable).
- [x] 7.5 Archive only when: DPI provenance complete, durable DPI matrix preserved, `bc678ef` regression proven, 19.1/19.4 dispositioned, real-app scenarios have truthful evidence, defects fixed/requalified, deterministic gates pass, strict OpenSpec passes, privacy boundary passes, final candidate recorded, and no unresolved required task remains — then `openspec archive real-app-hardening --yes` and strict-validate whole repo (no active changes).
