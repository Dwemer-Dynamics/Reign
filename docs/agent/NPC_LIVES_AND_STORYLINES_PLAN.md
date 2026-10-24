# NPC Lives and Storylines consolidated plan

Status: approved for implementation and validation, 2026-10-11. Deployment is held until the user stops the game and server and confirms readiness.

Give Reign characters daily activities, interests, personal goals, lasting experiences and enforceable commitments. Deliver one connected system covering Approach, conversation resumption, personal stories, temporary leave, party announcements and summoned meetings.

The AI supplies characterization, dialogue and bounded interpretation. Saved game rules determine eligibility, audience, agreements, timers, native effects and rewards. This is a design specification; the features are not complete merely because this plan is recorded.

## 1. Shared rules for reliable behavior

- Extend the current explicit action system. NPC story requests produce real NPC proposals that the player can accept, decline or counter through the existing action controls.
- The story engine creates a permitted proposal from the saved story phase before the opening is presented. A missing model action field must not erase the offer. Mechanical execution never depends on recognizing an offer in ordinary prose.
- Bind consent to exact participants, proposal identity, revision and terms. Changing terms creates a revision requiring fresh agreement. An unrelated yes, quoted agreement, hypothetical statement, silence or closing the window does not authorize an action.
- Use the existing agreement journal and native adapters for scheduled execution and observed fulfillment. Commit once, deduplicate retries and retain receipts across save/load. Accepted agreements are not rerolled by later dialogue.
- Keep three kinds of evidence distinct: what someone said, what a story permits as narrative fact, and what the native game actually did. Announcing a march does not move the party; narrating a victory does not win a duel.
- Preserve established scene and audience boundaries. Extend the current conversation continuation, grounding and observer systems; do not introduce a competing conversation cache.
- Refusal, delay and inability are different outcomes. Mechanical uncertainty leaves the request pending or visibly blocked rather than inventing success.

## 2. Approach and conversation resumption

### Fresh approaches

Rename ordinary fresh in-person Reign Talk controls to Approach. Apply the shared policy across individual, party and applicable court/event entry points. Letters, an explicit meeting continuation and an already-delivered NPC invitation retain their own context.

A short fast-model call establishes what the NPC is doing and how receptive they are, using their complete base personality summary, authoritative relationship, location, time, duties, current life facts and relevant recent events.

Possible openings include working, resting, studying, tending an animal, repairing something, enjoying company, preparing for an obligation, worrying about a private matter or wanting assistance. The player can talk briefly, help, interrupt, insist or leave. Busy behavior must not prevent access to essential native services.

Use a separate configurable model route, initially inheriting party-banter settings. **2026-10-12 user revision:** remove the original two-second target/2.5-second fallback cutoff; normal providers commonly take longer. Await the normal provider response asynchronously without an Approach-specific deadline. Old saved deadlines cannot replace a still-running reply. Keep following the same saved operation while chat is open; closing stops UI polling and reopening recovers its result without another model request. Normal provider-error and interrupted-worker recovery still apply. This revision affects only the added Approach feature; existing wilderness/social/scripted openings retain their own handling.

Cache the activity episode. Opening and closing the UI cannot reroll mood, receptiveness, available requests or activity. Material native changes update context without fabricating a new conversation.

### The one-hour rule

After the initial NPC response or any later completed exchange, retain the last accepted campaign timestamp for that encounter.

- Reopening before one full in-game hour has elapsed restores the transcript and waits for the player, with no automatic NPC response and no new opening LLM call.
- At or after one full hour, the next ordinary approach may generate exactly one fresh opening based on current circumstances.
- Use elapsed campaign time, not wall-clock time, a clock-hour boundary, UI lifetime or a timestamp refreshed by merely opening the screen.
- Preserve this behavior when switching NPCs, saving/loading, reconnecting or moving between supported chat surfaces. Keep private/public contexts and participant sets separate.
- Reconnect to an existing unfinished reply or its saved result. Do not start a duplicate request because the window reopened.
- A fresh opening refreshes the encounter framing. It does not clear story progress, pending proposals, pets, past announcements, promises or memories.

Genuine story/event transitions have their own saved scene identity and one authored opening. They must not also trigger an ordinary greeting. Native location changes update the scene facts even when reopening remains silent.

## 3. Personal invitations and story progression

### Eligibility and pacing

All persistent characters can have life state and Approach behavior. Spontaneous personal-story invitations are limited to NPCs actually accompanying the player in the main party, including eligible temporary guests. Being in the same clan, army, town or keep is insufficient.

Use the jealousy system's neutral invitation popup so the player does not know the purpose before opening it. Recheck presence and safety before presentation. Present one popup at a time without adding a global daily invitation cap.

