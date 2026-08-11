## ADDED Requirements

### Requirement: TabDock SHALL discover a guest's effective native minimum track size at runtime
TabDock SHALL query each currently visible guest's effective native minimum
track size (width and height, in physical pixels) via a cross-process
`WM_GETMINMAXINFO` probe using `SendMessageTimeout` with `SMTO_ABORTIFHUNG`
(never blocking the UI thread on a hung guest). The probe result SHALL be
cached and re-probed on discrete visible-set/window-state transitions
(split enter/exit/replace, active-tab change, survivor promotion, resize end)
and on a debounced periodic interval, never on every frame. If the probe fails,
times out, or is UIPI-blocked, TabDock SHALL treat the guest as unconstrained
(fail-closed to no constraint) rather than guessing a width or hardcoding a
per-application value, and SHALL NOT let the failure disrupt layout.

#### Scenario: A browser's native minimum is discovered at runtime
- **WHEN** a browser guest (e.g. Edge/Chrome) is the visible guest and TabDock probes its `WM_GETMINMAXINFO`
- **THEN** the probe returns the browser's effective minimum track size (e.g. ~500-650 px wide) and TabDock caches it without blocking the UI

#### Scenario: A hung guest does not block the UI
- **WHEN** a visible guest's window is not responding to the probe
- **THEN** `SendMessageTimeout` with `SMTO_ABORTIFHUNG` returns/times out and TabDock treats the guest as unconstrained, with no UI-thread stall

### Requirement: The container SHALL enforce a dynamic minimum size derived from the visible guests
The container's native minimum track size (`WM_GETMINMAXINFO` `ptMinTrackSize`)
SHALL be at least the content minimum (computed by
`SplitGeometry.MinContentWidth`/`MinContentHeight` from the currently visible
guests' native minima) plus the chrome delta, so the user cannot drag-resize the
shell below what the visible guests can physically fit. In normal mode the
content minimum SHALL be the active guest's minimum; in split mode it SHALL be
the exact partition's width/height for both members (content width at least
`max(2*leftMin, 2*rightMin - 1)`; content height at least the taller member's
minimum). The container SHALL NOT be constrained when no guest is visible.

#### Scenario: A split with a wide native-minimum guest cannot be narrowed below the constraint
- **WHEN** a split `{A, B}` is active, B enforces a 500 px native minimum width, and the user drag-resizes the container narrower
- **THEN** the container stops shrinking once the right pane would fall below B's 500 px minimum, and B never visibly escapes its pane

#### Scenario: Single-guest mode is constrained by the active guest's minimum
- **WHEN** a single guest enforcing a 500 px native minimum is the active tab and the user drag-resizes the container narrower
- **THEN** the container stops shrinking at the guest's 500 px minimum (plus chrome) and the guest continues to fill the content area

### Requirement: Requested-vs-observed geometry SHALL be reconciled without a resize war
After TabDock issues a native positioning write for a desired pane/content rect,
it SHALL verify the guest's actual `GetWindowRect`. If the observed rect does not
match the desired rect within the 1 px glue epsilon, TabDock SHALL mark the
guest as non-compliant for that exact rect and SHALL NOT re-issue the write for
the same rect on subsequent layout passes (preventing a per-frame resize war),
while still pinning the container z-order below the guest. When the desired rect
changes (container grows) or the guest becomes compliant (its native minimum
changed), TabDock SHALL clear the non-compliance and re-glue normally. A bounded
`SHEPHERD[size-constraint]` diagnostic SHALL be emitted on the non-compliance
transition, never continuously.

#### Scenario: A guest that refuses its pane is not fought every frame
- **WHEN** a guest's native minimum exceeds its assigned pane and TabDock has marked it non-compliant for that rect
- **THEN** subsequent layout passes do not re-issue the positioning write for that same rect, the container stays pinned below the guest, and the `SHEPHERD[size-constraint]` diagnostic is bounded (not per-frame)

#### Scenario: Widening the container re-glues a previously non-compliant guest
- **WHEN** a non-compliant guest's pane rect changes because the container grows wide enough to fit
- **THEN** TabDock clears the non-compliance and re-glues the guest to its pane normally

### Requirement: Constraint state SHALL track the currently relevant visible guest set
When the visible set changes (split enter/exit/replace, survivor promotion,
active-tab change, group switch, member pop-out/close), TabDock SHALL recompute
the container's minimum size from the NEW visible guests and SHALL NOT retain a
stale minimum from a departed member.

#### Scenario: Popping a split member drops the constraint to the survivor's minimum
- **WHEN** a split `{A, B}` with two 400 px-minimum guests is active and B is popped out, promoting A to full width
- **THEN** the container's minimum track drops to A's own ~400 px minimum, not the pair's ~800 px minimum

#### Scenario: Switching to a narrower guest lowers the normal-mode constraint
- **WHEN** the active single guest changes from one with a large native minimum to one with a small or no minimum
- **THEN** the container's minimum track is recomputed to the new guest's minimum and the container can be narrowed accordingly