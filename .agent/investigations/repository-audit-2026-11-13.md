# Investigation: repository-wide audit outcome

**Date:** 2026-11-13 campaign (validation spans the following local morning)
**Status:** concluded

## Question

Which independently verifiable correctness, security, reliability, resource and tooling defects can be repaired without weakening TabDock's Shepherd architecture, HWND identity checks, journal durability or validation isolation?

## Findings

Eighteen verified findings were repaired across repeated source, counterexample and validation passes. The [audit plan](../plans/repository-audit-2026-11-13.md) records detailed root causes, rejected hypotheses and individual red/green logs.

| # | Verified defect and implemented repair | Significant verification |
|---|---|---|
| 1 | Concurrent trace writers could evict newer records. Sequence allocation and ring installation now share one lock. | Exact newest-tail concurrent stress failed before the fix; full suites pass. |
| 2 | Refreshed/disposed picker rows retained subscriptions and notified obsolete state. Explicit handler detachment and current-row guards added. | Retired-row tests reproduced five unexpected notifications, now none. |
| 3 | Duplicate JSON marker properties escaped structured storage refusal through lazy parsing. Materialization now occurs inside the malformed-marker boundary. | Required/unknown duplicate-property cases refuse without changing evidence. |
| 4 | JSON/JSONL sanitization leaked credential aliases such as `pwd`, `db_pwd` and `client-credentials`. Alias detection extended. | Planted nested secrets are removed; unrelated `compass` metadata survives. No real credential exposure observed. |
| 5 | WinEvent callbacks queued before Stop could replay after Start. Dispatch work now carries a monitor-lifetime generation. | Deterministic stopped/restarted-context counterexamples went red, then green. |
| 6 | Post-token release finalization could mutate a recycled same-process HWND. Surviving per-HWND nonce proof is now mandatory. | Four missing/foreign-nonce mutation-boundary cases fail closed. |
| 7 | Journal-finalization retries mistook their own removed capture token for recycle evidence. Exact-object release phase is retained until unregister. | Locked-journal visible/hidden retries succeed safely; absent process/nonce or foreign token performs no native mutation. |
| 8 | Locked OpenSpec dev dependencies included vulnerable Zod 4.4.3. Updated only Zod to 4.6.7; added a warm-install full-dev npm audit gate. | npm advisory GHSA-mrhx-c7xc-m3w5 removed; final audit zero vulnerabilities; AST-bound failure/success propagation test passes. OpenSpec remains 1.8.0. |
| 9 | Lifecycle's second queue hop lost member generation and applied an old gesture end to a replacement. Queue retains captured-object identity and detaches reentrant batches. | Real monitor → lifecycle → never-shown container regression preserves replacement gesture state. |
| 10 | Native minimum/scaling/epsilon and outer-chrome arithmetic overflowed. Wide exact arithmetic and positive saturation added. | Adversarial integer cases, 10,000-case BigInteger oracle and existing million-case partition sweep pass. |
| 11 | Emergency release discarded complete layout intent before persistence. Full ordered metadata/active intent is snapshotted before teardown, separately from native recovery evidence. | Ten shutdown/new/restored/pending/repeated-cleanup tests plus surrounding persistence/release coverage pass. Ordinary pop-out remains unchanged. |
| 12 | A VM collection event retained its closed container. The container's own listener is now detached separately from VM cleanup. | Forced-GC regression collects the actual closed, never-shown container while keeping the VM alive. |
| 13 | Live GetWindow walks could cycle indefinitely. Traversal is capped at 4,096 probes without per-layout sets. | Cyclic/unending seam cases terminate; stable 2,000-window chain still resolves. No workstation hang was observed. |
| 14 | Evidence paths admitted Win32 aliases/devices and junction escapes. PowerShell and driver policies reject unsafe names, existing reparse components and case-aliased duplicate child paths. | Actual session-owned junctions, alternate streams, trimmed names, superscript COM/LPT devices and duplicate-case counterexamples go red/green. |
| 15 | Worker DPI probes could repopulate an invalidated cache. Synchronized access and invalidation generations prevent obsolete publication. | Per-monitor and whole-cache invalidation race tests pass; native probe stays outside the lock. |
| 16 | Startup measurement ignored backup layout restoration and native journal rescue. Read-only preflight now checks both state files, refuses journals and mismatched storage overrides. | AST-extracted real preflight tested against disposable fixtures, preserving evidence; live startup benchmark deliberately not launched. |
| 17 | Child-manifest or artifact read errors threw out of aggregation; manifest hashing used a different read from parsing. Errors now yield FAIL_HARNESS and hash the parsed byte snapshot. | Exclusive-lock cases and complete parent/child hierarchy contracts pass; six new deterministic import cases added overall. |
| 18 | Merge copied evidence through destination junctions before checking containment. Every copy now requires an authorized destination root before directory creation or writing. | Real merge counterexample previously wrote outside; now refuses with redirected directory and candidate bytes unchanged. |

Principal changes are in `Services/`, `ViewModels/CapturePickerViewModel.cs`, `Views/ContainerWindow.xaml.cs`, `scripts/`, `tests/ValidationDriver/TabDock.ValidationDriver/`, their regression suites and canonical specifications. No unrelated dependencies or broad application rewrites were introduced.

## Approaches tried

