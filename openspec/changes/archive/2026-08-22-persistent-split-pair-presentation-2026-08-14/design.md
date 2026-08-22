# Design: two-level split state

## State ownership

`ContainerWindow` remains the sole owner of the runtime relationship and native
presentation authority:

- `_splitLeft` and `_splitRight` identify the relationship by captured-window
  reference.
- `_splitPairPresented` records whether those members are the current visible
  two-pane set.
- `_splitForeground` remembers the last focused member across suspension.
- `_shepherdActiveWindow` is a pair member when the pair is presented and is the
  selected non-member when the relationship is dormant.

`GroupViewModel.DisplayTabs` remains the only tab-strip projection. The existing
composite is retained while the relationship exists, so dormant state renders
`[A|B] [C]` without a second membership source of truth.

## Transitions

- Enter/explicit reconfigure: journal-hide any departing visible guests,
  define LEFT/RIGHT, mark pair presented, install the existing composite, and
  layout/settle the pair.
- Suspend: journal-hide both members while the pair remains authoritative. If
  either hide is recovery-pending, re-present the pair and leave the selected
  non-member unchanged. After both succeed, mark presentation dormant, retain
  the composite and focused-member identity, and present the requested
  non-member full-width.
- Resume: journal-hide the current non-member, fail closed if that hide is
  uncertain, mark the pair presented, preserve LEFT/RIGHT identity, and focus
  the clicked member through the canonical member-focus operation.
- Explicit exit: clear the relationship and composite. In dormant state keep
  the current non-member visible and active; in presented state preserve the
  existing survivor semantics.
- Structural removal: clear an invalid relationship. Presented state promotes
  the surviving member as before; dormant state keeps the current non-member
  visible and leaves the surviving former member hidden and ordinary.

## Presentation-dependent callers

Relayout, size constraints, minimize/restore, move/size reconciliation,
foreground pairing, Ctrl+Tab pair cycling, expected diagnostics, and the
post-popup settle must use pair-presented state. Relationship-aware callers
(member menu suppression, member-removal detection, explicit exit, and the
composite projection) must use relationship-defined state.

The settle callback carries a presentation generation/current-state check. A
stale callback must disarm when the desired presentation is now a non-member,
so it cannot resurrect the pair.

## Rendering evidence

The existing outer HWND and client-rect observations remain useful but are not
treated as proof of client repaint. Controlled GuineaPig and isolated temporary
Chromium profiles expose a resize counter and viewport dimensions in the title;
the validation sequence samples those values immediately after each transition
without clicking inside a guest. Missing browser installations are reported as
environment-unavailable.
