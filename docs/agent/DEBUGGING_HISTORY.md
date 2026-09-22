# Reign Debugging History

## 2026-09-09 — Pending government agreement prevented a paused checkpoint

Court Life advancement reached day 91164.5055 with a responsive paused game and drained queues, but the checkpoint controller repeatedly tried to acknowledge a nonexistent diplomacy announcement. World Test counted an NPC peace agreement with `announcementReady=false` and `executionStatus=executing`; the native delivery API correctly withheld it while government consideration continued. Advancing time to bypass the save guard would have hidden the mismatch.

Both announcement counts now require `announcementReady=true`, matching delivery eligibility. Pending actions retain their ordinary government deadlines and failure checks. Two regression cases compare the overview with the production delivery API across pending, ready, delivered, acknowledged, missing-readiness, and completed-government states. Validation `20260909-155413-19d3c4d7` passed Tier 3, 446 MCP tests, quick verification and enforce-mode hygiene; the same artifact passed all 126 World Test checks in the isolated baselines suite. Only the server executable and symbols were deployed, preserving the paused native process and installed client.

Native proof: the same campaign state then reported zero ready announcements and retained the pending action. The guarded checkpoint completed native saving, finalized Save Sync, and returned `postSaveRuntimeQuiesced=true`. Evidence: `.codex-build/court-life-acceptance/20260909-resume/announcement-readiness-checkpoint-report.json`, `announcement-readiness-regression-evidence.json`, and `court-contract-deployment-20260909-155413-19d3c4d7/deployment-evidence.json`. A separate earlier native freeze at day 91164.916 remains unproven and is not claimed fixed by this correction.

## 2026-09-09 — Shared event beat IDs removed saved NPC replies from prompts

Zotyra's Event Chat responses were present in storage, but the next prompt retained only player messages. `EventTranscriptLine` assigns the player and all NPCs one shared `turnId`; `PromptTranscriptIdentity` had treated that beat ID as one utterance. The key now includes role and stable speaker identity, preserving each speaker while collapsing retries. Stable ascending timestamp order also preserves same-second append order. Existing saved transcripts recover on read; no migration or replay is needed.

The same reader serves social and generated-wilderness events. Normal party/castle structured history and current-session individual/official database projections avoid that stable-ID branch; correspondence uses a separate input/memory path. Regression coverage verifies persisted multi-speaker beats, retry suppression, exchange boundaries, session isolation and complete prompt envelopes. Earned personal affinity now eases authority apprehension without weakening initial caution, formal-address permission or conduct limits.

Evidence: `20260909-055142-e84b397e` passed `changed` Tier 3, 431 MCP tests, 102 quick checks and hygiene. Its immutable server passed 279 architecture cases plus settlement authority and 51 prompt cases plus the budget check. [Cause, scope, calibration and exact reports](EVENT_CHAT_CONTINUITY_FIX.md). Follow-up: deployment and native/provider dialogue acceptance remain separate; this task did not alter the running campaign.

## 2026-09-07 — Save Sync startup mistook a database outage for obsolete snapshots

During the Court Life baseline preparation, PostgreSQL returned `57P03` (database starting up). `SaveSyncPointSnapshotAvailable` converted the exception to false, and startup cleanup interpreted false as authority to delete snapshot directories and rewrite the ledger. Six registered points across two campaigns lost their filesystem payloads; native saves and PostgreSQL snapshot schemas survived. A database-only availability check subsequently masked the missing files.

Startup inspection now preserves all files and ledger bytes for both failed database probes and missing snapshots. Availability requires the filesystem manifest, campaign directory, and PostgreSQL snapshot. Two fault-injection cases in the Save Sync self-tests prove preservation. Validation `20260907-185527-1f35f391` passed the persistence boundary and hygiene gate; its exact server artifact was deployed. This prevents recurrence but does not recover already deleted files.

The initial preservation export also failed on a deeply nested audit filename beneath the Steam installation path. Export staging now uses a short temporary root. The subsequent 421,081,743-byte archive completed and contains the native saves and PostgreSQL dump. It preserves the post-incident state, not the missing original snapshot payloads. BaseThree creation remains pending exact baseline recovery; later acceptance files must not be substituted for BaseTwo's earlier state.

Evidence: `.codex-build/court-life-acceptance/20260907/save-sync-startup-incident.json`, `server-deployment-20260907-185527-1f35f391/deployment-evidence.json`, and `QIHzLi6DhpYF-post-incident-preservation.zip` (SHA-256 `B06A108D2F70CA4E43D9D81ED2E35822A181E59C53F266822A0B113D91363AAC`) in the same directory. Follow-up: recover matching snapshot files from an independent backup; retain completed BaseTwo acceptance evidence without replaying it merely because the baseline changes.

