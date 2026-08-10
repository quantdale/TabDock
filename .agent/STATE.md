# Agent state

## Production-ready milestone closure — 2026-08-10

Status: production readiness **PASS**. The vertical split-screen, guest
z-order stabilization, inline capture/group UX, tab X/middle-click pop-out
behavior, native move/resize re-glue, and final direct-click pairing fix are
implemented, documented, archived, and validated. The final milestone commit
is the next repository action; its hash is intentionally not embedded in this
same commit and will be reported after creation.

The direct-click root cause was that direct guest activation reliably emitted a
desktop-level `EVENT_OBJECT_REORDER` (`GetDesktopWindow`, `OBJID_CLIENT`,
`CHILDID_SELF`) while `EVENT_SYSTEM_FOREGROUND` could be delayed or coalesced.
`WinEventMonitor` now installs the narrowly filtered reorder hook, snapshots
foreground HWND at callback time, validates it again after UI dispatch, and
routes repair through the existing `WindowShepherdService` pairing policy.
`PairZOrderBehind` avoids duplicate writes when the container is already
adjacent.

## Architecture and UX invariants

- Captured guests remain independent top-level windows: no reparenting,
  `WS_CHILD` conversion, owner mutation, or guest style/exstyle containment
  mutation.
- `WindowShepherdService` is the authoritative capture, journal, positioning,
  and local guest/container z-order policy. There is no global `HWND_BOTTOM`
  repair, competing z-order subsystem, aggressive polling, or sleep workaround
  in production code.
- Split mode is exactly two visible members in deterministic LEFT/RIGHT
  physical-pixel panes; pair identity is reference-based and lifecycle-safe.
- Journal-before-hide, O(1) captured-HWND lookup, and the existing crash-rescue
  rules remain intact.
- Routine capture is inline in the container. In-window Group switching and
  `+ New group` remain available; the launcher/picker is retained only as the
  deliberate fallback for global-hotkey/no-container capture.
- Tab X and middle-click perform Pop out and keep the external process alive;
  explicit Close window remains the WM_CLOSE action. Native guest title-bar
  movement/resize is re-glued to its assigned pane rather than popping out.
- This milestone does not claim a single-container architecture.

## Validation

Passed:

- `dotnet build TabDock.csproj`
- `dotnet build TabDock.sln`
- `dotnet build tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj`
- `dotnet build tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj`
- `.\scripts\validate.ps1`
- `openspec validate --all --no-interactive` — 11/11 current items
- `dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --list`
- High-risk smoke scenarios, each in a fresh supervised driver process:
  `directclick-foreground-pairing`, `contextmenu-render-stability`,
  `split-contextmenu-render-stability`, `chrome-click-render-stability`,
  `split-directclick`, `split-native-move-reassert`,
  `split-native-resize-reassert`, `tab-closebutton-popout`, and
  `tab-middleclick-popout`.

Recorded closure evidence also includes 10/10 direct-click cycles with
189–213 ms repair windows, the adjacent split/render/input/tab/native-re-glue
regressions, and a supervised Chrome + Edge + Windows Terminal torture run
covering external foreground steals, browser chrome, split focus,
move/maximize/minimize/restore, pop-out, re-capture, and group switching.

The completed OpenSpec change is archived at
`openspec/changes/archive/2026-08-10-2026-08-10-vertical-split-screen`.
There are no known critical blockers for this milestone.

## Repository checkpoint

- Branch: `main`.
- Pre-closure HEAD: `71f8e3c854c7800b80d1d082fcb0b140079109ce`.
- The complete validated milestone is ready for one coherent local commit.
- Do not push or create a tag unless a later request explicitly authorizes it.

## Next action

Stage and review the complete intended production-ready milestone, create the
single closure commit, then verify `git status`, `git log -1 --oneline`,
`git rev-parse HEAD`, and `git show --stat --oneline HEAD`.
