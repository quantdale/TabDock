# Correction / follow-up prompt for the implementation session

Continue the existing OpenSpec remediation work, preserving all dirty user/source/motion/configuration changes. Do not commit, push, reset, clean, archive, or claim full qualification without explicit authorization/evidence.

Read `AGENTS.md`, `.agent/STATE.md`, `.agent/plans/audit-remediation-index-2026-10-20.md`, and `.agent/investigations/openspec-apply-review-2026-10-20.md`. Reconcile current Git and OpenSpec task status first: the reviewed snapshot had only A01 and A04 applied, each at 8/9, with the other nine still unimplemented. If the worktree has progressed, verify whether the findings still reproduce before editing; do not overwrite newer work or reimplement satisfied fixes.

## 1. Correct A01 under its existing opsx-apply change

Use `fix-malformed-state-version-recovery` and its current requirements. In `Services/DiagnosticEnvironmentService.cs`, a future schema result must stay `unsupported (future-schema-version)` even when today's Groups property is absent, null, an object, or otherwise has a different shape. Current examples `{"Version":3}`, `{"Version":3,"Groups":null}` and `{"Version":3,"Groups":{}}` incorrectly become corrupt while restore correctly treats them as unsupported. Use `CurrentVersion + 1` in maintained fixtures rather than hardcoding 3.

Add failing pure-classifier tests first, then repair classification precedence. Keep available metadata best effort, supported-schema structural corruption checks intact, and unknown future bytes untouched. Do not change this contract or mark the future document corrupt just to satisfy the old test. No schema bump or native recovery change is needed.

## 2. Strengthen A04's identical-snapshot retry proof

Use `fix-persistence-latest-request-wins`. Its `LatestSnapshot_RetriesAfterFailedFinalWrite` currently constructs a new Group on each attempt, producing different serialized IDs. Reuse one retry object/snapshot or fix its ID, demonstrate identical intended JSON across failure/retry, assert previous complete primary/backup preservation, and prove that the retry actually installs after the fault is removed. A prematurely advanced saved-content marker must make this regression fail. The current production exact-object retry passed review, so do not rewrite the writer without new failing evidence.

## 3. Lock down the unexpected-load protection branch

Add a deterministic explicit-temp-path test that reaches the newly added outer Load catch, not merely the inner read-error classifier. For example, an injected reader returning null can provoke a parse argument failure. Assert load protection is latched and later sync, async, manager emergency and exit-style empty saves cannot replace evidence. Preserve primary/backup bytes as applicable. The branch currently passed an isolated probe but lacks maintained direct coverage.

## 4. Verify corrections using safe focused checks

Run `dotnet build TabDock.sln`, then the explicit-fixture persistence and pure diagnostic classification tests in Debug and Release. The review filter was:

```text
FullyQualifiedName~TabDock.UnitTests.Persistence|FullyQualifiedName~TabDock.UnitTests.DiagnosticEnvironmentClassificationTests
```

It passed 80/80 before corrections; new regression counts may differ. Record exact red/green results and commands. Review whether a claimed task needs reopening while its fix/proof is incomplete; do not silently leave contradicted claims checked. Keep A01 task 3.2 and A04 task 3.3 deferred, not completed or archived, until their actual gates pass.

## 5. Continue only the validation-safety prerequisites next

Stage `keep-recovery-discovery-read-only` (A10) and `isolate-validation-storage-roots` (A02) under their separate opsx-apply workflows, following their designs and unchecked tasks. Start with direct-directory fixtures and non-mutating bootstrap/root probes. Preserve the SID-scoped lease, fail-closed path/ownership proof, existing identity/journal authority and no-reparent model. APPDATA/LOCALAPPDATA environment overrides are not proof of Windows storage isolation.

Before both safety fixes are verified, do not run the full unit suite, `scripts/validate.ps1 -Ci -Publish`, real doctor/ZIP process fixtures against user storage, recovery smoke, normal UI soak, or unattended real-input scenarios. Afterwards run the genuine isolated process/CLI fixtures and full unit/Release gates, then close the deferred A01/A04 tasks only on real passing evidence. Never upgrade an environment/supervision block or synthetic resource PASS into physical/native qualification.

Stop after this correction and validation-safety stage with an accurate per-change status, validation evidence and remaining work. Other motion/diagnostics/icon proposals remain separate later changes unless explicitly selected; do not treat this prompt as approval to implement all nine.
