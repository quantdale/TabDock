# TabDock runtime audit — a422785

Baseline audited: `a422785960c903b7ef00d6329675ddc5ec3cec11`

This pass deliberately prioritizes the real WPF/native runtime path over the
release-control and deterministic policy models. The repository is heavily
hardened on paper, but several of those abstractions are not actually the
authority used by `ContainerWindow` at runtime.

## Highest-confidence findings

### P0 — split non-member activation still depends on a fragile two-handler route

The ordinary `TabsListBox_PreviewMouseLeftButtonDown` marks a non-member click
handled while a split is presented. A second handler is then added at
`OnContentRendered` with `handledEventsToo=true` and performs another hit-test
to recover the click.

This creates an unnecessary ordering dependency between two handlers on the same
routed event. The deterministic `SplitInteractionPolicy` suite is not the code
actually executing that WPF transition.

Hotfix: wire the split interaction handler directly in XAML, which registers it
during `InitializeComponent`, before the ordinary drag/selection handler added
later in code. Remove the handledEventsToo recovery registration. There is now
one pair->C/D transaction per click.

### P0/P1 — hot tab switches force synchronous durable disk I/O

`GroupManager.SwitchActiveTab` immediately calls `RequestDurableSave`, which
serializes state and writes it synchronously.

More seriously, every `WindowShepherdService.Hide` calls `JournalHide`, even
though every successful capture already synchronously committed the complete
capture-session recovery entry before the first presentation mutation. The
journal writer is deliberately durable.

A normal A->B switch therefore enters durable storage on the UI/input path. A
split A/B->C transition hides two members before showing C and can pay the
journal cost twice plus the state-file save.

Hotfix:
- active selection becomes debounced persistence and repeated same-index
  activation becomes a no-op;
- capture generations whose rescue entry is already known durable skip the
  redundant `JournalHide` rewrite;
- an intentional-hide journal marker invalidates that known-durable-rescue flag,
  so a retained capture must re-establish rescue intent before a later hide.

The initial capture/release safety boundary stays synchronous.

### P1 — split z-order health check requires strict HWND adjacency

The split layout fast path currently considers the pair healthy only when
`GetWindow(top, GW_HWNDNEXT) == bottom`.

That is stronger than the visual invariant. IME windows, shell helpers,
accessibility overlays, GPU helpers, and unrelated top-level HWNDs can exist
between them. If one does, every relayout can issue another full deferred
position batch even when both guest rectangles are already correct.

Hotfix: test relative ordering (`top` occurs somewhere above `bottom`) and pin
the container behind the bottom member. Do not continuously fight unrelated
HWNDs for adjacency.

### P1 — `ensureFinalPass` always causes two frames when called while idle

Both the live `ContainerWindow.RequestRelayout` and
`PresentationLayoutCoordinator.RequestRelayout` set the after-pending latch
before checking whether a render is already pending.

Thus `RequestRelayout(ensureFinalPass: true)` while idle schedules the requested
pass and then unconditionally schedules a second pass. The latch is only needed
when a pass was already pending.

Hotfix: latch only in the already-pending branch.

### P1 — `LayoutUpdated` is used as an unconditional render scheduler

The window subscribes `LayoutUpdated += (_, _) => RequestRelayout()`.
`LayoutUpdated` fires for unrelated WPF layout work such as tab-strip changes.

Even though later geometry guards can skip native calls, this still queues
Render-priority work and amplifies the event graph.

Hotfix: cache the physical `ContentHost` screen rectangle and request relayout
only when that rectangle actually changes.

### P2 — child marker resize forces `SWP_FRAMECHANGED`

`NativeHwndHost` is a plain child HWND used as a geometry marker. Its
`ArrangeOverride` resize includes `SWP_FRAMECHANGED`, which requests non-client
frame recalculation that this marker does not need.

Hotfix: remove `SWP_FRAMECHANGED`.

## Structural finding: green tests are validating a parallel model

`SplitPresentationController`, `SplitInteractionPolicy`, and
`PresentationLayoutCoordinator` were introduced as hardening seams, but the
real `ContainerWindow` still owns duplicated split state, duplicated relayout
state, WPF routed-event behavior, and most native transition ordering.

The unit tests are valuable, but until these controllers become the actual
runtime authority, a large class of desktop regressions can remain invisible to
them. A second-stage agent pass should wire the runtime through those seams or
delete the dead parallel abstractions.

## Additional repository-wide risks for stage 2

1. Move ordinary `state.json` persistence to a single background writer. Build
   an immutable DTO snapshot on the dispatcher, coalesce generations, and write
   the latest snapshot off-thread. Keep the recovery journal separate and
   synchronous only at true safety boundaries.
2. Coalesce duplicate foreground/reorder WinEvents per HWND per dispatcher turn.
3. Gate or coalesce high-frequency `DiagnosticRuntime.Record` events during
   resize/foreground storms.
4. Instrument the actual runtime path with timing counters:
   input-down -> pair hidden -> target shown -> foreground requested.
5. Examine `WM_WINDOWPOSCHANGING` as an earlier guest re-glue signal. The current
   `WM_WINDOWPOSCHANGED` path runs after the WPF top-level window already moved,
   and the Shepherd architecture uses separate DWM top-level surfaces, which
   imposes a real synchronization ceiling during drag.
6. If measured DWM tearing remains unacceptable after duplicate work is removed,
   explicitly evaluate the architecture ceiling instead of adding more
   callbacks. Separate top-level HWNDs cannot be made identical to a single
   compositor surface by adding more `SetWindowPos` calls.

## Validation required after applying the hotfix

- Release build and full unit suite.
- Existing hermetic `validate.ps1 -Ci -Publish`.
- Four-tab real desktop scenario: split A/B, click C, resume pair, click D,
  repeat >= 50 cycles.
- Record input-to-presentation latency and native operation counts.
- Chrome + Edge + Explorer/Notepad mix.
- IME/overlay present while split, verifying no repeated deferred batch storm.
- Drag/resize container while watching for repeated identical SetWindowPos
  operations.
- Force-kill recovery after a background tab has been hidden repeatedly, proving
  the capture-time durable journal still rescues it.
