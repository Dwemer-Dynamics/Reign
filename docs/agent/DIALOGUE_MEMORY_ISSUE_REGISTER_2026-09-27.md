# Dialogue and memory accuracy issue register — 2026-09-27

This is the accountable backlog for the upcoming fix plan requested in task `01a0dbd1-5dc6-79e3-82b4-3d64bcc922e7`. It consolidates the known findings, deployed regression guards, unverified claims and unfinished acceptance requirements. **84 individually identified items remain accountable to the next plan.** They are not 84 newly confirmed bugs. The latest additions are ROUTE-06, FACT-10 and the user's approved quarter-time courier requirement ROUTE-07; the previous 81 items are preserved.

Evidence cutoff: the final family reply completed at **2026-09-27 23:35:30 UTC**, and the session closed by the player at 23:36:08 UTC. E12 evaluates the final three replies and closure summary. E13 adds the user's Constalia event report through its third reply at approximately 23:58:21 UTC. E14 adds all eight Ira/Maxax group responses through **2026-09-28 00:29:43 UTC**, with closure recorded as closed_by_player at 00:30:36 UTC. E15 adds two Gavene responses through **00:38:14 UTC**, including her false denial of a verified outgoing letter. E17 adds eight Ira private-tent replies through **01:52:05 UTC**; that session remains ongoing. Scheduled monitoring remains paused. Earlier baseline reports remain unchanged.

The [next-build proposal](DIALOGUE_MEMORY_NEXT_BUILD_PROPOSAL_2026-09-27.md) maps every one of these 84 IDs to implementation/retained regression work or an explicitly justified option deferral, with route coverage and measured acceptance. It is a proposal, not implementation or certification.

## Scope and completion rules

- Apply improvements to **every place that creates, retrieves, interprets, summarizes or uses conversation and memory**, with route-specific proof.
- Improve future conversations only. Preserve historical records, campaign progress and relationship losses; no retrospective rewriting or refunds.
- Preserve personality, suspicion, disagreement, refusal, uncertainty and legitimate ordinary slights. Missing an answer in retrieval must not manufacture player misconduct.
- Prefer local evidence handling. High Memory Mode is optional, off by default, with a separately selected checker; only the main dialogue model rewrites visible prose.
- An unchecked item means its disposition and acceptance are still required. **Implemented guard** does not mean its old failure is still occurring; **verification gap** does not mean a bug is proven.
- The next plan must map **every ID** to an implementation/test work package or an explicit justified deferral. Group related work without deleting individual cases. Keep speculative causes distinct from proven mechanisms.
- Check off an item only after implementation where needed and appropriate verification. Build success, a passed parsing repair, a loaded source ID, model confidence, or a smaller prompt alone cannot close semantic defects.
- This update changes documentation only. It does not authorize production changes, settings changes, provider calls, campaign operations, deployment or a resumed watch.

Recommended planning order: protect question/answer sequence and actual evidence delivery; prevent harmful repairs and poisoned lasting updates; align relationship reasoning with grounded conduct; then complete route coverage, checker quality and measured/native acceptance. This is a recommendation for the upcoming plan, not a new deployment decision.

## Evidence index

Evidence IDs below refer to retained local investigations. The analytical register belongs in source control; raw campaign records, prompts and diagnostics remain under ignored `.codex-build`. Local evidence links require the retained workspace.

| Evidence | Scope and authoritative files |
|---|---|
| E1 | [Rhagaea audit, including turn-14 follow-up](../../.codex-build/investigations/rhagaea-conversation-20260925-01a0dbd1/REPORT.md). Original/repaired response, native follow-up and parser proofs linked there. |
| E2 | [81-reply relationship/repair/drink audit and fix plan](../../.codex-build/investigations/dialogue-accuracy-plan-20260926-01a0dbd1/REPORT_AND_FIX_PLAN.md). 80 complete replies and one partial; individual failure classes retained. |
| E3 | [Full 53-Ira-exchange audit](../../.codex-build/investigations/ira-live-watch-20260926-01a0dbd1/full-conversation-memory-audit/REPORT.md), [all exchanges](../../.codex-build/investigations/ira-live-watch-20260926-01a0dbd1/full-conversation-memory-audit/ALL_53_EXCHANGES.md), [380-entry memory ledger](../../.codex-build/investigations/ira-live-watch-20260926-01a0dbd1/full-conversation-memory-audit/MEMORY_LEDGER.md), and [179 proposed/new-write assessments](../../.codex-build/investigations/ira-live-watch-20260926-01a0dbd1/full-conversation-memory-audit/new-write-assessments.json). This supersedes the earlier 12-only scope. |
| E4 | [Fixed 37-response multi-route audit](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/REPORT.md), [per-response ledger](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/PER_RESPONSE.md), and [harmful F01 repair proof](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/confirmed-harmful-repair-F01.json). Cutoff 21:35:13 UTC. |
| E5 | [Continuation W001–W012](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/CONTINUATION.md), [previously explained plan proof](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/PLAN_RECALL_FINDING.md), and [paused monitor state](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/monitor-state.json). |
| E6 | [Latest W007–W012 source/prompt/write evaluation](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/LATEST_REVIEW.md) and [why the negative assessments became neutral](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/RELATIONSHIP_GATE_EXPLANATION.md). The latter distinguishes missing evidence metadata from semantic adjudication. |
| E7 | [Implementation history](../../.codex-build/investigations/dialogue-memory-implementation-20260927-01a0dbd1/RESULTS.md). Superseded artifact/native-continuation statements defer to E8; earlier deployed safeguards remain regression requirements. |
| E8 | [Latest readiness](../../.codex-build/investigations/dialogue-memory-implementation-20260927-01a0dbd1/continuation-resume/READINESS.md), [acceptance gaps](../../.codex-build/investigations/dialogue-memory-implementation-20260927-01a0dbd1/continuation-resume/ACCEPTANCE-GAPS.md), and [latest deployment](../../.codex-build/investigations/dialogue-memory-implementation-20260927-01a0dbd1/continuation-resume/deployment-20260927/REPORT.md). These distinguish implementation, offline validation, measured quality and native acceptance. |
| E9 | [Earlier dialogue fix validation/deployment handoff](../../.codex-build/investigations/dialogue-accuracy-fix-20260926-01a0dbd1/VALIDATION_AND_DEPLOYMENT_HANDOFF.md). Metadata preservation after deployment is retained as a regression, not claimed broken merely because pre-fix cases exist. |
| E10 | User-approved plan in this task; [existing roadmap item](../../REIGN_ROADMAP.md), [precision-memory design](MEMORY_PRECISION_UPGRADE.md), and [shared-activity plan](SHARED_ACTIVITY_MEMORY_PLAN_2026-09-25.md). Current user scope and the latest evidence override stale status notes. |
| E11 | [Latest nine replies W013–W021](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/LATEST_REVIEW_W013_W021.md): repeated threshold/priority loop, actual acceptance of parallel progress, legitimate remaining questions, source-verified stored writes, repair/native receipts and four false battle-leadership memories. Includes the user's requirement to consider a stronger fast-checker system. |
| E12 | [Final family replies W022–W024 and closure](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/ongoing-watch/LATEST_REVIEW_W022_W024.md): conditional-promise drift, a fifth false leadership memory from “smiled,” an incomplete retrieval-eligible closing summary, mostly accepted departure, and current-source group-state/repair-status findings. |
| E13 | [Constalia event C001–C003](../../.codex-build/investigations/recent-conversations-20260927-2103-01a0dbd1/constalia-event/REPORT.md): her original acknowledgment of fourteen prisoners and settled promises is loaded but omitted; new replies overwrite the prior plan, demand proof again, conflate original-band and total counts, and store inability to give a date before a reply opportunity. Native historical prisoner transfer is not independently certified. |
| E14 | [Ira/Michael/Maxax group evaluation](../../.codex-build/investigations/ira-maxax-group-20260927-01a0dbd1/REPORT.md): four player turns, eight responses, all 24 main-request message hashes verified. Successful private plant recognition and plausible public discretion; Maxax shows no suspicion. Confirmed private-narration prompt exposure and mixed-visibility memory write. Newly generated marriage background lacks earlier disclosure, but legitimate growth/ambivalence must remain possible; an incorrect change is not proven. Two successful metadata repairs, no negative receipts, High Memory Mode off. |
| E15 | [Gavene letter denial G001–G002](../../.codex-build/investigations/gavene-letter-recall-20260927-01a0dbd1/REPORT.md): her authored letter is delivered/read and present in canonical chat history but absent from both complete prompts. G002 denies it, treats an answered visit-motive question as dodged, stores a false forgery/fabrication belief and applies −1. Michael's reply is still in transit. The carved-horse memory is supplied, yet she answers about her native mount. |
| E16 | [Courier timing and quarter-time requirement](../../.codex-build/investigations/gavene-letter-recall-20260927-01a0dbd1/COURIER_SPEED_FINDING.md): the reply's scheduled 5.3893-day transit exceeds the observed 2.0193-day player journey. The user approves multiplying delivery duration by 0.25. Current source has a three-day cross-kingdom base, multiple timing paths/minimums, copied reply transit and a roughly five-hour polling cadence. No new NPC responses are added to the audited population. |
| E17 | [Ira private follow-up I001–I008](../../.codex-build/investigations/ira-private-followup-20260927-01a0dbd1/STATUS.md): positive correction/reciprocal uptake alongside I004/I006 speaker reversals despite correct evidence; I004 unresolved identical prose; failed or removed continuity updates leave revision 1 stale. I007 invents Rhagaea's grief from an ambiguous mother reference, stores it as Michael's statement and applies −1 using that premise. Includes distinct empty length/stop cases, narrow repairs, 28 unique memory-source assessments and unresolved requested-versus-observed native deltas. The newer population is 82 accepted NPC replies; provider attempts are not additional responses. |

The 81-reply and 53-exchange audits overlap and must not be added as separate populations. The latest 37 plus 24 continuation replies total **61 newer responses**, plus three Constalia event replies, eight Ira/Maxax group replies and two Gavene replies for **74**. The closed family session itself contains 13 player turns and 39 NPC replies (15 baseline plus 24 continuation). Source-ledger occurrences, helper requests, retries and generated drafts are not extra completed dialogue turns. Later Constalia replies discovered but not assessed are excluded.

### 2026-09-27 final review additions

