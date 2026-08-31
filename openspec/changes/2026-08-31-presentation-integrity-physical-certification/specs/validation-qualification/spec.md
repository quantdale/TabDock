# validation-qualification delta — presentation-integrity physical certification

## MODIFIED Requirements

### Requirement: ValidationDriver SHALL support bounded configurable shards

The driver SHALL discover or accept explicit TabDock and GuineaPig paths for
Debug/Release and RID variants. Named shards SHALL be declared by the canonical
scenario catalog, SHALL contain only their catalog members, and SHALL have
explicit count/runtime budgets. Every scenario SHALL declare capability
requirements, use the canonical qualification outcome vocabulary, and emit a
result artifact linked from the run manifest. A direct scenario or shard run
SHALL write a versioned child manifest in an isolated artifact directory.
`all` SHALL create a parent run identity, import and verify every declared
child manifest, preserve per-process spawn/time safety caps, and fail closed on
missing, malformed, contradictory, stale, or tampered child evidence.

Presentation-integrity qualification SHALL explicitly distinguish deterministic
coverage from physically lease-qualified coverage. A synthetic/headless PASS
MAY support the implementation contract but SHALL NOT satisfy a physical field
cell that requires real guarded input, real monitors/DPI, a real topmost band,
or real application fullscreen behavior. Physical first-attempt outcomes SHALL
be retained across reruns.

#### Scenario: Release artifacts run without source edits
- **WHEN** the driver is invoked with `--configuration Release` against Release
  artifacts
- **THEN** it locates both executables, validates their candidate identities,
  resolves declared capabilities before destructive setup, and runs the selected
  catalog scenario or shard

#### Scenario: All runs as bounded shards
- **WHEN** the driver is invoked with `--yes all`
- **THEN** it runs the catalog-declared shards sequentially in isolated child
  directories, verifies each child manifest and exit outcome, and reports the
  first disagreement or non-pass shard without a monolithic impossible budget

#### Scenario: A scenario cannot prove its harness boundary
- **WHEN** a selector, cleanup, ownership, or evidence invariant is not proven
- **THEN** the scenario is recorded as `FAIL_HARNESS` with bounded evidence
  and the shard does not relabel it as a product failure

#### Scenario: Reruns remain first-attempt authoritative
- **WHEN** a physical presentation-integrity scenario has investigation reruns
- **THEN** the first valid attempt remains authoritative and a later pass after
  a valid failure is recorded as unresolved/flake evidence rather than PASS

#### Scenario: Synthetic pass does not satisfy a physical field cell
- **WHEN** a headless or synthetic presentation-integrity scenario passes but
  the matching requirement calls for real guarded input/topology
- **THEN** deterministic coverage is recorded separately and the physical cell
  remains pending or blocked until physically qualified
