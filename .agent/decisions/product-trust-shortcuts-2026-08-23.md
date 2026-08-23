# Decision: Focus-independent tab shortcut pair

**Date:** 2026-08-23
**Status:** accepted

## Context

TabDock needs a global path because the captured guest is normally the
foreground window. The shortcut must not become a desktop-wide application
switcher, and common graphics/display-driver shortcuts make
`Ctrl+Alt+Left/Right` unsafe for this product.

## Decision

Register `Ctrl+Alt+PageUp` for previous and `Ctrl+Alt+PageDown` for next, both
with `MOD_NOREPEAT`. App resolves the foreground HWND to a current captured
guest or its owning container/chrome, then calls the same container-level
navigation operation used by local `Ctrl+Tab` / `Ctrl+Shift+Tab`. Unrelated,
stale/recycled, closed, or modal/chrome contexts are strict no-ops. Capture
hotkey availability remains a separate state from tab-navigation availability.

## Consequences

- Page navigation works while a guest owns focus without changing arbitrary
  desktop applications.
- Local Ctrl+Tab remains a fallback if either global registration fails.
- A paired registration is advertised only when both directions succeed.
- The choice avoids the known left/right graphics-driver collision surface.

## Evidence

- `Services/HotkeyService.cs`, `Services/GlobalTabNavigationPolicy.cs`, and
  `Views/ContainerWindow.xaml.cs` share the scoped path.
- `tests/UnitTests/GlobalTabNavigationPolicyTests.cs` and
  `HotkeyRegistrationPolicyTests` cover guest/container/unrelated/stale scope,
  registration failure, Page keys, and repeat suppression.