- **RET-01/02/03/08, ROUTE-01:** Constalia demonstrates missing settled-history content even with 11,567–15,788 selected-context characters. A nonempty section or loaded correct summary does not prove relevant delivery; the final family replies still have zero optional selected-context characters.
- **CONT-01/02/03/05/06, OBS-02:** Current `GroupConversationState.cs:153–181` replaces open questions with question-mark lines from the latest NPC reply; it does not represent durable answer links or opportunities. Its player contribution key at line 173 omits turn identity, a source-level collision risk for repeated identical text requiring a regression, not a demonstrated loss of canonical history here.
- **STORE-03, FACT-03/08:** W022 strengthens a conditional forewarning promise. Constalia C003 mixes two survivors of the original seventeen with fourteen total prisoners across groups and stores “cannot specify the date” before Michael answers the new date question.
- **STORE-06:** Family closure summary `scene_73715D49751A21F5DBEC700503770BBA` is incomplete, active and retrieval-eligible, with an opening-only extract despite 13 counted exchanges. Source-list completeness is not semantic summary completeness.
- **STORE-07/08:** W023 adds active falsehood memory `memory_whl_5d9e41225ba94d59a485efbc108ff356` and whole-monologue belief `belief_0ad65840b5514ef09a16141bbc7bd707`. There are now five observed false leadership memories from this mechanism. Later causal influence on prose remains unproven.
- **REP-12, REP-10, HMM-06/07:** Group contribution status still reads the legacy `.,` suffix in current source. High Memory Mode precedes a later agency stage in the event pipeline; bind verification to the accepted revision. This is a design coverage gap, not an observed false enabled-checker pass; the reviewed mode was off.
- **REL-02/03/04:** Constalia C002's missing conduct quote neutralizes a negative assessment without fixing its mistaken memory premise. The final family batch contains positives, including Temurtai → Michael +3, and no new negative assessment.
- **OBS-03:** The exact final “at the gates” reference, literal scouting-contract wording, historical native prisoner-transfer result and later use of the newly stored falsehoods remain explicit verification gaps. The final family departure is confirmed closed; Constalia's event was still in progress at its review cutoff.

### 2026-09-27 local-time Ira/Maxax additions

- **ROUTE-05, STORE-08:** S005's explanation of Ira's private recognition reaches Maxax's S006 prompt twice and S008 once. A separate episodic memory mixes the observable plant exchange with private meaning and marks all three participants as knowing it. Current ordinary-memory eligibility accepts known-by/participant membership; `private` visibility alone does not restrict the record to its owner. The memory ID was not selected in the observed Maxax requests, and no subsequent suspicion is demonstrated.
- **ROUTE-01/04, FACT-01, STORE-02:** A newly generated marriage chapter uses current profiles/affinity but omits Ira's previous private disclosure. Treat this as a provenance/reconciliation gap, not a proven incorrect emotional change. The user explicitly wants Ira and Maxax to be able to grow closer through shared interests despite the player's history. Preserve past episodes, allow present feelings and independent NPC relationships to evolve, and distinguish generated background from sourced events.
- **STORE-03, FACT-03/04, RET-05/08:** S007's private comprehension narrows “perhaps here or maybe Onira” into an Onira plan. Current and historical profile age/clan differ. Closing summary covers the scene but adds an unsupported travel direction and has inconsistent coverage/completion indicators; it is not the earlier opening-only failure.
- **CONT-05, REP-01/05/09/12, HMM-02/07, ACC-06:** Preserve correct plant recall, concealment, Maxax's independent plant hobby and zero jealousy assumptions. Test less repetitive group farewells. Both missing-proposal repairs succeed without assessment loss; High Memory Mode is off. A checker must not force disclosure or manufacture suspicion from player-visible private narration.

### 2026-09-27 local-time Gavene additions

- **ROUTE-06, RET-01/08:** Verified NPC-authored outgoing letter `letter_371a3c2c6070470d842c40ba2e03f728` is read and in canonical correspondence history, but not loaded or supplied to either current request. Her own authorship must survive route/session changes; player reply `letter_3716c6e99e0e40afabe11540a1407aca` is in transit and must remain unavailable to her until receipt. UI history visibility and `history_written` flags do not prove model delivery.
- **CONT-03/04, STORE-02/08, REL-02/03/04, HMM-07:** Missing evidence becomes certainty of forgery or player fabrication, a confidence-0.9 stored belief and a native-applied −1. The quoted player statement is real but does not establish misconduct. “I got your letter, and made a point to stop by” answers why he visited. No repair introduced the denial; High Memory Mode was off.
- **FACT-10:** Correctly supplied memory of a carved horse on a sill loses to a native riding-horse interpretation. Bind the valid native-mount guard to that exact entity; do not let it erase a separate token, replica or figurative reference.

### Ira continuation I005–I008

- **FACT-01, CONT-03/04, STORE-02/03, REL-02/03/04:** I005 accepts one correction but adds an unsupported motive; I006 claims Michael's correction as her earlier statement. I007 can legitimately seek clarification, but its invented “Rhagaea's grief” becomes a stored memory and −1 evasion rationale. Distinguish a vague answer from no answer, and the NPC's hypothesis from what Michael actually said.
- **CONT-01/05, REP-03/04/12:** I005 combines two speech spans across stage directions; the exact-quote guard rejects that constructed quote and local repair drops the entire update. I006 again proposes expected revision 2 against current revision 1. The old rejection episode survives both. Correctly retained beliefs do not prove all dependent state was updated.
- **REP-05/06/09, RET-07/08/10:** Two more raw stop-empty recoveries, one missing-array agency call and local gesture removal coexist with semantic errors. Main prompts remain about 29–30k provider input tokens with zero optional selected-memory section, despite substantial protected history. Structured resolved status is narrow, not semantic certification.
- **REL-06:** Requested +3 yields acknowledged native 20→27 in I004 and 23→29 in I006; their directional-affinity changes are +3 each. Record this as an unresolved native-modifier/synchronization/accounting discrepancy, not proven duplicate application.
- **Positive controls:** I005 accepts the correction about wanting to see her; I006 goes first as asked; I007 may reasonably require a clearer proposal and react with alarm. Independent feelings and marital development remain allowed. Eight private replies keep audience scope limited to Michael/Ira; High Memory Mode remains off.

- **CONT-03/04/05, STORE-02/03:** [I008](../../.codex-build/investigations/ira-private-followup-20260927-01a0dbd1/FOLLOWUP_I008.md) correctly understands the Maxax/maternal-help clarification, but the old episode remains active as a new agency concern commits. The obligation to share his crazy ways remains unresolved after he answers. The contaminated grief memory is supplied again; his fresh correction overcomes it this turn. Distinguish answered from accepted, and retire only the resolved concern rather than requiring agreement with his options.

## Retrieval and final prompt delivery

- [ ] **RET-01 — Loaded historical answers never reach the provider**\
  **Status:** Confirmed open. **Evidence:** E5; E6.\
  **Finding/requirement:** Temurtai's acknowledged independence plan survives in memory 9fd35a91897544a8a6ec71baa037568b and loaded summary sum_62BFD5BCAAC74F5B0CFE4433A98E500B, but is absent from the reviewed final requests. The legacy summary-rendering path and the optional packet allocation are separate failure points; do not claim that this exact summary entered the packet and was then removed.\
  **Closure proof:** Trace load, selection, rendering and final content separately. Recalling the overall vision must not depend on the player restating it. Verify the original explanation and acknowledgment in the actual request.

- [ ] **RET-02 — Budgeting removes relevant selected memory while adverse concerns survive**\
  **Status:** Confirmed open. **Evidence:** E5; E6.\
  **Finding/requirement:** W001–W008 and W010–W012 retain zero optional selected-memory characters; W009 retains 746 characters of a less relevant meeting. Protected relationship material and new unresolved concerns survive. These are not memory-free prompts, but the allocation favors the accusation over its answer.\
  **Closure proof:** Protect relevant answers, corrections and boundaries before repetitive historical scaffolding. Test end-to-end allocation under the observed long prompts.

- [ ] **RET-03 — Question retrieval drops its answer and reciprocal question**\
  **Status:** Confirmed open. **Evidence:** E3.\
  **Finding/requirement:** Ira's early question remains in 43 later prompts, while its complete answer appears in only three of them. Turn 44 reopens the answer and confuses the reciprocal question. Corrections and completed discussions suffer the same asymmetric selection.\
  **Closure proof:** Retrieve question, speaker, answer, reciprocal question and resolution as linked evidence. Measure answer coverage independently of question/source-ID presence.

- [ ] **RET-04 — Ranking misses the active topic and earlier relationship milestones**\
  **Status:** Partially implemented; acceptance open. **Evidence:** E3; E8.\
  **Finding/requirement:** The source replay improves to 252/272 required sources but still misses development cases 34, 36, 37, 43, 44, 46, 48 and 51. Relevant NPC statements must influence the query as well as the latest player text.\
  **Closure proof:** Rank using current exchange/topic and relevant NPC contributions; preserve relationship foundations without crowding out the immediate answer. Retain exact missed-source fixtures.

- [ ] **RET-05 — Irrelevant announcements and persona material displace useful evidence**\
  **Status:** Confirmed observed; regression required. **Evidence:** E3.\
  **Finding/requirement:** The Ira audit contains 26 low-relevance public announcements and 21 authored persona items, including unrelated clan, diplomacy and battle material. Historical accuracy alone does not make a memory relevant.\
  **Closure proof:** Measure relevance and marginal prompt value, remove duplicate instructions, and retain needed character context. Do not convert hearing public news into participation.

- [ ] **RET-06 — Negated or metaphorical intimacy becomes protected relationship history**\
  **Status:** Implemented guard; regression required. **Evidence:** E3; E7; E8.\
  **Finding/requirement:** The phrase 'not intimate, just less performative' was promoted through intimacy keywords. Hypothetical affection, third-party acts and emotional closeness need different treatment from completed physical intimacy.\
  **Closure proof:** Retain the deployed filters and test negation, actor, hypothetical status and genuine affection together; do not solve false positives by discarding real milestones.

- [ ] **RET-07 — Recall from the beginning of a long relationship**\
  **Status:** Fixture passed; broader acceptance open. **Evidence:** E8.\
  **Finding/requirement:** The user requires early memories to remain retrievable after hundreds of conversations. A first-meeting fixture after 300 later conversations passes; that is not proof of native long-history accuracy for all routes.\
  **Closure proof:** Maintain source-backed long-term retrieval beyond recent sessions; test early questions, first meetings, later corrections and competing similar episodes without future leakage.

- [ ] **RET-08 — Loaded IDs and truncated audit text can falsely suggest evidence coverage**\
  **Status:** Confirmed evidence risk; tooling acceptance open. **Evidence:** E3; E4; E5; E6.\
  **Finding/requirement:** Complete captured requests and pinned prompt pages, not the source manifest alone, established the omissions. Some MCP responses hit the 4 MiB cap.\
  **Closure proof:** Certify supporting content, speaker and sequence in every final/retry request. Preserve hashes/evidence IDs and report incomplete prompt or source coverage explicitly.

