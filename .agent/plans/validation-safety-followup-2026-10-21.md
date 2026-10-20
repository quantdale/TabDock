# Follow-up prompt: Repair A02 safety boundaries before accepting full qualification

Review evidence: `.agent/investigations/validation-safety-review-2026-10-21.md`.

Continue the existing OpenSpec implementation, not a new broad refactor. Read `AGENTS.md`, current state, that review and the A02/A10 change artifacts. Reconcile the actual worktree first; preserve the user's dirty motion/configuration/source edits and later-session work. Do not commit, push, reset, clean, archive or broaden into the seven unimplemented product changes.

The previous A01 future-version correction, A04 fixed-ID identical-snapshot retry regression, unexpected-load protection tests and A10 read-only separation passed review. Keep them. Current review found A02 safety gaps despite 122/122 focused tests and 11/11 storage self-tests passing. Reopen the relevant A02 tasks/acceptance claims while correcting them; do not mark a known gap complete because a normal-path gate passed.

## 1. Fix resolver refusal and alias handling (S1)

Add red fixtures using fake production directories, never the real profile:

- Explicit whitespace root/run inputs must refuse rather than selecting production as if both variables were absent. Distinguish null/absent from invalid explicitly provided values.
- `\\?\` and ordinary Windows spellings of the same directory must not bypass production-overlap exclusion. Reject unsupported namespaces or compare consistently verified directory identities. Consider relevant short-path/device aliases; retain reparse/relative/noncanonical refusal.
- Invalid overrides must exit before logging/rescue/persistence with no production fallback. Keep no-override ordinary startup and the SID-scoped lease semantics unchanged.

Put new native path declarations in `NativeMethods.cs` only if genuinely required; do not add an unrelated dependency or expand the threat model into same-user adversary authentication.

## 2. Repair cleanup authorization across every exit (S2/S3)

- In the driver and PowerShell, require a complete supported marker bound to the actual root, expected run and candidate, with current path/ownership proof before recursive deletion. A matching RunId alone is insufficient. Reject incomplete/unsupported/wrong-root/wrong-candidate/copied markers.
- In `scripts/validate.ps1`, ensure probe errors, child errors, timeouts, sentinel failures and other exceptions still recheck ownership and retain ambiguous trees. The outer TempRoot finally must not recursively bypass the inner product-root cleanup refusal.
- Add actual script-level failure tests in addition to helper tests. Reproduce a fake probe that changes its fixture marker to another owner and exits 4: the retention sentinel must survive both actual function cleanup and outer finally. Do not test by risking an outside user directory.

## 3. Validate the proof contract completely (S4)

Update both preflight verifiers and their self-tests to require the supported ProbeSchema, correct field types, `IsValidationRoot=true`, the required role set and matching canonical root/run/candidate binding. The previous helper accepted schema 999 with IsValidationRoot false and no Roles. Missing/unknown/malformed proof must produce a stable non-authorizing outcome before mutating launch. Positive fixtures must contain the complete real protocol rather than an intentionally weakened subset.

## 4. Enforce a real process deadline (S5)

Replace sequential synchronous stdout/stderr ReadToEnd before WaitForExit in `ValidationStorageProof.TryRunProbe`. Drain both streams concurrently under one bounded cancellation/deadline; terminate and reap only the launched child on timeout/error. Add safe held-open-stdout and stderr-flood fixtures. Current code took 35.11 seconds for a child exceeding the supposed 30-second budget; a never-ending child would bypass it indefinitely. Do not replace this with another full-pipe deadlock.

## 5. Isolate test-global state and repair warning-free validation (S6)

`ValidationStorageRootTests` installs/reset process-global ProductStorage and Console.Out while other test classes may run. An actual permitted interleaving makes `RuntimeStabilizationTests.SwitchActiveTab_SameIndexIsNoOp_AndDifferentIndexNeverWritesSynchronously` throw on the other class's refused root.

Use a genuinely nonparallel xUnit collection or separate process/scoped dependency boundary that protects unrelated consumers too; a lock used only inside the mutating class is insufficient. Restore prior state/output with reliable teardown. Add an isolation regression rather than dismissing failures as load timing.

Convert blocking settlement calls in `PersistenceUnexpectedLoadProtectionTests.cs:153,157` to async/await; a forced Release rebuild currently emits two xUnit1031 warnings. Preserve the actual unexpected-load branch coverage.

Also align `ResourceSoakRunner`'s validation run ID with `TestRunProvenance.BeginRun()`: capture it after initialization, not before, and verify marker/proof/manifest use the same current ID without launching the UI soak.

## 6. Validate and report honestly

1. Keep all destructive/refusal tests inside disposable fake production/run roots. Use safe run-owned fake probes to test failures; no normal UI soak, recovery against the real profile, or SendInput.
2. Run the build and targeted resolver/proof/cleanup/discovery/persistence/correction tests in Debug and Release; require red-to-green evidence for each review case and a warning-free forced Release rebuild.
3. Verify the actual PowerShell error/finally paths, proof schema matrix, time budget and global-test isolation. Retain first failures and any unexplained flake rather than reporting best-of-N PASS.
4. Only once the safety boundaries are proven, repeat legitimate isolated child/CLI fixtures and the full unit/Release CI/publish gate. Requalify A01/A04 final gates that depended on A02 rather than discarding their working fixes. Do not silently use old normal-path logs as proof of repaired failure paths.
5. Leave accurate per-change task states and final evidence/limits in the checkpoint. No physical-input, Windows-10, mixed-DPI or signing qualification is implied. Stop after these corrections and safety qualification; wait for a separate request to apply the other product changes.
