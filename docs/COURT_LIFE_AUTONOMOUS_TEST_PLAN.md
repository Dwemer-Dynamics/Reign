# Court life autonomous player acceptance plan

Status: testing authorized on BaseTwo and underway. Created 2026-09-05; revised 2026-09-07 to follow the user's instruction against repetitive live tests.

This plan covers international affairs, family visits and neglect, favor expiration and jealousy, patronage, noble visitor stays, and their integration into the ruler docket. Canonical build and deterministic validation are part of implementation and deployment. **Do not start the provider-backed or native campaign runs in this document until the user supplies the exact baseline save and asks to begin testing.** The user intends to change the reasoning effort first. No acceptance result is implied by this plan or by a successful build.

The implementation checkpoint is [COURT_LIFE_IMPLEMENTATION.md](agent/COURT_LIFE_IMPLEMENTATION.md). The supported testing routes, current profile names, confirmations and gates are owned by `reign.testing.json` and [TESTING_TOOL_GUIDE.md](agent/TESTING_TOOL_GUIDE.md). Query `reign_get_testing_catalog` again at the start of execution; resolve any drift before using a command. Do not edit the roadmap.

## 1. What constitutes proof

Every live acceptance scenario has a recorded starting state, a player-visible encounter, actual natural-language player turns submitted through the production interface, the resulting NPC replies, the ruler's production confirmation when appropriate, and the final state/receipt. Inspect both the presentation and the state. A plausible reply without a real game effect is a failure. A correct state change with misleading dialogue is also a failure. Tool success, compilation, or a synthetic fixture assertion alone is not player acceptance.

Avoid redundant reruns of essentially the same test. Different catalog templates remain distinct tests and may use similar flows; every template retains its live acceptance requirement. Different approaches, characters, substantially different wording, boundaries and failure modes also justify separate cases. Record the purpose of an additional attempt on the same case. Reuse applicable successful evidence and rerun only for a meaningful variation, failure investigation, or verification of a fix and its smallest affected regression boundary. Do not automatically repeat each successful case or seed. This clarification preserves full template coverage.

There are four distinct evidence levels:

| Level | Purpose | Does it satisfy natural-player acceptance? |
| --- | --- | --- |
| Deterministic contracts and isolated database tests | Exact arithmetic, eligibility, transitions, normalization, serialization and duplicate handling | Only the rules explicitly proved; never native integration |
| Provider-backed bounded conversation | Real language interpretation, character consistency, quoted agreement and response integrity | Conversation behavior only |
| Guarded native campaign with production screens | Real portraits, buttons, conversations, payments, diplomacy, relations, loyalty, movement and saves | Yes, for each observed feature and outcome |
| Assisted NPC capability case | A request-scoped cooperative prompt helps reach a difficult legal branch | Capability proof only; must be followed by an independent unassisted case |

A fixture may arrange an eligible family, visitor, grievance, treasury, ruler personality, faction relation, clock boundary, or commission. It must identify every mutation, use the normal production record/schema, and satisfy the same action validator as an organic event. It may not pre-write the expected effect, mark a turn complete, insert an acceptance receipt, waive costs, suppress consequences, move a prisoner through an invalid path, or bypass any campaign protection. The independent postcondition observer must read production state rather than the fixture's expected-result object.

## 2. Baseline, isolation and unattended operation