- [ ] **RET-09 — Semantic indexing timeouts and fallback retrieval**\
  **Status:** Observed operational gap; cause not isolated. **Evidence:** E8.\
  **Finding/requirement:** Post-deployment indexing timeouts and full-text fallback predate the latest change. A loaded 384-dimensional worker does not prove indexing or semantic retrieval completed successfully.\
  **Closure proof:** Measure index freshness, failures, fallback selection and resulting evidence coverage. Diagnose against retained logs without attributing every recall failure to the worker.

- [ ] **RET-10 — Large total prompts despite compact-memory goals**\
  **Status:** Confirmed prompt-size problem; target unmet. **Evidence:** E4; E8.\
  **Finding/requirement:** The newer audit measures 24,859–41,654 input tokens, median 30,049, across 48 main calls. Protected history, repeated instructions and persona compete with the 2,000–4,000 dynamic-memory target.\
  **Closure proof:** Budget total input separately from dynamic memory; justify expansions and preserve essential evidence. Meet the controlled 40% median total-input reduction gate in ACC-03.

## Question, answer and conversation continuity

- [ ] **CONT-01 — Question ownership is not reliably preserved**\
  **Status:** Confirmed open. **Evidence:** E3; E4.\
  **Finding/requirement:** Ira says Michael asked a question she originally asked; reciprocal questions get folded into the first question. In the family discussion, NPC-owned fears and questions also change owners.\
  **Closure proof:** Give each question and answer explicit speaker, addressee, source span and sequence links. Verify both stored records and generated interpretation.

- [ ] **CONT-02 — NPCs accuse the player before an opportunity to answer**\
  **Status:** Confirmed open. **Evidence:** E4; E5; E6.\
  **Finding/requirement:** R15/R16, F03, W005, W008/W009 and W012 judge a question introduced earlier in the same NPC batch as already evaded. R12 also invents Michael's reaction to a rebuke without an intervening player turn.\
  **Closure proof:** Track the last player opportunity relative to each question and narrated player action. A later NPC in a batch cannot manufacture an intervening answer, silence, reaction or refusal.

- [ ] **CONT-03 — A narrower follow-up replaces the original question retroactively**\
  **Status:** Confirmed open. **Evidence:** E5; E6.\
  **Finding/requirement:** The NPCs accept an answer, ask about lieges, garrisons or readiness thresholds, then claim that was what they asked all along. The new concern may be legitimate; the retrospective accusation is not.\
  **Closure proof:** Link follow-ups as new questions with their own timestamps and answer opportunities. Preserve acknowledgment of the prior answer.

- [ ] **CONT-04 — Answered, unconvincing, intentionally unknown and unanswered are conflated**\
  **Status:** Confirmed open. **Evidence:** E3; E5; E6.\
  **Finding/requirement:** The overall independence plan is treated as absent when an exact seller, town, date or military contingency remains undecided. An NPC can disagree with the plan without forgetting it.\
  **Closure proof:** Represent these states separately, including explicit reopening reasons. Keep distinct unresolved concerns, such as Temurene's Rhagaea question, without erasing resolved ones.

- [ ] **CONT-05 — Repeated arguments and group contributions stall the scene**\
  **Status:** Confirmed open. **Evidence:** E4; E5; E6.\
  **Finding/requirement:** R13–L03 repeat the gate/authority objection; later family turns repeat floor/roof/door/road framing. Several NPCs rephrase the same demand instead of contributing or responding. Niasen and the cupped-hands scene also show verbose recaps and shared metaphors.\
  **Closure proof:** Measure semantic repetition across turns and speakers, answer uptake, topic progress and distinct character contributions. Removing a repeated gesture alone is insufficient.\
  **2026-09-28 refinement:** Enforce a provider-free speaking floor before per-NPC generation. Sociability/assertiveness must heavily drive unsolicited participation (extremely outgoing/confident almost always inclined; opposite near zero), with context secondary and no universal single-interjection cap. Direct invitations bypass probability. Verify quoted/private names, all/subset invitations, actual handover recipients, retry freezing, quiet-listener recall, all-quiet player-source retention, zero skipped-writer calls and no silence penalty/forced event exit across group routes. Stage29 source is implemented for validation; native/voice acceptance remains open.

- [ ] **CONT-06 — Logical conversation identity across storage sessions and reconnects**\
  **Status:** Implemented; native acceptance open. **Evidence:** E3; E7; E8.\
  **Finding/requirement:** The 53 Ira exchanges span four storage sessions, initially miscounted as only 12. Explicit scoped, single-use, save-backed native continuation is now implemented; earlier reports saying no caller supplies it are superseded by E8.\
  **Closure proof:** Verify scene transitions, reconnects, end reasons, save/load and idempotent starts with the implemented handoff. Never merge conversations solely because participants match.

- [ ] **CONT-07 — Completed activities and plans remain unresolved or reopen incorrectly**\
  **Status:** Confirmed open. **Evidence:** E3; E4; E10.\
  **Finding/requirement:** Ira's answered topic and later correction leave old outstanding plans; future plant identification becomes completed history. Older travel/scouting plans must be reconciled with supported results, without treating mere arrival as completion of every objective.\
  **Closure proof:** Link proposal, progress, completion, abandonment and reopening to specific source-backed steps. Reuse the existing shared-activity plan and preserve partial/failed outcomes.

- [ ] **CONT-08 — Parallel work is forced into a single-choice sequence**\
  **Status:** Confirmed open. **Evidence:** E11.\
  **Finding/requirement:** W013–W018 repeatedly demand one first piece even after current activity across the plan is described; W019 finally admits that parallel progress was misread as refusal to choose. A demand for a single next task must not overwrite a valid parallel strategy.\
  **Closure proof:** Track dependencies, parallel activities, readiness conditions and genuinely unspecified priorities separately. Preserve W019's acceptance; allow new risk or family-contribution questions without reopening the whole vision or misdating the answer.

## Factual interpretation and source fidelity

- [ ] **FACT-01 — Correct supplied evidence is still misinterpreted**\
  **Status:** Confirmed open. **Evidence:** E3; E4; E6.\
  **Finding/requirement:** Ira's turn 8 question reversal, Predmira E15 and Ulagara W011 show errors even when the relevant source is in the request. Better retrieval alone cannot resolve these cases.\
  **Closure proof:** Separate missing evidence from incorrect interpretation and from repair-introduced errors. Require source-grounded actor/action/sequence checks at acceptance.

- [ ] **FACT-02 — Corrected invented quotations recur**\
  **Status:** Confirmed open. **Evidence:** E3.\
  **Finding/requirement:** Ira invents a Rhagaea 'Proud' quotation, acknowledges correction, then repeats the claim later; a bad scene prefix carries it forward.\
  **Closure proof:** Bind quotations to source spans and acknowledged corrections. Do not promote inferred sentiment or another NPC's private speech into a witnessed exact quotation.

- [ ] **FACT-03 — Intentions and hypotheticals become completed facts**\
  **Status:** Confirmed open. **Evidence:** E3; E6.\
  **Finding/requirement:** Examples include future plant identification, a generic arena analogy becoming personal history, and W011's stored army detail dropping its future preparation condition.\
  **Closure proof:** Keep intention, hypothetical, narrated action and confirmed native result distinct. A planned army is not an existing army; an analogy is not an observed act.

- [ ] **FACT-04 — Episode chronology and location are conflated**\
  **Status:** Confirmed open. **Evidence:** E3; E6.\
  **Finding/requirement:** W011 moves the next-morning exit/banner discussion onto the prior wall scene despite correct prompt chronology. Ira reverses salon arrival/departure and invents 'this morning' timing.\
  **Closure proof:** Bind claims to the correct episode and relative time; test adjacent similar scenes, arrivals/departures and references to the previous night.

- [ ] **FACT-05 — Actors, recipients and direction of actions drift**\
  **Status:** Confirmed open. **Evidence:** E4; E6.\
  **Finding/requirement:** W010 expands an offer to either daughter into an offer to all three people. R12 contains confused self-attribution. The harmed fear attribution and letter travel-direction reversal are additional cases.\
  **Closure proof:** Preserve speaker, actor, target and recipient set independently. Test first/second/third-person transformations and group summaries against source spans.
  **2026-10-01 progress:** Today's Rhagaea R003 credited Michael with her preceding instruction to stop talking about the future, despite the correct original appearing twice in the actual writer prompt. Shared source-clause validation, dependent-write quarantine, bounded CPU NLI comparisons and structured GLiNER candidate extraction are implemented for validation. Generic model scores remain advisory; no semantic qualification, historical repair, campaign changes or deployment is claimed. Evidence: `.codex-build/investigations/rhagaea-today-20261001-01a0dbd1/REPORT.md` and `.codex-build/dialogue-claim-validation-20261001-01a0dbd1`.

- [ ] **FACT-06 — Exclusive choices and accepted decisions are reversed**\
  **Status:** Confirmed open. **Evidence:** E4.\
  **Finding/requirement:** Predmira E13 accepts incompatible options; E14 chooses travel now and caravan later; E15 describes that as a choice costing Michael a traveling companion even though E14 is present twice.\
  **Closure proof:** Represent incompatible options and the selected/deferred outcome; later prose and memory must honor the controlling decision.

- [ ] **FACT-07 — Identity and gender are wrong in stored or generated material**\
  **Status:** Confirmed observed; route coverage open. **Evidence:** E4; E6.\
  **Finding/requirement:** Temurtai is described with male pronouns in an older letter/meeting summary; the played Cupped Hands opening misgenders Hulara despite female source context. The unrecovered original meeting is not otherwise certified.\
  **Closure proof:** Validate entity identity/pronouns in generation, summaries, correspondence and retrieval. Keep old source records intact; prefer verified source facts for future output.

- [ ] **FACT-08 — Partial examples and rhetorical impressions become exact totals**\
  **Status:** Confirmed count defects plus verification gaps. **Evidence:** E3; E4; E6.\
  **Finding/requirement:** Selected tournament examples become a complete tally; W009 stores 'four times' despite distinct player topics. Fifteen crowns, three-day intervals and W012's 'twice' remain unverified rather than proven false.\
  **Closure proof:** Require complete scoped evidence for exact counts, distinguish reported from native totals, and preserve uncertainty when source coverage is partial.

