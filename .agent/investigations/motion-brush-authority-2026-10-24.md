# Motion brush authority — 2026-10-24

## Finding

Border feedback copied the current brush into a local `Background`/`BorderBrush`. That local value outranked style triggers and bindings, so split-active fills and picker focus borders stopped updating after load.

## Fix

Semantic brushes stay under their style, trigger, or binding. Hit-test-transparent `OverlayRole` layers paint hover, selected, and focus decoration. Detach removes only those layers and clocks. Shared frozen brushes are not animated. `IsMotionSelected` is the explicit selection seam for the still-active picker mapping change; list-item selection remains the default when it is unset.

## Evidence

Debug solution build: 0 warnings, 0 errors. Full xUnit suite before the actual-template addition: 964/964. `MotionBrushAuthorityTests` after the actual `TdTextBox` template case: 7/7, including split-active style updates, picker focus recycling, live bindings, and frozen-brush integrity, in both motion modes. Physical split/keyboard smoke was not run.
