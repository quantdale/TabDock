# TabDock agent state

## Git authority and self-reference rule

Git is authoritative for current `HEAD`, branch, `origin/main`, and clean or
dirty state. Every fresh session resolves them dynamically with:

```text
git rev-parse HEAD
git branch --show-current
git rev-parse origin/main
git status
```

This file never claims that an embedded SHA is the commit containing this file.
Embedded SHAs describe historical or last-substantive implementation commits
only. CI run IDs are historical evidence for the SHA they name. Do not create
a state-only commit to record the preceding push or its CI result; report the
final pushed SHA and CI result in the session output and let the next session
verify them independently.

## Current checkpoint — R12–R15 final source-hardening pass

- Objective: close only the independently identified R12 HWND mutation-boundary,
  R13 legacy pending-recovery, R14 DPI-contract drift, and R15 npm lifecycle
  warning findings; do not repeat the completed deep audit.
- Status: R12–R15 implementation, canonical local qualification, and final
  source review are complete; the worktree is intentionally staged pending the
  substantive commit, fast-forward push, and hosted CI inspection. Git refs
  remain dynamically authoritative.
- Active plan/spec records:
  `.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md` and
  `openspec/changes/post-remediation-review-followup-2026-08-13/`.
- Prior review confirmed: R7's bool identity gate could clear recovery
  evidence after an unverifiable strong probe; R8 materially changed v2
  journal fields without a schema bump and rescue discarded tokenless entries.
- Prior review also confirmed: R9 omitted restore visibility; R10 had stale
  post-push wording; and R11 needed current action-runtime review. All are
  addressed in the repository content described above.
- R12 now has strong post-journal capture revalidation, cheap generation gates
  before later native mutations, exact-token cleanup, and deterministic capture,
  hide, release, and rescue sequencing tests. R13 now has read-only
  `--pending-recovery` discovery plus explicitly confirmed `--recover-pending`
  v1/v2 recovery with temporary generation tokens and sibling-safe retirement;
  a durable resolved marker can also be retried through disk-only cleanup.
- R14 source/docs/spec now agree that known DPI-unaware guests are accepted
  under physical outer-window geometry, with DWM-rendering and hardware
  qualification caveats; unknown awareness remains fail-closed. R15's exact
  Node 24/npm 11 `--ignore-scripts` install and OpenSpec validation passed.

## Resume protocol

Repository-content status:
R7 tri-state/release safety, R8 versioned journal compatibility, R9 restore
post-state semantics, R10 state protocol, R11 workflow runtime review, and
focused R4/R6 reviews are implemented and deterministically validated. The
canonical Release gate is green; Git refs and hosted CI remain dynamically
owned by Git/the hosting service.

Handoff rule:
On resume, inspect Git dynamically. If this content is already committed and
pushed, do not repeat commit/push. Query hosted CI for the current main SHA
dynamically; a green result requires no state-only follow-up commit.

## Findings and implementation decisions

- R3: canonical instructions now require dynamic Git resolution and prohibit
  self-referential current-HEAD/own-CI state records. This file is compact;
  prior campaign evidence remains in the execplan.
- R4: `CapturedWindow` records process start, GUI thread, and a reversible
  per-capture HWND token. Hot positioning/re-glue paths use HWND/PID/thread/
  class, live object binding, and the token. Slow/destructive/delayed,
  foreground, min-track, release, and recovery paths add executable path and
  native `GetProcessTimes` process-instance verification. Journal cleanup also
  matches the complete captured identity tuple, including executable/class
  metadata, and recovery requires a nonzero
  journaled token, so stale callbacks cannot erase or mutate a newer
  same-HWND record. Failures fail closed.
- R5: all production `ShowWindow` restore/show/hide paths verify resulting
  iconic/zoomed/visible state; the `ShowWindow` BOOL is treated as previous
  visibility only. Delayed minimize restore goes through the strong gate.
- R6/M8: `GetDpiForMonitor` is no longer called. A PMv2 thread creates a hidden
  PMv2 helper HWND at the target monitor and reads `GetDpiForWindow`; context
  and helper lifetime are restored in `finally`. Conversion has an injectable
  deterministic seam; physical mixed-DPI qualification remains external.

### Safety closure decisions

- R7 uses explicit `MATCH`, `MISMATCH`, and `UNVERIFIABLE` outcomes. Only a
  match permits native mutation; unverifiable release retains its journal and
  logical member for retry.
- R8 makes the current journal schema v3. Historical v1/v2 tokenless evidence
  is preserved as named pending/manual-recovery evidence rather than silently
  discarded; future versions remain untouched.
- R7 release ownership is transaction-first: Shepherd verifies and completes
  native release before GroupManager removes a member. `RecoveryPending`
  retains both the member binding and durable journal for retry; a positive
  mismatch may clear only the exact old identity tuple. Group close and
  emergency release continue safe members while retaining pending members;
  a partial close does not activate a pending guest.

## Preserved invariants

Shepherd/no-reparent architecture; no production `SetParent` or
`AttachThreadInput`; no global `HWND_BOTTOM`; UIPI/elevation gates; HDWP
chaining; full-state crash journal and persistence fail-safe; zero-tab group
fix; WinEvent fail-closed policy; support-bundle privacy; bounded min-track
probing; and ValidationDriver safety guards.

## Validation and remaining work

- Focused and canonical validation: Debug/Release app and ValidationDriver
  builds passed with 0 warnings/errors; diagnostics self-test passed `153/0`;
  OpenSpec passed `16/16`; NuGet audit and support-bundle privacy passed;
  pending-recovery discovery and self-contained publish smokes passed. Exact
  Node 24/npm 11 pinned OpenSpec installation with `--ignore-scripts` passed
  version and validation. Commit, push, and hosted CI remain.
- Remaining product qualification: M2 supervised session-ending cancellation;
  physical M8 mixed-DPI/multi-monitor matrix; unavailable Chrome scenarios;
  and the foreground-policy-limited split case.
- Do not run destructive session-ending tests, fake mixed-DPI qualification,
  or unsafe arbitrary-window input.
- External qualification remains: M2 supervised Windows session-ending test;
  physical M8 mixed-DPI/multi-monitor matrix; Chrome scenarios; and the
  foreground-activation-limited split scenario if still applicable.

## Historical evidence

The preceding remediation and qualification history is preserved in the
execplan. Its earlier hosted CI run 16 (`31708992953`) for the historical
qualification SHA `a48121c7f91d3643096d1b9ec79da681af9633e8` is historical
evidence only and is not the current Git state. The previous state file had
grown into a self-referential checkpoint chain; its durable campaign detail is
retained in the execplan rather than repeated here.

## Next actions

1. Create the one coherent substantive commit from the reviewed staged tree.
2. Push `main` with an ordinary fast-forward and verify local/main ref parity.
3. Inspect hosted CI for the exact final SHA; fix only a real hosted defect and
   stop committing once green.
