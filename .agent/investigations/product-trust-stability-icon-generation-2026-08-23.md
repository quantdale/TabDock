# Investigation: Release icon-generation test timing

**Date:** 2026-08-23
**Status:** concluded

## Question

Why did the final Release suite fail
`CapturePickerViewModelTests.BackgroundIconResolution_IsGenerationSafe`, and
does the fix change product behavior?

## Findings

- The full Release run failed only this existing fact: 647/648 passed.
- The fact passed when isolated in Release.
- Its second-generation assertion waited for a dispatcher-marshalled worker
  result with a fixed two-second limit, while the first generation was
  deliberately blocked and the full suite was running in parallel.
- The assertion tests generation ordering, not that a thread-pool worker starts
  within two seconds. The worker can be delayed before the current-generation
  result is posted even though the implementation remains correct.

## Approaches tried

- Re-ran the isolated Release fact: passed.
- Added explicit test-only extraction-completion gates for the current and
  failure generations; the dispatcher pump retains a bounded 15-second safety
  limit and still requires the newest generation's rows to own the icons.
- Re-ran the full Release suite after the test-only repair: 648/648 passed.

## Conclusion

The failure was a test latency assumption under full-suite scheduler load, not
a Product Trust production regression. The repair makes the ordering gate
explicit and does not alter application code or weaken the generation check.

## References

- `tests/UnitTests/CapturePickerViewModelTests.cs`
- `ViewModels/CapturePickerViewModel.cs`
- `dotnet test TabDock.sln -c Release --no-build`
