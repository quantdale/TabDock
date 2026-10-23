# Motion color restoration — 2026-10-23

## Finding

Border feedback stored its "normal" color in the same brush it animated. After a selection animation filled, reading that brush returned the selected endpoint, so deselect animated back to the selected color. The audited fixture was `#FF202020/#FF505050` normal and `#FF2864B4/#FFFF0000` selected.

## Fix

The existing attachment owner keeps independent base fill and border tokens. Destinations come from selection, hover, focus, and those tokens. A new clock replaces the previous one. `TryUpdateBorderBaseTokens` is the single seam for a later live style/binding update; it does not sample an animated endpoint and does not implement picker mapping.

Keyboard focus is read from the border, its templated parent, or the ancestor row. A row can be focused without the child border itself being the focused element.

## Unchanged invariants

`FeedbackDuration` (90 ms), `StateDuration` (150 ms), `NavigationDuration` (220 ms), and the shared ease-out curve are unchanged. No palette token, native guest positioning, or HWND animation was edited.

## Evidence

`MotionColorStateTests` and `MotionFeedbackLifecycleTests`: 14/14 passed, both motion modes. `dotnet build TabDock.sln` Debug: 0 warnings, 0 errors. Full xUnit suite: 958/958 passed. A02/A10 were already archived, so the old full-suite deferral did not block this gate.
