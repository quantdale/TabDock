# Tasks — presentation-integrity physical certification

## 0. Orientation

- [x] 0.1 Resolve `HEAD`, `origin/main`, branch, and worktree dynamically.
- [x] 0.2 Read repository guidance, architecture/testing references, both
  presentation-integrity changes, and relevant canonical specs before editing.
- [x] 0.3 Confirm the implementation candidate is `4aaf3fcaa72edf48865030db43bccf7bd50e21b8` and that the requested certification change was absent, then create this evidence-only change at the requested path.

## 1. Exclusive desktop safety gate

- [x] 1.1 Run no-input capability and topology discovery. The session is
  interactive/unlocked with two monitors and `SendInput` technically available.
- [x] 1.2 Establish the supervised exclusive lease and prove no user is using
  the desktop. **BLOCKED_SUPERVISED:** this agent session has no independent
  operator confirmation or exclusive desktop lease; no mouse or keyboard input
  was sent and no destructive scenario was started.
- [x] 1.3 Preserve the existing `WindowFromPoint`, `GA_ROOT`, foreground,
  process-start, HWND, and identity guards as the only physical-input boundary.

## 2. Durable evidence

- [x] 2.1 Record candidate identity, binary hash, OS/build, monitor bounds and
  work areas, DPI, application availability, lease state, and exact commands.
- [x] 2.2 Preserve first-attempt authority. No valid physical product failure,
  rerun, or best-of-N result exists because the safety gate blocked before
  input.
- [x] 2.3 Record that no guest, container, popup, or captured-member HWND was
  created by this campaign; prior doctor observations are not this campaign's
  scenario evidence.

## 3. Original Chrome-occlusion reports

- [x] 3.1 Color/accent menu cycles are **BLOCKED_SUPERVISED** before input;
  physical first result is unavailable, not PASS.
- [x] 3.2 Rename entry, commit/cancel/click-away, long-name, and repeat cycles
  are **BLOCKED_SUPERVISED** before input.
- [x] 3.3 Split menu, pair focus, dismiss, end, and dormant/resume cycles are
  **BLOCKED_SUPERVISED** before input.
- [x] 3.4 Inline `+` capture open/close/reopen/cancel/capture for single and
  split presentation is **BLOCKED_SUPERVISED** before input.

## 4. Guest-native presentation transitions

- [x] 4.1 Guest caption maximize and restore are **BLOCKED_SUPERVISED** before
  the required physical guest-title-bar click.
- [x] 4.2 Win+Up/restore is **BLOCKED_SUPERVISED** before input.
- [x] 4.3 Real F11 fullscreen enter/exit, single and split where supported, is
  **BLOCKED_SUPERVISED** before input.
- [x] 4.4 Dual-monitor transfer and mixed-DPI transfer are **BLOCKED_SUPERVISED**
  before input; topology exists but exclusive supervision is absent.
- [x] 4.5 Determine the controlled fixture's topmost support. The initial
  candidate lacked this capability; the smallest test-only `--topmost`/
  `Form.TopMost` switch was added to GuineaPig and documented. Physical
  topmost qualification remains **BLOCKED_SUPERVISED** before any input.

## 5. Z-order, load, and layout

- [x] 5.1 Unrelated foreground overlap, owned dialog, and local z-order are
  **BLOCKED_SUPERVISED** before input.
- [x] 5.2 `EVENT_OBJECT_LOCATIONCHANGE` load cases are **BLOCKED_SUPERVISED**
  before the required moving/resizing workload; deterministic coverage remains
  separate.
- [x] 5.3 Physical title-centering width/name/DPI matrix is
  **BLOCKED_SUPERVISED** before UIA measurement; available DPIs are recorded.

## 6. Existing high-risk scenarios

- [x] 6.1 The required rename, group-menu, dropdown, add-window, context-menu,
  chrome-click, foreground-pairing, maximize, split, drag/reorder, torture,
  and browser scenario reruns are **BLOCKED_SUPERVISED** before input.
- [x] 6.2 Record exact fresh-state rerun commands and do not launch `all` from
  this unsupervised session.

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
  accepted split-exit pass remains preserved.
- [ ] 8.11 Run the complete deterministic, catalog, OpenSpec, and CI-safe
  gates against the final working tree.
- [ ] 8.12 Commit/push the authoritative main tree and verify clean
  `HEAD == origin/main`.
