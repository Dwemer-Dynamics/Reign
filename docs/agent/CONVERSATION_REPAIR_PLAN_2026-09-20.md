# Michael–Hulara conversation repair plan — 2026-09-20

## Status and scope

The user stopped monitoring and requested this implementation plan. Automation monitor-hulara-conversation-and-drink-count is PAUSED; its saved status was verified. This document specifies future work. Product repairs, behavioral validation and deployment have not been performed by this task.

Last monitored turn: 2026-09-21 04:06:02 UTC. Recorded Hulara intoxication was 5.25 at native hour 2186439.5081026945. The same game instance lost 0.75 at 03:50 and 0.50 at 04:01, so correcting only those two recent increments would give 6.50. That is an accounting comparison, not an authoritative reconstruction of the entire evening: earlier reload ancestry and an unquantified drained remainder prevent a single proven historical total.

Implement three connected repairs: drinking accounting, accepted-action routing, and scene continuity. Preserve the earlier September 15 drinking repair and later action/identity/tournament work. The earlier [handoff investigation](CONVERSATION_DRINKING_HANDOFF_INVESTIGATION_2026-09-20.md) remains the evidence for the first whiskey failures.

## Evidence that the implementation must reproduce

Times are UTC on 2026-09-21; the local planning date is September 20, America/Chicago. Complete private captures are under .codex-build/investigations/drinking-handoff-20260920/. Use minimal anonymized fixtures in managed tests; retain full dialogue and prompts in local evidence.

| Time | Observation | Required repair |
| --- | --- | --- |
| 00:28:37 and 02:49:01 | Player offers whiskey skin; accepted NPC pull has completed sip metadata but beverage_unknown and zero consumed | Connect an evidenced offer to the NPC's accepted vessel and consumption; +0.25 per independent case |
| 03:03:00 | A mouthful is followed by a sentence identifying the same drink as kumis; zero consumed | Resolve a unique consumed object across the accepted descriptive sentence |
| 03:09:18 | Cooking phrase “testing its give. It tears clean” relinquishes the cup | Require a real vessel operation with the correct subject and object |
| 03:36:56 | Final “drains what's left” records zero because remaining volume is unknown | Carry justified refill volume; distinguish unquantified consumption from no consumption |
| 03:50:12 and 04:01:15 | Accepted “refilled cup” retains remaining=0; three then two portions rejected as vessel_empty | Recognize accepted refill descriptions and reset volume before consumption |
| 03:54:38 and 03:57:35 | “accept the rope” metaphor and a handshake clear vessel state | Non-drink clauses must leave vessel state intact |
| 03:57:35 | Model labels sip, while “takes a drink” forces one serving | Make the generic-quantity policy explicit and test adjective variants |
| 04:06:01 | Sip and pull correctly consume 0.50, then “swallows hard” erases the remaining half cup | Treat a completing swallow as part of the preceding ingestion |
| 03:57:35 | Previously cooked/eaten lamb is presented as raw and needing preparation | Preserve completed scene activity and check contradictory continuation |
| 03:27:18 | Possible reversal of the seven-tournament attribution | Preserve actor/source attribution; test native victory ownership without treating this inference as a proven participation history |
| 04:06:02 | NPC explicitly accepts the clan banner; saved memory records agreement; gate is roleplay_only and queues nothing | Reconcile accepted commitment with routing; prove native execution separately |

Evidence bundles: evidence.json, monitor-20260921-0314.json, -0329.json, -0337.json, -0350.json, -0357.json and -0405.json.

## Implementation order

### 1. Build complete-exchange regression fixtures

Start with the captured player request, prior vessel/count, accepted final reply, model events, native clock, stable scene identity and expected state. Run them through the production seams, including final-text cleanup, rather than testing only individual regular expressions.

Use two layers: focused clause/transition tests for diagnosis, and sequence tests that assert the final counter, held contents, remaining volume, receipt and next-turn behavior. A counter assertion alone misses the false cup-clearing defects.

Retain the existing DRINK-001 through DRINK-090 corpus and its negative controls. Add fixtures to the existing verification suites; avoid creating another standalone runner.

### 2. Repair drink interpretation and vessel transitions

**One ordered event stream.** Interpret acquisition, refill, consumption, spill, relinquishment and replacement in accepted action order. Each event identifies its speaker, object, source span and certainty. Both quantity and vessel resolution consume that same interpretation, eliminating the smaller conflicting adjective/reference recognizers.

