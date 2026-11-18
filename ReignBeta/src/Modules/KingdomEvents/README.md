# KingdomEvents module (client)

Kingdom events. Kingdom-wide events raised from campaign state.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 3 |
| `Events` | 1 |

Namespaces in use: `ReignBeta.KingdomEvents.Campaign (2)`, `ReignBeta.Events (1)`, `ReignBeta.WorldSimulation.Campaign (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignKingdomEventsCampaignBehavior`.
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignKingdomEventsCampaignBehavior`.
- Types registered with the save definer (identity must not change without save compatibility): `ReignKingdomEventRecord`, `ReignKingdomEventTestLedger`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (7), Persistence (4), Characters (4), WorldSimulation (3), Diplomacy (2), Rebellion (1), Arena (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Campaign/ReignKingdomEventInGameTests.cs`, `Campaign/ReignLiveInteractionKingdomEventHost.cs`.
