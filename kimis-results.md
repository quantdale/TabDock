# Codebase Deep Audit — TabDock

> Read-only whole-codebase audit of the TabDock repository (`D:/Documents/tryPython/TabDock`).
> Deliverable requested as `kimi-results.md` (the audit spec's `CODEBASE_AUDIT.md` filename is
> overridden by the operator's explicit instruction).
>
> **Operating constraint honored:** the repository was treated as strictly read-only. No source,
> test, documentation, configuration, or Git state was modified, created, deleted, or moved. The
> only file written by this audit is `kimi-results.md`. No remediation was performed.

---

## 1. Executive Summary

TabDock is a C#/.NET 8 WPF utility that merges independent top-level windows into a tabbed container
under the **Shepherd** model: captured guests are positioned, z-ordered, shown, and hidden over the
container's content area via Win32 (`SetWindowPos`/`ShowWindow`/`SetForegroundWindow`) but are **never
reparented or restyled**. The codebase is unusually disciplined for a native-interop desktop app: it
has a real crash-recovery journal, a two-tier window-identity gate, a fail-closed product-mutation
lease, deterministic geometry self-tests, and a genuinely well-engineered two-stage release pipeline.

**Overall assessment:** The implementation is mature and the core safety premises are sound. However,
the audit found a pattern of **"test-only" or "parallel" logic that is not what actually runs in
production**, plus a small number of correctness and privacy gaps that are masked by the very
self-tests meant to catch them. None of the findings rise to *Critical* (no demonstrated data loss,
security compromise, or unrecoverable corruption), but several are *High* because they undermine the
assurances the project markets most (deterministic split behavior, crash recovery, signed release
integrity) or because they contradict the project's own documented identity model.

**Major strengths:** two-tier window-identity gate applied at every native-mutation site (no ungated
guest mutation found); fail-closed mutex DACL; deterministic, fuzz-covered geometry; inert-by-default
telemetry; non-blocking, thread-safe logging ring; high-quality unit tests; thorough release
engineering.

**Major weaknesses:** (1) the heavily unit-tested pure split state machine is *not* the production
authority, so a whole class of transition regressions would ship green; (2) the most safety-critical
crash-recovery and single-file-publish behaviors are gated only by supervised/manual harnesses, not
by merge-blocking CI; (3) window titles (frequent PII) are written verbatim to the on-disk log while
only the *export* is sanitized; (4) capture admission duplicates and diverges from the central
identity gate, fatally vetoing identity-stable captures on ordinary title changes.

**Highest-risk areas:** `SplitPresentationController` vs `SplitPresentationPolicy` divergence;
`WindowShepherdService.Capture` admission; persistence/journal load-ordering coupling; CI/publish
gates; on-disk log privacy.

**Findings by severity:** Critical: 0 · High: 5 · Medium: 29 · Low: 31 · Info/Opportunity: 3
(plus 5 consolidated positive confirmations). Total distinct findings: ~68.

**Production-readiness:** Conditionally ready. Safe to ship today for its documented use, but the
High-severity items should be closed before relying on the split behavior, release pipeline, or
crash-recovery guarantees in adversarial conditions.

**Overall confidence in the audit:** High for the subsystems read directly by the sub-agents (the
entire first-party `Services/`, `Views/`, `ViewModels/`, `Models/`, `Converters/`, `Infrastructure/`
trees, plus tests/build/CI/docs). Medium for cross-runtime behavior that could only be confirmed by
exercise (real-native rescue, single-file bundle launch, full DPI matrix), which was not executed in
this read-only, headless environment.

---

## 2. Audit Scope

**Areas inspected (first-party source):** the entire `TabDock.csproj` application — `Models/`,
`ViewModels/`, `Views/`, `Services/`, `Converters/`, `Infrastructure/`, `NativeMethods.cs`,
`App.xaml.cs` — plus the test, spike, script, CI, documentation, and specification trees.

**Languages/frameworks:** C# 12, .NET 8 (`net8.0-windows`), WPF, P/Invoke (Win32 user32/dwmapi/
shell/advapi32), `System.Text.Json` with source-generated context, `System.Threading.AccessControl`.

**Validation commands executed:** `dotnet --version`; `dotnet build TabDock.sln -c Debug` (see
§4). Safe, non-destructive. No tests were executed by the audit harness itself (the unit-test runner
and the real-input ValidationDriver require the Windows/WPF runtime and interactive desktop); their
*coverage* was audited statically.

**Areas excluded and why:**
- `bin/`, `obj/`, `.audit_tmp/` (if any) — build output, generated, not first-party source.
- `Spike/TabDock.Spike/` — explicitly experimental; read at the dependency/contract level only.
- Vendored NuGet package internals (`System.Threading.AccessControl`) — third-party, stable,
  treated as trusted ABI.
- Full `release-tooling-tests.ps1` (2356 lines) and the full `openspec/` spec suite (20+ specs) —
  sampled, not line-read in full (see §22).

**Audit method:** 7 parallel read-only sub-agents, each owning a non-overlapping subsystem, each
applying the full lens set (correctness, concurrency, failure-mode, error-handling, resource-lifecyle,
security/privacy, performance, architecture, test-quality, docs-divergence, dead-code). Findings were
then de-duplicated, re-numbered, and consolidated by root cause. Several systemic issues were
deliberately cross-referenced rather than reported repeatedly.

---

## 3. System Architecture Model

*(Condensed from `docs/ARCHITECTURE.md` and direct verification; the audit did not find contradictions
in this model except where noted in the findings below.)*

