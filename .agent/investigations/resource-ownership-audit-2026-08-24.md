# Resource ownership audit — 2026-08-24

## Scope and conclusion

This audit covers the native/UI/process/file lifetimes most likely to be
missed by ordinary functional scenarios. The source was inspected before
adding resource assertions. The audit found explicit cleanup contracts in the
production paths that were checked; it did not reproduce a production leak in
this pass. The principal gap was observability: the existing torture paths did
not retain a machine-readable resource series. The new validation-side probe,
series analyzer, lifecycle profiles, and artifact writer address that gap
without adding a production sampler or a second native presentation backend.

## Ownership matrix

| Resource | Acquire/create site | Owner and normal release | Failure/shutdown release | Repeated-use behavior | Existing coverage | Risk/gap |
| --- | --- | --- | --- | --- | --- | --- |
| WinEvent hook | `Services/WinEventMonitor.cs` `SetWinEventHook` | `WinEventMonitor`; `Stop` calls `UnhookWinEvent` and clears the active hook/context | `Dispose` calls idempotent stop; callback dispatch fails closed after teardown | Start is bounded and does not install a second live hook | WinEvent routing/replay and monitor lifecycle tests | Native hook count was not directly measured; new fake lifecycle profile plus process resource series cover the boundary |
| Global/local hotkeys | `Services/HotkeyService.cs` `RegisterHotKey` | `HotkeyService`; `Detach` unregisters each registered ID and removes the HwndSource hook | `Dispose`/window close are idempotent; partial registration failure rolls back | Reattach is guarded; no repeated registration loop | Hotkey policy and close/reopen tests | Registration is observable by explicit seam assertions, not a portable OS counter |
| HwndSource hook | `HotkeyService`, `Views/ContainerWindow.xaml.cs` | Owning service/window removes hooks before source/window disposal | Close path removes hook even when view-model teardown fails | One source per owner; no repair loop | Window close and hotkey tests | Retention is localized; resource gate checks window/handle plateau |
| Extracted HICON handles | `Services/IconService.cs` `ExtractIconEx` | `IconService`; both small and large handles are destroyed in `finally` | `finally` covers conversion/freeze and exceptions | Cache is bounded to the process lifetime; frozen `ImageSource` has no native handle ownership | Icon extraction/fallback tests | Existing source fix is explicit; picker/icon churn now exercises generation and release semantics |
| WPF icon `ImageSource` | `IconService` conversion | Frozen image is retained only by the bounded icon cache/row | Cache and picker row references are released by generation replacement/VM disposal | Same executable key reuses the cache entry | Icon service tests | No per-image `Dispose` exists; native ownership must remain at HICON boundary |
| Picker refresh CTS/tasks | `ViewModels/CapturePickerViewModel.cs` refresh generation and `CancellationTokenSource` | Picker VM cancels/disposes previous CTS and generation-gates results | `Dispose` cancels/disposes CTS and detaches manager events | Superseded refreshes cannot repopulate current rows | Picker generation/cancellation tests | Async retention is difficult to inspect directly; headless picker/icon profile covers repeated supersession |
| Picker row event subscriptions | `CapturePickerViewModel` subscribes to `WindowInfo.PropertyChanged` | Row lifetime is bounded by the picker VM collection; collection replacement drops row references | VM `Dispose` clears rows and detaches manager subscription | Refresh replaces rows; close releases VM | Picker close/selection tests | New close/reopen churn validates no stale row population; no production change made |
| Group/tab event subscriptions | `ViewModels/GroupViewModel.cs` and group/tab events | Group VM `Detach` removes group, manager, tab, and per-tab handlers | Close/release paths call detach before group disposal | Membership changes use the existing authoritative manager | Group lifecycle and close tests | No parallel stress-only membership state is introduced |
| Dispatcher timers | `GuestLifecycleService`, container coalesced/minimize timers, app retry timer | Creating owner stops/removes its timer during detach/close | App exit stops retry timer; container close disarms layout timers | Timers are per lifecycle owner and are not recreated by repair polling | Minimize/restore and teardown tests | Timer count is not directly enumerated; thread/window/resource series and deterministic profile provide bounded evidence |
| Logger stream/writer/queue | `Services/LoggingService.cs` | Logger owns bounded tail, queue, writer, and file stream | `Dispose` flushes/closes all; rotation closes prior stream | Tail is fixed-capacity and rotation is bounded | Logging and diagnostic tests | Support-artifact churn adds repeated close/size assertions |
| Diagnostic trace | `DiagnosticTrace` ring buffer | Fixed-capacity ring owns only recent records | Clear/final snapshot returns bounded state | Writes wrap at a fixed limit | Trace bound/privacy tests | New diagnostics profile repeats fill/wrap/clear |
| Support ZIP streams | `DiagnosticReportService` ZIP/FileStream/StreamWriter | Report operation owns all `using` scopes | Temporary/final move cleanup is attempted on failure | Each report starts from an isolated output path | Support-bundle/privacy validation | New diagnostics profile covers repeated isolated generation semantics; no sensitive fields added |
| Persistence streams and temp files | `PersistenceService` atomic write/backup paths | Operation owns each `FileStream`/writer; temp path is moved into primary | `using` closes streams; stale temp/sidecar cleanup follows existing recovery policy | Writes replace bounded primary/backup generations | Persistence 1000-save and stale-temp tests | New persistence profile checks temp absence and primary+backup bound in an isolated root |
| Recovery journal/sidecars | Recovery services and `RecoveryJournal` | Persistence/recovery authority owns journal, pending sidecars, and compaction | Replay/retirement/cleanup paths remove completed residue | Generation/token rules prevent stale replay | Recovery and redirected smoke suites | Resource campaign does not alter durability or identity rules; profile only uses isolated fixtures |
| Process objects/handles in driver | `GuardedProc`, `ResourceSnapshotProbe`, `Process` APIs | Driver owns spawned process wrappers; probe closes native process/snapshot handles | Guarded cleanup kills only provenance-owned children; probe `finally` closes handles | Spawn cap and per-run cleanup bound repeated use | Existing guarded-spawn tests | The new resource runner disposes its target wrapper and avoids leaking `GetProcessesByName` wrappers |
| Process/thread/window enumeration handles | `ResourceSnapshotProbe` Toolhelp and `EnumWindows` | Probe owns snapshot/process handles for one sample | `finally` closes every acquired native handle | Each sample is read-only and independent | New probe code + analyzer tests | Missing field/error blocks; no titles/paths/commands are collected |
| Container HWNDs | `WindowShepherdService`, `ContainerWindow` | Shepherd/window owner tracks and closes container HWNDs; guests stay top-level | Window close releases presentation and unregisters hooks; app exit emergency release remains | Repeated capture uses existing group/shepherd authority | Existing capture/close/torture tests | New live process mode observes count only; it never mutates guest HWNDs |
| App-level services | `App.xaml.cs` | App owns WinEvent, hotkeys, lease, logger, and exit handlers for process lifetime | Exit handler performs emergency release/save/flush and disposes services | Intentional process lifetime is explicit, not a leak by itself | App exit/recovery/diagnostic tests | Resource campaign does not force premature disposal of process-lifetime services |

## Decisions and follow-ups

* Keep all measurement and Windows resource P/Invoke in the ValidationDriver.
  Production startup acquires no sampler and no additional native authority.
* Treat a missing metric, probe error, invalid ordering, counter reset, or
  process-generation change as `BLOCKED`, never as zero or `PASS`.
* Use strict small budgets for handles/USER/GDI/threads/windows and larger,
  documented budgets for byte counters. Warm-up is excluded only by the
  explicit analyzer configuration; settled tail slope remains checked.
* Keep physical-input scenarios supervised. The headless profiles exercise
  existing pure policy/state seams and isolated files only; they do not satisfy
  physical, mixed-DPI, OS-version, signing, or human-smoke gates.
* If a live run reproduces growth, reduce it to the owning production seam and
  add a focused regression before changing any ownership code. No production
  lifecycle fix was justified by the current baseline evidence.
