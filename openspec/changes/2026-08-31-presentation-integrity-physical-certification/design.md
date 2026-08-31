# Design — presentation-integrity physical certification

## Baseline and claim boundary

Baseline implementation checkpoint:
`4aaf3fcaa72edf48865030db43bccf7bd50e21b8`.

Before running, resolve current `HEAD`, `origin/main`, branch and worktree
dynamically. Newer mainline commits supersede the embedded checkpoint.

The starting claim is deliberately narrow:

> Deterministic implementation and qualification standard met; physical
> certification of the original field reports remains pending.

A physical PASS can strengthen that claim. A blocked capability cannot.

## Operating rules

- Real mouse/keyboard input requires the repository's exclusive supervised
  desktop lease and ownership checks. If the lease cannot be proven, do not
  send input.
- Do not run physical input concurrently with the user's normal desktop work.
- Preserve first-attempt result/evidence. Investigation reruns are additive.
- A valid `FAIL_PRODUCT` followed by PASS is not a release PASS; retain it as
  a flake/unresolved defect until understood.
- Separate capability absence from product behavior:
  `BLOCKED_ENVIRONMENT`, `BLOCKED_SUPERVISED`,
  `BLOCKED_CAPABILITY`, and `SKIP_CAPABILITY` are not failures and not
  passes.
- Never infer visual health only from HWND rectangle equality. Use
  `WindowFromPoint`/root ownership and client-render/liveness evidence where
  applicable.

## Physical certification matrix

### Matrix A — original chrome-occlusion reports

Run repeated cycles with at least one controlled guest; include a real app
(Notepad/Terminal/browser) when safely available.

1. Accent/color menu: open, choose several colors, dismiss by click and Escape.
2. Workspace/group rename: enter by every supported path, type, commit, cancel,
   click-away, reopen.
3. Split affordance/menu: create/focus/end/resume as applicable.
4. "+" inline capture surface: open/close/reopen/cancel/capture.
5. Combine the above with a presented split pair where the product supports it.

For every cycle prove:
- target TabDock chrome is visible and receives the intended click/keyboard;
- guest(s) remain live and visible in the expected region;
- content-center point ownership does not resolve to an opaque container when
  the guest is expected there;
- no corrective tab click is needed after the interaction closes;
- no stranded popup depth or repeated z-order repair remains.

Target at least 20 cycles for color/menu/rename where runtime permits and at
least 8 cycles for heavier split/capture workflows.

### Matrix B — guest-native window-state transitions

Physically invoke the guest's own UI/system behavior:

- standard maximize/restore via the guest caption;
- Win+Up/restore where allowed;
- browser/real-app F11 fullscreen and exit;
- custom fullscreen/maximize controls if a known app uses them;
- repeat from single-guest and split presentation where meaningful.

Verify strong captured identity, logical membership, visibility, assigned
region, foreground usability, monitor, and no post-transition corrective click.

Synthetic `SW_SHOWMAXIMIZED` remains useful diagnostic coverage but cannot
alone satisfy this matrix.

### Matrix C — monitor/topology

When two real monitors are available:

- move the TabDock container between monitors;
- attempt guest-native Win+Shift+Arrow / monitor transfer while captured;
- maximize/fullscreen on one monitor then restore;
- exercise both transfer directions;
- repeat with different monitor DPI/scaling when available.

Record physical monitor bounds/work areas/DPI and guest/container monitor
identity. If only one monitor or one DPI is available, mark the unavailable
cells honestly.

### Matrix D — topmost and external overlap

Use a controlled `WS_EX_TOPMOST` guest/window if the harness can create one
safely; otherwise add the smallest test-only capability necessary.

Exercise:
- color/workspace/split popup above the area where the topmost guest exists;
- rename/input focus;
- owned MessageBox/dialog;
- unrelated ordinary foreground window overlapping TabDock;
- direct guest click after external foreground steal.