**Ground vessel operations.** An operation must act on a drink vessel or an unambiguous immediate reference to it. Bound subject/object resolution to the relevant clause. A noun such as “hand” or “give,” a metaphorical rope, or a pronoun referring to meat cannot clear the held cup. Unknown non-drink text has no vessel side effect. Actual handoffs, spills, empty descriptions and replacements retain their effects.

**Offer and acceptance.** Read the current request's action spans into a bounded proposed-offer context: source turn/span, offering actor, recipient, vessel, catalogue contents and nullable volume. Promote it only when the final NPC action uniquely accepts or consumes that vessel. An offer never supplies NPC consumption by itself. Refusal, wrong recipient, water substitution, multiple candidate vessels, historical/conditional wording and ordinary speech remain negative controls.

**Identity and possession.** Keep one unambiguous vessel's identity separately from whether this NPC currently holds it. Handing back a known skin changes possession; unique reacquisition can recover its contents. Do not borrow from unrelated scenes or unverified reloads. Add optional versioned evidence/possession fields to the existing state representation only after proving its snapshot compatibility.

**Refills and contents.** Recognize “refilled cup,” “freshly filled cup,” explicit refill verbs and a cup explicitly found full. For an ordinary cup an established full refill supplies one serving. “Fresh cup” alone or an unspecified pour does not prove volume. Skins, bottles and jugs retain unknown capacity unless stated. Recognize the named beverage in an adjacent descriptive sentence only when it refers uniquely to the consumed object; nearby alcohol never converts another vessel's water.

**Occurrence binding.** Retain the exact final action and selected occurrence. A supplied evidence quote may have a different descriptive boundary if its unique literal span identifies that same consumption. It must not bind to acquisition alone, another occurrence, two consumptions or deleted text. Rejected metadata must not erase independently supported final narration or produce a second increment.

**Swallow continuations.** “Takes a mouthful … swallows it,” “swallows hard” and equivalent completion language represent one ingestion. “Takes another sip” establishes a separate portion. A continuation neither consumes again nor invalidates the remaining beverage.

**Recommended quantity clarification.** Preserve full serving=1, half=0.5 and sip/pull/mouthful=0.25, explicit fractions/counts, known-volume caps and the existing tolerance/recovery rules. For a generic act such as “takes a drink from the cup,” use compatible bounded event metadata, then the existing quarter-serving fallback; the noun phrase alone should not force a whole cup. An explicit full cup or stated full serving remains 1. This deliberately changes the current bare-“a drink” heuristic: the captured event marked sip would become 0.25, and adding “slow” must not change the answer. Update the affected legacy expectations and documentation together.

**Unmeasured remainder.** Never invent a bottle's capacity or an exact amount after an unspecified spill. Resolve normal refills upstream. If a proven final drain still has unknown volume, preserve an explicit unquantified-consumption/measurement-quality outcome in the audit instead of presenting it as evidence that no alcohol was consumed. Define and test this outcome alongside numeric acceptedAmount; do not backfill old receipts.

**Commit once.** Adjudicate the final accepted reply after all repairs/cleanup, then atomically commit counter, vessel and receipt under existing locking/replay protection. Preserve Endurance tolerance, campaign-hour recovery, threshold refusal, stale-clock protection and Save Sync rollback.

### 3. Reconcile explicit clan acceptance with the action gate

The live catalog already exposes mechanical join_clan. The observed failure is before the planner: QueueAcceptedDialogueActions consults ActionGateShouldPlan, then skips both planning and suggested actions when the gate is false. This is different from the older Chagun pending_hook capability failure.

Make “an accepted action needs execution” distinct from “the action has already executed” in the gate's production contract. A concrete current offer plus the NPC's final present acceptance should be eligible for normal planning even though no native transfer has occurred yet.

Add a bounded acceptance-consistency check before the early skip. It must consider the current negotiated offer, actual NPC speech, source/destination identities, unfulfilled conditions, revocations and intoxication restrictions. A mismatch sends only the validated candidate to the existing planner/validator; it never directly edits clan membership or treats a banner metaphor as consent.

Start with the captured permanent-clan case and the existing guest/departure/gift controls. Keep temporary scouting/travel, wearing colors, future plans, quoted consent, player-authored NPC acceptance and conditional bargains distinct from permanent recruitment. Preserve exact negotiated terms and normal eligibility checks.

