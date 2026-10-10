# Personal NPC Gossip — numerical design

Status: implementation in progress; provider-free verification and native acceptance tracked separately. Updated 2026-09-26. Policy version: `personal-gossip-v1`.

This replaces the initial chat plan. The user explicitly revised negative gossip: an NPC may exaggerate a real encounter into a false event to hurt someone. The system must retain what happened separately from the story told. The other selected requirements remain: personality/trust-based discretion, distance-based campaign time, one in-game hour locally, brief NPC-to-NPC exchanges, limited retelling, and dialogue-first consequences.

All formulas and limits below are proposed v1 tuning, not measured player-behavior probabilities or existing game defaults. They are fixed implementation defaults until deliberately revised and versioned. The current source establishes the trait scales and relationship authority, not these new weights.

## 1. Inputs, numerical conventions, and when to evaluate

Use the existing 43 `traitPercentages`, not a new personality model or the separately calibrated Court virtues. Relevant keys already exist: `sociability`, `impulsiveness`, `pride`, `curiosity`, `vengefulness`, `compassion`, `jealousy`, `envy`, `honesty`, `loyalty`, `tact`, `discipline`, `empathy`, `shame`, `familyMotivation`, and `socialTrust`. Read them through the existing trait-percentage adapter; its legacy foundation fallback is `clamp(50 + 25 * foundationModifier, 0, 100)`, with missing traits at 50. Log fallback use; do not construct unrelated characters to run gossip.

| Existing trait band | Meaning |
|---|---|
| 0–20 | Very low trait / strong inverse tendency |
| 21–40 | Low |
| 41–60 | Ordinary or situational |
| 61–80 | High |
| 81–100 | Strong defining tendency |

Use current authoritative directional affinity `R(A,B)` on -100..100. Never substitute the reverse direction, Bannerlord's averaged pair relation, or retired relationship facets. For family pairs not yet materialized, use the existing canonical relationship baseline resolver without inventing a new pair. Missing unrelated pair affinity is 0, not evidence of friendship. Friends start at +30, close friends at +50; dislike starts at -10, rivalry at -30, enemies at -50, and hatred at -70.

`pct(x, cap)` means clamp to 0..cap and round once to an integer, with halves away from zero. Unrounded intermediate scores remain doubles. A percentage succeeds when a persisted uniform d100 roll is at or below it: 0 never succeeds; 100 always succeeds. Stage rolls use different seed salts. The same campaign/timeline, speaker, conversation episode, canonical topic, and stage reuse the same roll after retries, reopening, and reload.

### Evidence-backed feelings

The accepted dialogue response can classify three private feelings: positive excitement, curiosity/concern, and desire to hurt or competitively provoke someone. Each classification must cite accessible current dialogue or an established personal concern. The model chooses a level, not a probability:

| Level | Definition |
|---|---|
| 0 | No relevant feeling; routine acknowledgement or small talk |
| 25 | Mild pleasure, annoyance, or curiosity |
| 50 | A personally interesting question, meaningful pleasure, concrete grievance, or competitive opportunity |
| 75 | Strong excitement, strong hurt, persistent curiosity, or a strong desire to provoke a rival |
| 100 | An exceptional personal development, major betrayal, or defining emotional moment |

Accept only these labels with valid evidence references and classifier confidence >=0.80. Unsupported signals contribute 0. A factual correction, quoted insult, hypothetical act, or friendly disagreement is not automatically a grievance. Stored subjective beliefs may motivate an NPC, but remain beliefs rather than proof of the underlying event.

For impression gossip, let `delta` be the net sum of that NPC's authoritative, server-applied `final_delta` receipts toward the player in this conversation, deduplicated by exchange. Exclude unrelated ambient drift, projections, pending assessments, and unapplied proposals. A pending native acknowledgement does not erase an already applied server adjudication. Its emotional input is `Q = min(100, 20 * abs(delta))`. Zero delta gives no good/bad impression; capped-to-zero positive prose does not manufacture a positive receipt.

The general emotional input `E` is the greatest applicable feeling or Q. Evaluate when E >=50. A very sociable or curious NPC (`sociability >=75` or `curiosity >=75`) can also evaluate a real mild topic at E >=25. E=0 never creates gossip.

### One opportunity, not another roll every line

Evaluate immediately after accepted exchanges and after their relationship receipt is available; do not wait for the conversation to close. Repeated discussion updates the same canonical topic. Reopening the same discussion on the same campaign day also reuses it. New exchange IDs, wording, provider retries, and dialogue length are not new opportunities.

