# Dependency policy

Dependencies point inward toward contracts and pure domain logic.

| Layer | May depend on | Must not depend on |
|---|---|---|
| Core contracts | .NET base libraries | PostgreSQL, HTTP, TaleWorlds, UI |
| Domain modules | Core contracts, other explicitly declared domain contracts | TaleWorlds, server process state, file layout |
| Persistence | Core contracts, Npgsql | TaleWorlds UI/runtime |
| Server orchestration | Contracts, domain modules, persistence adapters | Bannerlord assemblies |
| Bannerlord adapter | Contracts, client transport, TaleWorlds | Server implementation internals |

When a dependency is added or reversed, update `reign.modules.json` in the same change and run Tier 4. Tier 3 expands only to the module's explicit `boundaryModules` and boundary project/validation sets.

Contract tests should prove shapes, ranges, error semantics, and serialization compatibility. Consumer tests should rely on those contracts instead of reconstructing the producer implementation.
