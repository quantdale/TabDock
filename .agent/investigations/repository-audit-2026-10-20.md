# Investigation: Current-tree repository audit

**Date:** 2026-10-20
**Status:** concluded — findings only; no fixes implemented

## Question and scope

What is actually wrong, fragile, unnecessarily slow, or insufficiently tested in this repository, independently of documentation claims?

Audited `main` at `eafe922d925b54001d35886f3807e1312cdb16cd` (equal to `origin/main`) **plus the existing uncommitted motion/UI changes**. Findings explicitly distinguish committed code from that work-in-progress. No application source, application tests, project configuration, or release workflow was edited by this audit. Existing work was preserved; only this report, the audit plan, and the checkpoint were added/updated. Nothing was committed or pushed.

The review covered capture/admission/identity and release boundaries, guest-event routing and split/presentation ownership, persistence, recovery discovery, diagnostics/privacy, picker/icon lifecycle, current WPF motion/templates, and build/test/release qualification. Source and fresh executable evidence were authoritative. Repowise was unavailable; the available review graph was incomplete for current C# ownership, so focused current-source reads and Git were used instead.

## Summary

**11 actionable defects: 3 high priority, 6 medium, 2 low.** The strongest results are reproduced against actual application methods or actual application XAML, not copied replacement implementations.

| ID | Priority | Finding | Affected tree |
| --- | --- | --- | --- |
| A01 | High | Malformed schema-version types throw; startup cleanup overwrites recoverable state/backup | Committed |
| A02 | High | APPDATA-only test isolation does not isolate the Windows application-data directory | Committed tooling |
| A03 | High | Motion property-descriptor subscriptions permanently retain discarded controls | Uncommitted motion |
| A04 | Medium | Returning to the last saved state loses to an older in-flight async save | Committed |
| A05 | Medium | In-process support export loses logical snapshots by reading WPF objects off-thread | Committed |
| A06 | Medium | Motion reads animated colors as normal colors, leaving selection/focus/hover colors stuck | Uncommitted motion |
| A07 | Medium | Motion installs local brush values that override existing style/binding state | Uncommitted motion |
| A08 | Medium | Capture-row highlighting observes ListBox selection, not checked capture selection | Uncommitted motion |
| A09 | Low | Button overlay lookup runs before the template children exist | Uncommitted motion |
| A10 | Low | Read-only discovery/doctor can delete stale recovery temp files | Committed |
| A11 | Medium | Capturing a window can block the UI waiting for a cosmetic icon worker | Committed |

## High-priority findings

### A01 — Wrong-type Version can destroy the useful backup during startup failure

**Locations:** `Services/PersistenceService.cs:485`, `:550-554`, `:269-307`; `Services/DiagnosticEnvironmentService.cs:482`; `App.xaml.cs:221-234`, `:364-371`; `Services/GroupManager.cs:526-556`.

`JsonElement.TryGetInt32` is not a type-independent try-parse: it throws `InvalidOperationException` when the JSON value is not a number. The primary-state reader calls it without checking `ValueKind` and catches only `JsonException` around parsing. Valid JSON containing `"Version":"oops"`, `null`, `true`, `{}`, or `[]` therefore escapes `Load()` instead of being classified corrupt. `_stateLoadFailed` is not set on that escape.

This is worse than an unavailable restore. Startup already constructed the manager; its catch calls `EmergencyReleaseAll`, which saves the empty group set. That save is allowed, copies the malformed primary over the valid backup, then installs an empty primary. Thus the valid backup is lost instead of restored. The exit path also requests saves.

**Actual probe:** all five wrong-type fixtures threw `InvalidOperationException`. With a valid, materialized backup alongside the malformed primary, invoking the actual startup-cleanup primitive produced:

```text
startup-style EmergencyReleaseAll after malformed Version preserves known-good backup=False primaryGroups=0
```

The analogous doctor reader also threw for a wrong-type Version; the CLI outer catch would return 1, rather than successfully reporting corruption. Its failure is not the data-loss path, but shares the unchecked-type error.

