# Investigation: presentation-integrity physical certification

**Date:** 2026-08-31  
**Status:** concluded for this desktop session — physical campaign blocked before input  
**Campaign:** `openspec/changes/2026-08-31-presentation-integrity-physical-certification/`  
**Starting HEAD:** `4aaf3fcaa72edf48865030db43bccf7bd50e21b8`  
**Starting origin/main:** `4aaf3fcaa72edf48865030db43bccf7bd50e21b8`  
**Branch:** `main`  
**Worktree at orientation:** clean

## Question

Does this desktop session provide valid physical evidence for the original
presentation-integrity reports, and did physical use reveal a residual defect
that needs repair?

## Candidate identity

- Starting implementation SHA: `4aaf3fcaa72edf48865030db43bccf7bd50e21b8`.
- Final campaign candidate SHA: `a131a9f8ec9810a4015db2bd935cdda749f9f278`.
- Final Release binary: `bin/Release/net8.0-windows/win-x64/TabDock.exe`.
- Final `TabDock.exe --version`: TabDock 1.0.0, Release, `win-x64`,
  self-contained, x64; embedded commit matches the final candidate SHA.
- Final candidate executable SHA-256:
  `4E5EF396EE585FC02C5C5632F854B78DA3BF37AACA4C8000F72EABC92F1B2103`.
- The pre-commit binary used for the read-only gate had embedded starting
  candidate `4aaf3fc` and hash
  `3C542DC37BE449923539AE169E646A741B781A510E811DA7CCE966BBBCF7D786`.
  It was not used as physical scenario evidence; no physical scenario was
  launched before or after the campaign commit.

## Physical environment

Read-only probes were run at 2026-08-31T13:35:20Z–13:37:23Z.

- `TabDock.exe --doctor`: Windows 11 Pro family, raw product label Windows 10
  Pro, display version 25H2, build 26200 revision 9278,
  `Microsoft Windows NT 10.0.26200.0`, .NET 8.0.30, x64 OS/process,
  standard-user elevation, session 1.
- ValidationDriver no-input preflight: interactive session `true`, workstation
  locked `false`, x64 process/OS.
- Physical monitors (native bounds/work areas):
  - primary: bounds `(0,0)-(1920,1200)`, work area
    `(0,0)-(1920,1140)`, 120x120 DPI, 125%;
  - secondary: bounds `(1920,0)-(3840,1080)`, work area
    `(1920,0)-(3840,1032)`, 96x96 DPI, 100%.
