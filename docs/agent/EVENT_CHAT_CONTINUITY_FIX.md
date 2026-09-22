# Event Chat continuity and earned familiarity

Date: 2026-09-09. Status: implemented and verified in an isolated Release artifact; not deployed by this task.

## Cause and repair

The Zeonica Courtyard Dance conversation with Zotyra contained saved NPC responses, but the next prompt's `context.loaded.eventLines` contained only player messages. The audit correlation `f15a9a1f8a704b97ab7e4f42f916bac7` in campaign `hWXFlXjqS2vS` established the discrepancy. Its audit evidence is in `%LOCALAPPDATA%/Bannerlord Reign/campaigns/hWXFlXjqS2vS/audit/audit.jsonl`.

`EventTranscriptLine` gives the player and every NPC in one group beat the same `turnId`. `PromptTranscriptIdentity` previously treated that ID as one utterance, so canonicalization retained the first player message and removed the NPC contributions. The key now includes role and stable speaker identity. A retry of the same contribution still collapses even when the file row ID changes. Existing stored transcripts recover on their next read with the corrected server; no migration or replay is needed.

The event reader also retains append order for equal timestamps. The former descending-sort-and-reverse sequence reversed messages written in the same second. The bounded canonical window still includes the complete exchange at its boundary.

## Other conversation scenarios

| Route | Finding and verification |
| --- | --- |
| Social events, including dances and feasts; generated wilderness events | Share the affected persisted event-history route in `SocialEventRespond`. The read-time correction applies to all templates. Isolated tests persist and reload two multi-speaker beats, including retry and same-second ordering cases. |
| Party Chat and castle sessions, including Family Chambers, Royal Council and Homes | Structured history goes through `BuildPartyChatPromptTranscript`, which projects speaker/role/exchange fields before canonicalization. It does not suffer the same wholesale NPC-side omission with its normal input. Shared-builder fixtures verify retention, replay suppression and exclusion of another session. Scene labels are routing examples, not independent native runs. |
| Individual dialogue and specialized direct modes, including official ambassador, noble docket, ruler petition and Court Life | Current-session database history projects `turn_id` as `id`, so it uses the role/speaker-aware derived identity rather than the faulty stable-turn branch. The grouped database roundtrip verifies a player plus two NPC contributions remain distinct. Full individual prompt tests assert both history sentinels appear exactly once. |
| Correspondence | Uses its received-letter input and NPC memory packet, not this event transcript filter. Envelope tests verify both the received letter and supplied prior NPC writing survive composition. This does not prove semantic memory retrieval for every historical letter. |

All callers of the shared canonicalizer gain the speaker-aware protection if they receive shared beat IDs. Provider-free tests prove history assembly, not the quality or novelty of generated replies.

## Familiarity and authority

Initial foreign-sovereign caution remains 85 under the ordinary default context. Earned positive `personalAffinity` reduces apprehension by 1.5 points per affinity point, rounded away from zero, capped at 30 points. Examples: affinity 0/5/10/30 gives danger 85/77/70/55. This calibration is an implementation choice and remains subject to dialogue acceptance.

Relief requires verified personal identity and a ranked authority relationship. Public standing alone, negative personal affinity, unknown identity, current lethal threats, coercion and captivity give no relief. Peer/subordinate behavior is unchanged. Existing relationship updates supply the affinity; no new familiarity counter, automatic friendliness grant or persistent schema is introduced.

The original authority danger still determines the defiance ceiling. Formal-address requirements and explicit informal-address permission remain independent. The prompt permits appropriate ease and warmth as apprehension decreases while retaining those conduct boundaries. Audit posture fields expose `authorityDanger`, `personalAffinity` and `familiarityReduction` so the adjustment can be inspected.

## Verification and runtime state

Manifest-selected `changed`, Tier 3 validation passed in 297.391 seconds: [validation report](../../.codex-build/reign-mcp/validation/20260909-055142-e84b397e/validation-report.json). Run ID: `20260909-055142-e84b397e`. Source fingerprint: `96c83f2540c449214930f8180802b90987b34da43c134e3029ca74c63d8d7624`. All six selected projects succeeded, all 431 MCP tests passed, and quick verification passed 102/102, including 34/34 sovereign-conduct cases. [Repository hygiene](../../.codex-build/reign-mcp/validation/20260909-055142-e84b397e/repository-hygiene-report.json) passed in enforce mode with no issues.

Final behavioral checks used that exact successful server artifact:

| Suite | Result | Durable evidence |
| --- | --- | --- |
| `offline / interaction_architecture` | 279/279 architecture cases plus settlement authority passed; 23.695 seconds | [Architecture report](../../.codex-build/event-chat-continuity-20260909/interaction-architecture-report.json), run `verify-1788933462231-281d0901` |
| `quick / prompt_efficiency` | 51/51 caching/composition cases and the prompt-budget check passed; 11.778 seconds | [Prompt report](../../.codex-build/event-chat-continuity-20260909/prompt-efficiency-report.json), run `verify-1788933515792-29c721b7` |

The earlier focused dialogue validation also passed 150/150: [report](../../.codex-build/reign-mcp/validation/20260909-052642-4adbd32e/validation-report.json). No provider calls or normal-campaign mutations were required. Prior failed reports remain available; two stale assertions were corrected to the existing authorities (21 previewer prefabs/20 runtime movies, and current native-Hero kinship wording). Concurrent narrative-tool compilation and tracking failures were repaired with their owning task before the successful final run.

This task has not deployed or restarted the game/server. The read-only runtime check found game PID 35464, server PID 1400 on port 5101, and dedicated Control Center PID 20320. A separate petition task installed validation `20260909-030628-962d7106`, predating this fix. Native dialogue and provider-backed demeanor acceptance therefore remain outstanding. They require installation through the supported visible lifecycle and the existing provider/disposable-save gates. Other narrative work continues separately in the working tree; these reports identify the immutable artifact actually tested.