- [ ] **FACT-09 — Narrated or inferred action is treated as native execution**\
  **Status:** Confirmed evidence conflation; some outcomes unverified. **Evidence:** E2; E4.\
  **Finding/requirement:** Isolde narration implies payment/readiness; native arrival is embellished into checkpoint challenge, guard quotation and passage reason. Offscreen narrative developments are not automatically game-state facts.\
  **Closure proof:** Require native receipts for native effects and label fictional observations/inferences appropriately. Do not infer payment, travel orders, promises or completed objectives from decorative prose.

- [ ] **FACT-10 — Native entity facts override a different narrative referent**\
  **Status:** Confirmed reference error; contributing guard requires scoped verification. **Evidence:** E15.\
  **Finding/requirement:** Gavene answers about her Canterion Charger/stables when the letter and shared history concern the carved wooden horse on her sill. The correct carved-horse memory is in the actual prompt alongside an authoritative native-mount warning. The warning's sole causal responsibility is not proven.\
  **Closure proof:** Bind type/ownership/authority to the specific entity. Test native mount versus carved token, replicas/namesakes/metaphors and genuine ambiguity; preserve both valid records and prefer source-supported referents or clarification.

## Memory writes, beliefs and consolidation

- [ ] **STORE-01 — Acknowledged corrections do not supersede old assertions**\
  **Status:** Confirmed open. **Evidence:** E3; E4; E6.\
  **Finding/requirement:** Ulagara retracts the surprise-visit accusation in R11, but the old adverse assertion remains active without a supersession link. W011 accepts the vision; future reconciliation with the earlier false belief is not established.\
  **Closure proof:** Keep historical originals while linking corrections, resolution and supersession for future retrieval. Verify the next actual request, not only the presence of a new corrective row.

- [ ] **STORE-02 — Incorrect interpretations become self-reinforcing beliefs**\
  **Status:** Confirmed open. **Evidence:** E3; E4; E5; E6.\
  **Finding/requirement:** Ira's repeated-deflection beliefs and the family scene's deliberate-avoidance/lack-of-vision comprehensions persist. Subjective suspicion, such as consort speculation, is sometimes promoted into certainty.\
  **Closure proof:** Validate lasting updates against original answers and chronology. Preserve subjective feelings as subjective; do not store a false historical premise as established evidence for future hostility.

- [ ] **STORE-03 — Summaries lose qualifiers and add exclusivity or intent**\
  **Status:** Confirmed open. **Evidence:** E3; E6.\
  **Finding/requirement:** Ira examples change voluntary into unsolicited, invent exclusive overlook knowledge, or drop 'possibly'. Later family summaries broaden recipients and shorten future conditions.\
  **Closure proof:** Retain qualifications, negation, scope and source span. Test contradiction, unsupported exclusivity and changed intent, including the FACT-03/05 cases.

- [ ] **STORE-04 — Temporary reactions and metaphors become permanent traits**\
  **Status:** Confirmed open. **Evidence:** E3.\
  **Finding/requirement:** A local dislike of being followed becomes a lasting aversion reused in 12 prompts; comparative remarks become habitual location claims. Metaphors also enter completed-scene evidence.\
  **Closure proof:** Separate stable traits from current feelings, context-dependent preferences, metaphor and hypothesis. Require appropriate evidence before creating durable character facts.

- [ ] **STORE-05 — Truncated scene prefixes claim unsupported completion**\
  **Status:** Implemented guard; regression and legacy handling open. **Evidence:** E3; E7.\
  **Finding/requirement:** Fourteen 450-character prefixes in the Ira audit end mid-word or preserve gestures/hypotheticals as completed-scene facts.\
  **Closure proof:** Use complete supported spans for future summaries. Preserve historical records and retrieve original dialogue/corrections preferentially when a legacy prefix is incomplete or misleading.

- [ ] **STORE-06 — Failed consolidation leaves incomplete but retrieval-eligible summaries**\
  **Status:** Confirmed open. **Evidence:** E4.\
  **Finding/requirement:** Cupped Hands memory_4C23B336C8E5FA8FAEC64ABD01286E1C fails four attempts. Fallback scene_D45F3031ADBDACE59E37D4987EBB276C remains active/retrieval-eligible, lists all eight turns but summarizes only the opening and omits the answer.\
  **Closure proof:** Keep originals, explicit incomplete coverage and bounded retries. Incomplete summaries must not masquerade as whole-scene resolution or displace the missing answer.

- [ ] **STORE-07 — Ordinary narration creates false battle-leadership memories**\
  **Status:** Newly confirmed open. **Evidence:** E11.\
  **Finding/requirement:** Full replies W013/W014/W017/W020 are deterministically classified as leadership claims; ordinary words Settled/called/startled/acknowledges contain 'led'. Earlier Hideout participation is then treated as contradictory evidence and four active detected_falsehood memories are stored between the sisters, despite no claim to have led that battle.\
  **Closure proof:** Extract a specific asserted actor/action/event before history classification; exclude substrings, stage directions, metaphor, other actors and hypothetical plans. Bind evidence to the claimed event before storing a contradiction. Check the whole write path, including High Memory Mode off; do not assume these newly stored rows caused the observed prose without delivery evidence.

- [ ] **STORE-08 — Whole witnessed monologues enter accepted-unverified belief storage**\
  **Status:** Observed representation risk; downstream misuse unverified. **Evidence:** E11.\
  **Finding/requirement:** The same historical-claim path stores four complete NPC replies as Temurene's beliefs when she lacks qualifying world-event knowledge. Source policy labels these accepted_unverified, but the raw text mixes speech, narration, questions, interpretation and multiple actors.\
  **Closure proof:** Preserve original speaker and provenance, extract bounded claims, retain uncertainty and prevent unsupported speech from becoming a verified personal fact or permanent belief through retrieval/consolidation. Confirm downstream behavior before claiming a specific later misinterpretation.

## Repair behavior and provider failures

- [ ] **REP-01 — Unrelated repair replaces valid metadata and memory updates**\
  **Status:** Deployed guard; regression required. **Evidence:** E1; E2; E3; E7.\
  **Finding/requirement:** Earlier missing-proposal repairs discard positive assessments, emotion and signals; some also drop episodic memories. The initial 81-reply audit finds eight erroneous penalties; the overlapping 53-Ira audit has its own eight pre-fix reversals and ten lost memory writes. Do not add these counts as disjoint populations.\
  **Closure proof:** Preserve unrelated owned fields, distinguish omitted/empty/invalid/explicitly-cleared values, and verify both losses and suppressed meaningful gains. Retain the deployed fix under every repair route.

- [ ] **REP-02 — Integrity repair introduces a false attribution and marks it cleared**\
  **Status:** Confirmed open. **Evidence:** E4.\
  **Finding/requirement:** F01 changes Temurtai's 'what scares me' into Michael's fear. A generic shared-word agency check matches unrelated material; changing 'You asked' to 'You said' passes without restoring truth. Trace 86cc618ab68645f28222c92ce8827846.\
  **Closure proof:** Bind disputed claims to exact source actor/action spans. Compare before/after semantics and reject a repair that creates a new contradiction, even if the trigger phrase disappears.

- [ ] **REP-03 — False agency findings lead to unchanged unresolved repairs**\
  **Status:** Confirmed open. **Evidence:** E5.\
  **Finding/requirement:** W002's 'Not because you asked nicely' is flagged as inverted agency through weak historical matching. Three stages do not mean three independent semantic failures; prose remains unchanged while repair metadata changes.\
  **Closure proof:** Require a specific conflicting source claim, suppress generic phrase matches and stop bounded retries when no supported correction exists.

- [ ] **REP-04 — Repair success measures syntax or markers rather than factual correctness**\
  **Status:** Confirmed open. **Evidence:** E3; E4; E5; E6.\
  **Finding/requirement:** The 53-Ira audit records 55 structurally passed stages despite substantive errors; F01 is marked clear after harmful rewriting. Later successful empty-object recoveries also leave incorrect conversation reasoning.\
  **Closure proof:** Validate the actual claim and dependent outputs after repair. Report semantic coverage separately from successful parsing or local repetition cleanup.

- [ ] **REP-05 — Casual dialogue triggers negotiation and unsupported evidence-key repairs**\
  **Status:** Observed failure class; regression required. **Evidence:** E2; E7.\
  **Finding/requirement:** Missing proposalDecisions invites unnecessary agency calls and invented proposals; canonical evidence keys/values and quotation typography fail repeatedly. Corein 6f461f03d4104846965c4ce657b7e219 and Isolde 1cd7c06dd6064d0ca352100046b91fff are retained cases.\
  **Closure proof:** Complete genuinely empty proposal arrays locally; constrain real negotiation repair to valid keys, values and supported quotes. Do not invent a negotiation to satisfy schema.
  **2026-09-29 UTC, paused-task addition:** The user reports two Constalia workshop replies replaced by the verification notice. The separate failure task attributes correlations `34b0f75217be491f9e7de1bf7ece698e` and `e9a09016e4074b2cbd9d2b6900130a8b` to omitted `proposalDecisions`, an existing conditional record and nonmatching repair `replyQuote` spans. This task has retained the screenshot and reported diagnosis, without independently re-auditing or changing code. An existing negotiation record must not by itself force an unrelated ordinary reply to re-decide it. A failed negotiation/action validation must preserve otherwise safe prose and surface a separate status; unsupported agreement/execution claims require targeted correction or qualification, not unconditional acceptance. Add omitted/empty/invalid arrays with and without an active record, bad-quote repair, bounded retry, unchanged valid dialogue and next-turn continuation cases. Full note: [negotiation failure requirement](../../.codex-build/evidence-accuracy-system-01a0dbd1/NEGOTIATION_FAILURE_NOTE.md). Existing REP-10/REP-12 own effect isolation and structured status; no new issue ID or completion claim.

- [ ] **REP-06 — Empty provider objects cause frequent format retries**\
  **Status:** Confirmed recurring; one budget-exhaustion case established, other causes remain open. **Evidence:** E2; E4; E6; E17.\
  **Finding/requirement:** Original audit: 19 empty objects (17 stop, two length). New 37-response audit: 11 (ten stop, one length). W007–W012 adds four stop recoveries. E17 I003 returns `{}` with `length`, 8,000 completion tokens and 7,990 reasoning tokens; I004 returns `{}` with `stop`, 6,010 completion tokens and 4,324 reasoning tokens. Both raw provider envelopes contain the empty object, and both format recoveries succeed. I005/I007 add raw stop-empty cases with 5,288/3,128 completion tokens and 4,186/1,131 reasoning tokens, each recovered once. Overlapping windows must not be summed without deduplication.\
  **Closure proof:** Inspect raw envelope, finish reason, output limit, provider-specific reasoning controls and schema compatibility. Distinguish the observed output-budget exhaustion from unexplained stop-empty responses and extraction errors. Measure reasoning versus usable-answer tokens and quality/latency under a fixed manifest; keep retry budgets bounded rather than adding blind attempts or indiscriminately raising limits.

