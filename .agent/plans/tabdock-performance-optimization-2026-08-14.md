# TabDock performance optimization campaign — 2026-08-14

## Objective

Run a measurement-first performance, efficiency, and maintainability pass on
the live `main` baseline while preserving the Shepherd/no-reparent model,
native identity gates, recovery durability, input/focus behavior, DPI behavior,
and existing validation contracts.

## Baseline authority

- Live baseline at session start: resolved from Git as `e0cde3405d7c2434baff3583439c1d5cb661d76a`
- Branch: `main`; `origin/main` matched at session start.
- Prior pass: `docs/internal/perf-2026-07-25.md`; it was code-reviewed but not
  stopwatch-measured. Its existing optimizations are treated as invariants.

## Execution phases

1. Phase A — add and run the non-gating performance harness; record build and
   runtime baselines. [complete]
2. Phase B — apply only very-low-risk changes with deterministic coverage:
   desktop reorder early-drop, diagnostic trace allocation cleanup, and safe
   post-Loaded container HWND reuse. [complete]
3. Phase C — remove proven duplicate builds and make OpenSpec installation
   repository-owned, exact, lifecycle-disabled, and npm-cacheable. Evaluate
   NuGet lock mode without forcing fragile restore behavior. [complete]
4. Phases D/E — compare picker cold/warm icon cost and other proxies; retain
   asynchronous or policy changes only when repeated measurements justify them.
   [complete: picker retained; logger/persistence/min-track/z-order changes
   intentionally deferred]
5. Phase F — characterize large stateful classes and extract no policy unless
   ownership and native transaction ordering remain clearer. [complete: no
   safe extraction justified]
6. Phases G/H — run the canonical Release qualification, review specs/diff,
   then commit/push only as explicitly authorized and verify exact-SHA CI.
   [G complete; H pending commit/push/hosted CI]

## Change record format

For each retained optimization, record original cost, preserved behavior,
safety argument, measurements, and validation in
`docs/internal/perf-2026-08-14.md`. Spec deltas, if any, remain in their
canonical OpenSpec change/spec source; generated mirrors are not hand-edited.

## Current status

- Phase A infrastructure: implemented; runtime baseline, post-change picker,
  and build matrix measurements captured.
- Runtime changes: low-risk changes and bounded asynchronous picker icon
  resolution implemented with deterministic self-tests.
- CI/tooling changes: solution duplicate builds removed; locked OpenSpec/npm
  tooling and NuGet lock mode implemented.
- Validation: final canonical Release qualification passed; hosted-CI evidence
  remains pending the explicit commit/push step.
- Blockers: none known.
