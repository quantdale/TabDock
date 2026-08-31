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

The initial plan called for bounded ValidationDriver scenarios and no
production behavior. The continuation below records the evidence-driven
production repair that became necessary after a valid physical failure.
Topmost support used only the smallest fixture/setup capability; no permanent
topmost policy or reparenting was added.

If a cell cannot run, the record names the exact missing supervision,
application, topology, OS, signing, or fixture capability and gives the rerun
command. Synthetic policy coverage remains separate from physical
qualification.

## Continuation — browser F11 repair and physical evidence

The supervised continuation ran against the dynamically verified `main`
candidate `b0975b2a724f0cf9551c4e106dfc6449c8643002` plus uncommitted,
evidence-driven changes. The first Chrome F11 run was valid
`FAIL_PRODUCT`: Chromium entered borderless monitor-wide F11, and the
existing `SetWindowPos` restore was refused by Chromium's native minimum
size. The raw run, log excerpt, and artifact remain frozen in the campaign
investigation.

The minimal repair preserves Shepherd's independent top-level-window model:
when a strongly identified Chrome/Edge/Brave guest is borderless and outside
its assigned pane, Shepherd posts exactly one identity-checked F11
key-down/key-up pair to that same browser HWND, suppresses duplicate requests
until the resulting `LOCATIONCHANGE`, then applies the ordinary assigned
rectangle. It does not reparent the guest, change its style, manipulate an
unrelated window, or make TabDock permanently topmost. The
`NativePresentationRestorePolicy` unit contract covers iconic/zoomed restore
and borderless geometry mismatch.

Fresh physical browser requalification passed for Chrome
(`71656457-b555-42ce-a782-b4947f33f292`), Edge
(`9cb2ad2a-a6bc-41a2-b196-2492702b9331`), and Brave
(`01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6`). Edge's first physical artifact
exposed a same-process registered Chromium dynamic surface under
`WindowFromPoint`; the qualifier now accepts only that surface when its
PID, process start, executable, and registered `.DynamicSurface` role match
the captured browser. Foreign roots still block the scenario.

The continuation also adds physical evidence scenarios for mixed-DPI
dual-monitor transfer, captured topmost interaction,
controlled `LOCATIONCHANGE` load, and title centering. All four passed:
`f423509e-f869-4097-b938-964355bd9101`,
`f3ee2adb-2d4b-46ce-973f-ccbf789e5aca`,
`345e33f8-8086-4819-9e5f-72acbdec45ed`, and
`1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19`. Monitor placement uses only
identity-checked test-owned arrangement; the Win+Shift+Arrow actions and
all guest/menu/input actions remain real guarded input.

## Continuation — adjacent qualification outcome

After the browser repair, fresh Release runs passed rename,
group-rename-menu, add-window-toggle, capture-inline-ui,
split-focus-bidirectional, contextmenu-render-stability,
chrome-click-render-stability, directclick-foreground-pairing, and
dragreorder. Three fresh `split-exit` attempts remained
`BLOCKED_ENVIRONMENT` because the foreground/point guard encountered a
wrong-owned or unregistered point; the earlier accepted split-exit pass is
retained. No adjacent raw block was relabeled as a product failure.

## Historical continuation wave — capability completion and stable foreground qualification

The first supervised rerun proved that the remaining gap is no longer a general
desktop-supervision problem. Valid leases were accepted for multiple bounded
runs. The continuation therefore targets two harness limitations:

1. missing physical scenarios for the still-blocked matrix cells; and
2. generic foreground setup that can fail before a scenario has a chance to use
   its stronger point-ownership/clickability proof.

### Foreground qualification

The generic `StartScenario` foreground gate SHALL remain fail-closed, but its
implementation must be reviewed so "foreground arrangement" and "foreground
proof" are not conflated.

A safe continuation MAY attempt a bounded, identity-checked foreground
arrangement and then prove the result. If ordinary `SetForegroundWindow`
cannot obtain foreground because of Windows foreground-lock behavior, a
scenario MAY use a guarded activation click only when all of the following hold:

- the target is a current test-owned/adopted TabDock surface;
- `WindowFromPoint` + `GA_ROOT` resolves exactly to that target;
- the desktop lease remains valid;
- no foreign window covers the activation point;
- the click itself is recorded as an explicit qualification action;
- foreground is re-read and verified immediately afterward;
- failure leaves the scenario blocked/fail-harness before destructive setup.

This is not permission to bypass foreground checks, click blind, use
`AttachThreadInput`, globally reorder foreign windows, or silently downgrade
the lease. Prefer one shared foreground-qualification primitive with
deterministic fake-probe tests over scenario-specific retries.

### Missing physical scenario capabilities

Add the smallest ValidationDriver/test-fixture support needed for:

- **guest caption maximize/restore** — a guarded click on the captured guest's
  real maximize/restore caption control, with same-identity/membership/geometry
  assertions afterward;
- **Win+Up/restore** — guarded system shortcut against the captured guest, with
  current foreground/identity proof before input;
- **F11 fullscreen** — isolated Chrome/Edge/Brave path that sends real F11,
  records style/exstyle/placement/rect/monitor/membership where available, then
  exits fullscreen and verifies recovery;
- **dual-monitor transfer** — explicit guarded Win+Shift+Arrow or equivalent
  system transfer attempt in both directions, including 125%↔100% topology;
- **topmost guest** — dispatch GuineaPig's existing `--topmost` fixture and
  exercise popup/rename/dialog/local-z-order interactions;
- **LOCATIONCHANGE load** — controlled test-owned unrelated window churn plus
  captured-guest churn, collecting callback/rejection/post/coalescing/native
  repair counts and responsiveness;
- **title centering** — UIA/native geometry measurement of title/editor midpoint
  versus container client midpoint at narrow/default/wide widths, short/long
  names, and both physically available DPI monitors.

These are qualification features, not production features. Keep them isolated
to ValidationDriver/GuineaPig and test evidence unless a valid physical
`FAIL_PRODUCT` proves a product change is necessary.

### Stop rule

If two consecutive fresh attempts fail the same generic foreground
qualification before scenario setup, stop physical execution and diagnose the
shared harness primitive before adding more retries. Do not treat repeated
setup failure as product evidence.
