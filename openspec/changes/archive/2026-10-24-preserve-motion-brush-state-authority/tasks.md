## 1. Establish semantic rendering ownership

- [x] 1.1 Confirm A03/A06 are present and inventory every EnableBorderStates template with its base/active, hover, selected and keyboard-focus source/precedence.
- [x] 1.2 Add failing actual-template tests for split active-state changes, picker focus borders, live BindingExpression/style changes and shared frozen brush integrity after Loaded.
- [x] 1.3 Add source-authority/recycling/detach tests, including selected+hover+focus combinations and both motion policies.

## 2. Move motion to its own rendering channel

- [x] 2.1 Replace competing local Background/BorderBrush assignments with template-provided hit-test-transparent, layout-neutral feedback layers; do not inject dynamic adorners or mutate shared resources.
- [x] 2.2 Update tabs, split halves, both pickers, text inputs, combo/menu rows and scroll thumbs to retain one semantic source and remove only redundant rendering writers.
- [x] 2.3 Feed current base-source changes into A06's independent tokens via A03's detachable owner; preserve original bindings/styles on attach, active updates and detach.
- [x] 2.4 Keep focus visibly highest-priority without changing split controller/native foreground behavior, hit areas, AutomationIds or the color palette.

## 3. Verify real templates and prerequisite contracts

- [x] 3.1 Run `dotnet build TabDock.sln` plus targeted brush-authority, keyboard-focus, A03 lifetime and A06 round-trip tests; use no desktop input or shown windows.
- [x] 3.2 Verify live state after reload/recycling, intact base value sources and unchanged automation/layout semantics; hand the explicit picker-state input seam to A08.
- [x] 3.3 After A02/A10 land, run complete unit/Release validation; keep physical split/keyboard smoke as separately supervised evidence, not a synthetic PASS.
