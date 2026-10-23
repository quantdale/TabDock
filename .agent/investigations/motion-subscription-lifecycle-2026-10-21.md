# Motion subscription lifecycle — 2026-10-21

## Finding

A03 was a managed retention leak, not a GDI/USER leak. `DependencyPropertyDescriptor.AddValueChanged` kept discarded styled buttons and feedback borders alive after teardown. Public `Tag` values (`wired`, `wired-late`) were only wiring flags and could not identify the handler to remove.

## Fix

`MotionProfile` now stores one private per-element attachment owner. Enable installs element-local lifetime hooks. Load activates one observer set. Unload, disable, template replacement, and disposal remove the exact descriptor/event handlers, abort queued callbacks, stop owned clocks, and bump the generation. There is no static control registry. Reduced-motion detection remains a one-time startup read; tests may override it without adding a live preference watcher.

## A09 incidental result

Deferred activation until `Loaded` resolves `TdButton` `HoverOverlay` and `PressedOverlay` after the template exists. On the actual shared template, with reduced motion, a raised `MouseEnter` sets hover opacity to 1 and an authorized `IsPressed` change sets pressed opacity to 1. The old first-load zero-opacity symptom was not reproduced after this lifecycle change.

`fix-button-feedback-template-timing` should retest that contract and add any missing regression it still needs. It should not add a second observer, Tag flag, or polling loop if this owner already satisfies the part-timing behavior.

## Evidence

Correction (2026-10-24): this historical run used a shown off-screen HWND;
it did not establish a never-visible test fixture. The corrected fixture and
new lifecycle regressions are recorded in [session correction review](session-correction-review-2026-10-24.md).

- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug --filter FullyQualifiedName~MotionFeedbackLifecycleTests`: 5/5 passed. The 30-control probe asserts observers attach, then collects discarded styled buttons, feedback borders, and disabled buttons in both motion modes, beside plain controls.
- Synthetic resource snapshots remain labeled synthetic in `ResourceSoakRunner` and are not used as this proof.
- Full suite remains appropriate now that A02/A10 are archived; it was not required to accept this change and is part of the later integrated gate.

## Non-claims

No SendInput, production-storage mutation, color-restoration fix (A06), brush-authority fix (A07), or picker selection mapping (A08) is implied by this historical run. Its hidden-HWND claim is superseded by the correction above.
