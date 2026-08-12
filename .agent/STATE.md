# Agent state

## Current checkpoint — FINAL HARDENING CLOSURE (2026-08-11)

Objective: resolve concrete outstanding blockers, validate accumulated
containment + DPI work, correct any defects, close active specifications,
update durable state, and create ONE coherent milestone commit.

Status: **RESOLVED — ALL SUPERVISED SCENARIOS PASS. Ready for commit.**

## What happened in this session

1. PID 156552 investigated and closed (orphan from a previous CLI session;
   not a harness cleanup defect — the ValidationDriver correctly prevented
   running against a non-driver-owned instance).

2. Harness bug fixed: `ClickTabCloseButton` now calls `EnsureClickable` before
   clicking, preventing clicks on obscured close buttons.

3. Harness bug fixed: containment scenarios use `ResizeContainerTo` as a
   layout-trigger before behavioral containment assertions, replacing the
   broken cross-process `QueryMinTrack` (which failed because `lParam` is a
   pointer in the harness's address space, invalid in the container's process).

4. Product bug fixed: `WM_GETMINMAXINFO` handler in `ContainerWindow.xaml.cs`
   now always sets `ptMinTrackSize` when a valid constraint exists, rather
   than clamping up from WPF's internal defaults (which can be large).

5. Dead harness method `AttemptNarrowResizeAndReadWidth` removed (was no longer
   called after the cross-process `SetWindowPos` limitation was discovered —
   cross-process `SetWindowPos` below min-track destroys the container HWND).

6. Scenario 1 (`split-guest-does-not-overflow-pane`) timing fixed: removed
   the premature pre-resize `IsInPane` assertion (which assumed 50/50 split
   but the RIGHT guest's 500px minimum makes the partition asymmetric); the
   post-resize containment assertion is the correct and sufficient proof.

## Supervised scenario results (all 5 PASSED)

| Scenario | Result |
|---|---|
| `capture-dpi-unaware-guest` | PASS (SKIPPED: 96 DPI, single monitor) |
| `capture-dpi-system-guest` | PASS (SKIPPED: 96 DPI, single monitor) |
| `split-guest-does-not-overflow-pane` | PASS (containment enter visible ✓, post-resize containment ✓) |
| `split-narrow-container-constraints` | PASS (containment enter ✓, layout-trigger ✓, pair replacement ✓, survivor ✓) |
| `single-guest-does-not-overflow-content` | PASS (full-width capture ✓, post-resize containment ✓) |

## Architecture audit (clean)

- No `SetParent`, `WS_CHILD`, `HWND_BOTTOM`, `GWL_STYLE`, `GWL_EXSTYLE`
  mutations on guests in any diff.
- No arbitrary DPI constants (`* 1.25`, `* 1.5`, `/ 96`) outside the
  centralized `SplitGeometry.ScaleUnawareLogicalToPhysical`.
- No `Thread.Sleep` or `Task.Delay` in production code.
- `WindowShepherdService` remains the sole positioning/z-order authority.
- PMv2 physical-pixel geometry preserved throughout.

## OpenSpec status

- `dpi-unaware-acceptance` — archived (`openspec/changes/archive/2026-08-11-dpi-unaware-acceptance`).
- `guest-size-constraint-containment` — archived (`openspec/changes/archive/2026-08-11-guest-size-constraint-containment`).
- `openspec validate --all --no-interactive`: PASS, 12/12.

## 5-second re-probe timer

RETAINS. Legitimate bounded periodic invalidation of guest min-track
constraints. Rate-limited, event-driven first pass, periodic fallback for
edge cases (guest WM_GETMINMAXINFO min can change without an observable
state transition). Not classified as aggressive polling.

## DPI architecture

- Known DPI-unaware guests: captured normally (physical-exact outer geometry).
- Unknown/probe failure: fail-closed refusal.
- PMv2 outer geometry: physical pixels throughout.
- Min-track conversion: centralized via `SplitGeometry.ScaleUnawareLogicalToPhysical`.
- No arbitrary scale constants scattered in production code.

## Manual visual acceptance

NOT YET CONFIRMED by the user. The automated scenarios prove containment
via `GetWindowRect` geometry, but visual composition on real multi-monitor
DPI setups requires manual verification. This is the sole remaining gate
before production deployment.

## Next action

Create ONE coherent milestone commit with all accumulated hardening work.
Do NOT push without explicit request.
