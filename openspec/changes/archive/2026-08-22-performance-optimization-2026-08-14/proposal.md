## Why

TabDock's previous performance pass removed several major hot-path costs but
did not have repeatable measurements. The live application still pays for
desktop-wide reorder callbacks that can be rejected before diagnostics and UI
marshalling, and the diagnostic trace constructs redundant metadata dictionaries
on every event. Build validation also recompiles projects already covered by
the solution and installs OpenSpec globally, which weakens reproducibility.

## What changes

- Add a non-gating performance harness and PowerShell entry point that emits
  environment-fingerprinted JSON and readable summaries for trace, logging,
  picker, persistence, and build measurements.
- Reject uncaptured desktop `EVENT_OBJECT_REORDER` callbacks immediately after
  the callback-time foreground/member resolution; preserve the dispatcher hop
  and reference-identity revalidation for captured events.
- Remove redundant `DiagnosticEventRecord.Data` construction while preserving
  non-null public data, defensive copies, ordering, capacity, and concurrency.
- Reuse the loaded container HWND on post-Loaded hot paths.
- Build the solution once in validation, then build only projects outside the
  solution explicitly.
- Install the exact OpenSpec CLI from a repository-owned npm lockfile with
  lifecycle scripts disabled and an Actions npm cache keyed by that lockfile.
- Evaluate async picker icons, persistence replacement primitives, z-order
  epochs, minimum-track scheduling, NuGet lock mode, and large-class extraction
  from repeated evidence; retain no speculative complexity when the evidence
  is weak.

## Scope and invariants

This change does not reparent or restyle guests, weaken HWND/process-start or
generation checks, alter crash-recovery durability, debounce semantic saves,
change split identity, remove WinEvent classes, or change user-visible picker
candidate membership/order. Medium-risk options are kept only when the
measurement harness demonstrates a repeatable benefit and deterministic tests
cover stale/lifecycle/failure behavior.

## Impact

The code impact is limited to diagnostics, WinEvent filtering, loaded-container
HWND reads, validation/tooling, and test/performance infrastructure. No persisted
state or recovery-journal schema changes are introduced.
