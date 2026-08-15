# Final Manual Windows Release Smoke

**Status: NOT PERFORMED — BLOCKED_EXTERNAL until executed by a human on real
Windows.**

This is the final human gate for the **exact release candidate artifact**
(verify `TabDock.exe --version` reports the release commit and a SHA-256 equal
to `release-manifest.json`'s). It is a smoke test, not an exhaustive suite:
every item is a quick manual check on a real desktop.

Result vocabulary per item: `PASS`, `FAIL`, `SKIP_NOT_APPLICABLE`,
`BLOCKED_ENVIRONMENT`. The overall smoke is PASS only when all applicable
items PASS with evidence (a checklist signed by the operator).

## Setup

- Fresh Windows 10/11 x64 session; at least 2-3 real applications available
  (e.g. Notepad, Windows Terminal, a browser).
- Run the release `TabDock.exe` (never a debug build).
- Record the candidate SHA-256 and the machine OS build before starting.

## Application

| # | Check | Pass criteria |
|---|-------|---------------|
| 1 | Launch | Launcher appears; no crash; `--version`, `--doctor`, `--pending-recovery` exit 0 |
| 2 | Normal exit | Close from launcher; process exits cleanly |

## Groups

| # | Check | Pass criteria |
|---|-------|---------------|
| 3 | New Group | Creates a group |
| 4 | Rename group | Inline rename commits on Enter; blank rejected |
| 5 | Color picker | Accent color changes and persists |
| 6 | Group dropdown | Switch between groups via Group ▾ |
| 7 | Delete group (empty + populated) | Confirmation; guests released and running; group does not return after restart |

## Capture

| # | Check | Pass criteria |
|---|-------|---------------|
| 8 | Inline Add App | Captures a window into the open group |
| 9 | Global capture hotkey | Ctrl+Alt+G opens the standalone picker |
| 10 | Capture 2-3 applications | All present as tabs; active fills content area |
| 11 | Reject unsafe/unverified target | TabDock container itself or an unverifiable target is refused with a clear message |

## Tab operations

| # | Check | Pass criteria |
|---|-------|---------------|
| 12 | Switch tabs | Active guest swaps; others hidden |
| 13 | Reorder tabs | Order updates |
| 14 | Pop-out via X | Guest returns to standalone at original placement; process alive |
| 15 | Middle-click pop-out | Same as 14 |

## Guest lifecycle

| # | Check | Pass criteria |
|---|-------|---------------|
| 16 | Guest self-close | Tab disappears; container closes on last tab |
| 17 | Guest self-hide/tray | Tab disappears; guest stays hidden; not resurrected after restart |
| 18 | Release and restore window state | Normal/maximized guests return to exact prior state |

## Container

| # | Check | Pass criteria |
|---|-------|---------------|
| 19 | Minimize/restore | Guests re-glue; split panes stay partitioned |
| 20 | Maximize/restore | Content area fills; guests resize with it |
| 21 | Move/resize | Guests track the content area without gaps |

## Split screen

| # | Check | Pass criteria |
|---|-------|---------------|
| 22 | Create split pair | [ A \| B ] composite tab appears |
| 23 | Interact with LEFT | Left pane receives input; partner stays visible |
| 24 | Interact with RIGHT | Same for right |
| 25 | Display unrelated third tab | C full-width while A/B dormant; pair restores from either half |
| 26 | Split relationship persists | Through tab switches and restarts of the container |
| 27 | Restore split | Composite half restores exact A/B pair |
| 28 | Explicit exit split | Split ends only via Exit/selection/pop-out/close |
| 29 | Remove split member | Survivor takes full width immediately |

## Native guest interaction

| # | Check | Pass criteria |
|---|-------|---------------|
| 30 | Guest title-bar move | Dragging guest by its own title bar re-glues it to its pane |
| 31 | Re-glue after native move/resize | Same for native resize |

## Recovery

| # | Check | Pass criteria |
|---|-------|---------------|
| 32 | Force-kill TabDock (Task Manager) | Guests survive; none destroyed (never reparented) |
| 33 | Verify guest processes survive | All captured processes still running |
| 34 | Relaunch | Journal recovery restores identity-valid guests to intended state; intentionally hidden guests stay hidden |
| 35 | Pending legacy evidence | If `--doctor` reports pending v1/v2 evidence, `--pending-recovery` lists it read-only; no automatic rescue occurs |

## Support

| # | Check | Pass criteria |
|---|-------|---------------|
| 36 | Support bundle hotkey | Ctrl+Alt+Shift+D writes a sanitized ZIP to the desktop |
| 37 | Support bundle privacy check | ZIP contains no username, profile paths, or credentials (inspect before sharing) |

## Final exit

| # | Check | Pass criteria |
|---|-------|---------------|
| 38 | Normal shutdown | Guests released; independent state retained |

## Browser note

If Chrome/Edge/Brave/Firefox is unavailable, report the exact unavailable
browser. Never substitute unavailable-browser coverage with PASS.

## Recording

The operator signs the checklist with: candidate SHA-256, machine OS build,
date, and every item's result. The smoke is PASS only with every applicable
item PASS; anything unexecuted is `BLOCKED_EXTERNAL` and is recorded as such
in `release-manifest.json` (`externalGates.finalWindowsHumanSmoke`).
