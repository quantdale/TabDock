# Re-review: A02 S1–S6 corrections

**Date:** 2026-10-21
**Status:** complete — changes requested; review only
**Plan:** `.agent/plans/validation-safety-rereview-2026-10-21.md`

## Scope and verdict

Reviewed the current uncommitted correction implementation against the six findings and follow-up prompt from `.agent/investigations/validation-safety-review-2026-10-21.md`. Git independently reports `main`, with HEAD and origin/main both `eafe922d925b54001d35886f3807e1312cdb16cd`. The earlier reviewed source archive and current file bodies, rather than the goal-completion statement, were used as evidence. Existing unrelated motion/configuration work was preserved.

**The corrections repair the ordinary cases from the previous review, and the xUnit warnings are gone. Four residual defects still prevent unconditional A02 safety acceptance: two destructive cleanup boundaries, an exit-status regression, and an unbounded PowerShell post-exit drain.** These were reproduced on actual methods with disposable fixtures; no real user storage or native window was manipulated.

## Standards

No additional material manually assessed standards violation. P/Invoke remains centralized in `NativeMethods.cs`; the new code uses the existing project/style conventions. Fresh forced Release solution rebuild: **0 warnings, 0 errors**. The earlier two xUnit1031 warnings are repaired by async settlement awaits. External review-harness compilation issues were fixed only in the disposable harness, not in the repository; they are not application build failures.

## Spec findings

### R1 — P1: Cleanup follows a replaced ancestor junction and deletes evidence outside the original tree

**Locations:** `tests/ValidationDriver/TabDock.ValidationDriver/ValidationStorageProof.cs:183-195`; `scripts/ValidationStorage.ps1:175-211`.

The driver checks the marker file itself for ReparsePoint but does not recheck the root's ancestor components. Its verified-identity helper expands short names; it does not resolve/reject an ancestor junction. The PowerShell cleanup also lacks a component reparse check and uses lexical full-path equality. A complete marker is therefore insufficient to prove the directory still belongs to the previously verified tree.

**Actual fixture:** create an ordinary owned root and complete marker, move its ancestor into another review-owned directory, then replace that ancestor with a junction to the moved directory. No marker rewriting or concurrent race is necessary. The actual child resolver now refuses the path with `reparse-point-component`, but the actual cleanup helper accepts it and recursively deletes the relocated evidence. The corresponding PowerShell outer-cleanup authority also accepts the complete marker and deletes the relocated enclosing tree through the junction.

```text
PASS bootstrap refuses replaced ancestor: refusal=reparse-point-component
FAIL cleanup refuses junction ancestor and retains relocated evidence:
  deleted=True; refusal=null; outsideEvidenceRetained=False
FAIL ancestor junction replacement => refuse outside cleanup:
  deleted=True; outsideEvidenceRetained=False
```

All junction targets and deleted files were inside newly created review-owned scratch directories, not a profile or user directory. The fixture junction was explicitly removed before ordinary fixture teardown.

**Contract:** A02 rejects reparse components and requires cleanup confined to verified temporary storage with current ownership; the design explicitly calls for revalidation after marker/path replacement. The prior correction prompt also requires current path/reparse constraints immediately before deletion. This is that existing accidental-replacement boundary, not a request for protection against an authenticated same-user attacker.

**Correction:** Reuse the bootstrap's full component-level reparse policy for root, relevant cleanup ancestry, and marker before either recursive cleanup; refuse and retain the enclosing tree on ambiguity. Identity normalization alone is not a replacement for that policy. Add a moved-ancestor/junction regression for both C# and PowerShell, not only a marker-file symlink check.

### R2 — P2: A missing expected product root skips ownership proof and authorizes parent deletion

**Locations:** `tests/ValidationDriver/TabDock.ValidationDriver/ResourceSoakRunner.cs:381-392`; `scripts/ValidationStorage.ps1:175` and the final parent deletion.

