# Rebellion module (client)

Rebellion. Rebellion detection, uprisings and their campaign effects.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the target structure.

## Layout

| Folder | C# files |
|---|---|
| `World` | 6 |
| `Campaign` | 2 |
| `Integration` | 1 |
| `Testing` | 1 |

Namespaces in use: `ReignBeta.World (6)`, `ReignBeta.Campaign (3)`, `ReignBeta.Integration (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignRebellionCampaignBehavior`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignRebellionServerClient.cs`.
- Static `.Instance` singletons: `ReignRebellionCampaignBehavior`.
- Types registered with the save definer (identity must not change without save compatibility): `ReignRebellionMembershipRecord`, `ReignRebellionMovementRecord`, `ReignRebellionPledgeRecord`, `ReignRebellionPlotRecord`, `ReignRebellionSummonsRecord`, `ReignRebellionWeeklyRollRecord`.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (8), Platform (7), Characters (4), Dialogue (2), Diplomacy (1).

Counts are distinct type names referenced per file, summed; they show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (see restructuring step C9): `Testing/ReignRebellionPreparationTestHost.cs`.
