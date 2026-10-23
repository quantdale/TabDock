# production-diagnostics Specification

## Purpose
Read-only doctor, support-bundle, build identity, native snapshot, and diagnostic-trace supportability without Shepherd write authority.
## Requirements
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

### Requirement: Doctor SHALL tolerate invalid root schema metadata
Doctor SHALL classify wrong-type, invalid numeric, and unsupported root schema metadata without throwing or invoking mutating restore. Malformed metadata SHALL be reported as corrupt, a supported missing legacy version SHALL be distinguished from invalid metadata, and a representable future version SHALL be reported unsupported. These classifications SHALL NOT prevent other available report sections from being emitted with exit code zero.

#### Scenario: Wrong-type Version still produces a doctor report
- **WHEN** persisted state contains Version as a string, null, boolean, object, or array
- **THEN** doctor returns zero, reports corrupt schema metadata, emits the remaining available sections, and preserves primary and backup bytes

#### Scenario: Numeric and missing versions are classified deliberately
- **WHEN** state has a fractional, out-of-range, nonpositive, missing, or representable future Version
- **THEN** doctor distinguishes corrupt numeric metadata, supported legacy absence, and unsupported future schema without modifying the file

### Requirement: Read-only recovery inspection SHALL preserve all storage artifacts
Pending-evidence discovery SHALL be non-mutating by default and for pending-recovery listing, doctor, support export, launcher attention and count projections. Apart from explicitly requested report output, inspection SHALL preserve the product directory's file set, file bytes and last-write times, including aged pending-prefix temporary fragments, sources, sidecars and transaction records. It SHALL perform no native recovery mutation and remain usable without the product mutation lease. Eligible hygiene SHALL occur only on an explicitly mutating path after both storage-root proof and mutation-lease acquisition.

#### Scenario: Aged temp fragments survive default discovery
- **WHEN** default discovery inspects a directory containing pending-prefix temp fragments older than the existing hygiene age limit
- **THEN** it leaves those fragments and all other evidence artifacts unchanged while reporting inspection as read-only

#### Scenario: CLI and in-process inspection preserve the same fixture tree
- **WHEN** listing, doctor, support export and launcher/count inspection run against verified isolated fixtures containing sources, ledgers, old/new temps, malformed and unrelated files
- **THEN** fixture file sets, bytes and last-write times are unchanged and no native recovery mutation is recorded

#### Scenario: Explicit hygiene retains its safety boundary
- **WHEN** an existing mutating startup or supervised recovery session performs eligible temp hygiene
- **THEN** it has already validated its storage root and acquired the existing mutation lease, deletes only policy-eligible temp fragments, and preserves authoritative pending/ledger and unrelated evidence

#### Scenario: Refused coordination does not authorize cleanup
- **WHEN** the root is unverified or the mutation lease is unavailable
- **THEN** no hygiene deletion occurs and read-only inspection does not attempt to bypass the refusal

### Requirement: Live logical export SHALL use immutable owner-thread observations

An in-process diagnostic export SHALL collect logical presentation state on its owning UI thread before background report/archive work. Once captured, that logical data SHALL be immutable with respect to subsequent live-window changes, and background work SHALL NOT read live controls or view models. Every available container observation SHALL be included; partial/unavailable collection SHALL be distinguished from a genuinely empty session with sanitized status in the same bundle. Collection SHALL remain side-effect-free.

#### Scenario: One live container produces one logical entry

- **WHEN** an in-process export has one available dispatcher-owned container
- **THEN** its bundle includes that container's group, active/split state and expected geometry without a cross-thread access failure or silently empty logical array

#### Scenario: A container disappears during collection

- **WHEN** a container cannot be observed during capture
- **THEN** other available observations remain included and the same bundle records sanitized partial/unavailable evidence rather than claiming an empty healthy session

#### Scenario: Live state changes after capture

- **WHEN** the captured container changes selection or closes while the archive is written
- **THEN** the archive contains the captured immutable logical values and never rereads the disposed/live UI object

### Requirement: Hotkey diagnostic export SHALL not block its UI dispatcher

The hotkey export SHALL perform only bounded logical collection on the UI dispatcher, then observe asynchronous report/archive completion or failure without synchronously waiting on worker I/O or blocking dispatcher round trips. UI-free CLI export without a live provider SHALL remain supported and SHALL identify the absence of in-process logical evidence honestly. Existing privacy redaction and no-native-mutation guarantees SHALL remain unchanged.

#### Scenario: Archive work is held while UI remains usable

- **WHEN** archive work is deliberately held on a test gate after logical capture
- **THEN** the hotkey operation yields to the UI dispatcher before that gate opens and eventual success/failure is observed

#### Scenario: CLI export has no live provider

- **WHEN** export runs as a diagnostic-only CLI process
- **THEN** it emits a valid sanitized report with explicit no-live-provider logical status without creating the main UI or invoking native presentation mutation

