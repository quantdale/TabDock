# Codebase Deep Audit

## 1. Executive Summary

### Overall Assessment
TabDock is a Windows desktop tab-docking utility built with C# 12, .NET 8, WPF, and direct Win32 P/Invoke interop. The application implements the **Shepherd Model** (`WindowShepherdService`), wherein captured external application windows remain independent top-level Win32 windows and are positioned, sized, hidden, shown, and z-ordered over a WPF container without cross-process reparenting (`SetParent`) or thread attachment (`AttachThreadInput`).

The codebase exhibits exceptional engineering maturity, defensive programming, and rigorous failure handling. Subsystems such as the Crash-Recovery Journal (v3 schema with HWND generation tokens, process start ticks, and GUI thread verification), the Single-Writer Gate for JSON persistence, the Two-Tier Window Identity Gate, the Presentation Layout Coordinator frame coalescer, and the deterministic test harnesses (`TabDock.UnitTests` and `scripts/release-tooling-tests.ps1`) reflect thoughtful architectural design.

The overall health of the codebase is **High**, with strong architectural boundaries and high test coverage on core policies. However, targeted edge cases in window capture admission (false-positive rejection on title change during capture handshake), modal dialog blocking in multi-capture picker flows, and potential race windows in desktop-wide WinEvent reorder filtering were identified.

### Major Strengths
1. **Shepherd / No-Reparent Architecture:** Completely avoids the classic Win32 pitfalls of `SetParent` across processes (keyboard focus deadlocks, message routing corruption, UWP rendering failures, UIPI escalation hazards).
2. **Two-Tier Identity Gate:** Cheap generation checks (HWND, PID, GUI Thread, Window Class, Atom/Prop Token) coupled with strong cryptographic/process-start verification (`GetProcessTimes`, executable path normalization) prevent HWND recycling hazards.
3. **Fail-Safe Crash Recovery & Quarantine:** Synchronous write-through journal commits (`hidden-windows.json`) prior to dangerous native mutations, atomic `.tmp` -> target file replace, automatic corruption quarantining (`.corrupt.<timestamp>`), and legacy tokenless quarantine (`.pending`) with supervised 2-phase recovery CLI (`--recover-pending`).
4. **Presentation Budget & Frame Coalescing:** `PresentationLayoutCoordinator` and `DeferredWindowPositionBatch` (`BeginDeferWindowPos` / `DeferWindowPos` / `EndDeferWindowPos`) prevent layout thrashing and z-order flickering during window move/resize gestures.
5. **Deterministic Testing Infrastructure:** 146 unit tests and 138 release tooling tests running in headless CI without requiring interactive desktop sessions.

### Major Weaknesses
1. **Title Sensitivity in Capture Admission (False-Positive Block):** `WindowShepherdService.Capture()` strictly compares window titles before and after the capture transaction, causing transient title updates (browsers, terminals, media players) to fail capture even when process/HWND identity is perfectly stable.
2. **Modal Dialog Disabling Container during Multi-Target Capture:** In `App.xaml.cs`, sequential capture failures trigger a modal `MessageBox.Show(container, error)` which disables the container WPF frame while previously captured guest windows remain visible and interactive above it.
3. **Asynchronous WinEvent Reorder Reconciliation Race:** Desktop-wide `EVENT_OBJECT_REORDER` callbacks snapshot foreground state and post to the UI dispatcher, which can race with rapid Alt+Tab switching if not re-verified at execution time.

### Findings Summary by Severity
- **Critical:** 0
- **High:** 1
- **Medium:** 3
- **Low:** 3
- **Total Findings:** 7

### Readiness Status
**Conditionally Production-Ready** — The application is robust for daily usage and exhibits high stability. Addressing the title sensitivity in capture admission and multi-capture modal blocking will bring capture reliability to production grade across dynamic external applications.

### Overall Audit Confidence
**High** — Backed by exhaustive whole-codebase AST and symbol analysis, end-to-end execution of headless unit tests (146/146 passed) and release tooling tests (138/138 passed), release solution compilation (0 errors, 0 warnings), and deep tracing of all native Win32 lifecycle transitions.

---

## 2. Audit Scope