Roll once per eligible campaign day after a minimum seven days between new invitations from the same NPC. Save the roll, selected scenario and cooldown so reloads cannot reroll them. Use authoritative NPC-to-player affinity and these editable Web UI anchors, interpolated between values:

| Affinity | Daily chance after cooldown |
| ---: | ---: |
| -100 | 0% |
| -80 | 0.05% |
| -60 | 0.10% |
| -40 | 0.20% |
| -20 | 0.30% |
| 0 | 0.50% |
| 20 | 1% |
| 40 | 5% |
| 60 | 12% |
| 80 | 25% |
| 100 | 40% |

Below 60, select brief moments and personal-leave requests. At 60+, add outings and accompanied visits. At 80+, add major arcs. Brief scenes at low affinity may foreshadow a later arc without prematurely activating its higher-trust chapters.

At high affinity, default weights are 80% brief moments, 15% outings/personal leave and 5% major arcs. Redistribute unavailable categories among eligible content. Allow one active major arc per NPC. Save a 15-31.5-day cooldown between new major starts; subsequent chapters use authored prerequisites rather than repeating that start cooldown. A Reign season is 31.5 campaign days and a year is 126.

Affinity unlocks willingness to share. Separate objectives establish progress. Losing affinity may suspend an unreached chapter but does not erase completed experiences.

### Authored scenes with flexible roleplay

Each versioned definition specifies personality/culture/skill affinities, prerequisites, participant and destination bindings, private facts, phase prompts, permitted branches, required evidence, rewards and enduring outcomes.

Most brief stories take two to six substantive exchanges. Progress depends on meaningful participation and authored outcomes, including thoughtful disagreement, rather than exact keywords, a prescribed answer or message count alone.

Use a small reusable objective vocabulary: explore a subject, offer support, reach an agreement, visit a bound location, participate in an activity, deliver a verified item, complete a native duel/mission and reflect on the outcome. Conversation objectives use bounded assessment with evidence from accepted turns; ambiguous evidence grants no progress. Native objectives require native receipts.

Protect the current objective and constraints in every scene prompt. Score story participation separately from affinity; avoid farming progress by repeating the same argument or repeatedly reopening the scene.

Every directed phase exposes a Story Location in its bound town, village or castle once revealed. Reuse the wilderness event presentation under Story Encounter, with independent story progression and completion rules. Camp moments use the current setting. Resolve named places and people against actual available world entities; newly authored supporting characters need stable identities through existing character-generation paths.

Use native private practice duels for training and a dedicated story adapter for bandit-hideout missions. Bind the correct NPC, participants, objective and mission outcome; apply cleanup and rewards only from the verified result.

### Content volume and variety

The initial catalogue contains 240 distinct authored scenarios: 200 brief moments, 28 outings/personal-leave scenarios and 12 major arcs. Destination substitutions, renamed objects and cultural wording do not count as new scenarios.

The thirty content families below provide a broad pool of directions:

| Family | Example scenarios |
| --- | --- |
| Camp cooking | Salvage a stew; recreate a breakfast from home; invent a travelling recipe |
| Food curiosity | Seek regional bread; identify an unfamiliar spice; admit disliking a prized delicacy |
| Stars | Learn navigation; compare childhood sky legends; name a shared constellation |
| Music | Finish an unfinished verse; practise rhythm; remember a lullaby |
| Dancing | Rehearse privately; work through embarrassment; learn another culture's steps |
| Games | Teach a childhood game; solve a tactical puzzle; arrange a friendly contest |
| Humor | Exchange terrible jokes; tell an embarrassing story; untangle a misunderstood proverb |
| Reading | Discuss a troubling passage; read aloud; choose a first book |
| Writing | Compose an apology; help with an awkward letter; share a first poem |
| Languages | Practise greetings; explain an untranslatable phrase; repair an accidental insult |
| Drawing | Sketch camp life; attempt a portrait; compare observations of a landscape |
| Crafts | Carve a charm; braid a cord; repair a cherished object |
| Clothing | Mend a damaged garment; choose festival clothing; explain an inherited accessory |
| Animals | Name a pet; settle a nervous horse; discover an unexpected animal interest |
| Riding | Correct a riding habit; recall a favourite mount; try a friendly riding challenge |
| Weapons | Discuss balance; maintain a blade; identify a weak technique |
| Physical confidence | Practise balance; learn to throw; face a difficult climb |
| Care and medicine | Learn bandaging; discuss fear of treatment; persuade someone to rest |
| Scouting | Read the weather; identify harmless tracks; remember a useful landmark |
| Nature | Recognize birds; gather flowers; share a favourite landscape |
| Local curiosities | Visit an unusual bridge; investigate a custom; seek an overlooked place |
| Beliefs | Discuss a superstition; observe a small ritual; prepare for a festival |
| Family | Plan a reunion; select a family gift; discuss sibling comparisons |
| Homesickness | Recreate a household tradition; revisit a familiar street; mark an anniversary |
| Remembrance | Explain a keepsake; tell a story of the deceased; visit a meaningful grave |
| Friendship | Prepare a surprise; settle a petty quarrel; express overdue gratitude |
| Vulnerability | Admit failure; retract an exaggerated boast; learn to accept help |
| Ambitions | Dream of a workshop; learn an instrument; consider teaching others |
| Everyday ethics | Return lost property; discuss an unfair bargain; respond to a humiliating joke |
| Reconciliation | Offer an overdue apology; resume an abandoned interest; seek an estranged friend |