**Improvement:** classify wrong JSON types before numeric access; ensure every unsuccessful load leaves a safe save policy; cover the complete load-failure → cleanup-save → backup-preservation sequence, not just the parser in isolation.

### A02 — The allegedly isolated process commands still resolve the real user profile

**Locations:** `tests/ValidationDriver/TabDock.ValidationDriver/ResourceSoakRunner.cs:196-211`; `scripts/validate.ps1:87-105`; `Services/PersistenceService.cs:98-109`; `Services/WindowShepherdService.cs:292-294`; `Services/PendingRecoveryService.cs:184-187`.

The process soak sets child `APPDATA`/`LOCALAPPDATA` environment variables and starts the real application. The recovery smoke similarly sets only `APPDATA` before launching `--recover-pending`. The product resolves storage with Windows `Environment.GetFolderPath(SpecialFolder.ApplicationData)`, which does **not** follow that environment override on the tested Windows/.NET runtime.

**Actual child-process probe**, with APPDATA set to a disposable fixture name:

```text
APPDATA environment contains requested fixture=True
APPDATA environment honored by GetFolderPath=False
```

This establishes the isolation failure without starting the normal UI or touching a guest. The real resource-soak path can therefore load/save the user's groups and run normal journal rescue. The recovery smoke can inspect/clean real pending evidence; EOF is not a sufficient guard because `RunInteractive` performs completed-transaction reconciliation and sidecar cleanup before prompting (`PendingRecoveryService.cs:218-280`). It must not be treated as an empty-profile process test.

**Improvement:** provide a deliberate, verified application state-root boundary or use an actually separate Windows profile/environment. Assert the resolved root inside the child before any mutating service starts. Do not run the UI soak against a normal profile based only on these environment overrides.

**Audit safety note:** the standard validation script was run before discovering this flaw, and its recovery smoke reported the expected no-unresolved-pending result. No selection or YES confirmation was supplied. There was no pre-run snapshot proving that its real-profile cleanup changed nothing, so this audit does not retroactively certify that smoke's isolation. Further UI/process soaks were not run.

### A03 — Motion handlers root every discarded subscribed control

**Locations:** `Infrastructure/MotionProfile.cs:105`, `:177`, `:189`, `:230`, `:367`, `:387`, `:458`.

The motion behaviors call `DependencyPropertyDescriptor.AddValueChanged` with anonymous handlers and never call `RemoveValueChanged`. These descriptors retain their subscribed components. There is no retained handler identity for teardown, no unload cleanup, and no disable-path cleanup. Event subscriptions to a control's own events are not themselves the problem; the descriptor ownership is.

**Actual GC probe:** construct/discard 30 enabled borders and 30 actual `TdButton` template controls, let animation/dispatcher work settle, and force repeated collection/finalization:

```text
control borders alive=0/30
control buttons alive=0/30
discarded motion borders alive=30/30
discarded actual TdButton controls alive=30/30
```

The result repeats with `TABDOCK_REDUCE_MOTION=1`. Disabling animation duration does not remove the subscriptions. Reopening pickers, rebuilding split/tab items, or closing styled windows can accumulate retained controls and their referenced visual trees. The probe proves managed retention, not a measured GDI/USER leak or a particular process-memory growth rate.

**Improvement:** make attachment/detachment lifecycle-owned and idempotent, or use a non-rooting observation mechanism. Add real WPF weak-reference lifecycle tests; the synthetic resource gate cannot detect this defect.

## Other confirmed defects

### A04 — Unchanged-save optimization defeats latest-wins persistence

**Locations:** `Services/PersistenceService.cs:154-180`, `:247-248`, `:269-307`.

Both save entry points return before claiming a new generation when `BuildStateJson` matches `_lastSavedJson`. That comparison describes the last completed disk write, not the desired state or pending write.

**Deterministic actual-code repro:** save A; start async B and hold it at the injected write boundary; request A again; release B. The A request is skipped, so B wins. Both a final synchronous Save(A) and a final SaveAsync(A) leave **B** on disk:

```text
persistence A -> async B -> sync A: expected=A actual=B
persistence A -> async B -> async A: expected=A actual=B
```

