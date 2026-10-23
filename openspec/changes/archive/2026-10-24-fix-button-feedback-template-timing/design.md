## Context

See proposal.md and audit A09. The root border's attached-property callback can see TemplatedParent before its children exist. The handler marks itself wired while permanently capturing null HoverOverlay/PressedOverlay references. A03 provides a detachable attachment owner.

## Goals / Non-Goals

**Goals:** Correct feedback after template realization/replacement, one live observer set, and benign handling of intentionally missing parts.

**Non-Goals:** No button redesign, token changes, or changes to commands and native guest presentation.

## Decisions

1. Separate activation of lifetime hooks from resolution of template parts. Resolve named parts on the realized current template at Loaded/ApplyTemplate boundaries through the owning button's template namescope or verified descendant lookup. A TemplatedParent alone is not readiness proof.
2. When realization is pending, defer a bounded recheck tagged with the attachment/template generation. Missing parts after realization are optional/disabled feedback, not a forever-ready null cache or an endless polling loop. No repeated layout timer is introduced.
3. Template replacement/unload invalidates queued work and clears old part references before reattachment. Reuse A03's exact-handler teardown; no second descriptor observer or Tag flag. Initialize current hover/press state so an already-active control does not wait for a future transition.
4. Test actual TdButton and derived templates with managed pointer/pressed/keyboard state drivers, not SendInput. Realize, load, enter/leave, press/release, replace templates, and reload; inspect overlays/transforms and verify original controls collect.

## Risks / Trade-offs

- A queued part lookup may outlive its template → generation/current-owner guard on every deferred callback.
- Optional parts vary across template variants → no exception or fallback visual redesign; remaining command/focus behavior stays intact.
- Early lifecycle changes overlap A03 → apply after that change and keep one owner.

## Migration Plan

Add the audited zero-opacity regression first, repair realization timing, then validate both normal and reduced-motion endpoint behavior and lifecycle collection. The current uncommitted motion work is a prerequisite. No data or native-window migration.
