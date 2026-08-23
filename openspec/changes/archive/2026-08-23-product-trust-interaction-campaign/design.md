## Context

The current product already has the relevant safety authorities: pending
recovery discovery parses source/ledger liveness and the supervised command owns
all recovery mutation; `GroupManager.SetCaptureAllowed` gates admission;
`TabNavigationPolicy` returns reference-identified targets; and
`SplitPresentationController` commits split policy results only after guarded
native work. The changes in `proposal.md` add observable projections and input
routes around those authorities. The container remains a WPF chrome window while
captured guests remain independent top-level HWNDs.

## Goals / Non-Goals

**Goals:**

- Make recovery attention, capture admission, global-navigation availability,
  and split actions visible and automation-addressable.
- Make global navigation prove current foreground ownership before selecting a
  container, and make local/global navigation share one operation.
- Make split discovery state-aware without duplicating split state or native
  presentation logic.
- Add deterministic seams that test identity, liveness, transitions, and
  accessibility projections without unattended synthesized input.

**Non-Goals:**

- No new recovery command, automatic legacy recovery, token installation,
  journal compaction, or in-app recovery mutation.
- No change to the Shepherd/no-reparent model, capture identity rules, split
  policy semantics, pane containment, z-order repair, or pop-out behavior.
- No Ctrl+Alt+Left/Right registration; those combinations are deliberately
  avoided because of graphics/display-driver collisions.
- No replacement of local Ctrl+Tab, no desktop-wide application switcher, and
  no attempt to navigate a remembered group when foreground ownership is
  unproven.

## Decisions

### 1. Project pending recovery from existing discovery

Add a launcher-attention projection in `PendingRecoveryService` that reuses the
existing catalog and `HasUnresolvedEvidence` calculation. It counts unresolved
pending files, not raw entries, because one pending source is the user-facing
recovery item and the existing command reports both file and entry counts.
Unreadable/corrupt files count as attention; a directory/read failure produces
an attention state with an unavailable-count message rather than a false zero.

The projection calls discovery in a read-only mode that skips the existing
age-gated temporary-fragment cleanup. This keeps simply constructing or
refreshing launcher state free of file mutation while preserving the command's
existing cleanup behavior and all ledger/source semantics. The banner carries
literal command text and has no recovery button.

### 2. Publish admission as a value plus event

`GroupManager` remains the only writer. It stores an immutable admission value
(`Allowed`, `Reason`) and raises a change event whenever either field changes,
including a reason update with the same boolean value. `MainViewModel`,
`GroupViewModel`, and `CapturePickerViewModel` subscribe/unsubscribe and expose
read-only projections. Their commands use the authority's boolean only for
`CanExecute`; the reason is presentation/help text, not a second rule.

Shortcut availability is held in separate view-model properties. Thus an owned
capture shortcut can be unavailable while direct capture remains enabled, and
a healthy shortcut cannot override a blocked admission. Existing monitor retry
calls continue to call `SetCaptureAllowed`; the UI simply observes the result.

### 3. Extend the message-only hotkey sink and resolve scope before navigation

`HotkeyService` keeps the existing capture/diagnostic sink and adds two IDs for
PageUp/PageDown with `MOD_CONTROL | MOD_ALT | MOD_NOREPEAT`. If either tab
registration fails, the pair is treated as unavailable and any partial tab
registration is removed; capture and diagnostic registration behavior remains
independent. An event carries direction, and a separate availability property
is projected for diagnostics/help.

The App handler reads the current foreground HWND and passes four proof
functions to a pure `GlobalTabNavigationPolicy`: resolve a captured member,
resolve that member's group, resolve a container/chrome group, and prove the
captured member is still current. A current captured guest takes precedence;
otherwise a live container or owned chrome maps to its own group. Null,
stale/recycled, ambiguous, closed, or unrelated results fail closed. The live
container additionally rejects active context menus, capture panels, rename
editors, close prompts, shutdown, and invalid HWNDs.

`ContainerWindow.NavigateTabs(bool backward)` contains the existing policy
invocation and target application. The local preview handler calls it, and the
global App handler calls it after scope resolution. It returns without a
presentation write for one-tab groups or unresolved targets. Presented split
member focus still routes through `FocusSplitMember`; dormant/member and
ordinary targets retain current controller/binding behavior.

### 4. Use a pure affordance projection over controller state

Add a native-free `SplitAffordancePolicy` that accepts the current tabs,
active reference, controller LEFT/RIGHT references, presented flag, and an
eligibility predicate. It returns a visible/enabled state and action records
(`CreatePair`, `FocusLeft`, `FocusRight`, `ResumeLeft`, `ResumeRight`,
`EndRelationship`) with references, never presentation indices. This is a
menu-projection/validation policy only; the controller remains the relationship
authority.

The container adds one persistent caption button and creates a short-lived
context menu from the projection. The action handler revalidates the action
against current tab membership, controller state, and Shepherd identity before
calling `StartSplitFrom`, `FocusSplitMember`, `ResumeSplitPair`, or `ExitSplit`.
The menu is tracked as chrome so global navigation and post-popup split settle
cannot re-enter it. A stale menu action therefore becomes a no-op, while a
member-removal event continues through the existing controller/member-removal
path.

### 5. Accessibility and documentation are part of the boundary

All new controls and dynamic actions receive stable AutomationIds and readable
names/help text. The launcher banner uses a warning treatment and explicit
command text but is not modal. Architecture/testing/README documentation will
describe the two new global combinations, their strict scope, the local
fallback, and the split affordance state model.

## Risks / Trade-offs

- **[Risk]** Pending discovery could accidentally perform existing temporary
  cleanup during launcher construction. → Use a read-only discovery option and
  test before/after bytes and directory entries; keep cleanup explicit to the
  supervised/diagnostic paths.
- **[Risk]** Global hotkeys could act on an arbitrary group or race a closed
  container. → Resolve current foreground on every notification, require live
  member/container proof, use the registry on the UI thread, and recheck
  chrome/closed state immediately before navigation.
- **[Risk]** WPF popup HWND ownership can make a menu appear eligible for global
  navigation. → Map owned chrome only through live root-owner/container checks
  and reject active chrome interactions in the container.
- **[Risk]** Dynamic menu items can retain view models after close. → Track one
  affordance menu, unsubscribe on close/container teardown, and keep action
  records reference-based and short-lived.
- **[Risk]** Admission transitions can leave a stale command enabled. → Raise
  property changes and command requery from the canonical manager event; add
  allowed/blocked/retry/failure transition tests for every projection.
- **[Risk]** Split UI could drift from existing context-menu semantics. → Keep
  the policy projection pure, use existing controller-facing operations only,
  and add source/authority tests forbidding direct split-state mutation in the
  affordance path.

## Migration Plan

1. Add the OpenSpec capability deltas and deterministic policy/event tests.
2. Implement service/view-model projections, hotkey registration/scope
   resolution, and container/launcher chrome.
3. Run the full deterministic and repeated stability gates; run supervised
   scenarios only on an exclusively controlled desktop.
4. If a release needs rollback, remove the new UI/hotkey registrations while
   leaving persisted recovery evidence and existing split/capture state formats
   untouched. No data migration is required.
