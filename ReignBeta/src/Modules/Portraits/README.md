# Portraits module (client)

Portraits. AI portrait generation and caching (`AIPortraits`), native portrait patches and portrait widgets.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `AIPortraits` | 33 |
| `UI` | 7 |
| `Integration` | 5 |

Namespaces in use: `AIPortraits (33)`, `ReignBeta.Integration (5)`, `ReignBeta.UI.Portraits (2)`, `ReignBeta.UI.Widgets (2)`, `ReignBeta.UI (1)`, `ReignBeta.UI.EventArt (1)`, `ReignBeta.UI.Tavern (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `ConversationBehavior`, `PreloadCampaignBehavior`.
- Harmony patch classes: `CharacterTableauRenderPatch`, `OnRenderPatch`, `PortraitZoomButtonPatch`, `TextureSetterPatch`.
- Other server clients: `Integration/ReignOutfitClient.cs`.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (30), Characters (18), Arena (12), Dialogue (3), WorldSimulation (2), UI (1), Rebellion (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.
