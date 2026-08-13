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

## Current checkpoint — post-remediation safety closure

- Objective: resolve the narrow R7–R11 source findings and re-review R4/R6;
  do not repeat the completed deep audit.
- Status: R7/R8/R9 implementation and deterministic validation complete; R10
  state-protocol cleanup, R11 workflow review, and focused R4/R6 review are
  complete. Repository-content implementation and pre-push validation are
  complete; Git handoff and hosted CI are dynamic follow-up state.
  The verified pre-closure baseline was clean and matched `origin/main` at
  `f4f953c962549726c8362b94e93538b9ea85b750`.
- Active plan/spec records:
  `.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md` and
  `openspec/changes/post-remediation-review-followup-2026-08-13/`.
- Prior review confirmed: R7's bool identity gate could clear recovery
  evidence after an unverifiable strong probe; R8 materially changed v2
  journal fields without a schema bump and rescue discarded tokenless entries.
- Prior review also confirmed: R9 omitted restore visibility; R10 had stale
  post-push wording; and R11 needed current action-runtime review. All are
  addressed in the repository content described above.

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

- Passed in this closure: Debug and Release application/solution/driver builds;
  canonical `validate.ps1 -Configuration Release -Ci -Publish`; NuGet audit;
  support-bundle privacy; diagnostics self-test `111 checks / 0 failures`; and
  OpenSpec validation `16/16`.
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

## Next actions (conditional on dynamic Git state)

1. Resolve branch, `HEAD`, `origin/main`, and worktree status.
2. If this repository content is uncommitted, create the single substantive
   commit and push it as a normal fast-forward; otherwise skip that phase.
3. Query hosted CI for the current main SHA and stop committing once green.
