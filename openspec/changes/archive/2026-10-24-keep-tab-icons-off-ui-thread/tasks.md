## 1. Prove membership is independent of cosmetic work

- [x] 1.1 Add an actual IconService/GroupViewModel test that holds a shared picker producer on a gate and requires AddCapturedWindow to complete before that gate opens; retain elapsed time only as supplementary evidence.
- [x] 1.2 Add existing-live-member construction, cached success/null, extraction failure, delayed dispatch, removal/rollback/Detach and request-replacement fixtures using explicit temp storage and an STA dispatcher.

## 2. Provide bounded shared async icon delivery

- [x] 2.1 Expose asynchronous case-insensitive single-flight icon lookup with two active native producers and a finite queued-unique-path cap, preserving completed/failure caching.
- [x] 2.2 Keep consumer cancellation/queue rejection separate from cached extraction failure; ensure they do not cancel shared producers still needed by current consumers.
- [x] 2.3 Switch group/tab construction and capture completion to completed-cache lookup plus placeholder, followed by asynchronous current-lifetime icon projection; no UI extraction or wait adapter calls.
- [x] 2.4 Apply only frozen results on the owning dispatcher by current tab/request reference, invalidate consumer lifetimes on removal/rollback/Detach and avoid retaining closed UI while native work continues.
- [x] 2.5 Reuse the shared async path for picker work without regressing bounded batched dispatch, refresh-generation rejection or completion observability.

## 3. Verify bounds, safety and existing cache contracts

- [x] 3.1 Test shared picker/tab deduplication, worker/queue saturation, consumer cancellation, frozen-image delivery and retry after noncached admission rejection.
- [x] 3.2 Run `dotnet build TabDock.sln` and targeted tab-icon, picker-icon, mutation/durability and weak-reference lifecycle tests without production storage or native guest capture.
- [x] 3.3 Verify icon failure cannot roll back an admitted capture or change journal/identity authority, and that no cache eviction/persistence change was introduced.
- [x] 3.4 After A02/A10 safety fixes land, run full unit/Release validation and record normal/reduced-motion compatibility; do not claim a universal speedup or force-cancelable native extraction.
