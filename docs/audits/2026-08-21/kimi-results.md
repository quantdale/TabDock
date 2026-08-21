# Codebase Deep Audit

> Consolidated from a 12-agent parallel read-only audit of the TabDock repository
> (C#:12 / .NET 8 / WPF / Windows / P-Invoke). Source of raw findings:
> `AgentSwarm-chatcmpl-tool-...-3acdb80a-294c-4fe3-9dc7-26ab30c5d9c6.txt`
> (≈1290 lines, 12 scope sections).
>
> This report is read-only and does NOT implement any finding. Every recommendation is directional only.

## 1. Executive Summary

TabDock is a Windows desktop utility that uses the **Shepherd model**: captured windows
remain independent top-level HWNDs positioned over a container rather than reparented. The
audit found a codebase that is, in its security- and data-durability-critical core,
**unusually well-engineered**: atomic crash-journal writes, fail-closed corruption handling,
layered window-identity gating, correct P/Invoke marshaling, a sound two-stage release
pipeline, and a rich deterministic self-test harness. No Critical defects and no exploitable
security vulnerabilities were found.

The weaknesses are concentrated in three areas:
1. **Process-scoped vs. window-scoped identity** — identity is keyed on PID/thread/class/exe/
   process-start, not the individual window instance; the per-capture token is the only
   instance proof and is applied late (capture) or deliberately removed (release). This is the
   single most important *new* systemic risk from the deep dive: it can drive a destructive
   `WM_CLOSE` to the wrong (recycled, same-process) window (AUDIT-068, High) and also weakens
   capture disambiguation (AUDIT-026, Medium).
2. **Split-presentation dual-source-of-truth vestigial layer** — the split subsystem accreted a
   dead parallel state/observation layer (coordinator generation token, controller settle fields,
   classifier step-7, budget sink) that unit tests exercise in isolation but production never
   drives (AUDIT-004/005/029/031/033/034 cluster). This is the dominant source of test/prod
   divergence and an ordinary-click interaction freeze (AUDIT-004).
3. **Release CI** — the two release workflows cannot upload their artifacts (AUDIT-002, High),
   so the release/RC pipelines are non-functional on first real use; and the deterministic
   self-tests, while substantive, are only triggered via `--selftest-diagnostics`/CI and not by
   the running app (AUDIT-003, High).

Findings skew toward Medium/Low maintainability, doc/code divergence, and bounded
performance/robustness items. The persistence/recovery journal — alarmingly characterized in the
first pass as fragile — is in fact **atomic, fail-closed, idempotent (per-window token), and
resistant to HWND reuse/closed windows**, and the release-signing chain is genuinely
defense-in-depth with four independent gates. The two largest *systemic* risks (see §18) are
**(a) process-scoped vs. window-scoped identity** (root cause of AUDIT-026 + AUDIT-068 — the
single most important new systemic risk, since it can drive a destructive `WM_CLOSE` to the
wrong window) and **(b) the split-presentation subsystem's dead parallel state/observation
layer** (AUDIT-004/005/029/031/033/034). Note AUDIT-001 was **downgraded** from High to Low: it
is **not** the P0 the first pass claimed (the off-UI race premise was refuted; see §8).

**Production-readiness verdict:** The product's core data-durability and window-identity
behavior is production-grade, and the persistence layer is sounder than the first pass implied.
However, as audited it is **not yet release-ready** for an automated/published release: the
release CI is broken (AUDIT-002), the self-tests are only run via CLI/CI (AUDIT-003), and the
most serious correctable defect is now **AUDIT-068** — a close-group `WM_CLOSE` that can hit a
recycled same-process window. The unit test suite also has a **real flake** (AUDIT-069) that
undermines the previously-claimed all-green status. Day-to-day interactive use by the current
developer is low-risk because the fail-closed production paths are strong, the supervised
ValidationDriver exercises the recovery flows manually, and the sole High concurrency defect
from the first pass no longer stands.

**Overall confidence:** High for static-correctness and architecture conclusions; Medium for
concurrency/performance severities that depend on live-desktop timing (no interactive session
was available; all findings are from source reading + the project's own deterministic tests).

### Findings by severity

| Severity | Count |
|---|---|
| Critical | 0 |
| High | 3 |
| Medium | 20 |
| Low | 46 |
| Info | 8 |
| **Total** | **77** |

> Severity totals above reflect the second-pass deep-dive re-ratings (6 scoped subagents; the
> unit suite was actually executed this time). Notable deltas vs. the first pass: AUDIT-001
> downgraded High→Low (its off-UI race premise was refuted), AUDIT-011 downgraded High→Medium
> (Load-failure *is* exercised in CI via `--selftest-diagnostics`), AUDIT-023/026/033 upgraded
> Low→Medium, AUDIT-005 downgraded Medium→Low, AUDIT-039/043 reclassified Low→Info (the latter a
> false positive), and four new findings added (AUDIT-068 High, AUDIT-069 Medium, AUDIT-070/071
> Low). The pre-existing P3 Info items (journal-cache divergence, dead converter resource,
> missing OpenSpec, publish smoke, diagnostic allocations, sanitizer edges) were renumbered
> AUDIT-074–079 to make room for the new IDs.

### Most important architectural observations
- Shepherd/no-reparent invariant is genuinely preserved in production (only `NativeHwndHost`
  sets `WS_CHILD` on TabDock's *own* marker window; `SetParent` is never introduced into the product).
- "Exactly two" split invariant is structural (`_splitLeft`/`_splitRight` fields), not a fragile count.
- Crash-journal writes are `WriteThrough` + `Flush(true)` + atomic temp-file `Move`, preceding every
  dangerous native mutation — a textbook safe-journal design.
- Layered window identity (token + PID + thread + class + exe + process-start) rejects same-process
  HWND recycling and stale `CapturedWindow` objects.
- Cross-file domain constants are single-sourced (no conflicting duplicate magic numbers).
- But: several safety/perf features (relayout generation guard, coordinator budget sink, controller
  settle state) are dead in shipping code — the wiring was never connected.

## 2. Audit Scope

**Areas inspected (12 scopes / subagents):**
- SCOPE-MODELS — Models, persistence & recovery journal
- SCOPE-NATIVE — native capture, shepherding & HWND lifetime
- SCOPE-SPLIT — split-screen, presentation & DPI
- SCOPE-VIEW — ViewModels & WPF Views lifecycle
- SCOPE-CONC — concurrency, hotkeys, timers, session & shutdown
- SCOPE-DIAG — diagnostics, logging, privacy & observability
- SCOPE-TESTS — unit & e2e test quality & gaps
- SCOPE-BUILD — build system, CI/CD, scripts & dependencies
- SCOPE-ARCH — architecture, docs/openspec divergence & cross-file consistency
- SCOPE-SEC — security, elevation, P/Invoke safety & secret handling
- SCOPE-PERF — performance, scalability & resource lifecycle
- SCOPE-SELFTEST — self-test subsystem & runtime-stabilization resilience

**Languages/frameworks:** C# 12, .NET 8 (WPF, Windows), P/Invoke (`user32`/`dwmapi`/`kernel32`/`advapi32`),
PowerShell (release/validation scripts), YAML (GitHub Actions), xUnit.

**Apps/packages/services covered:** `TabDock` (single-product project), `TabDock.Spike` (experimental, excluded
from most deep reads), `TabDock.UnitTests`, `TabDock.ValidationDriver` (real-input harness), `tests/Performance`,
`WindowShepherdService`, `GroupManager`, `WinEventMonitor`, `SplitPresentationController`/`Coordinator`,
`PersistenceService`, `DiagnosticReportService`, `HotkeyService`, `ProductMutationLease`, plus the full
`*SelfTest.cs` set and `openspec/specs|changes`.

**Validation commands executed:** `dotnet build TabDock.sln -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**
(see §4). No `dotnet test`, no interactive Windows run (read-only + no display session).

**Areas excluded + why:**
- Live interactive Windows desktop / SendInput ValidationDriver scenarios — require a supervised, visible
  display and real external guest windows; forbidden by read-only + non-interactive constraint.
- Spike project deep logic — experimental; only flagged where it touches the no-reparent invariant.
- Providerer-implementation of release signing against real DigiCert/HSM — no signing secrets available.

**Limitations:** All findings are static-analysis + project self-test source reading. Concurrency, race,
DPI, and pixel-flakiness severities are reasoned from code paths, not reproduced. Verification Limitations
from each scope are preserved in §4 and per-finding Confidence.

## 3. System Architecture Model

**Components & responsibilities**
- `App.xaml.cs` — WPF entry; parses diagnostic CLI (`DiagnosticCommandLine.TryParse`) and exits before service
  construction for self-test/diagnostic commands; constructs `GroupManager`, `WindowShepherdService`,
  `WinEventMonitor`; runs `RescueOrphanedWindows` at startup; owns session-ending/shutdown ordering.
- `GroupManager` — owns `Groups`, maintains O(1) `_capturedIndex` (`Dictionary<IntPtr,CapturedMember>`) derived
  purely from `Members.CollectionChanged`; debounced `RequestSave`.
- `WindowShepherdService` — the Shepherd authority: capture (journal-before-mutate), hide, release,
  z-order pairing, min-track probing, durable hidden-window journal (`hidden-windows.json`).
- `WinEventMonitor` — installs `SetWinEventHook`; native callback runs off-UI, filters to captured windows,
  `Post`s to the UI `SynchronizationContext`; re-verifies identity at dispatch.
- `SplitPresentationController` / `PresentationLayoutCoordinator` / `SplitInteractionPolicy` — runtime-only
  split-pair state machine, relayout coalescing, and interaction classification.
- `PersistenceService` — atomic, fail-closed JSON state (`state.json` + `.bak`) with version migration.
- `DiagnosticReportService` / `DiagnosticEnvironmentService` / `DiagnosticTrace` / `LoggingService` /
  `RuntimeTelemetry` — observability + sanitized support-bundle export.
- `ProductMutationLease` — supervised-recovery privilege boundary (user-SID DACL mutex).

**Dependencies / data flow:** UI events → `WinEventMonitor` (filter+Post) → `GroupManager`/VM →
`WindowShepherdService` (mutates HWNDs, journals each dangerous op). Persistence and journal are independent
durable stores. Diagnostics read process/window state through fakes-or-native `IWindowIdentityNativeApi`.

**State ownership:** `Groups`/`Members`/`Tabs` are UI-thread-affine (`ObservableCollection` owned by
`GroupManager`, surfaced via `MainViewModel.Groups`). Split presentation state lives *only* in memory
(`SplitPresentationController`), never serialized.

**Persistence:** `state.json` v2 (layout intent only — bounds/title/label, no HWNDs, no credentials).
`hidden-windows.json` v3 journal (rescue evidence). Both via `WriteThrough`+`Flush(true)`+atomic `Move`.
Migration in-memory; fail-closed on corrupt/unsupported/unreadable.

**External systems:** Win32 desktop (hooks, windows), OS session manager (`WM_QUERYENDSESSION`),
release signing (DigiCert/HSM/local-pfx), GitHub Actions (CI).

**Key workflows:** capture (journal→DWM-suppress→token-install), hide (journal→`ShowWindow(SW_HIDE)`→verify),
release (verify→show→clear journal), startup rescue (`RescueOrphanedWindows` before any instance mutates),
session-ending (`EmergencyReleaseAll` → save/flush → stop monitor).

**Concurrency model:** UI thread is the mutation authority; `WinEventMonitor` callback is the *only* background
reader, marshalled via `SynchronizationContext.Post`. `RuntimeTelemetry`/`DiagnosticTrace` use lock/interlocked.
⚠ The `_capturedIndex` Dictionary was previously flagged as read on a background callback thread and written on the
UI thread **without synchronization** (AUDIT-001, originally High). The deep dive **refuted** this: `WinEventMonitor`
installs its hooks with `WINEVENT_OUTOFCONTEXT` and captures `SynchronizationContext.Current` from the UI thread, and
per the Win32 contract out-of-context WinEvent callbacks are delivered on the installing (UI) thread — so the index
is UI-thread-only. Only an *unenforced thread-affinity fragility* remains (AUDIT-001, now **Low**); a future change
that installed the hooks off-UI would reintroduce the race.

**Major invariants:** no-reparent; durability-before-danger; fail-closed identity; "exactly two" split;
atomic writes; user-thread UI affinity; supervised-recovery isolation.

## 4. Validation Results

| Command | Result | Relevant observations |
|---|---|---|
| `dotnet build TabDock.sln -c Debug` | **Build succeeded, 0 Warning(s), 0 Error(s)** | Build elapsed 00:00:01.91. `TabDock.Spike`, `TabDock` (win-x64), `TabDock.UnitTests` all compiled. Confirms net8.0 + `System.Threading.AccessControl 10.0.11` resolves cleanly; no NU1605; consistent with SCOPE-BUILD positive finding. |
| Self-test suite (in-repo `*SelfTest.cs`) | Not executed by this audit | Read-only constraint + no interactive session. Subagents read the tests; they are described as real-behavior (fake-API) assertions, not no-ops. NOTE: per AUDIT-003 these are **never run by the running app** — only via `TabDock.exe --selftest-diagnostics` / `validate.ps1`. |
| `dotnet test TabDock.UnitTests` | **Executed this pass (flaky)** | Deep-dive ran `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug` **4×**: run 1 **FAILED 145/146** (`SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration`, 14 s — "latest async generation never reached disk"); that single test passed **3× in isolation (<1 ms)**; full-suite runs 2–3 passed 146/146. The suite is therefore **non-deterministically flaky** (~1/4 failure rate in this small sample). The `SaveAsync` test couples correctness to a 15 s `SpinWait` against off-thread `Task.Run` writes sharing the global thread pool — see AUDIT-069. |
| ValidationDriver real-input scenarios | Not executed | Require a supervised Windows desktop + SendInput + installed browsers; explicitly out of scope for read-only/non-interactive runs. |

**Subagent-executed validation (reported in source scopes, not re-run here):**
- SCOPE-SPLIT built `TabDock.csproj` → 0 errors.
- SCOPE-BUILD built `TabDock.csproj -c Release` → 0 warnings / 0 errors.
- SCOPE-TESTS (first pass, *not re-run* at the time): xUnit `tests/UnitTests` Debug → 146/146 pass. **Corrected by the deep dive:** that "146/146" number was reported, not executed; on actual execution the suite is flaky (see above and AUDIT-069).
- SCOPE-SEC/SELFTEST/DIAG: deterministic self-tests (`PersistenceSelfTest`, `RecoveryJournalSelfTest`,
  `WindowIdentitySelfTest`, `WindowReleaseSelfTest`, `ProductMutationLeaseSelfTest`, `MonitorDpiSelfTest`,
  `DiagnosticPrivacySelfTest`, `CaptureBoundarySelfTest`, `PendingRecoverySelfTest`, `RuntimeStabilizationSelfTest`,
  `DeterministicSelfTests`) are described as real-behavior assertions; full pass/fail status not independently reproduced.

**Notable:** the build produces **0 warnings**, which means the `CS8625` nullability warnings cited by
SCOPE-TESTS/CONVERTER-NULLABILITY (AUDIT-049) are currently *not* surfacing in this build configuration —
the finding may be environment/configuration-specific (warnings-as-errors or analyzer settings differ), and
should be re-confirmed before action; the underlying signature/behavior mismatch is still valid.

## 5. Critical Findings

None. No defect met the Critical bar (catastrophic data loss, severe security compromise, unrecoverable
corruption, or violation of a core guarantee). The most severe issues are High but bounded (see §6).

## 6. High-Severity Findings



### [AUDIT-002] Release CI cannot upload artifacts (missing `actions: write` token scope)
- **Severity:** High
- **Confidence:** High
- **Category:** Build / Reliability
- **Affected areas:** `.github/workflows/prepare-release-candidate.yml:71-72,297` (job `prepare-candidate` has no `permissions:` override); `.github/workflows/release.yml:52-53,128` (job `qualify` has no `permissions:` override)
- **Summary:** Both workflows set a top-level `permissions: contents: read`, and their single jobs do not
  override it. Each job then calls `actions/upload-artifact`. Uploading workflow artifacts requires the
  `actions: write` token scope; with only `contents: read`, the `GITHUB_TOKEN` has no `actions` scope and the
  upload fails with HTTP 403 ("Resource not accessible by integration").
- **Evidence:** `prepare-release-candidate.yml:71-72` defines only `contents: read`; the `prepare-candidate` job
  (`:75`) has no `permissions:` block, so it inherits that; it calls `actions/upload-artifact@...` at `:297`.
  `release.yml:52-53` defines only `contents: read`; job `qualify` (`:56`) has no `permissions:` block and
  uploads at `:128`. Decisive corroboration: `publish-release.yml:91-99` explicitly grants its `verify` job
  `actions: write` *specifically* to enable its own `upload-artifact` at `:447` — the author knew upload needs
  `actions: write` but omitted it from the other two workflows.
- **Failure scenario:** Dispatch `prepare-release-candidate.yml` (or `release.yml`) → all build/sign/qualify
  steps pass → the "Upload … artifact" step fails 403. The immutable candidate / RC artifact is never retained,
  so the entire two-stage release (or RC retention) cannot proceed.
- **Impact:** The release and RC-retention pipelines are non-functional on first real use, despite every earlier
  gate passing. (Docs note no real release has occurred yet — `BLOCKED_EXTERNAL` — so the bug is latent/untested.)
- **Root cause:** An explicit top-level `permissions: contents: read` narrows the default `GITHUB_TOKEN` (which
  normally includes `actions: write`); the artifact-upload steps need `actions: write`, which was added to
  `publish-release.yml` but forgotten in the other two workflows.
- **Recommended direction:** Add `actions: write` to the job-level `permissions:` of `prepare-candidate`
  (Stage A) and `qualify` (RC), at minimum for the upload step; or widen the workflow-level permissions to
  include `actions: write`.
- **Verification recommendation:** Manually dispatch either workflow (RC can use `not-configured`) and confirm
  the upload step succeeds, or `gh run view` the run and confirm no 403 on the upload step.
- **Deep-dive update (DEEP-TEST-006, High — CONFIRMED):** Both `prepare-release-candidate.yml` and
  `release.yml` declare only `permissions: contents: read` and then call `actions/upload-artifact@v7`
  (which requires `actions: write`) → 403 on first real use. **Important nuance:** `build.yml` (the
  PR/CI gate) does **not** upload artifacts and is fully functional; only the two release/RC pipelines
  are broken. `publish-release.yml` correctly adds `actions: write` in its verify job, proving the
  requirement was known.

### [AUDIT-003] Self-test suite is never executed by the running application
- **Severity:** High
- **Confidence:** High
- **Category:** Reliability / Architecture
- **Affected areas:** `App.xaml.cs:62,78-89,105-113`; `Services/DiagnosticCommandLine.cs:120-125,197-319`; `scripts/validate.ps1:238-240`
- **Summary:** The self-test suite (all `*SelfTest.cs` plus the `DiagnosticSelfTest` aggregator) is **never
  executed by the running application**. It runs only when a user/CI explicitly invokes
  `TabDock.exe --selftest-diagnostics` (and `--selftest-geometry`/`--selftest-native-abi` are separate gates).
  The normal startup path (`Application_Startup`) has no self-test call; a diagnostic command is intercepted at
  `DiagnosticCommandLine.cs:121` and the process exits before any service is created.
- **Evidence:** Grep across all `*.cs` shows the only production invocation of `DiagnosticSelfTest.Run()` is
  `DiagnosticCommandLine.cs:121`; `App.xaml.cs` gates startup with `DiagnosticCommandLine.TryParse` (`:82`) and
  otherwise constructs real services (`:118-210`). `validate.ps1:239` is the sole driver in CI.
- **Failure scenario:** A regressed binary is built and shipped/deployed without running `validate.ps1` (or a
  user launches the app directly). Because the app consults **no** self-test at runtime, it proceeds with
  whatever environment/code state exists — there is no runtime fail-open or fail-closed behavior tied to a
  self-test, and a self-test failure can never surface to a normal user.
- **Impact:** The audit's core question ("fail-open vs fail-closed when a self-test fails?") is moot at runtime:
  self-tests provide **zero runtime stabilization resilience**. Runtime coherence is instead provided entirely by
  the production code (synchronous crash-journal, fail-closed persistence, identity gates). The self-tests are
  correctness gates, not runtime guards — partly by design, but it means the documented "runtime stabilization"
  is validated only offline, and a diverged environment (e.g., a DPI-probe regression) is not caught by any
  runtime self-check.
- **Root cause:** Self-tests were deliberately scoped as an offline/CI gate (per `RuntimeStabilizationSelfTest`
  header comment and AGENTS.md "validate.ps1"), not as a startup assertion.
- **Recommended direction:** Decide explicitly whether any lightweight, fast post-startup self-check (e.g.,
  monitor-DPI probe sanity or journal-storage availability) should run in the live path and feed user/telemetry
  visibility; otherwise document that self-tests are CI-only and rely on the production fail-closed paths for
  runtime resilience.
- **Verification recommendation:** Grep + read of `App.xaml.cs` startup and `DiagnosticCommandLine.cs`;
  confirmed no runtime invocation (verified directly in this consolidation).
- **Deep-dive update (DEEP-TEST-005, High — CONFIRMED by design):** No production `DiagnosticSelfTest.Run()`
  call exists outside the CLI/CI path; the suite runs via `TabDock.exe --selftest-diagnostics` /
  `validate.ps1`. This is intended offline/CI gating, not a runtime guard. Note rescue/restore *is*
  deterministically covered headlessly in CI (see the AUDIT-013 update), so the contract is not
  uncovered — merely not enforced at runtime.

### [AUDIT-068] Close-group WM_CLOSE can be delivered to a recycled same-process window
- **Severity:** High
- **Confidence:** Medium
- **Category:** Correctness / Security
- **Affected areas:** `Views/ContainerWindow.xaml.cs:871-925` (snapshot `:884-893`, verify `:914-922`, `PostMessage(WM_CLOSE)` `:923`); `Services/WindowIdentityGate.cs:344-412` (`VerifyReleasedWindowCloseTarget`, esp. `:366` & `:369-393`); `Services/WindowShepherdService.cs:850-868` (`TryCreateReleasedWindowCloseTarget`); `openspec/changes/final-production-readiness-closure-2026-08-14/specs/close-group-release-and-close/spec.md:31-33`
- **Summary:** The close-group "Yes" flow snapshots each still-captured window's native identity
  (HWND/PID/thread/exe/class/process-start) *before* release (`ContainerWindow.xaml.cs:884-893`), releases the
  group (`_viewModel.CloseGroup()` at `:909`), then for each snapshot calls
  `_shepherd.VerifyReleasedWindowCloseTarget(target, …)` (`:914-918`) and only on `Match` posts
  `PostMessage(target.Hwnd, WM_CLOSE, …)` (`:923`). `VerifyReleasedWindowCloseTarget`
  (`WindowIdentityGate.cs:344-412`) is the **sole gate before `WM_CLOSE`**. It re-reads
  PID/thread/class/exe/process-start and requires the capture token to be **absent** (`:366`). For a window
  destroyed and whose HWND is **recycled within the same process to a sibling window B** (same PID, same GUI
  thread, same class, same exe, same process-start), every field matches B and B carries no capture token, so the
  verifier returns `Match` and `WM_CLOSE` is sent to B — an unintended, unrelated window.
- **Evidence:** The verifier checks `GetProcessIdentity` → `current.ProcessId/ThreadId` (`:369-375`),
  `GetClassName` (`:377-381`), `GetProcessImagePath` (`:383-387`), `GetProcessStartTimeUtcTicks` (`:389-393`) —
  all compared to the *process-scoped* snapshot. None is window-instance-unique. The token is intentionally
  required to be absent (`:366`), so the only per-window-instance discriminator (the capture token) is **not**
  available post-release by design (`WindowIdentityGate.cs:38-48`). `ReleasedWindowCloseTarget` (`:50-83`) carries
  only process-scoped fields. Spec `close-group-release-and-close/spec.md:31-33` explicitly requires "A recycled
  HWND is never closed … TabDock SHALL not post `WM_CLOSE` to the replacement" — the sole gate cannot catch the
  same-process case.
- **Failure scenario:** A multi-window app on one GUI thread (browser, Office, IDE). User opens the close-group
  prompt with window A (hwnd=X) captured and **alive**. TabDock snapshots A (X, same P/T/C/E/S) into
  `windowsToClose` (`:884-893`). Between snapshot and the verify/`WM_CLOSE` (all in one synchronous `Closing`
  handler, no dispatcher drain), A's own thread destroys A and re-creates sibling B reusing HWND X while keeping
  the same class/exe. `CloseGroup` releases A (removes its token). `VerifyReleasedWindowCloseTarget` now sees X
  alive (B), token absent, P/T/C/E/S all matching → `Match` → `PostMessage(X, WM_CLOSE)` closes **B**, not A.
- **Impact:** Unintended `WM_CLOSE` on an unrelated window = unexpected termination / potential data loss of a
  third-party window. Directly contradicts a written behavioral guarantee. No cross-process/escalation (local
  only), but the consequence is destructive and the sole gate provides **zero** protection for the common
  same-process, same-thread multi-window case.
- **Root cause:** Same as AUDIT-026 — identity is **process-scoped, not window-scoped**. The per-capture token is
  the only window-instance proof and is deliberately removed on release, so after release a recycled same-process
  HWND cannot be distinguished from the original.
- **Recommended direction (no implementation):** Before `PostMessage(WM_CLOSE)`, either (a) retain a lightweight
  release-specific marker `SetProp` on the window at release time that the verifier *requires to still be present*
  (reversing the "token must be absent" rule for the close path), or (b) add a window-instance discriminator that
  survives release (e.g., verify the window still has identical `GetAncestor(GA_ROOTOWNER)`/parent/owner and
  identical immutable style bits captured at snapshot), or (c) narrow the `Match` window by re-snapshotting
  immediately before `WM_CLOSE` and comparing a freshly-read immutable attribute a recycled sibling would not
  share. At minimum, extend the spec's "recycled HWND" scenario to the same-process case and add a deterministic
  test.
- **Verification recommendation:** Deterministic test injecting an `IWindowIdentityNativeApi` where the released
  target's HWND is reported alive with a *different* window object but identical PID/thread/class/exe/process-start;
  assert `VerifyReleasedWindowCloseTarget` returns `Replaced`/`Unverifiable`, not `Match`. Also a behavioral test
  of the close-group Yes path with a recycled same-process HWND.

## 7. Medium-Severity Findings

### [AUDIT-004] EnterSplit ignores DefinePair fail-closed early-return → controller/ViewModel desync on re-split
- **Severity:** Medium
- **Confidence:** High
- **Category:** Correctness
- **Affected areas:** `Services/SplitPresentationController.cs:66-77` (`DefinePair`); `Views/ContainerWindow.xaml.cs:2785-2841` (`EnterSplit`)
- **Summary:** `SplitPresentationController.DefinePair` is the runtime authority; when replacing an existing
  pair it hides departing members and **returns early (leaving the OLD pair authoritative) if any such hide
  returns `WindowHideOutcome.RecoveryPending`**. But `EnterSplit` calls `DefinePair` as `void` and
  **unconditionally continues** to mutate `_shepherdActiveWindow`, `_viewModel.SetActiveTab`,
  `_viewModel.SetSplitComposite`, and `LayoutSplitPanes()` regardless of whether the transition actually took.
- **Evidence:** `DefinePair` early-returns at `SplitPresentationController.cs:74` (`if (o == WindowHideOutcome.RecoveryPending) return;`) with `_left/_right/_presented` untouched. `EnterSplit` does not branch on any return value; it proceeds to `_shepherdActiveWindow = left; _viewModel.SetActiveTab(leftTab); _viewModel.SetSplitComposite(leftTab, rightTab); LayoutSplitPanes();` (`:2826-2841`). The suspend paths *do* honor the `bool` return.
- **Failure scenario:** Group shows split A|B. User starts a new split from a third tab C→D; `EnterSplit(C,D)`
  hides departing A and B; B's hide returns `RecoveryPending`. `DefinePair` returns early (controller still A|B),
  but `EnterSplit` installs composite `[C|D]` and calls `LayoutSplitPanes` which positions the still-present A|B.
  Result: strip shows `[C|D]` while B is actually visible in a pane; controller's `Left/Right/IsSplitPresented`
  disagree with the display.
- **Impact:** Authoritative split state diverges from the tab-strip projection; a guest the user believes
  replaced is still presented. No crash/leak, but a real user-visible correctness defect reachable through
  ordinary UI.
- **Root cause:** `DefinePair` exposes a fail-closed contract via early-return, but its caller (`EnterSplit`)
  was written as if it always succeeds; the `void` return is not honored.
- **Recommended direction:** Make `DefinePair` return `bool` (or have `EnterSplit` re-read
  `_splitController.IsRelationshipDefined/Left/Right` after the call); when the transition did not take, bail and
  re-present the original pair.
- **Verification recommendation:** A unit/ValidationDriver scenario starting split A|B, forcing
  `RecoveryPending` on a departing-member hide while starting a new split, then asserting controller `Left/Right`
  and the displayed composite agree.
- **Deep-dive update (DEEP-SPLIT-001, impact **upgraded**):** This is a **functional break, not merely a
  cosmetic mismatch**. After the desync the tab strip becomes **non-interactive for ordinary clicks**:
  `SplitInteractionFix.SuspendPresentedPairForUserSelection` returns `false` for every non-controller-member
  click (C/D/E), so any tab click is a no-op and the user must close the actual members A/B (or the container)
  to recover; `HandleMemberRemoved` returns null for non-members, so closing C/D does nothing to the controller.
  Panes still show A|B. Reachable through ordinary UI under the specific `RecoveryPending` trigger.

### [AUDIT-006] Capture picker enumerates all top-level windows synchronously on the UI thread, with no try/catch
- **Severity:** Medium
- **Confidence:** High
- **Category:** Reliability / Performance
- **Affected areas:** `ViewModels/CapturePickerViewModel.cs:92-193` (esp. `EnumWindows` callback `:135-187`); `:89` (constructor calls `Refresh()`); `Views/ContainerWindow.xaml.cs:1879` (`OpenCapturePanel`)
- **Summary:** `CapturePickerViewModel.Refresh()` is invoked from the constructor and performs a synchronous
  `NativeMethods.EnumWindows` over every top-level window, issuing cross-process reads per candidate
  (`IsWindowVisible`, `GetWindowLongPtr`, `GetWindowTextString`, `GetClassNameString`, `DwmGetWindowAttribute`,
  `GetWindowThreadProcessId`, `GetProcessImagePath`). All runs on the UI thread (VM constructed on the
  dispatcher thread). There is **no try/catch** around `EnumWindows`; the per-window callback calls native methods
  that can throw (e.g., `GetProcessImagePath` on pid 0 / denied access). (Consolidates SCOPE-VIEW/PICKER-ENUM-UI-BLOCK
  and SCOPE-PERF/PICKER-ENUMWINDOWS-UI-THREAD.)
- **Evidence:** Constructor calls `Refresh()` at `CapturePickerViewModel.cs:89`; the enumeration loop `:135-187`
  has no enclosing try; the icon worker *is* wrapped in try/catch (`:258-277`), showing the gap is inconsistent.
- **Failure scenario:** Opening the inline capture panel on a busy desktop with hundreds of windows blocks the UI
  for a noticeable interval; if any single window triggers a CLR exception inside the callback, it propagates out
  of `Refresh()` → constructor → `OpenCapturePanel`, and the picker fails to open.
- **Impact:** UI-freeze during enumeration and a picker that cannot open at all on one bad-window exception.
- **Root cause:** Desktop enumeration kept on the UI thread for direct binding mutation; the robustness hygiene
  applied to the icon worker was not applied to the enumeration itself.
- **Recommended direction:** Move enumeration off the UI thread with a marshalled collection swap (mirroring the
  threaded icon path), and/or wrap `EnumWindows` in try/catch (log + open with whatever windows were collected).
- **Verification recommendation:** Test injecting a throwing native call; assert `Refresh()` does not throw and
  the picker opens. Measure UI-thread blocked time during `Refresh()` with a large open-window count.

### [AUDIT-007] DPI probe creates/destroys a hidden helper window on every call (no per-monitor cache)
- **Severity:** Medium
- **Confidence:** High
- **Category:** Performance
- **Affected areas:** `Services/MonitorDpiService.cs:87-170` (`GetEffectiveDpi`); `Services/WindowShepherdService.cs:1136` (`ToPhysicalScaleForGuest`), `:523` (`GetMonitorEffectiveDpi` at capture)
- **Summary:** `GetEffectiveDpi` creates a hidden STATIC helper window, calls `GetDpiForWindow`, then destroys it
  on every single call, with thread-DPI-context save/restore around it. No per-monitor caching. One of the more
  expensive native sequences (window create/destroy + context switches) runs on the UI thread.
- **Evidence:** `MonitorDpiService.cs:114` `CreateWindowEx(...)` and `:147` `DestroyWindow(helper)` inside
  `GetEffectiveDpi`, with no cache field. Callers: `ToPhysicalScaleForGuest` (`:1136`) invoked from
  `GetEffectiveMinTrackSize` for every DPI-unaware guest on a scaled monitor; also `:523` during Capture.
- **Failure scenario:** A DPI-unaware guest docked on a 150% monitor; every dirty-constraint re-probe (5s timer,
  tab-switch, container restore) recreates/destroys a window for a DPI value that does not change. Two guests in
  split → two window lifecycles per probe.
- **Impact:** Avoidable UI-thread window churn and DPI-context thrash; adds latency to constraint transitions.
- **Root cause:** Authoritative DPI value recomputed from scratch instead of memoized per monitor handle.
- **Recommended direction:** Cache effective DPI keyed by monitor handle (invalidate only on `WM_DPICHANGED`).
- **Verification recommendation:** Track `CreateWindowEx`/`DestroyWindow` counts while docking a DPI-unaware
  guest and forcing re-probes; expect >1 creation without a fix, exactly 1 with caching.

### [AUDIT-008] Synchronous cross-process WM_GETMINMAXINFO probe can block the UI thread up to 100 ms per guest
- **Severity:** Medium
- **Confidence:** High
- **Category:** Performance / Reliability
- **Affected areas:** `Services/WindowShepherdService.cs:1035-1086` (`GetEffectiveMinTrackSize`), `:1056` (`SendMessageTimeout`), `:184` (`MinTrackProbeTimeoutMilliseconds = 100`)
- **Summary:** `GetEffectiveMinTrackSize` sends a synchronous cross-process `WM_GETMINMAXINFO` via
  `SendMessageTimeout` on the UI thread, blocking up to 100 ms per guest. It runs on every dirty-constraint
  transition (active-tab switch, container minimize/restore, 5s periodic timer, split-enter), not merely on user resize.
- **Evidence:** `WindowShepherdService.cs:1056` `SendMessageTimeout(..., SMTO_ABORTIFHUNG | SMTO_NORMAL, MinTrackProbeTimeoutMilliseconds, ...)`. `RefreshSizeConstraint` (`ContainerWindow.xaml.cs:2270`) calls it for each visible guest, invoked from per-frame `RequestRelayout` execute whenever `_constraintDirty`; `_constraintDirty` set by 5s timer, tab switch, move/size-end.
- **Failure scenario:** Two guests in split mode under a 5s timer tick while one guest is merely slow (not hung —
  `SMTO_ABORTIFHUNG` only saves hung targets) → up to ~200 ms serial UI-thread blocking mid-interaction.
- **Impact:** UI-thread stalls / input jank on constraint transitions.
- **Root cause:** Synchronous native probe on the UI thread for a value that is then cached; 100 ms is reachable for a sluggish-but-alive guest.
- **Recommended direction:** Run the probe off the UI thread (or refresh opportunistically async); bound total probe time and avoid re-probing when the cached value is good enough.
- **Verification recommendation:** Test simulating a guest answering after 90 ms; assert UI thread blocked ≥90 ms on a constraint transition without an off-thread fix.

### [AUDIT-009] Picker per-row change handlers trigger O(n²) global command requery storm
- **Severity:** Medium
- **Confidence:** High
- **Category:** Performance
- **Affected areas:** `ViewModels/CapturePickerViewModel.cs:211-218` (`AddCandidate` handler), `:43-52` (`HasSelection` getter), `:56-59` (`RelayCommand.RaiseCanExecuteChanged` → `CommandManager.InvalidateRequerySuggested`)
- **Summary:** Every `WindowInfo` row gets a `PropertyChanged` handler that, on ANY property change (including the
  worker-posted icon assignment per candidate), calls `OnPropertyChanged(nameof(HasSelection))` and
  `((RelayCommand)GroupSelectedCommand).RaiseCanExecuteChanged()`, which triggers a process-wide
  `CommandManager.InvalidateRequerySuggested()`, forcing every command in the app to re-evaluate `CanExecute`.
  `HasSelection` is itself an O(n) scan over `Windows`.
- **Evidence:** `CapturePickerViewModel.cs:211-218` subscription; `:46 RaiseCanExecuteChanged() =>
  CommandManager.InvalidateRequerySuggested()`; `:43-52 HasSelection` loops over `Windows`. The worker assigns
  `row.Icon` per candidate, each raising the handler.
- **Failure scenario:** Picker refresh enumerating N≈200 windows: worker posts N icon assignments → N global
  `InvalidateRequerySuggested()` calls, each re-runs the `HasSelection` O(n) predicate → O(n²) indirect work plus
  repeated global requery storms during one refresh.
- **Impact:** Noticeable hitch when the capture picker populates; scales quadratically with candidate count.
- **Root cause:** Per-row handlers wired to a global app-wide requery invalidation rather than a scoped/local update.
- **Recommended direction:** Debounce/throttle `InvalidateRequerySuggested`; scope `CanExecute` to a single
  `SelectedCount` integer; avoid posting per-row `PropertyChanged` that fans out to a global requery.
- **Verification recommendation:** With N candidates, count `RequerySuggested` firings / `HasSelection`
  invocations during refresh; expect O(N) with a fix vs O(N²) today.

### [AUDIT-010] Sanitized-log title safety relies on a static allow/deny list, not structured redaction
- **Severity:** Medium
- **Confidence:** High
- **Category:** Privacy / Architecture
- **Affected areas:** `Services/DiagnosticEnvironmentService.cs:189-222,239-276`; `Services/DiagnosticReportService.cs:31,170,215`
- **Summary:** The only protection keeping captured window titles out of the support bundle's `recent-log`
  section is a hardcoded substring deny-list (`"title changed"`, `"Shepherd-captured"`, `"Shepherd-released"`,
  `"Created group"`, `"Quarantined corrupt"`) combined with an allow-list of tag prefixes. `SanitizeText`
  itself has no concept of window titles — it only redacts paths, usernames, and `password=`/`token=` keywords.
  Any future log line that includes a window title under a *kept* tag would survive `SanitizeText` unchanged and
  land verbatim in `recent-log.txt` and `[recent-log-sanitized]`.
- **Evidence:** Deny/allow logic at `DiagnosticEnvironmentService.cs:199-216`; `SanitizeText` (`:239-276`) contains
  no title handling. Current title-bearing lines only avoid leakage because they happen to contain deny-list
  strings.
- **Failure scenario:** A developer adds `Log($"SHEPHERD[foo] handled guest '{window.OriginalTitle}'")`; the
  title is perma-leaked into every exported ZIP; existing privacy tests stay green.
- **Impact:** Privacy regression (window-title disclosure) bypassing the stated privacy contract
  (docs/ARCHITECTURE.md:54-55: "omit raw window titles").
- **Root cause:** Privacy of titles enforced by grep-style string matching rather than structural redaction.
- **Recommended direction:** Drive privacy from structured data (a `LogScope`/`PrivacyClass` per line, or never
  log titles and hash them like `NativeSnapshotService` does), or have `SanitizeText` hash any external free-text
  value.
- **Verification recommendation:** Test materializing a `TabDock.log` with title-bearing lines under each kept tag,
  running `ReadSanitizedRecentLogText`, asserting no title substring survives; wire into `DiagnosticPrivacySelfTest`.

### [AUDIT-011] Persistence Load failure-path has zero CI-runnable unit coverage
- **Severity:** Medium
- **Confidence:** High
- **Category:** Tests
- **Affected areas:** `tests/UnitTests/PersistenceTests.cs`, `tests/UnitTests/PersistenceSingleWriterTests.cs`; `Services/PersistenceService.cs:260-702` (`Load`), `:71` (internal ctor); in-app `Services/PersistenceSelfTest.cs`
- **Summary:** `PersistenceService.Load` — the entire fail-safe read path (corrupt-quarantine→backup-recovery,
  `Unsupported` future-version refusal, `Unreadable`/access-denied protection, partial-array salvage,
  empty-shell skipping) — has **zero coverage in the xUnit `tests/UnitTests` project**. The unit project only
  tests DTO round-trips and the *write* gate. The failure branches are covered *only* by the in-app
  `--selftest-diagnostics` (`PersistenceSelfTest.cs`), which is not part of `dotnet test`.
- **Evidence:** `Load()` (`:260-702`) has no xUnit caller; the internal ctor at `:71` already accepts injectable
  `Func<string,FileAttributes>`/`Func<string,string>` — exactly the seams a unit test needs — but no unit test
  uses them for the read path. `PersistenceSelfTest.cs:64-118` exercises malformed-primary/backup, both-corrupt,
  and unreadable-primary deterministically.
- **Failure scenario:** A regression in `TryReadStateFile`/`QuarantineCorruptStateFile` (e.g., an exception that
  wipes user state) would pass `dotnet test` and only be caught by a human running the supervised app self-test.
- **Impact:** The most safety-critical persistence logic (must "never mistake a fail-safe read error for empty
  user state") is invisible to the fastest, CI-runnable test layer.
- **Root cause:** Persistence read-path tests placed in an embedded app self-test rather than the dedicated unit
  project, despite the class being fully injectable.
- **Recommended direction:** Expose the internal ctor to `TabDock.UnitTests` (`InternalsVisibleTo`) and add `Load`
  failure cases there; or formally document the in-app self-test as the contract for these branches.
- **Verification recommendation:** New xUnit test: construct with malformed primary + valid `.bak` → assert
  `Load()` returns restored groups; unreadable primary → assert `StateLoadFailed == true` and backup byte-identical.
- **Deep-dive update (DEEP-TEST-004, High → Medium — re-rated):** The core point stands — `PersistenceTests.cs`
  never calls `PersistenceService.Load()` and the corruption→quarantine→backup-fallback path has zero **xUnit**
  coverage. **But** the Load failure-path IS exercised in CI: `build.yml` runs `validate.ps1 -Ci`, which invokes
  `TabDock.exe --selftest-diagnostics` (`validate.ps1:239`), and `PersistenceSelfTest.cs:64-118` exercises
  malformed-primary / corrupt-backup / both-corrupt. So "zero CI-runnable coverage" was inaccurate; the gap is
  "not in the unit project / not in coverage metrics" (and co-mingled in one try — see AUDIT-018), not
  "uncovered by CI".

### [AUDIT-012] ValidationDriver "no EXCEPTION" assertions are vacuous (false confidence)
- **Severity:** Medium
- **Confidence:** Medium
- **Category:** Tests
- **Affected areas:** `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Core.cs:752` (`CountNewLines(off,"EXCEPTION")==0`), used across crash/close-group/soak scenarios
- **Summary:** Many scenarios assert the *absence* of a failure marker (`== 0` count of `EXCEPTION`/specific error
  lines). Such assertions pass vacuously if the feature under test never ran or a code change removed the log
  line entirely — they cannot distinguish "feature worked cleanly" from "feature path was never reached".
- **Evidence:** `Scenarios.Core.cs:752` asserts `TabDockLog.CountNewLines(off, "EXCEPTION") == 0`. Same pattern
  recurs across crash-rescue, close-group, torture scenarios.
- **Failure scenario:** A regression silently disables a code path (e.g., the close-group Yes handler stops
  running guest-exit logic but also stops emitting the expected `EXCEPTION`); the "no EXCEPTION" check still
  passes, giving false confidence.
- **Impact:** High pass-rate that masks real breakage; "ALL SCENARIOS PASSED" can be green while a feature is dead.
- **Root cause:** Absence-assertions favored over positive "feature executed" assertions.
- **Recommended direction:** Pair every "no EXCEPTION" check with a positive assertion that the path ran (e.g.,
  assert the expected `SHEPHERD[…]`/`Reordered tab` line was emitted).
- **Verification recommendation:** Temporarily stub a scenario so the feature is skipped; confirm the "no
  EXCEPTION" check still passes (demonstrating vacuity), then confirm a positive assertion would catch it.
- **Deep-dive update (DEEP-TEST-003, Medium — framing **corrected**):** The "vacuous" framing is **overstated**.
  Production routing of *both* UI-thread (`Application_DispatcherUnhandledException`, `App.xaml.cs:379`) and
  background-thread (`AppDomain.CurrentDomain.UnhandledException`, `:429`) crashes goes through
  `_log.LogException`, which emits the literal `EXCEPTION in …` line (`LoggingService.cs:142`). So a logged crash
  *does* reach the log and *is* caught by `CountNewLines(...,"EXCEPTION")==0`. The assertions are meaningful,
  not no-ops. **Residual risk (kept at Medium):** a code change that silently swallows an exception (`catch {}`
  without `LogException`) would make the guard pass vacuously while the feature is broken; and absence-of-log
  cannot distinguish "feature worked" from "feature never ran".

### [AUDIT-013] No deterministic/headless integration test for persist-kill / crash-rescue / split-survivor restart
- **Severity:** Medium
- **Confidence:** High
- **Category:** Tests / Reliability
- **Affected areas:** `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs:768-883` (`StartScenario`); `docs/TESTING.md:62-65,87,289`; `scripts/validate.ps1` (CI path "never sends desktop input")
- **Summary:** The end-to-end behaviors that matter most — persist→force-kill→relaunch restore (`persist-kill`),
  hidden-window-journal crash rescue (`crashkill-rescue`), and split survivor promotion across a real restart —
  are validated *only* by the real-input ValidationDriver, which requires a supervised interactive Windows
  desktop. There is no fast, headless, `dotnet test`-runnable integration test for any of these paths; the xUnit
  suite only covers pure functions and the write gate.
- **Evidence:** `StartScenario` (`:768`) spawns a real `TabDock.exe`, waits for `MainWindow`, relies on
  `Thread.Sleep(1000)` and real `SendInput`. `TESTING.md:87` states the run "must be supervised"; CI
  (`validate.ps1 -Ci`) runs only `--selftest-geometry`/self-tests and "never sends desktop input".
- **Failure scenario:** A regression in restore/rescue logic ships because automated CI only exercised pure-function
  unit tests and self-tests; the supervised harness was never run.
- **Impact:** Critical recovery paths have no automated regression gate; quality depends on a human running a
  destructive, interactive suite.
- **Root cause:** Integration tests built as a real-desktop harness rather than against the already-injectable
  service APIs.
- **Recommended direction:** Add headless integration tests driving the public restore/rescue APIs against
  injected FS / fake journal — `PersistenceService` (injectable IO) and `WindowShepherdService`/`GroupManager`
  (constructable with `LoggingService`+paths) are testable without a window.
- **Verification recommendation:** A `dotnet test` case seeding a `hidden-windows.json` journal, calling
  `RescueOrphanedWindows`/`RestoreState`, asserting the guest is restored — runs in CI without a display.
- **Deep-dive update (DEEP-TEST-005, Medium — framing **corrected**):** Restore/rescue IS deterministically
  covered headlessly in CI. `RecoveryJournalSelfTest.cs`/`RuntimeStabilizationSelfTest.cs`/
  `PendingRecoverySelfTest.cs` call `WindowShepherdService.RescueOrphanedWindows` with fake APIs and run via
  `--selftest-diagnostics` (CI). The *real-input* ValidationDriver scenarios (`persist-kill`, `crash-rescue`,
  `torture`) remain opt-in/supervised and **NOT** CI-gated — but the contract itself has deterministic CI
  coverage.

### [AUDIT-014] ValidationDriver pixel health checks are flaky (GPU windows read as solid black)
- **Severity:** Medium
- **Confidence:** Medium
- **Category:** Reliability / Tests
- **Affected areas:** `tests/ValidationDriver/TabDock.ValidationDriver/Pixels.cs:19-94` (`CaptureHostScreenArea` BitBlt), `:160-194` (`ComputeAvgBrightness`/`ComputeAvgFrameDiff`), `:90-94` (doc-comment admitting GPU windows read as solid black)
- **Summary:** Render-health assertions depend on screen-region `BitBlt` and heuristic brightness/variance
  thresholds. The helper's own doc-comment records that screen-region BitBlt "spuriously show[s] a
  hardware-accelerated window's content as solid black" — falsely *failing* a live GPU guest and falsely
  *passing* a genuinely blank window. Absolute-brightness thresholds misread black-themed/paused content.
- **Evidence:** `Pixels.cs:90-94` documents the GPU false-black behavior; `ComputeAvgBrightness` returns
  "< ~1.0 means black/blank"; `ComputeAvgFrameDiff` "> ~0.005 means visible change".
- **Failure scenario:** A render-health scenario on a locked/RDP session or paused GPU window → BitBlt returns
  black → scenario fails (or a blank window passes as "rendering").
- **Impact:** Non-deterministic, environment-coupled failures that erode trust in the harness.
- **Root cause:** Choosing DWM screen-region capture (flaky for GPU) as the primary probe, with absolute-brightness heuristics.
- **Recommended direction:** Prefer `CaptureWindowViaPrintWindow` for GPU guests; assert frame-to-frame *change*
  rather than absolute brightness; make pixel checks advisory/non-fatal or bounded.
- **Verification recommendation:** Run a render-health scenario against a paused GPU window; confirm the false
  black reading, then confirm `PrintWindow` reads it correctly.

### [AUDIT-015] RC workflow offers `digicert-stm` signing provider that its own job cannot perform
- **Severity:** Medium
- **Confidence:** High
- **Category:** Correctness / Build
- **Affected areas:** `.github/workflows/release.yml:42-50` (input `options`), `:43` (description), `:119` area env block (no `SM_*` vars)
- **Summary:** The RC (`release.yml`) `signing-provider` input lists `digicert-stm` as valid, but its own
  description says digicert-stm "is not supported on this RC-only path (no DigiCert tooling setup)" and
  `docs/release/publication-gates.md:490-494` states RC supports only `not-configured`/`local-pfx`. The RC job
  has no DigiCert action step and passes no `SM_*` env vars, so selecting `digicert-stm` causes `sign-release.ps1`
  to fail and the RC run to error.
- **Evidence:** `release.yml:42-50` includes `digicert-stm`; `:43` comment contradicts it; `:119-123` sets only
  `SIGNING_PROVIDER`/`SIGNCERT_*` (no `SM_*`); `sign-release.ps1:403-421` digicert-stm branch requires `smctl` +
  `SM_*` env → absent → failure.
- **Failure scenario:** Operator dispatches RC with `signing-provider: digicert-stm` → signing preflight fails →
  release-qualify.ps1 throws.
- **Impact:** Confusing failure and a misleading capability; may be mistaken for a real signing misconfiguration.
- **Root cause:** Stale/overscoped `options` list not aligned with the RC body and docs.
- **Recommended direction:** Remove `digicert-stm` from the RC `options` list (or, if intended, add the DigiCert
  setup step + `SM_*` env to the RC job).
- **Verification recommendation:** Parse the workflow input schema; dispatch with `digicert-stm` and confirm the
  outcome.

### [AUDIT-016] ARCHITECTURE.md citations are stale (off by 1,300–2,300 lines)
- **Severity:** Medium
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `docs/ARCHITECTURE.md:11` (verification claim), `:697-717,750-768,775-818,827-842,875-914,632,653`
- **Summary:** `docs/ARCHITECTURE.md` opens with "Every claim below was verified against current source; citations
  use `path.cs:line`" (`:11`), yet the `ContainerWindow.xaml.cs` line citations in §2 and §5 are off by
  ~1,300–2,300 lines (the file is now 3,584 lines). An agent/reviewer tracing the documented flows lands inside
  unrelated code and may "fix" the wrong method.
- **Evidence:** Doc cites `CaptureWindow` at `697-717` → actual `1933`; `SyncShepherdActiveWindow` `750-768` →
  `2100`; `LayoutShepherdActiveWindow` `775-818` → `2392`; `GetContentAreaScreenRect` `827-842` → `2505`;
  `RestoreMinimizedWindow` `875-914` → `3162` (200 ms timer at `3188`); `LoggingService.cs:10,31` → `:34`/`:263`.
- **Failure scenario:** A reviewer tracing capture/minimize-restore lands inside unrelated code (e.g., `875-914`
  is now inside the split-relayout region) and misreads behavior.
- **Impact:** The architecture map is an unreliable code-navigation aid and its self-asserted verification is false.
- **Root cause:** Doc authored when `ContainerWindow.xaml.cs` was ~1/3 its current size; citations never re-verified.
- **Recommended direction:** Drop `:line` suffixes from narrative citations (cite method/symbol names) or re-verify
  every citation and add a CI lint that fails if a cited `file:line` no longer contains the named symbol.
- **Verification recommendation:** Grep each cited symbol in the cited file; symbols now resolve far from the
  cited ranges (verified-consistent with the scope's grep).

### [AUDIT-017] Split log vocabulary in docs/code diverges from production emissions
- **Severity:** Medium
- **Confidence:** High
- **Category:** Correctness (docs/code contract), Tests
- **Affected areas:** `docs/ARCHITECTURE.md:357-359`; `Views/ContainerWindow.xaml.cs:2836` (`SPLIT[enter]`), `:2874` (`SPLIT[suspend]`), `:2960` (`SPLIT[resume]`), `:2914` (`SPLIT[single]`); `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs:33`; `QualificationResultWriter.cs:142`
- **Summary:** The documented split log vocabulary lists `SPLIT[enter]/[exit]/[replace]/[member-gone]/[persist]`,
  but `SPLIT[replace]` is **never emitted by any production `.cs`** (the replace path reuses `SPLIT[enter]` at
  `ContainerWindow.xaml.cs:2836`). Conversely `SPLIT[suspend]`/`SPLIT[resume]`/`SPLIT[single]` ARE emitted but are
  absent from the vocabulary. A test helper contains a dead branch predicated on the non-existent `SPLIT[replace]`.
- **Evidence:** `Scenarios.Split.cs:33` comment claims `SPLIT[replace]` is "present in committed source" — false.
  `QualificationResultWriter.cs:142` branches on `line.Contains("SPLIT[replace]")`, permanently dead.
- **Failure scenario:** A developer/triager trusts the vocabulary, looks for `SPLIT[replace]` on a replace
  transition and never finds it, or misses the `SPLIT[suspend]`/`[resume]`/`[single]` lines that distinguish
  presented-vs-dormant state.
- **Impact:** Documentation misrepresents observable behavior; a dead test branch. No current scenario hard-asserts
  `SPLIT[replace]`, so nothing fails today, but it is a latent trap.
- **Root cause:** `SPLIT[replace]` was renamed to reuse `SPLIT[enter]` (or never implemented); doc/vocabulary and
  the comment were not updated.
- **Recommended direction:** Remove `SPLIT[replace]` (or add the emission if intended), add
  `SPLIT[suspend]/[resume]/[single]` to the vocabulary + ARCHITECTURE.md:357-359; fix the comment and dead branch.
- **Verification recommendation:** `grep -rn "SPLIT\[replace\]" Services Views App.xaml.cs` → no production
  emission; `grep "SPLIT\[suspend\]|SPLIT\[resume\]|SPLIT\[single\]"` → three emission sites.

### [AUDIT-018] PersistenceSelfTest wraps all checks in a single try, masking cascading regressions
- **Severity:** Medium
- **Confidence:** High
- **Category:** Reliability / Tests
- **Affected areas:** `Services/PersistenceSelfTest.cs:33-219` (single try at `:33`, catch at `:201-205`)
- **Summary:** `PersistenceSelfTest.Run()` wraps ~35 `Check(...)` calls in one `try`. If any single check throws,
  the `catch` (`:201-205`) does only `failures++; checks++;` and returns — **all remaining checks are skipped
  and only one failure is recorded**.
- **Evidence:** `PersistenceSelfTest.cs:33 try { … }` sequential `Check(...)`. Contrast `CaptureBoundarySelfTest`
  / `WindowReleaseSelfTest` where each assertion is a separate `bool`-returning method that internally guards
  itself.
- **Failure scenario:** A production change makes an early fixture throw. The catch records 1 failure and the
  15+ later checks never execute — a second independent regression is invisible until the first is fixed.
- **Impact:** Reduced diagnostic value of the gate; cascading regressions masked behind a single opaque failure.
- **Root cause:** Whole-suite guard instead of per-assertion isolation.
- **Recommended direction:** Isolate each `Check(...)` (or group) in its own try/catch that records the specific
  failure and continues, matching `CaptureBoundarySelfTest`/`WindowReleaseSelfTest`.
- **Verification recommendation:** Read of source; the single-try structure is evident at `:33-219`.

### [AUDIT-019] RuntimeTelemetry is permanently disabled (no caller ever sets Enabled = true)
- **Severity:** Medium
- **Confidence:** High
- **Category:** Observability
- **Affected areas:** `Services/RuntimeTelemetry.cs:30` (default false); call sites `Views/ContainerWindow.xaml.cs:701,752`, `SplitInteractionFix.cs:111-126`, `Services/GroupManager.cs:129,147`, `Services/WindowShepherdService.cs:946,1163,1299,1446,1538,1903`
- **Summary:** `RuntimeTelemetry` has a master `Enabled` switch defaulting to `false`, and no code anywhere sets it
  to `true`. All `RecordXxx()`/`BeginTransition()` early-return when disabled, so every telemetry call in the
  production path is a no-op and p50/p95/p99 latency is never collected. The runtime-stabilization campaign's
  own goal #10 ("production paths call the counters") is satisfied at the call site but never at collection.
- **Evidence:** Grep for `Enabled = true` / `RuntimeTelemetry.Enabled` returns no assignment outside the class
  definition. Yet production code calls `RecordShowWindow`/`RecordSetWindowPos`/`RecordJournalCommit`/etc.
- **Failure scenario:** A regression in transition latency or native-operation counts goes unnoticed in production.
- **Impact:** Observability gap — the runtime stabilization campaign cannot be measured in production.
- **Root cause:** The switch was added as "opt-in behind a diagnostic switch" but no caller ever enables it.
- **Recommended direction:** Enable `RuntimeTelemetry` for the diagnostic/`--selftest-*` paths or a verbose flag,
  so the counters are exercised; or remove the dead call sites.
- **Verification recommendation:** Grep for `Enabled` assignment (none found); read of class and call sites.

### [AUDIT-020] Single-bool self-tests hide which sub-assertion failed
- **Severity:** Medium
- **Confidence:** High
- **Category:** Tests / Maintainability
- **Affected areas:** `Services/DiagnosticCommandLine.cs:285,287,294,295,298`; `Services/WindowIdentitySelfTest.cs:12-142`; `Services/MonitorDpiSelfTest.cs:12-73`; `Services/DiagnosticCommandLine.cs:322-361,539-587`
- **Summary:** Several self-tests aggregate ~15-30 sub-assertions into a single `bool` returned to one `Check(...)`.
  When such a test fails, the gate records exactly **one** failure with no indication of which sub-condition broke
  (e.g., `WindowIdentitySelfTest.CoversIdentityTiers()` ANDs ~24 conditions; `MonitorDpiSelfTest.CoversProbeAndConversionSeam()`
  ~15).
- **Evidence:** `DiagnosticSelfTest.Run` calls `Check(WindowIdentitySelfTest.CoversIdentityTiers())` (`:287`),
  `Check(MonitorDpiSelfTest.CoversProbeAndConversionSeam())` (`:294`), `Check(ShowWindowSemanticsSelfTest.CoversPostStateSemantics())`
  (`:295`), `Check(MinTrackProbeSelfTest.InitializesEveryField())` (`:285`), `Check(ContainerGeometrySelfTest.UsesContainingMonitorWorkArea())`
  (`:298`).
- **Failure scenario:** A change breaks only `extremeCoordinatesDoNotOverflow` in the DPI test; the gate reports
  `failures=1` for the whole `MonitorDpiSelfTest` check with no pointer to the overflow path — the developer must
  bisect the method.
- **Impact:** Weak diagnosability of the gate; prolongs triage.
- **Root cause:** Convenience bool methods reused for both documentation and the gate.
- **Recommended direction:** Count assertions inside these methods (return `(checks, failures)`), or have the gate
  print which named sub-condition failed.
- **Verification recommendation:** Read of the methods and aggregation calls.

### [AUDIT-069] Unit test suite is flaky (contradicts unverified 146/146 claim)
- **Severity:** Medium
- **Confidence:** High
- **Category:** Tests / Reliability
- **Affected areas:** `tests/UnitTests/PersistenceSingleWriterTests.cs:87-119` (`SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration`); whole `tests/UnitTests` xUnit project (146 tests)
- **Summary:** The first-pass "146/146 pass" was a *reported number, never executed*. On actual execution
  (`dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug`, 4 runs) run 1 **FAILED 145/146**
  (`SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration`, 14 s — "latest async generation never reached disk");
  that single test passed **3× in isolation (<1 ms)**; full-suite runs 2–3 passed 146/146. The suite is
  **non-deterministically flaky** (~1/4 failure rate in this small sample).
- **Evidence:** `SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration` spins 60 `SaveAsync` calls then
  `SpinWait.SpinUntil(..., TimeSpan.FromSeconds(15))` for generation 59 (`:97-108`). Under full-suite thread-pool
  contention (the same project runs `PersistenceHammer_…` with 1000 `Task.Run` + 50 sync saves; `:200-249`) the 60
  off-thread writes can be starved past 15 s even though the latest-wins gate is correct. The hammer test itself
  awaits `WhenWritesSettledAsync()` (`:222`), which this test does not.
- **Failure scenario:** Any CI run (`build.yml` → `validate.ps1` `dotnet test`) that lands the persistence
  collection under pool pressure goes red on a *transient* test, eroding trust and inviting "re-run until green".
- **Impact:** False CI red / intermittent; directly refutes the prior all-green claim and is stronger evidence of
  *false confidence* than the first pass captured. Shutdown durability itself is correct (state flushes
  synchronously before `Shutdown(0)`, traced end-to-end) — the flake is a test-scheduling artifact, not a
  production data-loss bug.
- **Root cause:** Test couples correctness to a 15 s `SpinWait` against off-thread `Task.Run` writes sharing the
  global thread pool with the rest of the suite; no `WhenWritesSettledAsync()` await is used here.
- **Recommended direction (do not implement):** Have the test `await persistence.WhenWritesSettledAsync()`
  instead of a fixed `SpinWait`, or raise/parameterize the timeout; optionally coalesce the 60-call burden.
- **Verification recommendation:** Run the full suite ≥10× in CI and report flake rate; assert the failing test is
  purely a scheduling artifact (it passes in isolation).

### [AUDIT-023] Intentional-hide marker can be overwritten by a later normal Hide of the same window
- **Severity:** Medium *(re-rated from Low — DEEP-PERSIST-001)*
- **Confidence:** High
- **Category:** Correctness
- **Affected areas:** `Services/WindowShepherdService.cs:1881-1887` (`JournalMarkIntentionalHide`), `:1867-1879` (`JournalHide`), `:1646-1704` (`ReleaseIntentionalHide`/`ReleaseBoundaryFailure`), `:2493-2506`/`RescueOrphanedWindows` `DoNotRescue` branch); `Models/PersistedState.cs:72` (`DoNotRescue`)
- **Summary:** `JournalMarkIntentionalHide` writes `DoNotRescue=true` and **removes** the token from
  `_durablyJournaledCaptureTokens` (`:1885`). If `ReleaseIntentionalHide` then returns `RecoveryPending` (native
  `RemoveProp` failed, token still on the HWND, `DoNotRescue=true` entry remains on disk), the window is still
  registered in its group. When that member is later backgrounded, `Hide` is invoked; `JournalHide` sees the token
  **absent** from the set (`:1869`) and calls `UpsertJournalEntry(window, doNotRescue:false, …)` (`:1875`) —
  `doNotRescue` is a **hardcoded literal `false`** (`:1875`), ignoring any prior `DoNotRescue=true`. On the next
  launch `RescueOrphanedWindows` takes the non-`DoNotRescue` branch (`RestoreJournaledPresentation`, `:2509`) and
  **re-shows a window the user deliberately hid** (tray-style close / guest-initiated hide).
- **Evidence:** `UpsertJournalEntry` does `file.Entries.RemoveAll(e => IsSameJournalIdentity(e, entry));
  file.Entries.Add(entry)` with `entry.DoNotRescue = doNotRescue` (`:1897-1901`) — a later `false` upsert
  unconditionally replaces the earlier `true` entry. This violates the written behavioral guarantee in
  `openspec/changes/archive/2026-07-25-audit25-remediation/design.md:27`.
- **Failure scenario:** W captured → user sends W to tray (`Release(W, show:false)`) → native token-removal fails
  → `ReleaseIntentionalHide` returns `RecoveryPending`, on-disk `DoNotRescue=true`, HWND still carries token, W
  hidden but still a group member → user activates another tab → `Hide(W)` → token absent in set → entry flipped to
  `DoNotRescue=false` → force-kill → relaunch → `RescueOrphanedWindows` SHOWS W against intent.
- **Impact:** A deliberately-hidden window is resurrected on the next launch — a privacy/intent breach, not data
  loss. User-visible correctness violation of a written guarantee.
- **Root cause:** `DoNotRescue` intent and the `_durablyJournaledCaptureTokens` set are manipulated inconsistently
  across `JournalMarkIntentionalHide`/`JournalHide`; a `RecoveryPending` return from intentional-hide leaves the
  entry in an ambiguous state with no re-assertion of the marker.
- **Recommended direction:** On any `JournalHide`/`UpsertJournalEntry`, if the live identity already carries a
  `DoNotRescue` intent, preserve `DoNotRescue=true`; or re-add the token to the set inside `JournalMarkIntentionalHide`;
  or have `ReleaseIntentionalHide` clear the entry (as the normal-release paths do) instead of leaving
  `DoNotRescue=true` dangling on a `RecoveryPending` return.
- **Verification recommendation:** Deterministic `RecoveryJournalSelfTest`-style test: seed a `DoNotRescue=true`
  entry, drive `Release` → `RecoveryPending`, then `Hide`, assert on-disk entry still `DoNotRescue==true`.

### [AUDIT-026] Capture recycle weakness: same-process/class/title/thread window not distinguished by identity gate
- **Severity:** Medium *(re-rated from Low — DEEP-NATIVE)*
- **Confidence:** High
- **Category:** Security / Correctness
- **Affected areas:** `Services/WindowShepherdService.cs:398-689` (capture), final title recheck `:576-601`,
  `TryCompleteCaptureAfterJournal` `:270-338` (gate at `:285`, `SetProp` at `:308`);
  `Services/WindowIdentityGate.cs:255-328` (`EvaluateBeforeCaptureToken` — **no title check**), `:182-184` (token check)
- **Summary:** Within a single process, PID+thread+class+exe+process-start are identical for any two windows, so
  `processStartTimeUtcTicks` adds no discrimination between two windows of the *same* process. The per-capture
  token is the only cross-instance differentiator and is installed *after* the journal is committed.
- **Evidence (strengthened):** The gate re-run after `JournalCapture` is `EvaluateBeforeCaptureToken`
  (`WindowShepherdService.cs:285`), which checks `IsWindow`/token-availability/PID/thread/class/exe/process-start but
  **omits the title** — contrast the pre-journal final recheck at `:576-601` which *does* compare `finalTitle`. So
  the strong discriminator applied at `:596` gives **no** protection across the `JournalCapture`→`SetProp` span. The
  token (`SetProp` at `:308`) is therefore installed on whatever window currently owns the HWND value and matches
  pid/thread/class/exe/start. For multi-window same-GUI-thread apps (browsers/Office/IDEs), those five fields are
  **identical across windows**, so the token is the *only* differentiator and is applied **after** the gate.
- **Failure scenario:** A guest closes while being captured; the same GUI thread immediately creates a new
  top-level window of the same class/title (same exe/start). `TryCompleteCaptureAfterJournal` sees matching
  identity and installs the token on the recycled HWND; a later `Release` would mutate the recycled window.
- **Impact:** Captures the wrong (recycled) window; on release the wrong window is repositioned to the original's
  saved bounds (recovered, not destroyed), so bounded — hence Medium, not High.
- **Root cause:** Process-instance identity is process-scoped, not window-scoped; the token is the sole
  window-instance proof and is applied late. **Same root cause as AUDIT-068.**
- **Recommended direction:** After `JournalCapture`, persist the live HWND's `GetWindowThreadProcessId`/`GetClassName`
  snapshot and require the token install to land on the *same* window object already bound in `_capturedByHwnd`; or
  add a window-instance discriminator (see AUDIT-068).
- **Verification recommendation:** Deterministic unit test injecting identical PID/thread for two sequentially-probed
  HWNDs while `GetCaptureIdentityToken` returns zero until install, asserting `Capture` rejects the recycled object.

### [AUDIT-033] Cross-monitor DPI min-track for split members computed against each guest's own monitor
- **Severity:** Medium *(re-rated from Low — DEEP-SPLIT-002)*
- **Confidence:** Medium
- **Category:** Correctness (DPI)
- **Affected areas:** `Services/WindowShepherdService.cs:1122-1148` (`ToPhysicalScaleForGuest`), `:1068-1073`
  (min-track read); `Views/ContainerWindow.xaml.cs:2270-2331` (`RefreshSizeConstraint`/`ComputeContainerMinTrack`);
  `SplitGeometry.cs:58-63` (`MinContentWidth`)
- **Summary:** Split members are glued to the container's single content rect (one monitor), but each member's
  effective min-track is scaled by *that member's* monitor DPI (`MonitorFromWindow(guestHwnd,
  MONITOR_DEFAULTTONEAREST)` — the guest's **current** monitor, not the pane/container monitor). When the guest is
  on a **lower-DPI** monitor than the pane, the computed physical minimum is **too small**, under-constraining the
  container — a real correctness defect, not merely "over-large / not pixel-accurate".
- **Evidence (under-constrain math):** For a DPI-*unaware* guest, Windows scales its `WM_GETMINMAXINFO` logical min
  by the monitor beneath the guest. TabDock scales by `MonitorFromWindow(guestHwnd)` (`WindowShepherdService.cs:1135`).
  Let guest X be on MONITOR_A = 100% (logical min `m`); probe → `ToPhysicalScaleForGuest` uses DPI 96 → `lw = m`.
  Container on MONITOR_B = 150%. `MinContentWidth(true, lw, rw)` = `max(2·m, 2·rw−1)` ≈ `2·m`
  (`SplitGeometry.cs:62`). At that width the panes are ≈ `m` physical px, but on MONITOR_B at 150% X's **real**
  physical minimum is `ceil(1.5·m)`. So the pane (≈`m`) is **narrower than X can fit**, X overflows/refuses the
  pane (`MarkRefusingPane`, `ContainerWindow.xaml.cs:2760-2776`).
- **Failure scenario:** Multi-monitor, mixed DPI. A DPI-unaware app X open on a 100% monitor is captured, then
  entered into a split whose container sits on a 150% monitor. Until the next constraint probe re-reads X *after*
  it is docked on the 150% monitor, resizing the container below X's true minimum is permitted, producing a
  mispositioned/overflowing pane. (The over-constrain direction — guest on higher-DPI monitor than pane — remains
  fail-safe, as the first pass noted.)
- **Impact:** Docked layout breaks for mixed-DPI DPI-unaware guests crossing monitors.
- **Root cause:** Min-track DPI source is the guest's current monitor, but the pane geometry lives on the container's
  monitor; the two diverge whenever the guest is/was on a different monitor than the dock.
- **Recommended direction:** When both members are moored to one container, scale the containment minimum by the
  **container/pane monitor** DPI (`MonitorFromWindow(_containerHwnd,…)`) rather than `MonitorFromWindow(guestHwnd,…)`.
- **Verification recommendation:** Mixed-DPI two-monitor ValidationDriver scenario with a DPI-unaware GuineaPig
  whose probe min is set; assert the container's enforced `ptMinTrackSize` equals `2·ceil(m·paneDpi/96)`.

## 8. Low-Severity Findings

### [AUDIT-021] Failure-suppression HashSets are unbounded and keyed by recycled HWND (not cleared on release)
- **Severity:** Low *(Deep-dive update: reclassified — legitimate Low logic/resource-lifecycle issue, UI-thread-only, **not** a concurrency defect; see AUDIT-001 refutation)*
- **Confidence:** High
- **Category:** Correctness / Resource Lifecycle
- **Affected areas:** `Services/WindowShepherdService.cs:170` (`_positioningFailuresLogged`), `:176` (`_identityFailuresLogged`), `:208` and `:796` (only `.Add`, never cleared/removed)
- **Summary:** Two `HashSet<long>` keyed by `hwnd.ToInt64()` record "this HWND already logged a
  positioning/identity failure once." Added on every failure, never cleared, never scoped to a live
  `CapturedWindow`. Over a long session they grow unbounded; because the key is the HWND integer (which Windows
  recycles), a recycled HWND reused by an unrelated window inherits the stale "already logged" entry and
  suppresses that window's first legitimate failure log. (Consolidates SCOPE-NATIVE/UNBOUNDED-FAILURE-SUPPRESSION-SETS
  and SCOPE-PERF/LOG-SUPPRESSION-SETS-UNCLEARED — same two sets.)
- **Evidence:** Declaration `:170`/`:176`; `LogPositioningFailureOnce` (`:205-212`) does
  `_positioningFailuresLogged.Add(hwnd.ToInt64())`; `EvaluateCurrentCapturedWindow` (`:796`) does
  `_identityFailuresLogged.Add(window.Hwnd.ToInt64())`. No `.Clear()`/`.Remove(...)` exists (grep-confirmed).
- **Failure scenario:** Window A errors once (logged), is released, HWND recycled to a new window B. B's first real
  positioning failure is silently swallowed because A's stale entry still says "already logged."
- **Impact:** Bounded memory growth across a session; latent incorrect suppression of first-failure diagnostics for
  recycled HWNDs.
- **Root cause:** Failure-suppression state bound to the HWND value (Windows recycles it) rather than to the live
  captured-window lifetime, and never evicted.
- **Recommended direction:** Scope suppression to the live `CapturedWindow` (remove on release/destroy, key by
  object identity or `WindowIdentityToken`); or clear the set when the owning member is released.
- **Verification recommendation:** Deterministic test logging a failure for hwnd H, releasing it, forcing HWND reuse
  for a different window, then asserting the new window's first failure IS logged.

### [AUDIT-022] v1/v2 journal load force-sets OriginallyVisible = true (over-broad override)
- **Severity:** Low *(Deep-dive update — DEEP-PERSIST-002: confirmed Low with stronger evidence the overwritten value is doubly inert — `RescueOrphanedWindows` bails at `:2439` and the instance path sets `_journalLoadFailed` (`:2029`) which makes every `SaveJournal` throw (`:2374`) and every `UpsertJournalEntry` return false)*
- **Confidence:** Medium
- **Category:** Correctness
- **Affected areas:** `Services/WindowShepherdService.cs:2108-2114` (`LoadJournal` static), `Models/PersistedState.cs:84-91`
- **Summary:** For `sourceVersion < CurrentVersion` (v1 **and** v2), static `LoadJournal` unconditionally sets
  `entry.OriginallyVisible = true` and `entry.OriginalShowCommand = NativeMethods.SW_SHOW` for every entry,
  overwriting any `false` value a v2 file may have legitimately recorded. The justification comment is v1-specific
  but applied to v2.
- **Evidence:** `LoadJournal` `:2110-2114` comment references v1-only behavior; the v2 fixture
  (`RecoveryJournalSelfTest.LegacyV2IsPreserved`) includes `"OriginallyVisible": true` but the schema permits `false`.
- **Failure scenario:** A v2 journal entry recording a window that was *hidden* before TabDock shepherded it would,
  on a defensive runtime load, be force-classified as originally-visible.
- **Impact:** Latent only — v2 is never auto-rescued and runtime instance sets `_journalLoadFailed=true` for any
  `Version < 3`, so the overwritten value is never used by native rescue. No user-visible defect.
- **Root cause:** Over-broad version branch (treats v1 and v2 identically) driven by a v1-specific assumption.
- **Recommended direction:** Scope the `OriginallyVisible = true` override to `sourceVersion == LegacyMinimalVersion`
  (1) only, or key it on the entry actually lacking the field; leave v2 `OriginallyVisible` as deserialized.
- **Verification recommendation:** Unit test loading a v2 file with `"OriginallyVisible": false`, asserting the
  value is preserved by `LoadJournal`.

### [AUDIT-024] Split-pair presentation state is intentionally not persisted across restarts
- **Severity:** Low *(Deep-dive update — DEEP-PERSIST-003: confirmed Low / Info-by-design. Split-pair presentation state is deliberately not persisted; restored groups open empty containers. No defect)*
- **Confidence:** High
- **Category:** Architecture
- **Affected areas:** `Models/PersistedState.cs:12-39` (no split fields), `Models/SplitPresentationPolicy.cs`, `Services/SplitPresentationController.cs`
- **Summary:** Split-pair presentation (`SplitPresentationState`/`SplitPresentationController`) is purely runtime and
  **not** serialized into `state.json` or `hidden-windows.json`. `PersistedState`/`PersistedGroup` carry only
  `Groups`→`Tabs` (bounds/title/label). By design (project philosophy: "layout intent only; HWNDs don't survive
  reboots").
- **Evidence:** `PersistedState` has only `Version` + `Groups`; `PersistedGroup` has `Id/Name/AccentColor/ActiveIndex/Tabs`.
- **Failure scenario:** User defines a split pair, restarts TabDock → the pair relationship is gone.
- **Impact:** Acceptable by design, but diverges from user expectation that "split screen" persists.
- **Root cause:** Deliberate scope decision; split identity references bind to live `CapturedWindow`s.
- **Recommended direction:** Persist the split relationship keyed on stable `PersistedTab` identity (exe/title/bounds)
  if persistence is desired, or document the limitation in user-facing docs.
- **Verification recommendation:** Inspect `state.json` after defining a split pair and restarting; confirm no split fields.

### [AUDIT-025] Duplicate SHA-256 computed twice per pending recovery file
- **Severity:** Low
- **Confidence:** High
- **Category:** Performance
- **Affected areas:** `Services/PendingRecoveryService.cs:1216` and `:1218` (`ReadFile`)
- **Summary:** The full-file SHA-256 (`Sha256(rawBytes)`) is computed twice per pending file — once for
  `PendingRecoveryFile.SourceFileSha256` (`:1216`) and again into the local `sourceSha256` (`:1218`) used for every
  entry's fingerprint/ledger lookup.
- **Evidence:** `:1216 SourceFileSha256 = Sha256(rawBytes),` and `:1218 string sourceSha256 = Sha256(rawBytes);`.
- **Failure scenario:** Large pending evidence files → redundant full-buffer hashing on every `Discover`.
- **Impact:** Minor CPU; no correctness impact. Discover is read-only and infrequent.
- **Root cause:** Two separate expressions instead of one reused binding.
- **Recommended direction:** Compute `byte[] raw = rawBytes; string sourceSha256 = Sha256(raw);` once and assign to
  both the local and the `PendingRecoveryFile.SourceFileSha256`.
- **Verification recommendation:** Code review confirms a single call.

### [AUDIT-027] Min-track probe fails open to zero constraint when probe times out with no cached value
- **Severity:** Low
- **Confidence:** High
- **Category:** Correctness
- **Affected areas:** `Services/WindowShepherdService.cs:1057-1085` (`GetEffectiveMinTrackSize`)
- **Summary:** When the cross-process `WM_GETMINMAXINFO` probe times out / UIPI-blocks / fails AND no prior cached
  value exists, the method returns `(0, 0, false)` — size-constraint containment is silently dropped for that guest
  until a cached value exists.
- **Evidence:** `:1065` returns `(0, 0, false)` on first failure with no cached value; `:1080` same in the catch.
- **Failure scenario:** A guest hangs its message pump exactly when the dirty-constraint refresh probes it, with no
  prior successful probe, gets laid out with zero minimum size, so TabDock can size its pane smaller than the
  guest's true minimum (the guest then clamps itself, causing a visual mismatch).
- **Impact:** Cosmetic layout inconsistency, not a safety/correctness break.
- **Root cause:** "No cached value ⇒ unconstrained" is a deliberate fail-open for size policy.
- **Recommended direction:** On first-failure with no cache, fall back to the guest's current `GetWindowRect`
  dimensions as a conservative minimum, or surface "unavailable" to the caller.
- **Verification recommendation:** Unit test with a fake hung window returning 0 from `SendMessageTimeout`,
  asserting the chosen fallback.

### [AUDIT-028] Hide post-state race: guest re-showing itself between SW_HIDE and IsWindowVisible read
- **Severity:** Low *(Deep-dive update — DEEP-NATIVE: confirmed Low / benign. `ShowWindowVerified` re-reads `IsWindowVisible` immediately after `ShowWindow(SW_HIDE)`; a re-show yields `visibleAfter==true` → returns false → retained as `RecoveryPending` and retried. No wrong-window mutation; the guest is never lost)*
- **Confidence:** Medium
- **Category:** Correctness
- **Affected areas:** `Services/WindowShepherdService.cs:1445-1478` (`Hide`), `:1832-1840` (`ShowWindowVerified`)
- **Summary:** `Hide` trusts the post-state `IsWindowVisible` read immediately after `ShowWindow(SW_HIDE)`. If a
  guest re-shows itself on its own thread between the `SW_HIDE` and the read, the post-state check fails and the
  outcome is `RecoveryPending` rather than `Hidden`. This is safe (no wrong mutation) but means a guest that
  race-re-shows during the hide is left `RecoveryPending`.
- **Evidence:** `:1464 VisibilitySucceeded(previouslyVisible, visibleAfter, expectedVisible:false)`; the guest is
  never re-checked for a transient re-show.
- **Failure scenario:** Guest receives `SW_HIDE` and its own code immediately calls `ShowWindow(SW_SHOW)` on a
  different thread before TabDock's `IsWindowVisible` read.
- **Impact:** Benign — the transaction is retained as pending and retried; the guest is never lost.
- **Root cause:** Post-state verification is a single instantaneous read, not a brief settle check.
- **Recommended direction:** Optional short settle (re-read visibility once after ~16 ms) before declaring `Hidden`
  vs `RecoveryPending`.
- **Verification recommendation:** Unit test with a fake API that flips `IsWindowVisible` to true immediately after
  `ShowWindow(SW_HIDE)`; assert `RecoveryPending`.

### [AUDIT-029] Two distinct PresentationLayoutCoordinator instances (property vs field)
- **Severity:** Low *(Deep-dive update — DEEP-SPLIT: confirmed Low. `LayoutCoordinator` property (`:308`) is never read vs `_layoutCoordinator` field (`:150`) which is live — confirmed dead seam)*
- **Confidence:** High
- **Category:** Architecture / Maintainability
- **Affected areas:** `Views/ContainerWindow.xaml.cs:44` (`LayoutCoordinator { get; }`), `:150` (`_layoutCoordinator = new()`), `:308` (`LayoutCoordinator = new PresentationLayoutCoordinator();`), `:753` (`_layoutCoordinator.RequestRelayout`)
- **Summary:** The container owns `PresentationLayoutCoordinator` twice. The field `_layoutCoordinator` (`:150`) is
  the one actually used for relayout (`:753`). A separate get-only property `LayoutCoordinator` (`:308`) is assigned
  a fresh instance — a different object with no budget sink. Any external/diagnostic observer reading
  `container.LayoutCoordinator` observes a non-authoritative coordinator that never services a relayout.
- **Evidence:** `RequestRelayout` uses `_layoutCoordinator` (`:753`); the property is never read by production code.
- **Impact:** Test/diagnostic seam trap; two sources of truth for "the coordinator." Currently latent.
- **Root cause:** Refactor left a dead property alongside the live field.
- **Recommended direction:** Drop the `LayoutCoordinator` property (or make it return `_layoutCoordinator`); route
  all relayout and budget wiring through the single instance.
- **Verification recommendation:** N/A (dead-code cleanup).

### [AUDIT-030] Controller settle sub-state and ExplicitExit are dead in production
- **Severity:** Low
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `Services/SplitPresentationController.cs:20-21,42-43,85-86,185-198` (`_settlePending`,`_settleGeneration`,`ArmSettle`,`DisarmSettle`,`SettlePending`,`SettleGeneration`,`IsCurrentSettle`), `:134-173` (`ExplicitExit`)
- **Summary:** The container manages split settle entirely with its own fields, building the settle
  `SplitPresentationState` by hand. The controller's own settle fields (`_settlePending`, `_settleGeneration`,
  `ArmSettle`, `DisarmSettle`, `SettlePending`, `SettleGeneration`, `IsCurrentSettle`) are never driven in
  production; likewise `ExplicitExit` is never called (exit clears the relationship via `HandleMemberRemoved`).
- **Evidence:** `ExplicitExit` has no callers (grep); `IsCurrentSettle` (controller) has no production caller; the
  live settle check is `SplitPresentationPolicy.IsCurrentSettle` from `SplitInteractionFix.cs:264`.
- **Impact:** Duplicated, divergent settle representations; a future change to one path can silently disagree with
  the other. No runtime effect today.
- **Root cause:** State ownership migrated from controller to container without removing the controller's
  now-unused settle members.
- **Recommended direction:** Remove the dead settle fields/`ArmSettle`/`ExplicitExit` from the controller, or
  consolidate settle ownership in one place.
- **Verification recommendation:** N/A (dead-code cleanup).

### [AUDIT-031] SplitInteractionPolicy.Classify step-7 is a dead/confusing block
- **Severity:** Low *(Deep-dive update — DEEP-SPLIT: corrected + confirmed Low. Step-7 (`:132-144`) is unreachable: the sole production caller (`:94`) resolves at step 6; additionally steps 3–4 (stale/recovery fail-closed) never execute because the live caller **hardcodes** `nativeOutcome: Succeeded`)*
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `Services/SplitInteractionPolicy.cs:119-142`
- **Summary:** After the decisive `SuspendPairForGuest` branch (`:119-120`), there is a second
  `if (current.RelationshipDefined && !current.PairPresented && !isTargetSplitMember)` block (`:132-142`) whose
  body only ever `return SplitInteractionAction.None;` — identical to the final `return
  SplitInteractionAction.None;` (`:144`). The 20-line comment describes dormant guest switching via
  `SelectNonMember` that the code never performs.
- **Evidence:** `:132-144` both paths return `None`; the classifier is only invoked from
  `ContainerWindow.SplitInteractionFix.cs:94-101` with `isSplitPresented: true, isTargetSplitMember: false`, so it
  always resolves at step 6 (`SuspendPairForGuest`) and never reaches step 7.
- **Impact:** Misleading "policy" prose that does not match behavior.
- **Root cause:** Leftover from an earlier design.
- **Recommended direction:** Delete the dead block and consolidate the doc comment to match the single `None`
  outcome.
- **Verification recommendation:** N/A (comment/code cleanup).

### [AUDIT-032] PairZOrderBehindGuest identifies the split member by raw HWND equality
- **Severity:** Low *(Deep-dive update — DEEP-NATIVE: confirmed Low. Raw HWND equality, but downstream token gates (`PairZOrderBehind` `:1300`, `SetForeground` `:1539`) + ordered destroy handling bound impact to a self-healing no-op)*
- **Confidence:** Medium
- **Category:** Correctness
- **Affected areas:** `Views/ContainerWindow.xaml.cs:2568-2569,2572-2574` (`PairZOrderBehindGuest`, split branch)
- **Summary:** When a split member becomes system foreground, it is identified by `_splitController.Left.Hwnd ==
  foregroundHwnd` / `Right.Hwnd == foregroundHwnd`, then `FocusSplitMember` is driven from that match. Identity is
  by HWND value, not by the strong `CapturedWindow`+`IsCurrentCapturedWindow` gate used elsewhere.
- **Evidence:** Comparison uses `.Hwnd` equality; no `IsCurrentCapturedWindow` check before treating `fg` as the
  member.
- **Failure scenario:** An HWND is recycled so a different window now owns a value equal to a live split member's
  HWND and becomes foreground; `PairZOrderBehindGuest` would match it and call `FocusSplitMember` on the actual
  member.
- **Impact:** Edge-case (requires HWND reuse in the exact foreground WinEvent); self-heals on next interaction.
- **Root cause:** Direct HWND comparison instead of the established identity gate.
- **Recommended direction:** Resolve the member via `IsCurrentCapturedWindow`/reference identity, or ignore the event
  when the foreground HWND is not a current member.
- **Verification recommendation:** Unit test feeding a recycled-HWND foreground event; assert no spurious
  `FocusSplitMember`.

### [AUDIT-034] Coordinator budget sink never wired; layout/defer counts via coordinator are no-ops
- **Severity:** Low *(Deep-dive update — DEEP-SPLIT-003: Low, but **description corrected**. The ENTIRE presentation budget is dead — `PresentationBudget` and `WindowShepherdService.BudgetSink` are never assigned anywhere in production (grep), so even the "direct" `RecordLayoutSplit`/`RecordDeferBatch` calls are null no-ops, not merely a bypassed coordinator sink)*
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `Services/PresentationLayoutCoordinator.cs:108-109,114-117` (`RecordLayoutSplit`/`RecordLayoutSingle` → `_budget?.`), `Views/ContainerWindow.xaml.cs:150` (`_layoutCoordinator = new()` with no sink), `:2716,2724,2765` (direct `PresentationBudget?.RecordLayoutSplit()`), `Services/WindowShepherdService.cs:1224-1226` (`BudgetSink?.RecordDeferBatch()`)
- **Summary:** The production coordinator is constructed with no `IPresentationBudgetSink`, so the coordinator's
  `RecordLayoutSplit`/`RecordLayoutSingle`/`RecordDeferBatch` are null-guarded no-ops. Layout/defer budget counting
  actually happens by calling `PresentationBudget?.RecordLayoutSplit()` directly in `LayoutSplitPanes` and
  `BudgetSink?.RecordDeferBatch()` in `WindowShepherdService` — i.e., the coordinator's bookkeeping role is
  bypassed. Unit tests inject a sink, masking this in isolation.
- **Evidence:** `_layoutCoordinator` is `new()` with no args; grep confirms production relayout never injects a sink;
  budget recording duplicated via direct calls elsewhere.
- **Impact:** Two parallel budget paths; the coordinator's counters never populated in the shipping app.
- **Root cause:** Budget wiring moved to direct calls while the coordinator retained the (now dead) sink plumbing.
- **Recommended direction:** Either pass `PresentationBudget` into the coordinator used for relayout, or delete the
  coordinator's sink plumbing.
- **Verification recommendation:** N/A (consolidation cleanup).

### [AUDIT-035] ContainerWindow subscribes DeleteGroupRequested but never unsubscribes it
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Reliability / Maintainability
- **Affected areas:** `Views/ContainerWindow.xaml.cs:325` (`+=`), `:1020-1021` (unsubscribe list); `ViewModels/GroupViewModel.cs:106` (event definition)
- **Summary:** `ContainerWindow` subscribes `_viewModel.DeleteGroupRequested += ViewModel_DeleteGroupRequested` in its
  constructor but never unsubscribes it in `ContainerWindow_Closed`. The symmetric `EmptiedByPopOut` unsubscribe at
  `:1021` makes the omission asymmetric.
- **Evidence:** Grep shows subscription at `:325`, unsubscriptions at `:1020` (`PropertyChanged`), `:1021`
  (`EmptiedByPopOut`), `:1064`, `:1065`, `:1067`, `:1069` — but no `DeleteGroupRequested -=`.
- **Failure scenario:** If a `GroupViewModel` is ever retained after its container closes, the dead `ContainerWindow`
  (and its visual tree) would be kept reachable via the delegate and could re-enter `ViewModel_DeleteGroupRequested`
  (which shows a `MessageBox`) on a closed window.
- **Impact:** Latent memory/lifecycle leak and a possible stray dialog on a closed container; today masked by
  per-container VM lifetime.
- **Root cause:** Incomplete teardown symmetry — the unsubscribe list was written incrementally and this one missed.
- **Recommended direction:** Add `_viewModel.DeleteGroupRequested -= ViewModel_DeleteGroupRequested;` in
  `ContainerWindow_Closed` next to the `EmptiedByPopOut` unsubscribe.
- **Verification recommendation:** Lifecycle test that closes a container, forces the `GroupViewModel` to stay alive,
  and asserts `DeleteGroupRequested` no longer invokes the closed window's handler.

### [AUDIT-036] Rename blank/whitespace desyncs TextBox from model
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Correctness / UI
- **Affected areas:** `ViewModels/GroupViewModel.cs:23-36` (`Name` setter); `Views/ContainerWindow.xaml.cs:1613-1621` (`RenameBox_LostFocus`), `:1623-1644` (`RenameBox_KeyDown`)
- **Summary:** `GroupViewModel.Name` silently rejects blank/whitespace (correct for data integrity), but the rename
  `TextBox` is two-way bound with `UpdateSourceTrigger=LostFocus`. When the user clears the box, the binding pushes
  `""` → setter ignores it (no `PropertyChanged`) → the `TextBox` keeps displaying `""` while the model retains the
  old name.
- **Evidence:** `Name` setter returns early on `IsNullOrWhiteSpace` without raising notification (`:23-36`);
  `RenameBox_LostFocus` (`:1613`) only sets `IsRenaming=false` and calls `SaveState()` with no `UpdateTarget`; only
  the Escape branch (`:1640`) calls `UpdateTarget` to revert.
- **Failure scenario:** User opens rename, deletes the name, clicks elsewhere → box shows empty, model keeps prior
  name; reopening rename shows the stale empty string until some other refresh.
- **Impact:** Minor visual desync; no data corruption (intentional keep-old-name behavior), but confusing UX.
- **Root cause:** The reject-without-notify path leaves the two-way binding's source (TextBox) out of sync with the
  unchanged target.
- **Recommended direction:** In `RenameBox_LostFocus`, when the committed value was rejected, call
  `RenameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget()` to revert the visible text.
- **Verification recommendation:** Manual UI test (clear name, click away, reopen) or a binding-state unit test.

### [AUDIT-037] ActiveTab SelectedItem binding can transiently deselect during split presentation
- **Severity:** Low
- **Confidence:** Low
- **Category:** Correctness / UI
- **Affected areas:** `Views/ContainerWindow.xaml:148-149` (`ItemsSource=DisplayTabs`, `SelectedItem=ActiveTab Mode=OneWay`); `ViewModels/GroupViewModel.cs:171-208` (`SetSplitComposite`/`RebuildDisplayTabs`); `ViewModels/SplitCompositeViewModel.cs:52`
- **Summary:** The tab `ListBox` binds `SelectedItem` one-way to `ActiveTab` (a `TabViewModel`), but while a split is
  presented `DisplayTabs` contains a `SplitCompositeViewModel` in the LEFT slot and suppresses the RIGHT member. When
  `ActiveTab` is a split member, `SelectedItem` cannot resolve to a container item, so WPF clears the selection;
  the composite's highlight then depends solely on its `IsActive` (`= Left.IsActive || Right.IsActive`) → `IsSelected`
  TwoWay path.
- **Evidence:** `DisplayTabs` composite insertion at `GroupViewModel.cs:199-207`; `SelectedItem={Binding ActiveTab,
  Mode=OneWay}` at `ContainerWindow.xaml:149`; code special-cases split for `SelectedIndex` but the XAML
  `SelectedItem` binding is still active.
- **Failure scenario:** In split mode, a programmatic `SetActiveTab` to a split member sets `SelectedItem` to a
  `TabViewModel` not present in `DisplayTabs`; WPF may momentarily deselect before the composite's
  `IsActive`→`IsSelected` re-selects it, producing a transient highlight flicker.
- **Impact:** Cosmetic flicker; the visible result is still correct. Low confidence because the IsActive→IsSelected
  binding is believed to mask it.
- **Root cause:** `SelectedItem` (raw `TabViewModel`) and `ItemsSource` (`DisplayTabs`, containing composites) are not
  1:1 during split presentation.
- **Recommended direction:** Bind `SelectedItem` to the corresponding `DisplayTabs` entry, or drop the `SelectedItem`
  binding during split and rely on the `IsActive`/`IsSelected` path.
- **Verification recommendation:** Runtime UI test toggling split and switching active members while watching the
  strip selection/highlight.

### [AUDIT-038] ViewModelBase raises PropertyChanged with no Dispatcher/thread guard
- **Severity:** Low *(Deep-dive update — DEEP-CONC: confirmed Low / latent fragility only. `ViewModelBase` has no `Dispatcher` guard, but all current mutators are UI-thread (verified), so no active defect)*
- **Confidence:** Medium
- **Category:** Architecture / Concurrency
- **Affected areas:** `ViewModels/ViewModelBase.cs:15-30` (`SetProperty`/`OnPropertyChanged`); `ViewModels/MainViewModel.cs:19` (`Groups => _manager.Groups`)
- **Summary:** `ViewModelBase.SetProperty`/`OnPropertyChanged` raise `PropertyChanged` directly with no
  `Dispatcher`/thread check. `MainViewModel.Groups` is the same `ObservableCollection<Group>` owned by
  `GroupManager`. All currently observed mutators run on the UI thread, so it is safe today — but the design has no
  defensive marshalling, so any future worker-thread mutation would throw `InvalidOperationException`.
- **Evidence:** `ViewModelBase.cs:20` invokes `PropertyChanged?.Invoke` with no guard; `WinEventMonitor.cs:107-117`
  explicitly *refuses to start* without a UI `SynchronizationContext`, confirming UI-thread affinity by convention.
- **Failure scenario:** A future change publishes group/member changes from a background thread (async restore,
  batched persistence) and instantly breaks every bound `ListView`/`ListBox`.
- **Impact:** Latent fragility; no current defect.
- **Root cause:** Thread-affinity enforced only at the `WinEventMonitor` entry point, not at the
  `INotifyPropertyChanged`/`ObservableCollection` boundary.
- **Recommended direction:** Document the UI-thread contract on `ViewModelBase` (or marshal `PropertyChanged`/`CollectionChanged`
  through the captured `Dispatcher` if cross-thread mutation is ever needed).
- **Verification recommendation:** Static analysis / code-review gate.

### [AUDIT-039] Non-volatile `_uiContext` cross-thread read in WinEventMonitor
- **Severity:** Info *(re-rated from Low — DEEP-CONC-002)*
- **Confidence:** Medium
- **Category:** Concurrency
- **Affected areas:** `Services/WinEventMonitor.cs:189` (`_uiContext = null` in `Stop`), `:309` (`if (_uiContext != null)` in `Post`), `:185` (`_running = false` in `Stop`)
- **Summary:** The background callback thread reads the non-`volatile` field `_uiContext` (`:309`) while the UI
  thread writes it in `Stop()` (`:189`). Technically unsynchronized read/write pair.
- **Evidence:** `_uiContext` is a plain `SynchronizationContext?` field; the only sync in `Post` is the null check.
  `Raise` is guarded by `_running` (`:339`), which `Stop` sets to `false` *before* nulling `_uiContext`.
- **Failure scenario:** A non-volatile read could observe a stale non-null `_uiContext` after `Stop` — but `Raise`
  then reads `_running == false` and returns, so the worst outcome is a benign dropped event.
- **Impact:** None in practice (mitigated by the `_running` guard); code-smell/data-race flag.
- **Root cause:** Reliance on the `_running` guard instead of a memory barrier for the `_uiContext` hand-off.
- **Recommended direction:** Mark `_uiContext` and `_running` `volatile`, or guard both with a small `lock`.
- **Verification recommendation:** Static analysis (CA2002/thread-safety linter) would flag the cross-thread
  non-volatile field.
- **Deep-dive update (DEEP-CONC-002, Low → Info):** Not a cross-thread read at all. `Post` (read) and `Stop`
  (write) both run on the **UI thread** — `OnWinEvent`/`Post` run on the UI thread (see the AUDIT-001 refutation)
  and `Stop` is only ever called from UI-thread contexts (`Application_SessionEnding`, `SyncWinEventMonitor`, the
  `DispatcherTimer` retry, `Application_Exit`). There is no second executor, so the non-volatile flag is never
  touched by two threads. Code smell only.

### [AUDIT-040] Deferred batch empty list returns BeginFailed (misleading result code)
- **Severity:** Low
- **Confidence:** High
- **Category:** Correctness
- **Affected areas:** `Services/DeferredWindowPositionBatch.cs:112-113`
- **Summary:** `Apply` returns `DeferredWindowPositionResult.BeginFailed` when `entries.Count == 0`. That is a
  misleading result code for a zero-entry (no-op) batch — not a `BeginDeferWindowPos` failure. Production callers
  always pass >0 entries, so currently unreachable in production, but the API contract is wrong for the
  "zero/one/many" boundary.
- **Evidence:** `if (entries.Count == 0) return DeferredWindowPositionResult.BeginFailed;`
- **Failure scenario:** A future caller passes an empty list and treats `BeginFailed` as a real failure, triggering
  needless retry/error logging.
- **Impact:** Misleading telemetry/diagnostics; no functional break today.
- **Root cause:** Empty-list handled with a semantically wrong enum value rather than an explicit `Empty`/no-op.
- **Recommended direction:** Return a distinct `Empty` result (or `Applied` for a true no-op) and reserve
  `BeginFailed` for an actual `BeginDeferWindowPos == NULL` failure.
- **Verification recommendation:** Unit test `Apply` with 0 entries asserts the new result code.

### [AUDIT-041] Deferred BeginInvoke in SyncWinEventMonitor may run after monitor disposal
- **Severity:** Low *(Deep-dive update — DEEP-CONC-003: confirmed Low / safe-by-construction. The deferred `BeginInvoke` *can* run post-disposal, but the closure null/disposed checks + `Stop` early-return make it harmless)*
- **Confidence:** Medium
- **Category:** Reliability
- **Affected areas:** `App.xaml.cs:591-597` (`Dispatcher.BeginInvoke` in `SyncWinEventMonitor`), `App.xaml.cs:370-371` (`_events?.Dispose()` / `_winEventMonitorDisposed = true` in `Application_Exit`)
- **Summary:** `SyncWinEventMonitor` defers `_events.Stop()` by one dispatcher turn. That closure reads `_events`
  (a field) and can be pumped after `Application_Exit` has disposed the monitor and set `_winEventMonitorDisposed`.
  Safe today only because `WinEventMonitor.Stop` early-returns on `!_running && !HasInstalledHooks` and never
  checks `_disposed`.
- **Evidence:** `Stop()` guard at `WinEventMonitor.cs:183` does not consult `_disposed`; `Dispose()` (`:390-397`) calls
  `Stop()` then sets `_disposed = true`. The closure checks `_winEventMonitorDisposed`/`_events == null` but not
  whether `_events` was disposed.
- **Failure scenario:** `Application_Exit` disposes `_events`; the previously-queued `BeginInvoke` runs afterward and
  calls `_events.Stop()`. Today it returns early and is harmless, but safety depends on `Stop`'s early-return
  contract rather than an explicit disposed-check.
- **Impact:** None today; fragile — a future change to `Stop()` that touches `_disposed` or `_hook*` after the
  early-return could throw.
- **Root cause:** Late-bound dispatch closure over a long-lived field with no disposed-guard inside the delegate.
- **Recommended direction:** Capture the monitor reference into a local and null the field consistently, or add a
  `_disposed` check at the top of `Stop()`.
- **Verification recommendation:** Test disposing the monitor then pumping a pending `BeginInvoke`, asserting no
  exception and idempotent no-op.

### [AUDIT-042] Diagnostic export hotkey runs ZIP build synchronously on the UI thread
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Performance
- **Affected areas:** `App.xaml.cs:247-270` (`ExportDiagnosticsFromHotkey`); `Services/HotkeyService.cs:125-129` (raised from `WndProcHook` on UI thread)
- **Summary:** `Ctrl+Alt+Shift+D` raises `DiagnosticHotkeyPressed` from the HwndSource hook (UI thread), which
  synchronously calls `DiagnosticReportService.ExportBundle(path)` — a ZIP assembly over logs/state — blocking the UI
  thread for the duration.
- **Evidence:** `HotkeyService.WndProcHook` (`:117-132`) raises on the UI thread; `App.ExportDiagnosticsFromHotkey`
  (`:247-270`) calls `ExportBundle` inside a `try` with no off-thread dispatch.
- **Failure scenario:** With a large rotated log set, the diagnostic export briefly freezes the launcher and any open
  containers.
- **Impact:** Momentary UI unresponsiveness on an explicit user action; low severity.
- **Root cause:** Diagnostic export not offloaded to a background task/awaited.
- **Recommended direction:** Offload `ExportBundle` to a background thread / `Task.Run` and surface completion via UI.
- **Verification recommendation:** Manual: trigger export with oversized logs and observe UI responsiveness.

### [AUDIT-043] `--version` leaks raw executable path (and thus username)
- **Severity:** Info *(re-rated from Low — FALSE POSITIVE, DEEP-SEC-043)*
- **Confidence:** High
- **Category:** Privacy / Consistency
- **Affected areas:** `Services/DiagnosticCommandLine.cs:104`; `Services/DiagnosticReportService.cs:66-82` (`:75`, `:81`), `:33, :99, :207`; `Services/BuildIdentity.cs:61`
- **Summary:** **FALSE POSITIVE.** The first pass concluded `--version` "leaks raw executable path (and thus
  username)" because `DiagnosticCommandLine.cs:104` passes an **unredacted** `BuildIdentity` to `FormatVersion`
  and `:75` emits `executable: {identity.ExecutablePath}` with "no redaction". The first pass stopped reading at
  `:75` and missed `:81`, which is the decisive control: `FormatVersion` **returns
  `DiagnosticEnvironmentService.SanitizeText(builder.ToString().TrimEnd())`**. The ENTIRE output — including the
  raw `ExecutablePath` — is run through the redactor before it leaves the process.
- **Evidence:** `DiagnosticEnvironmentService.cs:244-275` `SanitizeText` replaces `appData`→`%APPDATA%`,
  `userProfile`→`%USERPROFILE%`, `localAppData`→`%LOCALAPPDATA%` (`:247-254`), `Environment.UserName`→`<user>`
  (`:257-258`), and any absolute path via the `s_absolutePath` regex (`[A-Za-z]:[\\/]|\\)[^\r\n"'<>|]+`)→`<path>`
  (`:267`). So for an installed exe the `localAppData` replacement yields `executable: %LOCALAPPDATA%\TabDock\TabDock.exe`;
  for a dev path under `C:\Users\Michael Roy\Documents\...` the `userProfile` replacement yields
  `executable: %USERPROFILE%\Documents\TabDock\...`; and in every residual case the absolute-path regex collapses
  the whole path to `executable: <path>`. The username never appears.
- **Failure scenario (claimed, not real):** `TabDock.exe --version` prints
  `executable: C:\Users\Michael Roy\AppData\Local\TabDock\TabDock.exe`. **Actual output:** the redaction contract
  holds (one of the three forms above). No PII disclosure.
- **Impact:** None. No username/profile-path leak.
- **Root cause (of the first-pass miss):** The analysis stopped at the raw emit at `:75` and did not follow the
  final `SanitizeText` at `:81` (the same pattern erroneously treated `FormatDoctor`/`ExportBundle` as the only
  redacted paths, while `FormatVersion` self-sanitizes).
- **Recommended direction:** No code change. Optionally add a deterministic self-test asserting
  `TabDock.exe --version` output contains neither `C:\Users\...` nor the username, to lock in the guarantee.
- **Verification recommendation:** `dotnet publish` the exe, run `TabDock.exe --version`, confirm the `executable:`
  line is `%LOCALAPPDATA%`/`<path>`/`%USERPROFILE%`/` <path>`, never a literal profile path or username.

### [AUDIT-044] Privacy self-test pending-recovery check is vacuous
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests / Privacy
- **Affected areas:** `Services/DiagnosticPrivacySelfTest.cs:51-69`; `Services/PendingRecoveryService.cs:101-134`
- **Summary:** The pending-recovery portion writes a fixture `hidden-windows.json.pending` whose only sensitive field
  is `ExePath:"C:\Users\private\guest.exe"`, then asserts the `FormatDiscovery` output does NOT contain `pendingRoot`,
  `"private"`, `"guest.exe"`, or `"window title"`. But `FormatDiscovery` (`:101-134`) **never prints `ExePath` or a
  title** — the assertions are trivially true and provide zero coverage of path/title redaction. The real
  redaction-sensitive path (candidate titles printed in `RunInteractive` at `:246`/`:273`) is not exercised.
- **Evidence:** `FormatDiscovery` body has no `ExePath`/`Title` emission; self-test asserts absence of strings the
  function never emits; the `title="..."` lines exist only in `RunInteractive`, which the self-test does not call.
- **Failure scenario:** If `FormatDiscovery` were changed to print raw `ExePath`, the self-test would still pass.
- **Impact:** False privacy confidence; the strongest redaction guarantee the test appears to make is not actually tested.
- **Root cause:** Test fixture and assertions written against an assumed output shape that doesn't match.
- **Recommended direction:** Make the fixture assertion meaningful (assert `FormatDiscovery` output equals a known
  redacted shape), or extend the test to call the candidate-listing path and assert titles are shown only via the
  bounded terminal sanitizer and paths are leaf-only.
- **Verification recommendation:** Test forcing a candidate with a private `ExePath` and sensitive `Title` through the
  actual listing path; assert the full path and raw title never appear.

### [AUDIT-045] No correlation ID links the rotating log to the diagnostic trace
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Observability
- **Affected areas:** `Services/LoggingService.cs:109-138`; `Services/DiagnosticTrace.cs:31-65`; `Models/Diagnostics.cs:206-230`
- **Summary:** The two observability channels — rotating text log (`LoggingService.Log`, tagged lines) and structured
  `DiagnosticTrace` (ring buffer) — share no correlation identifier. `DiagnosticEventRecord` carries `Sequence`,
  `TimestampUtc`, `Kind`, `GroupId`, HWNDs, `Action`, `Result`, but nothing linking a trace event to its
  corresponding log lines.
- **Evidence:** `LoggingService.Log` (`:109`) takes only a formatted string; `DiagnosticEventRecord`
  (`:206-230`) has no log-correlation field; trace records serialized into `trace.jsonl`, log into `recent-log.txt`
  independently.
- **Failure scenario:** A `guest.capture` trace event occurs but the corresponding SHEPHERD log line is several lines
  away; correlating them requires manual timestamp/HWND cross-referencing and is lossy.
- **Impact:** Reduced diagnostic usefulness of the very bundle the diagnostics system exists to produce.
- **Root cause:** The two subsystems built independently without a shared operation/span id.
- **Recommended direction:** Thread an optional correlation id (per-operation GUID or the trace `Sequence`) into both
  `Log` and `Record`.
- **Verification recommendation:** Export a bundle, confirm a given capture operation's trace record and log lines
  share a common id.

### [AUDIT-046] Secret-redaction coverage gaps in SanitizeText
- **Severity:** Low *(Deep-dive update — DEEP-SEC: confirmed Low. Gaps real (`pwd=`, `client_secret=`, URL-with-credentials) but no in-repo caller logs such material)*
- **Confidence:** Medium
- **Category:** Privacy / Maintainability
- **Affected areas:** `Services/DiagnosticEnvironmentService.cs:24-32`
- **Summary:** `SanitizeText`'s credential regexes cover `password|passwd|token|secret|api[-_]?key|authorization` and
  `Bearer …` and `secret-/token-/api_key-<value>` tokens, but **not** `pwd=`, `pass=`, bare `key=`,
  `client_secret`-style without the exact keyword, or credentials embedded in URLs (e.g.
  `https://user:password@host/`). The absolute-path regex only matches `[A-Za-z]:\…` or `\\…`, so a
  `https://…` URL with an embedded password is neither path- nor secret-redacted.
- **Evidence:** Regex definitions at `:24-32`; no `pwd`/`pass`/`key`/`url` handling.
- **Failure scenario:** A future caller logs `connection=pwd=Tr0ub4dor&3` or `url=https://alice:s3cr3t@api.example.com/x`
  under a kept tag; the value survives into the bundle.
- **Impact:** Low because no in-repo caller logs such material, but the redactor is not defense-in-depth complete.
- **Root cause:** Keyword list intentionally narrow to avoid false positives.
- **Recommended direction:** Broaden the secret matcher (add `pwd`, `pass`, `client_?secret`, URL-with-credentials
  pattern); or hash any value that fails a "looks like a secret" heuristic.
- **Verification recommendation:** Add adversarial fixtures for `pwd=`, `pass=`, `https://user:pass@host` to
  `DiagnosticPrivacySelfTest` and assert redaction.

### [AUDIT-047] PresentationOperationBudget/relayout-coalesce tests are tautological
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests
- **Affected areas:** `tests/UnitTests/PresentationOperationBudgetTests.cs:277-321`; `tests/UnitTests/RequestRelayoutFinalPassTests.cs:71-80`; `Services/PresentationLayoutCoordinator.cs:95-103` (`CoalesceAndExecute`)
- **Summary:** The "coalesce" budget tests are tautological/misnamed. `CoalesceAndExecute` always executes its
  callback exactly once regardless of `coalescedRequests` (does not touch the counter); the budget counts are
  incremented by the test's *own* lambda, not by the coordinator. `UnchangedLayoutUpdated_ProducesNoRelayout` merely
  calls `RequestRelayout` once and asserts one execution — it never tests the "unchanged layout → no relayout"
  suppression its name claims.
- **Evidence:** `PresentationLayoutCoordinator.cs:95-103` sets `_relayoutPending=true; _relayoutPending=false;
  execute();` — no use of `coalescedRequests`. `PresentationOperationBudgetTests.cs:279-291` asserts `executes==1`
  after passing `coalescedRequests:5`; `RecordLayoutSingle` is called inside the test lambda.
- **Failure scenario:** A bug that genuinely stopped coalescing (N requests → N passes) would NOT be caught.
- **Impact:** False confidence in the relayout-coalescing contract (a core perf/stability invariant).
- **Root cause:** Tests built around a synthetic `CoalesceAndExecute` helper rather than real `RequestRelayout` +
  deferred `scheduleRender` callback.
- **Recommended direction:** Drive real coalescing: enqueue N `RequestRelayout` calls before the `scheduleRender`
  callback is invoked, then assert exactly one `execute`. Rename `UnchangedLayoutUpdated_ProducesNoRelayout` or make
  it actually suppress on unchanged layout.
- **Verification recommendation:** Test calling `RequestRelayout` 5× into a queue and asserting `executes==1` after
  draining.

### [AUDIT-048] SaveAsync synchronous-guard assertion is racy
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Reliability / Tests
- **Affected areas:** `tests/UnitTests/PPersistenceSingleWriterTests.cs:65-84` (`SaveAsync_DoesNotWriteSynchronously`); `Services/PersistenceService.cs:149` (`Task.Run`)
- **Summary:** The test asserts `File.Exists(path)` is `false` *immediately* after `SaveAsync`, which dispatches via
  `Task.Run`. This is timing-dependent: on a fast threadpool the file may already exist, making the assertion flaky.
  It passed this run (146/146) but is fundamentally racy.
- **Evidence:** `PersistenceService.cs:149 Task.Run(() => CommitJson(json, generation))`; the test checks
  `File.Exists(path)` right after the call, with no delay.
- **Failure scenario:** Under CPU pressure or a fast threadpool, the immediate `File.Exists` returns `true` → spurious
  test failure.
- **Impact:** Intermittent CI red on the persistence test, eroding trust in the suite.
- **Root cause:** Single instantaneous existence check instead of a bounded "did not write within X ms" poll.
- **Recommended direction:** Assert eventual completion (already done with `SpinWait` later) and, for the
  synchronous-guard, poll a tight window (`SpinWait.SpinUntil(() => !File.Exists(path), 200ms)`).
- **Verification recommendation:** Run the test in a tight loop under `ThreadPool` saturation; observe occasional failure.

### [AUDIT-049] Converter nullability signature mismatch (CS8625 warnings)
- **Severity:** Low
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `Converters/BoolToVisibilityConverter.cs:11`; `Converters/ColorToBrushConverter.cs:11` (`Convert(object value, …)`); `tests/UnitTests/ConverterTests.cs` (12 `CS8625` warnings); `docs/TESTING.md:466` (zero-warnings policy)
- **Summary:** Both converters declare `Convert(object value, …)` with a **non-nullable** `value`, but their runtime
  logic safely handles `null`, and `ConverterTests` asserts `null` renders `Collapsed`/`Transparent`. This mismatch
  produces `CS8625` warnings and contradicts the tested contract, while the repo policy expects zero warnings.
  **Note:** the `dotnet build TabDock.sln` run in this consolidation produced **0 warnings**, so this may be
  configuration-specific (warnings-as-errors/analyzer settings) — re-confirm in the relevant build before action.
- **Evidence:** `ConverterTests.cs(29,76)` etc. → `warning CS8625`; `BoolToVisibilityConverter.cs:14 bool flag =
  value is true;` handles null; `ConverterTests.cs:38-40` passes `null`.
- **Failure scenario:** A maintainer trusts the `object value` (non-null) signature, removes the null guard, and
  breaks the verified "int Groups.Count → Collapsed" contract.
- **Impact:** Violates the project's zero-warning rule and encodes a lie about the method's contract.
- **Root cause:** Signature annotated stricter than actual behavior/tested contract.
- **Recommended direction:** Declare `Convert(object? value, …)` to match behavior and silence the warnings.
- **Verification recommendation:** Change signature to `object?`, rebuild — warnings disappear, all `ConverterTests`
  still pass.

### [AUDIT-050] Redundant near-duplicate split-policy Facts inflate coverage count
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests / Maintainability
- **Affected areas:** `tests/UnitTests/SplitPresentationPolicyTests.cs:199-236,409-451`; `tests/UnitTests/SplitInteractionPolicyTests.cs` (≈40 Fact methods)
- **Summary:** The split-policy files contain a very high density of near-duplicate `Fact`s asserting the same pure
  state-machine transitions. This inflates the perceived coverage count (146 unit facts total) without adding
  marginal assertion power, and can mask real gaps by making the suite *look* exhaustive.
- **Evidence:** `SplitPresentationPolicyTests.cs:199-236` (three near-identical "dormant survives" loop facts);
  `:409-451` (two "dormant survives many cycles" facts); `SplitInteractionPolicyTests.cs` repeats `None`/`IgnoreButton`/
  `RejectStale` across permutations.
- **Failure scenario:** A genuinely untested transition (e.g., a new `SplitNativeTransitionOutcome` value) hides behind
  the volume of passing but redundant facts.
- **Impact:** False confidence from count; maintenance drag.
- **Root cause:** White-box enumeration of a pure function rather than parameterized invariants.
- **Recommended direction:** Consolidate into `[Theory]`/`[InlineData]` cases keyed to distinct invariants; reserve
  richer sequence coverage for the production `DeterministicSelfTests`.
- **Verification recommendation:** Reduce fact count via parameterization; ensure each distinct invariant still has
  at least one case.

### [AUDIT-051] Firefox scenarios advertised but never executed/verified
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests
- **Affected areas:** `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs:165-172,347-352`; `docs/internal/TEST_PLAN.md §4`
- **Summary:** Firefox scenarios are written into the catalog and accept `--guest firefox-normal`, but are documented
  as never executed/verified on the dev machine (HYPOTHESIS). The catalog advertises Firefox coverage that produces
  no evidence in any run. The harness already SKIPs absent browsers, so this is largely honestly handled.
- **Evidence:** `Scenarios.cs:165-172` comment: "Firefox is not exercised on the dev machine … the case exists so the
  code path is written and reviewable, but it cannot be run/verified there." `TEST_PLAN.md §4` confirms Firefox is not
  installed.
- **Failure scenario:** A Firefox-specific break (window class `MozillaWindowClass`) ships unverified under a green run.
- **Impact:** Coverage gap for one major browser; honestly handled via SKIP.
- **Root cause:** Environment limitation (Firefox not installed), declared up front.
- **Recommended direction:** Keep the written-but-unverified cases clearly labeled HYPOTHESIS and ensure the CLI, when
  Firefox is absent, reports SKIP rather than silently passing.
- **Verification recommendation:** Run `browser-multi --guest firefox-normal` without Firefox; confirm SKIP/BLOCKED,
  not false PASS.

### [AUDIT-052] Docs misstate which workflow publishes (release.yml vs publish-release.yml)
- **Severity:** Low
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `docs/release/repository-protection.md:18`
- **Summary:** The doc says the production path is gated by the dispatch-only `release.yml` workflow "which
  re-verifies artifact provenance … and the Authenticode signature before creating a v* tag." In reality `release.yml`
  is RC-qualification-only and never publishes or creates a tag; the publish/provenance-verification/tag-creation
  workflow is `publish-release.yml` (Stage B).
- **Evidence:** `repository-protection.md:18` vs `release.yml` header (`# RC QUALIFICATION-ONLY workflow (never
  publishes)`) and `publish-release.yml` ("STAGE B … creates the GitHub Release").
- **Impact:** Reader confusion about which workflow actually publishes.
- **Root cause:** Doc drift after the two-stage split.
- **Recommended direction:** Correct the reference to `publish-release.yml` (Stage B).
- **Verification recommendation:** Read the three release workflows and the doc paragraph.

### [AUDIT-053] System.Threading.AccessControl pinned to 10.0.11 with misleading comment in a net8.0 project
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Build / Maintainability
- **Affected areas:** `TabDock.csproj:51,17`
- **Summary:** `System.Threading.AccessControl` is pinned to `10.0.11` (a .NET 10 servicing package) with a comment
  claiming "Stable .NET 8 ACL support" and "has no net8.0 transitive dependencies." It does ship a `lib/net8.0`
  asset (build confirmed 0 warnings/0 errors), but the version number and comment are misleading for a `net8.0`
  target. Also `NuGetAudit` is disabled locally (`<NuGetAudit>false</NuGetAudit>`).
- **Evidence:** `TabDock.csproj:51 Version="10.0.11"`; `obj/project.assets.json` shows `lib/net8.0` resolved to
  `10.0.11`; `:17` disables local audit.
- **Impact:** Confuses maintainers about supported runtimes; local vulnerability audit off.
- **Root cause:** Pinning a .NET 10-era package version in a net8.0 project; comment not updated.
- **Recommended direction:** Pin a net8-aligned version (e.g., `8.0.0`) if the 10.0.11 API isn't required, or document
  *why* 10.0.11 is needed; keep NuGetAudit awareness for local builds.
- **Verification recommendation:** Build (done) confirms it compiles; audit the resolved dependency tree.

### [AUDIT-054] release-qualify.ps1 has two .DESCRIPTION help blocks
- **Severity:** Low
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `scripts/release-qualify.ps1:5-29` and `:59`
- **Summary:** The script's comment-based help contains two `.DESCRIPTION` blocks; PowerShell only honors the first,
  so the second is dead and can mislead readers.
- **Evidence:** `release-qualify.ps1:5-29` (first `.DESCRIPTION`), `:59` (second).
- **Impact:** Minor doc confusion.
- **Root cause:** Copy/paste of help sections.
- **Recommended direction:** Remove the duplicate `.DESCRIPTION`.
- **Verification recommendation:** `Get-Help` shows only the first description.

### [AUDIT-055] sign-release.ps1 emits Status=SIGNED on a failed signature verification
- **Severity:** Low *(Deep-dive update — DEEP-SEC-055: confirmed Low and **double-neutralized**. `release-qualify.ps1:392` gates on exit code before recording the JSON `Status`; `Test-PublicationEligibility` (`release-tooling.ps1:1124`) independently runs `signtool verify` on the actual bytes. The misleading `Status=SIGNED` JSON never reaches disk)*
- **Confidence:** Medium
- **Category:** Correctness
- **Affected areas:** `scripts/sign-release.ps1:192-198` (`Complete-RealSignerValidation`)
- **Summary:** When `Test-AuthenticodeSignature` fails, the code sets `$script:status = 'SIGNED'` (then
  `verification = 'FAILED'`, returns `3`). Emitting `Status=SIGNED` on a failed verification is semantically wrong;
  currently safe only because every caller checks the exit code (3) before using the JSON.
- **Evidence:** `sign-release.ps1:192-198`.
- **Impact:** Latent: a future caller that ignores the exit code would treat a failed-sig verification as `SIGNED`.
- **Root cause:** Mislabeled status field in the failure branch.
- **Recommended direction:** Set `status = 'SIGNING_FAILED'` in that branch.
- **Verification recommendation:** Unit/regression test capturing the emitted JSON on a tampered/unsigned file.

### [AUDIT-056] Solution platform matrix advertises Any CPU/x86 but csproj forces win-x64
- **Severity:** Low
- **Confidence:** High
- **Category:** Build
- **Affected areas:** `TabDock.sln:15-35` vs `TabDock.csproj:31`
- **Summary:** The solution advertises Debug/Release × {Any CPU, x64, x86}, but the main `TabDock.csproj`
  unconditionally forces `RuntimeIdentifier=win-x64`, so the "Any CPU"/x86 configs never produce their nominal RIDs —
  every build is win-x64. CI uses `dotnet build`/`dotnet test`, which follow the csproj, so this is not functional.
- **Evidence:** `TabDock.sln:15-35` config list vs `TabDock.csproj:31 <RuntimeIdentifier>win-x64</RuntimeIdentifier>`.
- **Impact:** Confusing project configuration; no functional defect.
- **Root cause:** Unconditional RID in csproj not reflected in the solution configs.
- **Recommended direction:** Align solution configs to win-x64, or document that RID is forced.
- **Verification recommendation:** Inspect build outputs for each solution config.

### [AUDIT-057] .agent/STATE.md internal test-count inconsistencies
- **Severity:** Low
- **Confidence:** High
- **Category:** Maintainability
- **Affected areas:** `.agent/STATE.md` (unit-test counts "141"/"136"/"146"; release-tooling "137"/"139"/"118"/"134"/"137")
- **Summary:** `.agent/STATE.md` presents test/regression counts as current facts that contradict each other within
  the same file. Unit-test counts appear as 141, 136, and 146; release-tooling counts as 137, 139, 118, 134, and 137.
  A reader cannot determine the true current count.
- **Evidence:** Direct quotes from `.agent/STATE.md` lines ~28 ("141 PASS"), ~410/413 ("136 deterministic cases"),
  ~473 ("146 PASS"), ~409 ("137 cases"), ~480 ("139 PASS"), ~213 ("118"), ~249 ("134"), ~407 ("137 cases").
- **Failure scenario:** A reviewer/CI gate trusts one number and over-/under-states qualification status.
- **Impact:** Doc hygiene / false precision. Low because counts are snapshots, not code-affecting.
- **Root cause:** Counts appended per campaign without reconciling earlier entries or pinning a single "current" value.
- **Recommended direction:** Maintain one canonical "current test counts" line (updated at each milestone) and mark
  historical counts as dated, or generate the number from `dotnet test` output.
- **Verification recommendation:** Read `.agent/STATE.md`; the contradictory numbers are co-located and self-evident.

### [AUDIT-058] Local-PFX signing passes the PFX password on the command line + sets an unused env var
- **Severity:** Low *(Deep-dive update — DEEP-SEC-058: confirmed Low, but the **sub-claim corrected**. `$env:TABDOCK_SIGN_PASSWORD` is consumed at `sign-release.ps1:373` (it is the password holder, not dead). The core concern — the password ends up on `signtool`'s command line — stands; dev/RC only)*
- **Confidence:** High
- **Category:** Security
- **Affected areas:** `scripts/sign-release.ps1:365-390`
- **Summary:** In the `local-pfx` path the PFX password is passed to signtool as `/p $env:TABDOCK_SIGN_PASSWORD`,
  placing the exportable-PFX password in the signing process's command line, readable by other local processes via
  process enumeration. A redundant `$env:TABDOCK_SIGN_PASSWORD` env var is set but never consumed by signtool (the
  value is already expanded into the argument array), adding an exposure window with no benefit.
- **Evidence:** `& $signtool @('sign','/fd','sha256','/f',$tempPfx,'/p',$env:TABDOCK_SIGN_PASSWORD,'/tr',...)`
  (`:373-375`); env var set at `:369` and never read by signtool.
- **Failure scenario:** A co-located malicious/privileged local process on the signing machine reads the signtool
  command line and recovers the exportable PFX password during the signing window.
- **Impact:** Disclosure of the code-signing PFX password. Bounded because `local-pfx` is explicitly dev/RC-only and
  never the production signer.
- **Root cause:** signtool's `/p` only accepts the password inline; the env-var indirection is dead code that
  additively expands exposure.
- **Recommended direction:** Drop the unused env variable; prefer the DigiCert/HSM path for any sensitive signing;
  document that `local-pfx` must run on a trusted, single-tenant host.
- **Verification recommendation:** Inspect signtool.exe command line on a signing host during `local-pfx` execution.

### [AUDIT-059] Local-PFX temp PFX file created with default (inheritable) ACL
- **Severity:** Low *(Deep-dive update — DEEP-SEC-059: confirmed Low; **priority overstated** in the first pass. `%TEMP%` is per-user (user+SYSTEM ACL); the file is deleted synchronously in `finally`; multi-user/shared-TEMP is required for harm. Dev/RC only)*
- **Confidence:** Medium
- **Category:** Security
- **Affected areas:** `scripts/sign-release.ps1:365-390`
- **Summary:** The exportable PFX is written with `WriteAllBytes` to `%TEMP%\TabDock-cert-<guid>.pfx`, which uses
  default (typically user+SYSTEM, inheritable) ACLs. Until the `finally` block deletes it, another local
  user/process able to read the temp directory could read the exportable private key. The path is random (guid) and
  deleted promptly, reducing the window.
- **Evidence:** `$tempPfx = Join-Path ([IO.Path]::GetTempPath()) ("TabDock-cert-"+[Guid]::NewGuid()...);
  [IO.File]::WriteAllBytes($tempPfx, ...)` (`:365-368`), deleted in `finally` (`:387-389`).
- **Failure scenario:** Local multi-user host; another principal reads the PFX from the shared temp directory during
  signing.
- **Impact:** Exportable code-signing key disclosure (dev/RC only).
- **Root cause:** Temp file created with default ACL rather than a tightly-scoped, current-user-only ACL.
- **Recommended direction:** Create the temp PFX with an explicit current-user-only ACL (or use an in-memory key where
  permitted), and delete before any long-running step.
- **Verification recommendation:** Use a file-monitor / `icacls` probe during a `local-pfx` run.

### [AUDIT-060] Diagnostic export bundle has no destination-path validation
- **Severity:** Low *(Deep-dive update — DEEP-SEC-060: confirmed Low. User-controlled destination; no untrusted input)*
- **Confidence:** High
- **Category:** Security
- **Affected areas:** `Services/DiagnosticReportService.cs:184-218`; `Services/DiagnosticCommandLine.cs:112-117,135-140`; `App.xaml.cs:251-262`
- **Summary:** `ExportBundle(outputPath)` only does `Path.GetFullPath(outputPath)` and writes there; it does not
  restrict the destination to a safe directory. The `outputPath` originates from CLI arguments the user types
  themselves. The in-product hotkey path generates a timestamped name on the Desktop. `File.Move(..., overwrite:true)`
  silently replaces an existing file at that path.
- **Evidence:** `string path = ... Path.GetFullPath(outputPath);` (`:186-188`); `File.Move(temporaryPath, path,
  overwrite: true)` (`:218`).
- **Failure scenario:** `TabDock.exe support --output C:\Windows\System32\foo.zip` writes to a system dir if the user
  has rights (self-inflicted; no elevation beyond the user's own token).
- **Impact:** User-controlled overwrite of an existing file / writing to unexpected locations; minimal since it
  requires the user's own intent + permissions.
- **Root cause:** No allow-list / safe-directory check on the export destination.
- **Recommended direction:** Validate that the resolved directory is under a user-writable, expected root
  (Desktop/Documents/current dir) or warn before overwriting an existing file.
- **Verification recommendation:** Run `support --output <existing-file>` and confirm overwrite behavior.

### [AUDIT-061] Diagnostic export uses a 1-second timestamp filename and overwrites silently on collision
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Correctness
- **Affected areas:** `Services/DiagnosticReportService.cs:186-188,199,218`; `App.xaml.cs:254`
- **Summary:** The default bundle filename uses a 1-second-resolution timestamp
  (`TabDock-Diagnostics-yyyyMMdd-HHmmss.zip`). Because the final write is `File.Move(temporaryPath, path,
  overwrite: true)`, two exports issued within the same second resolve to the same `path` and the second silently
  overwrites the first.
- **Evidence:** Filename template at `App.xaml.cs:254`; `overwrite: true` at `DiagnosticReportService.cs:218`.
- **Failure scenario:** User triggers the diagnostic export twice in one second; the first ZIP is replaced by the
  second.
- **Impact:** Silent overwrite / loss of an in-progress export; low real-world harm.
- **Root cause:** Timestamp granularity (1 s) coarser than possible trigger rate; unconditional `overwrite:true`.
- **Recommended direction:** Append a sub-second component or short random/sequence suffix, and/or refuse to overwrite
  when the target already exists.
- **Verification recommendation:** Press the export hotkey twice quickly; confirm whether two distinct files exist.

### [AUDIT-062] Elevation-guard fail-closed branch lacks automated regression coverage
- **Severity:** Low *(Deep-dive update — DEEP-NATIVE-062: confirmed Low. The elevation guard is genuinely fail-closed (verified: indeterminate+non-elevated → `return null`); only a test-coverage gap remains)*
- **Confidence:** Medium
- **Category:** Tests
- **Affected areas:** `Services/WindowShepherdService.cs:440-466`; `openspec/changes/archive/2026-07-30-fix-elevation-check-failopen/tasks.md:16-18`
- **Summary:** The fail-closed elevation branch (indeterminate check + TabDock not elevated → reject) is
  security-critical, yet it is only verified end-to-end manually (archive task 3.2 reports an elevated Registry
  Editor was capture-rejected via a throwaway harness). There is no deterministic automated test that injects an
  `IWindowIdentityNativeApi` returning an indeterminate elevation result and asserts `Capture` returns null.
- **Evidence:** archive tasks.md 3.2 "verified manually"; no `CaptureElevation`-style test found for the indeterminate
  path.
- **Failure scenario:** A future refactor silently collapses the indeterminate branch back into the success path; the
  manual-only verification would not catch it in CI.
- **Impact:** Risk of reintroduction of the exact CVE-class fail-open the fix addressed.
- **Root cause:** Security-critical branch lacks automated regression coverage.
- **Recommended direction:** Add a deterministic test using an `IWindowIdentityNativeApi` stub that returns
  `checkOk=false` for elevation and asserts `Capture` refuses when TabDock is non-elevated and permits when elevated.
- **Verification recommendation:** `dotnet test` after adding the test; intentionally break the branch to confirm the
  test fails.

### [AUDIT-063] Icon resolution blocks the UI thread in view models (inconsistent with picker worker)
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Performance
- **Affected areas:** `ViewModels/GroupViewModel.cs:143` (ctor), `:288` (`AddCapturedWindow` → `_icons.GetFileIcon`); `Services/IconService.cs:61-111` (`GetFileIcon`), `:90` (blocking `waitFor!.Task.GetAwaiter().GetResult()`)
- **Summary:** The production view model resolves icons on the UI thread via `GetFileIcon`, which on a cache miss
  runs `ExtractIconEx` + `Imaging.CreateBitmapSourceFromHIcon` synchronously, and may additionally BLOCK on
  `waitFor!.Task.GetAwaiter().GetResult()` if another request for the same exe is in flight. The capture picker avoids
  this (uses `TryGetCachedFileIcon` + a worker), so the two code paths are inconsistent.
- **Evidence:** `GroupViewModel.cs:143 tvm.Icon = _icons.GetFileIcon(m.ExePath);` and `:288`; `IconService.cs:90`
  blocks when `producer == null`, `:95 ExtractFileIcon` runs on the current thread otherwise.
- **Failure scenario:** Capturing a window whose exe icon was never cached → first-time `ExtractIconEx` from disk
  executes on the UI thread, blocking input; if two same-exe captures race, the second UI-thread caller blocks.
- **Impact:** Minor capture-time UI jank; inconsistent with the picker's offloaded design.
- **Root cause:** Icon extraction synchronous on the UI thread in view models, while the only async path is the picker
  worker.
- **Recommended direction:** Route `GroupViewModel` icon resolution through the same bounded worker/`TryGetCachedFileIcon`
  pattern, assigning a placeholder and filling async.
- **Verification recommendation:** Capture two never-before-cached windows of the same exe back-to-back; measure UI-thread
  blocking via a thread-blocking profiler.

### [AUDIT-064] NativeSnapshotService process cache is unbounded (never evicted)
- **Severity:** Low
- **Confidence:** High
- **Category:** Resource Lifecycle
- **Affected areas:** `Services/NativeSnapshotService.cs:20` (`_processCache`), `:192-223` (`GetProcessDetails` adds, never evicts)
- **Summary:** `_processCache` (`Dictionary<uint, ProcessDetails>`) caches per-PID process details for the lifetime of
  the service and is never cleared or bounded. Entries for exited processes persist until the service is disposed.
- **Evidence:** `NativeSnapshotService.cs:20` field; `GetProcessDetails` (`:192-223`) always `_processCache[pid] =
  details;` with no expiry/trim.
- **Failure scenario:** A long diagnostic session that snapshots repeatedly accumulates one entry per distinct PID ever
  seen; exited processes never reaped.
- **Impact:** Monotonic memory growth in a diagnostic/validation session; low in practice but a genuine unbounded
  collection.
- **Root cause:** Cache with no eviction policy.
- **Recommended direction:** Bound the cache (LRU or clear on each top-level `Capture*` entry) or key it to a
  short-lived snapshot scope.
- **Verification recommendation:** Invoke `CaptureTabDockWindows` repeatedly across many process lifecycles; assert
  `_processCache.Count` stays bounded.

### [AUDIT-065] Privacy self-test is not hermetic (reads real user AppData/state)
- **Severity:** Low
- **Confidence:** Medium
- **Category:** Tests / Privacy
- **Affected areas:** `Services/DiagnosticPrivacySelfTest.cs:51-102,184` (`ExportBundle`)
- **Summary:** `DiagnosticPrivacySelfTest.Run()` validates real privacy behavior by invoking the **real**
  `DiagnosticReportService.ExportBundle(bundlePath)` and `PendingRecoveryService.FormatDiscovery(pendingRoot)`, which
  read the **real user AppData** (logs, state, environment) and write a real `support.zip`. Not hermetic with respect
  to host data, though cleaned up in `finally`.
- **Evidence:** `:51-87` calls `DiagnosticReportService.ExportBundle` (real service) then asserts the bundle contains
  no profile/AppData/username/secret strings; fixtures also read `Environment.GetFolderPath(...)` and `Environment.User...`.
- **Failure scenario:** On a host whose real AppData legitimately contains the username/profile path inside a
  non-redacted field, the bundle would include it and the privacy check would fail — a false-negative coupled to the
  environment.
- **Impact:** Gate determinism depends on the host environment; reduces portability and introduces real side effects
  beyond temp files.
- **Root cause:** Reuse of production export/format methods for validation rather than a fixture-injected reporter.
- **Recommended direction:** Acceptable as-is (genuinely validates the real privacy contract), but document that the
  privacy self-test is environment-sensitive and ensure CI runs it on a clean profile.
- **Verification recommendation:** Read of source; not executed (read-only).

### [AUDIT-066] Lease self-test creates real named kernel mutexes (side effects beyond temp files)
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests / Resource lifecycle
- **Affected areas:** `Services/ProductMutationLeaseSelfTest.cs:82-140` (ExclusiveAndReusable), `:221-240` (DifferentUserScopedLeasesCanCoexist)
- **Summary:** `ProductMutationLeaseSelfTest` creates real named kernel mutexes (`Local\TabDock-lease-...`, name
  includes a Guid) and `ExclusiveAndReusable` deliberately **abandons** one (thread exits without releasing) to
  exercise `AbandonedMutexException` recovery. Real side effect beyond temp files.
- **Evidence:** `:84 name = ... + "lease-selftest-" + Guid.NewGuid()`; `:117-138` spawn a thread that acquires a real
  mutex and exits without `Release`/`Dispose`; `:144-158` create `Global\TabDock-lease-denied-selftest-*`; `:180-197`
  leak a `Mutex` whose `SafeWaitHandle` is asserted closed.
- **Failure scenario:** If the self-test process is force-killed mid-run, the OS releases the mutex (abandoned or
  closed) — safe. Unique Guids prevent cross-run collisions.
- **Impact:** Minimal; noted against the audit's "side-effect-free" dimension.
- **Root cause:** Real kernel-object testing for lease semantics.
- **Recommended direction:** No change required; ensure cleanup remains (`Dispose`).
- **Verification recommendation:** Read of source.

### [AUDIT-067] No automated end-to-end hard-kill + relaunch crash recovery test
- **Severity:** Low
- **Confidence:** High
- **Category:** Tests / Verification
- **Affected areas:** `Services/RuntimeStabilizationSelfTest.cs:44-71`; `Services/RecoveryJournalSelfTest.cs:97-116`; `docs/runtime-stabilization-2026-08.md:37-58`
- **Summary:** The self-tests prove the *logic* of runtime stabilization and offline journal rescue, but do **not**
  execute a true hard-kill + relaunch rescue of a real captured window. That end-to-end scenario (doc scenarios A–G,
  especially F) requires a human at a Windows desktop; the `ValidationDriver` is opt-in real-input and not a
  deterministic gate.
- **Evidence:** `RuntimeStabilizationSelfTest.JournaledCapture_OrdinaryHidesAreZeroCommit` (`:44-71`) asserts the
  rescue entry stays on disk after 100 hides but never relaunches; `RecoveryJournalSelfTest.ValidV3Rescues` (`:97-116`)
  drives `RescueOrphanedWindows` over faked entries; `docs/runtime-stabilization-2026-08.md:56-58` states A–G "are NOT
  covered by the deterministic gates."
- **Failure scenario:** A regression in `App.xaml.cs:147` `RescueOrphanedWindows` invocation, or in the relaunch
  wiring, would not be caught by any deterministic self-test; only surfaces in the supervised human ValidationDriver
  run.
- **Impact:** A class of end-to-end crash-coherence regressions is uncovered automatically.
- **Root cause:** Fundamental limitation — deterministic self-tests cannot simulate a process death + fresh-process
  rescue of real HWNDs without a live desktop.
- **Recommended direction:** Where feasible, expand offline coverage of the rescue-on-startup path (e.g., a self-test
  that writes a v3 journal to a temp dir and calls the real startup rescue entrypoint against faked native APIs).
- **Verification recommendation:** Read of source + doc; not executed.

### [AUDIT-001] Unsynchronized captured-index accessed from WinEvent callback thread; session-ending teardown widens the window
- **Severity:** Low *(re-rated from High — DEEP-CONC-001; the off-UI race premise was **refuted**)*
- **Confidence:** High
- **Category:** Concurrency (latent fragility, not active defect)
- **Affected areas:** `Services/WinEventMonitor.cs:102-138` (`Start` installs `WINEVENT_OUTOFCONTEXT` + captures `SynchronizationContext.Current` from the UI thread), `:226-322` (`OnWinEvent`/`Post`); `Services/GroupManager.cs:40,188-227,231-329`; `App.xaml.cs:134,554-555,568`
- **Summary (refuted):** The first pass claimed `OnWinEvent` runs on a "background WinEvent callback thread" that
  races `GroupManager._capturedIndex` against UI-thread mutation, causing process-terminating corruption. **This
  premise is refuted.** `WinEventMonitor.Start` installs its hooks with `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`
  while `SynchronizationContext.Current` is the UI thread's (captured at `WinEventMonitor.cs:102-138`). Per the Win32
  contract, *out-of-context* WinEvent callbacks are delivered on the **installing (UI) thread**, so `OnWinEvent` and
  every `_capturedIndex` reader/writer execute on the **same UI thread**. There is no cross-thread race, no torn read,
  and no process-terminating corruption. The project's own prior internal finding (`investigation_findings.md` item I4)
  already reached this same conclusion.
- **Evidence:** `Start()` requires a non-null `SynchronizationContext.Current` (`:107`) and is only ever called from
  `SyncWinEventMonitor` on the UI thread (`App.xaml.cs:568`; comment `:554-555`). Microsoft's `SetWinEventHook`
  documentation states out-of-context events are delivered on the thread that called `SetWinEventHook`. The code's own
  comment at `WinEventMonitor.cs:311-315` says the `Post` hop exists for *ordering*, not cross-thread safety. The
  project's prior `investigation_findings.md` I4 classified the `_capturedIndex` concern as Info/unenforced-affinity.
- **Residual (only real issue):** Unenforced thread-affinity fragility. `_capturedIndex`'s single-threaded safety
  depends entirely on the `WINEVENT_OUTOFCONTEXT` + UI-thread-install convention and is **not** enforced (no
  `Dispatcher.CheckAccess()` guard, no lock). A future change that installed the hooks off-UI, or moved the filter
  `Post` onto a pool thread, would introduce exactly the race this finding originally described.
- **Recommended direction (do not implement):** Keep the dictionary as-is. Add a cheap guard —
  `Debug.Assert(Dispatcher.CurrentDispatcher.CheckAccess())` or a thrown `InvalidOperationException` — at the top of
  `OnWinEvent`/`IsCapturedWindow`/`GetCapturedWindow`/`TryGetCapturedMember` to make the affinity contract explicit
  and fail-fast if a future change breaks it. Do **not** convert to `ConcurrentDictionary` for the stated "b/g thread
  vs UI thread" reason — that reasoning is void.
- **Verification recommendation:** A single static assertion/unit test that `OnWinEvent` is invoked on the installing
  (UI) thread, plus the `CheckAccess` guard above, closes the latent gap.

### [AUDIT-005] Coordinator stale-generation guard is inert in production (InvalidateLayout never called)
- **Severity:** Low *(re-rated from Medium — DEEP-SPLIT-005)*
- **Confidence:** High
- **Category:** Reliability
- **Affected areas:** `Services/PresentationLayoutCoordinator.cs:33,56-91` (`RequestRelayout`, `InvalidateLayout`); `Views/ContainerWindow.xaml.cs:150,753,308` (`_layoutCoordinator`, `RequestRelayout`)
- **Summary:** `RequestRelayout` captures `_layoutGeneration` at schedule time and the Render-priority callback only
  executes when `scheduledLayoutGen == _layoutGeneration`. `_layoutGeneration` is only ever bumped by `InvalidateLayout()`
  (`:33`), whose **sole caller in the whole repo is a unit test** (`tests/UnitTests/HardeningRegressionTests.cs:31,46`).
  Production never calls it, so the stale-check is always false and the documented "stale Render callback suppression"
  never triggers in the shipping app.
- **Evidence:** Grep for `InvalidateLayout`/`IsCurrentLayout` shows production never references them — only unit tests
  do. Correctness is **not** masked by luck: the downstream guards in `LayoutSplitPanes`/`LayoutShepherdActiveWindow`
  (`IsSplitPresented`, `_containerHwnd == IntPtr.Zero`, `_shepherdActiveWindow == null`, `_guestMoveSizeActive`,
  `WindowState == Minimized`, `IsContainerChromeInteractionActive()`) fully neutralize the stale-frame case.
- **Impact:** Defense-in-depth defeated, but **neutralized in production** — no current crash or misposition.
- **Root cause:** The coordinator's generation token was never wired into the production transitions.
- **Recommended direction:** Either call `_layoutCoordinator.InvalidateLayout()` from the production split/relayout
  transitions, or delete the dead stale-generation machinery (it is vestigial — see the AUDIT-004/005/029/031/033/034
  systemic note in §18). Keep downstream guards regardless.
- **Verification recommendation:** A test that bumps the coordinator generation after scheduling and asserts the
  queued callback is discarded; plus a production-path test confirming `InvalidateLayout` is invoked on the relevant
  transitions.

### [AUDIT-070] Session-ending `ClearCapturedMembersAfterSessionEnding` is defeated by call order
- **Severity:** Low *(new — DEEP-CONC-004; logic/ordering, not concurrency; largely moot)*
- **Confidence:** Medium
- **Category:** Correctness (session-ending coherence)
- **Affected areas:** `App.xaml.cs:457-491` (`Application_SessionEnding`); `GroupManager.cs:400-435` (`ReleaseTab` removes member), `:526-551` (`ClearCapturedMembersAfterSessionEnding`)
- **Summary:** Within `Application_SessionEnding`, `EmergencyReleaseAll()` (`App.xaml.cs:459`) calls `ReleaseTab` for
  every member, and `ReleaseTab` calls `group.Members.RemoveAt(index)` (`GroupManager.cs:418`) **before**
  `ClearCapturedMembersAfterSessionEnding()` is invoked (`App.xaml.cs:491`). The latter iterates `Groups` and does
  `if (group.Members.Count == 0) continue;` (`GroupManager.cs:530-531`). After `EmergencyReleaseAll`, every group has
  zero members, so the method's metadata-preservation loop (`foreach (CapturedWindow member in group.Members) { …
  PersistedTabs.Add(…) }`) **never executes** — the documented layout-intent preservation is a no-op in the normal
  session-ending path.
- **Evidence:** `ReleaseTab` mutates `Members` at `GroupManager.cs:418`; `ClearCapturedMembersAfterSessionEnding`
  skips empty groups at `:530-531`; the metadata copy loop is at `:533-549`.
- **Failure scenario:** Only matters if the one-way teardown policy were ever relaxed to *survive* a cancelled logoff.
  In that world, released groups would lose their persisted layout intent because the preservation step is skipped.
- **Impact:** None today — the policy is strictly one-way (`Shutdown(0)` at `App.xaml.cs:504`), so TabDock never
  survives a session-ending to need the metadata. Latent defect if that policy is relaxed.
- **Root cause:** The session-ending ordering removes members before the preservation step runs.
- **Recommended direction:** If the one-way policy is ever relaxed, reorder so metadata is copied **from `Members`
  before `EmergencyReleaseAll` removes them**, or have `ClearCapturedMembersAfterSessionEnding` itself perform the
  release. Not needed under the current policy.
- **Verification recommendation:** Unit test: populate a group with captured members, run the session-ending-equivalent
  steps, assert `PersistedTabs` reflects the members *if* the app were to persist-and-continue.

### [AUDIT-071] UIPI-elevated guest can remain permanently hidden after rescue retry exhausts
- **Severity:** Low *(new — DEEP-PERSIST-004; Reliability)*
- **Confidence:** Medium
- **Category:** Reliability
- **Affected areas:** `Services/WindowShepherdService.cs:2585-2668` (`RestoreJournaledPresentation`), `:2477-2533` (retry loop)
- **Summary:** If a hidden guest becomes UIPI-elevated between capture and rescue, `SetWindowPlacement`/`SetWindowPos`
  fail (`placementOk == false` at `:2630`) or post-state visibility mismatches (`:2647`), returning `RecoveryPending`.
  The entry is re-written to the retry journal (`:2532`) and retried every launch, but the elevation condition is
  permanent for that guest, so it stays hidden indefinitely.
- **Evidence:** `:2630-2634` returns `RecoveryPending` on native restore failure; no elevation-specific recovery; the
  retry loop has no terminal discard for permanently-unreachable guests.
- **Impact:** Bounded — requires a guest to elevate itself *and* be hidden at crash. The same UIPI condition would
  block even normal positioning, so the guest is genuinely unreachable by TabDock. **Fail-closed** (never resurrects
  a wrong window) but a real "window lost while hidden" edge.
- **Root cause:** No terminal discard for a guest that is permanently unreachable due to elevation.
- **Recommended direction:** Optionally cap retry attempts or surface a diagnostic that a hidden guest could not be
  restored due to elevation; not a correctness fix.
- **Verification recommendation:** A retry-loop test with a guest that reports `SetWindowPlacement` failure every
  attempt; assert a bounded number of retries and a surfaced diagnostic.

## 9. Optimization Opportunities

*(Opportunities are non-defect observations; see also §10/§18. Severity here is Info/effort-light unless noted.)*

- **O1 — Cache per-monitor effective DPI** (relates AUDIT-007): create/destroy a hidden helper window only once per
  monitor per session; invalidate on `WM_DPICHANGED`. Eliminates recurring UI-thread window churn.
- **O2 — Off-thread min-track probing** (relates AUDIT-008): run `WM_GETMINMAXINFO` via `SendMessageTimeout` on a
  worker (or accept cached value opportunistically) to remove up-to-200 ms serial UI stalls.
- **O3 — Off-thread/guarded capture-picker enumeration** (relates AUDIT-006): move `EnumWindows` + per-window native
  probes to a background thread with a marshalled row swap, and wrap in try/catch.
- **O4 — Scope command requery** (relates AUDIT-009): replace per-row global `InvalidateRequerySuggested` with a
  single `SelectedCount` integer and debounced requery to remove O(n²) fan-out.
- **O5 — Off-thread diagnostic ZIP export** (relates AUDIT-042): `Task.Run` the `ExportBundle` in
  `ExportDiagnosticsFromHotkey`.
- **O6 — De-duplicate SHA-256** (relates AUDIT-025): compute `Sha256(rawBytes)` once per pending file.
- **O7 — Bound/evict the snapshot process cache** (relates AUDIT-064) and clear failure-suppression sets on release
  (relates AUDIT-021).
- **O8 — Async icon resolution in view models** (relates AUDIT-063): reuse the picker's bounded worker/`TryGetCachedFileIcon`.
- **O9 — Reuse diagnostic payloads** (relates Info AUDIT-078): pooled/struct diagnostic dictionaries + relax the global
  trace lock if ordering is not required.

## 10. Architectural Improvement Opportunities

- **A1 — Wire the relayout generation token into production transitions** (relates AUDIT-005): either call
  `_layoutCoordinator.InvalidateLayout()` from DefinePair/Suspend/Resume/Exit, or compare a coarser token production
  actually advances. Restores the stale-callback defense-in-depth.
- **A2 — Single coordinator source of truth** (relates AUDIT-029/034): drop the dead `LayoutCoordinator` property and
  the dead budget-sink plumbing; route all relayout + budget counting through the one instance actually used.
- **A3 — Consolidate split settle ownership** (relates AUDIT-030): remove the controller's dead settle fields and
  `ExplicitExit`, or formally move settle state into the controller and delete the container's hand-built copy.
- **A4 — Make fail-closed contracts explicit in callers** (relates AUDIT-004): `EnterSplit` must honor `DefinePair`'s
  return/authoritative state instead of treating it as `void`.
- **A5 — Structured privacy instead of string matching** (relates AUDIT-010/046): drive log redaction from a
  per-line `PrivacyClass`/`LogScope` or hash all external free text; codify a `diagnostic-privacy` OpenSpec (AUDIT-076).
- **A6 — Thread-affinity contract for the VM layer** (relates AUDIT-038): document (or enforce via marshalling) that
  `ViewModelBase`/`ObservableCollection` mutations are UI-thread-only.
- **A7 — Persistence read-path as a first-class unit suite** (relates AUDIT-011): `InternalsVisibleTo` the unit
  project and cover `Load` failure branches; the app self-test is necessary but not sufficient for CI.
- **A8 — Deterministic headless recovery integration tests** (relates AUDIT-013/067): drive restore/rescue APIs against
  injected FS/fake journals so recovery paths have a CI gate.
- **A9 — Runtime self-check posture decision** (relates AUDIT-003): decide whether a fast post-startup self-check feeds
  user/telemetry, or document self-tests as CI-only.

## 11. Test and Quality-Gate Gaps

- The **most safety-critical persistence read-path** (`PersistenceService.Load`) has **no CI-runnable unit coverage**
  (AUDIT-011).
- **No deterministic integration test** for persist-kill / crash-rescue / split-survivor restart (AUDIT-013).
- **Self-tests never run at runtime** — they are an offline/CI gate only (AUDIT-003).
- **Vacuous/absence assertions** in the ValidationDriver give false green (AUDIT-012); **pixel flakiness** erodes trust
  (AUDIT-014).
- **Single-try PersistenceSelfTest** masks cascading regressions (AUDIT-018); **single-bool self-tests** hide which
  sub-assertion failed (AUDIT-020).
- **Privacy self-test** is both **vacuous** for pending-recovery (AUDIT-044) and **non-hermetic** (AUDIT-065).
- **Tautological coalesce tests** (AUDIT-047), **racy SaveAsync assert** (AUDIT-048), **redundant split-policy Facts**
  (AUDIT-050), **unverified Firefox** (AUDIT-051) — all reduce signal-to-noise.
- **Elevation fail-closed branch** lacks automated coverage (AUDIT-062).
- **Zero-warnings build** observed in this run contradicts the converter `CS8625` warnings cited by the scope
  (AUDIT-049) — likely config-dependent; reconcile before acting.

## 12. Security and Privacy Assessment

**Strong / verified-correct (no action):**
- Elevation guard is tri-state and **fail-closed** — indeterminate + self-not-elevated blocks; no fail-open
  reintroduced (SCOPE-SEC verified).
- P/Invoke string/buffer marshaling is correctly sized (StringBuilder capacities, `AllocHGlobal(Marshal.SizeOf<…>())`,
  growing `GetProcessImagePath`, exact-size `TOKEN_ELEVATION` read); no `unsafe` blocks; 44-byte `WINDOWPLACEMENT`
  locked by a self-test.
- WinEvent callback is a rooted field-delegate with dispatch-time `_running` + membership re-checks — robust against
  HWND recycling and GC of the native callback.
- Support-bundle export applies layered sanitization and **hashes titles**; backed by a real ZIP-inspecting
  self-test for doctor/bundle paths.
- Release signing enforces non-exportable-key production policy with independent signtool/RFC3161/EKU/publisher-subject
  verification and fail-closed hash triple-consistency; no real secrets committed.
- `ProductMutationLease` uses a SID-derived `Global\` name with a protected current-user-only DACL, never falls back
  to a broad-DACL constructor, and validates the existing object's security.

**Gaps / residual risk:**
- **Title privacy relies on a static allow/deny list**, not structured redaction — a future title-bearing log line
  under a kept tag bypasses the contract (AUDIT-010).
- **`--version` was wrongly claimed to leak the raw executable path** — **refuted** by the deep dive (AUDIT-043,
  now **Info / false positive**). `FormatVersion` returns `SanitizeText(...)`, which redacts the username and
  profile path (`%LOCALAPPDATA%`/`%USERPROFILE%`/`<path>`), so no PII disclosure occurs. The first pass stopped
  reading at the raw emit and missed the final redaction.
- **Sanitizer coverage edges**: secret keywords (`pwd=`/`pass=`/URL credentials) and WSL/`\Device\` paths not covered
  (AUDIT-046 / Info AUDIT-079).
- **Privacy self-test** is vacuous for the pending-recovery listing (AUDIT-044).
- **Local-PFX signing** exposes the PFX password on the command line + creates a temp PFX with default ACL
  (AUDIT-058/059) — bounded to dev/RC, but a supply-chain hygiene gap.
- **Export bundle** has no destination allow-list and silently overwrites on same-second collision (AUDIT-060/061).
- **No `diagnostic-privacy` OpenSpec** exists despite being referenced (AUDIT-076) — the privacy contract is
  under-documented as a durable requirement.

**Verdict:** No exploitable vulnerability in the shipping security-critical paths. Residual privacy/secret-handling
risks are low-severity and mostly foregrounded by future code changes rather than present leaks. The dominant security
theme is *defense-in-depth hardening* (structured redaction, broader secret regex, command-line/ACL hygiene for the
signing path), not remediation of a live flaw.

## 13. Reliability and Failure-Recovery Assessment

- **Crash-journal and persistence are genuinely fail-closed and atomic** — the strongest part of the system. Corrupt/
  unsupported/unreadable state is quarantined/refused; a `.bak` is preserved; saves are latest-generation only.
- **Startup rescue** (`RescueOrphanedWindows`) runs before instance mutation and is identity-gated; robust to HWND
  recycling and token mismatch.
- **Primary native hazard is *process-scoped vs window-scoped identity*, not index concurrency** (AUDIT-068, High;
  AUDIT-026, Medium). The previously-flagged `_capturedIndex` cross-thread race (AUDIT-001) was **refuted** by the
  deep dive — `WINEVENT_OUTOFCONTEXT` delivers the WinEvent callback on the installing (UI) thread, so the index is
  UI-thread-only; only an *unenforced thread-affinity fragility* remains (Low). The real native risk is that a
  same-process HWND recycle can defeat both the capture gate and the post-release `WM_CLOSE` verifier.
- **Session-ending ordering** contradicts the documented "stop monitor before clearing index" contract; the call
  order also defeats `ClearCapturedMembersAfterSessionEnding` (AUDIT-070, Low — moot under the current one-way
  `Shutdown(0)` policy but latent if that policy is ever relaxed).
- **Self-tests, though excellent as offline gates, never run at runtime**, so runtime resilience rests entirely on the
  production fail-closed paths (AUDIT-003). The stabilization campaign's promised telemetry is permanently disabled
  (AUDIT-019), so production regressions in transition latency/operation counts are invisible.
- **Recovery paths lack an automated gate** (AUDIT-013/067); a regression in `RescueOrphanedWindows` wiring would only
  surface in the supervised human ValidationDriver run.
- **Minor robustness gaps**: fail-open min-track on probe timeout (AUDIT-027), hide post-state race (AUDIT-028),
  intentional-hide marker overwrite (AUDIT-023), unbounded failure-suppression sets (AUDIT-021).

## 14. Performance and Scalability Assessment

- **Hot paths are well-engineered**: O(1) WinEvent filter index, hooks installed only when needed, per-frame relayout
  coalescing, redundant-glue guards, dirty-gated + cached min-track probe, journal I/O avoided on ordinary tab hide,
  no-op telemetry in production, icon de-duplication, batched split positioning.
- **Real UI-thread bottlenecks** (all Medium): DPI-probe window churn (AUDIT-007), synchronous `WM_GETMINMAXINFO`
  blocking up to 100 ms/guest (AUDIT-008), picker `EnumWindows` on UI thread (AUDIT-006) and O(n²) requery storm
  (AUDIT-009), synchronous diagnostic ZIP export (AUDIT-042), UI-thread icon extraction (AUDIT-063).
- **Resource lifecycle**: unbounded `_processCache` (AUDIT-064) and unbounded failure-suppression HashSets
  (AUDIT-021) grow monotonically over long sessions; both are low-rate and bounded in practice.
- **Scalability**: designed for a handful-to-hundreds of captured windows per desktop; the O(1) index and coalescing
  hold at scale. The picker enumeration cost scales with the *desktop* window count (not the captured count), which is
  the main scaling concern on busy desktops.

## 15. Maintainability / Technical Debt Assessment

- **High documentation debt**: `ARCHITECTURE.md` line citations are stale by 1,300–2,300 lines (AUDIT-016); split log
  vocabulary diverges from code (AUDIT-017); release doc misstates the publishing workflow (AUDIT-052); `.agent/STATE.md`
  counts contradict (AUDIT-057).
- **Dead/inert code around the split coordinator**: duplicate coordinator instance (AUDIT-029), dead budget-sink
  plumbing (AUDIT-034), dead controller settle state + `ExplicitExit` (AUDIT-030), dead classifier block (AUDIT-031),
  inert stale-generation guard (AUDIT-005). This is the single biggest maintainability cluster and a systemic risk
  (see §18).
- **Dead converter resource** (Info AUDIT-075); misleading `sign-release.ps1` status field (AUDIT-055); duplicate
  PowerShell `.DESCRIPTION` (AUDIT-054); version-pin comment drift (AUDIT-053); solution/platform matrix mismatch
  (AUDIT-056).
- **Test maintainability**: redundant Facts (AUDIT-050), tautological coalesce tests (AUDIT-047), racy assert
  (AUDIT-048) — high count, lower marginal signal.
- **Net**: the code is clean and well-commented in the critical paths, but the *split-presentation coordination* area
  has accumulated drift between what exists in code and what is actually wired, which will mislead the next engineer.

## 16. Documentation / Implementation Divergence

- **`docs/ARCHITECTURE.md`**: verification claim (`:11`) is false; `ContainerWindow.xaml.cs` citations off by
  1,300–2,300 lines (AUDIT-016); split log vocabulary wrong (AUDIT-017).
- **`docs/release/repository-protection.md:18`**: misattributes publishing to `release.yml` instead of
  `publish-release.yml` (AUDIT-052).
- **OpenSpec drift**: `diagnostic-privacy` spec referenced by the remediation proposal does not exist (AUDIT-076); the
  `final-production-readiness-closure` richer "sidecar ledger / NativeRecoveryComplete" journal model (noted by
  SCOPE-NATIVE) is not implemented — confirm whether future work or an unimplemented requirement.
- **No-reparent invariant**: faithfully preserved and correctly documented (positive).
- **Cross-file domain constants**: single-sourced and consistent (positive) — the brief's prime concern about
  duplicated rules with conflicting values was **not** substantiated.

## 17. Dead / Stale / Suspicious Code

- **Dead `LayoutCoordinator` property** (`ContainerWindow.xaml.cs:308`, distinct from `_layoutCoordinator`) — AUDIT-029.
- **Dead controller settle fields + `ExplicitExit`** (`SplitPresentationController.cs`) — AUDIT-030.
- **Dead step-7 block in `SplitInteractionPolicy.Classify`** — AUDIT-031.
- **Dead budget-sink plumbing in `PresentationLayoutCoordinator`** (no sink injected) — AUDIT-034.
- **Inert `InvalidateLayout`/`_layoutGeneration`**: only called by unit tests, never production — AUDIT-005.
- **Dead `BoolToVisibilityConverter` resource** (`App.xaml:11`, no live binding) — Info AUDIT-075.
- **Dead `digicert-stm` RC option** (`release.yml`) that the RC job cannot perform — AUDIT-015.
- **Dead env-var `$env:TABDOCK_SIGN_PASSWORD`** set but never read by signtool — AUDIT-058.
- **Dead branch in `QualificationResultWriter.cs:142`** predicated on non-existent `SPLIT[replace]` — AUDIT-017.
- **Misleading `status='SIGNED'` on failed sig verification** — AUDIT-055 (not dead, but wrong-label).
- **Misleading empty-list → `BeginFailed`** in `DeferredWindowPositionBatch` — AUDIT-040.

## 18. Cross-Cutting/Systemic Issues

1. **Split-presentation coordination drift (the dominant *test/prod-divergence* systemic risk).** At least seven
   findings (AUDIT-004, -005, -029, -030, -031, -033, -034) cluster around the `PresentationLayoutCoordinator` /
   `SplitPresentationController` / `ContainerWindow` split machinery: generation guard inert, duplicate coordinator
   instance, dead budget plumbing, dead settle state, dead classifier block, an under-constraining cross-monitor DPI
   min-track, and a caller that ignores the controller's fail-closed return. The features exist in code but are
   **not wired into the shipping transitions**, so their safety/perf guarantees are illusory today and fragile against
   future edits — and unit tests exercise them in isolation while production never drives them (see the
   "Deep-dive systemic root causes" subsection below). *Recommended systemic fix:* treat the
   relayout/coordinator/budget/settle as a single owned subsystem; either connect it end-to-end or delete the unused
   parts, and add a CI lint that fails if a "safety" feature's activation path is never referenced in production.
2. **Process-scoped vs window-scoped identity is the real native hazard (root cause of AUDIT-068 + AUDIT-026).**
   Identity is keyed on PID/thread/class/exe/process-start, not the individual window instance; the per-capture
   token is the only instance proof and is applied late (capture) or deliberately removed (release). This is the
   single most important *new* systemic risk from the deep dive: it can drive a **destructive `WM_CLOSE` to a
   recycled same-process window** (AUDIT-068, **High**) and also weakens capture disambiguation (AUDIT-026, Medium).
   The previously-flagged `_capturedIndex` cross-thread race (AUDIT-001) was **refuted** by the deep dive —
   `WINEVENT_OUTOFCONTEXT` delivers the WinEvent callback on the installing (UI) thread, so the index is
   UI-thread-only; only an *unenforced thread-affinity fragility* remains (Low). The native mutation surface outside
   identity-release remains sound because every other sink re-verifies the per-window capture token.
3. **Validation gates are strong but offline-only.** The self-test harness is excellent but never runs at runtime
   (AUDIT-003); the persistence read-path (AUDIT-011) and recovery flows (AUDIT-013/067) have no automated CI gate.
   The project's safety posture is real but **unverified by the fastest layer** and depends on humans running the
   supervised ValidationDriver.
4. **Privacy is string-matched, not structured.** Title redaction (AUDIT-010), secret regex (AUDIT-046), and the
   missing `diagnostic-privacy` spec (AUDIT-076) all stem from treating redaction as grep-style allow/deny lists
   rather than a structural contract. Future code changes are the main exposure.
5. **Documentation has materially drifted from code** in the architecture map (AUDIT-016), split vocabulary
   (AUDIT-017), and release workflow description (AUDIT-052) — enough that an engineer trusting the docs will land in
   the wrong method.
6. **Release CI is non-functional on first real use** (AUDIT-002) and offers an impossible signing option (AUDIT-015) —
   the release pipeline needs validation before a general release.

### Deep-dive systemic root causes

The second-pass deep dive (six scoped subagents; see §22) isolated two root causes that explain most of the
correctable defects and the test/prod divergence:

1. **Identity is process-scoped, not window-scoped** (AUDIT-026, AUDIT-068). The per-capture token is the only
   window-instance proof, but it is installed *after* the identity gate during capture (AUDIT-026) and deliberately
   removed on release — so `VerifyReleasedWindowCloseTarget` even *requires* its absence (AUDIT-068). Consequently a
   same-process HWND recycle defeats both the capture gate and the post-release `WM_CLOSE` verifier, while every
   other (token-gated) Shepherd sink remains immune. This is the single most important *new* systemic risk because
   it can drive a **destructive `WM_CLOSE` to the wrong window** (AUDIT-068).
2. **The split-presentation subsystem accreted a dead parallel state/observation layer** (AUDIT-004/005/029/031/033/034)
   that unit tests exercise in isolation but production never drives. The authoritative runtime path lives in
   `ContainerWindow.SplitInteractionFix.cs` and reads `_splitController.Generation` directly; the coordinator's
   generation token, the controller's settle/`ExplicitExit`/`IsCurrentSettle` fields, the classifier step-7, and the
   entire presentation-budget seam are never referenced in shipping code. This is the dominant source of *test/prod
   divergence*: a unit test that injects a sink and calls `InvalidateLayout` passes while the shipping app ignores the
   whole layer — exactly the class of defect that produced AUDIT-004's interaction freeze and AUDIT-005's inert guard.

## 19. Improvement Backlog

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
|---|---|---|---|---|---|---|
| P3 | AUDIT-001 | Low | `_capturedIndex` race **refuted** (UI-thread via `WINEVENT_OUTOFCONTEXT`); only unenforced thread-affinity fragility remains | Latent if hooks ever installed off-UI | S | High |
| P0 | AUDIT-002 | High | Release CI missing `actions: write` → artifact upload 403 | Release/RC pipelines cannot retain artifacts | S | High |
| P0 | AUDIT-003 | High | Self-tests never executed at runtime | No runtime stabilization resilience; offline-only verification | M | High |
| P0 | AUDIT-068 | High | Close-group `WM_CLOSE` can hit a recycled same-process window (process-scoped identity) | Destructive close of an unrelated window | M | Medium |
| P1 | AUDIT-004 | Medium | EnterSplit ignores DefinePair fail-closed early-return | User-visible split state/display desync | M | High |
| P2 | AUDIT-005 | Low | Coordinator stale-generation guard inert (InvalidateLayout never called) — neutralized by downstream guards in production | Defense-in-depth defeated, no current crash | S | High |
| P1 | AUDIT-006 | Medium | Picker EnumWindows on UI thread, no try/catch | UI freeze / picker fails to open on one bad window | M | High |
| P1 | AUDIT-007 | Medium | DPI probe creates/destroys helper window every call | UI-thread window churn on constraint transitions | S | High |
| P1 | AUDIT-008 | Medium | Synchronous WM_GETMINMAXINFO blocks UI up to 100 ms/guest | Input jank on constraint transitions | M | High |
| P1 | AUDIT-009 | Medium | Picker O(n²) global requery storm | Populate hitch scaling with desktop windows | M | High |
| P1 | AUDIT-010 | Medium | Title privacy via static allow/deny list | Future window-title leak into support bundle | M | High |
| P1 | AUDIT-011 | Medium | Persistence `Load` failure-path has no **xUnit** coverage (exercised in CI via `--selftest-diagnostics`) | Safety-critical read regression invisible to unit layer | M | High |
| P1 | AUDIT-013 | Medium | No deterministic recovery integration test | Critical recovery paths ungated in CI | L | High |
| P1 | AUDIT-015 | Medium | RC offers digicert-stm it cannot perform | Confusing/guaranteed-fail RC dispatch | S | High |
| P1 | AUDIT-016 | Medium | ARCHITECTURE.md citations stale by 1.3–2.3k lines | Docs mislead engineers to wrong methods | S | High |
| P1 | AUDIT-017 | Medium | Split log vocabulary diverges from code | Misleading docs + dead test branch | S | High |
| P1 | AUDIT-018 | Medium | PersistenceSelfTest single-try masks cascading regressions | Gate undercounts failures | S | High |
| P1 | AUDIT-019 | Medium | RuntimeTelemetry permanently disabled | No production observability for stabilization | S | High |
| P1 | AUDIT-020 | Medium | Single-bool self-tests hide failing sub-assertion | Weak gate diagnosability | S | High |
| P2 | AUDIT-012 | Medium | Vacuous "no EXCEPTION" assertions | False green in ValidationDriver | S | Medium |
| P2 | AUDIT-014 | Medium | Pixel health checks flaky for GPU windows | Intermittent CI/QA reds | M | Medium |
| P2 | AUDIT-021 | Low | Unbounded failure-suppression sets keyed by recycled HWND | Memory growth + suppressed first-failure log | S | High |
| P2 | AUDIT-022 | Low | v1/v2 journal overwrites OriginallyVisible | Latent only (v2 never auto-rescued) | S | Medium |
| P1 | AUDIT-023 | Medium | Intentional-hide marker overwritten by later Hide → deliberate-hide window resurrected on relaunch | Privacy/intent breach on next launch | M | High |
| P2 | AUDIT-024 | Low | Split presentation not persisted | Deliberate; doc the limitation | S | High |
| P2 | AUDIT-025 | Low | Duplicate SHA-256 per pending file | Minor CPU | S | High |
| P1 | AUDIT-026 | Medium | Capture recycle weakness (same-process multi-window; token installed after gate) | Wrong-window capture; same root cause as AUDIT-068 | M | Medium |
| P2 | AUDIT-027 | Low | Min-track fails open to zero on probe timeout | Cosmetic layout mismatch | S | High |
| P2 | AUDIT-028 | Low | Hide post-state race (guest re-show) | Benign RecoveryPending | S | Medium |
| P2 | AUDIT-029 | Low | Two coordinator instances (property vs field) | Diagnostic seam trap | S | High |
| P2 | AUDIT-030 | Low | Dead controller settle state / ExplicitExit | Divergent dup state | S | High |
| P2 | AUDIT-031 | Low | Dead classifier step-7 block | Misleading prose | S | High |
| P2 | AUDIT-032 | Low | PairZOrderBehindGuest raw HWND equality | Edge-case spurious focus | S | Medium |
| P1 | AUDIT-033 | Medium | Cross-monitor DPI min-track uses guest's monitor, not pane's → under-constrain when guest on lower-DPI monitor | Docked layout breaks for mixed-DPI guests | S | Medium |
| P2 | AUDIT-034 | Low | Coordinator budget sink never wired | Two parallel budget paths | S | High |
| P2 | AUDIT-035 | Low | DeleteGroupRequested never unsubscribed | Latent leak/stray dialog | S | Medium |
| P2 | AUDIT-036 | Low | Rename blank desyncs TextBox from model | Minor UX desync | S | Medium |
| P2 | AUDIT-037 | Low | ActiveTab SelectedItem flicker in split | Cosmetic flicker | S | Low |
| P2 | AUDIT-038 | Low | ViewModelBase no thread guard | Latent cross-thread fragility | S | Medium |
| P3 | AUDIT-039 | Info | Non-volatile `_uiContext` — **not** a cross-thread read (Post/Stop both UI-thread) | Code smell only | S | Medium |
| P2 | AUDIT-040 | Low | Empty deferred batch returns BeginFailed | Misleading API contract | S | High |
| P2 | AUDIT-041 | Low | Deferred BeginInvoke after monitor disposal | Fragile (depends on Stop early-return) | S | Medium |
| P2 | AUDIT-042 | Low | Diagnostic export sync on UI thread | Momentary UI freeze | S | Medium |
| P3 | AUDIT-043 | Info | `--version` path leak — **false positive** (output is `SanitizeText`'d) | No leak | S | High |
| P2 | AUDIT-044 | Low | Privacy self-test pending-recovery vacuous | False privacy confidence | S | High |
| P2 | AUDIT-045 | Low | No correlation ID log↔trace | Reduced bundle diagnosability | M | Medium |
| P2 | AUDIT-046 | Low | Secret-redaction regex gaps | Future secret leak | S | Medium |
| P2 | AUDIT-047 | Low | Tautological coalesce tests | False confidence in invariant | S | High |
| P2 | AUDIT-048 | Low | SaveAsync racy assert | Intermittent CI red | S | Medium |
| P2 | AUDIT-049 | Low | Converter nullability mismatch | Zero-warning violation (verify config) | S | High |
| P2 | AUDIT-050 | Low | Redundant split-policy Facts | Inflated coverage signal | S | High |
| P2 | AUDIT-051 | Low | Firefox scenarios unverified | Coverage gap (honestly SKIP'd) | S | High |
| P2 | AUDIT-052 | Low | Release doc misstates publishing workflow | Reader confusion | S | High |
| P2 | AUDIT-053 | Low | AccessControl 10.0.11 misleading comment | Maintainer confusion | S | Medium |
| P2 | AUDIT-054 | Low | Duplicate .DESCRIPTION in ps1 | Minor doc confusion | S | High |
| P2 | AUDIT-055 | Low | sign-release emits SIGNED on failure | Latent mislabel | S | Medium |
| P2 | AUDIT-056 | Low | Solution platform matrix mismatch | Confusing config | S | High |
| P2 | AUDIT-057 | Low | STATE.md count inconsistencies | False precision | S | High |
| P2 | AUDIT-058 | Low | PFX password on command line (`$env:TABDOCK_SIGN_PASSWORD` is consumed, not dead) | Dev/RC key disclosure | S | High |
| P3 | AUDIT-059 | Low | Temp PFX default ACL (per-user %TEMP%, deleted in `finally`) | Dev/RC key disclosure; multi-user/TEMP required | S | Medium |
| P2 | AUDIT-060 | Low | Export bundle no destination validation | Self-inflicted overwrite | S | High |
| P2 | AUDIT-061 | Low | Export 1s-timestamp filename overwrite | Silent export loss | S | Medium |
| P2 | AUDIT-062 | Low | Elevation fail-closed branch untested | Reintroduction risk | S | Medium |
| P2 | AUDIT-063 | Low | UI-thread icon extraction in VM | Capture-time jank | M | Medium |
| P2 | AUDIT-064 | Low | Unbounded snapshot process cache | Monotonic memory growth | S | High |
| P2 | AUDIT-065 | Low | Privacy self-test non-hermetic | Environment-coupled gate | S | Medium |
| P2 | AUDIT-066 | Low | Lease self-test real mutex side effects | Minor (safe, Guid-scoped) | S | High |
| P2 | AUDIT-067 | Low | No e2e hard-kill+relaunch test | Recovery regression ungated | L | High |
| P2 | AUDIT-069 | Medium | Unit test suite is flaky (`SaveAsync_RapidCoalesce` 15 s SpinWait vs shared thread pool) | False CI red; undermines all-green claim | M | High |
| P3 | AUDIT-070 | Low | Session-ending `ClearCapturedMembersAfterSessionEnding` defeated by call order | Latent (moot under one-way `Shutdown(0)`) | S | Medium |
| P3 | AUDIT-071 | Low | UIPI-elevated guest can stay permanently hidden after rescue retry exhausts | Fail-closed window-lost edge | S | Medium |
| P3 | AUDIT-074 | Info | Per-instance journal cache divergence (ordering assumption) | None today; document guarantee | S | Medium |
| P3 | AUDIT-075 | Info | Dead BoolToVisibilityConverter resource | Maintainer confusion | S | High |
| P3 | AUDIT-076 | Info | Referenced `diagnostic-privacy` OpenSpec missing | Spec/code drift | S | High |
| P3 | AUDIT-077 | Info | Publish smoke limited in CI (self-tests only on build exe) | Coverage gap for publish | S | High |
| P3 | AUDIT-078 | Info | Per-event diagnostic allocations + global trace lock | Minor hot-path alloc | S | Medium |
| P3 | AUDIT-079 | Info | Sanitizer coverage edges (WSL/`\Device\`/URL paths) | Negligible; conservative | S | Medium |

## 20. Recommended Remediation Order

1. **P0 — Fix the process-scoped identity hazard (AUDIT-068, High; AUDIT-026, Medium).** Before `PostMessage(WM_CLOSE)`
   in the close-group path, retain a release-specific marker `SetProp` the verifier *requires to still be present*, or
   add a window-instance discriminator that survives release; extend the `close-group-release-and-close` spec's
   recycled-HWND scenario to the same-process case and add a deterministic test. (The `_capturedIndex` concurrency
   hole from the first pass, AUDIT-001, was **refuted** by the deep dive — it is now a **Low** latent thread-affinity
   fragility; do **not** convert to `ConcurrentDictionary` for that reason. A lightweight `CheckAccess` guard is the
   correct, minimal mitigation.)
2. **P0 — Fix release CI (AUDIT-002 + AUDIT-015).** Add `actions: write` to the `prepare-candidate` and `qualify` job
   permissions; remove the impossible `digicert-stm` RC option. Validate the release pipeline on a dry run before any
   real release.
3. **P0 — Decide runtime self-test posture (AUDIT-003).** Either add a fast post-startup self-check feeding
   observability, or document self-tests as CI-only. Then enable `RuntimeTelemetry` on a diagnostic/verbose path
   (AUDIT-019) so the stabilization campaign is actually measurable.
4. **P1 — Wire/clean the split coordination subsystem (AUDIT-004/005/029/030/031/034).** Make `EnterSplit` honor
   `DefinePair`'s result; connect the relayout generation token; collapse the duplicate coordinator instance, dead
   budget plumbing, and dead settle state into one owned, tested path. This removes the largest maintainability and
   latent-correctness cluster.
5. **P1 — UI-thread responsiveness (AUDIT-006/007/008/009/042/063).** Off-thread picker enumeration (+ try/catch),
   cache per-monitor DPI, off-thread/offline min-track probing, scoped command requery, off-thread diagnostic export,
   async icon resolution.
6. **P1 — Recovery/persistence test gates (AUDIT-011/013/018/020/067).** `InternalsVisibleTo` the unit project and
   cover `Load` failure branches; add headless recovery integration tests; isolate self-test assertions; count
   sub-assertions.
7. **P1 — Documentation accuracy (AUDIT-016/017/052/057).** Re-verify `ARCHITECTURE.md` citations (or drop `:line`),
   fix the split vocabulary + dead branch, correct the release-workflow doc, reconcile `STATE.md` counts.
8. **P1 — Privacy hardening (AUDIT-010/043/044/046/076).** Move to structured/privacy-class redaction; redact the
   `--version` exe path (AUDIT-043 is now a **false positive** — output is already `SanitizeText`'d, but structured
   redaction remains the right direction); make the privacy self-test meaningful + hermetic; broaden secret regex;
   author the `diagnostic-privacy` OpenSpec.
9. **P2 — Bounded collections / minor robustness (AUDIT-021/027/028/040/064).** Clear failure-suppression sets on
   release; conservative min-track fallback; short settle on hide; distinct empty-batch result.
10. **P2 — Test-quality hygiene (AUDIT-012/014/047/048/049/050/051/062).** Positive assertions in ValidationDriver,
    PrintWindow-based/ advisory pixel checks, real coalesce tests, poll-based SaveAsync assert, `object?` converter
    signature, parameterized split Facts, elevation-guard regression test.
11. **P2 — Release/script hygiene (AUDIT-053/054/055/056/058/059/060/061).** Align AccessControl comment/pin, remove
    duplicate `.DESCRIPTION`, fix `SIGNED` label, align solution platform, tighten local-PFX password/ACL handling,
    validate export destination.
12. **P3 — Info items (AUDIT-074/075/077/078/079).** Document the journal-cache ordering guarantee; remove dead
    converter; reconcile publish-smoke coverage; relax diagnostic alloc/lock if safe; broaden sanitizer edge cases.

## 21. Positive Findings

- **Atomic, crash-safe persistence & journal writes** (`WriteThrough` + `Flush(true)` + atomic temp-file `Move`) with
  fail-closed corruption handling (quarantine to `.corrupt-`, refuse all later saves on load failure, preserve `.bak`).
- **Layered window-identity gate** (token + PID + thread + class + exe + process-start) correctly rejects same-process
  HWND recycling and stale `CapturedWindow` objects; deterministic self-tests cover pid/thread/class/exe/start/token
  mismatches, destroyed, and unverifiable cases.
- **Elevation guard is tri-state and fail-closed** — indeterminate + self-not-elevated blocks; no fail-open
  reintroduced (verified directly).
- **P/Invoke correctness**: sized StringBuilder/AllocHGlobal buffers, growing `GetProcessImagePath`, exact-size
  `TOKEN_ELEVATION`, no `unsafe` blocks, 44-byte `WINDOWPLACEMENT` locked by a self-test.
- **WinEvent trust boundary**: rooted field-delegate callback, dispatch-time `_running` + membership re-checks,
  refuses to start without a UI `SynchronizationContext`.
- **Geometry/DPI math is provably correct** and exhaustively self-tested (4096×7×6 matrix + 100k fuzz; 14-case + sweep
  DPI scaling that never under-estimates).
- **Monitor DPI probe robust** with proper thread-DPI-context restore and 8 failure-branch coverage.
- **Z-order pairing avoids repair storms**; lifecycle events correctly *not* coalesced; `DeferredWindowPositionBatch`
  revalidates every entry and abandons without `EndDeferWindowPos` on failure.
- **No-reparent / no-restyle invariant genuinely preserved** in production (only TabDock's own marker window uses
  `WS_CHILD`).
- **Cross-file domain constants single-sourced** (no conflicting duplicate magic numbers) — the brief's prime concern
  was not substantiated.
- **Schema versions correctly namespaced** (state v2, journal v3 in distinct classes).
- **Client/server exit-code contract accurate**; AGENTS.md package claim verified (sole product `PackageReference`).
- **Release pipeline is supply-chain-hardenened**: complete action pinning to immutable SHAs, least-privilege
  `persist-credentials:false`, secrets referenced by name only, fail-closed two-stage design, exact-SHA +
  version-authority enforcement, triple-hash publication gate, no `continue-on-error` abuse.
- **Self-tests are real-behavior, not smoke tests**: drive actual production code through fake-API seams that count
  mutations and assert on journal entries / outcomes; strong adversarial fail-closed coverage (HWND reuse, identity
  changes mid-transaction, malformed/future ledgers, access-denied storage, DPI failures, abandoned-mutex recovery);
  fail-closed aggregation at the gate (`validate.ps1` throws on non-zero exit).
- **ValidationDriver safety wrapping is best-in-class** for a real-input harness: per-scenario spawn/run caps,
  single-instance mutex, ancestor/self-process kill refusal, hermetic state snapshot/restore, `DoNotKill` for the
  user's own apps, provenance-gated input, no skipped/`[Only]`/tautological tests, 146/146 unit pass.
- **LoggingService is well-engineered**: bounded 1 MB rotation, non-blocking drop-on-load queue, capped `.err`
  fallback, backoff, thread-safe idempotent dispose.
- **DiagnosticTrace is correctly bounded and thread-safe** (ring buffer, lock-serialized, defensive dict copy).
- **ProductMutationLease DACL-verified, fail-closed, abandoned-mutex-recovered.**
- **HotkeyService architecture sound** (message-only sink survives launcher close, `MOD_NOREPEAT`, idempotent
  Detach/Dispose).
- **Capture-picker icon worker robust** (bounded single worker, Interlocked cancellation, generation-stamped
  post-back, shutdown guards).

- **(Deep-dive confirmations — second pass)** The persistence/recovery journal is **far more robust than the first
  pass implied**: atomic (`WriteThrough`+`Flush(true)`+atomic `File.Move`), **fail-closed**, **idempotent** via the
  per-window token, with a 6-field identity cross-check that prevents resurrecting dead/recycled HWNDs, and the
  Shepherd invariant preserved on recovery. Release-signing is genuinely **fail-closed with four independent gates**
  (`signtool verify` + RFC3161 timestamp + EKU + current-publisher-policy triple-binding). The `build.yml` PR/CI
  gate is **functional** (build + `dotnet test` + `--selftest-*` + doctor + bundle-privacy + OpenSpec validation).
  The offline self-test harness substantively covers rescue/restore/`Load`-failure
  (`RecoveryJournalSelfTest`/`RuntimeStabilizationSelfTest`/`PendingRecoverySelfTest`/`PersistenceSelfTest` via
  `--selftest-diagnostics`). Shutdown durability is correct: state is flushed **synchronously** before `Shutdown(0)`.

## 22. Audit Coverage / Confidence

- **Scopes covered:** all 12 planned scopes, each producing positive findings + findings + verification limitations.
- **Source verification performed in this consolidation:** AUDIT-002 and AUDIT-003 were re-confirmed against source
  (release workflows set `permissions: contents: read` with no `actions: write` and call `actions/upload-artifact`;
  `App.xaml.cs` intercepts self-test CLI and exits before service construction, with no runtime self-test invocation).
  AUDIT-001's premise was **refuted** by the deep dive (see §8): `WINEVENT_OUTOFCONTEXT` delivers the WinEvent
  callback on the installing UI thread, so `_capturedIndex` is UI-thread-only. The new High, AUDIT-068, was
  established by the DEEP-NATIVE agent from source (process-scoped identity → destructive `WM_CLOSE` to a recycled
  same-process window).
- **Second-pass deep dive (this revision):** six scoped subagents (DEEP-CONC, DEEP-PERSIST, DEEP-NATIVE, DEEP-SEC,
  DEEP-SPLIT, DEEP-TEST) re-examined the concurrency, persistence/recovery, native/HWND, security, split/DPI, and
  test/CI scopes. Crucially, the unit suite was **actually executed this time** (`dotnet test` 4×), revealing it is
  **non-deterministically flaky** (~1/4 failure rate in sample; AUDIT-069) — contradicting the first pass's unexecuted
  "146/146 pass" claim.
- **Build verification:** `dotnet build TabDock.sln -c Debug` → **0 warnings, 0 errors**.
- **Confidence by area:**
  - *Static correctness / architecture / security-critical paths*: **High** (verified by code + project self-tests).
  - *Concurrency (AUDIT-001 refuted; AUDIT-068 identity race), DPI, pixel flakiness*: **Medium** (AUDIT-001's off-UI
    race premise refuted by the Win32 `WINEVENT_OUTOFCONTEXT` delivery contract; AUDIT-068's HWND-recycle race reasoned
    from code; no live-desktop reproduction available).
  - *Behavioral test outcomes*: the first-pass audit did **not** execute the suite; the deep-dive **did** (4 runs) and
    found flakiness (AUDIT-069). ValidationDriver/self-test pass/fail read from source + CI config.
- **Known limitations:** no interactive Windows session for the first-pass read; the deep-dive did run `dotnet test`
  but not the supervised ValidationDriver; release scripts not run against real signing secrets; Action-SHA currency
  and NuGet-audit findings were not network-verified. The converter `CS8625` warnings (AUDIT-049) did **not**
  reproduce in this build's 0-warning result and should be re-confirmed in the relevant configuration.

## 23. Final Assessment

TabDock is a mature, security- and data-durability-conscious Windows desktop application. Its critical core — atomic
crash journaling, fail-closed persistence, layered window-identity gating, correct P/Invoke, supply-chain-hardened
release design, and a serious deterministic self-test harness — is **production-grade**. No Critical defects and no
exploitable security vulnerabilities were found.

The audit's **largest systemic risk is not a single bug but a pattern**: the split-presentation coordination subsystem
contains several safety/perf features that exist in code but are **never wired into the shipping transitions** (inert
generation guard, duplicate coordinator instance, dead budget plumbing, dead settle state, and a caller that ignores
the controller's fail-closed return). Their protection is illusory today and fragile against future edits, and the
documentation has drifted enough to mislead the next engineer.

The issues that should block a general/automated release until addressed are: (1) the **process-scoped-vs-window-scoped
identity hazard** — the close-group path can post a destructive `WM_CLOSE` to a recycled same-process window
(AUDIT-068, **High**), the single most serious *correctable* defect; (2) the **broken release CI** that cannot upload
artifacts and offers an impossible signing option (AUDIT-002/015); and (3) the **real unit-test flake** (AUDIT-069)
that undermines the previously-claimed all-green status. Note the first pass's P0 `_capturedIndex` concurrency hazard
(AUDIT-001) was **refuted** (UI-thread by `WINEVENT_OUTOFCONTEXT`) and is now a **Low** latent fragility, and the
persistence/recovery layer proved **sounder than first reported** (atomic, fail-closed, idempotent; its `Load`/rescue
paths *are* exercised in CI via `--selftest-diagnostics`, AUDIT-011/013 corrected).

**Production-readiness verdict:** *Not yet release-ready for an automated/published release*, but safe for continued
interactive development by the current owner, whose strong fail-closed production paths and supervised ValidationDriver
cover the recovery scenarios manually. Closing the release-CI P0 (AUDIT-002), the runtime-self-test-posture P0
(AUDIT-003), the identity-hazard P0 (AUDIT-068), and fixing the test flake (AUDIT-069) — plus the split-coordination
cluster (P1: AUDIT-004/005/023/026/029/031/033/034) — would move TabDock to release-ready with high confidence. The
remaining Medium/Low/Info findings are real but bounded maintainability, robustness, privacy-hardening, and
documentation items that can be scheduled incrementally per the backlog in §19.

**Overall confidence in this assessment: High** for static-correctness and architecture conclusions; Medium for
concurrency/performance severities that depend on live-desktop timing. The deep dive executed the unit suite
(revealing AUDIT-069 flakiness) but did not run the supervised ValidationDriver or release scripts against real
secrets.
