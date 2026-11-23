# Court module (client)

Castle court life. Courts and court life, nobles and visitors, ruler/noble/international dockets, patronage, capital ambassadors, family chambers, the training yard and the war council.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 38 |
| `UI` | 28 |
| `Integration` | 8 |
| `FamilyChambers` | 5 |
| `TrainingYard` | 4 |
| `WarCouncil` | 1 |

Namespaces in use: `ReignBeta.Court.Campaign (33)`, `ReignBeta.Court.UI (25)`, `ReignBeta.Court.Integration (8)`, `ReignBeta.WorldSimulation.Campaign (5)`, `ReignBeta.Court.FamilyChambers (4)`, `ReignBeta.UI (4)`, `ReignBeta.Court.TrainingYard (3)`, `ReignBeta.Court (2)`, `ReignBeta.Court.WarCouncil (1)`, `ReignBeta.Platform.Integration (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignCourtCampaignBehavior`, `ReignCourtNobleCampaignBehavior`, `ReignFamilyChambersCampaignBehavior`, `ReignTrainingYardCampaignBehavior`, `ReignWarCouncilCampaignBehavior`.
- `ReignServerClient` partial files (server routes for this module): `Campaign/ReignPatronageHistory.cs`, `Integration/ReignFamilyVisitClient.cs`.
- Other server clients: `Integration/ReignCastleSceneClient.cs`, `Integration/ReignCourtServerClient.cs`, `Integration/ReignInternationalCourtClient.cs`, `Integration/ReignRulerPetitionSceneClient.cs`.
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignCourtCampaignBehavior`, `ReignCourtNobleCampaignBehavior`, `ReignFamilyChambersCampaignBehavior`, `ReignWarCouncilCampaignBehavior`.
- `ReignCourtCampaignBehavior` implements World Simulation's `ICourtWorldBridge` explicitly (`Campaign/ReignCourtWorldBridge.cs`) and publishes it as `ReignServices.CourtWorld`; Court depends on World Simulation, not the reverse.
- Types registered with the save definer (identity must not change without save compatibility): `AmbassadorPosting`, `CapitalAmbassadorTestLedger`, `CastleBathHistoryRecord`, `CastleRoomSessionRecord`, `CourtAgendaItem`, `CourtCounterSample`, `CourtDecisionOption`, `CourtMatter`, `CourtObligation`, `CourtOfficeAssignment`, `CourtPlot`, `CourtRegentAssignment`, `CourtSession`, `FamilyChambersSessionRecord`, `ForeignAmbassadorPosting`, `IntelligenceOperation`, `KingdomCapitalDesignation`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (68), WorldSimulation (65), Dialogue (40), Characters (36), Portraits (23), UI (18), Rebellion (14), Arena (8), Government (7), Persistence (7), Diplomacy (6), Spymaster (5), Relationships (5).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Campaign/ReignCapitalAmbassadorInGameTests.cs`, `Campaign/ReignCourtLifeCaptiveFixture.cs`, `Campaign/ReignCourtLifeFamilyFixture.cs`, `Campaign/ReignCourtLifeInGameTests.cs`, `Campaign/ReignLiveInteractionCapitalAmbassadorHost.cs`, `Campaign/ReignLiveInteractionCourtHost.cs`, `Campaign/ReignLiveInteractionCourtLifeHost.cs`, `Campaign/ReignLiveInteractionRoyalCouncilHost.cs`, `Campaign/ReignLiveInteractionRulerDocketHost.cs`, `Campaign/ReignNobleDocketInGameTests.cs`, `Campaign/ReignRoyalCouncilInGameTests.cs`, `Campaign/ReignRulerDocketInGameTests.cs`, `Campaign/ReignRulerDocketLaunchTests.cs`.