1. Record the user-provided save's exact name/path, campaign and timeline identities, native game version, Reign source fingerprint, deployed artifact hashes and canonical validation report. Verify the visible installed Control Center is on `http://127.0.0.1:5101`; the installed launcher is the authoritative runtime path. The workspace developer cache must not be substituted for installed portrait/runtime data.
2. Enroll that exact baseline through the guarded campaign-test MCP tools with this objective. Create and verify a task-named disposable copy before any time advancement or mutation. A suggestive save filename is not authorization or isolation proof. Never overwrite, rename or delete the baseline.
3. Record native and server starting snapshots: ruler identity/marriage/family/ages; kingdom/capital/war state; gold; directional relations; favor/contact timestamps; family attention rows; diplomatic pressure and pending actions; native loyalty; parties/offices/quests; existing visitors, commissions and docket. Redact credentials and provider secrets from reports.
4. Use one overwritten rolling checkpoint by default. The Save Sync unique-state limit is 15; inspect capacity before a milestone save. Clean up only exact run-owned saves through the documented confirmation-gated cleanup route. Never delete another task's state.
5. At every checkpoint pause native time, wait for the campaign-test quiescence gate, inspect pending network/native actions and failures, then save. A save made before required queues drain fails that checkpoint. Record any intentionally pending long-term object, such as a commission or referral, separately from in-flight transport.
6. Use existing bounded campaign advancement tools. Never start a second server for tests. Recover ordinary acknowledgements, stale handles and transient transport errors by re-observing state and trying another supported MCP/harness/native control route. Do not repeat an uncertain payment or decision blindly; inspect its receipt first.
7. After a fix, rebuild through `reign_validate`, deploy those exact artifacts through the visible lifecycle, re-observe the disposable state, and rerun the failing case plus its affected regression boundary. Source or schema changes invalidate the old fingerprint's acceptance where relevant.
8. Finish with native time paused, queues drained, the rolling disposable saved, the original baseline untouched, and the visible installed server/game state described in the report.

## 3. Scenario ledger and execution order

Create a durable run directory beneath the catalog's evidence root, using one run ID throughout. Keep a machine-readable scenario ledger alongside a readable report. Each row records:

`scenarioId`, `requirementIds`, `sourceFingerprint`, `campaignTestRunId`, `campaignId`, `timelineId`, `baselineIdentity`, `fixtureId`, `matterId`, `templateId`, `source`, `severity`, `participantIds`, `preStatePath`, `actualPlayerWords`, `npcReplyPaths`, `turnIds`, `decisionOptionId`, `acceptedTerms`, `nativeReceiptIds`, `postStatePath`, `screenshots`, `providerTraceIds`, `assisted`, `result`, `failureReason`, `retestOf`.

Allowed results are passed, failed, blocked with a concrete prerequisite, and not run. Never silently drop an ineligible template; record its missing prerequisites and arrange an eligible fixture or report the gap. A retry is a separate attempt under the same scenario, preserving the first failure. Store exact visible words, not just paraphrased test expectations.

Execute in this order:

1. Readiness and deterministic boundary results; installed artifact and schema checks.
2. One complete unassisted case from each source through the production court screen.
3. Full template coverage, supported decisions and negative language cases.
4. Time boundaries, failures, duplicate callbacks, save/load and succession changes.
5. Interactions among sources, regression checks against existing noble/notable petitions, and visual acceptance.
6. Existing 180-day ruler-play soak extended with the new checkpoints below.
7. Independent replay of every previously assisted critical branch without a cooperative override, then final evidence audit.

The new `court_life_*` profiles extend the existing guarded `ruler_docket_test` operation. Preparation, observation and production-screen interactions are separate steps. Use `court_life_preflight`, `court_life_prepare`, `court_life_open`, `court_life_converse`, `court_life_choose_confirm`, `court_life_snapshot`, `court_life_observe`, `court_life_clock_observe`, and `court_life_save_prepare`/`court_life_save_verify` as registered by the catalog. Visitor creation teleports the selected group immediately without advancing time. Deadline advancement remains with the existing guarded campaign service. The harness must never advance time or save by a hidden alternate path.

## 4. Shared docket and audience cases

