# Shared activity memory and plan completion — 2026-09-25

Status: implemented and offline validated on 2026-09-25; deployment is paused at the user's explicit request. Product Release Tier 4 `20260925-213711-488d755a` passed 767 MCP, 11 relationship and 110 quick checks. The final artifact passed all 821 focused prompt checks; Linux publish `20260925-214808-7dece86b` and enforced hygiene passed. Exact reports, fingerprints, hashes and acceptance gaps are in `.codex-build/investigations/shared-activity-memory-20260925/READINESS.md`. Native/provider acceptance remains a separate gate. This extends the existing roadmap item **Preserve completed scene activities and speaker attribution through conversation prompt compaction**. It does not replace the prior [continuity plan](CONVERSATION_CONTINUITY_PLAN_2026-09-21.md) or [precision-memory design](MEMORY_PRECISION_UPGRADE.md).

## Intended behavior

Characters should remember activities they actually shared with the player, recognize what finished, and continue from the aftermath without needing the player to retell it. This applies even when the next message is unrelated and even when nobody explicitly promised the activity beforehand.

For an agreed trip to Pravend to enter its tournament, arrival closes the travel step, the actual tournament result closes participation and records the outcome, and the next conversation starts after that experience. A later trip to Pravend is a new occurrence. An earlier promise to scout its approach may become obsolete after arrival; arrival does not prove that scouting was performed.

For hunting looters, the native battle outcome supplies the defeated party, participating group, result and supported casualties. Defeating a party can finish that particular hunt; it does not mean every opponent died or every bandit in the region is gone. Characters should react naturally to victory, defeat, retreat, wounds, losses and rescue, without obligatory recap speeches.

2026-09-25 addition: characters should also know when their last recorded chat with the player happened, without treating recorded chats as their only contact. When they actually travel together, assume some ordinary interaction between those chats. Supply small, consistent details of shared daily life without inventing consequential conversations, decisions or relationship progress. Weeks between chat sessions must not automatically become weeks of silence.

## Verified current failure

Campaign `woERXl3xD23E`, timeline `main_258fb5560008_recovered_64e99061c8`, observer Hulara (`reign_court_town_K1_child_2`):

| Evidence | Observation |
| --- | --- |
| Native arrival, day 91120.02848629862 | Michael entered Pravend; event `...000000091489_f5b6e26b`. |
| Native tournament, day 91120.2633078206 | Michael and the team won at Pravend; Hulara, Temurtai and Ulagara are explicitly winning teammates. Event `...000000091900_2c4122d7`. |
| Reply at 2026-09-25 19:26:33 UTC, current day 91122.58348 | Hulara says “I'll wear it at Pravend” and asks “How far to Pravend from here, and has Ulagara given you the first leg yet?” Correlation `539a5cba1eed46f892d7731272b62deb`. |
| Exact provider live-turn message | Still contains the day 91112.82 planning conversation and a **current plan** to scout approaches to Pravend before arrival. It has no authoritative Pravend result or corresponding native event ID. It does contain a public rumor about the victory, explicitly labeled unverified. |
| Player correction at 19:44:39 UTC | After “we just won that tournament,” Hulara acknowledges the win but again demands the first route leg before scouting. Correlation `921400fa45a94f56b0d03c9ae60e4a35`. |
| Nearby native battle, day 91122.50939024537 | A completed Hideout battle records the player's side winning. This demonstrates existing battle evidence, not a verified reproduction of the user's looter example. Event `...000000095480_b5a4d33c`. |

The inspected live message is the entire message 2: 73,728 characters, evidence ID `6d968d83fe354f4b88645a6db927298d`, SHA-256 `B91C940FFCD8D23C5D6C5E2CB3550CA6832AC8B92CB072FAD5AF63A285D9B73D`. Its hash matches the provider capture. The other two messages were not fully paged in this investigation.

Precision diagnostics for this turn are in **shadow** mode and include native-history candidates. That does not establish that those candidates reached the model. The actual serialized live message, rather than the shadow packet or a generic coverage flag, establishes the omission. The audit identifies the missing final evidence and stale state; it does not isolate the loss to one legacy retrieval/selection branch.

Private evidence: `.codex-build/investigations/shared-activity-memory-20260925/evidence.json` and `provider-live-message.txt`. Keep full player transcripts and prompts out of the public server repository.

## Existing foundations and gaps

