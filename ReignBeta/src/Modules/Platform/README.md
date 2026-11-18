# Platform module (client)

Mod entry point and shared runtime. `SubModule.cs` (behaviour and Harmony registration), the core of the `ReignServerClient` HTTP client, settings, compatibility shims, campaign preparation and the main AI campaign behaviour.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `Integration` | 15 |
| `Compatibility` | 5 |
| `Campaign` | 2 |
| `Runtime` | 2 |
| `(root)` | 1 |
| `Domain` | 1 |
| `Settings` | 1 |

Namespaces in use: `ReignBeta.Platform.Integration (18)`, `ReignBeta.Platform.Compatibility (4)`, `ReignBeta.Platform.Runtime (4)`, `ReignBeta.Platform.Campaign (2)`, `ReignBeta.Platform.Domain (2)`, `AIEventsAndIntrigue.Settings (1)`, `ReignBeta (1)`, `ReignBeta.Platform.Settings (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ReignAICampaignBehavior`, `ReignCampaignPreparationCampaignBehavior`.
- `ReignServerClient` partial files (server routes for this module): `Integration/ReignLiveTestClient.cs`, `Integration/ReignRulerClient.cs`, `Integration/ReignServerClient.Banter.cs`, `Integration/ReignServerClient.cs`, `Integration/ReignXpClient.cs`.
- Other server clients: `Integration/ReignInitializationServerClient.cs`.
- `AIEventsSettings.Instance` stays a static adapter (not in the registry).
- Behaviours registered in `ReignServices` (their `Instance` property forwards to the slot): `ReignAICampaignBehavior`, `ReignCampaignPreparationCampaignBehavior`.
- `Runtime/ReignServices.cs`: the typed static service registry for every client behaviour that used a static `Instance`. Each behaviour writes its slot where it used to set `Instance`; nothing fills or clears slots on game start or end.

## Dependencies

Other client modules whose types this module references, most-used first: WorldSimulation (77), Characters (49), Portraits (23), Dialogue (21), Diplomacy (21), Court (19), Persistence (10), Relationships (9), Reputation (7), Arena (7), Government (7), UI (6), Rebellion (2), KingdomEvents (1), Spymaster (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Integration/ReignLiveTestClient.cs`.
