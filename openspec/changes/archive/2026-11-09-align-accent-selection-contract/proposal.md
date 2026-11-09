## Why

`openspec/specs/group-color-picker/spec.md` still requires `GroupViewModel.PickColorCommand` to exist as a no-op that must not change `AccentColor`. That command was removed in `a7ab0a7`, while the container color chip already changes accent. Agents following the stale spec can restore a capture-trigger footgun or treat the working accent menu as forbidden.

## What Changes

- Replace the removed-command no-op requirement with the shipped accent-selection contract: no `PickColorCommand`, color-chip choices set and persist `AccentColor`, and that path does not raise `AddWindowsRequested`.
- Correct the capability purpose, which still describes the removed command as a live no-op. OpenSpec sync leaves an existing purpose untouched, so this correction is explicit.
- Align the agent guide's "do not clean up" list with current source so it no longer preserves removed members or describes the live launcher selection as unused.
- Add a source-contract regression for the color-chip path and the absent command.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `group-color-picker`: Accent selection is the container color chip, not a `PickColorCommand` no-op.

## Impact

- `openspec/specs/group-color-picker/spec.md`
- `docs/internal/AGENT_GUIDE.md`
- `tests/UnitTests/InteractionSourceContractTests.cs`
- No capture, persistence, or Shepherd behavior changes. The color-chip handler already sets `AccentColor` and calls `SaveState()`.
