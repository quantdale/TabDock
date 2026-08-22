# Wave 4: migrate hermetic self-tests out of the shipping executable

## Why

Roughly 6,000 lines of hermetic test code (10 standalone `Services/*SelfTest*.cs`
suites plus embedded test classes in production files) ship inside
`TabDock.exe`, reachable only through the undocumented
`--selftest-diagnostics`/`--selftest-geometry` flags. The cost is not bytes — it
is a ~1:1 test-to-product ratio in the recovery subsystem, test harnesses
reachable in the customer binary, and an inability to tell product from test by
namespace. The xUnit project already runs against the same product assembly in
the same CI gate via `InternalsVisibleTo`.

## What changes

- All hermetic suites move to `tests/UnitTests` as semantically named facts and
  theories; redundant wrappers are deleted rather than migrated.
- `--selftest-diagnostics` (`DiagnosticCommandKind.SelfTest`,
  `DiagnosticSelfTest`) is removed entirely.
- The deterministic partition matrix/fuzz becomes xUnit-owned;
  `--selftest-geometry` and `SplitGeometry.RunSelfTest` are removed from the
  product assembly.
- `--selftest-native-abi` is retained in `TabDock.exe`: its WINDOWPLACEMENT
  contract evidence genuinely requires a real built Windows process against
  real user32, and hosted CI runs it on two different Windows images.
- Validation scripts and workflow comments reflect the new authority:
  xUnit qualifies hermetic behavior; executable smokes qualify real process
  behavior only.

## Non-goals

- No product behavior change of any kind; no Shepherd/persistence/diagnostics
  refactoring (Wave 4 rule 20). The only product-source change beyond
  deletions is one optional dispatcher parameter on the already-internal
  CapturePickerViewModel test constructor.
- Archiving previously completed OpenSpec changes (Wave 5 repository hygiene).
