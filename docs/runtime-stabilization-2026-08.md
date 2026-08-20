# Live-runtime stabilization — campaign notes (2026-08)

Scope: continue the runtime stabilization campaign from baseline `a422785` /
working commit `08fc456`. The audit's hotfixes (two-handler click path,
durable-disk-I/O on tab switch, z-order relative order, ensureFinalPass latch,
LayoutUpdated dirty detection, SWP_FRAMECHANGED removal, WinEvent storm
coalescing) were already applied in `08fc456`. This pass closes the remaining
verifiable gaps and records the supervised-validation + investigation work.

## Concrete code changes in this pass

- **#3 background state writer**: `PersistenceService` now separates snapshot
  (`BuildStateJson`) from the blocking atomic commit (`CommitJson`).
  `GroupManager.RequestSave` routes through the new `SaveAsync`, which
  serializes the immutable JSON on the UI thread and performs the WriteThrough +
  fsync + rename off-thread. Safety-critical boundaries (capture, release, group
  mutation) still call the synchronous `Save`. Same-index `SwitchActiveTab` is a
  true no-op; a different-index selection no longer writes `state.json`
  synchronously on the click turn.
- **#4 zero redundant journal commits**: `WindowShepherdService` tracks
  capture generations whose rescue entry is already durable
  (`_durablyJournaledCaptureTokens`). Ordinary `Hide` after a durable capture
  returns before rewriting the identical entry; an intentional-hide marker
  invalidates the flag so a later retained capture re-establishes rescue intent.
  Verified by `RuntimeStabilizationSelfTest.JournaledCapture_*`.
- **#5 ensureFinalPass**: latch only when a pass is already pending, so
  `RequestRelayout(ensureFinalPass:true)` yields exactly ONE pass when idle and
  ONE existing pass + ONE follow-up when pending. Verified by
  `RequestRelayoutFinalPassTests`.
- **#7 relative z-order**: split z-order health uses `ZOrder.IsOrderedAbove`
  (relative order, ignores IME/helper/overlay HWNDs between panes) instead of
  strict `GetWindow(top, GW_HWNDNEXT) == bottom`. Verified by
  `RuntimeStabilizationSelfTest.ZOrder_*`.
- **#10 instrumentation**: `RuntimeTelemetry` already records per-transition
  timings and native-operation counts; production paths call the counters.

## Supervised live desktop validation (run on a real Windows session)

Use Chrome + Edge + Notepad/Explorer as captured guests.

- **A. Four-tab split escape**: A+B split → C → A+B → D → A+B, repeat ≥50
  alternating cycles. Expect 50/50 successful escapes (no trapped-in-split).
- **B. A/B member focus**: alternate A/B 100×. Expect no member hide/show and no
  repeated geometry batch after stable layout.
- **C. Ordinary rapid switching**: ≥200 switches across A/B/C/D. Expect no stale
  visible windows, no stacked guests, no swallowed clicks, no crash.
- **D. Split with IME/helper/overlay HWND present**: open an IME / accessibility
  overlay during split; expect no repeated deferred-positioning storm.
- **E. Drag/resize container ≥20s**: record native operation count and visible
  jitter (see `RuntimeTelemetry` p50/p95/p99).
- **F. Hard-kill recovery**: background captured guests, force-kill TabDock,
  relaunch; expect all rescued.
- **G. Lifecycle**: active/background/split/dormant member close,
  minimize/restore, Alt+Tab, pop-out, close-cancel.

These require a human at a Windows desktop; they are NOT covered by the
deterministic gates. `tests/ValidationDriver` provides the real-input harness
for A–F.

## WM_WINDOWPOSCHANGING investigation (conclusion)

After eliminating duplicate work (coalesced `RequestRelayout` via
`PresentationLayoutCoordinator`, single per-frame pass, `WM_WINDOWPOSCHANGED`
re-glue in the same message loop as the container move), the guest is already
re-glued in the same compositor frame as the container.

`WM_WINDOWPOSCHANGING` fires *before* the final rect is known, so a handler
there would have to guess the post-move geometry and would itself become a
source of churn rather than a better signal. Under the Shepherd model (separate
top-level DWM surfaces, no reparenting), the guest can never be pixel-identical
to a single compositor surface regardless of how many callbacks are added.

**Conclusion**: keep `WM_WINDOWPOSCHANGED` (already the most immediate native
signal). Do NOT add `WM_WINDOWPOSCHANGING`. Any residual sub-pixel drag jitter
after duplicate-work removal is an architecture ceiling of the separate-surface
Shepherd model, not a callback deficit. No animation / reparent / style
mutations reintroduced.
