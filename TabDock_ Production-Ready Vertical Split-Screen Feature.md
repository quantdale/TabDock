/goal

# TabDock: Production-Ready Vertical Split-Screen Feature

You are working inside the **TabDock** repository.

Your objective is to completely design, implement, test, harden, document, and validate a new **vertical split-screen feature** for TabDock.

This is a long-running agentic implementation task.

Do NOT stop after making the first implementation work.

Continue autonomously until the feature is genuinely **production-ready**, all relevant tests pass, regressions have been investigated, documentation/specification is synchronized with implementation, and the final repository state is coherent.

The final outcome of this session should be:

> TabDock supports displaying exactly two captured application tabs simultaneously in a left/right vertical split, with a polished context-menu workflow, correct Shepherd lifecycle behavior, robust handling of edge cases, automated ValidationDriver coverage, and no regression to existing TabDock behavior.

You are running with **DeepSeek V4 Flash**, including DeepSeek V4 Flash secondary agents through Swarm Mode.

This model can be highly capable but may lose architectural context or hallucinate during long agentic runs.

Therefore this task has strict **evidence, waypoint, verification, and swarm-reconciliation requirements** described below.

Do not rely on conversation context alone.

Persist important state into the repository during the implementation so work can recover cleanly after context compaction.

---

# 0. OPERATING MODE

You are in `/goal` mode.

Continue working until the objective is actually complete.

Do not interpret:

- one successful build,
- one successful manual interaction,
- one passing test,
- or an apparently correct implementation

as completion.

You are finished only when the Production Readiness Gate near the end of this prompt is satisfied.

Work autonomously.

Do not repeatedly ask the user questions for implementation details that can be safely resolved from:

1. these requirements,
2. repository source,
3. existing TabDock conventions,
4. conservative product design.

If a minor ambiguity exists, choose the least surprising behavior, record the decision in the waypoint, implement it consistently, and test it.

Do not invent repository APIs, files, classes, or behavior.

Verify everything against source before relying on it.

---

# 1. FIRST ACTION: LEARN THE CURRENT REPOSITORY STATE

Before modifying code:

1. Read `AGENTS.md`.
2. Read `CLAUDE.md` if relevant, but remember source overrides stale documentation.
3. Read `README.md`.
4. Read `docs/ARCHITECTURE.md`.
5. Read `docs/TESTING.md`.
6. Inspect `openspec/`.
7. Inspect the current implementation of:
   - `App.xaml.cs`
   - `Views/ContainerWindow.xaml`
   - `Views/ContainerWindow.xaml.cs`
   - `Services/WindowShepherdService.cs`
   - `Services/GroupManager.cs`
   - `Services/GuestLifecycleService.cs`
   - `Services/WinEventMonitor.cs`
   - `Models/Group.cs`
   - `Models/CapturedWindow.cs`
   - `Models/PersistedState.cs`
   - `ViewModels/GroupViewModel.cs`
   - ValidationDriver scenarios relevant to tab switching, positioning, drag-out, minimize/restore, closing, persistence, and crash recovery.
8. Run:
   - `git status`
   - `git branch --show-current`
   - `git rev-parse HEAD`
   - relevant recent `git log`
9. Inspect whether there are newer changes than the project documentation assumes.

Do not trust documentation line numbers blindly.

The source tree is authoritative.

---

# 2. TABDOCK ARCHITECTURAL FOUNDATION

Understand this before touching the feature.

TabDock uses the **Shepherd model**.

Captured applications are NOT children of the TabDock window.

They remain ordinary independent top-level HWNDs.

TabDock creates the visual illusion that they live inside a container by positioning their top-level windows over the container content region and managing:

- position,
- visibility,
- z-order,
- lifecycle events,
- focus,
- tab state.

This feature MUST extend the Shepherd model.

## Absolute prohibition

DO NOT introduce:

- `SetParent`
- HWND reparenting
- guest style mutation to emulate child windows
- guest exstyle mutation to emulate child windows
- guest owner mutation
- screenshot-based rendering
- PrintWindow-based compositing
- embedded guest surfaces
- browser-specific integration

The split-screen feature must still operate by shepherding **two normal top-level guest windows**.

If your design requires reparenting, your design is wrong.

Stop and redesign it.

---

# 3. FEATURE DEFINITION

Implement **vertical split screen**.

"Vertical split" means:

```text
┌───────────────────────────────────────────────┐
│ TabDock tab strip / chrome                    │
├───────────────────────┬───────────────────────┤
│                       │                       │
│      LEFT WINDOW      │     RIGHT WINDOW      │
│                       │                       │
│                       │                       │
│                       │                       │
└───────────────────────┴───────────────────────┘
```

There are two panes:

- LEFT
- RIGHT

Do NOT implement top/bottom splitting.

Do NOT implement arbitrary grids.

Do NOT implement more than two simultaneously visible guest windows.

This feature is specifically a **2-way left/right vertical split**.

---

# 4. PRIMARY USER EXPERIENCE

The feature is initiated from a captured app tab.

The user right-clicks a tab.

The tab on which the context menu was invoked becomes the **LEFT pane**.