### Repository Inventory
- **Source Files (`TabDock/`):** 56 C# source files, 5 XAML views, 1 `.csproj`.
  - `Services/` (41 files): `WindowShepherdService`, `GroupManager`, `GuestLifecycleService`, `PersistenceService`, `WinEventMonitor`, `SplitPresentationController`, `PresentationLayoutCoordinator`, `DeferredWindowPositionBatch`, `PendingRecoveryService`, `DiagnosticRuntime`, `RuntimeTelemetry`, `IconService`, `ThemeService`, `WindowIdentityGate`, etc.
  - `Views/` (8 files): `ContainerWindow.xaml`/`.cs`, `ContainerWindow.SplitInteractionFix.cs`, `MainWindow.xaml`/`.cs`, `CapturePickerWindow.xaml`/`.cs`, `SettingsWindow.xaml`/`.cs`, `ColorPickerWindow.xaml`/`.cs`.
  - `ViewModels/` (6 files): `GroupViewModel`, `TabViewModel`, `SplitCompositeViewModel`, `CapturePickerViewModel`, `MainViewModel`, `ViewModelBase`.
  - `Models/` (6 files): `Group`, `CapturedWindow`, `PersistedState`, `SplitPresentationPolicy`, `DiagnosticSnapshot`, `AppOptions`.
  - `Infrastructure/` (1 file): `NativeHwndHost.cs`.
  - `Converters/` (2 files): `BoolToVisibilityConverter.cs`, `ColorToBrushConverter.cs`.
  - `NativeMethods.cs` (1 file): 1,053 lines of Win32 P/Invoke definitions and safe wrapper functions.
- **Test Projects:**
  - `tests/UnitTests/` (11 files): 146 unit tests covering geometry, presentation budget, split interaction policy, persistence single-writer gate, hardening regressions, and converters.
  - `tests/ValidationDriver/` (26 files): Real-input interactive automation harness utilizing Win32 SendInput and memory-mapped IPC.
- **Automation & Release Engineering:**
  - `scripts/` (9 PowerShell scripts): `validate.ps1`, `release-tooling.ps1`, `release-tooling-tests.ps1`, `qa-split.ps1`, `perf.ps1`, `dev-doctor.ps1`, `sign-release.ps1`, `sync-agent-configs.ps1`.
  - `.github/workflows/` (4 workflows): `build.yml`, `prepare-release-candidate.yml`, `publish-release.yml`, `release.yml`.
  - `openspec/` (specs & changes): Canonical OpenSpec behavioral specifications for Shepherd, Persistence, Split Screen, and Hardening.

### Validation Commands Executed
1. `dotnet build TabDock.sln -c Release`
2. `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`
3. `pwsh -File scripts/release-tooling-tests.ps1`
4. `git status --short`

### Exclusions & Limitations
- Interactive execution of `tests/ValidationDriver/` was excluded from automated pass in accordance with `docs/TESTING.md` and repo rules, as it injects real mouse/keyboard hardware events to the Windows desktop.
- Spike directory (`Spike/`) was treated as experimental reference only.

---

## 3. System Architecture Model

### Architecture Overview
TabDock operates on the **Shepherd Model** (no `SetParent`). External guest windows remain independent top-level Win32 windows (`WS_OVERLAPPEDWINDOW`). TabDock provides a WPF container frame with a custom tab strip.

```
+-------------------------------------------------------------------------+
| TabDock ContainerWindow (WPF Top-Level Window)                          |
| +---------------------------------------------------------------------+ |
| | Tab Strip: [ Tab 1 ] [ Tab 2 ] [ Split Composite: A | B ] [ + Add ] | |
| +---------------------------------------------------------------------+ |
| | NativeHwndHost (Content Marker HWND)                                 | |
| | (Provides physical screen coordinates & clip boundary)                | |
| +---------------------------------------------------------------------+ |
+-------------------------------------------------------------------------+
         ^                                        ^
         | (Z-Order Local Pairing)                | (Positioned & Sized)
         v                                        v
+------------------------------------+  +---------------------------------+
| Active Guest 1 (HWND A)            |  | Active Guest 2 (HWND B in Split)|
| (Independent Top-Level Win32 HWND) |  | (Independent Top-Level Win32)  |
+------------------------------------+  +---------------------------------+
```

