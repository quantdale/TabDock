## REMOVED Requirements

### Requirement: PickColorCommand is a documented no-op, not a misdirected trigger
**Reason**: The command was removed after caller verification. Keeping a requirement that it exist and that executing it must not change accent color contradicts the shipped color chip and invites restoring the old capture-picker miswire.
**Migration**: Select a workspace accent from the container color chip. Do not restore `PickColorCommand`. Capture still opens only through the existing add-windows request.

## ADDED Requirements

### Requirement: Accent selection does not open capture
The product SHALL NOT expose `PickColorCommand`. Choosing a palette color from the container color chip SHALL set that workspace's accent to the chosen color and persist it. That choice SHALL NOT open the capture picker. Opening the capture picker SHALL remain a separate add-windows action.

#### Scenario: A palette choice changes accent without opening capture
- **WHEN** the user chooses a palette color from the container color chip
- **THEN** the workspace accent becomes that color, the change is persisted, and the capture picker does not open

#### Scenario: Capture is not reached through a color command
- **WHEN** the user opens the capture picker
- **THEN** that action is the add-windows request, not a color command, and `PickColorCommand` is not available