Record an accepted-but-unqueued action as an actionable routing failure with the existing user-visible failure/clarification behavior. It must not disappear as no_action_detected with empty errors. Keep three separate states: agreement reached, action queued, native effect confirmed. Narrative memory may record the agreement; authoritative clan membership changes only on the native result.

The native JoinClan handler already assigns and verifies actor.Clan and emits hero_joined_clan. Verify that path on an authorized disposable save, including retries and save/reload; change the client only if that proof reveals a separate defect. The current failure does not justify bypassing native validation.

### 4. Preserve scene progress and attribution through compaction

Historical prompt evidence 6a2854eaff2148f3a254708492675af3 was captured before the failing 03:57 turn. It contains both a memory that the meal was completed and an older raw-lamb preparation passage. The live transcript is shortened. Therefore lost retrieval alone is not a proven explanation.

Compact complete attributed records rather than cutting character ranges inside multiline dialogue. CompactPromptEvidenceBlock currently keeps head/tail text and only seeks newline boundaries; a newline is not a speaker, turn or source boundary. Every retained fragment needs its actor, turn/session, time, provenance and an explicit partial-excerpt marker if excerpting is unavoidable.

Keep a bounded current-scene progress section outside general history compaction. Reuse existing accepted conversation state and source-bearing turns for the latest activity, completed steps, current props and pending agreements. If an added structured progress field is necessary, put it in the existing dialogue response/state transaction with literal source references, no extra provider call, and explicit snapshot tests. Location/clothing state currently does not cover cooking or other completed activities.

Separate current accepted scene facts from historical narrative and native world facts. New low-impact backstory and in-character self-correction remain allowed. Latest player corrections and actual new objects/repeated activities must be able to advance the scene; a genuinely new shoulder can be cooked again. Do not treat an NPC's earlier mistaken claim as native truth.

Extend the existing continuity check/repair path to compare proposed activity against supported completed steps and to preserve the actor owning each native achievement. The seven-victory source names Michael; it does not authorize invented spectator attendance. Retain existing secrecy, witness, known-identity and group-attribution constraints.

A continuity repair must be rechecked against the final action gate and drinking evidence. Do not let an edited reply retain stale acceptance, consumption or source spans from its discarded draft. Acceptance tests must inspect the same final reply shown to the player.

## Required behavioral proof

| Test group | Pass condition |
| --- | --- |
| Whiskey offer + accepted pull | +0.25 with correct beverage and source/acceptance evidence; handback/reacquisition works |
| Accepted refilled empty cup + three portions | Recorded baseline 3.75 becomes 4.50; the old empty state cannot cap all portions to zero |
| Full cup + sip + pull + completing swallow | +0.50, same kumis vessel remains at 0.50, no third ingestion |
| “give” in meat, handshake, rope metaphor | Zero drink increment and unchanged unrelated vessel |
| Completed mouthful, beverage named next sentence | +0.25 only for the uniquely identified consumed beverage |
| Shortened evidence span, repaired action index, cleanup | One correct occurrence; duplicates/deleted/ambiguous spans cannot add consumption |
| Quantity wording pairs | “drinks”/“takes a drink” with the same compatible sip evidence agree; explicit full cup remains 1 |
| Known remainder, unknown pour, spill, bottle | Respect known caps; unknown capacity never becomes an invented full serving |
| Refusal, water, another actor, historical/conditional text | No incorrect alcoholic increment or possession transfer |
| Retries, concurrent calls, save rollback, stale clock | One commit per accepted turn; exact restored receipt/state; campaign-time-only decay |
| Threshold crossings | Correct 50%, 75%, 100% symptoms and 100% commitment suppression; restriction released only below half |
| Captured clan offer + final acceptance | Exactly one valid join_clan candidate/queue entry; native proof remains separate |
| Guest, banner disguise, revoked or conditional acceptance | No permanent clan transfer; clear unresolved state when clarification is needed |
| Queue/native failure or retry | Explicit failure/pending result, no claimed native completion, no duplicate transfer |
| Cooked/eaten meal + negotiation + handshake | Meal stays completed; old preparation cannot become the current task again |
| New meal or explicit user correction | Legitimate new preparation proceeds; no blanket prohibition on repeated activities |
| Tight prompt budgets / shared group turns | Whole attributed records retained, current progress preserved, no speaker/witness reassignment |
| Individual, Party/Castle Chat, social-event routes | Shared adjudication and final-reply ordering yield equivalent accounting and commitments |

