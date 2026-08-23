# Investigation: intermittent full-suite lease test

**Date:** 2026-08-23
**Status:** concluded

## Question

Did the earlier cold-start/scheduler-sensitive failure recur during the Product
Trust & Interaction Campaign validation, and is it caused by this campaign?

## Findings

- A complete Debug run passed 648/648, as did the first additional stability
  run.
- A later complete Debug run reproduced one failure in the existing
  `ProductMutationLeaseTests.Lease_IsExclusiveThenReusableAndRecoversAbandonedOwnership`
  fact; the other 647 facts passed.
- A diagnostic probe showed that the named mutex opened with the expected ACL,
  but `WaitOne(0)` still saw it owned after the abandoned-owner thread had
  joined; this was a test setup ordering issue, not an ACL or
  ProductMutationLease implementation failure.
- `GC.KeepAlive(mutex)` alone stabilized the Debug repetitions but did not
  stabilize the Release suite. The final test-only repair adds an explicit
  `ownerMayExit` barrier: the owner remains alive after signaling ownership,
  the test releases that barrier, joins the owner, and only then attempts
  recovery. The handle is also kept alive until delegate return.
- Ten further minimal-output full Release runs each passed 648/648 after the
  barrier repair.
- The lease fact contains the only explicit short setup wait
  (`ownerReady.Wait(TimeSpan.FromSeconds(2))`); the repair does not alter that
  wait or the lease implementation.

## Approaches tried

- Re-ran the failing fact alone: passed.
- Re-ran the complete suite with normal diagnostics: passed 648/648.
- Repeated the complete Debug suite ten more times after the handle-lifetime
  repair: all ten passed 648/648.
- Repeated the complete Release suite ten more times after the explicit
  owner-exit barrier: all ten passed 648/648.

## Conclusion

The failure recurred under full-suite load and was root-caused to the test
not explicitly controlling the lifetime of its intentionally abandoned owner
between the ownership signal and thread exit. `ownerMayExit` plus
`GC.KeepAlive(mutex)` makes the test exercise the intended Windows
abandoned-owner handoff in both configurations. This is a test-only ordering
repair: no arbitrary timeout and no production lease behavior were changed.
Ten subsequent Debug and ten subsequent Release suites passed 648/648.

## References

- `tests/UnitTests/ProductMutationLeaseTests.cs:79-127`
- `dotnet test TabDock.sln -c Debug --no-build`
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug --no-build --filter FullyQualifiedName~ProductMutationLeaseTests.Lease_IsExclusiveThenReusableAndRecoversAbandonedOwnership`