**Components & responsibilities**
- `App` (orchestrator): lease → cleanup stale temp → construct services → attach WinEvent policy →
  restore state → rescue orphaned windows → open containers → register hotkey. All shutdown paths
  (`Exit`, `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `SessionEnding`) run
  `EmergencyReleaseAll → SaveState → FlushJournal → dispose` idempotently.
- `WindowShepherdService`: the **only** native write authority. Capture/release/position/z-order/
  show/hide, two-tier identity gate, DWM transition suppression, and the crash-recovery journal.
- `GuestLifecycleService` + `WinEventMonitor`: event → handler mapping. Hooks dispatched via
  `SynchronizationContext.Post` (never `Send`); O(1) HWND→member resolution; hooks gated on
  `IsMonitoringNeeded`; install is a bounded 3-attempt transaction; capture is disabled while hooks
  are unhealthy.
- `GroupManager`: membership index (`O(1)` via `CollectionChanged`), active-tab/split coordination,
  release/emergency-release primitives.
- `SplitPresentationController` / `SplitInteractionPolicy` / `SplitGeometry` /
  `PresentationLayoutCoordinator`: split-screen (exactly two guests, LEFT/RIGHT, runtime-only) state,
  hit-testing, deterministic partition, and coalesced single Render-priority relayout.
- `PersistenceService` + `PendingRecoveryService`: `state.json` (layout intent, schema v2) and
  `hidden-windows.json` (crash-recovery journal, schema v3), both atomic/durable.
- `ProductMutationLease`: `Global\TabDock-<userSID>` mutex with a protected DACL; fail-closed.
- Diagnostics (`DiagnosticReportService`, `DiagnosticTrace`, `LoggingService`, `RuntimeTelemetry`):
  read-only supportability boundary; reports/zip redact user-profile paths, omit raw titles, sanitize
  the log tail; nothing auto-uploaded.

**Data flow:** input (hotkey / picker) → capture admission (identity/elevation/DPI) → durable journal
write → native mutation → lifecycle tracking (WinEvents) → tab-strip projection → persistence on
teardown. Data leaves only via the crash journal, `state.json`, the rotating log, and the
user-initiated diagnostics export.

**State ownership:** guest state is owned by `CapturedWindow` (identity, not index); group membership
by `Group.Members`; split relationship by `SplitPresentationController` (reference identity);
persisted layout intent by `PersistenceService`; recovery evidence by the journal.

**Concurrency model:** UI-thread-serialized mutations; WinEvents `Post`-ed to the dispatcher; async
icon extraction on a worker; lock-protected diagnostic ring/trace; per-instance persistence write
gate. Native mutations are gated by the two-tier identity check (cheap token-only on the hot path,
strong exe+process-start on destructive paths).

**Major invariants the audit challenged:**
1. *Journal-before-dangerous-mutation* — confirmed honored in the journal write path.
2. *Two-tier identity (Match/Mismatch/Unverifiable) gates every native mutation* — confirmed at every
   guest-mutation site; **one exception**: `Capture()` admission is hand-rolled and diverges (AUDIT-002/008).
3. *Local pairing (container below guest)* — confirmed by upward `GW_HWNDPREV` walk.
4. *Atomic relayout* — confirmed via `PresentationLayoutCoordinator` generation gating **on the settle
   path only**; direct layout callers are not generation-gated (AUDIT-015).
5. *Fail-closed lease / identity* — confirmed sound (AUDIT-051-053 note only latent/test gaps).

---

## 4. Validation Results

```text
Command : dotnet --version
Result  : 8.0.424
```

```text
Command : dotnet build TabDock.sln -c Debug
Result  : Build succeeded. 0 Warning(s). 0 Error(s). Time Elapsed 00:00:02.95
Relevant observations :
  - Restored and built three projects only: TabDock.Spike, TabDock (main app),
    TabDock.UnitTests. The ValidationDriver and Performance projects are NOT in
    TabDock.sln, so they were not compiled by this command — confirming AUDIT-032
    (harness structurally outside the primary solution gate).
  - The main project built as SelfContained win-x64 even in Debug (win-x64
    self-contained output), consistent with AUDIT-063 (unconditional RID/SelfContained).
  - 0 warnings / 0 errors: no compiler-disabled diagnostics, no suppressed lints
    hiding defects at the build level.
```

```text
Command : dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug
Result  : Passed! Failed: 0, Passed: 146, Skipped: 0, Total: 146, Duration: 990 ms
Relevant observations :
  - The 146 passing tests are meaningful (partition math, policy transitions,
    generation monotonicity, single-writer concurrency, budget counting) — NOT
    tautological. This is consistent with the META-positive finding.
  - However, they do NOT exercise the gaps this audit identified: the production
    split controller transitions (AUDIT-003), GroupViewModel.DisplayTabs
    projection (AUDIT-020), the persistence/journal corruption+rescue invariants
    (which live only in CLI self-tests, AUDIT-001), or real-native crash rescue
    (AUDIT-031). So a green run does not cover the highest-risk findings — itself
    a finding (AUDIT-001/003/020/031).
  - As with the build, only the UnitTests project ran; ValidationDriver/Performance
    were not executed (AUDIT-032).
```

> The build is non-destructive and modifies only `obj/`/`bin/` (build output, excluded from audit scope
> and from `git status` of tracked source).

---

## 5. Critical Findings

**None.** No defect was found that demonstrably causes catastrophic data loss, a security compromise,
unrecoverable corruption, or a violation of a core system guarantee under realistic conditions. The
two closest candidates — crash-recovery "un-docking" on hard kill (AUDIT-006) and cross-process torn
writes (AUDIT-027) — are bounded and recoverable, hence rated Medium/High rather than Critical.

---

## 6. High-Severity Findings

### [AUDIT-001] Critical persistence invariants are proven only by out-of-band CLI self-tests, not the xUnit runner
**Severity:** High · **Confidence:** High · **Category:** Test-Gap
**Affected areas:** `Services/DiagnosticCommandLine.cs:121,288-308`; `Services/PersistenceSelfTest.cs`; `Services/RecoveryJournalSelfTest.cs`; `tests/UnitTests/*` (PersistenceTests, PersistenceSingleWriterTests, HardeningRegressionTests)
**Summary:** The corruption→backup→quarantine, both-corrupt-keeps-evidence, future-version-preservation,
and rescue-identity-safety guarantees live in `PersistenceSelfTest`/`RecoveryJournalSelfTest`, which run
**only** through the `--selftest` CLI path. The xUnit project contains only DTO round-trips and the
single-writer hammer; it never covers `Load`'s corrupt→backup→quarantine logic, future-version
preservation, or `RescueOrphanedWindows` identity safety.
**Evidence:** `PersistenceSelfTest.Run`/`RecoveryJournalSelfTest.Run` are referenced solely from
`DiagnosticCommandLine.cs`; the in-scope unit tests never call them.
**Failure scenario:** A regression in `CommitJson`/`Load`/`QuarantineCorruptStateFile`/rescue can pass
`dotnet test` entirely; only an explicit `TabDock.exe --selftest` (not wired into normal CI test
execution) would catch it.
**Impact:** The most safety-critical guarantees of the subsystem are outside the automated unit-test net.
**Root cause:** Self-tests were built as in-process diagnostics rather than xUnit cases; the two suites diverged.
**Recommended direction:** Promote (or wrap) `PersistenceSelfTest`/`RecoveryJournalSelfTest` into
`tests/UnitTests` and run them under `dotnet test`. Do **not** implement.
**Verification recommendation:** Temporarily break `CommitJson`/`Load`; confirm `dotnet test` currently passes (proving the gap), then that the promoted test fails.

### [AUDIT-002] Capture admission fatally vetoes on window-title mutation, contradicting the project's own two-tier identity model
**Severity:** High · **Confidence:** High · **Category:** Correctness
**Affected areas:** `Services/WindowShepherdService.cs:432-433,591-601` (title-equality veto); doc comment `:769-772`; `Services/WindowIdentityGate.cs:167-247` (title deliberately excluded)
**Summary:** `Capture()` snapshots `initialTitle` and re-reads `finalTitle`, then **refuses capture** with
"The window identity changed while it was being captured" if they differ by `StringComparison.Ordinal`.
Yet the class's own doc comment states the title is *deliberately not part of stable identity*, and
`WindowIdentityGate` omits title from both tiers. The capture handshake runs elevation/DPI/exe/
process-start probes over several milliseconds, during which any title-updating guest (browsers, editors
with unsaved-marker, terminals, media players) will fail capture.
**Evidence:** `:433/591-601` vs `:769-772` ("title is deliberately not part of the stable identity") vs
`WindowIdentityGate.Evaluate` (only HWND/PID/thread/class/[exe]/[start]/token).
**Failure scenario:** User picks a Chrome tab or a Notepad-with-unsaved-changes window; during the probes
the title changes; `Capture` returns null and the window is never docked.
**Impact:** Valid, identity-stable captures are refused — fail-closed on an axis the design says should be permissive. Degrades core functionality for the most common window types.
**Root cause:** Capture-path identity checks were hand-rolled inside `Capture()` instead of being routed through `WindowIdentityGate`, and a title-equality veto was added without reconciling it with the documented model. (See AUDIT-008.)
**Recommended direction:** Drop the title-equality fatal gate; rely on `WindowIdentityGate` tiers. Record `finalTitle` on success if needed. Do **not** implement.
**Verification recommendation:** Deterministic test: `GetWindowTextString` returns two strings across the probes but PID/thread/class/exe/start/token stable → assert capture succeeds.

### [AUDIT-003] The heavily unit-tested pure split state machine is NOT the production authority; the controller re-implements a diverging transition set
**Severity:** High · **Confidence:** High · **Category:** Test-Gap / Architecture / Docs-Divergence
**Affected areas:** `Models/SplitPresentationPolicy.cs:62-153` vs `Services/SplitPresentationController.cs:58-183`; only `tests/UnitTests/SplitPresentationPolicyTests.cs` exists
**Summary:** `SplitPresentationPolicy` is a pure, extensively unit-tested state machine, but production
never calls it. `SplitPresentationController` carries *parallel* transition logic with its own
generation bumps and native hides, and the two **diverge in survivor semantics**: the policy asserts
that removing a member while *dormant* keeps the non-member guest as survivor
(`RemoveMember_WhileDormant_KeepsGuestSurvivor` asserts `ActiveGuest == "C"`), but the controller
*always* returns the other member and promotes it. So the behavior validated by the suite is not what runs.
**Evidence:** `SplitPresentationController.cs:175-183` returns `survivor = ReferenceEquals(removed,_left)?_right:_left`; no reference to the policy. `SplitPresentationPolicyTests.cs:321-333` asserts the dormant non-member survives. `ContainerWindow.xaml.cs:3096-3132` uses the controller's survivor.
**Failure scenario:** Dormant pair A|B with non-member C active; user removes A. Tests say C stays active; production makes B active and hides C.
**Impact:** The suite gives **false assurance** — a regression in the real transition logic would not be caught. Divergence can silently ship.
**Root cause:** The policy was built as a "qualification contract" separate from the runtime controller; the controller was not refactored to delegate to it.
**Recommended direction:** Make `SplitPresentationController` delegate state decisions to `SplitPresentationPolicy` (applying hides around the returned desired state), or generate the controller from the policy. Add controller-level transition tests. Do **not** implement.
**Verification recommendation:** Test driving `SplitPresentationController` through RemoveMember-while-dormant and asserting the same survivor the policy predicts; flag any divergence in CI.

### [AUDIT-004] CI validates the single-file published artifact only with `--version`; WPF/single-file runtime is never exercised in the merge gate
**Severity:** High · **Confidence:** High · **Category:** Test-Gap / Build
**Affected areas:** `scripts/validate.ps1:37,238-241,326`; `.github/workflows/build.yml:60`
**Summary:** The automated build gate (`build.yml` → `validate.ps1 -Ci -Publish`) runs the geometry/
diagnostics/native-ABI self-tests against the *ordinary* (non-bundled) build output, then publishes the
single-file bundle and exercises **only `--version`** on it. The shipping artifact's WPF runtime
(AppHost extraction, single-file assembly loading, XAML/`InitializeComponent`/`MainWindow`) is never
driven beyond a version print in the per-PR gate (release qualification does more, but is a manual dispatch).
**Evidence:** `validate.ps1:37` self-tests run on `bin\Release\net8.0-windows\win-x64\TabDock.exe`; `:309-327` publish step runs only `--version` on `$publishedExe`.
**Failure scenario:** A merge that breaks WPF startup under single-file bundling passes `build.yml` green and is caught only when a human later dispatches `prepare-release-candidate.yml` — which can sit merged indefinitely.
**Impact:** The most likely "builds but won't launch" regression for a WPF self-contained single-file app is not blocked by the automated merge gate.
**Root cause:** `validate.ps1` self-tests target the non-bundled build; the publish smoke is intentionally minimal.
**Recommended direction:** After publish, run `--selftest-geometry`/`--selftest-diagnostics`/`--selftest-native-abi` (and a guarded `--doctor`) against the published single-file `$publishedExe`, not just `--version`. Do **not** implement.
**Verification recommendation:** Add the self-test invocations against `$publishedExe` in `validate.ps1`; confirm `build.yml` turns red when a deliberately broken WPF startup is introduced.

### [AUDIT-005] Release trust boundary (`workflow_sha == github.sha`) is untested and rests on an unverified GitHub-context assumption
**Severity:** High · **Confidence:** Medium · **Category:** CI/CD / Architecture
**Affected areas:** `.github/workflows/prepare-release-candidate.yml:97-108`; `publish-release.yml:119-131,472-484`
**Summary:** The two-stage production release's central trust boundary — "policy code and candidate
source start from the SAME trusted revision" — is enforced by asserting `github.workflow_sha ==
github.sha` (and `github.ref == refs/heads/main`) for `workflow_dispatch`. This equality holds only if
the dispatched ref's HEAD is the exact commit that last modified the workflow file, and the check is
**never exercised by any automated test** (it lives inline in YAML, not in `release-tooling-tests.ps1`).
**Evidence:** `prepare-release-candidate.yml:107` `if ($env:WORKFLOW_SHA -ne $env:DISPATCH_SHA) { throw "BLOCKED: ..." }`; identical at `publish-release.yml:128` and `:482`. The comment at `:89/:110` states the assumption as fact.
**Failure scenario (two directions):** If `github.workflow_sha` for `workflow_dispatch` instead reflects
the workflow file's own last-modified commit (which differs from branch HEAD whenever a non-workflow
commit is latest on main), the production gate throws `BLOCKED` on every normal dispatch — making the
production release path unusable. Conversely, if the equality is satisfied in a way that does not
actually guarantee policy/source co-revision, the boundary provides weaker protection than claimed.
**Impact:** The security model of the entire two-stage release hinges on this one comparison; a wrong
assumption either blocks all production releases or weakens the guarantee.
**Root cause:** Trust-boundary enforcement placed in YAML and validated only by code review, not by the hermetic test suite.
**Recommended direction:** Empirically confirm `github.workflow_sha` vs `github.sha` for a real
`workflow_dispatch`; if not reliably equal, replace the heuristic with an explicit revision pin or a
checkout-derived policy-version check, and add a regression test in `release-tooling-tests.ps1`. Do **not** implement.
**Verification recommendation:** Dispatch `prepare-release-candidate.yml` on a main whose latest commit did NOT touch the workflow file; observe pass/fail and record actual `workflow_sha`/`sha` from the run log.

---

## 7. Medium-Severity Findings

### [AUDIT-006] Crash rescue restores pre-capture geometry; a force-kill un-docks a perfectly docked window
**Severity:** Medium · **Confidence:** High · **Category:** Correctness / Reliability
**Affected areas:** `Services/WindowShepherdService.cs:551-552,1849-1887,2609-2627` (journal entry built once from `OriginalPlacement`; `RestoreJournaledPresentation` replays it); comment at `:933-935` is misleading
**Summary:** The capture-session journal entry is built once from pre-capture `OriginalPlacement`/`OriginalBounds`
and is never refreshed on reposition/hide/show. `RescueOrphanedWindows` therefore always re-applies
*pre-capture* placement. A force-kill of TabDock teleports every captured guest — even a perfectly docked,
visible, active tab — back to where it sat before it was ever docked.
**Evidence:** `JournalCapture`/`JournalHide` call `UpsertJournalEntry(window,…)` = unchanging `OriginalPlacement` (`:1914-1940`); `RestoreJournaledPresentation` (`:2609-2627`) re-applies it.
**Failure scenario:** TabDock force-killed while a guest is docked and visible → next launch rescue re-shows it via `SetWindowPlacement(OriginalPlacement)`, un-docking it.
**Impact:** Crash recovery silently un-docks windows to stale geometry instead of preserving the user's last docked layout; undermines the recovery journal's purpose and contradicts the documented invariant.
**Root cause:** The journal is a "capture-session" record anchored to pre-capture placement, never updated on reposition.
**Recommended direction:** Refresh the journal entry's placement/bounds on each successful `PositionAndShow`/`PositionGuest`, or clear/neutralize it when normally showing an already-journaled entry. Realign the `:933-935` comment. Do **not** implement.
**Verification recommendation:** Integration test: capture+position a window, force-kill, relaunch, assert it returns to its *docked* rect, not `OriginalPlacement`.

### [AUDIT-007] Two parallel HWND→member maps desync during the capture gap; a DWM-suppressed stray window is left unhandled
**Severity:** Medium · **Confidence:** Medium · **Category:** Concurrency / Resource-Lifecycle
**Affected areas:** `Services/WindowShepherdService.cs:198,649,705-708`; `Services/GroupManager.cs:40,266,299`; `Views/ContainerWindow.xaml.cs:1959-1981`
**Summary:** `WindowShepherdService._capturedByHwnd` is bound at `Capture()` time (before journal/token), while
`GroupManager._capturedIndex` is populated only when the caller later adds the member to `Group.Members`
(after `Capture` returns). Between those two steps the guest is already token-tagged and DWM-suppressed
but is **not** in the lifecycle index, so `GuestLifecycleService`'s destroy/hide handlers won't find it
to revert token/DWM state or remove its tab.
**Evidence:** `_capturedByHwnd.Bind` at `:649` precedes `JournalCapture` (`:664`) and token+DWM (`:676`); `_capturedIndex` created only on `group.Members.Add` in `AddCapturedWindow` after return (`:1981`).
**Failure scenario:** Capture returns successfully, DWM suppression applied, but before `AddCapturedWindow` the guest crashes/closes → no tab to remove; token + `DWMWA_TRANSITIONS_FORCEDISABLED` linger on a now-foreign/destroyed HWND.
**Impact:** Narrow (single-turn) leak of reversible native state and a possible stray journal entry; bounded but unhandled by the lifecycle layer.
**Root cause:** Implicit cross-service ordering between `WindowShepherdService` binding and `GroupManager` index population, with no `CollectionChanged` linkage between the two maps.
**Recommended direction:** Have `Capture` return a "pending" state promoted only once the member is added to a group, or make the lifecycle index the single source of truth. Ensure any capture failing the post-`Capture` recheck calls `Release` to revert token/DWM before abandoning. Do **not** implement.
**Verification recommendation:** Unit test simulating destroy of the HWND between `Capture` returning and `AddCapturedWindow`; assert token/DWM state is reverted.

### [AUDIT-008] Capture admission is hand-rolled and duplicates/diverges from `WindowIdentityGate`
**Severity:** Medium · **Confidence:** High · **Category:** Architecture / Test-Gap
**Affected areas:** `Services/WindowShepherdService.cs:398-703` vs `Services/WindowIdentityGate.cs:167-247,255-328`
**Summary:** `Capture()` re-implements identity checks (IsWindow, PID, elevation, DPI, exe, process-start,
`_capturedByHwnd`, `IsCaptureTokenAvailable`) inline and does **not** call `WindowIdentityGate`. This
duplication is the direct cause of AUDIT-002: the two paths have already diverged (capture adds a
title veto the gate explicitly excludes). Any future gate change must be mirrored by hand or they drift further.
**Evidence:** `Capture` `:419-630` reimplements what `WindowIdentityGate.Evaluate`/`EvaluateBeforeCaptureToken` already encapsulate, plus the title check at `:593-601`.
**Failure scenario:** A maintainer tightens/loosens the gate and forgets the parallel copy in `Capture`; capture admission and the mutation gate silently disagree about a safe identity.
**Impact:** Hidden inconsistency risk; the "two-tier" model is not actually centralized.
**Root cause:** Capture admission predates or was written separately from the `WindowIdentityGate` abstraction.
**Recommended direction:** Route `Capture`'s admission through `WindowIdentityGate` (with `verifyExecutable:true, verifyProcessInstance:true` and the token pre-install check), removing the inline duplication and the title veto. Do **not** implement.
**Verification recommendation:** Refactor `Capture` to call the gate; keep `CaptureBoundarySelfTest` green; add a test asserting title changes don't fail capture.

### [AUDIT-009] A stale/legacy/incomplete journal latches `_journalLoadFailed` and permanently disables all captures with a misleading error
**Severity:** Medium · **Confidence:** Medium · **Category:** Correctness / Failure-Handling
**Affected areas:** `Services/WindowShepherdService.cs:2025-2036` (`LoadJournal` sets `_journalLoadFailed` for legacy/incomplete v3), `:2372-2378` (`SaveJournal` throws if set), `:401` (`Capture` consults only `EnsureJournalStorage`)
**Summary:** If a legacy (v1/v2) or identity-incomplete v3 journal is on disk, `_journalLoadFailed` is set
true and **sticks for the process lifetime**; `SaveJournal` then throws, `JournalCapture` returns false,
and `Capture` aborts with "recovery journal commit failed." `Capture`'s admission checks only directory
*writability* (`EnsureJournalStorage`), not `_journalLoadFailed`, so the real cause (a quarantined legacy
journal) is hidden behind a misleading "commit failed" message.
**Evidence:** `LoadJournal` `:2025-2036` sets the flag; `SaveJournal` `:2374-2375` throws; `UpsertJournalEntry` `:1893` returns false; `Capture` `:401` checks only `EnsureJournalStorage`.
**Failure scenario:** A previous run left a legacy/incomplete `hidden-windows.json`; on next launch rescue
quarantines it to `.pending`, but if that quarantine didn't happen, `_journalLoadFailed` is set; every
subsequent `Capture` in that session fails at journal commit with a confusing message.
**Impact:** Rare, but produces a persistent "capture disabled" state for the whole session with a misleading reason; recovery requires manual deletion of the stale journal.
**Root cause:** `_journalLoadFailed` conflates "startup rescue should not overwrite" with "this session cannot journal at all," and `Capture` doesn't consult it.
**Recommended direction:** Consult `_journalLoadFailed` in `Capture`'s admission (or reset it after a
successful `PreservePendingJournal`), and surface a precise error. Do **not** implement.
**Verification recommendation:** Plant a stale v1 journal that fails quarantine, start, attempt capture, assert refusal with an accurate reason; clear the file, assert capture then succeeds.

### [AUDIT-010] Dead classifier/settle API; the view inlines a hardcoded classify, bypassing the controller wrapper
**Severity:** Medium · **Confidence:** High · **Category:** Dead-Code / Docs-Divergence
**Affected areas:** `Services/SplitPresentationController.cs:200-207` (`ClassifyInteraction`), `:188-193` (`ArmSettle`), `:21,86,192` (`_settleGeneration`); `Views/ContainerWindow.SplitInteractionFix.cs:92-101` (inlined `SplitInteractionPolicy.Classify` with `nativeOutcome: Succeeded`, `isTargetSplitMember: false`); `SplitInteractionPolicy.cs:113` (unreachable final `return ResumeMember`)
**Summary:** `SplitPresentationController.ClassifyInteraction` is never called; the view calls
`SplitInteractionPolicy.Classify` directly with `nativeOutcome` and `isTargetSplitMember` hardcoded.
`ArmSettle` and `_settleGeneration` are set but never read in production. The documented "controller
wraps SplitInteractionPolicy" is false at the call site. The final `return ResumeMember` in the policy
is unreachable (the two preceding branches are exhaustive).
**Evidence:** Repo-wide grep: `ClassifyInteraction`/`ArmSettle` only at definitions; `SettleGeneration` only set and consumed by one unit test. `ContainerWindow.SplitInteractionFix.cs:94-101` bypasses the wrapper.
**Failure scenario:** A future maintainer "fixes" classification by updating `ClassifyInteraction` and the change has no effect because nothing calls it.
**Impact:** Misleading architecture; duplicated decision entry points that can drift.
**Root cause:** Wrapper added for symmetry but the view evolved to call the policy directly.
**Recommended direction:** Wire the view to `_splitController.ClassifyInteraction(...)` (passing the real
`nativeOutcome`/`isTargetSplitMember`), or delete `ClassifyInteraction`/`ArmSettle`/`SettleGeneration` and the unreachable branch. Do **not** implement.
**Verification recommendation:** Static check for uncalled private members; or a test asserting the view's classify result equals `controller.ClassifyInteraction(...)`.

### [AUDIT-011] A second, divergent `SuspendSplitPairForGuest` implementation is dead code
**Severity:** Medium · **Confidence:** High · **Category:** Dead-Code / Architecture
**Affected areas:** `Views/ContainerWindow.xaml.cs:2849-2915` (`SuspendSplitPairForGuest`) vs `ContainerWindow.SplitInteractionFix.cs:149-206` (`SuspendPresentedPairForUserSelection`, the live path)
**Summary:** Two parallel pair→single-guest suspension routines exist. `SuspendSplitPairForGuest` is never
invoked; the comment in `SuspendPresentedPairForUserSelection` describes it as a "variant" but it is the
only one actually used. The dead version does not manage settle disarm/constraint-refresh/foreground as carefully.
**Evidence:** Grep for `SuspendSplitPairForGuest` returns only the definition at `:2849`; all live flows go through `SuspendPresentedPairForUserSelection`.
**Failure scenario:** A future caller wires up `SuspendSplitPairForGuest` expecting canonical behavior and gets an inconsistent (likely wedged) z-order/settle state.
**Impact:** Maintenance hazard; duplicated, divergent logic.
**Root cause:** Leftover from a refactor that split the suspend path into the `.SplitInteractionFix` partial.
**Recommended direction:** Delete `SuspendSplitPairForGuest` (or consolidate the two into one clearly-named method). Do **not** implement.
**Verification recommendation:** Grep/compiler check that no private method is uncalled.

### [AUDIT-012] Split settle re-arms and re-foregrounds the current member on every `DisplayTabs` mutation while presented
**Severity:** Medium · **Confidence:** Medium · **Category:** Performance / Correctness (churn)
**Affected areas:** `Views/ContainerWindow.SplitInteractionFix.cs:228-242` (`SplitDisplayTabs_CollectionChanged`), `:244-308` (`SplitPresentationSettle_Rendering`); `ViewModels/GroupViewModel.cs:189-208` (`RebuildDisplayTabs` fires `CollectionChanged` on Add/Remove/Move while a composite exists)
**Summary:** `SplitDisplayTabs_CollectionChanged` arms a `CompositionTarget.Rendering` settle (which calls
`LayoutSplitPanes()` + `SetForeground(foreground)`) whenever `DisplayTabs` changes *while split is
presented*. But `RebuildDisplayTabs` fully rebuilds `DisplayTabs` on **any** `Tabs` mutation while a
composite exists — including adding/removing/reordering a *third* (non-member) tab. Each such mutation
re-glues and re-foregrounds the current foreground member. Also, while `_splitPresentationSettlePending`
is true and chrome is active, the rendering handler early-returns without disarming/unsubscribing, so
the per-frame `+=` handler stays subscribed for the whole popup duration.
**Evidence:** `GroupViewModel.cs:212-217` calls `RebuildDisplayTabs()` whenever `_splitComposite != null`; `SplitDisplayTabs_CollectionChanged` only guards `if (_splitPresentationSettlePending) return;`. `SplitPresentationSettle_Rendering:287-288` returns while chrome active.
**Failure scenario:** While A|B split is presented and a C/D tab is added/removed/reordered, the foreground member is re-positioned and re-foregrounded on each edit — bounded but unnecessary native churn and a visible focus pulse.
**Impact:** Minor visual churn and extra native calls on ordinary tab edits during presentation.
**Root cause:** Settle arms on the coarse `CollectionChanged` signal rather than on a split-affecting projection change.
**Recommended direction:** Only arm the settle when the change actually affects split presentation (member added/removed, or composite installed/cleared), not on every third-tab edit; disarm-or-defer the rendering handler when chrome stays active. Do **not** implement.
**Verification recommendation:** Test injecting successive `Tabs` adds/removes while a split is presented; assert `SetForeground`/layout count is bounded (ideally 1, not N).

### [AUDIT-013] Controller `Foreground`/`ActiveGuest` points at a hidden member after a suspend
**Severity:** Medium · **Confidence:** High · **Category:** Correctness / Docs-Divergence
**Affected areas:** `Services/SplitPresentationController.cs:89-113` (`SuspendForGuest` sets `_presented=false` but leaves `_foreground` unchanged); `:50-56` (`ToState()` reports `ActiveGuest`); `ContainerWindow.SplitInteractionFix.cs:194` (view patches `_shepherdActiveWindow`)
**Summary:** After `SuspendForGuest` succeeds, the controller's `_foreground` still references the (now hidden)
former split member while `_presented` is false. The policy analogue `SelectNonMember` explicitly sets
`ActiveGuest = guest`. The view patches this by pre-setting `_shepherdActiveWindow = guest`, but
`controller.Foreground`/`ToState().ActiveGuest` continue to report the stale, hidden member.
**Evidence:** `SplitPresentationController.cs:109-112` — only `_presented=false; _generation++; DisarmSettle();` after hiding; `_foreground` untouched. `ToState():55` returns `_foreground?.Hwnd ?? _left?.Hwnd`.
**Failure scenario:** After suspending A|B for guest C, `controller.ToState().ActiveGuest` (used in
diagnostics/settle) names the hidden A/B member, not C.
**Impact:** Misleading diagnostics; latent bug if a future consumer trusts `Foreground` while dormant.
**Root cause:** Controller transition setters were not aligned with the policy's `ActiveGuest` convention.
**Recommended direction:** On `SuspendForGuest`, set `_foreground = null` (or to the guest) and reflect it in `ToState()`. Do **not** implement.
**Verification recommendation:** Unit test asserting `controller.Foreground == null` (or == guest) after `SuspendForGuest`.

### [AUDIT-014] The operation budget is inert in production — "single-pass" is test-only, not enforced
**Severity:** Medium · **Confidence:** High · **Category:** Architecture / Test-Gap
**Affected areas:** `Views/ContainerWindow.xaml.cs:305-307` (`new SplitPresentationController(ops:..., isCurrent:...)` — no `_budget`); `Services/PresentationLayoutCoordinator.cs:24` (no `_budget`); `Services/PresentationOperationBudget.cs:64-184` (counter is purely a test seam)
**Summary:** Both the controller and the layout coordinator are constructed *without* a budget sink in
production, so every `RecordLayoutSplit`/`RecordDeferBatch`/`RecordHide`/etc. is a null-guarded no-op.
All budget assertions live in `PresentationOperationBudgetTests.cs` against fake ops. The single-pass
discipline is enforced only by control flow, never by a runtime guard.
**Evidence:** `ContainerWindow.xaml.cs:305-307` passes `ops`/`isCurrent` but not `budget`; `LayoutCoordinator = new PresentationLayoutCoordinator();` passes nothing; `_budget?.Record...` degrades to no-op.
**Failure scenario:** A code change introduces a second `LayoutSplitPanes`/hide in a single input turn; production runs fine (no assert), only a kept-up unit test would notice the extra count.
**Impact:** The budget provides zero production safety; it is documentation/measurement only.
**Root cause:** Budget was designed as a diagnostic seam, not a guard.
**Recommended direction:** Make the budget a DEBUG-only runtime assert (throw/log on overflow), or document
it as test-only and give the controller/coordinator direct unit coverage on their transition counts. Do **not** implement.
**Verification recommendation:** A controller-level test (with a real budget sink) performing Enter→Suspend→Resume→Exit and asserting exact hide/show/defer counts.

### [AUDIT-015] Generation gating is applied only to the settle path, not to direct `LayoutSplitPanes` callers
**Severity:** Medium · **Confidence:** Medium · **Category:** Concurrency / Correctness
**Affected areas:** `Views/ContainerWindow.xaml.cs:2679-2777` (`LayoutSplitPanes`, guarded by `IsSplitPresented` + null-member checks only); `:2228-2260` (`FocusSplitMember`); `:1083-1136` (`StateChanged` → `RequestRelayout`); WinEvent reassert paths `:2472-2585`
**Summary:** The generation check fully answers "can a stale callback mutate current state?" for the
`CompositionTarget.Rendering` settle, but the *direct* layout entry points (`FocusSplitMember`,
`EnterSplit`, `ExitSplit`, `StateChanged`'s `RequestRelayout`, periodic WinEvent reasserts) call
`LayoutSplitPanes()` with no generation token. They are currently safe only because `LayoutSplitPanes`
early-returns when `!IsSplitPresented` or members are null — fragile if a transition leaves `_presented ==
true` with logically-stale members.
**Evidence:** `LayoutSplitPanes:2681` (`if (!IsSplitPresented) return;`), `:2685` (null-member guard), but no generation comparison; contrast `SplitPresentationSettle_Rendering:264-279` which compares generation twice.
**Failure scenario:** A suspend/exit/define transition returns mid-way (`RecoveryPending`) with `_presented` true but members hidden; a concurrent WinEvent reassert fires `LayoutSplitPanes`, re-showing the half-hidden pair.
**Impact:** Transient wrong z-order/visibility during concurrent transitions; bounded by defensive guards but not by generation.
**Root cause:** Generation is treated as a settle-only concept while layout callers trust `IsSplitPresented`.
**Recommended direction:** Pass/capture the generation at each `LayoutSplitPanes` entry and compare against
`_splitController.Generation` before mutating, or make `IsSplitPresented` the single always-correct gate. Do **not** implement.
**Verification recommendation:** Test bumping generation (simulating an intervening transition) then calling `LayoutSplitPanes`, asserting it no-ops despite `IsSplitPresented` still true.

### [AUDIT-016] `IconService.GetFileIcon` blocks the UI thread synchronously when a producer is in flight
**Severity:** Medium · **Confidence:** High · **Category:** Concurrency / Performance
**Affected areas:** `Services/IconService.cs:69-90` (`GetFileIcon` waiter does `waitFor!.Task.GetAwaiter().GetResult()`); callers `ViewModels/GroupViewModel.cs:143,288` (UI thread), `ViewModels/CapturePickerViewModel.cs:256` (worker thread)
**Summary:** Concurrent requests for the same exe use a producer/waiter pattern. The waiter path is a
**synchronous block**. The same exe's icon is also fetched on the **UI thread** (tab construction, inline
capture rows). If the UI thread requests the exe while the picker worker is the producer, the UI thread
blocks until the worker's native `ExtractIconEx` completes.
**Evidence:** `IconService.cs:89-90` blocks; `GroupViewModel.cs:143,288` call on UI thread; `CapturePickerViewModel.cs:231-236` spawns the producer worker.
**Failure scenario:** User captures a window whose exe icon is already queued by an in-flight picker icon worker → the calling UI operation stalls on `GetResult()` for the duration of extraction + marshaling.
**Impact:** Brief UI-thread stall/hitch; bounded by distinct exe count, but it is a blocking wait on the UI thread — exactly what the async picker worker was built to avoid.
**Root cause:** The wait path treats "another thread is producing" as "wait synchronously" rather than coalescing/routing to the dispatcher.
**Recommended direction:** Make the UI-thread caller non-blocking (return cached-or-null and let the worker
populate, or route UI requests through the same `TryGetCachedFileIcon` fast path). Do **not** implement.
**Verification recommendation:** Test calling `GetFileIcon` from a simulated UI thread while a worker holds the producer; assert the UI call does not block beyond a microtask.

### [AUDIT-017] `ViewModel_DeleteGroupRequested` drops the request when a close prompt is already open
**Severity:** Medium · **Confidence:** High · **Category:** Correctness / Concurrency
**Affected areas:** `Views/ContainerWindow.xaml.cs:341-345` (`ViewModel_DeleteGroupRequested`) vs `_closePromptOpen` deferral in `App.ShowCapturePicker` (`App.xaml.cs:789`) and `ViewModel_EmptiedByPopOut` (`:329-339`)
**Summary:** `ViewModel_DeleteGroupRequested` returns early with no action if `_closePromptOpen` is true.
Unlike `ViewModel_EmptiedByPopOut` (which defers via `_closePending`) and `App.ShowCapturePicker` (which
defers until no close prompt is open), the delete path **silently discards** the request.
**Evidence:** `ContainerWindow.xaml.cs:343` `if (_closePromptOpen) return;` with no deferral; contrast `:329-339` (`EmptiedByPopOut` sets `_closePending`) and `App.xaml.cs:789-793`.
**Failure scenario:** Container A's close prompt is open; a deferred/queued trigger fires `DeleteGroupRequested` for container B → returns early → no delete, no prompt, no user feedback.
**Impact:** User-initiated destructive action silently ignored; inconsistent with the two peer reentrancy paths that defer instead of drop.
**Root cause:** Asymmetric reentrancy handling — the close-prompt guard was added to the picker path and `EmptiedByPopOut` but not mirrored into the delete path.
**Recommended direction:** Mirror the deferral pattern (set `_closePending`/queue a `Close()` after prompt) or re-invoke after the prompt returns. Do **not** implement.
**Verification recommendation:** Test that a `DeleteGroupRequested` arriving while `_closePromptOpen` is honored after the prompt closes.

### [AUDIT-018] Diagnostic hotkey runs a (potentially slow) zip export synchronously on the UI thread
**Severity:** Medium · **Confidence:** Medium · **Category:** Performance / Failure-Handling
**Affected areas:** `App.xaml.cs:247-270` (`ExportDiagnosticsFromHotkey` → `DiagnosticReportService.ExportBundle(path)`); `Services/HotkeyService.cs:125-129` (sink on UI thread)
**Summary:** `Ctrl+Alt+Shift+D` calls `ExportBundle` (writes a `.zip` to the Desktop) plus several
`DiagnosticRuntime.Record` calls **synchronously on the dispatcher thread**, directly from the hotkey
`WndProcHook`. The UI is blocked for the duration of bundle assembly.
**Evidence:** `App.xaml.cs:255` `string output = DiagnosticReportService.ExportBundle(path);` inside `ExportDiagnosticsFromHotkey`; hotkey sink delivers on the UI thread (`HotkeyService.cs:117-132`).
**Failure scenario:** User presses the diagnostic hotkey while many logs/traces are present → UI freezes for the zip write; if Desktop I/O is slow/throttled, the freeze is user-visible.
**Impact:** UI unresponsiveness on a user-facing hotkey.
**Root cause:** Export treated as a cheap side-effect; not offloaded to a worker.
**Recommended direction:** Move `ExportBundle` to a background task and surface completion/failure via a
dispatcher-marshaled toast/log line. Do **not** implement.
**Verification recommendation:** Manual timing test; optionally a unit test on `ExportBundle` latency boundary.

### [AUDIT-019] Capture picker can open during app shutdown / emergency release
**Severity:** Medium · **Confidence:** High · **Category:** Concurrency / Failure-Handling
**Affected areas:** `App.xaml.cs:169-171,769-804` (`OnCaptureRequested` → `ShowCapturePicker`); `:379-395,441-505` (shutdown paths set `IsAppShuttingDown`); `Views/ContainerWindow.xaml.cs:198` (static `IsAppShuttingDown`)
**Summary:** `ShowCapturePicker` guards reentrancy with `_pickerOpen` and "a container close prompt is
open," **but not `IsAppShuttingDown`**. The hotkey sink is disposed only in `Application_Exit`; `SessionEnding`
and `DispatcherUnhandledException` call `Shutdown` *after* `EmergencyReleaseAll`/`SaveState`. A buffered
`WM_HOTKEY` delivered during teardown re-enters `ShowCapturePicker` → `ShowDialog` → a modal prompt over an app tearing down its captured guests.
**Evidence:** `ShowCapturePicker` (`:774-804`) checks only `_pickerOpen` and `IsClosePromptOpen`; `IsAppShuttingDown` never consulted there. `OnCaptureRequested` (`:769`) is the hotkey sink.
**Failure scenario:** Logoff begins → `SessionEnding` releases guests and calls `Shutdown`; a buffered `WM_HOTKEY` is dispatched → picker modal appears, stacking over teardown.
**Impact:** Stray modal during shutdown; can delay/block orderly exit; guards elsewhere assume no picker after shutdown.
**Root cause:** `_pickerOpen`/`IsClosePromptOpen` guards are modal-loop focused; the shutdown flag is not part of the picker-admission check.
**Recommended direction:** Add `if (ContainerWindow.IsAppShuttingDown) return;` to `ShowCapturePicker` (and/or `OnCaptureRequested`). Do **not** implement.
**Verification recommendation:** Repro: arm logoff while queuing the hotkey; assert no picker dialog.

### [AUDIT-020] `GroupViewModel.DisplayTabs` tab-strip projection has no unit-test coverage
**Severity:** Medium · **Confidence:** High · **Category:** Test-Gap
**Affected areas:** `ViewModels/GroupViewModel.cs:74-208,210-237` (`DisplayTabs`, `RebuildDisplayTabs`, `Tabs_CollectionChanged`); `tests/UnitTests/*` (only `GroupTests`/`ConverterTests`)
**Summary:** The entire projection logic — mirror-on-add/remove/move, full rebuild under split, suppression
of the RIGHT member, insertion of the `[ A | B ]` composite at the LEFT slot — is exercised only at
runtime. Nothing in `tests/UnitTests` constructs a `GroupViewModel` (it needs `GroupManager`/
`IconService`/`LoggingService`). So split/move/rebuild correctness under adversarial cases is unverified by automation.
**Evidence:** `tests/UnitTests/GroupTests.cs` is model-only; `ConverterTests.cs` is pure converters; no `GroupViewModel`/`DisplayTabs` test exists.
**Failure scenario:** A regression in `RebuildDisplayTabs`/`Tabs_CollectionChanged` (e.g., off-by-one on
`e.NewStartingIndex`, mishandled `Move` under split) would ship undetected.
**Impact:** The most logically intricate UI-projection code is unguarded by tests; high value-to-effort for a headless test with fakes.
**Root cause:** No harness wiring for `GroupViewModel` with stub `GroupManager`/`IconService`.
**Recommended direction:** Add `tests/UnitTests/GroupViewModelTests.cs` driving `AddCapturedWindow`/`ReleaseTab`/`SetSplitComposite`/`ClearSplitComposite`/`ReorderTabs` and asserting `DisplayTabs`; introduce a thin `IIconService` seam to enable construction without real icon extraction. Do **not** implement.
**Verification recommendation:** The new tests themselves; target add/remove/move with and without an active split.

### [AUDIT-021] Window titles (PII) are written verbatim to the on-disk log; only exports are sanitized
**Severity:** Medium · **Confidence:** High · **Category:** Security / Privacy
**Affected areas:** `Services/WindowShepherdService.cs:701,1708,1821` (logs `OriginalTitle`/`initialTitle`/`finalTitle`); `Services/LoggingService.cs:109-138` (no redaction); contrast `DiagnosticReportService.cs:149` (title hashed in export)
**Summary:** `LoggingService.Log` persists messages with no redaction. Capture/release/position paths embed
the live window title directly. Window titles routinely contain PII (document names, email subjects, chat
participants). The structured diagnostic report reduces titles to `titleSha256`/`titleLength`, and the
support bundle sanitizes via `ReadSanitizedRecentLogText`, but the raw rotating log does not — an internal
privacy inconsistency against the project's stated "no PII" goal.
**Evidence:** `LoggingService.Log` (`:121`) builds `$"[{...}] {message}"` verbatim; `WindowShepherdService` logs `cw.OriginalTitle` at `:701/1708/1821`. `DiagnosticPrivacySelfTest` asserts only the sanitized *export*, never the raw `TabDock.log`.
**Failure scenario:** User captures "Q3 Earnings - J. Smith.pdf - Adobe Acrobat"; the title is persisted in cleartext in `TabDock.log` (survives rotation) for any local reader or backup/sync of AppData.
**Impact:** Local PII disclosure to anyone reading the log; weakens the project's own privacy posture. Exploitability is low (log is user-ACL'd), severity driven by the stated privacy commitment. (Closely related to AUDIT-022.)
**Root cause:** Redaction applied only at the diagnostic-export boundary, not at the log sink; title hashing was done for the structured report but not the textual log.
**Recommended direction:** Route capture/position log lines through `DiagnosticEnvironmentService.SanitizeText` (or hash titles in the textual log the way the structured report does). Do **not** implement.
**Verification recommendation:** Extend `DiagnosticPrivacySelfTest` to capture the raw `TabDock.log` sink and assert a synthetic title is absent/sanitized.

### [AUDIT-022] Log-tail "omit raw window titles" is enforced by keyword coupling, not a redaction rule
**Severity:** Medium · **Confidence:** High · **Category:** Privacy / Architecture / Test-Gap
**Affected areas:** `Services/DiagnosticEnvironmentService.cs:189-222` (`ReadSanitizedRecentLogText`), `:239-276` (`SanitizeText`); title-bearing lines `WindowShepherdService.cs:701,1821`, `GuestLifecycleService.cs:412`
**Summary:** The sanitized log tail relies on a skip-list of literal substrings — `"Shepherd-captured"`,
`"Shepherd-released"`, `"title changed"`, `"Created group"`, `"Quarantined corrupt"`. `SanitizeText` has
**no concept of a window title**; it redacts only absolute paths, secret-looking values, and usernames. So
title omission holds today *only because* every title-logging site happens to include one of those exact
substrings.
**Evidence:** The three title-bearing lines each contain a skip keyword; `SanitizeText` (`:239-276`) only replaces paths/secrets/usernames.
**Failure scenario:** A developer adds a `STATE[…]` or `SHEPHERD[…]` line that includes `OriginalTitle`/`GetWindowTextString` but omits the literal skip words → that line passes the keep-filter and reaches the support ZIP with the raw title, because nothing redacts titles.
**Impact:** A raw third-party window title (often a document name, account, or URL) could leak into a shared support bundle. Latent today; no current exploit. (Enables the gap behind AUDIT-021.)
**Root cause:** Privacy enforcement split between "don't log titles in KEPT lines" (author discipline) and a generic text sanitizer that doesn't model titles.
**Recommended direction:** Either never emit titles into log lines at all, or add a title-redaction pass to `SanitizeText`/the log filter keyed on `OriginalTitle` content. Don't rely on substring coincidence. Do **not** implement.
**Verification recommendation:** Self-test seeding the real log with a KEPT-tag line containing a known title; assert it is absent from `ReadSanitizedRecentLogText`.

### [AUDIT-023] `DiagnosticPrivacySelfTest` gives false assurance — it never asserts titles absent, only current-user profile strings
**Severity:** Medium · **Confidence:** High · **Category:** Test-Gap / Privacy
**Affected areas:** `Services/DiagnosticPrivacySelfTest.cs:13-105` (esp. `:34,:49,:84-86`)
**Summary:** The self-test meaningfully exports a real bundle and inspects every ZIP entry (good), but its
assertions only check absence of the current `UserProfile`/`AppData`/`LocalAppData` literals and a few
hardcoded secrets. It does **not** assert an arbitrary window title is absent, and it checks
`C:\Users\private\guest.exe` even though the bundle never reads pending-journal file contents (that
assertion is effectively vacuous).
**Evidence:** `:84-86` iterates entries checking only `profile, profileSlash, appData, appDataSlash, localAppData, username` + three secret tokens; no title, no secondary-volume path, no arbitrary absolute path is probed.
**Failure scenario:** A leak of (a) a raw title, (b) a path on a secondary volume (`D:\Users\Bob\…`, `\\server\share\…`), or (c) a captured exe under a non-`%USERPROFILE%` location would not be caught; the gate stays green. Directly enables AUDIT-021/022 to ship undetected.
**Impact:** CI privacy gate can pass while a real PII leak exists.
**Root cause:** Test fixtures mirror the *known* redaction outputs rather than asserting the *invariant* (no PII of any kind).
**Recommended direction:** Assert absence of a planted window title across all bundle entries; assert absence of arbitrary/secondary absolute paths and of a planted exe-under-profile app name. Do **not** implement.
**Verification recommendation:** Inject a fixture log containing `STATE[…] title='SECRET-DOC-NAME'` and a `D:\Users\Other\app.exe` path; require the test to fail until redaction covers them.

### [AUDIT-024] On the file-backed path, dropped log lines are silently and permanently lost
**Severity:** Medium · **Confidence:** Medium · **Category:** Failure-Handling
**Affected areas:** `Services/LoggingService.cs:127-130` (`TryAdd` drop), `:232-240` (`_memoryLines` only fed when `!_fileBacked`)
**Summary:** `Log()` uses `TryAdd(line, 0)` (non-blocking); on a full queue it increments `_droppedLines`
and discards the line. The bounded in-memory `_memoryLines` tail (512) is **only** populated in the
`!_fileBacked` branch; on the normal file-backed path, dropped lines vanish with no fallback. The "N lines
dropped" notice is emitted only if/when the writer later catches up.
**Evidence:** `_queue` capacity 4096; `TryAdd` failure → `Interlocked.Increment(ref _droppedLines)` with no retention; `_memoryLines` guarded by `if (!_fileBacked)`.
**Failure scenario:** Disk stall / AV scan / network-home-dir latency makes the writer fall behind during a
failure burst; the single error line explaining the crash is dropped; only a generic "N lines dropped"
count remains — which itself may be lost on teardown (see AUDIT-025).
**Impact:** Diagnostics can miss the exact line needed to explain a failure, precisely when the system is unhealthy. Documented tradeoff, but a real blind spot for a *diagnostics* subsystem.
**Root cause:** Non-blocking drop policy with no spillover retention for the common (file-backed) case.
**Recommended direction:** Add a small bounded in-memory spill ring even when file-backed, or prioritize
ERROR/EXCEPTION-severity lines over chatty DEBUG ones when the queue is full. Do **not** implement.
**Verification recommendation:** Unit test that stalls the writer and asserts critical lines are written or retained in memory, not silently dropped.

### [AUDIT-025] `LoggingService.Dispose` joins the writer for only 2s, then leaks the queue and stops draining
**Severity:** Medium · **Confidence:** Medium · **Category:** Failure-Handling / Resource-Lifecycle
**Affected areas:** `Services/LoggingService.cs:337-352`
**Summary:** `Dispose()` does `if (_writerThread.Join(TimeSpan.FromSeconds(2))) _queue.Dispose();` — if the
join times out (writer stalled on a slow/failing disk), the queue is never disposed and remaining queued
lines are never flushed.
**Evidence:** `:350` `Join(2s)` gating `_queue.Dispose()`; no alternative flush path on timeout.
**Failure scenario:** Process teardown on a stalled disk: final crash/exception logs remain in the queue, the
writer never drains, and they are lost. Compounds AUDIT-024 on an unhealthy disk.
**Impact:** Loss of final diagnostic lines exactly during failure recovery; minor handle/queue leak on abnormal exit.
**Root cause:** Fixed 2s budget with no truncation-flush fallback.
**Recommended direction:** On join timeout, attempt a best-effort synchronous drain of whatever is queued (or
write the in-memory tail) before giving up. Do **not** implement.
**Verification recommendation:** Fault-injection test: stall writer, call Dispose, assert queued lines are flushed or persisted to the `.err` fallback.

### [AUDIT-026] `HardeningRegressionTests` exercises zero persistence code
**Severity:** Medium · **Confidence:** High · **Category:** Test-Gap / Docs-Divergence
**Affected areas:** `tests/UnitTests/HardeningRegressionTests.cs` (entire file)
**Summary:** Despite being named for "hardening-audit fixes," the file tests only
`PresentationLayoutCoordinator` and `SplitPresentationPolicy`. There is no reference to
`PersistenceService`, `WindowShepherdService`, the journal, or `RescueOrphanedWindows`.
**Evidence:** Full read: all 6 facts (`Coordinator_*`, `Split*`) concern layout/split policy.
**Failure scenario:** A reviewer trusting the audit's file scope believes persistence hardening is covered by unit tests; it is not.
**Impact:** False sense of coverage; the "tests proving properties" lens is unsatisfied for persistence. (Reinforces AUDIT-001.)
**Root cause:** File naming/ownership drift.
**Recommended direction:** Add genuine persistence/crash-recovery regression cases here, or rename to avoid implying persistence coverage. Do **not** implement.
**Verification recommendation:** Grep the test project for `PersistenceService`/`RescueOrphanedWindows` — only present via the promoted self-tests (AUDIT-001).

### [AUDIT-027] Single-writer guarantee is in-process only; no cross-process lock on the shared filenames
**Severity:** Medium · **Confidence:** Medium · **Category:** Concurrency
**Affected areas:** `Services/PersistenceService.cs:52` (`_writeGate`), `:231-258` (`CommitJson`); `Services/WindowShepherdService.cs:2380-2388` (`SaveJournal`); `tests/UnitTests/PersistenceSingleWriterTests.cs` (single instance)
**Summary:** `_writeGate` and `_lastAttemptedGeneration` are instance fields. The atomic write still uses
fixed, process-shared filenames (`state.json`, `.bak`, `.tmp`; `hidden-windows.json`, `.tmp`). Two TabDock
processes (or the app plus the `ValidationDriver` harness) writing the same `%APPDATA%\TabDock` files
concurrently can interleave `.tmp`/`File.Copy`/`File.Move` on those shared names, producing a torn primary or journal.
**Evidence:** `CommitJson` guarded only by `lock (_writeGate)` (per-instance); `PersistenceSingleWriterTests` stress only intra-process concurrency; `SaveJournal` has no cross-process serialization.
**Failure scenario:** User runs two TabDock instances, or the ValidationDriver runs alongside the app; both call `Save` → `.tmp` clobbered mid-write → `File.Move` promotes a partial `.tmp` → corrupt `state.json`/`hidden-windows.json`.
**Impact:** Cross-process torn writes; the both-corrupt self-protection then preserves *both* bad copies (data loss for layout intent).
**Root cause:** Single-instance assumption never encoded as a lock; the write gate is thread-level only.
**Recommended direction:** Add a process-wide named `Mutex`/file lock around `CommitJson` and `SaveJournal`, or enforce single-instance at launch. Do **not** implement.
**Verification recommendation:** Two processes each calling `Save` in a tight loop against the same path, asserting final parse-ability.

### [AUDIT-028] Fail-closed state depends on `Load()` having run first; `Save()` never re-verifies an existing primary's schema version
**Severity:** Medium · **Confidence:** Medium · **Category:** Correctness
**Affected areas:** `Services/PersistenceService.cs:165-169` (`_stateLoadFailed` gate in `BuildStateJson`), `:212-218` (only `Directory`/`Unreadable` re-classified), `:323-328` (future-version handling exists only in `Load`)
**Summary:** The documented "future schema version must block later saves" guarantee is enforced in `Load` by
setting `_stateLoadFailed = true`. `BuildStateJson` (used by `Save`/`SaveAsync`) re-classifies the primary
path only for `Directory`/`Unreadable`, and never re-reads the schema `Version`. If `Save` is invoked before
`Load` (or `Load` is skipped), a future-version primary would be overwritten/downgraded.
**Evidence:** `Load` (`:323-328`) returns `Unsupported` → `_stateLoadFailed`; `BuildStateJson` (`:165`) only checks `_stateLoadFailed`; (`:212`) classifies path. No version re-check on the existing primary before `CommitJson`'s `File.Copy`/`Move`.
**Failure scenario:** A caller constructs `PersistenceService` and calls `Save` before `RestoreState()/Load()`; the on-disk `state.json` is a future version → it gets overwritten with the current schema.
**Impact:** Silent downgrade/destruction of future-version user state.
**Root cause:** The fail-closed invariant is unlocked by a separate `Load()` call rather than being re-established by `Save()` itself.
**Recommended direction:** Have `BuildStateJson`/`CommitJson` classify and refuse a future-version primary directly (mirror the `Load` logic) rather than depending on `_stateLoadFailed`. Do **not** implement.
**Verification recommendation:** Unit test: place a `Version=3` (future) primary, call `Save` *without* `Load`, assert the file is unchanged and `_stateLoadFailed` is set.

### [AUDIT-029] An unparseable future-version journal is quarantined (moved), not preserved verbatim
**Severity:** Medium · **Confidence:** Medium · **Category:** Failure-Handling
**Affected areas:** `Services/WindowShepherdService.cs:2066-2137` (`LoadJournal` static); `:2440-2485` (`RescueOrphanedWindows`)
**Summary:** For a parseable future version, `LoadJournal` leaves the file untouched (preserved). But if the
future-version file parses as JSON yet fails `JsonSerializer.Deserialize` (structurally incompatible major
format this build cannot materialize), the outer `catch` quarantines it to `*.corrupt.*` — moving it out of
place. So "future version blocks and is preserved" holds only for *deserializable* future files.
**Evidence:** `:2069-2082` extract version in inner try; `:2089-2090` deserialize in outer try; `:2118-2136` quarantine on any exception. `RescueOrphanedWindows` (`:2437`) returns on `loadFailed` without preservation for this case.
**Failure scenario:** A future author ships a v3-breaking format this older build cannot deserialize; the journal is silently relocated to `.corrupt.*` rather than preserved verbatim, partially defeating "future version must never be destroyed."
**Impact:** Future-version evidence can be lost (relocated) instead of preserved, for the unparseable subset.
**Root cause:** Version detection and deserialization are sequential but the quarantine path does not distinguish "future-but-unparseable" from "corrupt-and-current."
**Recommended direction:** Preserve (move to `.pending`/`.future`) any file whose *declared* `Version` exceeds
`CurrentVersion`, regardless of deserialization outcome, before any quarantine. Do **not** implement.
**Verification recommendation:** Fixture `{"Version":99,"Entries":[...]}` → assert original preserved (not `.corrupt`).

### [AUDIT-031] The real-native crash-recovery rescue path is not covered in CI
**Severity:** Medium · **Confidence:** High · **Category:** Test-Gap
**Affected areas:** `Services/RecoveryJournalSelfTest.cs` (uses `FakeRecoveryApi`); `tests/ValidationDriver/.../Scenarios.CrashRescue.cs` (not run in CI); `scripts/validate.ps1:239`
**Summary:** The crash-recovery journal's *replay algorithm* is covered in CI by `RecoveryJournalSelfTest.Run()`
(16 checks) via `--selftest-diagnostics`, but that self-test drives `RescueOrphanedWindows` with an in-memory
`FakeRecoveryApi`. The **real-native rescue path** (actual Win32 against real force-killed windows: identity
verification, real `SetWindowPlacement`/`SetWindowPos`/`ShowWindow`, DWM transition restore) is validated
only by the supervised live `crash-recovery` shard, explicitly excluded from hosted CI.
**Evidence:** `RecoveryJournalSelfTest.cs:463-545` `FakeRecoveryApi` implements `IRecoveryNativeApi` in-memory; `Scenarios.CrashRescue.cs:33` `ctx.TabDock.Kill()` + relaunch + `WaitForLogLine("SHEPHERD[rescue]")` is the only real-native coverage; `build.yml` never invokes ValidationDriver scenarios.
**Failure scenario:** A regression in real identity verification or DWM-transition restore passes CI's fake-API self-test but breaks the headline Shepherd guarantee ("force-kill TabDock, relaunch, every identity-valid guest returns to original state").
**Impact:** The single most safety-critical recovery property is gated only by a supervised, human-run harness, not by any merge-blocking CI.
**Root cause:** Real-native rescue requires a live desktop and force-kill/relaunch, which cannot run unattended on the hosted runner.
**Recommended direction:** Add a headless-but-real integration layer calling `RescueOrphanedWindows` against real (test-created, non-UI) windows using the production `IRecoveryNativeApi`. Do **not** implement.
**Verification recommendation:** Integration test with the real native API; confirm it fails when the real identity gate is deliberately broken.

### [AUDIT-032] ValidationDriver harness is excluded from `TabDock.sln`; some scenarios never run in `all`
**Severity:** Medium · **Confidence:** High · **Category:** CI/CD / Test-Gap
**Affected areas:** `TabDock.sln:8,12` (only Spike + UnitTests); `tests/ValidationDriver` (not in sln); `KNOWN_ISSUES.md` H-NEW
**Summary:** `TabDock.sln` includes `TabDock.Spike` and `TabDock.UnitTests` but NOT `tests/ValidationDriver` or
`tests/Performance`. A compile break in the harness is caught only by `validate.ps1`'s explicit `dotnet build
$DriverProject`. This was a real historical incident (KNOWN_ISSUES H-NEW: three missing-brace compile errors
went undetected, invalidating prior "PASS" claims). Also, scenarios gated behind `--guest` (browser/real-app)
and `StandaloneExtraScenarios` are not in `AllOrder`, so `all` never executes them; KNOWN_ISSUES admits the Firefox scenarios are "written but never executed."
**Evidence:** `TabDock.sln` grep returns only `TabDock.Spike` and `TabDock.UnitTests`; `KNOWN_ISSUES.md:447-466` documents the harness-not-in-sln compile incident; `:609-614` admits Firefox paths "written but never executed … Treat as HYPOTHESIS."
**Failure scenario:** A scenario body syntactically valid but logically broken (or changed app API) can rot undetected if outside `AllOrder` and reachable only via `--guest`/standalone.
**Impact:** Portions of the "comprehensive" ValidationDriver coverage are self-documented as never-run; the harness's own build is structurally outside the primary solution gate.
**Root cause:** Harness kept out of `TabDock.sln` by design (separate real-input harness), with compile safety depending solely on `validate.ps1`.
**Recommended direction:** Add a CI step that at least compiles ValidationDriver *and* statically validates scenario registration/dispatch coverage (assert every `Scenarios.*` method is reachable), and mark never-run guest scenarios as `SKIP_NOT_APPLICABLE` in CI. Do **not** implement.
**Verification recommendation:** CI job failing when a `public static void (Ctx, Options)` scenario method is not registered in any dispatch array.

### [AUDIT-033] RC workflow exposes an unsupported `digicert-stm` signing provider (fails closed, foot-gun)
**Severity:** Medium · **Confidence:** High · **Category:** CI/CD / Configuration
**Affected areas:** `.github/workflows/release.yml:43-50` (input options), `:119-124` (env); `scripts/sign-release.ps1:403-451`
**Summary:** The RC-qualification-only `release.yml` offers `digicert-stm` as a selectable `signing-provider`,
but the RC job never installs the DigiCert action, never wires `SM_*` secrets (only local-PFX), and does not
set production-gate env vars. Selecting `digicert-stm` makes `sign-release.ps1`'s branch run `smctl` (not
installed) → exit 3 → qualification fails. The description even warns it is unsupported on this path.
**Evidence:** `release.yml:43-50` lists `digicert-stm`; `:119` `SIGNING_PROVIDER: ${{ inputs.signing-provider }}`; `sign-release.ps1:412-422` resolves `smctl` and throws `SIGNING_FAILED` when not found.
**Failure scenario:** An operator selects `digicert-stm` for an RC run; the run fails at the sign step with a confusing `SIGNING_FAILED`/smctl-not-found error rather than an early clear guard.
**Impact:** Misleading UI option; wasted RC runs. Not a security hole (fails closed).
**Root cause:** The input `options` list was not narrowed to the RC-supported set (`not-configured`, `local-pfx`).
**Recommended direction:** Remove `digicert-stm` from `release.yml`'s `signing-provider` options. Do **not** implement.
**Verification recommendation:** Lint workflow inputs against the signer backends actually wired in the job.

### [AUDIT-034] The most security-critical release invariant has zero automated test coverage
**Severity:** Medium · **Confidence:** High · **Category:** Test-Gap / CI/CD
**Affected areas:** `.github/workflows/prepare-release-candidate.yml` & `publish-release.yml` (inline YAML checks); `scripts/release-tooling-tests.ps1` (tests only the PowerShell module)
**Summary:** `release-tooling-tests.ps1` (run in `build.yml`) thoroughly tests the PowerShell release module,
but the *boundary that ties policy to the executed workflow revision* (`github.workflow_sha == github.sha`,
`github.ref == refs/heads/main`, cross-run artifact-name/SHA binding) lives inline in workflow YAML and is
exercised by no automated test. This is the coverage gap specific to the trust boundary in AUDIT-005.
**Evidence:** `release-tooling-tests.ps1` mocks signing/policy functions but cannot assert YAML-level
`github.*` comparisons or `download-artifact` run-binding logic; `publish-release.yml:178-189` and `:197-225` are pure YAML, untested.
**Failure scenario:** A typo or semantic drift in the YAML boundary (e.g., dropping the `workflow_sha` check) would not be caught by any test and could silently weaken release integrity.
**Root cause:** Workflow YAML is not in the unit-testable surface; the hermetic suite covers only the PowerShell module.
**Recommended direction:** Extract the boundary checks into testable helpers invoked by both the workflow and
`release-tooling-tests.ps1`, or add a YAML-level contract test asserting the required `github.*` guards are present and correctly wired. Do **not** implement.
**Verification recommendation:** A test parsing the workflow YAML and asserting the presence and exact form of the trust-boundary steps.

---

## 7b. (continuation of severity sections — see next block for Low/Info)

> *The Medium section above ends the High+Medium findings. The Low and Info findings follow.*

---

## 8. Low-Severity Findings

### [AUDIT-035] `Release` Mismatch leaves the capture token on a foreign HWND, permanently blocking that HWND value
**Severity:** Low · **Confidence:** High · **Category:** Resource-Lifecycle
**Affected areas:** `Services/WindowShepherdService.cs:1588-1614` (Mismatch path), `:2739-2748` (token removal); consumed by `Capture` `:625`
**Summary:** On the `Mismatch` branch (recycled HWND now owned by a different process but still carrying
TabDock's `SetProp` token), the code calls `JournalClear` + `UnregisterCapturedIdentity` but **not**
`RemoveCaptureIdentityToken` (contrast the `Released` path which does, `:1808`). The stale token remains
until the foreign window is destroyed; a later capture of that same HWND value hits `IsCaptureTokenAvailable`
(`:625`) and is refused for the foreign window's lifetime.
**Impact:** Fail-closed (safe) but produces a confusing "already has a capture identity token" rejection for a genuinely new window. Undocumented.
**Recommended direction:** On a positively-proven `Mismatch` (not merely `Unverifiable`), attempt `RemoveCaptureIdentityToken` best-effort; keep `Unverifiable` as no-touch. Do **not** implement.

### [AUDIT-036] Hot-path mutation is gated solely by the capture token (implicit contract)
**Severity:** Low · **Confidence:** High · **Category:** Architecture
**Affected areas:** `Services/WindowShepherdService.cs:804-820,937-991,1005-1032,1161-1229`; `Services/WindowIdentityGate.cs:167-247`
**Summary:** Every native mutation on the positioning fast path uses the cheap tier
(`verifyExecutable:false, verifyProcessInstance:false`) — correct *because* a recycled HWND into a new
process won't carry our `SetProp` token. The finding records this as the **single linchpin** of the hot
path: any future mutation path that positions a guest without a prior `SetCaptureIdentityToken` would have
no anti-recycle guard.
**Impact:** None today; a maintenance guardrail note.
**Recommended direction:** Add a comment at the cheap-tier entry stating the token is the sole recycle guard, and keep a self-test that positions a guest whose token was stripped (expect no mutation). Do **not** implement.

### [AUDIT-037] `RecoveryPending` retains original-placement; rescue replays stale geometry on an already-released window
**Severity:** Low · **Confidence:** Medium · **Category:** Failure-Handling
**Affected areas:** `Services/WindowShepherdService.cs:1402-1479` (Hide Unverifiable→RecoveryPending), `:1588-1630` (Release Unverifiable→RecoveryPending), `:2609-2627` (RestoreJournaledPresentation)
**Summary:** When identity is `Unverifiable`, `Hide`/`Release` return `RecoveryPending` and intentionally do
not clear the journal or mutate the window (sound fail-safe). But the journal entry always carries
`OriginalPlacement` (see AUDIT-006) and is never updated, so rescue re-applies *pre-capture* geometry to a
window the user already released/repositioned.
**Impact:** Low-probability re-detach; the window is at least made visible.
**Recommended direction:** On `RecoveryPending` from `Release`/`Hide`, downgrade the retained entry to "no placement restore needed." Do **not** implement.

### [AUDIT-038] UI-thread serialization is the only guard against reentrant `Release`/`Hide` during a WinEvent
**Severity:** Low · **Confidence:** Medium · **Category:** Concurrency / Architecture
**Affected areas:** `Services/GroupManager.cs:400-435`, `:489-516`; `Services/WindowShepherdService.cs:1588-1830`; `Services/GuestLifecycleService.cs:80-88,429-469`
**Summary:** `ReleaseTab` reads `group.Members[index]` then removes at `RemoveAt(index)` *after* `_shepherd.Release`
returns, relying on synchronous UI-dispatcher execution so no `WinEvent` can interleave. Currently safe, but
fragile if any `Release` becomes reentrant/async.
**Impact:** Currently none; latent risk if the single-UI-thread assumption is violated.
**Recommended direction:** Document the invariant and/or guard each `CapturedWindow` with an `isReleasing` flag. Do **not** implement.

### [AUDIT-039] Unbounded `_positioningFailuresLogged` / `_identityFailuresLogged` sets grow for the process lifetime
**Severity:** Low · **Confidence:** Medium · **Category:** Resource-Lifecycle
**Affected areas:** `Services/WindowShepherdService.cs:170,176`
**Summary:** These `HashSet<long>` accumulate one entry per distinct failing HWND and are never cleared.
They cap per-window log spam (good) but over a long session with many transient/recycled top-level windows
they grow without bound.
**Impact:** Minor memory growth; not a leak of unmanaged handles, but unbounded managed state.
**Recommended direction:** Periodically prune or cap the sets. Do **not** implement.

### [AUDIT-040] `ReleaseVisible` treats `showCmd == 0` as `SW_SHOW` (magic-0 mapping)
**Severity:** Low · **Confidence:** Medium · **Category:** Correctness
**Affected areas:** `Services/WindowShepherdService.cs:1773-1775`
**Summary:** `int showCommand = window.OriginallyVisible ? (window.OriginalPlacement.showCmd == 0 ? SW_SHOW : (int)showCmd) : SW_HIDE;`. `showCmd == 0` is `SW_HIDE` in `WINDOWPLACEMENT` semantics; mapping it to `SW_SHOW` is an undocumented assumption. In practice `GetWindowPlacement` returns non-zero for visible windows, so the result is correct for the only realistic input.
**Impact:** Negligible.
**Recommended direction:** Replace the magic `0` with an explicit `SW_SHOWNORMAL` and document why. Do **not** implement.

### [AUDIT-041] Context menu offers an empty "Split screen" submenu when exactly one tab exists
**Severity:** Low · **Confidence:** High · **Category:** Correctness (UI)
**Affected areas:** `Views/ContainerWindow.xaml.cs:1462-1503`
**Summary:** The split-offer branch fires for any count other than 2 (where it's a direct item), including 1,
producing a zero-child "Split screen" submenu. The spec requires ≥2 tabs to split.
**Impact:** Dead-end UI affordance; minor confusion, no crash.
**Recommended direction:** Guard the split offer with `tabCount >= 2`. Do **not** implement.

### [AUDIT-042] `DefinePair` replace-path leaves the old pair half-hidden on `RecoveryPending`
**Severity:** Low · **Confidence:** Medium · **Category:** Failure-Handling
**Affected areas:** `Services/SplitPresentationController.cs:66-77`; `Views/ContainerWindow.xaml.cs:2785-2842`
**Summary:** When `DefinePair` replaces a pair and the departing-member hide returns `RecoveryPending`, it
exits leaving the OLD pair with `_presented == true` and one old member possibly hidden. Repair relies on a
subsequent `LayoutSplitPanes` re-show; if the container closes or the settle is skipped before rendering, the
member may remain hidden.
**Impact:** Transient/edge; fail-closed but unverified.
**Recommended direction:** On `RecoveryPending` during replace, re-show any already-hidden old member or guarantee a repair relayout even when chrome is active. Do **not** implement.

### [AUDIT-043] `HotkeyService.Register` is non-reentrant after `Dispose`
**Severity:** Low · **Confidence:** High · **Category:** Resource-Lifecycle
**Affected areas:** `Services/HotkeyService.cs:39-89,91-115`
**Summary:** After `Dispose()`→`Detach()` sets `_source = null`/`_hook = null`, a subsequent `Register()` returns
early at `:41` without rebuilding, so the hotkey can never re-register. Today `Dispose` is only at process
exit, so latent.
**Impact:** Latent: hotkey silently stops working if lifecycle ever becomes dynamic.
**Recommended direction:** Track a separate `_disposed` flag; reset `_registered`/`_diagnosticRegistered` in `Detach`. Do **not** implement.

### [AUDIT-044] `CapturePickerViewModel.WindowInfo` PropertyChanged handlers are never detached on refresh
**Severity:** Low · **Confidence:** High · **Category:** Resource-Lifecycle
**Affected areas:** `ViewModels/CapturePickerViewModel.cs:211-218,104,216`
**Summary:** Every candidate row subscribes an anonymous `PropertyChanged` handler; `Refresh()` clears and
repopulates but never unsubscribes. Until the next refresh, discarded `WindowInfo` instances (and their
closures over `_viewModel`) stay rooted.
**Impact:** Minor managed memory pressure and redundant event churn; not a native leak.
**Recommended direction:** Unsubscribe on removal, or use a single `ObservableCollection.CollectionChanged` listener. Do **not** implement.

### [AUDIT-045] `SelectedItem` is bound `OneWay`; user selection reaches the VM only via the `SelectionChanged` side channel
**Severity:** Low · **Confidence:** Medium · **Category:** Architecture / Correctness
**Affected areas:** `Views/ContainerWindow.xaml:148-149`; `Views/ContainerWindow.xaml.cs:1286-1312`; `ViewModels/GroupViewModel.cs:84-96`
**Summary:** `SelectedItem="{Binding ActiveTab, Mode=OneWay}"` means clicks can't write back through binding;
`TabsListBox_SelectionChanged` detects a genuine click and calls `SetActiveTab`. Couples selection sync to
`SelectionChanged` firing and the `_inSelectionSync` guard.
**Impact:** Selection can silently diverge from logical active tab if a path changes ListBox selection without raising `SelectionChanged`.
**Recommended direction:** Bind `SelectedItem` `TwoWay` (keep the reentrancy guard) or own selection purely in the VM. Do **not** implement.

### [AUDIT-046] `PickColorCommand` is a dead no-op with no XAML binding
**Severity:** Low · **Confidence:** High · **Category:** Dead-Code
**Affected areas:** `ViewModels/GroupViewModel.cs:98,154-160`
**Summary:** `PickColorCommand` is initialized to `RelayCommand(_ => { })` (explicit no-op) and is bound to
nothing in any XAML. Retained as a placeholder after a behavior change.
**Impact:** Harmless dead code; latent trap if a future dev binds it expecting color behavior.
**Recommended direction:** Remove the command (and the `AccentColor` color-picker UI if also dead), or implement it. Do **not** implement.

### [AUDIT-047] Global static `ContainerWindow.IsAppShuttingDown` couples all containers and is read in hot paths
**Severity:** Low · **Confidence:** High · **Category:** Architecture
**Affected areas:** `Views/ContainerWindow.xaml.cs:198`; `App.xaml.cs:214,355,381,399,453,962`
**Summary:** `IsAppShuttingDown` is a `public static bool` shared across every `ContainerWindow` and the App
orchestrator. It gates the close-confirm prompt, but is set *after* `EmergencyReleaseAll` on several paths, so
a guest destroy arriving in that gap still triggers a close prompt.
**Impact:** Occasional stray close prompt during teardown; otherwise benign.
**Recommended direction:** Set the flag at the earliest point on every exit path, or pass lifecycle state per container. Do **not** implement.

### [AUDIT-048] `DeleteGroupRequested` is not picker-aware (modal stacking)
**Severity:** Low · **Confidence:** Medium · **Category:** Concurrency
**Affected areas:** `App.xaml.cs:774-804`; `Views/ContainerWindow.xaml.cs:341-390`
**Summary:** The picker modal is guarded by `_pickerOpen` plus the close-prompt check, but `_pickerOpen` is
App-private and not exposed to the container's delete/close guards. A delete can be initiated while the
capture picker's `ShowDialog` loop is pumping. Same family as AUDIT-017/019.
**Impact:** Stacked modals / answer-out-of-order.
**Recommended direction:** Expose a shared "any modal prompt open" predicate consulted by both the picker admission and the delete/close prompts. Do **not** implement.

### [AUDIT-049] `ConsoleSession` gating semantics overstated vs implementation
**Severity:** Low · **Confidence:** High · **Category:** Architecture / Docs-Divergence
**Affected areas:** `Services/ConsoleSession.cs` (whole); `Services/DiagnosticCommandLine.cs:144-158`
**Summary:** The system model describes "ConsoleSession check gates operations to the interactive console
session," but `ConsoleSession.TryCreate` only ensures a *usable stdio console* (`AttachConsole` to parent or
redirected handles); no `WTSQuerySessionInformation`/session-0 check exists.
**Impact:** Documentation/security-model mismatch; recovery is effectively gated by *human interactivity*, not WTS session membership.
**Recommended direction:** Clarify the doc to say "requires a usable interactive console," or add an explicit interactive-WTS-session check if session-0 recovery is a concern. Do **not** implement.

### [AUDIT-050] Capture elevation check fails OPEN when TabDock itself is elevated
**Severity:** Low · **Confidence:** High · **Category:** Correctness
**Affected areas:** `Services/WindowShepherdService.cs:440-466`
**Summary:** When `IsProcessElevated(target)` is indeterminate (OpenProcess/OpenProcessToken denied), the code
refuses only if TabDock is **not** elevated; if TabDock **is** elevated it proceeds. Intentional fail-open
exception. No UIPI risk (an elevated process legitimately mutates any window).
**Impact:** None security-wise; the risky non-elevated-vs-elevated-target case still refuses.
**Recommended direction:** Keep, but add a self-test proving the elevated-self + indeterminate-target path. Do **not** implement.

### [AUDIT-051] Capture PID/identity-reuse window between elevation check and strong identity (no direct test)
**Severity:** Low · **Confidence:** High · **Category:** Test-Gap
**Affected areas:** `Services/WindowShepherdService.cs:419-642`
**Summary:** The elevation decision (`:440-466`) is based on `pid` captured at `:420`; the strong identity
re-derivation at `:576-609` re-reads pid/thread/exe/class/**process-start**. A target replaced between those
points is caught only by the `ProcessStartTimeUtcTicks` mismatch (`:603`) — a correct but implicit backstop.
**Impact:** None today (start-time reuse is impossible); real exploitability nil.
**Recommended direction:** Add a deterministic self-test simulating PID reuse (same pid/thread/exe/class, different start tick) between elevation probe and final identity, asserting capture is refused. Do **not** implement.

### [AUDIT-052] `ProductMutationLease` has no in-process single-acquire guard (recursive double-acquire succeeds)
**Severity:** Low · **Confidence:** High · **Category:** Concurrency
**Affected areas:** `Services/ProductMutationLease.cs:33-128`; `App.xaml.cs:1068-1077`
**Summary:** A .NET named `Mutex` is recursive per-thread; calling `TryAcquire` twice yields two
`ProductMutationLease` objects each wrapping separate handles to the same kernel mutex; both `WaitOne(0)`
succeed. Today only one lease is created at startup, so latent.
**Impact:** None in current usage.
**Recommended direction:** Document the single-acquire contract, or assert/guard against an already-held lease. Do **not** implement.

### [AUDIT-053] Capture identity token is a predictable sequential counter, not crypto-random
**Severity:** Low · **Confidence:** Medium · **Category:** Security
**Affected areas:** `Services/WindowShepherdService.cs:618`; `Services/WindowIdentityGate.cs:179-184`; contrast `PendingRecoveryService.AllocateRecoveryToken` (explicitly "cryptographically random")
**Summary:** The per-capture token set via `SetProp` is a sequential `Interlocked` counter. A same-integrity
attacker could predict the next token and `SetProp` it on a crafted window; strong-tier operations also
require a matching `ProcessStartTimeUtcTicks` (unforgeable for a new process), and the hot-tier payoff is
only repositioning the attacker's own window.
**Impact:** Negligible; no cross-process escalation. Inconsistent with the recovery-token design.
**Recommended direction:** Optionally switch the capture token to `RandomNumberGenerator` for consistency. Do **not** implement.

### [AUDIT-054] `ExePath` logged raw in `ENV[container]`/`STATE[` lines (relies only on the generic path pass)
**Severity:** Low · **Confidence:** High · **Category:** Privacy (by-design, undocumented exposure)
**Affected areas:** `Views/ContainerWindow.xaml.cs:627,1254`; contrast `Services/WindowShepherdService.cs:2513` (`RedactPath`)
**Summary:** Most exe-path logging is wrapped in `RedactPath(...)`; but `ENV[container]` and `STATE[` lines embed
`active.ExePath` raw. Redaction happens later only via `SanitizeText`'s absolute-path pass (profile root
masked, revealing installed software under the profile).
**Impact:** Limited; consistent with stated design, but the self-test (AUDIT-023) does not assert the `%USERPROFILE%` residual.
**Recommended direction:** Route all exe-path logging through a `RedactPath`-wrapped helper for uniform behavior. Do **not** implement.

### [AUDIT-055] Logging degradation is invisible in the support report
**Severity:** Low · **Confidence:** Medium · **Category:** Diagnostics / Failure-Handling
**Affected areas:** `Services/LoggingService.cs:80-105`; `Services/DiagnosticEnvironmentService.cs:127-158` (never reads logging status)
**Summary:** If log-dir creation fails, logging silently switches to in-memory-only and sets
`StorageFailureReason`, but nothing in `DiagnosticReport`/`InspectPersistence` surfaces this. The bundle's
`RecentLog` reads `unavailable (log-absent)`, indistinguishable from "no log yet."
**Impact:** Support misreads absence of evidence as evidence of absence.
**Recommended direction:** Include `_fileBacked`/`StorageFailureReason` in `DiagnosticReport.Issues`. Do **not** implement.

### [AUDIT-056] `trace.jsonl` in the support bundle is not valid JSONL
**Severity:** Low · **Confidence:** Medium · **Category:** Correctness / Docs-Divergence
**Affected areas:** `Services/DiagnosticReportService.cs:247-250`; `Services/DiagnosticEnvironmentService.cs:279-293,287`
**Summary:** For `trace.jsonl`, `AddEntry` splits the trace by newlines and calls `SanitizeJsonText` on each
line, but `SanitizeJsonText` re-serializes each record with `WriteIndented = true`, so each object spans
multiple lines. The resulting file is pretty-printed JSON per record, not valid JSONL.
**Impact:** Interop/tooling correctness only; no privacy impact.
**Recommended direction:** Use a compact serializer for the `.jsonl` path, or name the entry `trace.json`. Do **not** implement.

### [AUDIT-057] `SanitizeText` misses relative paths, WSL paths, and bare credential tokens
**Severity:** Low · **Confidence:** Medium · **Category:** Privacy
**Affected areas:** `Services/DiagnosticEnvironmentService.cs:21-32,267`
**Summary:** `s_absolutePath` only matches `[A-Za-z]:\` or `\\`. A relative path, a WSL path
(`/mnt/c/Users/…`), or a bare high-entropy token with no `password=`/`token=` label is not redacted.
**Impact:** Narrow; primary vectors (absolute Windows paths, labeled secrets) are covered.
**Recommended direction:** Extend the path regex to WSL/relative forms and add a heuristic for bare high-entropy secret strings. Do **not** implement.

### [AUDIT-058] Hardware/device identifiers in the bundle are not covered by the privacy gate
**Severity:** Low · **Confidence:** Low · **Category:** Privacy
**Affected areas:** `Models/Diagnostics.cs:61-69`; `Services/DiagnosticEnvironmentService.cs:85-125`; `DiagnosticPrivacySelfTest.cs` (no device-id assertion)
**Summary:** `DisplayAdapterSnapshot.DeviceId` can contain SMBIOS/PCI identifiers. Not personal data per se,
but forms a hardware fingerprint usable to correlate reports across users. The privacy self-test never checks device identifiers.
**Impact:** Minor fingerprinting exposure; not a direct PII leak.
**Recommended direction:** Decide intentionally whether device IDs are in scope; if so, hash/normalize them and add a self-test assertion. Do **not** implement.

### [AUDIT-059] `RedactPath` is a no-op wrapper over `SanitizeText`
**Severity:** Low · **Confidence:** Low · **Category:** Dead-Code / Clarity
**Affected areas:** `Services/DiagnosticEnvironmentService.cs:224-229`; used at `NativeSnapshotService.cs:53,200`, `WindowShepherdService.cs:2513,2518`, `App.xaml.cs:262`, `DiagnosticReportService.cs:33`
**Summary:** `RedactPath(path)` just calls `SanitizeText(path)`. For a bare path the two behave identically, so
the name implies path-only redaction while it actually runs the full text sanitizer.
**Impact:** Low; privacy-preserving but surprising and undocumented.
**Recommended direction:** Make `RedactPath` do only path-variant replacement and document the difference, or inline `SanitizeText` and drop the alias. Do **not** implement.

### [AUDIT-060] Hidden-windows journal has no `.bak` copy (asymmetry with `state.json`)
**Severity:** Low · **Confidence:** High · **Category:** Info/Opportunity / Failure-Handling
**Affected areas:** `Services/PersistenceService.cs:244-249` (backup copy) vs `Services/WindowShepherdService.cs:2380-2388` (`SaveJournal`: single `.tmp`+rename, no backup)
**Summary:** `state.json` copies the prior primary to `.bak` before overwriting; the journal rewrites via a
single `.tmp`+atomic `File.Move` with no secondary copy. Low impact in practice — the journal's pre-rewrite
file already contains the complete prior evidence, and the journal holds only guest-window metadata
reconstructable from live windows.
**Recommended direction:** Accept as-is unless the journal ever stores non-reconstructable state; if so, add the same `.bak` copy. Do **not** implement.

### [AUDIT-061] A current-version token-mismatch journal entry is silently deleted, not sidecar-preserved (docs over-claim)
**Severity:** Low · **Confidence:** Low · **Category:** Docs-Divergence / Failure-Handling
**Affected areas:** `Services/WindowShepherdService.cs:2440-2485` (`RescueOrphanedWindows`); `:2480-2485` (`EvaluateRecoveryIdentity` Mismatch → drop)
**Summary:** The documented invariant ("stale/corrupt evidence quarantined to `.pending`, never silently
deleted") applies to corrupt/legacy/incomplete evidence, but a fully-current v3 entry whose *runtime* capture
token no longer matches is classified `Mismatch` and dropped without `.pending` preservation. For a normal
rescue the window was already shown, so this is harmless; the only real risk is a guest whose token prop was
cleared by a third party without HWND recycling, leaving it hidden with its recovery entry gone.
**Impact:** Documentation/behavior divergence; a real (if narrow) stranding edge.
**Recommended direction:** Clarify the docs that only *corrupt/legacy/incomplete* evidence is sidecar-preserved; optionally preserve token-mismatch-but-otherwise-matching entries in `.pending`. Do **not** implement.

### [AUDIT-062] `System.Threading.AccessControl` pinned to `10.0.11` in a `net8.0` project
**Severity:** Low · **Confidence:** Medium · **Category:** Build / Configuration
**Affected areas:** `TabDock.csproj:51`
**Summary:** The only non-framework package is pinned to `10.0.11` (a .NET 10-era version) while the app
targets `net8.0`. It builds in CI today, but a `net8.0` app referencing a 10.0.x runtime package can pull
transitive `System.Runtime` assets from a newer runtime band; this should be deliberately confirmed.
**Recommended direction:** Confirm the package's supported TFMs include `net8.0` (or pin to the latest `8.0.x`); add it to the NuGet audit/lock expectations. Do **not** implement.

### [AUDIT-063] Unconditional `SelfContained=true` + `RuntimeIdentifier=win-x64` for all configurations
**Severity:** Low · **Confidence:** Medium · **Category:** Build
**Affected areas:** `TabDock.csproj:30-31`; `tests/UnitTests/TabDock.UnitTests.csproj:15`
**Summary:** These properties are set unconditionally, so even Debug/test builds inherit a win-x64
self-contained output, inflating Debug builds and potentially altering how `dotnet test` builds the test host.
**Recommended direction:** Gate `SelfContained`/`RuntimeIdentifier` to the publish path (or `Release`) via conditions. Do **not** implement.

### [AUDIT-064] Three-layer duplication of split/identity policy tests
**Severity:** Low · **Confidence:** High · **Category:** Test-Quality
**Affected areas:** `tests/UnitTests/SplitPresentationPolicyTests.cs`, `SplitInteractionPolicyTests.cs`; `tests/ValidationDriver/.../DeterministicSelfTests.cs`; `scripts/qa-split.ps1` (only manual runner of `--selftest all`)
**Summary:** The same pure policy transitions are asserted in three places (xUnit, app self-tests, ValidationDriver). The ValidationDriver `--selftest all` is only invoked by the manual `qa-split.ps1`, never by `build.yml`. Not false confidence (CI unit tests cover the logic) but a drift surface.
**Recommended direction:** Pick one authoritative location for the pure split/identity contract (the xUnit unit tests, which already run in CI) and have the others reference it, or document the ValidationDriver copy as a supervised-only mirror. Do **not** implement.

### [AUDIT-065] Minor config inconsistencies: `dev-doctor.ps1` rollForward default and `sync-agent-configs.ps1` overwrite
**Severity:** Low · **Confidence:** High · **Category:** Configuration
**Affected areas:** `scripts/dev-doctor.ps1:72`; `scripts/sync-agent-configs.ps1` (whole); `global.json:3-4`
**Summary:** `dev-doctor.ps1:72` defaults `rollForward` to `'latestPatch'` while `global.json` declares
`'latestFeature'` (harmless today). `sync-agent-configs.ps1` regenerates agent config mirrors into 8 tool
directories and overwrites hand-edits in non-canonical copies (by design, `.claude` is canonical) without warning.
**Impact:** Low; documentation/operational friction.
**Recommended direction:** Align the doctor's default with `latestFeature`, and have `sync-agent-configs.ps1` warn before overwriting a divergent non-canonical copy. Do **not** implement.

---

## 9. Optimization Opportunities

- **AUDIT-012 (native churn):** Arm the split settle only on split-affecting projection changes, not every
  third-tab edit; disarm the rendering handler when chrome stays active. Likely removes several
  `SetForeground`/`LayoutSplitPanes` calls per tab edit during presentation.
- **AUDIT-016 (UI-thread block):** Make `IconService.GetFileIcon` UI callers non-blocking so icon extraction never stalls the dispatcher.
- **AUDIT-018 (UI freeze):** Offload `DiagnosticReportService.ExportBundle` to a background task.
- **AUDIT-024/025 (diagnostic loss):** Add a bounded in-memory spill ring (or severity-prioritized drop) and a
  best-effort synchronous drain on Dispose timeout, so the *diagnostics* subsystem does not lose the exact
  line needed during a failure burst.
- **AUDIT-039 (unbounded sets):** Prune/cap `_positioningFailuresLogged`/`_identityFailuresLogged`.
- **AUDIT-007 (capture-gap leak):** Eliminating the two-map capture gap removes a class of DWM/token reversion work and a stray-journal edge.
- **App-startup cost:** Consider building the published single-file bundle's runtime validation into CI (AUDIT-004) to avoid discovering WPF/single-file regressions only at release time.

---

## 10. Architectural Improvement Opportunities

- **Centralize the identity model (AUDIT-002/008):** Route `Capture()` admission through `WindowIdentityGate`
  so the two-tier model is defined and enforced in exactly one place. Removes the divergence that currently
  fatally vetoes title-changing captures.
- **Make `SplitPresentationPolicy` the production authority (AUDIT-003):** Delegate `SplitPresentationController`
  state decisions to the already-tested pure policy (applying native hides around the returned desired
  state), eliminating the parallel, diverging transition logic and the false test assurance.
- **Eliminate parallel/dead split code (AUDIT-010/011):** Wire or delete `ClassifyInteraction`/`ArmSettle`/
  `_settleGeneration` and the dead `SuspendSplitPairForGuest`; remove the unreachable policy branch.
- **Single source of truth for HWND→member (AUDIT-007):** Derive `WindowShepherdService._capturedByHwnd` from
  `GroupManager._capturedIndex` (or promote capture only after group membership), so lifecycle events always resolve.
- **Generation as a first-class, always-correct gate (AUDIT-015):** Extend generation comparison from the settle
  path to all direct `LayoutSplitPanes` callers, or guarantee `IsSplitPresented` fully flips on every transition.
- **Production-enforced budget (AUDIT-014):** Either make `PresentationOperationBudget` a DEBUG assert or give
  the controller/coordinator direct unit coverage, so the single-pass discipline is enforced, not aspirational.
- **Privacy as a structured invariant (AUDIT-021/022/023):** Model "no PII (titles, arbitrary paths) in any
  diagnostic output" as a single sanitization layer applied at every sink (log + export), not as per-line
  author discipline plus a generic text pass.
- **Modal-guard coordinator (AUDIT-017/019/048):** A single "any modal prompt open" predicate consulted by the
  picker admission, the delete prompt, and the close prompt removes three asymmetric reentrancy gaps.
- **Cross-process persistence safety (AUDIT-027):** A process-wide lock (or enforced single-instance) around the shared `state.json`/`hidden-windows.json` writes.

---

## 11. Test and Quality-Gate Gaps

- **AUDIT-001:** Critical persistence/crash-recovery invariants are only in CLI self-tests, outside the xUnit runner.
- **AUDIT-003:** The production split state machine has no controller-level transition tests (only the unused pure policy is tested).
- **AUDIT-004:** CI exercises the single-file published artifact only with `--version`.
- **AUDIT-020:** `GroupViewModel.DisplayTabs` projection (the most intricate UI logic) has no unit tests.
- **AUDIT-023:** `DiagnosticPrivacySelfTest` never asserts titles/arbitrary paths absent — false privacy assurance.
- **AUDIT-026:** `HardeningRegressionTests` names imply persistence coverage that does not exist.
- **AUDIT-031:** Real-native crash-recovery rescue is not in CI (only fake-API self-test + supervised harness).
- **AUDIT-032:** ValidationDriver is outside `TabDock.sln`; some scenarios are self-documented as never-run.
- **AUDIT-034:** The release trust boundary has zero automated test coverage.
- **AUDIT-051:** No direct test for capture PID-reuse TOCTOU between elevation probe and strong identity.
- **AUDIT-064:** Split/identity policy tests duplicated across three layers; the ValidationDriver copy is not in CI.

**Priorities:** the tests that would catch the most dangerous defects are (1) promoted persistence/journal
self-tests under `dotnet test` (AUDIT-001), (2) controller-level split transition tests (AUDIT-003), (3)
single-file-artifact runtime validation in CI (AUDIT-004), and (4) a real-native rescue integration test
(AUDIT-031).

---

## 12. Security and Privacy Assessment

**Concrete vulnerabilities:** None rising to Critical/High exploitability. The native-mutation trust boundary
is sound — every guest mutation is preceded by the two-tier identity gate or `VerifyReleasedWindowCloseTarget`,
and no ungated guest-mutation path was found (positive confirmation, IDENT-009). The mutex DACL is correctly
fail-closed against pre-existing/weaker/foreign objects (IDENT-011). Recovery is interactive + identity-gated
(IDENT-005). Capture elevation fails closed for the risky non-elevated case (IDENT-003 open only when
self-elevated, which is safe).

**Meaningful hardening opportunities:**
- **Privacy leak (AUDIT-021/022/023):** Window titles (frequent PII) are written verbatim to the on-disk log
  and only the *export* is sanitized; the log-tail omission relies on keyword coupling rather than a redaction
  rule; the privacy self-test never asserts titles are absent. This is the most material privacy gap.
- **Capture token predictability (AUDIT-053):** sequential counter vs crypto-random recovery token — negligible
  today but inconsistent.
- **Device-id fingerprinting (AUDIT-058):** hardware identifiers in the bundle are out of the privacy self-test's scope.
- **Relative/WSL path and bare-token redaction gaps (AUDIT-057):** narrow, primary vectors covered.

**Secret handling:** No secrets are persisted or logged; `BuildIdentity` carries no timestamp; commit is read
from generated metadata (no Git shell-out). Telemetry is inert-by-default with no network egress.

**Authorization boundaries:** The single-instance lease and capture identity gate are the authorization model;
both are well-engineered (see positives). The only authorization *documentation* gap is `ConsoleSession`
overstatement (AUDIT-049).

**Input boundaries:** Capture admission is the principal untrusted-input boundary (foreign HWNDs); it is
two-tier gated except for the hand-rolled title veto (AUDIT-002) and the stale-journal capture-disable
(AUDIT-009), both of which are *over*-strict rather than exploitable.

---

## 13. Reliability and Failure-Recovery Assessment

- **Crash rescue (AUDIT-006):** Restores *pre-capture* geometry rather than last-known docked state, so a
  hard kill un-docks windows. Bounded and recoverable, but contradicts the documented guarantee.
- **Stale-journal capture-disable (AUDIT-009):** A legacy/incomplete journal can permanently disable capture
  for a session with a misleading error.
- **RecoveryPending replay (AUDIT-037):** Retained original-placement entries can re-detach an already-released window.
- **Unparseable future journal (AUDIT-029):** Moved to `.corrupt` rather than preserved for the unparseable subset.
- **Logging under failure (AUDIT-024/025):** Dropped lines lost on the file-backed path; 2s Dispose join can
  lose final diagnostics on a stalled disk — a blind spot for a *diagnostics* subsystem.
- **Cross-process torn writes (AUDIT-027):** Two instances can interleave shared-file writes.
- **Idempotency / restart:** `RescueOrphanedWindows` is idempotent for the normal path; `state.json` restore is
  robust to missing-primary + valid-backup. The async save generation gate is sound (PERS verified-safe).
- **Shutdown/startup:** All teardown paths run `EmergencyReleaseAll → SaveState → FlushJournal → dispose`
  idempotently; `SessionEnding` is one-way and never resumes half-torn-down (good). The picker-during-shutdown
  gap (AUDIT-019) is the main teardown race.

---

## 14. Performance and Scalability Assessment

TabDock is a desktop utility with a small, bounded object count (a few captured windows per container), so
classical O(n²)/scalability limits are not a concern. The performance findings are localized:
- **UI-thread blocking (AUDIT-016):** `IconService.GetFileIcon` can block the dispatcher on a producer wait.
- **Native churn during presentation (AUDIT-012):** Re-foregrounds/re-glues the foreground member on every
  third-tab edit while a split is presented.
- **UI freeze on diagnostics export (AUDIT-018):** Synchronous zip assembly on the hotkey path.
- **Unbounded managed state (AUDIT-039):** Log-failure/identity-failure sets grow for the session; icon cache
  unbounded (AUDIT-066/UI-007).
- **Coalesced relayout (positive):** `PresentationLayoutCoordinator` correctly issues at most one native
  reposition batch per WPF frame; `SHEPHERD[position]` is kept cheap per the perf invariant.

No probable CPU/RAM bottleneck for realistic use; the items above are smoothness/responsiveness polish.

---

## 15. Maintainability / Technical Debt Assessment

Disproportionately expensive or risky areas to change:
- **The split subsystem (AUDIT-003/010/011/013/014/015):** parallel/dead code, a test-only budget, a diverging
  controller, and generation gating only on the settle path make split-behavior changes high-risk and
  under-tested by the suite that claims to cover them.
- **`Capture()` admission (AUDIT-002/008):** hand-rolled identity logic duplicated from `WindowIdentityGate`,
  with a fatal title veto that contradicts the documented model — a trap for future identity changes.
- **Two-map capture ownership (AUDIT-007):** implicit cross-service ordering that only works because of a
  single-turn gap and a picker recheck.
- **Privacy sanitization (AUDIT-021/022/059):** split between per-line author discipline and a generic text
  pass, with a no-op `RedactPath` alias — easy to regress.
- **Dead code (AUDIT-010/011/046/059):** classifier/settle API, `SuspendSplitPairForGuest`, `PickColorCommand`,
  `RedactPath` wrapper — minor noise but increases the surface for mistaken "fixes."

---

## 16. Documentation / Implementation Divergence

- **AUDIT-002/008:** `WindowShepherdService` doc comment says title is "deliberately not part of stable identity,"
  but `Capture()` fatally vetoes on title change — direct contradiction.
- **AUDIT-010:** Docs say "controller wraps SplitInteractionPolicy"; the view bypasses the wrapper with hardcoded values.
- **AUDIT-013:** Controller `Foreground`/`ToState().ActiveGuest` stale after suspend, diverging from the policy's `ActiveGuest` convention.
- **AUDIT-026:** `HardeningRegressionTests` name implies persistence coverage that does not exist.
- **AUDIT-029/061:** Docs claim future-version/evidence is "never silently deleted/preserved"; unparseable future
  journals are quarantined and current-version token-mismatch entries are dropped.
- **AUDIT-049:** `ConsoleSession` doc implies WTS/session-0 gating that is not implemented.
- **AUDIT-056:** `trace.jsonl` is emitted pretty-printed, not valid JSONL.
- **AUDIT-062/063:** csproj package/RID choices diverge from the net8.0/test expectations (latent).
- **OpenSpec (AUDIT-067):** Spot-checked `hidden-window-journal` spec aligns with self-tests; full spec↔code drift not verified.

---

## 17. Dead / Stale / Suspicious Code

- **AUDIT-010:** `SplitPresentationController.ClassifyInteraction`, `ArmSettle`, `_settleGeneration` — never called / never read in production.
- **AUDIT-011:** `ContainerWindow.SuspendSplitPairForGuest` — never invoked (dead divergent duplicate).
- **AUDIT-046:** `GroupViewModel.PickColorCommand` — explicit `RelayCommand(_ => { })` no-op, no XAML binding.
- **AUDIT-059:** `DiagnosticEnvironmentService.RedactPath` — no-op wrapper over `SanitizeText`.
- **AUDIT-013 (stale state):** Controller `Foreground`/`ActiveGuest` stale after suspend (behavioral, not literal dead code).
- **AUDIT-036 (suspicious):** Final `return ResumeMember` in `SplitInteractionPolicy.Classify` is unreachable (exhaustive preceding branches).
- **`SHEPHERD[position]` comment (`:933-935`)** is incorrect about rescue restoring TabDock-controlled state (AUDIT-006).

---

## 18. Cross-Cutting / Systemic Issues

1. **"Test-only / parallel logic that isn't production" (root cause behind AUDIT-003, 010, 011, 014, 013, 015,
   064).** The split subsystem in particular has a heavily-tested pure policy that is *not* the runtime
   authority, a budget that is inert, dead wrapper APIs, and generation gating only on the settle path. The
   unit suite gives false assurance about split behavior.
2. **Decentralized identity model (root cause behind AUDIT-002, 008, 009, 053).** `Capture()` re-implements
   identity admission instead of using `WindowIdentityGate`; the gate is correct everywhere *except* this one
   divergent caller, which both over-rejects (title veto) and can be disabled by a stale journal.
3. **Privacy enforced by discipline + generic pass, not by a single invariant (AUDIT-021, 022, 023, 054, 057,
   058, 059).** Titles/arbitrary paths can reach diagnostic outputs; the self-test confirms only what it was
   seeded with.
4. **CI gates the *build* but not the *shipping runtime* or the *release trust boundary* (AUDIT-004, 005, 031,
   032, 034).** The single-file artifact, real-native rescue, and release policy/source co-revision are gated
   only by manual/supervised harnesses.
5. **Implicit lifecycle ordering (AUDIT-007, 038, 015, 019).** Several correctness properties depend on
   UI-thread serialization or single-turn gaps rather than explicit guards/generations.

---

## 19. Improvement Backlog

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
| -------- | -- | -------- | ------- | ------ | ------ | ---------- |
| 1 | AUDIT-003 | High | Pure split policy not production authority | False assurance on split behavior | M | High |
| 2 | AUDIT-002/008 | High | Capture title veto contradicts identity model | Valid captures refused | S | High |
| 3 | AUDIT-004 | High | CI exercises single-file artifact only via --version | "Builds but won't launch" ships | M | High |
| 4 | AUDIT-005/034 | High | Release trust boundary untested | Release integrity unverified | M | Medium |
| 5 | AUDIT-001 | High | Persistence invariants only in CLI self-tests | Critical guarantees unguarded in CI | M | High |
| 6 | AUDIT-006 | Medium | Rescue restores pre-capture geometry | Hard kill un-docks windows | M | High |
| 7 | AUDIT-031 | Medium | Real-native rescue not in CI | Recovery guarantee unverified in CI | L | High |
| 8 | AUDIT-009 | Medium | Stale journal permanently disables capture | Capture unusable all session | S | Medium |
| 9 | AUDIT-021/022/023 | Medium | Titles (PII) in on-disk log; privacy self-test false assurance | Local PII disclosure | M | High |
| 10 | AUDIT-016 | Medium | IconService blocks UI thread | UI hitch | S | High |
| 11 | AUDIT-019 | Medium | Picker can open during shutdown | Stray modal / blocked exit | S | High |
| 12 | AUDIT-017 | Medium | Delete request dropped when close prompt open | Lost destructive action | S | High |
| 13 | AUDIT-027 | Medium | Cross-process torn writes to shared files | Data loss on dual instance | M | Medium |
| 14 | AUDIT-028/029 | Medium | Schema-version/future-journal handling gaps | State downgrade/loss edge | S | Medium |
| 15 | AUDIT-010/011 | Medium | Dead split wrapper/suspend code | Maintenance hazard | S | High |
| 16 | AUDIT-013/015 | Medium | Stale controller Foreground / generation gating gap | Diagnostic/transient wrong state | M | High |
| 17 | AUDIT-014 | Medium | Operation budget inert in production | No production safety net | S | High |
| 18 | AUDIT-018 | Medium | Sync diagnostics zip export | UI freeze | S | Medium |
| 19 | AUDIT-020 | Medium | DisplayTabs untested | UI projection regressions | M | High |
| 20 | AUDIT-024/025 | Medium | Log lines lost on failure | Diagnostics blind spot | M | Medium |
| 21 | AUDIT-032 | Medium | ValidationDriver outside sln; scenarios never-run | Coverage gaps | S | High |
| 22 | AUDIT-033 | Medium | RC workflow unsupported signing option | Foot-gun | S | High |
| 23 | AUDIT-035 | Low | Release Mismatch leaves token | Confusing capture block | S | High |
| 24 | AUDIT-043/044 | Low | Hotkey non-reentrant; picker handlers leak | Latent/maintenance | S | High |
| 25 | AUDIT-049 | Low | ConsoleSession docs overstated | Doc mismatch | S | High |
| 26 | AUDIT-062/063 | Low | csproj version/RID choices | Latent build skew | S | Medium |

---

## 20. Recommended Remediation Order

1. **Critical correctness/test assurance (highest leverage):** AUDIT-003 (make the tested policy the authority),
   AUDIT-002/008 (route capture through the gate, drop title veto), AUDIT-001 (promote persistence/journal
   self-tests to xUnit).
2. **Shipping/release gates:** AUDIT-004 (validate single-file artifact in CI), AUDIT-005/034 (verify + test the
   release trust boundary), AUDIT-031 (real-native rescue integration test).
3. **Recovery correctness:** AUDIT-006 (refresh/neutralize journal on reposition), AUDIT-009 (don't let a stale
   journal disable capture), AUDIT-028/029 (schema/future-journal handling).
4. **Privacy:** AUDIT-021/022/023 (single sanitization layer at every sink + self-test asserting no titles/
   arbitrary paths), AUDIT-054/057/058.
5. **Concurrency/data-integrity:** AUDIT-027 (cross-process lock), AUDIT-007 (single ownership source),
   AUDIT-038 (document/guard reentrancy).
6. **UX correctness/robustness:** AUDIT-019/017/048 (modal-guard coordinator), AUDIT-016/018 (offload UI work),
   AUDIT-020 (DisplayTabs tests), AUDIT-041 (empty split submenu).
7. **Cleanup / low-risk:** AUDIT-010/011/046/059 (dead code), AUDIT-013/015 (generation/foreground
   consistency), AUDIT-043/044/062/063/065 (config/lifecycle hygiene), AUDIT-024/025 (diagnostic resilience).

> All items above are **recommendations only**. No remediation was performed by this audit.

---

## 21. Positive Findings

These are genuinely well-engineered aspects, supported by direct inspection — preserved so future work does
not casually rewrite them:

- **Two-tier window-identity gate (IDENT-009):** Every guest native mutation is preceded by the identity gate
  or `VerifyReleasedWindowCloseTarget`; no ungated guest-mutation path was found.
- **Fail-closed mutex DACL (IDENT-011):** Pre-existing/weaker/foreign `Global\TabDock-<SID>` objects are
  rejected, never weakened or replaced.
- **Interactive + identity-gated recovery (IDENT-005/008):** Supervised recovery requires a human `YES` plus a
  live strong-identity proof; no WM_CLOSE reaches an unverified/recycled window.
- **PII-free fingerprint (IDENT-007):** `EnvironmentFingerprint` emits only OS/runtime/monitor/DPI — no
  username/machine/domain.
- **Inert telemetry (DIAG positive):** `RuntimeTelemetry` defaults off, no network egress, no persistence.
- **Thread-safe, non-blocking logging (DIAG positive):** Lock-free `TryAdd` ring; `DiagnosticTrace` is
  lock-protected; `FileShare.ReadWrite|Delete` allows bundle generation while another instance holds the log.
- **Deterministic geometry (SPLIT positive):** `SplitGeometry.Partition` is DPI-agnostic by design,
  fuzz-covered (100k cases), and the single definition; the wedge guard prevents container-between-panes.
- **Atomic persistence (PERS positive):** `CommitJson` ordering (backup → write-through `.tmp` → atomic
  rename), quarantine-before-accept, and intra-process generation-latest-wins are sound.
- **High-quality unit tests (META positive):** Partition math, policy transitions, generation monotonicity,
  single-writer concurrency, budget counting are real assertions, not tautologies.
- **Well-engineered release pipeline (META positive):** Two-stage, policy/source/artifact isolation, signed-
  artifact hash triple-consistency, least-privilege job splits.
- **Robust lifecycle teardown (UI positive):** `ContainerWindow_Closed` systematically stops every timer,
  unsubscribes handlers, and detaches; `NameChanged` debounce is sound.

---

## 22. Audit Coverage / Confidence

**Deeply inspected:** the entire first-party application tree — `Services/*` (all ~40 files, with
`WindowShepherdService`, `GuestLifecycleService`, `GroupManager`, `WinEventMonitor`, `Split*`, `Persistence*`,
`Diagnostic*`, `Identity*` read in full or near-full), `Views/*` (including `ContainerWindow.xaml.cs` in full
for lifecycle/split/capture/prompt regions), `ViewModels/*`, `Models/*`, `Converters/*`,
`Infrastructure/NativeHwndHost.cs`, `NativeMethods.cs`; the test tree (`UnitTests` fully; `ValidationDriver`
sampled via `Program.cs`/scenario inventory/`CrashRescue`/`Torture`/`DeterministicSelfTests`); all five
csproj files; all PowerShell scripts; all `.github/workflows`; `AGENTS.md`, `README.md`, `KNOWN_ISSUES.md`,
`docs/ARCHITECTURE.md`, `docs/TESTING.md`, `docs/internal/*`; sampled `openspec/specs`.

**Moderately inspected:** `ContainerWindow.xaml.cs` split-transition geometry (logic read, not every
transition traced); `release-tooling-tests.ps1` (header/summary only, 2356 lines); remaining
`ValidationDriver` scenario shards (inferred from `docs/TESTING.md` inventory + `Program.cs` dispatch); the
full `openspec/` spec suite (one spec sampled).

**Could not fully validate (runtime/environment limitations):**
- Real-native crash rescue, single-file-bundle WPF startup, and the full DPI matrix require the Windows/WPF
  runtime and an interactive desktop; not executable in this headless, read-only environment.
- `dotnet test` / `ValidationDriver` execution was not performed by the audit (their *coverage* was audited
  statically). The build was attempted (§4); results appended when the background task completes.
- True disk-full / read-only-media mid-session and multi-instance concurrent-write behavior were reasoned
  about, not reproduced.

**Deserves a second specialized audit:** the `ValidationDriver` real-input harness end-to-end (it is the only
place real-native rescue and multi-window scenarios are exercised), and a full `openspec/specs` ↔
implementation drift diff.

---

## 23. Final Assessment

**Overall health:** Mature and disciplined for a native-interop desktop utility. The core safety premises
(identity gating, fail-closed lease, crash journal, deterministic geometry, inert telemetry) are genuinely
well-engineered and largely correct.

**Largest systemic risk:** the split subsystem's "test-only / parallel logic that isn't what runs"
(AUDIT-003 and its satellites) — it undermines the very self-tests that are supposed to guarantee split
behavior, and several of its safety nets (budget, generation gating, dead wrapper) are inert or missing.

**Strongest subsystem:** Identity/access-control and the persistence atomic-write path — both are sound,
fail-closed, and (for identity) applied consistently at every mutation site.

**Weakest subsystem:** The split presentation layer (correctness/test-assurance gap) and the diagnostics
*privacy* boundary (titles/arbitrary paths can reach outputs; self-test gives false assurance).

**Highest-value improvement:** Make `SplitPresentationPolicy` the production authority and route `Capture()`
through `WindowIdentityGate` (AUDIT-003/002/008) — two changes that remove the largest false-assurance and
over-rejection risks at once.

**Most urgent remediation:** Close the High-severity items (AUDIT-001..005) — promote persistence self-tests
to CI, centralize the identity model, validate the single-file artifact in CI, and verify/test the release
trust boundary.

**Remaining uncertainty:** Cross-runtime behaviors (real-native rescue, single-file launch, full DPI matrix)
and the full `openspec`↔code drift were not executable/fully diffed in this environment; they are flagged for
a follow-up specialized audit rather than asserted.

---

*End of report. Read-only audit; no code, test, configuration, or Git state was modified. Only
`kimi-results.md` was created.*
