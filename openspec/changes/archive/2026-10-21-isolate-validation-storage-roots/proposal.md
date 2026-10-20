## Why

Audit finding A02 proves that setting child `APPDATA` does not redirect Windows `Environment.GetFolderPath(ApplicationData)`. The current resource soak and redirected recovery smoke can therefore read, clean, rescue from, or save the real user's TabDock storage while reporting isolation.

## What Changes

- Introduce one immutable, explicitly selected validation storage root resolved before logger or mutating-service construction.
- Require a run-owned marker and a read-only child preflight proving the effective product directory before any mutating test child starts.
- Route state, backups, journals, pending recovery, logs, startup hygiene, and diagnostic inspection through the same resolved root.
- Fail closed for malformed, unsafe, unavailable, or unverified overrides; never fall back to production storage.
- Replace APPDATA-only isolation in the soak and recovery smoke without weakening the SID-scoped mutation lease or process/input guards.

## Capabilities

### New Capabilities

- `validation-storage-isolation`: explicit, verified, fail-closed storage-root selection for qualification children.

### Modified Capabilities

- `test-tooling-safety`: require child-observed root proof and run-owned cleanup for process qualification.

## Impact

Affects application bootstrap, storage/diagnostic services, the ValidationDriver process soak, and `scripts/validate.ps1`. Production startup without a validation override keeps the Windows profile default. Adds qualification-only bootstrap inputs/probe, not a supported profile-switching feature. Source evidence: audit A02.

## Non-goals

No new lease namespace, concurrent production/validation mutation owners, migration of user files, automatic cleanup of unrelated roots, or removal of physical-input supervision. Planning only.

## Sequencing

Safety prerequisite for future process/ZIP regression runs across these proposals. Coordinate with `keep-recovery-discovery-read-only` before running the full canonical gate. Validate root refusal using probe-only child processes before any application/soak launch.
