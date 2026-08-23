# TabDock Ship-Readiness Review — 2026-08-23

**Branch:** `codex/ship-readiness-overhaul-20260823`  
**Baseline:** `main` at `ba3115a138eed81e4a56c023aa3381f2c14a20cd`  
**Goal:** make the product materially closer to release quality without destabilizing the hardened native-window ownership and recovery core.

## Executive conclusion

The repository is in a substantially better engineering state than the legacy-looking UI implies. Current `main` already contains extensive hardening for HWND identity, crash recovery, lifecycle monitoring, DPI/layout behavior, split presentation, state durability, diagnostics, packaging, and release controls. The historical issue ledger and the 2026-08-22 maintainability audit describe many defects or design debts that have since been addressed by later hardening/refactor commits.

The largest current product-quality gap is the presentation and capture workflow, not an absence of native safety machinery. The previous UI exposed a mature engine through stock WPF surfaces, weak hierarchy, hidden gestures, poor empty states, and a capture picker that could not efficiently handle a busy desktop. One concrete functional UX defect remained: pressing Refresh in the capture picker discarded every checked window even when the same windows were still valid candidates.

This branch therefore keeps the native transaction/recovery subsystems stable and concentrates changes at the view and capture-view-model seams.

## Repository-wide review coverage

The review inventoried and cross-checked the repository's major surfaces rather than treating the three visible XAML files as the product:

- application composition and lifecycle in `App.xaml` / `App.xaml.cs`;
- product/project metadata and self-contained `win-x64` release configuration;
- model state, persisted layout intent, captured-window identity, and split presentation policy;
- launcher, group, tab, split-composite, and capture-picker view models;
- launcher, capture picker, custom container chrome, tab strip, split surface, inline capture panel, and native content host;
- `GroupManager` ownership/indexing, capture admission, durable semantic saves, release and emergency-release paths;
- window shepherding, guest lifecycle, hide provenance, WinEvent monitoring, foreground/z-order policy, DPI and pane containment, split presentation controller, and coalesced layout scheduling;
- persistence, pending recovery, diagnostic/reporting, environment fingerprinting, single-instance/product-mutation lease, hotkey policy, and release support surfaces;
- `.github/workflows`, release/qualification scripts, and native ABI qualification entry points;
- the xUnit regression corpus and source-contract tests that protect presentation/native interaction seams;
- `KNOWN_ISSUES.md`, architecture/testing documentation, and prior 2026-08-21 / 2026-08-22 audit material.

A repository marker sweep also found no TODO/FIXME/HACK/placeholder/`NotImplementedException` residue that would indicate intentionally unfinished product paths.

## Findings and actions

### Fixed in this branch

1. **Capture refresh destroyed valid user selection.**
   - Before: `CapturePickerViewModel.Refresh()` cleared `Windows`, which necessarily cleared all checkbox state.
   - After: selection is restored only when the immediate native identity still matches by HWND + PID + executable path. HWND reuse with a different PID is deliberately not trusted.

2. **Capture selection did not scale to a busy desktop.**
   - Added live filtering by title, executable path, and window class.
   - Added selected-window count, `Select all visible`, `Clear`, and explicit no-match/no-candidate states.
   - Kept a master candidate collection so filtering never silently drops previously selected targets from the eventual capture request.

3. **Standalone and inline capture had divergent, weak ergonomics.**
   - Both surfaces now use the same view-model search/filter/selection behavior and terminology.
   - Existing admission gating and capture result semantics remain authoritative.

4. **Launcher looked like scaffolding rather than a product surface.**
   - Rebuilt hierarchy around workspaces and primary actions.
   - Added explicit Open affordances while retaining double-click and Enter activation.
   - Made workspace counts, member counts, empty guidance, recovery attention, capture-admission state, and global-navigation availability legible.
   - Kept existing automation IDs used by validation tooling.

5. **Container capabilities were difficult to discover.**
   - Reworked custom chrome, workspace selector, split affordance, tab strip, split-composite visual state, empty workspace guidance, and inline capture surface.
   - Workspace identity remains visible through accent treatment without painting the entire product surface in a saturated group color.
   - Existing `DisplayTabs`, authoritative `ActiveTab`, split-half event handlers, native `ContentHost`, and automation contracts were retained.

6. **No coherent visual system existed.**
   - Added a centralized palette and shared button/text/list/input styles in `App.xaml`.
   - This prevents launcher/picker/container drift and makes interaction states consistent.

7. **Initial primary-action contrast was insufficient.**
   - During self-review, white normal-size button text on `#5B8CFF` measured only about 3.16:1.
   - Split decorative accent from the primary action fill. Primary actions now use `#3F6FD9`, which provides about 4.68:1 against white; accent text on dark/soft surfaces uses a lighter dedicated token.

8. **Capture UX changes lacked regression coverage.**
   - Added headless tests for title/executable/class filtering, refresh-stable selection identity, and select-all-visible behavior.

### Reviewed and deliberately not rewritten

The following areas are high-risk and already carry dense, targeted regression coverage. No reproduced defect in this review justified changing them merely for churn:

- `WindowShepherdService` native capture/release transactions and recovery-journal ordering;
- `GuestLifecycleService`, hide provenance, WinEvent hook lifecycle, and capture-admission failure policy;
- `ProductMutationLease` and persisted/recovery-state ownership;
- native deferred positioning, pane containment, monitor/DPI seam handling, and maximize bounds;
- split state/policy/controller authority and generation guards;
- crash/session-ending emergency release and state normalization;
- diagnostic privacy/redaction and release-control policy.

This is intentional risk management, not an assertion that Win32 integration can be proven correct by static review.

## Regression contracts preserved

The container restyle intentionally preserves the source/runtime contracts already guarded by tests, including:

- `ItemsSource="{Binding DisplayTabs}"`;
- `SelectedItem="{Binding ActiveTab, Mode=OneWay}"`;
- `IsSelected="{Binding IsActive, Mode=TwoWay}"` on ordinary tab containers;
- the native `ContentHost` marker as the content-area authority;
- split half identity and interaction handlers;
- capture, split, recovery, and tab automation IDs consumed by validation tooling.

## Validation plan / release boundary

The branch is opened as draft PR #12 so the repository's Windows pull-request workflow runs against the real branch. That workflow is expected to execute the Release qualification entry point, xUnit suite, package/publish smoke, release-tooling regression suite, and native ABI evidence on hosted Windows runners.

A green hosted pipeline is **necessary but not sufficient** to label TabDock shippable. Before release, run the repository's supervised physical-Windows acceptance gates on the final candidate, including real-app capture/release, focus/input switching, split behavior, drag/reorder/pop-out, multi-monitor/DPI placement, crash-kill rescue, and the repository's release qualification/signing policy as applicable.

Until those external/native scenarios pass on the final SHA, the correct release statement is **"candidate hardened and CI-qualified"**, not **"fully certified for shipment."**
