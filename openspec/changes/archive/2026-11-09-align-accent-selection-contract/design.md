## Context

See proposal.md. The color chip in `Views/ContainerWindow.xaml` already opens `ColorContextMenu`, and `ColorMenuItem_Click` assigns `GroupViewModel.AccentColor` then calls `GroupManager.SaveState()`. `AddWindowsRequested` is raised only by `GroupViewModel.RequestAddWindows()`. No product behavior change is required.

## Goals / Non-Goals

**Goals:**

- Make the main spec describe the shipped accent path.
- Stop agent instructions from preserving removed members or calling live launcher selection unused.
- Lock the wiring with a headless source contract, not a SendInput scenario.

**Non-Goals:**

- Changing palette colors, persistence format, or capture admission.
- Removing `CloseGroupCommand` / `CloseRequested`. They still have no subscriber, and this change does not reopen that cleanup.
- Rewriting historical audit notes that described the old command.

## Decisions

- Replace the requirement instead of editing it in place. The old header claims the command is a documented no-op; a modified body under that header would stay misleading.
- Edit the main spec purpose directly. Delta purposes are ignored for existing capabilities.
- Guard the contract in `InteractionSourceContractTests` by slicing the production handler. A physical color-menu click is unnecessary for this wiring invariant and is outside unattended validation.

## Risks / Trade-offs

- [Source contract can miss a new capture path that does not use the current handler names] → The test also asserts `PickColorCommand` is absent and that `AddWindowsRequested` is invoked only from `RequestAddWindows`.
- [Historical qualification notes cite the old no-op spec] → Leave those records unchanged. They describe the evidence of that run, not the current contract.

## Migration Plan

Update the main spec, agent guide, and contract test together. Rollback is reverting that commit; no data migration.

## Open Questions

None.
