# Reign project workflow

For substantive work, inspect active same-project task summaries, including worktrees, and current Git status. Read another task's details only for likely overlap. Brief internal coordination with an active same-project task is authorized to establish ownership or a handoff within existing scope; do not resume stopped tasks or assign new work. Recheck shared resources immediately before validation, deployment or campaign operations. Follow [task coordination](docs/agent/TASK_COORDINATION.md) when overlap exists.

Use Reign MCP as the primary project interface where its workspace, source, status, logs, audit, runtime, testing or build tools can inform or perform the work. Check its catalog before an ad hoc route. Retrieve relevant evidence before conclusions; record unavailable, gated or insufficient coverage. Use contextual documentation below rather than loading the entire project.

Complete authorized implementation through appropriate verification and requested deployment. Fix failures caused by the change and continue recoverable work without stopping for another approval at each routine step. Preserve cost, campaign, destructive-action and external-communication gates. With an active goal, follow [continuity procedures](docs/agent/CONTINUITY.md): ordinary popups, transient failures and restarts are recovery work; completion requires the stated verifiable condition.

## Validation and behavioral proof

- `reign.modules.json` owns module/dependency/facet routing; `reign-projects.json` owns active projects. Obtain `reign_get_validation_plan` before code edits and use `reign_validate` afterward. Use exact changed paths and the lowest manifest-selected tier; see [validation guidance](docs/agent/VALIDATION.md) when selecting scope or handling failures.
- Use `changed` for ordinary work, `product` for explicit product-wide confidence, and `all` for ecosystem release or manifest-required ownership, routing, shared-build or validator changes. Documentation/roadmap/policy-only changes are Tier 1 no-build plans. Read-only audits need no product build. Missing, unknown or ambiguous paths block; classify them before continuing.
- Query `reign_get_validation_status` before validation and pass the current task UUID as `requestingTaskId`. A snapshot is not a reservation. Never bypass or delete the OS build lease.
- Do not directly invoke `dotnet build`, `dotnet test`, `dotnet restore`, MSBuild, VSTest, project build scripts or verification executables. If MCP transport is unavailable, use `ReignMcp/scripts/reign-validate.ps1`; CI uses the same engine. Direct build repair requires `REIGN_MCP_REPAIR=1` and may target only `ReignMcp`.
- Adding/moving a `.csproj` requires the `all` plan and resolution of every unclassified project. Never classify generated, backup, decompiled or third-party trees as managed source.
- Use deterministic contracts, Verification Lab and feature harnesses for behavioral proof. Expose bounded inputs, observable outputs and isolated state. Offline results do not replace required native Bannerlord or human acceptance.
- Repair or extend a shared MCP/harness capability when proportionate to the current task or its recovery. Preserve its production quality, gates, documentation and tests. Record unrelated future improvements separately; document any necessary fallback and coverage gap.
- Group coherent edits before validation. Reuse successful fingerprint-compatible reports with sufficient coverage; do not rebuild unchanged source or rebuild separately for deployment. Deploy the exact validated artifacts.
- Report the validation profile, tier, duration, fingerprint, report/hygiene paths and material coverage gaps. Compilation alone cannot establish testable behavior or override failed hygiene.

## Testing and campaign safety

`reign.testing.json` owns testing capabilities, profiles, gates, evidence and continuity helpers. When the route is uncertain, query `reign_get_testing_catalog` and use [the testing guide](docs/agent/TESTING_TOOL_GUIDE.md) to select the relevant procedure. A changed tool, command, scenario, profile, gate, isolation rule, evidence schema or cleanup route must update the catalog, owning procedure, applicable help/security documentation and contract coverage in the same change.

For unattended native advancement, use guarded campaign-test MCP tools. Enroll the exact user-provided baseline and objective, then create and verify a task-named disposable copy before advancing time. A save name never proves disposability. At checkpoints, pause native time, wait for quiescence, inspect failures/in-flight work, then save; saving before queues drain is a test failure. Save Sync permits 15 unique states. Default to one rolling checkpoint; milestones require reported capacity. Delete only exact run-owned saves through confirmation-gated cleanup, never the baseline. Retain documented provider, long-running-test and runtime-control gates.