| ID | Setup and natural action | Required observations |
| --- | --- | --- |
| D01 | Load at 07:59, wait across 08:00, open court | One stable daily roll; maximum five new entries; no repeat on hourly ticks or reopen |
| D02 | Repeated deterministic seeds with all sources eligible | Weights 6 notable, 6 visitor, 5 domestic, 5 international, 5 family, 3 patronage out of 30; patronage is least frequent |
| D03 | Due foreign replies of 0, 1, 4, 5 and more than 5 | Oldest due replies reserve up to five slots; remaining ordinary roll is 1d(5-R); excess replies remain pending |
| D04 | Enable Chancellor before the roll, then disable later | All new sources suppressed while active; no family dismissal for a suppressed/invalidated opportunity; no double generation on resume |
| D05 | Accumulate more than five pending nonexpired entries | Every entry remains accessible in the existing scroll area; daily creation cap still holds; expiration order is correct |
| D06 | Open and return without a decision, then reopen | Same matter, people, terms, transcript and completed-turn receipts; no reroll or free duplicate reward |
| D07 | Send a question with one, two and four participants | Correct portraits/names; every actual speaker uses their own knowledge/personality; no one speaks the ruler's words |
| D08 | Disconnect provider before opening, midway through a multi-NPC beat, and after a visible reply but before its agreement interpretation | Clear recoverable failure, no effect, no false completed family turn; successful earlier replies are retained; retry reuses the exact saved reply and does not duplicate speech, acceptance, counteroffers or completed turns |
| D09 | Close/reopen after a reply, repeat callback and confirm twice | Single receipt and single effect; opening is not counted as a spoken player turn |
| D10 | Lose capital, ruler office, kingdom, or become captive during a pending matter | Authority revalidated; no stale court execution or visit punishment for technical unavailability |
| D11 | Participant dies, becomes captive, leaves eligible kingdom, receives a party/office or becomes hostile | Event/visit revalidates safely; unrelated party/quest/office remains intact; clear reason in evidence |
| D12 | Save before a decision, after a confirmed decision, and with a pending delivery | State and stable IDs survive a different game process and server restart; already-applied effects do not repeat |

Use ordinary player wording such as “Tell me what happened,” “I need to hear the other side,” “What exactly would this cost?”, “I have not decided yet,” and “I will hear you again later.” Do not teach the NPC schema names, option IDs, trait thresholds or expected hidden numbers. The harness may select the resulting visible control using its stable binding after the player has reviewed the terms.

## 5. International affairs

### Exhaustive catalog coverage

Enumerate the actual runtime international catalog into the ledger and assert at least 30 minor, 15 serious, 15 severe and 15 critical distinct templates, with a 50/30/15/5 severity roll. For **every template**, build eligible native participants and any real agreement prerequisites; open the production audience; ask at least two relevant questions; hear all principals; choose a supported resolution; inspect the real resulting effect and durable history. Rotate acceptance, rejection, compromise, referral and unilateral ruling so every supported option family is covered at every applicable severity. Do not substitute one representative template for full catalog coverage.

Each template must present a premise rather than a fixed script, use real kingdom/lord identities, describe unverified allegations as allegations, and offer an implemented action. The model may add compatible low-impact detail; it may not invent a death, crime conviction, treasury loss, owned fief, army movement or completed treaty to make the story fit. Use materially different wording or characters to test relevant variation; a routine second seed is not required.

### Political and diplomatic matrix

