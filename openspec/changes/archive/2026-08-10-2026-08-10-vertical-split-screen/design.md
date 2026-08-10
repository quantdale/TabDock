# Design — vertical split screen

## State model (owner: `ContainerWindow`)

- `_splitLeft` / `_splitRight` — `CapturedWindow?` references (identity, not index). `IsSplitActive => _splitLeft != null && _splitRight != null`.
- `_splitForeground` — the member currently on top of the z-order (the one the user last focused); can differ from the active tab after a direct guest click.
- `_shepherdActiveWindow` remains the logical active/focused member; during split it is always one of the pair.
- Runtime-only: never persisted. The pair is tied to live attached guests; on restart groups restore empty layout intent (spec §25 default).

## Geometry

`GetContentAreaScreenRect()` (the `NativeHwndHost` client rect in physical pixels) stays the single source. `SplitRect` derives:
- LEFT = `{left, top, left + Width/2, bottom}`
- RIGHT = `{left + Width/2, top, right, bottom}`

Integer `Width/2`; odd widths give the right pane the extra pixel. No DPI conversion.

## Positioning / z-order

`LayoutSplitPanes()`:
1. Compute both pane rects; if both guests already cover their panes (1px epsilon) and are visible, skip guest repositioning but re-pair the container below the partner.
2. Otherwise position the foreground guest at `HWND_TOP`, position the partner immediately below it, then pair the container immediately below the partner.

Rationale: the complete three-window sequence is applied in one policy-owned
operation, including after activation raised the container. Result order:
`top, bottom, container`, without moving the container below unrelated windows.

Direct activation reconciliation is event-driven. `WinEventMonitor` observes
the desktop client's `EVENT_OBJECT_REORDER` notification, snapshots the current
foreground top-level HWND in the native callback, and validates that snapshot
when the UI dispatch runs. If it is a captured member, the event routes through
the same `ContainerWindow.PairZOrderBehindGuest` and Shepherd pairing policy as
the foreground path. `PairZOrderBehind` is a no-op when the container is already
the next visible window, so repair-generated reorder notifications cannot create
competing native writes or a polling loop.

## Context menu (code-behind construction on open)

In `TabsListBox_PreviewMouseRightButtonDown`, before `menu.IsOpen = true`, `ConfigureSplitMenuItems` rebuilds the split entries (idempotent, deduped by `Tag` prefix `SPLIT-`):
- tabs < 2 → `Split screen` disabled.
- tabs == 2 → direct action (auto-selects the sole other tab).
- tabs >= 3 → `Split screen` submenu of `TabViewModel`s (excluding the initiating tab), each with `Title`/`Icon`, click → `StartSplitFrom(left, candidate)`.
- split active → `Exit split screen`.

Building in code-behind avoids WPF ContextMenu `RelativeSource` binding traps (menus live outside the window's visual tree) and matches the existing `ColorMenuItem_Click` pattern.

## Tab-click semantics

- Clicking a split member → keep split; that member becomes active + `_splitForeground`; re-lay both panes.
- Clicking a non-paired tab → `ExitSplit(keepActive: thirdTab)`; third tab becomes the single full-width guest; former members hidden journal-safely.

## Exit split

`ExitSplit(keepActive)`: clear split state; hide the departing member(s) via `_shepherd.Hide` (journal-before-hide preserved); survivor = `keepActive` if a member, else the current active member if part of the pair, else the left member; `SetActiveTab(survivor)` + full-width layout. Never releases either guest.

## Member-removal cleanup

`ContainerWindow` subscribes to `_viewModel.Tabs.CollectionChanged`. Only `Remove` actions trigger `HandleSplitMemberRemoved` (reorder `Move` also carries `OldItems` and must not tear down the split — this was a found-and-fixed bug). On a split member leaving (pop-out, drag-out, self-close, self-hide, group close), clear split and promote the survivor.

## Lifecycle integration

- Minimize → hide both; restore → lay out both (`StateChanged`).
- `LocationChanged`/`SizeChanged`/`LayoutUpdated` → `RelayoutGuests` (split-aware).
- WM_ACTIVATE reassert → re-lay both + `SetForeground(active member)`.
- Split member foreground → re-pin container behind the other member (`PairZOrderBehindGuest`).
- `RestoreMinimizedWindow` / `NoteGuestMoveSize` accept either split member; drag-out measured against the member's own pane.
- `GuestLifecycleService.OnWindowHidden`: a split member hiding itself is guest-initiated teardown (both are visible in split); the container-minimized guard covers both split members.

## Persistence

Split is runtime-only. No schema change. (Spec §25 default accepted.)

## Logging

`SPLIT[enter]`, `SPLIT[exit]`, `SPLIT[replace]`, `SPLIT[member-gone]`. Hot-path `SHEPHERD[position]` unchanged (cheap; no `DescribeWindow`).

## Testing

15 pig-only, hermetic scenarios in `AllOrder` (listed in `tasks.md`). Pane membership asserted from the content-host rect halves within the existing tolerance (never `GetParent`). `SPLIT[*]` log assertions reference committed source.
