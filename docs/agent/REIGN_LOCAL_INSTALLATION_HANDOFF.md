# Installed Reign and performance handoff — 2026-09-14

The complete new release package is installed and retained. The user's latest instruction replaces the earlier restore-after-test plan: keep the new client, server, dependencies and content, and migrate the original saves. The server is visibly running; Bannerlord is stopped. No native campaign was loaded or advanced and no paid provider calls were made during this installation/migration.

## Active installation and source

| Purpose | Active location |
|---|---|
| Private client/integration checkout | `D:/Projects/Reign` — `https://github.com/Dwemer-Dynamics/Reign.git`, `main` |
| Public server/shared/release checkout | `D:/Projects/ReignServer` — `https://github.com/Dwemer-Dynamics/ReignServer.git`, `main` |
| Installed server version | `D:/Reign/ReignServer/versions/0.1.0-preview.1-c24cfc974de5` |
| Player data | `D:/Reign/Data` |
| Shared shipped content | `D:/Reign/Data/Content` |
| Game | `D:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord` |
| Client and dependency modules | The game's `Modules` directory; the internal client identity remains `ReignBeta` |
| Installation record | `C:/Users/speed/AppData/Local/Bannerlord Reign/installation.json` |
| Live native saves | `C:/Users/speed/Documents/Mount and Blade II Bannerlord/Game Saves` |
| Preserved game save archive | `C:/Users/speed/Documents/Mount and Blade II Bannerlord/Reign Save Archive` |
| Transfer package | `D:/ReignLaunch/Ready/Reign-0.1.0-preview.1` |

Use either existing desktop server shortcut or Start Menu **Reign / Start ReignServer**. Both desktop links now invoke the installed unified launcher. The XML previewer desktop shortcut now selects the D: client checkout. The separate CharacterRealizer image-generator shortcut is unchanged. Original shortcut bytes are backed up.

The active server is `127.0.0.1:5101`; its native Windows PostgreSQL is `127.0.0.1:55432`, database `Reign`, owner `reign`. The bundled local vector worker is on 8082. All belong to the visible server/Control Center lifetime. Developer validation uses a separate database on 55433. WSL/5432 is retained only as the old database backup and may still support unrelated software; do not terminate or unregister that distro.

All future source changes and commits belong to the two D: checkouts. Five declared junctions expose server-owned directories inside the client integration workspace; commit them in ReignServer. The former C: source checkout is frozen recovery material and has no origin. This Codex task and the idle performance task are still attached to that old project. Machine-local MCP configuration points to D:, but the existing loaded connector still reports C: source. Open the D: project/reload its MCP connection before subsequent work. Until then, source validation must use the canonical D: `ReignMcp/scripts/reign-validate.ps1` fallback, honoring the normal lease and task ID.

## Exact installed artifact and verification

Artifact source commits are Reign `ea18cb82712aa9dc75479a8a5e542764d0cf8912` and ReignServer `85541f189d1c073640563fdc8839c4fec41a6090`. Later documentation-only commits are recorded separately in `D:/ReignLaunch/source-commit-pair.json`. No repository or package has been uploaded. The local release is version `0.1.0-preview.1`, protocol 1, content `2026.09.14`.

Full validation `20260914-215942-32f76582` passed **all / Release / Tier 4** in **747.558 seconds** by report timestamps: 21 projects, 22 operations, 601 MCP tests, 11 relationship tests, 23 editor tests, 75 renderer tests and 155/155 offline checks. Fingerprint: `c24cfc974de5a1a1d27591da55010acd3ac5a95664cdb6d04e6242d7c8458417`. Reports are `validation-report.json` and `repository-hygiene-report.json` under `D:/Projects/Reign/.codex-build/reign-mcp/validation/20260914-215942-32f76582/`; enforced paired hygiene had 3,961 tracked files and zero issues.

Canonical package `20260914-221428-e7565d49` passed in 330.312 seconds with that fingerprint. `package-report.json` and `assembly-proof.json` are under `D:/Projects/Reign/.codex-build/reign-mcp/release-package/20260914-221428-e7565d49/`. The transfer folder has 14 files / 6,427,660,374 bytes. Setup SHA-256: `daab3872125876e37ade09e9c5f800db2d697b044c0035d275ab29d5ddf72e74`. NTFS hard links avoid an extra local payload copy; copying to another volume materializes independent bytes.

Fresh installation evidence is under `D:/ReignLaunch/acceptance/real-install-20260914/`:

- `fresh-install-acceptance.json`: the original module, dependencies, user data and entire game profile were isolated before the actual compiled installer ran. The corrected compiled negative case returned 1 with a failure page; the successful full installation returned 0. Its journal is `D:/Reign/Data/setup/525bd50021f2488ba0a39a11205de979.json` and records completed owned staging cleanup.
- `installed-package-proof.json`: all 16,757 shipped files and 12,278 portrait timestamps matched. Native PostgreSQL 15.19 started empty with no campaigns or provider credentials; bundled local model inference and the dedicated Control Center passed.
- `fresh-native-menu-proof.json` and `fresh-native-reign-main-menu.jpg`: actual Bannerlord `1.4.8.119303` with installed dependencies and Reign reached its native main menu. Owned War Sails was `1.2.8.119303`. The original save profile was absent for this test. The module log reported zero errors and one existing warning that the optional `EncyclopediaData.OnTick` safety patch target was absent. This proves main-menu loading, not campaign behavior or interface acceptance.