| ID | Player-facing situation/action | Required postconditions |
| --- | --- | --- |
| I01 | Own noble complains of a foreign lord: “Tell me your losses and what redress you seek.” | Real eligible principals; substantiated facts separated from claims; severity and offered remedies consistent |
| I02 | Foreign ambassador complains of own lord: “Bring me the evidence before I accept that accusation.” | Actual foreign representation and charter; own lord can answer; no default admission by ruler |
| I03 | NPC ruler hears a complaint involving the player's lord and supports their own lord | NPC-to-player pressure changes exactly once by the severity rule; no AI decision on behalf of the player |
| I04 | NPC ruler rules against their own lord in favor of player's lord | Their pressure toward player decreases by 6/10/16/24 as applicable, clamped by existing pressure bounds; no player pressure meter invented |
| I05 | Player sides with own lord, foreign lord, or agreed compromise at each severity | Correct directional winner/loser relation deltas and acceptance mitigation; foreign ruler quarter effect with midpoint away-from-zero rounding; no reversed pair |
| I06 | “I will pay the stated compensation.” Review and confirm | Real payer balance decreases and recipient/ledger increases exactly once; insufficient funds prevents execution |
| I07 | “I will pay half, provided your ruler considers the matter closed.” | Conditional counteroffer is not payment or settlement; only exact accepted supported terms can be confirmed |
| I08 | “Apologize and withdraw the allegation.” | Only supported apology/withdrawal/history/pressure effects; no nonexistent arrest or compensation |
| I09 | Real trade/gift/prisoner/alliance opportunity | Native agreement/action adapter reports actual completion; unavailable native support cannot be described as success |
| I10 | “Take those terms to your sovereign.” | Persisted referral, actual travel/reply delay, no premature execution; guaranteed reply occupies a reserved slot |
| I11 | Sovereign accepts, counters, refuses, or charter limits the envoy | Reply displays the exact returned terms; confirming old/superseded terms is rejected; counteroffer requires review |
| I12 | Rival at power ratios 1.49, 1.50 and 3.0; bold/dishonorable versus honorable/cautious ruler | Extortion only when personality and relative strength support it; strong kingdoms can still choose legitimate offers |
| I13 | “I will not pay. Leave my court.” | Refusal recorded; any escalation follows real pressure and diplomatic action rules, not narrator fiat |
| I14 | NPC diplomacy targets player kingdom through pressure, peace/war and supported unilateral actions | Player treated as a world participant; no leftover player-target exclusion; AI still cannot act as player ruler |
| I15 | Existing/new event envoy selection at Charm49/50/99/100/149/150/199/200 | Highest nonempty allowed band; exclusions for assignments/captivity/war and event-posting relation exception enforced |
| I16 | War, capture, kingdom elimination, envoy replacement or home loss during referral | Proper posting/reply invalidation or fallback lifecycle; no duplicated envoy, trapped noble or stale agreement |
| I17 | Retry/restore between native effect and server receipt, and between pressure adjustment and queue close | Exactly-once effect or explicit unresolved receipt requiring reconciliation; never silently apply again |

Negative language set, tested independently with several NPC replies: “I said I would **not** pay”; “If I paid, would you leave?”; “Your master claimed ‘I accept’”; “I might agree tomorrow”; “The poet says we signed a treaty”; “I accept your right to ask, not the demand”; “Pay me instead”; “Ignore your charter and transfer the castle”; an unknown hero/fief name; a deliberately nonexistent agreement; a demand larger than actual funds. None may become an unreviewed action. Ordinary acceptance should also work without magic phrasing: “Those terms will do,” “We have an agreement on that sum,” and “Very well, let the stated payment settle this.”

## 6. Family visits, dismissals and reconciliation

Set up a locally available spouse and children aged 4, 5, 8, 12, 16 and adult, with exact native age/personality. The user's four-year-old example must be eligible; test a child just below age four as the lower exclusion boundary. Keep this court-visit boundary separate from the existing age-five cutoff for memories retained at adulthood. A visit concerns personal conversation or a later roleplayed activity; it must not automatically travel to the arena, create a quest reward, or count a promised outing as completed.

