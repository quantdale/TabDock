## Context

The Shepherd `WM_ACTIVATE` handler in `Views/ContainerWindow.xaml.cs:191-213` defers a `BringToFront` by 120ms whenever the container is activated (`WA_ACTIVE`/`WA_CLICKACTIVE`) while a guest is active. `WindowShepherdService.BringToFront` (257-287) calls `SetForegroundWindow(guest)`, which moves the system foreground to the guest and deactivates the container.

WPF context menus close on owner deactivation, and the rename box loses focus on deactivation. So whenever the docked guest holds foreground (the normal state), any interaction with the container's own chrome — right-clicking a tab (Pop out / Close window), clicking the accent-color chip, double-clicking the title to rename — activates the container, opens the chrome UI, and then the 120ms reassert steals foreground back to the guest, closing that UI. The `browser-lifecycle` scenario reproduces this deterministically (5/5 fails); a real user gets a flash-and-close tab context menu.

`ColorContextMenu` is a named field on the window (`Views/ContainerWindow.xaml` line 62-73). The tab context menus are declared per-tab in the DataTemplate (lines 174-178) and opened manually in `TabsListBox_PreviewMouseRightButtonDown` (481-501). The rename state is exposed as `GroupViewModel.IsRenaming` (`ViewModels/GroupViewModel.cs:57`).

## Goals / Non-Goals

**Goals:**
- The deferred reassert must not steal foreground while the user is interacting with the container's own chrome (tab context menu, accent-color menu, rename box).
- Preserve all existing reassert behavior for genuine guest-foregrounding intents (alt-tab back, caption click, tab-switch click).
- Keep the fix local to `ContainerWindow`; no changes to `WindowShepherdService` or the ValidationDriver.

**Non-Goals:**
- Changing `BringToFront` semantics or the 120ms deferral itself.
- Addressing the harness `ForceForeground` environment flakiness documented in `KNOWN_ISSUES.md`.
- Fixing the pre-existing maximize-geometry trio (`maximize-repro`/`repeat-cycles`/`crossfeature`).

## Decisions

**D1: Suppress the reassert at timer-tick time via a chrome-interaction guard, not by changing the trigger.**

The guard is evaluated inside the existing tick handler, immediately before `BringToFront`:

```csharp
if (_shepherdActiveWindow == activeWindow
    && NativeMethods.IsWindowVisible(activeWindow.Hwnd)
    && !IsContainerChromeInteractionActive())
{
    _shepherd.BringToFront(activeWindow, hwnd, GetContentAreaScreenRect());
}
```

Rationale: the suppression must be a *state* decision at the moment the reassert would act, not a decision at `WM_ACTIVATE` time — the menu opens via `Dispatcher.BeginInvoke` (Input priority) after the activate message, and the default `DispatcherTimer` runs at Background priority, so the open menu is always observable by tick time. Checking at tick time also covers late-opening menus and the unbounded-delay case. Alternatives considered:
- *Check mouse button state in the WM_ACTIVATE handler*: wrong, the reassert is deferred precisely because the menu isn't open yet when `WM_ACTIVATE` arrives.
- *Change `BringToFront` to skip when foreground-locked*: doesn't distinguish chrome interactions from genuine alt-tab/click intents.

**D2: `IsContainerChromeInteractionActive()` checks three independent signals.**

```csharp
private bool IsContainerChromeInteractionActive()
{
    if (_openTabContextMenu is { IsOpen: true }) return true; // tab Pop out / Close window menu
    if (ColorContextMenu.IsOpen) return true;                 // accent-color menu
    if (_viewModel.IsRenaming) return true;                   // rename box focused
    return false;
}
```

- The tab menu reference is tracked in a new nullable field `_openTabContextMenu`, assigned inside the existing dispatcher callback in `TabsListBox_PreviewMouseRightButtonDown` (when it opens the menu) and cleared on the menu's `Closed` event. This is necessary because the tab menus are per-tab DataTemplate instances with no stable field; checking `IsOpen` on the tracked instance avoids scanning the visual tree at tick time (which would be fragile mid-layout).
- `ColorContextMenu.IsOpen` and `GroupViewModel.IsRenaming` are already available state — no new plumbing.
- Rationale for three signals instead of one generic "is any popup open": explicit, self-documenting, and matches the exact chrome surfaces; there is no cheap generic "any Popup open on this window" API in WPF.

**D3: Suppression only withholds the `SetForegroundWindow` steal — it never repositions or hides the guest.**

`BringToFront` is not called at all when the guard trips. The guest stays exactly where the Shepherd already placed it (it is kept positioned over the content area by the WinEvent positioning paths), so the suppression is purely a foreground-activation decision. This satisfies the "guest remains correctly placed" spec scenario without extra work.

## Risks / Trade-offs

- **Stale `_openTabContextMenu` reference** (menu destroyed without `Closed` firing) → Guard checks `IsOpen`; a destroyed menu reports `IsOpen == false`, so the stale reference is harmless, and the `Closed` handler clears it. Memory impact is nil (one menu instance).
- **A user opens the menu, dismisses it, then immediately alt-tabs intending the guest** → the menu's `Closed` clears the guard and the next activation reasserts normally. No path loses the reassert permanently.
- **Rename box focus retention during suppression** → rename commits via `LostFocus` (568-576); since the reassert no longer steals focus, the user must explicitly commit (Enter/Escape/click-away), which is the pre-existing intended flow.
- **Coverage gap** → The fix is exercised by the existing `browser-lifecycle` scenario (tab menu Pop out) once re-run; the color-menu and rename paths are covered by the spec scenarios and the manual checklist but have no automated scenario today. Acceptable — out of scope to add scenarios here.

## Migration Plan

Single-container-window change, no deploy/rollback concerns beyond reverting the edit. No data migration.

## Open Questions

None.
