## Why

During TabDock startup, a restored/opened group can end up hidden behind an
already-existing desktop window when the group's initial position overlaps that
window. The startup-restore path shows each restored (empty) container with a
bare `Window.Show()` and never issues an explicit z-order or activation claim,
so whether the container lands above or below an overlapping pre-existing
window depends entirely on the OS foreground grant at the moment of `Show()`.
When that grant is missing the container is silently buried, and nothing
repairs it (the WinEvent pipeline is not installed for empty groups and every
container z-order memory requires a live guest). The burial persists for the
session.

## What Changes

- Add a bounded, one-shot **startup z-order reconciliation**: after the
  restored containers are all shown and registered, raise each one to the top
  of the **normal** z-order band via `HWND_TOP` + `SWP_NOACTIVATE`, so a
  user-initiated launch always places TabDock's restored surface visibly above
  any overlapping unrelated window.
- The reconciliation is z-order-only — it issues **no foreground call**, so it
  cannot steal focus and it respects a later user activation of another app. It
  raises restored groups in the **normal** z-order band **without taking focus**.
  TabDock has no supported background/silent/auto-start launch path (single
  instance exits a second launch; no Run-key/tray/silent flag), so there is no
  such mode whose non-intrusiveness would need to be preserved.
- Preserve the Shepherd local-stack invariant (visible guest above container);
  the startup raise runs before any guest exists and is overwritten cleanly by
  the first capture's `PositionAndShow`.
- Add non-vacuous regression coverage (real-input, supervised) that asserts on
  actual native z-order / foreground, plus guards against a wrong fix that
  steals foreground.

## Capabilities

### New Capabilities
- `startup-group-visibility`: user-initiated TabDock startup must surface each
  restored group above any overlapping unrelated window; the group must not be
  accidentally buried; a later external foreground activation must be respected
  and TabDock must never re-steal foreground; no global topmost, no guest
  style/owner mutation, no foreground-stealing loops.

### Modified Capabilities
<!-- none: this is new behavior, not a change to an existing requirement -->

## Impact

- `App.xaml.cs` — `Application_Startup` restore path: call the new one-shot
  reconciliation after the restored-container loop.
- `WindowShepherdService` — reused `RaiseContainerForChrome` (the existing
  z-order authority primitive, `HWND_TOP` + `SWP_NOACTIVATE`); no new native
  pathway.
- `tests/ValidationDriver/.../Scenarios.StartupHide.cs` (new) —
  `startup-group-not-hidden-behind-existing-window`,
  `startup-does-not-steal-foreground-after-external-activation`,
  `startup-local-stack-above-unrelated-when-guest-present`.
- No guest HWND, style, owner, or geometry is changed; no `SetParent`,
  `WS_CHILD`, `HWND_BOTTOM`, permanent `Topmost`, or `SetForegroundWindow`
  loop is introduced.