Both outer cleanup paths gate ownership verification on the expected product directory still existing. If a previously established product root disappears or is renamed, the guard is skipped and the entire enclosing tree is deleted. That turns loss of proof into permission to broaden cleanup.

**Actual fixture:** create a complete marked `validation-product`, write retention evidence, rename the directory to `renamed-product` within the run tree, then clean up using the original recorded path. The actual compiled `ResourceSoakRunner.TryDeleteTemporaryRoot` was invoked through reflection without starting its Run/soak path. Its renamed evidence and enclosing tree were deleted. Invoking the actual PowerShell cleanup authority produces the same result.

```text
FAIL actual soak cleanup retains evidence if expected root disappeared:
  renamedEvidenceRetained=False; enclosingTreeRetained=False
FAIL expected root disappeared => retain ambiguous tree:
  deleted=True; renamedEvidenceRetained=False
```

**Contract:** cleanup SHALL require current ownership; ambiguous/lost ownership retains the tree rather than authorizing a wider recursive delete. The implementation may still distinguish a run which never selected a product root (for example purely headless work) from a run whose established root lost proof.

**Correction:** Once a product root has been recorded, always demand current proof, including when the directory is missing or its type changed. Let the helper refuse a missing marker/root rather than bypassing it. Test missing/renamed/replaced-file roots at both outer cleanup boundaries.

### R3 — P2: The driver authorizes a probe that exits nonzero

**Location:** `tests/ValidationDriver/TabDock.ValidationDriver/ValidationStorageProof.cs:272-273`.

The timeout rewrite removed the prior probe ExitCode check. After the streams drain, TryRunProbe returns true unconditionally. VerifyChildProof therefore authorizes a nonzero-exit probe whenever its stdout contains a complete matching proof.

**Actual fixture:** a small review-owned console child emits schema-1 validation-root proof, complete roles and exact requested root/run/candidate, then exits **4**. The actual linked current helper returns null, meaning authorization:

```text
FAIL nonzero probe exit must refuse:
  real child exit=4; authorizationResult=null (AUTHORIZED); elapsedMs=120
```

No mutating child was launched. The previous reviewed snapshot explicitly refused `process.ExitCode != 0` at line 167; this is a regression introduced while fixing the timeout. The PowerShell implementation still correctly checks exit status.

**Contract:** failed preflight refuses mutation; a success-looking payload is not enough after the producing process reports failure.

**Correction:** Require both a successful exit and complete valid proof. Add exit-4-with-valid-proof, exit-0-with-invalid-proof, and exit-0-with-valid-proof process fixtures to the driver's maintained suite.

### R4 — P2: PowerShell pipe completion remains unbounded after a child exits

**Locations:** `scripts/ValidationStorage.ps1:140-148`; the same shape at `:296-306` in recovery smoke.

The two streams now drain concurrently, fixing the direct live-child and stderr-fill examples. However, only the process wait is bounded. The following `.Result` calls have no deadline. If an exited child leaves stdout/stderr inherited by another process, the parent remains blocked until those handles close — indefinitely for a nonterminating holder. The C# helper has an explicit bounded post-exit drain; PowerShell does not.

**Actual bounded fixture:** the fake probe starts a child that holds the inherited pipes for four seconds and exits promptly itself. Against a **300 ms** budget, actual Invoke-StorageProbe returns successfully only after **4,154 ms**, rather than refusing within a bounded drain. The holder is review-owned and exits itself; its recorded PID was checked/waited after the probe.

```text
FAIL post-exit inherited pipe is bounded:
  refused=False; elapsedMs=4154; budgetMs=300
```

**Contract:** S5 and the correction prompt require one bounded process/stream deadline, not just a timed process-exit call.

**Correction:** Carry one elapsed-time/cancellation budget through exit and completion of both drains in both PowerShell helpers. On expiry, refuse and dispose/reap only proven launched resources; do not solve this by indiscriminately killing process names or arbitrary descendants. Add an exited-parent/inherited-pipe fixture, alongside the current live-child and stderr-flood tests.