These fixtures must pass on isolated test state. A historic audit replay may compare calculations; it must not execute the user's old commitments or rewrite campaign counters.

## Owned paths and existing overlap

Implementation belongs in sibling D:/Projects/ReignServer; logical ReignServer/... paths below are used by the canonical validation planner.

- Drinking: src/Modules/Dialogue/ConversationIntoxication.cs, ConversationBeverages.cs, ConversationDrinkingLanguage.cs, ConversationDrinkVessels.cs, ConversationDrinkingRegressionTests.cs, ConversationIntoxicationSelfTests.cs; docs/server/ConversationIntoxication.md.
- Action routing: src/Modules/Dialogue/DialogueActionReliability.cs and DialogueActionReliabilitySelfTests.cs; the existing queue seam in src/Modules/Platform/Program.cs.
- Continuity: src/Modules/Dialogue/PromptCaching.cs, RoleplayContinuity.cs, MemoryRouting.cs, ConversationSceneState.cs as needed, and the existing conversation/prompt tests.
- Catalog/procedure: D:/Projects/Reign/reign.testing.json and docs/agent/testing/DIALOGUE_AND_PROVIDERS.md, plus applicable prompt/receipt contract documentation. Update schema descriptions and negative/cleanup coverage in the same change.
- Native proof reference: D:/Projects/Reign/ReignBeta/src/Modules/WorldSimulation/World/ReignRegularActionExecutor.cs, JoinClan. No client modification is presumed.

Planning baseline: both checkouts are on unstable. Client HEAD 2908e0d71514480364091deff06bdd4eb0463b59; server HEAD 0c551f29b3d3418f2bd65ec98ef1d8a4be8863d1. There are extensive unrelated client deltas and 34 existing server deltas. Program.cs and ConversationSceneState.cs already contain changes; preserve them and inspect exact ownership before implementation. At the final 04:23 UTC check, Action Testing was active and validating Program.cs and DialogueActionReliabilitySelfTests.cs. It confirmed ownership of a gold-command routing repair and agreed to preserve this task's documentation. Reconcile those changes before implementing section 3. Balance noble armor tiers was idle. Recheck active tasks, worktrees, both Git states and the validation lease at execution time.

## Validation and release sequence

1. Add the coherent fixture and implementation changes, including catalog/procedure updates. Obtain a fresh exact-path plan if scope changes.
2. The complete proposed source/catalog scope currently selects changed / Release / Tier 3. Focused drinking-only or continuity-only source plans select Tier 2; the catalog and combined integration scope require the broader plan. This planning run did not execute those builds.
3. Recheck reign_get_validation_status and use task UUID 01a0c1cd-7ba6-7282-b24f-9373eff99c1f with reign_validate. Use the lowest manifest-selected tier and normal hygiene. Do not build each work package separately when one coherent final build covers it.
4. On that exact successful Release artifact, run conversation_intoxication and prompt_efficiency through reign_run_offline_verification. Include the production action-reliability and prompt/continuity contracts: the general quick suite alone does not prove these cases. Add persistence/snapshot proof if state representation changes.
5. Review every captured expected/actual receipt, zero false-positive controls, native-action queue outcomes, source fingerprint and hygiene. Document residual unquantified volumes and model/native coverage honestly.
6. Produce the compatible Linux artifact once when deployment is authorized, deploy exact validated artifacts through the dedicated ReignServer procedure and verify hashes/health. Retain the runtime rollback artifact.
7. Complete authorized native/provider acceptance on an enrolled disposable copy: varied fresh wording, all affected routes, actual clan membership and exactly-once receipt, threshold/recovery, scene continuity and save/reload. The original AlphaOne and existing transcript are not test fixtures.

Completion means correct counters and vessel state, no unrelated-text mutations, preserved scene progress, and accepted mechanical actions either execute with native proof or surface an accurate failure. A successful compilation, remembered agreement or healthy server alone does not meet that condition.

## Current delivery

This turn stops the automation, saves this plan, updates the earlier investigation and existing roadmap entries, and runs documentation-only Tier 1 validation. No code fix, provider test, campaign correction or deployment is claimed.
