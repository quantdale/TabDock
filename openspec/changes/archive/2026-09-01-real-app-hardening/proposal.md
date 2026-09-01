# Real-app hardening

## Why

The DPI/topology campaign closed rows 4.8, 18.6, 19.2, 19.3 with 14 RUNNABLE PASS / 21 BLOCKED_CAPABILITY on `dc22ff3`, but predecessor rows **19.1** (restricted browser F11 visual evidence) and **19.4** (adopted real-app crop/minimization/privacy evidence) were explicitly migrated as a handoff to a dedicated real-app campaign. They remain floating obligations.

At the same time, the production DPI repair `bc678ef` now has a durable acceptance matrix and provenance record (`HISTORICAL_TRIGGER_NOT_RECOVERABLE` with deterministic defect analysis and `GuestDpiPositionScopeTests` regression), but the real applications themselves have not been qualified under the same Shepherd, identity, lease, and visual-privacy contracts that the GuineaPig fixtures satisfied.

## What Changes

- Migrate and disposition rows **19.1** and **19.4** with row-level handoff: original wording, migration reason, current support, scenario, acceptance, visual/privacy, final disposition — no floating obligations.
- Harden **Chromium family** (Chrome, Edge, Brave — Firefox remains `SKIP/BLOCKED` if unavailable): real guarded `browser-fullscreen-contained` with capture, F11 enter, F11 containment/exit (one identity-checked browser-local F11 exit, duplicate suppression, LOCATIONCHANGE re-entry, return to pane, no repeated toggles, bounded foreground), F11 repeat, monitor continuity on 120/96 DPI, and restricted before/fullscreen/after visual packets with multimodal review where required.
- Review **false-positive/compatibility** risk of the borderless-geometry F11 classifier against normal maximized Chromium, ordinary borderless, PWA/windowed app, kiosk-like, popup/devtools, stale style, and monitor-sized non-F11 windows; smallest safe repair if a valid defect is found, otherwise no broadening.
- Harden **Windows 11 Notepad**: dynamically inspect actual packaged/broker/host HWND, owner/root, PID, start time, executable identity, ancestry, and whether capture identity remains valid; exercise capture/focus/tab/maximize/transfer/release/re-capture and observe close only if run-owned; preserve `BLOCKED_CAPABILITY`/`BLOCKED_ENVIRONMENT` rather than weaken provenance if unsafe.
- Harden **Windows Terminal**: inspect launcher/monarch/host HWND ownership, process-start identity, root, whether `wt` reuses an existing host, and which process is run-owned; exercise run-owned clean launch and adopted-existing paths separately (capture/focus/tab/maximize/transfer/release/re-capture); prove launcher exit ≠ guest disappearance.
- Enforce **run-owned vs adopted** safety boundary: exact HWND/PID/start/executable/owner/root/generation, never by title; never cleanup-own a foreign process; never minimize/close unrelated windows; strict foreground/point-ownership proof or `BLOCK`.
- Apply **adopted-app privacy** (`REAL_APP_RESTRICTED`): default capture minimized/cropped to smallest approved region; no whole-desktop, unrelated windows, URL beyond unavoidable controlled target, personal documents, terminal history, credentials, or unrelated regions; prefer blank/test content; `BLOCKED_CAPABILITY`/`REVIEW_UNAVAILABLE` if privacy-safe crop impossible.
- Produce a **physical real-app acceptance matrix** with app, executable, process-start/HWND/root identity, run-owned/adopted, scenario/attempt, source/destination monitor/DPI, lease, foreground, point ownership, native/visual outcome, packet hash, cleanup, final disposition.
- Close **19.1** only after available Chromium families exercised with exact identity, physically observed F11, containment/recovery proven, restricted screenshots retained, packet hash-valid, capable review where required, privacy bounded, synthetic not substituted. Close **19.4** only after adopted identity proven, scope restricted, desktop capture disabled, packet reflects privacy class.

## Non-Goals

- No `SetParent`/reparenting, style stripping, permanent topmost, global z-order polling, blind repeated `SetWindowPos`, killing adopted user apps, process-name-only ownership, title-based identity, relaxed foreground/point checks.
- No arbitrary real applications beyond the three families.
- No registry DPI mutation, blind Display Settings automation, or unsupported display mutation.
- No claim that synthetic GuineaPig visuals satisfy real-app gates.
- No retagging of the historical `6bb8e` v1.1 binary or `dc22ff3` packets.
- No whole-desktop capture, URL harvesting, or credential collection.

## Capabilities

### Modified Capabilities

- `presentation-integrity`
- `native-window-identity`
- `validation-qualification`
- `visual-qualification-evidence`
- `qualification-control-plane`

## Acceptance Boundary

Complete only when:

- durable DPI matrix and `bc678ef` provenance are preserved;
- `GuestDpiPositionScope` safety is reviewed and non-vacuously regression-tested;
- rows 19.1/19.4 have explicit dispositions and no floating obligations;
- Chromium/Notepad/Terminal have truthful supervised dispositions (or capability blocks) with first-failure authority preserved;
- privacy boundary proven (no whole-desktop, `REAL_APP_RESTRICTED`);
- deterministic, selftest, catalog, OpenSpec, and CI-safe gates pass on the final settled source;
- final exact candidate (SHA, executable/driver hashes, version, signing, eligibility) recorded independently from `6bb8e`;
- OpenSpec has no active changes after archive; Git is main-only, `HEAD==origin/main`, worktree clean; no unsupported production-release claim.

If an unresolved valid product defect remains, do not archive.
