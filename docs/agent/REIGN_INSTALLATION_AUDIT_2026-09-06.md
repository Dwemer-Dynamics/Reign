# Reign installation and leftover-data audit

Audit date: 2026-09-06 UTC (2026-09-05 in Chicago). Read-only inspection of installed and configured runtime storage. No cleanup, deployment, builds, restarts, save changes, or test execution. Only this report and audit evidence were written.

## Findings that matter

The installed module contains **22,629,882,114 bytes (22.63 GB / 21.08 GiB)** across **131,822 files**. **16,181,009,612 bytes (16.18 GB, 71.5%) are explicit deployment/backup trees**. Another 310.45 MB is developer material. The installed directory should not be uploaded wholesale.

The feared accumulation of campaign portrait folders is **not present at the current top-level portrait root**: it contains `_shared` plus one campaign, `ZznDfXifQbqw`. The shared library is **4.45 GB**, with 1,228 portrait masters and their derivatives/metadata. Preserve it. The campaign portrait directory is 20.95 MB and has matching local Save Sync evidence. Old data does remain elsewhere: 23 legacy in-module campaign directories, 85 history-outbox campaign directories, generated scene caches, nested backups, and external test/recovery data.

A conservative full-art Reign package candidate is **5.81 GB uncompressed**, or **5.95 GB including the measured embedding cache and installed dependency mods**. Sampled DEFLATE compression suggests approximately **5.6-5.9 GB download** for those measured components. This is not yet a complete offline installer: the current startup/archive paths depend on a machine-specific WSL/PostgreSQL installation whose redistributable payload has not been defined. Do not use 5.95 GB as a promise that a fresh PC can run Reign offline.

## Scope, evidence, and confidence

Installed root: `D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta`.

Configured data root: `C:\Users\speed\AppData\Local\Bannerlord Reign`. The installed `Start ReignBeta Server.cmd` sets `REIGN_DATA_ROOT` to this location and `REIGN_SAVE_SYNC_ROOT` to its `save-sync` child. The migration marker records copying from the module's `server\app\data` on 2026-07-31.

Evidence directory: `C:\Users\speed\Documents\Bannerlord Events\.codex-build\installation-audit-20260906`.

- `inventory-start.json`, `external-inventory-start.json`: exhaustive metadata inventories, including byte lengths, modification times, directories, traversal failures, and skipped links. No traversal errors or links were found in either inventory.
- `file-inventory.csv`: every installed file with absolute path, size, category, and classification reason.
- `directory-findings.csv`: size-ranked absolute directory paths. Ancestor/child subtotals overlap; do not add them together.
- `classified-inventory.json`, `category-totals.json`: reproducible category accounting.
- `campaign-reconciliation.json`: physical save names, bounded ledger fields, campaign-root comparison, outbox sizes, and explicit database-verification limitation.
- `server-counterparts.json`, `legacy-data-comparison.json`: targeted byte/hash comparisons without copying file contents.
- `compression-sample.json`, `prerequisite-mods.json`: package-estimate inputs.
- `source-evidence.json`: source hashes and installed verification-contract comparisons. CampaignBackups, SaveSync, and RuntimeData match their installed contract copies. This does not substitute for behavioral verification of the executable.
- `mcp-availability.json`: failed read-only MCP health/storage-audit responses; port 5101 refused connections. The server was left offline. The database registry, PostgreSQL storage size, import protections, and final live cleanup verdict remain unverified.
- `inventory-drift.json`: final metadata recheck and source/ledger drift results.

GB/MB in this report are decimal. These are logical file bytes, not allocated clusters or deduplicated physical disk space. Source, log, credential, database, save, or portrait payloads were not copied into the evidence package. Metadata and hashes remain local.

## Installed-module accounting

These rows are mutually exclusive and sum exactly to the measured installed total.

