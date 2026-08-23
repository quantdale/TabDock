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
(`SplitGeometry.Partition`). Its invariants — exact coverage, zero overlap,
zero gap, no inverted or overflowing rects — SHALL be self-testable headlessly:
they are qualified by the headless xUnit suite over an exhaustive deterministic
matrix (all widths 1..4096, representative heights, positive/zero/negative
origins, odd widths), a seeded fuzz sweep (100,000 rects, fixed seed 20260810),
and the size-constraint minimality math. The product executable carries no
geometry self-test mode.

#### Scenario: Odd widths partition without overlap
- **WHEN** the partition qualification runs over widths 799/800/801/1023/1024/1025/1919/1920/1921 at positive and negative origins
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

### Requirement: The split relationship SHALL persist across ordinary non-member selection
Once a split relationship `{LEFT, RIGHT}` is defined it SHALL remain defined
until explicit exit, explicit split reconfiguration, or structural invalidation
of a member. The relationship and its presentation SHALL be separate states.

While the pair is presented, exactly LEFT and RIGHT SHALL be visible in their
original panes and the focused member SHALL be the logical active member.

When a user selects a non-member C, TabDock SHALL journal-safely hide LEFT and
RIGHT, retain the relationship and composite projection, present C as the only
visible full-width guest, and make C the logical active guest. This is a
dormant-pair state, not a relationship exit. Selecting either composite half
SHALL journal-safely hide C, restore the exact same LEFT/RIGHT pair, preserve
pane identity, and focus the clicked member.

If either split-member hide cannot complete safely and returns recovery-pending,
TabDock SHALL fail closed: the split remains authoritative, the clicked
non-member SHALL NOT become selected/active, and the existing pair SHALL be
re-presented so a partially completed hide cannot leave one blank pane.

A newly captured window added while the pair is presented SHALL continue to be
hidden journal-safely so the visible set remains the pair. Ctrl+Tab while the
pair is presented SHALL continue to cycle only between its members. While the
pair is dormant, ordinary navigation among non-members SHALL remain available;
selecting a split member resumes the pair.

#### Scenario: Pair to third tab suspends without teardown
- **WHEN** a split `{A, B}` is active with a third captured tab C and the user left-clicks C's ordinary tab
- **THEN** `{A, B}` remains the defined relationship, C is full-width and usable, A and B are hidden, the composite remains represented, and no relationship `SPLIT[exit]` occurs

#### Scenario: Third tab resumes the unchanged pair
- **WHEN** C is full-width with dormant relationship `{A, B}` and the user clicks either composite half
- **THEN** C is hidden, A remains LEFT, B remains RIGHT, both panes are presented immediately, and the clicked member becomes foreground

#### Scenario: An uncertain split hide fails the third-tab switch closed
- **WHEN** the user left-clicks C while `{A, B}` is split and hiding either A or B becomes recovery-pending
- **THEN** C does not become the active tab, the split remains authoritative, and TabDock re-presents A and B through the existing split layout path while preserving recovery evidence

#### Scenario: Hovering a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user hovers C without left-clicking it
- **THEN** the pair remains `{A, B}`, both panes stay visible/glued, C stays hidden, and no split exit or release occurs

#### Scenario: Right-clicking a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user opens and dismisses C's context menu
- **THEN** the pair remains active and visible and C remains a captured hidden non-member

### Requirement: Current split members SHALL not offer redundant split initiation
When a tab is a current member of the defined split relationship, its context
menu SHALL omit `Split screen` and SHALL retain `Exit split screen`, whether the
pair is presented or dormant. Non-member menu behavior SHALL remain unchanged
except for explicit reconfiguration operations.

#### Scenario: Paired member context menu
- **WHEN** the user right-clicks either member of `{A, B}` in presented or dormant state
- **THEN** `Split screen` is absent and `Exit split screen` remains present

### Requirement: Dormant relationship exit and invalidation preserve the active guest
Explicitly exiting a dormant `{A, B}` relationship SHALL clear the relationship,
restore ordinary tabs, and leave current non-member C visible and active.
Removing a member from a dormant relationship SHALL dissolve the composite while
leaving C visible and the surviving former member hidden. Removing a member from
a presented pair SHALL retain the existing survivor-promotion semantics.

#### Scenario: Dormant exit leaves the non-member visible
- **WHEN** C is full-width with dormant relationship `{A, B}` and the user explicitly chooses Exit split screen
- **THEN** `{A, B}` is cleared, C remains the full-width active guest, and A/B return as ordinary hidden captured tabs

#### Scenario: Dormant member removal leaves the non-member visible
- **WHEN** A is structurally removed while dormant `{A, B}` is presenting C
- **THEN** the composite is dissolved, C remains visible and active, and B remains a hidden ordinary captured tab

### Requirement: Split diagnostics distinguish relationship from presentation
Logical diagnostics SHALL report relationship-defined and pair-presented state
separately. Expected pane rectangles SHALL be populated only while the pair is
presented; dormant diagnostics SHALL identify the current full-width guest while
retaining LEFT/RIGHT relationship metadata.

#### Scenario: Dormant diagnostics expose relationship without pane expectations
- **WHEN** `{A, B}` is dormant while C is full-width
- **THEN** diagnostics report the pair relationship as defined, pair presentation as false, C as the active full-width guest, and no expected A/B pane rectangles

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

