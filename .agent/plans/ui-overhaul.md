# Plan: TabDock interface overhaul

**Status:** implementation complete; supervised qualification pending
**Updated:** 2026-11-13

## Objective

Redesign every WPF frontend surface while retaining window-management logic, native geometry contracts, commands, keyboard access, and automation IDs.

## Audit and reference lock

- Persona: Windows power users organizing independent application windows into task workspaces. Stack: .NET 8 WPF/XAML; no web routing or mobile frontend.
- Surfaces: launcher (workspace list, create/open/capture/exit, recovery and blocked-capture attention); standalone capture picker (search, destination, selection, refresh, submit/cancel); container (caption, rename/accent, workspace menu, tab strip, split halves, pop-out/close menus, inline capture, native content host, empty overlay).
- Refero Linear style `3affd07f-e21e-46a4-8aee-26accba689a6` informs restrained instrument panels; Tapbots style `889337e0-2d2c-4980-a9b5-2bca2cb5394d` informs tactile rounded actions and lavender highlights. Refero Linear settings screen `cf84ce2f-7463-41a4-8f07-d9ad30110b02` and search screen `3497da4b-ed42-4b05-913f-e449ed9b766f` ground the hierarchy and separation of search from results.
- Reject marketing-page scale, character artwork, all-over gradients, and sidebar navigation without corresponding destinations. Native guests remain the main content.
- Tokens: canvas #0B0B12; surfaces #15151F / #1C1C29 / #252535; primary #C4AEFF with #211535 ink; accent #A78BFA; text #F7F8FA / #BABACB / #9292A9. Warning and danger retain their distinct semantic palettes.
- Typography: Segoe UI Variable Display headings, Segoe UI Variable Text controls, Cascadia Mono metadata; sizes 32/28/19/16/13/11. Spacing 4/8/12/16/20/24; cards radius18, controls10, rows12.
- Motion: existing WPF cubic ease-out, 90/150/220 ms feedback/state/navigation, 1.015 hover lift and .985 press. Retain reduced-motion and attachment lifecycle handling. Existing platform capabilities suffice; no new packages.

## Completed implementation

- [x] Audit all windows and research Refero styles/screens.
- [x] Shared palette, typography, cards, actions, selection/focus states, checkboxes, icon fallbacks and motion.
- [x] Launcher hierarchy, separate action row, workspace identity tiles and empty state.
- [x] Capture picker proportional search/destination layout, selection hierarchy, empty state and footer.
- [x] Container toolbar separated from centered caption, compact caption buttons, tabs/split/menu styling and native-safe empty overlay.
- [x] Inline capture bounded by actual window height minus measured caption/tab bands; scrolling list leaves confirmation controls reachable at minimum size.
- [x] Build, regression suite, offscreen fixture renders at default/minimum sizes.
- [ ] Supervised native capture/split/pop-out/keyboard workflow and physical mixed-DPI validation.

## Evidence

- `dotnet build TabDock.sln --no-restore`: 0 warnings/errors.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj --no-build --no-restore`: 1,004 passed.
- `git diff --check`: passed. User authorized committing and pushing directly to main on 2026-11-13; check exact-SHA hosted CI after pushing.
- Render helper and PNGs: `D:/Documents/tryPython/_AgentArtifacts/TabDock/sessions/20261113-codex-ui-overhaul/`. Helper parses real XAML/resources with fixture data and removes application event handlers; no real guest capture or synthesized input. Final run uses inactive offscreen fixture windows and real layout bindings. Minimum container confirms Cancel/Add selected remain visible.
- Refreshed Repowise index; read current source because initial index was stale/degraded.

## Handoff

Implementation is ready for review. Real-input qualification requires supervision under docs/TESTING.md; user was asked asynchronously. Build/unit tests and fixture renders do not prove live native window workflows or physical mixed-DPI behavior.
