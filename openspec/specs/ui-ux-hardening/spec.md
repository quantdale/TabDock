# ui-ux-hardening

## Purpose
Covers the UI/UX hardening campaign: split-screen pair semantics (the pair is one persistent logical selection unit with peer members, survivor promotion on member removal, deterministic self-testable partition), window-state reconciliation against the final content rect, native move/size completion reconciliation against final geometry AND local z-order, the known-DPI-unaware physical-coordinate contract, and the bounded environment fingerprint.
## Requirements

### Requirement: The split pair is one logical selection unit with peer members
While a split is active the pair SHALL be the selected tab-strip unit, rendered
as exactly one composite item `[ A | B ]`; no ordinary individual tab SHALL
represent a split member. After split creation the LEFT and RIGHT members SHALL
be peers: every member-focus entry point (composite half click, tab activation,
direct guest click, activation reassert, keyboard navigation) SHALL route
through one canonical operation that updates the focused member, the logical
active member, the bounded `SPLIT[focus]` diagnostic (emitted only when the
focused member changes), the local z-order, and real foreground — and SHALL NOT
change split membership, hide the partner, or exit the split. Initiator/partner
history SHALL be irrelevant: for an active pair `{LEFT, RIGHT}`, clicking either
member SHALL keep both panes rendered and glued with exactly one focused
member.

#### Scenario: Focusing the partner keeps both panes rendered
- **WHEN** a split `{A, B}` is active (A initiated, B chosen as partner) and the user clicks B's composite half
- **THEN** B becomes the focused member (`SPLIT[focus]` names B), both panes stay visible and glued, the split stays active, and B receives real foreground input

#### Scenario: Focusing the initiator keeps both panes rendered
- **WHEN** a split `{A, B}` is active and the user clicks A's composite half after focusing B
- **THEN** A becomes the focused member, both panes stay visible and glued, and the split stays active

#### Scenario: Mirror construction order behaves identically
- **WHEN** a split is created with B as the initiator and A as the partner
- **THEN** focusing either member behaves exactly as in the A-initiated case, with the same pair/visibility/focus invariants

### Requirement: Survivor promotion on member removal
When a split member leaves the group (pop-out, close, self-hide), the split SHALL terminate and the surviving member SHALL be promoted to the single visible guest; no subsequent active-tab re-derivation SHALL hide or displace the promoted survivor.

#### Scenario: Popping the focused partner with three tabs promotes the survivor
- **WHEN** a group has tabs `[A, B, C]`, split `{A, B}` is active, B is the focused member, and B is popped out via its half close button
- **THEN** A becomes the single visible full-width guest (visible, glued, active), C stays hidden, and B is released to standalone

### Requirement: The container z-order SHALL keep the focused member on top
During split the local stack SHALL be `focused guest → partner guest → container` after every focus change. When both panes are already glued to their rects, the layout SHALL verify the pair's actual window order before applying the cheap container pin; if the order does not hold, the atomic positioning batch SHALL re-assert it.

#### Scenario: Half-click on the partner never occludes it
- **WHEN** the user clicks the partner's composite half while both panes are glued
- **THEN** the partner ends up on top of the stack, visible and receiving input — the container SHALL NOT be wedged between the panes

### Requirement: Deferred split positioning respects HDWP generation boundaries
The split positioning batch SHALL validate each guest's cheap capture
generation immediately before its `DeferWindowPos` queue operation. A failed
native `DeferWindowPos` SHALL be abandoned without `EndDeferWindowPos` as
required by Win32. If a later generation check fails while a valid HDWP exists,
the valid batch SHALL be closed with `EndDeferWindowPos` and the stale-guest
fallback SHALL not run. The final check-to-commit race that Win32 does not
expose as an atomic cancellation boundary SHALL remain explicit.

#### Scenario: A stale split guest is not queued
- **WHEN** a split guest generation changes before its deferred queue operation
- **THEN** that guest is not passed to `DeferWindowPos`, the valid HDWP
  lifecycle is closed safely, and no fallback mutation targets the stale guest

### Requirement: Window-state transitions reconcile against the final content rect
For every supported transition (Normal→Maximized, Maximized→Normal,
Normal→Minimized→Normal, Maximized→Minimized→Maximized) the visible guest(s)
SHALL be re-glued against the FINAL content rect — never a pre-transition
stale rect. Minimize SHALL hide the visible guest(s); restore SHALL re-show
them at the correct pane/full rect with the split still active. The partition
invariants (exact coverage, zero overlap, zero gap; odd-width remainder to the
right pane) SHALL hold after every transition, with no accumulating 1 px drift.

