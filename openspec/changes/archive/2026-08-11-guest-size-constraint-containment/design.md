## Context

TabDock's Shepherd positions a captured external window over the container's
content area with `SetWindowPos` and, until now, never verified the resulting
`GetWindowRect`. Real applications (browsers, Explorer, terminals) enforce a
native minimum track size via `WM_GETMINMAXINFO`. When a split pane
(`content/2`) or the normal-mode content area is narrower than that minimum,
the guest clamps back up to its minimum and visibly overflows the pane. The
overflow grows as the container narrows. The container has no minimum-size
constraint, so nothing stops the user from entering the impossible region, and
the redundant-glue guard then re-issues the failing write every layout pass (a
resize war).

## Goals / Non-Goals

**Goals:**
- Make the container stop shrinking below what the currently visible guests can
  physically fit (dynamic native min-track), so a guest is never asked to occupy
  a pane narrower than its own native minimum.
- Verify requested-vs-observed geometry after each positioning write and bound
  any non-compliance (no per-frame resize war), with a bounded diagnostic.
- Derive all constraints at runtime from native probes — never hardcode
  per-application widths.
- Cover the policy with a deterministic self-test and supervised regression
  scenarios.

**Non-Goals:**
- No reparenting, guest style/ex-style mutation, owner mutation, clipping, or
  `HWND_BOTTOM` repair. WindowShepherdService remains the sole positioning/z-order
  owner.
- No change to the exact split partition math (`SplitGeometry.Partition`).
- No attempt to force a guest below its own native minimum (impossible).

## Decisions

**D1 — Discover each guest's native minimum via `WM_GETMINMAXINFO` over
`SendMessageTimeout`.** Rationale: it is the standard, read-only, generic source
of a window's effective minimum. `SMTO_ABORTIFHUNG` bounds the wait so a hung
guest can never block the UI. UIPI only blocks it across an integrity boundary,
which TabDock already refuses to capture (elevation guard). Fail-closed to "no
constraint" on any failure rather than guessing. *Alternative rejected:* probing
only during the guest's own size negotiation (not reliable cross-process) and
hardcoded per-app widths (version/DPI/machine-fragile).

**D2 — Enforce the constraint as the container's native `ptMinTrackSize` in
`WM_GETMINMAXINFO`, not WPF `MinWidth`.** Rationale: the container already owns a
`WM_GETMINMAXINFO` handler for maximize clamping; extending it is the natural,
physical-pixel-consistent place. Native min-track clamps both user drag-resize
and programmatic `SetWindowPos` (verified: a window with a min-track clamps a
smaller requested size back up). *Alternative rejected:* WPF `MinWidth` mixes
logical-pixel layout with the physical-pixel native contract and is bypassed by
native resize.

**D3 — Compute the content minimum from the visible guests with a pure, tested
function.** `SplitGeometry.MinContentWidth(split, L, R)` =
`max(2*L, 2*R-1)` for split (exact partition: LEFT=floor(W/2)>=L, RIGHT=ceil(W/2)>=R),
`L` for normal; `MinContentHeight` = the taller member's (split) or the active
guest's (normal). The outer min-track adds the chrome delta (outer minus content).
This is exercised by `--selftest-geometry`.

**D4 — Cache minima; re-probe on discrete transitions and a debounced interval.**
Refresh on split enter/exit/replace, active-tab change, survivor promotion,
`WM_EXITSIZEMOVE`, and a 5 s periodic timer. Never probe per frame. This keeps the
probe (a cross-process `SendMessageTimeout`) out of the hot path while still
picking up dynamic minima (sidebar/toolbar toggles).

**D5 — Bounded requested-vs-observed reconciliation.** After a positioning write,
read `GetWindowRect`; if it still differs from the desired rect within the 1 px
epsilon, mark the guest non-compliant for that exact rect and skip re-writes for
it on later passes (still pinning z-order). Clear when the rect changes or the
guest becomes compliant. Emit a bounded `SHEPHERD[size-constraint]` diagnostic on
the transition. This is the resize-war stopper for the dynamic-min-growth case
that min-track alone cannot prevent.

## Risks / Trade-offs

- [A guest's native minimum grows beyond the current container size] → The
  refusal guard stops the war; the container's min-track is raised so the next
  resize clamps; a bounded diagnostic is logged. The guest sits at its native
  size (unavoidable) rather than being fought.
- [`SendMessageTimeout` cost on a slow guest] → Cached; only re-probed on
  transitions and a 5 s interval; `SMTO_ABORTIFHUNG` bounds each probe.
- [Cross-machine/DPI variance in returned minima] → All values are runtime-native
  (never the machine's numbers encoded as truth); the physical-pixel convention
  and DPI fail-closed capture are preserved.
- [Extreme case: guest minima exceed the monitor work area] → Maximize still uses
  the work area; the min-track is larger than the monitor, which prevents
  shrinking (the desired "no silent overflow"), and the refusal guard avoids a
  war.

## Migration Plan

Backward-compatible behavior addition: existing containers gain a min-track only
when a visible guest has a measurable native minimum. No persisted-state change,
no dependency change. Rollback is a revert of the production source changes.

## Open Questions

- Supervised confirmation of the user-drag min-track clamp on real Edge/Explorer
  across DPI and Windows builds (the deterministic logic and pig scenarios cover
  the mechanism; live visual confirmation is outstanding).