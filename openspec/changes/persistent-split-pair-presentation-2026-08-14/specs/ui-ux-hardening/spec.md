# Persistent split pair presentation

## MODIFIED Requirements

### Requirement: A split relationship persists across ordinary non-member selection

Once a split relationship `{LEFT, RIGHT}` is defined, it SHALL remain defined
until explicit exit, explicit split reconfiguration, or structural invalidation
of a member. The relationship and its presentation SHALL be separate states.

While the pair is presented, exactly LEFT and RIGHT SHALL be visible in their
original panes and the focused member SHALL be the logical active member.

When a user selects a non-member C, TabDock SHALL journal-safely hide LEFT and
RIGHT, retain the relationship and composite projection, present C as the only
visible full-width guest, and make C the logical active guest. This is a
dormant-pair state, not a relationship exit. If either hide is recovery-pending,
the pair SHALL remain authoritative and SHALL be re-presented; C SHALL NOT
become selected.

Selecting either composite half SHALL journal-safely hide C, restore the exact
same LEFT/RIGHT pair, preserve pane identity, and focus the clicked member. No
new Split screen command is required.

#### Scenario: Pair to third tab suspends without teardown

- **WHEN** `{A, B}` is presented and the user clicks non-member C
- **THEN** `{A, B}` remains the defined relationship, C is full-width and
  usable, A and B are hidden, the composite remains represented, and no
  relationship `SPLIT[exit]` occurs

#### Scenario: Third tab resumes the unchanged pair

- **WHEN** C is full-width with dormant relationship `{A, B}` and the user
  clicks either composite half
- **THEN** C is hidden, A remains LEFT, B remains RIGHT, both panes are
  presented immediately, and the clicked member becomes foreground

#### Scenario: Repeated non-member switching retains the pair

- **WHEN** `{A, B}` is dormant and the user switches C -> D -> C -> composite
- **THEN** each non-member is the only visible full-width guest and the final
  composite activation restores the original `{A, B}` relationship

### Requirement: Split member context menus describe the existing relationship

When a tab is a current member of the defined split relationship, its context
menu SHALL omit `Split screen` and SHALL retain `Exit split screen`. This applies
whether the pair is presented or dormant. Non-member menu behavior SHALL remain
unchanged except for explicit relationship-aware operations already supported.

#### Scenario: Presented member menu omits split initiation

- **WHEN** the user opens the context menu on A or B while `{A, B}` is presented
- **THEN** `Split screen` is absent and `Exit split screen` is present

#### Scenario: Dormant member menu omits split initiation

- **WHEN** C is full-width and the user opens the composite half menu for A or B
- **THEN** `Split screen` is absent and `Exit split screen` is present

### Requirement: Dormant exit and structural invalidation preserve the active non-member

Explicitly exiting a dormant relationship SHALL clear the relationship and
restore ordinary individual tabs while leaving the current non-member visible
and active. Removing a member from a dormant relationship SHALL dissolve the
composite without activating or releasing the surviving former member; the
current non-member SHALL remain visible. Removing a member from a presented
pair SHALL retain existing survivor promotion semantics.

#### Scenario: Dormant exit leaves the non-member visible

- **WHEN** A/B is a dormant relationship and C is the current full-width guest,
  and the user invokes `Exit split screen` from the A/B composite
- **THEN** the A/B relationship is cleared, C remains the visible active guest,
  A and B become ordinary hidden captured tabs, and no captured process is
  released

#### Scenario: Dormant member removal leaves the non-member visible

- **WHEN** A/B is dormant, C is full-width, and A or B is structurally removed
- **THEN** the composite is dissolved, C remains visible and active, the
  surviving former member becomes an ordinary hidden captured tab, and no
  relationship teardown activates the former member

### Requirement: Split diagnostics distinguish relationship and presentation

Logical diagnostics SHALL report relationship-defined and pair-presented state
separately. Expected pane rectangles SHALL be populated only when the pair is
presented. In dormant state the current non-member SHALL be reported as the
expected full-width guest while LEFT/RIGHT remain observable as relationship
metadata.

#### Scenario: Dormant diagnostics identify a single guest