| Current source | Reuse and required extension |
| --- | --- |
| `ReignBeta/src/Modules/WorldSimulation/Campaign/ReignWorldHistoryCampaignBehavior.cs` | Already observes arrivals/departures, tournament starts/results, battles, raids, sieges, prisoner changes, quests, trade, troop changes, armies, clan changes, family and ship events. Extend payloads and participant evidence; do not install duplicate listeners for the same outcome. |
| `ReignBeta/src/Modules/WorldSimulation/Integration/ReignWorldHistoryClient.cs` | Existing outbox, retry, batching, routine coalescing and sequence-aware flush. Add a bounded pre-dialogue activity synchronization path without blocking the native UI or flushing the whole world's backlog. |
| `ReignServer/src/Modules/WorldSimulation/WorldHistory.cs` | Canonical source events, entities, knowledge rules and ingestion. Add idempotent activity projection at ingestion, with existing campaign/timeline authority. |
| `ReignServer/src/Modules/Dialogue/WorldHistoryDialogueMemory.cs` | Native recall is topic-gated, with special recognition of recent tournament wins. General recent shared activity needs its own deterministic recall lane independent of the latest keywords. The six-result topical selection is insufficient as the only completion mechanism. |
| `ReignServer/src/Modules/Dialogue/ConversationContinuity.cs` | Existing versioned agenda/episode/relationship records, scene handoffs and completion criteria. Add native-backed completion transitions and a resolved view of legacy current-plan fields. Existing writes primarily depend on accepted dialogue output. |
| `ReignServer/src/Modules/Dialogue/MemoryContextV2.cs` and `Program.cs` | Implement one shared activity selection helper used by both the active legacy packet and precision mode. Native candidates in the precision path currently have `Required=false`; changing only that shadow path would not fix the captured live request. |
| `ReignServer/src/Modules/WorldSimulation/WorldHistoryRetention.cs` | Routine history is compacted after 15.75 campaign days, with some types after 5; significant history after 63. Preserve source-backed personal milestones and completion receipts before aggregate retention discards their detail. |
| `ReignServer/src/Modules/Dialogue/SemanticMemory.cs` | Routine arrivals and many trade/quest details are excluded from the significant-event embedding list. Immediate awareness must work without embeddings or an LLM summary. |
| Dialogue receipt, scene, group transcript and Save Sync integrations | Reuse accepted-only writes, exact action receipts, witness/privacy boundaries, source coverage and rollback machinery. Extend their contracts together. |

Three payload limitations matter:

- Arrival/departure records name the party and traveler/leader; they do not snapshot all accompanying heroes. Current membership cannot retroactively prove who traveled.
- Battle hero entities are derived from party rosters, including at completion. That is group association, not proof that every member spawned, remained conscious or personally fought. Capture participation before removals/deaths and refine it with mission evidence.
- Battle summaries are generic, such as “Hideout completed near Hideout.” Preserve opponent identity/type, winner, retreat, casualty categories and meaningful location in structured facts. Server entity compaction must not erase the enemy type needed to distinguish looters from other enemies.

## Proposed flow

`native outcome / verified action receipt / accepted scene outcome`
→ `canonical source event`
→ `activity and observer projection`
→ `matching plan-step transitions`
→ `required recent-outcome context`
→ `dialogue and durable recall`

### 1. One factual activity record with observer references

Keep native world history and accepted receipts as the sources of truth. Add a rebuildable activity projection in the same campaign database, not a separate competing truth store.

Suggested logical records:

- **Activity:** schema/revision, occurrence ID, activity kind, campaign, timeline, restore generation, source event/receipt IDs and hashes, original correlation, native sequence, start/end world day, wall-clock receipt time, location, targets, phase, outcome and supported facts.
- **Observer:** activity ID, hero ID, role, acquisition basis, learned day, evidence IDs, visibility and completeness. Store one shared outcome with small observer references, not full copied prose for each companion.
- **Plan step:** accepted-intent source, owner and agreeing participants, activity kind, exact target/place where known, earliest eligible event/time, occurrence constraint, completion predicate, parent ID, status, revision and matched evidence.
- **Projection cursor:** source watermark and restore generation, so ingestion/retry/repair can prove which events have been applied.

Contact continuity adds separate explicit-exchange, co-presence and modeled-routine records described in section 6. Modeled routine never acquires a native-outcome or accepted-dialogue evidence type.

Observer roles distinguish `participant`, `direct_witness`, `accompanying_party_member`, `received_report` and `public_news`. Personal participation, watching the event, traveling with the party and hearing about it must produce different wording. Prisoners, absent scouts, companions in another party, new recruits and people left behind do not acquire firsthand memories automatically. Joining later does not backfill attendance.

The current native battle roster supports “your party won”; it does not alone support “you struck the last blow.” Wounded/excluded members may know the result through an explicit return-to-party report while lacking details from the fight. Record that report as a separate acquisition.

