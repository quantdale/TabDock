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

## Acceptance matrix

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

## Original-report disposition

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
