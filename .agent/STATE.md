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

**Active OpenSpec change:** `openspec/changes/dpi-topology-hardening/`.
Strict pre-edit validation passed (`valid=true`, 1/1 change). The change is
not archived while physical dispositions and final gates remain open.

**Status:** implementation complete; physical qualification is the active
phase. The application candidate boundary is the exact committed source used
for the physical run; later documentation/evidence commits must not retag
that binary.

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
  `2a506279-7d87-4d6b-ab40-d45dd164b10f`: **173/173 PASS**.
- Focused capability selftest run
  `6333e5fb-7bdc-4bab-b8fd-c4355665ba72`: **41/41 PASS**.
- Focused visual selftest run
  `7f73a4f3-4f47-4081-a10c-fa8ad7aaa580`: **14/14 PASS**.
- Release and Debug unit tests: **795/795 PASS** each.
- Release solution, ValidationDriver, and GuineaPig builds: **0 warnings,
  0 errors**.
- Native ABI/version smokes passed for the Release executable. Catalog is
  `scenario-catalog-2026-09-01-v2` with 135 dispatchable scenarios.

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

### Physical qualification next action

Build the exact Release candidate from the committed implementation source,
then run only safely available bounded scenarios with
`--visual-evidence checkpoints --visual-review-packet`. Preserve every first
physical outcome, including `BLOCKED_CAPABILITY`, `BLOCKED_ENVIRONMENT`,
`BLOCKED_SUPERVISED`, `FAIL_HARNESS`, and `FAIL_PRODUCT`; never best-of-N a
valid failure. Do not claim synthetic or unavailable cells as physical PASS.

### Historical boundaries

The prior presentation-integrity and visual-evidence campaigns are archived;
their candidate/run identities and valid first failures remain historical and
are not retagged. Predecessor rows `4.8`, `18.6`, `19.2`, and `19.3` are
explicit obligations of this change. Real-app rows `19.1` and `19.4` remain a
separate hardening handoff and are deliberately out of scope.

### Durable records

- Implementation/baseline investigation:
  `.agent/investigations/dpi-topology-hardening-implementation-2026-09-01.md`
- Canonical testing workflow: `docs/TESTING.md`
- Canonical visual workflow: `.agent/workflows/visual-evidence-review.md`
- Active plan: `openspec/changes/dpi-topology-hardening/`

Update this file after each physical run, defect disposition, validation
milestone, and before final handoff. Keep it concise and evidence-based.
