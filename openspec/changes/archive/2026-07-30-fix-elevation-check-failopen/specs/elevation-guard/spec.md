# elevation-guard delta — fix-elevation-check-failopen (new capability)

## ADDED Requirements

### Requirement: Elevation status of a capture target is determined tri-state
The elevation check used by `WindowShepherdService.Capture` SHALL distinguish three outcomes — target elevated, target not elevated, and check indeterminate (e.g. `OpenProcess`/`OpenProcessToken`/`GetTokenInformation` failure) — instead of collapsing check failure into "not elevated".

#### Scenario: A failed check is not treated as "not elevated"
- **WHEN** the token query for a capture target fails for any reason
- **THEN** the check reports "indeterminate", and this outcome is distinguishable at the call site from a successful "not elevated" result

### Requirement: Capture fails closed when elevation is indeterminate
`WindowShepherdService.Capture` SHALL refuse to capture a window whose elevation status is indeterminate, unless TabDock itself is running elevated, and SHALL log the underlying native error via `FormatLastError()`. A refused capture produces the same clear user-facing message class as the known-elevated refusal.

#### Scenario: Indeterminate elevation refuses capture with a visible diagnostic
- **WHEN** the user attempts to capture a window whose owning process's token cannot be queried, and TabDock is not elevated
- **THEN** the capture is rejected with a clear message, the native error is logged, and no positioning is attempted on the target window

### Requirement: Guest positioning failures are surfaced in the log
`SetWindowPos`/`ShowWindow`/`SetForegroundWindow` failures in `PositionAndShow`, `Hide`, and `Release` SHALL be logged with `FormatLastError()` at most once per window per session, so UIPI-blocked or dead-HWND positioning failures are diagnosable without adding per-mouse-tick cost to the hot drag path (PERF25-3 invariant).

#### Scenario: A UIPI-blocked positioning call appears exactly once in the log
- **WHEN** a captured guest's positioning calls fail repeatedly (e.g. the guest became elevated mid-capture)
- **THEN** the first failure is logged with the native error, subsequent identical failures for the same window are suppressed, and `SHEPHERD[position]` hot-path logging is unchanged