New evidence can raise a topic's probability against its existing roll. For example, a saved roll of 55 fails at 40% and can later pass at 60%; ten evaluations at 40% still have a 40% chance, not a cumulative chance approaching certainty. A topic that is already queued cannot create another outbound item. Its type and recipient are frozen; relevant corrections update an unsent message, and invalidated source events cancel it. Already delivered stories remain historical records and can be contradicted by later evidence.

## 2. Sharing, intent, recipients, and discretion

### A. Chance to want to share

```
P_share = pct(0.35*E + 0.25*sociability + 0.15*impulsiveness
              + 0.10*pride + 0.15*curiosity - 15, 85)
```

This is the chance to proceed to a disclosure decision, not a guarantee that private details will be sent. Strong feelings and personality dominate. There is no flat per-line gossip chance.

### B. Which type

There are exactly three output types: `negative`, `positive`, and `impression`. Positive includes trusted consultation and curiosity, not only happiness. Impression carries positive/negative valence separately from type.

A negative candidate needs a known person the speaker wants to hurt and a real encounter or received story to exploit. Require hurt/provocation feeling >=50 **and** directional affinity toward that recipient <=-10. A grievance never bypasses the relationship threshold. The recipient need not be mentioned when local; remote contact requires an explicit validated mention. A competitive boast can qualify even when the speaker likes the player and enjoyed their encounter.

Let grievance G be 0 for none, 25 for a slight, 50 for a meaningful perceived injury, 75 for a serious betrayal/humiliation, or 100 for a defining injury. These use the same evidence gate. Let `D = max(0, -R(speaker,victim), G)`. Let J and N equal the jealousy and envy traits only when the speaker's own accessible knowledge establishes the corresponding romantic/attachment rivalry or status/possession comparison; otherwise they are 0. Secret feelings belonging to the recipient are unavailable to this calculation. Let `B_victim = max(0, R(speaker,victim))`.

```
P_negative_given_share = pct(
    0.35*vengefulness + 0.20*D + 0.15*hurtFeeling + 0.10*pride
    + 0.10*(100-compassion) + 0.05*J + 0.05*N - 15
    - 0.20*loyalty*B_victim/100, 85)
```

After P_share succeeds, provisionally select a negative recipient with the weights below, then calculate this probability from that person's grievance, directional relationship and supported motive. On success, choose negative. Otherwise choose positive if a qualifying excitement/curiosity topic and friendly recipient exist; that topic's own feeling must meet the 50 threshold, or 25 with sociability/curiosity >=75. Otherwise choose impression if delta is nonzero and an eligible recipient exists. Otherwise send nothing. This prevents three independent type rolls from making disclosure nearly inevitable.

Honesty is intentionally absent from the decision to hurt: an honest, spiteful NPC can weaponize a true incident. Honesty controls deception and breaking an explicit promise below.

### C. Recipient choice

Candidates must be living, known, persistent NPC identities available for communication. Exclude the player, speaker, prisoners/unavailable identities and earlier outward-chain holders. Positive sharing/consultation and either sign of impression require directional R >=30, or immediate family/spouse with R >=0. Immediate family means parents, children and siblings; cousins and other relatives use the friendship threshold. Negative gossip goes directly to a recipient with R <=-10 and supported hurt/provocation >=50. A bad impression describes the player's encounter, not hostility toward its listener. Mentioning anyone never overrides these gates. The previous neutral-question and friendly-smear intermediary exceptions are removed.

Local means matching **nonempty settlement IDs or nonempty travelling-party IDs**. Shared army, clan, kingdom, or nearby coordinates alone never qualify. Only a person explicitly mentioned in the accepted triggering dialogue may be contacted remotely. Resolve unambiguous names and kin references such as Ira's “my mother” or the player's “your mother” to Rhagaea; ambiguous names do not unlock travel. Background profiles, retrieved passages and names introduced by generated gossip are not triggering mentions. Carry validated identities and accepted source-turn references into every retelling. A distant unnamed friend is ineligible even at +100.

```
W_friendly = 10 + max(0,R(speaker,recipient))
             + 25*familyMotivation/100 if immediate family/spouse
             + 20 if directly relevant to the topic
W_negative = 30 + max(-R(speaker,recipient), evidence-backed grievance)
             + 20 if directly relevant to the topic
```

