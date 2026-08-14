## Implementation

- [x] Intercept an ordinary non-member left-click above the split ListBox guard and route it through journal-safe `ExitSplit(target)`.
- [x] If split exit becomes recovery-pending, keep the pair authoritative and re-present both panes rather than selecting the third tab.
- [x] Add one bounded post-context-menu split presentation settle using the existing layout and foreground APIs only.
- [x] Keep hover, right-click, split-member focus, capture-during-split, and Ctrl+Tab pair cycling semantics unchanged.

## Regression coverage

- [x] Update `split-click-third` to require split exit, selected third-tab full-width presentation, hidden former pair, preserved capture membership, and restored ordinary tab strip.
- [x] Keep the historical `split-third-tab-click-persists` CLI name as a compatibility alias but make it repeatedly exercise split -> click-third -> normal-tab recovery.
- [x] Update `split-composite` so its non-member click section expects a split exit rather than persistent pair behavior.
- [x] Require both direct and submenu split entry to emit the bounded post-popup settle and place the initiating/focused member in real foreground.
- [x] Keep `split-third-tab-hover-persists` unchanged as the regression proving hover does not exit.
- [x] Reconcile the canonical `openspec/specs/ui-ux-hardening/spec.md` contract with this change.

## Qualification contract

Canonical deterministic/hosted qualification for this change is the normal repository gate:

- Release solution, ValidationDriver, GuineaPig, and Performance compile qualification;
- diagnostics/self-tests with zero failures;
- OpenSpec validation;
- recovery and support-bundle privacy smokes;
- self-contained publish and exact build-identity smoke.

Execution results, Git refs, and the exact push-triggered hosted-CI run are dynamic session evidence under `AGENTS.md` and are intentionally not persisted as post-CI checkboxes that would require another state-only commit.

## External real-input qualification

- [ ] On an interactive Windows desktop, reproduce three captured apps, split two, click the third, and verify the third becomes full-width and ordinary switching resumes without releasing any app.
- [ ] With an available Chromium-family guest, create a split and verify the initial client presentation adapts to the pane without a corrective first click inside the browser.
- [ ] Keep existing foreground/direct-click split scenarios green; never bypass the ValidationDriver foreground safety preflight to force a result.
