# Agent state

## Current checkpoint — whole-codebase audit complete (2026-08-11)

Objective: execute the repository-wide audit requested in `goal.txt`, preserve
the Shepherd/no-reparent architecture, fix confirmed high/medium correctness,
data-loss, HWND-safety, lifecycle, timer, persistence, and harness findings,
and leave evidence-backed production-gate state.

Status: autonomous audit and remediation complete for the audited scope. The
worktree is intentionally uncommitted on `main`; no push, PR, reset, clean, or
revert was performed. Final assessment: **READY WITH DEFERRED DEBT**.

## Completed

- Recovered the repository baseline at `5404349998c49365f04873f0d7d5a2c53814b776`
  and inspected production source, native interop, models/view models/views,
  ValidationDriver/GuineaPig, Spike, project files, scripts, CI, docs, history,
  and all 12 canonical OpenSpec specs.
- Updated the durable audit waypoint at
  `docs/internal/whole-codebase-audit-waypoint.md` with architecture, coverage,
  WCA-01 through WCA-24, deferred debt, and exact validation.
- Hardened hidden-window journal ordering/failure behavior and retryable rescue;
  persistence unreadable-state handling and duplicate-ID repair; capture/picker
  identity checks; transactional capture insertion; WinEvent hook transactions
  and captured-member dispatch identity; startup/container rollback; session
  ending normalization; native marker registration; and stale one-shot timers.
- Strengthened ValidationDriver state-snapshot recovery, window identity gates,
  verified native window operations, spawned-guest cleanup, UIA read-only
  behavior, and global mouse/keyboard cleanup.
- Updated canonical specs for journal retry/fail-closed hide, persistence
  identity/read-failure behavior, fail-closed DPI probes, and session-ending
  normalization. Corrected the internal guide's OpenSpec capability count to
  12 and corrected architecture documentation for fail-closed DPI probes,
  retryable rescue, and session-ending normalization.
- Preserved all Shepherd invariants: no production `SetParent`, no guest style or
  owner mutation, no `HWND_BOTTOM`, no production sleeps/polling workaround, and
  `EVENT_OBJECT_REORDER` callback-time identity protection.

## Validation

- Main, ValidationDriver, GuineaPig, Spike, and solution `dotnet build` commands:
  PASS, 0 warnings/errors.
- `scripts\\validate.ps1`: PASS.
- `scripts\\validate.ps1 -Publish`: PASS, including Release self-contained
  single-file `win-x64` publish.
- `openspec validate --all --no-interactive`: PASS, 12/12.
- `TabDock.exe --selftest-geometry`: PASS, exit code 0.
- `git diff --check`: PASS; only expected LF/CRLF conversion warnings.
- `repowise update`: PASS/already up to date after final edits.
- Static safety scans: no UIA action fallbacks; no direct scenario native
  mutators outside verified helper/input layers; no production reparenting,
  bottoming, guest style mutation, or production sleep/delay calls.

## Important facts and limits

- The formal Codex Security Deep Scan did not start: its worker required a
  managed filesystem permission profile unavailable in this session. Manual
  source-based security review was completed; no plugin result is claimed.
- No unattended ValidationDriver real-input run was performed. The repository
  policy requires supervised desktop interaction. Cross-machine monitor/DPI and
  native fault-injection cases therefore remain explicitly unverified.
- Deferred debt is recorded in the waypoint: conservative shutdown flag
  semantics after externally cancelled logoff; missing fault-injection seams;
  supervised cross-monitor/DPI matrix; and low-priority native/icon-path checks.
- Untracked `goal.txt` is user-supplied and must be preserved with the audit
  changes; do not stage or delete it.

## Next action

Perform one final read-only `git status --short`/diff classification, then hand
off the uncommitted work. If a supervised operator is available, run the
documented ValidationDriver batch and the cross-monitor/DPI matrix; otherwise
the autonomous gate is complete with the stated deferred debt.