The user then chooses another tab for the **RIGHT pane**.

Example:

```text
Tabs:
Chrome | Terminal | VS Code
```

If the user right-clicks `Chrome` and selects:

```text
Split screen
    Terminal
    VS Code
```

then choosing `VS Code` creates:

```text
LEFT        RIGHT
Chrome      VS Code
```

The tab used to initiate split MUST always be the left pane for this workflow.

---

# 5. NUMBER-OF-TABS BEHAVIOR

The context-menu behavior depends on how many captured tabs currently exist in the group.

## One tab

If the group contains fewer than 2 eligible captured tabs:

- split screen cannot start;
- do not create empty panes;
- do not modify layout;
- do not hide/show anything unnecessarily.

Prefer making the Split Screen command visibly **disabled** rather than allowing it to fail after clicking.

The UI should make it obvious that a second tab is required.

Do not show misleading choices.

---

## Exactly two tabs

If exactly two eligible captured tabs exist:

Example:

```text
Chrome | Terminal
```

and the user right-clicks `Chrome`:

Selecting:

```text
Split screen
```

must automatically select `Terminal`.

Result:

```text
LEFT        RIGHT
Chrome      Terminal
```

Do NOT make the user choose from a redundant one-item submenu unless existing WPF/context-menu architecture makes a direct action significantly worse.

Preferred UX:

```text
Split screen
```

→ immediate split.

---

## Three or more tabs

If more than two eligible tabs exist:

Example:

```text
Chrome | Terminal | VS Code | Explorer
```

Right-clicking `Chrome` should expose something equivalent to:

```text
Split screen >
    Terminal
    VS Code
    Explorer
```

The initiating tab itself must not appear as a candidate partner.

Selecting a candidate puts:

- initiating tab → LEFT
- selected candidate → RIGHT

Use the visible/custom tab label according to the existing TabDock naming conventions.

Use existing icon infrastructure if doing so is straightforward and consistent with current menus; do not destabilize the implementation merely for menu icons.

---

# 6. SPLIT STATE

Design a clear runtime representation of split state.

Do not scatter booleans across `ContainerWindow.xaml.cs`.

There must be an understandable authoritative representation of:

- whether split mode is active;
- which captured member occupies LEFT;
- which captured member occupies RIGHT.

Prefer identity based on the actual existing runtime member model rather than fragile list indexes where possible.

Remember that:

- tabs can reorder;
- members can disappear;
- HWNDs can die;
- windows can pop out;
- the active index can change.

A tab reorder must not accidentally swap or destroy the split pair merely because indexes changed.

Avoid coupling split identity solely to `ActiveIndex`.

Inspect the domain model before choosing the representation.

---

# 7. SPLIT LAYOUT GEOMETRY

The existing full content region remains the source geometry.

Do not introduce DIP-based guest placement.

TabDock currently reasons about guest placement using physical screen pixels.

Preserve that architecture.

Given content rectangle:

```text
Left
Top
Width
Height
```

derive:

```text
LEFT RECT
RIGHT RECT
```

from that full rectangle.

Ensure:

- no overlap;
- no gaps unless a deliberate visual divider requires one;
- full available width is accounted for;
- odd pixel widths are handled deterministically;
- resizing the container updates both guests;
- moving the container updates both guests;
- maximizing/restoring updates both guests;
- monitor changes continue to work under the existing physical-pixel model.

A safe conceptual split is:

```text
leftWidth = totalWidth / 2
rightWidth = totalWidth - leftWidth
```

but inspect current geometry code and implement it consistently with existing conventions.

Do not introduce unnecessary DPI conversions.

---

# 8. VISUAL DIVIDER

Provide a clean vertical separation between the two panes if it can be implemented without compromising guest positioning.

The divider should visually communicate the split.

Do NOT build a draggable ratio/resizer unless it emerges as extremely low-risk and already fits existing architecture.

For this feature, the required split may remain **50/50**.

A production-ready fixed 50/50 split is preferable to an unstable draggable splitter.

Scope discipline matters.

---

# 9. SHEPHERD POSITIONING

The current single-visible-guest assumption will likely need careful extension.

Do not simply call the existing single-tab positioning method twice without understanding its z-order assumptions.

Investigate:

- `PositionAndShow`
- `PairZOrderBehind`
- foreground handling
- activation reassert
- tab switching
- `LayoutUpdated`
- `LocationChanged`
- `SizeChanged`
- hide/show logic

Determine how to correctly keep **both split guests visible and correctly z-ordered with the same container**.

Both guest HWNDs must visually remain over their corresponding pane without covering:

- the other pane,
- the TabDock chrome,
- menus,
- dialogs,
- capture picker.

Do not create two conflicting z-order loops.

There should be one coherent split-aware positioning policy.

---

# 10. EXISTING GEOMETRY INVARIANTS

Preserve these unless source investigation proves the architecture has since changed:

- guest geometry uses physical pixels;
- content-area coordinates come from Win32/client-to-screen calculations;
- no manual guest DPI transformation;
- `LocationChanged`, `SizeChanged`, and `LayoutUpdated` all participate in guest re-glue;
- the existing 1-pixel/no-op optimization should not be casually removed;
- hot-path positioning logging must remain cheap.