### 2. Reliable delivery before the next reply

Capture small native snapshots on the game thread; serialize/project them off-thread. Reuse existing IDs, timeline outbox and retry boundaries. Do not call an LLM per arrival, kill, transaction or campaign tick.

Carry an **activity watermark** with conversation requests. Flush relevant pending player-group outcomes asynchronously before assembly and have the server acknowledge their projection. Sequence gaps from unrelated world traffic must not stall every conversation; use an activity-specific accepted-event set/cursor with explicit gaps.

If synchronization fails, retain the outbox, expose a bounded diagnostic and avoid claiming an unobserved completion. Do not silently fall back to presenting a known stale plan as current. Do not freeze Bannerlord indefinitely waiting for server or vector health.

Routine world-history coalescing must not collapse two separate visits into one activity or discard the roster/occurrence identity needed for completion. Derive player activity occurrences before lossy aggregation, or exempt those records from that coalescing while still batching their transport.

### 3. Close the right part of the plan

Track agreement separately from execution. A discussed destination creates no map command, changes no party AI and grants no companion consent. Use existing accepted dialogue to register an intention only when its owner actually accepts it. Events still become memories when there was no registered intention.

Recommended step lifecycle: `proposed → accepted → in_progress → completed | failed | cancelled | superseded`. Keep outcome separate: a tournament can be finished with a loss, a siege can end through abandonment, and a quest can terminate by expiry.

Completion predicates are deterministic once the intent has been resolved:

- Reach a named place: actual arrival there after acceptance, by the intended travelers.
- Enter/finish a specific tournament: same location and occurrence; entering is not winning. Require winner/team evidence for a win.
- Defeat a looter group: matching enemy party or supported looter category, player's participating side wins. Use a count only if the agreed objective specifies one.
- Buy/give/recruit/release: exact successful native receipt with the agreed recipient, item/person and quantities.
- Quest or escort: the authoritative quest outcome, including success/failure details; merely reaching its destination is not full quest completion.

Matching uses stable entity/occurrence IDs, time/sequence bounds, participants and source lineage. No keyword-only “completed” scan and no model declaration can manufacture success. If “that tournament” could refer to multiple occurrences, leave the link unresolved while supplying the known event facts. Repeated visits and tournaments get new occurrence IDs.

Finish only proven steps. Arrival can supersede preparatory route questions without proving scouting occurred. A successful tournament does not resolve “earn the clan's trust.” Finding a relative alive does not automatically heal an emotional concern. Partial hunts, cancelled plans, interruptions and failures remain explicit.

### 4. Reconcile stale state, then inject compact evidence

Render a mandatory **Recent shared activities and resolved plans** block before the next reply, regardless of the latest message's topic. Priority is:

1. Outcomes that settle or contradict a currently supplied plan/scene.
2. Significant shared outcomes since this observer's last recorded explicit exchange, retaining any relevant outcome not yet supplied to that observer. Routine contact does not advance this coverage boundary.
3. Relevant current-trip milestones and unresolved steps.
4. Older activity retrieved for the current subject.

Proposed ordinary target: roughly six compact outcomes and 800–1,500 tokens, measured against the existing context policy. This is not a hard cap that drops essential facts. Merge related source records into one complete episode, deduplicate shared blocks/rules, expand within verified capacity when necessary, and protect the existing relationship foundation.

Example context for Hulara:

> The Pravend trip's arrival and tournament steps are complete. On day 91120.26331, Michael, Hulara, Temurtai and Ulagara won its team tournament. Hulara is a verified winning teammate. Treat the earlier first-leg/arrival preparations as historical for that trip. Actual scouting completion is unverified. A new trip or separate unresolved concern requires its own evidence.

Hypothetical looter example:

> Your party defeated the identified looter group after agreeing to pursue it. You participated in the encounter. The recorded outcome is victory; some enemies were captured or escaped. This encounter is finished. Any broader promise to clear the region remains open.

Do not render that looter example as an actual fact about the user's current save.

Resolve the active-plan view, legacy `currentPlan`/`currentCrisis` fields, scene handoff, current location and old summaries together. Keep their original history and provenance; do not simply delete every plan mentioning Pravend. Invalidate only the superseded portion, retain unrelated concerns, and label the old text as historical.

Use fresh native location for “now”; a last-known home/settlement is not current presence, and a historical destination is not a live travel order. Preserve plausible established camp/room detail only while compatible with native movement.