- [ ] **REP-07 — Scene-location mismatch causes motive repairs**\
  **Status:** Observed failure class; regression required. **Evidence:** E2.\
  **Finding/requirement:** Four retained motive repairs fail around CampOpenRoad versus native Hideout. Source/regex analysis alone is not native acceptance of a repair.\
  **Closure proof:** Resolve authoritative scene context and test the exact mismatch without forcing unrelated motivation changes.

- [ ] **REP-08 — Financial metaphors and valid readiness language trigger action/debt repairs**\
  **Status:** Observed false positive; regression required. **Evidence:** E2.\
  **Finding/requirement:** Isolde 12e5bb92913e416db7300db804f56b28 is flagged despite an actual payment/readiness context. The separate unsupported-execution case must remain distinguishable.\
  **Closure proof:** Use actor, accepted action and native evidence rather than isolated debt/payment words. Preserve metaphor without allowing fabricated completed transactions.

- [ ] **REP-09 — Style repair misses repeated meaning or leaks its own instructions**\
  **Status:** Confirmed observed. **Evidence:** E2; E4; E6.\
  **Finding/requirement:** Political group_rhetorical_convergence repairs fail; F02 exposes 'not copying Temurtai's gesture' in prose. W011's local stage-direction removal succeeds but leaves the repeated argument.\
  **Closure proof:** Assess substantive group contribution as well as gesture variation. Never make repair instructions visible narration; retain character voice and useful disagreement.

- [ ] **REP-10 — Prose changes require dependent-effect validation**\
  **Status:** Implemented safeguards; broader acceptance open. **Evidence:** E2; E7; E8.\
  **Finding/requirement:** A changed factual claim can invalidate its memory, relationship rationale, plan or proposed action. Blindly preserving every old field is as unsafe as deleting unrelated fields.\
  **Closure proof:** Give repairs explicit field ownership and dependency invalidation. Revalidate affected writes/effects while preserving unrelated valid data, consent/refusal and authorized intentional deception.
  **2026-09-29 UTC requirement:** Negotiation/action validation failure must not invalidate the entire conversation automatically. Suppress the failed agreement/action and its dependent memory or relationship claims; retain independently valid prose and updates only after their own validation. Never store a technical notice as something the NPC said, invent a successful action, or weaken quote/consent evidence to keep the chat moving. Verify partial outcomes and retry deduplication through the next prompt; implementation remains paused.

- [ ] **REP-11 — Cached invalid responses and duplicate system rows obscure retry behavior**\
  **Status:** Confirmed open. **Evidence:** E4.\
  **Finding/requirement:** Cupped Hands retries receive cached empty results in 0 ms without new provider calls; two resolution rows describe the same event. These are not duplicate NPC replies or proven duplicated drink events.\
  **Closure proof:** Separate cache replay from fresh generation, deduplicate logical resolution writes, and bound consolidation retries. Count provider calls and costs from actual requests.

- [ ] **REP-12 — Punctuation is mistaken for authoritative repair status**\
  **Status:** Implemented diagnostics; acceptance open. **Evidence:** E2; E3; E5; E6; E7.\
  **Finding/requirement:** The user repeatedly observes '.,'. W002 has one remaining unresolved finding after three stages; W007–W012 recoveries are resolved. A marker alone cannot establish what failed.\
  **Closure proof:** Expose format recovery, metadata correction, visible prose correction and unresolved failure with per-stage findings. Keep legacy marker compatibility, but make structured status authoritative in the web audit.

## Relationship scoring and believable reactions

- [ ] **REL-01 — Fallback chooses arbitrary sign or misreads negation/another actor**\
  **Status:** Deployed guard; regression required. **Evidence:** E1; E2; E3; E7.\
  **Finding/requirement:** The earlier relationship fallback selects sign from a text hash, treats 'not suspicious' as negative, or attributes a third party's threat to Michael after metadata loss.\
  **Closure proof:** Retain grounded fallback behavior and prevent repair-induced reversals across all routes. No historical refunds or campaign edits are part of this task.

- [ ] **REL-02 — Missing evidence fields suppress potentially legitimate negative reactions**\
  **Status:** Confirmed open. **Evidence:** E6.\
  **Finding/requirement:** W007/W008/W009/W012 have negative routine assessments at confidence 0.60–0.70 but empty currentConductQuote; normalization neutralizes them. This is a metadata rejection, not a finding that their interpretation is false.\
  **Closure proof:** Distinguish omitted evidence from a disproved premise; preserve supported personality-driven losses when evidence is present. Do not weaken the gate merely to make scores decline.

- [ ] **REL-03 — An exact quote alone cannot justify the interpretation**\
  **Status:** Confirmed semantic gap. **Evidence:** E2; E3; E6.\
  **Finding/requirement:** The existing negative-evidence gate checks confidence, rationale and a quote in the current contribution. It does not establish which question came first, whether an answer existed, or whether remembered accusations were true.\
  **Closure proof:** Evaluate the actual surrounding contribution, speaker, boundaries, prior answer and correction. Friendly intent does not guarantee approval, but a fabricated premise cannot justify punishment.

- [ ] **REL-04 — Angry prose, stored beliefs and relationship scores disagree**\
  **Status:** Confirmed open. **Evidence:** E4; E5; E6.\
  **Finding/requirement:** The recent family replies remain accusatory and store adverse interpretations while penalties are zero. Blocking a score does not repair the reasoning or stored memory.\
  **Closure proof:** Audit response, interpretation, evidence and receipt together. Correct false premises and report withheld effects honestly; do not invent a penalty solely to match angry prose.

- [ ] **REL-05 — Preserve real slights without excessive losses or flattened gains**\
  **Status:** Implemented limits; calibration open. **Evidence:** E1; E2; E3; E4; E8.\
  **Finding/requirement:** Competitive pride, suspicion, disagreement, jealousy and refusal can be legitimate. Ira's playful 'Sorry princess' receives -3 then is acknowledged as misread; other repairs suppress meaningful positive support behind the routine +10 cap.\
  **Closure proof:** Ordinary perceived slights remain routine, capped at -1; stronger conduct needs independent support. Test legitimate negatives and meaningful gains, not just zero erroneous losses. Do not treat every critical NPC as broken.

- [ ] **REL-06 — Proposed, normalized, capped and native changes must be distinct**\
  **Status:** Observed accounting requirement; acceptance open. **Evidence:** E1; E6; E17.\
  **Finding/requirement:** Earlier pending receipts needed a later native request for confirmation. W010–W012 include Temurene→Michael +3 and Temurtai→Ulagara +3; these are different relationship directions. Zero can mean neutralized, capped or unapplied. E17 I004/I006 request +3 while native acknowledgment reports +7/+6; the directional affinities still advance +3. Cause remains unverified and must not be mislabeled a second LLM penalty or duplicate application.\
  **Closure proof:** Trace observer/target, original assessment, gate reason, magnitude/cap and native acknowledgment. Verify positive calibration as well as penalties; do not count NPC-to-NPC gains as player gains.

## Drink events, quantities and persistence

- [ ] **DRINK-01 — Valid drinking grammar is missed**\
  **Status:** Implemented parser changes; native acceptance open. **Evidence:** E1; E3; E7; E8.\
  **Finding/requirement:** Rhagaea's 'taking a measured sip' is rejected while 'takes' counts; eight observed sips become seven at the initial cutoff. Ira's three 'pull' constructions at turns 17, 23 and 27 all fail.\
  **Closure proof:** Retain exact parser regressions and verify accepted narration after all repairs. Later no-drink conversations are negative controls, not proof these positive cases work natively.

- [ ] **DRINK-02 — Vessel, beverage, handoff and remaining volume are incomplete**\
  **Status:** Confirmed evidence gap; implementation acceptance open. **Evidence:** E1; E3.\
  **Finding/requirement:** Rhagaea's wine identity persists but vessel kind is blank and remaining volume null; Ira's whiskey-skin handoffs complicate ownership and beverage state.\
  **Closure proof:** Bind drinker, vessel, beverage and accepted action through handoffs/refills. Unknown volume must remain unknown; do not claim a reliable count of cups or refills.

- [ ] **DRINK-03 — Consumption-event count is confused with alcohol quantity**\
  **Status:** Implemented distinction; acceptance open. **Evidence:** E1; E3; E7.\
  **Finding/requirement:** A pull proves a consumption event but not a whole standard drink. The old quarter-sip units are engine estimates, not physical measurements.\
  **Closure proof:** Keep exact event counts separate from known/estimated quantity and uncertainty. Do not inflate an unspecified pull into one standard drink.

- [ ] **DRINK-04 — False drinking and duplicate effects must stay excluded**\
  **Status:** Implemented controls; regression required. **Evidence:** E1; E3; E4; E6; E8.\
  **Finding/requirement:** Emotional swallowing, holding a vessel, contemplated/refused actions, quotations and another actor's drinking must not count. Retries, narration recovery and semantic rewrites must not double count an accepted event.\
  **Closure proof:** Test positive/negative controls together and bind idempotency to accepted actor/action events. Rejected metadata recovered from final narration is one event, not a second miss or extra drink.

- [ ] **DRINK-05 — Drink state must survive save/load without changing thresholds**\
  **Status:** Native acceptance open. **Evidence:** E1; E8.\
  **Finding/requirement:** Existing Endurance tolerance, intoxication bands, blocking and recovery must remain stable. Offline drink checks do not establish save/load, vessel persistence or long-conversation native behavior.\
  **Closure proof:** Verify persistence and exact event counts in a task-owned disposable save, including retries and handoffs. Preserve thresholds and distinguish campaign-hour recovery from wall-clock delay.

## Coverage across every conversation and memory route

- [ ] **ROUTE-01 — Shared improvements must apply everywhere**\
  **Status:** User requirement; incomplete acceptance. **Evidence:** E4; E7; E8; E10.\
  **Finding/requirement:** Cover individual dialogue, party/group, social/wilderness scenes, tavern-house negotiation/scenes, official/court/relationship dialogue, correspondence, generated event dialogue and background creation/consolidation/retrieval. Shared hooks exist in some routes; that is not proof of complete coverage.\
  **Closure proof:** Maintain a route-by-safeguard matrix with explicit tests and gaps. Do not mark the feature complete from the individual-conversation path alone.

