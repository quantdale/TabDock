# Review: Correction and validation-safety stage

**Date:** 2026-10-21
**Status:** concluded — changes requested; no fixes implemented by this review

## Scope and verdict

Reviewed the current uncommitted A01/A04 corrections and A10/A02 implementations against `HEAD` / `main` / `origin/main` at `eafe922d925b54001d35886f3807e1312cdb16cd`. A01/A04/A10/A02 task lists currently say 9/9, 9/9, 10/10 and 16/16. The remaining seven changes are still planning-only. Existing motion/UI/configuration work was preserved and is not implicitly approved.

**The original R1/R2 corrections are implemented, and the new unexpected-load protection tests exercise the intended branch. The ordinary A10 read-only separation looks sound. A02 is not ready for unconditional acceptance: actual fixture probes reproduce six actionable safety/qualification findings below.** Passing ordinary tests do not establish the missing refusal and cleanup guarantees.

## Standards

No additional material manually assessed coding-standard violation was found. New default storage consumers consistently use the shared resolver and no new application P/Invoke or unrelated dependency was introduced.

The claim of a clean build needs qualification: a fresh Release rebuild succeeds with **two xUnit1031 warnings** at `tests/UnitTests/PersistenceUnexpectedLoadProtectionTests.cs:153,157` for blocking task waits. These are tooling-reported issues, not duplicated as manual smell findings. Convert the affected test to async/await and verify a warning-free rebuild rather than relying on incremental-build output.

## Spec findings

### S1 — P1: The resolver can accept production aliases or silently fall back on invalid explicit input

**Locations:** `Services/ProductStorage.cs:133-138`, `:163-173`, `:332-350`.

The A02 contract requires production overlap/alias rejection and no production fallback for invalid overrides. Current checks use lexical `GetFullPath`/relative-path comparison, not a consistent filesystem identity. A matching marker and a root spelled as `\\?\C:\...\production-profile` are accepted even when `productionDirectory` names the same directory as `C:\...\production-profile`. This is an alternate Win32 spelling, not a reparse point, so the component check does not help.

The paired-input check also collapses explicit whitespace values into absence: resolving root=`" "`, run=`" "` returns a usable non-validation storage object selecting the supplied production directory. Invalid explicitly supplied configuration must be refused rather than interpreted as ordinary startup.

**Actual built-code probes, using a fake production directory inside an owned temp tree:**

```text
production path alias refused: aliasKind=extended-prefix usable=True
explicit whitespace pair refused: usable=True validation=False selectedProduction=True
```

The trailing-dot fixture was refused, so that spelling is not reported as another demonstrated bypass. No real production directory or journal was used.

**Correction:** Distinguish truly absent inputs from invalid blank supplied inputs. Reject unsupported device/extended namespaces or normalize supported forms to a common, verified directory identity before overlap/ownership checks; cover other relevant Windows aliases as well. Keep parent and child path rules consistent and fail closed if identity cannot be established. Do not create a marker in real product storage to reproduce this.

### S2 — P1: PowerShell outer cleanup deletes evidence after ownership is lost on an early failure

**Locations:** `scripts/validate.ps1:168-267`, especially `:251-260`; outer cleanup `:495-501`.

`RetainTempRoot` is set only in the late happy-path ownership check. If a probe loses/changes the marker and exits nonzero, `Invoke-StorageProbe` throws before that check. The inner finally correctly avoids its product-root deletion, but the outer script finally still recursively deletes the entire TempRoot, including the ambiguous validation root.

This contradicts the explicit A02 scenario **“the launcher retains ambiguous storage for review and does not recursively delete it.”** A local refusal should not authorize a broader parent-directory deletion.

**Actual-source repro:** a run-owned fake probe changes only its fixture marker to another owner, writes a retention sentinel and exits 4. A wrapper executes the real two PowerShell function ASTs and the real outer finally body from `validate.ps1`, with only a disposable TempRoot. Result:

```text
EXPECTED fixture probe refusal: ... exit code 4
Ownership changed inside probe; evidence retained by actual function + outer finally: False
```

**Correction:** Put ownership/retention assessment on all exits, including probe failure, child failure, timeout, sentinel failure and exception paths. The outer directory cleanup must honor a refused child-root deletion, not bypass it. Where ownership cannot be re-established, retain the enclosing tree. Add actual-script failure-path tests, not just helper-level ownership tests.

### S3 — P2: Cleanup authorizes deletion from RunId alone, ignoring the marker's root/candidate/schema binding

