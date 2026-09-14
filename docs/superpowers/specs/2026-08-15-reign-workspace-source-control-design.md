# Reign Workspace Source-Control and Project-Memory Design

**Date:** 2026-08-15

**Status:** Historical design, superseded 2026-09-14 by the authorized paired `Dwemer-Dynamics/Reign` and `Dwemer-Dynamics/ReignServer` cutover. Follow [repository recovery](../../agent/REPOSITORY_RECOVERY.md). The original design below is retained as history; its former GitHub destination is retired.

## Objective

Create `speedaemonc4/Reign-Workspace` as a private GitHub repository that is the recoverable, authoritative off-machine backup for the complete Reign development workspace. Preserve `Dwemer-Dynamics/Reign` and `Dwemer-Dynamics/Reign-Server` unchanged as legacy snapshots.

## Repository model

The existing local Git repository at `C:\Users\speed\Documents\Bannerlord Events` remains the source workspace. Its current local commit history is preserved. After a safe baseline is prepared, the repository receives an `origin` remote pointing to the new private GitHub repository and publishes one clearly selected canonical branch.

The new repository is a monorepo. It contains the Bannerlord client, Reign server, MCP servers, vector worker, shared tools, tests, manifests, roadmap, agent instructions, and durable documentation needed to understand, validate, and recover Reign.

## Safe inclusion policy

Track human-authored and recovery-critical material, including:

- `AGENTS.md` and applicable nested agent instructions.
- `REIGN_ROADMAP.md`.
- `reign-projects.json`, `reign.modules.json`, and `reign.testing.json`.
- Reign source projects, source code, project files, solution files, scripts, configuration templates, tests, fixtures, and documentation.
- Required lightweight assets whose redistribution and storage are appropriate.
- Project-memory documents described below.

Do not track:

- Build outputs, package caches, intermediate files, generated validation output, temporary files, or local tool caches.
- Runtime logs, crash dumps, transient screenshots, generated portraits, or test artifacts.
- Bannerlord saves, Save Sync data, campaign copies, databases, backups, or runtime state.
- API keys, tokens, credentials, connection strings containing secrets, machine-specific private configuration, or environment files containing secrets.
- Decompiled Bannerlord or third-party source trees, redistributable game binaries, installed dependencies, or other material that should be reacquired from its lawful source.
- Large generated or derived assets that can be recreated and are not required to recover authored project state.

The root `.gitignore` is the primary exclusion mechanism. Before the first baseline commit, candidate files are audited by category, size, and secret-risk. Ambiguous files are excluded until deliberately classified.

## Existing history and legacy repositories

The two existing private organization repositories remain unchanged:

- `Dwemer-Dynamics/Reign`, last pushed 2026-07-11, remains a legacy game-client snapshot.
- `Dwemer-Dynamics/Reign-Server`, last pushed 2026-07-10, remains a legacy server snapshot.

The new monorepo does not force-push, rewrite, merge, rename, archive, or delete either legacy repository. The current local repository's existing commits are retained even though they cover only a small subset of the workspace. The safe baseline commit adds the current classified workspace without claiming that previously untracked files have older Git history.

## Durable project memory

The repository keeps concise, linked project memory under `docs/agent/`:

- Existing architecture, subsystem, database, dependency, testing, and validation guidance remains authoritative where applicable.
- `DECISIONS.md` records significant architecture and product decisions, alternatives considered, rationale, consequences, and date.
- `DEBUGGING_HISTORY.md` records costly or recurring failures, symptoms, root cause, failed approaches, successful fix, verification evidence, and pitfalls.
- `PERFORMANCE.md` records measured performance problems, baselines, decisions, tradeoffs, and evidence.
- `KNOWN_PITFALLS.md` records short operational and engineering hazards that future agents should check before changing affected systems.
- `PROJECT_MEMORY.md` acts as an index pointing future agents to the appropriate focused document rather than duplicating content.

Entries are added only when there is a durable lesson. Ordinary implementation detail remains in commits, tests, and source documentation.

