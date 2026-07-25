## Why

The "TabDock Engineering Audit — 2026-07-25" (`docs/internal/audit-2026-07-25.md`, pinned at commit `f9950990bc3745457545b81d38c45676e3defba5`) found five new issues (AUDIT25-01 through AUDIT25-05) and reconfirmed one previously-tracked bug (`investigation_findings.md:285`, `GroupViewModel.PickColorCommand`) still present. All six are independent, single-file, low-risk fixes the audit itself sequences as quick wins with no ordering constraints between them (`docs/internal/audit-2026-07-25.md` §5 "Roadmap": *"All five items are independent, low-risk, single-file changes with no ordering constraints between them"*). Bundling them into one change avoids six near-identical proposal/design/tasks cycles for work the audit already scoped and prioritized.

## What Changes

- **AUDIT25-01** — Debounce `WindowShepherdService`'s hidden-window crash-recovery journal writes (`Services/WindowShepherdService.cs:128-174,289-350`), mirroring `GroupManager.RequestSave`'s existing `DispatcherTimer` debounce pattern (`Services/GroupManager.cs:54-67`), so a synchronous read-modify-write to `hidden-windows.json` no longer runs on the UI thread on every tab switch. Adds a forced-flush requirement on whichever crash/exit paths are decided on (see Open Decisions) so debouncing cannot silently weaken the crash-recovery guarantee the journal exists for.
- **AUDIT25-02** — Add an exe-path-keyed icon cache to `IconService` (`Services/IconService.cs:20-67`) so `CapturePickerViewModel.Refresh`'s `EnumWindows` loop (`ViewModels/CapturePickerViewModel.cs:60-109`, icon fetch at line 99) stops re-extracting an icon via `ExtractIconEx` for every window of an already-seen executable. Cache-first fix only — no async/incremental restructuring of `Refresh` in this change.
- **AUDIT25-03** — Rewrite the stale WS_CHILD/Reparent-era comment at `Services/WinEventMonitor.cs:125-129` to describe the actual Shepherd-era reason a captured window is filtered by direct HWND match instead of `GetAncestor(GA_ROOT)`. Comment-only; the audit's proposed replacement text (§ AUDIT25-03) is the basis for the new comment. Zero behavioral change.
- **AUDIT25-04** — Address the permanently-disabled `<NuGetAudit>false</NuGetAudit>` in `TabDock.csproj:12-13`, currently justified only by a build-environment network constraint rather than scoped to it. Default approach per the audit's own stated preference: leave the suppression in place and add a standing reminder comment instructing whoever adds the first NuGet dependency to re-verify the constraint and re-run an audit out-of-band first. See Open Decisions — relocating the suppression instead is on the table.
- **AUDIT25-05** — Coalesce the per-activation `DispatcherTimer` allocations in `ContainerWindow.WndProc`'s `WM_ACTIVATE` handler (`Views/ContainerWindow.xaml.cs:142-164`) and the sibling `ContainerWindow_StateChanged` "settled" snapshot timer (`Views/ContainerWindow.xaml.cs:296-320`, timer at 313-319) into one cancellable `DispatcherTimer?` field per call site, `Stop()`-ing any pending instance before starting a new one — the same coalescing shape `App.xaml.cs`'s `DebounceNameChanged` (`App.xaml.cs:347` onward) already uses.
- **(tracked, not new)** `GroupViewModel.PickColorCommand` (`ViewModels/GroupViewModel.cs:103`, also referenced at line 57) currently invokes `AddWindowsRequested` instead of any color-picking behavior — a self-described placeholder (`investigation_findings.md:285`). Fixed in this change because it's the same size/risk class as the AUDIT25 quick wins, not because it's a new audit finding.

## Capabilities

### New Capabilities
- `hidden-window-journal`: Debounced, crash-safe persistence behavior for the hidden-window crash-recovery journal (AUDIT25-01).
- `capture-picker-icons`: Icon-resolution behavior for the capture picker's window list, including the exe-path cache (AUDIT25-02).
- `container-activation-timers`: Coalesced-timer behavior for `ContainerWindow`'s activation re-assert and state-changed snapshot logic (AUDIT25-05).
- `group-color-picker`: Correct behavior for the group tab strip's color-picking command (item #6 / `PickColorCommand`).

### Modified Capabilities
(none — no existing `openspec/specs/` capabilities exist yet in this repo; all four touched behaviors above are newly specified rather than modified.)

## Impact

- **Code**: `Services/WindowShepherdService.cs`, `Services/GroupManager.cs` (reference pattern only, not modified), `Services/IconService.cs`, `ViewModels/CapturePickerViewModel.cs`, `Services/WinEventMonitor.cs`, `TabDock.csproj`, `Views/ContainerWindow.xaml.cs`, `ViewModels/GroupViewModel.cs`, and whichever `App.xaml.cs` crash/exit handlers are chosen for the AUDIT25-01 flush (see Open Decisions).
- **No API/dependency changes.** No new NuGet packages, no persisted-file schema changes (the journal's on-disk shape is unchanged, only its write cadence), no XAML markup changes.
- **Explicitly out of scope for this change** (do not touch in implementation):
  - `tests/ValidationDriver/**`, `Spike/**`, and `*.xaml` markup — the audit did not cover these, and this change doesn't either.
  - Re-running or standing up CI.
  - AUDIT25-02's full async/incremental icon loading — the audit's own recommendation treats the exe-path cache as the quick win and async loading as a separate follow-up; this change implements only the cache.

## Open Decisions (all resolved)

1. **AUDIT25-01 crash-flush paths — RESOLVED: all five paths.** `App.xaml.cs` has five exit/crash paths in play: `Application_Exit` (`App.xaml.cs:133-144`), `Application_DispatcherUnhandledException` (`:154-166`), `CurrentDomain_UnhandledException` (`:171-183`), `Application_SessionEnding` (`:186-200`), and the early-startup failure path at `:123-127`. **Decision:** wire `FlushJournal()` into all five, not a subset — `FlushJournal()` is a no-op when nothing is pending, so covering all five costs nothing extra and removes any risk of guessing the wrong subset.
2. **AUDIT25-04 annotate vs. relocate — RESOLVED: annotate in place.** No CI config is visible in this repo, so there's nowhere concrete to relocate the suppression to yet. Extend the existing `TabDock.csproj:12` comment in place; do not relocate.
3. **`PickColorCommand` fix shape — RESOLVED: option (a), bug-only no-op.** Not option (b) (accent-color cycling). Rationale: option (b) would give a completely unbound, unreachable command fake-real behavior that looks like it does something when nothing in the UI can ever trigger it — a future reader seeing "cycles through presets on Execute()" would reasonably assume a UI path calls it, and there isn't one. Option (a) is honest about the command's current inert state and fully closes the actual bug (wrong target invoked) without manufacturing observable-looking behavior nobody can reach. `specs/group-color-picker/spec.md` has been updated to the option (a) requirement/scenarios; the option (b) language has been removed, not left as a fallback note.
