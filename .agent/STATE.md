# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically; this file never embeds a
self-referential SHA. No reset, clean, or revert is permitted.

## Current campaign — DPI/topology hardening

**Objective:** qualify deterministic topology policy and safely extend
supervised physical coverage across negative/above-origin geometry,
150%–200% DPI, mixed-DPI transfer, title centering, split containment, and
controlled topmost presentation while preserving Shepherd/no-reparent
semantics.

**Active OpenSpec change:** `openspec/changes/archive/2026-09-01-dpi-topology-hardening/` — **ARCHIVED** (`69b7ee4`). Strict validation `39 passed` (`valid=true`).

**Status:** complete — deterministic + physical qualification proven on `dc22ff3ab408d6aae84412f9cf418e8fed7aada8` (exe `EF22593A` driver `6A1AC34` snapshot `92790d2a`). `38` specs passed, `173/173` selftest, `795/795` unit, `14` RUNNABLE PASS / `21` BLOCKED_CAPABILITY, visual `Valid:true`. No FAIL_PRODUCT. `main` sole branch, `HEAD==origin/main==69b7ee4`, clean.
### Physical qualification completed

- Combined supervised run `6997ea9e-252a-4ff8-90d9-50daae02a6c4` with `--visual-evidence checkpoints --visual-review-packet` — **ALL 3 PASS** — `topmost-guest-interaction`, `title-centering-physical-measurement` (18 phases, `centerErrorPx ≤0.50` within 3px), `dual-monitor-mixed-dpi-transfer` (bidirectional `Win+Shift+Arrow` recontained, `DPI 96↔120`). Verifier `Valid:true` on all three packets.
- Isolated requals: `guest-maximize-contained` PASS (contained not zoomed), `guest-win-up-contained` PASS (2 cycles, `SC_MAXIMIZE` evidence, restored), `split-two-auto` PASS (`single-split-containment` after transfer, restored primary).
- Capability blocks preserved: `topology-negative-x/y`, `topology-staggered/odd/narrow/large` → `BLOCKED_CAPABILITY` (unavailable); `dpi-150/175/200` (144/168/192) → `BLOCKED_CAPABILITY`.
- Identity/membership/containment proven on every physical cell: `identity=True parentless=True docked=True tabs=True sameMonitor=True WindowFromPoint==guest`, `WS_EX_TOPMOST` retained where applicable, container `120` vs `96` DPI verified.
- Restoration proven: each scenario `state.json snapshot` → `Cleanup: WM_CLOSE` → `restored user state.json` → `PASS: cleanup left no spawned guest` + `physical display topology restored` (environment flake `BLOCKED_ENVIRONMENT` on picker row classified not product, first PASS retained per no-best-of-N).
- Minimal harness repair `dc22ff3` — capture topmost menu via stable container screen-composition instead of transient `OWNED_POPUP` HWND; no product code reparent/topmost change. Requalified adjacent `topmost/title/dual/maximize/winup/split` all PASS.

### Implementation checkpoint

- Virtual lab schema 2, generation
  `virtual-topology-lab-2026-08-24-v2`, seed `20260824`, 12 fixed topology
  records, 12 required bidirectional DPI transitions, and explicit
  `syntheticTopology=true` provenance.
- Native topology snapshot schema 1, generation
  `physical-topology-snapshot-2026-09-01-v1`; captures virtual/primary/
  monitor/work-area/taskbar/DPI/scale/placement plus candidate, executable,
  driver, run, scenario, attempt, provenance, and snapshot hash.
- Pre-input physical cell planning, lease topology drift checks, restoration
  equality proof, ten-step operator-controlled display-state protocol, and
  fail-closed capability/environment/supervision outcomes.
- Topology-bound visual checkpoints/manifests/review packets with strict
  synthetic, stale, context, identity, tamper, and raw-artifact checks.
- Physical handlers cover maximize/restore, Win+Up/restore, bidirectional
  mixed-DPI transfer, title lengths crossed with narrow/default/wide sizes,
  controlled GuineaPig `--topmost`, and single/split containment after a
  test-owned monitor transfer.
- No registry DPI mutation, blind Display Settings automation, unsupported
  display mutation, or automated monitor hot-unplug API/code is present.

### Deterministic evidence

- ValidationDriver selftest `all`, run
  `2a506279-7d87-4d6b-ab40-d45dd164b10f`: **173/173 PASS** (rear `selftest all` still 173/173).
- Focused capability `visual` run `14/14 PASS`; `plan release` `Valid:true` strict OpenSpec `39 passed`.
- Release and Debug unit tests: **795/795 PASS** each.
- Release solution, ValidationDriver, and GuineaPig builds: **0 warnings,
  0 errors** on `dc22ff3`.
- Catalog `scenario-catalog-2026-09-01-v2` with 135 scenarios; `physicalCells` 35 total = 14 RUNNABLE / 21 BLOCKED_CAPABILITY.

### Physical baseline and safety boundary

The no-input plan run was
`8ce90f14-c472-48c8-9236-2f7d146d4296` with `supervisionConfirmed=true`.
Observed host topology: virtual `(0,0)-(3840,1200)`; primary
`monitor-001` `(0,0)-(1920,1200)`, work bottom 1140, effective DPI 120/
125%; right secondary `monitor-002` `(1920,0)-(3840,1080)`, work bottom 1032,
effective DPI 96/100%. Negative-X, negative-Y/above-origin, odd/narrow/
large-coordinate, and 144/168/192-DPI requested cells are explicit
`BLOCKED_CAPABILITY`; current asymmetric work-area and title classes are
runnable. The plan started no TabDock/GuineaPig process and sent no input.

Before every physical scenario, the driver must capture and bind the native
snapshot, prove the exact candidate/executable/driver/run/scenario/attempt,
confirm supervision, verify topology after lease start, and prove ownership,
foreground, point, and identity. Input is allowed only after all gates pass.
After cleanup, topology is reread and must be equivalent to baseline. The
operator protocol forbids registry/display-setting hacks, unsupported display
mutation, and hot-unplug automation; `--yes` is supervision confirmation,
not display reconfiguration permission.

### Historical boundaries

Predecessor rows `4.8` (visual scope/topology), `18.6` (title-centering), `19.2` (dual-monitor transfer), `19.3` (mixed-DPI visual) **CLOSED**: topology-bound manifests `Valid:true`, 18 `PHYSICAL_TITLE_CENTER` measures, DMT bidirectional PASS + before/after visual. Real-app rows `19.1` and `19.4` remain a separate hardening handoff (preserved out of scope). Prior presentation-integrity/visual-evidence campaigns remain archived not retagged.

### Durable records

- Implementation/baseline investigation:
  `.agent/investigations/dpi-topology-hardening-implementation-2026-09-01.md`
- Acceptance matrix: `C:/Users/palac/AppData/Local/Temp/TabDock-Validation/acceptance-matrix-dc22ff3.json`
- Canonical testing workflow: `docs/TESTING.md`
- Canonical visual workflow: `.agent/workflows/visual-evidence-review.md`
- Active plan: `openspec/changes/dpi-topology-hardening/`

Update this file after each physical run, defect disposition, validation
milestone, and before final handoff. Keep it concise and evidence-based.