#### Scenario: Maximize then restore keeps the panes partitioned
- **WHEN** a split is active and the container is maximized and then restored (repeated cycles)
- **THEN** after each transition both guests exactly partition the current content rect (no overlap, no gap, both visible) and the split stays active

### Requirement: The split partition SHALL have a deterministic, self-testable definition
The 50/50 partition SHALL be defined by exactly one function
(`SplitGeometry.Partition`). The application SHALL expose a
`--selftest-geometry` mode that, with no UI and no input, asserts the partition
invariants over a deterministic matrix (all widths 1..4096, representative
heights, positive/zero/negative origins, odd widths) and a seeded fuzz sweep
(100,000 rects), exiting 0 only when every check passes, and logs
`SELFTEST[geometry]`.

#### Scenario: Odd widths partition without overlap
- **WHEN** the self-test runs over widths 799/800/801/1023/1024/1025/1919/1920/1921 at positive and negative origins
- **THEN** LEFT.Right == RIGHT.Left, RIGHT.Right == content.Right, LEFT.Width + RIGHT.Width == content.Width, and zero overlap/gap hold for every case

### Requirement: Known DPI-unaware guests use the physical-coordinate contract
Capture SHALL accept a guest when its awareness probe succeeds and identifies a
known `DPI_UNAWARE` context and the target monitor's effective DPI is valid.
TabDock SHALL continue to position the independent top-level outer HWND from
its PerMonitorV2 caller in physical screen pixels. Windows may bitmap-scale the
unaware guest's content, so rendering can be blurry even when outer geometry is
correct. The guest's 96-DPI logical minimum-track value SHALL be converted at
the centralized boundary using the target monitor's effective DPI. If the
awareness or monitor-DPI probe fails, returns unknown, or returns an invalid
zero value, capture SHALL fail closed rather than admitting unverified
coordinate assumptions. Deterministic tests SHALL NOT claim physical mixed-DPI
hardware qualification.

#### Scenario: Known DPI-unaware window at 150% scaling
- **WHEN** the user attempts to capture a known DPI-unaware window on a monitor at 150% scaling
- **THEN** capture is accepted, its outer geometry remains physical-pixel based, and any content blur is a Windows scaling effect rather than a capture refusal

#### Scenario: Unaware minimum tracking is converted centrally
- **WHEN** an unaware guest reports a 96-DPI logical minimum track of 500 on a 144-DPI monitor
- **THEN** TabDock applies a 750-pixel physical minimum; at 96 DPI the value remains 500, and an aware guest does not receive unaware scaling

#### Scenario: An invalid DPI probe does not admit an unverified guest
- **WHEN** the guest awareness context or system-DPI probe fails or returns zero during capture
- **THEN** capture is refused with a clear error and the guest is not admitted with unverified coordinate assumptions

### Requirement: Environment fingerprint for diagnosability
The application SHALL log a bounded environment fingerprint: at startup
(`ENV[startup]`: OS version/build, .NET runtime, process bitness, monitor
count/bounds/work areas/primary flags), when the launcher appears
(`ENV[launcher]`: system DPI), per open container (`ENV[container]`: container/
content-host/guest rects, window state, active monitor, DPI), and in the
`STATE[settled]` snapshot (platform + guest executable). No fingerprint data
SHALL be logged per frame.

#### Scenario: A startup log is self-describing
- **WHEN** the application starts on a customer machine
- **THEN** the log contains `ENV[startup]` with OS/.NET/bitness and the full monitor layout, and each open container logs `ENV[container]` — sufficient to diagnose machine-specific geometry failures without further queries

### Requirement: The split pair SHALL persist until an explicit presentation switch or structural teardown
Once a split pair `{LEFT, RIGHT}` is active it SHALL remain the selected
composite unit while the user interacts with either split member and while a
non-member tab is merely hovered or right-clicked. An ordinary **left-click**
on a non-member tab SHALL instead be treated as an explicit presentation switch:
TabDock SHALL journal-safely hide both split members, clear the split composite,
make the clicked tab the ordinary active tab, and present it full-width without
releasing any captured member.

