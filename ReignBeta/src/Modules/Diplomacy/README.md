# Diplomacy module (client)

Kingdom diplomacy. World diplomacy decisions and clan accords.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `UI` | 6 |
| `World` | 5 |
| `ClanAccords` | 3 |
| `Campaign` | 2 |
| `Integration` | 2 |
| `Runtime` | 1 |

Namespaces in use: `ReignBeta.World (5)`, `ReignBeta.UI (4)`, `ReignBeta.Campaign (3)`, `ReignBeta.ClanAccords (3)`, `ReignBeta.Integration (2)`, `ReignBeta.UI.ViewModels (2)`, `ReignBeta.Runtime (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignClanAccordsCampaignBehavior`, `ReignWorldDiplomacyCampaignBehavior`.
- Extends behaviours owned by another module through `partial class`: `ReignAICampaignBehavior` (Platform).
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignClanAccordsClient.cs`, `Integration/ReignDiplomacyServerClient.cs`.
- Static `.Instance` singletons: `ReignClanAccordsCampaignBehavior`, `ReignWorldDiplomacyCampaignBehavior`.
- Types registered with the save definer (identity must not change without save compatibility): `ReignDiplomaticAgreementRecord`, `ReignTreatyObligationRecord`, `ReignWarOriginRecord`.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (30), Platform (17), Characters (6), Rebellion (5), Dialogue (3), Arena (3), Persistence (2), Government (2), Court (2), UI (1), Portraits (1), Relationships (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `ClanAccords/ReignClanAccordNativeTests.cs`.
