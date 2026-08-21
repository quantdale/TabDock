# Codebase Deep Audit

Audit date: 2026-08-21 (Asia/Manila)  
Repository: `D:\Documents\tryPython\TabDock`  
Audited revision: `a4e5a3d8c32e85c8da31c9b200aef9fa1dd9b904` (`main`)  
Comparison reference: `origin/main` resolved to the same revision  
Audit mode: read-only technical audit; no application change was implemented.

This report uses the user-requested output path `luna-results.md` for the audit
deliverable.

## 1. Executive Summary

TabDock is a high-maturity Windows desktop utility whose production runtime uses the Shepherd model: captured applications remain independent top-level windows and are positioned over a WPF container. The architecture avoids the historical cross-process `SetParent`/`AttachThreadInput` failure class, and the current code has unusually strong identity, recovery-journal, persistence, diagnostic, and release-provenance controls.

Eight findings were retained after source verification:

| Severity | Count | Assessment |
|---|---:|---|
| Critical | 0 | No confirmed critical runtime or release finding. |
| High | 2 | Dynamic title equality can reject healthy captures; release workflow inputs are interpolated into PowerShell source. |
| Medium | 3 | Modal multi-capture failure UX, queued z-order recheck race, and degraded placement restoration on a rare native fallback. |
| Low | 3 | Inert color command, unbounded diagnostic suppression sets, and one observed timing-sensitive test gate. |

The application is suitable for supervised beta and daily use, but the release process should remain conditional until workflow-dispatch values are transported as data rather than embedded in executable PowerShell. That issue reaches signing-service secrets in Stage A and `contents: write` in Stage B. The runtime findings are bounded and do not invalidate the Shepherd safety model, but the title check is user-visible and the release fallback lacks a complete maximized/minimized restoration guarantee.

The strongest evidence is current source inspection plus successful non-interactive validation: Release builds completed with zero warnings and zero errors; 146 unit tests passed; 138 release-tooling tests passed; NuGet vulnerability audit reported no vulnerable packages; 20 OpenSpec items passed; diagnostics, persistence, privacy, native ABI, redirected recovery, single-file publish, and published `--version` smokes passed. One `-Ci -Publish` run exposed a single 15-second persistence-test timeout (145/146); an immediate standalone test and a complete rerun passed, so this is recorded as a low-confidence test-reliability gap rather than a confirmed data-loss defect.

Major strengths are the Shepherd/no-reparent boundary, layered HWND/process identity, journal-first recovery, serialized durable persistence, bounded/coalesced event handling, privacy-aware diagnostics, and exact-byte Stage A/Stage B release provenance. Major weaknesses are concentrated at boundaries: mutable title metadata is still treated as capture identity, queued native-event intent is not finally foreground-validated, one native placement fallback is geometry-only, and workflow-dispatch input is parsed as PowerShell source. The highest-risk area is the release trust boundary because the affected jobs can hold signing-service credentials or release-write permission. The strongest architectural observation is that the Shepherd model removes an entire class of cross-process ownership and input-queue failures; the weakest subsystem is the duplicated workflow-input boundary. Overall confidence is High for static/runtime-seam conclusions and Medium for unrun interactive desktop and physical compatibility behavior.

## 2. Audit Scope

The audit read `real-goal.txt`, the canonical agent instructions, `.agent/STATE.md`, the active plans and decisions needed for context, the current source, tests, scripts, CI workflows, OpenSpec specifications, architecture/testing documentation, and relevant history. Repowise was used first for repository mapping, hotspots, symbol/context lookup, risk mapping, and dead-code candidates; every retained finding was checked against the current source.

Relevant technology and applications discovered were C# 12, .NET 8, WPF, Win32/P/Invoke, PowerShell, GitHub Actions, the WPF desktop application, the separate Spike, the UnitTests project, the real-input ValidationDriver/GuineaPig harness, the compile-only Performance runner, and the external Stage A signing/Stage B publication control plane. The application has one direct package reference (`System.Threading.AccessControl`); the linker task is SDK auto-referenced.

Repository areas inspected included:

- Production composition and UI: `App.xaml.cs`, `App.xaml`, `Views/`, `ViewModels/`, `Models/`, `Infrastructure/`, `NativeMethods.cs`, `TabDock.csproj`, `TabDock.sln`, and `app.manifest`.
- Runtime services: all 41 files under `Services/` (about 17,432 lines), including Shepherd capture/release, identity, WinEvent dispatch, guest lifecycle, group management, persistence, journal recovery, presentation batching, diagnostics, logging, hotkeys, and icon handling.
- Data/UI support: 6 model files (about 700 lines), 6 view-model files (about 1,109 lines), 8 view files (about 4,506 lines), and the native WPF host.
- Tests and harnesses: 11 unit-test files (about 2,246 lines), 29 ValidationDriver/GuineaPig files (about 15,807 lines), and the experimental Spike.
- Engineering controls: 9 scripts (about 5,678 lines), 4 GitHub workflows (about 1,210 lines), 12 current OpenSpec specs, release documents, privacy/doctor tooling, and configuration files.
- History and working-tree context: current `HEAD`, branch/remotes, recent hardening commits, pre-existing untracked reports, and stale local remote-tracking references.

Repowise was refreshed to the audited `HEAD`; its current status reports 247 pages, no language-model provider, and an index synchronized to the exact revision above. It was treated as a navigation map, not as proof. The repository’s source, workflow, and test files were read directly before conclusions were retained.

Pre-existing working-tree changes and untracked artifacts were preserved. In particular, the existing audit result files, `real-goal.txt`, other user-supplied result files, `.agent/STATE.md`, and other unrelated artifacts were not edited or deleted. The only intentional repository file created by this audit is this report.

Limitations:

