# War Council Map Task Handoff — 2026-08-23

## Purpose and authority

This document is the durable handoff from Codex task `019ffe63-46a8-7313-84b0-2ff1f496d85e` to a fresh task. The original task became too large and unreliable in the desktop/mobile clients. The receiving task must read the entire current `C:\Users\speed\Documents\Bannerlord Events\AGENTS.md` before taking any action and must treat the current filesystem, installed game/editor state, MCP evidence, and this document as authoritative.

Do not restart the feature from scratch. Preserve unrelated worktree changes and continue from the current sources and evidence. Re-observe mutable external state before acting.

## Updated autonomous goal

Set and pursue this goal until its verifiable completion condition is met:

> Autonomously finish rebuilding, integrating, deploying, and launch-validating the Reign War Council system, centered on one seamless 16,384×16,384 authored parchment master exported as exactly sixteen 4096×4096 runtime tiles. All static cartography—including coastlines, rivers, roads, bridges, exact mountains and forests, geographic detail, settlement illustrations and labels, parchment wear, scroll edges, folds, and decorative clutter—must be baked into the raster and must match the installed Bannerlord/War Sails `NavalDLC/Main_map` terrain and locations. Runtime overlays are limited to genuinely dynamic parties, detection/selection state, tooltips, and invisible clickable regions. Complete MCP/harness/native disposable-save evidence must prove geographic accuracy, visual quality, seamlessness, UI behavior, performance, lifecycle safety, save/reload behavior, and launch readiness.

The goal is not complete until the final deployed game build has been inspected natively and every required acceptance item below has authoritative evidence.

## Critical architecture decision — do not regress

The user explicitly chose pre-rendered baked terrain over hundreds or thousands of small runtime terrain widgets.

- Create one seamless 16,384×16,384 master image.
- Render all static terrain and settlements into that master in one global coordinate system.
- Split the finished master into a 4×4 grid of exactly sixteen 4096×4096 tiles.
- The sixteen tiles must losslessly reconstruct the master and show no seams.
- Do not create runtime tree, mountain, bridge, road, river, settlement-art, settlement-label, wear, scroll-edge, fold, or clutter widgets.
- Only live mobile parties, detection/selection effects, tooltips, and invisible settlement/party click regions may be separate runtime objects.
- If supported safely by Gauntlet, only visible tiles should remain active; do not weaken correctness merely to add tile streaming.
- Use an appropriate Bannerlord-compatible compressed runtime texture format after the lossless source raster is accepted.
- Remove or stop registering obsolete extra tile assets after the final 4×4 set is proven. The current sprite directory still contains rejected `tile_0_0` through `tile_7_7` leftovers, while the active widget constant is already 4×4.

## Geographic accuracy is non-negotiable

The rejected build placed circular forest stamps across plausible climate zones. This is wrong. The final map must reproduce the actual installed game geography rather than inventing terrain.

- Forest coverage, density, shape, and transitions must come from official editor/scene terrain-layer or flora evidence. Forests should read as natural individually engraved trees distributed through the real forested regions, never circular stamps.
- Mountain chains, ridges, passes, snow regions, and foothills must follow the actual `Main_map` elevation/material terrain at the correct coordinates. Do not scatter decorative mountain motifs on a generic elevation threshold.
- Coastlines, islands, lakes, rivers, deltas, and navigable water must match the installed map.
- Roads and every bridge/crossing must be baked at their real positions and orientations. Parse official scene entities for bridge transforms and deduplicate culture-variant or nested duplicates.
- Settlements must use audited live coordinates and must never appear in water.
- Outer mesh is excluded. Use only the actual playable map rectangle.
- The editor is the preferred authority for missing terrain-layer information. Load `NavalDLC/Main_map` read-only, export the relevant terrain material maps, record layer names and hashes, and do not save or modify the scene.
- Image generation may author reusable ink style, parchment, wear, and decorative motifs, but it must never invent geographic placement.

## Installed authoritative sources

The source manifest with exact SHA-256 hashes is:

`C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-task-handoff-20260823\authoritative-source-manifest.json`

Primary installed sources:

- Scene XML: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\NavalDLC\SceneObj\Main_map\scene.xscene`
- Terrain edit data: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\NavalDLC\SceneEditData\Main_map\terrain_ed.bin`
- Compiled flora: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\NavalDLC\SceneObj\Main_map\flora.bin`
- Audited settlements: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v4\evidence\settlement-coordinate-audit.csv`
- Editor terrain capture: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v4\evidence\terrain-1025-export\heightmap-1025x1025-y-up-f32le.bin`
- Official packed 4096 height texture: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v5c\evidence\editor-captures\world_map_heightmap.png`
- Official river mask: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v5c\evidence\editor-captures\worldmap_river_mask.png`
- Official top-down captures: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v5c\evidence\editor-captures\official-main-map-centered-top.png` and `official-playable-crop.png`
- Prior read-only editor proof: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v4\evidence\editor-route\main-map-live-readonly-proof-20260823-042934.json`

Known playable rectangle already measured from official border entities:

- `minX = 13.353`
- `minY = 6.733`
- `maxX = 992.535`
- `maxY = 937.948`
- nominal world size `1040.0`

These constants are currently in `BannerlordEditorMcp/scripts/render-war-council-map.py`; re-audit rather than silently changing them.

## Stable visual references

The original temporary user attachments have been copied into:

`C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-task-handoff-20260823\references`

Their hashes are in `reference-manifest.json` in that directory.

Key references:

- `codex-clipboard-fb17daf3-fe25-4b26-aea1-c0ad11b1dc06.png`: rejected current build with circular forest blobs.
- `codex-clipboard-f7535eb7-10ee-465c-b104-31fc540c53b2.png`: target natural engraved forest/mountain treatment on worn parchment.
- `codex-clipboard-af14ec5e-9485-4cef-af66-0d4df0b3e45f.png`: actual in-game regional terrain and bridge placement ground truth around central/southern Calradia.
- `codex-clipboard-584d244f-8efa-4c56-9d47-38952fa0c272.png`: preferred sepia color, fine engraving, and parchment treatment.
- `codex-clipboard-a3e51176-6498-4bf8-83e1-0f107138dc00.png`: required line sharpness and thin detail at the final close viewing scale.
- `codex-clipboard-4927bd4b-b76e-4de0-840b-0913de09cb65.png`: general medieval cartographic vocabulary.
- `codex-clipboard-3c5d8b81-80f1-48da-90fa-dedb8d4676fe.png`: rejected design where the scroll frame sat inside/over the map.
- `codex-clipboard-2fccf87c-846a-4a0e-96ba-e66cbe3da928.png`: broad in-game map reference, not sufficient authority for exact terrain.
- `1-Photo-1.jpg`: rejected dense-token/settlement diamond state and earlier water-tree problem.
- `ChatGPT Image Aug 22, 2026, 09_24_02 PM.png`: latest preferred wooden miniature family reference; only unit figures remain dynamic.

## Art direction

- Sepia/ink, finely engraved, ancient documentary-map appearance.
- Photoreal worn parchment rather than a clean computer print.
- Crisp thin tree, mountain, river, coast, road, settlement, and lettering lines at the final close viewing scale; no blurred upscaling or thick blocky lines.
- Wear, stains, folds, tears, scroll edges, and tasteful medieval cartographer clutter are baked into the master so they align across tiles.
- Scroll edges and clutter become visible only after panning to the outer bounds.
- The scrolling map sits inside a separate non-scrolling Reign court-style black/gold frame. The frame must surround the viewport, not overlap the map.
- The screen uses the whole available screen area, with a large map viewport and a right-hand roster/council column.
- Current design direction is a single close strategic scale with click-drag panning; prior multi-level zoom/dot behavior was superseded by the user's fixed close-view simplification. Do not reintroduce variable zoom without confirming a later explicit requirement.
- Preserve the successful level of detail achieved in the latest run at the actual in-game close viewing scale. The terrain rebuild is required to correct geographic placement and natural terrain composition; it must not reduce the existing line density, fine settlement detail, label legibility, or crisp appearance at that scale.
- Treat native-resolution final-scale crops—not a downsampled overview—as the visual-quality authority. Capture repeatable crops from the current/latest build before replacement when possible, then compare the same mapped regions after the rebuild so line weight and detail do not regress.

Current source art:

- `ReignBeta/artwork/war-council-map-sources/worn-scroll-parchment.png`
- `ReignBeta/artwork/war-council-map-sources/engraved-symbol-atlas.png`
- `ReignBeta/artwork/war-council-map-sources/engraved-terrain-atlas-v8.png`
- `ReignBeta/artwork/war-council-map-sources/wood-unit-atlas.png`

The newest generated terrain motif atlas is:

`C:\Users\speed\.codex\generated_images\019ffe63-46a8-7313-84b0-2ff1f496d85e\exec-1dd1d610-3d3c-40d1-9b4d-7fbda424a072.png`

Project-bound copy:

`C:\Users\speed\Documents\Bannerlord Events\ReignBeta\artwork\war-council-map-sources\engraved-terrain-atlas-v8.png`

It contains separate transparent trees/bushes/ridges and is an appearance source only. The built-in ImageGen mode was used. Prompt intent: create a transparent atlas of separate fine sepia engraved conifer, bush, and mountain/ridge motifs matching the supplied worn medieval parchment reference, with no circular forest clusters and no geography.

## Static settlements baked into the raster

- Every town, castle, and village is drawn directly into the master at its audited location and labeled underneath in medieval map lettering.
- Do not add separate visible runtime settlement miniatures or labels.
- Town art is the largest reference size; castle art is approximately 75% of town size; village art is approximately 50% of town size.
- Settlement art must remain legible but subordinate to geography.
- Invisible clickable regions and tooltips may remain dynamic so settlements can be interacted with.
- Create collision-aware label placement without moving a settlement away from its real coordinate.
- Prove every audited settlement is represented exactly once, of the correct type, on land where appropriate, and at its mapped coordinate.

## Dynamic party presentation

- Show every active NPC-led party belonging to the player's clan and player-led kingdom.
- Show other kingdoms' and relevant non-kingdom NPC parties according to War Council intelligence detection, not as permanently omniscient data.
- The assigned War Councilor's Tactics skill controls periodic foreign-party detection/refresh over campaign days. This system must be deterministic/harnessable and save-safe.
- Player-realm figures use black-painted wood. Foreign/enemy figures use brown wood.
- Use infantry, archer, cavalry, and ship miniatures based on actual party composition/movement domain.
- Party figures should be consistently scaled with one another and clearly legible at the fixed close strategic view. They remain smaller than baked town art; the final requested direction was roughly half the former oversized unit scale.
- Player/realm lord markers carry Roman numerals matching their roster card.
- Clicking a player-realm map marker highlights and scrolls/selects that lord's card.
- Selection/highlight effects and tooltips remain dynamic.

## Lord roster, councilor, communication, and reports

- Right-side scrollable list of all living adult nobles in the player's kingdom except the player.
- Each card includes stable native portrait, name, clan, availability, party size/composition, Roman numeral, and current status.
- Portraits must not flicker during live refreshes.
- Partyless eligible nobles have a `Mobilize & Recruit` action using native limits, cost, spawn, and recruitment rules; no royal bypasses.
- A small raven carrying a scroll sends a rapid message to that lord. This opens a purpose-built command/message interaction and copies the exchange into the main correspondence interface for continued messaging.
- The map is observational and must not use dragged party markers to issue orders. The earlier drag/drop Hold/Patrol/Attack interface is superseded.
- Current/received orders may still be displayed in the status field. Commands are delivered by correspondence or in-person interaction through the durable Campaign Command authority.
- Include the assigned War Councilor portrait below the lord list.
- Include scrollable kingdom military-strength reference and recent battle-report panels. Mouse-wheel scrolling must work when the pointer is over every scrollable box, not only through tiny scrollbars.
- Battle rows show winner, initial force sizes, losses, land/naval type, and captured lords; retain the newest 100 battles or 30 campaign days, whichever is smaller.
- Opening the War Council pauses campaign time and closing it restores the prior safe time state.

## Current implementation state

The current renderer is:

`C:\Users\speed\Documents\Bannerlord Events\BannerlordEditorMcp\scripts\render-war-council-map.py`

It already defines:

- `MASTER_SIZE = 16384`
- `TILE_COUNT = 4`
- `TILE_SIZE = 4096`
- lossless tile reconstruction verification
- official height, river, settlement, parchment, and motif inputs

It is not accepted. `draw_terrain_symbols` still invents broad climate zones and places forest cluster cells on a lattice. Replace that implementation; do not tune its random probabilities. The new renderer must accept audited editor terrain/material/flora inputs, parse bridges from official scene data, record hashes and counts, and fail closed when required authoritative inputs are missing.

The active map widget already declares a 4×4 tile grid:

`ReignBeta/src/Modules/Court/UI/Widgets/ReignWarCouncilMapWidget.cs`

Important War Council implementation paths:

- `ReignBeta/GUI/Prefabs/ReignWarCouncilScreen.xml`
- `ReignBeta/src/Modules/Court/UI/ReignWarCouncilScreenManager.cs`
- `ReignBeta/src/Modules/Court/UI/ViewModels/ReignWarCouncilScreenVM.cs`
- `ReignBeta/src/Modules/Court/UI/Widgets/ReignWarCouncilMapWidget.cs`
- `ReignBeta/src/Modules/Court/WarCouncil/ReignWarCouncilCampaignBehavior.cs`
- `ReignServer/shared/Reign.Core.Contracts/WarCouncil/ReignWarCouncilRules.cs`
- `ReignServer/src/Modules/Platform/VerificationLab.cs`
- `reign.testing.json`
- `docs/agent/TESTING_TOOL_GUIDE.md`

The current `REIGN_ROADMAP.md` War Council entry is stale. It still says to exclude enemy parties and issue map orders by marker drop. Reconcile it with this handoff only after implementation and verification; do not create a duplicate item and do not mark it complete early.

## Current editor and lifecycle state

At handoff time, the Bannerlord editor is open and responsive:

- PID observed: `18284`
- title: `Edit Mode: ../../Modules/NavalDLC/SceneObj/Main_map/scene.xscene`
- process snapshot: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-task-handoff-20260823\process-state.json`

