# Agent state

## UI/UX hardening campaign — Round 5: supervised closure COMPLETE (2026-08-11)

Objective: goal-del-leter.txt — resolve the two newly confirmed product defects
(split not persistent; post-drag blanking), preserve the protected behaviors
(smooth live-drag, maximize/restore, popups), complete the supervised
ValidationDriver closure, sync OpenSpec/docs/state, then certify and commit the
customer-ready milestone.

Baseline: branch `main`, HEAD `448e8ef`; working tree carries the entire
uncommitted campaign. Shepherd/no-reparent preserved; `WindowShepherdService`
remains the sole positioning/z-order owner.

## Round-4 fixes (carried, all build-clean)

### Defect 1 — split is NOT persistent (user-confirmed)
Single funnel `ActiveTab` change → `SyncShepherdActiveWindow` called
`ExitSplit(keepActive: C)` for any non-member activation. **Fix:** split branch
rejects non-member activation (journal-safe hide `SPLIT[persist]`, active tab
reverted to the focused member via `FocusSplitMember`); Ctrl+Tab cycles only the
pair; `TabsListBox_PreviewMouseLeftButtonDown` swallows non-member tab clicks
during split (× buttons excluded); `TabsListBox_SelectionChanged` guarded with
`_inSelectionSync` re-entrancy guard (this guard also fixed a REAL stack
overflow observed live during batch runs: the ListBox IsSelected↔IsActive
TwoWay ping-pong when the revert re-activates the focused member).

### Defect 2 — post-drag blanking (user-confirmed)
**Fix:** `WM_EXITSIZEMOVE` → one coalesced `RequestRelayout()` (final
reconciliation); redundant-glue short-circuit now validates the local pairing
(`WindowShepherdService.IsContainerBelowGuest` upward GW_HWNDPREV walk skipping
invisible windows) before skipping writes; gated off while chrome is raised.
Healthy steady state: 0 native writes; broken pairing: exactly 1 idempotent pin.

## Round-5 findings DURING supervised validation (new)

1. **PRODUCT BUG — close-group prompt covered by the docked guest.** The 120ms
   `WM_ACTIVATE` reassert (`ContainerWindow.WndProc`) fired ~120ms after the
   user clicks the container's × (the click activates the container), raising
   the docked guest ABOVE the just-shown "Close group" MessageBox — its
   Yes/No/Cancel buttons ended up covered by the guest (proven live:
   WindowFromPoint at the Yes button resolved to the guest). Real users would
   be unable to close a populated group via the ×. **Fix (one line):**
   `IsContainerChromeInteractionActive()` now includes `_closePromptOpen`.
   Verified: `exitpopulated`/`closegroupprompt` PASS.

2. **HARNESS — split-composite middle-click never reached the app.** Root cause:
   an environmental layout collision — the ×-popped member C (released to its
   own placement) overlapped the container's strip left half, and WindowFromPoint
   at the click point resolved to C, not the container. Fix: `MoveContainerClearOf`
   (moves the container to a work-area corner clear of the released guest before
   the middle-click step) + WindowFromPoint probe in `Input.MiddleClickAt` +
   `EnsureClickable` before the click.

3. **HARNESS stale assumptions rewritten** (all latent — never run before this
   session):
   - `split-maximize-restore-no-overlap`: each cycle ended MAXIMIZED but started
     assuming NORMAL → cycle-end normalization added.
   - `split-drag-release-render-stability`: half-click coordinates were computed
     once but the container oscillates ±130px per drag cycle → per-cycle UIA
     re-read; also the "already-focused member click logs no SPLIT[focus]" case
     is now structurally impossible.
   - `exitpopulated` (M6): the launcher is HIDDEN while a container is open
     (documented design) → rewritten to the reachable flow: caption-× real
     click → "Close group" prompt → Yes → launcher reappears → Exit; Yes click
     and Exit click retried (first click on a fresh modal can be consumed by
     activation).
   - `persist-kill`/`persist-active-tab-index`/`restored-group-survives-member-reclose`:
     single-pass WM_CLOSE exit broke because closing the last container
     RE-SHOWS the launcher → `CloseAllWindowsUntilExit` (repeat close waves);
     relaunch "MainWindow up" check was racy (restored container hides the
     launcher within ~50ms of startup) → wait for ANY visible top-level window.
   - `CaptureIntoExistingGroupViaAddButton` + reattach/hotkey-afterclose:
     the container's "+" opens the INLINE capture panel, not the standalone
     "Capture windows" picker (design change) → rewritten to drive the inline
     panel (row toggle + "Add selected" + toggle-close); reattach rename retried.

