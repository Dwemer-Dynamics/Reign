# Arena module (client)

Arena and tournaments. The vendored ArenaOverhaul sub-project (its own `ArenaOverhaul.csproj`, referenced by `ReignBeta.csproj`) plus Reign's tournament integration.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the target structure.

## Layout

| Folder | C# files |
|---|---|
| `ArenaOverhaul` | 67 |

Namespaces in use: `ArenaOverhaul.TeamTournament (12)`, `ArenaOverhaul.Patches (11)`, `ArenaOverhaul.Tournament (8)`, `ArenaOverhaul.Helpers (6)`, `ArenaOverhaul.ModSettings (6)`, `ArenaOverhaul.TeamTournament.ViewModels (5)`, `ArenaOverhaul.ArenaPractice (4)`, `ArenaOverhaul (2)`, `ArenaOverhaul.Extensions (2)`, `ArenaOverhaul.TeamTournament.Patches (2)`, `ArenaOverhaul.ViewModelMixin (2)`, `ArenaOverhaul.CampaignBehaviors (1)`, `ArenaOverhaul.CampaignBehaviors.BehaviorManagers (1)`, `ArenaOverhaul.Extensions.Harmony (1)`, `ArenaOverhaul.Models (1)`.

## Public surface

- Campaign behaviours deriving from `CampaignBehaviorBase` (all registered in `Platform/SubModule.cs`): `AOArenaBehavior`.
- Harmony patch classes: `ArenaMasterPatch`, `ArenaPracticeFightMissionControllerPatch`, `DefaultCombatXpModelPatch`, `DefaultTournamentModelPatch`, `FightTournamentGamePatch`, `MissionStarterOpenTournamentFightMissionPatch`, `TournamentBehaviorPatch`, `TournamentCampaignBehaviorPatch`, `TournamentFightMissionControllerPatch`, `TournamentGamePatch`, `TournamentManagerPatch`, `TournamentMissionViewsOpenTournamentFightMissionPatch`, `TournamentVMPatch`.
- Projects: `ArenaOverhaul/ArenaOverhaul.csproj`.

## Dependencies

None inside `ReignBeta/src/Modules`.