- [ ] **ROUTE-02 — Correspondence reverses who promised to travel**\
  **Status:** Confirmed open. **Evidence:** E4.\
  **Finding/requirement:** The delivered Itaria letter reverses Michael's return-after-Odokh promise. The earlier Phycaon context is valid at that source time; a later Onira context does not retroactively erase it.\
  **Closure proof:** Apply the same actor, direction and temporal evidence checks to delivered letters and replies. Preserve the original source 49d75f1c85c04b6487165eb1964a049a_player.

- [ ] **ROUTE-03 — Generated openings and event scenes bypass equivalent checks**\
  **Status:** Confirmed coverage gap. **Evidence:** E4.\
  **Finding/requirement:** The played Cupped Hands scene misgenders Hulara; a Morning Hand candidate was not proven played. Generated scenes can establish faulty context before normal dialogue safeguards run.\
  **Closure proof:** Verify generated context, actor facts and accepted scene updates using the shared safeguards. Test actual played outputs separately from unused drafts.

- [ ] **ROUTE-04 — Background summarization and future memory use need the same guarantees**\
  **Status:** Confirmed coverage gap. **Evidence:** E4; E7; E8.\
  **Finding/requirement:** Checker hooks on dialogue, social, party and letters do not prove equivalent handling in generators and background consolidation. Invalid summaries can contaminate every later route.\
  **Closure proof:** Apply source fidelity, qualification, completion, resolution and incomplete-status contracts to writes and future retrieval, including High Memory Mode off.

- [ ] **ROUTE-05 — Witness scope, public information and delivery are conflated**\
  **Status:** Confirmed prompt/write privacy defects plus eligibility requirements and source gaps. **Evidence:** E2; E3; E4; E6; E14.\
  **Finding/requirement:** Public announcements are not personal participation; another NPC's private transcript is not knowledge. W009's shared meeting explicitly lists all four witnesses, so cross-owner retrieval is not itself a defect. Corein/Gereigan drafts are not proven delivered. E14 forwards private inner narration to Maxax and grants him known-by membership for a memory containing Ira's private recognition, although he shows no suspicion.\
  **Closure proof:** Preserve historical witness/recipient eligibility and distinguish draft, delivered/read, rumor, observation, player-facing private narration and private memory. Split mixed-visibility writes before publication; attendance must not widen private knowledge. Test successful concealment and a contrasting actual disclosure, including next retrieval and gossip. Recover missing original sources before calling a recall accurate or unauthorized.

- [ ] **ROUTE-06 — Characters forget their own correspondence or learn undelivered letters**\
  **Status:** Confirmed authorship-recall failure; receipt/privacy controls required. **Evidence:** E15.\
  **Finding/requirement:** Gavene denies her real outgoing letter despite its authored row and delivered chat-history entry. The relevant mail session is absent from the new in-person prompt. The player's reply remains in transit, so treating all UI letters as NPC knowledge would introduce another error.\
  **Closure proof:** Reuse the canonical letter/thread ledger for observer-relative authored/sent/delivered/read knowledge, protected content retrieval across all routes and long histories, reply/invitation links, branch-aware save/load and idempotency. Verify final prompt and next writes; sender authorship is known at dispatch, recipient content only on eligible receipt, and sender knowledge of receipt requires acknowledgment/evidence. Missing retrieval cannot justify a false accusation or penalty. Preserve genuine intentional deception where supported.

- [ ] **ROUTE-07 — Couriers take longer than the player; quarter letter transit time**\
  **Status:** Observed slow delivery; exact 0.25 multiplier approved by the user. **Evidence:** E15; E16.\
  **Finding/requirement:** The player reached Gavene about 2.0193 days after sending a reply scheduled for 5.3893 days. New ordinary letter transit must be one-quarter of its current duration (75% shorter), giving this case 1.3473 days. Apply across correspondence producers, including NPC replies and autonomous/court mail; do not let old floors or copied reply ETAs undo or compound the change.\
  **Closure proof:** One versioned timing policy scales base, distance contribution, caps, unknown-location fallback and ordinary minimum once; already-immediate urgent mail remains immediate. Test old/new-policy replies, duplicates, save/load, branch recovery, moving recipients, source-specific notices and native delivery lag. Scale the ordinary 0.25-day minimum to 0.0625 day; check delivery at least hourly with a proposed healthy-runtime lag target of at most one game hour. Preserve event prerequisites, shipment travel, generation cooldowns and historical/in-flight ETAs. Prove the Gavene reply arrives before the fixture's player visit and becomes available to the recipient only on eligible delivery. Details and provenance gaps remain in E16.

## High Memory Mode and checker configuration

- [ ] **HMM-01 — Separate checker selection must be explicit and persistent**\
  **Status:** Implemented UI/configuration; usability and live acceptance open. **Evidence:** E7; E8.\
  **Finding/requirement:** High Memory Mode defaults off in Control Center Models. Memory consolidation and Memory Checker Model are different roles: GLM-5.2 in the former does not select Flash in the latter. A blank checker prevents enabling the mode.\
  **Closure proof:** Keep independent provider-profile selections; require a checker before enabling and never silently substitute the dialogue model. Respect the user's fast subscription-covered checker preference; validate persistence and clear role labels.

- [ ] **HMM-02 — Optional checking must not introduce hidden calls**\
  **Status:** Implemented bounds; all-route proof open. **Evidence:** E7; E8.\
  **Finding/requirement:** Memory-relevant turns include recall, quotations, corrections, unresolved questions and lasting updates. Use dialogue_verification, one checker and at most one main-model semantic rewrite. Normally two calls, or three with a rewrite.\
  **Closure proof:** Off means zero new verification/semantic rewrite calls, including precision ambiguity helpers. Existing format and action safety remain operational. Test all routes and the configurable 10-second default timeout.

- [ ] **HMM-03 — Fast checker false positives and uncertainty mistakes remain**\
  **Status:** Confirmed model-quality gap. **Evidence:** E8.\
  **Finding/requirement:** GLM-4.7-Flash is a candidate, not certified: the nine-case comparison includes false attribution findings rejected locally and underreported uncertainty/qualification problems. MiMo comparison times out in five of nine cases.\
  **Closure proof:** Benchmark supported source-bound findings, false positives, missed errors and uncertainty. Reject category relabeling that bypasses actor/source checks; no-source/no-error must not count as verified accuracy.

- [ ] **HMM-04 — The checker reports errors; the main model owns visible revision**\
  **Status:** Implemented guards; end-to-end acceptance open. **Evidence:** E8.\
  **Finding/requirement:** Five isolated writer probes correct five supported findings, including long context. Those captured checker judgments do not prove an end-to-end working checker. Unchanged false claims, added questions or unrelated voice changes can still invalidate a rewrite.\
  **Closure proof:** Require targeted main-model revision with local source and effect validation. Checker must not rewrite, choose relationship numbers, invent facts or execute actions; preserve feelings, consent, refusal and authorized deception.

- [ ] **HMM-05 — Timeout, malformed findings and failed rewrites must fail honestly**\
  **Status:** Implemented failure policy; broader acceptance open. **Evidence:** E7; E8.\
  **Finding/requirement:** Rejected or partial findings, provider failure and a failed revision must not appear as successful verification or produce unsupported side effects or relationship penalties.\
  **Closure proof:** Test timeout, false-positive, unsupported-source, malformed-result and rewrite-failure paths. Record uncertainty/unresolved issues and preserve valid unrelated effects without accepting invalid updates.

- [ ] **HMM-06 — Checking cost and latency need attributable outcomes**\
  **Status:** Instrumentation requirement; measurement open. **Evidence:** E7; E8.\
  **Finding/requirement:** Record trigger, selected model, request purpose, latency, outcome, changed fields, findings and unresolved issues. Successful repair stages alone do not quantify user benefit.\
  **Closure proof:** Measure retrieval, generation, checker, rewrite/retry and total cost separately; show unnecessary corrections and fallback behavior. Keep production settings unchanged during planning.

- [ ] **HMM-07 — Strengthen the fast checker around grounded conversation state**\
  **Status:** User-authorized design option; not implemented or certified. **Evidence:** E11; E8.\
  **Finding/requirement:** The user explicitly permits improving the fast-LLM system to achieve believable remembering people. Isolated quote/error checking misses changing questions, no answer opportunity, forced single-choice framing, acknowledged answers, repeated arguments and false lasting interpretations. All nine E11 replies had High Memory Mode off, so they are not an evaluation of checker-on effectiveness.\
  **Closure proof:** Plan and compare a compact source-grounded conversation-state contract: asked/answered/new/unconvincing/unknown/resolved concerns, actors and sequence, accepted decisions and parallel plans, plus proposed memory/belief updates. Check the draft and associated effects against that evidence with bounded supported findings; have the main model revise only real errors. Preserve character disagreement, explicit uncertainty, independent model selection and existing optional-mode/call bounds unless a changed design is expressly proposed and agreed. Fix missing evidence and separate unsafe write paths rather than expecting a checker to compensate for them.

## Acceptance, test data and delivery

- [ ] **ACC-01 — Full baseline and independent holdouts must test the actual problem**\
  **Status:** Partially complete; semantic holdout work open. **Evidence:** E2; E3; E4; E5; E6; E8.\
  **Finding/requirement:** Retain all 53 Ira exchanges, not just the last 12, plus at least 100 independently labeled holdouts from other conversations. The existing 100 source-span cases from 14 conversations are not full generated-dialogue acceptance.\
  **Closure proof:** Reconstruct only evidence available at each turn, preserving speaker/sequence and no later-answer leakage. Include every confirmed case in E1–E6 and all memory-ledger exceptions; keep independent labels and regression/holdout separation.

- [ ] **ACC-02 — Required-evidence and answer coverage remain below proof needed**\
  **Status:** Gate unmet. **Evidence:** E8.\
  **Finding/requirement:** Latest source replay is 252/272 (92.6471%) versus a 98% gate; legacy is 244/272 (89.7059%). 100/100 answer-source presence is not correct generated answers, and abstention is not accuracy.\
  **Closure proof:** Meet at least 98% required-evidence coverage with separately reported answer coverage, semantic error/abstention rates, attribution, corrections, chronology and hypothetical/completed distinctions.

- [ ] **ACC-03 — Forty-percent median total-input reduction**\
  **Status:** Gate unproven. **Evidence:** E4; E8.\
  **Finding/requirement:** Neither compact packet size nor shorter repair requests proves the agreed 40% reduction in total provider input. Existing source replay lacks a fully comparable historic belief/summary/native/worker state.\
  **Closure proof:** Use a fixed equivalent-work manifest and report whole-request tokens, evidence retained, latency and retries. Do not trade missing answers for a smaller prompt.