Re-observe before relying on the PID. Do not save `Main_map`.

The editor launcher profile currently has gameplay frameworks and `ReignBeta` disabled and `BannerlordEditorBridge` enabled. Snapshot:

`C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-task-handoff-20260823\LauncherData-editor-profile.xml`

After editor work, close the editor visibly and restore the normal game module profile before native testing. Prior backups exist under:

`C:\Users\speed\Documents\Bannerlord Events\.codex-build\war-council-map-v4\evidence\editor-route`

At the last campaign checkpoint, the server was stopped and an earlier campaign-test run had been armed. Treat these IDs as historical until MCP re-observes them:

- run: `war-council-16tile-latest-20260823`
- immutable baseline: `ReignTest_WarCouncilLaunchAcceptan_ca8b48cb_Current`
- run-owned disposable current: `ReignTest_WarCouncil16TileLatestSa_ca86f8a5_Current`

Never mutate the user's original `courtest` save. Re-enroll/reverify the exact baseline and disposable namespace through guarded MCP tools before any campaign mutation.

## 2026-08-24 save-recovery prerequisite checkpoint

Before resuming map work, the user's reported `newtest` load failure was isolated with native A/B evidence:

- Original `newtest.sav` remains untouched at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`; an identical untouched backup is under `ReignBeta/server/deployment-backups/save-recovery-newtest-20260824-073100/`.
- `newtest` fails under both the corrected candidate client DLL and exact prior known-good client DLL `FFA900A880E9197D9FA48B529299D9DFC5552C05B3716AC4C0511DF2592C6960` with native `Array dimensions exceeded supported range`. This proves the save artifact is already unreadable rather than establishing a current-DLL regression.
- The immediately prior `saveauto3` baseline loads successfully under both tested client boundaries.
- The corrected candidate DLL `BC0C462CB772945EF89EE275EB6DB693C0FD5A61986D3352C19E5546A03B95F3` is restored in the installed ReignBeta client. Its class-name inventory exactly matches the prior known-good DLL (1,455 classes, zero differences) while preserving the six validated chat/UI deltas from validation `20260824-072441-abeddbaa`.
- Guarded campaign-test run `save-load-recovery-saveauto3-20260824` enrolled immutable `saveauto3`, created only the run-owned disposable `ReignTest_Saveloadrecoveryafternew_b9c347fb_Current`, checkpointed it, and reloaded it in a different Bannerlord process with matching campaign `hfeTuwPMLJdm`, timeline `main_4b36370f9165_branch_58a5f3189b`, world day `91094.6222124595`, and ready Save Sync.
- Durable restart receipt: `campaign_test_restart_fd0c6fe35802496c8d044b75f179d8e0`.
- The restarted campaign was paused and fully drained before Bannerlord was closed. The visible Reign Control Center/server/vector lifetime group remains running.
- At this checkpoint no Bannerlord Scene Editor is running and Editor MCP reports disconnected. Relaunch official NavalDLC `Main_map` read-only before terrain capture, and never save it.

### 2026-08-24 final save-overflow repair and native proof

- Exact cause: Castle Hall `TranscriptJson` became a 39,447-byte strings-archive entry. Bannerlord serializes each archive-entry length as signed `Int16`, so this wrapped to `-26089` and the native loader failed in `ReadBytes` with `Array dimensions exceeded supported range`.
- The original `newtest.sav` remains untouched at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`.
- Recovered baseline `newtest_recovered.sav` retains the newest 17 complete transcript entries (sequence 14-30), has SHA-256 `B02DFF5D8FF794BB3D7BDD77A68FD4AE6AEB6DDCF0625A8B7493BEBDC23CAD11`, and passed strict archive parsing before native use.
- Prevention stores Castle Hall transcripts through `CastleRoomSessionRecord.TranscriptChunks` and `ReignSavePayloadCodec`; any legacy raw field is migrated before native saving. UI restoration/persistence now uses the bounded read/write methods.
- Final Tier 3 validation `20260824-091351-c89f9e1e` passed at fingerprint `e64364a108539ecc62458636441d5590c514132a10be65b9e1ee878bd2242718`. Report: `.codex-build/reign-mcp/validation/20260824-091351-c89f9e1e/validation-report.json`.
- The exact validated client DLL is deployed at SHA-256 `55DC92EC3CB96F866EC21D44F406AD645BDCA106F5B24791A8E4B8A6DBF9B8F1`. It retains the prior class-name inventory exactly: 1,455 classes, zero name differences.
- Guarded run `newtest-recovered-overflow-fix-20260824` loaded immutable `newtest_recovered`, created only `ReignTest_Newtestrecoveredsaveload_81990aaf_Current`, checkpointed/drained it, and reloaded it in a different native game instance (`game-20260824092709-85485da1` to `game-20260824093318-246d6ec7`).
- Durable restart receipt: `campaign_test_restart_062b0c73c012446384bb41551ff85024`. The reloaded active save, campaign, timeline, and world day all matched; Save Sync was aligned and queues drained. The disposable Current SHA-256 is `41D5348FF82C63499D6D70D5D7D3FA6BCB0AE11EE1D91522E3CEF6DAB1D58254`.
- Detailed evidence: `.codex-build/diagnostics/newtest-save-repair-20260824/repair-evidence.json`.
- Bannerlord was closed only after the restarted disposable campaign was paused and drained. The visible Reign server/Control Center/vector lifetime group remains running. Resume map work from a freshly re-observed editor/MCP state.

