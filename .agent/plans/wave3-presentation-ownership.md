# Wave 3 — Presentation-State Ownership Map (COMPLETE)

Campaign: architecture hardening, Wave 3 only (presentation ownership).
Baseline: main @ acdf680, clean. Final tip: de69e01 (pushed). This file is the
working design record for the wave; final dispositions below mirror
`.agent/STATE.md`.

OUTCOME: all six sub-waves implemented and verified. Gate: Debug/Release
builds 0w/0e; unit tests 302/302 both configs; release tooling 150/150;
validate.ps1 -Ci PASS; git diff --check clean.

## A. Semantics inventory

### 1. Split LEFT member

- CURRENT MUTABLE OWNERS: `SplitPresentationController._left` (runtime);
  policy string `SplitPresentationState.Left` (transition-time desired state).
- DESIRED AUTHORITY: `SplitPresentationController._left` (unchanged), committed
  ONLY from policy output.
- DERIVED PROJECTIONS: `GroupViewModel.SplitCompositeViewModel.Left`
  (strip projection); diagnostics snapshot fields.
- TRANSITION ENTRY POINTS: EnterSplit → DefinePair/Reconfigure;
  HandleMemberRemoved; CommitExplicitExit (clears).
- INVALIDATION EVENTS: split enter/replace, member removal, explicit exit,
  container teardown/session ending.
- TESTS: SplitPresentationPolicyTests (policy matrix),
  SplitPresentationControllerTests, new ToState()==policy cross-checks.

### 2. Split RIGHT member

- Identical shape to LEFT (`_right` / `State.Right`,
  composite projection suppressed-tab side).

### 3. Relationship defined

- CURRENT MUTABLE OWNERS: `_left != null && _right != null` (derived inside
  controller from #1+#2 — already single-owner by construction).
- DESIRED AUTHORITY: controller pair fields (unchanged).
- TESTS: controller tests + transition cross-checks.

### 4. Pair presented/dormant

- CURRENT MUTABLE OWNERS: `SplitPresentationController._presented`.
- DESIRED AUTHORITY: unchanged (single owner already); commits must come from
  policy output only (SuspendForGuest/ResumeMember hand-write today but with
  values identical to policy result — canonicalize).
- DERIVED PROJECTIONS: `IsInSplit` (lifecycle classification), layout mode
  choice in RelayoutGuests, Ctrl+Tab scoping via TabNavigationPolicy arg.
- TRANSITION ENTRY POINTS: SuspendForGuest (pair→dormant),
  ResumeMember (dormant→presented), HandleMemberRemoved,
  CommitExplicitExit, DefinePair (always presented).
- INVALIDATION EVENTS: third-tab activation, member click resume, member
  removal, explicit exit/replacement.
- TESTS: SplitPresentationControllerTests (pending retains prior mode),
  behavior tests, new cross-checks.

### 5. Logical active/foreground guest (THE Wave-3B hazard)

- CURRENT MUTABLE OWNERS (three parallel writes):
  - `ContainerWindow._shepherdActiveWindow` (~11 hand-sync sites);
  - `SplitPresentationController._foreground` (FocusMember etc.);
  - `GroupViewModel._activeTab` (UI selection; Group.ActiveIndex positional).
- SEMANTICS ESTABLISHED BY CODE READING:
  `_shepherdActiveWindow` = "the guest the native presentation currently
  presents as active" (focused member when presented; single full-width guest
  otherwise). After every completed transition it equals ActiveTab.Model and,
  in presented mode, Foreground. Differences exist only inside bounded
  transaction scopes (pre-seed trick, pending-hide rollback).
- DESIRED AUTHORITY: `SplitPresentationController` owns the runtime value for
  ALL modes (standalone included) via policy-backed transitions:
  - presented focus: FocusMember (exists);
  - dormant non-member switch: NEW SelectGuest (NoPair/dormant commit,
    policy-backed);
  - standalone switches: same SelectGuest path (controller starts in
    NoPair(activeGuest=null));
  - teardown: NEW Clear().
  The view's `_shepherdActiveWindow` FIELD IS DELETED; readers use
  `SplitController.Foreground`.
- DERIVED PROJECTIONS: ViewModel ActiveTab (user-selected logical tab — kept
  distinct by contract, updated AFTER commit), tab-strip highlight bindings,
  WM_ACTIVATE reassert closure, diagnostics.
- TRANSITION ENTRY POINTS: SyncShepherdActiveWindow (ordinary switch +
  rollback), FocusSplitMember, EnterSplit, ResumeSplitPair, ExitSplit,
  HandleSplitMemberRemoved, SuspendPresentedPairForUserSelection,
  ContainerWindow_Closed, ClearReleasedTabsAfterSessionEnding.
