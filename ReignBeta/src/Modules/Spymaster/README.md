# Spymaster module (client)

Spymaster. The spymaster court office: missions, reports and their UI. Compiled out with Court when `ExcludeCourt=true`.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the target structure.

## Layout

| Folder | C# files |
|---|---|
| `Campaign` | 8 |
| `UI` | 2 |
| `Integration` | 1 |

Namespaces in use: `ReignBeta.Court (7)`, `ReignBeta.Campaign (1)`, `ReignBeta.Integration (1)`, `ReignBeta.UI (1)`, `ReignBeta.UI.ViewModels (1)`.

## Public surface

- Extends behaviours owned by another module through `partial class`: `ReignCourtCampaignBehavior` (Court).
- Other server clients: `Integration/ReignSpymasterServerClient.cs`.

## Dependencies

Other client modules whose types this module references, most-used first: Court (22), WorldSimulation (7), Characters (6), Platform (5), UI (1), Rebellion (1), Portraits (1).

Counts are distinct type names referenced per file, summed; they show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (see restructuring step C9): `Campaign/ReignLiveInteractionSpymasterHost.cs`, `Campaign/ReignSpymasterGovernmentCertificationFixture.cs`, `Campaign/ReignSpymasterInGameTests.cs`, `Campaign/ReignSpymasterOrganicInGameTests.cs`, `Campaign/ReignSpymasterTestRuntime.cs`.
