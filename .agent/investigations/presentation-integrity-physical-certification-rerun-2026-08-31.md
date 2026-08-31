# Investigation: presentation-integrity physical certification rerun

**Date:** 2026-08-31  
**Status:** concluded for this desktop session — partial physical evidence, full
certification stopped after the environment could no longer establish a safe
TabDock foreground target  
**Campaign:** `openspec/changes/2026-08-31-presentation-integrity-physical-certification/`  
**Prior evidence:** preserved unchanged in
`.agent/investigations/presentation-integrity-physical-certification-2026-08-31.md`

## Question and disposition rule

Does the current Release candidate physically reproduce and resolve the
presentation-integrity reports under the user-granted exclusive supervised
lease? A raw driver `FAIL_PRODUCT` is retained as first-attempt evidence. It is
not silently converted to PASS; after preserving it, the failure is classified
as product, harness, environment, supervision, or capability only when the
artifact and source establish that distinction.

A valid physical attempt requires all of the following at the click point:

- interactive, unlocked input desktop;
- candidate process and HWND generation identity;
- test-owned or explicitly adopted target provenance;
- `WindowFromPoint` followed by `GA_ROOT` ownership;
- foreground and target continuity;
- no foreign top-level window covering the target point;
- cleanup limited to registered test-owned identities.

The user explicitly granted exclusive mouse/keyboard supervision for this
rerun. The driver still retained every native identity and clickability guard;
no blind click or guard bypass was used.

## Candidate identity

- Starting `HEAD`, `origin/main`, and branch at orientation:
  `dd5f819484498b1e74678710bde58d55fbdcf8fa`, `main`.
- Candidate executable:
  `bin/Release/net8.0-windows/win-x64/TabDock.exe`.
- `--version`: TabDock 1.0.0, Release, `win-x64`, self-contained, X64;
  embedded commit matches `dd5f819484498b1e74678710bde58d55fbdcf8fa`.
- Candidate executable SHA-256:
  `D2BC99361705240FD1EAB14784D7AA3807AFB1F6F00F870B3C982EE2C3E106A9`.
- All physical artifacts in this rerun record candidate SHA
  `dd5f819484498b1e74678710bde58d55fbdcf8fa`.

## Physical environment and lease evidence

The no-input physical plan was run before and after the scenario work:

```text
dotnet run --project tests/ValidationDriver/TabDock.ValidationDriver/TabDock.ValidationDriver.csproj --configuration Release --no-build -- --plan physicalMixedDpi --configuration Release --rid none
```

The final probe reported `interactiveSession=true`, `workstationLocked=false`,
`sendInputAvailable=true`, two monitors, and `mixedDpi=true`. `--doctor`
reported Windows 11 Pro family (raw product label Windows 10 Pro), 25H2, build
26200 revision 9278, .NET 8.0.30, standard-user session 1.

Monitor topology:

- primary: bounds `(0,0)-(1920,1200)`, work area `(0,0)-(1920,1140)`,
  120x120 DPI / 125%;
- secondary: bounds `(1920,0)-(3840,1080)`, work area `(1920,0)-(3840,1032)`,
  96x96 DPI / 100%;
- virtual desktop origin `(0,0)`; no negative-coordinate monitor.

Capability probes found Chrome, Edge, Brave, Windows Terminal, and Notepad;
Firefox was unavailable. `notepadBrokerBehaviorDetectable=true`,
`stageBAvailable=false`, and candidate signing was not configured.

The accepted physical runs emitted valid lease checkpoints in their timelines.
Those checkpoints record candidate/test-runner identities, owned TabDock and
GuineaPig HWNDs, adopted browser descendants where applicable, point ownership
through `WindowFromPoint`/`GA_ROOT`, foreground transitions, and cleanup
identity. `EnsureClickable` proceeded only when the resolved root matched the
expected owned target. When Edge or Chrome covered a point, the driver wrote an
identity diagnostic and refused the click instead of fighting the foreground.

The lease could not be re-established for the last two `dragreorder` attempts:

- `93118e80-2faf-45ed-aa69-81d32847088a`;
- `c31afe50-d5dd-4a26-b20f-336b71b96f64`.

Both failed during generic scenario setup with
`ValidationDriver could not establish a verified TabDock foreground target.`
No guest setup, click, or destructive scenario action followed either failure.
This is the known Windows/sandbox foreground-arrangement limitation documented
in `KNOWN_ISSUES.md`; the generic guard remains intentionally hard and was not
weakened. Physical execution stopped at that point. No additional physical
input was sent after the second failure.

