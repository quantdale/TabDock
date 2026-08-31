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

- [x] 6.1 Audit the generic `StartScenario` foreground-admission path and
  preserve the existing fail-closed behavior.
- [x] 6.2 Separate foreground arrangement from foreground proof in a shared
  fail-closed harness primitive; retain all lease, identity, point-ownership,
  foreign-coverage and cleanup guards.
- [x] 6.3 Add deterministic fake-probe tests for SetForeground success/failure,
  guarded owned-point activation, foreign coverage, stale/recycled HWND,
  foreground loss after activation, and no-input refusal.
- [x] 6.4 Prove the revised primitive cannot send blind input, cannot
  manipulate unrelated windows, and still stops before destructive setup when
  foreground cannot be safely established.
- [x] 6.5 Re-run the formerly blocked generic setup path from clean state;
  accepted fresh runs and the retained blocked split-exit attempts are
  recorded in the continuation investigation.

## 7. Initial unsupervised checkpoint (historical)

- [x] 7.1 The initial unsupervised phase produced no `FAIL_PRODUCT`; the
  later supervised continuation and residual repair are recorded in §8.
- [x] 7.2 Run the required Debug/Release deterministic builds and tests,
  ValidationDriver catalog/self-tests, and strict OpenSpec validation after
  the campaign artifacts and authorized fixture/harness changes settled.
  The first stale-count self-test failure is retained as `FAIL_HARNESS`; the
  corrected gate passes.
- [x] 7.3 Reconcile the initial investigation, ledger, `.agent/STATE.md`,
  and proven documentation facts for that phase.
- [x] 7.4 Produce the initial acceptance matrix and original-report
  disposition.
- [x] 7.5 Complete the initial phase only as a blocked physical-certification
  checkpoint; do not treat it as final certification.

## 8. Continuation — supervised physical qualification and residual repair

- [x] 8.1 Stabilize the shared foreground qualification primitive and add
  deterministic fail-closed seam coverage (10/10 foreground self-tests).
- [x] 8.2 Run the GuineaPig caption-maximize and Win+Up physical scenarios;
  both passed with native state, identity, monitor, point ownership, and live
  rendering evidence.
- [x] 8.3 Preserve the valid Chrome F11 `FAIL_PRODUCT` first attempt
  (`7f5ba57f-af6e-491e-81aa-b33bd8229471`) and diagnose the native Chromium
  minimum-size refusal.
- [x] 8.4 Apply the smallest Shepherd-preserving browser-F11 restore policy,
  then requalify Chrome (`71656457-b555-42ce-a782-b4947f33f292`), Edge
  (`9cb2ad2a-a6bc-41a2-b196-2492702b9331`), and Brave
  (`01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6`) as physical PASS.
- [x] 8.5 Preserve the Edge first-attempt same-process dynamic-surface
  qualifier failure (`0e559627-46f5-463c-9560-ec64913fb3d0`) and the
  intermediate Edge F11 timing failure (`443624df-51bb-4801-a8ad-249b498302b5`)
  as raw artifacts; narrow the browser point qualifier without accepting
  foreign coverage.
- [x] 8.6 Add and pass the mixed-DPI dual-monitor transfer scenario
  (`f423509e-f869-4097-b938-964355bd9101`), including both real
  Win+Shift+Arrow directions, same-monitor containment, and 120/96 DPI
  observations.
- [x] 8.7 Add and pass the topmost guest interaction scenario
  (`f3ee2adb-2d4b-46ce-973f-ccbf789e5aca`), covering direct input, group
  menu, rename editor, and unrelated foreground recovery.
- [x] 8.8 Add and pass the controlled LOCATIONCHANGE load scenario
  (`345e33f8-8086-4819-9e5f-72acbdec45ed`), including callback/rejection/
  membership/dispatch/post/lifecycle metrics, bounded repairs, and
  responsiveness.
- [x] 8.9 Add and pass the physical title-centering measurement scenario
  (`1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19`) for short/long names at 120/96
  DPI and both monitor placements.
- [x] 8.10 Rerun every adjacent physical qualification cell required by the
  originating report after the browser repair and record first-attempt
  outcomes. Nine fresh cells passed; `split-exit` remained
  `BLOCKED_ENVIRONMENT` after three fail-closed retries, while the earlier
- [x] 8.11 Run the complete deterministic, catalog, OpenSpec, and CI-safe
  gates against the final working tree. Debug/Release builds and tests,
  driver/fixture builds, 153/153 selftests, 135-entry catalog/plans, strict
  OpenSpec, and CI-safe Release validation passed.
- [x] 8.12 Commit/push the authoritative main tree and verify clean
  `HEAD == origin/main`. Exact current-main gate passed at
  `914a25923bd4bb1f5c08d925bfb210bb9208853f`; Release executable SHA-256 is
  `E3C830202F07C522B8B0A210B4181D96D92158D84F86576CF23DDEDEA9BBF06F` and
  its embedded source identity matches that SHA.