## Worktree safety

The repository is heavily dirty with many unrelated changes from other Reign work. Preserve them. Do not reset, checkout, clean, broadly stage, or absorb them into War Council work.

Exact snapshots at handoff:

- `.codex-build/war-council-task-handoff-20260823/git-status-porcelain.txt`
- `.codex-build/war-council-task-handoff-20260823/git-diff-name-only.txt`
- `.codex-build/war-council-task-handoff-20260823/git-staged-name-only.txt`

Review applicable ownership rules before editing. Stage only explicit reviewed paths if publishing is later requested.

`reign.modules.json` was changed to classify `ReignBeta/artwork/**` under the UI facet after a fail-closed validation-plan block. Because `reign.modules.json` changed, final validation must use the manifest-selected ecosystem `all` profile unless the current plan authoritatively says otherwise. Do not run `dotnet`, MSBuild, VSTest, or build scripts directly.

## Required next actions

1. Read full current `AGENTS.md`, this handoff, `reign.modules.json`, `reign.testing.json`, and the applicable testing guide sections.
2. Call the Reign MCP validation-plan and testing-catalog tools before edits/testing.
3. Re-observe editor and MCP status. If the editor is open on `Main_map`, continue read-only terrain-layer inspection.
4. Export authoritative forest/vegetation, rock/mountain/snow, road, and other required terrain material maps from the editor. Record layer names, bounds, orientation, dimensions, timestamps, and SHA-256 hashes. Do not save the scene.
5. Parse top-level bridge entities from `scene.xscene`, excluding parent towns that merely contain bridge descendants; deduplicate near-identical variants and record exact transforms.
6. Replace procedural terrain placement in the renderer with actual terrain-mask/elevation/slope/scene-driven placement while retaining the target engraved style.
7. Render a fresh versioned evidence directory (suggested `.codex-build/war-council-map-v8`) containing the 16K master, sixteen tiles, render audit, reconstruction proof, source hashes, and representative crops.
8. Visually inspect full overview plus native-resolution crops around Zeonica/central rivers and bridges, west forests, northern mountains/snow, east rivers/desert, coast/islands, tile boundaries, and all four scroll edges. Compare against the stable references and official editor/game evidence.
9. Fix runtime UI/runtime overlays around the accepted raster; remove registration of obsolete visible settlement/terrain widgets and extra tile leftovers.
10. Extend shared Verification Lab/harness/catalog coverage when needed; update `reign.testing.json`, testing guide, help/security docs, and catalog-contract tests together for any testing-tool behavioral change.
11. Run `reign_validate` with the manifest-selected final profile and inspect the structured report and repository-hygiene evidence.
12. Deploy only the exact validated artifacts. Follow the visible unified Reign server/Control Center/vector lifecycle; never start a hidden/detached server or a second listener.
13. Use a guarded verified disposable copy of the user's baseline for native acceptance. Test open/close pause semantics, click-drag bounds, final-scale sharpness, all terrain regions, every settlement, bridges, roster and card scrolling, mouse wheel, portrait stability, marker-to-card selection, Roman numerals, party composition/ship selection, foreign detection, raven/correspondence, mobilization, status refresh, strength/reports, save/reload, and performance.
14. Re-run any failed layer after fixes, preserve checkpoints, and do not stop on ordinary popups or transient UI failures.
15. Update the roadmap only when evidence justifies it; mark complete only after user-facing launch acceptance is actually proven.

## Acceptance matrix

Completion requires evidence for all of the following:

