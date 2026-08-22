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
