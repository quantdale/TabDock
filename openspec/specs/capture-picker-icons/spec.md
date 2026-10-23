# capture-picker-icons

## Purpose
Captures icon-caching behavior for the capture picker's `IconService`, ensuring icons for windows sharing an executable path are extracted at most once per process lifetime.
## Requirements
### Requirement: Icons are cached per executable path
`IconService` SHALL cache the resolved `ImageSource` icon for a given executable path after the first successful (or unsuccessful) extraction, keyed by that exe path, for the lifetime of the `IconService` instance. Subsequent requests for the same exe path SHALL be served from the cache instead of re-invoking `ExtractIconEx`/`CreateBitmapSourceFromHIcon` (`Services/IconService.cs:33-67`).

#### Scenario: Second window of the same executable reuses the cached icon
- **WHEN** `CapturePickerViewModel.Refresh` enumerates two or more top-level windows belonging to the same executable path (e.g. two browser windows)
- **THEN** `ExtractIconEx` is invoked at most once for that exe path during the enumeration, and every `WindowInfo` for that exe path receives the same cached `ImageSource`

#### Scenario: Different executables are cached independently
- **WHEN** the picker enumerates windows belonging to distinct executables
- **THEN** each distinct exe path gets its own independent cache entry and its own icon

#### Scenario: A failed extraction is cached to avoid repeated failed attempts
- **WHEN** icon extraction for a given exe path fails (e.g. the file is inaccessible) and that exe path is encountered again in the same or a later `Refresh()` call within the `IconService` instance's lifetime
- **THEN** the cached failure (no icon) is returned without re-invoking `ExtractIconEx` for that exe path

### Requirement: Cache is scoped to the IconService instance's process lifetime
The icon cache SHALL persist across multiple capture-picker opens/refreshes within the same running TabDock process (since `IconService` is constructed once at startup and lives for the process lifetime), and SHALL NOT be persisted to disk or survive a process restart.

#### Scenario: Cache persists across repeated picker opens in one session
- **WHEN** the user opens the capture picker, closes it, and reopens it later in the same TabDock session
- **THEN** exe paths already cached from the first open are served from cache on the second open without re-extraction

### Requirement: Uncached picker icons resolve lazily with refresh-generation safety

`CapturePickerViewModel.Refresh` SHALL add the complete filtered candidate set to
the observable collection without waiting for uncached icon extraction. Cached
icons SHALL be applied immediately; uncached icons MAY be resolved by a bounded
background worker and applied on the UI dispatcher only when the result belongs
to the current refresh generation and candidate row. Closing or refreshing the
picker SHALL invalidate older results without updating the current collection.

#### Scenario: Candidate rows appear before a cold icon finishes

- **WHEN** a candidate has an uncached executable icon
- **THEN** the candidate row is available after enumeration without waiting for
  icon extraction, and the row eventually receives its frozen icon or a null
  failure result

#### Scenario: Repeated executable paths share one extraction

- **WHEN** multiple candidate rows have the same uncached executable path
- **THEN** the icon service performs at most one concurrent extraction for that
  path and all matching rows receive the same cached result

#### Scenario: A stale refresh result is ignored

- **WHEN** refresh N starts icon work and refresh N+1 completes before that work
- **THEN** refresh N's result does not mutate rows belonging to refresh N+1

#### Scenario: Closing the picker invalidates icon work safely

- **WHEN** the picker closes while an icon is being resolved
- **THEN** the worker terminates or completes without throwing or updating a
  disposed picker

### Requirement: Large picker refreshes SHALL coalesce icon UI updates

Uncached icon extraction SHALL remain generation-safe and cancellation-safe, but
the picker SHALL coalesce result application onto a bounded number of dispatcher
callbacks rather than scheduling one UI callback per candidate row. Correctness
SHALL be expressed through generation/row ownership, not wall-clock thresholds.

#### Scenario: A thousand candidates do not create a thousand dispatcher posts

- **WHEN** a refresh resolves icons for a large synthetic candidate set
- **THEN** icon results are applied in coalesced batches, stale generations are
  ignored, and the candidate collection remains complete

#### Scenario: A superseded batch cannot overwrite the current refresh

- **WHEN** refresh N is canceled by refresh N+1 while its icon worker is active
- **THEN** no result from N mutates rows owned by N+1 and completion remains
  observable without a timing-based correctness assertion

### Requirement: Tab creation SHALL not wait for cosmetic icon resolution

Captured-tab insertion and group/view-model construction SHALL use completed cached icons or placeholders without waiting for in-flight extraction or performing uncached native extraction on the UI thread. Authoritative capture/membership work SHALL complete independently of cosmetic icon availability. Deferred icon results SHALL be frozen, applied only on the owning UI dispatcher, and bound to a still-current tab/request lifetime rather than a positional index.

#### Scenario: Capture finishes while a shared icon producer is held

- **WHEN** a picker worker is extracting an executable icon and its producer is deliberately held on a test gate
- **THEN** adding a captured tab for that executable completes with a cached icon or placeholder before the producer gate opens

#### Scenario: Existing populated group construction does not extract on the UI thread

- **WHEN** a group view model is constructed from existing live tab metadata with uncached executable paths
- **THEN** construction returns with complete tab models without waiting for cosmetic extraction, and current tabs receive deferred results on their owning dispatcher

#### Scenario: A removed or replaced tab rejects late results

- **WHEN** a tab is removed, insertion is rolled back, the owning view model detaches, or request identity changes before extraction completes
- **THEN** the stale result is not applied and the canceled consumer does not retain the obsolete UI lifetime

### Requirement: Deferred tab icons SHALL preserve bounded shared-cache semantics

Deferred picker/tab requests SHALL share at most one concurrent producer per case-insensitive executable path under bounded active-worker and queued-work limits. Actual extraction success or failure SHALL retain the existing instance-lifetime cache policy. Consumer cancellation or queue rejection SHALL NOT be recorded as an actual cached extraction failure, cancel another current consumer's producer, or block UI membership work.

#### Scenario: Picker and tab share one producer

- **WHEN** multiple picker rows and a captured tab request the same uncached path concurrently
- **THEN** one producer supplies the same completed cached result to all still-current consumers without UI-thread waits

#### Scenario: Saturation and cancellation stay cosmetic

- **WHEN** queued extraction work reaches its declared bound or a consumer is canceled
- **THEN** membership remains responsive, current shared consumers remain valid, and unexecuted work is not permanently cached as an extraction failure
