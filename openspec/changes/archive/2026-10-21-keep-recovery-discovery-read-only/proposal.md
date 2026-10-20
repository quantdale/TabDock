## Why

Audit finding A10 creates an aged pending-prefix temp file and calls discovery that reports `readOnly: true`; the file is deleted. Default discovery hygiene leaks mutation into doctor, support export, and pending-recovery inspection without the product mutation lease.

## What Changes

- Make pending-evidence discovery non-mutating by default and for every read-only caller.
- Move eligible stale-temp hygiene into an explicitly mutating, lease-held recovery/startup path.
- Preserve temporary files, pending sources, ledgers, and transaction evidence during inspection, independent of age.
- Add complete before/after directory and native-call assertions for CLI, bundle, and launcher inspection paths.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `production-diagnostics`: require default discovery and all read-only diagnostics to preserve recovery storage artifacts, including stale temp fragments.

## Impact

Affects PendingRecoveryService discovery/hygiene boundaries, diagnostics callers, and recovery/diagnostic tests. Existing pending parsing, supervised confirmation, and generation/identity gates remain unchanged. Source evidence: audit A10; the observed deletion targets temp fragments, not proof of lost authoritative pending journals.

## Non-goals

Do not delete additional evidence types, broaden automatic native recovery, change sidecar retirement policy, or introduce a maintenance background loop. Planning only.

## Sequencing

Independent safety fix; apply alongside `isolate-validation-storage-roots` before the full test/CLI/export gate. Read-only discovery regressions can run immediately against explicit disposable directories. Coordinate shared discovery call sites with A02 without treating environment overrides as isolation.
