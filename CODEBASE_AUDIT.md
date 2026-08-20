# Codebase Deep Audit

**Repository:** TabDock — a Windows desktop utility (C# 12 / .NET 8 / WPF / P-Invoke)
**HEAD at audit:** `a4e5a3d8c32e85c8da31c9b200aef9fa1dd9b904` (branch `main`, remote is main-only)
**Audit type:** Deep, read-only, whole-repository technical audit. No source/test/config/CI files were modified. The only intentional repository write is this file.
**Auditor process:** Manual deep-read of the core engine + persistence + split state machine by the lead auditor, plus parallel read-only subsystem sweeps (persistence/recovery, native/P-Invoke/DPI, tests/CI/release). Two subsystem sweeps (diagnostics/privacy/startup and MVVM/split) were interrupted by an external service quota; those areas were then covered directly by the lead auditor. See §22 for the resulting confidence map.

---

## 1. Executive Summary

TabDock merges independent top-level application windows ("guests") into a tabbed/split container under the **Shepherd model**: guests are positioned, z-ordered, shown/hidden, and DWM-transition-suppressed over the container's content area, but **never reparented, restyled, or re-owned**. This is a deliberate architecture chosen to eliminate an entire class of keyboard-input/DPI/attach-detach bugs that a prior `SetParent`-based backend suffered.

**Overall assessment.** This is an *unusually hardened, mature* codebase that has clearly been through many prior adversarial audit campaigns (documented in `.agent/STATE.md`, `KNOWN_ISSUES.md`, `investigation_findings.md`, and an extensive archived OpenSpec change history). The safety-critical invariants that matter most for a window-shepherding tool — **journal-before-dangerous-mutation**, **two-tier window-identity gating at every native boundary**, **latest-wins single-writer persistence**, **idempotent crash-recovery replay**, **UI-thread confinement of all shared mutable state**, and **fail-closed capture admission** — genuinely hold under the concurrency and failure modes the code actually experiences. I found **no CRITICAL defect** and **no data-loss/corruption/security hole** in the core runtime paths after adversarial review.

**Major strengths.** (1) The `WindowShepherdService` identity gate and journal ordering are exemplary and self-tested. (2) The WinEvent pipeline is entirely UI-thread confined (OUTOFCONTEXT hooks on the installing thread) and re-validates identity at both native-callback and dispatch time. (3) Persistence is genuinely torn-write immune (WriteThrough+fsync `.tmp` → atomic rename, generation-gated latest-wins). (4) Diagnostic/support output has multi-layered privacy redaction and never emits raw window titles. (5) The release pipeline is fail-closed, evidence-gated, SHA-pinned, and least-privilege.

**Major weaknesses / highest-risk areas.** (1) **Test confidence is concentrated on the pure "policy" layers while the production orchestration is validated only by a real-input harness that never runs in CI** — the single most important systemic risk (see [AUDIT-001], [AUDIT-005], [AUDIT-006]). (2) A **duplicated split-screen state machine**: the exhaustively-tested `SplitPresentationPolicy` is *not* the code production runs, and the two implementations already disagree on one transition ([AUDIT-002]). (3) The **Stage-B production publication workflow appears structurally unable to complete** because it attaches a release asset that is not present in the publishing job ([AUDIT-003]) — latent today only because production signing is intentionally `BLOCKED_EXTERNAL`. (4) Documentation/reality drift and committed process artifacts ([AUDIT-011], [AUDIT-012]).

**Findings by severity:** CRITICAL 0 · HIGH 1 · MEDIUM 5 · LOW 9 · INFO/OPPORTUNITY 6.

**Production readiness.** The repository-side engineering is **conditionally production-ready**. The application runtime is in strong shape. The blockers are (a) the latent Stage-B publish-asset defect, which will surface the first time a signed candidate is published, and (b) the project's own honestly-documented external gates (real Authenticode signing, human smoke test, physical mixed-DPI qualification, Windows 10 x64 compatibility) that remain `BLOCKED_EXTERNAL`. The project's self-assessed verdict — **"GO FOR RELEASE CANDIDATE / BETA ONLY; v1.0.0 PREPARED BUT INTENTIONALLY NOT PUBLISHED"** — is accurate and this audit concurs.

**Confidence in the audit:** High for the core engine, persistence, native interop, privacy, and CI/release tooling (read in full and cross-verified). Medium for `PendingRecoveryService` (2,049 lines, read but the combinatorial interrupted-transaction space was not exhaustively proven) and the diagnostics command-line surface (lightly covered). See §22.

---

## 2. Audit Scope

**Languages / frameworks:** C# 12, .NET 8 (`net8.0-windows`), WPF, extensive Win32 P/Invoke (USER32/DWM/SHCORE), PowerShell (release/validation tooling), GitHub Actions YAML, OpenSpec (Node tooling), xUnit.

**First-party surface (in scope):** ~102 `.cs` files (~25.6k lines excluding tests/Spike), 4 XAML, the root `App.xaml.cs` (1,119), `NativeMethods.cs` (1,052), `Views/ContainerWindow.xaml.cs` (3,584), `Services/WindowShepherdService.cs` (2,762), `Services/PendingRecoveryService.cs` (2,049), the `Services/`, `Models/`, `ViewModels/`, `Views/`, `Converters/`, `Infrastructure/` trees; `tests/UnitTests`, `tests/ValidationDriver`, `tests/Performance`; `.github/workflows/*.yml`; `scripts/*.ps1`; `TabDock.csproj`, `TabDock.sln`, `global.json`, `app.manifest`, `.editorconfig`, `.gitattributes`; `openspec/` specs; the architecture/testing/release docs.

**Read in full by the lead auditor:** `WindowShepherdService.cs`, `ContainerWindow.xaml.cs`, `GroupManager.cs`, `GuestLifecycleService.cs`, `WinEventMonitor.cs`, `PersistenceService.cs`, `SplitPresentationController.cs`, `SplitPresentationPolicy.cs`, `DiagnosticEnvironmentService.cs`, relevant `App.xaml.cs` sections, `docs/ARCHITECTURE.md`, `AGENTS.md`, `.agent/STATE.md`, `TabDock.csproj`, `global.json`, `app.manifest`.

**Validation commands executed (read-only):** git state resolution (`rev-parse`, `ls-remote`, `branch -r`, `status`), targeted `grep`/`ripgrep` cross-reference checks, directory/inventory enumeration. See §4.

**Excluded (with reason):**
- `node_modules/` under `.opencode/`, `.kilo(code)/`, `tools/openspec/` (~8,700 JS/TS/map files) — vendored third-party dependency caches; not first-party.
- `bin/`, `obj/`, `.repowise/` — build output / local index caches.
- `Spike/TabDock.Spike/Program.cs` — an experimental spike; present in the solution but excluded from the product build (`DefaultItemExcludes` excludes `Spike/**`); not shipped.
- Binary assets (icons).

**Important limitations:** This is a desktop-automation product whose most failure-prone behavior (native window manipulation, DPI, real input, live crash recovery) is intrinsically hard to validate without a live interactive Windows desktop. No code was executed. Runtime/OS-level claims (power-loss durability, `SetProp` survival across `TerminateProcess`, GitHub Actions expression coercion) are reasoned from documented contracts, not observed.

---

## 3. System Architecture Model

**Major components and responsibilities:**

| Component | Responsibility |
| --- | --- |
| `App` (`App.xaml.cs`) | Orchestrator: DI-by-hand construction, single-instance product-mutation lease, startup ordering, all shutdown/crash/session-ending paths, hotkeys, capture-picker entry, diagnostic-command dispatch. |
| `WindowShepherdService` | The **only** native write authority. Capture/hide/release/position/z-order/foreground/restore + the crash-recovery journal (`hidden-windows.json`). Two-tier identity gate. |
| `ContainerWindow` | WPF host window. WndProc (WM_ACTIVATE/ENTER/EXITSIZEMOVE/WINDOWPOSCHANGED/GETMINMAXINFO), coalesced relayout, split panes, tab-strip drag/reorder/pop-out, chrome popups, close/delete prompts. Delegates split state to controllers. |
| `SplitPresentationController` | Runtime authority for split pair identity / presented-vs-dormant / foreground / generation. |
| `SplitPresentationPolicy` / `SplitInteractionPolicy` | *Pure* state-transition & hit-classification policies (string-identity; window-free). |
| `PresentationLayoutCoordinator` / `PresentationOperationBudget` | Coalesced per-frame relayout scheduling; native-operation budget counting seam. |
| `GroupManager` | Owns `Groups`, the O(1) HWND→member index (maintained via `CollectionChanged`), capture/release/switch/reorder/close, emergency release, save routing. |
| `GuestLifecycleService` | Single place where each WinEvent becomes a decision (destroy/hide/minimize/move-size/foreground/reorder/name-change), with per-HWND debouncing/coalescing. |
| `WinEventMonitor` | `SetWinEventHook` wrapper; native callback filters + posts to the UI `SynchronizationContext`. |
| `PersistenceService` | `state.json` layout-intent persistence; single-writer gate, latest-wins generations, atomic durable writes, corruption taxonomy/quarantine. |
| `PendingRecoveryService` / `ProductMutationLease` | Supervised manual recovery of legacy/incomplete journals; secured `Global\TabDock-<SID>` single-instance mutex. |
| Diagnostics (`Diagnostic*`, `NativeSnapshotService`, `EnvironmentFingerprint`, `LoggingService`) | Read-only supportability surface: doctor report, sanitized support bundle, bounded trace ring, held-open rotating log. |

**Data flow / state ownership.** Two persisted files under `%APPDATA%\TabDock\`: `state.json` (layout intent — group/tab metadata only; **no HWNDs, content, or secrets**) and `hidden-windows.json` (crash-recovery journal — per-capture identity + original presentation state). All model state (`Groups`, `Members`, split fields, the captured index, and both shepherd-side journal caches) is **UI-thread owned**. The only off-thread work is the persistence `SaveAsync` durable write (thread-pool), gated by a lock + monotonic generation.

**Concurrency model.** WinEvent hooks are `WINEVENT_OUTOFCONTEXT`, so callbacks run on the installing (UI) thread and `Post` to the same thread to observe post-operation state. Crash handlers marshal to the UI dispatcher (1 s deadline) or are log-only when the runtime is terminating. This makes the whole app effectively single-threaded except for the persistence writer.

**Key invariants (verified present):** journal-durable-before-mutation; clear-journal-only-after-restore; identity `Match` required for any native mutation (`Unverifiable` → recovery pending, `Mismatch` → discard); container pinned immediately below its active guest; at most one native relayout batch per WPF frame; single-writer latest-wins persistence; capture disabled unless both the journal is durably writable and the WinEvent monitor is healthy.

---

## 4. Validation Results

```
Command: git rev-parse HEAD; git branch --show-current; git status --short
Result:  a4e5a3d… ; main ; (untracked audit scratch files only: CODEBASE_AUDIT_v3.md, *-results.md, real-goal.txt; modified .agent/STATE.md)
Observations: The pre-existing working-tree changes (M .agent/STATE.md and several untracked *.md) were present at audit start and are NOT this audit's doing; left untouched.

Command: git ls-remote --heads origin
Result:  a4e5a3d…  refs/heads/main   (ONLY main)
Observations: Docs' "main-only" claim is TRUE on the real remote. `git branch -r` still lists origin/agent/staging — a STALE local remote-tracking ref (not pruned). Hygiene only. [AUDIT-016]

Command: grep -n "public void EmergencyReleaseAll" Services/GroupManager.cs  (and RescueOrphanedWindows / FlushJournalGuarded)
Result:  EmergencyReleaseAll at line 489 (doc cites :395-419); RescueOrphanedWindows at 2426 (doc cites :626-673); FlushJournalGuarded at 529.
Observations: docs/ARCHITECTURE.md line citations are substantially stale despite the doc's "Every claim … verified against current source" header. [AUDIT-011]

Command: grep for SplitPresentationPolicy.{SelectMember,SelectNonMember,RemoveMember,Reconfigure,DefinePair,ResolveNativeTransition} in production trees
Result:  No production call sites; only SplitPresentationController (via ToState()→IsCurrentSettle/ClassifyInteraction) and test files reference the policy's transition methods.
Observations: Confirms the pure policy transitions are test-only. [AUDIT-002]

Command: git ls-files | grep -E '(\.py|goal\.txt|investigation_findings|runtime_audit|stabilization…)' ; grep .gitignore
Result:  tabdock_*.py, goal.txt, investigation_findings.md, tabdock_runtime_audit.md, tabdock_runtime_stabilization_agent_prompt.txt, KNOWN_ISSUES.md are tracked; none are gitignored.
Observations: One-shot source-mutating migration scripts + agent-process artifacts committed to the app root. [AUDIT-012]

Command: find openspec/changes -maxdepth1 -type d (non-archive) ; count unchecked tasks
Result:  8 active (non-archived) changes; all tasks checked except production-release-v1-0-0-closure (9, all BLOCKED_EXTERNAL) and startup-group-visibility (8 real code tasks, NOT external-blocked).
Observations: startup-group-visibility is a designed-but-unimplemented feature; completed changes not archived. [AUDIT-004]
```

The canonical build/test commands (`dotnet build TabDock.sln`, `dotnet test tests/UnitTests`, `scripts/validate.ps1`, `scripts/release-tooling-tests.ps1`) were **not** executed here (read-only audit, no Windows/.NET runtime invoked); `.agent/STATE.md` records them passing at this SHA (146 unit + 139 release-tooling cases). Those counts were not independently reproduced.

---

## 5. Critical Findings

None. After adversarial review, no defect met the CRITICAL bar (catastrophic data loss, security compromise, unrecoverable corruption, or violation of a core guarantee). The recovery-journal, persistence, and identity-gate paths that could produce such outcomes are the most heavily hardened parts of the codebase.

---

## 6. High-Severity Findings

### [AUDIT-003] Stage-B publication workflow attaches a release asset absent from the publishing job

**Severity:** High **Confidence:** Medium-High (static analysis; not runtime-observed — publish path is currently unreachable) **Category:** Release-chain correctness
**Affected areas:** `.github/workflows/publish-release.yml` (publish job "Publish GitHub Release" ~:572-603 and "Verify release assets"), `prepare-release-candidate.yml`, `scripts/release-tooling.ps1` (`Complete-ReleaseRecords`)

**Summary.** The Stage-B `publish` job runs `gh release create … candidate-artifact/release-external-evidence.json …` and then a "Verify release assets" gate that lists `release-external-evidence.json` as required — but that file is never present in the publish job's workspace.

**Evidence.** `release-external-evidence.json` is a `workflow_dispatch` input authored by humans after Stage A. Stage A (`prepare-release-candidate.yml`) uploads only what `release-qualify.ps1` writes: `TabDock.exe`, `release-manifest.json`, `SHA256SUMS.txt` — never the evidence file. The Stage-B **verify** job materializes the evidence into `candidate-artifact/release-external-evidence.json` for validation, but uploads only the `verified-handoff/` artifact (not `candidate-artifact/`). The **publish** job runs on a fresh runner, re-downloads `candidate-artifact` directly from the Stage-A run (Stage-A-only bytes), and no step in the publish job writes the evidence file. Attaching a nonexistent asset path fails `gh release create`, and the follow-up asset check throws on the missing file.

**Failure scenario.** The first time production signing is configured and an operator dispatches `publish-release`, publication aborts (either at `gh release create` or the asset-verification gate) — a release-day outage.

**Impact.** The production publication path is structurally non-functional. Currently **masked** because production signing is `BLOCKED_EXTERNAL`, so Stage A never yields a publishable signed candidate and Stage B is never exercised end-to-end. This is a latent release outage, **not** a weakening of the eligibility gate (eligibility is validated in the verify job before publish).

**Root cause.** The validated evidence lives only in the verify job's ephemeral workspace and is not carried into the publish job (neither via `verified-handoff` nor re-materialized from `inputs.external-evidence`).

**Recommended direction.** Carry the validated evidence forward — add `release-external-evidence.json` to the `verified-handoff` upload, or re-write it from `inputs.external-evidence` at the start of the publish job — so the attached asset and the "Verify release assets" contract are satisfiable.

**Verification recommendation.** Add a workflow-dry-run or a `release-tooling-tests.ps1` static assertion that every asset named in `gh release create` is provably producible in the publish job. The current static suite only checks textual guarantees and cannot catch a missing-artifact path.

---

## 7. Medium-Severity Findings

### [AUDIT-001] Production presentation orchestration is validated only by a real-input harness that never runs in CI (systemic false-confidence)

**Severity:** Medium **Confidence:** High **Category:** Test coverage / architecture
**Affected areas:** `tests/ValidationDriver/**` (all `Scenarios.*`), `.github/workflows/*.yml`, `scripts/validate.ps1`, `ContainerWindow.xaml.cs`, `App.xaml.cs`

**Summary.** The most failure-prone behavior — native window positioning/hide/show, z-order pairing, DPI transitions, real input, drag docking, live crash recovery, reattach, torture — is exercised only by the `ValidationDriver`, which sends real `SendInput` and requires a live interactive desktop. No workflow ever passes `-Scenario`; `build.yml` runs `validate.ps1 -Ci -Publish` and the release workflows run `-Ci` only. In automation these behaviors are effectively untested; a regression there passes CI green.

**Evidence.** `grep` of `.github/workflows/*` for `Scenario|ValidationDriver|dotnet run` returns no matches. `validate.ps1` guards the driver behind an explicit "sends REAL mouse/keyboard input" warning. What *is* automated is genuinely good but narrow: hermetic `--selftest-*` executable modes and the headless xUnit *policy* tests.

**Impact.** Green CI does not attest to the correctness of the shipped window-shepherding behavior; correctness there rests on the manual harness plus the human external gates. This is the root cause under which [AUDIT-002], [AUDIT-005], and [AUDIT-006] are specific instances.

**Root cause.** Inherent to a desktop-automation product on hosted runners; partially mitigated by the mandatory human external-evidence gates in the Stage-B eligibility check.

**Recommended direction.** Keep the human external gates mandatory; consider a self-hosted interactive runner for a smoke subset; and extract more orchestration logic into headless-testable coordinators (see [AUDIT-002]). Document explicitly which scenarios are CI-unreachable.

**Verification recommendation.** A CI-visible manifest of "behaviors covered only by ValidationDriver" that fails if a new scenario is added without a coverage note.

### [AUDIT-002] Duplicated split state machine — the exhaustively-tested policy is not the code production runs, and the two already disagree

**Severity:** Medium **Confidence:** High **Category:** Architecture / duplicated domain rule / test confidence
**Affected areas:** `Services/SplitPresentationController.cs`, `Models/SplitPresentationPolicy.cs`, `Views/ContainerWindow.xaml.cs` (`HandleSplitMemberRemoved` ~:3096), `tests/UnitTests/SplitPresentationPolicyTests.cs`

**Summary.** `SplitPresentationController` is the runtime authority `ContainerWindow` uses. It reimplements every split transition (`DefinePair`, `SuspendForGuest`, `ResumeMember`, `ExplicitExit`, `HandleMemberRemoved`, `FocusMember`) directly on its own fields, and only consults `SplitPresentationPolicy` for `IsCurrentSettle` and interaction classification. The pure `SplitPresentationPolicy.Select*/RemoveMember/ExplicitExit/Reconfigure/ResolveNativeTransition` methods — the ones `STATE.md` advertises as "exhaustively" tested — are **not called by any production code**; grep confirms only tests and the controller's unrelated `ToState()` usage reference the policy.

**Evidence — the two implementations already disagree.** On member removal in the *dormant* state (pair defined but not presented; an unrelated non-member is the visible guest):
- Policy `RemoveMember` (`SplitPresentationPolicy.cs:113-124`): survivor = `ActiveGuest` when the removed member was not active — i.e. it correctly keeps the visible non-member.
- Controller `HandleMemberRemoved` (`SplitPresentationController.cs:175-183`): survivor = *always the other member*, regardless of what is visible.

The application stays correct only because `ContainerWindow.HandleSplitMemberRemoved` **ignores** the controller's returned survivor in the `!wasPresented` branch and re-derives the visible guest itself. Correctness depends on the caller compensating for a wrong return value.

**Failure scenario.** A future change routes the dormant-removal survivor through the controller's return value (the natural thing to do), silently promoting the wrong window; the "exhaustive" policy tests do not catch it because they test the other implementation.

**Impact.** Two divergent encodings of one domain rule; exhaustive tests validate the wrong one, creating false confidence about the production controller.

**Root cause.** The controller was extracted for testability but did not delegate its transitions to the already-tested pure policy; the policy became parallel/test-only.

**Recommended direction.** Make the controller delegate its transitions to `SplitPresentationPolicy` (converting at the `CapturedWindow`↔identity-string boundary), or delete the unused policy transition methods and retarget the exhaustive tests at the controller. Either way, one authority, one test target.

**Verification recommendation.** A test that drives `SplitPresentationController.HandleMemberRemoved` in the dormant case and asserts the survivor matches the policy's `RemoveMember` result.

### [AUDIT-005] `PresentationOperationBudget` tests assert on test-issued fake calls, not production behavior

**Severity:** Medium **Confidence:** High **Category:** Test quality / false confidence
**Affected areas:** `tests/UnitTests/PresentationOperationBudgetTests.cs` (e.g. `NormalTab_A_to_B_…`, `PresentedSplit_To_Guest_Budgets`, `DormantPair_Resume_Budgets`, `SplitMemberFocus_…`)

**Summary.** Several "operation budget" cases manually call `ops.Hide/PositionAndShow/SetForeground` and `budget.RecordLayoutSingle()` in the test body, then assert the fake counted exactly those test-issued calls. The "exactly one show / one foreground / one layout pass per switch" contract these appear to guard is not validated against the production path (the real orchestration lives in `App.xaml.cs`/`ContainerWindow`, only exercised by the never-in-CI ValidationDriver). Where a real object is exercised (`SplitPresentationController` hide/z-order), the *hide* counts are genuine; the show/foreground/layout counts are still the test's own.

**Impact.** A regression that issued a duplicate `PositionAndShow`/`SetForeground`/layout would not be caught. Genuine value is limited to the controller's hide/z-order/fail-closed behavior.

**Root cause.** The production call *sequence* is simulated inside the test instead of invoked end-to-end.

**Recommended direction.** Assert budgets only over operations issued by the class under test, or extract the switch orchestration into a headless coordinator so the single-pass contract is validated without WPF. (Directly linked to [AUDIT-001].)

### [AUDIT-004] `startup-group-visibility` is a specified-but-unimplemented startup z-order feature; completed OpenSpec changes are not archived

**Severity:** Medium **Confidence:** High **Category:** Incomplete functionality / process hygiene
**Affected areas:** `openspec/changes/startup-group-visibility/` (8 unchecked, non-external code tasks), `App.xaml.cs` startup, 8 non-archived active changes

**Summary.** The active OpenSpec change `startup-group-visibility` describes a real reliability/UX fix — restored group containers at startup can appear **behind** a pre-existing window, and TabDock does not reconcile their z-order — with concrete unchecked tasks (`App.ReconcileRestoredContainerZOrder()`, call it after startup, three regression scenarios, build, validate). None are `BLOCKED_EXTERNAL`; the feature is simply unimplemented. Separately, 8 changes remain in the active `changes/` directory though most have all tasks checked; the OpenSpec convention is to archive completed changes.

**Evidence.** `grep -c '\- \[ \]'` on the change's `tasks.md` = 8, all code tasks; `grep` finds no `ReconcileRestoredContainerZOrder` in source. README (§Known limitations, ~:203) independently notes restored-group visibility quirks.

**Impact.** A known startup-visibility defect the team scoped but did not fix; a reader cannot easily tell which active changes are done vs. pending.

**Recommended direction.** Either implement the change or move it to a clearly-labelled backlog; archive the completed changes so `changes/` reflects only in-flight work.

### [AUDIT-006] Persistence `WhenWritesSettledAsync` does not settle all in-flight writes and is not wired into shutdown despite its documented contract

**Severity:** Medium **Confidence:** High **Category:** Shutdown-flush correctness / misleading API
**Affected areas:** `Services/PersistenceService.cs:60-64, :110-116, :149`; `tests/UnitTests/PersistenceSingleWriterTests.cs:222-230`

**Summary.** `SaveAsync` sets `_lastWriteTask = Task.Run(() => CommitJson(...))`; `WhenWritesSettledAsync()` returns only that single most-recently-*enqueued* task. Under thread-pool reordering, an earlier-enqueued task can still be the *winning* (highest-generation) writer and still be running after the awaited task completes. The suite itself works around this by additionally `SpinUntil(!File.Exists(path + ".tmp"))`. The field/method docs call this "the graceful shutdown flush," but grep confirms it is called only from tests — production shutdown uses synchronous `GroupManager.SaveState()`.

**Impact.** Latent. Today none (shutdown re-saves synchronously and supersedes the async path). A future maintainer trusting the name/docs and wiring `await WhenWritesSettledAsync()` as the shutdown barrier would introduce real last-second data loss under load.

**Recommended direction.** Track outstanding writes so the await genuinely drains them, or rename/redocument it as "awaits only the last-enqueued task" and reconcile the field comment with the fact that shutdown uses `SaveState`.

---

## 8. Low-Severity Findings

### [AUDIT-007] Persistence latest-wins depends on single-thread issuance; generation is claimed after the snapshot is built
**Severity:** Low (latent) **Confidence:** High **Category:** Race / TOCTOU. `PersistenceService.cs:124-131, :143-150`.
`BuildStateJson` runs first, then `Interlocked.Increment` claims the generation — so build-order and claim-order can invert if two callers ever run concurrently, letting an older snapshot win at the `CommitJson` gate. The class documents a guarantee ("a delayed/stale async snapshot can never overwrite a newer attempted save") that actually rests on an unstated invariant: **all callers are on the UI thread** (verified true for every current caller). **Direction:** claim the generation before/atomically with the snapshot, or assert the single-thread precondition at the API boundary.

### [AUDIT-008] `state.json` backup copy is non-atomic and the atomic replace is not directory-fsync'd
**Severity:** Low **Confidence:** Medium-High **Category:** Durability. `PersistenceService.cs:244-249, :704-716`; journal equivalent `WindowShepherdService.cs:2380-2406`.
The `.bak` is produced with a plain `File.Copy` (not write-through), and `File.Move(tmp→primary)` replaces the directory entry without an explicit directory flush, so on **power loss** (not process kill) the rename or the backup may not be durable. The primary is never *torn* (main invariant holds); the residual risk is only backup integrity + rename durability under power loss — acceptable under the "layout intent, best-effort" contract, but the code presents `.bak` as a first-class part of "the same save transaction." **Direction:** write-through the `.bak` (or rename old-primary→`.bak` before writing new), and/or use `File.Replace`; document the power-loss limitation.

### [AUDIT-009] Out-of-band `WM_GETMINMAXINFO` probe assumes guest handlers are side-effect-free
**Severity:** Low **Confidence:** Medium **Category:** Cross-process messaging. `WindowShepherdService.cs:1043-1073`.
`SendMessageTimeout(WM_GETMINMAXINFO)` is sent to a foreign guest as a *query*. Marshalling is correct (system-marshalled message, `wParam=0`, buffer pre-seeded to zero — which correctly fixed an observed 65,535px garbage minimum, `fDeleteOld:false` correct, 100 ms `SMTO_ABORTIFHUNG` bound). A guest whose handler has side effects (recomputes/caches layout assuming an in-progress sizing loop) could misbehave. Bounded by the timeout + per-`CapturedWindow` cache; no TabDock-side corruption. **Direction:** document the residual assumption in the method summary; keep the technique (no better native API exists).

### [AUDIT-010] `PreservePendingJournal` write-then-delete can duplicate legacy recovery evidence on interruption
**Severity:** Low **Confidence:** Medium **Category:** Recovery-evidence lifecycle. `WindowShepherdService.cs:2179-2208`.
The `.pending` sidecar is written durably, then the original is deleted. A crash between the two leaves both; the next launch re-reads the still-legacy original and produces a second byte-identical `.pending.001` before deleting the original. Result: duplicate manual-recovery sidecars (`PendingRecoveryService.Discover` lists both). No auto-mutation risk (legacy is never auto-rescued); cosmetic/operational. **Direction:** dedup by content hash before writing a new sidecar, or make the preserve step replay-safe.

### [AUDIT-011] `docs/ARCHITECTURE.md` line-number citations are substantially stale despite the "verified against current source" claim
**Severity:** Low **Confidence:** High **Category:** Documentation / implementation divergence. `docs/ARCHITECTURE.md`.
The doc header asserts "Every claim below was verified against current source; citations use `path.cs:line`," yet cited lines are off by 100–1,800 lines: `EmergencyReleaseAll` cited `GroupManager.cs:395-419` (actual 489), `RescueOrphanedWindows` cited `WindowShepherdService.cs:626-673` (actual 2426), `FlushJournalGuarded` cited `App.xaml.cs:320-330` (actual 529). The prose is accurate; only the anchors drifted. Because the doc explicitly promises verified anchors, the drift erodes trust and wastes navigation time for the agents this doc targets. **Direction:** regenerate anchors or switch to symbol references; soften the "verified" header, or add a CI check that citations resolve.

### [AUDIT-012] One-shot source-mutating migration scripts and agent-process artifacts are committed to the application root
**Severity:** Low **Confidence:** High **Category:** Repo hygiene / stale code. Repo root.
Tracked in the app root: `tabdock_persistence_single_writer_fix.py` and `tabdock_runtime_hotfix.py` (one-shot Python scripts that **rewrite `Services/*.cs`**), plus `goal.txt` (45 KB), `tabdock_runtime_audit.md`, `tabdock_runtime_stabilization_agent_prompt.txt` (22 KB), and `investigation_findings.md`. None are gitignored. The `.py` scripts are already-applied migrations; re-running one against already-fixed source is at best a no-op and at worst a corrupting re-edit, and they invite exactly the kind of automated source mutation the project otherwise guards against. `KNOWN_ISSUES.md` is a historical (resolved) bug-hunt log kept intentionally. **Direction:** move applied migration scripts + agent prompts/notes into `.agent/` or `docs/internal/` (or delete), leaving the product root to product files.

### [AUDIT-013] `release.yml` RC `signing-required` input is a silent no-op (boolean-vs-string coercion)
**Severity:** Low **Confidence:** Medium **Category:** CI expression footgun. `.github/workflows/release.yml:123`.
`RELEASE_SIGNING_REQUIRED: ${{ inputs.signing-required == 'true' }}` compares a `type: boolean` input to a string literal; GitHub coerces to number, making `true == 'true'` → `1 == NaN` → **false** always. So an RC dispatched with `signing-required=true` still produces an unsigned/NOT_CONFIGURED RC. Production is unaffected (`prepare-release-candidate.yml` hard-codes `'true'` as strings). RC never publishes, so impact is a false sense that RC signing was enforced. **Direction:** reference the boolean directly (`${{ inputs.signing-required }}`). The 130-case suite doesn't cover this expression.

### [AUDIT-014] `RequestRelayoutFinalPassTests.UnchangedLayoutUpdated_ProducesNoRelayout` is a tautology that does not test its stated behavior
**Severity:** Low **Confidence:** High **Category:** Test quality. `tests/UnitTests/RequestRelayoutFinalPassTests.cs:71-80`.
The body unconditionally calls `RequestRelayout` and asserts it executed once; it never models an unchanged content rect nor the suppression decision (which lives in `ContainerWindow_LayoutUpdated`, not exercised). It is functionally identical to the idle case above it, implying coverage of the "skip relayout when rect unchanged" optimization that it does not provide. **Direction:** drive the actual change-detection path or rename/remove.

### [AUDIT-015] `GetWindowLongPtr`/`SetWindowLongPtr` bind via implicit ANSI fallback rather than an explicit W entry point
**Severity:** Low **Confidence:** High **Category:** P/Invoke resolution. `NativeMethods.cs:84-88`.
Both declare `EntryPoint = "GetWindowLongPtr"` (no `*A`/`*W` suffix) with default `CharSet=Ansi`; the loader binds `…PtrA`. Harmless on x64 (only shipping target; the sole caller uses the numeric `GWL_EXSTYLE` where A/W is irrelevant), and every other string-bearing import in the file uses `CharSet.Unicode`. It would `EntryPointNotFoundException` on x86 (no `*PtrA` export), which is not built. **Direction:** add `CharSet = CharSet.Unicode` (or bind `…PtrW`) for consistency.

---

## 9. Optimization Opportunities

The performance posture is already strong and well-documented (the "PERF25" pass): O(1) HWND→member index, hooks gated on `IsMonitoringNeeded`, coalesced per-frame relayout, cheap hot-path logging (no `DescribeWindow` on the drag tick), bounded/cached min-track probing, name-change debouncing, and an off-thread persistence writer. Remaining opportunities are minor:

- **UI-thread stat calls in the async save path (Low).** `BuildStateJson` calls `File.Exists`/`File.GetAttributes` (`PersistenceService.cs:209-213`) synchronously on the UI thread even in `SaveAsync`. The heavy durable write is correctly off-thread; only these light stats remain on the input/render turn (matters only on a slow/network `%APPDATA%`). Consider moving the path classification into the off-thread commit.
- **`TrimMode partial` is dead configuration ([AUDIT-017 / INFO]).** No `PublishTrimmed=true` exists anywhere, so trimming never runs — which is the *safe* outcome for WPF (reflection/COM/XAML). No bundle-size win is being realized and the property misleads; either remove it or (carefully) enable trimming with WPF-aware roots. Not recommended to enable for a WPF app.
- **Diagnostic SHA-256 of the artifact** is computed only on explicit export, never on startup — already optimal.

No speculative micro-optimizations are worth pursuing; the hot paths are already at ~one integer comparison per drag tick.

---

## 10. Architectural Improvement Opportunities

1. **Collapse the duplicated split state machine ([AUDIT-002]).** The single highest-value architectural cleanup: one authority (`SplitPresentationController` delegating to the pure `SplitPresentationPolicy`), one test target. Eliminates the class of "tests validate code we don't run" bug.
2. **Make presentation orchestration headless-testable ([AUDIT-001]/[AUDIT-005]).** The switch/split/relayout sequencing is entangled with WPF in `ContainerWindow`/`App`; the budget tests resort to simulating the sequence. Extracting the *decision* sequence into a coordinator that emits an ordered list of presentation ops (already partly modelled by `IPresentationOperations`/`IPresentationBudgetSink`) would let the "single-pass" contracts be asserted against real code in CI.
3. **`ContainerWindow.xaml.cs` is a 3,584-line god-object.** Split extraction has begun (controllers), but WndProc, drag/reorder, chrome popups, close/delete prompts, capture panel, min-track constraint, and diagnostics still co-reside. Continued safe extraction (drag controller, chrome/popup z-order coordinator) would improve testability and reduce the surface where the many carefully-reasoned invariants interact.
4. **Structural single-instance enforcement.** The persistence single-writer guarantee currently rests on a *procedural* invariant (all callers on the UI thread, [AUDIT-007]). Claiming the generation atomically at the API boundary would make the guarantee structural rather than caller-dependent.

Do **not** over-abstract elsewhere: the "no interface seam where there's one implementation" decisions (e.g. the container registry passed as a live dictionary to `GuestLifecycleService`) are appropriate and should be preserved.

---

## 11. Test and Quality-Gate Gaps

- **Highest priority:** end-to-end presentation/native behavior is CI-untested ([AUDIT-001]); the human external gates are the only backstop.
- **False-confidence tests:** budget tests assert on test-issued calls ([AUDIT-005]); one relayout test is a tautology ([AUDIT-014]).
- **Duplicated-rule blind spot:** exhaustive `SplitPresentationPolicy` tests do not cover the production `SplitPresentationController` ([AUDIT-002]).
- **CI expression untested:** the RC `signing-required` coercion bug ([AUDIT-013]) is invisible to the 130-case static suite.
- **Genuinely strong tests (keep):** `PersistenceSingleWriterTests` (real 1,000-write concurrency hammer + torn-write/`.tmp`/`.bak` invariants against the real service), `SplitPresentationPolicyTests`/`SplitInteractionPolicyTests` (real pure-state-machine boundaries + generation monotonicity), `Geometry`/`Group`/`Persistence`/`Converter`/`HardeningRegression` tests, and the deterministic `release-tooling-tests.ps1` adversarial suite (asserts verdict **and** failure text).
- **Recommended additions:** a controller-level dormant-removal survivor test ([AUDIT-002]); a publish-job asset-producibility check ([AUDIT-003]); a doc-anchor resolution check ([AUDIT-011]).

---

## 12. Security and Privacy Assessment

**No concrete vulnerability found.** The app is `asInvoker` (never auto-elevates), refuses to capture elevated windows unless itself elevated, and fails closed on indeterminate elevation. There is no network surface, no dynamic evaluation, no injection sink (the only inter-process message sends are the system-marshalled `WM_GETMINMAXINFO` query and a bounded `WM_CLOSE` PostMessage gated by exact identity re-verification). Subprocess use is limited to the guarded diagnostic/validation tooling.

**Single-instance lease** (`ProductMutationLease`): secured `Global\TabDock-<current-user-SID>` mutex created via `MutexAcl` with a protected DACL granting only `Synchronize | Modify | ReadPermissions` to the current SID — no Everyone/World/inherited access; SID/ACL failure is fail-closed; `AbandonedMutexException` is treated as acquired; every failure path disposes the handle. Verified sound.

**Privacy** (a stated design guarantee — verified thorough): `DiagnosticEnvironmentService.SanitizeText` redacts profile roots in both separator forms (`%APPDATA%`/`%USERPROFILE%`/`%LOCALAPPDATA%`), username and `DOMAIN\user`, a catch-all absolute-path regex, and secret/`Bearer`/secret-token patterns; JSON is redacted by sensitive key name while staying parseable; the support-bundle log tail keeps only tagged lines, **drops** every title-bearing line (`title changed`, `Shepherd-captured`, `Created group`, …), and redacts single-quoted values. Window titles are deliberately hashed where included (`HashTitle`), and `DescribeWindow` emits only HWND/rect/state flags (no title) — I specifically checked that the retained `ENV[container]`/`STATE[settled]` lines cannot leak a raw title through `DescribeWindow`. Nothing is uploaded automatically.

**Hardening opportunities (not vulnerabilities):** capture identity tokens are process-local while the `SetProp` marker name is machine-global ([AUDIT-018, INFO]) — same-HWND cross-instance capture relies on the pre-`SetProp` check rather than token uniqueness; only relevant to an unusual multi-instance-same-window race. The username replacement in `SanitizeText` can over-redact for very short usernames (harmless for privacy).

**Supply chain / CI security (verified strong):** all `actions/*` and the DigiCert action pinned to full 40-char SHAs (enforced by the regression suite); `persist-credentials: false` on every checkout; `contents: write` isolated to the single publish job after all gates; `npm ci --ignore-scripts`; a genuine NuGet-audit gate (`-p:NuGetAudit=true -warnaserror:NU1900-1904`, the effective gate; `dotnet list --vulnerable` is report-only [AUDIT-019, INFO]); production signing allowlisted to `digicert-stm`/`CLOUD_HSM` with mock modes that can never claim an approved provider; a fail-closed publication-eligibility gate (SHA/version/run-id/provider/key-protection/cert-identity/timestamp/on-disk-`signtool verify /pa /v /tw` cross-check).

---

## 13. Reliability and Failure-Recovery Assessment

The strongest subsystem. Verified behaviors:
- **Crash/kill after capture:** the guest process survives (never reparented); the journal remembers its full original state; startup `RescueOrphanedWindows` restores it after strong identity re-verification (IsWindow + PID + GUI-thread + exe + class + process-start-time + per-HWND token), touching a recycled HWND never (no inherited props → token mismatch → discarded).
- **Interrupted rescue / repeated launches:** idempotent — placement/show/transitions reapplied, token removal ends the loop; a post-token-removal crash yields a positive `Mismatch` that discards the stale entry rather than double-restoring.
- **Partial persistence write:** primary never torn (WriteThrough `.tmp` → atomic rename); missing-primary + valid-backup recovers; unreadable/future-version primary is preserved and blocks overwrites (never mistaken for empty state); proven corruption is quarantined before a backup becomes authoritative.
- **Shutdown/session-ending/crash paths:** every path runs emergency-release + journal flush; session-ending is one-way and idempotent even if Windows cancels the logoff; terminating AppDomain exceptions are log-only (no off-thread journal mutation), non-terminating ones marshal to the UI thread with a 1 s deadline.
- **Recovery-pending semantics:** an `Unverifiable` identity never mutates and never detaches a logical member without recovery evidence; `CloseGroup`/`EmergencyReleaseAll` retain pending members and move on rather than looping.

Residual gaps: power-loss (vs process-kill) durability of the rename/backup ([AUDIT-008]); potential duplicate legacy `.pending` sidecars ([AUDIT-010]); the misleading shutdown-flush API ([AUDIT-006]).

---

## 14. Performance and Scalability Assessment

Scale is bounded by design (a handful of groups × a handful of tabs per human user), so absolute scalability is a non-issue. The relevant metric is per-frame cost during drag/resize, which is already minimized (coalesced single relayout per frame, one integer comparison per drag tick, redundant-glue guards, refused-pane guard against resize wars). Startup is O(groups) with per-group native fingerprint logging (bounded). No N+1, no unbounded structures, no polling loops (`EVENT_OBJECT_LOCATIONCHANGE` and periodic health polls are deliberately avoided). The only residual per-frame system messages are bounded and cached. No scaling limit of concern.

---

## 15. Maintainability / Technical Debt Assessment

Disproportionately expensive-to-change areas: `ContainerWindow.xaml.cs` (3,584 lines; dense, heavily invariant-laden — every change risks interacting with the many documented races) and `PendingRecoveryService.cs` (2,049 lines; large interrupted-transaction state space). Both are *well-commented* (the comments explain *why*, not *what*, and encode hard-won bug history), which materially offsets the size. The duplicated split machine ([AUDIT-002]) and the CI-untested orchestration ([AUDIT-001]) are the debt items most likely to cause a silent regression. Repo-root clutter ([AUDIT-012]) and doc-anchor drift ([AUDIT-011]) are low-cost hygiene debt.

---

## 16. Documentation / Implementation Divergence

- **Stale line anchors** in `docs/ARCHITECTURE.md` despite a "verified against current source" header ([AUDIT-011]).
- **`GetJournalCache` comment** claims rescue "unconditionally deletes hidden-windows.json"; rescue actually deletes / rewrites-with-retry / preserves-on-failure / `.pending`-quarantines depending on state (`WindowShepherdService.cs:1996-2004` vs `:2437-2533`). Lazy-load remains correct, but the stated reasoning is wrong ([AUDIT-020, INFO]).
- **WINDOWPLACEMENT rationale inverted:** the doc/self-test comment frames 44 bytes as a deviation from a "60-byte SDK contract"; in `winuser.h`, `rcDevice` is `_MAC`-only, so **44 is the canonical x64 SDK size** and 60 is the deviation. The *code* is correct (44, `length = Marshal.SizeOf<WINDOWPLACEMENT>()`, round-trip self-test); only the rationale would mislead a future maintainer into "fixing" the struct and breaking every placement restore ([AUDIT-021, INFO]). `NativeMethods.cs:768-791`, `DiagnosticCommandLine.cs:363-374`.
- **`WhenWritesSettledAsync` docs** describe a shutdown flush the method neither performs completely nor is wired to ([AUDIT-006]).
- **STATE.md "SplitPresentationPolicy exhaustive"** overstates what the production path is tested against ([AUDIT-002]).

---

## 17. Dead / Stale / Suspicious Code

- `SplitPresentationPolicy.{SelectMember, SelectNonMember, RemoveMember, ExplicitExit, Reconfigure, ResolveNativeTransition}` and static `DefinePair`/`NoPair` — **no production callers** (test-only); either wire in or retire ([AUDIT-002]).
- `TabDock.csproj <TrimMode>partial</TrimMode>` — dead without `PublishTrimmed` ([AUDIT-017]).
- Root `tabdock_*.py` migration scripts — already applied; stale/hazardous in-tree ([AUDIT-012]).
- Stale local `origin/agent/staging` remote-tracking ref (the real remote has only `main`) — `git remote prune origin` ([AUDIT-016]).
- `AllocateRecoveryToken` 32-bit branch can throw `OverflowException` instead of retrying (`PendingRecoveryService.cs:781-795`) — dead on the x64-only target ([AUDIT-022, INFO]).

No commented-out code blocks, `.only`/skipped tests, or reachable debug/admin backdoors were found.

---

## 18. Cross-Cutting / Systemic Issues

1. **Test authority vs. runtime authority mismatch** — the recurring root cause behind [AUDIT-001], [AUDIT-002], [AUDIT-005]: the pure, headless-testable layers are excellent and thoroughly tested, but they are not always the code production runs, and the code production *does* run (WPF/native orchestration) is only validated by a non-CI harness + human gates.
2. **Procedural (not structural) invariants** — several correctness guarantees rest on "all callers are on the UI thread" (persistence generation ordering [AUDIT-007]; shepherd journal-cache/`_minTrack`/failure-log collections; the split fields). True today and well-commented, but unenforced; a single future off-thread caller breaks them silently. This is an acceptable, deliberate tradeoff — worth keeping visible.
3. **Documentation drift** — anchors, rationale, and API contracts have drifted from code in several places ([AUDIT-011], [AUDIT-020], [AUDIT-021], [AUDIT-006]); the prose is trustworthy, the specifics less so.

---

## 19. Improvement Backlog

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | AUDIT-003 | High | Stage-B publish attaches an absent evidence asset | Release-day publish outage (latent) | S | Med-High |
| 2 | AUDIT-002 | Medium | Duplicated split state machine; policy is test-only & already disagrees | Silent split regressions; false test confidence | M | High |
| 3 | AUDIT-001 | Medium | Production orchestration CI-untested (ValidationDriver only) | Regressions pass CI green | L | High |
| 4 | AUDIT-006 | Medium | `WhenWritesSettledAsync` misleading / not wired | Latent shutdown data loss on future use | S | High |
| 5 | AUDIT-005 | Medium | Budget tests assert on test-issued calls | Duplicate-op regressions uncaught | M | High |
| 6 | AUDIT-004 | Medium | `startup-group-visibility` specified but unimplemented | Restored container hidden behind windows | M | High |
| 7 | AUDIT-013 | Low | RC `signing-required` boolean coercion no-op | Ineffective RC signing toggle | XS | Med |
| 8 | AUDIT-007 | Low | Persistence latest-wins relies on single-thread issuance | Latent stale-write if off-thread caller added | S | High |
| 9 | AUDIT-008 | Low | Backup non-atomic; rename not dir-fsync'd | Power-loss backup/rename durability | S | Med-High |
| 10 | AUDIT-010 | Low | `.pending` write-then-delete can duplicate evidence | Duplicate manual-recovery sidecars | S | Med |
| 11 | AUDIT-009 | Low | Out-of-band WM_GETMINMAXINFO side-effect assumption | Rare guest-specific misbehavior | XS | Med |
| 12 | AUDIT-011 | Low | ARCHITECTURE.md stale line anchors | Wasted navigation; eroded trust | S | High |
| 13 | AUDIT-012 | Low | Committed migration scripts / agent artifacts in root | Hazardous re-run; clutter | XS | High |
| 14 | AUDIT-014 | Low | Tautological relayout test | False coverage impression | XS | High |
| 15 | AUDIT-015 | Low | `*WindowLongPtr` ANSI-fallback binding | x86-only latent; consistency | XS | High |
| 16 | AUDIT-016..022 | Info | prune ref; TrimMode; cross-thread fields; token scope; NuGet report-only; comment/rationale; 32-bit token | Clarity/hardening | XS–S | Mixed |

---

## 20. Recommended Remediation Order

1. **Fix the release publish path ([AUDIT-003])** before any real publication attempt — it is the only finding that would cause an outright failure the moment the external gates clear.
2. **Unify the split state machine ([AUDIT-002])** and add the controller-level dormant-removal test — closes a real divergence and the false-confidence gap.
3. **De-risk the shutdown-flush API ([AUDIT-006])** and **the persistence generation-ordering invariant ([AUDIT-007])** — cheap, prevents future data-loss footguns.
4. **Address CI coverage of orchestration ([AUDIT-001], [AUDIT-005])** — structural, larger; pursue via headless coordinator extraction ([AUDIT-010/architecture]).
5. **Implement or reclassify `startup-group-visibility` ([AUDIT-004]).**
6. **Durability + recovery hygiene ([AUDIT-008], [AUDIT-010]).**
7. **CI/build cleanups ([AUDIT-013], [AUDIT-015], [AUDIT-017], [AUDIT-019]).**
8. **Documentation + repo hygiene ([AUDIT-011], [AUDIT-012], [AUDIT-016], [AUDIT-020], [AUDIT-021]).**

Dependency note: [AUDIT-005] resolution is easiest *after* the [AUDIT-002]/orchestration-extraction work; do them together.

*This is a recommendation only. No remediation was performed.*

---

## 21. Positive Findings (verified by inspection)

- **`WindowShepherdService` identity gate & journal ordering** — journal is durable before the first DWM/position/hide mutation on every path; cleared only after a fully-verified restore; a cheap generation re-check is the *last* managed step before each native call; `Unverifiable` never mutates. Exemplary.
- **WinEvent pipeline** — OUTOFCONTEXT hooks confine callbacks to the UI thread; bounded 3-attempt install transaction that never overwrites a failed-unhook handle; the delegate is field-rooted (no GC-callback crash); re-validation at both callback and dispatch time; deliberate "Post not Send" to observe post-operation state.
- **Persistence** — genuine torn-write immunity, generation-gated latest-wins verified against a real 1,000-write concurrency hammer, and a careful missing/corrupt/unreadable/unsupported taxonomy that refuses to overwrite unknown state.
- **Privacy redaction** — multi-layered and title-safe (see §12).
- **Release/CI tooling** — fail-closed eligibility gate, SHA-pinned actions, least-privilege permissions, mock-signing that can never masquerade as production, and a genuinely adversarial deterministic regression suite.
- **DPI/native hygiene** — leak-free PMv2 probe lifecycle (helper HWND + thread-context restored in `finally`, enumerated by self-test), correct HDWP transaction lifetime, overflow-safe DPI math, exact 44-byte WINDOWPLACEMENT contract with a runtime round-trip self-test.
- **GroupManager active-index handling** — "follow the member, not the slot" on release; the O(1) index maintained *structurally* via `CollectionChanged` rather than at every mutation site.

These are load-bearing and should not be casually rewritten.

---

## 22. Audit Coverage / Confidence

**Inspected deeply (High confidence):** `WindowShepherdService`, `ContainerWindow`, `GroupManager`, `GuestLifecycleService`, `WinEventMonitor`, `PersistenceService`, `SplitPresentationController`/`SplitPresentationPolicy`, `DiagnosticEnvironmentService` (privacy), `App.xaml.cs` (startup + all shutdown/crash paths), core `NativeMethods`/DPI/identity (via a full subsystem sweep), tests/CI/release tooling (full sweep), the architecture/state docs, and repo/git structure.

**Inspected moderately (Medium confidence):** `PendingRecoveryService` (2,049 lines) — read in full; the phase-gated, generation-revalidated, user-confirmed design appears sound and idempotent, but the combinatorial interrupted-transaction × ledger-rebind × candidate-mismatch space was not exhaustively proven. `DiagnosticCommandLine` argument surface — lightly covered (local single-user desktop tool; low risk, but path-handling of user-supplied export targets was not deeply traced). ViewModels/Converters/CapturePicker — the split-relevant paths were traced; the converters' `ConvertBack` and picker enumeration filters were not exhaustively re-read (the interrupted MVVM sweep confirmed the split-projection finding before terminating).

**Could not validate (runtime/environment):** power-loss durability of `File.Move`/`Flush(true)`; `SetProp` token survival across `TerminateProcess`; GitHub Actions expression coercion ([AUDIT-013]); the actual failure of `gh release create` on a missing asset ([AUDIT-003]); guest-specific `WM_GETMINMAXINFO` side effects. All reasoned from documented contracts.

**Deserving a second specialized audit:** `PendingRecoveryService` interrupted-transaction permutations (a focused fault-injection review), and a headless-orchestration test-coverage initiative ([AUDIT-001]).

---

## 23. Final Assessment

- **Overall health:** Strong. This is a carefully-engineered, deeply-audited application whose safety-critical machinery (identity, journal, persistence, privacy, crash recovery) is genuinely robust. No CRITICAL/HIGH runtime defect was found.
- **Largest systemic risk:** the gap between what is exhaustively tested (pure policy layers) and what actually runs in production (WPF/native orchestration), validated in automation only by proxy — surfacing concretely as the duplicated split machine and the CI-untested presentation path.
- **Strongest subsystem:** `WindowShepherdService` (identity gate + crash-recovery journal).
- **Weakest subsystem:** the release *publication* workflow (Stage B) — structurally unable to complete as written, though latent behind the intentional external-signing block.
- **Highest-value improvement:** unify the split state machine and make the presentation orchestration headless-testable ([AUDIT-002] + [AUDIT-001]).
- **Most urgent remediation:** fix the Stage-B publish asset defect ([AUDIT-003]) before the external gates clear.
- **Remaining uncertainty:** the interrupted-transaction space of `PendingRecoveryService` and all live-desktop native behavior, neither of which could be exercised in a read-only, no-runtime audit.

The project's own honest verdict — **repository-side hardening complete; GO FOR RELEASE CANDIDATE / BETA ONLY; v1.0.0 prepared but intentionally not published pending real external evidence** — is accurate, with the one caveat that the publish path itself ([AUDIT-003]) should be corrected before that publication is attempted.

---

*End of audit. No application code, tests, configuration, CI, or documentation (other than this file) were modified during this audit.*
