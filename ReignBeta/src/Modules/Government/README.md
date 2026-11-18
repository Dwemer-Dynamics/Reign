# Government module (client)

Cultural governments and political parties. Government state, political parties, hearings and sessions, and the government UI.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 31 |
| `Testing` | 7 |
| `Domain` | 6 |
| `Integration` | 5 |
| `UI` | 5 |

Namespaces in use: `ReignBeta.Government (44)`, `ReignBeta.Integration (3)`, `ReignBeta.UI.ViewModels (3)`, `ReignBeta.UI (2)`, `ReignBeta.Court (1)`, `ReignBeta.Campaign (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignGovernmentCampaignBehavior`.
- Extends behaviours owned by another module through `partial class`: `ReignCourtCampaignBehavior` (Court).
- Harmony patch classes: `ReignGovernmentEdictModelPatch`, `ReignGovernmentEdictVolunteerPatch`, `ReignGovernmentNativeCallOfferPatch`, `ReignGovernmentNativeControlEnabledPatch`, `ReignGovernmentNativeControlExecutePatch`, `ReignGovernmentNativeControlReasonPatch`, `ReignGovernmentNativeDecisionScreenPatch`, `ReignGovernmentNativeDiplomacyExplanationPatch`, `ReignGovernmentNativeElectionPatch`, `ReignGovernmentNativeNoticePatch`, `ReignGovernmentNativeOutcomePatch`, `ReignGovernmentNativePeaceCallbackPatch`, `ReignGovernmentNativePeaceOfferPatch`, `ReignGovernmentNativePeaceResolvedPatch`, `ReignGovernmentNativeProposalPatch`, `ReignGovernmentNativeRelationPrivacyPatch`, `ReignGovernmentNativeTreatyOfferPatch`, `ReignGovernmentRuralIssueContextPatch`, `ReignGovernmentRuralVolunteerPatch`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignGovernmentHearingServerClient.cs`, `Integration/ReignGovernmentPoliticalClient.cs`, `Integration/ReignGovernmentServerClient.cs`.
- Static `.Instance` singletons: `ReignGovernmentCampaignBehavior`.
- Types registered with the save definer (identity must not change without save compatibility): `ReignGovernmentLobbyRecord`, `ReignGovernmentMeetingRecord`, `ReignGovernmentPartyRecord`, `ReignGovernmentPressureRecord`, `ReignGovernmentResolutionRecord`, `ReignGovernmentSeatRecord`, `ReignGovernmentStateRecord`.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (56), Characters (11), Platform (11), Court (9), Persistence (5), Diplomacy (3), Spymaster (2), Dialogue (2), Portraits (2), Rebellion (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Testing/ReignGovernmentCertificationTests.cs`, `Testing/ReignGovernmentFiefNativeTests.cs`, `Testing/ReignGovernmentHearingNativeTests.cs`, `Testing/ReignGovernmentInGameTests.cs`, `Testing/ReignGovernmentPlayerFlowTests.cs`, `Testing/ReignGovernmentPrivateConversationTests.cs`, `Testing/ReignLiveInteractionGovernmentHost.cs`.
