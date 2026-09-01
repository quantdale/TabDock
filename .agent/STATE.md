# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — FINAL CLOSURE CORRECTION ACTIVE

**Objective:** Correct four premature real-app-hardening closure defects and
close only with a technically defensible final disposition.

**Active OpenSpec change:** `real-app-hardening-final-closure`
(strict validation `valid=true` before implementation).

**Status:** The previously archived real-app hardening campaign stands as
history, but review found incomplete evidence. This correction campaign does
NOT invalidate previously valid evidence — only these remain:

1. Edge's preserved first valid `FAIL_PRODUCT` (second F11 cycle, first
   invocation) is `FLAKE_UNCLASSIFIED` and not yet explained → bounded 5×3
   characterization + defensible final disposition.
2. Real Chromium visual acceptance was checked complete without actual
   restricted browser visual packets or capable multimodal review → real
   Chrome/Edge/Brave packets + review + tamper check.
3. Final-validation task was checked complete although the canonical
   `validate.ps1 -Configuration Release -Ci -Publish`, explicit native ABI
   gate, and resource/privacy/recovery qualification were not all actually
   executed → run the actual gates.
4. Final report said 26 real-app tasks while the canonical archived
   `tasks.md` contains 38 checkbox rows → ledger corrected to 38 with a
   mapping for 1.7 / 4.3 / 7.1 + `EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION`
   (`.agent/investigations/real-app-hardening-final-closure-2026-09-02.md`).

No product repair is authorized merely to obtain closure; no timeout/retry/
second-F11/weakened-assertion "fixes". Git authority must be resolved
dynamically.

### COMPLETED / ARCHIVED

- presentation integrity (`2026-08-31-presentation-integrity-physical-certification` and `2026-08-31-user-reported-presentation-integrity`)
- physical presentation certification (supervised native/lease/geometry/DPI/z-order/cleanup evidence)
- visual evidence / multimodal review (`2026-08-31-visual-evidence-ai-review` + `2026-09-01-visual-evidence-closure-and-performance-requalification`)
- visual performance / resource requalification (resource lifecycle, visual overhead budgets, historical bundle compatibility)
- DPI/topology hardening (`2026-09-01-dpi-topology-hardening` — `dc22ff3ab408d6aae84412f9cf418e8fed7aada8` exe `EF22593A` driver `6A1AC34` snapshot `92790d2a`, `173/173` selftest, `795/795` unit at archive, `14` RUNNABLE PASS / `21` BLOCKED_CAPABILITY, visual `Valid:true`, 0 FAIL_PRODUCT)
- real-app hardening (`2026-09-01-real-app-hardening` — Chromium Chrome/Edge/Brave F11, Windows 11 Notepad broker, Windows Terminal host; `812/812` unit, `173/173` selftest at final; Chrome/Brave 2/2 PASS, Edge FLAKE_UNCLASSIFIED, Notepad BLOCKED_ENVIRONMENT, Terminal BLOCKED_CAPABILITY; `REAL_APP_RESTRICTED` privacy enforced)

### Qualification status (final settled candidate)

- **Source:** final settled `HEAD` is not embedded; verify dynamically (last substantive implementation before archive was `c7d69ff856aff8f3179d6d8e1b6309728327c06e`, executable `57B4DC26E42B0B0440F84313F042641A892EE9416B8EA088A2DCD74C354DB10C` `1.1.0+c7d69ff`, driver `0BAFC906EEC34880437BCAF85567ED14A52631A170ACF56876994AB842D44618`). Historical `6bb8ecc` v1.1 qualification is preserved as pre-final; `bc678ef` DPI repair and `GuestDpiPositionScope` seam are part of final code, version stays `1.1.0` qualification-only per `release-engineering` (signing NOT_CONFIGURED, external gates blocked).
- **Deterministic:** `dotnet build` Debug/Release 0 warnings, `812/812` Debug/Release unit, `173/173` selftest, `14/14` visual, `39` OpenSpec strict, `Valid:true` historical DPI packets, Chrome/Brave native packets `Valid:true`-equivalent, Edge preserved failure, Notepad/Terminal blocked with rationale.
- **Supervised available physical cells:** DPI 14 RUNNABLE PASS (120/96), title center `≤0.50 px`, mixed-DPI bidirectional, topmost, maximize/winup/split containment; real-app Chrome/Brave F11 2 cycles PASS, Edge flake retained, Notepad/Terminal blocked per matrix.

### External / not product defect

- **Signing:** `NOT_CONFIGURED` (Authenticode material not present; `SIGNING_PROVIDER=not-configured`; production eligibility `BLOCKED_EXTERNAL` per `release-engineering`)
- **Production eligibility:** `BLOCKED_EXTERNAL` (qualification-only; external human/machine evidence and signing remain external gates)
- **Unavailable hardware/capability:** negative-X/Y, above-origin, staggered/odd/narrow/large topology, 144/168/192 DPI (150%/175%/200%), Firefox not installed, Notepad single-instance tab host, Terminal monarch reuse, whole-desktop capture, universal pixel golden — all `BLOCKED_CAPABILITY`/`SKIP_CAPABILITY` with truthful rationale, not product defects
- **Human/supervised gates:** physical input, lease, foreground, point ownership, supervised visual review for required packets — `BLOCKED_ENVIRONMENT`/`REVIEW_UNAVAILABLE` where applicable, not fabricated PASS

### Durable records

- DPI acceptance matrix (sanitized): `.agent/investigations/dpi-topology-hardening-acceptance-matrix-2026-09-01.json` (`sourceMatrixRecovered=true`, 35 cells)
- DPI repair provenance: `.agent/investigations/dpi-positioning-repair-provenance-2026-09-02.md` (`HISTORICAL_TRIGGER_NOT_RECOVERABLE`, deterministic defect, `GuestDpiPositionScopeTests` 17 cases)
- Real-app handoff: `.agent/investigations/real-app-hardening-handoff-2026-09-02.md` (19.1/19.4 row-level closure)
- Real-app acceptance matrix: `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.json` + `.md` (Chrome/Brave PASS, Edge FLAKE, Notepad/Terminal blocked, privacy `REAL_APP_RESTRICTED`)
- Corrective ledger reconciliation: `.agent/investigations/real-app-hardening-final-closure-2026-09-02.md` (38-row ledger, 1.7/4.3/7.1 mapping, Edge obligation)
- Current regression: `tests/UnitTests/GuestDpiPositionScopeTests.cs` (812 tests at final)
- Active change: `real-app-hardening-final-closure` (corrective; NOT archiveable until Edge disposition + real Chromium packets/review + canonical gates + 38-row ledger are proven)

Update this file after each physical run, defect disposition, validation
milestone, and before final handoff. Keep it concise and evidence-based.
