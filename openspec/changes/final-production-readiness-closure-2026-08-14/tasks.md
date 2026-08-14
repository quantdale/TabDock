## 1. Released close-target identity

- [x] 1.1 Add an immutable released-window close-target snapshot and explicit Match/Destroyed/Replaced/Unverifiable verifier using the existing strong native identity seam.
- [x] 1.2 Update the close-group Yes path to snapshot before release, fail closed on incomplete release/snapshot evidence, and post `WM_CLOSE` only after independent post-release verification.
- [x] 1.3 Add deterministic identity and close-group tests for matching fields, PID/thread/executable/class/process-start mismatches, unavailable probes, destroyed targets, and recycled HWNDs.

## 2. Recovery source and transaction durability

- [x] 2.1 Change successful pending-entry retirement to ledger-only logical resolution while unresolved siblings remain, deleting the source only after all entries are durably resolved and preserving unknown JSON fields.
- [x] 2.2 Add deterministic unique legacy transaction rebinding for changed source SHA/index and fail-closed ambiguity/foreign-token handling, including duplicate and three-sibling fixtures.
- [x] 2.3 Classify native-complete targets as exact, destroyed, positive replacement, or unverifiable; permit disk-only cleanup for destroyed/replaced targets and prove no native work repeats.
- [x] 2.4 Expand pending-recovery fault-injection tests across every durable phase, reverse/middle/duplicate sibling retirement, retirement retries, unknown fields, current/legacy evidence, foreign tokens, and native-complete reuse.

## 3. Persistence resilience

- [x] 3.1 Make root property classification case-insensitive and parse groups/tabs independently so null or malformed nested records are salvaged without discarding valid siblings/groups.
- [x] 3.2 Bound restored active indexes after salvage and log bounded semantic corruption without weakening unreadable, corrupt-primary, backup, or future-schema overwrite protection.
- [x] 3.3 Add persistence fixtures for null groups/tabs, malformed tabs, later valid groups, case variants, save-after-salvage, future state, unreadable primary, valid backup, and bounded active indexes.

## 4. Per-user mutation lease

- [x] 4.1 Derive a canonical `Global\\TabDock-<SID>` lease name from `WindowsIdentity.User`, reject unsafe/missing identity, and preserve same-user cross-session/abandoned-owner behavior.
- [x] 4.2 Update lease documentation/spec references and add seam tests for same/different SID names, unsafe values, exclusivity, release/reacquisition, abandoned ownership, and read-only independence.

## 5. Privacy and terminal safety

- [x] 5.1 Apply one bounded Unicode-safe terminal sanitizer to every untrusted supervised-recovery display field, including title, executable, class, candidate label, filename, and errors/status.
- [x] 5.2 Add adversarial terminal-output tests covering ESC/CSI/OSC, C0/C1, DEL, CR/LF/tab, Unicode separators, emoji, CJK, surrogate pairs, and maximum-length values.
- [x] 5.3 Update README/support guidance, architecture/testing docs, and release-positioning wording so sanitized `--support-bundle`/`--doctor` artifacts are primary and raw logs are explicitly sensitive.

## 6. Durable specifications and HDWP contract

- [x] 6.1 Validate the final-closure OpenSpec deltas and align durable agent state/execplan records with the implementation without self-referential SHA or CI claims.
- [x] 6.2 Review HDWP implementation/spec wording and preserve the documented residual check-to-commit race without changing the validated batch lifecycle.

## 7. Qualification and handoff

- [x] 7.1 Run targeted self-tests and affected Release builds, including supported close-group/exitpopulated real-input scenarios and isolated console qualification. (The guarded close-group scenarios were attempted but the environment blocked both at verified-foreground preflight before input.)
- [x] 7.2 Run the complete Release CI/publish/OpenSpec/NuGet/privacy/diff gates and explicit ValidationDriver/GuineaPig builds.
- [ ] 7.3 Review the complete diff and forbidden architectural calls, commit coherent closure content, fetch/push `main` normally, and verify the exact final GitHub Actions run until green without creating a release.