Require each relevant completion source or its covering activity record to survive the **actual final serialized prompt**. Check both active legacy and precision modes, not only shadow diagnostics. A public rumor about an event must not demote a speaker's proven firsthand experience.

Use the same packet for individual chat, Party Chat, social/wilderness events and correspondence, with channel-specific knowledge. A letter can refer to a shared past without implying its writer is currently present. Later group speakers receive earlier accepted replies and need not repeat the same recap.

### 5. Make activities available in history and long-term memory

Expose typed narrator/event entries interleaved with conversation by native time, clearly identifying observed activities separately from the modeled everyday contact in section 6. Neither becomes invented player/NPC transcript dialogue. Keep the original transcript immutable. The history view can join these typed entries; no UI redesign is required for the first implementation.

Store consequential episodes for later recall with the participants, outcome, source IDs and resolved steps. Examples include tournament wins/losses, surviving an ambush, a rescue, a first shared journey, a costly defeat and completed promises. Condense routine trade/recruitment/travel only when related facts and ordering remain recoverable.

Protect unresolved dependencies and personal milestones from raw-history expiry. Retain a minimal immutable evidence capsule with provenance when its source detail would otherwise be compacted; an aggregate count alone cannot prove who attended or which promise finished. Budget growth must not erase the user's established bond with a character.

### 6. Track explicit chat and ordinary contact separately

Maintain distinct evidence for **when the player last used chat with this character** and **whether they have remained in everyday contact**. A long gap in the first does not establish a gap in the second. This also distinguishes traveling companions from characters who have actually been apart. Do not infer neglect or abandonment solely from a chat gap while ordinary contact continues; any actual relationship concern still retains its own evidence.

| Record | Meaning and required evidence |
| --- | --- |
| Last recorded explicit exchange | The previous nonempty, persisted chat exchange actually delivered to the relevant participants, with source session/turn, speakers, audience, channel and double-precision campaign day. Keep direct chat, group exchanges and delivered correspondence distinguishable. Hearing a group conversation is not proof of a private conversation or personal reply. |
| Shared-presence interval | Campaign/timeline/generation, player and companion IDs, party/travel episode, observed start/end and membership/availability changes. Preserve evidence, known gaps and uncertainty. Current membership alone cannot establish weeks of prior contact. |
| Routine contact | A compact, stable summary of ordinary interaction modeled within an eligible shared-presence interval. Store `provenance=modeled_routine`, supporting interval IDs, participants, date range, chosen detail identifiers and generator version. It is authorized background simulation, not a witnessed event or a recovered chat transcript. |

**Explicit chat time.** Resolve the previous exchange before assembling the current reply, so opening chat or submitting the new message does not immediately replace the answer with “just now.” Advance the record only from accepted, nonempty dialogue that actually reached its intended audience. Failed requests, quiet/skipped attempts, retries, menu openings, summaries, background routine and undelivered letters do not count. Use native campaign time for elapsed days, not server receipt time or real days with the game closed; reuse the campaign calendar for display. If old history is missing, say the last recorded exchange is unknown rather than declaring a first meeting or a period of silence. “Last explicit chat” is a record category, not proof that every such exchange was intimate or important.

**Eligibility for ordinary contact.** Use observed co-travel and participant availability, including current temporary guests. Same clan, same settlement or different parties in the same army is insufficient on its own. Split intervals for departures, detachments, captivity, unconsciousness, death and other established barriers; resume when eligible presence is actually restored. Ordinary wounds alone need not prevent conversation. Unknown intervals remain unknown. Explicitly established avoidance or silence overrides the default to the extent actually established; ordinary tension does not automatically mean nobody speaks. Generate no shared routine before a companion joins or during a recorded absence.

**Small details that may fill the gap.** Select brief motifs compatible with the known situation and existing character/relationship tone. Greetings, passing practical remarks and light group chatter are safe defaults. More specific details need their setting to be supported:

| Situation | Permitted background texture | Boundary |
| --- | --- | --- |
| Ordinary shared travel | Passing greetings, brief check-ins, remarks about the journey, occasional light banter | No invented player quotations, personal preferences, confessions or arguments. Civil practical talk can fit a strained relationship; warmth must fit the existing one. |
| Known terrain or weather | A passing remark about a documented road condition, landscape or weather | Do not invent rain, snow, landmarks, destinations or route decisions. Use generic journey chatter when detail is unavailable. |
| An established halt or camp | A little casual talk during the pause or around an already established fire | Does not establish camping, lodging, a meal, sleep or resource consumption merely because time passed. |
| Known shared duties, mounts or equipment | Mundane practical exchanges about the existing routine | No repair completion, skill gain, item transfer, payment, binding instruction or new assignment. |