- The real-input `tests/ValidationDriver/` scenarios were not run. `docs/TESTING.md` requires a supervised interactive Windows session because the harness sends real mouse and keyboard input with `SendInput`.
- No physical Windows 10 machine, mixed-DPI hardware matrix, browser matrix, elevated application, or production DigiCert signing operation was available for this audit.
- The Spike was reviewed as experimental tooling, not treated as the production capture backend.
- No network service or remote API is part of the application runtime; release workflows were reviewed statically and their local policy tests were run.

## 3. System Architecture Model

TabDock’s composition and control flow are:

```text
App.xaml.cs
  ├─ single-instance and product-mutation lease
  ├─ startup / crash / session-ending / shutdown coordination
  ├─ GroupManager ── Groups ── Containers + ViewModels
  │      └─ PersistenceService ── state.json / .bak / atomic .tmp
  ├─ WindowShepherdService ── capture / hide / show / placement / z-order / journal
  ├─ WinEventMonitor ── posted WinEvents ── GuestLifecycleService
  ├─ HotkeyService and diagnostics
  └─ WPF ContainerWindow + NativeHwndHost marker

Independent guest HWNDs remain top-level windows:
  guest HWND(s) positioned over the content marker
  container HWND pinned below the active guest(s)
```

The principal architectural invariants are:

1. A guest is never reparented, re-owned, restyled, clipped, or attached to the TabDock input queue. `WindowShepherdService` changes only reversible presentation state: placement, z-order, visibility, and DWM transition suppression.
2. Capture admission combines HWND, process ID, GUI-thread ID, class, executable path, process-start ticks, elevation, and a private HWND property token. The token and object binding defend against same-process HWND recycling.
3. Dangerous presentation mutations are journal-first. The hidden-window journal is durably written before hiding, uses atomic replacement and backup/quarantine behavior, and is retained when identity or storage evidence is insufficient.
4. Persistence has distinct synchronous semantic saves and asynchronous high-frequency saves. JSON is built before the background task, generations are monotonic, disk mutation is serialized under one gate, and stale async generations are rejected.
5. WinEvents arrive from an out-of-context native hook and are posted to the WPF dispatcher. Guest lifecycle handlers coalesce name changes and presentation repairs; object identity is rechecked before most dispatched actions.
6. Split presentation uses a deferred native positioning batch and operation budgets. The container remains below both independent guest windows.
7. Production release is split into Stage A candidate preparation and Stage B publication. Stage A builds/signs/qualifies once; Stage B downloads and validates the exact bytes without rebuilding, re-signing, or executing the candidate.

The architecture is coherent. The main residual risks are at boundaries between mutable UI metadata and stable identity, native event time and dispatcher time, native placement APIs and fallback semantics, and CI input data and PowerShell code.

## 4. Validation Results

| Command / check | Result | Relevant observations |
|---|---|---|
| `dotnet build TabDock.sln -c Release --nologo` | Passed; 0 warnings, 0 errors. | Main app, Spike, and UnitTests built. |
| `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release --nologo --no-build` | Passed; 146 passed, 0 failed, 0 skipped. | Headless unit suite passed in under one second on the final standalone run. |
| `dotnet test ... --filter FullyQualifiedName~SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration` | Passed; 1 passed. | Targeted rerun disproved a persistent failure after the first validation timeout. |
| `pwsh -NoProfile -File scripts/release-tooling-tests.ps1` | Passed; 138 passed, 0 failed. | Manifest, signing, provenance, policy, action-pin, permission, and Stage A/B static tests passed. |
| `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci` | Passed. | Audited restore, vulnerability report, builds, unit tests, self-tests, doctor, pending recovery, redirected recovery, privacy, and OpenSpec passed. |
| `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci -Publish` | First run stopped at 145/146. | `SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration` reached its 15-second timeout; no publish occurred. |
| Same `validate.ps1 -Ci -Publish` rerun | Passed end-to-end. | Single-file `win-x64` publish and published `--version` smoke passed. |
| NuGet vulnerability audit inside `validate.ps1` | No vulnerable packages. | `TabDock`, `TabDock.Spike`, and `TabDock.UnitTests` were checked from the configured sources. |
| OpenSpec validation | 20 passed, 0 failed. | Current specs and active changes validated. |
| Native ABI self-test | Passed on Windows 11 build 26200. | `WINDOWPLACEMENT` length 44 accepted; unsupported 60-byte form rejected as expected. |
| `dotnet list TabDock.csproj package --include-transitive` | Passed. | Direct package is `System.Threading.AccessControl` 10.0.11; ILLink task is SDK auto-reference. |
| `dotnet msbuild TabDock.csproj -getProperty:PublishTrimmed -getProperty:TrimMode ...` | Passed. | Publish properties are self-contained, single-file, ReadyToRun, `win-x64`, with `TrimMode=partial`. |
| `repowise update` / `repowise status` | Synchronized to audited `HEAD`. | Used for mapping and hotspot discovery only; source was verified directly. |
| `git status --short`, `git rev-parse`, `git ls-remote --heads origin` | Passed/read-only. | `HEAD` and `origin/main` match; branch is `main`; pre-existing dirty/untracked work was preserved. |
| Interactive ValidationDriver | Not run. | Deliberately excluded under `docs/TESTING.md` supervision rules because it sends real input. |

The one transient persistence timeout is significant as a quality-gate observation. The implementation’s production save path still uses a serialized gate and durable writes, and the test passed on rerun; there is not enough evidence to classify it as a product persistence failure. It does show that the test’s fixed wall-clock budget can produce a false negative under some local scheduling or I/O conditions.

## 5. Critical Findings

No Critical finding was confirmed. The production runtime contains no active cross-process reparenting path, no `AttachThreadInput` path, no unbounded log-file fallback, no confirmed arbitrary network input surface, and no evidence of journal or JSON write tearing in the current implementation. The release workflow injection issue below is High rather than Critical because it requires an actor who can dispatch these manually trusted workflows; it is nevertheless a release-blocking hardening item for a signing/publishing pipeline.

## 6. High-Severity Findings

### AUDIT-001 — Mutable window titles are treated as a fatal capture-identity mismatch

