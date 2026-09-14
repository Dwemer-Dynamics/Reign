# Bannerlord Reign

Bannerlord Reign is the ReignBeta module for Mount & Blade II: Bannerlord. The beta module id remains `ReignBeta`, while the user-facing systems are branded as Bannerlord Reign.

This repo contains the game-side module: Gauntlet UI, MCM settings, action routing, resolvers, validators, action execution, portrait integration, chat surfaces, gauntlets, and test harness hooks.

Native campaign history, knowledge delivery, and evidence-backed claim verification are documented in [docs/WorldHistory.md](docs/WorldHistory.md).
Managed local embeddings, Qdrant integration, indexing, privacy filtering, and hybrid recall are documented in [docs/SemanticMemory.md](docs/SemanticMemory.md).
The server-side Offline-First Verification Lab and the client game-tier bridge are documented in the Reign Server repository at `docs/VerificationLab.md`.

The local web/API server lives in the separate `Reign-Server` repository. Runtime server builds, campaign data, API keys, logs, generated portraits, and per-save state are intentionally ignored here.

## Build

```powershell
dotnet build .\ReignBeta.csproj -c Release
```

The live Bannerlord install target used during development is:

```text
D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta
```

## Notes

- Do not commit API keys or `server/app/data/settings.json`.
- Generated campaign folders and audit logs are save-specific runtime data.
- `tools/Overnight` contains the restart/repair loop helpers for long live dialogue action testing.