Relevance must come from the speaker's accessible knowledge; hidden feelings cannot improve a weight. After relationship/accessibility/location filtering, retain at most eight candidates per type, first eligible mentioned people, then higher base weights, then stable identity order. If named and unnamed candidates both remain, multiply every named base weight by:

```
4 * total unnamed weight / smallest named base weight
```

Leave unnamed weights unchanged. Draw once from the resulting weights; if only one group exists, use its ordinary base weights. One mentioned Rhagaea plus two equal local friends gives 80%/10%/10%; two equal mentioned people plus one local friend gives 44.444%/44.444%/11.111%; one mentioned enemy plus one ordinary rival gives 80%/20%; ordinary weights 80 and 40 give 66.667%/33.333%. A sole candidate receives 100% of recipient selection, conditional on sharing and disclosure. Every named candidate individually outweighs the entire unnamed group by at least four to one. These are not overall gossip probabilities.

Freeze the final recipient when queued. A failed disclosure never causes another recipient draw. Impression fallback must use an eligible friend/family recipient or send nothing. If no eligible audience exists, create no delivery job and retain no story waiting for arrivals. A later substantive accepted exchange may reassess the topic with its saved stage rolls; hourly presence updates cannot retry it.

### D. Chance to reveal details

Classify each proposed disclosure by the most private real source fact it uses. A fabricated addition cannot launder a genuine secret into the public category. Missing privacy classification defaults to personal. A fact the recipient already knows is not a new confidence breach.

| Source privacy | Base chance |
|---|---:|
| Public/nonpersonal fact, with no confidence attached | 100%; no discretion roll |
| Personal detail | 65 |
| Intimate or materially compromising detail | 40 |
| Explicitly entrusted secret | 15 |

For nonpublic material:

```
discretion = 0.45*tact + 0.30*discipline + 0.25*empathy
F = max(0, R(speaker,recipient))
B = strongest positive affinity toward another person whose confidence is exposed

P_detail = pct(base - 0.45*discretion + 0.20*impulsiveness
               + 0.15*E + 0.15*F - 0.30*loyalty*B/100
               - 0.20*shame if exposing the speaker's own intimate/compromising conduct
               + 0.30*P_negative_given_share if the selected intent is negative
               - 0.30*honesty if the speaker explicitly promised secrecy, 95)
```

The conditional terms are added/subtracted only when their stated condition holds. With no other confidence-owner, B=0. Immediate family does not automatically receive every secret. Honest loyalty can produce 0%; no mandatory minimum leak chance overrides it. Low discretion and strong spite can still permit betrayal.

If detail disclosure fails, permit only a detail-free impression when delta is nonzero and mentioning the encounter's existence passes its own privacy classification against the same disclosure roll. If the relationship or meeting itself is secret, its existence is also secret. A failed secrecy roll must not leak it through a supposedly vague hint. Remove locations, gifts, intimate acts, promises, and other identifying details from the impression.

## 3. Negative exaggeration and recipient response

### A. Truth and the told version are separate

Keep immutable source evidence and the speaker's interpretation separately from the delivered claim, the selected distortion, and what each recipient believes. Intentional distortions may be false about what occurred. The speaker remembers knowingly making that claim; recipients do not receive the hidden label saying it was a lie.

After negative intent and disclosure pass:

```
distortionPressure = 0.40*vengefulness + 0.30*hurtFeeling
                     + 0.20*pride + 0.10*impulsiveness
P_distort_given_negative = pct((100-honesty)*distortionPressure/100, 85)
```

Failure preserves an accurate account, although its selection and delivery can still be cruel. Success selects a distortion severity using this score and table:

```
Z = pct(0.60*(100-honesty) + 0.25*vengefulness + 0.15*hurtFeeling, 100)
```

| Z | Loaded exaggeration | False escalation | Invented consequential detail |
|---|---:|---:|---:|
| 0–39 | 80% | 20% | 0% |
| 40–69 | 45% | 45% | 10% |
| 70–100 | 20% | 45% | 35% |

These percentages are conditional on the distortion roll succeeding. The severity draw is categorical and totals 100%; it is not three additional success rolls.

| Actual encounter | Possible chosen claim |
|---|---|
| Shared a pleasant conversation | “They could hardly bear to let me leave.” — loaded exaggeration |
| Shared wine without flirting | “They were making advances to me.” — false escalation |
| Spent some time talking privately | “We spent the night together.” — invented consequential detail |

