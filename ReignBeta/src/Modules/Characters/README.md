# Characters module (client)

Hero identity and character life. Hero profiles and identity sync, family, character knowledge, wanderers, NPC lives and storylines, tavern houses, notables, and the character editor.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 43 |
| `Integration` | 7 |
| `Family` | 4 |
| `UI` | 3 |
| `Actions` | 1 |
| `Domain` | 1 |
| `Knowledge` | 1 |
| `World` | 1 |

Namespaces in use: `ReignBeta.Characters.Campaign (39)`, `ReignBeta.Characters.Integration (7)`, `ReignBeta.WorldSimulation.Campaign (4)`, `ReignBeta.Characters.Family (3)`, `ReignBeta.Characters.UI (2)`, `ReignBeta.Characters.Actions (1)`, `ReignBeta.Characters.Domain (1)`, `ReignBeta.Characters.World (1)`, `ReignBeta.Family (1)`, `ReignBeta.Knowledge (1)`, `ReignBeta.UI (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignCharacterEditorCampaignBehavior`, `ReignCharacterKnowledgeCampaignBehavior`, `ReignEncounteredResidentsCampaignBehavior`, `ReignFamilyCampaignBehavior`, `ReignNativeStartingAgeCampaignBehavior`, `ReignNpcLivesBehavior`, `ReignRecruitedNotableCampaignBehavior`, `ReignStartingChildrenCampaignBehavior`, `ReignTavernHouseCampaignBehavior`, `ReignWandererPopulationCampaignBehavior`.
- Harmony patch classes: `NpcMeetingCharacterStayPatch`, `NpcMeetingPartySpawnPatch`, `NpcMeetingPartyStayPatch`, `ReignNpcStoryRescuePrisonerPatch`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignCharacterEditorClient.cs`, `Integration/ReignNpcLivesClient.cs`, `Integration/ReignNpcStoryClient.cs`, `Integration/ReignTavernHouseClient.cs`, `Integration/ReignTownNotableProfileClient.cs`, `Integration/ReignWandererClient.cs`.
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignCharacterEditorCampaignBehavior`, `ReignCharacterKnowledgeCampaignBehavior`, `ReignEncounteredResidentsCampaignBehavior`, `ReignFamilyCampaignBehavior`, `ReignNpcLivesBehavior`, `ReignStartingChildrenCampaignBehavior`, `ReignTavernHouseCampaignBehavior`, `ReignWandererPopulationCampaignBehavior`.
- Types registered with the save definer (identity must not change without save compatibility): `ReignCharacterKnowledgeRecord`, `ReignConceptionRecord`, `ReignParentageRecord`.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (77), Platform (58), Dialogue (23), Persistence (16), Relationships (8), Portraits (7), Arena (6), Diplomacy (4), Rebellion (2), UI (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Campaign/ReignEncounteredResidentNativeTestHost.cs`, `Campaign/ReignNpcLivesAnnouncementFixture.cs`, `Campaign/ReignNpcLivesComposerFixture.cs`, `Campaign/ReignNpcLivesTestHost.cs`.