These are summaries such as “You exchanged greetings and a little practical road chatter during the journey,” not a fabricated transcript of what the player said. They can support natural familiarity and occasional references to small shared moments. They cannot resolve an open concern, complete a promised task, convey private news, grant consent, create romance/intimacy, change relationship scores, award XP or perform a native action. They cannot establish that an outstanding personal or strategic topic was discussed. Consequential social events continue to require the normal accepted scene or native evidence.

**Generation and retention.** Use a lightweight deterministic routine adapter alongside activity projection, with no provider call per tick or day. A proposed default is at most one compact routine entry per eligible party/day interval with one or two suitable details and observer references, then consolidation across a continuous journey. This is a storage cadence, not a claim that a conversation occurred at midnight or a requirement to invent a fresh anecdote every day. Include interval/participant identity and generator version in stable keys, retain selected details, and reuse them across chat surfaces; reopening chat, retries and save/reload must not change what supposedly happened. Rotate eligible motifs without forcing cheerful banter or repeating the same line daily. A generic continuity summary is enough when specific detail is unsupported. Lazy generation may fill documented presence intervals; it cannot reconstruct unobserved weeks from the current roster.

Retain a compact record of elapsed shared contact and only a small selection of recent texture. Fold older routine into a journey summary without multiplying embeddings or copying prose per companion. Consolidation, retrieval and any history presentation must preserve the modeled-routine label. A later summary must not promote it into an actual player statement, agreement, firsthand major event or explicit-chat timestamp.

**Next-reply context.** Add a short contact-continuity block beside the required activity block in both active legacy and precision modes. Include the previous explicit exchange's time/channel, supported shared presence since then, any actual separation, and a bounded routine summary. Preserve the distinction under compaction; optional colorful details are discarded before required outcomes, unresolved concerns or established relationship history. Routine contact must never mark tournament/battle evidence as already delivered or suppress an overdue completion update.

Hypothetical context, not a finding about the current save:

> Your last recorded direct chat with the player was twelve campaign days ago. You have remained together in the party during the documented journey since then. Ordinary greetings and practical road chatter occurred as modeled background contact. No additional substantial discussion or agreement is established. Do not describe those twelve days as complete silence or absence. The unresolved question from the recorded chat remains unresolved.

An appropriate natural reference might be: “We've traded words on the road, but it's been a while since we sat down like this.” Characters need not mention a timer, the chat interface or routine contact in every reply. Claims about the last private talk, a specific topic or actual separation must follow their respective evidence. If asked what was said between chats, give only the supported broad routine, never invented quotations or consequential revelations.

## Coverage matrix

“Existing” below means an inspected source hook/receipt foundation exists, not that dialogue delivery or native acceptance is already verified. Additional hooks must be verified against the installed Bannerlord version before implementation.

