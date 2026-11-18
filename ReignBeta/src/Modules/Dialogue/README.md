# Dialogue module (client)

Conversations. Individual NPC conversations, party chat and banter flow, correspondence and letters, personal gossip, the chat view, structured dialogue actions and dialogue audits.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `UI` | 21 |
| `Integration` | 11 |
| `Campaign` | 10 |
| `Runtime` | 5 |

Namespaces in use: `ReignBeta.UI.ViewModels (12)`, `ReignBeta.Integration (11)`, `ReignBeta.Campaign (10)`, `ReignBeta.UI (7)`, `ReignBeta.Runtime (5)`, `ReignBeta.UI.Widgets (2)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignBanterCampaignBehavior`, `ReignIndividualConversationBehavior`, `ReignPartyChatHistoryBehavior`, `ReignRescueConversationBehavior`, `ReignXpCampaignBehavior`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignChatViewClient.cs`, `Integration/ReignCommunicationClient.cs`, `Integration/ReignCorrespondenceClient.cs`, `Integration/ReignCourtDialogueClient.cs`, `Integration/ReignNpcDialogueAuditClient.cs`, `Integration/ReignPartyDialogueAuditClient.cs`, `Integration/ReignPersonalGossipClient.cs`, `Integration/ReignPregnancyContextClient.cs`, `Integration/ReignStructuredActionClient.cs`.
- Static `.Instance` singletons: `ReignBanterCampaignBehavior`, `ReignIndividualConversationBehavior`, `ReignPartyChatHistoryBehavior`, `ReignRescueConversationBehavior`, `ReignXpCampaignBehavior`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (75), Characters (44), WorldSimulation (43), Portraits (25), Persistence (11), Arena (9), Court (8), Rebellion (7), Relationships (7), UI (7), Government (4), Diplomacy (3).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Campaign/ReignLiveInteractionConversationClockHost.cs`, `Campaign/ReignLiveInteractionCorrespondenceHost.cs`.
