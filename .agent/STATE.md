# TabDock agent state

## Objective and status — 2026-11-13

Local frontend conformance/recovery fixes complete; supervised native/release qualification remains pending. See [the successor plan](plans/ux-successor-2026-11-13.md) for decisions, campaigns, validation and limits, and [the prior overhaul](plans/ui-overhaul.md) for the visual reference lock. Resolve current branch/HEAD/origin/status dynamically with Git; historical starting implementation was `c0b28c7` on clean `main`.

## Completed

- Fixed launcher Enter opening the selected workspace instead of a focused Open button's workspace.
- Both capture surfaces now support row Space selection without confusing checked state with list navigation or double-toggling checkboxes; native commands/admission remain authoritative.
- Compact inline capture shows a complete candidate plus actions at 420x300, including tab overflow. Minimum launcher recovery states and standalone no-results guidance no longer clip; removed duplicate empty-state actions and decorative height.
- Distinct enabled-control boundary token, shared inline icon fallback, and context-specific no-candidate/no-match headings preserve the existing graphite/lavender system.
- Added 25 managed regressions and refreshed frontend/testing documentation. Preserved Shepherd/no-reparent, native content markers/geometry, strong capture identity, persistence, keyboard contracts and AutomationIds.

## Validation

- Debug solution build: 0 warnings/errors; 1,029 unit tests passed.
- Release CI/publish gate on implementation working tree: PASS; 1,029 tests, audited dependencies (no vulnerabilities reported by configured sources), auxiliary builds, synthetic resources, native ABI, diagnostics/recovery/privacy, 39 specs, self-contained publish/version. Exact-commit rerun and final Git SHA are reported in session output, not embedded as this file's own identity.
- Driver deterministic self-tests: 205 passed; release-tooling suite: 179 passed.
- Managed dark-theme client renders reviewed at minimum/default launcher/picker/container sizes with empty, blocked, long-content and overflowing-tab states. Both motion modes covered for capture keyboard/selection feedback and existing template suites.
- Independent adversarial source review accepted fixes with native-qualification caveats. C# LSP probes were inconclusive; compiler/tests provide build evidence.
- Evidence outside Git: workspace artifact root `_AgentArtifacts/TabDock/sessions/20261113-pi-ux-successor/`. The plan identifies red/green logs, renders and review reports.

## Remaining qualification / next action

An exclusively available supervised Windows desktop is required for actual capture/split/pop-out, physical keyboard/activation and screen-reader checks. Windows 10 and physical mixed-DPI cells plus production signing require external hardware/materials/approval. No unattended input, display mutation, production profile mutation, deployment or performance claim was made. Managed render/key fixtures are not native workflow certification. No further evidence-backed local source change was identified in the bounded reassessment; avoid speculative redesigns.