| Category | Bytes | Disposition |
|---|---:|---|
| Conservative runtime binaries and fixed art | 1,358,025,403 | Retain for the package candidate; not a proven minimum |
| Protected shared portrait products | 4,451,735,050 | Retain in the full-art candidate; do not purge as campaign leftovers |
| Explicit deployment/backup trees | 16,181,009,612 | Confirmed non-runtime backup candidates; exclude from distribution; deletion/retention approval is separate |
| Developer source, contracts, diagnostics, calibration evidence | 310,446,159 | Exclude from ordinary runtime estimate; consider an explicit alpha diagnostics pack |
| Save-associated campaign portrait cache | 20,953,686 | Legitimate local campaign data; preserve, exclude from fresh installations |
| Other local data or unresolved retention/dependency ownership | 307,712,204 | Exclude user-specific data from distribution; resolve uncertain dependencies and ownership before pruning |
| **Total** | **22,629,882,114** | **No bytes deleted** |

The machine-readable classifier combines the last two rows as `unresolved_local` (328,665,890 bytes); the campaign ledger permits the separate 20,953,686-byte preservation finding above. **Confirmed deletable orphan-campaign bytes: zero**. Backup classification is confirmed; whether a backup is still needed by another task is not.

## Size-ranked findings

The following paths are relative to the exact installed root stated above; every absolute path and file is supplied in the CSV evidence. Rows here may overlap the accounting categories above but do not overlap one another.

| Installed path | Size | Evidence / confidence | Recommendation |
|---|---:|---|---|
| `deploy-backups` | 8.089 GB | Explicit deployment snapshots, including old complete server/verification trees; high | Exclude from downloads. Review retention by deployment/task before any deletion |
| `server\deploy-backups` | 5.526 GB | One old server snapshot containing further backup trees and development environments; high | Exclude; avoid recursively backing this directory up again |
| `PortraitCache\_shared` | 4.452 GB | 1,228 master portraits plus display derivatives; explicitly protected by cleanup code; high | Preserve; include current library in full-art sizing |
| `staging\deployment-backups` | 1.646 GB | Entire nonempty staging payload consists of deployment snapshots; high | Exclude; retain until owning tasks no longer need rollback |
| `GUI` | 705.93 MB | Sprite sheets, source parts, prefabs, fonts, and contracts; runtime references include direct map-tile loads; high for retention, not minimality | Retain conservatively. Large PNGs are not automatically obsolete |
| `server\deployment-backups` | 604.05 MB | Named deployment rollback copies; high | Exclude; separate retention review |
| `UiCalibration` | 259.35 MB | 258.98 MB is captured snapshots; high | Developer evidence, not fresh-install content; do not disrupt UI work by deleting it |
| `EventArt` | 213.83 MB | Event/phase art and text consumed by event fallback resolution; high for retention | Retain conservatively |
| `server\app\deploy-backups` | 199.27 MB | Backups inside the active application's directory; high | Exclude; packaging must explicitly fence them out |
| `server\app\native-portrait-generator` | 162.96 MB | Build publishes the generator and native render host; high | **Preserve unchanged** |
| `CourtPetitionArt` | 149.40 MB | 50 PNGs written from provider results under hashed cache keys; high for generated-cache classification, unresolved ownership | Exclude existing session images from fresh installations; add ownership-aware cleanup |
| `server\app\vector-worker` | 146.12 MB | Managed semantic-memory runtime; high | Retain; unrelated to the retired image generator |
| `.codex-backups` | 117.33 MB | Explicit local rollback copies; high | Exclude; review retention |
| `CastleChatArt` | 44.90 MB | 14 generated session/family images written by the client; high for cache classification, unresolved ownership | Exclude session data from distribution; inspect references before pruning |
| `TavernArt` | 40.84 MB | Culture scenes and current modern frame; high for retention | Retain conservatively; old `tavern_frame.png` (3.03 MB) is a later narrow review candidate, not a deletion instruction |
| `server\app\data` | 25.86 MB | Legacy data location; active launcher redirects elsewhere; high | Exclude from distribution; compare against active root, not blanket-delete |
| `Videos` | 24.55 MB | Startup/menu video; high for retention | Retain |
| `PortraitCache\ZznDfXifQbqw` | 20.95 MB | Campaign ID matches local ledger with seven present native-save slots; high for preservation | Preserve; do not ship this user's campaign cache |
| `server\app\verification_contracts` | 18.47 MB | Build intentionally copies source contracts for Verification Lab; high | Developer/alpha diagnostics choice; omission requires isolated verification behavior checks |
| `logs` | 11.78 MB | Runtime log files; high | Exclude, keep locally for diagnosis under a future retention policy |
| `HistoryOutbox` | 10.10 MB | 85 campaign directories, including nonempty pending queues; high for scope gap, unresolved orphan status | Preserve pending records until owner/acknowledgement review |
| `RelationshipOutbox` | 6.84 MB | One campaign-scoped outbox; high for scope gap, unresolved orphan status | Preserve until owner/acknowledgement review |

