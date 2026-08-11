# Agent state

## Current checkpoint — supervised UI/UX hardening follow-up (2026-08-11)

Objective: investigate the timing-sensitive post-caption-drag blanking found
with Computer Use, verify the close-group modal against real captured guests,
preserve the Shepherd/no-reparent architecture, and leave durable evidence.

Status: implementation and validation complete for the exercised scope.
Working tree is intentionally uncommitted on `main`; no push or PR was made.

## Completed

- Read the supplied goal prompt and the repository guidance, including the
  supervised-only real-input policy.
- Reproduced the post-drag covered/blank guest state twice with Chrome,
  File Explorer, and PredatorSense captured in one group. Added an explicit
  follow-up Render reconciliation for `WM_EXITSIZEMOVE` when a coalesced pass
  is already pending.
- Fixed the close-group modal z-order race: chrome layout/pairing is suppressed
  while the prompt is open; the container is temporarily raised; a one-shot
  dispatcher tick raises the native dialog; teardown restores normal z-order
  and reconciles the guest. No `SetParent` or guest style mutation was added.
- Final Computer Use evidence: `Close group` appeared above PredatorSense with
  Yes/No/Cancel usable; Escape restored the guest; a final bounded caption drag
  settled with the full guest visible.
- Updated the detailed waypoint at
  `docs/internal/ui-ux-stabilization-waypoint.md` with the implementation,
  retest, and remaining qualifications.

## Validation

- `dotnet build TabDock.csproj --no-restore`: PASS, 0 warnings / 0 errors.
- `dotnet build TabDock.sln --no-restore`: PASS, 0 warnings / 0 errors.
- GuineaPig and ValidationDriver builds: PASS, 0 warnings / 0 errors.
- `scripts/validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS.
- `openspec validate --all --no-interactive`: PASS, 12/12.
- `git diff --check`: PASS (only normal LF/CRLF conversion warnings).
- Native invariant audit found only existing positioning/event infrastructure;
  no new reparenting, style mutation, bottoming, or production sleep/polling.

## Remaining limits / next action

- No full supervised ValidationDriver real-input batch was started in this
  follow-up; the result is not a cross-machine monitor/DPI matrix and is not a
  claim of universal bug-freedom.
- Review the final diff and hand off the uncommitted changes. Do not commit or
  push unless the user explicitly requests it.