The 28 outing/leave templates cover pet adoption, a seed market, a bookbinder, an instrument maker, a cultural dance, a storytelling circle, a poetry gathering, a harvest feast, a smithing demonstration, a pottery firing, a dye workshop, a mill visit, a board-game hall, an archery meet, a horse fair, a wrestling exhibition, a stargazing hill, a coastal walk, a spring pilgrimage, an orchard harvest, a family visit, a mentor visit, a graveside remembrance, a reconciliation visit, helping a healer, a communal kitchen, caring for a foal and repairing a local shrine.

The 12 major arcs cover a missing childhood friend, unfinished apprenticeship, family divide, lost heirloom, disgraced mentor, promised performance, workshop ambition, a fallen companion's oath, disputed inheritance, unfinished combat training, a feared return to a meaningful place and a friend following a troubling path. Each needs distinct stakes, progression and consequences; they must not all resolve as bandit rescues.

Prefer unused scenarios and underrepresented experiences across the campaign. Track template, motive, activity, complication and resolution. Exclude the party's last 20 completed templates and the initiating NPC's last three activity families, and do not repeat a completed nonrepeatable scenario for that NPC. Major templates do not repeat within one campaign. Author-marked repeatable hobbies require a new development and at least one season since that NPC's previous instance. An exhausted compatible pool yields no invitation instead of relaxing these exclusions.

## 4. Personal leave and independent lives

### Trust and privacy

At 0-20 affinity, the NPC may keep the purpose and destination private. At 21-39, provide limited context. At 40+, explained family visits become a normal possibility. At 60+, they may invite the player; declining to accompany them does not automatically prevent them from travelling alone.

Ordinary personal absences default to three to seven campaign days. Modest independent hobbies and local experiences are also allowed. These can create personal memories and interests without requiring the player to arrange every development.

### A focused negotiation

Create a saved request with its private purpose, urgency, destination, proposed absence, negotiable limits, known disclosures, current revision, objections, accepted arguments and execution receipts.

Every relevant prompt retains the unresolved purpose. Tangents cannot transform a family visit into permanent service, complete a different quest or erase the desire to leave. The NPC can acknowledge a tangent and return naturally to the issue; prolonged diversion suspends the unresolved request.

| Outcome | Mechanical behavior |
| --- | --- |
| Continue discussing | Preserve the request and current terms |
| Agree to leave now | Depart at the next valid native opportunity |
| Agree to wait a specified time | Save departure time and depart automatically when due |
| Persuaded to stay for now | Retain the desire and schedule reconsideration |
| Permission refused and NPC stays | Apply the resolved consequence once and schedule reconsideration |
| Permission refused and NPC leaves anyway | Record an independent NPC decision, never player consent |
| Purpose becomes resolved or impossible | Follow an evidence-backed authored closure branch |

Persuasion uses reasons, personality, urgency, duties, trust and existing objections. Use the existing normalized trait helpers. It is not repeated Charm dice rolling. Rephrasing the same argument adds no chance; a relevant new fact can change the assessment. A respectful explanation or apology may reduce relation loss without changing the decision.

Interpret candidate concessions with exact dialogue evidence, validate the permitted outcome, then generate a matching response. A contradictory response gets one bounded repair; continued failure leaves state unchanged with a clarification. No prose-only departure, invented consent or unvalidated replacement goal.

### Separate clocks and native lifecycle

Keep departure time, absence duration and reconsideration time separate. “Wait two days, then go for five” means departure in two days and return five days after actual departure. An agreed wait always ends in automatic departure when native conditions allow it; no second permission request.

A successful stay-for-now negotiation defaults to reconsideration in seven campaign days, or the story's explicitly authored shorter interval. This reopens the same request and does not depend on winning another random story roll. Retain previous promises and concessions.

Revisions invalidate superseded pending timers. Battles can delay safe departure without shortening the visit or penalizing the player. Preserve identity, clan, equipment and valid service agreements. Temporary personal leave must not call the action that terminates temporary guest service.