- INVALIDATION EVENTS: every one of those transitions.
- ORDERING (preserved): guarded native work → controller commit → VM
  SetActiveTab projection → layout request. RecoveryPending ⇒ no controller
  commit, VM reverted where it had speculatively moved.
- TESTS: new controller tests (SelectGuest/Clear/generation exactness);
  source-contract test: no `_shepherdActiveWindow` field may return to the
  view; ToState() cross-checks after each transition.

### 6. Single visible ordinary guest

- Same storage as #5 (mode without relationship). No separate owner. Layout
  reads authority via Foreground; visibility is observed native fact, never
  tracked logically.

### 7. Split generation

- CURRENT MUTABLE OWNERS: `SplitPresentationController._generation` — BUT two
  commit styles coexist: `= desired.Generation` (×2) vs manual `_generation++`
  (HandleMemberRemoved, CommitExplicitExit); ExplicitExit policy result
  discarded entirely.
- DESIRED AUTHORITY: controller field, written ONLY inside a canonical commit
  helper fed by policy output. No hand increments outside it.
- TESTS: generation-exactness assertions in new cross-check suite.

### 8. Settle generation

- CURRENT MUTABLE OWNERS: `SplitPresentationController._settleGeneration`
  (armed at DefinePair/ArmSettle with current generation).
- DESIRED AUTHORITY: unchanged (already single-owner; commit paths re-arm
  canonically). View owns only arm/disarm scheduling (CompositionTarget hook).
- TESTS: SettleGeneration lifecycle test (exists) + stale-callback coverage.

### 9. Pending settle

- CURRENT MUTABLE OWNERS: `SplitPresentationController._settlePending`;
  CompositionTarget.Rendering subscription liveness in the view mirrors it.
- DESIRED AUTHORITY: unchanged; DisarmSplitPresentationSettle remains the ONE
  disarm+unsubscribe point (teardown consolidation in 3F: OnClosed keeps the
  call, ContainerWindow_Closed keeps its call — both idempotent through the
  controller guard; documented rather than merged because Closed can run
  without OnClosed ordering guarantees across partials is not worth churn).

### 10. Pane refusal history

- CURRENT MUTABLE OWNERS: `ContainerWindow._refusedPaneByHwnd`
  (Dictionary<long HWND, RECT>), ~14 clear sites + mark/clear helpers.
- DESIRED AUTHORITY: NEW small stateful collaborator
  `PaneContainmentCoordinator` (Services/) owning mark/query/clear/
  clear-all keyed by CapturedWindow REFERENCE (stronger than raw HWND;
  eliminates the recycled-HWND-key residual risk; entries only ever exist
  for currently-presented guests and every visible-set boundary clears all).
  Decision stays in PaneContainmentPolicy (pure).
- INVALIDATION EVENTS (classified, all preserved):
  geometry changed (WM_EXITSIZEMOVE), DPI changed, display topology changed,
  min-constraint periodic refresh, active guest changed, split entered,
  split suspended (user selection), split resumed, split exited (both
  branches), member removed, guest move/size end (+render final pass),
  per-guest compliance restored, teardown.
- HIDDEN-RESTORE GUARANTEE: suppression decision remains
  `visible && same-rect` via PaneContainmentPolicy — coordinator stores rects
  only; visibility is sampled at decision time.
- TESTS: PaneContainmentPolicyTests stay green; NEW coordinator lifecycle
  tests (mark/query/clear/clearAll/rect-change/reference-key isolation).

### 11. Constraint dirty/minimum state

- CURRENT MUTABLE OWNERS: `_constraintDirty`, `_constraintMin{L,R}{W,H}` in
  the view.