- Virtual desktop: origin `(0,0)`; no negative-coordinate monitor.
- Available applications from capability/path probes: Chrome
  (`C:\Program Files\Google\Chrome\Application\chrome.exe`), Edge
  (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`), Brave
  (`C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`),
  Windows Terminal (`wt.exe`), and Windows Notepad. Firefox was unavailable.
- Initial candidate topmost fixture inspection found no `--topmost` option,
  `TopMost` assignment, `WS_EX_TOPMOST` declaration, or dedicated topmost
  scenario. This was a qualification gap, not a product defect.
- After the safety gate was recorded, the smallest authorized qualification-only
  fixture capability was added: GuineaPig accepts `--topmost`, propagates it to
  extra windows, sets WinForms `Form.TopMost`, and logs the option. No
  production TabDock code or dedicated scenario was added; physical topmost
  qualification remains blocked.

## Safety gate and first attempt

The native capability probe proves only an unlocked interactive session; it
is not proof that an operator has yielded the desktop exclusively to this
campaign. This agent cannot prove that the user is not simultaneously using
the desktop, and no independent supervised qualification lease was established.
The ValidationDriver's `--yes` flag would only skip its prompt; it would not
supply that missing supervision proof.

**Campaign first result:** `BLOCKED_SUPERVISED`, before any physical scenario,
mouse/keyboard input, capture, window mutation, or destructive setup.

- No `SendInput` call was issued by this campaign.
- No physical scenario was started; attempts are `0` for the scenario matrix.
- No reruns occurred.
- No first-attempt `FAIL_PRODUCT`, `FAIL_HARNESS`, or physical `PASS` exists.
- No guest, container, popup, or captured-member HWND was created by this
  campaign, so there are no campaign-owned HWND identities, geometry, z-order,
  foreground, or logical-membership observations to report.
- Existing doctor/log observations from earlier runs were not imported as
  physical scenario evidence.
- Existing `WindowFromPoint` → `GA_ROOT`, process/HWND identity, foreground,
  cleanup, and generation protections remain the required boundary for any
  future supervised attempt.

## Matrix disposition

| Capability / report | Candidate | Attempts | First result | Final result | Physical or deterministic | Evidence | Notes |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| Color/accent menu | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | No real input; deterministic coverage remains separate. |
| Workspace/group rename | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Entry, commit/cancel, click-away, long-name, and repeat paths not exercised. |
| Split menu/focus/resume | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Menu, partner focus, dismiss/end, dormant/resume paths not exercised. |
| `+` inline capture panel | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Open/close/reopen/cancel/capture paths not exercised. |
| Guest caption maximize/restore | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Synthetic maximize policy evidence is deterministic only. |
| Win+Up/restore | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2 | Required physical shortcut input was not sent. |
| Real F11 fullscreen | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2 | No browser scenario was started. |
| Dual-monitor transfer | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Two monitors exist; no supervised move/restore input. |
| Mixed DPI | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | 96/120 DPI exists; no UIA/physical measurement. |
| `WS_EX_TOPMOST` guest | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E3, E4 | Qualification-only `--topmost` fixture now exists; it was not exercised. |
| Unrelated foreground overlap | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | No unrelated foreground or owned-dialog interaction was started. |
| `LOCATIONCHANGE` load | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | Physical moving/resizing workload was not started; deterministic routing remains separate. |
| Physical title centering | `a131a9f` final | 0 | `BLOCKED_SUPERVISED` | `BLOCKED_SUPERVISED` | Physical gate | E1, E2, E4 | No UIA title-width/DPI measurement was possible without supervision. |

`E1` = read-only `dotnet run ... --plan physicalMixedDpi --configuration
Release --rid none` and the summarized `--plan all` capability probe.  
`E2` = starting-candidate `--version`/`--doctor` output, final-candidate
`--version` output, and the native monitor work-area probe; the final
candidate was rebuilt after its candidate commit before identity capture.
`E3` = initial and post-change source inspection of `PigOptions`, GuineaPig
argument parsing, form initialization, extra-window cloning, and
ValidationDriver catalog searches for `topmost`/`WS_EX_TOPMOST`.  
`E4` = deterministic gate commands recorded below; these are not physical
scenario evidence.

## Qualification-only harness changes and deterministic gates

The campaign made no production TabDock change. The authorized test-only
changes are:

- `TabDock.GuineaPig`: add the smallest `--topmost` switch, propagate it to
  cloned extra windows, set WinForms `Form.TopMost`, and include the value in
  startup logging.
- `TabDock.ValidationDriver`: correct the stale `CAT01` self-test expectation
  from 127 to the catalog's current 128 dispatchable scenarios. The first
  deterministic self-test run is retained as a valid `FAIL_HARNESS` result:
  runId `e66a69b1-c5a8-4664-92c1-92a60f9ca2a3`, `CAT01` failed, suite
  142/143, exit code 21. After the harness-only correction, runId
  `b9c42048-872a-4799-b8ae-442e9a57bb89` passed 143/143, emitted
  `deterministic-all status=PASS`, and emitted `RUN_MANIFEST result=PASS`.
- `docs/TESTING.md`: document the `--topmost` fixture option and its
  controlled topmost-band use.

Deterministic validation evidence:

- `dotnet build TabDock.sln -c Debug --no-restore`: PASS, 0 warnings, 0
  errors.
- `dotnet build TabDock.sln -c Release --no-restore`: PASS, 0 warnings, 0
  errors.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug
  --no-build --no-restore`: PASS, 725/725.
- Same unit suite in Release: PASS, 725/725.
- ValidationDriver Release and GuineaPig Release builds: PASS, 0 warnings,
  0 errors.
- ValidationDriver `--list`: catalog
  `scenario-catalog-2026-08-24-v1`, 128 dispatchable scenarios.
- ValidationDriver `--selftest all`: PASS, 143/143 after the stale-count
  harness correction.
- `openspec validate --all --strict --json`: PASS, 37/37 items; existing
  long-text notices are informational only.

These checks prove deterministic implementation and tooling behavior only.
They do not convert any blocked physical row into a physical pass.

`E5` = `scripts/validate.ps1 -Configuration Release -Ci -Publish`; it
completed successfully without a scenario argument or desktop input. NuGet
audit found no vulnerable packages. It passed Release app/driver/GuineaPig/
performance builds, 725/725 Release unit tests, the resource-stability gate,
native ABI contract self-test, version/doctor/recovery smokes, support-bundle
privacy inspection, OpenSpec validation 37/37, and the published executable
version smoke.

## Original-report disposition

1. **Chrome occlusion (accent, rename, split, `+`):** not physically
   reproduced in this session; not physically verified fixed; deterministic and
   prior controlled evidence only; capability blocked by the missing exclusive
   supervised lease; residual limitation is absent real-input proof.
2. **Off-center title:** not physically reproduced in this session; not
   physically verified fixed; deterministic structure/policy evidence only;
   physical UIA/DPI measurement is supervised-blocked.
3. **Guest maximize/fullscreen/monitor escape:** not physically reproduced or
   physically verified fixed; synthetic maximize/drift policy evidence remains
   deterministic only; guest title-bar, F11, and monitor-transfer input is
   supervised-blocked.
4. **Unreliable top layer:** not physically reproduced or physically verified
   fixed; deterministic local-pairing evidence remains separate. GuineaPig now
   has the smallest qualification-only `--topmost`/`Form.TopMost` fixture
   path, but no physical popup interaction was attempted and all supervised
   input remains blocked.

## Residual defects

No valid physical `FAIL_PRODUCT` occurred. Therefore this campaign made no
production repair and introduced no speculative z-order, geometry, polling,
reparenting, restyling, or topmost behavior. The initial missing GuineaPig
fixture capability was closed with a test-only switch; it was not evidence of
a TabDock product defect.

## Future supervised rerun

After a human operator establishes exclusive desktop ownership and agrees not
to touch mouse/keyboard input, build the exact candidate and run bounded named
scenarios from `docs/TESTING.md`, retaining the first attempt and artifact
manifest. Required physical commands include, as applicable:

```powershell
dotnet build TabDock.sln -c Release
dotnet build tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -c Release
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes rename
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes capture-inline-ui
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes guest-maximize-contained
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes <named-browser-or-topology-scenario>
```

Do not run these commands from the current unsupervised session. A future
valid first-attempt failure must be frozen, minimized, diagnosed, regression-
covered, fixed minimally, and rerun from fresh state; a later pass cannot erase
that failure.