Of the 16.18 GB backup total, **4,884,291,424 bytes are inside at least two backup-directory levels**. This is an overlapping subset, not extra space. For example, the old `server\deploy-backups\20260805-153619-ruler-diplomacy-1d3\app` snapshot contains another `deploy-backups` tree. Old verification copies also include Python build environments and generated data.

The flat `server` layout also remains beside `server\app`. Targeted comparisons found **38 same-name EXE/DLL counterparts, 33 byte-identical**. `server\ReignBetaServer.exe` differs from the active-path executable; the installed launcher selects `server\app\ReignBetaServer.exe`. The classifier retains **58.38 MB of this legacy layout as unresolved**, rather than assuming every copy can be removed. The active face detector and its ONNX dependencies remain retained; no model files were changed.

## Campaign/save and external-storage reconciliation

The configured external data root contains **4,557,794,653 bytes (4.56 GB)** across **44,273 files**, additional to the installed-module total. This excludes the PostgreSQL database and physical Bannerlord saves, whose byte inventory is separately recorded. Do not add these runtime folders to an installer.

| External path under `C:\Users\speed\AppData\Local\Bannerlord Reign` | Size | Finding |
|---|---:|---|
| `tests` | 1.897 GB | Test artifacts, including 1.225 GB in nine `snapshots` children and 653.42 MB of campaign-command evidence. Active-task retention is unresolved; do not delete |
| `vectors\local` | 1.057 GB | Persistent semantic data; metadata-only inventory cannot establish which records belong to retired campaigns |
| `save-sync\1ade1eb2d8bb` | 949.23 MB | Ledger identifies campaign `ZznDfXifQbqw`; hashed folder names are intentional, not evidence of an orphan |
| `campaigns\ZznDfXifQbqw` | 474.67 MB | Matches the local Save Sync campaign; retain |
| `models\embeddings` | 67.18 MB | Cached BGE ONNX embedding model/tokenizers; include a verified clean copy for an offline-capable semantic package |
| `logs` | 51.38 MB | Runtime diagnostics; retention review only |
| `campaigns\1fvsnLqtJX07` | 39.67 MB | No `campaign.json` or corresponding local Save Sync ledger found. Mainly character data; unresolved until database/import state is available |
| `test-data` | 14.11 MB | Conversation-roleplay fixtures/evidence; retain for owning tasks |
| `test_runs` | 6.05 MB | Test-run artifacts; retain for owning tasks |

Important distinctions:

- `save-sync\1ade1eb2d8bb\r` is **929,122,505 bytes** in two recovery copies. Its `c` content store is only **19,727,057 bytes**. Recovery retention, not merely save-slot count, dominates this folder. No copy was declared safe to delete.
- The ledger has seven points and all seven named `.sav` files exist: BaseTest, test2, saveauto1/2/3, and two task-named Noble Docket saves. This establishes preservation evidence, not proof of save contents or current database consistency.
- `campaigns\1fvsnLqtJX07` must remain unresolved: missing metadata/ledger alone cannot rule out a protected import, database-only campaign, interrupted operation, or an active task's data.
- The old module `server\app\data\campaigns` has **23** campaign directories; **21 IDs are absent from the configured external campaign directory list**. Their exact IDs and sizes are recorded in `campaign-reconciliation.json`. This is evidence of legacy-root leftovers, not a database-backed orphan verdict.
- In the full old `server\app\data` tree, 379 files have byte-identical active-root counterparts, 145 have different sizes, and 62 have no same-relative-path counterpart. Do not discard the whole tree as an identical migration copy.
- Both configured campaign-retirement staging roots are empty. No stranded retirement transaction was found there.
- Local save-associated campaign files plus Save Sync total **1,423,895,661 bytes outside the module**, plus the module's **20,953,686-byte campaign portrait cache**. Vectors, the second external campaign, and generated scene/outbox ownership remain unresolved.

