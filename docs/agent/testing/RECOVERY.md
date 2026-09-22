# Recovery and continuity

Read only the procedure relevant to the current task. Existing provider, campaign, and deployment gates remain authoritative. Dated procedure history was retained during the 2026-09-12 guide split.

## Recovery and overnight work

- Ordinary popups, stale handles, one transport failure, or lost focus are recovery events, not stopping conditions.
- Preserve the current run ID and checkpoint, re-observe state, then try the next supported route: MCP, feature harness, Verification Lab, visible native control, logs, or documented fallback.
- Do not blindly repeat an operation whose completion is uncertain. Check its receipt, correlation, save identity, or run report first.
- A cancelled or timed-out campaign advance is safe to retry only when its durable state reports `advance_cancelled_safe` or `advance_failed_safe` and embeds successful exact-run cancellation plus drain evidence. A `*_recovery_failed` state is not a checkpoint boundary and must be recovered without saving.
- Record milestone evidence paths, application/server/save state, and the next action so another turn can resume safely.

## Codex restart continuity

When Codex itself must restart—for example after an MCP/plugin update—write a durable checkpoint and call the installed Codex Restarter MCP `restart_codex_and_resume_task` with the current `CODEX_THREAD_ID`. Recovery is Desktop-only for planned restarts and unexpected closures: the watchdog captures unfinished tasks, reopens them in Codex Desktop, and never starts a hidden `codex exec resume` process. Windows or the user may activate a task independently of the watchdog, so background CLI ownership cannot be made reliably exclusive and is prohibited. After an unexpected closure, send a normal continuation message in the reopened durable task if needed. After recovery, reread `AGENTS.md` and re-observe external state before continuing.

Do not replace this narrow tool with ad hoc process termination. If the watchdog is unavailable, do not claim automatic continuation; leave a complete checkpoint for manual reopening.

## UI-open readiness deadlines

Ordinary standalone `ui-open` targets use a bounded 15-second native open-and-layout readiness deadline. War Council alone uses a bounded 120-second deadline because its lossless 16K tiled map may cold-load slowly, but success still requires the same fail-closed open-target proof plus at least two native late-update frames. The larger bound does not widen any other target deadline or make elapsed time itself proof. A controller `--timeout` only bounds observation and must be long enough to receive that target-owned result.
