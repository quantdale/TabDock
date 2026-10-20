## 1. Reproduce checked-state versus list-state divergence

- [ ] 1.1 Confirm A07 and its A03/A06 prerequisites are present; instantiate the actual standalone and inline picker templates with current WindowInfo fixtures.
- [ ] 1.2 Add failing tests checking a candidate whose ListBoxItem is not selected/focused, multiple checked rows, Select visible and Clear without changing the authoritative target set.

## 2. Bind the correct selection source

- [ ] 2.1 Add an explicit selected-state input to the existing feedback owner, distinguishing unspecified from false so ordinary workspace tabs retain ancestor selection.
- [ ] 2.2 Bind that input to candidate capture selection in both picker templates; keep keyboard focus, list selection, commands and identity admission independent.
- [ ] 2.3 Recompute on current DataContext/input changes and release old row references under existing lifecycle ownership.

## 3. Verify continuity and current-row safety

- [ ] 3.1 Add search hide/show, same-identity refresh, changed-identity refusal and checked→unchecked recycled-row visual tests.
- [ ] 3.2 Run `dotnet build TabDock.sln` and targeted picker selection/identity/visual tests in normal and reduced motion; rerun ordinary tab-selection and A03 lifetime coverage.
- [ ] 3.3 Verify existing AutomationIds, accessible help and submitted target identity remain unchanged; defer complete unit/Release validation until A02/A10 are safe.
