# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, worktree state, and
worktrees. Resolve those values dynamically (`git rev-parse HEAD`, `git rev-parse origin/main`, `git status`, `git branch --show-current`); this file never embeds a
self-referential SHA claiming to be the commit that contains this file. Embedded SHAs name historical or last-substantive implementation commits only. After a push, report final SHA and CI result in session output for independent verification.

## Current state — HARDENING SEQUENCE CLOSED

**Objective:** Corrective closure of `real-app-hardening-final-closure` is complete
and archived; the hardening sequence is closed with a technically defensible
final disposition.

**Status (2026-09-02):** the corrective campaign resolved all four carried-forward
obligations and was archived as
`openspec/changes/archive/2026-09-02-real-app-hardening-final-closure/`
(35/35 tasks, strict validation `valid=true`, canonical specs updated and valid;
`openspec/changes/` contains only `archive/`). No active OpenSpec change.

1. **Edge historical `FAIL_PRODUCT` (run `ba1cedc3`, candidate `ab8853f1`) —
   terminal classification `PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE`.** Bounded
   5×3 characterization at final candidate `fbc4d92` = 15/15 PASS (runs
   `928524b2`, `f0117322`, `7b8adc23`, `08a14221`, `b3d9464c`; setup-only
   `FAIL_HARNESS` `8cfbc878` preserved). Historical mechanism reconstructed
   from the TabDock log: one posted identity-checked F11 exit
   (`presentation-restore-request method=browser-f11`) was not consumed by
   Edge before the 3500 ms settle window; `size-constraint` correctly refused
   to force-resize a fullscreen browser; no product/harness change made, no
   unresolved valid product defect remains.
2. **Real Chromium visual acceptance — completed at final candidate `fbc4d92`:**
   Chrome run `6a5bb064`, Edge run `76fb0a68`, Brave run `b151a8ce`; each with
   restricted packets (14 PNG checkpoints, `TEST_OWNED`, `AllowVirtualDesktop=
   false`, topology `92790d2a`): packet SHA-256 `c03de022…dcfe` /
   `1a47df5a…a019` / `75c6c51c…c652`; operator multimodal review per
   `.agent/workflows/visual-evidence-review.md` (SUSPECT-benign / SUSPECT-benign
   (privacy clean) / OK); offline verifier `Valid:true` on all three; real-packet
   tamper rejection proven (one PNG byte flipped → `visual evidence hash
   mismatch`).
3. **Canonical final gates — actually executed at `fbc4d92`:**
   `validate.ps1 -Configuration Release -Ci -Publish` exit 0 (publish smoke
   sha `747187…2F85`), `--selftest-native-abi` PASS, resource-headless PASS
   (`--cycles 32 --profile all --seed 20260824`), 812/812 unit Debug+Release,
   173/173 selftest, 179/179 release-tooling, doctor/support-bundle (privacy
   clean)/pending-recovery PASS, strict OpenSpec 39/39, catalog 135.
4. **Ledger correction:** archived real-app ledger = 38 checkbox rows (not 26);
   mapping for 1.7 / 4.3 / 7.1 + `EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION`
   all `SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE` with final evidence in
   `.agent/investigations/real-app-hardening-final-closure-2026-09-02.md`.

***

### COMPLETED / ARCHIVED

- presentation integrity (`2026-08-31-presentation-integrity-physical-certification` and `2026-08-31-user-reported-presentation-integrity`)
- physical presentation certification (supervised native/lease/geometry/DPI/z-order/cleanup evidence)
- visual evidence / multimodal review (`2026-08-31-visual-evidence-ai-review` + `2026-09-01-visual-evidence-closure-and-performance-requalification`)
- visual performance / resource requalification (resource lifecycle, visual overhead budgets, historical bundle compatibility)
- DPI/topology hardening (`2026-09-01-dpi-topology-hardening` — `dc22ff3ab408d6aae84412f9cf418e8fed7aada8` exe `EF22593A` driver `6A1AC34` snapshot `92790d2a`, `173/173` selftest, `795/795` unit at archive, `14` RUNNABLE PASS / `21` BLOCKED_CAPABILITY, visual `Valid:true`, 0 FAIL_PRODUCT)
- real-app hardening (`2026-09-01-real-app-hardening` — Chromium Chrome/Edge/Brave F11, Windows 11 Notepad broker, Windows Terminal host; `812/812` unit, `173/173` selftest at final; Chrome/Brave 2/2 PASS, Edge FLAKE_UNCLASSIFIED, Notepad BLOCKED_ENVIRONMENT, Terminal BLOCKED_CAPABILITY; `REAL_APP_RESTRICTED` privacy enforced)

