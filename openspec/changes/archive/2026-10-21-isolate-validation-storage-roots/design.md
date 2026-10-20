## Context

See proposal.md and audit A02. Environment overrides do not control the Windows known-folder API used by persistence, journals, pending recovery, diagnostics, and logging. The current app constructor can start logging before Startup. Resolving only some service paths or verifying after rescue would be too late.

## Goals / Non-Goals

**Goals:** One immutable effective product directory, established and verified before any profile I/O or rescue. Reliable isolation proof from the real child bootstrap.

**Non-Goals:** No user-facing profile manager, weaker native identity checks, alternate mutation lease, or unattended SendInput.

## Decisions

1. Add a shared immutable storage-path value initialized at the earliest application bootstrap. Defaults remain the known roaming folder plus TabDock. The override names the complete product directory, not a second roaming folder with an implicit suffix. Explicit constructor paths remain available for unit fixtures.
2. Proposed qualification transport: `TABDOCK_VALIDATION_STATE_ROOT` plus `TABDOCK_VALIDATION_RUN_ID`, validated against a launcher-created marker inside a fresh run-owned directory. Require both fields, canonical absolute paths, matching marker/run identity, and no reparse-point components. Reject production product directories and aliases/overlap with them. Invalid overrides fail before even fallback logging; they never select the normal profile. This is an accidental-mutation guard, not authentication against another process owned by the same user.
3. Provide a qualification-only, read-only storage-probe command that runs the same resolver and reports normalized effective directory and run identity to the parent. The parent compares exact canonical values and the launched candidate identity before allowing a mutating child. The mutating bootstrap independently repeats validation to narrow probe/launch races. Keep the SID-scoped ProductMutationLease unchanged.
4. Route every default path consumer, including hygiene, diagnostics and pending recovery, through that value. Do not scatter environment reads among services. Replace only the APPDATA-isolated soak/smoke paths; preserve the established supervised driver snapshot/restore workflow.
5. Parent cleanup is restricted to its marker-bound temp tree and proven launched process. Never use a failed preflight to trigger real-profile cleanup. Retain privacy-safe root-proof status rather than personal paths in qualification manifests.

## Risks / Trade-offs

- Partial migration leaves an escape path → inventory all known-folder/path call sites and add child tests for every storage role.
- Marker/path replacement remains possible by same-user processes → revalidate on bootstrap and before cleanup; refuse on ambiguity.
- Probe exit success alone is weak evidence → require exact candidate/run/root matches and fail closed on missing output.

## Migration Plan

Introduce bootstrap/resolver and probe tests first, with sentinel fixture trees and no UI launch. Wire the two affected launchers only after refusal and root proof pass. Run full smoke only after A10, and normal UI soak only when its other opt-in safety prerequisites hold. No production files are migrated; reverting the launcher change must disable the affected soak/smoke, not restore APPDATA-only isolation.
