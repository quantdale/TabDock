## ADDED Requirements

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