Do not optimize away behavior merely because it looks redundant.

Existing behavior may be compensating for WPF/Win32 ordering.

---

# 11. SPLIT MODE AND TAB VISIBILITY

Ordinary TabDock mode normally has one visible guest.

Split mode has exactly two visible guests.

This changes a fundamental assumption.

Audit every code path that assumes:

```text
active tab == the only guest that should be visible
```

Search comprehensively.

Do not patch only the visual placement code.

Investigate at minimum:

- tab switching
- hide logic
- guest lifecycle hide handling
- minimize handling
- foreground handling
- container minimize
- container restore
- release
- close
- pop-out
- self-close
- self-hide
- drag-out
- group close
- application shutdown
- emergency release
- crash recovery
- z-order repair
- name-change events

Create split-aware semantics rather than special-casing until tests happen to pass.

---

# 12. CLICKING TABS WHILE SPLIT IS ACTIVE

Unless repository conventions strongly justify a superior behavior, implement this conservative interaction model:

### Clicking one of the two split members

Keep split mode active.

Do not hide its partner.

The clicked member may become the logical/focused member as required by current active-tab semantics, but both remain visible.

### Clicking a third tab that is NOT part of the current split

Exit the current split and return to normal single-tab mode with the clicked tab becoming the single active visible guest.

This is simple, predictable, and prevents ambiguous three-window behavior.

Document this decision in the waypoint and tests.

If existing architecture makes another behavior materially safer or more intuitive, you may implement the alternative, but it must:

- never show more than two windows;
- be deterministic;
- be tested;
- be documented in the waypoint.

---

# 13. EXITING SPLIT SCREEN

The user needs an obvious way out.

Add an appropriate context-menu command such as:

```text
Exit split screen
```

when split mode is active.

It should be available from at least the split members.

Exiting split screen must:

- return to normal one-visible-tab behavior;
- keep a sensible active member visible;
- hide the other split member through the normal safe hide/journal path;
- not release either guest;
- preserve tab order;
- preserve captured membership.

Do not require the user to pop out or close a guest merely to exit split mode.

---

# 14. STARTING A DIFFERENT SPLIT WHILE ALREADY SPLIT

Behavior must be deterministic.

Preferred behavior:

If the user invokes Split Screen on a tab while split mode is already active:

1. treat that tab as the new LEFT member;
2. allow choosing the new RIGHT member according to the same rules;
3. transition cleanly from the previous pair to the new pair;
4. hide any guest no longer in the pair using the normal journal-safe path.

Never allow overlapping split states.

There is only one active split pair per container/group.

---

# 15. TAB REORDERING

Split identity must survive reordering.

Example:

```text
Before:
Chrome | Terminal | VS Code

Split:
Chrome LEFT
VS Code RIGHT
```

After reordering tabs:

```text
VS Code | Chrome | Terminal
```

the visible pair should still represent the same captured windows.

Do not use positional indexes as the sole durable runtime identity of split members.

Preserve the existing anti-oscillation drag behavior.

Do not recompute tab drag midpoints in a way that reintroduces the historical H2 oscillation bug.

---

# 16. POP-OUT / DRAG-OUT BEHAVIOR

If either split member is released from TabDock through:

- context-menu Pop out,
- tab drag-out,
- native title-bar drag-out,

the split cannot continue with a missing half.

Therefore:

1. end split mode safely;
2. release the chosen guest through the normal Shepherd release path;
3. keep the remaining guest captured;
4. make the remaining guest the normal single visible tab where reasonable.

No stale split reference may survive.

Test both:

- LEFT member released;
- RIGHT member released.

---

# 17. GUEST SELF-CLOSE / DESTROY

If either split guest closes itself:

- remove the dead member through the existing lifecycle mechanism;
- terminate split mode;
- leave the surviving member as the normal visible tab;
- maintain valid `ActiveIndex`;
- preserve correct HWND index bookkeeping.

If no members remain, retain existing group/container close behavior.

Do not add a separate destroy pipeline for split mode.

Extend existing lifecycle policy coherently.

---

# 18. GUEST SELF-HIDE

Existing TabDock hide classification is correctness-sensitive because TabDock itself intentionally hides inactive guests.

Audit this carefully.

Split members are both intentionally visible.

If one independently hides itself, the behavior should remain consistent with existing guest-initiated hide policy.

Do not accidentally interpret:

- TabDock hiding a former split member,
- container minimization,
- split transitions

as guest-initiated teardown.

The existing hide gating logic is load-bearing.

Extend it rather than bypassing it.

---

# 19. CONTAINER MINIMIZE / RESTORE

When TabDock's container is minimized:

- both split guests must disappear appropriately according to current container-minimize behavior.

When restored:

- both split guests must return;
- both must occupy their correct pane;
- neither should cover the other;
- split state must remain active.

Add automated coverage.

---

# 20. CONTAINER MOVE / RESIZE / MAXIMIZE

While split mode is active:

- move container → both guests follow continuously;
- resize container → both pane rectangles update;
- maximize container → both use the maximized content region;
- restore → both return correctly;
- switching monitors → geometry must remain coherent.

Do not let one window lag behind the other.

Preserve existing performance constraints.

---

# 21. FOREGROUND AND FOCUS

This is likely one of the hardest parts of the feature.

Two top-level guest windows are simultaneously visible.

Investigate current focus/z-order logic carefully.

Desired behavior:

- clicking LEFT should allow LEFT to receive input;
- clicking RIGHT should allow RIGHT to receive input;
- both remain visibly positioned;
- activating TabDock should not unexpectedly hide one;
- interacting with the tab strip should not permanently bury a pane;
- context menus/dialogs must appear above guests appropriately;
- direct guest click behavior should not break container/guest z-order pairing.

Do not invent an aggressive foreground polling loop.

Use existing event-driven Shepherd architecture.

Use Swarm reviewers specifically on focus/z-order semantics.

---

# 22. GLOBAL HOTKEY / CAPTURE PICKER

The existing global capture workflow must continue to work.

If a new guest is captured into a group that is currently split:

- do not automatically create a three-pane layout;
- preserve the existing split pair unless existing capture semantics require returning to normal mode;
- the new member should join the tab collection normally.

Choose the conservative behavior with the smallest surprise and test it.

The capture picker must continue filtering:

- own windows;
- already captured windows;
- invalid/elevated targets;
- cloaked/non-eligible windows

according to existing behavior.

---

# 23. CLOSE GROUP / APPLICATION EXIT

Audit closing behavior.

When closing:

- a split group;
- the application;
- during emergency release;
- during dispatcher failure;

both split guests must be handled through existing release/shutdown rules.

Do not create a shutdown-only special implementation unless necessary.

No split member may remain accidentally hidden on normal exit.

---

# 24. CRASH SAFETY

The hidden-window journal is correctness-critical.

The ordering invariant remains:

```text
write journal
THEN
hide guest
```

never the reverse.

When switching from a split pair back to a single visible tab, the departing guest must use the existing safe hide mechanism.

Do not bypass journaling because the guest "was just in split mode."

On force-kill:

- visible split guests should remain ordinary surviving top-level windows because Shepherd never reparents them;
- any intentionally hidden windows must remain recoverable by the existing journal behavior.

Do not regress crash recovery.

---

# 25. PERSISTENCE

Do not casually modify the persisted schema.

`state.json` currently represents layout/tab metadata and has a Version field with no mature migration architecture.

The split relationship is inherently tied to currently attached runtime guests.

Therefore the default design for this feature should be:

> **Split mode is runtime state and does not require persistence across application restart.**

That is acceptable for this scope unless source inspection proves that persistence is necessary for consistency.

If you conclude split state MUST be persisted:

STOP before editing the schema.

First implement a proper version-read/migration strategy.

Do not silently add fields to persisted DTOs and hope old data continues working.

Record the decision and rationale in the waypoint.

---

# 26. CONTEXT MENU DESIGN

Integrate with the existing tab context menu rather than creating a disconnected UI.

Expected conceptual menu:

### Not split, one tab

```text
Split screen     [disabled]
Pop out
...
```

### Not split, exactly two tabs

```text
Split screen
Pop out
...
```

### Not split, 3+ tabs

```text
Split screen >
    Tab B
    Tab C
    Tab D
Pop out
...
```

### Split active

Appropriate tabs should expose:

```text
Exit split screen
```

and potentially:

```text
Split screen >
```

for replacing the pair.

Follow existing WPF menu conventions.

Avoid unnecessarily complex dynamic binding if a straightforward robust implementation exists.

---

# 27. SWARM MODE: USE IT DELIBERATELY

Use Swarm Mode extensively, but do NOT create chaos by having multiple agents blindly edit the same architecture simultaneously.

The primary agent is the integrator and authority.

Use secondary DeepSeek V4 Flash agents for bounded tasks.

## Initial swarm

Delegate at least these independent investigations:

### Agent A — Architecture and state

Investigate:

- current active-tab assumptions;
- GroupManager;
- guest visibility;
- split-state design;
- HWND identity;
- lifecycle implications.

Return:

- relevant files/symbols;
- concrete risks;
- recommended state model;
- no code modifications unless explicitly assigned.

### Agent B — UI and interaction

Investigate:

- tab context menu;
- dynamic menus;
- tab click behavior;
- drag/reorder;
- pop-out;
- ContainerWindow architecture.

Return a proposed interaction integration with exact relevant source locations.

### Agent C — Shepherd / WinEvent lifecycle

Investigate:

- positioning;
- z-order;
- focus;
- hide/show;
- minimize;
- foreground events;
- destroy/hide/move-size behavior.

Identify every single-visible-guest assumption.

### Agent D — Testing

Investigate ValidationDriver.

Design concrete new scenarios and identify reusable helpers.

Determine:

- how two simultaneously visible guests can be asserted;
- how pane rectangles can be computed from TabDockContentHost;
- which existing regression scenarios must run.

---

# 28. SWARM EVIDENCE RULE