The highest tier explicitly permits turning an encounter into something it was not, including a falsely claimed romantic encounter or insult. It must remain connected to the same encounter, people, and approximate time. Wholly unrelated rumor creation is outside v1. A false story never executes an action, establishes consent, creates intimacy or pregnancy state, grants an agreement, or becomes verified world history.

Select permission and severity in code before composing the outgoing story. The language model writes within that selection; an ordinary unapproved model hallucination must not be accepted as “exaggeration.” A model that cannot stay inside the brief leaves the item unsent with diagnostic evidence.

### B. Credibility uses only what the recipient can know

Arrival always records that the recipient heard the message. Credibility is a deterministic belief score, not a second delivery roll or a declaration of truth. Questions remain questions and get no factual belief score.

For an assertion that the recipient cannot independently resolve:

```
C = clamp-and-round to 5..95 of:
    40 + 0.25*R(recipient,currentSpeaker) + 0.20*(recipient.socialTrust-50)
    + accountBonus - 10*completedRetellings
    + 15 if independently corroborated
    - 25 if the recipient has evidence this speaker previously deceived them
    - 20 if inconsistent with facts the recipient already knows
```

`accountBonus` is +10 for a claimed firsthand account, 0 for attributed secondhand testimony, and -15 for an explicitly speculative interpretation. Claimed firsthand means what the speaker presents, including a liar pretending to have firsthand experience. Never read the speaker's actual honesty, hidden distortion flag, or inaccessible source transcript to detect the lie for the recipient. Corroboration is capped at one bonus and requires an independently accessible source root; repeated retellings are not independent evidence.

Conclusive firsthand disproof sets C=0; independent conclusive confirmation sets C=100. Lesser contradictions use the -20 term and remain disputed. Scores 0–24 are doubtful, 25–59 uncertain, 60–84 plausible, and 85–100 strongly believed. Even a strongly believed allegation remains an allegation in storage and generated dialogue.

Generate at most one brief NPC reply per delivered message. A direct question receives an answer, refusal, evasion, or acknowledgement based on the recipient's own knowledge and discretion; no additional random “heard it” roll is needed. Reuse the disclosure calculation for substantive reply facts with the roles reversed and the recipient's current feelings; no automatic reciprocal sharing of the original player's private transcript. Replies cannot start an unlimited dialogue loop.

Friendly gossip can inadvertently cause hurt. Compute that reaction from the recipient's own established feelings and concerns, including feelings the original speaker does not know. Do not invent romantic attachment because the recipient has a high jealousy trait. Hearing gossip changes native/player/NPC relationship scores by exactly 0. Later real conversations use the existing current-conduct relationship rules and do not automatically charge an old allegation as a fresh offense.

### C. Chance to bring it up with the player

At the first suitable player conversation after delivery:

```
P_raise = pct(10 + 0.30*recipient.curiosity + 0.25*personalStake
              + 0.20*uncertainty + 0.15*recipientFeeling, 80)
```

Personal stake is 0 for unrelated hearsay, 25 for ordinary news about a known person, 50 for a close friend/family matter, 75 for an existing attachment/status/concern directly affected, or 100 for a direct major personal consequence. Use the evidence/level gate above for recipientFeeling; missing evidence means 0. Uncertainty is `100-C` for an assertion, 75 for a still-open factual question, and 0 for an answered question or a purely subjective good/bad impression. A resolved concern does not roll.

Use one persisted roll per received topic, not per player turn. New material evidence may update its threshold against that roll; identical repetition cannot. Defer during higher-priority active business without consuming the opportunity. On success, nominate one natural conversational opening, not an obligatory first line. Mark it raised only after an accepted reply actually mentions it. At most one unsolicited gossip topic is raised per player conversation. The agenda expires after 14 game days; the history remains. When the player explicitly asks about known gossip, answer normally without this spontaneity roll.

## 4. Timing, retelling, and examples

### Delivery and limits

Queue during the player conversation, using accepted source evidence and a full authoritative native settlement/party snapshot. Campaign time, not elapsed real time, advances delivery. Ordinary local exchanges arrive after `1/24` day. Capture co-presence when queued: departure during that hour does not cancel the exchange or change its delay to correspondence. Wording may describe the exchange as taking place while parting. Only a validated mentioned recipient elsewhere uses correspondence routing:

| Route | Delay in campaign days |
|---|---|
| Same kingdom, native distance d available | `min(3, 1 + d/80)` |
| Different kingdoms, distance d available | `min(7, 3 + d/55)` |
| Distance unavailable | 2 in the same kingdom; 5 otherwise |

Use native map distance, not an LLM estimate. Unknown kingdom relationship uses the slower route. Resolve and freeze the route at dispatch; later target movement does not reroll or repeatedly reset the ETA. Poll on native hourly updates and flush due work before an affected character's prompt is assembled. Do not backdate a late processed message as knowledge the recipient already used. Same-location replies belong to that brief exchange, including parting exchanges; remote replies receive their own frozen route and return delay before the source learns them. Neither the destination nor a third-party retrieval path can see incoming content before its actual acquisition time.

For Ira/Rhagaea: a nonnegative mother/daughter relationship qualifies, and an explicit Rhagaea/“my mother” reference wins the named priority. A question about whether or why Rhagaea spoke to the player travels when she is elsewhere. Personal details still need discretion; Ira learns the answer only after its return journey.

Retelling uses the holder's own traits and feelings:

```
P_retell = pct(0.40 * P_share(holder) * (0.25 + 0.75*C/100), 34)
```

This replaces the ordinary P_share roll for that received topic; do not multiply by another sharing roll. Other intent/discretion/distortion decisions still apply. At C<25, a nonmalicious retelling can only be an explicitly doubtful inquiry and requires curiosity >=60. At C<60 any nonmalicious assertion remains explicitly uncertain. A successful malicious decision may knowingly repeat or further exaggerate a doubtful claim; record that choice rather than raising its credibility. A question-only item may be relayed only as an inquiry, with C=50 for this formula, never converted into an affirmative claim.

Limits are two outward hops (original recipient, then one new recipient), one onward recipient per received story, two distinct original items per speaker/player conversation, and three nonreply outbound items per speaker in a rolling 24 campaign hours. Caps include reserved composing/queued items and are persisted; cancellations or retries cannot farm extra rolls. Original and retold items expire for dispatch/delivery at 14 days from the root's creation. Replies already requested can complete afterward within their normal journey; old history is never deleted by propagation expiry. No loop to an earlier holder, no new root for a reply, and no duplicate perception of the same event through multiple copies.

### Worked tuning examples

These are illustrative synthetic profiles, not claims about Ira's or Rhagaea's actual personality.

| Trait | Ordinary | Discreet | Excitable | Spiteful, honest | Spiteful, dishonest |
|---|---:|---:|---:|---:|---:|
| Sociability | 50 | 30 | 85 | 75 | 75 |
| Impulsiveness | 50 | 20 | 80 | 75 | 75 |
| Pride | 50 | 35 | 65 | 85 | 85 |
| Curiosity | 50 | 60 | 70 | 65 | 65 |
| Honesty | 50 | 90 | 65 | 90 | 15 |
| Vengefulness | 50 | 15 | 30 | 90 | 90 |
| Compassion | 50 | 85 | 65 | 15 | 15 |
| Loyalty | 50 | 90 | 50 | 25 | 25 |
| Tact | 50 | 90 | 25 | 40 | 40 |
| Discipline | 50 | 85 | 30 | 45 | 45 |
| Empathy | 50 | 85 | 50 | 20 | 20 |
| Shame | 50 | 60 | 25 | 20 | 20 |

Use E=75, friendly-recipient affinity=60, and confidence-owner affinity=60. Disclosure examples are nonmalicious, concern another person's information, and the secret includes a promise. Negative examples instead have D=60, hurtFeeling=75, victim affinity=-60, and no activated jealousy/envy. Each row below labels its conditional denominator.

| Calculated percentage | Ordinary | Discreet | Excitable | Spiteful, honest | Spiteful, dishonest |
|---|---:|---:|---:|---:|---:|
| Start sharing, per eligible topic | 44% | 34% | 62% | 60% | 60% |
| Personal detail, after sharing intent | 64% | 34% | 78% | 79% | 79% |
| Intimate detail, after sharing intent | 39% | 9% | 53% | 54% | 54% |
| Promised secret, after sharing intent | 0% | 0% | 8% | 2% | 25% |
| Negative intent, after sharing intent | 36% | 19% | 29% | 57% | 57% |
| Distortion, after negative intent and disclosure | 29% | 4% | 19% | 8% | 71% |
| Invented consequential detail, after distortion | 10% | 0% | 10% | 10% | 35% |