## Qualification-only harness changes

No production `TabDock` source was changed. The rerun made only test-harness
changes:

- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Core.cs`: remove
  unconditional post-capture `ForceForeground` gates from `Rename` and
  `TabSwitchHideSafety`; require `EnsureClickable` before each caption/tab
  click and stop the scenario if the point is not directly owned.
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs`:
  reacquire the live UIA root while the inline capture surface is rendering,
  wait boundedly for the target row, and require `EnsureClickable` before the
  checkbox and `Add selected` clicks. `AddWindowToggle` also reacquires the
  live root when determining panel state.
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Drag.cs`: replace
  the post-capture hard `ForceForeground` gate in `DragReorder` with the same
  direct clickability proof; an obscured strip still fails closed.

These changes remove false negatives caused by a failed foreground-arrangement
request or stale UIA tree; they do not weaken point ownership, provenance, or
cleanup safety. The generic `StartScenario` foreground gate remains unchanged.

## Complete rerun ledger

The JSON manifests and timelines remain under
`artifacts/physical-certification-rerun-20260831/<run-id-without-hyphens>/`.
`raw` is the stored manifest result. `analysis` is the evidence-backed
classification used for campaign disposition.

| Run ID | Scenario(s) | Raw result | Analysis | Evidence summary |
| --- | --- | --- | --- | --- |
| `49ba0172-5e45-4c74-bb6d-a853ac9acdda` | `rename` | `FAIL_PRODUCT` | `FAIL_HARNESS` | Capture succeeded; the old unconditional container `ForceForeground` gate failed before rename input. Preserved as first attempt. |
| `e0cb4cc2-e392-4bab-a5db-1f85cbda3b9a` | `tabswitch-hidesafety` | `FAIL_PRODUCT` | `FAIL_HARNESS` | Same old post-capture foreground gate failed before the tab loop. Preserved as first attempt. |
| `a3f19024-ca4e-4161-a740-7de020979e8f` | `rename` | `PASS` | `PASS` | Additional supported rename pass. |
| `4da39053-0b9d-4ebb-9d37-0a636229da1b` | `rename-edge-cases` | `PASS` | `PASS` | Empty/normal/long-name, Escape, persistence, geometry, and guest-liveness checks passed. |
| `03c3aaa2-37d1-41bd-9382-98fcd2d470ad` | `split-exit` | `PASS` | `PASS` | Twenty split enter/exit cycles retained membership and presentation. |
| `8ebd4246-5a0f-401d-bdbd-a5b589297276` | `capture-inline-ui` | `FAIL_PRODUCT` | `FAIL_HARNESS` | Inline surface opened; stale UIA/direct clicks did not add the second tab. Preserved before the live-root correction. |
| `5895996f-059e-430f-9d0f-2a961bafbf7e` | `capture-inline-ui` | `PASS` | `PASS` | Corrected live-root wait and guarded clicks passed checkbox toggle, add, docking, and cleanup. |
| `ec20b719-d0bb-4a4a-a1ec-b81a9451b5de` | `group-dropdown-stability`, `group-rename-menu`, `add-window-toggle` | mixed: 2 `PASS`, 1 `FAIL_PRODUCT` | 2 `PASS`, 1 `FAIL_HARNESS` | Menus and rename passed. Add-window final capture hit the same stale UIA/direct-click false negative. |
| `20eb043f-144f-40c0-9d67-183ed2ec9abe` | `add-window-toggle` | `PASS` | `PASS` | Twenty toggle cycles, Cancel, final capture, docking, auto-close, and cleanup passed after correction. |
| `c6ce4a3e-4292-4cfa-998f-afb3ee9f5770` | `rename`, `tabswitch-hidesafety` | `PASS` | `PASS` | Corrected rename passed; 24 real tab clicks across three guests retained all tabs, guests, and final rendering. |
| `2971449b-5973-4f16-b777-97468c574563` | `split-repeat-cycles`, `split-focus-bidirectional` | `PASS` | `PASS` | Twenty split/rejoin cycles and twenty bidirectional focus cycles passed. |
| `ffa877f2-cf46-4770-9d3e-e5e110e9708d` | `contextmenu-render-stability`, `chrome-click-render-stability`, `directclick-foreground-pairing` | `PASS` | `PASS` | Twenty context-menu cycles, Chrome/tab rendering cycles, and external foreground/direct-click pairing passed. |
| `c5d0d932-f26a-4f82-8ecb-c8c6c0193283` | `split-resize`, `split-maximize-restore-no-overlap` | mixed: `PASS`, `FAIL_PRODUCT` | `PASS`, `BLOCKED_ENVIRONMENT` | Resize passed. Re-maximize was refused at `(1331,212)` because `GA_ROOT=0xD0F92` was an unrelated Edge window. |
| `2fe8f345-d912-4bc3-b90d-a98ccbbf39b2` | `guest-maximize-contained`, `maximize-repro` | `PASS` | `PASS` for the declared synthetic/container paths | Synthetic guest `ShowWindow(SW_SHOWMAXIMIZED)` containment and container-caption pig maximize cycles passed; this is not proof of a physical guest caption or Win+Up path. |
| `820f3cd0-23a6-4db5-8d5a-33a40b0232ac` | `maximize-repro --guest wt` | `FAIL_PRODUCT` | `BLOCKED_CAPABILITY` | Windows Terminal reused its existing monarch/current shell; no new `CASCADIA_HOSTING_WINDOW_CLASS` with proven launcher ancestry was admitted. No guest input was sent. |
| `e338f2a4-8819-4e83-a338-5edb549a7a4e` | `split-drag-release-render-stability`, `drag-release-render-stability` | `FAIL_PRODUCT` | `BLOCKED_ENVIRONMENT` | Early cycles ran, then no-op/covered drag points and unrelated Chrome coverage caused fail-closed refusal. No blind drag was attempted. |
| `96640587-76a3-4a4a-8f1b-5081d640892b` | `split-reorder`, `dragreorder` | mixed: `PASS`, `FAIL_PRODUCT` | `PASS`, `FAIL_HARNESS` | Split reorder passed. Dragreorder hit the old hard post-capture foreground gate before drag input. |
| `93118e80-2faf-45ed-aa69-81d32847088a` | `dragreorder` | `FAIL_HARNESS` | `FAIL_HARNESS` | Generic `StartScenario` could not establish a verified TabDock foreground target; no scenario setup/input followed. |
| `c31afe50-d5dd-4a26-b20f-336b71b96f64` | `dragreorder` | `FAIL_HARNESS` | `FAIL_HARNESS` | Same generic foreground failure on the consecutive retry; physical phase stopped. |

No raw failure above is treated as a valid product failure. The old hard gate
and stale UIA failures are preserved because they are first-attempt artifacts;
the corrected passes demonstrate the guard fixes without altering production
behavior. Foreign-window failures remain blocked because the driver correctly
refused to click an obscured point.

## Initial acceptance matrix (historical unsupervised checkpoint)

| Requested physical cell | Final disposition | Physical evidence and boundary |
| --- | --- | --- |
| Color/accent selector cycles | `SKIP_CAPABILITY` | `openspec/specs/group-color-picker/spec.md` documents `PickColorCommand` as a deliberate no-op; production has no reachable color selector. Zero cycles were fabricated. |
| Workspace/group rename | `PASS` for exercised supported paths | `c6ce...` and `4da...` pass entry, commit, Escape/edge cases, persistence, long-name, geometry, and guest liveness. First `49ba...` raw failure is `FAIL_HARNESS`. |
| Split creation, focus, dismissal, exit, resume | `PASS` for exercised paths | `03c3...`, `297...`, `966...` and group-menu runs pass repeated split/exit, partner focus, membership, and reorder paths. Full requested matrix was not claimed. |
| Inline `+` open/close/reopen/cancel/capture | `PASS` for exercised paths | `589...` and `20eb...` pass guarded live-root interactions, toggle cycles, Cancel, capture, docking, and cleanup. First `8eb...`/`ec20...` raw failures are `FAIL_HARNESS`. |
| Guest caption maximize/restore | `BLOCKED_CAPABILITY` | No driver scenario performs a physical click on a real guest caption. `guest-maximize-contained` uses synthetic `ShowWindow` and is reported separately. |
| Win+Up/restore | `BLOCKED_CAPABILITY` | No ValidationDriver scenario sends the required physical `Win+Up`; no shortcut result is claimed. |
| Real F11 fullscreen on Chrome/Edge/Brave | `BLOCKED_CAPABILITY` | Catalog/source search found no F11/VK_F11/fullscreen physical scenario. No F11 input was sent; Firefox is unavailable. |
| Dual-monitor transfer | `BLOCKED_CAPABILITY` | Two physical monitors and mixed DPI were observed, and physical captures occurred on the available desktop. No explicit supervised transfer/restore scenario was dispatched. |
| Mixed-DPI transition/measurement | `BLOCKED_CAPABILITY` | 96/100% and 120/125% monitors were present; no complete transfer plus UIA/title measurement path was available. |
| `WS_EX_TOPMOST` guest | `BLOCKED_CAPABILITY` | GuineaPig has a qualification-only `--topmost` fixture, but no ValidationDriver scenario dispatches it. No topmost claim is made. |
| Unrelated foreground overlap and local z-order | `PASS` for exercised paths | `ffa...` direct-click pairing used external Notepad/browser foreground changes and verified guest/container repair. Covered points in other runs were refused with diagnostics. |
| `EVENT_OBJECT_LOCATIONCHANGE` load/storm | `BLOCKED_CAPABILITY` | No dedicated comprehensive physical load matrix was dispatched. Synthetic and bounded drag evidence is not substituted for it. |
| Physical title centering | `BLOCKED_CAPABILITY` | No physical title-width/name/DPI measurement scenario exists. Rename geometry stayed unchanged, but that is not title-centering certification. |

## Initial original-report disposition (historical)

1. **Chrome occlusion:** exercised rename, tabs, split, inline capture, menu,
   Chrome-click, and direct foreground paths did not reproduce a product defect
   after harness false negatives were corrected. This is bounded evidence, not a
   claim that unexercised F11/topmost/load paths are certified.
2. **Off-center title:** remains physically unverified because the required
   title-width/name/DPI measurement cell is unavailable. No production geometry
   repair was justified.
3. **Guest maximize/fullscreen/monitor escape:** synthetic containment passed;
   real guest-caption, Win+Up, F11, and monitor-transfer cells remain blocked.
   The synthetic result is not promoted to physical evidence.
4. **Unreliable top layer:** exercised local menu/guest/container pairing passed,
   and all foreign-covered points were fail-closed. Topmost and full load
   qualification remain unavailable; no speculative z-order repair was made.

## Deterministic evidence

The current candidate and harness were rebuilt and checked after the source
changes. Final deterministic evidence includes:

- Debug and Release `dotnet build TabDock.sln`: PASS, 0 warnings, 0 errors;
- Debug and Release unit tests from
  `tests/UnitTests/TabDock.UnitTests.csproj`: PASS, 725/725;
- Release ValidationDriver and GuineaPig builds: PASS, 0 warnings, 0 errors;
- catalog listing: `scenario-catalog-2026-08-24-v1`, 128 dispatchable scenarios;
- final ValidationDriver selftest runId
  `e7b8e777-be37-4396-bdcd-67b79cd80639`: `suite=all passed=143 failed=0 total=143`,
  deterministic-all PASS and run manifest PASS;
- `openspec validate --all --strict --json`: PASS, 37/37;
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS, including
  NuGet audit with no vulnerable packages, resource stability, native ABI,
  recovery, support-bundle privacy, publish version smoke, and 725/725 Release
  tests; no scenario argument or desktop input was used.

These checks prove deterministic tooling and source behavior only. They do not
convert blocked physical cells into PASS.

## Residual defects and archive decision

No analytically valid physical `FAIL_PRODUCT` occurred. The only changes needed
were qualification-harness corrections for two known false-negative classes:
post-capture `ForceForeground` arrangement and stale inline UIA state. No
production TabDock fix, reparenting, z-order workaround, geometry adjustment,
extra polling, or topmost behavior was introduced.

The active OpenSpec change must **not** be archived as fully certified yet.
Major user-requested cells remain `BLOCKED_CAPABILITY` or
`BLOCKED_ENVIRONMENT`, especially real guest caption/Win+Up, F11, monitor
transfer, mixed-DPI measurement, topmost, load, and title centering. Retain the
active change and this rerun report. A future session should use a fresh
candidate and run only after the generic foreground lease is stable; a valid
first failure must remain authoritative and cannot be erased by a later pass.

## Continuation — valid browser F11 product failure

The next continuation obtained a stable native lease after the target-only
foreground arrangement fallback moved the test-owned target away from a
foreign window that covered every probe point. The first real browser F11
attempt was therefore actionable and is retained as a valid product failure.

- Run: `7f5ba57f-af6e-491e-81aa-b33bd8229471`
- Scenario: `browser-fullscreen-contained`
- Guest: isolated `chrome-normal`
- Candidate at action time: `b0975b2a724f0cf9551c4e106dfc6449c8643002`
- Raw artifact:
  `C:\Users\palac\AppData\Local\Temp\TabDock-Validation\runs\7f5ba57faf6e491e81aab33bd8229471\browser-fullscreen-contained.json`
- Raw result: `FAIL_PRODUCT`
- Qualification: exact browser HWND/PID/start identity, active lease,
  `WindowFromPoint` → `GA_ROOT`, exact foreground, real F11 `SendInput`, and
  test-owned cleanup all passed. No harness or capability refusal occurred.

Before F11, Chrome was captioned and contained at
`(96,196)-(1321,896)`, with style `0x16CF0000`, exstyle `0x200100`,
`zoomed=false`, and `showCmd=1`. After the real F11 action, the same browser
identity reported borderless fullscreen at `(0,0)-(1920,1200)`, style
`0x160B0000`, exstyle `0x200000`, `zoomed=false`, `showCmd=1`, primary monitor
120 DPI, parent `0`, and the host-center point still resolved to the browser
root. The transition also produced one
`SHEPHERD[drift-reconcile]` event.

The product then attempted to restore the assigned
`(96,196,1225x700)` rectangle, but `SHEPHERD[size-constraint]` refused it
because the fullscreen browser reported a native minimum equal to the full
monitor. The artifact stopped at
`chrome-normal F11 cycle 1/2 after-enter-reconcile: guest remains assigned to
host content rect`; the guest remained full-screen. Product log lines
4957–4963 preserve the repeated drift-reconcile, position, and native-minimum
refusal sequence. This is the first invariant failure: Shepherd attempted
ordinary pane containment while the browser was still in borderless F11
presentation, so the native minimum guard prevented the repair.

The raw artifact and log are frozen. It is not relabeled after subsequent
repair runs.

## Continuation — browser repair and requalification

The first valid Chrome failure was followed by a minimal production repair,
not a driver-only relabel:

- `Services/NativePresentationRestorePolicy.cs` isolates the restore decision:
  iconic/zoomed windows use the ordinary restore path, while a known
  Chromium guest that is borderless and outside its assigned rectangle may
  use the browser's own F11 exit path.
- `WindowShepherdService.RequestBrowserFullscreenExit` requires the current
  captured HWND, process-start identity, executable identity, and mutation
  generation. It posts one `WM_KEYDOWN`/`WM_KEYUP` F11 pair to that exact
  browser HWND and suppresses duplicate requests until the resulting
  `LOCATIONCHANGE` returns a captioned window. The normal pane
  `SetWindowPos` path then runs. No guest is reparented or restyled, and no
  unrelated window is touched.
- `ContainerWindow.ReconcilePresentationDrift` observes the browser's returned
  native style before the next repair. A pending browser exit is removed when
  the caption/thick-frame state is visible.
- The focused policy unit contract passed 5/5 after the Release build.

The first repair experiment, `9a655af7-2a2f-4641-a0a1-fda9d67f996e`, tried
`SWP_NOSENDCHANGING`; Chrome reasserted fullscreen and remained outside its
assigned pane. That experiment was not retained as the fix. The first posted
F11 repair run, `713da0e7-146e-440e-86bb-6597b6342d5d`, reached the normal
pane but the driver asserted before the asynchronous dock settled. Its raw
timing evidence led to the bounded `IsDocked` wait and pending-request guard.
The fresh Chrome requalification
`71656457-b555-42ce-a782-b4947f33f292` passed two real F11 cycles: each
physical F11 entered browser-owned borderless fullscreen, one repair request
exited that mode, and the same identity returned to the assigned pane with
parent `0`, unchanged membership, exact point ownership, and live pixels.

Edge exposed a browser-specific point qualification boundary. The first run
`0e559627-46f5-463c-9560-ec64913fb3d0` was raw `FAIL_PRODUCT` only because
the host-center `WindowFromPoint` root was Edge's same-process registered
`Chrome_WidgetWin_1` dynamic surface (`0x5481ED0`), not the captured guest
root (`0xB01EBC`). PID, process start, executable, and owned provenance all
matched; this was not foreign occlusion. The driver now accepts this
browser-owned surface only when its registered `.DynamicSurface` role and all
identity fields match. The raw artifact is preserved. The next Edge run
`443624df-51bb-4801-a8ad-249b498302b5` reached the repair request but remained
fullscreen at the assertion boundary and is preserved as intermediate timing
evidence. The fresh Edge run
`9cb2ad2a-a6bc-41a2-b196-2492702b9331` passed two cycles after the bounded
settle/duplicate guard. Brave passed two cycles in
`01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6`.

Raw browser artifacts:

- Chrome failure:
  `C:\Users\palac\AppData\Local\Temp\TabDock-Validation\runs\7f5ba57faf6e491e81aab33bd8229471\browser-fullscreen-contained.json`
- Chrome requalification:
  `C:\Users\palac\AppData\Local\Temp\TabDock-Validation\runs\71656457b55542cea782b4947f33f292\browser-fullscreen-contained.json`
- Edge qualifier failure:
  `C:\Users\palac\AppData\Local\Temp\TabDock-Validation\runs\0e55962746f5463c9560ec64913fb3d0\browser-fullscreen-contained.json`
- Edge intermediate failure:
  `C:\Users\palac\AppData\Local\Temp\TabDock-Validation\runs\443624df51bb4801a8ad249b498302b5\browser-fullscreen-contained.json`

## Continuation — remaining physical matrix cells

The following fresh supervised runs passed after the browser repair:

| Run ID | Scenario | Evidence |
| --- | --- | --- |
| `01d148a1-f381-4023-87f4-a6c2e6e2371f` | `guest-caption-maximize-contained` | Two real GuineaPig caption maximize/restore cycles; native `SC_MAXIMIZE`, same HWND/process identity, parentless guest, assigned monitor/point, tab, and live render. |
| `708fafaa-5da3-48ca-87c1-4daa0d4d77e5` | `guest-win-up-contained` | Two real Win+Up cycles; native `zoomed=True`, `showCmd=3`, same identity, monitor, point, tab, and render. |
| `f423509e-f869-4097-b938-964355bd9101` | `dual-monitor-mixed-dpi-transfer` | Primary `(0,0)-(1920,1200)` at 120 DPI and secondary `(1920,0)-(3840,1080)` at 96 DPI; identity-checked container placement crossed both monitors; real Win+Shift+Arrow directions were recontained without guest roam; 120/96 container DPI observed. |
| `f3ee2adb-2d4b-46ce-973f-ccbf789e5aca` | `topmost-guest-interaction` | Captured GuineaPig was identity-pinned into `WS_EX_TOPMOST`; direct text input, group popup, rename editor, unrelated foreground steal, reactivation, parentless docking, and normal-band container checks passed. |
| `345e33f8-8086-4819-9e5f-72acbdec45ed` | `locationchange-controlled-load` | Unrelated 18-iteration native move load produced zero Shepherd repairs; captured 12-iteration load produced 20 bounded repair lines (10 drift reconciles, 10 positions). Metrics delta: callbacks 58, rejected 33, membership 49, dispatch 25, posts 25, lifecycle 25; UIA response 859 ms and no exception. |
| `1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19` | `title-centering-physical-measurement` | Short and long names measured at primary 120 DPI and secondary 96 DPI. UIA title midpoint error was 0.50/0.00/0.00/0.50 px; all monitor and docking checks passed. |

Each scenario also had an earlier raw harness/timing failure while its
settle or setup guard was being finalized:
`0d6d7805-a350-4c90-8fba-04315f5a461e` (dual monitor),
`fea6d7ec-3b8e-4034-86cd-f9442214fe24` (topmost before capture),
`0cff0057-6b70-4f5d-8932-5f2c0c5c6a93` (metrics assertion and responsiveness
threshold), and `a6df7abf-c993-4205-a937-213b2275a910` (post-move docking
settle). Their raw artifacts remain under the same Temp run root and are not
promoted to product failures.

## Continuation — residual repair boundary

The only analytically valid product failure in this continuation is the first
Chrome F11 run. The repair is intentionally limited to known Chromium
executables and a pending identity-checked F11 request. GuineaPig,
Notepad, Windows Terminal, unknown executables, foreign point roots, stale
HWNDs, and invalid leases continue through the existing fail-closed paths.
The driver-side `GW_ENABLEDPOPUP`/dynamic-surface diagnostics document Edge's
registered same-process surface; they do not broaden acceptance of foreign
windows.

## Continuation — adjacent qualification subset

Fresh Release runs against the repaired tree passed the adjacent cells:

| Run ID | Scenario | Raw result | Evidence |
| --- | --- | --- | --- |
| `557d013b-9b2f-41b9-aba1-14adb5dc0e4c` | `rename` | `PASS` | Rename input, persistence, unchanged geometry, and cleanup passed. |
| `cce9dd41-589a-483b-a4f9-eb331a5c0276` | `group-rename-menu` | `PASS` | Menu rename, whitespace rejection, persistence, and cleanup passed. |
| `b75e8f44-206e-4eac-9b0b-354ab8ae8a09` | `add-window-toggle` | `PASS` | Twenty toggle cycles, Cancel, reattach, second-tab capture, docking, and cleanup passed. |
| `e00a0d66-8cbb-40ab-b895-6356f3943a54` | `capture-inline-ui` | `PASS` | Inline surface, guarded checkbox, second-tab capture, docking, and no picker passed. |
| `20c6ac8c-9ee8-41d3-b9c3-6e226a851cd9` | `split-focus-bidirectional` | `PASS` | Four real left/right focus cycles retained both panes and one composite item. |
| `c3499c7b-c432-4986-842a-452478542f95` | `contextmenu-render-stability` | `PASS` | Twenty context-menu cycles retained visibility and docking. |
| `fe0b31c6-17ac-4237-983f-79a4222a5a6b` | `chrome-click-render-stability` | `PASS` | Eight Chrome/tab render cycles retained the same docked guest. |
| `2bece2cf-1aa6-406d-901a-314b7527e9a0` | `directclick-foreground-pairing` | `PASS` | Real Notepad foreground steal, one direct guest click, text input, z-order repair, and cleanup passed. |
| `7e98b408-539d-44fd-a7e8-60690e858e40` | `dragreorder` | `PASS` | Reorder applied once, zero immediate flip-back pairs, bounded reorder count, drag-out release, and cleanup passed. |

`split-exit` was attempted twice from fresh state:
`58f04b3a-ffbc-4b7b-aa56-b1ee5c32a9fd` and
`de3a5cc7-e7b0-4e2c-ad2a-a7c6ce1aa33c`. Both raw results were
`BLOCKED_ENVIRONMENT`: the target point resolved to the exact owned container,
but the foreground remained on the other owned guest after the guarded frame
activation, so the driver refused the split-exit click. No blind input or
product failure occurred. The earlier accepted split-exit run
`03c3aaa2-37d1-41bd-9382-98fcd2d470ad` remains preserved as adjacent
behavioral evidence; the two fresh blocks are reported rather than converted
to PASS.

The adjacent subset therefore has nine fresh PASS cells and one supervised
foreground-environment block. All raw manifests remain under the external
ValidationDriver Temp run root; no run was edited or best-of-N substituted.

The `split-exit` retry
`f768ff27-2931-4705-8130-89b0bf23a95d` completed thirteen split
enter/exit cycles with all split, membership, tab-count, and cleanup assertions
passing before a later test point resolved to an unregistered foreign root
(`unregistered-window-not-owned-by-test-run`). The guard refused that input;
the raw result is `BLOCKED_ENVIRONMENT`, not `FAIL_PRODUCT`.

## Final deterministic and CI-safe gates before integration

The final working tree passed the non-input gates after the catalog expectation
was updated from the stale 128-entry count to the observed 135-entry catalog:

- Debug and Release `dotnet build TabDock.sln`: PASS, 0 warnings, 0 errors.
- Debug and Release unit tests: PASS, `732/732` in each configuration.
- Release ValidationDriver and GuineaPig builds: PASS, 0 warnings, 0 errors.
- ValidationDriver selftest run
  `4588e75b-f822-45b3-b300-31ac6abb1100`: `153/153`, including all
  foreground seam and catalog tests; deterministic-all and run manifest PASS.
- `--list`: catalog generation
  `scenario-catalog-2026-08-24-v1`, `135` dispatchable scenarios.
- `--plan release` and `--plan physicalMixedDpi`: PASS; the live topology
  reported two monitors at 96/120 DPI and the four new physical cells were
  runnable under the supervised lease.
- `openspec validate --all --strict --json`: PASS, `37/37`.
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS. Restore
  audit reported no vulnerable packages; Release build, driver/fixture,
  performance compile, 732/732 tests, 32-cycle headless resource stability,
  native ABI, version/doctor/pending-recovery, recovery-process,
  support-bundle privacy, OpenSpec, publish, and published `--version` smoke
  all passed. The gate used no desktop input.

The CI-safe executable and physical artifacts above were produced before the
integration commit, so their embedded pre-commit source identity is
`b0975b2a724f0cf9551c4e106dfc6449c8643002`. The post-push source identity and
fresh version smoke are recorded in the session delivery after integration;
the pre-commit physical run IDs remain bound to the candidate they actually
exercised.

## Final acceptance matrix after supervised continuation

| Requested physical cell | Final disposition | Evidence and boundary |
| --- | --- | --- |
| Color/accent selector cycles | `SKIP_CAPABILITY` | `PickColorCommand` is a documented deliberate no-op; no reachable selector exists and no cycles were fabricated. |
| Workspace/group rename | `PASS` for exercised paths | Fresh `557d013b-9b2f-41b9-aba1-14adb5dc0e4c` and prior edge-case passes covered input, persistence, geometry, and cleanup. |
| Split creation, focus, dismissal, exit, resume | `PASS` for exercised paths; `BLOCKED_ENVIRONMENT` on final split-exit requalification | Fresh bidirectional focus `20c6ac8c-9ee8-41d3-b9c3-6e226a851cd9` passed. `split-exit` had accepted pass `03c3aaa2-37d1-41bd-9382-98fcd2d470ad`; three later attempts failed closed only on foreground/foreign-point qualification. |
| Inline `+` open/close/reopen/cancel/capture | `PASS` for exercised paths | Fresh `e00a0d66-8cbb-40ab-b895-6356f3943a54` and `b75e8f44-206e-4eac-9b0b-354ab8ae8a09` passed guarded live-root interactions, Cancel, second-tab capture, docking, and cleanup. |
| Guest caption maximize/restore | `PASS` | GuineaPig physical caption scenario `01d148a1-f381-4023-87f4-a6c2e6e2371f` passed native maximize/restore and containment checks. |
| Win+Up/restore | `PASS` | GuineaPig `708fafaa-5da3-48ca-87c1-4daa0d4d77e5` passed two real Win+Up cycles with native zoom/show-command evidence. |
| Real F11 fullscreen on Chrome/Edge/Brave | `PASS after repair; first Chrome failure retained` | Chrome `71656457-b555-42ce-a782-b4947f33f292`, Edge `9cb2ad2a-a6bc-41a2-b196-2492702b9331`, and Brave `01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6` passed. Firefox was unavailable. |
| Dual-monitor transfer | `PASS` | `f423509e-f869-4097-b938-964355bd9101` passed both physical Win+Shift+Arrow directions and same-monitor guest containment. |
| Mixed-DPI transition/measurement | `PASS` for available topology | `f423509e-f869-4097-b938-964355bd9101` observed 120/96 DPI transfer; `1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19` measured titles on both monitors. |
| `WS_EX_TOPMOST` guest | `PASS` for controlled fixture | `f3ee2adb-2d4b-46ce-973f-ccbf789e5aca` pinned the captured guest topmost and passed direct input, popup, rename, foreground recovery, and normal-band container checks. |
| Unrelated foreground overlap and local z-order | `PASS` for exercised paths | Fresh `2bece2cf-1aa6-406d-901a-314b7527e9a0` passed Notepad steal/direct guest click and z-order pairing; foreign covered points remained fail-closed. |
| `EVENT_OBJECT_LOCATIONCHANGE` load/storm | `PASS` for controlled load | `345e33f8-8086-4819-9e5f-72acbdec45ed` passed unrelated-load zero repairs, captured-load bounded repairs, metrics, and 859 ms UIA response. |
| Physical title centering | `PASS` for exercised width/DPI matrix | `1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19` passed short/long names on primary 120 DPI and secondary 96 DPI, with midpoint error ≤0.50 px. |

This matrix distinguishes a bounded supervised environment block from a
product failure. The only valid `FAIL_PRODUCT` remains the frozen first Chrome
F11 run; the production repair is limited to that demonstrated browser
presentation defect.
