# Design — user-reported presentation integrity

## Context and evidence status

This change begins from a real user report and screenshots, not from a proven
root-cause analysis. Treat the following as **working hypotheses only**:

1. `BeginChromePopup` / `RaiseContainerForChrome` may elevate an opaque
   container HWND above an independently hosted guest, making the guest appear
   blank even though the guest is still visible and captured.
2. `IsContainerChromeInteractionActive` may suppress more than foreground
   stealing; if it also suppresses needed visual re-pairing, rename/menu/capture
   interactions can leave a correct-sized guest below the container.
3. Guest-originated maximize/fullscreen/monitor transitions may not always
   produce one of the currently observed native event paths, so the logical
   capture can survive while native presentation drifts.
4. The caption title may be centered only inside a remaining `*` grid column
   rather than around the actual client/window midpoint.

The implementation agent MUST attempt to disprove these hypotheses. A source
pattern that looks suspicious is not sufficient evidence to change native
behavior. Before an invasive fix, capture a minimal reproduction and an
observable before/after state showing the first violated invariant.

## Goals / Non-Goals

### Goals

- Preserve guest rendering and input while TabDock-owned chrome is being used.
- Keep foreground ownership and visual z-order as separate policy decisions.
- Detect and reconcile guest-originated presentation drift without polling or
  unbounded native mutation loops.
- Prevent a logically captured window from silently acting like an independent
  roaming window across maximize/fullscreen/monitor transitions.
- Keep owned menus/dialogs/editors/surfaces reachable above the pixels they need
  without covering the whole guest unnecessarily.
- Center the workspace title relative to the container itself, not to leftover
  caption space.
- Produce regression evidence that fails for the reported symptom before the
  fix and passes after it.

### Non-Goals

- No reparenting, owner reassignment of foreign guest HWNDs, or guest style
  mutation merely to simulate embedding.
- No permanent `HWND_TOPMOST` container and no global z-order enforcement
  against unrelated applications.
- No high-frequency unfiltered desktop hook/poll loop.
- No infinite "fight the app" loop when a guest intentionally resists geometry.
- No weakening of capture generation, executable/process-instance, recovery,
  or stale-HWND safety checks.
- No claim that the preliminary source findings are the final diagnosis.

## Investigation protocol

For each user report, create an investigation record under
`.agent/investigations/` containing:

- exact build/SHA, Windows version, monitor topology and DPI;
- exact guest application and whether its HWND, process, style/exstyle,
  placement, zoom/iconic/visible state, foreground state, monitor, and rect
  changed;
- container/content-host rects and local z-order chain;
- popup/editor/capture/split state and point ownership at representative
  coordinates;
- first observable divergence and the native/user action immediately preceding
  it;
- classification: reproduced product defect, adjacent defect, environment/
  capability limitation, harness defect, or not reproduced;
- accepted/rejected root-cause hypotheses and why.

Do not infer "released from the group" merely from visual escape. Verify the
authoritative `Group.Members` / captured identity and distinguish logical
membership from native presentation.

## Presentation model

The desired model has four distinct concepts:

1. **Logical capture authority** — whether a strong captured identity belongs
   to a group.
2. **Guest presentation** — the rect, visibility and local guest/container
   z-order that make the independent HWND look docked.
3. **Foreground/input authority** — which HWND should receive keyboard input
   now.
4. **TabDock transient chrome** — menus, rename editor, split actions, capture
   surfaces and owned dialogs that need temporary interaction priority.

A fix MUST NOT use one broad boolean or one global z-order action as a proxy for
all four unless evidence proves those states are equivalent for that path.

## Chrome interaction strategy

Required invariant: opening color/workspace/split menus, rename, tab menus,
dialogs, or the "+" capture workflow SHALL NOT make the guest content region
turn into the opaque container background merely as a side effect of giving
TabDock chrome interaction priority.

The agent must inspect the actual HWND composition and choose the smallest safe
mechanism. Candidate strategies include, but are not prescribed:

- elevate/own only the popup/dialog HWND that requires visibility;
- keep the guest above the opaque content host while retaining keyboard focus
  in TabDock chrome;