The MCP audit was requested but unavailable because the server was offline. No alternate database connection, service startup, or game operation was used to force a verdict. Zero-save campaign deletion, stale-slot retirement, pending imports, and semantic-vector ownership therefore remain deferred, not passed.

## Retired image-generator investigation

The task **Reign Image Generator** (`019fc08c-18d5-7cb1-a4ae-fb2c0f407b09`) in ImageGen describes a separate ComfyUI application at `D:\CharacterRealizer` with FLUX, RealVisXL/SDXL, and RealCore Pony models. Its last historical size report was 35.55 GiB; that was not remeasured and is not part of the 22.63 GB module total.

The installed inventory contains no CharacterRealizer, ComfyUI, RealVisXL, or RealCore named payload and no `.safetensors`, `.gguf`, or `.ckpt` model files. Targeted current portrait/platform source searches found no corresponding integration names or default ports. Some backup Python environments contain generic Hugging Face `_safetensors.py` helpers: these are not evidence of that generator. Renamed/embedded assets cannot be categorically ruled out by filename/source inspection alone, but no installed payload attributable to the retired generator was found.

The native character image generator, native render host, `portrait_models\version-RFB-320.onnx`, and active ONNX runtime are current features and are explicitly preserved.

## Hosting/package estimate and remaining prerequisite gap

| Component | Measured uncompressed bytes |
|---|---:|
| Reign runtime and fixed art, conservative retention | 1,358,025,403 |
| Complete current shared portrait products | 4,451,735,050 |
| **Reign full-art candidate** | **5,809,760,453** |
| External embedding cache | 67,181,330 |
| Installed Harmony, UIExtenderEx, MCM, and transitive ButterLib directories | 69,387,618 |
| **Measured candidate including model and mod prerequisites** | **5,946,329,401 (5.95 GB / 5.54 GiB)** |

This assumes the full current shared library ships to alpha users, preserves existing formats/derivatives, retains both SpriteParts and runtime sheets where dependency removal has not been proven, and omits personal campaigns, scene caches, logs, snapshots, backups, and old server layouts. It is a conservative file-selection estimate, not a built or validated release archive. Shared-library redistribution/content review and dependency package licensing are not established by a size audit.

The sample measured 61,973,290 bytes across weighted file samples, capped at the first 2 MiB per selected file, using zlib level 6. Images compress poorly: shared PNG ratio 0.9972 and other image/video ratio 0.9836; binary ratio 0.4294. Applying these strata gives about **5.56 GB for Reign alone**. Allow **5.6-5.9 GB** for a ZIP-like measured bundle including models/mod dependencies; this is a planning interval, not a statistical confidence interval. No large archive or full recompression was run. Solid archives may behave differently, and no lossy image reduction is assumed.

Retaining the current Verification Lab source contracts and small test executables would add roughly **19 MB**, plus any explicitly selected diagnostics dependencies. This is separate from the 259 MB calibration snapshots, which are local evidence. Fresh-install verification is needed before removing diagnostics that remain exposed in the alpha Control Center.

**Unmeasured prerequisites prevent an exact complete-offline-installer quote:**