### Qualification status (final settled candidate)

- **Qualification candidate (binary/driver authority): `fbc4d926abf34e1ace4260ddef25807699f402b9` — executable `BFC7EF3445020CE920A1BAA41C4B697AEDC306541FBCF03D8CCA9AD024059851` `1.1.0+fbc4d92`, driver `781FF394A77A9828F687D66276DD9E16A059D4DE7B4CE1E440B30AC3F5C1BEC0`. All physical Chromium evidence (Edge 5×3, Chrome/Edge/Brave packets) belongs to this SHA; later evidence/archive commits do not retag it. Historical `6bb8ecc`/`6223cbf`/`c7d69ff` qualifications are preserved as pre-final; version stays `1.1.0` qualification-only per `release-engineering`.
- **Deterministic:** `dotnet build` Debug/Release 0 warnings, `812/812` Debug/Release unit, `173/173` selftest, `179/179` release-tooling, `39` OpenSpec strict, catalog `scenario-catalog-2026-09-01-v2` 135, `Valid:true` for all three final-candidate Chromium visual packets, Edge `PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE` with preserved first-failure authority, Notepad/Terminal blocked with rationale.
- **Supervised available physical cells:** DPI 14 RUNNABLE PASS (120/120+96), title center `≤0.50 px`, mixed-DPI bidirectional, topmost, maximize/winup/split containment; real-app Chrome/Edge/Brave F11 at `fbc4d92` (15/15 Edge, 2/2 Chrome, 2/2 Brave) with restricted visual packets + operator review + tamper rejection; Notepad/Terminal blocked per matrix.

### External / not product defect

- **Signing:** `NOT_CONFIGURED` (Authenticode material not present; `SIGNING_PROVIDER=not-configured`; production eligibility `BLOCKED_EXTERNAL` per `release-engineering`)
- **Production eligibility:** `BLOCKED_EXTERNAL` (qualification-only; external human/machine evidence and signing remain external gates)
- **Unavailable hardware/capability:** negative-X/Y, above-origin, staggered/odd/narrow/large topology, 144/168/192 DPI (150%/175%/200%), Firefox not installed, Notepad single-instance tab host, Terminal monarch reuse, whole-desktop capture, universal pixel golden — all `BLOCKED_CAPABILITY`/`SKIP_CAPABILITY` with truthful rationale, not product defects
- **Human/supervised gates:** physical input, lease, foreground, point ownership, supervised visual review for required packets — `BLOCKED_ENVIRONMENT`/`REVIEW_UNAVAILABLE` where applicable, not fabricated PASS

### Durable records

- DPI acceptance matrix (sanitized): `.agent/investigations/dpi-topology-hardening-acceptance-matrix-2026-09-01.json` (`sourceMatrixRecovered=true`, 35 cells)
- DPI repair provenance: `.agent/investigations/dpi-positioning-repair-provenance-2026-09-02.md` (`HISTORICAL_TRIGGER_NOT_RECOVERABLE`, deterministic defect, `GuestDpiPositionScopeTests` 17 cases)
- Real-app handoff: `.agent/investigations/real-app-hardening-handoff-2026-09-02.md` (19.1/19.4 row-level closure)
- Real-app acceptance matrix: `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.json` + `.md` (final candidate `fbc4d92`: Chrome/Edge/Brave native PASS + packets + reviews + tamper; Edge terminal classification; Notepad/Terminal blocked; privacy `REAL_APP_RESTRICTED`/`TEST_OWNED`)
- Corrective ledger reconciliation: `.agent/investigations/real-app-hardening-final-closure-2026-09-02.md` (38-row ledger, 1.7/4.3/7.1 + Edge obligation mapping, final evidence, `SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE`)
- Current regression: `tests/UnitTests/GuestDpiPositionScopeTests.cs` (812 tests at final)
- Corrective change: **archived** `openspec/changes/archive/2026-09-02-real-app-hardening-final-closure/` (35/35 tasks; strict validation valid; canonical specs updated)

Update this file after each physical run, defect disposition, validation
milestone, and before final handoff. Keep it concise and evidence-based.