| Activity family | Completion and facts worth remembering | Source / work needed |
| --- | --- | --- |
| Travel to towns, castles, villages and hideouts | Departed, reached, left, changed route, returned; which trip and travelers | Existing settlement events. Add occurrence and accompanying roster snapshots; distinguish entering from merely passing nearby. |
| Everyday contact between explicit chats | Last recorded exchange, continued shared travel, actual separation, small routine interactions | Add explicit-chat recency and observed presence intervals, plus separately labeled modeled routine from section 6. Routine is neither task completion nor proof of a substantive conversation. |
| Sea travel and landings | Embarked, voyage/landing completed, ship lost/repaired | Existing ship lifecycle events cover ownership/damage work, not a complete voyage. Add verified embark/disembark/arrival adapters. |
| Tournaments, individual and team | Entered, participated, eliminated, finished, won/lost; town, winning team, prize receipt | Existing tournament callbacks and Arena Overhaul team outcome. Add stable occurrence linking and separate competitor/spectator roles. |
| Arena practice, sparring and duels | Session ended, result, actual participants; training progress if observed | Native mission/arena outcome adapter or existing successful Reign action receipt; opening the arena menu is insufficient. |
| Looters, bandits, hostile armies and ambushes | Enemy category/party, victory/defeat/retreat, casualties/captures, allies | Existing battle start/end. Enrich targets/results; preserve mission participants and event-start snapshots. |
| Hideout assaults and boss encounters | Assault succeeded/failed, target hideout, participant squad, boss duel if recorded | Existing Hideout battle outcome plus mission-specific roster/result evidence. Main-party membership alone does not prove squad attendance. |
| Aid to villagers, caravans and allied parties | Who was helped, battle result, survivors/rescue actually established | Correlate battle sides and rescue/prisoner evidence; avoid assuming every friendly battle was a requested escort. |
| Sieges and defense | Started, assault outcome, lifted/abandoned, settlement captured/lost, relevant losses | Existing siege, battle and ownership events. Correlate them into one occurrence; siege ending is not necessarily capture. |
| Raids and pillaging | Target, completion/interruption, spoils if received, village aftermath | Existing raid/village/battle/loot callbacks; no success from approach alone. |
| Captivity, ransom, escape and release | Captured, freed, ransomed, rescued; actor and actual beneficiary | Existing prisoner events and native action receipts; preserve release reason and avoid invented rescuer attribution. |
| Quests, deliveries, escorts and investigations | Accepted steps, progress, success, failure, expiry or abandonment | Existing quest/issue callbacks. Decode outcome detail; add specific objective adapters where generic quest text omits the proof. |
| Trading, supplies, horses, gear and gifts | Completed purchase/sale/transfer, item, quantity, recipient and payment | Existing item/barter/loot receipts; batch shopping into one trip episode. A proposal, inventory possession or talk of a gift is not proof of delivery. |
| Troop recruitment, upgrades and garrison work | Who joined, trained/upgraded or transferred; target/amount | Recruitment/garrison/desertion hooks exist. Add verified upgrades/training completion; avoid personal attendance for absent companions. |
| Companion/guest/clan membership | Invitation accepted, actual join, departure, return, dismissal, service end | Existing native guest/clan/action receipts. Separate accepted terms from execution and from travel or tournament completion. |
| Army joining, mustering and campaigns | Joined, gathered, fought, dispersed, returned to own party | Existing army callbacks. Add member-hero scope; army dissolution alone does not establish campaign success. |
| Rest, waiting, recovery, camps and lodging | Camp/night/rest completed, recovery state, resumed travel | Add a bounded state-transition adapter. Time passing alone must not invent sleep, a meal or a camp; native healing and narrated care are different facts. |
| Meals, drinks, feasts, dances and other social activities | Started/ended, actual participants, accepted scene outcome, consumption if supported | Reuse social-event and accepted scene/action evidence. Narrated completion stays labeled as narrated; it does not create inventory/gold/native effects. |
| Visits, audiences, family and personal business | Meeting actually occurred, known discussion settled, ceremony attended | Accepted interaction/quest/native family receipts. Town arrival does not prove meeting a resident, attendance at a wedding or access to private news. |
| Political, clan and property business | Oath/service accepted, treaty/peace enacted, fief/workshop acquired, appointment effective | Existing native world/action receipts, filtered to actual actors/witnesses or later reports. Completion does not imply every companion witnessed negotiations. |
| Construction, crafting and repairs | Actual completion/ownership/production receipt, promised item or project | Existing building/workshop/ship changes cover some cases. Verify smithing/repair completion hooks; opening a menu or commissioning work is not finishing it. |

Use a small registry declaring each adapter's source types, phase/result mapping, required fields, observer proof, coalescing key, completion predicates and regression cases. Unmapped outcomes are visible coverage gaps rather than silently fabricated generic successes. Keep this registry distinct from `reign.testing.json`, which owns testing capabilities and gates.

## Persistence and failure rules

- Projection updates and matched-plan revisions are idempotent per source occurrence and generation. Retries produce no duplicate personal memory or repeated completion transition.
- Snapshot participants before parties disband, prisoners move or heroes disappear. Preserve uncertainty if a late callback cannot reconstruct them.
- Save Sync captures projection state and revisions consistently with native history. Rollback restores/rebuilds to the exact save boundary, discards later outcomes and invalidates stale prompt/embedding caches.
- Reject delayed provider replies and native uploads from an obsolete restore generation. Preserve double-precision campaign time plus sequence ordering; rounded display dates cannot establish event order.
- Existing campaigns can be backfilled only from retained authoritative evidence and recorded historical participants. Current rosters, LLM recollection or user claims do not establish old attendance. Missing history remains explicitly unknown.
- Explicit-chat timestamps, presence intervals and routine entries follow the same Save Sync boundary and restore generation. Rewind removes later contact and details as well as later outcomes. Backfilled routine requires documented eligible presence, remains labeled as modeled, and cannot alter historical dialogue or retroactively apply relationship/native effects.
- Backfill/repair is previewed and idempotent; it never replays gifts, payments, movement, quests or native commands. Do not rewrite the player's original transcript.
- Event lifecycle is separate from data completeness: the current recorder emits `isComplete=true` even for `battle_started`. Likewise `quest_completed` may describe failure or expiry. Neither field name alone is a task-success predicate.
- Where authoritative completion is present but a draft still asks to prepare that same finished occurrence, use the existing bounded continuity repair with its evidence. Reject unsupported state writes; require a narrowly matched contradiction rather than banning all future references to the same town/activity.

