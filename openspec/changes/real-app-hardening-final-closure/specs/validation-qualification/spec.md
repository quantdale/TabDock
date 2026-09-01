# validation-qualification

## MODIFIED Requirements

### Requirement: Real-app qualification SHALL be exact-candidate, supervised, and first-attempt authoritative

Every real-app scenario (Chromium/Notepad/Terminal) SHALL declare capability needs (guest family `browser` or `real-app`, `--guest` value, monitor/DPI, supervision, lease, destructive-state `ExternalBrowser`/`UserOwnedExternal`), SHALL run only after `DesktopQualificationLease` proves candidate/executable/driver/run/scenario/attempt, topology, effective DPI, identity, foreground, and point ownership, and SHALL preserve the first valid attempt across `--reruns` (a valid `FAIL_PRODUCT` followed by `PASS` is `FLAKE_UNCLASSIFIED`, never best-of-N PASS).

`FLAKE_UNCLASSIFIED` SHALL be a provisional investigation disposition, not a final closure disposition. A final closure SHALL record a defensible classification for every preserved first valid `FAIL_PRODUCT` (`PROVEN_PRODUCT_DEFECT`, `PROVEN_HARNESS_DEFECT`, `PROVEN_ENVIRONMENT_FAILURE`, `CHARACTERIZED_PRODUCT_FLAKE`, or `NOT_REPRODUCED_BUT_UNEXPLAINED`); `CHARACTERIZED_PRODUCT_FLAKE` and `NOT_REPRODUCED_BUT_UNEXPLAINED` SHALL leave the closure open. Fifteen later PASS cycles SHALL NOT erase a valid historical failure.

Synthetic fixtures SHALL NOT satisfy a real-app gate. The canonical final gates (`scripts/validate.ps1 -Configuration Release -Ci -Publish`, the explicit `--selftest-native-abi` ABI probe, and the deterministic resource-headless gate with the canonical seed/cycle count) SHALL be actually executed against the exact final candidate and their results recorded; an inferred or substituted subset SHALL NOT be recorded as completion.

#### Scenario: First valid real-app failure is retained
- **WHEN** attempt 1 of `browser-fullscreen-contained` is `FAIL_PRODUCT` and attempt 2 is `PASS`
- **THEN** the recorded disposition is `FLAKE_UNCLASSIFIED` with both run/packet hashes retained, not ordinary `PASS`

#### Scenario: Unexplained first failure blocks final closure
- **WHEN** an archive-bound campaign holds a preserved first valid `FAIL_PRODUCT` whose cause is not proven to be product, harness, or environment
- **THEN** the final closure remains open and the investigation names the classification gap rather than archiving a "final closure"

#### Scenario: Real-app cell blocks before input when harness proof is absent
- **WHEN** foreground/point ownership or candidate/lease cannot be proven for a real-app HWND
- **THEN** the scenario records `BLOCKED_ENVIRONMENT`/`BLOCKED_CAPABILITY` without sending input

#### Scenario: Canonical final gates are executed, not inferred
- **WHEN** a campaign claims its final validation task complete
- **THEN** `validate.ps1 -Configuration Release -Ci -Publish`, the native ABI probe, and the resource-headless gate have actually run against the exact final candidate with recorded exit codes and evidence