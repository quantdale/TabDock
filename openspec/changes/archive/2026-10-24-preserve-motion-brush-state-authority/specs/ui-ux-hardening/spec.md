## ADDED Requirements

### Requirement: Motion decoration SHALL preserve live brush-state authority

Attaching motion SHALL NOT replace or override the binding/style source that defines an element's semantic base fill or border. Later active, selected, focus, style or theme-source changes SHALL remain observable while feedback is attached. Motion-owned decoration SHALL affect only its own rendering channel, SHALL NOT mutate shared brush resources, and SHALL reveal the current authoritative base state on exit or detach. Decoration SHALL NOT change layout, input hit areas, or automation identity.

#### Scenario: Split focus state updates after attachment

- **WHEN** LEFT/RIGHT semantic active state changes after a split-half template is loaded with motion enabled
- **THEN** the visible active indication follows the current member and is not pinned to the brush captured at load time

#### Scenario: A bound or styled base color changes under feedback

- **WHEN** the authoritative base brush changes while hover/selection feedback is active
- **THEN** the source remains bound/styled, the current feedback target stays coherent, and feedback exit reveals the new base color

#### Scenario: Detach and recycling preserve source authority

- **WHEN** feedback detaches or a realized row is recycled
- **THEN** the original value source remains intact and no motion-local value or shared-resource mutation masks the current state
