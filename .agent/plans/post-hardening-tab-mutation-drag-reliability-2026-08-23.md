# Post-Hardening: Tab Mutation & Dormant-Split Drag Reliability (2026-08-23)

Objective: (1) direct deterministic regression coverage for the untested
`GroupViewModel` mutation paths (`ReorderTabs`, `CommitReorder`, `ReleaseTab`);
(2) re-verify and root-fix the dormant-split strip-drag feature loss caused by
the Tabs ↔ DisplayTabs index-space mismatch; (3) preserve the H2
anti-oscillation snapshot rule and all split-presentation authority.

Baseline `3201d5d1b849b91fccfb35fb57f1e8b2680a1a8d`
(main == origin/main, clean). No commits newer than the handoff existed at
fetch time.

## Verified test gap (mission §2)

Repo-wide search of `tests/UnitTests/` finds ZERO references to
`ReorderTabs`, `CommitReorder`, or `GroupViewModel.ReleaseTab`. Existing
coverage touches only `Group`/`GroupManager` internals indirectly,
DisplayTabs projection shape (`GroupViewModelDisplayTabsTests`), and source
regex contracts. The handoff statement stands: no direct mutation-path suite.

## Verified dormant-split drag defect

`ContainerWindow.TabsListBox_PreviewMouseLeftButtonDown` gates on
`IsSplitPresented` only. During a DEFINED-but-DORMANT pair the handler falls
through and starts drags of non-member tabs — but:

1. `SnapshotDragMidpoints` iterates `_viewModel.Tabs.Count` containers from
   `ItemContainerGenerator.ContainerFromIndex(i)` while the ListBox is bound
   to `DisplayTabs`, which is ONE SHORTER than Tabs for as long as the pair
   exists (presented or dormant). The last container(s) resolve to null → the
   whole snapshot invalidates → `GetDropIndex` returns null forever → reorder
   is silently dead whenever a pair is merely defined.
2. Even if geometry existed, the returned DISPLAY-space boundary was fed
   directly into `Tabs`-space `ReorderTabs(currentIndex, targetIndex)` — wrong
   item for every slot after the composite.

Drag-out (release beyond bounds) is reference-based and unaffected; only
strip reorder is lost. `docs/ARCHITECTURE.md:255` already states the intent —
"the composite is not a drag unit; normal-mode drag reorder is unchanged" — so
this is a compliance fix against documented behavior, and the live OpenSpec
specs are silent on it (→ focused change `dormant-split-tab-drag`).

## Invariants preserved

- H2 anti-oscillation: midpoints snapshotted ONCE at drag start; a REORDER
  must never resnapshot (Move preserves both collection counts); only a true
  structural count change may invalidate+resnapshot (existing rule).
- Composite is NOT draggable/grabbable (DataContext cast fails today; keep).
  It IS a valid drop-boundary region for other tabs.
- Presented-pair protection unchanged: `IsSplitPresented` click-swallow and
  SplitInteractionPolicy suspension stay exactly as-is; LEFT/RIGHT identity
  stays reference-based; reordering unrelated tabs never dissolves the pair.
- `ReorderTabs` clamp-to-end contract ([A,B,C] ReorderTabs(0,999) ⇒ [B,C,A]);
  Move-not-RemoveAt+Insert identity preservation; ActiveTab follows moved item.
- RecoveryPending fail-closed release retention; non-pending outcomes remove
  the member (GroupManager semantics traced: Released AND TargetGoneOrRecycled
  both remove; only RecoveryPending retains).
- No public API changes solely for tests; production construction unchanged.

## Seams

Phase A needs NO new seam: real production stack is already headless-safe —
real `GroupManager` + real `WindowShepherdService(log, journalPath,
identityApi, dpiProbe, releaseApi)` over the existing test fakes
(`ShepherdFakeIdentityApi` multi-window via `.Add()`,
`ShepherdFakeReleaseApi`, journal pre-write + `BindCapturedWindowForTesting`)
+ real `PersistenceService(temp state.json)`. Deterministic outcomes:
healthy identity ⇒ Released; probe throw / start=0 ⇒ RecoveryPending;
definite PID/token/exe mismatch ⇒ TargetGoneOrRecycled.

Drag projection: pure internal helper `Services/TabStripDragProjection.cs`
(slot = midpoint + strip DataContext; resolver maps TabViewModel → Tabs.IndexOf,
SplitCompositeViewModel → IndexOf(Left), unknown → unresolved). No WPF types.

## Fix design (root, reference-based)