Return the same NPC through the appropriate native lifecycle. Handle capture, death, contract expiry and unsafe return explicitly; never resurrect or re-recruit a replacement. A completed journey creates NPC-owned experience records. Private details reach the player only when the NPC chooses to share them.

## 5. Lasting character changes and rewards

Maintain protected current-life facts for pets, keepsakes, interests, ambitions, commitments and completed story outcomes. A pet rabbit has a persistent identity and a current companionship fact supplied directly to its owner's relevant prompts, including later locations. It is a narrative companion; a 3D pet or inventory item is not required.

Keep current-life facts separate from relevance-filtered memory and the existing dynamic biography categories. Preserve compact active facts in every applicable prompt; retrieve detailed history when relevant. Do not leak another NPC's private history into shared scenes.

Use target-specific NPC skill awards and authoritative affinity changes, each tied to one completion receipt:

| Accomplishment | Default NPC skill XP | Affinity gain |
| --- | ---: | ---: |
| Meaningful discussion | 25 | +1 |
| Shared practice | 100 | +2 |
| Outing or training duel | 400 | +5 |
| Substantial arc milestone | 1,000 | +10 |

Map XP to actual native skills appropriate to the activity and respect native learning modifiers. Sword training can award its weapon skill; dancing can award Charm or Athletics; medical assistance can award Medicine. Authors choose the exact skill split. Independent accomplishments do not award the player credit for participation that did not occur.

Starting explicit-refusal losses are -2 for a small request, -5 for an outing, -8 for a major request or denied leave, and -12 for an important broken promise. Contextual persuasion can reduce or eliminate the applicable loss. Closing a chat, postponing through an accepted agreement, declining an undisclosed popup or encountering an impossible native situation is not automatically a refusal.

Track repeated deferrals and broken commitments, but never apply the same penalty twice. Reconcile story relationship effects with ordinary conversation evaluation to avoid charging or rewarding the same evidence twice.

## 6. Party announcements

Place **Announce** immediately to the right of **Send** in the party chat composer, available in the field and settlements. Both buttons use matching artwork and typography; shorten the input field to make room. Clicking Announce sends the current draft directly to actual party members, regardless of selected speakers or other people at the location. Clear the draft after queuing and retain the open chat and its selection. Record the exact player announcement without generating NPC replies, acknowledgments or a round of party chatter. This supersedes the separate announcement-mode design (user clarification, 2026-10-11).

### Exact audience

Resolve and freeze the audience from the actual main-party member roster at submission. Include named companions, family and temporary guests travelling with the player. Exclude prisoners, absent members, detached clan parties, other army parties, town residents, notables and keep visitors. Anonymous soldiers can be part of the addressed crowd without inventing persistent individual character records.

Validate the roster again at commit. Record campaign/timeline, announcement ID, exact source text, speaker, time, location and recipient IDs. Add one player-origin history event visible to those hearers; a location-wide conversation query must not broaden it. Later recruits do not retroactively hear it, while former members retain a memory of something they actually heard.

The announcement's history and delayed reaction prompts stay restricted to the recorded audience. No automatic settlement, world-news or generic observer fanout is allowed. Any later actual retelling must be a separate evidenced communication with its own recipients.

### Reactions later

Create a pending review reference for each named hearer. At their next eligible approached conversation, supply every announcement they heard and have not yet considered since their previous conversation. Let their personality, interests, relationship and current activity determine whether they express approval, concern, disagreement, curiosity or practical advice, combine several subjects, comment on only some, or say nothing about the announcements and continue their day. No acknowledgment is compulsory. Use the next normal generation rather than one provider call per NPC when the announcement is sent. These all-announcement and optional-silence requirements were clarified by the user on 2026-10-11.

Respect silent resumption: reopening inside the one-hour window does not force a reaction. If the player then speaks, the NPC can address the announcement in that next reply; otherwise it can appear in the next fresh approach. Busy or urgent scene requirements remain relevant.

Supply the entire pending audience-scoped batch; a recent-memory limit must never discard an unconsidered announcement. A compact review receipt names the last announcement in the supplied batch and records `commented` (with an exact quote from the accepted visible reply) or a deliberate `no_comment`. Review covers all supplied subjects without requiring speech about each. Commit only with an accepted nonempty character response or activity narration; a timeout fallback, empty/unpublished reply, invalid quote, foreign audience or stale identity cannot consume the batch. Announcements arriving during generation stay pending beyond that frozen boundary. Persist each hearer's independent choice and accepted source so retry, immediate reopening and reload do not demand the same review again. Older announcements remain available as ordinary audience-scoped memories.