- One 16,384×16,384 master exists and is the source of exactly sixteen 4096×4096 runtime tiles.
- Sixteen tiles reconstruct the master losslessly; no boundary seam appears in image analysis or in-game.
- Static map has no runtime terrain/settlement art widgets.
- Forests match official forested regions and do not form repeated circles.
- Mountains/ridges/snow match official geography.
- Coast, islands, lakes, rivers, deltas, roads, and bridges match official map coordinates.
- Every audited town/castle/village is present exactly once, correctly typed, labeled, and not misplaced into water.
- Baked wear, folds, scroll edges, and clutter align across all tiles and appear only at map bounds.
- Fine ink remains crisp at the actual fixed close view.
- Native-resolution before/after crops prove the rebuilt map retains or improves the last run's accepted close-view detail level while correcting terrain accuracy.
- Reign frame surrounds rather than overlaps the scrolling raster.
- All required dynamic player/kingdom and detected foreign parties appear with correct color, type, location, and stable scale.
- Roman numeral roster/marker mapping and marker-to-card selection work.
- Raven messaging/correspondence and native-safe mobilization work.
- All roster/reference/report boxes scroll by mouse wheel and scrollbar.
- Portraits do not flicker during refresh.
- Game is paused while the War Council is open.
- Save/reload preserves required War Council state and detection/order history.
- Native performance is acceptable with the paused game and final tile format.
- Final MCP validation, repository hygiene, exact-artifact deployment, visible lifecycle, and disposable-campaign native reports all pass with recorded paths.

## Existing evidence is historical, not final proof

Prior map renders and deployment reports under `.codex-build/war-council-map-v4` and `war-council-map-v5c` are useful diagnostic history but do not prove the final design. The user's latest screenshots explicitly reject the current terrain treatment. Do not reuse a prior green check or deployment report as acceptance evidence for the rebuilt map.

## Handoff completion condition

The receiving task should acknowledge that it has read this document and `AGENTS.md`, create the updated active goal, re-observe the editor/worktree/MCP state, and then continue autonomously from the required next actions without asking the user to restate the design.

## 2026-08-24 final v9e implementation and launch checkpoint

This section supersedes the earlier historical rejection/current-state notes above for the completed v9e raster and deployed runtime.

- Final authored output: `.codex-build/war-council-map-v9e`.
- Render audit: `.codex-build/war-council-map-v9e/evidence/render/war-council-map-render-audit.json`.
- The standard-RGB sepia master is exactly 16,384×16,384. Its SHA-256 is `75C923E3E99E26E8484FF851B8EE52CBF6D962DE22A48F7F88BA41EB46120B3F`.
- Exactly sixteen engine-ready 4,096×4,096 RGB PNG runtime tiles inverse-channel reconstruct their corresponding authored master crops with zero mismatched pixels. The runtime overview also inverse-channel reconstructs exactly.
- The full official river mask is carved into the master land/water contour before terrain, parchment, material, and coastline rendering. The shared coastline pass engraves both variable-width river banks; there is no skeleton/fixed-width river overlay and no blue parchment tint. Audit counts are 22,836 source-mask pixels, 4,474,976 projected master pixels, and 3,728,286 land-bound master pixels.
- The same audit proves 440 settlements, 69,500 official flora transforms, 39 strategic mountain anchors, 39 bridge placements at 33 physical sites, seven material layers, and zero procedural static placements.
- Final close scale is 64× and runtime party figures are 96 pixels. `reign.testing.json` version `2026.08.24.1`, `docs/agent/TESTING_TOOL_GUIDE.md`, production rules, Verification Lab, and `TestingCatalogContractTests` now agree on this contract.

### Final validation and exact-artifact deployment

- Final ecosystem Tier 4 validation run: `20260824-112957-58599ab7`.
- Final source fingerprint: `a4748bc6b59db3d35a23cc0efccddff375c421ef7b1db7fbaac54a679251e564`.
- Duration: 2026-08-24T11:29:57.7759131Z through 2026-08-24T11:51:00.4408033Z (21m03s).
- Result: all managed projects green, offline Verification Lab 120/120, repository hygiene green.
- Report: `.codex-build/reign-mcp/validation/20260824-112957-58599ab7/validation-report.json`.
- Hygiene evidence: `.codex-build/reign-mcp/validation/20260824-112957-58599ab7/repository-hygiene-report.json`.
- Final validated production client/server binaries are byte-identical to the already installed, native-tested deployment: client `ReignBeta.dll` SHA-256 `2449342414E4171628F160C38916509D3480D99B31A3F1D11969FC432B4BA3BC`; core contracts `53C5B791E798107BA9B94C4146141D50F11B9FD2F6CA3FF9D8C93086ADBFBBD7`; server `ReignServer.exe` `03AED9B13BD7037907DD57F201765051628C968F09AD245880AA2F7062018B40`; relationships `AD473D5F9255D8C7D2ADFA5006D32E919D0767DE885EC89E9E8B6BF56E0DC697`.
- Only the rebuilt final controller differed. Installed and workspace-staging `ReignLiveTest.exe` now exactly match the final validated SHA-256 `656F6B5614BD74570990E3FEB2CFB7D358CA540D0C106C2B8ED785F038C49131`. Recoverable prior copies are in `.codex-build/war-council-map-v9e/pre-catalog-controller-deployment-20260824-1155`.
- The unified visible server lifetime was stopped before replacement and restarted only through the installed `Start ReignBeta Server.cmd`. Post-restart health proved server PID 39996, Control Center PID 32212, `unifiedControlCenter=true`, and `allProcessesCloseTogether=true`.

### Native disposable-save acceptance

