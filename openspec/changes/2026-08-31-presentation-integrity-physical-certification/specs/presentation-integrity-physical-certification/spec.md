# presentation-integrity-physical-certification delta

## ADDED Requirements

### Requirement: Physical presentation-integrity claims SHALL require a valid interactive lease

A physical PASS for presentation integrity SHALL require an exclusive,
ownership-checked interactive Windows desktop lease immediately before guarded
input and authoritative assertions. A run without that proof SHALL be reported
as blocked rather than converted into physical evidence.

#### Scenario: Interactive lease cannot be proven
- **WHEN** the candidate and harness are healthy but the desktop cannot prove
  exclusive supervised input ownership
- **THEN** no guarded SendInput is issued and the scenario records
  BLOCKED_SUPERVISED or BLOCKED_ENVIRONMENT, not PASS

### Requirement: Original chrome-occlusion reports SHALL be physically exercised

Accent/color, workspace/group rename, split actions and the "+" capture workflow
SHALL each receive repeated physical qualification that proves both the TabDock
interaction surface and the guest content presentation. Correct guest geometry
alone SHALL NOT be sufficient when the guest is covered.

#### Scenario: Color menu remains usable over a live guest
- **WHEN** a captured guest is presented and the user repeatedly opens/selects/
  dismisses the accent menu through real guarded input
- **THEN** the menu remains clickable, the guest content region remains live and
  correctly owned where expected, and no corrective tab switch is needed

#### Scenario: Rename retains keyboard focus without blanking the guest
- **WHEN** the user repeatedly enters rename, types, commits/cancels and reopens
- **THEN** the editor receives keyboard input while guest presentation remains
  healthy and the final close settles without stranded stacking state

### Requirement: Guest-native maximize and fullscreen SHALL be physically qualified

Physical certification SHALL invoke maximize/fullscreen through the guest or
real operating-system/application interaction, not solely by synthetic
ShowWindow state mutation. The run SHALL verify current captured identity,
logical membership, assigned presentation, visibility, monitor and usability
after the transition.

#### Scenario: Real guest-caption maximize remains contained
- **WHEN** the user activates the captured guest's own maximize control through
  guarded physical input
- **THEN** the same captured identity reconciles to its authoritative TabDock
  presentation without requiring a corrective tab click

#### Scenario: Real browser/app F11 fullscreen is dispositioned
- **WHEN** an isolated real application with an F11-style fullscreen mode is
  available
- **THEN** fullscreen/exit is physically exercised and either passes the
  containment contract or produces retained FAIL_PRODUCT evidence; if the
  capability is unavailable it is recorded explicitly rather than inferred

### Requirement: Multi-monitor and topmost qualification SHALL be capability-honest

Dual-monitor transfer, mixed-DPI behavior and WS_EX_TOPMOST interactions SHALL
be physically qualified only when the required real capability exists. Missing
hardware/software SHALL remain a named blocked/skip cell and SHALL not weaken
the deterministic contract.

#### Scenario: Guest monitor-transfer attempt
- **WHEN** two real monitors are available and a still-captured guest is moved
  toward the other monitor through a normal system/user action
- **THEN** the observed result is classified against the authoritative
  presentation contract with monitor/DPI evidence retained

#### Scenario: Topmost guest and TabDock popup
- **WHEN** a controlled WS_EX_TOPMOST guest is available and TabDock opens an
  owned popup/dialog
- **THEN** local chrome/guest presentation is usable without converting the
  container into a permanent topmost window or fighting unrelated applications

### Requirement: LOCATIONCHANGE qualification SHALL demonstrate bounded load

The desktop-wide EVENT_OBJECT_LOCATIONCHANGE route SHALL be physically or
controlled-load qualified for unrelated geometry churn. Evidence SHALL include
callback/admission/coalescing/native-repair counts sufficient to show unrelated
events are rejected early and equivalent captured events do not create
unbounded dispatcher/native mutation growth.

#### Scenario: Unrelated windows generate location churn
- **WHEN** test-owned unrelated windows are repeatedly moved/resized while a
  guest is captured
- **THEN** unrelated callbacks do not cause proportional guest repair/native
  positioning work and the UI remains responsive

### Requirement: First-attempt physical evidence SHALL remain authoritative

A valid first-attempt FAIL_PRODUCT SHALL never be erased by a later PASS.
Reruns SHALL retain both outcomes and the campaign SHALL remain unresolved
until the failure is understood and dispositioned.

#### Scenario: Failure then pass
- **WHEN** a valid physical scenario first reports FAIL_PRODUCT and a fresh-state
  rerun later passes
- **THEN** both attempts remain in the evidence and the aggregate is not promoted
  directly to release PASS

### Requirement: Guarded physical actions SHALL retain current root and identity continuity

Every guarded action and authoritative assertion SHALL revalidate the target
process-start/HWND identity, `WindowFromPoint` → `GA_ROOT`, foreground, and
ownership conditions. A foreign or recycled root SHALL invalidate the lease and
prevent the action or mutation.

#### Scenario: A foreign covering root invalidates a step

- **WHEN** a foreign or recycled root is observed at a target point or as the
  required foreground immediately before a guarded action
- **THEN** the lease is invalidated, no input or mutation is issued, and the
  evidence retains the identity mismatch

### Requirement: Physical certification evidence SHALL separate deterministic coverage

Each presentation-integrity capability SHALL retain a candidate SHA and binary
identity, environment/topology snapshot, attempt count, first result, final
disposition, and bounded evidence reference. Deterministic tests, synthetic
transitions, and read-only probes SHALL be labeled separately and SHALL NOT be
promoted to physical PASS.

#### Scenario: A blocked first attempt remains visible

- **WHEN** a required physical scenario is blocked before input and a later
  attempt has different availability
- **THEN** the evidence retains the original blocked result and the later result
  separately rather than replacing the first attempt

#### Scenario: Geometry-only evidence cannot pass presentation

- **WHEN** a guest rectangle matches the content host but point ownership or
  client-render evidence shows an opaque container cover
- **THEN** the physical presentation cell is not a PASS