This can persist a stale active tab or geometry after a user returns to the previous state, including a final safety-boundary save during an in-flight write. The generation check cannot help when the latest request never reaches it.

**Improvement:** make coalescing/unchanged detection aware of in-flight writes and the latest requested snapshot. Add an A→B→A interleaving regression, not just unique-name generation hammers.

### A05 — Support ZIP omits the live logical state it is intended to diagnose

**Locations:** `Services/DiagnosticReportService.cs:45`, `:211-224`; `App.xaml.cs:238-252`, `:267`; `Views/ContainerWindow.xaml.cs:246-264`.

`ExportBundleAsync` runs `CreateReport` on a worker. That method invokes the in-process logical provider, which calls `ContainerWindow.CreateDiagnosticSnapshot`. Reading `WindowState` and other dispatcher-owned WPF state on that worker throws. App catches each failure and returns a shortened/empty list, allowing export to succeed without the missing container's active/split/expected-pane evidence.

**Actual probe:** an actual never-shown `ContainerWindow` snapshots successfully on its owning UI thread and throws the WPF cross-thread `InvalidOperationException` from the export-style worker. Using the actual exporter with an App-equivalent provider catch produces a real ZIP with `logical-snapshot.json` count **0**, expected **1**.

**Improvement:** collect the immutable logical snapshot on its owning dispatcher before offloading report/ZIP work. Avoid fixing this with a blocking dispatcher round-trip while the hotkey caller itself waits synchronously.

**Rejected suspicion:** no async-context deadlock was reproduced in the current exporter; it uses `ConfigureAwait(false)` at line 271. The synchronous hotkey call still waits for export work, but an indefinite synchronization-context deadlock is not a finding.

### A06 — Motion loses the original normal-state colors

**Locations:** `Infrastructure/MotionProfile.cs:330-351`.

`Update()` falls back to `normalBg.Color` / `normalBorder.Color`, but those are the same brush properties being animated. Once a selection animation fills to its endpoint, reading `.Color` returns that selected animated value, not the original normal color. Deselecting animates back to the selected color again.

**Actual probe:** normal colors `#FF202020/#FF505050`, selected colors `#FF2864B4/#FFFF0000`; after selecting, settling, deselecting, and settling, actual colors remain `#FF2864B4/#FFFF0000`. Reduced motion reproduces it too.

**Improvement:** keep immutable normal-state tokens/base values separate from the animated brushes; test complete round trips, including focus/hover, not only state entry.

### A07 — Animated brush setup overrides the remaining style-state authority

**Locations:** `Infrastructure/MotionProfile.cs:310-326`; `Views/ContainerWindow.xaml:330-347`, `:370-387`; `Views/CapturePickerWindow.xaml:148-157`; `Views/ContainerWindow.xaml:489-499`.

On Loaded, motion assigns `border.Background` and `border.BorderBrush` as local values. Those take precedence over style setters and DataTriggers (and replace a binding installed on the same property). Existing split-half `Left.IsActive` / `Right.IsActive` background triggers can no longer update the brush. The picker focus-border DataTriggers are also defeated; those borders do not supply a replacement `FocusBorder` token.

**Actual probe:** a minimal instance of the same remaining split-style DataTrigger pattern sets IsActive after motion attaches; expected blue, actual background remains transparent. The cause is local-value precedence, independent of animation timing.

**Improvement:** preserve one state authority and binding/style precedence instead of combining local brush assignment with still-active style triggers. Exercise selected/focus/active changes after Loaded and after recycling.

### A08 — Checked capture rows no longer drive their selected highlight

**Locations:** `Infrastructure/MotionProfile.cs:355-360`, `:382-387`; `Views/CapturePickerWindow.xaml:143-167`; `Views/ContainerWindow.xaml:484-507`.

Capture checkboxes bind to `WindowInfo.IsSelected`. The new motion behavior observes the ancestor `ListBoxItem.IsSelected`, a different property; the templates do not bind these states together. ListBox selection represents a single list row, while capture selection can contain many checked windows. The removed DataTrigger used the model property.