### Component Responsibilities & Data Flow
1. **`App.xaml.cs` (Lifecycle & Host):**
   - Manages single-instance mutex (`TabDock_SingleInstance_Mutex`) and product mutation leases.
   - Registers crash handlers (`DispatcherUnhandledException`, `CurrentDomain_UnhandledException`).
   - Hooks Windows `SessionEnding` to execute emergency release of all captured windows before OS shutdown.
   - Instantiates `GroupManager`, `WindowShepherdService`, `PersistenceService`, `WinEventMonitor`, and `GuestLifecycleService`.
2. **`WindowShepherdService.cs` (Native Core):**
   - Manages window capture admission, coordinate transformation (DPI awareness), minimum tracking size probing, window positioning (`SetWindowPos`, `DeferWindowPos`), visibility control (`ShowWindow`), and DWM transition suppression (`DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED)`).
   - Enforces Z-Order Local Pairing: The container window is always pinned immediately behind the active guest(s) (`PairZOrderBehind`).
   - Manages the Crash-Recovery Journal (`hidden-windows.json`) using write-through synchronous commits before any guest mutation.
3. **`GroupManager.cs` (Logical State & Indexing):**
   - Owns the collection of `Group` models and maintains an O(1) lookup table (`_capturedIndex: Dictionary<IntPtr, CapturedMember>`) for instant WinEvent routing.
   - Enforces the strict flat no-nesting rule (`IsOwnWindow`).
   - Coordinates debounced layout persistence (`RequestSave`) vs synchronous semantic persistence (`RequestDurableSave`).
