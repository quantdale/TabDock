## 1. Read-only recovery attention

- [x] 1.1 Add a read-only pending-recovery attention projection that reuses the existing catalog/unresolved-evidence calculation, counts unresolved files, retains unreadable attention, and does not run cleanup or recovery mutation.
- [x] 1.2 Add deterministic pending-attention tests for zero, one, multiple, fully resolved, corrupt/unreadable, and unchanged-evidence cases.
- [x] 1.3 Project the attention state into a non-modal launcher warning with exact inspect/supervised command text, stable AutomationId/name/help text, and complete hidden state when clear.

## 2. Canonical capture-admission presentation

- [x] 2.1 Replace the log-only capture-admission boolean boundary with an observable allowed/reason state and change event while retaining `GroupManager` as the only writer.
- [x] 2.2 Add transition tests for allowed/blocked, journal-unavailable, retry-pending, retry-success, bounded retry failure, and reason updates.
- [x] 2.3 Project admission state into launcher and container/capture-picker view models with command requery and no leakage on detach.
- [x] 2.4 Update launcher and container XAML for disabled/blocked capture controls, human-readable reason/help text, shortcut-only distinction, and stable AutomationIds.

## 3. Scoped focus-independent navigation

- [x] 3.1 Add PageUp/PageDown `MOD_NOREPEAT` registration/event support and availability diagnostics without breaking capture, diagnostic, or local shortcuts.
- [x] 3.2 Add pure foreground-scope resolution/registration policy tests for captured guest, owning container/chrome, unrelated foreground, stale/recycled HWND, multiple groups, and registration failure.
- [x] 3.3 Extract the shared container-level navigation operation and route local Ctrl+Tab plus global previous/next through `TabNavigationPolicy` and existing split focus/resume paths.
- [x] 3.4 Wire App foreground resolution to live groups/containers with fail-closed chrome/modal/close guards and expose global-navigation availability/help text.
- [x] 3.5 Add navigation regression/source/accessibility tests for wraparound, one-tab no-op, presented/dormant split, repeated suppression contract, and closed-container queued-message safety.

## 4. Always-visible split affordance

- [x] 4.1 Add a native-free split-affordance projection/validation policy for disabled, create, presented, dormant, eligible-partner, and stale-action states.
- [x] 4.2 Add deterministic projection/controller tests for button state, partner contents, create/resume/focus/end actions, stale targets, member removal, unrelated selection, and authority uniqueness.
- [x] 4.3 Add the persistent accessible `Split ▾` container control and tracked dynamic action menu, routing actions through existing create/focus/resume/exit/member-removal methods only.
- [x] 4.4 Add source/UI Automation coverage for stable control/action IDs, keyboard access, disabled explanation, and split control interaction with rename/capture/context chrome.

## 5. Integration, documentation, and validation

- [x] 5.1 Add cross-feature regression coverage for recovery banner/group states, blocked capture/hotkey/panel, global navigation/split/modal/closing/multiple-container interactions, and launcher/container lifetime.
- [x] 5.2 Update architecture, testing, README/help, shortcut decision records, campaign plan, and `.agent/STATE.md` with verified behavior and blockers.
- [x] 5.3 Run Debug/Release builds and tests, release-tooling tests, `validate.ps1 -Ci -Publish`, OpenSpec validation, and `git diff --check`; repeat the relevant full suite to assess cold-start stability.
- [x] 5.4 Run safe supervised ValidationDriver scenarios for global navigation, split affordance, and blocked-admission UX or record exact `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT` commands.
- [x] 5.5 Archive the completed OpenSpec change, commit intended campaign work, push `main`, and verify branch/remote synchronization and a clean worktree.