An announcement such as “We march for Pravend tomorrow” remains something the player said. It does not create movement orders, attendance agreements, completed events or compulsory NPC agreement. Sending grants no automatic affinity or XP. Any later conversation consequence follows the normal evidence-based relationship rules.

## 7. Summoning a subordinate to a meeting

Add a Summon for a meeting action to the existing action sentence builder. The player asks a clan leader or ruler to summon a named person under their current authority to the agreed location.

Example: “I would like you to summon Aldric here, remain until he arrives, and for both of you to stay three days afterward.”

### Authority and exact terms

The ruler may summon eligible nobles and mercenary personnel currently under that kingdom's authority. A clan leader may summon eligible members or personnel actually under their clan's authority. Shared location, friendship or belonging to another clan in the same kingdom does not give a clan leader command over that person.

Resolve the issuer, summoned hero, command authority, fixed destination, purpose and additional stay duration before agreement. Default the additional stay to three campaign days, editable to a positive exact duration in the action sentence. Both the issuer and summoned NPC remain for this agreed period, as confirmed by the user.

Bind the meeting place when the agreement is accepted. Support a town, village or castle; a field meeting uses a fixed valid campaign rendezvous point. It does not chase the player if they move away. The player remains free to leave or return.

Check capture, death, age, availability, passage, army command and conflicting commitments. Do not silently detach a subordinate army party, dissolve an army, end guest service, change a clan or create an unrelated permanent party. Required military release/detachment must be an explicit permitted term; otherwise explain the blocking duty or counteroffer.

### Fixed arrival schedule and waiting

2026-10-12 user revision supersedes the earlier courier and physical travel design: the leader quotes a fixed number of days when accepting the summons. Save `arrivalDays` in the signed action terms and `ArrivalDelayDays` / `ScheduledArrivalDay` in the durable meeting record. Estimate normal travel from current distance and the target party's current speed (four map units per hour for a partyless noble), then use 80% by default, rounded up to a hundredth of a campaign day. The Options slider `npcLivesSummonsTravelTimePercent` permits 10–100% in steps of five. An accepted date never changes when settings change. This is an estimate based on distance and speed, rather than a pathfinding prediction.

2026-10-12 later user revision supersedes the own-party relocation sequence below for **new agreements**: the summoned NPC attends alone while their party remains camped at its original position throughout the quoted wait and the entire meeting, then the NPC teleports back to it. Bind `attendanceMode=solo_camp` into the action terms before signatures and save `SoloAttendance`; missing historical values preserve the original accepted arrangement. On acceptance protect the existing owned camp using the temporary guest safety mechanism and detach the free NPC into native Traveling state. The fixed arrival date and full post-arrival stay remain unchanged. Block unsafe departure rather than pulling anyone from battle, captivity, sea travel or another protected duty. Do not reserve a passenger's other leader's party. Both participants stay for the full duration; after all compatible holds finish, return the NPC to the saved camp and restore its valid original flags/duties. Cancellation returns the free NPC too. Missing/changed parties use a safe friendly settlement fallback; capture/death never triggers release/resurrection. Existing accepted party-attendance meetings finish under their old terms. The user explicitly permits normal loot selling on arrival, so inventory sales are not themselves a failure and must never be silently undone. Native acceptance must cover protected wait/stay positions and contents, individual arrival, return, cancellation, revisions, reload and overlapping ownership. The next five numbered steps describe the earlier accepted implementation and remain as history.

1. On acceptance, reserve the issuer at the fixed meeting place and record the quoted arrival schedule. Do not dispatch the target along a dangerous map route or detach them before the date.
2. At or after the scheduled date, recheck both participants, current authority and the destination. Move the summoned noble and their own existing party to the location through native relocation APIs. Preserve party identity, troops, prisoners and inventory. A noble travelling as a passenger comes alone; never relocate another leader's party or the player party. Partyless nobles do not need a new permanent party.
3. Never move anyone out of a battle, sea journey or native transition. Wait until relocation is safe, without an arbitrary timeout. A prisoner, dead, disabled/fugitive/released or otherwise unavailable participant interrupts the meeting with a specific reason. Never free or reactivate an unavailable character. Unsafe/hostile destinations and lost authority also interrupt it.
4. Verify actual presence after relocation before marking arrival. Settlement meetings enter the actual town, village or castle; fixed field rendezvous use a safe land position. Start the full agreed stay at actual arrival, including when safe relocation was delayed. Both participants remain until that time expires.
5. Release only the ending meeting's holds and restore currently valid duties. Capture/death, cancellation or another interruption must not resurrect or free anyone, or release a different meeting's commitment. Native recruitment of a held partyless noble creates their new party at the reserved settlement and retains the hold.

