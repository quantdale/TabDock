## ADDED Requirements

### Requirement: Release-candidate UI changes SHALL have honest supervised coverage

The ValidationDriver inventory SHALL include redesigned launcher, picker,
keyboard, tab, split, layout, and recovery workflows where the environment can
run them safely. Scenarios requiring an exclusive interactive desktop, physical
multi-monitor topology, mixed DPI, or real external applications SHALL be
reported as `BLOCKED_SUPERVISED` or `BLOCKED_ENVIRONMENT` with exact rerun
commands rather than fabricated passes.

#### Scenario: The redesigned picker has a supervised workflow

- **WHEN** an eligible desktop runs the driver against the release candidate
- **THEN** first launch, inline/standalone capture, search, multi-select,
  refresh continuity, add, and blocked-admission states are exercised

#### Scenario: Keyboard-only and split workflows are represented

- **WHEN** the driver inventory is listed or executed
- **THEN** keyboard-only launcher/picker operation, tab navigation, split
  LEFT/RIGHT focus, dormant resume, reorder, pop-out, and maximize/restore are
  present as executable scenario names or explicit environment-blocked entries

#### Scenario: Missing desktop prerequisites remain visible

- **WHEN** real-input or topology prerequisites are unavailable
- **THEN** the run records the blocked status and exact command without marking
  the unexecuted native behavior as passed
