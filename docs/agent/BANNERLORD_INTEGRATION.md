# Bannerlord integration boundary

The Bannerlord project should remain a thin adapter over server/domain contracts.

## Bannerlord 1.5.7 policy (2026-11-11)

2026-11-13 regression recovery restores the resident spawn signature and animation-ready health-hook startup from the retained, previously qualified repair source. Resident registration resolves its complete nine-hook set before patching and rolls back only its own owner on failure. The native spawn signature includes optional Equipment and ItemObject parameters; optional arguments do not create a two-parameter overload. See the compatibility procedure for compiled-artifact regression proof and native acceptance limits.

Reign targets the licensed PC Bannerlord 1.5.7 / War Sails 1.3.7 installation. Retain native settlement stability: security is loyalty-neutral at 60, the ordinary rebellious-state threshold is 35 and the actual uprising loyalty gate is 25 with native military and daily-roll conditions. Civil Unrest retains its native 60/50 thresholds. Reign's recovery-scaled village penalties, food/resource rules, political effects, foreign-owner culture pressure and separate noble civil-war route remain. Agricultural stress still begins below 50 security. The removed native foreign-governor penalty has no active Reign switch; its old stored settings identifier is inert.

The official-module minimum is 1.5.7. Adapt private volunteer-record metadata, the anchor Settlement property and the explicit secondary governor-perk argument without changing funded recruitment or ship costs. War Sails now fixes away-governor trade context natively; remove the superseded Reign transpiler. Catalogue the eleven new native actions as excluded metadata without expanding operator execution.

Native blood feuds are disabled by `ReignBloodFeudPatches`: suppress the clan flag and feud state actions, feud relation/trait dispatch, automatic captivity executions and broker dialogue. Campaign session initialization also clears persisted clan feud flags, covering native callers that inline the property. On load and before queue consumers, clear feud settlement executions, the automatic player-execution flag and unmarked retaliation entries. Preserve historical execution counters, explicitly death-marked native execution orders and their ordinary completion path. Keep the native behavior and save keys registered. Do not reset relations, revive heroes or undo completed executions. Patch installation rolls back only its own Harmony owner and fails startup if a required binding is unavailable.

Verification: `BannerlordVersionCompatibilityTests` invokes the same-run licensed-assembly probe under canonical validation. Its isolated objects and fake save-load store never open or mutate a campaign. Native new-start/existing-save, save/reload, economic recovery and UI acceptance are separate evidence; compilation or a hook count does not establish them. See [campaign/runtime testing](testing/CAMPAIGN_AND_RUNTIME.md#bannerlord-version-compatibility).

- Read TaleWorlds state and translate it into stable Reign DTOs.
- Call Reign through the existing integration clients.
- Apply returned commands through campaign behaviors and runtime patches.
- Keep calculations that can be deterministic and Bannerlord-independent in domain modules.
- Do not place PostgreSQL drivers, schema logic, or database credentials in the Bannerlord client.

Internal TaleWorlds-facing implementation changes normally use the owning module's Bannerlord facet at Tier 2. A client/server or adjacent-module contract change uses Tier 3 boundary validation. Shared DTOs, save serialization/schema, packaging, project references, and explicit release checks require Tier 4.