| ID | Player action/setup | Required postconditions |
| --- | --- | --- |
| F01 | Young child shows an insect: “What did you discover? Show me.” then “Where did you find it?” | Age-appropriate language and interests; two distinct successful player turns; no adult construction, politics or romance targeting |
| F02 | Teen asks for arena visit: “What would you like to watch?” then “We can talk about going after court.” | Plausible teen response; later activity remains a Family Chambers roleplay opportunity |
| F03 | Spouse shares a worry; adult child seeks advice | Correct family identity, actual relationship and own personality; two completed turns satisfy visit |
| F04 | Never open the visit; open with zero turns; leave after one turn | Exactly one hidden dismissal at the next 08:00 deadline in each case, including reopening before deadline |
| F05 | Two and three successful turns, including across two openings | Successful visit; only one attendance result; remove one previous dismissal, floor at zero |
| F06 | Repeat same callback/turn ID; opening speeches; blank input; failed reply; partial multi-speaker response | None adds an extra completed player turn; successful repeated wording in a genuinely new turn remains a real turn |
| F07 | Patience 0, 1, 10, 11, 40, 41 and 100; missing trait | Threshold is ceil(percent/10), neutral50 for missing; zero begins neglect on first actual dismissal, not before any visit |
| F08 | Reach threshold, inspect next day's docket and social conversation | No more visits from that member; strong private neglect prompting; hidden counter never exposed as game statistics in dialogue/UI |
| F09 | Advance one day, several missed days and to relation0; include positive/negative public-standing offsets and a +100 capped attitude | Actual effective family-to-player relation falls1/day beginning following cycle and stops at0 by changing only personal affinity; public standing and opposite direction remain unchanged; existing negative values are not raised toward0 |
| F10 | Technical error, unavailable/dead/captive member, capital/rulership loss, Chancellor suppression | Visit invalidates appropriately without neglect penalty; merely ignoring a valid visit while away is still assessed correctly |
| F11 | In Family Chambers: “I have neglected you. I want to make time for you again.” NPC actually accepts | Neglect and dismissal counter clear only after accepted reconciliation; lost relation is not refunded; future visits can resume |
| F12 | Thanks, ordinary affection, gift, “I am not apologizing,” quoted apology, hypothetical/conditional reconciliation or NPC refusal | No false reconciliation or counter reset |
| F13 | Same reconciliation callback twice, reconnect during response, save/load before acceptance | One receipt and no duplicate mutation; uncertain response re-observed before retry |
| F14 | Child neglected, then matures to adult | Dedicated child directional value actually controls child response band; exact value migrates once into adult relationship; no shadow value or duplicate decay |
| F15 | Family member dies, spouse changes, ruler succeeds, kingdom/campaign timeline changes | Correct pair identity and lifecycle; no inheritance of another ruler's hidden neglect or unsupported cross-timeline receipts |

For emotional quality, compare the same family member before neglect, at threshold, and after accepted reconciliation. They should express hurt in their own manner, not recite a generic rule. Children express wanting parental attention; spouses may express romantic concern. No child may receive romantic jealousy prompting concerning adults.

## 7. Favor, reputation and jealousy

| ID | Setup/action | Required observations |
| --- | --- | --- |
| J01 | Favored by ruler; no real contact for29.999 then30days | Current favored tag expires at30; history remains; rumor/history alone cannot keep current favor alive |
| J02 | Actual spoken conversation and completed personal correspondence in both pair directions | Correct ruler-subject contact clock refreshed once per successful receipt; works for NPC rulers and the player |
| J03 | Passive proximity, opening cue, failed reply, unanswered letter, reading old history, duplicate callback | No contact reset; stale counters cannot immediately recreate expired favor |
| J04 | Legacy favored pair missing contact timestamp | One30day migration grace; save/reload does not restart grace indefinitely |
| J05 | Fresh actual contact after expiry | Existing legitimate favor acquisition can operate again; no permanent lockout |
| J06 | Jealous observer vs one current favorite; both observer and rival current favorites | Shared-favorite encounter weight halves; jealousy trait and directional standing do not change merely because of the reduction |
| J07 | One of two favored tags expires | Reduction stops; correct current tag/pair owner, not stale reputation history |
| J08 | Neglected spouse knows an adult opposite-sex NPC is favored | Prompt allows proportionate jealousy based on actual trait and known evidence; does not assert an affair as fact |
| J09 | Spouse does not know favor; rival wrong category; child observer | Knowledge gate respected; children receive attention jealousy only |
| J10 | Low/high jealousy, honor, boldness and existing relationship variants | Plausible differences in private hurt, open complaint, rivalry, restraint or direct questioning; deterministic eligibility and stochastic weights separately verified |
| J11 | Domestic jealous noble raises concern: “What have you actually seen?” | Complaint fits the domestic source slot; hearsay stays hearsay; no fabricated punishment or romantic consent |
| J12 | Player reassures, challenges rumor, openly favors both, refuses accusation or escalates verbally | Natural response, truthful known evidence, supported social effects; no forced resolution or automatic allegation proof |

