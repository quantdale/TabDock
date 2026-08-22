## ADDED Requirements

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
