## 1. Guard implementation

- [x] 1.1 Add `private ContextMenu? _openTabContextMenu;` field to `ContainerWindow` (near `_activateReassertTimer`, `Views/ContainerWindow.xaml.cs:41`)
- [x] 1.2 In `TabsListBox_PreviewMouseRightButtonDown`'s dispatcher callback (line 494-499), assign `_openTabContextMenu = menu;` and subscribe `menu.Closed` to clear it (guard against stale references)
- [x] 1.3 Add `private bool IsContainerChromeInteractionActive()` checking `_openTabContextMenu is { IsOpen: true }`, `ColorContextMenu.IsOpen`, and `_viewModel.IsRenaming`
- [x] 1.4 In the `_activateReassertTimer.Tick` handler (line 207-212), add `&& !IsContainerChromeInteractionActive()` to the guard before calling `BringToFront`
- [x] 1.5 Clear `_openTabContextMenu` when the container closes (in the existing teardown near `_activateReassertTimer?.Stop()` at line 376)

## 2. Verification

- [x] 2.1 `dotnet build TabDock.csproj` with zero warnings
- [x] 2.2 Run `browser-lifecycle --guest chrome-normal` standalone — PASS on the first attempt with no retry
- [x] 2.3 Run the pig `popout` scenario — still PASS (tab context menu path unaffected)
- [x] 2.4 Confirm no new `SHEPHERD[bring-to-front]` lines appear between the tab right-click and the menu-item click in the scenario's log window (menu no longer eaten)
