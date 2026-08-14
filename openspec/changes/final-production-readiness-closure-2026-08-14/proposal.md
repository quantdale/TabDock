## Why

The final production-readiness review found several source-level safety gaps
that remain in otherwise hardened Shepherd, persistence, and supervised
recovery paths. This closure pass is needed before release qualification so a
close-group confirmation cannot lose its close action, recovery evidence does
not invalidate unresolved siblings, malformed persisted records cannot erase
valid intent, and product mutation ownership/privacy contracts match the
per-user Windows desktop product.

## What Changes

- Snapshot and independently revalidate released native window identity before
  posting `WM_CLOSE`; never use the live capture registry after release and
  never close a recycled or unverifiable target.
- Keep pending recovery source bytes immutable while unresolved siblings remain;
  record logical retirement in the durable sidecar ledger and delete the source
  only after every entry is durably resolved.
- Add deterministic, fail-closed compatibility rebinding for uniquely provable
  transactions from the older rewritten-source implementation, while leaving
  ambiguous or foreign evidence untouched.
- Classify completed-recovery targets as exact match, positive replacement,
  destroyed, or unverifiable so durable native completion permits disk-only
  cleanup without touching a replacement or repeating native work.
- Salvage valid persisted groups/tabs around null or malformed nested records,
  preserve overwrite protection, bound restored active indexes, and make manual
  root-property classification case-insensitive like deserialization.
- Scope the product mutation mutex to the current Windows user SID in the
  `Global` namespace, fail closed if the SID cannot be established, and retain
  same-user cross-session and abandoned-owner semantics.
- Apply one bounded terminal-display sanitizer to every externally derived
  supervised-recovery field, preserving ordinary Unicode while neutralizing
  terminal controls and line separators.
- Make sanitized doctor/support-bundle output the primary support artifact,
  label raw logs as potentially sensitive, and replace prototype-only README
  positioning with an ordinary open-source disclaimer.
- Document the existing HDWP check-to-commit residual race accurately without
  weakening the validated Win32 batch contract.

## Capabilities

### New Capabilities

- `close-group-release-and-close`: Safe two-phase close-group Yes behavior and
  released-window identity verification.
- `product-mutation-lease`: Per-user, cross-session mutation exclusion and
  fail-closed user-identity derivation.
- `recovery-identity-reconciliation`: Completed-recovery target classification
  and disk-only cleanup safety.

### Modified Capabilities

- `hidden-window-journal`: Immutable unresolved pending sources, ledger-backed
  logical retirement, sibling durability, and deterministic compatibility
  rebinding.
- `persistence-resilience`: Salvage of malformed nested records, bounded
  active-index restoration, and case-insensitive root classification.
- `crash-shutdown-coherence`: Close-group Yes releases safely, then closes only
  independently verified released targets.
- `diagnostics-logging`: All supervised recovery candidate fields are safe for
  interactive terminals and support guidance names sanitized artifacts first.
- `ui-ux-hardening`: The documented deferred-positioning residual race is
  explicit and is not represented as an impossible atomic identity guarantee.

## Impact

Affected code includes `Views/ContainerWindow.xaml.cs`,
`Services/WindowShepherdService.cs`, `Services/WindowIdentityGate.cs`,
`Services/PendingRecoveryService.cs` and its self-tests,
`Services/PersistenceService.cs` and its self-tests,
`Services/ProductMutationLease.cs` and its self-tests, models, diagnostics,
README/docs, and the new OpenSpec delta artifacts. No third-party runtime
dependency or native reparenting mechanism is introduced; the existing
Shepherd, journal, support-privacy, and validation architecture remains in
force.