For the excitable profile, personal-detail sharing is `62% * 78% = 48.36%` per eligible topic before caps; a promised secret is `62% * 8% = 4.96%`. For a public real encounter with a supported hostile opportunity, the dishonest spiteful profile invents a consequential detail with `60% * 57% * 71% * 35% = 8.4987%`, approximately 8.5% per eligible topic. The otherwise identical honest profile is `60% * 57% * 8% * 10% = 0.2736%`. Nonpublic evidence adds the discretion factor, and capacity/source checks may reduce either result. These are products of conditional probabilities, not chances on every turn.

A recipient with affinity +60 toward the source and socialTrust=60 gives a claimed firsthand account C=67 when no other evidence modifiers apply. Curiosity=70, stake=75, and recipientFeeling=75 then yield P_raise=68%. An excitable new holder with P_share=62 and C=67 has P_retell=19%, before discretion and limits.

## 5. Implementation and acceptance

### Runtime boundaries

- Extend ordinary accepted dialogue's structured contract with optional evidence-backed `gossipSignals`: canonical topic/source IDs, feelings, intended harm subject if any, privacy/confidence ownership, and supported possible recipients. The model supplies no final chance or hidden knowledge. Additive fields absent from an older response mean no signal; invalid gossip data does not reject a valid player reply.
- Calculate chances, freeze rolls/recipient choices, and reserve capacity in the server. Persist the intent immediately; compose only a selected outgoing message asynchronously through the configured dialogue provider. This adds no separate assessment call to every player turn. Each delivered item permits one outgoing composition and one short reply, each 1–3 sentences with a 100-word ceiling. Failed jobs retry at most three total attempts, reuse their original decisions, and never block the player's dialogue.
- Implement the coordinator and deterministic functions in a new server Dialogue partial module with isolated self-tests. Integrate accepted individual, party, and social-event exchanges, canonical relationship receipts, identity knowledge, and background job/quiescence reporting. Official-memory firewalls and existing recipient privacy boundaries still apply.
- Add a bounded native sync/tick API for the full living identity roster, authoritative campaign time, settlement/party presence and positions, plus read-only status/audit inspection. Partial conversation rosters and army social groups are insufficient. Register a native hourly adapter; drain currently due/in-flight work at guarded checkpoints, not future in-transit items. Dead or invalid recipients cancel undelivered work; temporary service/provider failure defers it within expiry rather than inventing a placeholder history entry.
- Store `npc_gossip` observer histories with speaker/recipient, campaign/timeline, source provenance, intent, claimed content, actual acquisition day, hop count, and belief state. Keep truth/distortion/roll diagnostics inaccessible to recipients unless they have independent evidence. Remote return content has its own recipient receipt. Being the discussed subject or player is not transcript participation.
- Include a bounded recent gossip section in both memory modes and final prompt coverage diagnostics. Preserve uncertainty in consolidation and belief retrieval; support recalling NPC-only exchanges without mixing them into “what the player told me.” Expanding source links must not expose the originating private transcript or an undelivered reply.
- Retain actual source evidence, told versions, corrections, queues, stage draws, cooldown/cap reservations, delivery receipts, and reply jobs across Save Sync. Timeline restore invalidates stale generation results. Delivery/history writes are atomic and idempotent.
- Add versioned `personalGossip` tuning to existing settings, initialized to this document's defaults, with a master disable switch. Freeze policy version and decision inputs per queued story. Existing campaigns begin with empty queues; no historical replay. Do not resurrect removed `rumor_acquisition` tables or merge this feature into automatic public reputation scoring. No new player-facing UI is required.

### Proof required before feature completion