Severity: High  
Confidence: High  
Category: Correctness / capture reliability / UX  
Affected paths and symbols: `Services/WindowShepherdService.cs:433`, `:590-600`, `:765-771`; `CapturedWindow.OriginalTitle`.

Summary: `WindowShepherdService.Capture` snapshots `initialTitle`, performs the capture admission work, then requires `finalTitle` to equal the initial value byte-for-byte. A legitimate title update during that short handshake causes capture to be rejected even when the HWND, process, thread, class, executable, and process instance remain stable.

Evidence:

```csharp
string initialTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
...
string finalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
if (string.IsNullOrWhiteSpace(currentExePath)
    || !string.Equals(currentExePath, exePath, StringComparison.OrdinalIgnoreCase)
    || !string.Equals(finalClass, initialClass, StringComparison.Ordinal)
    || !string.Equals(finalTitle, initialTitle, StringComparison.Ordinal))
{
    error = "The window identity changed while it was being captured.";
    return null;
}
```

The same file’s `EvaluateCurrentCapturedWindow` documentation at `:765-771` explicitly says titles are deliberately not part of stable identity because guests legitimately change them. `GuestLifecycleService` separately debounces `EVENT_OBJECT_NAMECHANGE` and updates tab titles after capture. The cross-file contract is therefore internally inconsistent: title mutation is accepted during the captured lifetime but rejected during admission.

Failure scenario: a browser changes from a loading title to a document title, a terminal updates its working-directory caption, a media player updates playback text, or an editor adds/removes a dirty marker while the picker selection is being admitted. The user receives a generic identity-change failure and must retry.

Impact: healthy windows can fail to capture nondeterministically. The problem is especially visible with dynamic browsers, terminals, editors, and media applications. It does not imply HWND takeover or guest corruption; it is a false negative at the admission boundary.

Root cause: mutable presentation metadata was included in the fatal identity tuple even though the later identity gate intentionally separates stable process/window identity from mutable title text.

Recommended direction: use the existing stable identity tiers as the capture veto and treat the final title as the latest display metadata. Preserve the title-change event path for tab-label updates.

Verification recommendation: add a deterministic capture-admission test in the existing hardening seam that changes only the title between the initial and final probes and proves that capture succeeds; retain separate tests for class, executable, process-start, token, and HWND-generation changes.

### AUDIT-002 — Workflow-dispatch inputs are interpolated into executable PowerShell in the release trust boundary

Severity: High  
Confidence: High  
Category: Supply-chain security / CI runner command injection  
Affected paths and symbols: `.github/workflows/release.yml:74-75,124`; `.github/workflows/prepare-release-candidate.yml:290`; `.github/workflows/publish-release.yml:169,203,314,364,529`; related workflow-dispatch inputs `version`, `run-id`, `external-evidence`, and `sha`.

Summary: several user-controlled `workflow_dispatch` values are inserted directly into single-quoted PowerShell source inside `run:` blocks. Validation occurs after the expression has already been parsed as code. The production Stage A path protects the requested SHA with a trusted dispatch contract, but the free-form `version` value remains interpolated into the signing command. Stage B validates `run-id` after interpolating it into PowerShell and repeats the value in the job that has `contents: write`.

Evidence:

- RC qualification invokes `release-qualify.ps1 -Sha '${{ inputs.sha }}' -Version '${{ inputs.version }}'` in `.github/workflows/release.yml:124`.
- Production candidate preparation invokes the same pattern at `.github/workflows/prepare-release-candidate.yml:290`, after signing-service credentials are available to the job.
- Stage B assigns `$runId = '${{ inputs.run-id }}'` at `publish-release.yml:169` and `:203`, then checks `-notmatch '^\d+$'` only after PowerShell has parsed the surrounding script.
- Stage B embeds `inputs.run-id` again in the trusted policy gate (`:314`), the handoff record (`:364`), and the `contents: write` publish job’s handoff validation (`:529`).
- `external-evidence` is transported through an environment variable at `:294-299`, which demonstrates the safer data-transport pattern already used elsewhere.

Failure scenario: an authorized workflow dispatcher supplies a value containing a quote and PowerShell statement terminator. The resulting workflow script can execute an additional command before the intended script receives the value. On Stage A this can expose signing-service authentication material or alter the runner’s build/signing behavior. On Stage B the same class of issue can execute in a job holding `contents: write`, potentially changing release state or publishing attacker-selected data. The workflows are manual and gated, so this is not a public unauthenticated trigger; it is still an unacceptable trust-boundary weakness for production release automation.

Impact: arbitrary runner command execution under the workflow token and, depending on the path, access to signing-service secrets, artifact permissions, Actions artifact write permission, or release mutation permission. The exact-SHA and no-candidate-execution controls prevent several substitution attacks but do not make a parsed command-injection primitive safe.

Root cause: workflow expressions are being used as source interpolation instead of being passed as environment data or action inputs and then validated as data. The existing release-tooling tests cover artifact provenance and policy gates but do not assert that every workflow input is transported without raw interpolation.

Recommended direction: pass every dispatch value through step/job environment variables, validate strict formats before use, use typed/derived values where possible, and add a static workflow test that rejects raw `inputs.*` interpolation inside PowerShell code. Keep the existing exact-SHA, trusted-policy, least-privilege, and Stage A/Stage B separation controls.

Verification recommendation: test adversarial quote/newline/PowerShell metacharacter values in an isolated workflow-parser fixture; prove that the resulting command receives one data argument, that invalid SHA/run-id/version values fail before credentials or write permissions are materialized, and that the publish job cannot be reached with a malformed handoff.

## 7. Medium-Severity Findings

### AUDIT-003 — Sequential multi-capture failure can leave a visible guest above a disabled modal owner

Severity: Medium  
Confidence: High  
Category: UI modality / batch failure UX  
Affected paths and symbols: `App.xaml.cs:903-919`, `ContainerWindow.CaptureWindow`, Shepherd z-order behavior.

