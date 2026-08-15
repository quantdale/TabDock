# TabDock

A universal window tab-grouping tool for Windows. Merge multiple independent application windows (browser, terminal, editor, etc.) into a single container window with a browser-style tab strip.

Built with C# / .NET 8 / WPF, using only P/Invoke for native interop.

## Requirements

- Windows 10 (recent builds) or Windows 11
- .NET 8 SDK (for development builds)
- Visual Studio 2022 or `dotnet` CLI

## Build and run

### Development build

```powershell
dotnet build TabDock.csproj
```

`global.json` pins the .NET 8 SDK feature band (roll-forward stays within
.NET 8); any installed .NET 8 SDK with feature band 8.0.4xx or later satisfies
it. NuGet restore is ordinary — strict NuGet lock mode is deliberately
avoided because SDK-generated `Microsoft.NET.ILLink.Tasks` differences make
lock results unstable across supported SDKs — and CI enforces a mandatory
vulnerability audit (`NuGetAuditMode=all`, warnings as errors).

Run the app:

```powershell
.\bin\Debug\net8.0-windows\win-x64\TabDock.exe
```

### Publish a single-file executable

```powershell
dotnet publish TabDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting single executable is at:

```
.\bin\Release\net8.0-windows\win-x64\publish\TabDock.exe
```

### Support diagnostics

Every build carries its semantic version, source commit, build configuration,
runtime identifier, and informational version. The read-only diagnostic
commands run without opening the main WPF UI, acquiring the TabDock
mutation lease, installing WinEvent hooks, or loading/saving product state.
`--recover-pending` is the exception: it is an explicitly mutating,
supervised command and acquires that lease for its full interactive
transaction:

```powershell
.\TabDock.exe --version
.\TabDock.exe --doctor
.\TabDock.exe --doctor --output .\TabDock-doctor.txt
.\TabDock.exe --support-bundle --output .\TabDock-Diagnostics.zip
.\TabDock.exe --pending-recovery
.\TabDock.exe --recover-pending
```

`--doctor` is a read-only report. It includes sanitized Windows/runtime,
monitor/DPI, display-adapter, persistence, TabDock-process, HWND geometry and
z-order, foreground, visibility/minimize, point-probe, and bounded trace
sections. Window titles are represented by length and a short SHA-256; default
reports do not include document names, URLs, command lines, clipboard data,
credentials, cookies, or telemetry. Optional probes report `unavailable` and
do not make the command fail.

When a running session is visibly broken, press `Ctrl+Alt+Shift+D`. This
diagnostic-only global hotkey writes a sanitized ZIP to the desktop without
depending on the possibly-hidden TabDock header. The in-process bundle adds
the current logical group/split presentation and captured guest identities to
the native snapshot. The command-line bundle remains useful when no TabDock
instance is running.

#### Friend-machine workflow

1. Build and publish one known Release artifact, then record the SHA-256 from
   `TabDock.exe --version` (or `--doctor`). Send that exact executable.
2. Ask the friend to run `TabDock.exe --version` and return the output before
   reproducing anything. The commit and SHA-256 must match the developer's
   record.
3. Ask them to run TabDock normally and reproduce the issue.
4. While the failure is visible, press `Ctrl+Alt+Shift+D`; send the resulting
   desktop ZIP. If the hotkey is unavailable, run
   `TabDock.exe --support-bundle --output TabDock-Diagnostics.zip` after the
   failure and also send `TabDock.exe --doctor` output.
5. Do not send the whole `%APPDATA%\TabDock` directory. The ZIP and doctor
   report are the intended support artifacts; review them before sharing.

### Notes on Native AOT

WPF relies on COM activation, reflection emit, and other runtime features that are incompatible with .NET Native AOT, so the publish profile uses a **self-contained single-file executable** instead. This still produces one distributable file with no external runtime dependency.

## How to use

1. Launch `TabDock.exe`. The main launcher window appears.
2. Click **New group**. From an open group, use **Add App** to open the inline
   capture surface; press **Ctrl+Alt+G** when no group is selected to use the
   standalone capture picker.
3. In the picker, select the windows you want to group and choose whether to add them to a new group or an existing one.
   Once a group is open, use its Group ▾ menu to switch between open groups or
   create another group without returning to the launcher.
4. The container window shows a tab for each captured window. Click tabs to switch, drag tabs to reorder, or drag a tab out of the strip to release it back to a standalone window.
5. Double-click the group name in the title bar — or use the Group ▾ menu's
   **Rename group** — to rename it (Enter commits, Escape cancels, blank names
   are rejected).
6. Click the colored chip in the title bar to change the group's accent color.
7. Use the Group ▾ menu's **Delete group** to remove a group: captured windows
   are released back to standalone (applications keep running) and the group is
   not restored after a restart.
8. Closing a container asks whether to close the grouped applications or release them back to standalone windows.

Empty group shells are session-only: a group with no captured tabs is not saved
or reopened on the next launch. Groups restored with saved tab metadata remain
available as layout placeholders until you repopulate or delete them.

## Architecture overview

- `NativeMethods.cs` — all P/Invoke declarations in one place.
- `Services/WindowShepherdService.cs` — TabDock's only capture backend. Positions/shows/hides a captured window over the container's content area without ever reparenting or restyling it; also owns the crash-recovery journal for hidden guests.
- `Services/WinEventMonitor.cs` — out-of-process `SetWinEventHook` wrapper.
- `Services/GroupManager.cs` — owns all groups and enforces the flat, no-nesting rule.
- `Services/PersistenceService.cs` — JSON persistence of group metadata.
- `Services/LoggingService.cs` — rotating log in `%APPDATA%\TabDock\logs\`.
- `Infrastructure/NativeHwndHost.cs` — a plain `HwndHost` marker sized to match the WPF-rendered content area; captured windows are positioned over it, never reparented into it.
- `Views/ContainerWindow.xaml` — custom chrome, tab strip, content host.
- `Views/CapturePickerWindow.xaml` — fallback window picker used from the global
  hotkey/launcher when no selected container can host the inline capture surface.
- `Services/BuildIdentity.cs`, `Services/DiagnosticReportService.cs`, and
  `Services/NativeSnapshotService.cs` — local, read-only build/environment/HWND
  evidence. `DiagnosticRuntime` retains a 1024-event in-memory trace and the
  in-product diagnostic exporter correlates that trace with the current
  logical presentation snapshot. These services never become a second native
  positioning authority.

## Manual test checklist

Use this checklist to verify a build before considering it ready.

### Basic grouping

1. Open **Chrome**, **Windows Terminal**, and **Cursor** (or any editor) as separate windows.
2. Launch TabDock and press **Ctrl+Alt+G**.
3. Select the three windows in the picker and click **Group these**.
4. Verify the container opens with three tabs and the active window fills the content area.

### Tab switching and reordering

5. Click each tab; verify the correct window is shown and the others are hidden.
   Click a tab's `×` or middle-click it to pop it out without closing the external
   application.
6. Drag a tab left/right in the strip; verify the order updates.

### Group identity

7. Double-click the group name and rename it to "Acme Corp - Invoice".
8. Open the Group ▾ menu and choose **Rename group**; rename it again and verify
   the title bar/group selector update immediately, that a blank name is
   rejected, and that the new name survives a restart.
9. Click the colored chip and choose a different accent color; verify the title bar/tab highlight updates.
10. Open the Group ▾ menu and choose **Delete group**; confirm the prompt.
    Verify the captured windows return to standalone AND keep running, the
    container closes, and the group does not come back after restarting TabDock.

### Ungroup

9. Drag one tab out of the container; verify the window returns to a standalone window at its original size, position, and style (caption, borders, maximize button).
10. Right-click a tab and choose **Pop out**; verify the same.

### Close from inside a tab

11. Close one of the captured applications from its own UI (e.g., close Chrome).
12. Verify its tab disappears cleanly. If it was the last tab, verify the container closes.

### No nested groups

13. Try to capture an existing TabDock container window into another group (use the picker and select the TabDock window).
14. Verify the operation is refused with a clear message.

### Elevated windows

15. Open Windows Terminal as Administrator.
16. Try to capture it with TabDock running as a standard user.
17. Verify a clear message explains that elevated windows cannot be grouped unless TabDock is also run as administrator.

### Kill TabDock via Task Manager

18. Group several windows, with at least one on an inactive tab.
19. Kill `TabDock.exe` from Task Manager (`taskkill /F /IM TabDock.exe`).
20. Verify every captured window/process is still running — since none of them
    were ever reparented, killing TabDock can no longer destroy them. Relaunch
    TabDock and verify every identity-valid guest returns to its original
    reversible presentation state. An intentionally self-hidden/tray-style
    guest must remain hidden and must not be resurrected.
21. If `%APPDATA%` is unavailable, TabDock may launch with memory-only logs or
    persistence disabled, but capture is disabled unless the durable
    crash-recovery journal is available. Resolve the storage issue and restart
    before capturing windows.

### DPI change

22. Move the container between monitors with different scaling (e.g., 100% and 150%).
23. Verify the content area re-lays out and the active window fills the host.

### Maximize / restore

24. Maximize the container with a window docked; verify the docked window resizes to fill the whole content area.
25. Restore the container; verify the docked window shrinks back to match.
26. Repeat maximize/restore a few times with a **split screen** active; verify both panes stay exactly side-by-side (no overlap, no gap) after every transition.

### Split screen

27. With two captured windows, right-click a tab and choose **Split screen**; verify both windows appear side by side as one `[ A | B ]` tab item.
28. Click the LEFT half, then the RIGHT half, alternating several times; verify BOTH panes stay rendered and the clicked side receives input every time (switching focus must never hide the partner or leave a blank pane).
29. Pop one half out via its `×` (or middle-click); verify the other half takes the full width immediately and stays visible.
30. Maximize and restore the container while split; verify the panes stay cleanly partitioned.
31. **Split persistence:** with three captured windows A/B/C and A+B split, hover C's tab, then click it, then right-click it and dismiss the menu, then click the LEFT half, then the RIGHT half, then click C again — hovering leaves A/B presented; clicking C makes C the single full-width guest while the `[ A | B ]` relationship remains dormant; either composite half restores the exact A/B pair; split ends only via **Exit split screen**, an explicit new Split Screen selection, or popping/closing a member.

## Known limitations

- **Guest self-maximize:** if you maximize the docked window itself (not the container), it fills the whole monitor, breaking the docked look — there's no reliable signal that distinguishes this from an ordinary interactive resize, so nothing corrects it automatically. Not a rendering or input bug, just a cosmetic gap.

- **Elevated windows:** A non-elevated TabDock cannot capture a window owned by an elevated process due to UIPI (it can't position/foreground it either, not just reparent it). TabDock ships as a standard-user app and asks the user to run elevated if they need to group elevated windows.

- **DPI awareness:** A guest whose DPI-awareness probe succeeds and identifies it as known `DPI_UNAWARE` is capturable at any valid monitor scale. TabDock is PerMonitorV2 and positions independent top-level outer HWNDs in physical screen pixels, so the unaware guest's outer geometry remains physical-pixel exact; Windows may bitmap-scale its content, making it appear blurry just as it does when run standalone. An unaware guest's 96-DPI logical minimum-track size is converted centrally using the target monitor's effective DPI. A failed or unknown awareness/monitor-DPI probe is refused fail-closed. System-aware guests are expected to track correctly on a single-DPI system; mixed-DPI physical qualification remains external and is not claimed by deterministic tests.

- **Persistence across reboots:** HWNDs are not stable across reboots, so TabDock cannot reliably re-attach the exact same live windows after a restart. It persists group names, accent colors, custom labels, tab order, and executable paths as layout intent. It restores only groups that contain saved tab metadata, leaves those groups empty at runtime for the user to re-populate, and does not persist fresh zero-tab shells. It never persists application content.

- **Task Manager kill:** captured windows are never reparented, so force-killing
  TabDock (`taskkill /F`) no longer destroys them. The versioned journal records
  the full reversible presentation state before mutation and restores every
  identity-valid guest on the next launch. A guest that intentionally hides
  itself receives a durable no-rescue marker and remains hidden. A legacy v1/v2
  journal without the v3 HWND-generation token is preserved as
  `hidden-windows.json.pending*` for supervised manual recovery rather than
  being silently discarded; `--doctor` reports that pending state.

  If `--doctor` reports pending legacy evidence, run
  `TabDock.exe --pending-recovery` first. This is read-only and lists stable
  per-session entry IDs, schema versions, available historical fields, and
  sanitized status without exposing the pending file's full path. From a
  supervised terminal, run `TabDock.exe --recover-pending`, select one pending
  entry, select the exact live top-level window from the local candidate list,
  and type `YES` to confirm. The workflow validates every historical field
  present in that entry, durably records a resumable transaction, and installs
  a cryptographically random temporary generation guard before changing
  presentation. v1 evidence restores visibility only; v2 evidence restores
  its recorded presentation state unless `DoNotRescue=true`, which preserves
  intentional-hide semantics and never shows or repositions the guest. A
  rejected, ambiguous, failed, or unverifiable recovery retains the evidence;
  an interrupted transaction resumes by phase and does not repeat native work
  after its durable native-complete marker. Resolving one entry preserves
  unresolved siblings, and startup never performs tokenless legacy recovery.

- **Browser qualification:** browsers are exercised only when actually
  installed, with isolated temporary profiles that never touch the user's real
  profile. An unavailable browser is reported explicitly and never counted as
  coverage. Physical mixed-DPI qualification is a separate external gate (see
  `docs/release/mixed-dpi-qualification.md`).

## Releases and release qualification

### Version contract

`TabDock.csproj`'s `<Version>` is the single authoritative version mechanism.
`--version`, the assembly metadata, `BuildIdentity`, and the release manifest
all derive from it. Historical non-semantic tag names (`stable`, `split`) do
not dictate the semantic version contract.

### Release chain

Release engineering is exact-SHA and immutable:

```
intended SHA -> clean exact checkout -> audited restore -> publish ->
execute and qualify THE PUBLISHED EXE -> optional Authenticode signing ->
signature verification -> SHA-256 -> release-manifest.json + SHA256SUMS.txt ->
immutable artifact retention -> human gates -> intentional GitHub Release
```

- `scripts/release-qualify.ps1` enforces the exact SHA, refuses dirty
  worktrees, publishes once, qualifies the published executable (embedded
  source commit must equal the candidate SHA; the executable's self-reported
  SHA-256 must equal `Get-FileHash`; geometry + diagnostics self-tests run on
  that binary), and writes `release-manifest.json` + `SHA256SUMS.txt`.
- `.github/workflows/release.yml` is dispatch-only (never runs on a push) and
  requires an explicit commit SHA. Publication is a separate explicit
  decision that re-verifies provenance before consuming the preserved
  artifact — a stable release can never silently substitute a different
  binary.
- Qualification vocabulary: `PASS`, `FAIL`, `BLOCKED_EXTERNAL`,
  `BLOCKED_ENVIRONMENT`, `SKIP_NOT_APPLICABLE`. A 0/N scenario run, an
  unavailable browser, or missing mixed-DPI hardware is never `PASS`.
- Human gates stay explicitly unperformed until real evidence exists:
  final manual Windows smoke (`docs/release/final-smoke.md`) and physical
  mixed-DPI qualification (`docs/release/mixed-dpi-qualification.md`).

### Signing policy

Authenticode signing is optional by default and mandatory only when
`RELEASE_SIGNING_REQUIRED=true` is set. Material is supplied exclusively
through CI secrets (`SIGNCERT_BASE64`, `SIGNCERT_PASSWORD`, optional
`SIGNCERT_TIMESTAMP`); no certificate or password is ever committed.
`scripts/sign-release.ps1` records one of `NOT_CONFIGURED`, `SIGNED`,
`SIGNATURE_VERIFIED`, or `SIGNING_FAILED` in the release manifest, and when
signing changes the bytes both `unsignedQualifiedSha256` and
`finalSignedSha256` are recorded. An unsigned executable is never described
as signed. Git commit signatures are unrelated to Authenticode and are never
conflated.

### Reproducibility

`global.json` pins the .NET 8 SDK feature band; NuGet restore is ordinary
with a mandatory CI vulnerability audit (strict lock mode deliberately
avoided); OpenSpec tooling is pinned through `tools/openspec/package-lock.json`
and installed with `npm ci --ignore-scripts`.

## Diagnostics

- **Deterministic geometry self-test:** `TabDock.exe --selftest-geometry` runs
  the split-partition matrix + seeded fuzz (14.7M checks) with no UI and no
  input; exit code 0 = all pass. Safe to run on any machine, including a
  customer's, to validate the pane math without touching the desktop.
- **Diagnostics/privacy self-test:** `TabDock.exe --selftest-diagnostics`
  exercises native deferred-position chaining, persistence backup/version and
  unreadable-file handling, journal identity gates, monitor failure policy,
  storage degradation, and adversarial support-bundle sanitization. It creates
  only disposable temp fixtures and exits nonzero on any failure.
- **Environment fingerprint:** every startup writes `ENV[startup]` (OS, .NET,
  bitness, monitor layout) and `ENV[launcher]` (system DPI); every container
  logs `ENV[container]` (rects, monitor, DPI, guest). For support, use the
  sanitized `--support-bundle` ZIP or `--doctor` output as the primary artifact.
  The raw `%APPDATA%\TabDock\logs\TabDock.log` is an advanced local diagnostic
  only: it may contain window titles, executable paths, and other local
  environment details. Inspect and redact it before sharing; do not paste it
  publicly by default.

## Logging

Diagnostic logs are written to:

```
%APPDATA%\TabDock\logs\TabDock.log
```

Log rotation keeps the current file under 1 MB; older logs are moved to `TabDock.log.old`.

## License

TabDock is open-source software distributed under the MIT License. Review the
license and your environment before using it; no warranty is provided beyond
the terms of that license.
