## Why

The Shepherd `WM_ACTIVATE` reassert (`Views/ContainerWindow.xaml.cs:191-213`) defers a `BringToFront` for 120ms after any container activation, and `BringToFront` calls `SetForegroundWindow(guest)`. Whenever the docked guest holds foreground, clicking any piece of the container's own chrome (right-clicking a tab for **Pop out / Close window**, opening the accent-color menu, double-clicking the title to rename) activates the container and therefore schedules a reassert that steals foreground back to the guest ~120ms later — closing the just-opened context menu or dropping focus out of the rename box. The `browser-lifecycle` ValidationDriver scenario catches this as `FAIL: browser released by Pop out` (5/5 runs); a real user hits the same flash-and-close on the tab context menu.

## What Changes

- **Skip the deferred reassert while the user is interacting with the container's own chrome.** In the `WM_ACTIVATE` reassert timer's tick, before calling `BringToFront`, check whether any of the container's interactive chrome is active: an open tab context menu (`Pop out`/`Close window`), the open accent-color menu, or the rename box being edited. If so, do not call `BringToFront` — the activation was an interaction with container UI, not an intent to foreground the guest.
- Alt-tab back to the container, caption clicks, and tab-switch clicks (no open menu / no rename) still reassert the guest exactly as today.
- No new P/Invoke; no change to `WindowShepherdService.BringToFront` semantics.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `container-activation-timers`: the existing single-pending-timer reassert requirement gains the constraint that the reassert is suppressed while container chrome UI is open (tab context menu, color menu, rename), so an activation caused by a chrome interaction cannot close that UI.

## Impact

- `Views/ContainerWindow.xaml.cs` — the `WM_ACTIVATE` deferred-reassert tick gains a chrome-interaction guard; helpers to detect the open tab/color menus and rename state.
- No changes to `Services/WindowShepherdService.cs`, `GroupManager`, or the ValidationDriver.
- The `browser-lifecycle` scenario (unchanged) should pass on attempt 1 once the reassert stops closing the menu.
