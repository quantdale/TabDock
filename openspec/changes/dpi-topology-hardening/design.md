# Design — boundary topology and high-DPI qualification

## Context

The Shepherd model keeps captured guests as independent top-level HWNDs. That
makes coordinate space, monitor identity, DPI, native minimum size, local
z-order, and transition timing part of the correctness boundary.

Existing deterministic topology models boundary coordinates, while physical
evidence proved only a 120-DPI primary and 96-DPI right-hand secondary. This
campaign connects deterministic breadth to supervised physical evidence without
allowing synthetic results to masquerade as hardware qualification.

## Goals / Non-Goals

Goals: negative/above-origin geometry, 144/168/192 DPI, mixed-DPI transitions,
title midpoint measurement, containment, controlled topmost, restricted visual
evidence, reversible operator-controlled display state, and truthful physical
blocking.

Non-goals: automated registry/undocumented DPI changes, blind Display Settings
input, hot-unplug automation, whole-desktop recording, real-app-specific
behavior, or production repair without retained evidence.

## 1. Evidence layers

```text
deterministic topology laboratory (syntheticTopology=true)
        -> capability/supervised preflight
        -> physical exact-candidate cells with real guarded input
```

A synthetic PASS never satisfies a physical gate.

## 2. Canonical topology snapshot

Each physical attempt records virtual-screen rectangle; monitor/work rectangles;
primary flag; effective DPI and scale class; relative placement; work-area
deltas; candidate/driver/run/attempt identity; and whether topology was
operator-prepared or merely observed. The same facts are captured after the
scenario. Silent mid-attempt topology drift makes the result ineligible.

## 3. Boundary matrix

Deterministic coverage includes 96/120/144/168/192 DPI, right/left-negative,
above-origin, vertically staggered, asymmetric-work-area, odd-dimension and
large-coordinate layouts, plus 96↔144/168/192 and 120↔144/168/192 transitions
in both directions. Monitor reorder/removal remains synthetic policy coverage.

Physical runs exercise the subset safely available on the host; missing cells
are capability/environment blocks, not synthetic PASS.

## 4. Operator-controlled display state

Before a temporary physical configuration: snapshot state, prove no input is
being sent, let the operator establish or confirm the supported layout/scale,
re-read and verify, run the bounded cell, restore the original state, and
re-verify restoration. Failed restoration stops further physical cells.

No registry hacks, undocumented scaling APIs, blind Display Settings clicks,
unrelated-window minimization, or destructive hot-unplug automation.

## 5. Physical scenario families

- Transfer/containment: controlled GuineaPig first; real guarded transfer both
  directions; prove HWND/process identity, membership, monitor, DPI, rect,
  pane, point ownership, foreground, and liveness.
- Maximize/restore: guest caption maximize and Win+Up on representative
  high-DPI/mixed-DPI cells; no logical escape or unintended monitor jump.
- Title centering: numeric midpoint for short/long names and
  narrow/default/wide widths at each available DPI and after transfer.
- Controlled topmost: GuineaPig `--topmost`; no permanent container topmost.
- Split/single geometry: representative containment after transfer and odd
  partition boundaries.

## 6. Visual evidence

Retain restricted PRODUCT_OWNED/TEST_OWNED before/after checkpoints for title
centering, one mixed-DPI transfer, one topmost interaction, and one
maximize/restore transition. Bind images to candidate/run/scenario/attempt plus
topology/DPI snapshot. Do not expand to whole-desktop capture implicitly.

## 7. Failure and repair gate

Use PASS, FAIL_PRODUCT, FAIL_HARNESS, BLOCKED_ENVIRONMENT,
BLOCKED_SUPERVISED, BLOCKED_CAPABILITY, SKIP_CAPABILITY, and
FLAKE_UNCLASSIFIED. First valid failure remains authoritative.

Production repair requires frozen first evidence, first invariant divergence,
spec update if needed, non-vacuous regression, smallest Shepherd-preserving
fix, and adjacent topology/DPI requalification. Reparenting, permanent topmost,
global z-order polling/fighting, blind clicks, unchecked HWND reuse, and blind
repeated SetWindowPos are forbidden.

## 8. Gates and archive

Run repository-required Debug/Release builds/tests, driver/GuineaPig builds,
deterministic selftests, catalog, topology laboratory, visual verifier,
historical compatibility, strict OpenSpec, and canonical CI-safe Release
validation after implementation settles. Ordinary CI remains physical-topology
free, screen-capture-free, and model-free.

Final matrix must disposition every requested cell and explicitly close
migrated predecessor rows 4.8, 18.6, 19.2, and 19.3. Real-app rows stay in the
separate future campaign. Archive only after environment restoration, canonical
spec sync, truthful physical disposition, and clean Git authority.