- DISPOSITION: KEEP in the view (WPF interop-forced): consumed by
  WM_GETMINMAXINFO (WndProc) and RefreshSizeConstraint which probes native
  minima via the shepherd on the UI thread. Moving it would create an
  indirection with zero ownership gain — the audit's §3.2 finding targets the
  refusal dict (moved, see #10), not the WndProc-fed min cache. Documented
  decision.

### 12. Pending layout frame

- CURRENT MUTABLE OWNERS: `PresentationLayoutCoordinator._relayoutPending` /
  `_relayoutAfterPending` (live machinery; view schedules via delegate).
- DESIRED AUTHORITY: unchanged (coordinator).

### 13. Layout invalidation generation

- CURRENT MUTABLE OWNERS: `PresentationLayoutCoordinator._layoutGeneration` +
  `_pendingLayoutGeneration`; `InvalidateLayout()` has ZERO production callers
  → the stale-frame discard branch is unreachable in production (audit
  Top-Finding #2 tail).
- DECISION (Model B, evidence-based): stale render callbacks cannot execute
  against a different presentation world in production because every
  presentation-world change (a) routes through RelayoutGuests which re-reads
  current authority at execute time, and (b) the split settle has its own
  live generation guard (controller settle), and (c) teardown cancels the
  dispatcher continuation by clearing `_containerHwnd` before state. There is
  no reachable scenario where a queued Render frame must be cancelled rather
  than simply executed against fresh state: executing is idempotent
  (redundant-glue guards make it cheap/no-op) and always reads CURRENT state.
  Therefore REMOVE `_layoutGeneration`/`_pendingLayoutGeneration`/`gen` token
  and the stale-frame branch; RETAIN coalescing + ensureFinalPass latch
  semantics byte-for-byte (Q9/Q1/Q2 behaviors keep their tests). Record in
  ARCHITECTURE notes. If a future world-transition appears, reintroduce
  invalidation AT that semantic boundary — deliberately not speculative.
  Wait — check RequestRelayoutFinalPassTests: they exercise idle/pending/
  final-pass only, no generation test exists (the stale branch was tested
  nowhere — confirming deadness). Keep `UnchangedLayoutUpdated` test as-is.
- TESTS: existing 6 coordinator tests must stay green unmodified except any
  referencing removed members (none do).

### 14. ViewModel active tab/index

- CURRENT MUTABLE OWNERS: `GroupViewModel._activeTab`; `Group.ActiveIndex`
  (positional, persisted intent).
- DESIRED AUTHORITY: unchanged — legitimate UI/domain projection (Section 7
  of the assignment). Contract made explicit: updated strictly AFTER
  controller commit; reverted on RecoveryPending; never used as native
  truth. Tabs (identity/order) remain authoritative for membership;
  DisplayTabs remains strip projection only.
- TESTS: GroupViewModelDisplayTabsTests (index-divergence invariant),
  TabNavigationPolicyTests; new sequencing covered by cross-checks at
  controller level (VM projection order asserted in view-level source
  contract comments, behavioral proof lives in ValidationDriver scenarios).

## B. Sub-wave sequence (final dispositions)

1. 3A DONE (ffb2794): canonical `CommitDesired` helper; all transitions commit
   exclusively from policy output; mixed generation math deleted;
   ToState()==policy cross-check tests added.
2. 3B DONE (5e49018): controller.SelectGuest/Clear added; all
   `_shepherdActiveWindow` sites migrated; field DELETED (derived alias
   `ShepherdActiveWindow => _splitController.Foreground`); source-contract
   test forbids its return.
3. 3C DONE (712857c): PaneContainmentCoordinator (reference-keyed) replaced
   the view dict; 13 invalidation events routed; policy untouched; +8
   lifecycle tests.
4. 3D DONE (ac5dfa2): Model B applied — generation machinery removed
   (InvalidateLayout had zero production callers); coalescing/final-pass kept
   byte-for-byte; two dead-machinery stale tests replaced with a Model-B
   contract test; ARCHITECTURE.md notes updated (+stale Wave-1 budget-sink
   sentence corrected). NOTE vs plan: HardeningRegressionTests DID reference
   InvalidateLayout (added by 9713eee after this plan's draft claim of "no
   generation test exists") — handled as described.
5. 3E/3F DONE (de69e01): SplitInteractionFix.cs -> ContainerWindow.Split.cs
   with three concern sections; single Disarm path confirmed and dual-site
   teardown documented at both call sites (plan §9 disposition upheld);
   no behavior change.
6. DONE within 3D/3E commit set: async-callback safety audit documented in
   the coordinator remarks + ARCHITECTURE.md; settle stale-callback coverage
   confirmed existing (SplitControllerTransitionBehaviorTests +
   IsCurrentSettle policy tests); diagnostics snapshot verified to follow the
   controller authority via the ShepherdActiveWindow alias; stale comment
   references to the deleted field fixed in ValidationDriver +
   ReplaceableDispatcherTimerTests.

## C. Non-negotiables carried forward

HWND identity tiers, journal-before-mutation, RecoveryPending fail-closed,
exactly-once hides, Shepherd no-reparent, dormant-vs-presented semantics,
survivor promotion rules, resize-war suppression bounds, minimize/restore
re-show guarantee, Ctrl+Tab Wave-0 behavior.
