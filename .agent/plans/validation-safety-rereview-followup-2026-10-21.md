# Follow-up prompt: Close four residual A02 correction defects

Read `AGENTS.md`, `.agent/STATE.md`, and `.agent/investigations/validation-safety-rereview-2026-10-21.md`. Reinspect current Git/source because other sessions may have advanced. Continue only this correction scope; preserve all existing motion/configuration/product changes. Do not commit, push, reset, clean, archive or implement the seven unrelated OpenSpec changes.

The original ordinary-case S1–S6 fixes are substantially implemented: focused Debug/Release 136/136 each, storage self-tests 25/25, script safety 20/20 and warning-free forced Release build all pass. Preserve these fixes and A01/A04/A10. Four actual fixture failures remain, so A02 must not be accepted solely from the existing green matrix. Reopen affected safety/qualification task acceptance until the additional cases pass.

## R1/P1 — Revalidate path components before cleanup

Current C# and PowerShell cleanup accept a complete marker after an ancestor of the previously verified root has been moved and replaced by a junction. The actual bootstrap refuses that path as `reparse-point-component`; cleanup still deletes the relocated evidence through it.

- Apply the same full component-level reparse refusal policy used by bootstrap immediately before recursive cleanup. Include relevant enclosing cleanup ancestry and the marker, not only the marker file's attributes.
- Keep root/run/candidate/schema ownership binding. Short-name expansion or comparing two identities through the same junction is not enough to establish the original verified boundary.
- Add maintained tests for both cleanup authorities: create a valid root/marker, move its ancestor to another disposable fixture directory, replace the original ancestor with a junction, call cleanup, and assert refusal plus byte-preserved relocated evidence and retained enclosing tree.
- All source and target paths must be within newly owned scratch space. Remove the fixture junction itself before ordinary fixture teardown; never exercise the case against real user/profile storage.

Targets: `ValidationStorageProof.TryCleanupRunOwnedRoot`, `Remove-RunOwnedTempTree`, shared resolver path policy as needed. Centralize any new native declarations in `NativeMethods.cs`; do not add unrelated dependencies or an anti-same-user-attacker security layer.

## R2/P2 — Missing established root must refuse, not authorize broader deletion

`ResourceSoakRunner.TryDeleteTemporaryRoot` and the PowerShell cleanup currently skip marker verification when the expected product directory no longer exists. Renaming `validation-product` to `renamed-product` within the run tree then causes its evidence to be deleted by parent cleanup.

- Distinguish "no product root was ever established" from "the recorded root lost current proof."
- Once a root was recorded, always require current ownership, including missing/renamed/replaced-file root cases. A helper's missing-root refusal must not be bypassed by Directory.Exists/Test-Path guards.
- Add tests at both outer cleanup boundaries, not only direct inner marker helpers. Preserve no-root synthetic/headless cleanup when genuinely applicable.

## R3/P2 — Restore successful probe exit as a prerequisite

The C# timeout rewrite removed the old ExitCode check: a child printing complete valid proof and exiting 4 now returns null (authorization) from VerifyChildProof.

- Restore the requirement that the process exit successfully AND its complete proof verify.
- Add controlled process fixtures for valid-proof/exit-4 (refuse), invalid-proof/exit-0 (refuse), valid-proof/exit-0 (authorize). Do not actually launch a mutating product child in these tests.
- Keep concurrent stream draining and the bounded exit/drain behavior; do not reintroduce the earlier synchronous-read timeout bug.

## R4/P2 — Bound post-exit PowerShell stream drains

`Invoke-StorageProbe` and recovery smoke wait for exit with a timeout and then read unbounded Task.Result. A child exiting promptly while a review-owned descendant holds inherited pipes for four seconds made the real probe return successfully at 4,154 ms against a 300 ms budget.

- Carry one elapsed-time/cancellation deadline through process exit and completion of both stdout/stderr drains. Ensure success cannot be returned after an unbounded post-exit wait.
- On timeout/error, refuse and dispose/reap only proven launched resources. Do not kill arbitrary process names or unproven descendants to close pipes.
- Add exited-parent/inherited-pipe cases alongside live-child and stderr-flood cases; use a bounded holder and prove fixture execution. Avoid tests which pass merely because process start failed or a catch sets the success flag true.

## Validation and acceptance

1. Use only owned fake production/run roots and safe fake children; no native recovery, physical input, unattended UI soak or real-profile marker fixtures.
2. Establish red-to-green maintained coverage for R1–R4 before claiming completion. Preserve first failures; don't report best-of-N runs as authoritative.
3. Rebuild normally and force a warning-free Release rebuild; run focused Debug/Release correction/storage/inspection suites, driver storage self-tests and actual shared-script safety tests. Strictly validate A02 with the pinned OpenSpec CLI.
4. Only after refusal/cleanup/deadline boundaries pass, requalify full isolated unit/Release/process/publish gates and A01/A04/A10 acceptance that depends on A02. No physical/native qualification is implied.
5. Update tasks/state with exact fresh evidence and limits. Qualify the old historical flake attribution: a concrete global-state race was demonstrated and fixed, but its identity with the original failure is unproven without that failure's stack/error.
6. Stop after these bounded repairs and validation. No commit, push, archive or unrelated feature implementation.
