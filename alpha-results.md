# Codebase Deep Audit — TabDock

> Deliverable note: the task directive designated `alpha-results.md` as the sole audit-report artifact for this run (the directive text internally references `CODEBASE_AUDIT.md` / `CODEBASE_AUDIT_v4.md`; the user's explicit instruction names `alpha-results.md`, which is used here). Prior audit artifacts already present in the working tree (`CODEBASE_AUDIT.md`, `CODEBASE_AUDIT_v3.md`, `*-results.md`) were deliberately **not** used as input, to avoid anchoring; every finding below rests on source read during this audit session.

## 1. Executive Summary

**Overall assessment.** TabDock is an unusually well-hardened WPF/.NET 8 window-docking utility whose core transaction design is sound: durable-journal-before-mutation capture/hide/release transactions, tiered HWND-identity gates revalidated at every native mutation boundary, generation-gated deferred positioning, a single-writer persistence gate, and a release chain with trusted-policy separation and exact-SHA pinning. The defects found are concentrated at the seams between individually correct components: posted/deferred callbacks acting on stale state, silent failure paths that callers cannot detect, duplicated state machines drifting apart, and test/validation infrastructure that reports more confidence than it delivers.

**Finding counts:** Critical **0**, High **3**, Medium **13**, Low **55**, Info/Opportunity **30** — **101 findings** after deduplication of cross-auditor symptoms into root causes.

**Major strengths**
- Capture/hide/release/rescue are journaled durably *before* any dangerous native mutation; crash recovery reconciles from disk with fail-closed semantics.
- HWND-recycling TOCTOU is mitigated seriously: per-capture property tokens plus PID/thread/class/exe/process-start identity tiers, re-checked before every mutation.
- P/Invoke discipline is high: rooted delegate lifetimes, correct handle ownership, `SetLastError` captured immediately, struct layouts locked by self-tests.
- Test estate breadth is real: 146 unit tests pass, ten CI-wired in-app self-test suites, a real-input ValidationDriver with identity-provenance guards, and a 139-case release-tooling regression suite gated in hosted CI.

**Major weaknesses / highest-risk areas**
- [AUDIT-001] Crash-recovery ledger retention can silently mark freshly quarantined evidence as already-resolved (byte-identical re-creation), losing recoverability with exit code 0.
- [AUDIT-002] A rapid tab-switch round-trip can release the re-focused active tab as a "guest-initiated hide", leaving the user's active window hidden and undocked.
- [AUDIT-003] The Stage B publish workflow uploads a release-evidence asset its job never materializes — the two-stage production release chain cannot complete as written.
- Systemic: eight ad-hoc staleness/generation mechanisms guard posted callbacks ([AUDIT-093]); five near-copies of durability/quarantine helpers and six identity ladders drift independently ([AUDIT-064], [AUDIT-065]); several safety mechanisms are production-dead while tests exercise only their test-only stubs ([AUDIT-011], [AUDIT-023]).

**Production readiness:** **conditionally ready** for local/single-user use; the automated release-publication path is broken as written ([AUDIT-003]), and three Medium CI/test-honesty defects ([AUDIT-012]–[AUDIT-014]) mean some recorded qualification evidence overstates coverage. No Critical findings; no exploitable security vulnerability was demonstrated.

**Overall confidence in the audit:** High for application code (all 42 Services files, all Views/ViewModels/Models, NativeMethods, App startup read in full by at least one auditor; the four largest files were each read end-to-end twice by independent auditors). Medium for CI/release-chain behavior (traced statically plus one live CI run inspected; workflows were not executed). Runtime behaviors marked Medium confidence depend on Win32 timing semantics reasoned from documentation rather than reproduced live.

---

## 2. Audit Scope

**Repository areas inspected**
- Application: `App.xaml.cs`, `NativeMethods.cs`, all 42 `Services/*.cs` (including every `*SelfTest.cs`), all `Models/`, `ViewModels/`, `Views/` (`.xaml` + code-behind + partials), `Converters/`, `Infrastructure/NativeHwndHost.cs`.
- Tests: `tests/UnitTests` (read + executed), `tests/Performance` (read; compile-only in CI), `tests/ValidationDriver` + `GuineaPig` (~15.8k lines, source inspection only).
- Build/CI/scripts: `TabDock.csproj`, `TabDock.sln`, `global.json`, `app.manifest`, `.gitignore`, all nine `scripts/*.ps1`, all four `.github/workflows/*.yml`, `openspec/config.yaml`.
- Docs/specs: `README.md`, `KNOWN_ISSUES.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `docs/runtime-stabilization-2026-08.md`, `docs/release/*.md`, all 12 `openspec/specs/*/spec.md`, `AGENTS.md`.
- Git history: full `--oneline` scan of all 136 commits plus targeted `-p`/`--stat` on hot spots.
- Experimental: `Spike/TabDock.Spike/Program.cs`.

**Languages/frameworks:** C# 12, .NET 8, WPF, Win32 P/Invoke (DWM, USER32, GDI, shell), PowerShell tooling, GitHub Actions.

**Applications/packages discovered:** main WPF app (`TabDock.csproj`); experimental Spike host; three test projects (UnitTests xUnit, Performance harness, ValidationDriver + GuineaPig pig app); OpenSpec CLI tooling under `tools/openspec` (Node).

**Validation commands executed** — see §4.

**Areas excluded and why**
- `bin/**`, `obj/**` contents: build output (though *tracked in Git* — flagged as [AUDIT-071]).
- `tools/openspec/node_modules/**`: third-party packages, lockfile-pinned.
- `openspec/changes/**`: change proposals/tasks, not canonical specs (spot-checked where specs cite them).
- `.agent/`, `.repowise/`: agent state/local index, not runtime code.
- Pre-existing audit transcripts (`*-results.md`, `CODEBASE_AUDIT*.md`): excluded as input per independence rule; their existence is itself flagged as hygiene debt ([AUDIT-094]).
- ValidationDriver/GuineaPig execution: injects real global input and spawns real windows — unsafe to run unattended; source-audited instead.

**Important limitations:** no interactive GUI session was driven (findings about UI races are traced, not reproduced); Windows-only environment assumptions were checked against docs/manifest, not against other OSes (out of scope by design); the 138-vs-139 release-case count could only be established statically ([AUDIT-061]).

---

## 3. System Architecture Model

**Purpose.** TabDock lets a user capture arbitrary top-level windows and dock them as tabs or split panes inside TabDock container windows. Core premise ("Shepherd" model): captured windows are **never reparented**; they remain independent top-level HWNDs that TabDock positions over the container and keeps z-ordered behind the container chrome.

**Principal components**
- `WindowShepherdService` (2,762 lines) — owns capture/hide/release/rescue transactions against guest HWNDs; durable hidden-window journal (`hidden-windows.json`, schema v3); min-track/DPI scaling; deferred `SetWindowPos` batching; identity gating.
- `PendingRecoveryService` (2,049 lines) — supervised (`--recover-pending`) crash-recovery state machine over quarantined pending journals; phased durable transactions; resolution ledger sidecars (`.recovered`).
- `PersistenceService` — single-writer, generation-gated `state.json` save/load with corrupt-file quarantine and backup rotation.
- `GroupManager` / `Group` / `CapturedWindow` models — group/tab membership, active-index maintenance, emergency release.
- `GuestLifecycleService` + `WinEventMonitor` — out-of-context WinEvent hook (`EVENT_OBJECT_HIDE/DESTROY/NAMECHANGE…`), posted (never sent) to the UI thread, with dispatch-time re-verification of membership/visibility.
- Split subsystem — `SplitPresentationController` (+ pure `SplitPresentationPolicy`), `PresentationLayoutCoordinator` (relayout coalescing), `PresentationOperationBudget`, `DeferredWindowPositionBatch`, `SplitGeometry`, `SplitInteractionPolicy`; UI wiring in `ContainerWindow.xaml.cs` + partial.
- Diagnostics — `DiagnosticCommandLine` (CLI verbs: doctor, support bundle, self-tests, recover-pending), `DiagnosticEnvironmentService`/`ReportService`/`Trace`, `LoggingService`, `EnvironmentFingerprint`, `ConsoleSession`, privacy self-test.
- Platform services — `MonitorDpiService` (PMv2 probe via throwaway helper window), `DpiCapturePolicy` (fail-closed admission of DPI-unaware guests), `IconService`, `HotkeyService`, `ProductMutationLease` (SID-scoped single-instance/mutation lease), `SessionEndingPolicy`.

**Data flow.** Input enters via (a) capture picker (`CapturePickerViewModel` EnumWindows filter chain → `WindowShepherdService.Capture`), (b) persisted `state.json` restore, (c) crash-journal rescue at startup, (d) CLI diagnostic verbs. State lives in `%APPDATA%\TabDock\{state.json, state.json.bak, hidden-windows.json, hidden-windows.json.pending*, *.recovered ledgers, TabDock.log}`. Output leaves via the UI, the journal/state files, log/diagnostic bundles.

**State ownership & concurrency model.** Essentially all mutable shepherd/group/UI state is confined to the UI thread (no locks in the shepherd; correctness rests on thread confinement). Cross-thread surfaces are narrow and deliberate: `PersistenceService.SaveAsync` (single-writer gate + generation token), background icon worker, WinEvent Post hop, telemetry counters (Interlocked). Deferred work (timers, `CompositionTarget.Rendering`, `Dispatcher.BeginInvoke`, posted WinEvents) is guarded by per-concern generation/reference-equality checks — currently eight distinct ad-hoc mechanisms ([AUDIT-093]).

**Major invariants (as designed)**
1. Journal entry durable before any hide/capture mutation survives a force-kill.
2. A native mutation on a guest happens only after identity revalidation (token property + identity ladder) at that boundary.
3. Only one process mutates product state (`ProductMutationLease`); diagnostic commands bypass user state via isolated temp roots.
4. Recovery never mutates pending evidence bytes; retirement deletes source only when all siblings are resolved.
5. Guests are released or rescued on every exit path (clean, crash, session-end, monitor failure).

These invariants were adversarially challenged; violations found are reported below (notably invariant 4's ledger-retention gap, [AUDIT-001], and invariant-adjacent hide-classification race, [AUDIT-002]).

---

## 4. Validation Results

```text
Command: dotnet build TabDock.sln -c Debug --nologo
Result:  Success — 0 errors, 11 warnings
Relevant observations: all 11 are CS8625 (null literal into non-nullable parameter)
in tests/UnitTests/ConverterTests.cs; contradicts .agent/STATE.md's recorded
"0 warnings" claim [AUDIT-054].
```

```text
Command: dotnet test tests/UnitTests
Result:  146 passed / 0 failed / 0 skipped (~1s)
Relevant observations: green, but several suites exercise fakes or test-only
production stubs rather than production paths [AUDIT-011], [AUDIT-051],
[AUDIT-053]; one timing-sensitive assertion can flake [AUDIT-052].
```

```text
Command: scripts/validate.ps1 (inspected, NOT executed)
Result:  n/a
Relevant observations: launches the built TabDock.exe, spawns recovery-smoke
processes, writes publish artifacts — requires interactive control; its
non-interactive core is exactly the build+test pair above. CI wiring verified:
build.yml → validate.ps1 -Ci -Publish runs solution+driver builds, dotnet test,
and the app's --selftest-geometry/-diagnostics/-native-abi smokes, so the ten
Services/*SelfTest classes ARE CI-wired, not dead.
```

```text
Command: git status --short (pre-audit baseline)
Result:  clean working tree on branch main @ 791f020
```

```text
Command: gh run view 32426544631 (hosted CI at current HEAD, read-only inspection)
Result:  release-tooling regression suite 138/138 PASS
Relevant observations: passing count masks 13 cases whose expected-artifact-name
binding is silently skipped due to a fused variable token [AUDIT-014].
```

ValidationDriver/GuineaPig and the interactive parts of validate.ps1 were intentionally not executed (real-input injection; see §2 exclusions).

---

## 5. Critical Findings

None. No finding demonstrates likely catastrophic data loss, severe security compromise, unrecoverable corruption, or violation of a core system guarantee. The three High findings below are the practical ceiling.

---

## 6. High-Severity Findings

### [AUDIT-001] Stale `.recovered` resolution markers silently resolve byte-identical re-created pending evidence, permanently blocking re-recovery

**Severity:** High
**Confidence:** Medium
**Category:** Correctness / Data-integrity / Reliability
**Affected areas:** `Services/PendingRecoveryService.cs` (retirement `:1690-1694`, exact-match resolution `:1423-1441`, `AlreadyResolved :1288`, auto-retire cleanup `:175-198`/`:205-217`, phase-filter divergence `:807-819` vs `:1456-1463`, ownership conflict `:838-841`); interacts with `Services/WindowShepherdService.cs:162,618` (`_nextCaptureIdentityToken` static counter)

**Summary.** Retirement deletes the quarantined pending file but deliberately keeps the `.recovered` sidecar ledger ("the sidecar ledger is the logical retirement state"). Resolutions are keyed by source-file SHA-256 + fingerprint + index — content, not identity. Because capture identity tokens restart at 0 every launch and HWND numeric reuse is routine, a second hide-crash cycle for the same still-running guest can serialize a byte-identical quarantine file. `FindResolution` then exact-matches the stale marker, classifies the fresh evidence `AlreadyResolved`, and the cleanup pass retires (deletes) it — reporting success.

**Evidence.**
```csharp
if (allResolved)
{
    File.Delete(entry.FullPath);          // sidecar kept forever
```
`MarkResolved` binds `SourceFileId + SourceFileSha256 + fingerprint + index`; nothing records that the source was deleted and later re-created. Additionally the lookup predicates disagree on retired transactions: `FindTransaction` excludes `Phase == Retired` while `PersistTransaction`'s exact-match includes them and then refuses with "a different durable recovery transaction already owns this entry" — so the variant where the prior cycle went through an interrupted transaction dies with a misleading ownership error until the user hand-deletes the sidecar.

**Failure scenario.** Session A: guest captured (token=1), hidden, TabDock hard-killed → `hidden-windows.json.pending` quarantined; user runs `--recover-pending`, recovery succeeds, sidecar remains. Session B (new process): user re-docks and re-hides the same running guest, hard kill again → byte-identical pending file. `--recover-pending` matches the stale marker → retires the new evidence → prints "no unresolved pending recovery entries were found", exit 0. The freshly hidden guest is never offered recovery and no journal remains anywhere.

**Impact.** Silent loss of recoverability with a clean exit code — the exact failure the recovery subsystem exists to prevent — plus a permanent refusal path requiring manual file deletion.

**Root cause.** Resolution identity is bound to mutable content (file bytes) rather than a per-quarantine unique id; retirement retains the matching key material indefinitely.

**Recommended direction.** Delete the `.recovered` sidecar atomically with (or immediately after) `File.Delete(entry.FullPath)` in `RetireEntry`, or bind resolutions/transactions to a GUID written by `PreservePendingJournal` instead of content hash alone; align `PersistTransaction`'s exact-match predicate with `FindTransaction`'s retired-phase exclusion.

**Verification recommendation.** Self-test: perform recovery cycle A, then force a byte-identical second quarantine (fixed fake API + fixed token seed), assert the second run offers recovery (does not retire silently); and a variant asserting a retired transaction does not block a fresh transaction persist.

---

### [AUDIT-002] Rapid tab-switch round-trip releases the re-focused active tab as a guest-initiated hide — window left hidden and undocked

**Severity:** High
**Confidence:** Medium
**Category:** Correctness / Race condition / State management
**Affected areas:** `Services/GuestLifecycleService.cs:96-103,127-132,142-144`; `Services/WinEventMonitor.cs:296-298,339-359`; `Views/ContainerWindow.xaml.cs:2178-2195`

**Summary.** `EVENT_OBJECT_HIDE` callbacks snapshot visibility at native-callback time, then rely on dispatch-time logic: "by the time this queued event is dispatched, any TabDock-initiated switch has already completed." That assumption breaks when the switch round-trips: Ctrl+Tab twice in fast succession hides A (transition 1), then completes B→A (transition 2) *before* the queued hide event for A dispatches. At dispatch, A is captured, is the active tab again, callback-time visibility was `false` (so the transient-hide recheck is deliberately skipped), no container-hide expectation exists — execution reaches `RemoveDeadMember(..., show: false)` → `ReleaseIntentionalHide`: token removed, journal cleared, tab removed, window left hidden with no taskbar presence.

**Evidence.**
```csharp
// WinEventMonitor.OnWinEvent — snapshot at native callback time
bool? visibleAtCallback = eventType == EVENT_OBJECT_HIDE ? IsWindowVisible(hwnd) : null;
// GuestLifecycleService.OnWindowHidden
if (!wasHiddenAtCallback && IsWindowVisible(hwnd)) return; // transient-hide guard skipped
```
The dispatch-time membership re-verification passes because A is legitimately captured and active again.

**Failure scenario.** Guest A active, B inactive; key-repeat or two queued clicks produce back-to-back switches; WinEvents are documented as potentially delayed/coalesced relative to input. The user's active window silently vanishes from dock and screen; recovery requires the guest's own tray icon or manual intervention.

**Impact.** Direct user-visible loss of a docked application — the highest-frequency interaction (tab switching) has a realistic interleaving that destroys state the Shepherd contract promises to own.

**Root cause.** Hide classification trusts callback-time visibility alone; there is no timestamp correlation of WinEvent fire time against TabDock-initiated show/hide transitions for the general case (the minimize path has such an expectation mechanism; hides do not).

**Recommended direction.** Record a per-HWND timestamp of every TabDock-initiated hide/show and reject any hide event whose event time precedes the last TabDock-initiated show of that HWND, generalizing the existing `ContainerHideExpectation` pattern.

**Verification recommendation.** Deterministic test injecting a queued hide event with a stale event timestamp after a completed B→A switch; ValidationDriver scenario hammering Ctrl+Tab with key repeat while asserting member count stability.

---

### [AUDIT-003] Publish workflow uploads a release-evidence asset its job never materializes — two-stage release chain cannot complete

**Severity:** High
**Confidence:** High
**Category:** Build/CI / Release integrity
**Affected areas:** `.github/workflows/publish-release.yml:583-587` (asset list), `:294-299` (evidence written only in verify job), `:446-452` (handoff artifact contains only verification record + notes), `:504-511` (publish job re-downloads pristine Stage A artifact), `:597` (post-publish asset check also requires it); `docs/release/publication-gates.md:502`

**Summary.** The publish job's `gh release create` includes `candidate-artifact/release-external-evidence.json`, but that file is created only in the *verify* job (which writes `$env:RELEASE_EXTERNAL_EVIDENCE` into its own scratch copy). It is not part of the Stage A artifact (which contains only `TabDock.exe`, `TabDock.pdb`, `release-manifest.json`, `SHA256SUMS.txt`) and not in the `verified-handoff` artifact. The publish job re-downloads the original Stage A artifact fresh, so the referenced path does not exist. Verified directly in this audit by reading the workflow.

**Evidence.**
```yaml
gh release create $tag ... `
  candidate-artifact/TabDock.exe `
  candidate-artifact/SHA256SUMS.txt `
  candidate-artifact/release-manifest.json `
  candidate-artifact/release-external-evidence.json `   # absent in THIS job
  verified-handoff/publication-verification.json
```
The workflow's own "Verify release assets" step (`:597`) lists the same name as required, contradicting the flow it just ran; `publication-gates.md:502` documents the intended attachment.

**Failure scenario.** Operator completes every human gate and dispatches publish-release. `gh release create` fails on the missing path — depending on gh version either nothing is created (pipeline always red) or the tag `v<version>` is created first and asset upload fails midway, leaving a partial public release without its evidence/provenance record and a stale tag that makes corrected re-runs fail until manually deleted.

**Impact.** The documented production publication path is unusable as written; worst case publishes a partial release lacking its provenance evidence.

**Root cause.** Evidence content is passed as a workflow input but only reified in the wrong job; artifact plumbing was never exercised end-to-end (Stage B requires manual gates, so it rarely runs).

**Recommended direction.** Include `release-external-evidence.json` in the verified-handoff artifact, or rewrite it from the `external-evidence` input inside the publish job before `gh release create`.

**Verification recommendation.** Dry-run the Stage A→verify→publish chain against a scratch repo (the existing release-tooling scratch-repo pattern) asserting the published asset set equals the required list.

---

## 7. Medium-Severity Findings

### [AUDIT-004] `LogPositioningFailureOnce` violates its stated once-only invariant; recycled-HWND suppression is never cleared

**Severity:** Medium
**Confidence:** High
**Category:** Performance / Observability / Correctness
**Affected areas:** `Services/WindowShepherdService.cs:165-176` (comment), `:205-212` (implementation), suppression sets `_positioningFailuresLogged`/`_identityFailuresLogged`

**Summary.** The comment claims the hot drag path pays "one integer comparison per tick" on repeat failures (PERF25-3). In reality every repeat failure pays `FormatLastError()` (marshal + `Win32Exception` construction), a dictionary allocation, a timestamp format, and a locked write into the fixed 1024-entry `DiagnosticTrace` ring *before* the suppression check. Both suppression sets are keyed by raw HWND value and never cleared on `UnregisterCapturedIdentity`, so a recycled HWND's new window inherits the old window's silence.

**Evidence.** `string error = NativeMethods.FormatLastError(); if (_positioningFailuresLogged.Add(hwnd.ToInt64())) _log.Log(...); DiagnosticRuntime.Record("repair.native-failure", ...)` — the expensive work precedes the dedupe check and the trace record fires on every failure regardless.

**Failure scenario.** A guest persistently refuses z-order pins (e.g., UIPI-blocked after elevating mid-capture): every foreground/reorder WinEvent repair floods `repair.native-failure` records, evicting unrelated diagnostics from the ring precisely during the episode the trace exists for; a later recycled HWND exhibiting a different failure is never logged at all.

**Impact.** Diagnostic blindness during incidents plus avoidable hot-path cost; contradicted in-code invariant misleads maintainers.

**Root cause.** Suppression applied after cost; lifetime of suppression keys decoupled from window lifetime.

**Recommended direction.** Move `FormatLastError`/`Record` inside the `Add(...)` success branch (record a cheap suppressed counter otherwise); clear both sets' entries in `UnregisterCapturedIdentity`.

**Verification recommendation.** Unit test asserting repeat failures produce exactly one log line and zero trace records after the first; identity-unregister test asserting a fresh capture of a reused HWND logs anew.

---

### [AUDIT-005] `WM_GETMINMAXINFO` hook permanently disables the XAML-declared `MinWidth`/`MinHeight`; containers can shrink far below the declared floor

**Severity:** Medium
**Confidence:** High
**Category:** Correctness / UI
**Affected areas:** `Views/ContainerWindow.xaml.cs:551-586` (WndProc branch), `Views/ContainerWindow.xaml:12-13` (`MinWidth="320" MinHeight="240"`); corroborated independently by the P/Invoke auditor

**Summary.** The hook sets `handled = true` unconditionally whenever monitor info is available, short-circuiting WPF's built-in `WM_GETMINMAXINFO` processing — the only place `MinWidth`/`MinHeight` are enforced. When `ComputeContainerMinTrack` cannot produce a constraint (empty group, or guest min-probe failed with no cache), the min track stays at USER32 defaults (~112×27 px), far below the declared 320×240 DIP floor. When it succeeds, the floor becomes the guest's minimum plus chrome delta — also potentially below 320.

**Evidence.**
```csharp
if (ComputeContainerMinTrack(out var w, out var h) && w > 0 && h > 0)
    { mmi.ptMinTrackSize.x = w; mmi.ptMinTrackSize.y = h; }
Marshal.StructureToPtr(mmi, lParam, true);
handled = true;
```
The in-handler comment also misstates ordering ("WPF's internal handler may have already written a large default") — at hook time the buffer holds USER32 defaults.

**Failure scenario.** Empty group container (or guest min-probe timeout): drag the resize border → container shrinks to ~112 px wide; caption buttons clip and become unusable until manually re-widened.

**Impact.** The declared XAML contract is not enforced; chrome becomes unusable at small sizes in reachable states.

**Root cause.** Hook claims the message without folding the app-declared minimum into its computed constraint or falling back to WPF-derived values.

**Recommended direction.** Clamp to `Math.Max(computedMin, DPI-converted MinWidth/MinHeight)` before writing `ptMinTrackSize`, or fall back to WPF-derived values instead of leaving USER32 defaults; fix the comment.

**Verification recommendation.** Unit test on a extracted min-track predicate covering empty-group/probe-failure cases; ValidationDriver resize scenario asserting width ≥ 320 DIP equivalent.

---

### [AUDIT-006] `DefinePair` fails silently on `RecoveryPending`; `EnterSplit` cannot detect the failure and desyncs controller state from the strip

**Severity:** Medium
**Confidence:** High
**Category:** Correctness / State management
**Affected areas:** `Services/SplitPresentationController.cs:58-87` (void return; early return at `:74`); `Views/ContainerWindow.xaml.cs:2816-2841` (`EnterSplit` ignores outcome); independently found via git-history analysis

**Summary.** Every sibling transition (`SuspendForGuest`, `ResumeMember`, `ExplicitExit`) returns `bool` and callers branch on it; `DefinePair` returns `void`. When hiding a departing member hits `RecoveryPending` mid replace-pair, it bails with the OLD pair still `_presented`, while `EnterSplit` blindly continues: strip shows composite `[C|D]` selected, `_shepherdActiveWindow = C`, but `LayoutSplitPanes()` reads the controller's still-authoritative `A|B` and re-presents the old pair over the panes.

**Evidence.**
```csharp
WindowHideOutcome o = _ops.Hide(m);
if (o == WindowHideOutcome.RecoveryPending) return;   // old pair intact
...
// EnterSplit:
_splitController.DefinePair(left, right, left);
...
_viewModel.SetSplitComposite(leftTab, rightTab);
LayoutSplitPanes();
```

**Failure scenario.** Pair `A|B` presented plus third tab C; right-click C → Split screen → pick A (legal path). Hide(B) hits `RecoveryPending` (journal commit failure — exactly the condition this codebase otherwise fail-closes on). Strip promises `[C|A]` while `A|B` remain on screen; the composite's C half is dead (`FocusSplitMember` rejects non-members). Divergence persists until an unrelated transition resets split state.

**Impact.** UI/runtime authority disagreement in a reachable, user-initiated flow; silent because no error surface exists.

**Root cause.** Missing failure signal on one transition of an otherwise consistently fail-closed API family.

**Recommended direction.** Make `DefinePair` return success/failure (matching siblings) and have `EnterSplit` bail out — restoring prior presentation and active tab — when the replace transaction does not complete.

**Verification recommendation.** Unit test with failing-hide ops asserting controller state unchanged and caller-visible failure; extend the deterministic S-series accordingly.

---

### [AUDIT-007] Failed suspension can leave a "presented" pair with both members hidden and no scheduled repair

**Severity:** Medium
**Confidence:** Medium
**Category:** Correctness / Failure handling
**Affected areas:** `Services/SplitPresentationController.cs:99-113` (`SuspendForGuest` post-hide revalidation); repair paths `Views/ContainerWindow.xaml.cs:2861-2865`, `Views/ContainerWindow.SplitInteractionFix.cs:173-180`; guards `Views/ContainerWindow.xaml.cs:2687-2705`, `:479`

**Summary.** `SuspendForGuest` hides both members, *then* re-validates the guest, returning `false` with both members hidden while `_presented` stays true. Both callers "repair" by calling `LayoutSplitPanes()`, but that method early-returns on `_guestMoveSizeActive`, minimized state, `IsContainerChromeInteractionActive()`, or a zero content rect. The WM_ACTIVATE reassert timer cannot rescue this either — it requires `IsWindowVisible(activeWindow)`, false for the hidden members.

**Evidence.** `if (_isCurrent != null && !_isCurrent(guest)) return false;` placed after both hides; repair path routed through a conditionally-no-op relayout.

**Failure scenario.** User clicks a third tab; guest C dies/recycles between the identity gate and the post-hide recheck; chrome popup happens to be active (or container momentarily minimized) so the repair no-ops. Both panes stay blank with the pair marked presented indefinitely — nothing queues a relayout because nothing moved.

**Impact.** Persistent blank split presentation requiring user intervention; low probability per attempt but no self-heal once entered.

**Root cause.** Repair delegated to a path with cosmetic early-outs instead of an unconditional re-present primitive.

**Recommended direction.** On post-hide failure inside `SuspendForGuest`, restore visibility inline before returning false, or route repair through a dedicated unconditional re-present method that bypasses cosmetic guards.

**Verification recommendation.** Unit test: suspend with guest dying mid-operation + relayout suppressed → assert members re-shown or presentation flag cleared.

---

### [AUDIT-008] Interrupted recovery transaction whose target HWND is verifiably gone is an unrecoverable dead-end

**Severity:** Medium
**Confidence:** High
**Category:** Reliability / Recovery
**Affected areas:** `Services/PendingRecoveryService.cs:167-173` (cleanup filter requires `IsNativeRecoveryComplete`), `:229-237`, `:307-311` (hard hwnd equality), `:344-347`, `:561-572`; spec `openspec/specs/hidden-window-journal/spec.md:117-134`

**Summary.** Disk-only reconciliation handles destroyed targets only *after* the durable `NativeRecoveryComplete` marker; for earlier phases the only execution route requires a live candidate whose HWND equals the recorded one. There is no abandon/discard verb anywhere in `RunInteractive`. If the guest exits before the next supervised run, every subsequent `--recover-pending` lists the entry forever, enumerates zero candidates, exits 2 — the evidence and ledger transaction can never be retired by the product.

**Evidence.** `if (transaction == null || !IsNativeRecoveryComplete(transaction.Phase)) { error = "native recovery is not durably complete"; return false; }` plus `entry.Entry.Hwnd != candidate.Hwnd.ToInt64() → false`; grep confirms no retirement path for this state.

**Failure scenario.** Recovery installs the token and crashes at `after-setprop`; guest app is then uninstalled/crashes. Every future supervised run dead-ends on this entry until the user hand-deletes `%APPDATA%\TabDock\hidden-windows.json.pending(.recovered)`.

**Impact.** Operational trap: permanent exit-2 noise and manual remediation; native residue is zero (property died with the HWND), so it is pure bookkeeping retention.

**Root cause.** State machine lacks a supervised retirement transition for "target verifiably gone."

**Recommended direction.** Add an explicit YES-gated "target verifiably gone — retire evidence" option for interrupted transactions whose recorded HWND is absent and whose process-start/pid identity cannot return.

**Verification recommendation.** Self-test: interrupted transaction + destroyed-HWND probe → supervised retire succeeds and ledger compacts.

---

### [AUDIT-009] Recovery self-tests never exercise the current v3 journal schema — thread identity completely untested

**Severity:** Medium
**Confidence:** High
**Category:** Test gaps
**Affected areas:** `Services/PendingRecoverySelfTest.cs:1458-1471` (`JournalJson` called only with null/2/99), `:1481-1506` (`EntryV2` omits `WindowThreadId`); gate `Services/PendingRecoveryService.cs` (`HasThread = Has(...) && version >= CurrentVersion`), `CurrentVersion = 3` at `Models/PersistedState.cs:84`

**Summary.** Every self-test fixture is v1, v2, or future-version; none writes `WindowThreadId`, so `HasThread` is false in every test ever run and none of the five thread-comparison guards (`MatchesHistoricalEvidence`, `EvaluateRecoveryTarget`, `EvaluateRecoveryGeneration`, `ClassifyCompletedTarget`, `NativeRecoveryStatusProbe`) is exercised — yet v3 pending evidence is what the shipping shepherd actually quarantines.

**Evidence.** Fixture inventory vs the version gate quoted above; production writer confirmed at `WindowShepherdService.PreservePendingJournal` ("incomplete v3 evidence").

**Failure scenario.** A regression dropping the thread check from `MatchesHistoricalEvidence` (weakening the identity contract for exactly the schema version in production) passes the entire 33-check self-test suite.

**Impact.** False confidence in the recovery identity contract; the suite cannot catch its most plausible weakening regressions.

**Root cause.** Fixtures predate the v3 schema addition and were never extended.

**Recommended direction.** Add v3 fixtures (including `WindowThreadId`) covering discovery, matching, mismatch rejection, and completed-target reconciliation; include one negative test where only the thread id differs.

**Verification recommendation.** Mutation test: remove the thread comparison → suite must fail.

---

### [AUDIT-010] Per-`Write` AttachConsole/FreeConsole around lazily-cached `Console.Out` can silently drop all output after the first write

**Severity:** Medium
**Confidence:** Medium
**Category:** Reliability / Diagnostics
**Affected areas:** `Services/DiagnosticCommandLine.cs:171-194` (`Write`); multi-write sites `:122-124` (`--selftest-diagnostics`), `:129-131` (`--selftest-native-abi`); invocation shape `scripts/validate.ps1:76`, requirement `scripts/release-tooling.ps1:463`

**Summary.** `Console.Out` is initialized once per process wrapping the std handle captured during the *first* attach; .NET never refreshes it after `FreeConsole()`/re-attach. For a WinExe run attached-but-not-redirected (plain cmd window, or `Start-Process -NoNewWindow` with no redirection as validate.ps1 does), the second `Write` goes through the stale handle; failures are swallowed by `catch (IOException)` into `Debug.WriteLine`. Exit codes still reflect verdicts, so the loss is invisible.

**Evidence.** attach → `Console.WriteLine(text)` → finally `FreeConsole()` per call; two-call selftest verbs emit the environment report then the verdict line.

**Failure scenario.** Qualification evidence recording: `TabDock.exe --selftest-native-abi` in a normal cmd window prints line 1; the `SELFTEST[native-abi] ... result=PASS/FAIL` verdict line is silently lost — recorded evidence omits the verdict, or a FAIL looks like "nothing ran".

**Impact.** Corrupted release-qualification evidence trail; misleading CLI behavior in exactly the flows the release docs mandate.

**Root cause.** Console lifetime managed per-write instead of per-command-run, colliding with .NET's lazy singleton console writers.

**Recommended direction.** Attach once per command run, or `Console.SetOut(new StreamWriter(Console.OpenStandardOutput()))` after each attach.

**Verification recommendation.** Integration check invoking a two-write verb attached-but-unredirected and asserting both lines appear.

---

### [AUDIT-011] Budget/coalescing unit tests largely exercise fakes and a test-only production stub — tautological coverage

**Severity:** Medium
**Confidence:** High
**Category:** Test quality
**Affected areas:** `tests/UnitTests/PresentationOperationBudgetTests.cs:61-116,279-321`; stub `Services/PresentationLayoutCoordinator.CoalesceAndExecute` `:95-103`

**Summary.** `CoalesceAndExecute` exists only for tests, ignores its `coalescedRequests` argument, and unconditionally executes once; the `CoalescedRelayout_*` tests therefore assert that a method which always executes once executes once. The "ordinary switch" budget tests hand-call `ops.Hide/PositionAndShow/SetForeground` on a fake and assert the counter counted them — no production code runs. Real coalescing (`RequestRelayout` + `CompositionTarget.Rendering` in ContainerWindow) is covered nowhere headlessly.

**Evidence.** Stub body quoted in finding; test names vs bodies compared.

**Failure scenario.** Someone breaks real coalescing (e.g., re-arms Rendering per trigger) — every `CoalescedRelayout_*` test stays green.

**Impact.** Green suite masks regression in the actual frame-coalescing behavior the tests are named after.

**Root cause.** Production scheduling is UI-bound; a seam was added for tests but the tests assert the seam, not the scheduler.

**Recommended direction.** Drive the budget through `RequestRelayout` with a fake scheduler (as `HardeningRegressionTests` already does); delete `CoalesceAndExecute`; route ordinary-switch cases through a container-level seam.

**Verification recommendation.** Mutation test: break `RequestRelayout` coalescing → suite must fail.

---

### [AUDIT-012] ValidationDriver DPI-scenario self-skip is recorded as PASS in result artifacts (false green)

**Severity:** Medium
**Confidence:** High
**Category:** Test honesty / CI
**Affected areas:** `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Dpi.cs:131-137,154-158`; contrast correct pattern `Scenarios.Browser.cs:406`; writer `QualificationResultWriter.WriteScenario`

**Summary.** `PrepareDpiScenario` logs "SKIPPED … no non-100% monitor available" and returns null; the caller returns without setting `ctx.Status`, so results emit `result = "PASS"` with `failures=0 skipped=0`, and `RunScenario` counts it passed. The browser fixture correctly calls `ctx.Skip(...)`, producing a skipped marker.

**Evidence.** Skip-by-log-and-null vs `ctx.Skip` paths compared; JUnit emission traced.

**Failure scenario.** On a single-100%-monitor machine (typical CI/dev), `capture-dpi-unaware-guest`/`capture-dpi-system-guest` produce PASS artifacts; aggregate reports claim mixed-DPI acceptance coverage that never ran — the precise "false green" the scenario doc promises to avoid.

**Impact.** Mixed-DPI qualification evidence systematically overstated on the machines most likely to run automation.

**Root cause.** Early-return skip path predates the `ctx.Skip` mechanism.

**Recommended direction.** Call `ctx.Skip(reason)` before returning null, mirroring the browser fixture.

**Verification recommendation.** Run the shard on a single-monitor machine; assert artifacts show SKIP, not PASS.

---

### [AUDIT-013] Workflow-dispatch inputs interpolated directly into inline PowerShell — injection into privileged release jobs

**Severity:** Medium
**Confidence:** High
**Category:** Security / CI supply-chain
**Affected areas:** `.github/workflows/prepare-release-candidate.yml:132,290`; `release.yml:74,124`; `publish-release.yml:169,203,312-316,529`

**Summary.** `run: .\scripts\release-qualify.ps1 -Ci -Sha '${{ inputs.sha }}' -Version '${{ inputs.version }}' ...` splices free-form input text into the script body before any validation runs (numeric/hex checks happen *after* the injection point). The same workflows carefully pass `github.*` contexts through `env:` — `inputs.*` bypasses that indirection.

**Evidence.** Quoting-breakout example: version `1.0.0'; Write-Host $env:SM_API_KEY; '`.

**Failure scenario.** Anyone with `workflow_dispatch` rights — including a compromised maintainer token, exactly the insider threat model the repo's trust-boundary docs target — dispatches prepare-release-candidate with a crafted version; arbitrary commands execute in the Stage A job holding `SM_API_KEY`, `SM_CLIENT_CERT_PASSWORD`, and the materialized client certificate, with no commit trace.

**Impact.** Privilege escalation from dispatch-input to secret-bearing job execution; theoretical today (requires dispatch rights) but inconsistent with the repo's own hardening pattern.

**Root cause.** Inputs never routed through the env-indirection pattern used elsewhere.

**Recommended direction.** Map every `inputs.*` through step-level `env:` and read `$env:...` in scripts.

**Verification recommendation.** Add a workflow-lint check rejecting `${{ inputs.` inside `run:` blocks; penetration-style dispatch with a benign-but-breaking payload in a fork.

---

### [AUDIT-014] Fused variable token silently disables artifact-name binding assertions in 13 release regression tests

**Severity:** Medium
**Confidence:** High
**Category:** Test honesty / Release assurance
**Affected areas:** `scripts/release-tooling-tests.ps1:925,927,1084,1094,1103,1113,1122,1132,1142,1149,1161,1171,1181`; target guard `scripts/release-tooling.ps1:396`

**Summary.** `-ExpectedCandidateArtifactName $goodArtifactNamegoodPublisherSubject` fuses two defined variables into one undefined identifier, which evaluates to `$null` (no `Set-StrictMode`), binding an empty string. The target function skips its binding check when the expected name is null/whitespace — so the candidateArtifactName binding assertion is silently skipped in every direct-evidence test case, including ones named "…binds-sha-and-hash". All tests PASS (CI 138/138), hiding the gap.

**Evidence.** Variables defined at `:689`/`:692`; 13 fused call sites enumerated; guard clause quoted.

**Failure scenario.** A future regression breaking evidence→artifact-name binding alone passes the hosted-CI gate; the suite's documented guarantee is half-exercised at the direct-evidence layer (one gate-level test still covers it once).

**Impact.** Release-chain binding assurance weaker than documented and than CI output suggests.

**Root cause.** Typo + absence of strict mode.

**Recommended direction.** Replace the fused token with `$goodArtifactName` at all 13 sites; add `Set-StrictMode -Version Latest` to the harness so undefined-variable typos fail loudly.

**Verification recommendation.** After fix, mutation-test the binding logic → suite must fail; enable strict mode in CI.

---

### [AUDIT-015] Delete-group confirmation lacks the z-order raise treatment the close-group prompt got — modal can open beneath the docked guest

**Severity:** Medium
**Confidence:** High
**Category:** Correctness / UX
**Affected areas:** `Views/ContainerWindow.xaml.cs:341-390` (`ViewModel_DeleteGroupRequested`) vs `:820-846,947-1005` (`ContainerWindow_Closing`, `ArmClosePromptRaise`, `FindOwnedClosePrompt`), rationale comment `:1539-1544`

**Summary.** The ×-button close prompt raises the container into the topmost band, arms a 50 ms raise timer, and lifts the dialog itself — because "the guests are independent top-level windows and can already sit above the owner." The delete-group prompt does none of this: plain `MessageBox.Show(this, ...)`, with `_closePromptOpen` suppressing the 120 ms reassert and pairing repairs but nothing lowering the guest or raising the dialog.

**Evidence.** Side-by-side of the two modal sequences; the fixed-for-close-prompt comment describing the identical hazard class "observed live".

**Failure scenario.** Single-tab group with maximized guest covering the container; group menu → Delete group → confirmation renders behind the guest's opaque client area; app appears frozen until the user clicks elsewhere.

**Impact.** Reachable, user-initiated freeze impression; the same defect was already found and fixed for the sibling modal, so recurrence risk is demonstrated.

**Root cause.** Modal-raising helper exists but was never shared.

**Recommended direction.** Extract the close-prompt raise sequence (raise-container + `ArmClosePromptRaise` + owner-verified dialog lift) into one helper used by both modals.

**Verification recommendation.** ValidationDriver scenario: maximize guest, trigger delete-group, assert confirmation HWND is above the guest (or screenshot-based pixel check).

---

### [AUDIT-016] TESTING.md cites `SHEPHERD[dragout]` as "verified against committed application source" — no committed source emits it

**Severity:** Medium
**Confidence:** High
**Category:** Documentation divergence / Test validity
**Affected areas:** `docs/TESTING.md:354-361`; contrast `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Drag.cs:325` (legacy-assertion removal note); `KNOWN_ISSUES.md` historical mention

**Summary.** TESTING.md asserts every listed log-substring example was verified against committed source; repo-wide grep finds no emitter of `SHEPHERD[dragout]` — only scenario names and a comment explaining the legacy assertion was removed. The doc's verification claim is itself false for this item.

**Evidence.** Grep results quoted above; the project's own §D rule ("Assertions only reference instrumentation present in committed source") exists to prevent exactly this.

**Failure scenario.** The next harness author treats the example list as the verified whitelist, adds `WaitForLogLine(…, "SHEPHERD[dragout]")`, and produces a scenario that can only time out.

**Impact.** Vacuous/unpassable assertion trap; erosion of trust in the doc's central guarantee.

**Root cause.** Doc updated for the assertion removal without pruning the example list.

**Recommended direction.** Remove `SHEPHERD[dragout]` from the list (replace with an actually-emitted line such as `SHEPHERD[re-glue]`) and re-run the §D verification for remaining examples.

**Verification recommendation.** Scripted check: extract all log-substring examples from TESTING.md and grep each against emitters (this is also a good permanent CI lint).

---

## 8. Low-Severity Findings

Format per finding: severity/confidence, location, evidence, failure scenario, direction. All were challenge-checked against callers/guards before inclusion.

### [AUDIT-017] Managed batch-validation failure reported through the native last-error formatter; burns the once-per-HWND log slot
Low / High — `Services/WindowShepherdService.cs:1204-1213` + `:205-212`.
`ValidationFailed` means the *managed* generation gate refused (`DeferredWindowPositionBatch.Apply`), yet it is logged via `LogPositioningFailureOnce`, which reports stale native last-error ("No error") and inserts the HWND into `_positioningFailuresLogged` — permanently suppressing the log of a subsequent *genuine* native positioning failure for that guest.
Scenario: split guest goes stale mid-batch → validation refusal logs "Win32 0"; the real `SetWindowPos` failure seconds later is never logged.
Direction: log validation refusals with a dedicated message, separate from the native-failure suppression set.

### [AUDIT-018] `Hide()` mismatch path leaves shepherd binding and min-track cache for a positively-stale window (asymmetric with `Release`)
Low / High — `Services/WindowShepherdService.cs:1404-1414` vs `:1595-1610`.
`Hide` on `Mismatch` does only `JournalClear`; `Release` on the same result also unbinds `_capturedByHwnd` and removes `_minTrackCache`. Until the destroy WinEvent converges cleanup, a recycled HWND reusing that value is refused capture ("already bound"), and the stale `CapturedWindow` stays rooted.
Direction: mirror Release's mismatch handling in Hide, or document the deferral as intended.

### [AUDIT-019] Container's `Tabs.CollectionChanged`/`DeleteGroupRequested` handlers are never unsubscribed; teardown comment claims `Detach()` covers them
Low / High — `Views/ContainerWindow.xaml.cs:319,325,1020-1027`; `ViewModels/GroupViewModel.cs:489-498`. Independently found by two auditors.
`GroupViewModel.Detach()` removes only *its own* same-named handler; the window's handler (which routes tab removals into split teardown) stays attached forever, and the "do not unsubscribe here — double-unsubscribe" comment is factually wrong. Today the cycle is collected as an unreachable island after close; latent trap if a VM ever outlives its container (the scenario Detach's own doc anticipates).
Direction: explicitly unsubscribe both in `ContainerWindow_Closed` and correct the comment.

### [AUDIT-020] `_trackedTabContextMenus` never removes closed composite menus — unbounded per-right-click growth
Low / High — `Views/ContainerWindow.xaml.cs:1388-1389,1418-1419,1427-1433,1068-1070`.
Each right-click builds a new `ContextMenu` added to the tracking set; `TabContextMenu_Closed` never removes it; only window close clears the set. Every retained menu roots its `MenuItem.Command` → `TabViewModel` → `CapturedWindow` plus a visual subtree.
Direction: remove the sender from the set inside `TabContextMenu_Closed`.

### [AUDIT-021] Ctrl+Tab while a split pair is dormant can select the wrong strip item via misaligned `DisplayTabs` indexing
Low / Medium — `Views/ContainerWindow.xaml.cs:800-817`; projection source `ViewModels/GroupViewModel.cs:189-208`.
`Tabs[next]` index is reused against `DisplayTabs` which contains a composite item when dormant. If `ResumeSplitPair` bails on `RecoveryPending` and reverts ActiveTab, `SelectedIndex = next` selects the composite/off-by-one item; selection strip and active guest disagree with silent binding errors.
Direction: derive strip selection from ActiveTab's projected position instead of reusing the Tabs index.

### [AUDIT-022] Dead, drifted duplicate of the split-suspend transaction plus orphaned `ExplicitExit`
Low / High — `Views/ContainerWindow.xaml.cs:2849-2915` (`SuspendSplitPairForGuest`, zero callers), `:2973-3061` (`ExitSplit` re-implements exit inline); `Services/SplitPresentationController.ExplicitExit:134-173` (test-only); live twin `ContainerWindow.SplitInteractionFix.cs:149`. Also found via history analysis (the 3886e79→0df4f26 exited-divergence bug family).
Two ~60-line near-copies of the pair→single transaction must be kept in sync by comments only and have already drifted materially (different recovery handling, extra SetForeground).
Direction: delete the dead twin; route `ExitSplit` through `controller.ExplicitExit` or delete that too.

### [AUDIT-023] `PresentationLayoutCoordinator` staleness machinery is production-dead; duplicate refusal map with different semantics; discarded generation token
Low / High — `Services/PresentationLayoutCoordinator.cs:33,56-61,68-83,90,105-106,112-117,119-126`; live counterparts `Views/ContainerWindow.xaml.cs:88,2344-2367,2653-2672`; only `InvalidateLayout` caller is `tests/UnitTests/HardeningRegressionTests.cs:31,46`. Found independently by four auditors.
`_layoutGeneration` never changes in production so the stale-discard gate cannot fire; `long gen = ++_pendingLayoutGeneration; ... _ = gen;` is incremented and thrown away while its comment describes a mechanism that isn't there; the coordinator's `_refusedPaneByHwnd` API has zero production callers (container keeps a private identical-named map using ±1 px epsilon vs coordinator's exact equality); `NeedsPanePositionForTest` duplicates the live epsilon logic unchecked.
Scenario: a maintainer trusts the coordinator's exact-match refusal check or "stale frame discarded" comment and reintroduces the resize war the epsilon prevents.
Direction: wire invalidation into real mutation paths and fix coalesce-after-invalidate drop, or delete the dead surface; keep one refusal map (the epsilon one).

### [AUDIT-024] Runtime controller duplicates the policy state machine it claims to wrap; controller settle half is dead
Low / High — `Services/SplitPresentationController.cs:6-12,58-183,84-86,188-198`; `Models/SplitPresentationPolicy.cs:51-56,113-124`; parallel settle in `ContainerWindow.SplitInteractionFix.cs:26-27,236-241`.
Every transition rule exists twice (pure policy used by tests/driver; reference-based twins used by production); survivor-selection logic duplicated with nothing enforcing consistency; controller arms `_settlePending/_settleGeneration` that production never consumes.
Scenario: survivor-rule fix lands in policy (qualification passes) but not in the controller — production drifts.
Direction: make the controller a thin adapter over policy state, or correct docs and add a consistency test tying the two survivor rules together; delete unconsumed settle fields.

### [AUDIT-025] Unreachable branch and self-contradicting comment in `SplitInteractionPolicy.Classify`
Low / High — `Services/SplitInteractionPolicy.cs:104-113,122-142`. Corroborated by test auditor.
Lines 104-113: the two preceding conditions are exhaustive complements, making the final `return ResumeMember` unreachable. Lines 128-141 narrate behavior ("treat a dormant non-member hit as a suspend-like switch") the code does not implement (returns None).
Direction: delete line 113; rewrite the step-7 comment to state the actual invariant.

### [AUDIT-026] Recovery fake's `SetWindowPlacement` ignores `showCmd` visibility semantics — transient-show path untested
Low / Medium — `Services/PendingRecoverySelfTest.cs:1647-1654`; real semantics `Services/PendingRecoveryService.cs:462-497,922-937`.
Real `SetWindowPlacement` applies recorded showCmd (guest becomes visible at PlacementComplete); the fake models no visibility effect, so a v2-presentation entry with `OriginallyVisible=false` and non-SW_HIDE showCmd produces an untested transient flash of a guest whose contracted end-state is hidden.
Direction: model showCmd in the fake and assert end-state visibility; consider deferring placement mutation for hidden-contract entries.

### [AUDIT-027] `RemoveExactRecoveryToken` treated as compare-and-remove but Win32 `RemoveProp` removes by name regardless of value
Low / Medium — `Services/PendingRecoveryService.cs:1939-1944,939-957,606-610`.
No atomic CAS: if the property changes between GetProp and RemoveProp, RemoveProp still deletes whatever is installed. Mitigated in practice by `ProductMutationLease` exclusivity; residual microseconds-wide race deletes a concurrent supervisor's token, which then aborts safely.
Direction: document the residual race next to the existing non-atomicity caveat, or loop GetProp/RemoveProp until returned handle equals expected token.

### [AUDIT-028] One unreadable pending file blocks disk-only cleanup of completed transactions in all other files
Low / High — `Services/PendingRecoveryService.cs:154-161` early return preceding `:167-198`.
A malformed/truncated pending file short-circuits discovery before the completed-transaction retirement pass, freezing entirely safe disk-only retirement of *other* files' resolved evidence.
Scenario: truncated `.pending.001` from disk fault → every run exits 2 and resolved evidence accumulates indefinitely.
Direction: run the completed-transaction cleanup pass before/independently of the unreadable-file short-circuit.

### [AUDIT-029] State-directory loss permanently disables persistence for the session (no self-heal, unlike journal/log writers)
Low / High — `Services/PersistenceService.cs:231-258` (`CommitJson`), `:85-98` (directory created once in ctor); contrast self-healing `WindowShepherdService.SaveJournal:2380-2388` and `LoggingService.EnsureWriter:254-277`.
If `%APPDATA%\TabDock` is deleted while running, every later save fails with DirectoryNotFoundException (log-only), including exit-time save — while journal/log writers quietly recreate their directories.
Direction: mirror SaveJournal's `Directory.CreateDirectory` at the top of CommitJson inside the write gate.

### [AUDIT-030] Monitor-failure warning dialog throws and is lost when the launcher window is closed
Low / High — `App.xaml.cs:688-700` (`HandleWinEventMonitoringFailure`), pattern also at `:747`; `_mainWindow` nulled by Closed handler `:164-168`.
WPF's `MessageBox.Show(Window owner, ...)` dereferences the owner before ShowCore's null-fallback can engage; with `_mainWindow == null` the throw is swallowed by the surrounding catch, so the only user-visible notification of permanent monitoring failure ("captured windows were released safely and capture is disabled") never appears.
Scenario: user closes launcher while guests stay docked → hook install fails 3 retries → guests released, dialog lost, only a log line remains.
Direction: fall back to ownerless `MessageBox.Show(string, string, ...)` or the active container when `_mainWindow` is null.

### [AUDIT-031] Comment contradicts code: release-path hide filtering attributed to the wrong mechanism
Low / High — `Services/GuestLifecycleService.cs:100-103` vs `Services/GroupManager.ReleaseTab:407-418`.
Comment claims the member leaves `Group.Members` before `Release()` runs; code releases first, removes after. Filtering actually works because the hide WinEvent is posted and membership is re-verified at dispatch time. A maintainer "fixing" the ordering or making dispatch synchronous silently loses the guard.
Direction: correct the comment to credit the Post hop + dispatch-time re-verification (`WinEventMonitor.Raise`).

### [AUDIT-032] Single shared `_restoreMinimizedTimer` slot drops a pending restore when a second guest minimizes within 200 ms
Low / Medium — `Views/ContainerWindow.xaml.cs:3187-3198` (called from `GuestLifecycleService.cs:184-188`).
Each new minimize stops and replaces the previous timer; in split mode both members are eligible, so near-simultaneous self-minimizes leave the first pane iconic with no eager recovery until clicked.
Direction: key deferred restores per HWND (dictionary) instead of a single field.

### [AUDIT-033] Emergency release performs one synchronous durable save per member inside the shutdown/logoff critical section
Low / Medium — `Services/GroupManager.cs:433` (`RequestDurableSave("tab-released")` per ReleaseTab), `:489-516` (`EmergencyReleaseAll`); callers `App.xaml.cs:219,359,387,427,459,674`.
Session-end/dispatcher-exception paths invoke N full durable writes (backup copy + write-through + fsync + rename) on the UI thread inside Windows' few-seconds logoff budget; force-kill mid-loop leaves remaining guests shepherded (bounded by startup rescue, but session-ending normalization ran for none).
Direction: hoist one state commit after the loop, or rely on the journal alone during emergency paths.

### [AUDIT-034] Min-track cache retains a scale factor from the wrong monitor after a mixed-DPI move
Low / Medium — cache store `Services/WindowShepherdService.cs:1070-1073`; scaling `:1122-1148`; gate `Views/ContainerWindow.xaml.cs:2270-2307`; re-probe `:665-692`.
Cache records neither monitor nor DPI; nothing marks `_constraintDirty` on WM_DPICHANGED (not handled anywhere). After dragging 100%↔200% monitors, the enforced min-track is scaled by the wrong factor until drag-end or the 5 s timer heals it — transiently permitting pane overflow, the exact condition the contract prevents.
Direction: include monitor/DPI in cache validity, or mark dirty on WM_DPICHANGED.

### [AUDIT-035] `IconService.GetWindowIcon` is dead code carrying its own un-cached extraction path
Low / High — `Services/IconService.cs:47-58`. Zero callers repo-wide; bypasses the cache machinery and will silently rot.
Direction: delete or wire to a test.

### [AUDIT-036] UI thread can synchronously block on shell icon extraction via the shared in-flight task
Low / High — `Services/IconService.cs:89-90` (`waitFor!.Task.GetAwaiter().GetResult()`); UI-thread callers `ViewModels/GroupViewModel.cs:143,288,462`; worker `ViewModels/CapturePickerViewModel.cs:256`. Also filed by performance auditor.
The picker deliberately resolves icons off-thread, but post-capture `AddCapturedWindow` runs the blocking path on the UI thread: if the picker worker owns the in-flight entry the UI waits on it; otherwise the UI thread itself runs `ExtractIconEx` inline (unbounded stall for network image paths).
Direction: return null on miss for UI-thread callers and refresh late, mirroring TryGetCachedFileIcon.

### [AUDIT-037] Sanitized log tail drops almost all exception records — retention depends on accidental context strings
Low / High — filter `Services/DiagnosticEnvironmentService.cs:196-221`; emitters e.g. `App.xaml.cs:215`, `WindowShepherdService.cs:555`.
The allowlist keeps tagged lines; `EXCEPTION in <context>` lines survive only if the context string happens to contain a tag (`ENV[launcher]` survives, `FATAL Application_Startup` is dropped). Crash stacks — the main thing a support bundle is requested for — are excluded by accident.
Direction: emit exceptions under an explicit tag (e.g. `EXCEPTION[...]`) so they pass the allowlist.

### [AUDIT-038] `SanitizeText` replaces the raw username substring anywhere, corrupting diagnostic text
Low / High — `Services/DiagnosticEnvironmentService.cs:256-258` (+ redundant `:260-265`). Independently found by security/performance auditor.
Unanchored case-insensitive replace: a user named `win`, `app`, `system`, or `man` mangles kept content (`WinEvent` → `<user>Event`, `Microsoft Windows NT` → `Microsoft <user>dows NT`) exactly on machines where support needs readable evidence. The subsequent domainUser replacement is dead work.
Direction: restrict username redaction to path-like contexts or use boundary-aware matching.

### [AUDIT-039] Primary log grows without bound while rotation is persistently blocked; only the `.err` fallback is capped
Low / High — `Services/LoggingService.cs:293-331` vs `:63` (`MaxErrFileSize = 64 KB`).
Stream opened without `FileShare.Delete`, so any external reader holding the log makes every rotation `File.Move` fail forever; backoff stops churn but batches keep appending — no size-based stop/truncate fallback for the main log (spec accepts "logging continues uninterrupted" but not unbounded growth).
Direction: add a hard cap fallback (truncate-in-place or suppress past N×MaxSize) mirroring the .err bound.

### [AUDIT-040] `CaptureMonitors` last-write-wins `Status` conflates two independent probe failures
Low / High — `Services/EnvironmentFingerprint.cs:96-118`.
When GetMonitorInfo fails AND DPI returns 0, the earlier "unavailable (GetMonitorInfo)" status is overwritten by "degraded (DPI unavailable)" with zero-rect bounds; support chases the wrong probe.
Direction: compose status reasons or use separate fields per probe.

### [AUDIT-041] `RuntimeTelemetry` is unreachable dead infrastructure; if ever enabled it leaks abandoned transitions
Low / High — `Services/RuntimeTelemetry.cs:30,47,74-81,161-167`; sole transition caller `ContainerWindow.SplitInteractionFix.cs:111-126`; counters wired throughout shepherd/group/batch services.
`Enabled` is never assigned anywhere — all telemetry is a permanent no-op despite doc comments inviting enablement. If switched on: a transition begun whose suspend throws never reaches CompleteTransition, leaking a record forever (`MaxSamples` bounds only latencies); `Reset()` writes eight counters non-atomically against Interlocked increments.
Direction: wire Enabled to a real switch or delete the class; complete transitions in finally; bound `_transitions`.

### [AUDIT-042] Free-text credential regex misses underscore-prefixed key names despite "arbitrary report text" claim
Low / High — `Services/DiagnosticEnvironmentService.cs:24-26` (`s_secretValue`), doc `:231-238`; JSON path covered by `IsSensitiveKey:331-340`.
`\b(password|passwd|token|secret|api[-_]?key|authorization)\b` requires a word boundary, but `_` is a word character — `client_secret=x`, `my_api_key=x` never match in free text. The privacy self-test uses bare `password=`/`token=` so it keeps passing.
Direction: normalize key characters in the regex to match JSON-path semantics.

### [AUDIT-043] `ConsoleSession.TryCreate` leaks the input stream when output/error setup throws
Low / High — `Services/ConsoleSession.cs:76-113`.
stdin StreamReader created first; if stdout/stderr setup or SetIn/SetOut/SetError throws, the catch frees the console but never disposes the already-created streams.
Direction: track created streams locally and dispose them in the catch.

### [AUDIT-044] Elevation-status error reporting reads last error after intervening P/Invokes — correct today only by accident
Low / Medium — `Services/DiagnosticEnvironmentService.cs:61-69`; `NativeMethods.cs:969-1042` (2-arg vs 3-arg IsProcessElevated); sibling `EnvironmentFingerprint.cs:63-64`.
Calls the 2-arg overload then FormatLastError separately, violating the file's own "capture immediately" rule the discarded 3-arg overload exists for; currently right because successful CloseHandle preserves last error. `DescribeMonitors`' enumFailed read similarly runs after callbacks full of SetLastError=true calls.
Direction: use the 3-arg overload's errorDetail; capture enumeration errors inside the wrapper.

### [AUDIT-045] Rejected blank rename leaves a stale empty TextBox on the next rename session
Low / High — `ViewModels/GroupViewModel.Name setter :23-36`; binding `Views/ContainerWindow.xaml:102` (LostFocus trigger).
Push-mode binding writes "" into the TextBox before the setter rejects it; the reject branch raises no PropertyChanged, so Escape self-corrects but Enter/click-away leave the box empty — next rename opens showing "" for a named group.
Direction: raise OnPropertyChanged(nameof(Name)) in the reject branch.

### [AUDIT-046] Spike host: WndProc delegate not rooted after RegisterClassEx — GC-collectable callback thunk
Low / Medium — `Spike/TabDock.Spike/Program.cs:273-282`; correct pattern at `Infrastructure/NativeHwndHost.cs:33`.
Method-local delegate may be collected in Release while the class/window still points at its thunk → crash on next message. Contained to the experimental Spike.
Direction: promote to a static readonly field if the spike is kept buildable.

### [AUDIT-047] Dead P/Invoke surface, including a public `kernel32!GetLastError` re-declaration
Low / High — `NativeMethods.cs:62,137,202,210-213,219,329,501,661,87-88`.
Zero callers in the main project for all listed declarations. The public GetLastError DllImport is a standing footgun: per-thread last-error is only meaningful via Marshal.GetLastWin32Error(); any future caller reads a clobbered value.
Direction: delete unused declarations; remove GetLastError outright.

### [AUDIT-048] Stale "DRIVER-ONLY" annotation contradicted by production use
Low / High — `NativeMethods.cs:161-163` vs production caller `Services/NativeSnapshotService.cs:258`.
`WindowFromPoint` is annotated driver-only but is called by the diagnostics snapshot; a cleanup pass trusting the annotation breaks the build or mis-scopes blast radius.
Direction: update the annotation.

### [AUDIT-049] WINDOWPLACEMENT comments attribute the 60-byte size to the Windows SDK header — it was Mac-only
Low / High — `NativeMethods.cs:768-781`; repeated in `DiagnosticCommandLine.cs:363-375,460-468,526`.
`rcDevice` exists only under `#ifdef _MAC` in winuser.h; Windows WINDOWPLACEMENT is 44 bytes everywhere (shipped code is correct and locked by self-tests). A maintainer "fixing" the struct to match the comment would break every placement restore.
Direction: rewrite the comment to state the actual ABI fact.

### [AUDIT-050] ValidationDriver `Ctx.Skip` freezes status: failed checks after a Skip are swallowed and the scenario still counts as success
Low / High — `tests/ValidationDriver/.../Scenarios.cs:107-134,717,734-735`.
After Skip, `Check(false)` records failure details but leaves Status=Skip; RunScenario returns true for Pass-or-Skip, demoting even the mandatory orphaned-window cleanup guard. Latent today (only pre-check skips exist) but a standing trap.
Direction: demote Skip→Fail on any recorded assertion, or assert cleanup outside the status-latching path.

### [AUDIT-051] Mislabeled/duplicative unit tests claiming coverage they don't have
Low / High — `tests/UnitTests/RequestRelayoutFinalPassTests.cs:71-80`; `PresentationOperationBudgetTests.cs:360-381`.
`UnchangedLayoutUpdated_ProducesNoRelayout` duplicates another test's body; the actual unchanged-rect suppression lives in ContainerWindow and is exercised nowhere headlessly. `PresentationOperationCounter_ThreadSafeSnapshot` is strictly single-threaded despite the lock-based counter's name promising concurrency verification.
Direction: rename to what they do; add a real concurrent hammer or drop the claim; add a predicate seam for the unchanged-rect decision.

### [AUDIT-052] `SaveAsync_DoesNotWriteSynchronously` asserts on timing and can flake
Low / High — `tests/UnitTests/PersistenceSingleWriterTests.cs:66-84`; `Services/PersistenceService.cs:143-150`.
Asserts `File.Exists(path)` is false immediately after SaveAsync; on a hot thread pool the background write can complete first, failing a correct implementation.
Direction: prove the contract via an injectable scheduler seam or pending-write counter instead of racing the filesystem.

### [AUDIT-053] L-series provenance tests prove a mirror contract, not the live gate
Low / High — `TestRunProvenance.AcceptWindowEvidence:579-599` (used only by DeterministicSelfTests.IdentityTests) vs live gate `TryValidateWindow:242-318` which re-implements the rules imperatively.
Weakening the live gate (e.g., dropping the GetProp marker check) leaves all L-series tests green.
Direction: extract the live decision into ProvenanceContract and delegate, or drive TryValidateWindow directly against fake seams.

### [AUDIT-054] Unit-test project builds with 11 warnings; recorded "0 warnings" validation claims are stale
Low / High — `tests/UnitTests/ConverterTests.cs` (11× CS8625); claim at `.agent/STATE.md:504`.
Harmless at runtime but contradicts the repo's own recorded validation state and normalizes warning noise where CI treats some warning classes as errors.
Direction: cast the nulls (`null!`) or use typed sentinels.

### [AUDIT-055] Materialize step overwrites GITHUB_ENV instead of appending, clobbering variables written by earlier actions
Low / Medium — `.github/workflows/prepare-release-candidate.yml:231`.
`WriteAllText($env:GITHUB_ENV, ...)` replaces the whole file, destroying entries appended by setup-dotnet/setup-node (e.g., DOTNET_ROOT). Masked on hosted runners today; latent breakage on image changes/self-hosted runners.
Direction: append rather than rewrite.

### [AUDIT-056] PFX password exposed on signtool's command line while the header claims env-var secrecy
Low / High — `scripts/sign-release.ps1:373-374` vs comment `:120-123`. Independently found by the security auditor.
The env var is only staging; `/p $env:TABDOCK_SIGN_PASSWORD` puts the secret in argv of signtool.exe, readable by any same-user process (WMI/ETW/Process Explorer) for the signing duration. Temp PFX hygiene itself is good.
Direction: use /csp + certificate-store import or stdin/response-file transport; correct the comment.

### [AUDIT-057] `TrimMode=partial` is inert — trimming is not enabled; config misleads readers
Low / High — `TabDock.csproj:29-34`; publish invocations lack PublishTrimmed everywhere (`release-qualify.ps1:296-297`, `validate.ps1:311-321`, `perf.ps1:100`).
Dead configuration sitting under a comment explaining why trimming is NOT used; invites incorrect reasoning about binary size/reflection behavior or a future accidental enabling.
Direction: delete the line or annotate as reserved.

### [AUDIT-058] Comparison-evidence labels hardcoded to specific baseline SHAs even when custom baselines are requested
Low / High — `scripts/qa-split.ps1:252-253,286-287`.
With `-BaselineSha <custom>`, comparison.json records directories labeled `baseline-8b75c99`/`baseline-13c3d6f` containing custom-SHA results — labels contradict the `baselineA/B` fields, making archived evidence look tampered.
Direction: derive labels from requested SHAs.

### [AUDIT-059] ARCHITECTURE.md still describes the split cheap-pin guard as strict adjacency; code uses relative z-order
Low / High — `docs/ARCHITECTURE.md:265-269` vs `Views/ContainerWindow.xaml.cs:2590-2597,2736-2738`; change documented in `docs/runtime-stabilization-2026-08.md` #7.
Doc says pin precondition is `GetWindow(top, GW_HWNDNEXT) == bottom`; code uses `ZOrder.IsOrderedAbove` (tolerates helper HWNDs between panes). An agent "restoring" the documented behavior reintroduces the strict-adjacency churn the stabilization pass removed; the inline comment still explains the old vocabulary.
Direction: update doc bullet and inline comment to the relative-order invariant.

### [AUDIT-060] repository-protection.md says `release.yml` creates the `v*` tag; it is qualification-only and never publishes
Low / High — `docs/release/repository-protection.md:17-21` vs `README.md:367-371`, `publication-gates.md:489-497`, `release.yml:17-20,137`.
Tag creation belongs to Stage B `publish-release.yml`. An operator trusting this doc dispatches the wrong workflow believing it will produce the production release.
Direction: reword to name publish-release.yml as tag creator.

### [AUDIT-061] README says the release-tooling suite has "138 deterministic adversarial cases"; the doc pair and script indicate 139
Low / Medium — `README.md:389-390` vs `docs/release/publication-gates.md:658`; static count of New-TestCase sites = 139.
The canonical docs disagree; hardcoded counts rot with every case addition.
Direction: recount by execution; make one doc cite the number and the other reference it.

### [AUDIT-062] ARCHITECTURE.md log-line index lists a save log line that no code emits
Low / High — `docs/ARCHITECTURE.md:649` ("Saved {n} group(s)…", cited to PersistenceService.cs:114) vs actual `PersistenceService.cs:251` ("Saved state to {redacted path} (schema=2)").
Table is introduced as "Lines that tests and agents may rely on" — a stale entry creates vacuous/unpassable greps.
Direction: replace with the actual format and location.

### [AUDIT-063] `PersistenceService._lastSavedJson` is read and written across threads without synchronization
Low / High — read `:42,209` (UI thread, no lock/volatile) vs write `:250` (threadpool thread inside `_writeGate`).
Single-writer gate serializes disk mutations but not this dedupe field; worst case today is a redundant rewrite, but any future non-UI producer widens it — shared mutable state outside the very gate commit 2e5e4b1 added to make persistence race-free.
Direction: move dedupe comparison inside CommitJson under the gate.

### [AUDIT-064] Same durability concepts implemented three-plus times with drift potential
Low / High — durable write: `PersistenceService.WriteDurableText:704-716`, `WindowShepherdService.WriteDurableText/Bytes:2390-2406`, `PendingRecoveryService.WriteDurableJson:1722-1735`; quarantine: `PersistenceService:758-789` vs `WindowShepherdService:2120-2154`; unique-path magic loop `i < 1000` triplicated (+`:2163-2177`); v1 migration duplicated `PendingRecoveryService:1237-1243` / `WindowShepherdService:2106-2115`; accent default `"#2196F3"` hardcoded in five places (`Models/Group.cs:24`, `GroupManager.cs:342`, `PersistenceService.cs:636,640`, `ColorToBrushConverter.cs:15`).
A durability-rule change lands in one copy; state.json gains the fix while journal/recovery keep old behavior — the lost-update family commits f7c87df/2e5e4b1 were spent on.
Direction: consolidate into one DurableFile helper + one JournalMigrations home; name the accent default once.

### [AUDIT-065] Six near-identical native identity ladders, including two methods with the same name and different contracts
Low / High — `WindowIdentityGate.Evaluate/EvaluateBeforeCaptureToken/VerifyReleasedCloseTarget:167-413`; `WindowShepherdService.EvaluateRecoveryIdentity:2210-2298` / `EvaluateRecoveryGeneration:2306-2370`; `PendingRecoveryService.EvaluateRecoveryTarget:965-1011` / `EvaluateRecoveryGeneration:1045-1105` (same name as shepherd's, different seam) / `ClassifyCompletedTarget:631-718`.
Each repeats IsWindow→PID→thread→exe→class→process-start with slightly different optional tiers; a new identity field must be hand-replicated six times, and divergence already happened once (f6b1952).
Direction: extract one parameterized ladder evaluator with explicit tier extensions.

### [AUDIT-066] Misleading docs and dead seams around shutdown flush and policy inputs
Low / High — `PersistenceService.WhenWritesSettledAsync:110-116` (doc claims graceful-shutdown flush consumer; none exists — App exits via synchronous SaveState); `ContainerWindow.SplitInteractionFix.cs:81-101` computes `isButtonHit` then passes literal `false` to the shared-with-tests classifier (correct only due to an early return two lines up); `PresentationOperationBudget.RecordContainerRaise:55,129` declared/implemented but never called.
Removing the early return assuming Classify handles button hits starts suspending pairs on × clicks.
Direction: pass the computed value; delete or wire RecordContainerRaise; correct the flush doc.

### [AUDIT-067] Support-bundle privacy depends on exact log-marker string parity between ~40 logging call sites and one hardcoded filter list
Low / Medium — `DiagnosticEnvironmentService.ReadSanitizedRecentLogText:196-221`; emitters `WindowShepherdService.cs:701,1821`, `GuestLifecycleService.cs:412`, `GroupManager.cs:346`.
Title-bearing lines are dropped only when their literal marker appears verbatim in the blocklist and titles happen to be single-quoted. A new log site embedding guest-controlled text under a retained tag (SPLIT[/CHROME[) without single quotes flows into bundles unsanitized; the raw log intentionally keeps plaintext, so this filter is the only boundary.
Direction: centralize title-bearing formatting behind one redaction helper (or hash titles at emit, as NativeSnapshotService already does).

### [AUDIT-068] Effective-DPI probe creates and destroys a top-level HWND on every query
Low / High — `Services/MonitorDpiService.cs:87-170`.
Each GetEffectiveDpi does SetThreadDpiAwarenessContext → CreateWindowEx (hidden popup) → GetDpiForWindow → DestroyWindow; invoked per guest per constraint refresh (5 s timer) and resize-end for DPI-unaware guests, on the UI thread. Monitor DPI changes rarely.
Direction: cache monitor-handle→DPI with invalidation on display change/WM_DPICHANGED.

### [AUDIT-069] Diagnostic hotkey export hashes the entire executable and builds a ZIP synchronously on the UI thread
Low / High — `App.xaml.cs:247-270` → `DiagnosticReportService.cs:184-239` → `BuildIdentity.Capture(includeHash:true):103-124`.
Ctrl+Alt+Shift+D runs SHA256 over the running (single-file, tens-to-hundreds-of-MB) exe plus registry probes, desktop EnumWindows, process enumeration, and ZipArchive compression on the dispatcher thread — freezing UI including WM_ACTIVATE-driven foreground handoff.
Direction: compute hash/archive on a worker task; marshal completion back.

### [AUDIT-070] `_minTrackCache` retains released `CapturedWindow` objects on failed-release boundary paths
Low / Medium — `WindowShepherdService.ReleaseBoundaryFailure:1646-1662` vs removals at `:1597,1678,1709,1726,1785,1822,1828`.
Early boundary-failure exits skip cache removal; keyed by object reference, released instances (with placement snapshots/strings) stay rooted until process exit. One small struct per affected window; slow accumulation across repeated failing cycles.
Direction: remove the entry in ReleaseBoundaryFailure or clear centrally in Release's finally for non-pending outcomes.

### [AUDIT-071] Generated build outputs and one-off patch scripts are committed to Git
Low / High — `bin/Debug/**`, `bin/Release/**`, `tests/Performance/bin/Release/net8.0-windows/win-x64/**` (full runtime incl. `createdump.exe`), `obj/**`, root-level `tabdock_runtime_hotfix.py`, `tabdock_persistence_single_writer_fix.py`, numerous `*-results.md` transcripts.
Contradicts AGENTS.md's own rule; stale committed binaries are a footgun (running an outdated exe against current data formats) and bloat provenance review.
Direction: extend .gitignore and remove from the index in a dedicated cleanup commit (recommendation only).

---

## Info / Opportunity Findings

(These supplement §9-§10; no present defect is demonstrated in each, but each is a credible simplification/risk-reduction opportunity with concrete evidence.)

### [AUDIT-072] Legacy-journal downgrade block is dead code in every reachable path
Info / High — `WindowShepherdService.cs:2101-2115`; consumers `:2025-2036`, `:2439-2451`. Both consumers discard legacy files before using entries (loader sets `_journalLoadFailed`; rescue preserves raw bytes), so the v1 normalization mutation is never observed while suggesting to readers that it matters. Direction: delete and fold the historical note into the version-history comment.

### [AUDIT-073] Capture-time DWM transition-suppression HRESULT discarded — inconsistent with the file's fail-closed rigor
Info / High — `WindowShepherdService.cs:336` vs checked writes everywhere else (`RestoreOriginalTransitions:751-763`). Bounded consequence: cosmetic only; release restores symmetrically regardless. Direction: log the HRESULT rather than gating capture on it.

### [AUDIT-074] Stale comment: min-track probe timeout documented as "500 ms", constant is 100 ms
Info / High — `WindowShepherdService.cs:178-184`; constant asserted ≤100 by `DiagnosticCommandLine.cs:286`. Comment drift only.

### [AUDIT-075] Contradictory/garbled comments about event ordering and timer lifecycle
Info / High — `ContainerWindow.SplitInteractionFix.cs:35-40` vs `:66-71` (lines 66-67 assert the ordinary drag guard ran *before* the split handler; registration order proves the opposite — the split handler's handled-marking is what skips the guard); `ContainerWindow.xaml.cs:1047-1052` (close-prompt timer comment describes the exact outcome the preceding Stop()+null prevents; a word is missing). Maintainers trusting either could remove defensive code or reorder registration.

### [AUDIT-076] Empty-entry deferred batch reported as `BeginFailed`
Info / High — `DeferredWindowPositionBatch.cs:112-117`. Nothing attempted yet result names a native failure; routed into `FallbackPosition` (`WindowShepherdService.cs:1218-1220`). Unreachable today (only caller passes 3 entries); semantics trap. Direction: distinct NothingToDo result or documented precondition.

### [AUDIT-077] Lease self-test has a hard 2-second timing dependency
Info / Medium — `ProductMutationLeaseSelfTest.cs:132`. Under loader-lock/CI contention the wait can time out and report a false failure of a correct implementation. Direction: raise timeout or retry loop.

### [AUDIT-078] `HotkeyService.Detach` reports success without checking `UnregisterHotKey`, and removes the hook before unregistering
Info / High — `HotkeyService.cs:91-115`. BOOL result discarded; unconditional success log; WM_HOTKEY delivered in the remove-hook window is dropped. Only reachable via Dispose today. Direction: check results and log failures via FormatLastError.

### [AUDIT-079] Cloak-reason diagnostics collapsed to "True" by BOOL marshaling
Info / High — `NativeSnapshotService.TryGetCloaked:301-312`; declaration `NativeMethods.cs:424`. DWMWA_CLOAKED yields a DWORD reason (1=app, 2=shell, 4=other); marshaling into bool maps all nonzero to True, destroying exactly the distinction picker-filter comments say matters when debugging "captured a tab with nothing behind it". Direction: marshal as out int and emit raw code.

### [AUDIT-080] Duplicate foreground query in `NativeSnapshotService.CaptureWindow`
Info / High — `NativeSnapshotService.cs:125,149`. First read is dead work; two reads can straddle a foreground change (harmless — second wins). Direction: single read defines the field.

### [AUDIT-081] MonitorDpi self-test fake cannot fail the probe's thread-context verification branch
Info / Medium — `MonitorDpiSelfTest.cs:173-174` vs `MonitorDpiService.cs:100-103`. Fake always returns PMv2 from GetThreadDpiAwarenessContext so the first guard's failure path is untestable; a future edit breaking post-switch verification passes unchanged. Direction: flag-controlled non-PMv2 context + assert restore count.

### [AUDIT-082] `CaptureBoundarySelfTest` temp-directory cleanup swallows all failures
Info / High — `CaptureBoundarySelfTest.cs:124-129`. Locked files (AV/indexer) leave a self-test tree per case per run in %TEMP% with no signal; seven cases per invocation. Direction: report deletion failure into counters or reuse one root per process.

### [AUDIT-083] Empty `%APPDATA%` silently relocates all storage to CWD-relative paths
Info / Medium — `PersistenceService.cs:80-83`, `LoggingService.cs:75-78`, `WindowShepherdService.cs:253-255`, `App.xaml.cs:1089-1091`. GetFolderPath returns "" on failure → relative paths against process CWD; most steps then fail closed (why Info), but from a writable CWD state/log/journal silently split across launch directories — divergent "profiles" with no error. Direction: treat empty result as storage-unavailable (fail closed).

### [AUDIT-084] Dead/unreferenced MVVM surface: `RefreshIcon`, write-only `SelectedGroup`, unused converter resource
Info / High — `GroupViewModel.RefreshIcon:460-463` (zero callers); `MainViewModel.SelectedGroup:21-25` written TwoWay, never read; `BoolToVisibilityConverter` declared in App.xaml, exercised only by tests; unused `System.Linq` import in MainViewModel. Direction: delete or wire.

### [AUDIT-085] `GetModuleHandle` declared without CharSet, resolving to the A-suffix export
Info / High — `NativeMethods.cs:308-309`; callers pass null today (correct); a future non-ASCII module path silently fails. Direction: add CharSet.Unicode for consistency.

### [AUDIT-086] `GetWindowTextString` nullable signature contradicts its never-null behavior
Info / High — `NativeMethods.cs:870-879`; consumers e.g. `GuestLifecycleService.cs:396-403` (which carries a comment documenting bugs caused by historically believing null was possible). Direction: tighten return type to string.

### [AUDIT-087] ValidationDriver's `--selftest` suite is wired nowhere automated
Info / High — `ValidationDriver Program.cs:46-49,124-129`; absent from validate.ps1 and build.yml. Split S-series duplicates CI-gated xUnit tests, but the L-series provenance cases run in no gate at all — the only executable proof of the evidence table rots silently. Direction: invoke `--selftest all` from validate.ps1 or fold L-series into xUnit.

### [AUDIT-088] Performance harness is compile-only everywhere and enforces no budgets
Info / High — `tests/Performance/Program.cs` (measurement-only); `validate.ps1:231-233` ("compile-only"). Nothing ever runs it; no pass/fail thresholds exist. Performance regression testing does not currently exist — only manual measurement. Direction: document as manual diagnostic or schedule baseline-diff runs.

### [AUDIT-089] GuineaPig `ParseColor` silently ignores colors the driver actually passes
Info / High — `PigForm.cs:452-463`; callers pass gray/yellow/teal/orange/purple which fall back to SystemColors.Control. Benign today (no dominant-channel assertion on those guests) but the pig's logged color lies. Direction: add colors or log loudly on unknown.

### [AUDIT-090] RC workflow offers unusable `digicert-stm` choice and exposes PFX secrets unconditionally
Info / High — `release.yml:42-50,120-122`. Selecting digicert-stm burns a full qualification run to a predictable BLOCKED_EXTERNAL (`release-qualify.ps1:271-274`); legacy exportable-PFX secrets mounted even for not-configured runs whereas the production path deliberately never receives them. Direction: drop the choice; gate SIGNCERT_* on provider == local-pfx.

### [AUDIT-091] hidden-window-journal spec requires JournalHide to write disk "on every call"; code deliberately skips already-durable captures
Info / High — spec `openspec/specs/hidden-window-journal/spec.md:16-21` vs `WindowShepherdService.JournalHide:1867-1879` + `_durablyJournaledCaptureTokens:225-231`. The safety property (durable before SW_HIDE) is preserved and the optimization is documented in runtime-stabilization #4 — but the spec text was never amended, so spec and implementation formally disagree. Direction: amend the requirement with the durable-capture carve-out.

### [AUDIT-092] Pervasive stale `path:line` citations across ARCHITECTURE.md and specs
Info / High — representative drift enumerated (shepherd log lines cited at :214/:259/:299/:353/:418/:661 vs actual ~990/1477/1525/1708+1821/2499+; App CleanupStaleTempFiles :642-674 vs 1085-1117; HotkeyService :59-61 vs 65-67; ContainerWindow activation regions; GroupViewModel PickColorCommand :103 vs 160; IconService :33-67 vs 39-111; GroupManager EmergencyReleaseAll :395-419 vs 489-516; LoggingService :226 vs 263; also `container-activation-timers/spec.md:9,38`, `group-color-picker/spec.md:9`, `capture-picker-icons/spec.md:9`). Navigational only — symbols still exist. Direction: symbol-anchored references in canonical docs.

### [AUDIT-093] Systemic pattern: posted/deferred callbacks acting on stale state, patched with eight ad-hoc generation mechanisms
Info / High — history of `ContainerWindow.xaml.cs` (27 touching commits, ~20 fixes: 5404349, 5e2a2ba, 053a7d1, 2f2b572, 1411dbc, 3886e79, ed8bdd0, 13c3d6f, 0df4f26, bb624d4, 9713eee, 08fc456, f7c87df…), SplitInteractionFix rewritten within days (3886e79→0df4f26→bb624d4→9713eee), PersistenceService same-day lost-update chain (f7c87df→6ebdd8a→2e5e4b1).
Coexisting mechanisms: `_splitPresentationSettleGeneration`, controller `_generation/_settleGeneration`, coordinator `_layoutGeneration/_pendingLayoutGeneration` (the latter discarded), `_guestMoveSizeGeneration`, five copy-pasted reference-equality timer guards (`:469,673,953,1144,3191`), `_dragMidpointsCount` validity, WinEventMonitor `_running` re-resolution guards. One mechanism is already inert ([AUDIT-023]); each new async callback must remember to participate in all relevant ones.
Direction: consolidate the timer pattern into one ReplaceableTimer helper; document a single generation authority per concern.

### [AUDIT-094] Repository hygiene: unreviewable work landed on main; one-shot patch scripts and agent output dumps committed at root
Info / High — commits 27e0640 ("everything"), ace3161 ("stable"), 00f117a ("noop"), 08fc456 ("some unfinished shi" — 1,822 insertions mixing a 388-line ContainerWindow rewrite with agent prompts and hotfix scripts), 51f0c40, 14a9a46 ("results"), a4e5a3d ("extra"); root holds tabdock_*.py patch scripts pinned to old SHAs plus nine model-output transcripts.
Bisecting the recurring split/render regressions across these commits is nearly impossible; stale patch scripts can be re-run against evolved code.
Direction: adopt the .agent/ workflow for plans/results; archive or delete one-shot scripts; real commit messages.

### [AUDIT-095] ValidationDriver's global single-instance mutex lacks the user-scoping/DACL hardening applied to the product lease
Info / High — `GuardedProc.cs:31` (`Global\TabDockValidationDriver`, default security) vs `ProductMutationLease` (SID-scoped name, protected DACL, owner verification). Any local process can pre-create/squat the mutex and derail driver startup. Test-tooling blast radius only. Direction: reuse the lease pattern or session namespace.

### [AUDIT-096] Plaintext sensitive metadata persists in `%APPDATA%\TabDock` state/journal/log (by design, documented)
Info / High — `PersistedState.cs:29-38,49`; `WindowShepherdService.cs:701`; `GuestLifecycleService.cs:412`. ExePath/titles stored unencrypted under user-private ACLs; support-bundle path sanitizes (verified by adversarial privacy self-test); README warns raw log must be redacted before sharing. On record so the ACL assumption is explicit: roaming profiles/backup agents/misconfigured folder redirection weaken it. Direction: none required; optionally document the ACL assumption in release docs.

### [AUDIT-097] Log-tail read can start mid UTF-8 sequence, producing replacement-character garbage at the head
Info / High — `DiagnosticEnvironmentService.ReadRecentLogText:167-172`. Byte-offset Seek may split a multi-byte character → leading U+FFFD. Cosmetic. Direction: seek further back and trim to first newline.

### [AUDIT-098] Dead local in `InspectJsonFile`
Info / High — `DiagnosticEnvironmentService.cs:372`. `string status = ...` assigned, never read. Delete.

### [AUDIT-099] Stabilization self-test leaves an undisposed logger writing to a fixed, never-cleaned temp directory
Info / High — `RuntimeStabilizationSelfTest.cs:108-114`. Undisposed LoggingService (background writer + open handle until exit); finally deletes only statePath; fixed non-GUID directory shared by concurrent validate runs — diverges from sibling self-tests' hygiene convention. Direction: dispose + GUID-suffixed root + full cleanup.

### [AUDIT-100] Duplicated/dead classification clusters inside PendingRecoveryService
Info / High — `NativeRecoveryStatusProbe.Classify:1964-2011` vs adapter `:2013-2048` (line-for-line duplicate over two APIs); SHA computed twice `:1216,1218`; unreachable reconcile branch `:401-404` (reachable only from direct calls/self-tests); `SanitizeConsoleTitle:1395-1396` pure alias; hardcoded `%APPDATA%\TabDock` header label `:107` while directory is a parameter. Fixing a classification bug in one copy misses the other. Direction: implement once over the API interface; delete/comment the defensive branch.

### [AUDIT-101] Unbounded recovery artifacts: orphaned `.tmp` files and never-pruned retired ledger transactions
Info / High — `PendingRecoveryService.WriteDurableJson:1722-1735` (crash between temp write and Move strands `*.pending.recovered.tmp` forever; discovery filters them out so they are invisible); `PersistTransaction:806-874` appends Retired records forever, rewriting the growing file each phase. Hygiene/bloat only. Direction: sweep stale .tmp during Discover; compact ledger during retirement.

---

## 9. Optimization Opportunities

- **UI-thread stalls (highest user impact):** blocking icon extraction on UI thread [AUDIT-036]; synchronous SHA-256+ZIP diagnostic export [AUDIT-069]; per-member durable saves in emergency paths [AUDIT-033]. Each is bounded by nothing and directly freezes interaction with docked guests.
- **Repeated native churn:** DPI probe creates/destroys a helper window per query, invoked on 5 s timers per guest [AUDIT-068] — a per-monitor cache eliminates nearly all of it.
- **Hot-path logging cost:** `LogPositioningFailureOnce` pays format+allocation+locked trace write before its dedupe check [AUDIT-004].
- **Memory retention:** context-menu set growth [AUDIT-020], min-track cache retention on boundary failures [AUDIT-070], telemetry transition leak if ever enabled [AUDIT-041]. All small-per-item but unbounded over session length.
- **Startup/exit:** N synchronous fsync-grade writes during logoff [AUDIT-033]; `RequestDurableSave` O(n²) exit serialization noted by an auditor as below reporting threshold (single-user scale).
- **Not worth doing:** micro-optimizing the WinEvent filter chain or split geometry math — auditors found both already lean; the perf harness exists to keep it that way once actually run ([AUDIT-088]).

## 10. Architectural Improvement Opportunities

- **One staleness authority per concern** [AUDIT-093]: eight ad-hoc generation/reference mechanisms guard deferred callbacks; one is already inert [AUDIT-023]. A single `ReplaceableTimer` + documented generation ownership would eliminate the repo's dominant historical bug class.
- **Controller-as-adapter over policy** [AUDIT-024]: the split controller re-implements the pure policy state machine it documents itself as wrapping; production and qualification can drift silently.
- **Consolidate durability primitives** [AUDIT-064]: three hand-rolled durable writers, two quarantine implementations, triplicated unique-path loops, duplicated v1 migration.
- **Consolidate identity ladders** [AUDIT-065]: six near-copies including same-name/different-contract methods; divergence has already shipped once (f6b1952).
- **Fail-closed API symmetry** [AUDIT-006], [AUDIT-007]: make every controller transition return success/failure like its siblings; give suspension an unconditional re-present primitive instead of delegating repair to conditionally-no-op relayout.
- **Recovery ledger identity** [AUDIT-001]: bind resolutions to per-quarantine GUIDs rather than content hashes; add supervised retirement for verifiably-gone targets [AUDIT-008].
- **Dead-surface deletion** [AUDIT-022], [AUDIT-023], [AUDIT-035], [AUDIT-041], [AUDIT-072], [AUDIT-084], [AUDIT-100]: ~10 verified-dead members create false confidence and drift traps; deleting them is cheaper than maintaining parity comments.

## 11. Test and Quality-Gate Gaps

Prioritized by danger masked:
1. Recovery self-tests never exercise v3 schema/thread identity [AUDIT-009] — the shipping schema's identity contract is unprotected.
2. Tautological budget/coalescing tests [AUDIT-011] and mirror-contract provenance tests [AUDIT-053] — green while real paths regress.
3. DPI-scenario skips recorded as PASS [AUDIT-012] — mixed-DPI coverage overstated on exactly the machines automation runs on.
4. 13 release-binding assertions silently skipped [AUDIT-014]; no strict mode in the harness.
5. Missing negative/concurrency cases: hide-classification race [AUDIT-002], DefinePair failure path [AUDIT-006], suspend-with-dying-guest [AUDIT-007], concurrent counter hammer [AUDIT-051], restart-mid-recovery retirement [AUDIT-001].
6. ValidationDriver `--selftest` L-series wired nowhere [AUDIT-087]; performance harness never executed [AUDIT-088]; `Ctx.Skip` status latch trap [AUDIT-050]; timing-flaky persistence test [AUDIT-052]; mislabeled tests [AUDIT-051]; stale "0 warnings" state claim [AUDIT-054].

## 12. Security and Privacy Assessment

**Concrete vulnerabilities:** none demonstrated with Critical/High exploitability. The most material items:
- Workflow-dispatch input interpolation into privileged job scripts [AUDIT-013] — requires dispatch rights (insider/compromised-token threat model), inconsistent with the repo's own env-indirection pattern.
- PFX password on signtool argv contradicting the script's secrecy contract [AUDIT-056] — local same-user process exposure window during signing.
- Support-bundle privacy boundary resting on marker-string parity [AUDIT-067] plus underscore-key regex gap [AUDIT-042] — future log sites can leak guest-controlled text into "safe to share" bundles; username substring corruption [AUDIT-038] degrades evidence (over-redaction, not leakage).

**Meaningful hardening already present (verified):** source-generated JSON deserialization with no polymorphic typing; fail-closed elevation/DPI admission; SID-scoped product lease with DACL; exact-SHA action pinning; `persist-credentials: false`; Stage A receives no PFX secrets; adversarial privacy self-test.

**Sensitive data handling:** plaintext titles/exe paths in profile-local files is deliberate and documented [AUDIT-096]; the ACL assumption deserves explicit documentation.

## 13. Reliability and Failure-Recovery Assessment

- **Crash recovery design is strong** (durable journal before mutation, quarantine-not-mutate, phased transactions) but has two integrity gaps: content-hash resolution identity [AUDIT-001] and the destroyed-target dead-end [AUDIT-008], plus cleanup ordering blocked by unreadable siblings [AUDIT-028].
- **Restart behavior:** startup rescue runs pre-restore, exception-guarded; legacy/future/malformed journals quarantined or preserved (verified against self-tests). State-directory deletion permanently disables saves for the session [AUDIT-029]; empty APPDATA relocates storage silently [AUDIT-083].
- **Partial failure:** DefinePair/suspend partial failures leave UI/runtime disagreement [AUDIT-006], [AUDIT-007]; monitor-failure notification can be lost entirely [AUDIT-030].
- **Idempotency:** journal writes are idempotent-by-design (already-durable carve-out) though formally divergent from spec text [AUDIT-091]; RemoveProp CAS gap documented [AUDIT-027].
- **Shutdown:** session-end performs per-member synchronous saves inside the logoff budget [AUDIT-033]; release-path hide filtering depends on Post-hop timing with a comment attributing it to the wrong mechanism [AUDIT-031].

## 14. Performance and Scalability Assessment

Single-user desktop scope; no database/network dependencies. Bottlenecks are UI-thread stalls (§9) and repeated native probes; algorithmic complexity is sound everywhere examined (WinEvent filters, geometry fuzz-validated 1..4096 × 100k). Scaling limits: guest count multiplies per-event positioning work and emergency-save count linearly; the 1024-entry diagnostic ring can be flooded by a single failing guest [AUDIT-004]; log growth unbounded under rotation failure [AUDIT-039]. The performance harness exists but never runs [AUDIT-088], so regressions in these areas currently have no automated signal.

## 15. Maintainability / Technical Debt Assessment

Disproportionately expensive-to-change areas: `ContainerWindow.xaml.cs` (3,584 lines; 27 touching commits; five copy-pasted timer guards), `WindowShepherdService.cs` (2,762 lines, six responsibilities), the split subsystem's policy/controller/container triple [AUDIT-024], and the six identity ladders [AUDIT-065]. Comment rot is a distinct debt class here: at least ten findings are contradicted-or-stale comments that actively misdirect maintainers ([AUDIT-031], [AUDIT-049], [AUDIT-059], [AUDIT-062], [AUDIT-066], [AUDIT-075], [AUDIT-092], [AUDIT-025], [AUDIT-019], [AUDIT-004]). Repo hygiene (committed binaries, one-shot scripts, message-free commits [AUDIT-071], [AUDIT-094]) makes history-based remediation harder than the code itself requires.

## 16. Documentation / Implementation Divergence

Consolidated: [AUDIT-016] (TESTING.md cites non-existent log line, claim of verification false), [AUDIT-059] (strict adjacency vs relative z-order), [AUDIT-060] (wrong workflow named for tag creation), [AUDIT-061] (138 vs 139 case counts), [AUDIT-062] (non-existent save log line in the "lines tests may rely on" index), [AUDIT-091] (spec mandates per-call journal write; code deliberately skips durable captures), [AUDIT-092] (pervasive stale line citations), plus in-code doc divergence: [AUDIT-004], [AUDIT-019], [AUDIT-025], [AUDIT-031], [AUDIT-049], [AUDIT-056], [AUDIT-066], [AUDIT-075]. Pattern: docs were written against earlier SHAs and not re-verified when behavior changed; the project's own §D rule (assertions must reference committed instrumentation) is violated by its own canonical docs.

## 17. Dead / Stale / Suspicious Code

Verified-dead (zero callers, grep-checked): `SuspendSplitPairForGuest` [AUDIT-022], `SplitPresentationController.ExplicitExit` (production) [AUDIT-022], coordinator refusal API + `NeedsPanePositionForTest` + discarded `gen` [AUDIT-023], controller settle fields [AUDIT-024], `IconService.GetWindowIcon` [AUDIT-035], `RuntimeTelemetry` (never enabled) [AUDIT-041], nine NativeMethods declarations incl. public GetLastError [AUDIT-047], legacy-journal fixup block [AUDIT-072], `RefreshIcon`/`SelectedGroup`/converter resource [AUDIT-084], `RecordContainerRaise` [AUDIT-066], `WhenWritesSettledAsync` (production) [AUDIT-066], duplicate probe classifier [AUDIT-100], dead locals [AUDIT-098], unreachable policy branch [AUDIT-025], inert `TrimMode` [AUDIT-057]. Suspicious-but-alive: `isButtonHit` literal passed to shared classifier [AUDIT-066]; duplicate foreground query [AUDIT-080].

## 18. Cross-Cutting/Systemic Issues

1. **Deferred-callback staleness handled ad hoc** [AUDIT-093] — root cause of the historical bug stream; one mechanism already inert [AUDIT-023].
2. **Silent failure paths without caller-visible signals** — DefinePair void [AUDIT-006], lost warning dialog [AUDIT-030], swallowed console output [AUDIT-010], log-only persistence failure [AUDIT-029], skip-as-PASS [AUDIT-012].
3. **Duplicated domain rules drifting** — durability trio [AUDIT-064], identity sextet [AUDIT-065], policy/controller twins [AUDIT-024], suspend twins [AUDIT-022], probe classifiers [AUDIT-100].
4. **Suppression/caching keyed by recycled identities** — HWND-keyed log suppression [AUDIT-004], object-keyed caches outliving release [AUDIT-018], [AUDIT-070], content-hash recovery keys [AUDIT-001].
5. **Test honesty debt** — suites asserting fakes/stubs/mirrors [AUDIT-011], [AUDIT-053], [AUDIT-051]; skipped-as-passed [AUDIT-012]; silently-disabled assertions [AUDIT-014].

## 19. Improvement Backlog

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
| -------- | -- | -------- | ------- | ------ | ------ | ---------- |
| 1 | AUDIT-003 | High | publish-release uploads never-materialized evidence asset | Release chain broken / partial release | S | High |
| 2 | AUDIT-001 | High | `.recovered` sidecar resolves byte-identical new evidence | Silent recoverability loss | M | Medium |
| 3 | AUDIT-002 | High | Tab-switch race releases active tab hidden | User-visible app loss | M | Medium |
| 4 | AUDIT-013 | Medium | workflow_dispatch inputs injected into privileged PS | Secret-bearing job RCE via dispatch | S | High |
| 5 | AUDIT-012 | Medium | DPI scenario skip recorded PASS | False qualification coverage | XS | High |
| 6 | AUDIT-014 | Medium | fused token disables 13 binding checks | Release assurance gap | XS | High |
| 7 | AUDIT-006 | Medium | DefinePair silent failure desyncs split state | UI/runtime disagreement | S | High |
| 8 | AUDIT-005 | Medium | min-max hook disables declared minimums | Unusable chrome at small sizes | S | High |
| 9 | AUDIT-004 | Medium | log-once violates invariant; HWND suppression sticky | Diagnostic blindness | S | High |
| 10 | AUDIT-010 | Medium | console attach/free drops verdict lines | Corrupt qualification evidence | S | Medium |
| 11 | AUDIT-015 | Medium | delete-group modal beneath guest | Freeze impression | S | High |
| 12 | AUDIT-008 | Medium | destroyed-target recovery dead-end | Permanent exit-2 trap | M | High |
| 13 | AUDIT-009 | Medium | v3 schema untested in recovery suite | Identity contract unprotected | S | High |
| 14 | AUDIT-007 | Medium | failed suspension leaves blank presented pair | Persistent blank panes | M | Medium |
| 15 | AUDIT-016 | Medium | TESTING.md cites non-emitted log line | Vacuous assertion trap | XS | High |
| 16 | AUDIT-011 | Medium | tautological budget tests | False regression safety | M | High |
| 17 | AUDIT-029 | Low | persistence no self-heal on dir loss | Silent save failure | XS | High |
| 18 | AUDIT-030 | Low | monitor-failure dialog lost | Unnoticed guest release | XS | High |
| 19 | AUDIT-036 | Low | UI blocks on icon extraction | Interaction stalls | S | High |
| 20 | AUDIT-069 | Low | hotkey export hashes/zips on UI thread | UI freeze | S | High |
| 21 | AUDIT-068 | Low | DPI probe window churn per query | Repeated native cost | S | High |
| 22 | AUDIT-064 | Low | durability helpers triplicated | Drift risk | M | High |
| 23 | AUDIT-065 | Low | six identity ladders | Divergence risk | L | High |
| 24 | AUDIT-093 | Info | consolidate staleness mechanisms | Eliminates bug class | L | High |
| 25 | AUDIT-071 | Low | committed build outputs/scripts | Hygiene/provenance | S | High |

(Top 25 of 101 findings shown; full finding list in §5–§8 and the Info section is authoritative.)

## 20. Recommended Remediation Order

1. **Release-chain correctness** ([AUDIT-003], [AUDIT-013], [AUDIT-014], [AUDIT-012], [AUDIT-010]): the publication path and its evidence trail must work and tell the truth before anything else ships through it.
2. **Data-integrity/recovery** ([AUDIT-001], [AUDIT-008], [AUDIT-028], [AUDIT-009]): ledger identity + supervised retirement + v3 fixtures.
3. **High-risk races** ([AUDIT-002], [AUDIT-006], [AUDIT-007], [AUDIT-021]): hide-classification timestamps first — it needs the expectation mechanism generalized, which later split fixes reuse.
4. **User-facing correctness** ([AUDIT-005], [AUDIT-015], [AUDIT-030], [AUDIT-029], [AUDIT-045]).
5. **Architectural root causes** ([AUDIT-093], [AUDIT-024], [AUDIT-064], [AUDIT-065], dead-surface deletions): do after 3 so new seams absorb the fixes rather than precede them.
6. **Test gaps around those changes** (regression tests enumerated per finding; harness strict mode [AUDIT-014]; wire driver selftests [AUDIT-087]).
7. **Performance** ([AUDIT-036], [AUDIT-069], [AUDIT-068], [AUDIT-004], [AUDIT-033]) — after correctness, and only once the perf harness actually runs [AUDIT-088].
8. **Medium/low technical debt and hygiene** (docs divergence batch [AUDIT-016]/[AUDIT-059]-[AUDIT-062]/[AUDIT-091]/[AUDIT-092]; privacy hardening [AUDIT-038]/[AUDIT-042]/[AUDIT-067]; repo cleanup [AUDIT-071], [AUDIT-094]).

Dependencies: (3) before (5); (1) gates any release exercised mid-campaign; recovery fixtures (2) should land before touching shepherd journal code in (3).

## 21. Positive Findings

- **Transaction discipline:** capture/hide/release/rescue all follow durable-journal-before-mutation with generation revalidation immediately before each native write; adversarial tracing of both directions found the ordering intact.
- **Identity gating:** tiered HWND identity (token property → pid/thread → exe/class → process-start) re-checked at every mutation boundary is a genuinely strong anti-recycling design, rare at this scale.
- **P/Invoke hygiene:** rooted delegates, correct GDI/icon handle ownership on every path (verified by dedicated auditor sweep), WINDOWPLACEMENT ABI locked by self-tests, correct HDWP failure semantics.
- **Fail-closed culture:** DPI-unaware guests admitted only through a central conversion policy; unverifiable captures leave tokens installed deliberately; lease bypasses are confined to isolated-temp-root diagnostic commands.
- **Test estate breadth:** 146 passing unit tests, ten CI-wired self-test suites, a real-input ValidationDriver with identity-provenance input guards and crash-rescue scenarios described by two independent auditors as exemplary, and a 139-case release-tooling suite gated on exact SHA in hosted CI.
- **Release-chain security posture:** trusted-dispatch contracts, Stage A/B separation, least-privilege job split, full-SHA action pinning, `persist-credentials: false`, production path never receives exportable-PFX secrets.
- **Honest tooling:** the perf harness measures without pretending to gate; KNOWN_ISSUES.md and runtime-stabilization docs record real incident history with commit receipts.

## 22. Audit Coverage / Confidence

**Inspected deeply (full reads, at least once; largest files twice by independent auditors):** all 42 Services files, App.xaml.cs, NativeMethods.cs, all Views/ViewModels/Models/Converters, Infrastructure/NativeHwndHost.cs, ContainerWindow partials, Spike host, all unit-test sources, all ValidationDriver/GuineaPig sources (~15.8k lines), all nine scripts, all four workflows, csproj/manifest/global.json/.gitignore, README/ARCHITECTURE/TESTING/release docs, all 12 openspec specs, full git oneline history + targeted patch reads on four hot spots.

**Inspected moderately:** docs/internal waypoint records (sampled), openspec/changes (spot-checked where specs cite them), some self-test fixture bodies (wiring verified, assertions sampled).

**Could not be fully validated:** live GUI interaction sequences (races traced, not reproduced); Stage B workflow execution (statically traced + one hosted CI run inspected); 32-bit/x86 behavior (win-x64 only by RID); non-Windows platforms (out of scope by design).

**Runtime/environment limitations:** ValidationDriver and interactive validate.ps1 not executed (real input injection); release-tooling-tests.ps1 not executed locally (hosted result inspected instead).

**Areas deserving a second specialized audit:** WPF focus/activation interplay under real IME/overlay environments; long-duration memory-retention behavior under split-heavy usage; the release chain executed end-to-end against a scratch repository.

## 23. Final Assessment

- **Overall health:** good-to-strong core with concentrated seam defects; conditionally production-ready.
- **Largest systemic risk:** posted/deferred callbacks acting on stale state, guarded by eight ad-hoc mechanisms, one already inert — this is the root cause of the repo's dominant historical bug class and will produce the next one.
- **Strongest subsystem:** the Shepherd transaction/journal core and its identity gating — adversarially traced end-to-end twice without a Critical finding.
- **Weakest subsystem:** the release/publication toolchain (broken upload path, injected inputs, silently disabled test assertions, doc contradictions) — ironically the area with the most elaborate documentation.
- **Highest-value improvement:** generalize the container-hide-expectation timestamp pattern to all TabDock-initiated transitions ([AUDIT-002]) — it closes the worst user-visible bug and creates the foundation for consolidating the staleness mechanisms.
- **Most urgent remediation:** fix `publish-release.yml`'s evidence asset ([AUDIT-003]) — a one-line-class fix gating the entire documented release path.
- **Remaining uncertainty:** Medium-confidence findings ([AUDIT-001], [AUDIT-002], [AUDIT-007], [AUDIT-010], [AUDIT-026], [AUDIT-027], [AUDIT-032]-[AUDIT-034], [AUDIT-044], [AUDIT-046], [AUDIT-055], [AUDIT-061], [AUDIT-067], [AUDIT-070], [AUDIT-077], [AUDIT-081], [AUDIT-083]) depend on Win32/WPF timing or environment semantics reasoned from documentation; each names the targeted verification that would settle it.

---

*Audit performed read-only; no source, config, test, or documentation files were modified. Sole artifact produced: this file.*
