# Current Reign systems

Update this file whenever a major system is added, extracted, or materially changes its public entry points.

## Core contracts

- Location: `ReignServer/shared/Reign.Core.Contracts`
- Public entry point: `ReignRelationshipBaselinePolicy`
- Invariant: override > save-derived baseline > native baseline > current relation.

## Relationships

- Pure kernel: `ReignServer/shared/Reign.Relationships`
- Server orchestration: `ReignServer/src/Modules/Relationships`
- Bannerlord adapter: `ReignBeta/src/Modules/Relationships`
- Public kernel entry point: `RelationshipCompatibilityPolicy`
- Invariants: all 256 MBTI pairings are present; values are directional and constrained to `[-1, 1]`; relationship percentage adjustment clamps to `[-100, 100]`.

## Persistence and Save Sync

- Storage and Save Sync: `ReignServer/src/Modules/Persistence`
- Bannerlord save adapter: `ReignBeta/src/Modules/Persistence`
- Provider: PostgreSQL only in production; campaign and save-point state are schema-isolated.

## Feature subsystems

- Characters, Reputation, Dialogue, Diplomacy, Court, Spymaster, Kingdom Events, Rebellion, World Simulation, Portraits, and UI each own matching server and/or Bannerlord folders beneath `src/Modules`.
- Feature live-test harnesses live beneath `ReignServer/tests/ReignLiveTest/Features`.
- Feature MCP tools and contract tests live in matching module/feature folders instead of the validation-infrastructure area.
- Server route groups use `Program.<Subsystem>Routes.cs` partials; the central host only dispatches to those groups.

## Validation infrastructure

- Project catalog: `reign-projects.json`
- Module manifest: `reign.modules.json`
- Planner/runner: `ReignMcp`
- Manifest schema: `reign-module-manifest-v2`, enforcement `enforce`
- Authoritative evidence: `.codex-build/reign-mcp/validation/*/validation-report.json`
- Ownership invariant: every active validation-relevant path has exactly one module facet owner or an explicit non-code classification.
