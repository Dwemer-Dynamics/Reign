# Drinking handoff investigation — 2026-09-20

## Outcome and scope

Two live Michael–Hulara turns establish the same failure: the server recognizes a completed pull, resolves its portion as 0.25, then rejects the amount because the accepted vessel has no beverage identity. The model supplied a completed alcoholic sip event in both turns. Its event binding also failed because its evidence quote does not equal the parser's entire extracted clause.

The earlier repairs remain in source. The missing case is a beverage identified in the player's offer followed by the NPC accepting and drinking from that vessel without repeating its contents inside the action. The proposed repair is to carry a bounded, evidenced vessel identity through that accepted handoff and bind metadata to a unique consumption occurrence. Adding whiskey aliases or increasing model quantities cannot address this failure.

This task investigated and specified the repair. No product code was changed, behavioral tests executed, provider calls made, or game/server/campaign operations performed. The user subsequently stopped monitoring; the automation is PAUSED. The [complete repair plan](CONVERSATION_REPAIR_PLAN_2026-09-20.md) incorporates later refill, false-vessel-operation, swallow, scene-continuity and clan-action failures. Implementation and native acceptance remain outstanding.

## Captured evidence

Evidence: `.codex-build/investigations/drinking-handoff-20260920/evidence.json`. Source: read-only `reign_query_audit` for campaign `woERXl3xD23E`, speaking hero `reign_court_town_K1_child_2` (Hulara). Times below are UTC on September 21; local investigation date is September 20 in America/Chicago.

| Receipt time | Player's current offer | Accepted NPC action | Model event | Receipt |
| --- | --- | --- | --- | --- |
| 00:28:37.408 | “my skin of whiskey … pass it to her” | “takes the skin … drinks — a short pull” | `sip`, count 1, completed/alcohol true | proposed 0, consumed 0, drinks 1 |
| 02:49:01.775 | “my whiskey skin and offering it back to her” | “takes the skin and drinks — a longer pull” | `sip`, count 1, completed/alcohol true | proposed 0, consumed 0, drinks 1 |

Correlations: `c73e15ab4df4472db042f7024862c88d` and `894fb7d5f54b43cc9982218fa4827281`.

Both receipts have native hour `2186439.5081026945`, threshold 8, `staleClock:false`, `alcoholBlocked:false`, and no commitment suppression. The parser emits `quantitySource:explicit_portion`, then `reason:beverage_unknown`. The zero occurs before threshold/recovery arithmetic. The final visible action survives cleanup.

Each turn warrants a 0.25 increment under the documented serving rule. “Longer” does not establish an explicit half or full serving. The observed stored count remains 1. The monitor's 1.5 estimate is the arithmetic total if both missing increments are retained; a historical backfill would additionally require checking the exact Save Sync branch and receipt ancestry, because game reloads occurred between these turns. Do not reconstruct the campaign counter from audit text alone.

## Why the previous repairs missed this

The September 15 repair covered wrong/missing paragraph indices, false `down` matches, overbroad negation, partial quantities and cleanup removing real consumption. Its retained handoff is `D:/ReignLaunch/evidence/drinking-inspection-20260915-01a0a6be/BUILD_AND_HANDOFF.md`. Current source additionally contains the approved DRINK-001…090 language/vessel corpus and captured kumis cases.

The current handoff tests (`accepts_cup_from_player`, `takes_cup_from_player`) put “wine” directly inside the NPC action. The test helper supplies only that action to the adjudicator. It cannot expose the production exchange where the player names whiskey and the NPC says “the skin.” The sharing fallback test supplies precisely “They are sharing wine”; ordinary variations in a decision summary are not covered.

Inspection baseline: client `2908e0d71514480364091deff06bdd4eb0463b59`, server `0c551f29b3d3418f2bd65ec98ef1d8a4be8863d1`, both `unstable`, with unrelated existing deltas. The four drinking implementation files inspected were clean in the server checkout. Their bytes match the corresponding source files in the dedicated WSL server checkout; retained runtime version is `20260921004824-79324`. This source comparison and live receipts support the diagnosis; a synced source copy alone is not binary provenance proof.

## Faults in the current path

1. **Offer context is discarded.** `ConversationIntoxication.cs:186` passes parsed response, final reply and held vessel into `ValidatedDrinkAmount`; the current player text already in the request payload is unused. Whiskey is already classified alcoholic in `ConversationBeverages.cs:51`. There is no opportunity to associate that whiskey with the accepted skin.
2. **The fallback depends on narrow summary wording.** `CurrentTurnSharedBeverage` (`ConversationBeverages.cs:109`) recognizes only `is/are sharing/drinking …` in generated decision facts. The first turn's “Hulara accepted Michael's whiskey and shared a drink” does not match. The second turn has no beverage fact. Main dialogue knowledge does not flow into this accounting path.
3. **The event quote is compared with an implementation-defined clause.** `ConversationDrinkVessels.cs:166` requires exact equality with the recognizer's complete evidence text. The first model quote is “lifts it, and drinks — a short pull”; the parser's clause is “drinks — a short pull, enough to taste it properly”. The full action quote and occurrence 0 identify the same action, but metadata is rejected. The second shortened quote fails for the same reason. Fixing this binding alone still leaves beverage identity unresolved.
4. **Generic references are separately fragile.** `ConversationDrinkVessels.cs:203` uses another adjective list to recognize a generic portion. “longer pull this time, enough to feel…” lacks a recognized reference, so the second turn clears its just-observed skin. Portion recognition has already succeeded. Vessel use should follow the verified action sequence rather than depend on a second, different phrase list.

