# Reputation module (client)

Reputation. Combat, ruler and court-personality reputation behaviours.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 6 |
| `Domain` | 1 |

Namespaces in use: `ReignBeta.Campaign (7)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignCombatReputationCampaignBehavior`, `ReignCourtPersonalityReputationCampaignBehavior`, `ReignRulerReputationCampaignBehavior`, `ReignSocialBalanceHarnessCampaignBehavior`, `ReignWhoremongerCampaignBehavior`.
- Static `.Instance` singletons: `ReignCourtPersonalityReputationCampaignBehavior`, `ReignRulerReputationCampaignBehavior`, `ReignSocialBalanceHarnessCampaignBehavior`.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (10), Characters (9), Platform (8), Persistence (5), Rebellion (3), Arena (1), Diplomacy (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Campaign/ReignLiveInteractionSocialBalanceHost.cs`, `Campaign/ReignSocialBalanceHarnessCampaignBehavior.cs`.
