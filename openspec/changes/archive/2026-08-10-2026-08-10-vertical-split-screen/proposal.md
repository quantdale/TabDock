# Split Screen — vertical LEFT/RIGHT split of exactly two captured guests

## Why

TabDock today shows exactly one captured guest at a time over its container content area. Users frequently want two related applications side by side (a browser and a terminal, a doc and a reference panel) without juggling two separate TabDock groups or windows. The Shepherd model (guests are independent top-level HWNDs that TabDock positions, z-orders, shows and hides) makes a two-pane split possible without reparenting: lay out both guests over the left/right halves of the content area and keep the container behind both.

## What Changes

- **Vertical 2-way split (LEFT/RIGHT only).** Exactly two captured guests visible simultaneously, one per pane. No top/bottom splits, no grids, no third simultaneous guest.
- **Context-menu workflow.** Right-clicking a tab initiates the split (that tab becomes the LEFT pane). With exactly two tabs, "Split screen" is a direct action that auto-selects the sole other tab as RIGHT. With three or more, "Split screen" is a submenu of candidate partner tabs (excluding the initiating tab). With fewer than two tabs the item is shown disabled. When a split is active the menu also offers "Exit split screen".
- **Split state.** Owned by `ContainerWindow`, keyed by `CapturedWindow` reference (not positional index), so the pair identity survives tab reordering. Runtime-only — not persisted (the pair is tied to live attached guests; on restart groups restore empty layout intent).
- **Geometry.** 50/50 split of the full content rect in physical pixels: `leftW = Width/2`, `rightW = Width - leftW` (odd widths give the right pane the extra pixel). No DPI conversion.
- **Lifecycle integration.** Split-aware behavior for: tab switching (clicking a split member keeps split; clicking a non-paired tab exits split), container minimize/restore (both hide/return), move/resize/maximize (both track), foreground/focus (both accept input; the container stays z-ordered below both), pop-out/drag-out/self-close/self-hide (split terminates cleanly, survivor becomes the single visible guest), and crash-journal safety (departing members hidden via the journal-before-hide path).
- **Direct activation reconciliation.** A desktop-level `EVENT_OBJECT_REORDER` callback-time foreground snapshot is correlated and routed through the existing Shepherd pairing policy when a direct guest activation is not reliably represented by `EVENT_SYSTEM_FOREGROUND`; no polling or competing z-order subsystem is introduced.
- **Z-order invariant.** During split the local stack is deterministic:
  foreground guest, partner guest, then container. The container is paired below
  the partner rather than pushed behind unrelated desktop windows, so neither
  pane covers the content host and unrelated windows are not displaced.

Explicitly out of scope: draggable split ratio/resizer (fixed 50/50), top/bottom or N-way splits, persistence of the split across restart, and any reparenting/guest-style mutation (the Shepherd prohibition is preserved).

## Capabilities

### New Capabilities
- `split-screen`: the vertical two-pane split behavior — which workflow surfaces it, how the state is represented and owned, the geometry, the z-order/visibility invariants, and the lifecycle semantics.

### Modified Capabilities
(none)

## Impact

- **Code**: `Services/WindowShepherdService.cs` (split positioning/foreground/pairing primitives), `Services/WinEventMonitor.cs` and `Services/GuestLifecycleService.cs` (bounded direct-activation reconciliation), `Views/ContainerWindow.xaml.cs` (split state, layout, enter/exit, context-menu, split-aware lifecycle), `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs` and `Scenarios.Torture.cs` (validation), `Scenarios.cs` (registration), `Uia.cs` (read-only `IsMenuItemEnabled`).
- **Docs**: `docs/ARCHITECTURE.md` (split-aware Shepherd notes), `docs/TESTING.md` (new scenario list), `docs/internal/split-screen-implementation-waypoint.md` (session waypoint).
- **No new dependencies, no new projects, no persisted-schema change, no reparenting.**
- **Relationship to existing specs**: extends the Shepherd model already described in `docs/ARCHITECTURE.md`; the `e2e-scenario-coverage` spec's rule that assertions only reference committed instrumentation applies to the new split scenarios (`SPLIT[enter]/[exit]/[replace]/[member-gone]` are emitted by committed source).
