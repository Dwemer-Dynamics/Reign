# WorldSimulation module (client)

World simulation. World actions and the action gauntlet, arrests, campaign commands, combat, economy, news, party agency, rulers and world history.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Economy` | 26 |
| `World` | 22 |
| `Campaign` | 14 |
| `Actions` | 11 |
| `PartyAgency` | 7 |
| `Integration` | 6 |
| `Rulers` | 6 |
| `CampaignCommands` | 5 |
| `Events` | 5 |
| `Runtime` | 5 |
| `Arrests` | 4 |
| `News` | 4 |
| `UI` | 3 |
| `Domain` | 2 |
| `Combat` | 1 |

Namespaces in use: `ReignBeta.WorldSimulation.Economy (26)`, `ReignBeta.WorldSimulation.World (21)`, `ReignBeta.WorldSimulation.Campaign (16)`, `ReignBeta.WorldSimulation.Actions (10)`, `ReignBeta.WorldSimulation.PartyAgency (6)`, `ReignBeta.WorldSimulation.Rulers (6)`, `ReignBeta.WorldSimulation.CampaignCommands (5)`, `ReignBeta.WorldSimulation.Integration (5)`, `ReignBeta.WorldSimulation.Runtime (5)`, `ReignBeta.WorldSimulation.Domain (4)`, `ReignBeta.WorldSimulation.Events (4)`, `ReignBeta.WorldSimulation.News (4)`, `ReignBeta.WorldSimulation.UI (3)`, `ReignBeta.World (2)`, `ReignBeta.WorldSimulation.Arrests (2)`, `ReignBeta.Actions (1)`, `ReignBeta.Campaign (1)`, `ReignBeta.Events (1)`, `ReignBeta.PartyAgency (1)`, `ReignBeta.Platform.Integration (1)`, `ReignBeta.WorldSimulation.Combat (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignAgreementBehavior`, `ReignArrestCampaignBehavior`, `ReignCampaignCommandBehavior`, `ReignEconomyCampaignBehavior`, `ReignFollowCampaignBehavior`, `ReignGoldTransferService`, `ReignNewsCampaignBehavior`, `ReignPersonalItemService`, `ReignRulerCampaignBehavior`, `ReignSocialEventsCampaignBehavior`, `ReignTemporaryPartyGuestCampaignBehavior`, `ReignWorldHistoryCampaignBehavior`.
- Harmony patch classes: `DaysInSeasonPatch`, `DaysInYearPatch`, `ElapsedYearsPatch`, `GetDayOfSeasonPatch`, `GetDayOfYearPatch`, `GetSeasonOfYearPatch`, `GetWeekOfSeasonPatch`, `GetYearPatch`, `HeroAgePatch`, `HeroBirthDayPatch`, `MapDatePatch`, `RemainingYearsPatch`, `ToYearsPatch`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignCampaignCommandClient.cs`, `Integration/ReignSocialEventClient.cs`, `Integration/ReignWorldHistoryClient.cs`, `Integration/ReignWorldTestClient.cs`, `News/ReignNewsClient.cs`.
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignAgreementBehavior`, `ReignArrestCampaignBehavior`, `ReignCampaignCommandBehavior`, `ReignEconomyCampaignBehavior`, `ReignFollowCampaignBehavior`, `ReignGoldTransferService`, `ReignNewsCampaignBehavior`, `ReignPersonalItemService`, `ReignRulerCampaignBehavior`, `ReignSocialEventsCampaignBehavior`, `ReignTemporaryPartyGuestCampaignBehavior`, `ReignWorldHistoryCampaignBehavior`.
- `Domain/ICourtWorldBridge.cs`: everything World Simulation needs from Court, reached through `ReignServices.CourtWorld` (null when Court is absent). `Domain/ReignHouseholdClanMigration.cs`: a flag Court sets during household clan migration. Product code in this module names no Court or Spymaster type; only the test hosts listed below do.
- Types registered with the save definer (identity must not change without save compatibility): `ReignArrestCase`, `ReignNegotiatedActionRecord`, `ReignTemporaryPartyGuestRecord`, `ReignWorldActionRecord`, `SocialEventRecord`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (95), Characters (83), Diplomacy (42), Dialogue (42), Court (36), Rebellion (24), Government (18), Persistence (17), Arena (15), Relationships (13), Portraits (10), Spymaster (6), KingdomEvents (5), Reputation (4), UI (3).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Arrests/ReignArrestTestHost.cs`, `Campaign/ReignActionGauntlet.cs`, `Campaign/ReignLiveInteractionCampaignCommandHost.cs`, `Campaign/ReignLiveInteractionGauntletFixtures.cs`, `Campaign/ReignLiveInteractionNativeUiCalibrationHost.cs`, `Campaign/ReignLiveInteractionPartyAgencyFixtures.cs`, `Campaign/ReignLiveInteractionPassiveWorldHost.cs`, `Campaign/ReignLiveInteractionPersonalGossipHost.cs`, `Campaign/ReignLiveInteractionStructuredActionHost.cs`, `Campaign/ReignLiveInteractionTestHost.cs`, `Campaign/ReignLiveInteractionUiCalibrationHost.cs`, `Economy/ReignNpcCaravanTestHost.cs`, `Economy/ReignRaidFoodTestHost.cs`, `Economy/ReignWarEconomyNavalTestHost.cs`, `Economy/ReignWarEconomyTestHost.cs`, `Integration/ReignWorldTestClient.cs`, `Rulers/ReignRulerFixtures.cs`, `Rulers/ReignRulerPrisonerFixtures.cs`.
