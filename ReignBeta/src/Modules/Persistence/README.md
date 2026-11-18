# Persistence module (client)

Save integration. The Bannerlord `SaveableTypeDefiner` (`Save/ReignBetaSaveDefiner.cs`) and Save Sync with the server (`ReignSaveSyncCampaignBehavior`, `ReignSaveSyncCoordinator`).

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Integration` | 2 |
| `Save` | 2 |
| `Campaign` | 1 |

Namespaces in use: `ReignBeta.Persistence.Integration (2)`, `ReignBeta.Persistence.Save (2)`, `ReignBeta.Persistence.Campaign (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignSaveSyncCampaignBehavior`.
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignSaveSyncCampaignBehavior`.

## Dependencies

Other client modules whose types this module references, most-used first: Court (17), WorldSimulation (10), Platform (8), Government (7), Characters (6), Rebellion (6), Diplomacy (3), KingdomEvents (2).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.