The subsystem document currently says player narration cannot establish contents. That rule needs a precise refinement: a player's offer may supply proposed contents; the speaking NPC's accepted action must establish acquisition and consumption. Player narration alone must remain insufficient.

## Proposed repair

### Carry explicit vessel identity through acceptance

Build a bounded current-turn offer context from the actual request's action spans and already accepted scene vessel records. Retain source turn, exact source quote/span, offering actor, intended recipient, vessel reference, catalogue beverage identity, and nullable remaining volume. Merely mentioning wine elsewhere in speech, memories, a different vessel or another character's action must not qualify.

An offer remains a proposal. It may become the speaker's usable vessel context only when the final accepted NPC action uniquely acquires or drinks from that same offered vessel. An explicit NPC correction, water replacement, competing vessel, wrong recipient, refusal, historical/conditional offer or ambiguous reference prevents inheriting the alcoholic contents. Only the NPC's independently established consumption can increment the counter. The player's own drink never contributes to the NPC's count.

Keep established vessel identity separate from who currently holds it. A handback changes possession; it does not erase the identity of a known skin. Reacquisition must be evidenced before the NPC can consume from it again. Retain the existing campaign/timeline/session/location and elapsed-time boundaries; do not borrow a vessel across an unverified reload or a different scene. Bulk skins and bottles retain unknown capacity unless established explicitly. Scope and invalidation need tests before any persistence extension is accepted.

Use a shared per-turn context object at the existing `ApplyConversationDrinking` seam. Both individual dialogue and the common group/social route already call it with a payload containing `playerText`/`text`/`message`. The quantity, recovery, threshold, row-lock and turn-replay machinery can remain authoritative. Resolve the catalogue identity from evidenced contents; `alcohol:true` is never sufficient by itself. No extra provider call is needed for these captured cases.

### Bind to an occurrence rather than identical clause segmentation

Keep the unique exact final action quote and occurrence index. When an evidence quote is supplied, require a unique literal span in that final action that identifies the selected consumption verb/occurrence; permit it to include a lead-in or omit a descriptive tail. Reject a quote identifying another occurrence, spanning multiple consumptions, referring only to an acquisition, or surviving only in a deleted action. Preserve duplicate-occurrence rejection and conservative recovery when metadata is absent.

Resolve generic pulls/sips against the accepted vessel through the ordered action stream. Quantity classification should not be rerun through a smaller adjective list. A recognized pull remains 0.25 unless an explicit fraction/count changes it. Unknown named drinks, water, uncertain variants and ambiguous objects continue to reject alcoholic increments.

Record identity source, acceptance evidence, possession transition and binding details in receipts so an unknown-beverage rejection explains which link was missing. Existing receipts remain immutable; future adjudication changes do not authorize replaying old campaign turns.

## Acceptance tests for the repair

Use complete request/response fixtures, including the real player offer and original model event, through the production drinking seam. Expected values must be written independently of the recognizer.

| Fixture | Required result |
| --- | --- |
| Both captured whiskey exchanges, independently | +0.25 each; source offer and acceptance evidence retained |
| Captured exchanges as consecutive accepted fixture turns at one native hour, starting from 1 | 1 → 1.25 → 1.5 |
| Exact retry of either turn, including concurrent requests | No second increment; original receipt returned |
| Offer followed by acceptance only, then later evidenced consumption | 0 on receipt of vessel; +0.25 on pull |
| Handback and unique reacquisition of same established skin | Contents retained; no drinking while speaker lacks vessel |
| Whiskey offered but refused; player asserts that she drank | 0; no accepted alcoholic vessel |
| Whiskey nearby while she drinks her own water; offered vessel corrected to water | 0 |
| Two candidate skins, wrong recipient or another NPC drinks | No guessed binding or cross-character increment |
| Historical, quoted or conditional offer | Cannot supply current contents |
| Full action + occurrence with shorter/longer literal evidence span | Bind only when it identifies one actual consumption |
| Duplicate event, wrong occurrence, conflicting quote or deleted action | No extra increment or retargeting |
| Short/longer/measured pull and explicit two pulls/half a pull | 0.25 / 0.5 / 0.125 under existing quantity rules |
| Unknown bulk remainder, empty vessel, refill/spill/replacement | Preserve established volume rules; no guessed capacity |
| Threshold refusal, missing/stale clock, scope expiry and save rollback | Preserve current isolation/recovery/replay behavior |
| Individual and shared party/castle/social request routes | Same context and adjudication contract |

The proposed owned source paths resolve to `changed`, Release, Tier 2, dialogue facet, with `--run-motive-conversation-tests --json`. That plan was retrieved, not executed for a code change. Implementation must obtain a fresh exact-path plan/status, run canonical validation, then run `conversation_intoxication` and applicable `prompt_efficiency` checks on the exact successful artifact. If a profile, evidence schema, isolation/cleanup rule or tool contract changes, update the testing catalog, owning procedure and contracts together.

After separately authorized deployment and runtime handoff, observe fresh ordinary dialogue and use guarded disposable campaigns for required cross-route/threshold/save tests. Offline proof cannot establish native/model acceptance. Preserve the user's active conversation and original save.