Suggested player wording includes “You have seemed distant lately,” “I value both of your counsel,” “A song at court is not proof of an affair,” “What makes you believe that?”, and “I will speak with you privately later.” The test observer must inspect selected prompt evidence as well as the visible response to establish that the correct current tags and personality traits caused the behavior. Never score a coincidentally jealous reply as proof of tag integration.

## 8. Patronage

Enumerate all40runtime templates into the ledger. For every template, hear a real proposal and ask “What would my support let you do?” Validate its eligible objectives and reach, review a supported commission and observe its delivery. Use history-dependent templates only with an actual eligible recorded subject; record ineligibility otherwise, then arrange legitimate prerequisite history on the disposable fixture. The model must not fabricate a victory or coronation as a past fact.

| ID | Player action/setup | Required postconditions |
| --- | --- | --- |
| P01 | “Perform something for the court today.” 500denars | Actual debit; immediate bounded performance/cultural record; no kingdom-wide reward |
| P02 | “Make a small work for my household.” 500denars | One-day durable work with named creator; only a real supported native item can appear as an inventory object |
| P03 | Praise funding500/2000/5000/8000 | Correct court/local/three-settlement/kingdom reach and0/1/3/7day delivery; +3 eligible nobles toward ruler once |
| P04 | Loyalty objective at2000/5000/8000 | Actual supported town/castle loyalty +5 once at delivery, capped by native bounds; wrong/foreign settlements excluded |
| P05 | Ask for loyalty at500 or kingdom reach for a small keepsake | Unavailable option rejected; explanation matches available scope |
| P06 | “Take this work to these three towns…” with real named choices | Exact validated regional recipients reviewed; unknown, duplicate, hostile or excess destinations rejected; no silent redirect after agreement |
| P07 | Insufficient funds, exact funds, repeated confirm and callback | No negative money or free work; debit/effect once; amount and schedule remain visible |
| P08 | Start second active praise commission or second active loyalty commission | One active per objective enforced; different supported objective may coexist |
| P09 | Recipient noble dies/leaves kingdom; town captured before delivery | Snapshot and delivery eligibility reconciled; invalid recipients skipped with explicit evidence; no reward to newly unrelated kingdom |
| P10 | Server unavailable at relation delivery, then reconnect | Commission waits for authoritative directional adjustment receipts; no false completion/free reopened objective |
| P11 | Deliver after a multi-day jump and reload immediately | Exact once; delay uses campaign time; record and history survive different game process |
| P12 | Finish a commission | Completion report/history appears without consuming an extra docket slot; creator identity remains stable across repeat appearances |
| P13 | “I may fund you later,” “Would8000be enough?”, “I refuse to pay,” and NPC counteroffer | Discussion does not spend funds; confirmation matches exact reviewed terms |

Compare court praise recipients to the actual throne-room audience snapshot, not everyone merely standing somewhere in the settlement. Inspect both directional relation and native loyalty, because one objective must not accidentally apply the other's effect.

## 9. Noble visitors and private invitations

