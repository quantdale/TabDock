## Context

See `proposal.md` for motivation. The container content area is a WPF
`ContentBorder` (`TdContentVoidBrush`, `#07090D`) hosting `NativeHwndHost`.
That host registers window class `TabDockContentHost` and paints with
`CreateSolidBrush(0x001E1E1E)`. `docs/FRONTEND.md` already states the airspace
rule: the child HWND always paints above WPF siblings, which is why the empty
workspace prompt lives in `EmptyStateOverlay` (a `Popup`) rather than a sibling
panel. `FrontendDesignContractTests.DwmChromePaletteMatchesSharedTokenDefinitions`
already locks `WindowChromeTheme` COLORREFs to `App.xaml` tokens; the content
host has no equivalent.

The empty-state overlay is a `Popup` that opens only while `Tabs.Count==0`,
the container `IsActive`, and the inline capture panel is collapsed. An
inactive empty workspace or an open inline panel therefore uncovers the
native marker completely — that is the strongest visual case, not just the
margin around a centered card.

`RegisterClassEx` takes ownership of `hbrBackground` for the class lifetime.
The class is process-wide. Production starts a new process per session, so
changing the brush at registration is sufficient. The class name is a public
discovery contract (`ContainerWindow.GetContentAreaScreenRect` and the
ValidationDriver).

COLORREF layout is `0x00BBGGRR`. `#07090D` is R=`0x07`, G=`0x09`, B=`0x0D`,
packed as `0x000D0907`. `#1E1E1E` packed as `0x001E1E1E` only because R=G=B.

```
Empty workspace today
┌──────────────────────────────────────────┐
│ chrome / tab strip                       │
├──────────────────────────────────────────┤
│ native TabDockContentHost  #1E1E1E       │
│                                          │
│          ┌────────────────────┐          │
│          │ EmptyStateOverlay  │          │
│          │ (Popup, tokenized) │          │
│          └────────────────────┘          │
│                                          │
│ WPF ContentBorder #07090D is occluded    │
└──────────────────────────────────────────┘
```

## Goals / Non-Goals

**Goals:**

- Make the native host fill the same RGB as `TdContentVoidBrush`.
- Keep class name, HWND role, focusability, and Shepherd contracts unchanged.
- Prevent recurrence with a source lockstep test and maintainer-doc sentence.

**Non-Goals:**

- Replacing the native marker with a WPF-only void (airspace still requires it).
- Moving the empty-state overlay, changing automation IDs, or retokenizing the
  rest of the palette.
- Runtime reading of WPF brushes from `NativeHwndHost` (the class registers
  before the visual tree exists).
- Changing `WindowChromeTheme` (already locked).

## Decisions

1. **Change the registered class brush; do not rename the class.**
   Renaming `TabDockContentHost` would break geometry discovery and driver
   lookup for a cosmetic fix. Keep the name; change only `hbrBackground`.

2. **Hard-code the packed COLORREF next to a token comment, then lock both
   sources in the existing contract test.**
   Alternatives considered:
   - Parse `App.xaml` at runtime: fragile, and class registration happens
     before WPF resources are a reliable source.
   - Share a C# constant consumed by XAML: WPF resource dictionaries are the
     canonical token store today; introducing a second source without a test
     still drifts.
   - Keep WPF `#1E1E1E`: rejected; the overhaul intentionally darkened the void
     to `#07090D`.
   The chosen pattern copies `DwmChromePaletteMatchesSharedTokenDefinitions`:
   assert `TdContentVoidBrush` `Color="#07090D"` in `App.xaml` and
   `CreateSolidBrush(0x000D0907)` (or an equivalent explicit RGB pack) in
   `NativeHwndHost.cs`.

3. **Leave `ERROR_CLASS_ALREADY_EXISTS` cleanup as-is.**
   If the class already exists in-process, the existing class owns its brush.
   That path is a same-process re-entry, not a palette-migration vehicle. A
   color change takes effect on the next process start, which is how TabDock
   and the unit-test host already run.

4. **Document the lockstep in `docs/FRONTEND.md` airspace section.**
   The current airspace paragraph explains why overlays must be popups; it
   does not mention that the native fill is the visible void color. Without
   that sentence, a later token edit can repeat this miss.

## Risks / Trade-offs

- **[Risk] Same-process class already registered with the old brush.**
  → Mitigation: accept process-lifetime class identity; do not attempt
  `UnregisterClass` (the class may still have live HWNDs). Production and
  tests start fresh processes.

- **[Risk] COLORREF channel swap (`#07090D` written as `0x0007090D`).**
  → Mitigation: the contract test must assert the packed `0x00BBGGRR` value
  (or the three `ToColorRef`-style bytes), not merely the presence of `07090D`.

- **[Risk] Visual qualification of the empty-workspace field is environment
  gated.**
  → Mitigation: structural lockstep is the CI-authoritative gate. A supervised
  visual check of an empty container is recommended but not a publication
  substitute.

## Migration Plan

No persisted state, no journal format, no automation-ID migration. Deploy by
shipping a build that registers the new class brush. Rollback is the previous
executable; leftover `#1E1E1E` is cosmetic only.

## Open Questions

None. The token RGB, COLORREF packing, class name, and test pattern are
determined by current source.