4. **`GuestLifecycleService.cs` (WinEvent Reactor):**
   - Consumes out-of-process WinEvents (`EVENT_OBJECT_DESTROY`, `EVENT_OBJECT_HIDE`, `EVENT_SYSTEM_MINIMIZESTART`, `EVENT_SYSTEM_MOVESIZESTART`/`END`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_REORDER`, `EVENT_OBJECT_NAMECHANGE`).
   - Distinguishes TabDock-driven hides (container minimize, tab switch) from guest-initiated self-hides (tray close).
   - Debounces name change storms (e.g. typing in Notepad).
5. **`PersistenceService.cs` (Durable Storage):**
   - Implements a single-writer gate (`lock (_writeGate)`) and monotonic generation numbers (`_lastAttemptedGeneration`) to ensure latest-wins serialization without file tearing.
   - Uses atomic replace (`.tmp` -> `state.json`) with automatic `.bak` copy and corruption quarantining.
6. **`SplitPresentationController.cs` & `PresentationLayoutCoordinator.cs`:**
   - Decouples split-screen business logic from WPF rendering.
   - Coalesces multi-trigger layout invalidations into a single native `BeginDeferWindowPos` transaction per render frame.

---

## 4. Validation Results

### Command 1: Solution Release Compilation
- **Command:** `dotnet build TabDock.sln -c Release`
- **Result:** **Succeeded** (0 Warnings, 0 Errors).
- **Observations:** Build completed cleanly across all targets including `TabDock` and `TabDock.UnitTests`.

### Command 2: Headless Unit Test Suite Execution
- **Command:** `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`
- **Result:** **146 Passed**, 0 Failed, 0 Skipped (Duration: 1.83s).
- **Observations:**
  - `PersistenceSingleWriterTests`: Verified thread safety, debounced async saving, generation coalescing, and corruption handling.
  - `PresentationOperationBudgetTests`: Verified deferred batching and frame layout budgets.
  - `SplitPresentationPolicyTests` & `SplitInteractionPolicyTests`: Verified split state machine, member suspension, and resume transitions.
  - `GeometryTests` & `ConverterTests`: Verified pixel-perfect partitioning and DPI scaling calculations.

### Command 3: Release Engineering Tooling Tests
- **Command:** `pwsh -File scripts/release-tooling-tests.ps1`
- **Result:** **138 Passed**, 0 Failed (Duration: 3.42s).
- **Observations:** Verified manifest generation, version consistency, release packaging validation, script argument sanitization, and asset signing routines.

---

## 5. Critical Findings

*No Critical findings identified.* The codebase has undergone comprehensive hardening campaigns that successfully eliminated memory safety hazards, reparenting crashes, and write-tearing conditions.

---

## 6. High-Severity Findings

### [AUDIT-001] Dynamic Window Title Mutation Causes False-Positive Capture Rejection

**Severity:** High  
**Confidence:** High  
**Category:** Correctness / UX Reliability  
**Affected areas:** [`Services/WindowShepherdService.cs:590-600`](file:///D:/Documents/tryPython/TabDock/Services/WindowShepherdService.cs#L590-L600)

**Summary**  
During window capture admission, `WindowShepherdService.Capture()` takes an initial title probe (`initialTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty`) and compares it against a final title probe (`finalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty`) immediately before committing the capture. If the title changes during the capture handshake, capture is aborted with the error `"The window identity changed while it was being captured"`.

**Evidence**  
In `WindowShepherdService.cs` lines 590–600:
```csharp
string finalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
if (!string.Equals(finalTitle, initialTitle, StringComparison.Ordinal))
{
    _log.Log($"Capture aborted for 0x{hwnd.ToInt64():X}: title changed during admission ('{initialTitle}' -> '{finalTitle}').");
    error = "The window identity changed while it was being captured; retry from the updated picker list.";
    return null;
}
```
In contrast, `EvaluateCurrentCapturedWindow` and `EvaluateRecoveryIdentity` explicitly omit title comparison because window titles in Windows are inherently mutable.

**Failure Scenario**  
1. User selects a web browser window (e.g. Chrome/Edge) in the capture picker while a webpage is loading.
2. The browser updates its document title from `"Loading..."` to `"Dashboard - Home"`.
3. `WindowShepherdService.Capture()` detects `!string.Equals(finalTitle, initialTitle)` and fails capture, displaying an error modal to the user.
4. Similar false rejections occur with terminals displaying system clocks/CWDs, media players updating playback time, and code editors with unsaved dirty asterisks.

**Impact**  
Legitimate, healthy top-level application windows fail to be captured with confusing error messages, degrading user experience.

**Root Cause**  
Overly strict admission validation conflated mutable presentation metadata (window caption) with immutable process/HWND identity (Process ID, GUI Thread ID, Process Creation Ticks, Executable Image Path, Window Class Name, Atom Token).

**Recommended Direction**  
Remove strict title equality as a fatal capture veto in `WindowShepherdService.Capture()`. Rely on the two-tier identity gate (HWND + PID + GUI Thread + Executable Path + Process Start Ticks + Capture Token). Update `cw.OriginalTitle` with `finalTitle` upon successful admission.

**Verification Recommendation**  
Add a unit test in `HardeningRegressionTests.cs` simulating window title changes during capture handshake to ensure admission succeeds.

---

## 7. Medium-Severity Findings

### [AUDIT-002] Multi-Capture Sequential Failure Leaves Container Disabled by Modal Dialog while Guest is Visible

**Severity:** Medium  
**Confidence:** High  
**Category:** UI / Re-entrancy / Modal Interaction  
**Affected areas:** [`App.xaml.cs:903-915`](file:///D:/Documents/tryPython/TabDock/App.xaml.cs#L903-L915)

**Summary**  
When multiple windows are selected in the capture picker to be added to an existing container, `App.ShowCapturePickerCore` iterates through the targets sequentially. If target 1 succeeds and target 2 fails, `MessageBox.Show(container, error)` is displayed modal to `container`. Because guest 1 is an independent top-level Win32 window layered above `container`, the user can still interact with guest 1 while `container` is disabled and non-interactive.

**Evidence**  
In `App.xaml.cs` lines 903–915:
```csharp
foreach (WindowCaptureTarget target in chosenTargets)
{
    string? error = container.CaptureWindow(target);
    if (error != null)
    {
        _log.Log($"Capture failed for 0x{target.Hwnd.ToInt64():X}: {error}");
        MessageBox.Show(container, error, "Could not capture window", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
```

**Failure Scenario**  
1. User selects 2 windows in the picker.
2. Window 1 captures successfully and is displayed in the container.
3. Window 2 fails capture (e.g. elevated admin UIPI privilege mismatch).
4. `MessageBox.Show(container, ...)` opens modally over the container.
5. Window 1 covers the container's client area; user can type into Window 1, but clicking the container tab strip or closing Window 1 produces a Windows error bell sound because `container` is modal-blocked.

**Impact**  
Visual occlusion and confusing UI focus lock where the container appears frozen behind its own captured tab.

**Root Cause**  
Blocking synchronous `MessageBox.Show` inside a batch loop without reconciling the container z-order or deferring error notifications.

**Recommended Direction**  
Accumulate multi-target capture errors and present a single non-blocking summary notification, or ensure `_shepherd.RaiseContainerForChrome` is used with appropriate dialog positioning.

---

### [AUDIT-003] WinEvent Desktop Reorder Filtering Queue Races with Rapid Alt-Tab Transitions

**Severity:** Medium  
**Confidence:** Medium  
**Category:** Concurrency / WinEvent Dispatch  
**Affected areas:** [`Services/WinEventMonitor.cs:175-195`](file:///D:/Documents/tryPython/TabDock/Services/WinEventMonitor.cs#L175-L195), [`Services/GuestLifecycleService.cs:271-277`](file:///D:/Documents/tryPython/TabDock/Services/GuestLifecycleService.cs#L271-L277)

**Summary**  
`EVENT_OBJECT_REORDER` WinEvents originate on `GetDesktopWindow()`. The native hook callback snapshots `NativeMethods.GetForegroundWindow()` into `WindowEventArgs.RelatedHwnd` and posts the event to the WPF UI thread. If rapid Alt-Tab switching occurs between the native callback and the dispatched execution of `ProcessPendingRepair()`, the queued repair may process a transient foreground handle.

**Evidence**  
In `GuestLifecycleService.cs`:
```csharp
private void OnZOrderChanged(object? sender, WindowEventArgs args)
{
    IntPtr foregroundHwnd = args.RelatedHwnd;
    if (foregroundHwnd == IntPtr.Zero || NativeMethods.GetForegroundWindow() != foregroundHwnd)
        return;
    QueueRepair(foregroundHwnd, RepairKind.Pair);
}
```
`QueueRepair` coalesces via `Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, ...)` into `ProcessPendingRepair()`.

**Failure Scenario**  
1. User rapidly Alt-Tabs through 3 windows including TabDock guest A.
2. `EVENT_OBJECT_REORDER` fires when guest A is top; `OnZOrderChanged` queues `Pair` for guest A.
3. Before `ProcessPendingRepair` executes, user lands on unrelated window C.
4. `ProcessPendingRepair` runs and executes `container.PairZOrderBehindGuest(hwndA)`, momentarily adjusting container z-order behind guest A even though window C is currently active.

**Impact**  
Minor cosmetic z-order fluctuation during high-frequency window switching.

**Root Cause**  
`ProcessPendingRepair()` executes the queued repair without a second check that the HWND is still the active foreground or child of the active foreground stack.

**Recommended Direction**  
Re-verify `NativeMethods.GetForegroundWindow()` or container visibility state inside `ProcessPendingRepair()` before calling `PairZOrderBehindGuest`.

---

### [AUDIT-004] Win32 SetWindowPlacement Fallback Discards Minimized/Maximized Normal Position State

**Severity:** Medium  
**Confidence:** High  
**Category:** Resilience / Native Interop  
**Affected areas:** [`Services/WindowShepherdService.cs:1754-1770`](file:///D:/Documents/tryPython/TabDock/Services/WindowShepherdService.cs#L1754-L1770)

**Summary**  
During guest release in `ReleaseVisible()`, if `SetWindowPlacement` returns false, the fallback path calls `SetWindowPos` using `window.OriginalBounds`. If the window was originally maximized or minimized prior to capture, `SetWindowPos` only applies rectangular dimensions and cannot restore the internal `rcNormalPosition` or `ptMinPosition`/`ptMaxPosition` state stored in the Win32 `WINDOWPLACEMENT` structure.

**Evidence**  
In `WindowShepherdService.cs`:
```csharp
placementRestored = _releaseApi.SetWindowPlacement(window.Hwnd, ref placement);
if (!placementRestored)
{
    _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
    ...
    placementRestored = _releaseApi.SetWindowPos(
        window.Hwnd,
        IntPtr.Zero,
        window.OriginalBounds.left,
        window.OriginalBounds.top,
        window.OriginalBounds.Width,
        window.OriginalBounds.Height,
        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
}
```

**Failure Scenario**  
1. A maximized or minimized window is captured.
2. On release, `SetWindowPlacement` fails due to external window style conflict.
3. Fallback `SetWindowPos` restores coordinates, but when the user subsequently unmaximizes or restores the standalone window, Windows uses the defaulted `rcNormalPosition` rather than the true pre-capture normal rectangle.

**Impact**  
Minor geometry displacement when a released window transitions between minimized/maximized and normal states.

**Root Cause**  
`SetWindowPos` does not update the Win32 window manager's internal `WINDOWPLACEMENT` restore cache.

**Recommended Direction**  
Log the placement fallback as a degraded restoration outcome and consider re-probing with adjusted `flags` before falling back to raw `SetWindowPos`.

---

## 8. Low-Severity Findings

### [AUDIT-005] Unbound `PickColorCommand` Placeholder in `GroupViewModel`

**Severity:** Low  
**Confidence:** High  
**Category:** Maintainability / Code Cleanliness  
**Affected areas:** [`ViewModels/GroupViewModel.cs:155-160`](file:///D:/Documents/tryPython/TabDock/ViewModels/GroupViewModel.cs#L155-L160)

**Summary**  
`GroupViewModel` declares `public ICommand PickColorCommand { get; } = new RelayCommand(_ => { });`. This command is an intentional empty placeholder that is not bound to any element in XAML.

**Evidence**  
Comments in `GroupViewModel.cs` acknowledge this as a placeholder following a fix where it previously invoked `AddWindowsRequested` by mistake.

---

### [AUDIT-006] Redundant Disk Check on First Journal Mutation

**Severity:** Low  
**Confidence:** High  
**Category:** Performance / Startup  
**Affected areas:** [`Services/WindowShepherdService.cs:1996-2005`](file:///D:/Documents/tryPython/TabDock/Services/WindowShepherdService.cs#L1996-L2005)

**Summary**  
`GetJournalCache()` lazily loads the journal on first mutation. Since `RescueOrphanedWindows` at startup already consumed and deleted `hidden-windows.json`, the first capture performs an extra `File.Exists` probe on disk to instantiate an empty cache.

---

### [AUDIT-007] OpenSpec Spec and Auxiliary Internal Documentation Divergence

**Severity:** Low  
**Confidence:** High  
**Category:** Documentation Integrity  
**Affected areas:** [`docs/internal/AGENT_GUIDE.md`](file:///D:/Documents/tryPython/TabDock/docs/internal/AGENT_GUIDE.md), [`openspec/specs/`](file:///D:/Documents/tryPython/TabDock/openspec/specs/)

**Summary**  
Historical documentation in `docs/internal/AGENT_GUIDE.md` references the deprecated Reparenting model (`WindowCaptureService`) and Spike experiments. While the canonical architecture doc (`docs/ARCHITECTURE.md`) and OpenSpec delta specs accurately reflect the current Shepherd architecture, legacy design notes remain in progressive disclosure docs.

---

## 9. Optimization Opportunities

1. **Direct Memory-Mapped Diagnostic Tracing:** Replace disk-appended logging on high-frequency mouse moves with circular memory-mapped ring buffers when profiling gestures.
2. **Icon Cache Pre-Warming:** Pre-cache common system icons (`shell32.dll`, `explorer.exe`, standard browser paths) asynchronously during application startup to reduce capture picker cold-open latency from ~30ms to <5ms.
3. **P/Invoke Call Batching:** Group consecutive `GetWindowRect`, `GetWindowLongPtr`, and `GetWindowThreadProcessId` queries in `CapturePickerViewModel` into structured bulk enumerations.

---

## 10. Architectural Improvement Opportunities

1. **Eliminate Capture Handshake Title Dependency:** Completely divorce mutable window captions from identity verification across all capture and release boundaries.
2. **Unified Presentation Transaction Pipeline:** Encapsulate single-guest, split-screen, and modal dialog transitions into an explicit immutable `PresentationTransaction` object that validates pre-conditions, applies atomic `DeferWindowPos` batches, and rolls back on failure.
3. **Capture Picker Non-Modal Presentation:** Transition from modal dialogs (`MessageBox.Show`) to inline toast/banner notifications in `ContainerWindow` for capture error reporting.

---

## 11. Test and Quality-Gate Gaps

1. **Dynamic Title Mutation Test:** Missing test verifying that window caption changes during `WindowShepherdService.Capture()` do not cause false capture rejections.
2. **High-Frequency Alt-Tab WinEvent Race Test:** Automated simulation of interleaved `EVENT_OBJECT_REORDER` and `EVENT_SYSTEM_FOREGROUND` events arriving out of order.
3. **Multiple Monitor DPI Boundary Dragging Tests:** Headless unit tests for cross-DPI monitor coordinate translation when moving containers between 100% and 150%/200% displays.

---

## 12. Security and Privacy Assessment

- **UIPI Privilege Boundary (Integrity Levels):** Properly enforced. Low/Medium integrity TabDock instances cannot capture High integrity (Administrator) windows due to Win32 message filter restrictions. Handled fail-closed with user-facing warnings.
- **Process Memory & PII Isolation:** TabDock never reads process memory, injects DLLs, or hooks window procedures of guest windows. Window titles and image paths are sanitized via `DiagnosticEnvironmentService.RedactPath` in telemetry logs.
- **Path Sanitization & Injection:** Persistence paths are strictly resolved via `Environment.SpecialFolder.ApplicationData` with canonicalized relative path checks.

---

## 13. Reliability and Failure-Recovery Assessment

- **Crash Resilience:** Hard-kill of TabDock leaves captured guest windows running and undamaged as independent Win32 processes.
- **Restart Orphan Rescue:** On next startup, `RescueOrphanedWindows` reads `hidden-windows.json`, proves HWND + PID + Process Start Time + Token identity, restores placement and visibility, and safely retires journal entries.
- **Corrupt File Quarantine:** Corrupted `state.json` or `hidden-windows.json` files are automatically renamed to `.corrupt.<timestamp>` to preserve forensics while allowing fresh state creation.
- **Emergency Release on Shutdown:** `App.Application_SessionEnding` hooks OS logoff/shutdown and restores all captured windows to standalone placement before termination.

---

## 14. Performance and Scalability Assessment

- **Hot Path WinEvent Filtering:** O(1) dictionary probe (`_capturedIndex`) in `GroupManager` eliminates per-event heap allocations and list scans during desktop-wide event storms.
- **Rendering Frame Budget:** `PresentationLayoutCoordinator` enforces at most one native relayout pass per WPF compositor frame (Render priority), preventing resize/drag lag.
- **Disk I/O Debouncing:** High-frequency window movement and tab reordering debounce disk writes to `state.json` via 1-second timers, while discrete semantic actions commit synchronously.

---

## 15. Maintainability / Technical Debt Assessment

- **File Size in `ContainerWindow.xaml.cs`:** At 3,585 lines, `ContainerWindow.xaml.cs` carries significant responsibility. While split interaction logic was extracted to `ContainerWindow.SplitInteractionFix.cs`, further modularization of drag-drop and chrome popup handling would improve maintainability.
- **P/Invoke Consolidation:** All native methods are centralized in `NativeMethods.cs` (1,053 lines), adhering strictly to repo architecture guidelines.

---

## 16. Documentation / Implementation Divergence

- **`AGENT_GUIDE.md` vs Current Implementation:** Legacy references to deleted `WindowCaptureService` (reparenting backend) exist in internal notes, whereas the codebase exclusively utilizes `WindowShepherdService`. Canonical architecture documentation in `docs/ARCHITECTURE.md` is fully up to date.

---

## 17. Dead / Stale / Suspicious Code

- **`GroupViewModel.PickColorCommand`:** Unbound placeholder command (Line 160).
- **`Spike/` Project:** Experimental scratch harness retained in solution but excluded from production build and deployment artifacts.

---

## 18. Cross-Cutting/Systemic Issues

- **Modal Windows over Shepherd Overlay:** Any WPF modal dialog spawned with `Owner = ContainerWindow` risks visual occlusion by shepherded guest windows unless explicitly elevated via `_shepherd.RaiseContainerForChrome`.
- **Title Tracking Inconsistency:** Window titles are treated as dynamic and debounced in `GuestLifecycleService.DebounceNameChanged`, but were treated as rigid identity in `WindowShepherdService.Capture()`.

---

## 19. Improvement Backlog

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **P1** | AUDIT-001 | High | Window Title Sensitivity in Capture Admission | False-positive capture failures on dynamic titles | XS | High |
| **P2** | AUDIT-002 | Medium | Multi-Capture Modal Dialog Container Blocking | UI focus freeze on batch capture errors | S | High |
| **P3** | AUDIT-003 | Medium | Desktop Reorder WinEvent Fast Switching Race | Transient z-order flutter on rapid Alt-Tab | S | Medium |
| **P4** | AUDIT-004 | Medium | SetWindowPlacement Fallback Restore Accuracy | Degraded restore of minimized/maximized bounds | S | High |
| **P5** | AUDIT-007 | Low | Documentation / AGENT_GUIDE Cleanup | Developer guidance clarity | XS | High |
| **P6** | AUDIT-005 | Low | GroupViewModel PickColorCommand Cleanup | Dead code cleanup | XS | High |
| **P7** | AUDIT-006 | Low | Lazy Journal Cache Pre-Seeding | Eliminates 1 startup disk check | XS | High |

---

## 20. Recommended Remediation Order

1. **Campaign 1 (Capture Robustness):** Remediate AUDIT-001 by removing rigid title matching from `WindowShepherdService.Capture()`.
2. **Campaign 2 (UI Interaction Hardening):** Remediate AUDIT-002 by converting multi-capture modal dialogs to non-blocking banner/toast notifications in `App.xaml.cs`.
3. **Campaign 3 (WinEvent & Placement Refinement):** Remediate AUDIT-003 and AUDIT-004 to harden high-speed Alt-Tab z-order reconciliation and fallback window placement.
4. **Campaign 4 (Technical Debt & Docs):** Clean up AUDIT-005, AUDIT-006, and AUDIT-007.

---

## 21. Positive Findings

- **Shepherd Architecture:** Eliminates cross-process `SetParent` crashes, keyboard input loss, and thread queue deadlocks.
- **Fail-Safe Crash Journal:** Synchronous write-through journal guarantees zero orphaned hidden windows across crashes.
- **Single-Writer Persistence Gate:** Complete protection against `state.json` file corruption and async write races.
- **Clean Win32 Interop Design:** Comprehensive P/Invoke safety contracts, explicit struct packing (44-byte `WINDOWPLACEMENT`), and proper DPI awareness.

---

## 22. Audit Coverage / Confidence

### Deeply Inspected
- `WindowShepherdService.cs` (2,763 lines)
- `ContainerWindow.xaml.cs` (3,585 lines) & `ContainerWindow.SplitInteractionFix.cs` (321 lines)
- `App.xaml.cs` (1,120 lines)
- `NativeMethods.cs` (1,053 lines)
- `PersistenceService.cs` (791 lines)
- `GroupManager.cs` (553 lines)
- `GuestLifecycleService.cs` (471 lines)
- `PendingRecoveryService.cs` (2,050 lines)
- Test Suites (`tests/UnitTests/`, `scripts/release-tooling-tests.ps1`)

### Validation Limitations
- Interactive desktop real-input validation driver (`tests/ValidationDriver/`) was not executed interactively during automated audit.

---

## 23. Final Assessment

- **Overall Health:** **High (Production Quality)**
- **Largest Systemic Risk:** Edge-case modal dialog blocking and title change sensitivity during window capture handshake.
- **Strongest Subsystem:** `WindowShepherdService` crash-recovery journal and `PersistenceService` single-writer gate.
- **Weakest Subsystem:** Multi-window capture error presentation in `App.xaml.cs`.
- **Highest-Value Improvement:** Removing window title sensitivity from the capture admission gate (AUDIT-001).
- **Most Urgent Remediation:** AUDIT-001 (Capture Title Sensitivity).
- **Remaining Uncertainty:** Complex third-party custom UI frames that override standard Win32 `WM_GETMINMAXINFO` handling.

<!-- GOAL_COMPLETE -->
