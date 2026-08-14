## Context

The current implementation already has strong captured-window identity gates,
durable recovery phases, atomic JSON writes, and deterministic self-test seams.
The remaining defects occur at boundaries where those protections are currently
discarded: the close prompt retains a live capture object after release,
pending-entry retirement rewrites sibling indexes, nested persistence errors
escape at group scope, the product mutex has no user scope, completed recovery
does not distinguish replacement from uncertainty, and supervised output
sanitizes only titles.

## Goals / Non-Goals

**Goals:**

- Preserve exact release-before-close semantics while carrying an independent,
  strong enough native identity snapshot across the release boundary.
- Make the recovery source/ledger protocol stable under sibling retirement,
  duplicate records, process restarts, hard-kill phase interruptions, and
  legacy evidence produced by the previous rewriting implementation.
- Salvage valid persistence intent around malformed nested records without
  weakening unreadable, syntactically corrupt, or future-schema overwrite
  protection.
- Establish a canonical SID-derived `Global` lease name and fail closed when
  Windows user identity is unavailable.
- Keep native-complete cleanup disk-only for destroyed/positively replaced
  targets, and terminal output bounded and control-free.

**Non-Goals:**

- Reparenting, restyling, foreground-policy workarounds, or changes to the
  Shepherd architecture.
- An unsupported HDWP cancellation mechanism or a claim of atomic identity
  verification across the Win32 check-to-commit interval.
- Automatic recovery of ambiguous legacy evidence or removal of foreign native
  properties.
- A new third-party runtime dependency for mutex ACLs; the implementation will
  assess the .NET 8 ACL surface and keep the existing dependency-free product
  unless a framework-supported, proportionate option is available.

## Decisions

### Released close targets use an immutable snapshot and a no-registry verifier

Add a small released-target value containing HWND, PID, GUI thread, executable
path, class, and nonzero process-start identity. `WindowShepherdService` takes
the snapshot only after the existing strong captured identity gate succeeds.
After `CloseGroup` completes, a verifier probes the native fields directly and
returns `Match`, `Destroyed`, `Replaced`, or `Unverifiable`; it does not consult
the captured-object binding or the removed capture token. Only `Match` reaches
`PostMessage(WM_CLOSE)`. A target whose capture token unexpectedly remains is
not treated as an exact released target.

This keeps the established release transaction and its crash evidence intact.
It also avoids moving `WM_CLOSE` earlier, which would change user-visible
release semantics and could close an app before its presentation is restored.
The stable fields are the same bounded strong identity tier already used for
destructive Shepherd work. As with all external HWND identity, a last native
check-to-call race remains an ordinary Win32 limitation and is handled
fail-closed where evidence is unavailable.

### Pending sources are immutable until the file is fully resolved

`MarkResolved` remains the durable ledger commit. `RetireEntry` will verify the
source bytes and ledger, but will not remove an array element when any source
sibling lacks a resolution marker. Discovery uses exact source SHA, fingerprint,
and positional index for current files, so duplicate byte-identical entries are
distinct logical records. Once every current entry is durably resolved, the
source file is deleted as a unit; the `.recovered` ledger remains as audit
evidence and is ignored by pending-file enumeration.

For prior rewritten-source evidence, discovery first tries the exact binding.
If the SHA changed, it may rebind one non-retired transaction when exactly one
current entry has the fingerprint and exactly one ledger transaction can own
that fingerprint for the source file. The transaction is updated to the
current SHA/index on the next durable phase write. Multiple candidate entries,
multiple unresolved transactions, or a foreign source/file identity produce an
explicit unverifiable status. Legacy resolution markers with no source binding
may use the existing unique-fingerprint compatibility rule; non-empty old-SHA
markers are not applied to a changed source merely by fingerprint, preventing a
duplicate survivor from being mistaken for the retired record.

### Completed recovery has an explicit identity outcome