The expected policy is local presentation integrity, not global dominance.
TabDock must not make its container permanently topmost or fight an unrelated
foreground application.

### Matrix E — location-change load

Measure the `EVENT_OBJECT_LOCATIONCHANGE` hook under:

- one captured guest with unrelated windows being moved/resized repeatedly;
- captured guest resize/maximize churn;
- split presentation with both members stable;
- no captured guests.

Collect callback count, captured-member probes, posts/coalesced repairs, native
layout/SetWindowPos effects, and UI responsiveness. The exact thresholds should
be derived from existing telemetry/budgets rather than invented arbitrarily,
but the invariant is that unrelated churn is rejected early and equivalent
captured events coalesce without unbounded dispatcher/native mutation growth.

### Matrix F — caption geometry

Physically/through UIA measure display title and rename editor center across
representative:
- narrow/default/wide container widths;
- short and long names;
- 100%, 125%, 150%, 175%, 200% DPI when available.

Require midpoint tolerance consistent with WPF/device-pixel rounding and verify
no caption control becomes unreachable.

## Residual defect gate

If any valid physical run produces `FAIL_PRODUCT`:

1. Freeze the first-attempt artifacts and exact candidate SHA.
2. Reproduce minimally without changing code.
3. Find the first observable invariant divergence.
4. Challenge existing assumptions; do not immediately blame the most recently
   changed component.
5. Update this OpenSpec with the proven cause.
6. Add a focused deterministic and/or physical regression that fails on the
   candidate.
7. Implement the smallest bounded Shepherd-preserving fix.
8. Run focused gates, then rerun the failed physical scenario from a fresh
   state.
9. Rerun adjacent matrix cells to detect collateral regressions.

Do not introduce architecture changes for an unobserved theoretical edge case.

## Completion rule

This campaign is complete only when every matrix cell is one of:

- physically PASSed on a valid exclusive lease;
- honestly BLOCKED/SKIPPED with the missing capability named and deterministic
  coverage identified;
- failed, diagnosed, fixed, and requalified with the first failure retained.

A release/handoff summary must explicitly list which original user reports are
now physically reproduced-and-passed versus only deterministically covered.


## Evidence boundary

The candidate is the exact committed tree under test. Release identity is read
from `TabDock.exe --version`; the executable SHA-256 is recorded beside it.
Read-only discovery may inspect the desktop, processes, monitors, DPI, work
areas, and application availability. It must not mutate product state or send
input.

A physical attempt is admissible only when all of the following are proven in
one uninterrupted run:

1. the Windows interactive session is unlocked and `SendInput` is available;
2. a supervised operator has exclusive control of the desktop for the run;
3. the ValidationDriver desktop lease is active;
4. every TabDock, guest, popup, and adopted target is bound to a current
   process-start/HWND identity;
5. `WindowFromPoint` → `GA_ROOT` and foreground checks pass immediately before
   each guarded action and assertion; and
6. no foreign window covers a target or invalidates the lease.

If item 2 cannot be established, no physical input is issued even when the
native preflight reports an unlocked interactive session.

## Scenario evidence

Each scenario retains its first attempt, rerun lineage, outcome code, and
artifact references. Evidence records both sides of a presentation claim:
TabDock chrome must be visible and actionable, and representative guest points
must resolve to the expected guest root with live/client-render evidence.
Geometry equality alone is not a pass. Guest/container/popup HWNDs, process
identities, visibility, zoom/iconic state, `WINDOWPLACEMENT`, monitor, DPI,
foreground, local z-order, and logical membership are recorded where the
scenario exposes them.

The matrix uses the existing bounded ValidationDriver scenarios and adds no
production behavior. If the GuineaPig fixture lacks topmost support, only the
smallest test-fixture switch is added; no production z-order change is allowed.

If a cell cannot run, the record names the exact missing supervision,
application, topology, OS, signing, or fixture capability and gives the rerun
command. Synthetic policy coverage remains separate from physical
qualification.