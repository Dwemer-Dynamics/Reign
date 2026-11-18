# UI module (client)

Shared UI and runtime assets. Shared widgets and view models, UI calibration, typography, main-menu patches, hidden-information policy, tavern and event art, and runtime sprite sheets.

> Generated from the source tree on 2026-11-17 and checked by hand. `reign.modules.json` stays authoritative for ownership and validation routing; `docs/agent/ARCHITECTURE.md` describes the current structure and its direction.

## Layout

| Folder | C# files |
|---|---|
| `(root)` | 5 |
| `HiddenInformation` | 4 |
| `Tavern` | 4 |
| `Calibration` | 2 |
| `GameMenu` | 2 |
| `ViewModels` | 2 |
| `Widgets` | 2 |
| `EventArt` | 1 |

Namespaces in use: `ReignBeta.UI (5)`, `ReignBeta.UI.HiddenInformation (4)`, `ReignBeta.UI.Tavern (4)`, `ReignBeta.UI.Calibration (2)`, `ReignBeta.UI.GameMenu (2)`, `ReignBeta.UI.ViewModels (2)`, `ReignBeta.UI.Widgets (2)`, `ReignBeta.UI.EventArt (1)`.

## Public surface

- Harmony patches and runtime services at the module root: typography, main-menu logo and video, popup keyboard, runtime sprite sheets.

## Dependencies

Other client modules whose types this module references, most-used first: Platform (9), Portraits (6), Arena (3), Rebellion (1).

Counts are approximate: distinct type names referenced per file, matched by name and summed. They show coupling, not a layering rule.

## In-game test harness shipped in this module

These files belong to the test harness and are compiled into the mod today (ARCHITECTURE.md, restructuring direction step 3 moves them out of the product): `Calibration/ReignUiCalibrationCanvasWidget.cs`, `Calibration/ReignUiCalibrationService.cs`, `ViewModels/ReignUiCalibrationVM.cs`.
