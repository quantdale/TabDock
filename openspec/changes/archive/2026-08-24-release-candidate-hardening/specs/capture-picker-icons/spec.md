## ADDED Requirements

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
