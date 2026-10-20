## Context

See proposal.md and audit A08. WindowInfo.IsSelected is the capture target authority, independent from ListBoxItem.IsSelected. The standalone and inline templates currently supply generic feedback tokens but no picker-specific state input. A07 establishes safe rendering channels.

## Goals / Non-Goals

**Goals:** Every checked candidate is highlighted, independent of focus, and recycled rows show only their current model state.

**Non-Goals:** No changes to capture submission, identity keys, ListBox selection mode, or workspace-tab behavior.

## Decisions

1. Add an explicit selected-state input to the existing feedback owner, bound to WindowInfo.IsSelected in both picker templates. Distinguish an unspecified input from false: ordinary workspace rows keep their existing ancestor-selection default; picker rows always use their explicit model value.
2. Keep selection fill/border separate from keyboard-focus feedback. Multiple checked rows remain selected even if only one list row is focused. Pointer/list-focus movement must not write the model selection or change which targets are submitted.
3. Let existing model/filter/refresh commands remain authoritative. A binding update on DataContext replacement or explicit selected-state change recomputes the target under A06/A07; A03 detaches old subscriptions. Do not cache a WindowInfo reference beyond the active row generation.
4. Instantiate the actual standalone and inline templates in dispatcher-owned tests. Check individual toggles, Select visible, Clear, search hide/show, identity-safe refresh, and recycling. Keep submission/identity tests alongside visual assertions to catch accidental coupling.

## Risks / Trade-offs

- A boolean default could turn workspace rows permanently unselected → use an explicit override-presence distinction and test ordinary tabs too.
- Recycling can retain a checked visual from an older row → rebind and recompute immediately; include checked→unchecked DataContext replacement.
- Visual fixes can accidentally mutate target choice → assert the model target set is unchanged by focus/hover-only operations.

## Migration Plan

Apply after A07 and its prerequisites. Add the exact red actual-template checked-model fixture, bind the explicit state in both surfaces, and retain AutomationIds/help text. No state migration or new capture affordance; reduced-motion tests must show identical selected endpoints.
