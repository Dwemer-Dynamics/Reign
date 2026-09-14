# Reign continuity procedures

Read the goal section whenever a Codex goal is active. Read the restart section before a required Codex restart. These procedures preserve the existing recovery and safety boundaries.

## Goal-mode overnight persistence

- These rules apply automatically whenever a Codex goal is active in this workspace. Treat the goal as a durable objective that continues across turns until its stated, verifiable stopping condition is satisfied.
- Do not end or hand back an active goal because of an ordinary acknowledgement popup, informational dialog, transient timeout, application restart, stale window handle, lost UI focus, temporary transport failure, or one failed tool route. Treat these as recovery work.
- Dismiss ordinary non-destructive acknowledgement dialogs when doing so is within the authority already granted for the goal. Never use this rule to bypass a destructive-action confirmation, a required external-communication approval, provider-cost gate, disposable-save boundary, campaign protection, security control, or other explicit safety requirement.
- When one UI-control or tool route fails, preserve a checkpoint, re-observe the current state, and try the next safe supported route: Reign MCP, feature harness, Verification Lab, visible native UI control, process/log evidence, or the documented fallback for that subsystem. Continue any independent in-scope work while the failed route recovers.
- A Computer Use or browser-control failure applies only to that control route unless the governing tool explicitly makes further work unsafe. It is not by itself a reason to abandon the goal; continue through non-browser MCP, harness, filesystem, validation, or other supported interfaces and retry UI control on a later goal turn when appropriate.
- Work in recoverable checkpoints. After each material milestone, record what changed, what was verified, the exact evidence/report paths, the current application/server/save state, and the next action so a continuation turn can resume without repeating completed work.
- For overnight deployment or native-game work, preserve the user's original save, perform mutations only on explicitly authorized disposable copies, and return the visible server/game lifetime to the state required by the goal. Normal fixes, rebuild-validation cycles, visible restarts, test-data setup, and rollback/retry loops remain in scope when already authorized by the goal.
- Do not report an active goal as blocked until the same genuine blocking condition has prevented meaningful progress for at least three consecutive goal turns, including recovery attempts. A genuine blocker requires new user authority, unavailable external credentials/state, or a safety boundary that no supported in-scope route can satisfy. Difficulty, elapsed time, a single popup, a single tool failure, or incomplete testing is not a blocker.
- Stop an active goal only when its verifiable completion condition is met, the user pauses or clears it, or the repeated genuine-blocker threshold above is reached. On completion or genuine blockage, provide a concise checkpoint report rather than a vague status update.


## Codex restart continuity

- When Codex itself genuinely must restart, use the installed Codex Restarter MCP tool `restart_codex_and_resume_task` with the current `CODEX_THREAD_ID`; do not substitute an ad hoc process-kill or relaunch command.
- Before requesting the restart, record a recoverable checkpoint in the active task: completed work, uncertain operations that require re-observation, evidence/report paths, application/server/save state, and the exact next action.
- The Codex Restarter tray watchdog owns whole-app recovery. It captures all unfinished Codex tasks, restarts the desktop app, reopens them, and submits continuation turns. Passing the current task ID identifies the requesting task; it does not limit recovery to that task.
- A required Codex restart is recovery work, not a stopping condition. After restart, reread current `AGENTS.md`, re-observe external state, and continue from the durable task/filesystem checkpoint without blindly repeating uncertain mutations.
