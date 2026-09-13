## ADDED Requirements

### Requirement: Native content marker SHALL paint the shared content-void color
The container's native content-area marker HWND SHALL fill with the same RGB
color as the shared WPF content-void token (`TdContentVoidBrush`). Because that
marker is a child HWND, it paints above WPF siblings and is the visible void
whenever a guest does not fully cover the content rect, including an empty
workspace around the empty-state overlay, an empty workspace whose overlay is
closed because the container is inactive or the inline capture panel is open,
tab-switch gaps, and layout gaps.
The marker's window class name SHALL remain the existing
externally-discoverable content-host class so production geometry queries and
the validation harness keep a single HWND identity. A later palette change
that updates the WPF token SHALL be treated as incomplete until the native
fill matches.

The structural contract suite SHALL fail if the native class-background
COLORREF, the WPF token's `#RRGGBB` definition, and the documented airspace
lockstep disagree.

#### Scenario: Empty workspace void matches the shared token
- **WHEN** a container is open with zero captured tabs and the empty-state overlay is centered over the content area
- **THEN** the visible field around that overlay is the current content-void token color, not the retired `#1E1E1E` host fill

#### Scenario: Inactive empty workspace shows the token void
- **WHEN** an empty container loses activation (the overlay's `IsActive` condition closes the popup) or the inline capture panel opens over an empty workspace
- **THEN** the fully uncovered native marker still paints the current content-void token color

#### Scenario: Uncovered content rect does not flash the old host color
- **WHEN** the content marker is momentarily uncovered during a tab switch or layout gap
- **THEN** any visible native fill is the current content-void token color

#### Scenario: Token and native COLORREF stay in lockstep
- **WHEN** the structural frontend contract tests run
- **THEN** they fail if `TdContentVoidBrush`'s `#RRGGBB` in the shared resource dictionary does not match the native content-host class background COLORREF, or if the host class name used for discovery has changed

#### Scenario: Content-host identity is preserved
- **WHEN** production code or the validation harness locates the content-area marker by its known class name
- **THEN** that class name is unchanged and the marker remains a non-focusable, never-reparented child HWND
