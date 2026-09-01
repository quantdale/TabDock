# Design — real-app hardening

## Context

TabDock uses the Shepherd model: guests remain independent top-level windows positioned over the container. ValidationDriver already qualifies GuineaPig with `SendInput`, lease, point/foreground, and visual checkpoints. The preceding DPI campaign proved that model across 120/96 DPI with 14 RUNNABLE cells. Real apps (Chromium, Notepad, Terminal) expose new ownership, presentation-drift, and privacy questions but the same contracts apply.

```
Run-owned GuineaPig  ---> already qualified (Shepherd, DPI, visual)
      |
      +---> Chromium F11  (borderless, async LOCATIONCHANGE, one-shot F11 exit)
      +---> Notepad broker (packaged HWND, owner/root, process ancestry)
      +---> Terminal monarch (launcher vs host, reuse)
                |
                v
        Adopted vs Run-owned distinction (HWND/PID/start/exe/owner/root/generation)
                |
                v
        REAL_APP_RESTRICTED capture (cropped, no desktop, no secrets)
```

## Goals / Non-Goals

Goals: one campaign absorbs rows 19.1/19.4, proves browser fullscreen containment without false positives, proves Notepad/Terminal ownership, preserves first-failure authority, and produces a physical matrix that can be archived with explicit capability blocks if the proposal boundary permits them.

Non-goals: see proposal.

## 1. Row-level handoff

For 19.1 and 19.4 each row records: original wording, migration reason, current implementation support, scenario, exact acceptance, visual/privacy, final disposition. No row disappears.

## 2. Ownership boundary

Discovery before input:

```text
EnumWindows -> Filter Visible/NotTool/Titled/NotOwn/NotCaptured/NotCloaked
   -> For candidate HWND: GetWindowThreadProcessId -> OpenProcess -> QueryInformation
      GetWindowDpiAwarenessContext/GetAwareness, GetWindowLongPtr(STYLE/EXSTYLE),
      GetParent/GetAncestor(GA_ROOT), GetWindow(GW_OWNER), IsWindowVisible/IsIconic/IsZoomed,
      DWM cloaked, MonitorFromWindow -> GetEffectiveDpi, point probe
   -> strong identity: HWND + PID + TID + class + exe path + process-start ticks + HWND token
```

Run-owned: spawned by `SpawnGuest`/`SpawnBrowserGuest`/`SpawnClassGuest` with isolated profile or isolated `wt`/`notepad` launch; recorded in `TestRunProvenance` and killable only if exact PID/start matches. Adopted: discovered live window; may be bounded input target while identity matches, but owning process is never cleanup-owned (`DoNotKill`).

Title never participates in identity.

## 3. Chromium fullscreen

Browser F11 is detected as **borderless presentation on a known Chromium guest** (style `WS_CAPTION` absent, placement/monitor observation) rather than a generic global state. The existing repair:

- `NeedsNativePresentationRestore` checks `bypassNativeMinimum` for borderless Chromium; if true, `RequestBrowserFullscreenExit` posts **one** identity-checked `F11` via `SendF11To` (which proves foreground/lease and HWND generation) and **returns without touching the native rect**.
- `SHEPHERD[drift-reconcile]` and later `LOCATIONCHANGE` from Chromium re-enters the bounded Shepherd path, restoring `WS_CAPTION` and re-gluing to the pane.
- `SHEPHERD[presentation-restore-request]` is coalesced to one per drift; duplicate suppression is proven by counting log lines.
- No repeated `F11` loop and no tab click is used; native transition is observed via `NativeGuestSnapshot` before/after (outer, style, zoomed, monitor, title).

Compatibility review must prove the classifier does not send F11 to:
normal maximized Chromium, ordinary borderless, PWA/windowed app, kiosk-like, popup/devtools, stale transition, monitor-sized non-F11.

## 4. Windows 11 Notepad

On Windows 11, Notepad is a packaged app. The actual top-level may be a broker/host. Inspection must record executable identity, HWND, owner/root, PID, process-start, ancestry, and whether the UI surface survives process changes. The harness will:

- use `notepad-broker` application definition;
- capture via `CaptureIntoGroup` with strong identity;
- exercise focus/tab/maximize/transfer/release/re-capture;
- observe close only if the specific `SpawnedGuest` PID matches and termination is proven by that PID/start.

If the HWND is replaced or belongs to a broker, the scenario is `BLOCKED_CAPABILITY` rather than killing a user Notepad.

## 5. Windows Terminal

`wt.exe` is often a launcher that hands off to an existing `WindowsTerminal.exe` monarch. The harness will:

- inspect launcher PID vs visible HWND owner PID;
- record HWND, root, monitor, DPI, process-start for the visible surface;
- distinguish `Spawned` vs `Adopted` by whether the visible HWND's PID equals the spawned PID and shares the same process-start;
- prove launcher exit ≠ guest disappearance;
- cleanup only the exact run-owned PID/start.

An additional adopted-existing terminal path exercises capture without spawn.

## 6. Visual privacy

`REAL_APP_RESTRICTED` is the existing privacy class for adopted real apps. Recorder scopes remain test-owned/product-owned by default; real-app capture requires explicit policy and is cropped to the host client plus bounded context. VirtualDesktop remains disabled. Packets carry `privacyClass` and `actualCaptureRect`. Review workflow remains `.agent/workflows/visual-evidence-review.md`.

## 7. Failure and product-repair gate

First valid attempt is authoritative (fail or block). A valid `FAIL_PRODUCT` freezes evidence, identifies first divergence, updates the relevant requirement, adds non-vacuous regression, makes smallest Shepherd-preserving fix, then requalifies failing plus adjacent browser/Notepad/Terminal/presentation cells. Forbidden repairs listed in proposal.

## 8. Release/version boundary

Historical `6bb8e` v1.1 qualification is preserved as pre-final evidence. If current product source changed since `6bb8e` (it did — `bc678ef`), the final campaign must record a **new exact settled source SHA**, executable/driver SHA-256, informational version, release mode, signing status, and production eligibility; it must not reuse `6bb8e` hash or claim signing without material.

## 9. Deterministic and physical gates

After implementation settles:

```
dotnet build -c Debug / Release
dotnet test -c Debug / Release
build ValidationDriver/GuineaPig Release
selftest all / capability / visual
scenario catalog validation
topology lab / visual verifier / historical bundle compatibility
resource-lifecycle gates if affected
native ABI / release-tooling
scripts/validate.ps1 -Ci -Publish
plan real-app / physicalMixedDpi
```

Physical real-app cells run only with proven candidate/executable/driver/run/scenario/attempt, lease, topology, foreground, point ownership, and display-state restoration.

Archive only via canonical `openspec archive real-app-hardening --yes` after strict validation and with clean Git authority.
