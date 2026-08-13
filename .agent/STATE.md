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

## Current checkpoint — post-remediation review follow-up

- Objective: complete the narrow R3/R4/R5/R6 follow-up; do not repeat the
  completed deep audit.
- Status: implementation complete; all local targeted and canonical Release
  gates are passing; one coherent commit/push and hosted CI inspection remain.
- Starting canonical handoff observed at session start:
  `c6b119a028cc83c3a955b15a488f609976404f6e`.
- Last substantive implementation baseline before this pass:
  `f1dc7ab3ac616d6f1517efafdced5eb6418d3462`.
- Active plan/spec checklist:
  `.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md` and
  `openspec/changes/post-remediation-review-followup-2026-08-13/tasks.md`.

## Findings and implementation decisions

- R3: canonical instructions now require dynamic Git resolution and prohibit
  self-referential current-HEAD/own-CI state records. This file is compact;
  prior campaign evidence remains in the execplan.
- R4: `CapturedWindow` records process start, GUI thread, and a reversible
  per-capture HWND token. Hot positioning/re-glue paths use HWND/PID/thread/
  class, live object binding, and the token. Slow/destructive/delayed,
  foreground, min-track, release, and recovery paths add executable path and
  native `GetProcessTimes` process-instance verification. Journal cleanup also
  matches the captured identity tuple, and recovery requires a nonzero
  journaled token, so stale callbacks cannot erase or mutate a newer
  same-HWND record. Failures fail closed.
- R5: all production `ShowWindow` restore/show/hide paths verify resulting
  iconic/zoomed/visible state; the `ShowWindow` BOOL is treated as previous
  visibility only. Delayed minimize restore goes through the strong gate.
- R6/M8: `GetDpiForMonitor` is no longer called. A PMv2 thread creates a hidden
  PMv2 helper HWND at the target monitor and reads `GetDpiForWindow`; context
  and helper lifetime are restored in `finally`. Conversion has an injectable
  deterministic seam; physical mixed-DPI qualification remains external.

## Preserved invariants

Shepherd/no-reparent architecture; no production `SetParent` or
`AttachThreadInput`; no global `HWND_BOTTOM`; UIPI/elevation gates; HDWP
chaining; full-state crash journal and persistence fail-safe; zero-tab group
fix; WinEvent fail-closed policy; support-bundle privacy; bounded min-track
probing; and ValidationDriver safety guards.

## Validation and remaining work

- Passed: Debug and Release solution/driver builds; diagnostics self-test
  reports 89 checks / 0 failures; geometry self-test; canonical
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`; audited restore
  with no vulnerable packages; support-bundle privacy; 16/16 OpenSpec items;
  and self-contained publish/version smoke.
- Next: finish the full diff/status review, commit once, push fast-forward,
  then fetch/verify refs and inspect hosted CI without a state-only follow-up
  commit.
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

## Next action

Finish targeted source review and Release validation, mark the follow-up
OpenSpec tasks complete, update this checkpoint with validation facts that do
not name its own future commit, then commit and push once. After CI passes,
stop creating repository commits and report the ephemeral pushed SHA/run in
the session output.