If already present, the quoted wait is zero and verified arrival starts the stay immediately. Player movement never changes the fixed destination and does not cancel the bargain. Arrival and expiry notify the player without forcing a chat. Revisions require fresh accepted terms and a new exact arrival wait. New schedules skip the historical courier/travelling stages; existing arrived meetings can finish normally. Historical completed records stay intact. A saved unarrived meeting without an agreed scheduled date is explicitly interrupted and requires a new agreement; do not invent retroactive consent or an arrival date.

The earlier safeguard requirement remains: reject prisoners and unavailable participants before acceptance, execution, scheduled arrival and throughout the stay. Only an explicitly permitted army release may detach a subordinate's own party within the issuer's current authority. Battle/sea/transition delays remain possible and keep the issuer waiting. Deterministic checks plus disposable native proof must cover early arrival, exact schedule, reload, delayed arrival, party preservation, capture, cancellation, revision, simultaneous ownership and full stay/release.

Compatible simultaneous summons share hold ownership so completing one cannot release another's commitment. Reject or renegotiate conflicting destinations. Cancellation releases only this meeting's orders and records what happened. Restore duties only after checking they remain valid.

Reuse the existing action journal, campaign-order supervision, correspondence delivery and native hold policies. Extend them with arrival-linked dependencies and partyless attendance; existing timed-hold code alone does not yet implement this complete meeting contract. Keep the earlier roadmap requirement for informing and summoning-then-informing, linking this explicit meeting action as its concrete summons path.

## 8. Data, editing and interfaces

### Minimum shared contracts

| Contract | Purpose |
| --- | --- |
| StoryDefinition and StoryInstance | Versioned content, bindings, phase, objectives, permitted branches and earned outcomes |
| StoryAssessment and EffectReceipt | Evidence-backed progress, rewards and exactly-once native effects |
| NpcLifeState | Current activity, protected life facts, interests and private experiences |
| ConversationEncounter | Logical continuation, participants/context, opening receipt, last accepted campaign time and any pending response |
| PersonalLeaveRequest | Fixed purpose, disclosures, negotiation revision, independent decision and three distinct clocks |
| PartyAnnouncement and AnnouncementReaction | Exact player source, frozen recipients and per-hearer pending/delivered reaction evidence |
| SummonedMeeting | Issuer/target/authority, fixed destination, signed wait and scheduled date, actual arrival, holds, duration and recovery |

Expose bounded operations for Approach/resume, invitation selection, story entry and accepted turns, native acknowledgments, announcement submission/history, proposal review and meeting status/revision/cancellation. Personal-leave and meeting actions extend the existing action catalog and agreement interfaces; they must not create an alternative free-text action parser.

The native client supplies authoritative campaign time, roster, location, mission outcomes and movement execution. The server owns authored content, audience-scoped memories, prompts and assessment records. Carry campaign/timeline, operation identity and revisions through every boundary; accept downstream effects only against the matching record.

Include server records in Save Sync and native lifecycle/reward/hold records in campaign saves. Reconcile after interruption, reload and rewind. Pin active stories to their definition version. Existing saves receive empty new state and preserve their established conversations and commitments; do not fabricate historical announcements or completed stories.

### Authoring and settings

The Control Center provides feature toggles, the eleven affinity chance anchors, invitation/major cooldowns, category weights, Approach model settings, the one-hour resume interval, reward/refusal values and announcement/meeting options. The Approach timeout control is retired by the 2026-10-12 revision above.

Place the searchable personal story editor under **Diagnostics → NPC Lives and Story Editor**, with prompts, phase graph, character/culture affinities, prerequisites, secrets, locations, conversation/native objectives, negotiation limits, rewards, persistent outcomes, import/export and provider-free previews. Validate impossible branches, missing bindings, unreachable phases, duplicate rewards and unsupported native outcomes before enabling a definition. Edits create versions rather than rewriting active instances.

The player journal shows known personal matters, revealed Story Locations, clues, proposed/accepted terms, departure and return information, announcements and meeting progress. Private destinations, motives, assessments and hidden scoring remain private.

Use the approved Reign interface assets, tokens and typography. Reuse wilderness/story presentation and action controls; preserve bounded content and the full applicable preview/fidelity requirements. Turning off new stories stops new invitations while committed departures, returns, meeting holds and recovery continue safely. Disabling announcements prevents new sends without erasing already-heard history.

## 9. Implementation and acceptance

Build one integrated vertical slice before authoring the full catalogue: one brief moment, one pet outing, one progressive combat story, a private leave request with a timed delay, one party announcement and one summoned meeting.

Implementation order:

