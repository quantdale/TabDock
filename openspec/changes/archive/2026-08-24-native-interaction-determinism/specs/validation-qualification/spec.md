## MODIFIED Requirements

### Requirement: ValidationDriver SHALL support bounded configurable shards

The driver SHALL discover or accept explicit TabDock and GuineaPig paths for
Debug/Release and RID variants. Named shards SHALL contain known scenarios, and
`all` SHALL orchestrate bounded shard processes without removing per-process
spawn/time safety caps. Every scenario SHALL also declare capability
requirements, use the canonical qualification outcome vocabulary, and emit a
result artifact linked from the run manifest.

#### Scenario: Release artifacts run without source edits
- **WHEN** the driver is invoked with `--configuration Release` against Release artifacts
- **THEN** it locates both executables, resolves declared capabilities before destructive setup, and runs the selected shard when runnable

#### Scenario: All runs as bounded shards
- **WHEN** the driver is invoked with `--yes all`
- **THEN** it runs the named shards sequentially, each with independent safety budgets, and reports the first non-PASS shard with its canonical outcome without treating blocked or skipped scenarios as PASS

#### Scenario: A scenario cannot prove its harness boundary
- **WHEN** a selector, cleanup, ownership, or evidence invariant is not proven
- **THEN** the scenario is recorded as `FAIL_HARNESS` with bounded evidence and the shard does not relabel it as a product failure

#### Scenario: Reruns remain first-attempt authoritative
- **WHEN** a scenario is run with investigation reruns
- **THEN** the first attempt remains authoritative and a later pass after a valid failure is recorded as `FLAKE_UNCLASSIFIED`, never as PASS