## Implementation order and ownership

1. Add the common server activity projection/selection/reconciliation path for already reliable tournament and battle outcomes, with synthetic isolated regression fixtures and exact final-prompt assertions in both memory modes.
2. Extend native event snapshots and transport for travel, event occurrences, participation and opponent/result semantics. Wire all dialogue surfaces to the activity watermark and fresh native state.
3. Derive explicit-chat recency from canonical delivered exchanges; add presence-interval capture and the bounded routine adapter. Feed the separate contact-continuity block through the same final prompt paths, preserving provenance and existing relationship/action boundaries.
4. Add quest/receipt, commerce, recruitment/guest and social adapters, then the remaining matrix with explicit native-hook coverage.
5. Add retention capsules, rollback/retry contracts and a read-only diagnostics view/export of event → observer → plan → final-prompt evidence, including explicit-chat sources, presence gaps and modeled-routine provenance. Update catalog/procedure/help/security contracts for any changed testing tool.
6. Run manifest-selected validation, then appropriately authorized native/provider acceptance and coordinated deployment. Do not declare full coverage from compilation or synthetic prompts.

Server-owned source must be edited in sibling `D:/Projects/ReignServer`; private client/harness/docs remain in `D:/Projects/Reign`. Obtain a fresh exact-path validation plan for implementation. Persistence/shared-contract changes can broaden the selected tier; do not preselect a smaller build by assumption.

The user subsequently authorized implementation, build and validation, with an explicit deployment hold. The implementation below supersedes the earlier investigation-only scope.

### Implemented adapters and boundaries (2026-09-25)

`SharedActivityMemory.cs` projects supported native events and verified terminal action receipts into immutable source-hashed activity capsules with per-hero roles and accepted-reply delivery markers. Native recording preserves historical accompanying rosters and pre-battle rosters; mission evidence refines actual combat participation. Restricted squad members without that evidence retain party association. Existing native casualty entities keep killed, wounded and routed categories distinct. New callbacks cover party/companion membership, crafting, item handover, sea travel and arena practice; hourly bounded observations cover ordinary co-travel and stationary waiting. Existing travel, tournament/team result, battle/hideout/raid/siege, captivity/release, quests/issues, trade/loot/ransom, recruitment/garrison/desertion, army/service, ownership/building, ceremony and political event adapters feed the same path when player involvement and observer evidence exist. Accepted scene outcomes continue through the existing episode/scene-handoff path.

The client preserves shared events through routine outbox coalescing and trimming, prioritizes up to 256 pending player events before the next individual/party/social/letter request with a four-second bound, and carries event IDs plus restore generation. Campaign/timeline and request epoch fencing prevent cross-save upload reuse. The server verifies the acknowledged IDs; incomplete synchronization supplies protected uncertainty context. No per-event provider or vector dependency is introduced.

Activity selection combines recent records, family-diverse outcomes, accepted mechanical targets and topical history before packet limits. Exact mechanical steps close through revision-checked atomic updates; root emotional concerns remain open. Commerce predicates require the event, player role, item and quantity, and a single result cannot satisfy a multi-event count. A narrowly matched attempt to restart a completed named arrival enters the existing bounded continuity repair; new return trips and historical quotations remain valid. Unstructured legacy plans are preserved with a factual historical interpretation in the final prompt. Outcome capsules outlive lossy raw-history compaction; bounded selection and merged contact intervals control prompt size. Capsules are deliberately retained rather than deleting the evidence needed for future recall.

Last explicit spoken contact is read from a nonempty accepted NPC source turn on the exact player/timeline pair; letters remain separate from spoken recency. Only bounded native co-presence intervals support modeled routine. Stable generic motifs do not invent meals, weather, quotations, promises, relationships or native effects. Gaps and missing old observations stay unknown. Known historical event entities can be projected lazily and idempotently, without modifying the source conversation or replaying native actions. Unknown old travel is never backfilled.

Native callbacks and model wording still need the acceptance matrix below after deployment. Exact old prose plans without typed targets are contextualized, not automatically rewritten. Broad or compound objectives remain unresolved unless a supported exact mechanical predicate proves them. Final validation receipts and the deployment handoff live under `.codex-build/investigations/shared-activity-memory-20260925/`.

## Behavioral acceptance

Require deterministic assertions for:

- Pravend-style plan → arrival → team win → unrelated gift conversation: completed trip/result survives, old approach plan is historical, new return trips remain possible.
- Hunt → battle with looters: victory, retreat, defeat, mixed killed/wounded/captured/escaped outcomes and partial multi-battle objectives.
- Companion present, absent, wounded, prisoner, temporarily excluded, late joiner, spectator and actual mission participant.
- Repeated tournament in one town, repeated visits, two same-day battles, duplicate callbacks/retries and late event delivery.
- Quest success versus failure/expiry; arrival versus meeting; siege end versus capture; accepted gift versus delivery.
- Events arriving while dialogue is open; server restart; vector outage; consolidation backlog; normal close/reopen and Save Sync rewind.
- Native fact versus contradictory rumor, older summary or stale current-plan field, with no unauthorized emotional/relationship resolution.
- Budget pressure: required completion ID and observer role reach the actual serialized request while relationship foundations remain present.
- Individual chat, Party Chat, social/wilderness event and letter parity; later group speakers acknowledge prior accepted reactions without a mandatory recap.
- Retention/compaction keeps enough evidence to prove important outcomes and plan closure. Background traffic cannot starve the immediate player activity path.
- Twelve or more campaign days of documented co-travel without explicit chat: the last recorded exchange keeps its correct date, ordinary contact bridges the gap, and the next reply does not infer total silence, absence, neglect or abandonment from that gap alone.
- Genuine separation, later joining, a detached scout, a prisoner, unconsciousness, explicit avoidance and uncertain old history: routine appears only within eligible intervals. A delivered letter does not establish physical presence; listening to group chat does not become a private exchange.
- New chat opening, current-turn assembly, failed/empty/quiet replies, undelivered correspondence and duplicate retries: the previous explicit-chat date remains correct, with no false recency from background entries or processing time.
- Routine motifs remain stable across repeated chat openings, consolidation, restart and save/reload; Save Sync rewind removes future explicit and routine contact. No fabricated retroactive presence is allowed during old-campaign backfill.
- Minor interaction cannot settle promises or concerns, reveal private information, change consent/romance/relationship scores, award XP or trigger actions. Missing camp/weather/resource evidence keeps texture generic; actual established silence takes precedence.
- Both actual final prompt modes preserve explicit-chat recency versus ongoing contact under budget pressure; ambient entries never suppress undelivered major outcomes or become actual player quotations through compaction.

Use existing World History, continuity, prompt composition, action-receipt, precision-memory and Save Sync harnesses through canonical validation. Extend the owning procedure `docs/agent/testing/DIALOGUE_AND_PROVIDERS.md` and `reign.testing.json` when implementing new cases/profiles, preserving any other task's edits. Provider/native testing requires the project's usual explicit cost and disposable-save authorization.

Native acceptance must demonstrate at least a travel/tournament chain, looter battle, hideout with a restricted squad, failed/interrupted activity, repeat visit and save/reload. Inspect both exact prompts and delivered prose. The character need not recite every fact; it must not restart completed preparation or invent participation.

Contact-continuity acceptance also needs a long documented shared journey with no explicit chat, a genuine separation/reunion, and an unresolved prior concern. Confirm natural acknowledgement of ordinary interaction, correct prior-chat timing and no invented major discussion or relationship change. Any unattended time advancement uses the existing authorized disposable-save campaign-test procedure.

## Coordination and current validation scope

At investigation time only this task was active in the returned same-project inventory; both repositories have substantial pre-existing changes on `codex/publish-recent-reign-work`. The checked-in client/server HEADs were `8c31c4d2b8cdafc32d60ea325ac44e99b74b4046` and `3c6c08b5872231b73322e9f4c1a96897943c6a35`.

**Add culture-specific dialogue voice** (`01a0d943-a23a-7bb0-820f-7b0ae63ca11d`) is idle with a validated Linux build and an explicit instruction to await the coordinating task's message after the game stops. Its pending prompt files and the recruitment/action/correspondence deltas are preserved. Read-only task inspection reported that **Investigate random correspondence** had already coordinated an earlier combined deployment; that historical completion does not authorize another deployment here. Latest recruitment/tavern task detail pages were empty, so their latest readiness cannot be established from those pages.

No stopped task was resumed, no provider request was made, no runtime was changed and no deployment was attempted. Recheck task ownership and source/artifact compatibility immediately before any later shared edit, validation or deployment.

For this design-only change, the exact-path canonical plan selects **changed / Release / Tier 1, no build** for `REIGN_ROADMAP.md` and this document. Validation and hygiene receipts are retained in the private investigation evidence directory. Documentation validation does not prove the proposed behavior.