**Actual probe:** instantiate the actual standalone picker DataTemplate with a real WindowInfo, set its capture-selection property, and settle bindings. `modelSelected=True`, `rowSelected=False`; expected selected background `#FF18233F`, actual remains the normal background. This affects feedback, not which targets the capture command submits.

**Improvement:** observe the capture selection source for picker rows, while retaining normal ListBox selection semantics for workspace tabs.

### A09 — Buttons attach before their hover/pressed overlays are instantiated

**Locations:** `Infrastructure/MotionProfile.cs:75-105`; `App.xaml:99-118`.

During template instantiation the root Border already has a TemplatedParent, so attachment happens immediately. Its child overlays have not been built yet; `FindVisualChild` caches null. The root is marked wired, preventing a later retry.

**Actual probe:** apply the actual TdButton style, realize its template, raise Loaded, and raise the managed MouseEnter event without sending desktop input. Both named overlay elements now exist and the wired tag is set, but hover opacity remains **0**, not 1. The old style hover triggers were removed, so there is no fallback overlay feedback. This is reproduced in normal and reduced-motion modes.

**Improvement:** resolve named template parts only after template realization and make template replacement/unload safe as part of A03's lifecycle work.

### A10 — Read-only diagnostics do perform file deletion

**Locations:** `Services/PendingRecoveryService.cs:65-85`, `:142`, `:1530-1541`; `Services/DiagnosticEnvironmentService.cs:179`.

Discover defaults `cleanupTemporaryFiles=true`. Both FormatDiscovery (which prints `readOnly: true`) and doctor persistence inspection take that default. They can delete matching pending-prefix `.tmp` files older than 24 hours without the product mutation lease. Launcher attention explicitly disables this cleanup, so the CLI paths are inconsistent with that read-only boundary.

**Actual disposable-directory probe:** create an aged `hidden-windows.json.pending.fixture.tmp`, call actual FormatDiscovery, and observe `existsAfter=False` while the returned report says `readOnly: true`.

This is a contract/safety-boundary defect, not evidence that an authoritative pending journal was deleted: the current sweep targets temp fragments only.

**Improvement:** keep discovery non-mutating; perform hygiene only on an explicitly mutating, coordinated path.

### A11 — Cosmetic icon extraction can stall the UI capture path

**Locations:** `Services/IconService.cs:66-85`; `ViewModels/GroupViewModel.cs:168`, `:310`.

The picker resolves icons in the background, but `GroupViewModel.AddCapturedWindow` calls the synchronous icon API on the UI thread. If the picker worker is already extracting that executable's icon, the call waits for it for up to **5 seconds**, before inserting the captured member. Existing-member VM construction has the same synchronous API use.

**Actual probe:** hold a single in-flight extractor with a controlled 1.2-second delay, then call actual AddCapturedWindow on the UI thread. It takes **1216 ms** (repeat under reduced motion: **1212 ms**), waiting for cosmetic work. No native capture, real guest, or desktop input is involved in the fixture.

**Improvement:** use completed cache entries in UI operations and deliver uncached icons asynchronously with lifetime/generation checks. A cosmetic cache miss should not block membership/presentation work.

## Validation performed

Fresh results on Windows 11 build 26300 / .NET SDK 8.0.426, runtime 8.0.32:

| Check | Result |
| --- | --- |
| `dotnet build TabDock.sln` | PASS, 0 warnings/errors |
| `dotnet test TabDock.sln --no-build` | PASS, 847/847 |
| `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci -Publish` | PASS |
| Release application, driver, GuineaPig, Performance builds | PASS, 0 warnings/errors |
| Release xUnit | PASS, 847/847 |
| Audited NuGet restore/vulnerability report | PASS, no vulnerable packages reported |
| Native WINDOWPLACEMENT ABI self-test | PASS on current Windows build |
| CLI/version/doctor/bundle privacy/publish smoke | PASS for their supplied ordinary fixtures; isolation caveat A02 applies |
| OpenSpec validation | PASS, 38/38; this checks artifacts, not runtime correctness |
| Release-tooling adversarial regression suite | PASS, 179/179 |
| ValidationDriver `--selftest all --configuration Release --rid none` | PASS, 173/173 |
| Headless resource/visual gates | PASS, explicitly synthetic |
| Performance harness `--scenario all` | Completed; observational, not a regression baseline |
| Isolated actual-code audit probes, normal and reduced motion | 19 contract failures reproduced per run, covering 10 defects; A02 has a separate path-resolution probe |
| Product/test/tooling source hash comparison during final verification | PASS, all recorded hashes unchanged |
| `git diff --check` | PASS; only pre-existing LF/CRLF normalization warnings |

