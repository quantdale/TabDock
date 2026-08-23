## ADDED Requirements

### Requirement: Redesigned surfaces SHALL remain keyboard-complete and responsive

The launcher, standalone and inline pickers, container chrome, tab strip, split
composite, and rename/color/recovery surfaces SHALL keep their critical actions
reachable at 100% through 200% scaling and on small supported work areas. Long
titles, workspace names, executable paths, and tab counts SHALL trim, wrap, or
scroll without hiding a required action. The native ContentHost and physical
guest-placement contract SHALL remain unchanged.

#### Scenario: A small high-DPI window keeps picker actions reachable

- **WHEN** the picker is displayed on a small work area at high scaling with
  long candidate paths
- **THEN** search, selection, list scrolling, refresh, primary action, and
  cancel remain reachable without horizontal overflow or clipped status

#### Scenario: Long workspace and tab labels preserve chrome actions

- **WHEN** a workspace, application title, or split member has a long label
- **THEN** labels trim within their region and workspace selector, split, window
  state, and close controls remain accessible

#### Scenario: Focus indicators survive dark and high-contrast presentation

- **WHEN** a keyboard user moves focus through launcher, picker, tabs, split
  halves, and caption controls
- **THEN** the focused control remains visibly distinguishable without relying
  only on color or hover
