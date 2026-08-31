# ui-ux-hardening delta — user-reported presentation integrity

## MODIFIED Requirements

### Requirement: Native move/size completion SHALL reconcile against final geometry AND local z-order

When the container's own native move/size loop ends (`WM_EXITSIZEMOVE`), the
application SHALL schedule exactly one coalesced post-layout reconciliation
(through the existing Render-priority request mechanism, never synchronously
and never a timer) that re-validates the visible guest(s) against the FINAL
content rect and the local z-order pairing. The redundant-glue short-circuit
SHALL skip its native writes ONLY when the guest geometry matches within the
epsilon AND the required local visual-pairing invariant is already satisfied;
otherwise it SHALL perform the minimum identity-checked repair and become a
no-op once healthy.

TabDock-owned chrome interaction SHALL suppress guest foreground stealing when
required, but SHALL NOT use a blanket suppression rule that permits an opaque
container to cover the guest content unintentionally. Any temporary stacking
exception SHALL be scoped to the specific interaction/surface and its final
close SHALL request one bounded reconciliation. The local pairing predicate
MAY account for topmost-band and invisible-helper realities and SHALL NOT
require impossible strict adjacency. A healthy steady state SHALL issue zero
native writes. The per-frame drag path remains coalesced and bounded.

#### Scenario: The active guest stays rendered immediately after a container drag
- **WHEN** one captured guest is visible and the user drags the container through a multi-segment trajectory and releases with no later tab interaction
- **THEN** immediately after release the guest is visible, live, glued to the full content rect, and visually above the opaque content surface according to the local invariant — no tab switch is needed to recover it

#### Scenario: Both split panes stay rendered after a container drag
- **WHEN** a split `{A, B}` is active and the user drags the container and releases
- **THEN** immediately after release both panes are visible, glued, exactly partitioned, not unintentionally covered, and the split stays active

#### Scenario: Chrome interaction does not disable necessary visual pairing
- **WHEN** TabDock chrome owns keyboard/input priority while a guest remains presented
- **THEN** guest foreground stealing is suppressed as needed, but any non-activating visual repair required to prevent accidental container occlusion remains available or is replaced by an explicitly scoped stacking strategy

### Requirement: Redesigned surfaces SHALL remain keyboard-complete and responsive

The launcher, standalone and inline pickers, container chrome, tab strip, split
composite, and rename/color/recovery surfaces SHALL keep their critical actions
reachable at 100% through 200% scaling and on small supported work areas. Long
titles, workspace names, executable paths, and tab counts SHALL trim, wrap, or
scroll without hiding a required action. The native ContentHost and physical
guest-placement contract SHALL remain unchanged.

The workspace/group display title and rename editor SHALL use a centered region
whose midpoint follows the container client midpoint rather than the remaining
space between asymmetric caption controls. Under width pressure that region MAY
shrink/trim, but SHALL not overlap required caption controls.

#### Scenario: A small high-DPI window keeps picker actions reachable
- **WHEN** the picker is displayed on a small work area at high scaling with long candidate paths
- **THEN** search, selection, list scrolling, refresh, primary action, and cancel remain reachable without horizontal overflow or clipped status

#### Scenario: Long workspace and tab labels preserve chrome actions
- **WHEN** a workspace, application title, or split member has a long label
- **THEN** labels trim within their region and workspace selector, split, window state, and close controls remain accessible

#### Scenario: Workspace title stays centered despite asymmetric controls
- **WHEN** the right side contains workspace/split/add/window controls and the left side contains a smaller color/accent control region
- **THEN** the display title and rename region remain centered against the container client midpoint within layout rounding tolerance

#### Scenario: Focus indicators survive dark and high-contrast presentation
- **WHEN** a keyboard user moves focus through launcher, picker, tabs, split halves, and caption controls
- **THEN** the focused control remains visibly distinguishable without relying only on color or hover
