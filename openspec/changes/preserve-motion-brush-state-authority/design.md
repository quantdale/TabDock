## Context

See proposal.md and audit A07. Setting local Background/BorderBrush in Loaded overrides remaining style triggers and can replace bindings. Split-active state and picker keyboard focus still depend on those sources. A03 owns lifecycle, and A06 separates base tokens from animated values.

## Goals / Non-Goals

**Goals:** One semantic state projection, intact bound/styled base values, and motion-only rendering that cannot suppress active/focus changes.

**Non-Goals:** No split-controller state change, new theme, or picker checked-selection mapping.

## Decisions

1. Keep original base brush properties under their binding/style authority. Use template-provided, hit-test-transparent feedback layers for animated fill/border presentation rather than assigning a competing local brush to the original semantic border. Do not dynamically inject adorners, mutate shared frozen brushes, or depend on Tag.
2. Enumerate and migrate every EnableBorderStates template: tabs, split halves, both picker surfaces, text inputs, combo/menu rows and scroll thumbs. Where semantic state remains styled, read it as an input; where a motion layer represents a state, remove only the redundant rendering writer and retain the same state source. One documented precedence table resolves base/active, hover, selected, and keyboard-focus presentation.
3. Observe live base-source changes through A03's detachable owner, update A06's independent tokens, and render the current target without replacing the original BindingExpression or changing style-value precedence. On detach, remove only motion-owned layers/clocks; base sources remain current.
4. Keep keyboard focus as a highest-priority visible border layer independent of selection/hover. Test actual templates after Loaded and recycling, not only a synthetic standalone border.

## Risks / Trade-offs

- Extra overlay visuals can intercept input or affect layout → hit-test-transparent, layout-neutral layers; preserve existing AutomationIds/content hit areas.
- Competing focus triggers can obscure state → review each template's authority table and test focus plus selection/active combinations.
- Shared XAML edits conflict with A08/A09 → sequence this change after A03/A06 and before picker mapping; never run concurrent writers against these files.

## Migration Plan

Inventory affected templates, add red live-binding/style tests, then migrate rendering without palette changes. Check split A/B active indicators and both picker focus borders, and rerun A03/A06 coverage. No data migration. Rollback should disable the defective feedback decoration rather than restore local brush assignments over semantic bindings.