Replace the boolean live-target check in completed reconciliation with a
tri-state-plus-destroyed classifier. A missing HWND is safe because its
properties died with it. A positive PID/thread/executable/class/process-start
mismatch is safe disk-only replacement evidence; no property on that HWND is
removed. A complete match allows removal only of the exact recorded recovery
token. Any unreadable required probe retains the ledger and skips all native
work. All paths at or beyond `NativeRecoveryComplete` bypass placement,
visibility, and DWM calls.

### Persistence uses tolerant per-record parsing

Manual root probing will enumerate properties case-insensitively. Root schema,
future-version, and syntax policy remains unchanged. For a structurally valid
root/groups array, group and tab records are parsed independently from their
JSON elements. Null or malformed tabs are logged with a bounded, non-sensitive
record description and skipped; valid siblings and later groups are retained.
Restored active indexes are clamped to the salvaged tab count. A later save
therefore writes a coherent state containing all known-valid intent, while the
existing unreadable/future/corrupt-primary gates still prevent an empty
overwrite.

### The lease uses canonical SID scope without adding an unreviewed package

Read the current `WindowsIdentity.User` SID, canonicalize it through
`SecurityIdentifier`, and construct `Global\\TabDock-<sid>`. Same-user
processes across sessions therefore share one name, while different SIDs do
not. Missing or malformed identity returns acquisition failure. The .NET 8
reference surface does not include the newer `MutexAcl` helper without an
additional package; introducing that dependency solely for ACL construction
would expand the product dependency/audit surface. The closure therefore uses
SID scoping as the required boundary, documents that Windows default named
object ACL behavior remains an OS-level consideration, and tests unsafe-name
rejection and user isolation. The existing normal WaitOne/abandoned-owner
semantics remain unchanged.

### One sanitizer owns supervised terminal display

Create one bounded display sanitizer that iterates Unicode code points without
splitting surrogate pairs, replaces C0/C1/ESC/DEL and line separators, and
preserves ordinary Unicode. Apply it to title, executable filename, class,
candidate label, pending filename, status/error text, and discovery output.
The support-bundle/doctor sanitization pipeline is unchanged; this is only the
interactive local-console boundary.

### HDWP remains the documented Win32 contract

Keep the existing per-guest validation, returned-HDWP chaining, no-End path on
native Defer failure, and valid-batch close on a later generation failure. Add
the residual check-to-commit race to the delta documentation and tests rather
than introducing an unsupported cancellation or visible split tearing.

## Risks / Trade-offs

- **[Risk]** A released HWND can be destroyed between verification and
  `PostMessage`. → **Mitigation:** this is an ordinary final Win32 race; the
  verifier never uses the old capture registry, and all available identity
  evidence is checked immediately before the post.
- **[Risk]** Legacy rewritten files may lack enough information to map a token
  to one sibling. → **Mitigation:** only unique fingerprint/transaction
  evidence is rebound; ambiguity retains evidence and never removes a token.
- **[Risk]** Immutable sources remain on disk longer after one entry resolves.
  → **Mitigation:** the sidecar filters resolved entries immediately, and the
  source is removed atomically as a unit once all entries are durably resolved.
- **[Risk]** Salvage discards malformed nested records. → **Mitigation:** valid
  siblings are retained, the condition is logged, the old primary is copied as
  the next-save backup, and unreadable/future/syntax-corrupt files remain
  fail-closed.
- **[Risk]** SID-derived names reduce but do not replace a full ACL boundary.
  → **Mitigation:** no raw username fallback is allowed; the decision and
  official .NET mutex contract are documented, and a future ACL package can be
  evaluated independently without changing the lease semantics.

## Migration Plan

1. Existing v1/v2/v3 pending journals continue through the current discovery
   and supervised flows; new resolutions stop rewriting unresolved source
   arrays.
2. On an old rewritten file, unique transaction evidence is rebound only at a
   durable transaction phase; ambiguous records remain visible for supervised
   handling.
3. After all entries in a source are logically resolved, the source is deleted
   as one unit and the sidecar ledger is retained for audit/retry evidence.
4. No release tag or binary artifact is created by this change. The next
   release build must be made from the final pushed source SHA.