- Guarded run remains `newtest-recovered-overflow-fix-20260824` on exact Current `ReignTest_Newtestrecoveredsaveload_81990aaf_Current`.
- The user's original `newtest.sav` is still untouched at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`. Recovered baseline remains `B02DFF5D8FF794BB3D7BDD77A68FD4AE6AEB6DDCF0625A8B7493BEBDC23CAD11`.
- The successful rolling checkpoint was reloaded in a different native game instance with receipt `campaign_test_restart_192934ab30be409a852d3ce0564d70b1`.
- Clean post-reload run `live-1787570733572-acedd458` proved 55 total parties, 8 player-realm parties, 16 mobilizable lords, 3 orders, 64× scale, all 16 tiles, paused lifecycle, and zero failures. Branara's mobilized party/order persisted. Report: `C:\Users\speed\AppData\Local\Bannerlord Reign\campaigns\hfeTuwPMLJdm\tests\live-interaction\runs\live-1787570733572-acedd458.json`.
- Final installed-controller smoke run `live-1787572814046-aabe9c65` reproduced the same counts/state with zero failures after a full unified server and visible Bannerlord restart. Report: `C:\Users\speed\AppData\Local\Bannerlord Reign\campaigns\hfeTuwPMLJdm\tests\live-interaction\runs\live-1787572814046-aabe9c65.json`.
- Primary native images: `.codex-build/war-council-map-v9e/evidence/native/war-council-v9e-first-open.png`, `war-council-v9e-southeast-river.png`, `war-council-v9e-post-reload.png`, and `war-council-v9e-final-installed-controller.png`.
- Production behavior already proven in clean run `live-1787569429550-9ecf3ae9`: deterministic bounded map pan, marker selection, raven open/compose/cancel, mobilization, UI snapshot, close, and zero failures. Mobilization changed party count 54→55, player-realm count 7→8, mobilizable count 17→16, and order count 2→3.

### Native hardware-input acceptance completed

- Final guarded input run `live-1787573568534-6f38d594` used the visible Bannerlord window and a genuine Windows pointer-control route. Bannerlord recorded two complete map gestures: press `0→2`, move `0→1,700`, and release `0→2`. The successful inward drag changed the bounded map offset from `(-26312.0957, -32660.1055)` to `(-26042.5957, -32480.44)` while the fixed scale remained exactly `64`.
- Wheel input over the map left its scale and order count unchanged. The custom map diagnostic scroll counter remained zero through this OS helper route, but the same physical wheel route visibly moved all three required Gauntlet scroll panels: military strength advanced from Aserai/Vlandia/Sturgia to Western Empire/Battania/Nord/fen Niabar; reports advanced from Xandion/Agrynja/Culharn/Agnathea to later Desert Bandits/Bovidor/Gornlatuir/Popilia entries; and the lords roster advanced from Bevanoc/Beyanwyn/Branara/Llewara to Rhydara/Rianin/Dovia/Elideth/Nereth with the scrollbar thumb displaced.
- Native visual evidence: `.codex-build/war-council-map-v9e/evidence/native/war-council-v9e-hardware-scroll-panels.jpg`. Geometry snapshot: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\UiCalibration\snapshots\ReignWarCouncilScreen-20260824-121536-008.json`. Structured run report: `C:\Users\speed\AppData\Local\Bannerlord Reign\campaigns\hfeTuwPMLJdm\tests\live-interaction\runs\live-1787573568534-6f38d594.json`.
- The final input run completed 6/6 commands with zero failures and cleanly closed both the War Council and interaction session. Current visible state: unified server/Control Center running and healthy; Bannerlord PID 20068 running on exact disposable Current `ReignTest_Newtestrecoveredsaveload_81990aaf_Current` in Zeonica; campaign time stopped; no active live run; original save untouched.

## 2026-08-24 v10h river, scale, bridge-art, and settlement-size checkpoint

This section supersedes the v9e scale, river registration, bridge styling, settlement sizing, and final-deployment statements above. Historical v9e behavior evidence remains useful where the production behavior was not changed.

- Final authored output is `.codex-build/war-council-map-v10h`; audit is `.codex-build/war-council-map-v10h/evidence/render/war-council-map-render-audit.json`.
- The seamless 16,384×16,384 authored master SHA-256 is `612D9FA1CAA78053C1B9694F42A2F644A088EC49AE8443D1D2AEDD96912D71AC`. The runtime overview SHA-256 is `CF31E072B39A06003C70474A07BCFF90D557104DAA60C790F48388D0EBAF7982`.
- Exactly sixteen 4,096×4,096 lossless PNG tiles reconstruct the master with zero pixel mismatches. All 17 runtime map assets were copied to the workspace and installed module with zero SHA-256 mismatches.
- The official river source is registered north-up by a vertical flip only, with no horizontal mirror. It contains 22,836 source pixels, 4,771,883 projected playable pixels, and 4,718,768 land-bound water pixels. Rivers are subtracted from official land before one shared coast/lake/river bank-line pass; they are not decorative centerlines or runtime overlays.
- Editor evidence registered 32 of 33 physical bridge sites within five world units of official river water (median 0.452, maximum aligned 0.612); the remaining site is the official coastal approach. Bridges preserve each local official water span and crossing direction.
- Bridge art is now a narrow, low-opacity sepia engraved span with fine rails, a faint center seam, sparse joints, and tapered abutments. Native v10h inspection rejected the earlier broad plank treatment and confirmed the narrowed treatment reads with the map rather than as a ladder or dark overlay.
- Settlement illustrations are enlarged to town 160 px, castle 120 px, and village 80 px while retaining their 100%/75%/50% hierarchy. The fixed close strategic scale is 48× and dynamic party figures are 144 px.
- Static audit remains 440 settlements with zero water failures, 69,500 exact official flora transforms, 39 mountain anchors, 33 physical bridge sites, seven material layers, and zero procedural placements.

### Validation, deployment, and native evidence

- Manifest-selected changed-path plan is Tier 3. Canonical validation returned green by reusing run `20260824-163611-41629403`, fingerprint `899eeecb279f3e0f0cfee4883843e74ab85ecb39b01a3001a9e856c0e1100270`; report `.codex-build/reign-mcp/validation/20260824-163611-41629403/validation-report.json`; repository hygiene green.
- Final ecosystem validation call reused green Tier 4 run `20260824-155810-cae7ae1e`, fingerprint `dba6d01468e958f03e88c31b7937feb73547e789defc3e43c85a1379622e6db9`; offline Verification Lab 120/120; report `.codex-build/reign-mcp/validation/20260824-155810-cae7ae1e/validation-report.json`; hygiene evidence beside it.
- Exact v10h installed-asset backup boundary is `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\staging\deployment-backups\20260824-1724-war-council-v10h-fine-bridge`.
- Clean final native run `live-1787592568570-c9a71f9e` completed 7/7 commands with zero failures: War Council open, two bounded 5,000-pixel pans, final 500-pixel correction, snapshot, UI close, and interaction close. Report: `C:\Users\speed\AppData\Local\Bannerlord Reign\campaigns\hfeTuwPMLJdm\tests\live-interaction\runs\live-1787592568570-c9a71f9e.json`.
- Final native snapshot is `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\UiCalibration\snapshots\ReignWarCouncilScreen-20260824-173045-547.json`. It proves 48× scale, offsets `(-29631.5723, -14892.58)`, 16/16 tiles, 55 parties, 8 player-realm parties, 10 naval parties, 57 towns, 76 castles, 307 villages, panning enabled, and campaign time paused.
- Guarded campaign run `newtest-recovered-overflow-fix-20260824` is paused and caught up on exact disposable `ReignTest_Newtestrecoveredsaveload_81990aaf_Current`, world day `91094.6222124595`; no save was made during final art acceptance.
- The user's original `C:\Users\speed\Documents\Mount and Blade II Bannerlord\Game Saves\newtest.sav` remains unchanged at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`.
- Current visible lifecycle: Bannerlord PID 23668 remains running on the exact disposable Current save with time stopped and War Council closed; unified server PID 33352, Control Center PID 25416, vector worker PID 41840, `unifiedControlCenter=true`, and `allProcessesCloseTogether=true`.

