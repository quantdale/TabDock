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

## 7. Residual-defect and deterministic gates

- [x] 7.1 No `FAIL_PRODUCT` occurred; therefore no production defect was
  patched and no residual-fix regression was added.
- [x] 7.2 Run the required Debug/Release deterministic builds and tests,
  ValidationDriver catalog/self-tests, and strict OpenSpec validation after
  the campaign artifacts and authorized fixture/harness changes settled.
  The first stale-count self-test failure is retained as `FAIL_HARNESS`; the
  corrected gate passes.
- [x] 7.3 Reconcile the final investigation, this ledger, `.agent/STATE.md`,
  and proven documentation facts.
- [x] 7.4 Produce the final acceptance matrix and original-report disposition.
- [x] 7.5 Complete only as a blocked physical-certification campaign; do not
  claim the original presentation-integrity change is physically certified
  while major report paths remain blocked.