Every subagent report must provide evidence.

Require:

- file paths;
- class/method/symbol names;
- relevant behavior observed in source;
- uncertainties explicitly marked.

Do not accept statements such as:

> "There is probably a SplitManager."

unless the file and symbol actually exist.

If two agents disagree:

1. inspect the source yourself;
2. resolve the contradiction;
3. record the resolved fact in the waypoint.

Do not choose whichever answer sounds nicer.

---

# 29. SWARM EDITING POLICY

Prefer:

- subagents for reconnaissance;
- subagents for code review;
- subagents for test review;
- subagents for regression analysis.

The primary agent should integrate architecture-sensitive changes.

If parallel implementation is useful, assign only **non-overlapping ownership**, for example:

- primary → product architecture
- one agent → isolated ValidationDriver scenario file
- one agent → documentation/spec synchronization

Never allow simultaneous uncontrolled edits to:

- `ContainerWindow.xaml.cs`
- `GroupManager.cs`
- `GuestLifecycleService.cs`
- `WindowShepherdService.cs`

Merge/review every subagent result before accepting it.

---

# 30. ANTI-HALLUCINATION WAYPOINT SYSTEM

This is mandatory.

Long DeepSeek sessions can lose context.

Create a persistent waypoint file immediately after initial reconnaissance.

First check whether the repository already defines a sanctioned agent waypoint / ExecPlan / worklog convention.

Use that if it exists.

Otherwise create:

```text
docs/internal/split-screen-implementation-waypoint.md
```

This file is the authoritative working memory for this `/goal` session.

---

# 31. WAYPOINT CONTENT

Keep the waypoint concise but information-dense.

Use this structure:

```markdown
# Split Screen Implementation Waypoint

## Objective

## Baseline
- branch:
- starting HEAD:
- current HEAD:
- working tree:

## Confirmed Requirements

## Confirmed Architecture Facts

## Critical Invariants

## Current Design

## Decisions Made
- decision
- reason
- evidence

## Files Modified

## Tests Added

## Tests Passed

## Tests Failing

## Known Regressions / Risks

## Swarm Findings
### Accepted
### Rejected / superseded

## Current Implementation Status

## Exact Next Action

## Remaining Production-Readiness Gates
```

Do not turn the waypoint into a verbose diary.

It is a recovery mechanism.

---

# 32. WHEN TO UPDATE THE WAYPOINT

Update it:

1. immediately after repository reconnaissance;
2. after the initial architecture design;
3. after each major implementation milestone;
4. after receiving important swarm findings;
5. before making a risky architectural change;
6. after a failed test that changes your understanding;
7. after a significant bug fix;
8. before any expected context compaction;
9. before starting a new `/goal` iteration;
10. before final validation.

After context compaction or whenever your understanding feels uncertain:

DO NOT GUESS.

First read:

- the waypoint;
- `git status`;
- `git diff`;
- relevant source.

Then continue.

---

# 33. CURRENT-STATE RECONCILIATION LOOP

At every major milestone execute this mental/repository loop:

```text
READ waypoint
    ↓
CHECK git status + diff
    ↓
VERIFY current source
    ↓
STATE what is actually implemented
    ↓
COMPARE against requirements
    ↓
IDENTIFY smallest next missing milestone
    ↓
IMPLEMENT
    ↓
TEST
    ↓
UPDATE waypoint
```

Never continue for dozens of changes based solely on remembered context.

---

# 34. IMPLEMENTATION PHASES

Use phases.

Do not jump directly into large code changes.

## Phase 1 — Reconnaissance

Understand source and invariants.

Launch initial swarm investigations.

Create waypoint.

---

## Phase 2 — Architecture

Produce a concrete split-screen design.

Before implementation, determine:

- split state representation;
- ownership of split state;
- left/right identity;
- visibility policy;
- positioning policy;
- z-order policy;
- tab-click semantics;
- split exit semantics;
- lifecycle semantics;
- persistence decision;
- testing strategy.

Record it.

---

## Phase 3 — Minimal Core

Implement:

- split state;
- left/right rectangle calculation;
- two-guest positioning;
- enter/exit split programmatically.

Build immediately.

Do not add all UI first.

---

## Phase 4 — User Interface

Implement:

- context-menu command;
- disabled behavior for one tab;
- automatic partner for exactly two;
- candidate submenu for 3+;
- exit split.

Verify manually and automatically.

---

## Phase 5 — Lifecycle Integration

Handle:

- tab switching;
- reordering;
- pop-out;
- drag-out;
- self-close;
- self-hide;
- minimize;
- restore;
- foreground;
- container close;
- app exit.

---

## Phase 6 — Automated Tests

Add comprehensive ValidationDriver scenarios.

Run targeted tests repeatedly.

---

## Phase 7 — Regression Hardening

Run existing related scenarios and broader validation.

Use swarm reviewers.

Investigate every credible regression.

---

## Phase 8 — Documentation / Specs

Synchronize:

- architecture docs where behavior changed;
- testing docs;
- OpenSpec capability/spec workflow if required;
- AGENTS.md only if a new invariant/convention genuinely needs agent awareness.

