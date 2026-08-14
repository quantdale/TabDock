# Split third-tab activation and initial presentation settle

## Why

Two user-observed split-screen defects remain after the source-freeze campaign:

1. With three captured tabs, splitting two makes the unrelated third tab impossible to open because the split UI deliberately swallows/reverts its ordinary left-click.
2. Immediately after creating a split from a context menu, Chromium-family guests can retain a stale full-width client presentation until the pane receives its first real click, even though the outer HWND has been assigned a half-pane.

Both defects are presentation-policy issues. They do not require changing the Shepherd/no-reparent architecture, captured-window identity, persistence, recovery journal, or split geometry contract.

## What changes

- An ordinary **left-click** on a non-member tab becomes an explicit request to leave the current split and show that selected tab full-width.
- Hover and right-click on a non-member remain presentation-only and do not tear down the split.
- If journal-safe hiding of either split member cannot complete, the switch fails closed: the split remains authoritative, both panes are re-presented, and the third tab is not selected.
- Split creation receives one bounded post-popup settle after TabDock chrome is no longer active: re-layout the pair and request real foreground for the focused member.
- The settle does not synthesize `WM_SIZE`, alter guest styles, reparent guests, or weaken foreground/identity checks.

## Non-goals

- Changing Ctrl+Tab semantics while split is active.
- Remembering a dormant split pair after the user selects a third tab; selecting the third tab exits the split.
- Rewriting split geometry, HDWP batching, z-order verification, or recovery semantics.
- Browser-specific hacks.
