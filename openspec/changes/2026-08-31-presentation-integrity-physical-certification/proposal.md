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

The initial phase produced validation/evidence-only changes because it was
blocked before physical input. During the supervised continuation, a valid
physical Chrome F11 failure authorized the minimum production repair described
in the design continuation. No other speculative production behavior was
introduced.

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

## Continuation outcome

The shared foreground qualification gate now fails closed before any unsafe
input and has deterministic coverage for successful activation, stale/foreign
targets, unsafe points, lease invalidation, target disappearance, and bounded
failure cleanup. The physical continuation added real GuineaPig guest
caption-maximize, Win+Up, mixed-DPI transfer, topmost interaction,
`LOCATIONCHANGE` load, and title-centering scenarios.

The first valid Chrome F11 product failure was frozen before repair. The
identity-checked browser-F11 restore path then passed fresh Chrome, Edge, and
Brave physical runs. Edge's initial point mismatch was a same-process
registered Chromium dynamic surface, not foreign occlusion; the browser
qualifier accepts that surface only with matching process-start/executable and
registered role evidence.

Adjacent physical requalification is complete for every requested cell: nine
fresh cells passed; three `split-exit` attempts were retained as
`BLOCKED_ENVIRONMENT` because the fail-closed foreground/point guard refused
input, and the earlier accepted split-exit pass remains evidence. The final
deterministic/CI gates and authoritative `main` push remain before closure;
this change stays active until they are verified.

## Historical partial supervised certification checkpoint — 2026-08-31

A supervised rerun on candidate `dd5f819484498b1e74678710bde58d55fbdcf8fa`
established valid native leases for bounded attempts and materially improved the
physical evidence. Supported rename, split, inline-capture, context-menu/chrome,
direct-foreground/local-pairing and adjacent presentation paths passed after
harness-only false-negative corrections. No valid physical `FAIL_PRODUCT` was
confirmed and production TabDock code remained unchanged.

This is **partial**, not full, physical certification. The remaining uncertified
cells are now primarily missing ValidationDriver capabilities rather than
missing hardware or supervision:

- physical guest-caption maximize/restore;
- Win+Up/restore;
- real Chrome/Edge/Brave F11 fullscreen/exit;
- explicit dual-monitor transfer and mixed-DPI transition measurement;
- controlled `WS_EX_TOPMOST` guest dispatch (fixture exists, scenario does not);
- dedicated `EVENT_OBJECT_LOCATIONCHANGE` load/storm qualification;
- physical/UIA title-centering measurement across width/name/DPI;
- stable generic foreground qualification for scenarios that currently stop at
  `StartScenario` before setup/input.

The next wave SHALL build only the missing qualification capabilities and
stabilize the harness foreground-admission path without weakening the existing
ownership/coverage/foreground guards. Production code remains out of scope
unless one of those newly executable physical cells produces a valid
`FAIL_PRODUCT`.