1. Formula fixtures reproduce every table value, band boundary, 0/100 roll endpoint, and conditional probability. Increasing sociability raises sharing; increasing honesty lowers distortion; stronger bonds/loyalty protect confidences; activated jealousy cannot read a recipient's hidden feelings. Verify all values on boundary grids and seeded population samples.
2. Same-topic paraphrases, reopened sessions, 100 repeated evaluations, provider retry, restart and Save Sync reuse decisions. Probability never accumulates from repeated identical turns; new evidence may cross an existing threshold. Caps and canceled jobs cannot be used to reroll.
3. Exercise an honest cruel disclosure, an excited indiscretion, private consultation, positive gossip causing unexpected hurt, both impression signs, zero/capped/mixed delta, explicit confidence, and a secret encounter whose existence must not leak through hints.
4. Produce all three malicious distortion tiers through injected responders, including a false overnight claim from a real ordinary encounter. Verify the source record is unchanged, the author knows what they invented, the recipient lacks that hidden label, and no intimacy/consent/action/reputation mechanics fire.
5. Verify firsthand contradiction, attributed speculation, deceptive firsthand presentation, same-root retellings not becoming corroboration, and recipient-only feelings. A question about Rhagaea remains a question; her reply uses her own memories; Ira receives it only at the proper return time.
6. Before/after one local hour, distant and unknown-location routes, late processing, death, expiry, return delivery, retell limits, queue cancellation, worker failure, save/reload and rollback all have observable histories and no premature knowledge through any retrieval path.
7. Reserve final-prompt coverage for the selected recent gossip under pressure, with full source units/corrections and correct speakers. Prove ordinary current conversation still has priority and the player’s factual correction is not automatically treated as an offense.
8. Obtain a fresh exact-path validation plan before implementation. The previously inspected prospective client/server/persistence/testing paths selected `changed`, Tier 3; the final path set remains authoritative. Update the testing catalog, dialogue procedure, API help, and affected contracts. Run injected provider-free Verification Lab proof first; native/model prose acceptance requires the existing separately authorized provider and guarded disposable-campaign routes. Do not mark the roadmap complete from a plan or offline compilation.
9. Verify +29/+30 friendship, -1/0 family and -9/-10 dislike boundaries; naming never bypasses them. Verify local settlement/party versus army-only, distant unnamed versus named contact, no-audience/no-arrival retry, all named-weight examples and candidate-cap tie ordering. Departure retains the local hour and history/replies remain inaccessible before acquisition. Retelling carries only validated root mentions, with frozen route/presence/policy and zero automatic relationship effects.

### Implementation checkpoint — 2026-09-26

The `PersonalGossip*.cs` Dialogue modules implement policy, persisted decisions/jobs/observer receipts, bounded generation, reply knowledge, retelling, prompt history and provider-free fixtures. `ReignPersonalGossipClient.cs` captures up to 8192 living identities; incomplete rosters cannot choose an audience. Accepted individual/group exchanges and applied relationship receipts trigger decisions; native hourly ticks only deliver existing jobs. Rollback checks both persisted restore generation and process generation. API, isolation and proof procedures are in `testing/DIALOGUE_AND_PROVIDERS.md` and `reign.testing.json`. Compilation and provider-free execution are pending the coordinated shared validation; native/provider acceptance remains open.

Impressions use a deterministic detail-free sentence after discretion passes. Replies without supported facts use bounded acknowledgements/refusals; actual provider failure creates no placeholder. Substantive reply facts are bounded to the recipient's recent 60 accessible nongossip turns (whole records within 9000 characters); absence of older evidence defers a factual answer. Free-form paraphrase and semantic evidence classification still require native/provider acceptance. These conservative bounds do not permit access to the originating private conversation.

### Source anchors checked for this design

- `ReignServer/src/Modules/Characters/CharacterTraits.cs`: existing 43 trait names and meanings.
- `ReignServer/src/Modules/Characters/TraitPercentages.cs`: 0..100 bands and distinct calibrated Court virtues.
- `ReignServer/src/Modules/Relationships/Relationships.cs`: trait-read fallback and existing correspondence lifecycle.
- `ReignServer/src/Modules/Relationships/MbtiRelationships.cs`: directional friendship/enmity thresholds.
- `ReignServer/src/Modules/Relationships/ConversationRelationships.cs`: canonical per-exchange final deltas and directional authority.
- `ReignBeta/src/Modules/Dialogue/Integration/ReignCorrespondenceClient.cs`: existing distance-based route equations; this feature deliberately overrides its local six-hour default to the selected one hour.
- `ReignServer/src/Modules/Dialogue/CategorizedMemory.cs`, `MemoryContextV2.cs`, and `MemoryRouting.cs`: history storage, observer access, and the current player-source filtering that NPC-only gossip must handle explicitly.

The initial source inspection found client HEAD `8c31c4d2b8cdafc32d60ea325ac44e99b74b4046` and server HEAD `3c6c08b5872231b73322e9f4c1a96897943c6a35`, both on `codex/publish-recent-reign-work` with substantial existing local changes. Implementation preserves unrelated work; shared dialogue/memory edits and one combined validation are coordinated with the active same-project tasks.
