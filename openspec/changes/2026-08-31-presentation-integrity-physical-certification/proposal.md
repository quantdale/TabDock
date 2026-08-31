# Presentation-integrity physical field certification and residual repair

## Why

The user-reported presentation-integrity implementation is complete on main and
has strong deterministic evidence: clean Debug/Release builds, 725/725 unit
tests, 128 dispatchable ValidationDriver scenarios, strict OpenSpec validation,
point-ownership checks, and a synthetic guest-maximize drift path.

That evidence is not equivalent to physical certification of the original
reports. The prior host could not provide an exclusive supervised desktop,
real dual-monitor topology, WS_EX_TOPMOST coverage, mixed-DPI hardware, or safe
browser F11/fullscreen interaction. The remaining work is therefore a narrow
field-certification campaign, with residual code repair only when a physical
run proves a product defect.

## What Changes

- Physically exercise the original color/rename/split/"+" workflows repeatedly
  on an exclusive interactive Windows desktop and prove both TabDock chrome
  usability and guest rendering/input.
- Physically exercise guest-originated maximize/restore and real application
  fullscreen/restore rather than relying only on synthetic
  `SW_SHOWMAXIMIZED`.
- Exercise dual-monitor transfer, topmost-band behavior, mixed-DPI/topology,
  and unrelated foreground overlap when those capabilities are genuinely
  available.
- Measure the desktop-wide `EVENT_OBJECT_LOCATIONCHANGE` route under realistic
  unrelated geometry churn to confirm the filter+coalescer remains bounded.
- Preserve first-attempt physical evidence. A later pass does not erase a valid
  first failure.
- Make only evidence-driven residual fixes; do not reopen the architecture or
  introduce new native mechanisms merely to satisfy a speculative edge case.

## Capabilities

### New Capabilities

- `presentation-integrity-physical-certification`: supervised physical
  qualification matrix, evidence requirements, rerun semantics, and residual
  repair gate for presentation integrity.

### Modified Capabilities

- `validation-qualification`: presentation-integrity release claims must
  distinguish deterministic qualification from physical field certification.

## Impact

The expected default outcome is **validation/evidence only**. Production code
changes are authorized only if a valid physical run reproduces a product
failure. Likely test/evidence surfaces are `tests/ValidationDriver/`,
`.agent/investigations/`, `docs/TESTING.md`, and `.agent/STATE.md`.
If a residual defect is proven, the agent may touch the minimum production
surface needed and must update this OpenSpec with the proven cause and fix.

No reparenting, permanent topmost policy, global unrelated-window z-order
fight, unbounded polling, or speculative rewrite is authorized.

## Evidence scope and acceptance

- Bind every attempt to the exact candidate Git SHA and executable hash.
- Prove supervised desktop lease, target/point ownership, and cleanup boundary
  before physical input.
- Exercise title centering across short/long names, narrow/default/wide
  containers, and physically available DPI scales.
- Preserve first-attempt outcomes and bounded artifacts; classify unavailable
  hardware, supervision, applications, signing, or fixture support honestly.
- A matrix cell is valid only as a physical PASS, an exact BLOCKED/SKIP
  classification with the missing capability named, or a diagnosed,
  regression-covered failure that is requalified.

Deterministic, synthetic, and read-only evidence must remain explicitly
separate from physical certification. Major original-report paths remain
uncertified when their exclusive supervised run is unavailable.
