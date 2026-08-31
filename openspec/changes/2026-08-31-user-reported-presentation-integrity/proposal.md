# User-reported presentation integrity — chrome occlusion, guest escape, layering, and caption alignment

## Why

A real user reported four related failures while using TabDock: (1) opening the
accent-color menu, entering workspace rename, using split controls, or opening
the "+" capture surface can make the captured window content disappear until
the interaction closes; (2) the workspace/group title appears visibly
off-center; (3) maximizing/fullscreening a captured application can make it
behave as though it escaped the group and can be moved to another monitor; and
(4) the group's visual top-layer behavior is unreliable and can sometimes be
covered.

These reports are product evidence, but the exact root causes are **not yet
proven**. Preliminary source review identified plausible interaction points in
the current Shepherd implementation: container-wide z-order elevation during
TabDock chrome interactions, broad chrome-active suppression of guest
re-pairing, incomplete observation of guest-initiated geometry/window-state
changes, and asymmetric caption layout. Those are hypotheses to investigate,
not conclusions to implement blindly.

## What Changes

- Add an evidence-first investigation/reproduction phase for each report before
  choosing an implementation.
- Define a presentation-integrity contract that separates visual stacking,
  keyboard/foreground ownership, native guest geometry, and TabDock-owned
  transient chrome.
- Ensure TabDock-owned menus/editors/capture/split surfaces remain usable
  without unintentionally blanking or covering the captured guest content.
- Ensure captured guest-initiated maximize/fullscreen/move/monitor transitions
  cannot silently become an independently roaming presentation while TabDock
  still considers the guest docked.
- Add bounded recovery for guest presentation drift, with no infinite z-order
  or geometry fight and no weakening of strong HWND identity checks.
- Make the workspace/group title geometrically centered against the container,
  with responsive collision/trimming behavior.
- Add deterministic policy coverage and supervised real-input regressions for
  the reported flows, including normal, split, multi-monitor, external-window,
  and topmost-window cases where capabilities permit.

## Capabilities

### New Capabilities

- `presentation-integrity`: visual/foreground/geometry invariants for
  Shepherded guests while TabDock chrome and guest-originated native state
  transitions occur.

### Modified Capabilities

- `ui-ux-hardening`: replace the assumption that raising/suppressing an entire
  container during chrome interaction is intrinsically safe; require
  interaction-specific stacking that preserves both usable TabDock chrome and
  visible guest presentation, plus true caption centering.

## Impact

Likely investigation/implementation surfaces include
`Views/ContainerWindow.xaml`, `Views/ContainerWindow.xaml.cs`,
`Views/ContainerWindow.Split.cs`, `Services/WindowShepherdService.cs`,
`Services/GuestLifecycleService.cs`, `Services/WinEventMonitor.cs`,
`Services/WinEventRoutingPolicy.cs`, `NativeMethods.cs`, and
`tests/ValidationDriver/`. The agent MUST revise this list if evidence points
elsewhere.

The Shepherd/no-reparent architecture, strong HWND identity rules, bounded
native mutation policy, privacy rules, and supervised-input safety boundary
remain mandatory. No permanent always-on-top policy, arbitrary foreign-window
reordering, guest restyling, or reparenting is authorized by this proposal.
