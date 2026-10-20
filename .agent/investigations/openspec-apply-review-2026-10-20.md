# Review: Applied audit-remediation OpenSpec changes

**Date:** 2026-10-20
**Status:** concluded — corrections requested; no implementation changes made

## Scope and evidence

Reviewed the current uncommitted implementations against `HEAD` at `eafe922d925b54001d35886f3807e1312cdb16cd` on `main`, equal to `origin/main`, including new untracked source/test files. The actual applied changes are:

| Change | Current tasks | Review scope |
| --- | --- | --- |
| `fix-malformed-state-version-recovery` (A01) | 8/9 | New version policy, load protection, doctor classification, regression tests |
| `fix-persistence-latest-request-wins` (A04) | 8/9 | Writer-side equality suppression, save admission behavior, interleaving tests |
| Other nine audit-remediation changes | 0 completed each | Planning only; no implementation acceptance implied |

Existing motion/UI/configuration work was excluded from these implementations, not reverted or implicitly approved. The six relevant production/test files were fingerprinted and remained unchanged throughout review. Repowise resources were unavailable; actual source, Git and explicit-path tests were used.

## Standards

No material documented-standard violation was found in the reviewed implementation. The changes retain explicit usings, nullable/file-scoped C#, the existing native authority boundary, no extra production dependency, and no schema bump. The pure shared classifier removes duplicated root-Version policy rather than adding a second native/persistence authority.

No baseline smell was serious enough to report as an actionable finding. Formatting-only differences and generic optional refactors were not treated as defects. Pre-existing issues in unapplied changes remain outside this review verdict.

## Spec findings

### R1 — P2: Doctor overrides an unsupported future-version result with current-schema corruption

**Location:** `Services/DiagnosticEnvironmentService.cs:506-511`.

`ClassifyStateJson` first obtains `Unsupported` and sets `status = "unsupported (future-schema-version)"`, but then requires today's Groups array. A future-version document without Groups, or with Groups null/an object, returns `corrupt (required-array-missing)` instead. Restore correctly stops at the future-version gate before inspecting Groups, so the two paths disagree.

The A01 diagnostics delta explicitly requires: **“a representable future version SHALL be reported unsupported.”** A future schema need not retain today's layout shape. Mislabeling such data as corrupt gives misleading support/recovery guidance; this is not a demonstrated destructive restore regression, since the persistence path still protects the primary.

**Actual built-code fixture results:**

```text
{"Version":3}                 doctor=corrupt (required-array-missing)
{"Version":3,"Groups":null}   doctor=corrupt (required-array-missing)
{"Version":3,"Groups":{}}     doctor=corrupt (required-array-missing)
{"Version":3,"Groups":[]}     doctor=unsupported (future-schema-version)
```

In every fixture, actual restore blocked later empty saves and preserved the input bytes. No production-profile files or diagnostic process were used.

**Correction:** Make unsupported-version status authoritative before enforcing supported-schema structure. Available metadata can remain best effort, but absent/different Groups must not overwrite `unsupported`. Add the missing/invalid-shape matrix to the pure classifier tests; run real doctor fixtures only after A02/A10 make them safe. This corrects the existing A01 contract, not a new product feature.

### R2 — P3: A04's failure/retry regression does not retry the same snapshot

**Location:** `tests/UnitTests/PersistenceLatestRequestWinsTests.cs:281-287`; supporting default at `Models/Group.cs:27`.

`LatestSnapshot_RetriesAfterFailedFinalWrite` calls `MakeGroup(1, "retry-B")` separately before and after the injected failure. Every Group gets a fresh Guid. Since Id is serialized, the two JSON snapshots differ even though their names match.

The A04 requirement is: **“the same snapshot is requested after the fault is removed.”** The current fixture could miss a bug that prematurely advances the completed-content marker on failure: the different retry ID bypasses unchanged-content suppression. Matching only the name is insufficient regression evidence for that contract.

**Evidence:** actual Group construction produces different IDs for the two helper-equivalent calls. A separate actual-code probe reusing the exact same group object succeeds today, with three primary-install attempts (seed, failed B, retry B). Thus this is a test-proof gap, not a reproduced production retry failure.

**Correction:** Reuse one retry group/snapshot or assign a fixed ID, assert byte-identical intended metadata across attempts, verify the first failed attempt preserves prior complete state, and prove the identical retry performs an install rather than disappearing through deduplication. Ideally mutation-check that an erroneously advanced completed marker makes this test fail.

## Additional follow-up coverage, not another reproduced defect

The new `PersistenceService.Load` outer unexpected-failure catch (`:326-342`) is a useful safety defense but has no deliberate unexpected-path regression in the added A01 suite. After the type fix, wrong-type Version cases follow normal corruption handling; quarantine/read-denied cases exercise already-classified branches rather than this catch.

An explicit-path probe whose injected text reader returns null triggers an unexpected parse argument failure and confirms the current implementation latches `StateLoadFailed` and rejects subsequent sync/async empty saves. Convert an equivalent deterministic fault into a maintained test, including manager emergency/exit save boundaries and byte-preserved evidence. This suggestion strengthens the newly claimed safety boundary; it is not evidence that the current catch is broken. Merely throwing from a reader is insufficient to prove the outer catch if the inner reader catch classifies it normally.

## Validation

- `dotnet build TabDock.sln`: PASS, 0 warnings/errors.
- Debug targeted filter: **80/80 PASS**.
- Release targeted filter: **80/80 PASS**.
- Filter: `FullyQualifiedName~TabDock.UnitTests.Persistence|FullyQualifiedName~TabDock.UnitTests.DiagnosticEnvironmentClassificationTests`.
- Both changes pass strict OpenSpec validation; artifact validity does not establish runtime correctness.
- Disposable probe reproduces R1 in three future-schema variants; confirms R2 fixture-ID mismatch; confirms current exact-object retry and unexpected-load protection work.
- The standard full-suite/Release/process/ZIP gate was deliberately not invoked: A02/A10 are still unapplied. No recovery command, live UI soak, SendInput or production-profile fixture was run.
- Relevant source/test input hashes remained unchanged; no fixes, task-checkbox changes, commits or pushes were made by this review.

Ignored local evidence: `artifacts/openspec-apply-review-2026-10-20/` contains Debug/Release TRX, `probes.log`, reviewed-input hashes, and `probe-source.zip`. The temporary review probe references the real built app and does not replace product methods with copied implementations. Its exit 1 is expected while R1 and the coverage assertions remain red. Keep these machine-local artifacts out of Git.

## Verdict and handoff

**Standards: 0 material findings. Spec: 2 findings — one P2 diagnostic-classification defect, one P3 regression-proof gap.** A01's original wrong-type data-loss path and A04's A→B→A ordering fix pass focused checks. This is not unconditional completion/qualification: keep A01 task 3.2 and A04 task 3.3 deferred until the A02/A10 safety pair lands and the actual integrated gates pass.

Next implementation session should make the bounded R1/R2 corrections and add the unexpected-load regression, then stage A10/A02 rather than rerun unsafe full gates. Do not apply the other nine changes simply because their planning artifacts are complete.

Ready-to-use prompt: `.agent/plans/openspec-apply-followup-2026-10-20.md`.
