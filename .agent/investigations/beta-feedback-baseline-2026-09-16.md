# Investigation: beta feedback baseline on the current executable

**Date:** 2026-09-17
**Status:** complete for the available desktop; implementation, exact Release physical qualification, evidence closure, targeted ValidationDriver qualification, mainline integration, and exact-SHA hosted checks validated; mixed-DPI/Windows 10 physical repeats, the full physical ValidationDriver catalog, and production signing remain external or unperformed qualification items
**Build:** baseline `9b84a2a3f16b8cccf3bf18f6cc8fe045e0967229`; last substantive implementation commit `6172595cffcce2e2eee506c3eb9ea1791afa7fa8`; the final documentation closure is non-executable and was requalified through the applicable Release gates
**Branch:** `main`

## Question

What do the four beta reports mean on the current TabDock executable, and which
current product boundaries, if any, are responsible?

## Environment

- Windows 11, build `10.0.26200`, x64; .NET SDK `8.0.425`, SDK band selected by
  `global.json` `8.0.400` with `latestFeature` roll-forward.
- Primary monitor: `1920x1200`, work area `1920x1140`, DPI 120 (125%).
- Secondary monitor: `1920x1080`, work area `1920x1032`, DPI 96 (100%).
- Current repository root: `D:\Documents\tryPython\TabDock`.
- `dotnet build TabDock.sln -c Debug`: passed, 0 warnings/errors.
- `dotnet test TabDock.sln -c Debug --no-build`: passed, 838/838.
- ValidationDriver catalog: `scenario-catalog-2026-09-17-v3`, 136 scenarios (the
  foreground-pairing regression scenario added during this campaign is included
  in the current catalog).
- The supervised baseline `capture-inline-ui` run was fail-closed by
  `ForegroundQualification`: existing Chrome and Windows Terminal covered the
  required points. It launched and cleaned its own TabDock process, so this is
  a harness/environment result rather than a product result.
- The current desktop now exposes one `1920x1080` work area at 96 DPI; the
  earlier 1920x1200-at-120-DPI plus 1920x1080-at-96-DPI mixed-monitor topology
  is no longer available for a final repeat. Mixed-DPI physical qualification
  is therefore an explicit remaining environment item.
- Post-change Debug build: solution and ValidationDriver/GuineaPig builds passed
  with 0 warnings and 0 errors; xUnit passed 847/847. The canonical Release/CI
  gate passed with Release builds, NuGet audit, 32-cycle headless resource
  lifecycle, native ABI, command-line diagnostics, support-bundle privacy,
  OpenSpec 38/38, and single-file publish/version smoke. Those pre-integration
  Release binaries report the baseline source revision because the candidate
  was not committed yet.
- Candidate Release identity used for physical qualification: the single-file
  artifact at `artifacts/release-final-4f4998ff\TabDock.exe`, version `1.1.0`,
  source SHA `4f4998ffed1c9cbfd7f760ff48b54f48f2143ed1`, file SHA-256
  `3225289BF6A3EB28B9941D0B93F5C38694B53C5E5D81A30F3890A1B417F06CE1`,
  180,310,748 bytes. Its native ABI self-test exited 0 with
  `placementContract=PASS placementRoundTrip=PASS`.
- Final-tree Release identity was rebuilt after the documentation-only
  closure commit; the exact path, source SHA, version, size, and SHA-256 were
  captured from the artifact and its `--version` output at final handoff.

## Findings

### Report A — apparent blank/delay

The normal first capture, multi-capture, rapid switching, maximize/restore, and
mixed-DPI move trials did not show a guest-rendering delay. A 30-switch trial
across three GuineaPig guests completed with one screenshot before and after
each switch; measured switch intervals were approximately 310–920 ms (mean
approximately 576 ms), with no screenshot error or exception.

A different, reproducible blank state was found while opening TabDock chrome
menus. Group, split-affordance, accent-color, and tab context menus all left the
guest content region visually black while the popup was open. Native enumeration
during the group menu reproduced:

- popup visible and above the Group container;
- Group container visible at z-order rank immediately above the active guest;
- active guest visible and correctly sized but below the opaque container;
- `WindowFromPoint` at the content center resolving to the container, not the
  guest;
- closing the popup emitted the existing restore request and returned the guest
  above the container.

The inline capture panel and rename editor did not produce this blank state.

The confirmed disposition is a presentation/z-order defect in the popup-open
transition. The active guest is covered; it is not evidence of slow content
loading. The production fix is now in the candidate tree: each ContextMenu
opening path obtains its real popup HWND and repairs only the local popup /
guest / container stack, while popup close reasserts the ordinary stack only
when the container still owns foreground. A second adjacent transition defect
was also isolated: a queued minimize-start can accompany TabDock's own hidden
guest and race the hide provenance, and an active-tab switch can observe the
just-hidden old guest as foreground. The candidate preserves the hide ledger
until the matching hide event and transfers foreground only for a verified
TabDock-owned transition. Fresh Debug capture/split trials showed bright guest
content immediately. The exact candidate Release artifact repeated this path
with two GuineaPig guests: single presentation and switching were bright,
split presentation measured the native order `[guest-A, guest-B, Group]`, and
Group and accent context menus remained bright while open. The exact Release
artifact from the evidence-closure tree repeated the same behavior.

### Report B — Spotify could not be opened/captured

The installed Spotify process was found and captured from the real picker. Its
main window was a Chromium `Chrome_WidgetWin_1` surface. Capture succeeded, the
Spotify UI rendered, switching away and back rendered it again, maximize showed
the player UI, and release restored it as a standalone window. No Spotify name
allowlist or special case was needed.

Spotify did expose a generic native minimum-size behavior: its requested
`1000x750` minimum exceeded the initial content pane, so TabDock grew to fit the
guest. That is a general window-compatibility path, not a Spotify-specific
capture failure; no blank or failed capture was observed.

The candidate retained the generic identity and minimum-size paths; no
Spotify-name branch or allowlist was added. The installed executable and
distribution topology remain the supported case observed here. The exact
candidate Release artifact repeated capture, switch-away/back, maximize/
restore, minimize/restore, overlap activation, guest activation, and release
with the installed Spotify window; its UI remained visible throughout.

### Report C — clicking TabDock while overlapped

With no popup open, raw physical `SendInput` clicks on the visible title/chrome,
tab strip, and guest content were run with a test-owned unrelated overlay
present. Native foreground and z-order observations showed the intended TabDock
workspace/selected guest became interactive; the guest stayed in the local
workspace stack and did not become an always-on-top artifact over the unrelated
window when TabDock was not foreground.

Some Computer Use activation/click trials produced a different result: the
automation tool foregrounded a guest or failed to foreground the Group HWND
before its click. Those results are retained as tool-behavior evidence, not
treated as proof of a product defect. A fresh Debug process was then qualified
with a real unrelated GuineaPig overlay and native measurements. Before the
click the controlled order was `[overlay, guest-A, guest-B, Group]` with the
overlay foreground. A real click on visible TabDock chrome produced
`[guest-A, guest-B, Group, overlay]`, foregrounded the clicked guest, and left
both panes bright. Maximizing the overlay and activating TabDock produced the
same local order; activating the overlay again produced
`[overlay, guest-A, guest-B, Group]`, with all captured windows at their normal
extended style and below the unrelated foreground window. The ValidationDriver
version of this scenario was also attempted against both Debug and the exact
candidate Release artifact but fail-closed before input when the occupied
desktop could not satisfy its foreground lease; that earlier run was not a
product pass. The exact candidate Release artifact nevertheless completed the same
manual overlay matrix with native measurements: overlay foreground was
`[overlay, guest, Group]`; clicking exposed Group chrome gave
`[guest, Group, overlay]` with Group foreground; reactivating the overlay
restored `[overlay, guest, Group]`; clicking the exposed guest gave
`[guest, Group, overlay]`. Captured windows retained normal extended styles.
The later exact final Release run passed `split-workspace-foreground-pairing`
with real input and native assertions: the split stack was restored above an
interleaved unrelated window, the clicked member became foreground, pane
geometry remained assigned, and the inverse assertion kept all TabDock windows
below the unrelated foreground window.

