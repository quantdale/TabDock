## Context

See proposal.md and audit A03. MotionProfile is present only in the dirty worktree. Its anonymous descriptor observers are rooted externally; owned MouseEnter handlers alone are not the retention source. Loaded callbacks can also wire repeatedly, and Tag is used as a wiring flag.

## Goals / Non-Goals

**Goals:** Explicit attachment ownership, stable reattachment, and collectability of discarded controls in both motion modes.

**Non-Goals:** No new color algorithm, template-part timing repair, reduced-motion preference watcher, or native animation.

## Decisions

1. Store a per-element attachment owner in a private attached-property slot, not the public Tag. It retains exact observer/handler pairs and exposes idempotent attach/detach/dispose operations. Prefer tracked descriptor removal or genuinely weak observation over anonymous unremovable descriptor subscriptions.
2. Enabling installs only lifetime hooks initially; load activates observers once. Unload, disable, template replacement, or owner disposal removes descriptor/control handlers, cancels owned queued callbacks, stops owned clocks, and drops source/part references. Re-load constructs a fresh live binding without multiplying observers. Element-owned lifetime-hook cycles must not be rooted by a global registry.
3. Keep distinct feedback concerns under the same owner contract; later A06/A07/A09 changes extend that owner rather than introduce parallel subscription machinery.
4. Add dispatcher-owned real-template lifecycle tests using no-inline allocation helpers, weak references, settled dispatcher/clock work, and bounded repeated GC/finalization. Verify a disabled control group too. Synthetic counters remain unchanged and explicitly synthetic; they cannot substitute for these tests.

## Risks / Trade-offs

- Unloaded can be temporary during recycling → detach active observers, retain only collectible re-load hooks, and verify state works after reload.
- Test local variables or pending clocks can retain controls → isolate allocation, explicitly settle work, and report the specific surviving references rather than relax assertions to a memory budget.
- Template edits share MotionProfile → serialize dependent changes and preserve the user's motion baseline.

## Migration Plan

Add red lifecycle tests, introduce ownership, and cover buttons, rows, checkmarks, chevrons, border states, and entrance hooks. Require the 30-control reproduction to collect after teardown in normal and reduced motion. Existing templates remain opt-in and the visual language stays intact. No disk migration; rollback cannot be justified by a synthetic resource PASS alone.
