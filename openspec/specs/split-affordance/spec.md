# split-affordance

## Purpose

Makes split capability persistently discoverable in container chrome while
projecting the existing split relationship and presentation semantics without
introducing another authority.

## Requirements

### Requirement: Container chrome SHALL keep a split affordance visible

Every live TabDock container SHALL show a stable, keyboard-accessible `Split ▾`
or equivalent split control in its chrome. The control SHALL remain visible
regardless of whether a relationship is defined or presented.

#### Scenario: Split control is visible in ordinary container state
- **WHEN** a container is open with zero, one, or multiple tabs
- **THEN** the split control remains visible in the container chrome

#### Scenario: Fewer than two tabs disables split
- **WHEN** the container has fewer than two live tabs
- **THEN** the split control is disabled and its accessible help explains that another tab is required

### Requirement: Split affordance SHALL project eligible creation actions

With at least two tabs and no defined relationship, opening the affordance SHALL
list the active tab's eligible current captured partners, excluding itself.
Choosing a partner SHALL preserve the selected tab as LEFT and route pair
creation through the existing canonical split operation.

#### Scenario: Eligible partner list excludes the active tab
- **WHEN** the active tab has multiple other current captured tabs
- **THEN** the affordance lists each eligible partner and no self-target

#### Scenario: Choosing a partner creates the pair
- **WHEN** the user chooses an eligible partner
- **THEN** the active tab becomes LEFT, the chosen tab becomes RIGHT, and the existing split controller/presentation path creates and presents the pair

#### Scenario: Stale creation target fails closed
- **WHEN** a partner is destroyed, released, or HWND-recycled after the menu opens but before selection
- **THEN** the action performs no split mutation and leaves the current presentation authoritative

### Requirement: Split affordance SHALL project presented and dormant actions

When a relationship is presented, the affordance SHALL expose focus LEFT,
focus RIGHT, and end split actions as applicable. When the relationship is
dormant, the affordance SHALL expose resume/show split actions for the members
and end split. Actions SHALL preserve LEFT/RIGHT identity, the dormant
relationship, composite behavior, and existing split-member removal semantics.

#### Scenario: Presented pair exposes focus and end actions
- **WHEN** a split pair is presented
- **THEN** the affordance exposes focus LEFT, focus RIGHT, and end split, and focusing a member keeps both panes presented

#### Scenario: Dormant pair exposes resume and end actions
- **WHEN** a split relationship is dormant behind an unrelated active tab
- **THEN** the affordance exposes resume/show actions and end split; resuming restores the pair through the existing guarded path

#### Scenario: Ending dormant split retains the unrelated active tab
- **WHEN** the user ends a dormant relationship
- **THEN** the relationship is cleared, the unrelated active tab remains the full-width active guest, and former members return to ordinary hidden tabs

#### Scenario: Member removal invalidates an open affordance safely
- **WHEN** a split member leaves while the split menu/control is open
- **THEN** stale actions become no-ops, the pair is cleared through existing member-removal semantics, and any survivor promotion/dormant non-member preservation remains unchanged

### Requirement: Split affordance SHALL not create a second split authority

All split-affordance actions SHALL route through the existing split policy,
controller, guarded native identity checks, and canonical container focus,
resume, exit, and member-removal paths. The affordance SHALL not mutate split
membership, presentation flags, LEFT/RIGHT identities, or native guest state
directly.

#### Scenario: Ordinary unrelated selection preserves a defined relationship
- **WHEN** a third tab is selected while a relationship exists
- **THEN** the existing dormant/presented interaction path decides the presentation and the relationship identities remain unchanged unless the user explicitly ends or invalidates it

#### Scenario: Split control is automation-accessible
- **WHEN** UI Automation inspects the container chrome and its open actions
- **THEN** it can identify the split affordance, disabled reason, partner actions, LEFT/RIGHT actions, resume actions, and end action through stable AutomationIds and names