- for an in-window surface that consumes space (such as inline capture), adjust
  the guest's presentation region or composition so the surface is intentionally
  visible without covering unrelated guest pixels;
- replace the current container-wide "raise then globally suppress repair"
  model with scoped interaction state.

Whichever strategy is chosen must preserve accessibility, keyboard interaction,
split behavior, identity safety, and one bounded post-close reconciliation.
Nested/overlapping TabDock chrome interactions must not prematurely restore the
guest stack; use a scoped/ref-counted/tokenized model if the evidence requires
it rather than a fragile single boolean.

## Guest-originated maximize/fullscreen/monitor transitions

First determine the actual event/state sequence for:

- standard maximize/restore;
- F11-style application fullscreen;
- custom-drawn maximize/fullscreen controls;
- Win+Up / Win+Shift+Arrow and monitor transfer where safe to test;
- split and single-guest presentation;
- applications that change style/placement without entering an ordinary
  interactive move/size loop.

The current lack of `EVENT_OBJECT_LOCATIONCHANGE` is only one candidate
explanation. If a new event source is needed, admission must remain O(1) through
the captured-member index and delivery must be coalesced/bounded. Do not add a
desktop-wide high-frequency path without measuring callback/rejection cost.

Default desired behavior: a live, strongly identified captured guest remains
logically captured and visually constrained to its assigned full/split region.
A detected out-of-contract zoom/fullscreen/placement transition receives one
bounded identity-checked reconciliation through the existing presentation
authority. If a specific application repeatedly refuses the assigned geometry
and cannot be safely contained under the no-reparent/no-restyle contract,
TabDock must fail explicitly and boundedly rather than silently claiming a
healthy docked presentation or entering a resize/z-order war. The agent must
document and test the chosen fail-closed behavior.

## Z-order and topmost behavior

The local invariant concerns TabDock's own container, its captured guests, and
its owned chrome. It does not authorize fighting unrelated foreground windows.

Tests must cover ordinary and `WS_EX_TOPMOST` captured guests because they
occupy different z-order bands. A topmost guest must not force the container to
be permanently topmost. If a topmost guest makes a particular transient chrome
strategy impossible, the chosen policy must be explicit, bounded and
user-visible rather than flaky.

Point ownership (`WindowFromPoint` + root/identity) and local z-order walks
should verify the actual surface receiving clicks. Geometry equality alone is
not enough to declare rendering healthy.

## Caption centering

The workspace/group title's visual center should track the container client
midpoint, independent of the asymmetric controls on the left/right. Implement
this using a layout structure that gives the title an independent centered
layer/region rather than centering within leftover star-column space.

When available width is insufficient, the title may trim or constrain itself,
but it must not cover/disable the color chip, workspace selector, split control,
add button, minimize/maximize/close buttons, or resize affordances. The rename
editor follows the same centered region and preserves keyboard behavior.

## Validation strategy

### Deterministic/native-free

Add policy/state tests for any new chrome-interaction state machine, event
routing/coalescing decision, presentation-drift classifier, or bounded repair
decision. Include nested chrome, stale generation, destroyed/recycled HWND,
topmost-band classification, repeated location/state notifications, and
no-op-when-healthy cases.

### Controlled real-input / UIA

Add dedicated scenarios rather than relying on unrelated existing ones:

- `chrome-color-guest-visible`
- `chrome-rename-guest-visible`
- `chrome-split-menu-guest-visible`
- `chrome-add-panel-guest-visible`
- `guest-maximize-contained`
- `guest-fullscreen-contained`
- `guest-monitor-transfer-contained` (capability-gated)
- `topmost-guest-chrome-integrity`
- `workspace-title-centered`
- split variants of the chrome and guest-state cases where useful

At each interaction, assert both sides: TabDock chrome is visible/clickable AND
the guest content region remains live/presented as required. Use point
ownership/client-render evidence so a dark container covering a correctly sized
guest cannot pass. Physical input remains supervised and lease-gated.

## Decision gate

After reproduction, the agent may change the implementation plan in this
OpenSpec change when evidence contradicts the preliminary hypotheses. Preserve
the user-visible requirements and safety constraints, record the revised
reasoning, and do not keep a speculative mechanism merely because it was named
here.
