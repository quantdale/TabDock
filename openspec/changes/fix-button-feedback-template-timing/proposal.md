## Why

Audit finding A09 applies the actual TdButton template and realizes its overlays, yet MouseEnter leaves hover opacity at zero. Attachment runs while the template root has a parent but its named descendants do not exist, then permanently caches null parts.

## What Changes

- Resolve button feedback parts only after the current template is realized.
- Retry legitimate not-yet-realized states without marking a null-part attachment permanently complete.
- Rebind safely after template replacement/reload, restoring hover/pressed/keyboard feedback exactly once.
- Cover real shared/derived button templates in normal and reduced-motion modes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ui-ux-hardening`: require template-realization-safe button feedback and graceful behavior when optional parts are absent.

## Impact

Targets the uncommitted MotionProfile button attachment and App shared button templates/tests. Existing button commands, focus states, duration/easing tokens, and native HWND behavior are preserved. Source evidence: audit A09.

## Non-goals

No button redesign, palette changes, or fixes to generic border selection/source semantics. Lifetime cleanup is owned by A03. Planning only.

## Sequencing

Apply after `fix-motion-subscription-lifecycle` and reuse its detach/re-attach owner. Independent of the color/picker branch once that owner exists. Requires the current uncommitted motion baseline, not a speculative implementation against HEAD without MotionProfile.
