# Reign database architecture

PostgreSQL is the sole production persistence engine. The required databases are `reign` for the installed product and `ReignValidation` for isolated verification. Campaigns are isolated by schemas; Save Sync uses PostgreSQL snapshot schemas and restores schemas transactionally.

## Invariants

- Production server and Bannerlord deployment must not reference `Microsoft.Data.Sqlite`, SQLitePCL, or ship `e_sqlite3.dll`.
- Startup never searches for or automatically imports SQLite databases.
- Schema creation and evolution happen through PostgreSQL connections and are checked with `information_schema` or PostgreSQL catalog queries.
- Database/schema changes require Tier 4 validation. Behavioral changes that only query an unchanged repository contract use the owning subsystem tier.
- Validation payloads fail closed: a verifier process returning JSON without top-level `ok: true` or `passed: true` is a failed validation.

## Retired SQLite import

The one-way SQLite campaign importer has been removed. New and existing supported installations use PostgreSQL. Startup never imports old SQLite files. PostgreSQL SQL translation, campaign backups and Save Sync compatibility remain active and must not be removed as importer cleanup.