### Report D — clipped textbox/content

The screenshot source was identified as the real GuineaPig WinForms textbox,
not TabDock’s own rename editor. Its native control/client geometry and
TabDock’s capture search/rename controls were fully visible at the earlier
125%/100% monitor trial, normal and maximized, after capture, and after
repeated move/resize transitions. No clipping defect was reproduced, so no
arbitrary margin or padding change was justified. The current one-monitor
desktop cannot repeat the mixed-DPI portion. The exact Release artifact from
the evidence-closure tree captured a Release GuineaPig textbox, showed the
full control in the guest, accepted real `TEXT-FINAL-3E72` input, and exposed
the typed value through the guest accessibility tree without clipping.

## Candidate Release stress and lifecycle evidence

The final-tree Release artifact was also exercised with four real
Release GuineaPig windows captured into one workspace. The workspace remained
visibly populated at four guests. Sixteen real tab switches (four complete
rounds) measured 406–503 ms per switch, mean 428 ms, with no blank guest,
stale hidden-window presentation, or responsiveness failure observed. The
workspace was closed with the release-windows option, each campaign-owned
GuineaPig exited gracefully, and the TabDock process exited through its own
Exit control.

## Hypotheses tested

- Slow/failed guest rendering: weakened by successful capture, repeated
  screenshots, and native visible/geometry state; rejected for the popup blank.
- Initial-capture-only or many-tab performance failure: not reproduced in the
  controlled switching and nine-tab workload.
- Spotify-specific incompatibility: rejected by successful real Spotify capture
  and Chromium window handling.
- DPI/geometry clipping: not reproduced across both available monitor scales,
  maximize/restore, and repeated relayout.
- Computer Use click activation artifact: supported for some overlap trials;
  separated from the genuine popup z-order defect using raw `SendInput` and
  native `WindowFromPoint`/z-order measurements.
- Popup activation raises the opaque container while the active guest is below
  it: confirmed by native state and the current `ContainerWindow` transition
  guards. A systemic popup-open visual-stack reconciliation is implemented in
  the candidate and covered by source-boundary tests plus the supervised
  group-menu point-ownership assertion.
- A deferred split z-order batch can return success while leaving the container
  between two guests on this Windows desktop. Direct identity-scoped native
  writes succeeded and produced the required order, so the candidate uses a
  bounded sequential reassertion only at foreground-owned split boundaries;
  it measures the resulting order and refuses the operation when another app
  owns foreground.
- A delayed WM_ACTIVATE callback can outlive the user activation that scheduled
  it. The candidate rechecks workspace foreground ownership before and after
  native split layout, and the native z-order authority has the same
  background guard. This is the reason the inverse overlap assertion is part of
  the final physical matrix.

## References

- Current implementation: `Views/ContainerWindow.xaml.cs`, popup handlers,
  `LayoutShepherdActiveWindow`, and `LayoutSplitPanes`.
- Current native authority: `Services/WindowShepherdService.cs`.
- Physical regression seam: `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs`,
  `GroupDropdownStability`.
- Canonical behavior: `openspec/specs/presentation-integrity/spec.md`.

## Conclusion

The baseline had one confirmed current product defect: popup-open local z-order
could cover a live guest. The implementation in commit `6172595` fixes that
transition and the adjacent foreground/minimize races, with 847/847 automated
tests and fresh Debug/native measurements passing. The exact Release artifact
built from the integrated tree passed the available physical A-D/core matrix,
the four-guest soak, and the final launch/capture/release/exit smoke. Spotify
capture and the reported clipping symptom were not failures on this machine.
The final exact-tree hosted CI run passed both its build and native ABI
check-runs, and the final artifact identity is recorded at handoff. The final
record is closed for the available local evidence. Mixed-DPI and Windows 10
physical qualification, the full physical ValidationDriver catalog, and
production Authenticode signing remain explicitly unverified/external items;
the previously blocked ValidationDriver foreground-pairing lane was
subsequently qualified by the exact final Release run.