1. Shared records, explicit proposal creation, audience boundaries, save/load and failure recovery.
2. Approach and one-hour silent resumption, including interrupted opening tests.
3. Party announcements and delayed individual reactions.
4. Story engine, persistent life facts, NPC rewards and story editor.
5. Personal leave, native departure/return and focused persuasion.
6. Summoned meetings with a quoted fixed arrival schedule, native relocation and enforceable stays.
7. Native duel/hideout integration, all 240 definitions and variety simulations.
8. Full interfaces, integration regression, native/provider acceptance and final tuning.

Required proof:

| Area | Acceptance scenarios |
| --- | --- |
| Proposal reliability | An omitted model action field still leaves the real NPC offer available; unrelated agreement cannot authorize it; counters invalidate old revisions; duplicate requests execute once |
| Chat resumption | Immediate, 59-minute and 60-minute reopening; crossing a clock hour without 60 elapsed minutes; switching NPCs; save/load; pending reply; zero opening calls on silent resume |
| Invitations and variety | Exact chance anchors/interpolation; seven-day cooldown; party-only audience; one saved roll/day; category gates; major cooldown; all content counts; recency exclusion and exhausted pools |
| Story progression | Meaningful alternate answers; tangents; repeated phrases; wrong destination; native duel/hideout result; correct participant XP; persistent pet facts in later prompts |
| Leave | Wait-before-leaving versus time-away; stay-for-now reconsideration; new reasons versus repetition; refusal/mitigation/defiance; automatic due departure; native interruption; return of the same NPC |
| Announcement privacy | Town with residents and visiting nobles; same-clan absent NPC; different army party; prisoner; temporary guest; late joiner; changed roster during send; source visible only to actual hearers |
| Announcement behavior | No NPC response generation at send; no actions or rewards from announcement text; later distinct reactions; one-hour resume respected; retries preserve one history event and one delivered reaction |
| Summons | Valid ruler/clan/mercenary authority; invalid peer target; party and partyless scheduled arrival; 80% default wait and slider; no early relocation; issuer stays; actual arrival starts the full duration; both remain; player leaves; concurrent holds |
| Interrupted meetings | Capture/death/authority loss; army conflict; inaccessible meeting location; delayed but possible arrival; revision/cancellation; saved waiting/travel/meeting phases; no stale hold after termination |
| Persistence and UI | Campaign/timeline isolation, rewind, stale provider results, exact effect receipts, full applicable rendered matrix, reference fidelity and native input/navigation |

Mechanical fixtures must produce invariant results for the same accepted terms and evidence, across paraphrases, missing metadata, duplicate submissions and recovery. Conversational naturalness and persuasion interpretation require separate measured provider/native review; deterministic tests alone cannot establish them.

Before implementation, obtain the exact-path manifest validation plan for the final changes. Use canonical Reign validation, task-attributed lease checks and updated testing procedures/catalog where harnesses change. Preserve the paired client/server ownership boundary and unrelated work. Native campaign advancement uses an enrolled disposable copy; provider and deployment gates remain explicit.

Completion requires the full catalogue, verified native effects, prompt/privacy proof, rewards and saved-state recovery, applicable interface evidence and human/native acceptance. Feature implementation, paid-provider testing and deployment are subsequent work; this approval draft does not mark any feature complete.

## 10. Approved additions and execution boundary (2026-10-11)

- Web UI Options: master NPC Lives switch; independent Approach LLM, personal stories, brief experiences, outings, major stories, personal leave, independent activities, party announcements, delayed reactions and summoned meeting switches. Preserve individual preferences. Disabled features stop new activation; accepted stories and native commitments can finish, and history remains. Silent chat resumption is always protected.
- Percentage sliders with matching numeric inputs, live probability/cooldown previews and reset controls. Overall frequency and brief/outing/major/leave/independent frequencies range 0–500%, default 100%, in 5% steps. Invitation cooldown, major-start cooldown and default reconsideration delay range 25–400%, default 100%. Global and category frequency modifiers multiply allocated probabilities before the saved daily roll. Reducing one category does not increase another. Cap combined probability at 100% while preserving proportions. Existing rolls and accepted dates are never rewritten.
- Default category allocation at high affinity is 80 brief, 10 outing, 4 leave, 1 independent activity and 5 major. The 15% outings/leave allocation includes the independent-life variant. Eligibility redistributes unavailable categories before applying frequency modifiers; setting a modifier to zero does not redistribute its probability.
- Test six representative brief-story prompt playthroughs, not all 200 brief experiences. Validate all 240 definitions structurally without providers. Thoroughly test each distinct mechanic and significant branch.
- Duel/hideout automated tests must launch the actual encounter and use guarded run/campaign/timeline/encounter-bound win/loss/withdrawal controls through production resolution. Never satisfy an assertion by setting the quest complete or granting rewards directly. No computer-controlled combat playthroughs. User acceptance covers actual duel and hideout gameplay.
- 2026-10-11: User authorized building and validation and explicitly held deployment until they can stop the running game and server. Do not stop or deploy to their live installation as part of implementation. Native acceptance after deployment remains a separate evidence requirement.