Snapshot DISPLAY slots (identity + midpoint) instead of Tabs-indexed doubles.
Drop boundary before visible slot k resolves to the authoritative anchor of
slot k (its member's current Tabs index; Left member for a composite); past
the last slot resolves to Tabs.Count. With NO composite this reduces EXACTLY
to today's formula (anchor == display index == old return), so no-split
behavior is byte-preserved. Resolving anchors LIVE by stored reference keeps
mapping correct across intermediate in-drag reorders without resnapshotting.
Count-invalidation now compares `DisplayTabs.Count` (structural change only);
reorders change neither count ⇒ H2 loop cannot re-form. Unresolvable anchor ⇒
null (fail-closed, reorder disabled for that move).

Deliberate behaviors written down (mission §8):
- Intentionally disabled dragging: the composite itself (never grabbable);
  all strip dragging while the pair is PRESENTED (click-swallow + policy).
- Accidentally disabled (fixed here): non-member reorder/drag-out prep while
  the pair is DORMANT (snapshot crash-by-null + index-space mismatch).
- Ordinary non-member tabs remain draggable during a dormant pair: YES.
- Third-tab moves around the pair: allowed; pair identity untouched because
  membership is reference-based in the controller and RebuildDisplayTabs
  re-projects after every mutation.

## Regression matrix

GroupViewModelMutationTests (direct public methods, real stack):
ReorderTabs: oldIndex -1/Count/>Count strict no-op ×(model,VM,active);
negative destination no-op; same-index no-op; destination past end clamps to
[B,C,A] in BOTH collections with same instance moved and model ActiveIndex
agreement (regression pin: MoveTab reject vs Insert mismatch impossible);
backward/forward moves A B C D: D→0, A→3, B→2 exact parity each step; no-split
Tabs/DisplayTabs order equivalence after moves.
CommitReorder: repeated reorders → CommitReorder persists FINAL order to
state.json; intermediate orders not the durable result; commit without prior
move harmless; repeated commit coherent (synchronous Save = deterministic
barrier, no sleeps).
ReleaseTab (reference-identity pins): inactive-before-active keeps active by
REFERENCE + Group.ActiveIndex tracks; inactive-after-active same; active
ordinary released → neighbour rule pinned (Tabs[min(idx,Count-1)]), no stale
reference active; first-slot and last-slot active cases; final remaining tab →
0 tabs/members, ActiveTab null, EmptiedByPopOut exactly once; unknown TabViewModel
strict no-op; RecoveryPending → EVERYTHING retained (tabs, members, active,
no event, no half-mutation); TargetGoneOrRecycled → deliberate removal contract
pinned distinctly from pending.

TabStripDragProjectionTests (pure): no-split mapping identity; pair-first /
pair-middle / leading+trailing non-members (no +1 shortcuts); composite slot
boundary values; unresolvable anchor fail-closed; empty slots null.

Dormant integration (headless): A|B defined+dormant, C/D reorder through the
production projection helper + vm.ReorderTabs — success, pair references
intact, DisplayTabs coherent, later ClearSplitComposite restores alignment.
H2: explicit test that reorder-shaped mutations do not invalidate snapshots;
count-change does.

## Phases

A. Red net: GroupViewModelMutationTests + projection tests (compile against
   the new helper; VM suite green immediately since it pins CURRENT behavior).
B. Root fix: TabStripDragProjection + ContainerWindow snapshot/getdropindex
   rewrite (display slots, live-reference anchors, DisplayTabs count check).
C. Spec + qualification: OpenSpec `dormant-split-tab-drag` delta (ADDED
   dormant-strip-interaction requirement), full gate, archive, STATE handoff.

Validation: per-batch Debug build + focused tests + git diff --check; final
Debug/Release --no-incremental builds, xUnit both configs, release tooling,
validate.ps1 -Ci -Publish, openspec validate, diff --check. Supervised
real-input dormant-pair drag scenario recorded BLOCKED_SUPERVISED (exact
command in STATE), not run, not counted as failure.

## Rejected alternatives

- `if split → disable all drag`: codifies the feature loss (explicitly banned).
- `+1` positional arithmetic converting DisplayTabs↔Tabs indexes: breaks for
  pairs not at 0/1, leading/trailing non-members, mid-strip composites.
- Making SplitCompositeViewModel draggable as a unit: redesigns split UX; the
  product treats the presented pair as structurally protected.
- Mocking/reimplementing GroupManager inside tests: duplicates authority;
  real-stack fakes already exist and exercise the actual contracts.
- Large IDragService/view abstraction: two methods and one pure helper cover
  everything testable headlessly.
- HWND/title-based drop identity: unstable across recycles/renames; object
  identity only.