- The installed launcher requires WSL distribution `DwemerAI4Skyrim3`, starts its PostgreSQL service, and expects localhost port 5432. PostgreSQL archive operations also invoke tools through WSL. A clean Reign-specific provisioning/redistribution artifact has not been identified. The user's existing distro/database must not be copied into an installer.
- The server/client target .NET Framework 4.7.2. A compatible Windows runtime must be present or supplied through a selected offline prerequisite installer. That installer was not inventoried.
- The native generator is self-contained, but loads matching TaleWorlds assemblies from the user's own Bannerlord installation. The game is a user-owned prerequisite, not Reign download content.
- The dependency-mod inventory measures current folders, not clean official distribution archives. MCM's installed SubModule explicitly depends on ButterLib in addition to Harmony/UIExtenderEx.
- An offline installation bundle does not make cloud image/dialogue providers operate offline; service credentials and provider configuration are not shipped.

Consequently the complete fresh-PC offline budget is **5.95 GB plus the selected clean WSL/PostgreSQL and Windows prerequisite payloads**, with any alpha diagnostics added separately. The remaining number requires packaging decisions/artifacts, not a larger scan of this user's campaign data.

## Cleanup gaps and follow-up work (not implemented)

1. **Legacy runtime migration preserves old copies indefinitely.** `RuntimeData.cs:19-77` copies missing data and writes a marker; it does not retire the source tree. `CampaignBackups.cs:1164-1172` deletes roots resolved for the configured current campaign, portrait cache, and Save Sync paths. Add a guarded migration-residue audit with comparisons and explicit ownership; never silently purge different or unmatched files.
2. **Client-side outboxes and scene caches are outside the inspected retirement scope.** History writes `HistoryOutbox/<campaign>/<timeline>/pending.jsonl` (`ReignWorldHistoryClient.cs:749`); relationship outboxes use an equivalent module-root path (`ReignRelationshipDirectorClient.cs:346`). Castle/petition clients write module-root image caches (`ReignCastleSceneClient.cs:112`, `ReignRulerPetitionSceneClient.cs:94`). The inspected deletion implementation contains no corresponding paths. Add campaign ownership/reference manifests, pending-queue handling, guarded cleanup, and tests preserving shared and still-referenced content. This is a source-level coverage gap, not a reproduced runtime deletion failure.
3. **Backup capture can include older backups and generated environments.** The 4.88 GB nested-backup subset demonstrates the result. Use a manifest allowlist for deployment rollback content; exclude earlier backups, live data, staging, caches, and Python build environments. Audit existing rollback ownership before retention cleanup.
4. **Recovery/test evidence needs explicit retention accounting.** Surface Save Sync recovery bytes separately from retained save-point/content counts. Report test snapshot retention by run and active owner. Keep two recovery copies and active-test snapshots until their existing safety/continuity requirements have been checked.
5. **Packaging needs a clean install contract.** Define a distributable file manifest and independent runtime-data directories; make legacy layouts/backup trees fail a packaging audit. Keep current portrait/face detection seams. Validate the eventual package on an isolated install using the manifest-selected Reign validation and applicable harness/native acceptance layers, with no normal campaign mutation.
6. **Shared portraits are not a cleanup target.** Preserve masters, derivatives, prompts, and historical receipts. `PORTRAIT_PRODUCT_PIPELINE.md` already documents source-only retirement conditions; this current shared library has no `source.png` files. Do not treat similarly sized zoom/master files as interchangeable without consumer and byte-identity proof.

## Verification limits and handoff

This task performed metadata inventories, bounded source/manifest inspection, targeted hash comparisons, local save-ledger reconciliation, and limited in-memory compression sampling. It did not execute production code or prove a reduced installation boots. No harness tests were needed to change runtime behavior because no runtime behavior changed; the server-offline audit gap and deferred fresh-install/native tests are explicit above.

The report is a manifest-classified **Tier 1, non-code, no-build** change. Documentation validation and repository-hygiene results are recorded separately in `.codex-build\installation-audit-20260906\validation-result.json` to avoid altering this report after its validated fingerprint. Existing unrelated workspace/index changes are outside this audit and must not be repaired or absorbed here.

No cleanup action is authorized by this report. Distribution exclusions, backup retention candidates, and confirmed orphan deletion eligibility are distinct decisions. All installed files, saves, current portrait facilities, and other tasks were left intact.
