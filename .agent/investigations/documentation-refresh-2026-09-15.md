# Investigation: stale documentation and agent guidance

**Date:** 2026-09-15
**Status:** concluded

## Question

Which repository instructions and non-application artifacts no longer match
current source, tooling, Git state, or their historical scope?

## Findings

| Area | Evidence and correction |
| --- | --- |
| Checkpoint | Git began clean at `ea1ad43`; the repair described as uncommitted had landed. Preserved the old checkpoint as dated history and replaced STATE with a concise handoff. |
| Agent entrypoints | AGENTS incorrectly described canonical `.claude` workflows as generated mirrors; onboarding required broad reading and optional tooling before unrelated work. Clarified ownership, task-dependent setup, and template paths. |
| Goal adapters | Continuation text implied commit/push and fresh planning without user authorization. Generator-owned adapters now route through root instructions and the existing objective. |
| Generated workflows | Some skill copies retained 1.6.0-era text while canonical copies recorded 1.9.0 provenance. Extended the synchronizer across tracked harnesses, preserving colon/dash/skill invocation forms, frontmatter, argument placeholders, and unrelated files. Added read-only `-Check`. CLI dependency stays pinned at 1.8.0. |
| OpenSpec agent | GitHub's generated adapter recommended an unpinned global install. The generator now emits a small adapter using the pinned local CLI and current repository skills. |
| Product/architecture | Verified LOCATIONCHANGE subscription, drift policy, first ContentRendered reconciliation, incoming-iconic restoration, and title-bar re-glue against current production/driver source. Removed contrary old claims. |
| Testing | Replaced pytest-style `-k` with the supported .NET filter; removed instructions to overwrite real AppData during a corruption repro; clarified lease, process ownership, and first-attempt evidence rules. |
| Release | Verified catalog generation `scenario-catalog-2026-09-01-v2`, manifest v2, bundle/package/report v1, external evidence v3, and native ABI versus xUnit qualification boundaries. Corrected old generation/self-test descriptions. |
| Protection | Read-only GitHub ruleset/protection requests returned a plan-related 403. Marked the old applied-ruleset claim historical and current enforcement unverified; removed broad bypass creation instructions and clarified post-push CI versus enforced protection. No settings changed. |
| Navigation/history | Added docs/README.md, labeled KNOWN_ISSUES as history, corrected the preserved add-ons inventory's claim that local `.vscode/mcp.json` was tracked, and removed an accidental shell-output prefix from AGENT_GUIDE. |

## Approaches tried

- Repowise initially reported a stale index and unavailable synthesis provider;
  direct file reads and focused source queries supplied the audit evidence.
  `repowise update` completed successfully; its Git-based refresh is not proof
  that all uncommitted documentation is indexed.
- The final inline Markdown link scan covered 518 tracked and newly added
  Markdown files. The only missing local target was `LICENSE_ACKNOWLEDGEMENTS.md` in
  the verbatim third-party DigiCert research README. Its containing README
  explicitly preserves that snapshot as provenance; the vendor copy was not
  rewritten. Current repository documentation links were valid.
- Inline source-path checks classified missing JSON names as generated/runtime
  artifacts and the two removed PowerShell e2e paths as explicitly historical.
- The generic skill-creator validator passed the goal skill but rejected the
  existing upstream OpenSpec `compatibility` frontmatter key. That validator
  does not support the vendored schema; preserved the metadata and verified
  generated copies with the synchronizer instead.

## Validation

- Corrected .NET filter plus initial-presentation coverage: 26 passed, 0 failed.
- Release-tooling regression suite: 179 passed, 0 failed.
- Pinned OpenSpec: no active changes; all 38 specifications validate.
- Synchronizer: detected drift before regeneration (exit 1); after regeneration,
  all 142 generated files match (exit 0). Re-running leaves them unchanged.
- `git diff --check`: passed. Changes are documentation, agent adapters/skills,
  an ignore-file comment, and the synchronization script; no product source,
  dependency versions, CI workflow, or behavior specification changed.

## Conclusion

The documentation refresh is complete. Historical records remain evidence for
their stated baseline; the docs map directs current work to maintained guides.
External physical/release qualification is not claimed by this refresh. No
commit, push, release, or repository-settings mutation was performed.

## References

- `.agent/plans/documentation-refresh-2026-09-15.md`
- `Services/WinEventMonitor.cs`, `Services/GuestPresentationDriftPolicy.cs`,
  `Views/ContainerWindow.xaml.cs`, `Scenarios.Drag.cs`, `ScenarioCatalog.cs`
- `scripts/validate.ps1`, `scripts/release-qualify.ps1`,
  `scripts/qualification-bundle.ps1`, `scripts/qualification-package.ps1`,
  `scripts/run-qualification-package.ps1`, `.github/workflows/build.yml`
- [GitHub rules](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)