## Supervised ValidationDriver closure — COMPLETE (user palac present)

- Batch 1 (split core, 6): ALL PASS (incl. split-click-third, split-composite).
- Batch 2 (persistence/focus, 5): ALL PASS (incl. NEW split-third-tab-hover-persists,
  split-third-tab-click-persists).
- Batch 3 (window state, 6): ALL PASS (split-maximize-restore-no-overlap etc.).
- Batch 4 (movement, 6): ALL PASS (incl. NEW drag-release-render-stability,
  split-drag-release-render-stability).
- Batch 5 (popup/chrome, 7): ALL PASS.
- Batch 6 (group lifecycle, 6 + reattach pair): ALL PASS.
- Batch 7 (legacy, 11): ALL PASS.
- Stress (goal §40): split-focus-bidirectional 30, hover-persists 30,
  click-persists 25, split-repeat-cycles 25, drag-release 30, split-drag 30,
  maximize-restore 20, contextmenu 20, group-dropdown 20, add-window-toggle 20
  (the three popup scenarios were extended to honor --cycles) — ALL PASS.
- Batch G real apps: browser-lifecycle --guest chrome-normal PASS,
  browser-lifecycle --guest edge-normal PASS, maximize-repro --guest wt PASS.
- Failures encountered were classified per goal §41: 1 PRODUCT BUG (close-prompt
  covered — fixed in production), the rest HARNESS/STALE-ASSUMPTION or
  ENVIRONMENTAL-INPUT (released-guest overlap), each fixed at the harness layer.

## Final static validation (post all fixes)
- `dotnet build` ×4 (TabDock.csproj, TabDock.sln, GuineaPig, ValidationDriver):
  0 warnings / 0 errors.
- `scripts/validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS (14,718,730 checks, 0 failures).
- `openspec validate --all --no-interactive`: 12/12 PASS.

## Docs / OpenSpec
- `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `README.md`, waypoint Round 4-5,
  openspec change `tabdock-ui-ux-hardening` (proposal/design/tasks/delta spec)
  synced. Final Round-5 notes (close-prompt guard, validation ledger) appended
  to the waypoint.

## Commit policy
One milestone commit after the final diff audit; stage ONLY campaign files
(`goal-del-leter.txt` stays untracked — pre-existing prompt material). Do NOT
push / PR / tag.

## Next action
1. Final `git diff` audit (SetParent/GWL/HWND_BOTTOM/sleep/polling greps — done,
   clean; bounded probes retained: WindowFromPoint in Input.MiddleClickAt and
   the per-cycle click probes).
2. Append Round-5 ledger to `docs/internal/ui-ux-stabilization-waypoint.md`.
3. Stage + commit the milestone (`feat: harden TabDock UI and split-screen
   interactions`).

## Closure (2026-08-11, completed)
- Milestone commit `578b299` (`feat: harden TabDock UI and split-screen
  interactions`) — 27 campaign files (production fixes, harness, docs, change
  artifacts).
- OpenSpec change synced to main specs (new `spec/ui-ux-hardening`, 12
  requirements) and archived to
  `openspec/changes/archive/2026-08-11-tabdock-ui-ux-hardening/`; committed as
  `e4c85d8` (repo convention: separate "sync and archive OpenSpec changes"
  commit, cf. 86bee83/1411dbc). `openspec validate --all` 12/12 PASS after
  archive.
- Working tree clean except `goal-del-leter.txt` (untracked prompt material,
  intentionally uncommitted). No push, no PR, no tag.
- READINESS: PASS — no known reproducible blocker in validated scope.
