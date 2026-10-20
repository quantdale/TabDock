## ADDED Requirements

### Requirement: Latest admitted save intent SHALL survive unchanged-content optimization

Every admitted immutable save snapshot SHALL participate in request ordering even when it equals the last completed disk content. After admitted writes settle successfully, the primary SHALL contain the latest requested snapshot, including when an older different snapshot was queued or already committing. An unchanged optimization SHALL skip I/O only when no earlier admitted work can leave different final content. Synchronous safety saves SHALL return only after their request has passed the serialized durability decision; async settlement SHALL observe the latest admitted request. Failed writes SHALL preserve complete evidence and remain retryable rather than being marked saved on enqueue.

#### Scenario: A synchronous return to A supersedes an in-flight B

- **WHEN** A is durably saved, async B is held inside its write transaction, and a newer synchronous A is requested before B finishes
- **THEN** after the writer gate is released and the synchronous save returns, the primary contains A rather than B

#### Scenario: An asynchronous return to A supersedes an in-flight B

- **WHEN** A is durably saved, async B is held in flight, and a newer async A is requested
- **THEN** awaiting the latest admitted settlement after release leaves A on disk

#### Scenario: Idle unchanged state performs no replacement transaction

- **WHEN** A is already durably saved and no different write can remain in flight when A is requested again
- **THEN** the completed primary/backup set remains unchanged without a redundant replacement transaction

#### Scenario: The latest snapshot remains retryable after failure

- **WHEN** the final admitted write fails at a staged backup or primary boundary and the same snapshot is requested after the fault is removed
- **THEN** prior evidence remains complete during failure and the retry durably installs the requested state
