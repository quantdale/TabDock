# Persistent split relationship and presentation qualification

## Why

The immediately preceding split follow-up correctly made a third captured tab
clickable, but interpreted that click as a relationship teardown. The required
product behavior is narrower: selecting a non-member temporarily suspends the
pair presentation while retaining the exact runtime LEFT/RIGHT relationship.
Selecting either composite half must restore that same pair without a new split
command.

The three-app rendering report also requires evidence about client content, not
only outer HWND geometry. This change adds the state/diagnostic seams needed to
qualify that distinction without adding browser-specific native workarounds.

## What changes

- Separate split relationship existence from pair presentation mode.
- Keep the composite projection while a non-member is presented full-width.
- Add journal-safe suspend/resume transitions and dormant explicit-exit and
  structural-removal semantics.
- Omit `Split screen` from a current pair member's context menu while retaining
  `Exit split screen` in both presented and dormant states.
- Distinguish relationship and presentation in logical diagnostics and expected
  geometry.
- Keep queued split settle work tied to the currently desired pair presentation.
- Update guarded real-input scenarios and record comparative rendering evidence.

## Supersession

This focused change supersedes the immediately preceding
`split-tab-switch-and-settle-fix-2026-08-14` interpretation that an ordinary
third-tab click exits split. That earlier change remains historical context for
the initial clickability and settle defects; its third-tab contract is replaced
by this one.

## Non-goals

- Persisting the runtime split relationship across application restart.
- Replacing Shepherd/no-reparent or changing identity, recovery, input-safety,
  DPI, or persistence formats.
- Synthesizing `WM_SIZE`, adding browser-specific hacks, or releasing guests as
  part of ordinary presentation switching.
- A production Release or tag in this session.