### Implementation checkpoint

- [x] Inspect paired source ownership, existing options/action/conversation foundations and task overlap; obtain initial Tier 3 changed-path validation plan.
- [ ] Implement and validate shared policy, saved state, feature settings and frequency scales.
- [ ] Connect Approach, one-hour silent resumption and durable prompt context.
- [ ] Implement story selection, authored catalogue, locations, assessments, rewards and persistent facts.
- [ ] Implement personal leave negotiation and native departure/return.
- [ ] Implement roster-scoped announcements and delayed reactions.
- [ ] Implement authority-checked summoned meetings, scheduled relocation and native holds.
- [ ] Implement duel/hideout integration and guarded outcome testing.
- [ ] Complete provider-free checks, selected prompt tests, interface evidence and canonical validation; prepare exact artifacts without deployment.
- [ ] User native gameplay acceptance after coordinated deployment.

Implementation is present across the shared contracts, server and native client. Current implementation task: 01a1138b-49d8-7c23-891d-5accfd4af4d3. Preserve all unrelated client/server deltas. Completion checkboxes remain open where native acceptance is required.

2026-10-11 verification checkpoint: Tier 3 integration run `20261011-222451-88e562f2` passed client/server compilation, all 1,817 contracts, persistence/relationship boundaries and paired repository hygiene. The complete provider-free UI matrix passed 204 cases; the full approved-reference audit passed all 26 screens. The scoped typography review preserves all other screens and records the new Party Chat controls plus the existing documented Social Event speaker-label fix. Review evidence is under `.codex-build/ui-preview/npc-lives-01a1138b/`.

The final source batch adds party-wide exclusion of eight recent brief-story families, interrupted guest-service return guards, bounded recent-experience prompt context while always retaining durable possessions, and six samples through the production story prompt and accepted-evidence boundary. These checks use no paid providers. The authoring UI currently exposes versioned JSON, ordered phases, prompts, objectives, affinities, location requirements, rewards and facts with search/import/export/preview; it does not present a visual branching graph. Actual model interpretation, native input/navigation, live leave/meeting travel and real duel/hideout gameplay require the coordinated post-deployment acceptance stage. The user's game and server remain untouched.

2026-10-12 native testing checkpoint: The user subsequently authorized deployment, controlled native encounter outcomes on a disposable copy of the action-system baseline, and continuing the running game. Six representative brief experiences, native duel win/loss/withdrawal, hideout withdrawal, announcement audience/no-response behavior and persistent commented/no-comment reactions have evidence in `.codex-build/npc-lives-01a1138b/`. Leave persuasion preserves its fixed purpose and grants a repeated reason no additional credit. Required meeting arrival/release, leave departure/return, hideout victory/loss and pet final-outcome acceptance remain in progress; completion boxes remain open.

Native testing found a proxy timeout mismatch: Apache stopped at 360 seconds while interactive client requests allow 900 seconds. The reviewed server controls now align that timeout to 900 seconds, with an isolated generated-config contract. A timed-out proxy response does not prove the server cancelled generation; inspect canonical accepted sources before retrying. A separate provider service-unavailable response retained the player contribution without storing an NPC reply or granting story rewards. Story Location inquiry labels are compact, with complete titles, status, location and announcement text retained in their hints; rendered native verification remains required.

2026-10-12 controlled hideout loss passed after the bounded cleanup repair: both actual agents were witnessed, the normal loss callback resolved, no recovery or reward was granted, and native time remained exactly unchanged. Compact Story Locations rows and full hints passed rendered native review. Reload testing also reproduced a missing initial Story Locations entry while asynchronous personal state was loading. The client now requests the native menu's public option refresh only when story availability changes, preserving the current menu and readiness gate; validation and native reload verification of this repair remain pending.

2026-10-12 UI placement revision: Options contains NPC Lives feature switches and percentage adjustments only. Diagnostics → NPC Lives and Story Editor owns prompts, versioned definitions, previews/import/export, base timing and Approach provider/model controls. Dedicated saves and resets affect only their own fields; the global save preserves both sections. Verify navigation, reload, failure states and layout at 800/1280/1920 pixels before deployment.

2026-10-12 solo-camp implementation checkpoint: native duty/camp ownership, unavailable transit, standalone attendance, immediate return, signed attendance arrangement, legacy compatibility and read-only camp evidence are implemented in the next validation batch. Native acceptance of the new behavior remains open. Previous product run `20261012-223509-fb439ab7` passed all11 operations but contains an abandoned loot-selling restriction and is not deployed.
