# Relationships module (client)

Relationships. The relationship director, realtime relationship updates and player-approach behaviour.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 6 |
| `Integration` | 3 |
| `UI` | 2 |

Namespaces in use: `ReignBeta.Campaign (6)`, `ReignBeta.Integration (3)`, `ReignBeta.UI (2)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignPlayerApproachCampaignBehavior`, `ReignRelationshipCampaignBehavior`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignPlayerApproachClient.cs`, `Integration/ReignRelationshipDirectorClient.cs`, `Integration/ReignRelationshipRealtimeClient.cs`.
- Static `.Instance` singletons: `ReignPlayerApproachCampaignBehavior`, `ReignRelationshipCampaignBehavior`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (25), WorldSimulation (18), Characters (17), Dialogue (10), Persistence (7), Arena (4), Court (2), Diplomacy (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.