| ID | Setup/action | Required observations |
| --- | --- | --- |
| V01 | Domestic solo courtesy/business traveler; peaceful foreign traveler | Genuine available noble identity, reasonable independent purpose, respectful introduction and announced stay |
| V02 | One parent+one adult child; two parents+one child; one/two parents+two adult children, maximum4 | Correct relationships/roles and portraits; shared goal with individual private context; every member cooperates according to their own personality |
| V03 | Bold opposite-sex solo adult seeks ruler's attention | Native romance eligibility, age and kinship gates preserved; initiative reflects character, no automatic consent |
| V04 | Married ruler; initiating Boldness60/61 and Honor40/41 crossed | Private-meeting initiative only at required derived personality threshold; otherwise respectful visit remains possible |
| V05 | Unmarried/married, different sex combinations, close kin and minor boundaries | Correct eligible romantic candidate; relatives/minors cannot become courtship targets; ordinary social visits remain appropriate |
| V06 | Parents try to arrange time alone: “What brings your family to my court?” | Shared purpose visible through distinct natural responses; no character knows another's private thoughts without evidence |
| V07 | “I would like to speak privately later,” accepted vs refused/conditional | Only a mutual later-meeting context is recorded; no teleport, marriage, sex, conception or completed rendezvous |
| V08 | Later ordinary social/private conversation during the stay | Same visitor identity, purpose, planned duration and accepted invitation context are available; reply respects current relationship and consent |
| V09 | Roll a visit at paused time; stay3–7days and departure boundary; failed or partial placement | Entire group teleports to court immediately before its docket entry is published, with no travel schedule or time advancement. Full stay starts immediately. Failed placement cancels and unwinds completed moves; no remote audience, wandering or duplicate arrival |
| V10 | Party leader/member, governor, court officer, regent, ambassador, quest giver/target, companion mission | Excluded from visitor selection; no stolen party member, broken quest or office disruption |
| V11 | Native AI tries to create party/move settlement during reserved stay | Reservation guards preserve residence without disabling unrelated AI |
| V12 | External assignment/capture/war/death/capital loss wins during stay | Revalidation safely releases/invalidates; never pulls a captive or newly assigned hero away |
| V13 | Normal departure; original home captured or unavailable | Safe eligible home fallback, reservation removed, ordinary AI resumes; no permanently trapped guest |
| V14 | Save immediately after creation, during stay, after invitation and after departure; load an older queued visit | Stable members/start/departure/invitation receipts in another native process; no reset of duration or repeated move. Legacy queued visits revalidate and place on their next processing pass without waiting for the old travel date |
| V15 | End introduction without two turns or ignore guest | No family-dismissal stat; visitor and ruler's actual family are separate concepts |

Include restrained, honorable, shy, bold and jealous individuals. A cooperative override must never rewrite these actual traits or relax the married-target gate. If the natural character refuses a private meeting, that is a valid successful refusal case; arrange a different eligible case for acceptance coverage.

## 10. Cross-system failures, persistence and concurrency

Run these at a small number of deliberately chosen state boundaries where they expose a distinct failure mode, rather than repeating every scenario blindly:

- Server failure before a reply, after a reply before local acknowledgement, after local payment before server receipt, after a relation adjustment but before commission completion, and after a foreign decision before docket queue closure.
- Native process restart with pending family registration/turn outbox, foreign referral, visitor in transit/resident, patronage commission, expired favor migration and an active neglect decay pair.
- Load the exact disposable checkpoint into a **different native game instance**, not merely reopen the screen. Verify Save Sync restored the corresponding server state and timeline; no replay into a mismatched save.
- Same command/turn delivered twice; two confirmation clicks; old response arriving after screen closure; stale decision terms; incompatible ruler/capital/kingdom identity; missing participant; malformed model JSON; empty/repaired fallback reply.
- Concurrent due patronage delivery, family deadline, foreign reply and visitor departure at08:00. Each receives its own once-only transition; maximum new docket entries remains5; no hidden family penalty because transport was unavailable.
- New ruler succession, divorce/new spouse, child adulthood, foreign kingdom elimination and player kingdom dissolution. Retain historical records, end invalid live obligations safely, and preserve directional identity.
- Existing notable/direct-resource/gold/soldier petitions, domestic dispute/investigation/reversal and Chancellor salary/office behavior retain their prior expected results and UI controls.

## 11. Interface and natural-play acceptance