## 2026-09-07 — Pregnancy withdrawal displaced a rural notable into a town

Court Life testing on the disposable BaseTwo branch crashed during native daily recruitment. The managed stack identified `DefaultVolunteerModel.GetBasicVolunteer`; a read-only heap census found no null hero cultures, but one rural notable, Ewyn of Palisont (`CharacterObject_4669`), was in Sargot (`town_V1`) instead of her birth village (`village_V3_4`). The Reign log recorded pregnancy withdrawal moving her there. Native volunteer selection dereferences `CurrentSettlement.Village.Bound` for a rural notable, so the town residence violated its assumption.

`ReignFamilyCampaignBehavior` now leaves partyless settlement notables in place and restores already displaced active pregnant rural notables to their native birth village. The repair must run in `OnGameLoaded` as well as initial preparation: sealed campaign reloads skip `PrepareInitialState`. No native volunteer patch was needed. The repaired load logged the village restoration, then native day advancement and the pending patronage delivery completed without the crash.

Evidence: `.codex-build/court-life-acceptance/20260907/native-stacks-3300.json`, `native-roots-3300.json`, and `native-before-ui-recovery.log` in that directory; validation reports `20260907-155755-d3dd2083` and `20260907-161127-098c3c42`; native advance `live-1788798606435-25e48e70` and delivery observation `live-1788798700095-c09b6a43`. This proves recovery of the affected saved notable and subsequent daily tick; the full pregnancy feature matrix is separate coverage.

## 2026-09-06 — Alpha saves and diplomacy payer mismatch

MH1 and MH2 were written successfully but failed before Save Sync restoration with `Array dimensions exceeded supported range`. Read-only archive inspection found `_reign_startingChildren_v1` state occupying a 47,816-byte strings entry in each save, wrapping the signed Int16 length to -17,720. The adapter now writes bounded codec chunks under `_reign_startingChildren_chunks_v2`, leaves the old raw key empty, and reads compatible legacy state without rerunning child seeding. The user declined recovery; neither save was modified or deleted. A 120-household signed-short archive regression preserves the full payload. Evidence: `.codex-build/reign-mcp/validation/20260906-041733-d0d0d15f/validation-report.json` (8 focused checks), `.codex-build/reign-mcp/validation/20260906-041835-1925dc47/validation-report.json` (357 MCP checks plus persistence/court boundaries). Native new-campaign save/reload remains separate acceptance.

The Halthdar/Garios 650,000-denar, 84-day alliance failed because negotiation compared aggregate kingdom clan wealth against a transfer debiting the ruler. Diplomatic packages now expose/check `leaderGold`, reject unknown paid balances and revalidate counteroffers; aggregate `treasury` remains strategic context and the native execution guard remains authoritative. Character-editor polling also now checks owning campaign before accessing campaign time or applying delayed commands, preventing the menu-time null reference and cross-save application. Combined product verification and deployment evidence is recorded in `.codex-build/alpha-fixes-20260906/`.


Record costly or recurring failures after the root cause is known and the fix has evidence. Include failed approaches only when they would save future investigators meaningful time; do not paste raw logs or secrets.

## Entry format

### YYYY-MM-DD — Area — Symptom

- Context or symptoms:
- Decision or root cause:
- Alternatives/failed approaches:
- Consequences or successful fix:
- Verification/evidence:
- Follow-up:

### 2026-08-16 — Repository recovery — Fresh clone could not bootstrap validation

- Context or symptoms: The private remote cloned all 1,900 tracked files cleanly, but its first fallback validation failed with `NETSDK1004` because the MCP validator had no generated package-assets file.
- Decision or root cause: `reign-validate.ps1` accepted `-Restore` for managed validation but unconditionally bootstrapped the validator itself with `--no-restore`, so the restore option could never repair a pristine clone.
- Alternatives/failed approaches: Treating Git source recovery as sufficient would have left the documented validation route unusable; manually restoring outside the canonical script would have created an undocumented alternate workflow.
- Consequences or successful fix: The bootstrap now omits `--no-restore` only when `-Restore` is explicitly supplied, while ordinary runs remain no-restore. A real-script test uses a controlled `dotnet` shim to verify both bootstrap and validator invocations.
- Verification/evidence: The regression failed on the original bootstrap arguments and passed after the fix (`1/1`, 286 ms). The final fresh-clone validation evidence is recorded with the repository bootstrap checkpoint.
- Follow-up: Keep the fresh-clone proof in repository publication or transfer work; do not infer recoverability from push success alone.