If either split-member hide cannot complete safely and returns recovery-pending,
TabDock SHALL fail closed: the split remains authoritative, the clicked
non-member SHALL NOT become selected/active, and the existing pair SHALL be
re-presented so a partially completed hide cannot leave one blank pane.

A newly captured window added while split is active SHALL continue to be hidden
journal-safely so the visible set remains the pair. Ctrl+Tab while split is
active SHALL continue to cycle only between the two split members. Split-member
focus, direct guest click, hover, and non-member context-menu open/close SHALL
not by themselves change split membership.

#### Scenario: Clicking a third tab exits split and opens that tab
- **WHEN** a split `{A, B}` is active with a third captured tab C and the user left-clicks C's ordinary tab
- **THEN** a normal split exit occurs, A and B are hidden but remain captured, C becomes the single active full-width guest, the ordinary three-tab strip is restored, and no member is released

#### Scenario: An uncertain split hide fails the third-tab switch closed
- **WHEN** the user left-clicks C while `{A, B}` is split and hiding either A or B becomes recovery-pending
- **THEN** C does not become the active tab, the split remains authoritative, and TabDock re-presents A and B through the existing split layout path while preserving recovery evidence

#### Scenario: Hovering a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user hovers C without left-clicking it
- **THEN** the pair remains `{A, B}`, both panes stay visible/glued, C stays hidden, and no split exit or release occurs

#### Scenario: Right-clicking a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user opens and dismisses C's context menu
- **THEN** the pair remains active and visible and C remains a captured hidden non-member

### Requirement: Split creation SHALL settle presentation after TabDock chrome closes
A split created from a TabDock context menu SHALL receive one bounded
post-popup presentation settle after TabDock-owned chrome is no longer active.
The settle SHALL re-run the existing split layout against the current content
rect and request real foreground for the current focused split member through
the existing identity-checked foreground API. This settle SHALL not synthesize
`WM_SIZE`, alter guest window styles, reparent guests, use `AttachThreadInput`,
or bypass foreground/identity guards.

#### Scenario: Initial split does not require a corrective pane click
- **WHEN** the user selects Split screen from a tab context menu
- **THEN** after the menu closes both guests are laid out in their current panes and the focused/initiating member receives a real foreground request, so the first correct split presentation does not depend on the user clicking inside that guest

#### Scenario: TabDock chrome is never preempted by the settle
- **WHEN** another TabDock-owned popup/chrome interaction is still active while a split settle is pending
- **THEN** the settle remains pending and does not foreground a guest until the chrome interaction has ended

### Requirement: Native move/size completion SHALL reconcile against final geometry AND local z-order
When the container's own native move/size loop ends (`WM_EXITSIZEMOVE`), the
application SHALL schedule exactly one coalesced post-layout reconciliation
(through the existing Render-priority request mechanism, never synchronously
and never a timer) that re-validates the visible guest(s) against the FINAL
content rect and the local z-order pairing. The redundant-glue short-circuit
SHALL skip its native writes ONLY when the guest geometry matches within the
epsilon AND the container provably sits BELOW the guest (an upward
`GW_HWNDPREV` walk skipping invisible helper windows — not a strict-adjacency
probe, which cannot hold for topmost guests or hidden IME helpers); otherwise
it SHALL repair with the container pin (at most one `SetWindowPos`, idempotent
once healthy). The repair SHALL be suppressed while TabDock chrome is
intentionally raised above the guests (context menu, color/group menu, capture
panel, rename box); the popup-close path reconciles the stack. A healthy
steady state SHALL issue zero native writes. The per-frame drag path (one
coalesced pass, atomic split batches) SHALL be unchanged.

#### Scenario: The active guest stays rendered immediately after a container drag
- **WHEN** one captured guest is visible and the user drags the container through a multi-segment trajectory and releases (repeated cycles, NO tab interaction after release)
- **THEN** immediately after release the guest is visible, live, glued to the full content rect, and the top window at the content center — no tab switch is needed to recover it

#### Scenario: Both split panes stay rendered after a container drag
- **WHEN** a split `{A, B}` is active and the user drags the container and releases (repeated cycles, alternating the focused member)
- **THEN** immediately after release both panes are visible, glued, exactly partitioned (no overlap, no gap), neither covered, and the split stays active
