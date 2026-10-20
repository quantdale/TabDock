## Why

Audit finding A03 retains 30/30 discarded motion-enabled borders and actual styled buttons after forced collection, versus 0/30 controls. Property-descriptor subscriptions have no teardown, so routine template/window churn can retain visual trees even under reduced motion.

## What Changes

- Give each feedback attachment explicit, idempotent ownership and teardown for load/unload, enable/disable, and template replacement.
- Release property-descriptor observers and retained template references; prevent repeated Loaded events from multiplying subscriptions.
- Add real managed WPF control lifecycle/weak-reference tests in addition to the existing synthetic resource profiles.
- Preserve feedback tokens, reduced-motion semantics, and native guest presentation authority.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ui-ux-hardening`: require lifetime-owned, collectible motion-feedback attachments.
- `resource-lifecycle-qualification`: distinguish actual WPF collectability checks from synthetic counters.

## Impact

Targets the existing uncommitted `Infrastructure/MotionProfile.cs` and actual styled-control tests. No new NuGet package or P/Invoke is needed. Source evidence: audit A03; the leak is managed retention, not a claimed GDI/USER leak.

## Non-goals

Do not redesign visuals, change native HWND animation/positioning, fix state colors (A06/A07), or change button part timing (A09) in this change. Planning only.

## Sequencing

Requires preserving/integrating the current motion work, which is not in HEAD. Establish the shared attachment lifecycle first; later motion proposals must reuse it rather than add parallel observers. `fix-motion-color-state-restoration`, `preserve-motion-brush-state-authority`, and `fix-button-feedback-template-timing` build on this ownership seam.
