# startup-group-visibility Specification

## Purpose
TBD - created by archiving change startup-group-visibility. Update Purpose after archive.
## Requirements
### Requirement: User-initiated startup surfaces the restored group
When the user launches TabDock and one or more persisted (empty layout-intent)
groups are restored, TabDock SHALL raise each restored group container to the
top of the normal z-order band exactly once at startup, so a restored group
whose initial position overlaps an unrelated pre-existing desktop window is not
left hidden behind it. The raise SHALL be issued by the z-order authority
(`WindowShepherdService`) using the existing container-raise primitive with
`HWND_TOP` and `SWP_NOACTIVATE`, run only after the container HWND exists and
is registered, and must not mutate guest HWNDs, styles, owners, or placement.

#### Scenario: restored group overlaps an existing window
- **WHEN** an unrelated top-level window already exists on the desktop and a
  restored TabDock group container is shown with an initial position that
  overlaps it
- **THEN** the restored container is raised above that window so it is visibly
  usable and not buried, and the unrelated window is not displaced to a
  different z-order band (no `HWND_TOPMOST`, no `Topmost=true`)

#### Scenario: startup reconciliation is bounded
- **WHEN** TabDock starts with N restored groups
- **THEN** the reconciliation performs at most one non-activating z-order write
  per restored container, once at startup, with no timer, polling loop, or
  repeated foreground call

### Requirement: Startup does not steal foreground from the user
The startup z-order reconciliation SHALL issue no foreground call
(`SetForegroundWindow`/`Activate`) of its own. If, after TabDock starts, the
user activates another application, TabDock SHALL respect that activation and
SHALL NOT repeatedly re-raise or re-activate its own windows to take foreground
back. The reconciliation SHALL raise restored containers in the NORMAL z-order
band **without taking focus** — it changes visible z-order (the intended
surface establishment) but never changes which window has focus. TabDock has no
supported background/silent/auto-start launch mode, so there is no such mode
whose non-intrusiveness must be preserved.

#### Scenario: external activation is respected after startup
- **WHEN** the user activates an unrelated application after TabDock has
  started and settled
- **THEN** that application stays foreground and TabDock does not steal it back

#### Scenario: reconciliation changes z-order but not focus
- **WHEN** TabDock starts and raises a restored group container in the normal
  z-order band
- **THEN** the container is raised above any overlapping unrelated window
  without taking focus, and the foreground window is unchanged

### Requirement: Local controlled-HWND stack invariant is preserved
The startup reconciliation SHALL NOT disturb the canonical Shepherd local
z-order: a visible guest sits above its container (normal mode) and a focused
split member sits above its partner above the container (split mode). The
startup raise runs before any guest exists; when a guest is later captured,
`PositionAndShow`/`PairZOrderBehind` re-establish the guest-above-container
stack from scratch.

#### Scenario: local stack remains valid after a guest is captured post-startup
- **WHEN** a restored group is empty at startup (so the reconciliation runs
  once) and the user then captures a live guest into it
- **THEN** the captured guest is positioned above the container and the
  container remains immediately below it; the startup raise does not invert the
  stack

