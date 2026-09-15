# Documentation map

## Current guidance

| Need | Start here |
| --- | --- |
| Product use and limitations | [README](../README.md) |
| Fresh workstation setup | [Onboarding](../ONBOARDING.md) |
| Agent instructions and current work | [AGENTS](../AGENTS.md), [checkpoint](../.agent/STATE.md) |
| Architecture and native presentation | [Architecture](ARCHITECTURE.md) |
| WPF design and automation contracts | [Frontend](FRONTEND.md) |
| Builds, tests, diagnostics, supervised runs | [Testing](TESTING.md) |
| Detailed developer conventions | [Agent reference](internal/AGENT_GUIDE.md) |
| Current behavior specifications | [OpenSpec capabilities](../openspec/specs/) |
| Release evidence and publication | [Control plane](release/qualification-control-plane.md), [publication gates](release/publication-gates.md) |
| Repository protection and CI authority | [Protection policy](release/repository-protection.md) |

## Historical evidence

[KNOWN_ISSUES](../KNOWN_ISSUES.md), dated audits under `audits/`, campaign and
waypoint reports under `internal/`, and `.agent/` plans/investigations record
what was observed at their stated baseline. Their old commands, paths, issue
statuses, and validation counts are not current instructions. The historical
[browser test plan](internal/TEST_PLAN.md) predates Shepherd.

Completed OpenSpec changes remain under `openspec/changes/archive/`. Discover
active changes with the pinned CLI instead of resuming an archived change by
name. The [add-ons plan](agent-integrations/REPOSITORY_LOCAL_ADDONS_MASTER_PLAN.md)
is a preserved proposal, not an installation instruction.

When refreshing guidance, verify source and script contracts, keep historical
evidence dated, and update generated agent files through
`scripts/sync-agent-configs.ps1`. Its `-Check` mode detects mirror drift.
