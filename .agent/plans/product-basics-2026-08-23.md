# Product Basics: Capture Reporting + Group Management (2026-08-23)

Objective: close the two smallest high-certainty product gaps surfaced by the
post-hardening UX assessment, plus one hygiene gap from the same-day
integration audit. All mechanical or near-mechanical; zero Shepherd-invariant
risk. Baseline 0bc1cde (main == origin/main, clean).

## Selected findings (verified in current source this session)

| ID | Area | Evidence | Fix |
|---|---|---|---|
| PB-1 | Capture feedback asymmetry | Picker path aggregates failures into ONE owner-modal (`App.xaml.cs:902-931`); inline Add-App panel shows one modal PER failure and includes no aggregate (`ContainerWindow.xaml.cs:1768-1783`); user-facing lines embed raw `(0x…)` HWNDs (`App.xaml.cs:911`) | Extract shared `CaptureFailureReport` builder; route both paths through it; inline multi-failure aggregates once; user lines drop the HWND (log keeps full detail) |
| PB-2 | Every new group is literally "Group" | `GroupManager.CreateGroup:342-344` takes fixed default; menus build by Name (`ContainerWindow.xaml.cs:1552-1561`); only color dot distinguishes rows | Counter-suffix uniquification at creation (ordinal-ignore-case), explicit names included |
| PB-3 | Launcher rows are dead ends | `SelectedGroup` bound but consumed by nothing (`MainViewModel.cs:21-25`; `MainWindow.xaml.cs` empty); subtitle invites selection that does nothing (`MainWindow.xaml:24`) | Double-click / Enter on a row raises `OpenGroupRequested` → `OpenContainer` (registry-first `Activate()`, `App.xaml.cs:985-989`) |
| PB-4 | Restored-but-empty container has no guidance | `ContainerWindow.xaml:337-339` ContentBorder renders blank when zero tabs | DataTrigger empty-state hint over ContentBorder (Tabs.Count==0), non-hit-testable |
| HYG-1 | `state.json.bak.tmp` orphan never swept | New staged-backup candidate name (`PersistenceService.cs:285`) missing from `CleanupStaleTempFiles` list (`App.xaml.cs:1112`); a fully-written candidate is valid prior-primary content, so unconditional deletion would violate evidence-retention spirit | Age-gated (24h) sweep entry mirroring `PendingRecoveryService.OrphanTemporaryFileAge` |

## Deliberately deferred (needs product decision / separate session)

- M3 pending-recovery visibility banner (highest-severity trust gap; read-only
  launcher banner shape sketched by assessment — needs copy/dismissal decisions).
- M4 focus-independent tab navigation hotkeys (Ctrl+Alt+arrows conflict with
  common display-driver global hotkeys; combo selection needs care).
- M5 always-visible split affordance (pairs naturally with M4).
- Blocked-capture admission state on buttons (presentation decision atop
  `SetCaptureAllowed` reason, currently log-only).

## Non-negotiables preserved

No capture-admission changes (reporting only reads existing refusal reasons);
no recovery-policy changes (banner work deferred; supervised typed-YES stays
the only mutating path); no native/reparenting surface touched; journal and
persistence ordering untouched (HYG-1 only adds startup deletion of a
24h-or-older disposable fragment).

## Waves

1. **Wave A — tests first:** CaptureFailureReport formatting facts
   (single/multi, HWND-free user lines vs hwnd log lines, plural captions);
   CreateGroup uniquification facts (three defaults unique; explicit duplicate
   suffixed; case-insensitive collision); MainViewModel open-command fact
   (raises event with SelectedGroup).
2. **Wave B — implementation:** the five fixes above.
3. **Wave C — validation:** Debug+Release builds 0w/0e; xUnit both configs;
   release tooling; validate.ps1 -Ci -Publish; openspec validate;
   git diff --check; STATE.md handoff.

No OpenSpec change: no capability contract added or altered — presentation
unification toward R21-014's existing one-summary intent, naming/affordance
polish, and hygiene; consistent with the drag-reliability campaign precedent
(plan-only).
