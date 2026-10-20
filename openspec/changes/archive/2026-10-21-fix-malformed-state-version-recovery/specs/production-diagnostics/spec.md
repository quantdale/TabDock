## ADDED Requirements

### Requirement: Doctor SHALL tolerate invalid root schema metadata

Doctor SHALL classify wrong-type, invalid numeric, and unsupported root schema metadata without throwing or invoking mutating restore. Malformed metadata SHALL be reported as corrupt, a supported missing legacy version SHALL be distinguished from invalid metadata, and a representable future version SHALL be reported unsupported. These classifications SHALL NOT prevent other available report sections from being emitted with exit code zero.

#### Scenario: Wrong-type Version still produces a doctor report

- **WHEN** persisted state contains Version as a string, null, boolean, object, or array
- **THEN** doctor returns zero, reports corrupt schema metadata, emits the remaining available sections, and preserves primary and backup bytes

#### Scenario: Numeric and missing versions are classified deliberately

- **WHEN** state has a fractional, out-of-range, nonpositive, missing, or representable future Version
- **THEN** doctor distinguishes corrupt numeric metadata, supported legacy absence, and unsupported future schema without modifying the file
