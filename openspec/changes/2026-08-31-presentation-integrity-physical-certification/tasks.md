# Tasks — presentation-integrity physical certification

## 0. Orientation and safe desktop

- [ ] 0.1 Resolve Git authority dynamically and verify the candidate/mainline.
- [ ] 0.2 Read AGENTS.md, .agent/STATE.md, docs/TESTING.md, the completed
  presentation-integrity change, this change, and relevant canonical specs.
- [ ] 0.3 Verify/prove the exclusive supervised desktop lease before any
  SendInput. If unavailable, stop physical input and report BLOCKED_SUPERVISED
  or BLOCKED_ENVIRONMENT rather than proceeding blind.
- [ ] 0.4 Record Windows build, monitor topology/work areas/DPI, candidate
  executable identity, and available real applications in a new durable
  investigation/certification record.

## 1. Original user-report workflows

- [ ] 1.1 Accent/color menu repeated physical cycles with guest visibility,
  point ownership and interaction assertions.
- [ ] 1.2 Workspace/group rename repeated physical cycles: commit, cancel,
  click-away, reopen, long title.
- [ ] 1.3 Split affordance/menu cycles in normal and presented/dormant pair
  states where applicable.
- [ ] 1.4 "+" inline capture open/close/reopen/cancel/capture cycles with single
  and split presentation where applicable.
- [ ] 1.5 Run adjacent existing chrome/group scenarios under a valid lease and
  preserve first-attempt results.

## 2. Guest-originated maximize/fullscreen

- [ ] 2.1 Physically maximize/restore a controlled guest via its own caption.
- [ ] 2.2 Physically maximize/restore at least one real application.
- [ ] 2.3 Run Win+Up/restore where safe.
- [ ] 2.4 Run real F11 fullscreen/exit on an isolated browser or application
  where available.
- [ ] 2.5 Repeat relevant cases with a split relationship and verify LEFT/RIGHT
  identity/presentation remains coherent.
- [ ] 2.6 Compare physical results to the synthetic
  guest-maximize-contained evidence; do not substitute one for the other.

## 3. Monitor, DPI and topmost

- [ ] 3.1 With two real monitors, test container moves and guest-native monitor
  transfer in both directions; otherwise record BLOCKED_CAPABILITY.
- [ ] 3.2 Exercise maximize/fullscreen/restore across monitor placement.
- [ ] 3.3 Exercise mixed-DPI monitor transitions when real mixed-DPI hardware
  exists; otherwise record the exact unavailable matrix.
- [ ] 3.4 Exercise a controlled WS_EX_TOPMOST guest/window against TabDock
  popups/dialogs. Add only minimal test-fixture support if current GuineaPig
  lacks the capability.
- [ ] 3.5 Verify unrelated foreground windows are respected and no persistent
  topmost/global z-order fight occurs.

## 4. LOCATIONCHANGE load qualification

- [ ] 4.1 Establish baseline WinEvent/repair metrics with no captured guests.
- [ ] 4.2 Move/resize unrelated test-owned windows under one captured guest and
  measure callback rejection/coalescing/native-write behavior.
- [ ] 4.3 Stress captured guest maximize/resize/location churn and verify
  per-HWND coalescing plus UI responsiveness.
- [ ] 4.4 Exercise split presentation under unrelated location churn.
- [ ] 4.5 If event amplification or UI stalls are observed, diagnose and fix
  only the proven bottleneck with equivalence tests.

## 5. Caption physical/UIA qualification

- [ ] 5.1 Measure title midpoint vs container midpoint at narrow/default/wide
  widths and short/long names.
- [ ] 5.2 Repeat rename-editor measurement.
- [ ] 5.3 Repeat at all physically available DPI scales and prove required
  caption controls remain reachable.

## 6. Residual repair, only if needed

- [ ] 6.1 For each FAIL_PRODUCT, retain first-attempt evidence and produce a
  minimal reproduction before editing.
- [ ] 6.2 Update this OpenSpec with the proven cause and chosen bounded fix.
- [ ] 6.3 Add a non-vacuous regression capable of catching the physical failure.
- [ ] 6.4 Implement the smallest Shepherd-preserving fix; no speculative rewrite.
- [ ] 6.5 Run Debug/Release/unit/OpenSpec/deterministic gates, then rerun the
  failed physical scenario and adjacent cells.

## 7. Completion and handoff

- [ ] 7.1 Produce a matrix table for every required physical cell with
  PASS/FAIL_PRODUCT/FAIL_HARNESS/BLOCKED_*/SKIP_CAPABILITY, exact SHA and
  evidence reference.
- [ ] 7.2 Explicitly state which of the four original reports were physically
  reproduced-and-passed versus only deterministically covered.
- [ ] 7.3 Update docs/TESTING.md and docs/ARCHITECTURE.md only for proven new
  behavior/qualification facts; update .agent/STATE.md and investigation record.
- [ ] 7.4 Run strict OpenSpec validation and repository deterministic gates.
- [ ] 7.5 If all valid failures are resolved and every unavailable capability
  is honestly disposed, archive/sync the implementation and certification
  changes according to the repository OpenSpec workflow.
- [ ] 7.6 Commit/push the final evidence and any residual fix; report starting
  SHA, final SHA, commits, physical PASS matrix, blocked cells and remaining
  limitations.
