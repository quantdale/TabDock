## 1. Native host fill

- [x] 1.1 In `Infrastructure/NativeHwndHost.cs`, change the `TabDockContentHost` class `hbrBackground` from `CreateSolidBrush(0x001E1E1E)` to COLORREF `0x000D0907` (RGB of `TdContentVoidBrush` `#07090D`, packed `0x00BBGGRR`). Keep `WindowClass = "TabDockContentHost"`. Do not unregister or rename the class.
- [x] 1.2 Replace the stale comment that claims the brush matches WPF `#1E1E1E` with a comment that names `TdContentVoidBrush` `#07090D` and the airspace reason the native fill is the visible void.

## 2. Contract test and maintainer docs

- [x] 2.1 Extend `tests/UnitTests/FrontendDesignContractTests.cs` with a lockstep fact modeled on `DwmChromePaletteMatchesSharedTokenDefinitions`: assert `TdContentVoidBrush` `Color="#07090D"` in `App.xaml`, assert `CreateSolidBrush(0x000D0907)` (or the same packed value) in `NativeHwndHost.cs`, and assert `WindowClass` remains `"TabDockContentHost"`. The test MUST fail on a channel-swapped `0x0007090D`. Added `NativeContentHostFillMatchesSharedContentVoidToken` (regex-extracts the actual `hbrBackground` pack so a comment containing the right value cannot mask a swapped brush).
- [x] 2.2 In `docs/FRONTEND.md` airspace section, state that the native content-host fill is the visible void and MUST stay in lockstep with `TdContentVoidBrush`; point to the new contract test.

## 3. Verification

- [x] 3.1 Run `dotnet test tests\UnitTests\TabDock.UnitTests.csproj -c Debug` and `-c Release`; the new lockstep fact and existing frontend contracts MUST pass with 0 warnings. Evidence: 830/830 passed in both configurations (was 829; +1 lockstep fact), 0 skipped, no build warnings.
- [x] 3.2 Confirm `ContentHost` remains `Focusable="False"` with automation id `ContentHost` in `Views/ContainerWindow.xaml` (no driver-contract drift). Confirmed at `Views/ContainerWindow.xaml:605-607`; locked by `AccessibilityContractsExposeNamesAndAvoidDeadTabStops` and `ContainerPreservesPresentationAndDriverContracts`.
- [x] 3.3 If a supervised desktop is available, open an empty workspace and confirm the field around `EmptyStateOverlay` matches `#07090D` rather than `#1E1E1E`. Record `BLOCKED_ENVIRONMENT` if that lane cannot run; do not treat the absence as a product failure.
  - Recorded `BLOCKED_ENVIRONMENT` (2026-09-14): no supervised interactive desktop lane available this session. The structural token/COLORREF lockstep test is the authoritative gate per design.md; absence is not a product failure.
- [x] 3.4 Run `openspec validate align-native-content-host-void --strict` after any artifact edit made during apply.