**Locations:** `tests/ValidationDriver/TabDock.ValidationDriver/ValidationStorageProof.cs:108-126`; analogous check at `scripts/validate.ps1:251-260`.

Startup validates a versioned root/run/candidate marker, but cleanup accepts any JSON object containing the expected RunId. A marker with no schema, no ProductRoot, and no CandidatePath authorizes recursive deletion. A valid marker copied from another directory in the same run also authorizes deletion of the wrong directory.

**Actual cleanup-helper probes:**

```text
incomplete marker blocks cleanup: deleted=True retained=False
wrong-root copied marker blocks cleanup: deleted=True retained=False
```

Both deletions were limited to review-owned fixture directories. This is weaker than the A02 marker-bound cleanup design and its requirement that ambiguous ownership remain non-destructive.

**Correction:** Revalidate the complete supported ownership record, actual requested root, expected candidate/run identity and path/reparse constraints immediately before cleanup. Share or centrally test the same semantics in driver and PowerShell; malformed/partial/wrong-root/wrong-candidate markers must retain evidence. This is separate from S2, which bypasses even a failed inner check via parent cleanup.

### S4 — P2: Parent proof validation accepts unsupported or explicitly non-validation proof

**Locations:** `tests/ValidationDriver/TabDock.ValidationDriver/ValidationStorageProof.cs:71-100`; PowerShell preflight at `scripts/validate.ps1:171-187`.

The proof includes ProbeSchema, IsValidationRoot and covered Roles, but the launch gate only checks Status plus root/run/candidate strings. A proof with `ProbeSchema=999`, `IsValidationRoot=false` and no Roles is accepted when those strings match. The driver's new positive self-test itself supplies an incomplete proof shape, so it reinforces this weak check.

**Actual probe:** `VerifyProof` returned null (authorized) for that unsupported/non-validation record. No mutating child was launched.

**Correction:** Validate supported proof schema, required field types, explicit validation-root status and the required storage-role contract before authorizing launch. Unknown schemas/incomplete proofs must refuse. Convert malformed values into stable refusal outcomes rather than depending on arbitrary getter exceptions. Update both launchers and their negative/positive fixtures together.

### S5 — P2: Probe stream reads happen before the timeout and can hang indefinitely

**Location:** `tests/ValidationDriver/TabDock.ValidationDriver/ValidationStorageProof.cs:157-165`.

The helper calls synchronous stdout `ReadToEnd`, then stderr `ReadToEnd`, before `WaitForExit(30_000)`. A live child holding either pipe open never reaches the timed wait. Serial pipe draining can also deadlock if the unread stream fills. The parent process/time-bound requirement therefore is not enforced by the apparent timeout.

**Actual bounded fake-child repro:** a review-owned console executable holds stdout open for 35 seconds, then exits. The real helper returns only after **35.11 seconds**, not at its 30-second budget. A child that never exits would remain stuck in the read; the review did not leave such a child running.

**Correction:** Drain both streams asynchronously/concurrently under one cancellation/deadline, await process exit within that deadline, and reliably terminate/reap only the launched child on timeout or error. Add no-output-hang and stderr-flood fixtures. Do not merely move the existing sequential reads after the wait and leave a full-pipe deadlock possible.

### S6 — P2: Storage-root tests mutate process-global authority while unrelated test classes may run

**Locations:** `tests/UnitTests/ValidationStorageRootTests.cs:17`, `:37`, `:240-275`, `:322-327`; process-wide setters at `Services/ProductStorage.cs:85-96`.

The class installs and resets ProductStorage.Current, including a refused resolution, and redirects Console.Out. It has no nonparallel collection or process isolation. xUnit can execute unrelated classes concurrently; several existing tests use default-path consumers, including RuntimeStabilizationTests and actual support export. They can observe the wrong fixture root, a refusal, or a reset while still using the preceding root.

**Deterministic allowed-interleaving repro using actual test methods:** execute `ValidationStorageRootTests.RefusedStorage_BlocksDefaultConsumersWithoutProductionFallback`, keep that fixture alive, then execute `RuntimeStabilizationTests.SwitchActiveTab_SameIndexIsNoOp_AndDifferentIndexNeverWritesSynchronously` before the first fixture disposes. Result:

```text
FAIL real cross-class interleaving: InvalidOperationException:
product storage is unavailable (marker-missing); refusing any default storage fallback
```

No production-profile write was made: the refused override remained installed throughout the second call. This proves a concrete order-dependent test failure. It does **not** establish the cause of the previously reported Release flake, whose original stack/error was not supplied.

