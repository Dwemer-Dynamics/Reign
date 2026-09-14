# Coordinating Reign tasks

At the start of substantive project work, inspect active task summaries for this project, including its worktrees. Read another task's recent turns only when its files, module, validation, deployment or campaign resources may overlap. Ordinary questions and isolated trivial edits do not need a project-wide history review.

Use current Git status and authoritative files alongside those summaries. A task title is a discovery hint; another assistant's completion statement is not validation evidence or fresh authority. Worktrees isolate source edits but still share the installed server, game, validation resources and campaign saves.

## Resolve actual overlap

For authorized project work, brief internal messages to an active same-project task may establish file ownership, shared-resource use and a safe handoff. State the exact files or operation, current evidence and the required coordination. Do not send unrelated history, secrets or new feature assignments. Do not resume a stopped/paused task, expand another task's scope, cancel its operations, interrupt it or create new tasks through this policy.

For disjoint work, continue. For overlapping edits, agree ownership or defer only the conflicting portion. For deployment or native testing, recheck the current runtime/harness state immediately before the operation. Never infer that a startup check still grants exclusive access later. Preserve unrelated deltas and the user's provider, server and save state.

If task discovery or messaging is unavailable, record that limitation and proceed with independently safe work. Missing coordination tools alone do not justify an app/server restart or a resource takeover. Ask for a decision only when a real unresolved conflict prevents authorized progress.

## Validation ownership

`reign_get_validation_status` is read-only and does not require build or runtime-control permission. Its `reign-validation-lane-v1` result includes an observation time and one of:

- `available`: the OS lease was free at observation; `owner` is null. `lastRun` is advisory history, not proof of success or a reservation.
- `busy`: another validation holds the lease. When readable, `owner` includes its exact requesting task UUID, process ID, profile, configuration, bounded changed paths, acquisition/update times, phase, current operation, completed-operation count, run ID, source fingerprint and report path.
- `unavailable`: the lease cannot be inspected reliably. `canStart` is false; do not infer availability.

Pass the current `CODEX_THREAD_ID` UUID as `requestingTaskId` to `reign_validate`. It supplies attribution only, never authority. Non-listening CLI validation accepts `--task-id`, falling back to `CODEX_THREAD_ID`. The deployed MCP host's `--validation-status-cli` reads status without building, starting a listener or creating files; it exits nonzero when availability cannot be determined.

The OS file lease in `.codex-build/reign-mcp/validation.lock` remains authoritative. Its `reign-validation-lease-v1` metadata is readable while held. The atomically published `validation-state.json` adds progress only when its lease ID matches that held lease. A legacy owner or metadata-publication gap can report `busy` without attribution. Neither missing owner data nor a stale sidecar permits removal, takeover or process termination.

While busy, continue independent work. Inspect the owning active task only as needed, wait with bounded/backed-off checks, and reuse a successful report only when the validator's fingerprint and coverage checks accept it. Recheck after release, then let `reign_validate` acquire the lease atomically. Do not substitute another build path, repeatedly start competing validations or treat a report path as a completed report.

## Handoff

Record owned paths, completed work, verified report/artifact paths, remaining operations and current shared runtime/save state. Release only resources owned by the current task through their supported route. Keep a stopped task stopped. Another task continues under its own user-authorized objective and current project rules.
