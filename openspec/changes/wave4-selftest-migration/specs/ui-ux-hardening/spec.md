## MODIFIED Requirements

### Requirement: The split partition SHALL have a single deterministic definition
The 50/50 partition SHALL be defined by exactly one function
(`SplitGeometry.Partition`). Its invariants — exact coverage, zero overlap,
zero gap, no inverted or overflowing rects — SHALL be qualified by the
headless xUnit suite over an exhaustive deterministic matrix (all widths
1..4096, representative heights, positive/zero/negative origins, odd widths),
a seeded fuzz sweep (100,000 rects, fixed seed 20260810), and the size-constraint
minimality math. The product executable carries no geometry self-test mode.

#### Scenario: Odd widths partition without overlap
- **WHEN** the partition qualification runs over widths 799/800/801/1023/1024/1025/1919/1920/1921 at positive and negative origins
- **THEN** LEFT.Right == RIGHT.Left, RIGHT.Right == content.Right, LEFT.Width + RIGHT.Width == content.Width, and zero overlap/gap hold for every case