Summary: `ShowCapturePickerCore` processes selected targets sequentially. If one target captures and a later target fails, it immediately calls `MessageBox.Show(container, ...)`. The guest is an independent top-level window, so it can remain visible above the container while the container is disabled by the modal dialog.

Evidence: `App.xaml.cs:903-912` loops over `SelectedTargets`, captures each target, and calls `MessageBox.Show(container, error, ...)` on failure. Cleanup of a newly created group occurs only after the loop at `:916-919`; previously captured members remain materialized. The Shepherd class documentation and `ContainerWindow` pairing code confirm that guests are separate top-level HWNDs rather than children of the WPF owner.

Failure scenario: target A is captured successfully, target B is elevated, exits, or changes identity and fails admission. The warning dialog is owned by the container; the captured guest may cover the container’s chrome and accept input while the owner is modal-disabled. The user can see a live guest but cannot reliably reach tabs, close controls, or the error dialog.

Impact: confusing focus/occlusion, possible apparent application freeze, and a poor partial-success experience. This does not leave the guest unreleased by itself; the finally block and group lifecycle still run.

Root cause: a synchronous owner-modal error notification is emitted inside a multi-target loop without accounting for the independent z-order surface of already-captured guests.

Recommended direction: accumulate per-target failures and present one batch result after the capture transaction, or use a notification surface that cannot be occluded by a guest while preserving the existing owner and cleanup semantics.

Verification recommendation: add an interactive or injected UI test with two targets where the first succeeds and the second fails; assert the container remains operable, the failed target is reported, and the successful guest remains correctly paired and releasable.

### AUDIT-004 — Desktop reorder repair is foreground-checked before queueing but not again at final repair

Severity: Medium  
Confidence: Medium  
Category: Concurrency / WinEvent dispatch / z-order correctness  
Affected paths and symbols: `Services/WinEventMonitor.cs:260-269,342-353`; `Services/GuestLifecycleService.cs:264-320,323-344`; `Views/ContainerWindow.xaml.cs:2547-2586`.

Summary: a desktop `EVENT_OBJECT_REORDER` callback snapshots the current foreground HWND, posts the event to the UI dispatcher, and `OnZOrderChanged` checks the foreground snapshot against `GetForegroundWindow()` at dispatch time. `QueueRepair` then defers the actual pair operation at `DispatcherPriority.Input`. `ProcessPendingRepair` rechecks that the HWND is still a captured member and that its container exists, but does not recheck that it is still the current foreground window immediately before pairing.

Evidence: `GuestLifecycleService.OnZOrderChanged` at `:271-277` performs the callback/dispatch-time check. `QueueRepair` at `:307-320` coalesces the HWND and schedules `ProcessPendingRepair`. The final loop at `:323-344` calls `PairZOrderBehindGuest(hwnd)` after only membership/container checks. `WinEventMonitor.Raise` at `:342-353` correctly protects against Stop(), object replacement, and HWND recycling, but it does not establish a final foreground guarantee. `PairZOrderBehindGuest` has useful active-window checks for ordinary mode, but split handling and the surrounding z-order repair still rely on the stale event’s intended foreground identity.

Failure scenario: a guest becomes foreground, reorder is queued, and the user Alt-Tabs to another application before the Input-priority callback runs. The queued repair can apply the old pair intent after the foreground transition. In ordinary mode this is mostly cosmetic and some later checks may suppress it; split and rapid state transitions have a wider stale-intent window.

Impact: transient z-order churn, an unexpected container/guest pair movement, or a visual interruption while the user is switching applications. No evidence indicates input-queue corruption or a wrong-process mutation because the current-member/object checks remain in place.

Root cause: event freshness is validated at multiple earlier phases but the queued state does not carry or revalidate a final foreground generation at the native mutation boundary.

Recommended direction: retain the object-identity and coalescing guards, and add a final foreground/active-pair validation immediately before the pair mutation or discard stale repair entries when foreground intent has changed.

Verification recommendation: introduce a dispatcher-controlled test seam that queues a reorder, changes the foreground/member intent before processing, and asserts no stale pair operation; supplement with a supervised rapid Alt-Tab/direct-click scenario.

### AUDIT-005 — `SetWindowPos` release fallback does not fully restore original placement state

Severity: Medium  
Confidence: High  
Category: Native lifecycle / release correctness  
Affected paths and symbols: `Services/WindowShepherdService.cs:1730-1770`, `CapturedWindow.OriginalPlacement`, `OriginalBounds`.

Summary: `ReleaseVisible` normally restores the captured `WINDOWPLACEMENT`. If `SetWindowPlacement` fails, the fallback uses `SetWindowPos` with the captured rectangle and then separately calls `ShowWindow` using the original show command. That fallback cannot restore all `WINDOWPLACEMENT` state, particularly the normal-position metadata and exact minimized/maximized placement semantics.

Evidence: the normal path copies `OriginalPlacement` and calls `_releaseApi.SetWindowPlacement` at `:1750-1755`. On failure, the fallback at `:1760-1767` only supplies `OriginalBounds` to `SetWindowPos` with `SWP_NOZORDER | SWP_NOACTIVATE`. The subsequent show command at `:1773-1775` is derived from `OriginalPlacement.showCmd`, but it does not restore `rcNormalPosition`, min/max restore points, or other placement fields through the fallback API. Capture does preserve those fields at `:642-646`, and journal recovery has a complete placement path, so the loss is specific to this rare live-release fallback.

Failure scenario: a window captured while maximized or minimized rejects `SetWindowPlacement` because of a transient native/UIPI/target condition, while `SetWindowPos` still succeeds. Release returns it to a rectangle and show state that may not match the original monitor or future restore rectangle.

Impact: degraded user window state after a rare release failure. The service remains fail-closed around identity and reports recovery failure if both placement paths fail; this is not silent cross-process ownership damage.

Root cause: the fallback is intentionally geometry-only, but the success path treats that geometry operation as equivalent to restoring a full `WINDOWPLACEMENT`.

