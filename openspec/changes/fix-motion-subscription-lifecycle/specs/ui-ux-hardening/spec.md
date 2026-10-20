## ADDED Requirements

### Requirement: Motion feedback attachments SHALL be lifetime-owned and collectible

Motion feedback SHALL attach at most one active observer set per control/template generation. Unload, disable, template replacement and disposal SHALL release externally rooted subscriptions, queued callbacks, animation clocks and references to obsolete sources/template parts. Discarded controls SHALL become collectible once their owned work is settled and all external owners are released. Temporary unload/reload SHALL preserve functional feedback without duplicate observers, including when reduced motion is enabled.

#### Scenario: Discarded feedback controls are collected

- **WHEN** repeated real styled buttons and feedback borders are created, unloaded/disposed, released and subjected to settled-work collection checks alongside non-feedback controls
- **THEN** neither enabled nor reduced-motion attachments retain the discarded controls through property observers or static registries

#### Scenario: Reload does not multiply feedback

- **WHEN** a control is loaded, unloaded and loaded repeatedly
- **THEN** each state transition produces one current feedback response and the observer count returns to zero while inactive

#### Scenario: Disabling or replacing a template retires old ownership

- **WHEN** feedback is disabled or the control template changes while callbacks are queued
- **THEN** the old parts and control generation receive no delayed feedback and their obsolete references can be collected