Reuse the approved modern court shell and movie. Run the provider-free preview contract, relevant layout/scroll/portrait contracts, full rendered preview matrix and approved-reference fidelity audit, recording their evidence paths. Native Bannerlord acceptance is still required after those checks. Inspect one/four attendees, long names, long translated or generated text, absent portrait, loading, provider error, many pending cards, all decision states, resize/UI scale and scroll-to-bottom behavior. No legacy frame may show through. Natural dialogue must not expose hidden patience, dismissal, jealousy weights, raw pressure, personality percentages or model instructions.

Review every visible economic decision for intelligible cost, recipient, objective, reach and delivery time. Confirm and cancel must behave consistently; reviewing terms must not pay. Family and courtesy visits should read as social audiences, without invented judgments. An unavailable action needs a clear recoverable explanation. State transitions and transcript receipt counts must remain consistent with what the user saw.

## 12. Extended180-day ruler-play soak

Use the existing guarded ruler-play soak with these additional checkpoints; reuse applicable existing domestic-petition evidence. Keep organic event selection enabled between controlled cases. The soak proves elapsed-time lifecycle and stability; it does not require repeating an accepted conversation every day. At meaningful encounters vary topics, ask follow-ups, sometimes postpone, decline unsuitable commissions, accept some agreements and refuse others. Do not always choose the best-reward option.

| Campaign interval | Focus |
| --- | --- |
| Days1–7 | All six sources seen under eligible conditions; normal daily generation; visitorarrival/departure and short commissions |
| Days8–14 | Foreign referral/reply cycles, multiple patronage reaches, family visit attendance, mixed pending backlog |
| Days15–29 | Neglect threshold/first decay, jealousy conversations, continued visitor/office/war revalidation |
| Days30–31 | Favor expiry exact boundary, migration grace and real-contact refresh; native save/load checkpoint |
| Days32–60 | Accepted reconciliation, changed favorites, kingdom-targeted diplomacy, long-term pending deliveries and failure recovery |
| Days61–90 | Ownership/war changes, another process restore, child/household lifecycle where fixture makes exact boundary practical |
| Days91–120 | Sustained source mix/cooldowns, absence of duplicate rewards, memory and transcript consistency |
| Days121–180 | Regression replay, pressure escalation/de-escalation, kingdom loyalty/relations caps, queue/resource stability and final continuity |

At each interval inspect generation counts, source eligibility/skips, unresolved queue age, active commissions, visitor reservations, hidden family outcomes, directional deltas, current favor, pressure events and failure logs. Explain chance-based absences with eligibility evidence; use a separate deterministic fixture to cover missed branches. Never alter organic weights merely to make the observed sample look balanced. Track resource growth and response latency; escalate sustained regressions using existing scale/performance harnesses, not an invented pass threshold.

## 13. Release decision and handoff

Release acceptance requires all catalog templates exercised, all supported action families demonstrated through the production UI, every critical negative/boundary case passed, no unexplained duplicate/missing effect, correct independent save/load, required rendered/native interface evidence, and the completed soak. Every assisted critical branch needs an unassisted acceptance or a clearly reported outstanding natural-language gap. A pending feature or blocked prerequisite cannot be relabeled as a pass.

The final report must list the deployed source fingerprint and artifact hashes, canonical validation profile/duration/report and hygiene evidence, scenario totals by source/evidence level, exact remaining failures/not-run cases, baseline/disposable identities, save capacity, final application state, and links to the ledger, transcripts, screenshots and structured postcondition reports. If only deployment validation is complete, explicitly state that autonomous player acceptance has not begun.

### 2026-09-07 visitor timing revision
The user replaced scheduled noble travel with immediate placement when a visit is rolled. Earlier transit/arrival-delay evidence is retained as superseded history, not current acceptance. V09/V14 now require immediate placement, full-stay timing, failed-placement cleanup, safe return to the original location (or a safe home), and reload continuity. Other passed tests remain valid unless affected by this change.