Do not add stale line-number-heavy documentation.

---

## Phase 9 — Production Review

Perform independent swarm reviews and final gate.

---

# 35. TEST REQUIREMENTS

Add automated ValidationDriver coverage.

At minimum, create coverage equivalent to the following.

Names may follow repository naming conventions.

## Scenario 1 — Single tab blocks split

```text
split-single-disabled
```

Assert:

- split cannot start;
- guest remains full-width;
- no erroneous state change.

---

## Scenario 2 — Two-tab automatic split

```text
split-two-auto
```

Capture two guests.

Invoke Split Screen on guest A.

Assert:

- A occupies LEFT;
- B occupies RIGHT;
- both visible;
- neither covers the other's pane;
- both remain captured.

---

## Scenario 3 — Three-tab partner selection

```text
split-select-partner
```

Capture A/B/C.

Invoke split on A.

Choose C.

Assert:

```text
A = LEFT
C = RIGHT
B = hidden/inactive
```

according to split semantics.

---

## Scenario 4 — Exit split

```text
split-exit
```

Assert:

- returns to normal single-guest layout;
- chosen active guest uses full content rect;
- other remains captured but hidden;
- no guest is released.

---

## Scenario 5 — Container resize

```text
split-resize
```

Resize container repeatedly.

Assert both windows track calculated pane rectangles.

---

## Scenario 6 — Container move

```text
split-move
```

Move the container.

Assert both guests remain glued to their panes.

---

## Scenario 7 — Minimize / restore

```text
split-minrestore
```

Assert both disappear and return correctly.

---

## Scenario 8 — Reorder

```text
split-reorder
```

Reorder tabs while split.

Assert the same captured HWNDs remain LEFT/RIGHT.

Also ensure the historical drag oscillation regression is not reintroduced.

---

## Scenario 9 — Left pop-out

```text
split-popout-left
```

Assert split terminates safely.

---

## Scenario 10 — Right pop-out

```text
split-popout-right
```

Assert split terminates safely.

---

## Scenario 11 — Guest self-close

```text
split-selfclose
```

Close one split guest from inside the guest.

Assert:

- dead member removed;
- surviving guest becomes normal full-width;
- no stale split state.

---

## Scenario 12 — Guest title-bar drag-out

```text
split-titlebar-dragout
```

If reliably automatable with current harness.

---

## Scenario 13 — Third-tab click

```text
split-click-third
```

If following the recommended behavior:

- A/B split;
- click C;
- split exits;
- C becomes single visible guest.

---

## Scenario 14 — Focus interaction

```text
split-directclick
```

Interact with both left and right windows.

Verify both remain visually docked and accept input.

---

## Scenario 15 — Repeated cycles

```text
split-repeat-cycles
```

Enter/exit split repeatedly.

Look for:

- stale state;
- hidden guests;
- incorrect indexes;
- geometry drift;
- journal anomalies;
- excessive log churn.

Run multiple cycles.

---

# 36. RECTANGLE ASSERTIONS

Extend existing Shepherd-style docking assertions rather than testing parent-child relationships.

Do NOT assert `GetParent`.

For split mode derive expected rectangles from the actual TabDock content host rectangle.

Example conceptually:

```text
content = TabDockContentHost screen rect

expectedLeft:
    left = content.Left
    top = content.Top
    width = floor(content.Width / 2)
    height = content.Height

expectedRight:
    left = expectedLeft.Right
    top = content.Top
    width = content.Width - expectedLeft.Width
    height = content.Height
```

Use the repository's existing tolerance conventions.

Assert each guest against its expected pane.

---

# 37. EXISTING REGRESSION TESTS TO RUN

After split-specific tests pass, run relevant existing scenarios including equivalents of:

- tab switching hide safety;
- instant tab switching;
- drag reorder;
- chrome tab drag;
- popout;
- inactive popout;
- dragout by titlebar;
- self close;
- self hide;
- self minimize;
- minimize/restore;
- container minimize retains tabs;
- direct-click foreground pairing;
- double-capture refusal;
- close group prompt;
- exit populated;
- crashkill rescue;
- hotkey after close;
- hotkey hold single picker;
- persistence active-tab index;
- restored group/member lifecycle tests.

Use the actual scenario names from current source.

Do not assume the reconnaissance report is still current.

---

# 38. BUILD GATES

At minimum run the current equivalents of:

```powershell
dotnet build TabDock.csproj
dotnet build TabDock.sln

dotnet build tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj
dotnet build tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj
```

and:

```powershell
.\scripts\validate.ps1
```

if still current and safe.

Remember the ValidationDriver's current path/configuration quirks.

Inspect current scripts before running commands.

---

# 39. REAL-INPUT HARNESS SAFETY

The ValidationDriver uses actual UI input.

Respect all repository guardrails.

Do NOT create uncontrolled retry loops.

Do NOT spawn processes without the repository's guarded spawning pattern.

Do NOT bypass:

- process caps;
- mutexes;
- timeouts;
- cleanup;
- user-input safety mechanisms.