## Future-agent enforcement

The root `AGENTS.md` gains a source-control and project-memory policy requiring future Codex tasks to:

1. Treat `speedaemonc4/Reign-Workspace` as the canonical private remote unless the user later authorizes a transfer or replacement.
2. Inspect repository status before edits and preserve unrelated user changes.
3. Keep new human-authored source, tests, manifests, and durable documentation tracked unless an explicit exclusion applies.
4. Never add secrets, saves, runtime databases, logs, generated output, decompiled game code, or excluded third-party material.
5. Update the appropriate project-memory file when a task produces a durable architectural decision, expensive debugging lesson, performance conclusion, or recurring pitfall.
6. Verify that task-owned files are tracked and intentionally staged before committing or publishing.
7. Report uncommitted or untracked in-scope source at handoff instead of silently leaving it outside history.
8. Never push, force-push, rewrite history, change repository visibility, transfer ownership, or modify the legacy organization repositories without user authorization for that external action.

These rules complement the existing Reign MCP, validation, lifecycle, and roadmap policies rather than replacing them. Official Codex behavior loads repository-level `AGENTS.md` before work, making this policy available to future tasks started within the repository.

## Automated guardrails

Add a repository hygiene check routed through the Reign validation system so local agents and CI share the same policy. It should fail with actionable evidence when it detects:

- Human-authored Reign source or required manifests that are present but untracked or ignored unexpectedly.
- Files matching high-risk secret or private-runtime patterns.
- Tracked build output, logs, databases, saves, caches, backups, or decompiled/third-party trees.
- Newly tracked files above the repository's documented ordinary-file threshold unless explicitly allowlisted.
- Drift between the expected project-memory files, `AGENTS.md` guidance, and the validation/catalog documentation that governs the check.

The guardrail must be deterministic, workspace-confined, redacted, and callable through the Reign MCP validation path. It must not read protected runtime data merely to inspect its contents; path classification is sufficient for protected categories.

## Initial publication sequence

1. Record the pre-change local Git state and legacy GitHub repository metadata.
2. Build the root `.gitignore` from an inventory of the actual workspace.
3. Audit all remaining candidate files for ownership, size, secrets, generated content, third-party content, and recovery value.
4. Add the project-memory index and focused memory documents.
5. Add the future-agent policy and automated repository-hygiene guardrail.
6. Obtain the Reign MCP validation plan required by the affected manifests and validation code.
7. Commit the safe local baseline in reviewable commits without staging unrelated deletions or ambiguous files.
8. Run manifest-selected Reign validation and inspect the structured report.
9. Create `speedaemonc4/Reign-Workspace` with private visibility and no generated starter files.
10. Configure the local `origin`, publish the selected canonical branch, and verify visibility, branch, commit, and file inventory through GitHub.
11. Perform a fresh-clone recovery verification in an isolated temporary directory, confirming that required source, instructions, manifests, documentation, and validation entrypoints are present without excluded private/runtime material.

## Failure handling and reversibility

No legacy repository is modified. No local source file is deleted as part of classification; excluded files remain on disk. Creating the private repository is reversible, but deletion is not part of this work. If secret scanning or classification finds ambiguous material, publication pauses before push while independent local documentation and guardrail work may continue.

A failed upload does not trigger history rewriting or force-pushing. Authentication, organization-transfer, visibility, or GitHub App installation issues are reported separately from the integrity of the local baseline.

## Completion criteria

The work is complete only when:

- `speedaemonc4/Reign-Workspace` exists and GitHub reports it as private.
- The local repository has the expected `origin` and canonical branch relationship.
- The safe baseline, `AGENTS.md`, manifests, source, tests, and project-memory documentation are tracked and present remotely.
- Excluded generated, private-runtime, decompiled, third-party, credential, and oversized material is absent remotely.
- Manifest-selected Reign validation succeeds with an authoritative report.
- A fresh-clone recovery audit succeeds and records exact evidence.
- Both `Dwemer-Dynamics` legacy repositories remain unchanged.
