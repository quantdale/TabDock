## Context

The fail-closed DPI guard added during the whole-codebase audit refused to capture
any window whose awareness context equals `DPI_AWARENESS_UNAWARE` whenever
`GetDpiForSystem() != 96`. This blocked a real user capturing a DPI-unaware
window at non-100% scaling. The premise — that an unaware guest "would be
stretched and misplaced no matter what rect we hand it" — is wrong for OUTER-rect
positioning: DPI virtualization of geometry APIs is keyed to the CALLING thread's
awareness, so a PerMonitorV2 caller's `SetWindowPos`/`GetWindowRect` operate in
physical pixels against any target HWND. A native experiment on this machine
proved it: `SetWindowPos(200,150,1440,900)` on a DPI-unaware top-level window from
a PMv2 thread returned `GetWindowRect (200,150,1440x900)` exactly; an unaware
caller of the same window saw `(160,120,1152x720)` = physical ÷ 1.25 (virtualized).

Two secondary defects were found and fixed with the same change:
- The scale gate used `GetDpiForSystem()` (primary monitor), misclassifying
  targets on differently-scaled secondary monitors.
- The DPI-unaware guest's native minimum-track size (`WM_GETMINMAXINFO`, answered
  by the target's own window proc in its logical 96-DPI space) was treated as
  physical pixels, which would under-constrain the size-containment hardening at
  non-100% scaling and re-open the Edge/Explorer pane-overflow defect.

## Goals / Non-Goals

**Goals:**
- Capture a KNOWN DPI-unaware guest normally at any display scaling, with
  physical-exact outer geometry (blurriness is the guest's own DWM-scaling, not a
  TabDock defect).
- Classify scaling from the target's own monitor, not the primary.
- Keep fail-closed handling for a probe that fails or returns an unknown context.
- Preserve the size-constraint / pane-overflow hardening for unaware guests via a
  single authoritative logical→physical conversion.
- Add deterministic self-test coverage and supervised regression scenarios.

**Non-Goals:**
- No reparenting, guest style/ex-style mutation, owner mutation, clipping, or
  `HWND_BOTTOM`. Shepherd preserved.
- No `SetThreadDpiAwarenessContext` in production code (TabDock is already
  PerMonitorV2; the only place a thread context is deliberately changed is the
  GuineaPig test launcher, which creates windows in different awareness classes).
- No change to the exact split partition math.

## Decisions

**D1 — Replace the blanket refusal with a per-target-monitor, known-class gate.**
Only a probe that fails or returns an unknown context is refused. A KNOWN
DPI-unaware guest is captured normally. Rationale: the physical-pixel Shepherd
contract already positions an unaware guest's OUTER rect exactly (proven
experimentally); blurriness is the guest's inherent DWM-scaling, identical to
standalone. *Alternative rejected:* continuing to refuse unaware guests (the bug
under test) or teaching TabDock to un-blur a virtualized guest (impossible from
outside its process).

**D2 — Probe the target monitor's effective DPI, not the primary.** Use
`MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` then
`GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI)` (shcore) from the PerMonitorV2 thread.
`GetDpiForWindow(hwnd)` on an unaware window returns 96 by definition, and
`GetDpiForWindow(monitorHandle)` returns 0 — both unusable; `GetDpiForMonitor` on
an HMONITOR returns the true effective DPI. This is correct on mixed-DPI setups.

**D3 — Centralize the logical→physical min-track conversion.** A DPI-unaware
guest answers `WM_GETMINMAXINFO` in its 96-DPI space; the physical minimum is
`ceil(logical × monitorDpi/96)`. `SplitGeometry.ScaleUnawareLogicalToPhysical`
owns the pure math (the single authoritative coordinate boundary), exercised by
`--selftest-geometry`; `WindowShepherdService.ToPhysicalScaleForGuest` decides
"is this guest unaware" and feeds it the target monitor's DPI. Awareness-aware
guests and 100% scaling are strict no-ops.

## Risks / Trade-offs

- [Blurry unaware guests] → Accepted: blurriness is the guest's standalone DWM
  scaling, not a geometry defect; the user explicitly captured it. The dialog
  never misrepresents blur as a failure.
- [Min-track under-estimate on a scaled monitor] → Prevented by D3: the conversion
  rounds UP and is applied centrally, so the pane-overflow containment stays
  correct for unaware guests.
- [Probe failure] → Fail-closed with a precise, actionable message; an unknown
  context is never admitted.

## Migration Plan

Backward-compatible behavior change: previously-refused unaware guests now capture
normally. No persisted-state change, no dependency change. Rollback is a revert of
the production source changes.

## Open Questions

- Live visual confirmation of an unaware guest docked at 125%/150% on real
  hardware (the native round-trip is proven; the supervised scenario and manual
  handoff cover the visual acceptance).