## What is repaired

| Previous item | Current independent result |
| --- | --- |
| S1 blank-pair fallback / extended namespace alias | Maintained matrix and direct owned-fixture controls pass; extended/device inputs refused, explicit blank is not ordinary startup |
| S2 changed-owner exit-4 then outer finally | New shared authority retains the tested changed-owner tree; R2 remains a different missing-root bypass |
| S3 incomplete/wrong-root/wrong-candidate/unknown-schema marker | Maintained negative cases pass; R1 remains a path-replacement hole despite complete marker fields |
| S4 unsupported/nonvalidation/malformed proof contract | Maintained negative matrices pass; R3 is a separate process-exit regression |
| S5 direct live child and stderr fill | Driver/script examples pass; R4 remains post-exit unbounded stream completion in PowerShell |
| S6 process-global fixture state | Async-context scope, nonparallel collection and teardown are implemented; relevant tests pass |
| Unexpected-load xUnit warnings | Settlement awaits are async; forced Release rebuild is warning-free |
| Resource run ID | Captured after BeginRun, with current-ID guard before launch; verified in source/self-test without a UI soak |

The checkpoint's statement that the old load-timing flake **was** this race is stronger than the supplied evidence. A concrete race was reproduced and repaired; its equivalence to the historical failure is not established without that first failure's original stack/error. This is an evidence qualification, not an additional code finding.

## Fresh validation

| Check | Result |
| --- | --- |
| Debug solution build | PASS, 0 warnings/errors |
| Forced Release solution build (`--no-incremental`) | PASS, 0 warnings/errors |
| Focused unit suites, Debug | **136/136 PASS** |
| Same focused suites, Release | **136/136 PASS** |
| ValidationDriver Release build | PASS |
| Driver `--selftest storage` | **25/25 PASS** |
| Maintained `ValidationStorageSafety.Tests.ps1` | **20/20 PASS** |
| Repository-pinned A02 strict OpenSpec validation | PASS |
| Additional actual-source/actual-method fixture assertions | **Six red assertions representing R1–R4**, plus positive resolver controls |
| Reviewed input stability | All **48** source/test/config/spec/doc hashes unchanged |

Focused filter: unit namespace Persistence, DiagnosticEnvironmentClassificationTests, PendingRecoveryInspectionNonMutationTests and ValidationStorageRootTests. The selected tests use disposable paths; the driver/script suites were inspected before invocation. No full 944-test rerun, canonical CI/publish/process gate, actual application UI soak, native recovery or desktop input was performed during this review. Previously reported full normal-path PASS is not refuted as a run result, but does not establish these untested failure guarantees.

## Evidence and handoff

Ignored local evidence: `artifacts/validation-safety-rereview-2026-10-21/`, including builds, two TRX, driver/script safety output, `adversarial-driver.log`, `adversarial-script.log`, input hashes and `review-probe-source.zip`. The disposable probe links the current actual ValidationStorageProof source and references the actual built app; the soak cleanup call targets the actual compiled driver via reflection. PowerShell probes dot-source the actual shared helper. The fake program does not call product code, recovery, UI or native APIs.

No application/test/helper source, OpenSpec task checkbox, commit, push or archive was changed by this review. Only durable review records/checkpoint and ignored/temporary review fixtures were written. The seven other product changes remain out of scope.

**Next:** apply `.agent/plans/validation-safety-rereview-followup-2026-10-21.md`; repair R1–R4, add maintained red-to-green cases and requalify dependent gates only after the safety boundary is established. Do not discard working A01/A04/A10 fixes or the verified ordinary S1–S6 corrections.

**Summary:** Standards: 0 material manual findings, warning-free product rebuild. Spec: 4 findings; worst is P1 destructive cleanup through an ancestor junction (R1).
