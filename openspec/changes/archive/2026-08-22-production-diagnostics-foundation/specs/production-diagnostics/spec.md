## ADDED Requirements

### Requirement: Distributed binaries expose authoritative identity
Every built TabDock executable SHALL expose a structured identity containing product name, semantic version, informational version, source commit when available, build configuration, runtime identifier when available, process/OS architecture, executable path, and a best-effort executable SHA-256 in diagnostic output. Identity SHALL be derived from build metadata and generated assembly metadata; runtime SHALL not require Git, a checkout, or a network. Missing values SHALL be reported as unavailable rather than fabricated.

#### Scenario: version mode is UI-free
- **WHEN** `TabDock.exe --version` is invoked
- **THEN** it prints the identity, exits zero, creates no TabDock windows, installs no WinEvent hooks, and does not read or write product state

#### Scenario: commit survives outside the repository
- **WHEN** a published executable is copied to a machine without Git or source
- **THEN** `--version` and `--doctor` still report the embedded source commit or an honest unavailable value

### Requirement: Doctor is read-only and privacy-safe
`TabDock.exe --doctor` SHALL produce a human-readable support report and exit successfully when optional probes fail. It SHALL include build/runtime/Windows/elevation/session information, all monitor bounds/work areas/DPI/scale/orientation available through safe APIs, optional GPU/driver information, persistence summaries and corruption classifications, current TabDock process summaries, and sanitized recent logs. It SHALL not move, show, hide, capture, release, activate, resize, reorder, kill, or persist product windows/state. Usernames, machine names, full document titles, URLs, command-line secrets, clipboard contents, cookies, tokens, and arbitrary environment secrets SHALL be omitted or redacted by default.

#### Scenario: no state exists
- **WHEN** doctor runs on a machine without `%APPDATA%\TabDock\state.json`
- **THEN** it returns zero and reports an absent state file without creating one

#### Scenario: corrupt state exists
- **WHEN** the state file is unreadable or malformed
- **THEN** doctor returns zero and reports an unreadable/corrupt classification without quarantining, rewriting, or mutating it

### Requirement: Native and logical snapshots correlate safely
The diagnostic model SHALL distinguish observed native state from current logical presentation state. Native observations SHALL tolerate HWND destruction, process exit, access denial, failed class/title/monitor/DWM queries, and UIPI. For TabDock containers and captured guests it SHALL record, where available, HWND/PID/process identity, class, redacted title hash/length, visibility, iconic/zoomed state, screen/client geometry, monitor/DPI, owner, topmost/cloaked state, foreground state, z-order neighbors, and safe header/content/split `WindowFromPoint` probes. Logical snapshots SHALL record real group IDs, container HWNDs, active member, split membership/focus, visible members, expected pane rectangles, and interaction/minimize/maximize state. Snapshot creation SHALL be side-effect-free.

#### Scenario: header disappearance is classifiable
- **WHEN** a diagnostic snapshot is captured while a container appears missing
- **THEN** the report can distinguish hidden, minimized, covered by a guest, covered by another TabDock container, covered by an unrelated HWND, and foreground/reorder reconciliation history using observed geometry, visibility, z-order, foreground, and point-probe fields

#### Scenario: destroyed window races do not abort doctor
- **WHEN** a window disappears during enumeration or query
- **THEN** its observation is marked unavailable/destroyed/probe-failed as appropriate and the remaining report is emitted

### Requirement: Selected diagnostic events and repairs are bounded and ordered
TabDock SHALL retain a bounded in-memory ring of significant diagnostic events. Each event SHALL have a monotonic local sequence number and event time/context, and the trace SHALL include selected foreground, reorder, move-size, guest lifecycle, container activation/window-position, group/tab/split transitions, capture/release, layout/reconciliation, and native repair outcomes. Selected events MAY include callback and UI-dispatch observations. The trace SHALL not subscribe globally to high-volume location-change events or implement periodic health polling solely for this capability. Trace writes SHALL be safe from callback/UI concurrency and bounded in memory and exported logs.

#### Scenario: repair outcome is reconstructable
- **WHEN** TabDock attempts a significant native repair
- **THEN** the trace records why, desired target, observed-before state, attempted native action, result/error, and observed-after state when available

#### Scenario: ring buffer stays bounded
- **WHEN** more events than its configured capacity are recorded
- **THEN** the oldest events are evicted, sequence numbers remain monotonic, and snapshot/export does not mutate the trace

### Requirement: Local support export is explicit and non-mutating
The product SHALL support an explicit local diagnostic export suitable for a friend-machine workflow, producing a portable directory or ZIP containing identity/version, doctor report, environment/state summaries, native/logical snapshots, sequenced trace, and bounded recent log text. Export SHALL not upload data, enable telemetry, alter product state, call SaveState, capture/release guests, activate TabDock, or change native window state. The export SHALL be usable while the header is unavailable through a diagnostic trigger that does not rely solely on the header, and `--doctor` SHALL remain useful without a running instance.

#### Scenario: bundle contents are sanitized
- **WHEN** a user exports diagnostics without an explicit verbose-title opt-in
- **THEN** the bundle contains only redacted/hash window titles and bounded support data, not document contents, URLs, credentials, clipboard, cookies, or arbitrary personal files

#### Scenario: optional probe fails
- **WHEN** GPU, process, hash, DWM, or live-instance enumeration is unavailable
- **THEN** the export records an unavailable/error category and still completes the other sections