- Established a clean, freshly fetched baseline before the audit, isolated work on `audit/repository-20261113`, and preserved the uncommitted patch throughout resumptions.
- Read actual storage/recovery/privacy, native capture/release/identity, event/concurrency/DPI, WPF/container/viewmodel, validation ownership/visual, script/dependency and CI/release trust-boundary source. Revisited critical paths with independent counterexamples rather than trusting existing tests or comments.
- Used deterministic native seams, actual never-shown WPF containers, forced GC, locked files, real filesystem junction fixtures, AST-extracted script execution, property/oracle tests, dependency audits and static analysis. No desktop input or production-profile mutation was used.
- Rejected unsupported hypotheses instead of rewriting code: cold journal cache is populated by real capture before admission; externally writable collection Replace/Reset behavior lacks a verified current production caller; application-lifetime subscriptions are not automatically shorter-lived leaks.
- Repowise was unavailable and the code graph stale. Active C# LSP probes remained inconclusive; actual builds/tests and analyzers supplied alternative evidence. PSScriptAnalyzer was acquired only into session artifacts when not preinstalled.

## Final verification

Evidence is retained outside Git at `_AgentArtifacts/TabDock/sessions/20261113-pi-repository-audit/`.

| Current-tree gate | Verified result | Evidence |
|---|---|---|
| `scripts/validate.ps1 -Configuration Release -Ci -Publish` | PASS; **1,102** tests, zero build warnings/errors, auxiliary builds, publish/version smoke, ABI/CLI/storage/privacy checks and synthetic resource gate | `completion-validation.log`, `completion-resources/` |
| `dotnet build TabDock.sln` and Debug unit suite | PASS; zero warnings/errors; **1,102** tests, no failures/skips | `completion-debug.log` |
| `scripts/qa-split.ps1 -Tier deterministic` | PASS; **211** self-tests, synthetic topology and run manifest | `completion-deterministic.log`, `completion-deterministic/` |
| Release-tooling suite | **184/184** PASS | `completion-release-tooling.log` |
| Validation-storage safety suite | **24/24** PASS | `completion-storage-safety.log` |
| NuGet audited restores and full-dev npm audit | No reported vulnerabilities against configured sources | `completion-validation.log` |
| Read-only C# analyzers, app/unit and separate driver projects | Exit 0; both reports empty | `completion-analyzers-{app,driver}.log` and directories |
| PowerShell/workflow parsers and generated-config drift | 20 scripts, 4 workflows parse; 142 generated files have zero differences | `completion-{powershell-parse,workflow-parse,config-drift}.log` |
| PSScriptAnalyzer 1.24.0 | Zero errors; **261 warnings**, reviewed rather than suppressed | `completion-powershell-analysis.json`, delta report |
| Diff/secret-pattern checks | No whitespace errors; limited high-confidence credential-pattern scan has no findings | `completion-hygiene.log` |

The unit suite increased by **65 cases**, from 1,037 to 1,102. Compiler success is not presented as proof of native runtime correctness. Negative release-signature tests use dummy artifacts/mock signing; production signing was not exercised.

### Efficiency evidence

The fixes remove obsolete picker notifications/retention, bound pathological native work, prevent stale DPI cache publication and eliminate the duplicate manifest file read. These are demonstrated behavioral or structural improvements, not measured end-user latency claims. Sequential trace microbenchmarks retained identical allocation counts and had mixed timing directions (baseline/current medians approximately 1.46/1.22 ms per 10k no-data records, 2.52/2.52 ms per 10k with data, 27.47/32.28 ms per 100k). Samples were noisy: **no speedup or regression conclusion is justified**. Earlier concurrent measurements are not causal comparisons.

## Conclusion

The local audit/remediation is complete: all verified actionable findings from the repeated passes were repaired and final current-tree gates pass. No further verified actionable defect remained after the final source/diff/counterexample review. This is not a claim that the repository contains no undiscovered defects.

Remaining qualification risks and limits:

- Supervised native capture/release/activation/browser workflows, physical mixed-DPI and Windows 10/11 cross-machine testing, long physical resource soaks and production signing remain unqualified. Synthetic/headless evidence does not satisfy those release gates.
- Existing-reparse checks are defensive preflight checks, not race-free OS confinement against a concurrently malicious filesystem actor. Returned bundles are still untrusted data and must not be executed.
- PSScriptAnalyzer warnings are largely CLI-output/naming/ShouldProcess style advisories. Read warnings were triaged; three additions versus baseline are intentional missing-file catches and an AST-step test variable consumed dynamically. Inconclusive LSP output is not clean-file evidence.
- Dependency and limited secret-pattern scans are time/source/pattern bounded, not comprehensive security certification.

The audit concluded with changes intentionally uncommitted on `audit/repository-20261113`. The user subsequently authorized committing the audited patch and pushing directly to `main` without force. Historical base commit is `a85280fe46a09da717da462db8c07c9d55095a1d`; the local audit artifacts carry that base metadata plus the then-uncommitted working-tree changes, not a clean release-candidate identity. Resolve current publication state from Git and the exact-SHA hosted build; final SHA/CI results are reported in session output. The session-owned benchmark worktree was removed with Git; the pre-existing `.kilo` worktree was untouched.

## References

- [Audit plan and detailed evidence](../plans/repository-audit-2026-11-13.md)
- [Testing and qualification boundaries](../../docs/TESTING.md)
- [Windows file naming rules](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)
- [Windows junction semantics](https://learn.microsoft.com/en-us/windows/win32/fileio/hard-links-and-junctions)
