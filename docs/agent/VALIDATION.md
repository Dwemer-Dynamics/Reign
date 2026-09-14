# Tiered validation

For code changes, obtain `reign_get_validation_plan` before editing, then run `reign_validate` for the completed change. For policy or documentation changes, use the `changed` plan with their exact paths; a documentation-only plan succeeds at Tier 1 without a build. A read-only audit needs evidence relevant to its conclusions, not a product build.

`reign.modules.json` owns module, dependency, facet and validation routing. `reign-projects.json` owns project discovery. Use their plan rather than scanning the whole repository to infer ownership.

| Tier | Scope | Typical trigger |
|---|---|---|
| 1 | Focused compilation/tests, or no build for documentation | Internal implementation or policy |
| 2 | Complete owning subsystem | Subsystem behavior |
| 3 | Affected boundaries, dependencies and consumers | Public contract, persistent schema or Bannerlord adapter |
| Product 4 | Complete Reign runtime/tooling boundary and quick verification | Explicit product-wide confidence or a manifest-selected product boundary |
| Ecosystem 4 | Every managed project and offline verification | Release/milestone, project ownership, module routing, shared build infrastructure or validation-engine change |

## Select the plan

Use `changed` for ordinary work, with one or more exact workspace-relative paths. Resolve every code path to exactly one facet. Missing, unknown or ambiguous paths block immediately: no selected tier, projects, build or fallback. Correct ownership before continuing. `reign_audit_module_coverage` provides a read-only ownership inventory.

Files inside an existing `src/Modules/<Subsystem>/` inherit that facet. A new subsystem needs registration in `reign.modules.json`. Creating or moving a `.csproj` requires the `all` plan and resolution of every unclassified project. Do not classify artifacts, backups, decompiled sources or third-party binaries as managed source.

Use the lowest tier selected by the manifest. Testing-catalog, MCP feature, agent-policy, deployment, database and ordinary project-file changes follow their owning module/product paths; they do not imply ecosystem `all` by themselves. A shared contract or failed targeted check may require broader boundary proof. Diagnose a failure and rerun the failed or smallest applicable profile before repeating product or ecosystem validation.

Each plan reports status, nullable selected tier, non-code classification, path rules, affected modules, selected projects, verification operations and coverage completeness. The service also supports explicit `core` and `tooling` subsets; these do not replace a broader required plan.

## Run and coordinate

Before validation, call `reign_get_validation_status`. Supply the current task UUID as `requestingTaskId` to `reign_validate`. The OS lease serializes heavy validation; a status snapshot does not reserve it. See [task coordination](TASK_COORDINATION.md) for busy or unknown ownership. Reuse only a successful report whose source fingerprint, configuration, scope, verification tier, exact project set and retained artifacts cover the requested plan.

Group related edits before validating. Intermediate checks should reduce a concrete debugging risk. Do not repeat a successful covered validation for unchanged source. Keep long scale, stress and acceptance runs separate unless the change affects performance, concurrency, persistence or deployment behavior.

Use MCP for builds and tests. Do not directly invoke `dotnet build`, `dotnet test`, `dotnet restore`, MSBuild, VSTest, project build scripts or verification executables. If MCP transport is unavailable, use `ReignMcp/scripts/reign-validate.ps1`; CI and automation use that same non-listening engine. A direct build is permitted only to repair `ReignMcp`, with `REIGN_MCP_REPAIR=1`, targeting only that project. A busy lease is not transport failure and is not permission to bypass it.

The isolated test artifacts have no permission to mutate the visible server or a normal campaign. Continue authorized build/fix/retest cycles without another approval at each step; provider, campaign, destructive-action and tool-specific gates still apply. Select additional behavioral proof through the [testing guide](TESTING_TOOL_GUIDE.md). Compilation alone cannot prove testable behavior, and offline proof cannot establish required native or human acceptance.

## Evidence and deployment

Reports under `.codex-build/reign-mcp/validation` are authoritative. Inspect structured results and the repository-hygiene report; compilation does not override an enforce-mode hygiene failure. Report profile, tier, duration, source fingerprint, report and hygiene paths, plus material coverage gaps.

Deploy the exact artifacts from the successful report without rebuilding. For MCP deployment, follow [the deployment runbook](../../ReignMcp/docs/deployment-runbook.md). For installed Reign server replacement, retain the visible lifetime rules in `AGENTS.md`.
# Paired launch repositories

In the paired layout, the private `Reign` checkout is the MCP workspace and the sibling public `ReignServer` owns the five directories declared in `reign.repositories.json`. `ReignRelease/Connect-Repositories.ps1` creates verified source junctions. Validation retains the existing logical paths, combines source fingerprints and enforced hygiene from both repositories, and uses the same single OS lease under Reign. Unknown/misdirected junctions are not permitted. Changes to this layout, installer or validator require `all` / Tier 4. See [launch proof](testing/RELEASE_PACKAGING.md) for pre-upload clean-install requirements.