Recommended direction: distinguish “geometry restored” from “full placement restored” in the release contract, and define a fallback that preserves the original normal rectangle/show state as far as Win32 permits or retains recovery evidence until a full restoration is confirmed.

Verification recommendation: add injected native tests for `SetWindowPlacement=false` with original normal, minimized, and maximized states; verify exact postconditions and journal retention. Run a supervised real-window release test for the supported Windows builds.

## 8. Low-Severity Findings

### AUDIT-006 — `PickColorCommand` is a public inert placeholder with no binding

Severity: Low  
Confidence: High  
Category: Maintainability / UI completeness  
Affected paths and symbols: `ViewModels/GroupViewModel.cs:98-100,154-160`.

Summary: `PickColorCommand` is exposed as an `ICommand` but is deliberately initialized to `new RelayCommand(_ => { })`. Repository/XAML search found no `PickColor` or `ColorPick` binding. The comment documents that this replaced an incorrect command which opened the capture picker.

Failure scenario: a future XAML change binds the existing command and presents a color affordance without implementing behavior; the UI will accept the interaction and do nothing.

Impact: no current runtime impact because it is unbound. It increases the chance of silent UI incompleteness and leaves an unused public surface.

Root cause: an earlier incorrect binding was neutralized with a placeholder, but the command/property was retained without a feature contract.

Recommended direction: either remove the unbound surface until color picking is implemented or define the command’s expected UI contract and bind it to a real color workflow. Preserve the existing `AccentColor` persistence contract.

Verification recommendation: add a binding/command-surface test or a documented XAML assertion when the color feature is introduced.

### AUDIT-007 — Per-session HWND suppression sets are never retired

Severity: Low  
Confidence: High  
Category: Long-session memory / diagnostics  
Affected paths and symbols: `Services/WindowShepherdService.cs:165-176,205-210,796-799`.

Summary: `_positioningFailuresLogged` and `_identityFailuresLogged` are unbounded `HashSet<long>` instances keyed only by `HWND.ToInt64()`. They suppress repeated log lines, but no `Clear` or per-window removal exists. HWND values are recyclable rather than generation identifiers.

Failure scenario: a long-running TabDock session sees many failed/dead/elevated guest HWNDs. Each distinct failed value remains in the set for the process lifetime; if a numeric HWND is later recycled, its first failure can also be suppressed as if it were the old window.

Impact: bounded in ordinary usage and unlikely to cause material memory pressure, but memory grows with the number of distinct failures and diagnostics can lose the first report for a recycled handle. Normal runtime behavior and safety gates remain intact.

Root cause: a hot-path logging optimization uses a raw numeric HWND as a permanent session key rather than a lifecycle-bound captured-window identity or bounded eviction structure.

Recommended direction: retire entries when a captured member is released/removed, key suppression by the captured object/generation where appropriate, or use a bounded diagnostic cache with explicit session semantics.

Verification recommendation: inject many failing HWNDs and a recycled numeric value; assert bounded storage and one first-report event per logical window generation.

### AUDIT-008 — Persistence coalescing test has a timing-sensitive false-negative gate

Severity: Low  
Confidence: Medium  
Category: Test reliability / CI signal quality  
Affected paths and symbols: `tests/UnitTests/PersistenceSingleWriterTests.cs:87-118`; `Services/PersistenceService.cs:143-149,231-257`.

Summary: during the first complete `validate.ps1 -Configuration Release -Ci -Publish` run, `SaveAsync_RapidCoalesce_WritesOnlyLatestGeneration` timed out after 15 seconds with “latest async generation never reached disk.” The same targeted test, the full 146-test suite, and a complete `-Ci -Publish` rerun passed.

Evidence: the test dispatches 60 `Task.Run` writes and polls for the final generation for a fixed 15 seconds at `:97-108`. `PersistenceService.SaveAsync` schedules each commit independently at `:143-149`; the commit gate is serialized and latest-generation guarded at `:231-257`. The observed failure was not reproduced by immediate rerun.

Failure scenario: runner scheduling, filesystem contention, or adjacent validation activity delays the final task or the test’s read observation beyond the fixed budget. The validation job reports failure even though a later rerun completes.

Impact: noisy CI and reduced confidence in a release gate. The evidence does not establish a production persistence race or data loss; the test’s timing contract is the finding.

Root cause: a high-contention asynchronous stress test uses a wall-clock assertion without a deterministic completion hook for this specific burst. A `WhenWritesSettledAsync` seam exists and is used by other tests, but this case relies on polling.

Recommended direction: make the test wait on a deterministic drain/settlement signal in addition to checking the latest JSON, and retain a bounded timeout only as a failure escape hatch. Keep the production latest-wins and durable-write semantics unchanged unless a separate reproduction proves a product defect.

Verification recommendation: repeat the test under constrained CPU and slow filesystem conditions, capture the final generation and task state on timeout, and run it repeatedly in CI to establish a flake rate.

## 9. Optimization Opportunities

These are opportunities, not additional confirmed defects:

1. `Views/CapturePickerWindow.xaml:41-47` explicitly disables WPF list virtualization. The picker already filters aggressively and resolves icons asynchronously with an executable-path cache, so normal desktops are fine; enabling virtualization or measuring a very large-window desktop could reduce layout work at unusual scale.
2. `Services/GroupManager.cs:120-147` correctly separates one-second debounced saves from synchronous semantic saves. A future performance pass could measure dispatcher delay and write latency rather than changing durability based on assumptions.
3. `GuestLifecycleService` already coalesces reorder/foreground/move-size events and title changes. Instrumentation could distinguish events discarded as stale from events that produce a native repair, helping tune the queue without increasing hot-path logging.
4. `WindowShepherdService` probes cross-process minimum tracking sizes with a bounded 100 ms timeout and caches successful values. A larger multi-guest stress test could determine whether dirty constraint refreshes need a stricter budget or scheduling policy.
5. `LoggingService` uses a bounded queue and rotating file. Operational telemetry could report dropped-log counts and queue high-water marks so performance tradeoffs remain observable without making logs unbounded.