### Requirement: Rendering qualification SHALL measure client response, not only HWND geometry
Three-application qualification SHALL compare two versus three captured apps,
unsplit versus split, presented versus dormant, controlled windows versus any
available isolated Chromium-family windows, and repeated pair/non-member
transitions. A pass SHALL require client-observable resize/presentation state to
update before any corrective click inside the guest; outer HWND geometry alone
is insufficient. Missing browser installations SHALL be reported explicitly.

#### Scenario: Three-app client presentation settles without a guest click
- **WHEN** the harness compares unsplit, split, dormant, and resumed presentations for two and three controlled guests
- **THEN** each guest's post-message client evidence is recorded immediately after the transition and no first corrective guest click is required

#### Scenario: Isolated browser viewport evidence is explicit
- **WHEN** available Chromium-family browsers are driven through pair/non-member transitions using isolated profiles
- **THEN** the harness records client-reported viewport dimensions and resize counters, or reports missing browser coverage as BLOCKED_ENVIRONMENT

### Requirement: Guarded automated input SHALL prove current test-run ownership
Every automated mouse or keyboard operation that can affect a desktop window
SHALL be preceded by a current ownership proof for the intended target. The
proof SHALL bind a unique run identifier to the launched process identity
(PID, process-start identity, executable identity, and expected ancestry), then
to a discovered top-level HWND that is live, still owned by that process, and
marked for that run only after the process proof succeeds. The point target
SHALL be resolved through `WindowFromPoint` and `GA_ROOT` immediately before
input. A missing, stale, recycled, unrelated, or unverifiable root SHALL cause
the operation to fail closed without sending input.

PID, executable name, title text, UI Automation name, or a prior HWND value
alone SHALL NOT establish ownership. A recreated HWND SHALL be rediscovered
and revalidated; an HWND reused by another process SHALL be rejected. Browser
descendants SHALL be accepted only when they belong to the isolated browser
instance launched for the same run, never merely because the executable family
is installed.

#### Scenario: An unrelated overlay blocks a guarded click
- **WHEN** an unrelated window covers the intended test coordinate
- **THEN** the harness records the UIA/point/root/process/ancestry mismatch and
  sends no mouse or keyboard input

#### Scenario: A recreated test HWND is revalidated
- **WHEN** a controlled test process replaces its top-level HWND
- **THEN** the stale HWND is retired, the new HWND is accepted only after a
  fresh process and run-marker proof, and a recycled HWND from another process
  is rejected

### Requirement: Qualification results SHALL be machine-readable and tiered
The split qualification harness SHALL provide deterministic contract tests,
controlled HWND integration tests, guarded real-input tests, isolated browser
tests where available, bounded stress/failure-injection tests, and historical
comparison mode without weakening the input guard. Each scenario result SHALL
record its run identifier, scenario and iteration, candidate identity,
expected/observed state, relationship/presentation state, visible HWND and
geometry evidence, client-rendering evidence where applicable, and a bounded
failure artifact reference. Results SHALL be emitted as JSON and SHALL never
report a setup-stopped `0/N` run as a pass.

#### Scenario: A setup-stopped run is never reported as a pass

- **WHEN** a deterministic qualification scenario stops during setup
- **THEN** the harness emits a JSON result carrying the run identifier, scenario
  and iteration, candidate identity, expected/observed state,
  relationship/presentation state, visible HWND and geometry evidence,
  client-rendering evidence where applicable, and a bounded artifact reference,
  and records the run as blocked/failure rather than a passing `0/N`

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

### Requirement: Ordinary tab drag SHALL keep working while a split pair is dormant

While a split pair is defined but NOT presented, ordinary non-member tabs SHALL
remain fully draggable: reorder within the tab strip SHALL map drop positions
through the visible strip projection to authoritative tab order by slot item
identity (never by DisplayTabs↔Tabs positional arithmetic), and drag-out beyond
the container SHALL continue to release the dragged tab through the existing
release path. The split composite SHALL remain a deliberate non-drag unit in
both pair states, and SHALL remain a valid drop-boundary region so other tabs
can be reordered around the pair without dissolving or corrupting it. While the
pair IS presented, the composite remains the selected tab-strip unit and
ordinary-tab presses remain swallowed as today. Drop-target geometry SHALL be
snapshotted once per drag; reorders SHALL NOT invalidate that snapshot because
they change no collection counts, preserving the bounded-reorder-per-drag rule.

#### Scenario: Reordering an ordinary tab while a pair is dormant succeeds
- **WHEN** a split pair A|B is defined but a non-member C is active, and C is dragged past another non-member D in the strip
- **THEN** the reorder applies to the authoritative tab order, both collections agree, and the pair's member identities are unchanged

#### Scenario: The composite itself is never grabbed as a drag unit
- **WHEN** a press-and-drag begins on the split composite while the pair is defined (presented or dormant)
- **THEN** no tab-strip drag starts and neither member is released or moved by that gesture

#### Scenario: Drop boundaries resolve around the composite without index arithmetic
- **WHEN** a drop lands before, between, or after the composite slot regardless of how many non-members precede or follow the pair
- **THEN** the resulting authoritative insertion index equals the pair's LEFT member position or the neighbouring tab's live position, never a fixed offset

#### Scenario: Presented-pair behavior is unchanged
- **WHEN** a left press lands on an ordinary tab while the pair is presented
- **THEN** the press stays swallowed exactly as before this change and no drag begins

#### Scenario: Reorders do not resnapshot drag geometry
- **WHEN** repeated reorder moves occur during one drag with unchanged tab count
- **THEN** the drag-start slot snapshot remains authoritative for the whole drag and no flip-back oscillation can form; only a genuine structural count change may resnapshot
