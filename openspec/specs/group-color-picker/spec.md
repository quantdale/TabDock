# group-color-picker

## Purpose
Workspace accent selection is the container color chip. It must change and persist the workspace accent without opening the capture picker, and the removed `PickColorCommand` placeholder must not be restored as a capture trigger.

## Requirements

### Requirement: Accent selection does not open capture
The product SHALL NOT expose `PickColorCommand`. Choosing a palette color from the container color chip SHALL set that workspace's accent to the chosen color and persist it. That choice SHALL NOT open the capture picker. Opening the capture picker SHALL remain a separate add-windows action.

#### Scenario: A palette choice changes accent without opening capture
- **WHEN** the user chooses a palette color from the container color chip
- **THEN** the workspace accent becomes that color, the change is persisted, and the capture picker does not open

#### Scenario: Capture is not reached through a color command
- **WHEN** the user opens the capture picker
- **THEN** that action is the add-windows request, not a color command, and `PickColorCommand` is not available
