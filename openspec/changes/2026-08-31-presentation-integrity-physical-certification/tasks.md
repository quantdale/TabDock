# Tasks — presentation-integrity physical certification

## 0. Baseline and first blocked campaign

- [x] 0.1 Resolve Git authority and create the active physical-certification
  OpenSpec change.
- [x] 0.2 Record the initial no-input environment/capability matrix.
- [x] 0.3 Preserve the first campaign as honestly blocked when exclusive
  supervision was unavailable.
- [x] 0.4 Add qualification-only GuineaPig `--topmost` support and correct the
  stale 127→128 catalog self-test expectation without changing production code.

## 1. Supervised rerun and lease proof

- [x] 1.1 Accept the user's exclusive desktop supervision and establish valid
  native leases only when runtime ownership/coverage/foreground checks pass.
- [x] 1.2 Preserve candidate/process/HWND identity, adopted/owned provenance,
  `WindowFromPoint`→`GA_ROOT`, foreground continuity, point ownership, and
  cleanup identity for accepted runs.
- [x] 1.3 Preserve all first raw failures and classify them only after source +
  artifact analysis; no best-of-N promotion.
- [x] 1.4 Stop after two consecutive generic `StartScenario` foreground setup
  failures rather than weakening the guard.

## 2. Harness false-negative corrections

- [x] 2.1 Replace stale post-capture hard foreground assumptions in rename and
  tab switching with guarded direct clickability proof.
- [x] 2.2 Reacquire live UIA roots and boundedly wait for inline-capture rows;
  guard checkbox/submit clicks.
- [x] 2.3 Use guarded clickability before drag setup; keep obscured points
  fail-closed.
- [x] 2.4 Keep the generic `StartScenario` foreground gate unchanged during
  this rerun; production TabDock code unchanged.

## 3. Physically supported cells completed

- [x] 3.1 Workspace/group rename — PASS for exercised supported paths,
  including edge cases, persistence, geometry and guest liveness.
- [x] 3.2 Split creation/focus/dismissal/exit — PASS for exercised supported
  paths, including repeated enter/exit and bidirectional focus.
- [x] 3.3 Inline `+` capture — PASS for exercised open/close/reopen/cancel/
  capture/docking/cleanup paths.
- [x] 3.4 Context-menu/chrome rendering and direct foreground/local pairing —
  PASS for exercised paths with external foreground changes.
- [x] 3.5 Split resize and supported synthetic/container maximize paths — PASS
  where declared; synthetic maximize is not promoted to guest-caption proof.
- [x] 3.6 Color/accent selector — `SKIP_CAPABILITY`: production has no
  reachable selector; canonical group-color-picker behavior is deliberate no-op.

## 4. Raw failure disposition

- [x] 4.1 Preserve rename/tabswitch raw `FAIL_PRODUCT` manifests and classify
  as `FAIL_HARNESS` after proving stale unconditional foreground gates fired
  before scenario input.
- [x] 4.2 Preserve inline-capture/add-window raw failures and classify as
  `FAIL_HARNESS` after proving stale UIA/direct-click state.
- [x] 4.3 Preserve dragreorder raw failures caused by old hard foreground setup
  as `FAIL_HARNESS`.
- [x] 4.4 Preserve Edge/Chrome-covered drag/maximize attempts as
  `BLOCKED_ENVIRONMENT`; no blind input issued.
- [x] 4.5 Preserve Windows Terminal monarch reuse as `BLOCKED_CAPABILITY`.
- [x] 4.6 No valid physical `FAIL_PRODUCT` confirmed; no production repair
  authorized.

## 5. Deterministic verification after supervised rerun

- [x] 5.1 Debug/Release solution builds PASS, 0 warnings/errors.
- [x] 5.2 Debug/Release unit tests PASS, 725/725 each.
- [x] 5.3 ValidationDriver + GuineaPig Release builds PASS.
- [x] 5.4 Catalog remains 128 dispatchable scenarios.
- [x] 5.5 ValidationDriver selftests PASS 143/143.
- [x] 5.6 Strict OpenSpec validation PASS 37/37.
- [x] 5.7 CI-safe Release validation/publish, NuGet audit, resource stability,
  native ABI, recovery, privacy and publish smoke PASS.
- [x] 5.8 Rerun investigation and state checkpoint committed/pushed at
  `b0975b2a724f0cf9551c4e106dfc6449c8643002`.

## 6. Continuation — stable foreground qualification

- [ ] 6.1 Audit the generic `StartScenario` foreground-admission path and
  reproduce its two consecutive setup failures without changing product code.
