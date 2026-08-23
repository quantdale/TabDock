## Context

PR #12 keeps external guests as independent top-level windows and already has
strong native identity/admission gates. The new work must strengthen the picker
and WPF presentation seams without moving identity authority into a second
service or changing `ContentHost`, `DisplayTabs`, split ownership, or native
placement. See `proposal.md` and the existing capability specs for the external
contracts.

## Goals / Non-Goals

**Goals:**

- Make refresh continuity explicit and fail closed for process-instance/class
  changes while preserving title mutability and Windows path case semantics.
- Reduce picker UI churn for bulk selection and large icon batches.
- Make critical redesigned controls usable and understandable by keyboard and
  UI Automation, preserving existing IDs consumed by ValidationDriver.
- Add source-contract and deterministic coverage for XAML authority, focus, and
  generation behavior, plus honest supervised scenario inventory updates.

**Non-Goals:**

- No replacement of `WindowIdentityGate`, `WindowShepherdService`,
  `GuestLifecycleService`, persistence, mutation lease, or split controller.
- No attempt to prove a same-process HWND replacement between the final check
  and a native Win32 call; the existing final admission race remains documented.
- No arbitrary wall-clock performance gate and no unsupervised `SendInput` run.
- No new UI framework, dependency, onboarding wizard, or production release
  signing/publication bypass.

## Decisions

1. **Extend the picker target rather than create a second native identity gate.**
   `WindowCaptureTarget` and `WindowInfo` carry process-start ticks and class.
   Picker continuity uses a small explicit value comparer: HWND/PID/start/class
   plus `StringComparer.OrdinalIgnoreCase` for the executable path. Shepherd's
   existing strong admission remains authoritative at submit time. A canonical
   path normalizer is not introduced because both picker and final probes use
   `QueryFullProcessImageName`; case-insensitive comparison handles the proven
   Windows variance without changing display strings.

2. **Fail closed on unavailable process-instance identity for production rows.**
   The native enumerator omits candidates whose executable or process-start
   identity cannot be read. Test candidate injection may use zero ticks to keep
   headless projection tests independent of Win32; any submitted target with
   missing required evidence is still rejected by the final handoff check.

3. **Batch only view-model notification work.**
   Selection updates run under a small nesting-safe notification batch and emit
   one aggregate state update after the loop. Icon extraction remains one
   bounded worker and applies frozen results in small dispatcher batches, with a
   generation/row check for every result. This preserves row-level binding while
   avoiding a 1,000-post refresh storm.

4. **Use control-native keyboard semantics at the XAML boundary.**
   The tab list becomes focusable with explicit item names and focus styling;
   existing mouse-preview handlers are supplemented by Button Click/KeyDown
   paths. Split halves retain their AutomationIds and identity but become
   keyboard-focusable activation surfaces. Shared styles supply visible focus
   outlines, dark ComboBox/list treatment, disabled states, and accessible help
   text without a broad custom-control framework.

5. **Protect semantics with structural tests, not whitespace snapshots.**
   Source-contract tests parse text for preserved `ContentHost`,
   `DisplayTabs`/`ActiveTab`, AutomationIds, focusability, keyboard handlers,
   and absence of duplicate critical IDs. Behavioral tests cover identity,
   batching, filtering, generation ownership, and live projection changes.

## Risks / Trade-offs

- [Risk] Process-start probing adds work to native picker enumeration and may
  hide inaccessible windows → [Mitigation] it is a bounded admission-quality
  probe, skips only candidates Shepherd could not capture safely, and remains
  separate from per-frame layout checks.
- [Risk] Coalescing icon posts could delay an individual icon → [Mitigation]
  rows still appear before extraction, batches are small, and completion remains
  exposed for deterministic tests.
- [Risk] Custom focus/template changes can disturb UIA or ValidationDriver →
  [Mitigation] retain all existing IDs, add source-contract tests, and use
  supervised scenarios only when the desktop is safe.
- [Risk] Compact work areas may still be physically unable to display every
  control → [Mitigation] allow scrolling/wrapping/trimming and record any
  hardware-specific blocker instead of inflating native minimums blindly.
