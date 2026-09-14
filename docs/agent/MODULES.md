# Reign module catalog

`/reign.modules.json` is authoritative for source ownership, module dependencies, project selection, and module-specific verification commands. `/reign-projects.json` remains authoritative for active project discovery and build ownership.

Each logical module declares one or more `domain`, `server`, `bannerlord`, `harness`, `mcp`, or `tooling` facets. Each facet declares:

- `ownership`: repository-relative globs owned by exactly one facet.
- `projects` and `testProjects`: catalog IDs from `reign-projects.json`.
- `defaultTier`: the ordinary implementation tier for that facet.
- `focusedPaths`, `contractPaths`, `integrationPaths`, and `tier4Paths`: escalation rules.
- `validation`: focused or subsystem verification operations that run after successful builds.

Logical modules also declare `dependsOn` and explicit Tier 3 `boundaryModules`, projects, tests, and operations. Tier 3 expands only through those declared boundaries, rather than compiling the whole transitive graph.

Manifest enforcement is `enforce`. Unknown and overlapping code paths block immediately with no tier and no selected projects; they never fall back to Tier 4. A file added beneath an existing subsystem folder inherits its facet ownership. A genuinely new subsystem must be registered in the manifest, and that manifest edit receives one Tier 4 validation.

Current pure libraries:

- `Reign.Core.Contracts`: Bannerlord-independent shared relationship baseline contract.
- `Reign.Relationships`: compatibility calculations with exhaustive deterministic unit tests.
- `Reign.LegacySqliteImporter`: isolated one-way migration utility; it is tooling, not a runtime module.

Folder-owned logical modules are Core, Persistence, Characters, Relationships, Reputation, Dialogue, Diplomacy, Court, Spymaster, Kingdom Events, Rebellion, World Simulation, Portraits, UI, Platform, Bannerlord Editor, and Legacy Importer.
