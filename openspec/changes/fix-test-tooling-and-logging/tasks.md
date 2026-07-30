# Tasks

## 1. Spike window-ownership safety

- [x] 1.1 In `FindCmdWindow`, require `GetWindowThreadProcessId(candidate) == cmd.Id`; continue the retry loop on mismatch
- [x] 1.2 In `--host`/`--checker` child modes, validate `IsWindow` + expected class/PID before `SetParent`; fail cleanly on malformed args

## 2. Spike spawn-pattern compliance

- [x] 2.1 Route the `taskkill` spawn through `SpawnGuarded`; bump `MaxTotalSpawns` accordingly
- [x] 2.2 Verify the spawned process is tracked by `KillAllTracked`

## 3. ValidationDriver portability

- [x] 3.1 Resolve `TabDockExe`/`PigExe` relative to the driver assembly location; probe well-known paths / PATH for `ChromeExe`/`EdgeExe`
- [x] 3.2 Replace the fixed `TabDockChromeProfile` temp dir with `FreshProfileDir` in the two scenarios at `Scenarios.cs:2524,2649`

## 4. LoggingService robustness

- [x] 4.1 Cap the `.err` fallback file (keep last N KB, or suppress repeated identical errors)
- [x] 4.2 Back off rotation retries after a failed rotation (retry at most every N batches)
- [x] 4.3 Make `Dispose` re-entrant across threads via `Interlocked.Exchange` on an int flag

## 5. GetMessage signature

- [x] 5.1 Delete the unused `NativeMethods.GetMessage` declaration (no call sites in the main project; the Spike declares its own `int`-returning version), or change its return type to `int` if a caller is anticipated

## 6. Validation

- [x] 6.1 `dotnet build TabDock.sln` and the Spike/ValidationDriver projects clean
- [ ] 6.2 Run the Spike with a pre-existing console window open and confirm it is untouched
- [x] 6.3 Run ValidationDriver `all` to confirm the harness still passes after path/profile changes