## 10. Architectural Improvement Opportunities

1. Extract the capture-picker batch orchestration currently housed in `App.xaml.cs` into a small workflow/controller with explicit partial-success and notification policies. This would reduce composition-root size and make AUDIT-003 testable without a full WPF startup.
2. Introduce an explicit dispatcher/test seam for `GuestLifecycleService` repair scheduling. The current `Dispatcher.CurrentDispatcher.BeginInvoke` is correct for production UI affinity but makes final-foreground race tests harder to express.
3. Model release as an explicit restoration result containing placement completeness, visibility, transitions, token removal, and recovery-pending status. This would make the geometry-only fallback limitation visible at the type boundary.
4. Treat release-workflow inputs as a common typed policy boundary shared by RC, Stage A, and Stage B. The existing artifact policy module is strong; a common input parser/static invariant would prevent trust-boundary regressions across duplicated YAML.
5. Keep the Shepherd architecture as the primary seam. Reintroducing parent/style/input-queue mutation would trade away the strongest reliability property identified in this audit.

## 11. Test and Quality-Gate Gaps

The current automated suite is strong for policy and deterministic seams, but the following gaps remain:

- No deterministic capture-admission test proves that a title-only change between the initial and final probes is accepted; this directly exercises AUDIT-001.
- No automated UI test covers partial multi-target capture where a later target fails while an earlier guest remains visible.
- No dispatcher-controlled test proves a queued desktop reorder is discarded after a foreground change immediately before repair.
- No unit/native-seam test covers `SetWindowPlacement` failure with minimized and maximized original placement states.
- The release-tooling suite validates exact SHA, artifact hashes, signing identity, Stage A/Stage B separation, no candidate execution, action pins, and permissions, but it does not enforce “workflow inputs never appear as raw PowerShell source.”
- The persistence test’s one-off timeout shows that the current fixed polling budget can make the canonical validation signal flaky under some conditions.
- `ValidationDriver` contains broad real-input scenarios, including direct-click pairing, crash rescue, persistence kill, browser flows, split presentation, and guarded input. Those scenarios were not run here because they require supervised desktop control.
- Physical mixed-DPI and Windows 10/11 compatibility evidence is an external release gate, not a local automated result. Production signing is likewise external and was not attempted.

## 12. Security and Privacy Assessment

The runtime security posture is strong for a local Windows utility:

- `app.manifest` requests `asInvoker`; the app does not self-elevate and rejects higher-integrity targets under UIPI rather than weakening the boundary.
- `WindowIdentityGate` uses stable process/window facts plus a private capture token and process-start identity. The title is intentionally omitted from steady-state identity, which makes AUDIT-001 a clear admission-only inconsistency rather than a missing runtime identity check.
- The Shepherd model avoids cross-process parent/style/owner mutation and shared input queues. This materially reduces arbitrary guest-process interference and teardown risk.
- Persistence distinguishes missing, corrupt, unsupported, and unreadable state; it quarantines proven corruption and refuses unsafe overwrite. The journal uses identity validation before recovery and retains unresolved evidence.
- Logs are bounded and rotated. Doctor/support-bundle paths sanitize titles, paths, and credential-like values; `docs/TESTING.md` and README explicitly warn that raw local logs must be redacted before sharing.
- `ContainerWindow` diagnostics can include executable-path information in local state snapshots/logs. This is not an external disclosure path in the current design, but it is a privacy consideration if raw logs are copied outside the machine.
- The release pipeline has full-SHA action pins, `persist-credentials: false`, a trusted policy checkout, least-privilege job permissions, exact artifact hashes, signer identity checks, timestamp checks, and Stage B candidate non-execution. AUDIT-002 is the material residual weakness because manual inputs are still parsed as source in several steps.
- The only direct application package is `System.Threading.AccessControl` 10.0.11; the current vulnerability audit found no vulnerable packages. The SDK auto-reference `Microsoft.NET.ILLink.Tasks` was also resolved and audited.

No network listener, telemetry service, credential store, or remote API is part of the production runtime identified in this review. The release workflows do access GitHub and signing infrastructure, which is why their input boundary is treated separately from the desktop application.

## 13. Reliability and Failure-Recovery Assessment

Reliability is a principal strength. Capture journals the reversible recovery state before the first hide/presentation mutation; release verifies identity at each destructive boundary; journal replay checks HWND/PID/thread/class/executable/process-start evidence; corrupt or ambiguous evidence is preserved rather than guessed. Persistence uses a single writer, atomic move, write-through flush, backup, and latest-wins generations.

The application also handles process and UI lifecycle deliberately: container close removes hooks and timer work, group teardown detaches view-model subscriptions, session-ending and unhandled-exception paths attempt emergency release, and capture is disabled when durable journal storage is unavailable. The independent top-level guest model means a force-killed TabDock cannot destroy a guest through parent teardown.

There is no application network retry or remote-service restart path to assess. The release workflows intentionally fail rather than guess when GitHub artifacts, policy records, signing configuration, or external qualification evidence are missing.

Residual reliability risks are the three Medium findings: title admission false negatives, stale queued z-order intent, and geometry-only release fallback. The low persistence test timeout is evidence about the quality gate, not enough evidence about production write safety.

## 14. Performance and Scalability Assessment

The implementation is optimized around Windows desktop event behavior rather than bulk server scale:

- `GroupManager` maintains an O(1) captured-HWND/member index for desktop-wide WinEvent filtering.
- `GuestLifecycleService` coalesces title and repair storms; it does not hook high-volume location-change events.
- Split positioning uses `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` and records operation budgets to detect redundant work.
- `IconService` caches by executable path and shares in-flight extraction for repeated picker rows.
- `LoggingService` bounds queue and file growth.
- Persistence skips unchanged serialized state and moves high-frequency writes off the UI thread while keeping semantic boundaries synchronous.

