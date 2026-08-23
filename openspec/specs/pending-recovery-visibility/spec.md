# pending-recovery-visibility

## Purpose

Makes unresolved legacy recovery evidence visible in the normal launcher while
keeping inspection read-only and preserving the supervised, explicitly
confirmed recovery workflow.

## Requirements

### Requirement: Launcher SHALL surface unresolved pending-recovery evidence

When unresolved pending-recovery evidence is present, the launcher SHALL show
a visually distinct, non-modal recovery-attention banner with the count of
unresolved pending evidence files and the exact commands
`TabDock.exe --pending-recovery` and `TabDock.exe --recover-pending`.

#### Scenario: No pending evidence hides the banner
- **WHEN** pending-recovery discovery finds no unresolved or unreadable pending evidence
- **THEN** the launcher contains no recovery-attention banner

#### Scenario: One unresolved pending file is visible
- **WHEN** discovery finds one pending evidence file with at least one unresolved entry
- **THEN** the launcher shows a recovery-attention banner with count `1` and both exact supported commands

#### Scenario: Multiple unresolved files are counted
- **WHEN** discovery finds multiple pending evidence files with unresolved entries
- **THEN** the launcher shows their unresolved-file count and does not collapse the count to a yes/no state

#### Scenario: Fully resolved evidence is not counted
- **WHEN** every entry in a pending evidence file has a durable resolution and the source is otherwise fully resolved
- **THEN** that file does not contribute to the banner count

#### Scenario: Unreadable evidence remains visible as attention
- **WHEN** a pending evidence file or its recovery ledger is corrupt or unreadable
- **THEN** the launcher keeps recovery attention visible, fails closed, and never presents the evidence as safely absent

### Requirement: Launcher recovery visibility SHALL be read-only

Displaying or refreshing the banner SHALL only reuse read-only pending-evidence
discovery. It SHALL not install recovery tokens, mutate guest windows, write
resolution or transaction evidence, delete pending sources/sidecars, or take
ownership of a supervised recovery transaction.

#### Scenario: Displaying the banner does not mutate evidence
- **WHEN** the launcher projects a nonzero pending count
- **THEN** the pending source, sidecar, resolution, and transaction bytes remain unchanged and no native recovery operation occurs

#### Scenario: Supervised commands remain the only recovery mutation path
- **WHEN** a user wants to inspect or recover the evidence
- **THEN** the banner directs them to the existing read-only inspection and supervised recovery commands, with no in-app recovery button

### Requirement: Recovery banner controls SHALL be automation-accessible

The banner and its count/command content SHALL expose stable Automation
properties so UI Automation can identify the attention state and exact command
guidance without relying on color or screen position.

#### Scenario: Automation identifies recovery attention
- **WHEN** the banner is visible
- **THEN** it has a stable AutomationId and an accessible name describing pending recovery attention