- [ ] 6.2 Separate foreground arrangement from foreground proof in a shared
  fail-closed harness primitive; retain all lease, identity, point-ownership,
  foreign-coverage and cleanup guards.
- [ ] 6.3 Add deterministic fake-probe tests for SetForeground success/failure,
  guarded owned-point activation, foreign coverage, stale/recycled HWND,
  foreground loss after activation, and no-input refusal.
- [ ] 6.4 Prove the revised primitive cannot send blind input, cannot manipulate
  unrelated windows, and still stops before destructive setup when foreground
  cannot be safely established.
- [ ] 6.5 Re-run the formerly blocked generic setup path twice from clean state
  before using it for the remaining certification cells.

## 7. Continuation — guest-native maximize and shortcuts

- [ ] 7.1 Add a physical guest-caption maximize/restore scenario using guarded
  real input against the captured guest's caption.
- [ ] 7.2 Add a physical Win+Up/restore scenario with current guest foreground
  and identity proof before each shortcut.
- [ ] 7.3 Verify same captured identity, logical membership, authoritative
  geometry/monitor, guest liveness and no corrective tab click after each
  transition.
- [ ] 7.4 Exercise both a controlled GuineaPig and at least one suitable real
  application where the harness can prove ancestry/identity.

## 8. Continuation — real F11 fullscreen

- [ ] 8.1 Add an isolated Chrome/Edge/Brave F11 scenario; never rely on title
  matching alone for browser identity.
- [ ] 8.2 Physically enter/exit F11 and record available style/exstyle,
  `IsZoomed`, `WINDOWPLACEMENT`, rect, monitor, foreground, membership and
  point/client-render evidence.
- [ ] 8.3 Repeat from single-guest presentation and one meaningful split case.
- [ ] 8.4 If F11 produces a valid product failure, freeze first evidence before
  any production edit and follow the residual-defect gate.

## 9. Continuation — monitor and mixed-DPI transfer

- [ ] 9.1 Add an explicit two-monitor transfer scenario using guarded
  Win+Shift+Arrow or another normal system path.
- [ ] 9.2 Exercise primary 125% → secondary 100% and secondary 100% → primary
  125% transitions.
- [ ] 9.3 Combine transfer with maximize/restore and one F11/restore case when
  stable.
- [ ] 9.4 Record container/guest monitor identity, physical rects, work areas
  and DPI before/after every transition.

## 10. Continuation — topmost, load and title geometry

- [ ] 10.1 Dispatch the existing GuineaPig `--topmost` fixture from a
  ValidationDriver scenario and exercise TabDock popup/rename/dialog/local
  z-order behavior without making the container permanently topmost.
- [ ] 10.2 Add a dedicated `EVENT_OBJECT_LOCATIONCHANGE` load scenario with
  unrelated test-owned move/resize churn, captured-guest churn and split
  steady-state; record callback/rejection/post/coalescing/native-repair metrics.
- [ ] 10.3 Add a physical/UIA title-centering scenario for narrow/default/wide
  widths and short/long names, measuring title/editor midpoint against container
  client midpoint.
- [ ] 10.4 Run title measurement on both available DPI monitors (125% and 100%)
  and prove caption controls stay reachable.

## 11. Residual product-failure gate

- [ ] 11.1 For any valid `FAIL_PRODUCT`, retain the exact first attempt,
  candidate SHA/hash and native evidence before editing.
- [ ] 11.2 Find the first violated invariant and update this OpenSpec with the
  proven cause; do not assume the previous presentation diagnosis applies.
- [ ] 11.3 Add a non-vacuous regression that fails on the bad candidate.
- [ ] 11.4 Implement the smallest Shepherd-preserving production fix only when
  justified, then rerun deterministic and adjacent physical cells.

## 12. Final completion

- [ ] 12.1 Produce the final acceptance matrix with physical attempts, first
  outcomes, final dispositions and evidence references for every original cell.
- [ ] 12.2 Explicitly distinguish physically certified, deterministic-only,
  skipped and still-blocked original reports.
- [ ] 12.3 Run full deterministic gates after the final harness/product tree.
- [ ] 12.4 Reconcile `.agent/STATE.md`, investigation records and docs with
  proven facts.
- [ ] 12.5 Archive/sync the presentation-integrity OpenSpec changes only if
  every required cell is physically PASSed or explicitly accepted by a
  documented capability disposition; do not imply full certification while
  major requested paths remain unexecuted.
- [ ] 12.6 Commit/push and verify clean worktree with `HEAD == origin/main`.
