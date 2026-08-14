## Phase A — baseline and harness

- [x] Add the isolated performance project and `scripts/perf.ps1`.
- [x] Measure trace allocations, logger bursts, picker cold/warm refresh, and
  changed/unchanged persistence in JSON with readable summaries.
- [x] Add/record restore, solution/project, OpenSpec, publish, and final
  validation timing evidence.

## Phase B — very-low-risk runtime changes

- [x] Early-drop uncaptured desktop reorder events before trace allocation or
  dispatcher posting; retain captured reference and dispatch validation.
- [x] Add deterministic uncaptured/captured/released/recycled/correct reorder
  tests and relevant diagnostic trace assertions.
- [x] Remove redundant diagnostic data dictionary allocations and add defensive
  copy/snapshot/clear coverage.
- [x] Reuse `_containerHwnd` on post-Loaded paths while retaining lifecycle
  safety and the pre-Loaded handle query.

## Phase C — build and tooling

- [x] Remove solution-contained duplicate builds from `scripts/validate.ps1`.
- [x] Add locked repository-owned OpenSpec tooling and use it in local/hosted
  validation with npm cache and lifecycle scripts disabled.
- [x] Evaluate NuGet lock mode; do not retain it because the SDK-generated
  `Microsoft.NET.ILLink.Tasks` version differs between local and hosted SDK
  restores, and document the reproducibility tradeoff.

## Phases D/E — evidence-dependent runtime work

- [x] Compare repeated cold/warm picker measurements and retain async icon
  loading only if its latency benefit justifies bounded concurrency/lifecycle
  complexity and tests.
- [x] Add refresh-generation, cancellation, single-flight, and failure-path
  coverage for background icon resolution.
- [x] Measure the production logger burst and inspect relayout/tab-switch
  proxies; keep hot logging/z-order/min-track changes only with repeatable
  benefit and preserved safety. No additional policy change was justified.
- [x] Measure persistence transaction cost and retain the current durable
  transaction because no meaningful safe replacement case was established.

## Phase F — maintainability

- [x] Characterize `ContainerWindow`/`WindowShepherdService`; extract only pure
  policy if ownership and transaction order become clearer. Otherwise record
  that no extraction is justified.

## Phases G/H — qualification and handoff

- [x] Run full Release build, self-tests, OpenSpec, publish, and canonical
  validation from the final tree.
- [x] Review diff/invariants and update campaign record/state without a
  self-referential state-only commit.
- [ ] Commit/push only when explicitly authorized by the task and verify CI for
  the exact resulting SHA.
