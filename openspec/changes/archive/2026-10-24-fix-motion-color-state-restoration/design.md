## Context

See proposal.md and audit A06. Border feedback reads Color from the same brushes whose animation clocks supply effective values. A filled selected endpoint consequently becomes the next supposed normal target. A03 supplies attachment lifetime ownership; A07 will separately repair style/binding precedence.

## Goals / Non-Goals

**Goals:** Deterministic state targets and true return to base colors after complete or interrupted feedback.

**Non-Goals:** No palette/duration/easing change or picker-specific state-source mapping.

## Decisions

1. Capture independent base background/border color tokens in the attachment's state, separate from mutable rendering brushes and their clocks. State target calculation is based on semantic inputs and base tokens, never current animated values. A later authoritative base-source update may replace tokens through A07, not by sampling an endpoint.
2. Preserve current explicit state precedence: selection controls selected fill; otherwise hover controls hover fill; otherwise use base fill. Keyboard focus controls the focus border, followed by selected border, then base border. Exiting focus while selection remains must retain selection, not force normal.
3. Replace rather than compose successive clocks. Detach clears owned clocks; reduced motion applies the same target calculation without a temporal transition. Rapid reversals may start at the displayed intermediate color but their destination remains a separately calculated token.
4. Tests drive real controls and advance/settle clocks through a bounded dispatcher helper. Cover selection, focus, hover, their overlaps, exit order, interruptions, and reload. Correctness is final visible color and collectability, not elapsed animation timing or source markers.

## Risks / Trade-offs

- Keeping the first base token forever would ignore later style changes → expose a single authoritative-token update boundary for A07.
- Clearing an animation incorrectly can flash a stale local value → settle/remove clocks while restoring the intended base/target source and test exit paths.
- Generic border changes share a module with lifecycle work → use A03's existing owner and rerun its weak-reference tests.

## Migration Plan

Require the current motion baseline and A03. Add red round trips using the audited color fixture, fix target storage, and verify both motion policies. No visual-token or native-window migration. A07 may change rendering channels later, but must preserve these visible-state contracts rather than tests tied only to one brush field.
