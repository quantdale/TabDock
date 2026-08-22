# Post-Hardening: Stranded-Guest Transaction Tails (2026-08-23)

Objective: close the two verified HIGH-severity defects where a stale/dead
identity at a transaction tail leaves an external guest stranded or invisible,
plus one LOW diagnostic-hygiene sibling on the same tails. No new product
requirements: all three bring behavior into compliance with existing specs
(`native-window-identity` "partially completed release SHALL retain the member
binding"; `ui-ux-hardening` "exactly LEFT and RIGHT SHALL be visible";
ARCHITECTURE invariant that recycled/lost HWNDs can never inherit state).
Baseline 0a8cefc (main == origin/main, clean; builds/tests/tooling/validation
all green at campaign start).

## Verified findings (fresh multi-domain audit, cross-checked in source)

| ID | Sev | Location | Defect |
|---|---|---|---|
| SG-1 | HIGH | `WindowShepherdService.ReleaseIntentionalHide` (:1798-1863) + `WindowIdentityGate.EvaluateCore:307-314` | After this transaction's own successful token removal (:1829), a single `JournalClear` failure falls to the finalization block whose boundaries evaluate with `CaptureTokenRequirement.Required` → forced Mismatch ("HWND capture token differs") → `ReleaseBoundaryFailure` returns TargetGoneOrRecycled → member detached while the guest is still SW_HIDE and the DoNotRescue marker is cleared-or-stale-discardable (rescue token probe :2491). Live external app left permanently invisible — exactly what the :1850 comment says the block must prevent. |
| SG-2 | HIGH | `SplitPresentationController.SuspendForGuest:167-172` / `ResumeMember:182-200`; downstream `PositionGuestsDeferred` all-or-nothing gate (`WindowShepherdService:1257-1260`) | Suspend treats only RecoveryPending as abortive: a dead member's Hide returns TargetGoneOrRecycled and the dormant relationship commits referencing the dead HWND. ResumeMember has no liveness gate at all. On resume the view hides C, commits PairPresented with a dead pane, and `PositionGuestsDeferred` refuses ALL positioning because one entry fails its identity gate → blank panes + SetForeground into a hidden window until EVENT_OBJECT_DESTROY heals membership (persists if that event is missed). |
| SG-3 | LOW | `Release` mismatch path :1738-1743; `Hide` mismatch :1498-1504 | Mismatch paths clear journal/unregister but skip `ForgetDiagnosticSuppression`, so raw-HWND log-suppression slots survive into whatever window recycles the value — violating the documented eviction invariant (first failure of an inheriting window can be silently suppressed). |

Rejected after verification (documented for the next assessor): drag-reorder
silently disabled while a DORMANT relationship exists (`SnapshotDragMidpoints`
indexes DisplayTabs containers with Tabs-space indices) — real but fail-safe
and view-only; torn `.bak` durability gap (C-1); unbounded Resolutions ledger +
orphaned `.recovered` sidecars (C-2); duplicate `_isCapturedWindow` probes per
WinEvent (G-1); redundant back-to-back identity evaluations on the drag-tick
path (G-2 — rejected: mutation-boundary discipline, negligible measured gain);
tautology test `UnchangedLayoutUpdated_ProducesNoRelayout` and untested
`GroupViewModel.ReorderTabs` VM path (H-B2/H-A1) — test-debt items queued for a
future validation-focused session, not this correctness campaign.

## Non-negotiables preserved

Journal-before-dangerous-mutation ordering; capture-token/generation gates on
every boundary EXCEPT the two post-own-token-removal finalization boundaries
(which keep full PID/thread/class strength via the existing pre-token core);
fail-closed direction (dead/pending ⇒ no commit, retained presentation);
controller as single split authority; no view state reintroduced.

## Waves

1. **Wave A — regression net (red):**
   - `ReleaseTransactionTests`: intentional-hide release where the journal
     file is locked from the `release-intentional-hide-before-token-removal.before`
     sequencing stage (so `JournalMarkIntentionalHide` succeeds but the later
     `JournalClear` `File.Move` throws) → assert outcome == RecoveryPending,
     re-show attempted (ShowWindowCount includes the recovery SW_SHOW), token
     stays removed, DoNotRescue evidence still on disk.
   - `SplitControllerTransitionBehaviorTests`: ResumeMember returns false and
     commits nothing when any pair member fails `_isCurrent`; SuspendForGuest
     returns false with pair still presented when a member hide yields
     TargetGoneOrRecycled; DefinePair reconfigure treats a departing member's
     TargetGoneOrRecycled as Pending.
2. **Wave B — root cause (green):**
   - Shepherd: track own-token-removal in `ReleaseIntentionalHide`; route ONLY
     the two finalization boundaries through a token-tolerant evaluation built
     on the existing `WindowIdentityGate.EvaluateBeforeCaptureToken` core
     (HWND/PID/thread/class still enforced — a genuine recycle between hide and
     finalization still returns Mismatch → TargetGoneOrRecycled).
   - Controller: SuspendForGuest hide loop aborts on TargetGoneOrRecycled like
     RecoveryPending; ResumeMember gains an `_isCurrent` liveness gate over the
     resuming member AND both pair members before CommitDesired;
     DefinePair's departing-hide loop treats TargetGoneOrRecycled as Pending.
   - View: `ResumeSplitPair` pre-gates focused+partner liveness before hiding
     C; if `ResumeMember` still returns false (exotic re-entrancy), revert
     selection to C and re-present the single guest instead of laying out a
     dead pair.
   - Shepherd SG-3: add `ForgetDiagnosticSuppression` to both mismatch branches.
3. **Wave C — validation/cleanup:** Debug+Release 0w/0e, xUnit both configs,
   release tooling, `validate.ps1 -Ci -Publish`, openspec validate,
   git diff --check; STATE.md handoff. No OpenSpec delta (compliance fix with
   existing requirements); no docs contract changes beyond STATE.