The temporary probe project references the built application DLL directly and uses the existing internal test seams under the friend assembly name. It lives outside the repository, under the operating-system temp directory; no regression tests or fixes were added to the product projects. Its nonzero exit is intentional: it asserts the desired contracts and goes red on the defects above.

Local ignored evidence is under `artifacts/repository-audit-2026-10-20/`: `validation.log`, `release-tooling.log`, `deterministic.log`, `performance.log` / `.json`, `probes.log`, `probes-reduced.log`, `isolation.log`, unit TRX, resource/deterministic JSON, source hashes, and `probe-source.zip` (the temporary probe project/source, not a product patch). Do not commit these machine-local logs or generated artifacts.

To rerun the retained local probe after rebuilding the Release application: `dotnet run --project "$env:TEMP/TabDock-repository-audit-20261020/AuditProbe.csproj" -c Release` in PowerShell. Its local DLL reference binds the audited checkout; adjust that reference if relocating the source. Exit 1 with the reported failed assertions is the expected pre-fix result.

## Test gaps and optimization opportunities

- Add runtime WPF template/state/GC coverage. `FrontendDesignContractTests.cs:151-160` checks attached-property text markers, not working hover, checked selection, state exit, binding precedence, or collectability. A green marker test cannot validate the new motion engine.
- Add adversarial JSON root-field types and the full cleanup-save path. Existing malformed-record coverage misses A01's wrong-type root Version and backup destruction.
- Add deterministic non-monotonic persistence schedules such as A→B→A. Unique-state concurrent-write hammers miss A04.
- Add an in-process export test with a dispatcher-owned logical provider. Existing headless ZIP/privacy tests can pass with no UI provider and therefore miss A05.
- The headless resource snapshots are prescribed synthetic counters (`ResourceSoakRunner.cs:261-283`); picker-icon lifecycle profiles use small model dictionaries rather than actual styled WPF controls. They are useful analyzer/policy tests, not proof that the application has no leaks. Add a safe real managed-control lifecycle layer before relying on soak results.
- Remove UI waits for icons and avoid synchronous hotkey waiting for ZIP/hash/report work. Only the icon wait was stress-measured here; no arbitrary production-speedup percentage is claimed.
- Consider a lifetime/size policy for the process-wide executable-icon cache (`IconService.cs:39-40`) and responding to Windows reduced-motion changes after startup (`MotionProfile.cs:18-31`). These are source-backed improvement opportunities, not separately demonstrated user-facing failures.

## Rejected claims and qualification limits

No build break, ordinary xUnit failure, reported dependency vulnerability, or failed release-control regression was found. The suspected export async-context deadlock was disproved, not reported as a bug. No claim is made that every possible defect has been found.

No synthesized mouse/keyboard input, normal-app UI resource soak, supervised crash/recovery scenario, display reconfiguration, Windows shutdown/logoff test, or physical mixed-DPI/Windows 10 qualification was performed. The audit's own WPF objects/windows were never shown. Native diagnostic probes observe existing windows and create only their own hidden helpers. Native replay/unit/synthetic PASS is not physical interaction or release certification. The APPDATA-isolation defect is established by path resolution plus actual call sites, not by risking a user's windows to demonstrate damage.

## Conclusion and recommended order

The repository builds cleanly and has substantial deterministic coverage, but that does not establish correctness of the current tree. First address **A01** (recoverable-state loss), **A02** (unsafe validation isolation), and **A03** (motion lifetime leak). Then repair the persistence/diagnostics boundaries and motion state authority, add the runtime regressions, and only afterwards perform supervised native interaction qualification. All implementation remains explicitly deferred to the user.

**Plan:** `.agent/plans/repository-audit-2026-10-20.md`.