## Visible server lifecycle

- The supported server is `http://127.0.0.1:5101`.
- Start only through the visible installed shortcut or `Start ReignBeta Server.cmd`. Never launch `ReignBetaServer.exe` hidden/detached or with `-WindowStyle Hidden`.
- The server owns one dedicated app-style Control Center. Never open it in an ordinary browser tab.
- The server, dedicated Control Center and vector worker are one lifetime group; closing either visible Reign window must stop all three.
- Close the current visible Control Center or server console before replacing installed server files; restart through the unified visible launch path.
- Never start a second background server for tests. Use non-listening CLI tests, or stop the visible server first and return it visibly afterward.

## Interface authority

Read [REIGN_INTERFACE_DESIGN_RULES.md](docs/agent/REIGN_INTERFACE_DESIGN_RULES.md) before planning/editing any Reign interface, control, card, portrait treatment or native augmentation. The machine authorities are `ReignBeta/GUI/UiCalibration/modern-style-contract.json`, `ReignBeta/artwork/ui-modern-style-kit/palette.json` and `asset-manifest.json` in that same style-kit directory. Their exact tokens, hashes, geometry, composition, typography and fidelity requirements override visual guesses and legacy iterations.

Reuse approved assets and remove/disable superseded graphics. Fixed art must match the latest approved reference; dynamic content stays inside declared bounds/masks. Interface completion requires the provider-free preview contract, applicable scroll/layout/portrait contracts, complete rendered preview matrix, approved-reference fidelity audit, manifest-selected validation and required native acceptance, with evidence paths. Scoped previews remain partial.

## Source control and durable knowledge

- Preserve unrelated or ambiguous tracked deltas. Inspect ownership/ignore policy before edits. Intentionally track human-authored source, tests, manifests, policy and durable docs under managed roots; exclude generated output, saves/Save Sync, credentials, runtime databases/logs, generated portraits, builds/backups, decompiled sources, third-party binaries and caches.
- Stage explicit reviewed paths only; never `git add .`, `git add -A` or broad recursive wildcards. Before a push, inspect staged paths, large files, prohibited categories and redacted secret-classifier results. Report intentionally untracked or externally required material and obey enforce-mode hygiene.
- The canonical private client remote is `https://github.com/Dwemer-Dynamics/Reign.git`; the public server remote is `https://github.com/Dwemer-Dynamics/ReignServer.git`. Both use `main` for publication. All development and testing stay local in sibling checkouts. Server-owned directories are verified junctions listed in `reign.repositories.json`; edit and commit them in ReignServer. The former GitHub destination is retired from ongoing publication after verified cutover. Ownership, visibility, remote URL, default branch or GitHub state changes require explicit authorization and coordinated updates to the repository policies, this file, local origins and [repository recovery](docs/agent/REPOSITORY_RECOVERY.md).
- Local commits/pushes to these remotes are normal only when publishing or backup is requested. On 2026-09-14, after testing the retained new installation and portrait-discovery repair, the user confirmed that everything appears to be working and explicitly authorized the initial uploads. This supersedes the earlier initial-upload hold; it does not establish execution on a second computer. Creating/deleting/transferring repositories, visibility changes, force pushes or other remotes require explicit authorization.
- Apply the global Project Memory skill for substantive work. [PROJECT_MEMORY.md](docs/agent/PROJECT_MEMORY.md) indexes focused ledgers; save stable lessons only when their inclusion criteria apply, with dated evidence and no invented history or secrets. Preserve recoverable checkpoints at material milestones.
- Before a required Codex restart, follow [continuity procedures](docs/agent/CONTINUITY.md) and use `restart_codex_and_resume_task` with the current `CODEX_THREAD_ID`; never substitute ad hoc process killing/relaunch. Re-observe state after restart and continue the checkpoint.

These rules apply to authorized agents working anywhere in this workspace. Include relevant policy, owned paths, MCP plans and required evidence in any explicitly authorized delegation.
