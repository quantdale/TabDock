## 1. Exact-SHA artifact chain

- [x] 1.1 Add `scripts/release-qualify.ps1`: exact-SHA + clean-tree enforcement, audited restore, single-file publish, execution and qualification of the published executable (version identity equals candidate SHA, self-reported SHA-256 matches `Get-FileHash`, geometry and diagnostics self-tests), `release-manifest.json`, and `SHA256SUMS.txt`.
- [x] 1.2 Record in the manifest: product, semantic version, source commit SHA, artifact filename, artifact SHA-256, unsigned qualified SHA-256, final signed SHA-256 when applicable, runtime identifier, configuration, build identity, signing status, signature verification, qualification status, timestamp, workflow run id, and explicit external gates.
- [x] 1.3 Add `.github/workflows/release.yml`: dispatch-only, exact-SHA checkout verification, canonical qualification, artifact retention, and a separate explicit publication job that re-verifies provenance before `gh release create` and verifies assets afterwards.
- [x] 1.4 Do NOT rebuild at publication time; the release consumes the preserved qualified artifact.

## 2. Signing readiness

- [x] 2.1 Add `scripts/sign-release.ps1` with `NOT_CONFIGURED` / `SIGNED` / `SIGNATURE_VERIFIED` / `SIGNING_FAILED` states, secret-only material, explicit RFC3161 timestamping, temp-PFX lifecycle, and `RELEASE_SIGNING_REQUIRED` enforcement.
- [x] 2.2 Record signing status and (when applicable) both hash identities in the manifest; never describe an unsigned executable as signed.
- [ ] 2.3 Actual Authenticode signing with real material — NOT DONE: credentials are `BLOCKED_EXTERNAL`; infrastructure is ready and safe to leave unsigned.

## 3. Version consistency

- [x] 3.1 Document `TabDock.csproj` `Version` as the authoritative mechanism; release tooling records the expected version and validates it appears in the manifest; release notes render from the manifest.
- [x] 3.2 Confirm `--version`, `BuildIdentity`, informational version, and the manifest agree for a produced artifact.

## 4. Release workflow safety

- [x] 4.1 No stable release on every push: `release.yml` is `workflow_dispatch`-only.
- [x] 4.2 Fail-closed publication: manifest `PASS` + matching SHA + on-disk hash equality required before release; tag created at the exact candidate SHA; assets verified after publication.
- [x] 4.3 Actions versions are current supported majors matching the repository's existing green pins (`checkout@v7`, `setup-dotnet@v6`, `setup-node@v7`, `upload-artifact@v7`, `download-artifact@v7`).

## 5. Dependency and reproducibility policy

- [x] 5.1 Add `global.json` pinning the .NET 8 SDK feature band with `rollForward: latestFeature`; verify local build and CI honor it.
- [x] 5.2 Keep NuGet lock mode avoided (documented rationale: SDK-generated ILLink.Tasks instability); keep mandatory NuGet audit in CI; keep OpenSpec pinned via `npm ci` lockfile.
- [x] 5.3 Document the reproducibility policy in `docs/ARCHITECTURE.md` and README.

## 6. Human/external qualification procedures

- [x] 6.1 Add `docs/release/mixed-dpi-qualification.md` (16 scenarios, evidence requirements, `BLOCKED_NO_MIXED_DPI_HARDWARE` result).
- [x] 6.2 Add `docs/release/final-smoke.md` (38 checks, operator-signed evidence, exact-artifact identity requirement).
- [ ] 6.3 Execute the final manual Windows smoke — NOT DONE: `BLOCKED_EXTERNAL` (requires a human on real Windows with the exact artifact).
- [ ] 6.4 Execute physical mixed-DPI qualification — NOT DONE: `BLOCKED_EXTERNAL` (no mixed-DPI hardware evidence).
- [ ] 6.5 Browser qualification for unavailable browsers — NOT DONE: `SKIP_BROWSER_NOT_INSTALLED` reported per browser; no fabricated coverage.

## 7. Known limitations and documentation

- [x] 7.1 Review README known limitations against current behavior (self-maximize, UIPI/elevation, cross-reboot persistence intent, DPI awareness, legacy tokenless recovery, Task Manager kill, browser availability) and update where stale.
- [x] 7.2 Document signing policy, release workflow usage, and artifact provenance in README.
- [x] 7.3 Document the DWM transition-suppression rationale and residual hard-kill behavior in `docs/ARCHITECTURE.md`.

## 8. Legacy audit reconciliation closure

- [x] 8.1 Semantically reconcile `agent/tabdock-deep-audit-remediation` (WINDOWPLACEMENT ref/44-byte contract + ShowWindow declaration + Windows product normalization); all other legacy corrections were already present or superseded.
- [x] 8.2 Run canonical validation (builds, geometry/diagnostics 205 checks, doctor/version, support-bundle privacy, OpenSpec 19/19, publish smoke) — PASS.
- [x] 8.3 Push reconciliation and verify exact-SHA hosted CI.
- [x] 8.4 Close PR #10 without merging and delete the superseded branch once exact-SHA CI is green.
- [x] 8.5 Confirm remote branch set contains `main` only after cleanup (verified via `git branch -r`: `origin/HEAD -> origin/main`, `origin/main`).

## 9. Repository protection policy

- [x] 9.1 Document recommended GitHub settings (release-tag ruleset: no force push, no deletion; optional exact-SHA status requirement for `v*` tags) — actual repository setting mutation is `BLOCKED_ENVIRONMENT` unless API credentials with admin scope are available and the mutation is safe to perform.
- [x] 9.2 Apply the release-tag ruleset — DONE: ruleset `release-tags` (id 20878779, active, tag target, `refs/tags/v*`: deletion and force-push blocked) applied via the repository API with admin scope.

## 10. Final qualification and release decision

- [x] 10.1 Run the full canonical local qualification after all repository-side changes (`validate.ps1 -Configuration Release -Ci -Publish`) — PASS.
- [x] 10.2 Run `git diff --check` and a manual full-diff review for secrets, private keys, signing credentials, prohibited APIs, generated junk, and unrelated churn.
- [x] 10.3 Run `release-qualify.ps1` locally to prove the artifact chain and manifest generation end-to-end.
- [x] 10.4 Push the final production-closure commit and verify the exact final SHA's hosted CI run (must be green, must produce the candidate artifact for that SHA).
- [ ] 10.5 Release decision — NOT DONE in this change: expected verdict is GO FOR RELEASE CANDIDATE ONLY until the manual smoke, physical mixed-DPI, and signing gates have real evidence.