## 2026-08-24 v11j authored-terrain and footprint-integration checkpoint

This section supersedes the v10h artwork, settlement-placement, bridge-footprint, mountain/forest treatment, fixed-scale, token-size, and current-lifecycle statements above. It is a recoverable pre-validation checkpoint; deployment and native acceptance are still pending.

- Final candidate root: `.codex-build/war-council-map-v11j-footprint-approval`.
- Authored master: `.codex-build/war-council-map-v11j-footprint-approval/assets/reign_war_council_calradia_master.png`, exactly 16,384×16,384, SHA-256 `C75E383EDF3434B13A46426634892FDAA7C77DD2FE0B9E762DEF8430411545D6`.
- Runtime overview SHA-256: `96D04B1EF5219FA34E2ABC14EB5E05B511AC0B1F243A78543D03D6DF1C07B1F7`.
- Render audit: `.codex-build/war-council-map-v11j-footprint-approval/evidence/render/war-council-map-render-audit.json`.
- Exactly sixteen 4,096×4,096 runtime tiles reconstruct the authored master with zero mismatched pixels. The physical workspace War Council asset directory now contains exactly those sixteen map tiles; none of the rejected 8×8 tile files remain on disk. The direct `ReignEventArtTextureFactory` path resolver intentionally loads these PNGs outside the ordinary sprite-sheet atlas.
- The 17 runtime map assets were copied into the workspace only after a recoverable backup at `.codex-build/war-council-map-v11j-footprint-approval/pre-integration-workspace-assets-20260824-170949`; candidate/workspace hashes match 17/17. No installed game file was touched. Structured evidence: `.codex-build/war-council-map-v11j-footprint-approval/evidence/integration/workspace-integration-20260824-171354.json`.
- Official river registration remains a north-up vertical texture flip only, with no horizontal mirror: 22,836 source pixels, 4,771,883 projected pixels, and 4,718,768 land-bound water pixels. Rivers are part of the unified land/water contour, not add-on strokes.
- All 440 settlements have land-safe visible ink footprints with six master pixels of shoreline clearance. Sixty-seven required bounded shoreline-aware adjustment, six required adaptive illustration scale, and all remain within one-quarter illustration scale of the exact editor anchor. Askar remains a full 200-pixel town and is offset 112.606 master pixels from its editor anchor to keep its complete visible ink on land; Epicrotea uses the bounded 0.8 shoreline-safe variant.
- The 32 river-aligned physical bridge sites all prove opposite banks and at least 18 continuous master pixels of land beneath both illustrated ends. The one remaining official scene site is explicitly retained as a coastal-approach outlier. Bridge art is nearly top-down sepia engraved linework and is centered on the local official water span.
- The editor height/slope field plus 39 official strategic anchors produce 834 thicker mountain marks with 76.819% elevated-region coverage. Forests render 4,246 naturally spaced representatives selected only from the exact 69,500 `Main_map` FLR2 transforms. Raw game topology pixels composited: zero.
- Approved ground/water composition uses an eight-master-pixel beach band, crisp short water hatches, and fine plain dirt marks that remain legible at the 44× runtime crop scale. Approval comparisons: `.codex-build/war-council-map-v11j-footprint-approval/evidence/approval-comparisons/runtime-44x-askar-before-vs-corrected.png` and `runtime-44x-epicrotea-before-vs-corrected.png`.
- Production sources and testing catalog agree on one fixed 44× scale, a uniform 108-pixel dynamic party footprint for mounted/infantry/missile/naval pieces, a 60-pixel marker-selection radius, and a 4×4 runtime grid.
- Current external state at this checkpoint: Bannerlord, Reign server, Control Center, and vector worker are stopped; port 5101 is offline; the editor is closed/disconnected. Guarded run `newtest-recovered-overflow-fix-20260824` remains paused/caught-up on exact disposable `ReignTest_Newtestrecoveredsaveload_81990aaf_Current`; the user's original `newtest.sav` remains immutable at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`.
- Preserve the concurrent transcript-recovery/chat/UI deltas when validating and deploying. The other task deliberately left its compatibility artifact undeployed so one coherent client/server deployment can be produced from the current combined source.
- Pre-deployment inventory review caught and prevented an unsafe first combined candidate with one extra generated class and two extra async structs. The transcript recovery was restructured without new generated types: player-turn parsing is inline, durable history loading reuses the existing `RequestLiveNpcTurns` state machine and existing `PostJsonAsync` route, and the main-thread application uses a method group rather than a captured closure. Focused Tier 3 run `20260824-224247-6c21d8a1` passed; candidate client SHA-256 `700FD4DA0173824E7020E2BC93BCF84E932E5038FD3171696E29ED49C91A354B` exactly matches installed save-compatible DA99913D at 1,455 class names and 351 struct/state-machine names with zero differences. Evidence: `.codex-build/war-council-map-v11j-footprint-approval/evidence/save-compatibility/inventory-neutral-transcript-recovery-20260824.json`.
- One later comprehensive validation attempt (`20260824-223348-655d23ae`) failed only because the editor-MCP stdio integration test timed out during initialization; every product build/boundary and 22/23 editor tests passed. The exact failed boundary was rerun through canonical changed-path validation and passed in Tier 2 run `20260824-224606-45570c99`. This is diagnostic history, not the final combined release validation.
- Next action: run the manifest-selected canonical changed-path Tier 3 validation against the integrated rasters and combined source, deploy only its exact artifacts plus the 17 audited map assets through a recoverable stopped-runtime boundary, then complete guarded disposable-save restart, War Council behavior/performance/lifecycle proof, and native visual acceptance.

