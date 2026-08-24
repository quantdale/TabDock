# Investigation: Foreground transition lease admission

Date: 2026-08-24
Scope: ValidationDriver guarded foreground transitions

## Symptom

`Input.ForceForeground` called `DesktopQualificationLease.Checkpoint` with the
requested target before calling `SetForegroundWindow`. A transition from one
already-registered test-owned window to another could therefore be rejected
because the requested target was, correctly, not foreground yet.

## Root cause

The pre-action provenance check conflated two distinct observations: the safety
of the current foreground source and the success of the requested foreground
transition. It checked the second before the operation and did not represent
the first as a reusable admission gate.

## Classification

HARNESS DEFECT. No product code or native identity authority was implicated.
The foreign-window fail-closed behavior was retained.

## Fix

`Input.ForceForeground` now:

1. proves the current foreground is an allowed, identity-current run window;
2. performs the existing bounded foreground request and stable identity checks;
3. proves the requested target's exact identity and foreground state again
   before the caller can send interaction input.

## Evidence

- `LSE06-owned-foreground-source-is-admitted` passes.
- `LSE07-foreign-foreground-source-invalidates` passes with
  `ForeignForeground`.
- ValidationDriver `--selftest all`: 127/127.
- Debug/Release solution suites: 686/686.
- Release tooling: 177/177.
- Strict OpenSpec: 34/34.
- Canonical Release validation/publish: PASS.

The physical drag, split zero-delta, and inline second-tab cases were not
reclassified as product or harness outcomes because this desktop did not
provide the required exclusive supervised lease. They remain explicitly
blocked pending an exact-candidate physical run.