## Save migration and recovery evidence

Migration evidence and one-time guarded helpers are under `D:/ReignLaunch/migration-20260914/`. These helpers are receipts for this exact installation, not a general migration interface.

- `database-verified.json`: the full old `DwemerAI4Skyrim3:5432/Reign` dump restored into a separate temporary native database. Every schema definition and all 4,002 table content/count fingerprints matched in UTC before cutover. Ownership was reassigned to `reign`; no foreign table owners remained. The source dump is 264,837,306 bytes, SHA-256 `e2afa5f4be308c1b555a468380728f256aa3557ed43a14c446fa18a9150eefce`.
- `database-cutover.json`: the verified database became the new installation's `Reign`; the fresh acceptance database remains as `ReignFreshInstall_20260914_01a09688`. The WSL source was not changed or deleted.
- `campaign-files-proof.json`: 132,456 selected files / 6,828,055,782 bytes copied with matching SHA-256, byte length and modification time. This includes campaigns, Save Sync, vectors, native portrait sources, 195 campaign portrait files, 135 outbox files, 15 game-save-folder files (14 `.sav`) and 23 archive files. Old program, model, prompt, dependency, log and verification trees were excluded. `settings-normalization.json` records the sole preference change: the old absolute Codex executable path became `codex`, resolved from the installed launcher's bundled PATH. All other preference/provider values were preserved without logging secrets.
- `migration-startup-proof.json`: all 14 live saves match their campaign/save registrations, all four Save Sync ledgers remain byte-identical, all six non-internal campaign registry rows remain identical and all 17 database snapshot registry rows remain identical. The existing startup cleanup removed only its reserved `.save-sync-work` schema, which held 34 schema-version rows and one catalog revision, not a player campaign. The unfiltered before/after catalog mismatch is retained and the exact difference explained. The original work schema remains recoverable in WSL and the full dump.
- `runtime-proof.json`, `local-model-proof.json`, `control-center-observed.json`: migrated native server/database, visible dedicated Control Center and local model passed. No queued/active/failed background work or provider calls were observed at the final check. `shared-portrait-catalog-proof.json` confirms the actual Content root and all 1,228 packaged portrait sets ready.
- `installed-package-after-migration.json`: all 16,757 shipped files and 12,278 portrait timestamps still match the exact package after user data migration. The new install has not been replaced with old binaries.

Originals remain at `D:/ReignLaunch/acceptance/real-install-20260914/originals/modules/`, `C:/Users/speed/AppData/Local/Bannerlord Reign.pre-reinstall-01a09688` and `C:/Users/speed/Documents/Mount and Blade II Bannerlord.pre-reinstall-01a09688`. `original-preserved-backups-proof.json` verifies every original file after isolation: 273,998 files / 34,786,167,786 bytes, same hashes, timestamps and file identities. `isolation.json` records the permanent new-install disposition and exact backup/shortcut paths. Do not run whole-install restoration after this successful cutover without a new recovery decision.

## Performance continuation and remaining acceptance

The task **Investigate relationship queue lag** (`01a09cb2-e59e-7330-9bee-3ad7d83b31ff`) was idle when this handoff was prepared and was not resumed. Its existing performance objective and acceptance gates remain its authority. Use the new D: source and installed native database, and recheck task/runtime/build ownership first. The game is stopped; the migrated server and Control Center remain visibly running.

The preserved relationship test save is `ReignTest_RelationshipThroughputIn_8db15d0b_Current.sav`, campaign `l0jMsyposuhg`, timeline `main_09871fac8493_recovered_528dbaa5fb`, Save Sync point `ed6cd97304fa462e8a18960e8551e505`. The storage audit sees both its physical save and protected PostgreSQL snapshot. A name and this migration receipt do not establish disposable-save enrollment or authorize advancement; use the existing guarded campaign-test records and the performance task's user-provided baseline. This task did not measure throughput, load a campaign, save over a baseline or advance time.

Four old `ZznDfXifQbqw` rollback points already had `legacy_snapshot_unavailable` in the original ledger. Their native saves and all original files were retained. Three stale registrations in `woERXl3xD23E` also remain; no save cleanup was requested or performed. The relationship test point is available. Native campaign acceptance must respect these existing distinctions.

The campaign storage audit's shared-library metric still looks under `Data/PortraitCache/_shared`; the release correctly stores shipped art under `Data/Content/PortraitCache/_shared`. The actual portrait catalog and package hash audit pass. Record a future audit-routing correction; do not interpret its zero metric as missing art. The shared WSL migration helper also lacks a native Windows target and showed path/SQL-cast gaps; this run used the retained one-time native helper and full independent database comparison.

Another-computer execution, native campaign/save-reload/performance acceptance and private payload hosting remain open. The same-PC reinstall cannot establish a clean second computer. Initial uploads remain held by the existing launch requirement; do not mark the roadmap launch item complete. The transfer instructions and blank acceptance record are `D:/ReignLaunch/Ready/TEST_ON_ANOTHER_PC.md` and `ACCEPTANCE_RECORD.md`. Heavy source, runtime, database and evidence work remains on D:; keep the original backups until recovery retention is decided.