The practical scale is a desktop-sized set of top-level windows and a small number of groups/guests. WPF picker virtualization is disabled, and raw failed-HWND suppression sets are permanent per session; these are the main scaling opportunities. There is no evidence of an unbounded production queue, per-frame disk write, or global polling loop. No formal performance benchmark was run, so latency claims beyond the built-in budgets remain medium confidence.

The runtime has no database or network-serving workload, so database indexes, query plans, API latency, and network infrastructure cost are not applicable to the desktop process. GitHub, NuGet, OpenSpec, and the external signing provider are build/release dependencies; their network failure behavior is fail-closed in the release scripts but was not tested against live provider outages.

## 15. Maintainability / Technical Debt Assessment

Maintainability is good in core safety code but uneven at orchestration boundaries:

- P/Invoke declarations are centralized in `NativeMethods.cs`, nullable annotations are enabled, implicit usings are disabled, and naming/style are consistent.
- `WindowShepherdService` and `App.xaml.cs` are large, highly responsibility-dense files. Their extensive comments and test seams preserve historical reasoning, but changes require careful cross-file review.
- `App.xaml.cs` owns startup, containers, capture-picker behavior, shutdown, hooks, diagnostics, hotkeys, and failure UI; extraction would reduce change coupling.
- Release workflows repeat trusted-input and identity checks across YAML and PowerShell. The controls are strong but duplicated, creating drift risk such as AUDIT-002.
- `PickColorCommand` is documented as intentional but remains an inert public surface.
- Historical audit/design files are valuable context but increase search noise. Their dates and scope notes should remain prominent so agents do not treat old reparent-era conclusions as current implementation facts.

## 16. Documentation / Implementation Divergence

The canonical documentation generally matches the implementation: README and `docs/ARCHITECTURE.md` describe independent top-level guests, journal-first mutation, identity-gated release, split presentation, and external human qualification. OpenSpec validation passed all 20 current items.

The following divergence or interpretation risks remain:

- `docs/ARCHITECTURE.md` describes desktop reorder handling as callback-time foreground plus UI-thread revalidation. The code revalidates object identity in `WinEventMonitor.Raise` and foreground at `OnZOrderChanged`, but the final queued `ProcessPendingRepair` lacks a final foreground check. This is the documentation counterpart of AUDIT-004.
- `docs/internal/TEST_PLAN.md` and `docs/internal/deep-audit-2026-07-17.md` contain pre-Shepherd/reparent-era material. They are explicitly labelled historical in the current repository, so they are not runtime defects; they remain a search/maintenance hazard if read without their headers.
- `docs/internal/audit-2026-07-25.md` is pinned to an earlier commit and records a now-corrected `WinEventMonitor` comment as a confirmed issue. It is a historical audit record, not current source authority.
- The source comment in `WinEventMonitor.cs:275-281` is now aligned with Shepherd, so the earlier stale-comment issue is not reported as a current code finding.

## 17. Dead / Stale / Suspicious Code

Repowise identified 22 dead-code candidates, including 13 high-confidence unused-export candidates. Direct verification classified most as false positives or intentional scaffolding:

- `BoolToVisibilityConverter`, `ColorToBrushConverter`, `NativeHwndHost`, source-generated JSON types, and several diagnostic/presentation types are reachable through XAML, generated code, or indirect C# paths.
- `PickColorCommand` is the confirmed inert/unbound production placeholder and is reported as AUDIT-006.
- `Spike/TabDock.Spike/Program.cs` intentionally demonstrates reparenting, but it is a separate experimental project. Its current code requires explicit confirmation, caps spawns, snapshots console windows, validates HWND/class/PID ownership before `SetParent`, and is covered by test-tooling safety specifications. It must not be mistaken for the production backend.
- Root-level Python hotfix/audit scripts, result reports, `.agent` investigations, and historical documents are process artifacts rather than application inputs. The main project excludes `Spike`, tests, docs, `bin`, and `obj` from its default compile glob. They should be managed as repository hygiene only with owner approval.
- The local repository has stale remote-tracking branch names in `git branch -a`, while `git ls-remote --heads origin` currently exposes only `main`. This is Git metadata hygiene, not an application defect, but it can confuse branch-discovery automation.

No additional confirmed dead production service or unsafe production reparenting path was found.

## 18. Cross-Cutting/Systemic Issues

The findings cluster around four system boundaries:

| Boundary | Current strength | Residual issue |
|---|---|---|
| Stable identity vs mutable metadata | Strong PID/thread/class/exe/start/token gate | Capture admission still treats title as stable. |
| Native event time vs UI dispatch time | Object identity, coalescing, dispatcher affinity | Final foreground intent is not rechecked for every queued repair. |
| Full placement vs fallback geometry | Journal and normal `WINDOWPLACEMENT` path are comprehensive | Rare `SetWindowPos` fallback is not semantically equivalent. |
| Data trust vs executable trust | Stage A/B policy separation and hash gates are strong | Manual workflow input is parsed as PowerShell source. |

There is also a recurring “correct core, incomplete edge contract” pattern: the code often has a fail-closed path, but the user-facing or diagnostic result does not always preserve the full semantic state (modal partial success, geometry-only release fallback, or permanent logging suppression). These are appropriate targets for focused contract tests rather than broad rewrites.

## 19. Improvement Backlog

The backlog below records direction and verification targets; this audit did not implement any item.

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
|---|---|---|---|---|---|---|
| P0 | AUDIT-002 | High | Workflow-dispatch input is interpolated into PowerShell source. | Runner, signing-secret, artifact, and release-write exposure. | M | High |
| P1 | AUDIT-001 | High | Mutable title equality rejects healthy capture admissions. | Nondeterministic capture failure for dynamic applications. | S | High |
| P1 | AUDIT-004 | Medium | Queued reorder repair lacks a final foreground-intent check. | Transient stale z-order repair and visual churn. | M | Medium |
| P1 | AUDIT-005 | Medium | Placement fallback restores geometry but not full placement state. | Rare minimized/maximized/normal-state degradation. | M | High |
| P2 | AUDIT-003 | Medium | Partial capture failure can occlude a disabled modal owner. | Confusing, apparently frozen multi-capture UX. | M | High |
| P2 | AUDIT-008 | Low | Persistence coalescing test has a timing-sensitive false negative. | Noisy CI and weaker release-gate signal. | S | Medium |
| P2 | AUDIT-007 | Low | Failure-suppression sets grow for the process lifetime. | Small long-session memory cost and recycled-HWND log suppression. | S | High |
| P3 | AUDIT-006 | Low | `PickColorCommand` is an inert unbound public placeholder. | Silent future UI incompleteness and maintenance noise. | XS | High |

