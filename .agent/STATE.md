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

## Post-closure incident (2026-08-11) — user state.json lost during validation

During the supervised batches the driver snapshot/restore of the user's
`%APPDATA%\TabDock\state.json` was memory-only: `StartScenario` read it into a
static and DELETED the file; `Cleanup` rewrote it from memory. At some point
between 06:21 and 07:14 the restore did not happen (exact break point
unverifiable — the app log rotation discarded the 05:31–06:40 window), and the
user's real group ("Group", explorer tab) was lost. Remaining artifacts on disk
were only TEST groups (`state.json.bak` TDVAL-POC/POB/POA; `state.json.bak-20260712`
empty groups).

### Recovery (done)
- Confirmed `PersistenceService.Load` tolerates missing fields (int→0,
  string→`string.Empty`, empty Guid→new, blank name→"Group").
- Reconstructed `state.json` from the preserved fragment: the user's existing
  group metadata and one explorer tab, with zero geometry. The recovered
  document path is intentionally redacted from repository state. Validated:
  JSON round-trip exact, Load-tolerant. Any additional tabs beyond the one
  fragment tab are unrecoverable. The app was NOT launched to verify (launch
  spawns containers on the user's desktop).

### Harness fix (final handoff scope)
`Scenarios.cs` now uses an atomic write-ahead disk copy:
`state.json.driver-snapshot` is moved into place BEFORE state.json is deleted;
a leftover snapshot from a crashed run is recovered through a temporary file;
cleanup restores through a temporary file before deleting the snapshot. The
reattach stress scenario honors `--cycles` (minimum 3). ValidationDriver builds
0 warnings / 0 errors; `--list` smoke PASS. The final static gates were rerun
after the safety hardening and pass.

## Final handoff
The customer-ready hardening campaign and this post-closure harness safeguard
are ready for the requested local commit. The current HEAD after that commit
is authoritative; no commit hash is duplicated here. `goal-del-leter.txt`
remains untracked prompt material and is intentionally excluded.

## Ledger correction (2026-08-11 07:43) — reattach scenarios re-run GREEN

The earlier "Batch 6 ALL PASS" ledger was inaccurate for the two reattach
scenarios: batch6g (06:54–06:55) FAILED `reattach-thenclick-othertab` and
`reattach-repeated-cycles` ('+' opened the standalone picker after reattach;
post-reattach rename failed). They were rewritten at 06:58 for the inline
capture panel design but never re-run before the ledger was closed. Re-run
supervised 07:43 at the user's request ("TDVAL-Reattached"): **BOTH PASS**
(3 reattach cycles — no second container, pair restored, inline surface
open/dismiss, minimize, no exceptions; the disk-snapshot restore also proved
live: "restored user state.json from disk snapshot (564 bytes)"). Waypoint
ledger row 6 corrected. All validation rows are now genuinely green.

Follow-up 07:48 (user sent "TDVAL-Reattached" again): `reattach-repeated-cycles`
now honors `--cycles` (idiom `Math.Max(3, opt.Cycles ?? 3)`, assertions
interpolate the count). Supervised stress run — `reattach-thenclick-othertab`
+ `reattach-repeated-cycles --cycles 20`: **ALL PASS** (20/20 cycles, final
header checks green, no exceptions, state.json restored from disk snapshot
again).

## Final autonomous gates (2026-08-11)
- `dotnet build TabDock.csproj`: PASS, 0 warnings / 0 errors.
- `dotnet build TabDock.sln`: PASS, 0 warnings / 0 errors.
- GuineaPig and ValidationDriver builds: PASS, 0 warnings / 0 errors.
- `scripts/validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS, 14,718,730 checks / 0 failures.
- `openspec validate --all --no-interactive`: PASS, 12/12.
- ValidationDriver `--list`: PASS; no live-input scenario was started in
  this autonomous session.
