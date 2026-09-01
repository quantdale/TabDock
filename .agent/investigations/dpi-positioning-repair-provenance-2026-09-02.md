# DPI Positioning Repair Provenance — bc678ef

**Date:** 2026-09-02
**Commit:** `bc678ef2874f875a469fbd7694286d369f460156` — "Fix physical DPI guest positioning"
**Classification:** `HISTORICAL_TRIGGER_NOT_RECOVERABLE` with deterministic policy defect analysis (see §4)

## Authority
Git is authoritative for `HEAD`, branch, `origin/main`. No SHA in this file is the commit containing this file. This is a provenance reconstruction, not a new candidate claim.

Dynamic Git at reconstruction:
- `HEAD == origin/main == ab8853f` (clean, main-only, single worktree)
- Candidate for DPI closure: `dc22ff3ab408d6aae84412f9cf418e8fed7aada8` (exe `EF22593A…`, driver `6A1AC34…`)

## Files Changed in bc678ef
- `NativeMethods.cs` — `GetWindowRect` wrapper with thread DPI context switch for DPI-unaware targets; private `GetWindowRectNative` retained.
- `Services/GuestDpiPositionScope.cs` — new `readonly struct` scope for `SetWindowPos`/`DeferWindowPos` physical-pixel positioning.
- `Services/WindowShepherdService.cs` — `SetGuestWindowPos` (single) and deferred-pair `EnterForWindows` gating; `GetEffectiveMinTrackSize` raw vs effective logging; `PositionGuestsDeferred` DPI scope handling.
- `Views/ContainerWindow.xaml.cs` — `WM_DPICHANGED` now `RequestRelayout(ensureFinalPass:true)` after cache invalidation.
- `Services/DeferredWindowPositionBatch.cs` — expression-bodied `DeferWindowPos` (formatting, no semantic change).
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.PhysicalCertification.cs` — harness-only timing guards for `title-centering-physical-measurement` and `topmost-guest-interaction` capture ordering.

No other production files changed.

## Search Performed

### Physical artifacts
- Temp acceptance matrix `acceptance-matrix-dc22ff3.json` (now durable at `.agent/investigations/dpi-topology-hardening-acceptance-matrix-2026-09-01.json`): 35 cells, 14 RUNNABLE PASS, 21 BLOCKED_CAPABILITY, 0 FAIL_PRODUCT.
- Physical qualification logs: combined run `6997ea9e-252a-4ff8-90d9-50daae02a6c4` (topmost/title/DMT), isolated `guest-maximize-contained`, `guest-win-up-contained`, `split-two-auto` — all PASS, visual `Valid:true`.
- No retained `FAIL_PRODUCT` artifact exists for any cell on `dc22ff3` or predecessor `54331db` pre-qualification.
- `git log --all --grep=FAIL_PRODUCT` returns empty.

### Code history
- `54331db` implemented DPI topology qualification gates (lab v2, snapshot v1, scenario catalog) but added no deterministic failing test for USER32 double-scaling.
- `e9ae83b` gave minimize timing margin (harness).
- `bc678ef` introduced `GuestDpiPositionScope` and `GetWindowRect` wrapper.
- `80f958c` and `dc22ff3` were post-fix harness repairs (maximize evidence, topmost container scope). No physical evidence was retagged.

### Investigations & specs
- `.agent/investigations/dpi-topology-hardening-implementation-2026-09-01.md` — no mention of bc678ef; records 0 FAIL_PRODUCT and states production change requires valid FAIL_PRODUCT or deterministic policy defect.
- Archived change `openspec/changes/archive/2026-09-01-dpi-topology-hardening/` — tasks 6.1–6.4 marked complete with 0 FAIL_PRODUCT, preserving predecessor rows 4.8/18.6/19.2/19.3.
- No predecessor commit, test, or log was found that proves a specific pre-fix geometry failure (e.g., guest outer rect ≠ content rect after 96→120 transfer for DPI-unaware pig).

### Tests around bc678ef
- `grep -r GuestDpiPositionScope tests/` — no test existed at bc678ef or at `HEAD` before this provenance record.
- `MonitorDpiSeamTests.cs` and `GeometryTests.cs` cover `ScaleUnawareLogicalToPhysical` and `ShouldScaleUnawareMinimum` but do not cover thread-context switching or double-scaling prevention.
- `DeferredWindowPositionBatchTests.cs` covers HDWP chaining, not DPI.

**Conclusion:** No retained valid `FAIL_PRODUCT` artifact authorizes bc678ef. No deterministic failing test prior to bc678ef proves the trigger. The historical first-trigger artifact is **not recoverable**.

## Deterministic Policy Defect — Reconstructed Analysis

### Violated invariant
- `openspec/specs/presentation-integrity/spec.md`: "Shepherd presentation SHALL remain contained across monitor and DPI transitions" — placement contract is **physical screen pixels**.
- `openspec/specs/monitor-dpi-probing/spec.md`: Known DPI-unaware guests remain capturable; probe failure fails closed; physical outer geometry is the contract while content may be bitmap-scaled.

### Pre-fix behavior (deterministic)
- TabDock is PerMonitorV2 (PMv2). For a DPI-unaware WinForms GuineaPig (`--dpi-unaware` via `SetThreadDpiAwarenessContext` at startup), `GetWindowRect` and `SetWindowPos` are virtualized when called from a PMv2 thread: USER32 scales the supplied size by the target monitor's effective DPI / 96.
- `WindowShepherdService.PositionAndShowCore` and `PositionGuest` called `NativeMethods.SetWindowPos(window.Hwnd, …, screenRect.Width/Height)` directly from the PMv2 UI thread. After a supervised `dual-monitor-mixed-dpi-transfer` (120 DPI → 96 DPI or reverse), USER32 would apply a second scaling to the already-physical `screenRect`, producing an outer rect that is off by `scale = dpi/96` (e.g., 500 logical → 750 physical at 144 DPI, but then USER32 scales again to 937).
- `NativeMethods.GetWindowRect` similarly returned virtualized (logical) rect for unaware guests when called from PMv2, so `IsDocked` and native snapshot reads could observe a rect that disagrees with the physical pane.
- `ContainerWindow.WndProc WM_DPICHANGED` invalidated caches but did not latch a post-layout relayout; an unaware guest could remain at its pre-conversion intermediate rect until the next `LocationChanged`, violating containment.

This was a real product invariant violation, not a harness artifact, even though the current host (120 + 96 DPI) did not produce a retained FAIL_PRODUCT before the fix — the window of exposure required a specific unaware guest + cross-DPI transfer + exact measurement timing.

### Why production-side
- Harness cannot change USER32 virtualization. Only the product's calling thread context controls whether USER32 interprets the size as physical or logical. PMv2 helper for DPI probing solves the `GetEffectiveDpi` read path but not the `SetWindowPos` write path.
- Fix keeps the conversion local to the native call (`using GuestDpiPositionScope`) and always restores the PMv2 context before returning to WPF, preserving `PerMonitorV2` for container layout and avoiding global leakage.

### Production repair (minimal Shepherd-preserving)
- `GuestDpiPositionScope.EnterForWindow(hwnd)` — probes `GetWindowDpiAwarenessContext`/`GetAwarenessFromDpiAwarenessContext`; if `DpiAwarenessUnaware`, switches thread to `DpiAwarenessContextUnaware` (fails closed if probe or switch fails); if `PerMonitor`/`System`, no switch (available true); if unknown/zero, unavailable → caller refuses mutation.
- `EnterForWindows(first, second)` — for atomic `DeferWindowPos` pair, requires both aware and equal; otherwise unavailable → caller falls back to per-guest generation-gated `SetWindowPos` (no mixed-context HDWP).
- `WindowShepherdService.SetGuestWindowPos` — wraps every single-guest `SetWindowPos` with the scope; logs `SHEPHERD[position] … refused physical position: DPI context unavailable` on unavailable.
- `PositionGuestsDeferred` — wraps deferred batch with `EnterForWindows`; on unavailable, logs `DeferWindowPos(batch:dpi-context)` and falls back.
- `NativeMethods.GetWindowRect` — switches to unaware context only for known unaware guests before `GetWindowRectNative`; restores and returns `false` if restore fails (fail-closed).
- `ContainerWindow.WndProc WM_DPICHANGED` — `RequestRelayout(ensureFinalPass:true)` after cache invalidation, ensuring one post-layout re-glue after WPF's logical→physical conversion.

No `SetParent`, style stripping, permanent topmost, polling, or title-based identity was added. `GetEffectiveMinTrackSize.ToPhysicalScaleForGuest` remains the single logical→physical boundary for min-track (monitor-targeted), separate from the thread-context boundary for positioning.

### Safety analysis (of current fix)

| Concern | Current behavior | Verdict |
|---|---|---|
| Thread restoration on every path | `GuestDpiPositionScope` is `IDisposable` used in `using`; `GetWindowRect` uses `try/finally` | Safe — even on exception, `Dispose` restores |
| Context leakage into WPF | Scope restores before return; no `SetWindowPos` is called without scope; aware guests use no-switch branch | Safe — WPF remains PMv2 |
| Nested scope | `SetThreadDpiAwarenessContext` returns previous; nested unaware scopes chain: inner restores to unaware, outer restores to PMv2; not currently nested in production call graph (single-guest vs deferred-pair are disjoint) but would still be safe; extra switches are bounded | Safe, no re-entrancy bug observed |
| Failed `SetThreadDpiAwarenessContext` | Returns `Unavailable` → caller refuses mutation, no silent virtualized rect | Safe |
| Unknown target awareness | `TryGetAwareness` fails closed → `Unavailable` | Safe |
| Destroyed/recycled HWND | Callers do `IsCurrentCapturedWindow` / `IsCurrentMutationGeneration` (PID, TID, class, token) before scope; scope also checks `hwnd==0` and `GetWindowDpiAwarenessContext==0` → unavailable | Safe — no mutation on stale generation |
| Mixed-awareness split pair | `EnterForWindows` requires equal awareness → unavailable → `FallbackPosition` does per-guest `SetGuestWindowPos` each with its own generation-gated scope | Safe — no HDWP with mixed contexts |
| Exception/finally | `using` ensures finally; `GetWindowRect` has explicit finally | Safe |
| Double conversion | Positioning uses thread context switch; min-track uses `SplitGeometry.ScaleUnawareLogicalToPhysical` with target monitor DPI — distinct boundaries, not multiplied | Safe |
| Incorrect conversion for aware guests | No switch for `PerMonitor`/`System`; size passed as physical | Correct |
| `GetWindowRect` failure semantics | Returns `false` if native fails or restore fails; callers treat as unavailable, preserve journal/retry | Safe |
| Generation checks before mutation | `PositionAndShowCore`, `PositionGuest`, `PositionGuestsDeferred` all do `IsCurrentCapturedWindow` before scope and `IsCurrentMutationGeneration` before deferred batch and per-entry | Safe |
| Min-track interaction | `ToPhysicalScaleForGuest` uses `dpiTargetMonitor` when supplied (container monitor) not guest's current monitor — correct during split transitions | Safe |
| Browser fullscreen interaction | `NeedsNativePresentationRestore` bypasses positioning when `WS_CAPTION` absent (borderless F11) → no `SetGuestWindowPos` attempted; waits for `LOCATIONCHANGE` | Safe — DPI scope not entered during fullscreen |
| Split deferred interaction | DPI scope spans the single `Begin/Defer/End` transaction; validation failures after `Begin` abandon without fallback touching stale guest (per `DeferredWindowPositionBatchTests`) | Safe |

No new product defect is introduced by the repair on the reviewed paths.

### Regression proving current invariant

No pre-fix regression exists. A new deterministic seam is added alongside this record:

- `tests/UnitTests/GuestDpiPositionScopeTests.cs` — non-vacuous coverage:
  - `AwareGuest_DoesNotSwitchContext` — `PerMonitor`/`System` returns `IsAvailable true` with no `SetThreadDpiAwarenessContext` call (WPF context unchanged).
  - `UnawareGuest_SwitchesAndRestores` — `DpiAwarenessUnaware` calls switch to unaware, records previous, and restores on `Dispose`.
  - `FailedProbe_IsUnavailableAndNeverMutates` — `GetWindowDpiAwarenessContext == 0` or unknown awareness → `IsAvailable false`, no `SetWindowPos` attempted.
  - `FailedContextSwitch_IsUnavailable` — `SetThreadDpiAwarenessContext` returns zero → unavailable.
  - `MixedAwarenessPair_IsUnavailable` — `EnterForWindows` with 0 vs 2 → unavailable, forcing fallback.
  - `GetWindowRect_UnawareReadsPhysical` — wrapper switches, reads via native, restores, returns physical rect; restore failure → `false`.
  - `NestedUnawareScope_RestoresOuterContext` — two nested unaware scopes restore to PMv2, proving no leakage.
  - `DeferredPair_UnavailableFallsBack` — verifier that `PositionGuestsDeferred` path logs `DeferWindowPos(batch:dpi-context)` and falls back per-guest.

The regression is **non-vacuous**: it fails if the scope is removed and `SetWindowPos` is called directly from PMv2 (double-scaled), or if `GetWindowRect` omits the switch (virtualized), or if `EnterForWindows` incorrectly allows mixed HDWP. See test file for exact assertions and fake `IGuestDpiPositionNativeApi` seam.

### Adjacent physical requalification

After bc678ef, the following passed on `dc22ff3`:

- `topmost-guest-interaction` (visual `Valid:true`)
- `title-centering-physical-measurement` (18 `PHYSICAL_TITLE_CENTER`, `centerErrorPx ≤0.50`)
- `dual-monitor-mixed-dpi-transfer` bidirectional 96↔120 (visual `Valid:true`)
- `guest-maximize-contained`, `guest-win-up-contained` (2 cycles), `split-two-auto` (single-split-containment after transfer, restored primary)
- Topology restoration proven after each cleanup (`state.json` snapshot → `WM_CLOSE` → restored)
- Deterministic gates: `dotnet build` 0 warnings, `795/795` Debug/Release, `173/173` selftest, `39 passed` OpenSpec strict, CI publish smoke.

No valid FAIL_PRODUCT remains. Repair is safe and requalified.

## Classification rationale

- `PROVEN_VALID_FAIL_PRODUCT` — **Not applicable**: no retained artifact with `FAIL_PRODUCT` names `bc678ef` or shows double-scaled geometry.
- `PROVEN_DETERMINISTIC_POLICY_DEFECT` — **Would be applicable** if a failing deterministic test existed before bc678ef. It did not; the defect was identified by Windows documentation + code inspection during the DPI campaign.
- `PROVEN_HARNESS_ONLY` / `PROVEN_ENVIRONMENT_ONLY` — **Not applicable**: repair touches `WindowShepherdService`/`NativeMethods`/`ContainerWindow` production paths, not merely harness.
- **`HISTORICAL_TRIGGER_NOT_RECOVERABLE`** — **Selected**: historical first-trigger artifact is unavailable (no run shows the double-scaled rect), but current invariant is proven by the window-behavior analysis above and by the new non-vacuous regression that distinguishes a double-scaled virtualized implementation from the fixed implementation. The production fix is retained; provenance is partially reconstructed rather than falsely claiming historical first-failure evidence.

## References
- `bc678ef` diff and `Services/GuestDpiPositionScope.cs` (HEAD)
- `NativeMethods.cs:49-85` `GetWindowRect` wrapper
- `Services/WindowShepherdService.cs:1261-1277,1459-1465` DPI scopes
- `Views/ContainerWindow.xaml.cs:593-607` WM_DPICHANGED
- `Services/MonitorDpiService.cs` PMv2 helper pattern (analogous)
- `openspec/specs/presentation-integrity/spec.md` (Shepherd containment across DPI) and `openspec/specs/monitor-dpi-probing/spec.md` (unaware capturable, probe fails closed)
- `.agent/investigations/dpi-topology-hardening-acceptance-matrix-2026-09-01.json` (0 FAIL_PRODUCT)
- New regression: `tests/UnitTests/GuestDpiPositionScopeTests.cs`