- [ ] **ACC-04 — Local reranker selection and memory-product replacement**\
  **Status:** Deferred option; certification required. **Evidence:** E8; E10.\
  **Finding/requirement:** Benchmark the already supported MiniLM cross-encoder after evidence structure improves; compare Qwen3-Reranker-0.6B if useful. Graphiti/replacement service and a new main dialogue model remain options after measurement, not prerequisites.\
  **Closure proof:** Use precision quality gates/certificates before activation and benchmark resource use while Bannerlord runs. Reranking establishes relevance, not truth; no new hardware/subscription assumed.

- [ ] **ACC-05 — Offline success does not establish long-conversation and save/load behavior**\
  **Status:** Native acceptance open. **Evidence:** E8.\
  **Finding/requirement:** Use the authorized immutable BaseThree baseline through a verified task-owned disposable copy and guarded campaign tests. The last retained run had eight of 15 Save Sync states; recheck current capacity before reuse.\
  **Closure proof:** Verify long history, session transitions/reconnects, save/load, drink persistence, memory writes and native relationship receipts. Pause, drain work, inspect, then checkpoint; never advance the baseline or infer disposability from its name.

- [ ] **ACC-06 — Character voice, naturalness and unnecessary correction**\
  **Status:** Human acceptance open. **Evidence:** E3; E4; E5; E6; E8.\
  **Finding/requirement:** Verbose lectures, repeated metaphors, converging NPC voices and visible repair instructions remain despite successful mechanical checks. Legitimate skeptical/proud responses must remain possible.\
  **Closure proof:** Review all relevant routes with a human and retain before/after examples; measure redundant questions and corrections as well as truth. Do not optimize into generic agreeable dialogue.

- [ ] **ACC-07 — Build success and deployment do not close semantic acceptance**\
  **Status:** Prior artifact validation passed; future fix validation open. **Evidence:** E7; E8.\
  **Finding/requirement:** Latest retained product Tier 4 and Linux artifacts passed and were deployed, while precision remains shadow, reranking disabled and High Memory Mode off in the audited configuration. Earlier implementation/native-gap reports must not override later readiness.\
  **Closure proof:** For future coherent changes, obtain manifest scope, run relevant dialogue/memory/relationship/drink/provider/Save Sync/web checks and Linux build; deploy exact validated artifacts in the authorized window. Rollback restores software/retrieval mode without rewinding campaign progress.

- [ ] **ACC-08 — Paid comparisons require a fixed manifest and estimate**\
  **Status:** Cost/authorization boundary. **Evidence:** E8.\
  **Finding/requirement:** Fast subscription availability was authorized for testing, but one nominally subscription test reported a small nonzero USD charge. Subscription eligibility is not proof of zero cost or permission for unbounded evaluation.\
  **Closure proof:** Pin models, cases, call limits, quota/cost estimates and stop conditions before provider runs. Preserve the current task's read-only/planning boundary; do not run new experiments to populate this register.

## Audit reliability and unresolved evidence

- [ ] **OBS-01 — Audit filtering and response size limit investigation coverage**\
  **Status:** Observed tooling defect; follow-up open. **Evidence:** E1; E3; E4; E5.\
  **Finding/requirement:** The original MCP type/search arguments did not filter the server phase as expected; full nested responses can exceed 4 MiB. These are audit-tool limitations, not established dialogue causes.\
  **Closure proof:** Prefer bounded MCP queries and pinned pages, then documented read-only history/diagnostic fallback. Repair tool contracts proportionately when implementing relevant tooling; preserve explicit coverage gaps.

- [ ] **OBS-02 — Incomplete traces, session splits and duplicated attempts inflate coverage**\
  **Status:** Monitoring/data discipline requirement. **Evidence:** E3; E4; E5; E6.\
  **Finding/requirement:** The 53-exchange scene was initially treated as 12; two helper/manifest entries are not extra dialogue turns. The newer review has 37 baseline plus 24 continuation replies, then three separate Constalia event replies. The closed family session itself has 39 NPC replies and 13 player turns; scheduled monitoring remains paused.\
  **Closure proof:** Advance cursors only after completed response assessment, track in-flight work separately, deduplicate attempts and follow explicit continuations. Monitoring remains paused unless the user asks to resume.

- [ ] **OBS-03 — Do not silently convert unverified historical details into passes or defects**\
  **Status:** Explicit verification gaps. **Evidence:** E2; E3; E4; E6.\
  **Finding/requirement:** Open source checks include exact crown/crossing/day totals, original W009 meeting dialogue, Temurene's travel motive and prior access to the full plan, Temurtai's personal hearing/count of exit offers, public-rumor provenance, native payment completion, and whether undelivered drafts were ever used. W011's future-army wording is a contamination risk, not proof a native army was created.\
  **Closure proof:** Maintain each gap with its source reference and reason; recover original evidence or label it unresolved. Do not use absence from a bounded query as proof an event never happened.

## Reconciliation checklist for the upcoming plan

This crosswalk prevents a high-level work package from silently dropping a smaller failure class. Each linked source's individual exchanges, ledger entries and exception notes remain regression input even when several share one issue ID.

| Prior evidence or accepted requirement | Accountable items |
|---|---|
| E1/E2 positive-to-negative repair reversals; suppressed meaningful gains; negation/third-party scoring; native receipts | REP-01, REL-01–REL-06 |
| E1/E2 repair frequency, negotiation schema/evidence failures, venue mismatch, metaphor/action confusion, provider empties and punctuation | REP-03–REP-12, FACT-09, OBS-01 |
| E1/E3 missed sips/pulls, handoffs, unknown volume, actor controls, retry recovery and stable intoxication behavior | DRINK-01–DRINK-05 |
| E3 all 53 exchanges, 380 rendered memory variants, 179 write assessments, omitted answers, corrections, invented quotes, stale plans and scene-prefix defects | RET-03–RET-08, CONT-01/04/06/07, FACT-01–FACT-09, STORE-01–STORE-06, REP-01/04, ACC-01 |
| E4 E01–E15 reunion/choice/voice/count cases | FACT-06/08, CONT-05, REL-05, ACC-06, OBS-03 |
| E4 R01–R07 Cupped Hands generation, NPC answers and incomplete consolidation | FACT-07, STORE-06, REP-06/09/11, ROUTE-03/04 |
| E4 R08–R16, L01–L03, F01–F03 surprise-visit correction, impossible answer chronology, repetitive pressure and harmful integrity repair | CONT-01–CONT-05, FACT-01/05, STORE-01/02, REP-02/09, REL-02–REL-06 |
| E4 letters, generated openings, private/witness eligibility and delivery uncertainty | FACT-07/09, ROUTE-01–ROUTE-05, OBS-03 |
| E5 W001–W006 historical independence plan, missing final content, false agency finding and premature accusations | RET-01/02, CONT-02–CONT-04, REP-03/12, REL-02–REL-04 |
| E6 W007–W009 earlier standing-army answer, moving question, stored four-times claim and zero scores | RET-01/02, CONT-02–CONT-05, FACT-08, STORE-02, REL-02–REL-04 |
| E6 W010–W012 accepted vision, new threshold, wall/morning reversal, recipient expansion and future-army qualifier | CONT-02–CONT-04, FACT-03–FACT-05, STORE-01/03, REP-09, REL-06 |
| E11 W013–W021 four conditions become five, parallel work forced into one first step, earlier message assigned to later question, actual acceptance, valid remaining concerns and native gains | CONT-02–CONT-05/08, FACT-05/08, STORE-01–STORE-03, REP-03/06/09, REL-04–REL-06 |
| E11 four false battle-leadership memories, full witnessed monologues and stronger fast-checker scope | STORE-07/08, ROUTE-01/04/05, HMM-07 |
| E7/E8 already implemented safeguards, explicit native continuation and latest artifact validation | RET-06/07, CONT-06, STORE-05, REP-01/10/12, REL-01/05, DRINK-01/03/04, HMM-01–HMM-06, ACC-07 |
| E14 successful concealment, private narration exposure, mixed-visibility write, evolving marriage interpretation and tentative planting plan | ROUTE-01/04/05, FACT-01/03/04, STORE-02/03/08, CONT-05, REP-01/05/09/12, HMM-02/07, ACC-06 |
| E15 authored/read letter omitted, undelivered reply boundary, false forgery belief/applied −1, ignored visit answer and carved/native horse confusion | ROUTE-06, FACT-10, RET-01/08, CONT-03/04, STORE-02/08, REL-02/03/04, HMM-07 |
| E16 approved quarter-time courier transit, all producers, one-time scaling, delivery cadence and future-only persistence | ROUTE-07, ROUTE-06, ACC-05/07 |
| E17 corrected emotion/+3 but flipped speaker, missing repair quote spans, unchanged failed repair, revision-blocked episode correction, two empty-output classes, private scope and repetitive narration | FACT-01/03, CONT-01/03/04/05, STORE-01/02/03/08, REP-02/03/04/06/09/12, REL-05/06, ROUTE-05, RET-07/08/10, ACC-06 |
| Accepted source types/spans, attribution, resolution/supersession and future-only handling | RET-03/08, CONT-01/04/07, FACT-02–FACT-10, STORE-01–STORE-06, ROUTE-05/06 |
| Accepted all-routes scope, default-off separate fast checker, one check/one main rewrite, no hidden precision helper | ROUTE-01–ROUTE-05, HMM-01–HMM-06 |
| Accepted 53 + 100 evaluation, 98% required evidence, separate answer coverage, 40% total input, native save/load and human voice | ACC-01–ACC-07 |
| Local reranker/model/product options, running-game resource measurement and provider cost controls | RET-09/10, ACC-04/08, HMM-03/06 |
| Read-only observation, exact prompt/source verification, no invented passes, complete session coverage and paused watch | RET-08, OBS-01–OBS-03 |

## Next planning handoff

**2026-09-27 Ira follow-up requirement:** E17's ordinary memory, belief and comprehension accept the user's clarification, but episode `continuity_FCE1F6E70CE8F95A13418F4D9D90A54C` stays at revision 1 with the earlier rejection meaning. The draft and repair both specify expected revision 2 despite receiving revision 1. Extend STORE-01 and CONT-04 with mandatory accepted-turn → all relevant persisted records → next prompt proof. Bind source IDs and expected revisions locally, preserve genuine concurrent-write protection, and surface/reconcile rejected dependent updates without repeating relationship effects. For REP-03/04, replace first-420/360-character prefixes with the decisive attributed source/claim spans and preserve successful correction metadata while refusing to call unchanged known-bad prose repaired. These cases extend the existing 84 IDs; no item is complete.