If a foreground-sensitive test fails under an environment known to make foreground acquisition flaky, investigate and rerun it under the proper standalone conditions before declaring a product regression.

---

# 40. PERFORMANCE

Split mode doubles the number of actively positioned guest HWNDs.

Be mindful of hot paths.

Do not introduce:

- repeated process queries per layout tick;
- repeated icon extraction;
- repeated enumeration of all groups;
- repeated expensive Win32 description calls;
- synchronous filesystem work in positioning loops;
- aggressive polling.

Use the existing O(1) captured-member index.

Keep positioning operations direct and predictable.

---

# 41. LOGGING

Add diagnostics only where they materially help.

A useful split event vocabulary might be conceptually:

```text
SPLIT[enter]
SPLIT[exit]
SPLIT[replace]
SPLIT[member-gone]
```

Follow existing logging conventions.

Do not flood the log every frame.

Keep the existing hot-path `SHEPHERD[position]` behavior intact if tests depend on it.

Do not add expensive information gathering to per-position logging.

---

# 42. CODE QUALITY

Do not solve this by adding hundreds more unstructured lines into `ContainerWindow.xaml.cs` if a small, clear extraction can reduce complexity.

At the same time:

Do NOT perform a giant unrelated refactor.

Use targeted structure.

Potentially appropriate abstractions might include:

- split-layout state;
- pane rectangle calculation;
- split visibility policy;
- split transition helper.

But inspect the architecture first.

Do not create unnecessary enterprise abstractions.

The goal is:

```text
minimal architecture
+
clear ownership
+
testability
+
low regression risk
```

---

# 43. DO NOT OPPORTUNISTICALLY FIX EVERYTHING

The repository has known debt.

This feature task is not permission to rewrite TabDock.

Do not simultaneously attempt to fix:

- every dead method;
- all stale docs;
- every P/Invoke declaration;
- CI architecture;
- installer support;
- accessibility project-wide;
- persistence redesign;
- unrelated lifecycle issues.

Fix unrelated defects only when:

1. they directly block split screen;
2. they are exposed by split-screen testing;
3. leaving them unfixed prevents production readiness.

Record such fixes explicitly.

---

# 44. OPEN SPEC / PROJECT WORKFLOW

The repository uses OpenSpec.

Inspect current project conventions.

If feature development is expected to create an OpenSpec change/proposal, do so.

Do not blindly invent an OpenSpec command.

Inspect existing:

```text
openspec/config.yaml
openspec/specs/
openspec/changes/archive/
```

and agent instructions.

The final implemented capability/spec should accurately represent the feature.

If tool-generated agent mirrors exist, update the canonical source only and run the sanctioned synchronization script.

Do not hand-edit generated mirrors.

---

# 45. MID-IMPLEMENTATION REVIEW

Once the basic feature and primary scenarios pass, launch a second swarm review.

Assign independent agents:

### Reviewer 1 — Shepherd invariants

Ask:

> Find any place this split-screen implementation violates or weakens the Shepherd model, window lifecycle, journal-before-hide rule, z-order policy, physical-pixel geometry, elevation guard, HWND indexing, or WinEvent semantics.

### Reviewer 2 — State correctness

Ask:

> Search for stale split references, index-based identity bugs, member removal edge cases, tab reorder problems, invalid ActiveIndex transitions, and any path that can leave three guests visible.

### Reviewer 3 — UI regression

Ask:

> Review tab context menu, tab switching, drag/reorder, pop-out, close behavior, custom chrome, and interaction semantics for regressions caused by split screen.

### Reviewer 4 — Testing gaps

Ask:

> Attempt to break split mode. Identify missing ValidationDriver cases, race conditions, lifecycle transitions, and assertions that could pass vacuously.

Require evidence.

Reconcile every credible finding.

Update waypoint.

---

# 46. TEST FAILURE POLICY

Never patch blindly to make a test green.

For every meaningful failure:

1. reproduce;
2. identify whether the failure is:
   - implementation bug;
   - test bug;
   - environment flake;
   - stale assumption;
3. inspect relevant source/logs;
4. fix the actual cause;
5. rerun targeted test;
6. rerun adjacent regressions;
7. update waypoint if understanding changed.

Do not weaken assertions simply because a test is inconvenient.

---

# 47. REPEAT STABILITY TESTING

Once targeted scenarios are green, run split enter/exit cycles repeatedly.

Use the harness's existing `--cycles` facility where applicable.

Exercise:

```text
enter split
exit split
switch
enter another split
resize
move
minimize
restore
pop out
re-capture where appropriate
```

Look for intermittent problems.

One successful cycle does not demonstrate stability.

---

# 48. FINAL DIFF REVIEW

Before declaring completion:

Run:

```text
git status
git diff
```

and inspect every change.

Ask:

- Is every modification related to the feature or a necessary supporting fix?
- Did any debug instrumentation remain?
- Are there accidental generated files?
- Are there temporary screenshots/logs?
- Did an agent change configuration unnecessarily?
- Did swarm agents duplicate work?
- Did documentation drift from implementation?
- Did persisted schema accidentally change?
- Did any forbidden reparenting/style mutation appear?

Search explicitly for any newly introduced:

```text
SetParent
SetWindowLong
GWL_STYLE
GWL_EXSTYLE
```

or equivalent guest-style manipulation.

If any exist, justify them.

For guest containment, the correct answer should almost certainly be that none were introduced.

---

# 49. PRODUCTION READINESS GATE

Do NOT finish `/goal` until all applicable items are true.

## Product

- [ ] User can initiate vertical split by right-clicking a tab.
- [ ] Initiating tab becomes LEFT.
- [ ] Exactly two tabs auto-select the other.
- [ ] 3+ tabs allow selecting the RIGHT partner.
- [ ] One tab cannot enter split.
- [ ] Exactly two guests are visible in split.
- [ ] User can exit split.
- [ ] Split remains correct during move/resize/maximize/restore.
- [ ] Both panes accept interaction.
- [ ] Reordering does not corrupt pair identity.
- [ ] Pop-out/drag-out terminates split cleanly.
- [ ] Guest self-close terminates split cleanly.
- [ ] Clicking a non-paired tab has deterministic tested behavior.
- [ ] Container minimize/restore preserves split correctly.
- [ ] Group/application close releases guests correctly.

## Architecture

- [ ] No HWND reparenting introduced.
- [ ] No guest style/exstyle/owner mutation introduced for split.
- [ ] Split state has one clear owner.
- [ ] No fragile index-only split identity.
- [ ] HWND index invariants preserved.
- [ ] WinEvent handling remains O(1) where required.
- [ ] Journal-before-hide ordering preserved.
- [ ] Physical-pixel geometry preserved.
- [ ] No new polling architecture.
- [ ] No third guest can accidentally remain visible.

## Quality

- [ ] Main app builds cleanly.
- [ ] Solution builds cleanly.
- [ ] ValidationDriver builds.
- [ ] GuineaPig builds.
- [ ] `validate.ps1` passes if applicable.
- [ ] Split-specific scenarios pass.
- [ ] Relevant historical regression scenarios pass.
- [ ] Repeat-cycle testing passes.
- [ ] No unexplained intermittent failures remain.
- [ ] No temporary implementation scaffolding remains.

## Documentation

- [ ] Relevant architecture documentation updated.
- [ ] Testing documentation updated where required.
- [ ] OpenSpec capability synchronized if repository workflow requires it.
- [ ] New architectural invariants documented if genuinely necessary.
- [ ] Agent-config mirrors synchronized through sanctioned tooling if touched.

## Review

- [ ] Independent swarm Shepherd review complete.
- [ ] Independent swarm state review complete.
- [ ] Independent swarm UI review complete.
- [ ] Independent swarm test-gap review complete.
- [ ] Credible findings resolved or explicitly justified.
- [ ] Final `git diff` reviewed.
- [ ] Final waypoint reflects reality.

Only after these gates are satisfied may `/goal` terminate.

---

# 50. FINAL WAYPOINT

Before finishing, update the waypoint one final time.

It must state:

```text
Implementation: COMPLETE
Production readiness: PASS
```

only if that is genuinely true.

Include:

- final architecture;
- files changed;
- tests added;
- tests executed;
- results;
- known accepted limitations;
- remaining unrelated project debt;
- current HEAD/worktree state.

If the waypoint was intended only as temporary agent scratch and repository conventions do not permit keeping it, first transform any durable information into the appropriate architecture/spec/testing documentation, then remove the temporary waypoint before completion.

Do not delete it before all durable information has somewhere authoritative to live.

---

# 51. FINAL RESPONSE FORMAT

When `/goal` genuinely completes, return a detailed final report containing:

## Implementation Result

What was implemented.

## User Experience

Exactly how split screen works.

## Architecture

How two simultaneous top-level HWNDs are shepherded safely.

## Important Design Decisions

Especially state ownership, tab switching, split exit, and persistence.

## Files Changed

List each meaningful file and why.

## Tests Added

List every new scenario.

## Tests Run

List commands/scenarios and results.

## Regression Validation

Existing scenarios executed and outcomes.

## Swarm Review

What reviewers investigated and significant findings.

## Production Readiness

Explicit PASS/FAIL against the gate.

## Remaining Limitations

Only genuine known limitations.

## Repository State

- branch
- HEAD
- `git status`
- whether there are uncommitted changes

Do not merely say:

> Done.

Provide evidence.

---

# 52. FINAL MINDSET

The feature is not successful merely because two windows can be resized side-by-side once.

It is successful when split screen behaves as a native extension of TabDock's Shepherd architecture under:

- normal use,
- resizing,
- movement,
- focus changes,
- tab switching,
- tab reordering,
- pop-out,
- native drag-out,
- self-close,
- minimize/restore,
- shutdown,
- crash safety,
- repeated cycles.

DeepSeek V4 Flash may forget earlier assumptions during a long run.

The waypoint exists specifically to prevent that.

Use it.

The swarm exists to challenge your conclusions, not to amplify them.

Use it deliberately.

Source code is authoritative.

Evidence beats memory.

Tests beat assumptions.

Do not stop until the **TabDock vertical split-screen feature is production-ready**.