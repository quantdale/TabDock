## Context

The ValidationDriver harness (`tests/ValidationDriver/TabDock.ValidationDriver`) already drives real windows via `SendInput` at UIA-read coordinates and has mature infrastructure: `CaptureIntoGroup` (variadic picker flow), dock-state primitives (`IsDocked`/`IsReleasedAndShown`/`GuestMatchesHost`), `GuardedProc` spawn discipline, `TabDockLog`/`PigLog` assertion sources, and `Pixels` (`BitBlt` + `PrintWindow` PW_RENDERFULLCONTENT). The gaps are in *assertions and scenario selection*, not plumbing.

Key facts established during exploration:

- **H2** (`dragreorder`, `Scenarios.cs:2068`): asserts `Reordered tab >= 1` only. The H2 bug was hundreds of A↔B flips per drag — bug-present code passes today. Frozen-midpoint fix means a correct drag produces a small, bounded number of reorders.
- **H4**: browser coverage lapsed. `browser-tabswitch-hidesafety` (`Scenarios.cs:2506`) does 24 switches with zero pixel checks on the browser; `realapp-multi-render`'s Chrome `PrintWindow` check is best-effort/never-fails and involves no switching.
- **H5**: the drift watchdog no longer exists — Shepherd's pop-out model superseded it and is covered (`chrometabdrag`, `dragout-by-titlebar`). But `browser-lifecycle` (`Scenarios.cs:2467`) still positively asserts `LAYOUT[capture]` and checks `FindDriftWithoutPrecedingMovesize` against `LAYOUT[drift]`/`LAYOUT[movesize]` lines that committed source never emits.
- **H6**: no scenario minimizes the *container* with tabs and asserts retention. `minrestore` covers the opposite path (guest self-minimizes). All needed primitives exist (`ClickMinimizeButton`, `TabCount`, `IsDocked`).
- **Vacuous checks**: `browser-soak`/`browser-multi` assert zero `unhealthy` log lines; `RenderHealthService` was deleted in the Shepherd migration, so nothing can ever emit that line.
- The altswitch keyboard-input family is known-flaky under a foreground-holding interactive session (`KNOWN_ISSUES.md`); new scenarios should avoid depending on `ForceForeground`-heavy flows where possible.

## Goals / Non-Goals

**Goals:**

- Every fixed H-series bug has at least one automated scenario whose assertions would actually fail if the fix regressed.
- No assertion references instrumentation absent from committed source (enforce the `docs/TESTING.md` §D rule).
- New scenarios reuse existing primitives; no new infrastructure, no new GuineaPig switches, no production code changes.
- The four input-injection safety rules become reviewable spec requirements, and each new/modified scenario states how it satisfies them.

**Non-Goals:**

- Fixing the `ForceForeground` environmental flake (documented harness limitation).
- Promoting browser/real-app scenarios into `all` — `all` stays pig-only and hermetic; browser scenarios stay opt-in via `--guest`.
- Identifying or covering "H7/H9" (no such entries exist in the repo).
- New ValidationDriver infrastructure or production instrumentation.

## Decisions

### D1: H2 oscillation bound = count + adjacency, not timing

Extend `dragreorder` to count `Reordered tab` lines emitted during the drag and assert an **upper bound** (exact constant chosen at implementation from a passing run's observed count plus headroom — order-of-magnitude single digits, not a timing-derived value) and assert **zero immediate flip-back pairs** (a `Reordered tab` from index X→Y followed directly by Y→X for the same tab). Rationale: the bug's signature is oscillation, and flip-pairs detect it structurally, immune to machine speed; the count bound catches non-oscillating churn. Alternatives considered: pixel-diffing the tab strip (fragile, DPI-dependent) and wall-clock jitter measurement (flaky under load) — both rejected.

### D2: Chromium render check uses `PrintWindow`, asserted post-switch

In `browser-tabswitch-hidesafety`, after each switch *to* the browser tab, capture the guest via `CaptureWindowViaPrintWindow` (PW_RENDERFULLCONTENT — reads the guest's own back-buffer, unaffected by on-screen occlusion per `AGENTS.md`) and assert brightness/variance thresholds indicating live content, reusing the thresholds already proven in `maximize-repro`/`realapp-multi-render`. Screen `BitBlt` was rejected: known to read GPU windows black in some environments (`Pixels.cs`). A per-switch hard assert beats `browser-soak`'s every-5-cycles health check because the H4-class failure is *per-transition*.

### D3: Stale assertions are re-targeted at observable state, not new log lines

`browser-lifecycle`'s `LAYOUT[capture]` positive check is replaced by what it was proxying — capture success — which Shepherd makes directly observable: `IsDocked` (guest rect == content-area marker rect) plus presence of a fresh `SHEPHERD[position]`/`SHEPHERD[capture]`-family line for that guest. Drift checks that can only pass vacuously are deleted, not re-aimed (Shepherd has no drift watchdog; `chrometabdrag`/`dragout-by-titlebar` already cover the behavioral successor). Zero-`unhealthy` checks are deleted. **No new production instrumentation is added** — the perf invariants (`docs/internal/perf-2026-07-25.md`) make hot log lines a deliberate cost, and tests must follow the app, not vice versa.

### D4: H6 scenario joins `all`; second-tier flows join `all` too where pig-only

`container-minimize-retains-tabs` and the second-tier flows (hotkey stacking, inactive-tab pop-out, double-capture guard, persisted active-tab index) are all pig-based and hermetic, so they go into `AllOrder` — regressions should be caught by a routine `all` run, unlike the standalone H8 family. The one exception considered: hotkey-stacking holds `Ctrl+Alt+G` ~2 s, which is still deterministic and pig-independent; it stays in `all`.

### D5: Safety rules enforced by construction + review, not runtime guards

Each new/modified scenario satisfies the four rules by *using only the existing primitives* (`SpawnPig`/`SpawnNotepad` unique-title spawns, `Discover` PID/class/title verification, `EnsureClickable` never-click-blind, scenario-level try/finally `Cleanup`, no retry loops without re-discovery). The spec states the rules as requirements; tasks include a per-scenario checklist item. A runtime "safety wrapper" around all input was considered and rejected — it duplicates `GuardedProc`/`EnsureClickable` and adds a layer future scenarios would route around.

## Risks / Trade-offs

- [H2 upper bound too tight → false failures on slow/loaded machines] → derive the constant from observed passing-run counts with generous headroom; flip-pair check (not the count) is the primary regression signal.
- [Chromium `PrintWindow` thresholds vary by page content/theme] → drive the browser to a local deterministic test page (existing `chromeinput` pattern) rather than a live URL; assert relative liveness (brightness above floor + inter-frame variance), not exact pixels.
- [Retargeting `browser-lifecycle` weakens it if `SHEPHERD[*]` line names drift] → mitigated by the spec rule itself: implementation verifies each asserted log substring exists in committed source before relying on it, and `docs/TESTING.md` is updated to match.
- [Adding scenarios to `all` lengthens the routine run] → the additions are small pig flows (seconds each); the H6 scenario is the only one with minimize/restore settle waits.
- [New scenarios inherit the known `ForceForeground` flake in foreground-holding sessions] → accept and document; none of the new `all` scenarios needs programmatic foreground beyond what `EnsureClickable` already handles.