1. Produce implementation options and recommendations against the IDs above. Name the owning shared pipeline and every affected route; document where a common fix needs route-specific adapters.
2. Separate proved causes from hypotheses and choose fixtures before edits. Preserve earlier successful guards while reproducing the post-deployment failures.
3. Assign explicit acceptance and evidence for every item. A deferral needs its reason, consequence and follow-up; it must remain unchecked.
4. Keep historical audits fixed. Append new evidence and status with dates, and update this register plus the existing roadmap item rather than creating parallel unconnected backlogs.
5. Re-observe source/runtime/test-save state before any later implementation or testing; retained readiness and test-run snapshots are evidence, not current permissions or reservations.

**2026-09-27 checkpoint:** Register created by reconciling the retained audits and latest acceptance reports. No production source, settings, campaign records or runtime changed; no provider evaluation or product build was run. Current planning IDs are intentionally all unchecked.

**2026-09-27 latest-review addition:** E11 adds nine completed replies and four further accountable items. W019's actual acceptance of simultaneous progress must remain a positive regression; W020's risk question and W021's house-level support question are not automatically illegitimate. New count/qualification drift, answer chronology and false-history writes are preserved as separate cases. The user's target is believable remembering characters; a stronger optional fast-checker integration is within planning scope, with measured success required.

### 2026-09-28 — dedicated evidence system implementation in progress

The user selected one accuracy-first evidence-model architecture, superseding the four-system comparison. Current source adds pre-writer planning/reading, concurrent eligible original/letter searches, source-linked question/correction bundles, protected final evidence, original semantic index jobs and separate analyst/checker controls. Build and validation are authorized; deployment must pause until the user shuts down game/server. No historical edits/refunds or production-setting changes are authorized by this phase. Contract and captured-response replay evidence do not establish semantic accuracy. All unresolved issue checkboxes, independent labels, writer/update/next-read, index readiness, native and human acceptance remain open. Retain exact reports/artifacts in `.codex-build/evidence-accuracy-system-01a0dbd1`.


## Recent history window addition — 2026-09-29

User clarification: preserve the **most recent 30 individual messages**, not the relationship's opening messages or 30 exchanges, plus the **30 most recent eligible conversation summaries**. Keep these complete records in the character writer's context as well as making summary context available to the evidence analyst. Older history remains searchable. Select summaries by source chronology, deduplicate multiple views of the same episode, enforce observer/timeline/as-of and route restrictions, and retain original dialogue/later corrections as the authority over derived recollection. Count unavailable or excluded records honestly; IDs alone do not prove content reached the provider. Reading stored summaries introduces no additional inference call.

The ordinary 2,000–4,000 dynamic-token target and 40% median input-reduction target remain measured goals; they must not be achieved by silently dropping this newly requested context or required answers. Report the full resulting input and analyst stages separately. Build/validation are in progress; the architecture remains undeployed and no quality/completion checkbox is advanced by this addition.

### 2026-09-29 deployment and continued development checkpoint

The combined dialogue/evidence groundwork and the other task's nonblocking failure recovery are deployed from product `20260929-080755-b2b1d247` and Linux `20260929-084727-c8339919`; retained runtime `20260929091024-41377` passed artifact hashes, database/vector health and managed stop/start. See the private investigation `evidence-accuracy-system-01a0dbd1/REPORT.md`. This is an operational milestone, not completion of the accuracy gates. High Memory Mode remains on; the separate prewriter evidence model is not enabled for gameplay pending qualification.

Further controlled writer/storage/next-read tests confirm CONT-01/03/04 and STORE-01/02 remain open: a correct evidence packet can still become a false attribution or a definite relationship commitment in prose and newly stored memory. Five of seven original-writer development calls produced prose; two returned `{}` with normal provider completion. Separate reasoning-disabled diagnostics produced prose for those two cases, which does not establish a general provider fix. No campaign history or live model setting was changed.

The next source tranche addresses two bounded, independently reproduced defects: requests for information were rejected for lacking `?`, and ongoing visible actions were dropped while projecting observer knowledge. Shared request handling now covers direct/group/letter source binding and linked answer retrieval, with negative controls and skeptical-answer persistence. Projection retains exact visible spans while excluding private inference. Validation is pending at this checkpoint; these changes are not yet deployed. The v10 analyst prompt prototype and broader accuracy certification remain unapproved for gameplay.

## 2026-09-30 deployed-system evaluation

Latency/display candidate runtime `20260930023100-83548` is deployed. The active goal now evaluates tagging, original-source retrieval, accuracy, update/next-read behavior and speed across all conversation routes. This does not close the existing acceptance gates. Detailed private evidence is under `.codex-build/evidence-accuracy-system-01a0dbd1/memory-evaluation-20260930/`.

- **RET-01/08, STORE-06:** The current Rhagaea middle-term fallback was tagged with provenance-header words (`reported`, `speech`, `independent`) instead of source topics. `BuildMiddleTermSummary` extracted hints from its generated header. The scoped correction prefers original content tags, balances local lexical hints across source records, excludes standard record wrappers and keeps explicit negation. It changes future hint construction only; tags are never fact or visibility authority. Source text and historical tags stay intact. Contract validation is pending.
- **STORE-06, OBS-03:** Recorded consolidation calls show two distinct failures: `{}` with provider finish reason `length` at 1,200 output tokens, and returned whole-record selections totaling about 14,772/20,903 characters against the 1,800-character digest limit. One length response reports 1,199 reasoning tokens. A repeated over-budget result was also served from the local response cache. Original records survive with incomplete compaction status. Do not describe these failures as missing sources or assume a larger output limit fixes oversized selections.
- **RET-01/08, ROUTE-01, ACC-06:** Active original dialogue from the earlier timeline is still indexed in the current restore generation. Exact timeline equality excludes 1,607 such active turns from keyword and semantic discovery and from later `MemoryTimeVisible` checks. The recorded parent branch is real; its fork event is no longer retained. Current-save source membership must be established before broadening access; removing the timeline filter or trusting a branch-name prefix would risk discarded-future leakage. Diagnostic parent-only vector queries are evidence of discoverability, not permission for final prompt delivery. Safe cross-save/source-membership regression and remediation remain open.
- **HMM/ACC:** The fixed, small subscription screening is a development probe, not an independent accuracy certificate. Flash returned correct source assessments in two tested cases but timed out in another. Faster GLM Flash responses had need-link, quotation and shape failures; one typographic quote difference is not itself a semantic falsehood. Granite 8B returned a three-second speaker inversion rejected locally and lost uncertainty on another case. No live profile was changed.
- **OBS-03:** The MCP audit tool emits `type`/`search`, while the current server audit endpoint recognizes `phase` and does not apply those two filters. An unrelated returned row cannot prove absence of a matching event. Exact-correlation evidence and verified retained logs are required until the contract is corrected.

The continuation must retain separate correctness, answer coverage, invalid-citation, timeout, cold-index, queue-wait and successful-latency counts. Native acceptance, independent holdouts and subsequent writer/storage use remain unverified. All broad issue checkboxes stay open.

**2026-09-30 continuation — CONT-01/04, STORE-01/02, ACC-02/03:** Source-faithful writer replay avoided the two targeted Ira attribution errors, but one actual generated “Tell me something you know” question was rejected as `question_source_unverified`. The local form recognizer supported wh-clause requests but omitted nominal information requests. Extend the shared classifier with exact-source, next-answer and negative controls; retain incomplete status until artifact replay confirms storage and next read. Separately, targeted search scores accidentally protected every hit from the analyst input budget. A validated candidate separates relevance from explicit required/recent protection. One historical reader input fell from 36,229 to 11,045 estimated tokens while preserving the original first-meeting narration, but a corroborating later source was omitted. Source coverage, model interpretation and full multi-call cost remain separate acceptance gates. These are development findings; no broad issue is closed and no production history or model selection changes.

**2026-09-30 follow-up — CONT-01/04, ACC-02/03:** The nominal request now passes actual accepted-unit publication and reaches the next writer. Further adaptive replay retrieves the original player's lack of plant knowledge but drops it during reader allocation while retaining the NPC's expertise. The prior token reduction therefore fails required-evidence quality; hold that candidate from deployment pending repair. Targeted statement lookup now retains bounded adjacent original context, and trimming repacks complete backing bundles with an explicit content-coverage receipt. Artifact and semantic replay remain required. The unchanged153-case source-only result (255/272whole originals) does not exercise this failure. A raw writer `{}` with finish_reason=stop and a malformed next analyst review remain separate open reliability issues; no broad issue is closed.

**2026-09-30 follow-up — ACC-02/03, HMM:** The next source-complete replay retains all three required plant-history spans with correct speakers; its expanded 15,161-token estimate is not a general accuracy or efficiency certificate. The 1,593 prompt contracts pass. On the same source packet and common low effort, GLM Flash takes 13.313s with correct inspected teacher/future interpretation but three typography-only quotation differences; Granite 8B takes 15.821s with altered/oversized quotes and wrong aliases; DeepSeek Flash times out at 50.115s. Replaying GLM rejects all three findings despite unchanged quote meaning. Add narrow source-bound smart-quote restoration with original-span receipts and adversarial rejection controls. No model switch, production data rewrite or broad issue closure; final artifact/replay/native acceptance remain separate.

**2026-09-30 follow-up — RET-01/08, ACC-02/03:** Source review found that the new targeted result floor broke equal internal relevance scores by source ID, while semantic original hits share a score of 96. Preserve the search engine's result order for equal scores instead. Four deterministic controls cover tied semantic ordering, stronger-score precedence, exclusion of unsourced summaries and immutable candidates. This is a code-confirmed selection defect; the current private replay injects no semantic worker hits and cannot measure live rank quality. Two lexical-only query results also consume substantial context without containing the plant subject; retain that as a relevance/efficiency limitation for measured reranking, without discarding the complete required plant evidence. No current accuracy certificate or model change is implied.


**2026-09-30 follow-up — RET-01/08, STORE-01/02, ACC-02/03:** A two-turn accepted-memory replay reproduced later-source leakage when retrying the earlier turn. Both original-turn cutoffs stayed at 13 and the later canonical reply was excluded correctly, but two derivatives returned as `Legacy recollection; original evidence unavailable`. The derivative rows lack an accepted-sequence column, and expansion confused ineligible originals with missing history. The candidate follows canonical conversation source timing in the shared eligibility gate and prevents known excluded sources from returning through the precision fallback. Add four-layer/time/branch/lineage/privacy controls and the full continuation regression. The generated plant reply separately passed accepted-unit publication with its conditional memory preserved and motive comprehension marked as private interpretation; later semantic recall remains pending. No live campaign or model profile changed and no broad issue is closed.
