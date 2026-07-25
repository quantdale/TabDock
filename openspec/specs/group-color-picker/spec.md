# group-color-picker

## Purpose
TBD — captures the intended (non-)behavior of `GroupViewModel.PickColorCommand`, clarifying it as a documented no-op rather than a misdirected trigger for the capture picker.

## Requirements

### Requirement: PickColorCommand is a documented no-op, not a misdirected trigger
`GroupViewModel.PickColorCommand` SHALL NOT invoke `AddWindowsRequested` (`ViewModels/GroupViewModel.cs:103`). Executing `PickColorCommand` SHALL be a documented no-op: it SHALL NOT raise `AddWindowsRequested`, open the capture picker, or change `AccentColor` (or any other observable state).

#### Scenario: Executing the command raises no events and changes no state
- **WHEN** `PickColorCommand.Execute` is invoked
- **THEN** `AddWindowsRequested` is not raised, the capture picker is not opened, `AccentColor` is unchanged, and no other observable state changes

#### Scenario: AddWindowsRequested is only raised by RequestAddWindows
- **WHEN** any code path other than `GroupViewModel.RequestAddWindows()` needs to open the capture picker
- **THEN** `PickColorCommand`'s execution is not a valid trigger for that — `AddWindowsRequested` is raised solely by `RequestAddWindows()`