**Correction:** Run these global-state tests in a genuinely nonparallel collection (preventing concurrent unrelated default consumers), or isolate them in child processes/use scoped injection. Restore prior state and Console.Out with finally-based teardown; a local lock used only by this test class would not protect unrelated consumers. Add a maintained interleaving/isolation regression and rerun full validation only after safety gaps are repaired.

## Corrections and behavior verified positively

- A01's future-schema diagnostic precedence is repaired and has missing/null/object/string/number cases plus best-effort array metadata tests.
- A04's retry test now reuses a fixed-ID object, compares intended JSON, verifies actual install attempts and checks complete prior state on failure. Production request-ordering work was not unnecessarily replaced.
- The new unexpected-load test uses null reader output to reach the outer Load guard and checks direct sync/async and manager emergency/exit boundaries. Its blocking awaits should be changed to satisfy the analyzer.
- A10 removes mutating discovery defaults, preserving explicit-directory inspection artifacts and moving eligible temp hygiene to explicit startup/recovery callers. No additional A10 behavior defect was reproduced.
- Normal root/proof cases and the new driver's 11 advertised self-tests pass. Their negative matrices omit the demonstrated alias/blank/schema/partial-marker/outer-finally/stream-budget cases.

## Validation and limits

Fresh independent review evidence:

| Check | Result |
| --- | --- |
| Initial `dotnet build TabDock.sln` | PASS, 0 warnings/errors (incremental) |
| Focused correction/persistence/inspection/storage tests, Debug | 122/122 PASS |
| Same focused tests, Release | 122/122 PASS |
| Forced Release rebuild | succeeds, **2 xUnit1031 warnings**, 0 errors |
| ValidationDriver Release build | PASS, 0 warnings/errors |
| Driver `--selftest storage` | 11/11 PASS |
| A02 and A10 strict OpenSpec validation | PASS; artifact validity is not implementation safety |
| Resolver/helper adversarial fixture assertions | Five red assertions: alias, blank fallback, incomplete/cross-root cleanup, unsupported proof |
| Actual PowerShell failure-path fixture | Red: lost-owner evidence deleted by outer cleanup |
| Actual helper against bounded fake probe | Red: elapsed 35.11 s exceeds 30 s budget |
| Actual cross-class test interleaving | Red: default consumer observes refused global root |
| Review input stability | 29 fingerprinted source/test/config/doc files unchanged |

The selected filter was `Persistence|DiagnosticEnvironmentClassificationTests|PendingRecoveryInspectionNonMutationTests|ValidationStorageRootTests` scoped to the unit namespace. No full 930-test rerun, real application UI soak, rescue/recovery command, SendInput, signing, mixed-DPI, or Windows-10 qualification was performed by this review. The prior normal full-gate PASS is not fabricated by this report, but cannot prove safety for cases its fixtures do not cover.

All destructive probes operated solely inside temporary fake production/run roots. The C# helper was linked from the actual reviewed source; the resolver came from the actual built app. The PowerShell probe ran AST-extracted real functions/finally logic, not a hand-copied approximation. Safe fake executables were used only to exercise process-budget and refusal behavior, never as production candidates.

Local evidence is ignored under `artifacts/validation-safety-review-2026-10-20/`: TRX, `release-rebuild.log`, `adversarial-probes.log`, `script-cleanup.log`, `probe-timeout.log`, `test-interleave.log`, driver JSON/log, source hashes and `review-probe-source.zip`. Do not commit those machine-local files. No implementation or OpenSpec checkbox changes, commits or pushes were made.

## Acceptance and next action

**Standards:** no additional manual violation; two automated analyzer warnings remain. **Spec:** six findings; the worst are P1 resolver/cleanup boundary failures (S1/S2). Do not accept A02 as complete on its current green example matrix. Reopen its affected implementation/test/qualification work, fix the bounded cases, then repeat integrated validation under proven roots. A01/A04 acceptance gates that depend on verified A02 safety need requalification, without discarding their already-passing functional fixes.

Additional source observation for the follow-up owner: `ResourceSoakRunner.cs:51` captures validationRunId **before** its `BeginRun()` call at line 60, while Program dispatches this mode without beginning a run first. Align root proof/marker identity with the initialized run manifest, rather than an initial/stale run ID. This was not physically exercised as a UI soak and is not counted among the six fixture-reproduced findings.

Correction prompt: `.agent/plans/validation-safety-followup-2026-10-21.md`.