## 2026-08-25 V12F refinement completion and launch checkpoint

This section supersedes the v11j pending-validation/current-lifecycle statements above. The accepted authored raster, refinement features, deployment, native behavior, persistence, and lifecycle work are complete.

- Accepted map/runtime contract remains one 16,384×16,384 V12F authored parchment exported as exactly sixteen seamless 4,096×4,096 runtime tiles, plus the derived overview. The deployment audit records all 17 installed raster files as exact matches. Runtime uses the fixed 44× view and uniform 108-pixel live party miniatures; static cartography remains baked into the raster.
- Added complete bold-red `friendly party VS enemy party` headings for realm-involved battle reports; a smaller legend plus a scrollable directory containing all 440 settlements (57 towns, 76 castles, 307 villages) with exact click-to-center behavior; an independent 23-candidate War Councilor selector; safe explicit player-skill fallback; and image-to-image-derived black-and-gold Reign frames around previously unframed panels.
- Detection uses the appointed eligible clan/kingdom lord without altering that lord's native party, orders, availability, or other functions. If unassigned, it uses the player. Tactics scales linearly from range 70 at skill 0 to 520 at 300; Leadership scales foreign detection from 25% at 0 to 90% at 250; player-realm parties are unconditional; detection refreshes on every open. Both appointment and explicit player fallback persist and safely resolve across save/reload.
- Canonical Product Tier 4 Release validation: run `20260825-034609-70dcd40a`, fingerprint `489c94bee405289e36fb7d31d4dff96de4488adef0fe2409cc60be91ee88d36b`, report `.codex-build/reign-mcp/validation/20260825-034609-70dcd40a/validation-report.json`. Result: MCP `174/174`, relationship boundaries `11/11`, Verification Lab `72/72`, repository hygiene clean, and exact save-compatible 1,455-class/351-struct inventory parity.
- Exact deployed artifact hashes: client `ReignBeta.dll` `89EB921887A2557AAC0E91489204BE45F3903DE1CEC5221606DDCD2F15C9B04E`; server `ReignServer.exe` `19AE41A54A7AF649F8DFC27C11877A195B7A0B4B798153377B80E50220EA233B`. Deployment evidence and rollback boundary: `.codex-build/war-council-refinement/deployment/20260824-230038/deployment-evidence.json` and installed `server/deployment-backups/war-council-refinement-20260824-230038-89eb9218`.
- Primary native interaction run `live-1787631097695-6e4e0b9c` opened production War Council in 1.43 seconds and proved map pan/drag, fixed zoom, all-16-tile loading, directory scrolling and exact castle centering, lord selection, raven compose/cancel/send, native-safe mobilization, councilor selection, detection recalculation, framed panels, and UI close. Bevanoc changed effective skills from ruler Tactics/Leadership `10/5` (range `85`, foreign chance `0.263`) to `156/237` (range `304`, chance `0.8662`) while existing orders remained unchanged; realm parties remained unconditional. Mobilizing Llewara persisted as a ninth realm party and fourth order.
- First guarded save/reload receipt `campaign_test_restart_98c93ef51bbd44daa664c5aeff3a6d1b` proved selected Bevanoc, skill calculations, mobilization, orders, map contract, settlement directory, and frames in a new native game instance. A second save/reload receipt `campaign_test_restart_fbd55c202cc64bbdaf6373bc986977ef` proved the explicit `use ruler's skills` fallback persisted instead, with effective `main_hero`, Tactics/Leadership `10/5`, range `85`, chance `0.263`, nine realm parties, four orders, 15 mobilizable lords, 440 settlements, 44× view, and 16 tiles. Final fallback UI report: `C:\Users\speed\AppData\Local\Bannerlord Reign\campaigns\hfeTuwPMLJdm\tests\live-interaction\runs\live-1787633190941-2bd3ac66.json`.
- The native 30-day dataset had zero realm-involved battles, so the bold-red realm report case is proven by deterministic/product contract coverage rather than a fabricated or naturally occurring native report row. Other native behavior used the real disposable campaign state.
- Guarded run `newtest-recovered-overflow-fix-20260824` finished paused/caught-up on exact rolling disposable save `ReignTest_Newtestrecoveredsaveload_81990aaf_Current`. The user's original `newtest.sav` remains byte-identical at SHA-256 `8401300A0E0654D8DB324DC5F99E704377317FE02B617D8A13F47F550C5F44D3`. The recovered Main Hall transcript remains exactly 31 rows, SHA-256 `8558781BCD137A54A18B2AAAD5AFE7D3B2BE6FC26B06F3C162974AC6AEF1EA90`, ten waterfall rows, and zero `hi` rows.
- Final visible lifecycle is clean: War Council closed, no active interaction run, Bannerlord gracefully stopped, unified Reign server/Control Center/vector worker stopped together, and port 5101 offline. Windows control was explicitly released to the concurrent BattleTech task after shutdown.
