## Implementation

- [x] Intercept an ordinary non-member left-click above the split ListBox guard and route it through journal-safe `ExitSplit(target)`.
- [x] If split exit becomes recovery-pending, keep the pair authoritative and re-present both panes rather than selecting the third tab.
- [x] Add one bounded post-context-menu split presentation settle using the existing layout and foreground APIs only.
- [x] Keep hover, right-click, split-member focus, capture-during-split, and Ctrl+Tab pair cycling semantics unchanged.

## Regression coverage

- [ ] Update `split-click-third` to require split exit, selected third-tab full-width presentation, hidden former pair, preserved capture membership, and restored ordinary tab strip.
- [ ] Update the historical `split-third-tab-click-persists` scenario to exercise repeated enter-split -> click-third -> normal-tab recovery cycles (name may remain as a compatibility alias if renaming would create unnecessary registration churn).
- [ ] Update `split-composite` so its non-member click section expects a split exit rather than persistent pair behavior.
- [ ] Require split entry to settle the initiating/focused member as real foreground after the context menu closes.
- [ ] Keep `split-third-tab-hover-persists` unchanged as the regression proving hover does not exit.

## Qualification

- [ ] Release solution, ValidationDriver, GuineaPig, and Performance builds pass with zero warnings/errors.
- [ ] Diagnostics self-tests pass with zero failures.
- [ ] OpenSpec validates all specs/changes.
- [ ] Self-contained publish/version, recovery, and privacy smokes pass.
- [ ] Run the focused real-input split scenarios on an interactive Windows desktop when available; do not bypass foreground safety guards.
- [ ] Verify the exact final `main` SHA in push-triggered GitHub Actions before re-freezing source.
