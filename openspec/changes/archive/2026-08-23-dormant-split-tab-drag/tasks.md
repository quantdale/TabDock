# Tasks

- [x] 1.1 Verify the GroupViewModel mutation-path test gap (zero direct
      references to ReorderTabs/CommitReorder/ReleaseTab in tests).
- [x] 1.2 Verify the dormant-split drag defect against current source
      (SnapshotDragMidpoints iterated Tabs.Count containers over the
      DisplayTabs-bound ListBox; display-space boundaries fed to Tabs-space
      ReorderTabs).
- [x] 2.1 GroupViewModelMutationTests: invalid/same/past-end reorder pins,
      backward/forward parity, no-split DisplayTabs equivalence, durable
      CommitReorder semantics, ReleaseTab reference-identity matrix including
      RecoveryPending retention and non-pending removal contracts.
- [x] 2.2 TabStripDragProjection pure helper + headless matrix (no-split
      identity, pair-first/middle, leading/trailing non-members, composite
      boundaries, fail-closed anchors) + dormant-pair integration regression.
- [x] 2.3 ContainerWindow: display-slot snapshots with live-reference anchors;
      count-based structural resnapshot only; explicit composite non-drag rule.
- [x] 3.1 Full qualification: Debug/Release --no-incremental builds 0w/0e,
      xUnit both configs, release tooling, validate.ps1 -Ci -Publish,
      openspec validate, git diff --check.
- [x] 3.2 Archive this change via the canonical workflow after validation.
