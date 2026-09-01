# native-window-identity

## ADDED Requirements

### Requirement: Real-app ownership SHALL distinguish run-owned from adopted

Before any real-app mutation, the harness SHALL resolve run-owned versus adopted:

- `SpawnedGuest` path (isolated profile `FreshProfileDir`, `SpawnBrowserGuest`, `SpawnClassGuest`) records expected PID, HWND, thread, class, exe path, and process-start ticks in `TestRunProvenance`; `GuestInfo.DoNotKill` is set for adopted-discovery paths.
- `AdoptedExternal` path (existing Notepad broker surface or existing Terminal host) discovers live HWND via `EnumWindows` filters and proves executable, PID/start, owner/root, class, and generation without claiming cleanup ownership.
- Identity is never by title text; cleanup never targets a foreign PID/start.

#### Scenario: Adopted app is never killed by cleanup
- **WHEN** a scenario adopts a live Notepad or Terminal HWND whose PID/start differs from any spawned process
- **THEN** `Ctx.Cleanup` and `GuardedProc` prove the foreign PID remains alive after release, and no `WM_CLOSE`/terminate targets that PID

#### Scenario: Same-HWND recycled generation is rejected
- **WHEN** a real-app HWND is destroyed and Windows reuses the value for another window in the same PID
- **THEN** the per-capture HWND token and `IsCurrentCapturedWindow`/`IsCurrentMutationGeneration` (HWND, PID, TID, class, token) refuse the stale generation

### Requirement: Real-app capture SHALL prove strong identity before mutation

Capture, hide, release, foreground handoff, and recovery for real apps SHALL require the live HWND to match captured PID, thread, class, executable where applicable, process-start, and HWND token. Missing or unverifiable required probes SHALL be `Unverifiable` and SHALL NOT be treated as `Mismatch`; no native mutation SHALL occur and the member/journal SHALL be retained.

#### Scenario: Process-start unavailable fails closed
- **WHEN** the Notepad broker's process-start cannot be read (access denied or race)
- **THEN** capture is refused, no `SetProp`/DWM/presentation mutation occurs, and the result is `BLOCKED_ENVIRONMENT`/`FAIL_HARNESS`, not a silent product pass
