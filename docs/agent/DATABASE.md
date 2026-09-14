# Reign database architecture

PostgreSQL is the sole production persistence engine. The required databases are `Reign` for the installed product and `ReignValidation` for isolated verification. Campaigns are isolated by schemas; Save Sync uses PostgreSQL snapshot schemas and restores schemas transactionally.

## Invariants

- Production server and Bannerlord deployment must not reference `Microsoft.Data.Sqlite`, SQLitePCL, or ship `e_sqlite3.dll`.
- Startup never searches for or automatically imports SQLite databases.
- Schema creation and evolution happen through PostgreSQL connections and are checked with `information_schema` or PostgreSQL catalog queries.
- Database/schema changes require Tier 4 validation. Behavioral changes that only query an unchanged repository contract use the owning subsystem tier.
- Validation payloads fail closed: a verifier process returning JSON without top-level `ok: true` or `passed: true` is a failed validation.

## One-way legacy import

`ReignTools/Reign.LegacySqliteImporter` is the only component allowed to reference SQLite. It opens the source read-only, requires an already initialized and empty PostgreSQL campaign schema, runs in a serializable transaction under a campaign advisory lock, verifies row counts, and never deletes the source.

Example (run deliberately, outside normal server startup):

```powershell
dotnet run --project ReignTools/Reign.LegacySqliteImporter -- --source C:\path\world_memory.sqlite --campaign-id campaign_id --confirm "import legacy Reign SQLite"
```

Build and verification are still performed through the Reign MCP workflow; the example is the operator invocation after a verified tool build.
