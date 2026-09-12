# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — REFERO FRONTEND OVERHAUL

**Objective:** finish the Refero-guided frontend overhaul and bring PR #13
(`ui/refero-frontend-overhaul`) to a merge-ready, production-quality state.

**Plan:** `.agent/plans/refero-frontend-overhaul-2026-09-12.md` (source of truth).

**Status:** implementation and supervised runtime visual QA complete; final
exact-SHA hosted CI rerun remains before merge.

1. **Runtime visual qualification performed on the real Release application**
   (interactive Windows 11 desktop, 1920x1080 @ 96 DPI) by driving the actual
   WPF UI with UI Automation and reviewing captures of: launcher empty and
   populated states, hover/focus/min-width/tall states; picker populated,
   selected, search, zero-result, drop-down, and disabled-commit states;
   container empty (short and long names), one/two tabs, hover/focus, split
   (engaged and right-half focus), workspace/split menus, inline capture panel,
   and narrow width.
2. **Defects found in rendering and corrected:** system-light/accent title bar
   and border on standard windows (new `Infrastructure/WindowChromeTheme` DWM
   dark chrome); ComboBox system-theme leak and dark-on-dark selected text (full
   dark template); system-light scrollbars (dark scrollbar templates);
   system-accent menu highlight (full MenuItem template plus dark Separator);
   system ToolTip fallback (dark tooltip template); airspace-invisible
   empty-workspace prompt with unclickable CTA (activation-gated popup overlay
   that also yields to the inline capture panel); missing row/tab hover states;
   and 1px content shift on keyboard focus (color-only focus indication).
3. **Deterministic gates executed on the branch work (before docs commit):**
   `dotnet build TabDock.sln` Debug/Release 0 warnings; unit tests 820/820
   Debug and Release; `scripts/validate.ps1 -Configuration Release -Ci
   -Publish` exit 0; `scripts/release-tooling-tests.ps1` 179/179.
4. **Hosted CI:** run `34698091147` (pre-final) failed before runner allocation
   (zero steps, no runner) — infrastructure, not a source failure. A rerun is
   required on the final commit.

### Not reproducible in this environment (honest non-pass)

- 125%/150% DPI visual cells: single 96-DPI monitor (144/168/192 DPI remain
  `BLOCKED_CAPABILITY`, consistent with prior qualification records).
- Pending-recovery banner and capture-admission-blocked runtime states require
  manipulated recovery/health journal state; their structure is covered by
  contract tests and prior supervised evidence.

### Out of scope by architecture

- `WindowShepherdService`, split-presentation policy, HWND lifecycle,
  persistence, and capture identity are untouched; branch diff is limited to
  frontend XAML, two window code-behinds, one new chrome helper, native
  constants, contract tests, and documentation.

Update this file after each validation milestone, defect disposition, and
before final handoff.