## 20. Recommended Remediation Order

1. Harden workflow input transport and add adversarial static tests before the next production signing or publication attempt.
2. Resolve the title/identity contract and add the focused capture test; this is the most visible runtime correctness defect.
3. Close the queued foreground race with a dispatcher-controlled test and final mutation guard.
4. Define and test the `WINDOWPLACEMENT` fallback semantics, especially minimized/maximized cases.
5. Improve multi-capture failure presentation and test partial success.
6. Stabilize the persistence quality gate, then bound diagnostic suppression caches and clean inert/documentary surfaces.
7. Run supervised ValidationDriver shards and external Windows 10/mixed-DPI/browser qualification before declaring a production release fully qualified.

## 21. Positive Findings

- The Shepherd/no-reparent architecture is the correct safety boundary for arbitrary foreign Windows applications and removes the historical input, DPI, compositor, and teardown failure class.
- Identity verification is layered and generation-aware rather than relying on HWND alone. Both live mutation and recovery paths fail closed when evidence is missing.
- The journal is written before dangerous presentation mutation, uses durable/atomic storage, preserves corrupt or ambiguous evidence, and supports supervised pending recovery.
- Persistence has a real single-writer gate, monotonic latest-wins generations, atomic primary/backup handling, corrupt-vs-unreadable distinction, and preservation of future/unknown state.
- WinEvent processing is intentionally out-of-context, UI-dispatched, coalesced, and object-identity checked. The current race is a narrow final-intent gap, not a wholesale lifecycle design failure.
- Presentation batching, operation budgets, minimum-size caching, icon caching, and bounded logging show deliberate performance engineering.
- Diagnostics and support bundles have explicit redaction/privacy contracts, and the validation script verifies them.
- Release engineering has strong exact-SHA provenance, pinned actions, no persisted checkout credentials, trusted policy/source separation, independent final hash/signature checks, mandatory production signer identity, and no candidate execution in Stage B.
- Current deterministic validation is broad and green after rerun: 146 unit tests, 138 release-tooling tests, 20 OpenSpec items, native ABI checks, persistence/privacy diagnostics, and single-file publish smoke.

## 22. Audit Coverage / Confidence

| Area | Evidence depth | Confidence |
|---|---|---|
| Production C#/WPF architecture and lifecycle | Direct source reads across Models, Services, ViewModels, Views, App, native declarations, and manifest; symbol/context mapping through Repowise | High |
| Persistence and crash recovery | Direct source plus unit/self-tests and validation diagnostics | High |
| Release workflows and policy scripts | Direct YAML/PowerShell review, 138 release-tooling tests, adversarial policy fixtures | High for observed source; High for AUDIT-002 static risk |
| UI modal/z-order edge cases | Direct source and harness inspection; no interactive reproduction in this session | Medium to High |
| Native placement fallback | Direct source and native seams; no forced real `SetWindowPlacement` failure on a guest | High for code behavior; Medium for live frequency |
| Performance at unusually large desktop scale | Static structures and existing budgets; no dedicated benchmark | Medium |
| Real application/browser/DPI compatibility | Harness and documentation review only | Medium/Low until supervised external runs |
| Dead/stale code | Repowise candidates cross-checked with source, XAML, specs, and project boundaries | Medium to High |

The audit did not treat historical reports as current truth, did not count generated/cache files as production source, and did not convert unverified Repowise candidates into findings. Current Git authority was checked at the end of the evidence pass: `HEAD` and `origin/main` are both `a4e5a3d8c32e85c8da31c9b200aef9fa1dd9b904`, branch is `main`, and pre-existing dirty/untracked files remain preserved.

A second specialized audit should focus on supervised interactive behavior: Windows 10/11 compatibility, mixed-DPI monitor transitions, browser/Electron rendering, dynamic-title capture, rapid Alt-Tab/direct-click repair, modal partial-capture UX, and forced native placement-failure recovery. A separate release-security review should exercise the workflow inputs on an isolated runner after the input transport boundary is hardened.

## 23. Final Assessment

TabDock’s core runtime is well engineered and materially safer than the reparent-based design it replaced. The journal, identity gate, persistence writer, event lifecycle, split presentation, diagnostics, and release provenance controls are coherent and validated. No critical application defect was confirmed.

The audit is not a clean-release signoff because two high-severity boundaries remain: capture admission incorrectly treats a mutable title as identity, and release workflows still interpolate manual inputs into PowerShell source. The latter should be treated as a release-process blocker wherever signing secrets or `contents: write` are available. Three medium runtime findings are suitable for focused follow-up, and the low findings are maintenance/test-quality work.

- Overall health: high, conditionally ready.
- Largest systemic risk: untrusted workflow data crossing into executable release-policy code.
- Strongest subsystem: Shepherd identity/recovery/persistence safety boundary.
- Weakest subsystem: duplicated release-input handling, followed by edge-case UI/native restoration contracts.
- Highest-value improvement: make release inputs data-only and add adversarial workflow tests.
- Most urgent remediation: harden the Stage A/Stage B PowerShell input boundary before production signing or publication.
- Remaining uncertainty: unrun real-input desktop qualification, physical DPI/browser matrix, and live external signing-provider behavior.

No software fix, refactor, reformat, migration, or dependency change was performed. The report was written as the requested new deliverable, and existing user work was preserved.
