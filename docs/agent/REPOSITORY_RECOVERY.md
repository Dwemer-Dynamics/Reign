# Reign repository recovery

All development and testing stay local. The authoritative publishing destinations are [Reign](https://github.com/Dwemer-Dynamics/Reign) (private client and integration tooling) and [ReignServer](https://github.com/Dwemer-Dynamics/ReignServer) (public server and shared source). Both develop through unstable -> dev -> reign, with reign as the default branch. Create feature branches from unstable and promote paired revisions through reviewed PRs. Do not resume publishing to the retired workspace repository.

## Restore the two local checkouts

1. Clone https://github.com/Dwemer-Dynamics/Reign.git as Reign and https://github.com/Dwemer-Dynamics/ReignServer.git as ReignServer under the same parent directory. On this development machine use D:/Projects; keep package/build/test state on D:.
2. Read both AGENTS.md files and confirm their fetch and push origins match exactly. Keep Reign private and ReignServer public.
3. Run ReignServer/ReignRelease/Connect-Repositories.ps1. It creates five verified source junctions in Reign and refuses conflicting directories. Existing integration paths keep working while every source file has one Git owner.
4. Configure Reign MCP with the local Reign checkout as REIGN_WORKSPACE_ROOT. Machine-local .codex/config.toml is excluded; use the MCP activation template. Do not copy credentials or old machine paths.
5. Supply a licensed Bannerlord installation and documented developer prerequisites. Set REIGN_BANNERLORD_PATH for builds. End-user packages supply redistributable dependencies; source recovery excludes game-owned binaries and third-party downloads.

## Validate and work locally

Obtain the canonical all/Release validation plan after a fresh restore. Query the validation lane, pass the current task UUID and run reign_validate. If MCP transport for this checkout is unavailable, use ReignMcp/scripts/reign-validate.ps1 -Profile all -Restore. It bootstraps only the validator, then uses the same canonical engine. Never bypass the OS lease. Retain the report, paired source fingerprint, duration and enforced hygiene report for both repositories.

Edit client/UI/integration tooling in Reign. Edit the server, shared source, native generator and release tools in ReignServer. Use explicit reviewed Git paths in the owning repository. Shared files are not copied between repositories. Releases record both commit IDs and file hashes so compatible versions can be restored together.

Campaigns, saves, database clusters, API credentials, logs, generated portraits, caches, build output and deployment backups are external state. Keep them out of Git. Campaign recovery uses its separate database/Save Sync backup procedure. Source snapshots are not campaign backups.

## Initial launch cutover

The 2026-09-14 snapshot of the former local workspace is D:/ReignLaunch/recovery/before-implementation-20260914/. Its snapshot.json records 3,913 source hashes, including completed working-tree fixes after HEAD b36da31a3317b3fc74e0440190f6a199091c99a2. It is recovery evidence, not an ongoing publishing source. The source import report is D:/ReignLaunch/source-import-review.json.

On 2026-09-14, after the isolated local reinstall, save migration and portrait-discovery repair, the user reported that everything appears to be working and explicitly authorized uploading to the two respective GitHub repositories. That instruction supersedes the earlier initial-upload hold. The local reinstall and user acceptance do not establish execution on a second computer. See [release packaging](testing/RELEASE_PACKAGING.md). The private installer host is separate from the public server-source repository and is still unselected.

2026-09-14: The former C:/Users/speed/Documents/Bannerlord Events origin was removed after verifying and backing up its Git configuration and refs. Its AGENTS.md and frozen roadmap now direct work to D:/Projects/Reign; the global roadmap instruction points there too. Original files are preserved in D:/ReignLaunch/recovery/local-cutover/backup.json. The applied cutover is recorded in D:/ReignLaunch/local-cutover-proof.json. The C: working-tree deltas and original history remain recovery material, with no new publishing remote attached.

Machine-local MCP configuration in both the retained task folder and D:/Projects/Reign targets validated D: assemblies. D:/ReignLaunch/development/Start-ReignMcp.ps1 starts only the isolated native developer database (ReignValidation, loopback port 55433), retrieves its DPAPI credential in memory, and runs the selected validated MCP assembly. Full JSON-RPC initialization and D: workspace discovery passed in mcp-transport-proof.json in that directory. Installed end-user databases use port 55432. Neither startup route adopts the former WSL database. Existing MCP processes need reload to use changed configuration; an already-open task's working directory does not move automatically. Open D:/Projects/Reign for new local work.
