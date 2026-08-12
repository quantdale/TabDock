## 1. Production fix

- [ ] 1.1 Add `App.ReconcileRestoredContainerZOrder()` that raises each
      restored group's container to the top of the normal z-order band via
      `WindowShepherdService.RaiseContainerForChrome(hwnd)` and logs a single
      bounded `STARTUP[reconcile]` diagnostic line.
- [ ] 1.2 Call it from `Application_Startup` immediately after the
      restored-container `foreach`, before `SyncWinEventMonitor()`/the
      "TabDock startup complete." log.

## 2. Regression scenarios

- [ ] 2.1 Add `startup-group-not-hidden-behind-existing-window` (reproduction):
      build a persisted empty group, launch a blocker covering the primary work
      area, relaunch TabDock, and assert with native `WindowFromPoint` +
      `GW_HWNDNEXT` z-order that the restored container is above the blocker.
      Register in `RunScenario` + `StandaloneExtraScenarios`.
- [ ] 2.2 Add `startup-does-not-steal-foreground-after-external-activation`
      (guard): after startup settles, activate the blocker and assert TabDock
      does not re-take foreground. Register similarly.
- [ ] 2.3 Add `startup-local-stack-above-unrelated-when-guest-present` (guard):
      assert the container sits below its active guest, the container caption is
      above the blocker, and the content center resolves to the guest. Register
      in `AllOrder` + switch.

## 3. Validation (CLI-safe)

- [ ] 3.1 Build `TabDock.csproj`, `TabDock.sln`, ValidationDriver, GuineaPig,
      and Spike with 0 warnings / 0 errors.
- [ ] 3.2 Run `scripts/validate.ps1`, `TabDock.exe --selftest-geometry`,
      `openspec validate --all --no-interactive`, and `git diff --check`.

## 4. Durable state / docs

- [ ] 4.1 Update `.agent/STATE.md`, `docs/internal/whole-codebase-audit-waypoint.md`,
      and `docs/internal/ui-ux-stabilization-waypoint.md` with the
      `POST-HARDENING FINDING`, root cause, fix, tests, and manual-acceptance
      status.