- **WHEN** A/B remains defined but C is the current full-width guest
- **THEN** the diagnostic snapshot reports a defined relationship and
  `SplitPresented=false`, reports C as the expected full-width guest, and does
  not report A or B as expected visible panes

### Requirement: Presentation settle follows the current desired mode

Queued split creation settle work SHALL verify that the relationship still
exists and that pair presentation is still desired before relayout or
foreground. A non-member selection that suspends the pair SHALL invalidate or
disarm stale settle work; generic relayout SHALL never revive a dormant pair.

#### Scenario: Stale split settle cannot revive a dormant pair

- **WHEN** split creation queues presentation settle work and the user selects
  non-member C before that work executes
- **THEN** the queued work observes that single-guest presentation is desired,
  leaves A/B hidden, and does not foreground or lay out the dormant pair

## ADDED Requirements

### Requirement: Rendering qualification measures client response

Three-application qualification SHALL compare two versus three captured apps,
unsplit versus split, presented versus dormant, controlled windows versus any
available isolated Chromium-family windows, and repeated pair/non-member
transitions. A pass SHALL require client-observable resize/presentation state
to update before any corrective click inside the guest; outer HWND geometry alone
is insufficient. Unavailable browser coverage SHALL be reported explicitly.

#### Scenario: Controlled three-app presentation settles without a guest click

- **WHEN** controlled windows are captured as A/B, a third window C is added,
  A/B is split, and the qualification inspects A/B immediately without clicking
  inside either guest
- **THEN** each client reports the presentation/resize transition before any
  corrective guest click, and the same evidence is collected for C -> dormant
  pair -> restored pair transitions

#### Scenario: Isolated browser coverage is explicit

- **WHEN** isolated Chrome-family windows are available for the qualification
- **THEN** their outer rectangles and client-reported viewport dimensions or
  resize counters are recorded for unsplit, split, dormant, and resumed states;
  otherwise the report records `BLOCKED_ENVIRONMENT` for the unavailable browser
  portion without claiming a pass

### Requirement: Guarded qualification proves current test-run input ownership

Every automated input operation SHALL first prove the intended root HWND belongs
to the current run. The proof SHALL bind a unique run identifier to the launched
process PID, process-start identity, executable identity, expected ancestry, and
then to a live top-level HWND marked for that run only after process provenance
has succeeded. Immediately before input the harness SHALL resolve
`WindowFromPoint` to `GA_ROOT` and revalidate the current registration. Missing,
stale, recycled, unrelated, or unverifiable identity SHALL fail closed and send
no input. PID, executable name, title, UIA name, or an old HWND value alone is
not sufficient.

#### Scenario: Unrelated occlusion is a guarded failure

- **WHEN** an unrelated desktop window covers a test target coordinate
- **THEN** the harness emits a privacy-safe machine-readable identity diagnostic
  and sends no click or key

#### Scenario: Browser descendants stay within the isolated run

- **WHEN** a browser creates renderer or browser-process descendants for an
  isolated test profile
- **THEN** only descendants proven to belong to the launched isolated instance
  and its run-marked top-level HWNDs are accepted; existing personal browser
  windows are rejected

### Requirement: Split qualification is tiered and machine-readable

The change SHALL provide deterministic state-contract tests, controlled HWND
integration tests, guarded UI input tests, isolated browser tests where a
browser is installed, bounded stress/failure-injection tests, and isolated
historical comparison. Every scenario SHALL emit JSON containing the run ID,
scenario/iteration, candidate identity, expected and observed state,
relationship/presentation state, visible HWND/geometry evidence, client resize
evidence where applicable, and a bounded diagnostic/artifact reference. A run
that stops during setup SHALL be reported as blocked/failure rather than a
passing `0/N` result.

#### Scenario: Deterministic scenario emits machine-readable evidence

- **WHEN** a deterministic qualification scenario (for example the geometry
  self-test) runs to completion
- **THEN** its result JSON carries the run identifier, scenario and iteration,
  candidate identity, expected/observed state, relationship/presentation state,
  visible HWND and geometry evidence, client-resize evidence where applicable,
  and a bounded diagnostic/artifact reference; a run that stops during setup is
  reported as blocked/failure rather than a passing `0/N` result
