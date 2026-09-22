# Reign validation governance

`reign-projects.json` defines which source roots belong to active Bannerlord
Reign development. The MCP discovers every `.csproj` below those roots on each
plan or validation run. New projects therefore enter validation automatically.

Projects found outside a managed or explicitly ignored root are reported as
unclassified and make validation fail before any build starts. This is
intentional: copied contracts, deployment backups, decompiled source, legacy
projects, and third-party dependencies must never become product projects by
accident.

## Profiles

- `changed`: validates projects affected by one or more exact workspace-relative
  paths. Missing, unknown, or ambiguous paths block without running a build.
- `core`: validates the Bannerlord client, Reign server, and their verification
  executables.
- `product`: validates the complete Reign product graph (core plus MCP
  infrastructure) but excludes independently released editor, portrait, and
  migration tooling.
- `tooling`: validates MCP, editor, and native portrait tooling.
- `all`: validates every managed project and is reserved for ecosystem release,
  shared build/catalog ownership, module routing, or validator changes.

Non-test projects run `dotnet build`. Projects declaring `IsTestProject`,
referencing `Microsoft.NET.Test.Sdk`, or living below a `tests` directory run
`dotnet test`. All final outputs and TRX results are written below the unique
validation run directory. A JSON report records the plan, commands, durations,
results, and artifact locations.

When a core project is selected, the newly built isolated server also runs the
non-listening quick verification tier. Product-wide Tier 4 remains on quick
verification; only ecosystem `all` runs the complete offline tier. Its mutable test data remains inside that validation
run's artifact directory and does not touch the visible server or campaign
storage.

`reign.testing.json` is a platform/MCP contract and routes through its product
boundary rather than the ecosystem graph. `AGENTS.md` is policy-only and does
not compile code. Individual project files route to the owning module boundary;
only shared build files and project/module ownership catalogs require ecosystem
validation.

The validator holds a workspace-wide OS build lease. Call `reign_get_validation_status`
before validation and pass `requestingTaskId` for exact task attribution. The
read-only snapshot exposes the owner, scope, current operation and report path;
it never reserves, cancels or takes over work. A competing validation fails before
build work starts. Legacy or unreadable metadata can mean busy with unknown owner.
See [task coordination](../../docs/agent/TASK_COORDINATION.md).

Before starting work, the validator also looks for a successful report with the
same source fingerprint, configuration, scope, verification tier, and exact
project set. If its artifacts are still present, that report is reused instead
of rebuilding unchanged source.

## Entry points

- Codex and other MCP clients: `reign_get_validation_plan`, then
  `reign_validate`.
- CI or MCP transport recovery:
  `ReignMcp\scripts\reign-validate.ps1 -Profile changed` for ordinary work or
  `-Profile all` for an explicit ecosystem release.

The PowerShell entry point bootstraps only the validator and then executes the
same C# validation service exposed by MCP. It does not listen on a port, deploy
files, or control the visible Reign lifetime group.

## Enforcement

The repository `AGENTS.md` supplies durable policy. `.codex/hooks.json` adds a
trusted `PreToolUse` hook that rejects direct build, test, restore, publish,
MSBuild, VSTest, legacy build-script, and direct verification commands. The
message routes the agent to MCP or the shared CI fallback.

Codex requires the project and hook definition to be trusted. After a hook
change, review it through `/hooks`; trust is tied to the hook hash.
