# Plan: Final review of R1–R4 corrections

**Status:** complete — approved for reviewed scope; review only
**Updated:** 2026-10-21

## Objective

Independently verify the claimed R1–R4 repairs against current source and owned fixtures, including whether reparse/path-identity checks cover the actual deletion boundary. Review only; preserve all implementation work.

## Scope and constraints

- HEAD/main/origin/main independently verified at `eafe922d925b54001d35886f3807e1312cdb16cd`; implementation remains uncommitted.
- No fixes, task-checkbox changes, commits, pushes, resets, archives, real UI, native recovery, desktop input or full process gates until safety checks finish.
- Destructive probes use only newly owned scratch roots and explicitly remove fixture junctions before ordinary teardown.

## Steps

- [x] Inspect current cleanup, probe, deadline and maintained regression source.
- [x] Fingerprint reviewed inputs and run fresh focused Debug/Release validation plus inspected driver/script suites.
- [x] Probe direct/outer substituted-root and replaced-file cleanup boundaries against actual methods.
- [x] Record standards/spec verdict, correction guidance and checkpoint; verify source unchanged.

## Evidence and decisions

- Source now contains the requested ancestor-junction, renamed-root, exit-code and elapsed-budget changes, and maintained tests cover those exact shapes.
- Additional substituted-root and replaced-file probes pass: driver direct/outer and PowerShell all refuse and retain evidence. Maintained ancestor, renamed-root, exit-code and inherited-pipe cases also pass.
- Actual first Release `-Ci -Publish` gate PASS exit 0. The implementation session's retained gate log is Debug and was not accepted as Release evidence.
- All 14 reviewed input hashes unchanged. Report: `.agent/investigations/validation-safety-final-review-2026-10-21.md`.
- Evidence root: ignored `artifacts/validation-safety-final-review-2026-10-21/`.

## Handoff

**Next action:** Await direction. No further correction is required for the reviewed accidental-mutation boundary.
**Blockers:** None. Physical/native, mixed-DPI, Windows 10, signing and UI-soak qualification remain outside this review.
