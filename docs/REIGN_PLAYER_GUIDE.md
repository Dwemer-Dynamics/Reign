# Bannerlord Reign: Complete Player and Feature Guide

> Living source-reviewed edition: 2026-09-03
> Canonical scope: every player-visible or player-relevant behavior shipped by the Reign client, local server, campaign simulation, persistence layer, Control Center, and authored game data.

## What this guide is for

Reign is not only a dialogue mod. It changes time, character psychology, relationships, families, politics, war, settlement economics, information visibility, persistence, and the way AI statements become real Bannerlord actions. Much of that work is intentionally subtle. A pregnant noble quietly leaving field command, a rumor hardening into a reputation, a village becoming unable to supply more recruits, or a ruler choosing domestic support over foreign peace can all happen without a conventional quest notification.

This guide serves two purposes:

1. It teaches players where Reign's features are and how to use them.
2. It documents background rules that a casual player could not reliably infer by observation.

The guide describes production behavior, not test scaffolding. Developer and diagnostic controls are covered only where players or maintainers can encounter them. Exact probabilities and values are included when they materially explain an outcome. Generated dialogue remains variable, but its authority, memory, validation, and world-effect boundaries are fixed.

Availability labels are deliberate. **Shipped/certified** describes behavior that completed its release gate; **acceptance-stage preview** describes a deployed feature whose final native or provider-backed acceptance remains open; and **current source awaiting certification** identifies a revision that exists in the workspace but is not yet covered by the last completed release evidence. Active work that is not a usable player feature is not presented as available.

If the game and this document disagree, treat that as documentation drift or a bug. The implementation remains authoritative until the guide is corrected.

## Contents

1. [Getting started and how Reign fits together](#chapter-1-getting-started-and-how-reign-fits-together)
2. [Time, campaign preparation, and the living world](#chapter-2-time-campaign-preparation-and-the-living-world)
3. [Characters, personality, status, and knowledge](#chapter-3-characters-personality-status-and-knowledge)
4. [Conversation, party chat, correspondence, and social events](#chapter-4-conversation-party-chat-correspondence-and-social-events)
5. [Relationships, romance, marriage, pregnancy, and family](#chapter-5-relationships-romance-marriage-pregnancy-and-family)
6. [Rumors, reputations, and Public Standing](#chapter-6-rumors-reputations-and-public-standing)
7. [Court, capitals, government, councils, family chambers, and ambassadors](#chapter-7-court-capitals-government-councils-family-chambers-and-ambassadors)
8. [Spymaster, intelligence, sabotage, and covert politics](#chapter-8-spymaster-intelligence-sabotage-and-covert-politics)
9. [Diplomacy and political pressure](#chapter-9-diplomacy-and-political-pressure)
10. [Military command, party guests, War Council, duels, arrests, and rebellion](#chapter-10-military-command-party-guests-war-council-duels-arrests-and-rebellion)
11. [Kingdom catastrophes, boons, and economic simulation](#chapter-11-kingdom-catastrophes-boons-and-economic-simulation)
12. [Portraits, event art, the Memory Book, and interface changes](#chapter-12-portraits-event-art-the-memory-book-and-interface-changes)
13. [Saving, timelines, backups, and the Control Center](#chapter-13-saving-timelines-backups-and-the-control-center)
14. [Complete MCM settings reference](#chapter-14-complete-mcm-settings-reference)
15. [Complete executable action reference](#chapter-15-complete-executable-action-reference)
16. [Exact rules and formulas quick reference](#chapter-16-exact-rules-and-formulas-quick-reference)
17. [Feature locator and glossary](#chapter-17-feature-locator-and-glossary)

---

# Chapter 1: Getting started and how Reign fits together

## Chapter summary

Reign is a Bannerlord single-player module backed by a local companion server. Bannerlord supplies the authoritative campaign and native actions; the server supplies character construction, dialogue, memory, relationship reasoning, social simulation, and model routing. Generated text cannot directly rewrite the campaign. Reign first resolves real game objects, validates authority and current state, executes through a coded adapter, and records a receipt.

## Requirements and components

The module ID is `ReignBeta`. It depends on Bannerlord's Native, SandBoxCore, and Sandbox modules, with StoryMode supported when present. It also uses Harmony, UIExtenderEx, and Mod Configuration Menu.

The running system has three user-facing pieces:

- **Bannerlord Reign module:** campaign behaviors, native actions, menus, screens, portraits, and save-backed Reign state.
- **Local Reign server:** AI requests, character records, memories, relationships, diplomacy, action planning, logs, and campaign snapshots.
- **Dedicated Reign Control Center:** the visible app-style window for configuration, backups, character editing, diagnostics, and advanced tools.

The supported server address is `http://127.0.0.1:5101`. Start it through the installed Reign shortcut or `Start ReignBeta Server.cmd`. The server console, Control Center, and vector worker form one lifetime group: closing either visible Reign window shuts down the group. The Control Center is intended to be the dedicated window opened by Reign, not an ordinary browser tab.

## First campaign load

Reign holds the campaign behind a preparation screen until its critical foundations are consistent. The pipeline can:

- restore the matching Save Sync point;
- migrate and place the authored court households;
- generate permanent personalities for every living notable and apply their five native personality traits;
- initialize the 126-day calendar;
- open or restore the campaign's World History timeline;
- synchronize hero identity and portrait records;
- seed opening family, co-located, and political relationships from permanent MBTI compatibility;
- prepare family, temporary-party-guest, rebellion, diplomacy, social-event, court, ruler-docket, Government, and reputation state;
- process initial rumors, reputations, Public Standing, and native relationship projections;
- wait until critical queues are durably idle; and
- seal the prepared campaign before normal play and protected saving begin.

If preparation fails, the popup remains open and offers **Retry**. Completed stages are journaled, so retry resumes rather than deliberately repeating the entire pipeline.

## Five-day autonomous-world grace period

New Reign campaigns record an origin day. Autonomous world systems remain quiet for the first **5 campaign days**, allowing initialization and the player's opening situation to settle. Older saves created before this rule remain compatible and are not suddenly frozen for five days.

## Generated words versus real actions

Reign separates roleplay from authority:

1. An NPC speaks in character.
2. A hidden action gate decides whether the reply contains a real commitment, command, refusal, threat, condition, or roleplay-only statement.
3. A planner maps a genuine commitment to a bounded action vocabulary.
4. Resolvers identify actual heroes, clans, parties, settlements, items, workshops, prisoners, and kingdoms.
5. Validators check ownership, presence, authority, consent, war state, safety, and current campaign facts.
6. A native executor attempts the action.
7. Reign stores the result and shows or remembers only what actually happened.

An NPC can agree to a gift, marriage, transfer, arrest, order, or treaty in dialogue, but their prose does not make it true. The native receipt does.

## Feature availability at a glance

| Feature | Where to find it | Key prerequisite |
|---|---|---|
| Individual AI dialogue | Speak to an eligible adult living NPC | Reign and local server enabled |
| Party Chat | MCM utility/hotkey; default `\` | Eligible heroes in party or current safe location |
| Correspondence | MCM utility/hotkey; default `Ctrl+M` | A known living contact |
| Social event | Announced town-menu option | Open event in the current town |
| Wilderness event | Appears on open campaign map | Traveling outside settlements with eligible party heroes |
| Court / Rule Mode | Town or castle menu | Player rules a kingdom and has designated a capital |
| Government | Government button in Royal Court | Player rules a kingdom; Court System enabled |
| Ruler petitions | Court tab in Royal Court | An eligible subject has an unresolved concrete need |
| Chancellor | Appoint or dismiss through direct conversation | Player ruler; salary takes effect at next 08:00 |
| Family Chambers | Royal Court or Keep's Castle Layout | Eligible adult lords or their children present in the castle roster |
| Royal Council | Royal Court | Player rules a kingdom and has a capital; feature is available as an acceptance-stage preview |
| War Council | Military tab in Rule Mode | Royal court authority |
| Spymaster | Spymaster court tab | Active Spymaster officeholder |
| Capital | **Designate Capital** in an owned kingdom town | Player is current ruler |
| Resident foreign ambassador | Capital court | Peace, eligible foreign envoy, travel and residency |
| Temporary noble party guest | Ask an eligible noble in individual dialogue | Their explicit consent and a valid fixed or open-ended term |
| AI portrait | Reign screens and supported native portraits | Portrait feature and image provider enabled |
| Memory Book | Quest-screen insertion/overlay | Recorded illustrated memories |
| Save Sync and backups | Automatic plus Control Center | Local server available for protected points |

---

# Chapter 2: Time, campaign preparation, and the living world

## Chapter summary

Reign treats the campaign as a continuous, remembered world. It changes the calendar and aging rate, records objective history separately from what individuals know, models how news travels, and runs background diplomacy, relationships, economies, and events. This chapter explains the global foundations behind later features.

## The 126-day calendar

Reign changes the native **84-day year** into a **126-day year** with four **31.5-day seasons**. All character aging uses the same 126-day rate. Year, season, day-of-year, day-of-season, week-of-season, elapsed years, remaining years, and future-year calculations are routed through the Reign calendar.

When Reign first adopts an existing campaign, it creates a calendar anchor that preserves the apparent current date while converting future time to the longer calendar. Every known living hero also receives an age anchor, preventing an existing character from abruptly changing age. Dead heroes stop aging at their death day.

Practical consequences include:

- a “year” in Reign takes 50% more campaign days than native Bannerlord;
- pregnancies, reputation seasons, relationship seasons, treaties, and other systems should be interpreted by their stated day count, not by assuming the native year;
- all generated characters are instructed to reason using the Reign calendar, not the native one.

## World History: objective truth

World History is Reign's campaign-scoped, chronological ledger of native facts. It is separate from generated prose, individual memories, beliefs, rumors, and reputations. The coverage layer classifies the public Bannerlord campaign-event surface and records facts such as:

- battles, winners, losses, party participation, prisoners, and settlements;
- wars, peace, ruler changes, clan and kingdom movements;
- births, deaths, marriages, pregnancies, captures, releases, and escapes;
- settlement ownership, raids, sieges, tournament outcomes, and economic events;
- Reign court, arrest, duel, rebellion, diplomacy, social, and kingdom-event outcomes.

Battle records retain enough information for later correlation—for example, that captivity followed a particular defeat—rather than storing only a sentence.

## Knowledge is not omniscience

An objective event existing in World History does not mean every NPC knows it. Reign maintains knowledge receipts and perspective:

- participants know what they directly experienced immediately;
- ordinary facts involving a kingdom generally reach that kingdom after about **3 days**;
- major facts reach involved kingdoms after about **1 day** and the wider world after about **3 days**;
- private, hidden, or locally witnessed material follows its own visibility and evidence rules;
- a character can believe a false claim without the objective ledger changing.

This is why two NPCs can honestly give different answers about the same recent event.

## Claims, evidence, and lying

When a character makes a factual claim, Reign can compare it with speaker-visible evidence and objective history. Verdicts include:

- `verified`
- `partially verified`
- `contradicted`
- `not found`
- `history incomplete`
- `insufficient evidence`
- `ambiguous identity`

The speaker-visible verdict and objective verdict are stored separately. A first-hand witness who makes a directly false claim can be exposed immediately. A second-hand deception check uses the average of **Roguery and Charm**, with a deterministic deception chance clamped to **10%–80%** around a base of 50 plus the skill difference. A listener may confront, probe, quietly record the inconsistency, or pretend to believe it.

If a false claim is accepted, it becomes an unverified belief rather than objective truth. Later evidence can reconcile or supersede it. The player can privately recognize that an NPC is lying without the game revealing facts the player character does not actually know.

## Political pressure as background world motion

Non-player kingdoms experience recurring political-pressure incidents after the startup grace period. Each day has a deterministic **43%** global incident roll, provided at least two eligible non-player, non-rebel kingdoms exist.

The origin kingdom is rotated toward realms that have gone longest without originating an incident. The chance that an incident is hostile falls as that origin accumulates wars:

| Origin's current wars | Hostile incident chance |
|---:|---:|
| 0 | 65% |
| 1 | 50% |
| 2 | 35% |
| 3 or more | 20% |

Hostile channels include war, coercion, treaty strain, and punitive demands. Peaceful channels include peace, trade, security, and aid/exchange. The catalog contains **32 named situations with four presentation variants each**—128 incident archetypes—such as Border Retaliation, Caravan Seizure, Envoy Humiliated, War Weariness, Market Access, Shared Border Patrols, and Famine Relief.

Severity is minor (40%), moderate (35%), major (20%), or crisis (5%). Those ranks create 6, 10, 16, or 24 pressure and put 10, 15, 20, or 25 points of relationship stakes behind the ruler's choice. Three of the ruler's seven virtues are consulted. Supporting domestic lords adds pressure toward eventual diplomacy but harms the foreign-ruler relationship; preserving foreign relations does the reverse and disappoints selected domestic clans.

Pressure is directional and clamped to -100 through +100. Opposite incidents cancel existing pressure before crossing zero. At magnitude 10 or more, the direction receives a daily action chance equal to half its magnitude, capped at 50%. A selected direction becomes an expansion, peace, prosperity, or security diplomatic opportunity. Acceptance consumes the pressure. Refusal adds 8 hostile pressure and a -25 ruler-relationship incident.

## NPC dungeon security

Native Bannerlord gives player-owned settlement dungeons a special prisoner hold-rate bonus. Reign extends that same native hold rate to **all NPC-owned towns and castles**. This makes noble imprisonment by other realms more durable and keeps the political world from leaking prisoners simply because the owner is AI-controlled.

---

# Chapter 3: Characters, personality, status, and knowledge

## Chapter summary

Every Reign character has more than a generated voice. Reign builds a persistent psychological, social, economic, historical, and visual record, while separating public facts from private motives and secrets. Personality affects dialogue, relationships, diplomacy, rebellion, romance, office performance, and decisions throughout the mod.

## Campaign-scoped character records

Characters are stored per campaign and timeline. A record can include:

- native identity, clan, kingdom, role, occupation, age, culture, family, holdings, skills, traits, equipment, and current status;
- permanent MBTI type and foundation personality traits;
- seven derived court virtues;
- appearance, visible status, true status, clothing/status mismatch, wealth, and clan social credit;
- public and private history, secrets, goals, loyalties, obligations, memories, beliefs, rumors, reputations, and relationships;
- portrait sources, generated variants, and selected active portrait;
- dialogue, event, decision, and action history.

The **public layer** can be used openly by other characters. The **private layer** can influence lying, jealousy, caution, bargaining, intrigue, romance, or rebellion without being directly exposed to the player or other NPCs.

## Shipped characters and dynamic construction

Shipped, authored characters can materialize from canonical data without an LLM call. A dynamic character is fully constructed on the first interactive response that needs them. Construction is bounded to at most **two attempts**. If both fail, Reign returns a diagnostic failure instead of inventing a stateless fallback personality. A successfully constructed character is saved and reused.

## Foundation traits and seven virtues

Reign's private foundation uses **43 traits**, each expressed from -2 to +2 and also projected to stable 0–100 percentages. Percentage bands provide more nuance than Bannerlord's five native traits while remaining permanent enough for long-term continuity.

The seven widely reused derived virtues are:

- **Compassion:** concern for suffering and mercy.
- **Boldness:** willingness to initiate under social or strategic safety.
- **Honor:** truthfulness, fair dealing, and adherence to principle.
- **Loyalty:** attachment to people, clan, and sworn commitments.
- **Responsibility:** duty, follow-through, and institutional care.
- **Courage:** conduct under real danger. It is intentionally distinct from Boldness.
- **Judgment:** restraint, practical reasoning, and consequence awareness.

These virtues do not replace Bannerlord's native personality traits. Reign can apply compatible native traits to notables while retaining its richer private model.

## Permanent MBTI and directional compatibility

Reign supports all **256 directional pairings** among the 16 MBTI types. MBTI assignment is permanent unless explicitly changed in the Character Editor. Compatibility is directional: A's disposition toward B need not equal B's disposition toward A.

An unknown pairing starts from 60 before personality adjustment; ordinary compatibility is bounded to **19–81**. Native relationship baselines are adopted in this precedence order:

1. explicit Reign override;
2. save-derived Reign relationship;
3. native baseline;
4. current projected value.

Default native relationship anchors include spouse 50, immediate family 20, and lord-to-ruler 10. Reign then maintains its directional values and projects a safe aggregate back to Bannerlord where needed.

## Permanent notable preparation

At the first prepared campaign start, every living Bannerlord notable receives a permanent Reign personality and a complete five-trait native personality assignment. The game does not release normal play until the server confirms every assignment. Later passive regeneration does not overwrite explicit Character Editor changes.

## 720 authored court nobles

Reign adds **720 authored court nobles** grouped into **120 independent, landless noble households**. Each house begins with six family roles: father, mother, and four children. Houses are culture-appropriate and attached to a town or castle. They receive authored names, deterministic culture-valid faces and hair, family inheritance for younger members, and independent clans.

They begin at their home holding and serve as a deeper local court population. A landless house cannot field a party. If the house later acquires a fief, its members can enter normal campaign life, although a regular available clan commander takes precedence over a court noble when possible.

## Status, appearance, and wealth

Reign distinguishes what status truly exists from what an observer can see. Clothing, equipment value, clan tier, renown, fiefs, personal and clan wealth, retinue strength, and presentation create a visible social impression. A rich ruler dressed plainly or an impoverished courtier dressed extravagantly can produce a deliberate status mismatch.

Characters react to **known, visible status**, not omniscient bank balances. The same distinction feeds dialogue, deference, bargaining, portraits, and motive selection.

## Skills remain visible; exact relationships do not

Reign deliberately hides exact NPC relationship numbers across hero tooltips, encyclopedia pages, clan lists, marriage and heir views, party and recruitment UI, conversations, and relation-based sorting. The player sees uncertainty instead of a universal social spreadsheet. Recruitment failures can state that the exact relation is unknown.

This masking does **not** apply to the player character or when **Reveal All Character Relationships** is enabled in MCM.

Skills are the opposite: Reign explicitly keeps exact skill values visible in encyclopedia, character development, crafting, and education surfaces. NPCs may describe skill naturally in dialogue, but the player's mechanical skill UI remains readable.

## Character Editor

The Control Center's **Characters** tab can inspect and edit a character's complete Reign and supported Bannerlord record. It supports:

- searching and filtering lords, companions, notables, living, dead, and pending-edit characters;
- editing character-construction documents, traits, public/private history, relationships, memories, and records;
- reapplying an authored canonical profile or rerolling campaign-stable private hooks;
- selecting an active portrait variant;
- recording revisions with descriptions and conflict detection;
- rebuilding search, prompt, memory, and relationship projections after a change;
- queueing native changes for the loaded game; and
- rolling a prior revision back, including supported inverse native changes.

Some edits can materially change campaign state and are confirmation-gated. The editor is an advanced campaign tool, not an in-character player ability.

---

# Chapter 4: Conversation, party chat, correspondence, and social events

## Chapter summary

Reign turns conversation into a persistent, multi-channel social system. Individual talks, party groups, letters, court audiences, castle rooms, and formal events share character identity and memory while respecting physical presence, knowledge, authority, and the delay of communication.

## Individual conversation

Eligible adult living NPCs answer as themselves, grounded in current campaign facts, personality, rank, culture, relationship, memory, motive, visible status, and knowledge. They can refuse, lie, bargain, demand conditions, remember favors, react to witnesses, or end an exchange.

Continuity protections prevent common AI roleplay errors. A present speaker cannot claim to be absent; an NPC cannot speak for the player or complete unvalidated transfers in narration; group speakers should not copy one another; unknown identities are not freely revealed; and stale generated wording is treated as history rather than an instruction.

When **Enable Conversation Relationship Changes** is on, validated conduct can change directional affinity. Reign classifies acts such as honesty, insult, coercion, support, betrayal, gifts, flirtation, and life-changing aid. The size and persistence of an effect depend on severity, evidence, context, personality, and whether a supposed benefit actually occurred.

## Memory and continuity

Reign retains recent attributed raw turns, complete group scenes for participants, scene summaries, middle-term consolidations, rolling relationship arcs, objective history, obligations, beliefs, rumors, and semantic memory. Retrieval is ranked by relevance, recency, importance, perspective, and source type.

Semantic retrieval uses the local **BAAI/bge-small-en-v1.5** embedding worker by default. Full-text search remains an authoritative fallback. With external Qdrant selected, only vectors and metadata are externalized; raw prose remains in Reign's campaign storage.

## Party Chat

Party Chat is a living group conversation. Open it through the MCM utility or its configurable hotkey (default `\`). Participants can include eligible heroes in the main party and, in safe settlement contexts, relevant present heroes such as local leaders or household members.

The screen lets the player:

- select one or more participants;
- send one prompt to the active group;
- receive sequential, individually authored replies aware of earlier speakers;
- inspect portraits, open encyclopedia pages, or request AI portraits;
- retain the scene as a shared conversation and memory; and
- continue castle-room sessions with a generated room background when applicable.

Each NPC speaks only for themselves. Group transcripts are stored whole for characters who participated.

## Correspondence

Open Correspondence through the MCM utility or its configurable hotkey (default `Ctrl+M`). The contact list contains known living eligible characters and can also surface commanders who have sent urgent order reports.

Letters have a dispatch day, delivery day, thread, status, unread count, and persistent body. Their status moves through in-transit, delivered, and read states. Sending closes the compose screen after successful dispatch; selecting a correspondent displays the complete thread and marks delivered incoming letters read.

Travel time is physical:

- if sender and recipient are at the same resolved location, delivery takes **0.25 day**;
- within the same kingdom, transit is approximately `1 + distance / 80` days, capped at **3 days**;
- across kingdoms, transit is approximately `3 + distance / 55` days, capped at **7 days**;
- every route has a minimum of **0.25 day**.

Replies, rebellion summons, ruler-docket business, ambassador matters, and Campaign Command reports can all use the same letter infrastructure. Some explicitly sovereign player commands execute on dispatch by design; ordinary conversation and replies wait for delivery.

## Scheduled town social events

When enabled, Reign checks for an ordinary town event no more than once every **3 days**. If fewer than **2** events are open, the scheduling attempt has a **25%** chance to create one in an eligible town. Events are announced and remain available for their template's opportunity window, normally 2–4 days.

In the relevant town, use **Attend the announced Bannerlord Reign social event**. An interrupted active event can be reopened through **Return to the Bannerlord Reign social event**. Up to **25** NPC attendees are selected from eligible local characters, weighted by invitation suitability and recent attendance.

Reign ships six culture-specific families across Empire, Vlandia, Sturgia, Battania, Aserai, Khuzait, and Nord cultures:

| Family | Culture examples | Opportunity | Phases |
|---|---|---:|---|
| Feast | Villa Supper, Court Feast, Hall Feast, Forest Feast, Merchant Banquet, Steppe Feast, Longhouse Feast | 4 days | Arrival and Seating; First Course; Toasts and Table Talk; Entertainment and Side Talk; Farewells |
| Fair | Artisan Salon, Village Patron Fair, Fur Market, Woodcarver's Fair, Textile Exhibition, Tack Fair, Sea Fair | 4 days | Arrival at the Stalls; Demonstrations; Browsing and Bargaining; Patron Judging and Purchase; Closing the Fair |
| Dance | Courtyard Dance, Courtly Ball, Hall Revel, Grove Dance, Moonlit Courtyard Dance, Steppe Circle Dance, Longhouse Revel | 4 days | Arrival; Opening Mingling; First Dance; Refreshments and Second Dance; Closing |
| Performance | Poetry Recital, Minstrel Performance, Saga Night, Bardic Gathering, Storyteller's Evening, Horse-Song Performance, Skaldic Night | 3 days | Guests Gather; Host Introduction; First Performance; Audience Reactions; Second Performance; Debate and Praise; Final Performance; Closing Conversations |
| Contest | Patron's Contest, Court Challenge, Hall Trial, Grove Contest, Market Challenge, Steppe Challenge, Thing Challenge | 3 days | Gathering and Signups; Opening Boasts; First Round; Rest and Reactions; Second Round; Final Challenge; Prizes and Praise; After-Contest Mingling |
| Outing | Garden Outing, Hunting Outing, Winter Walk, Grove Outing, Oasis Outing, Steppe Ride, Shore Walk | 2 days | Gathering Outdoors; Setting Out; First Display; Trail Conversations; Main Activity; Rest and Refreshment; Returning; Parting Words |

Within a phase, NPCs respond sequentially to the player and to socially relevant prior contributions. Different phases carry different chances for ordinary affinity movement, stronger incidents, or romantic development; crowded public moments and private side conversations are intentionally not equivalent.

## Tournament celebrations

If enabled, every active tournament can create one culture-appropriate feast, fair, or dance. The opportunity remains open through the tournament and for **0.5 day after it finishes**. After victory, the opening context turns toward the winner and prize. Cancellation expires the celebration.

## Generated wilderness events

When enabled, Reign checks hourly while the player is traveling on the open campaign map. After at least **3 days** since the previous attempt, each eligible hour has a **2%** chance to open an event. It will not begin inside a settlement, in a battle, encounter, siege, menu, simulation state, or map conversation.

The event uses the terrain, location, time of day, and **1–3 eligible party heroes**. The server may create a title, approach, opening, player hook, emotional pressure, surface clues, hidden context, discovery routes, and an external known hero connection. A deterministic fallback encounter is used if generation fails. The player can address the matter, investigate, evade, apologize, challenge someone, or end the exchange through free-form conversation.

Relationship-development encounters use the same event presentation for private romance, rivalry, conflict, secrets, or obligations that can no longer remain unspoken.

---

# Chapter 5: Relationships, romance, marriage, pregnancy, and family

## Chapter summary

Reign replaces a single symmetric relationship number with persistent directional affinity and relationship state. Personality, shared presence, conversation, reputation, romance, betrayal, family ties, and political choices all feed that social world. The family system also distinguishes legal parentage from biological parentage and protects pregnant NPCs from field duties.

## Directional relationships

Every relationship can have two directions. A may trust B more than B trusts A. Native Bannerlord receives a projected aggregate where its systems require one number, but Reign retains both authoritative directions.

A pair is created only through a real source such as native history, family, marriage, co-presence, conversation, an event, political office, or another recorded interaction. Rumor and reputation effects do **not** create relationships between strangers.

## Passive relationships

When enabled, Reign periodically evaluates existing and co-present pairs without using an LLM. Main-party heroes receive full co-presence exposure. Characters sharing a settlement receive **0.35** exposure. MBTI compatibility, current state, family and political context, and stable individual variation can produce small directional drift.

Turning **Ambient Relationship Drift** off keeps relationship storage and explicit interactions active but prevents this small automatic movement. Turning **Enable Passive NPC Relationships** off disables the broader background director.

## Conversation effects and important acts

Validated conversation conduct can change affinity in one or both directions. Reign records the reason and evidence rather than applying an unexplained number. Especially important acts—saving a life, granting a home, grave betrayal, coercion after a favor, or sustained support—can create durable obligations or arcs. A gift does not count until the underlying item, gold, or benefit is actually validated.

Positive and negative changes can coexist. For example, a coerced beneficiary may retain some gratitude while also resenting the coercion. Grave coercion can erase retained appreciation.

## Relationship-development stories

Reign tracks five long-running storyline kinds:

- **romance**
- **rivalry**
- **marital conflict**
- **shared secret**
- **favor debt**

A rivalry becomes lasting around strength 75 after at least two supporting events. Marital conflict progresses through strain, troubled, separation-ready, and divorce-ready states. These arcs can surface as private wilderness/personal-matter encounters, conversation motives, letters, court matters, or later decisions.

## Romance stages

Romantic state uses hysteresis so one minor setback does not constantly flip a label:

| Stage transition | Enter at | Reset below |
|---|---:|---:|
| Flirtation | 30 | 25 |
| Growing bond / lovers | 50 | 45 |
| Serious bond | 70 | 60 |

Lovers end when either directional affinity falls below **30**. Same-sex romance is supported. Conception still requires a biologically valid pairing.

## Flings and affairs

A fling is possible only for a character with Judgment at or below **40**. The final chance is capped at **50%**, with a **25%** initial spark, a **7-day** cooldown, and a **20%** discovery chance. If either participant is married, each independently makes the relevant Judgment check; one permissive partner does not override the other's restraint.

Affairs can create rumors, Promiscuous or Unchaste reputation, jealousy, marital conflict, estrangement, secret pregnancy, or a later marriage. An affair-driven marriage chance is capped at **30%**.

## Marriage

With **Use Reign-Controlled NPC Marriage** enabled, Reign suppresses Bannerlord's random NPC matchmaking and runs its own relationship-aware marriage evaluation. Native player courtship remains available. Organic NPC marriage is evaluated daily for sufficiently serious eligible couples, with a **10%** opportunity chance when all conditions are satisfied.

Existing marriages remain native Bannerlord marriages. Reign adds directional marital state:

- a spouse becomes **estranged** when either spouse's affinity toward the other is at or below **-30**;
- divorce is queued when either direction reaches **-50**;
- Marital Strife, Divorcee, Disloyal, Promiscuous, or Unchaste social consequences can follow the underlying conduct.

## Conception, pregnancy, and parentage

Reign records both a **legal father** and a **biological father**. They may differ. Pregnancy can remain secret, and an illegitimate child's true parentage can later be revealed. Once revealed, the child receives the biological father link and a **Baseborn** surname. Pregnancy and birth records survive saving and timeline restoration.

**Native Bannerlord pregnancy involving the player is disabled.** The player cannot conceive—or cause a spouse to conceive—through Bannerlord's background spouse-visit pregnancy system. The **only** way to create a new child parented by the player is through a qualifying intimate event in an individual Reign chat. Ordinary marriage, traveling with a spouse, waiting together in a settlement, and the passage of campaign time cannot produce a player-parented pregnancy on their own.

The mother must be **18–45**. A lovers' conception opportunity is **5%** when biologically valid. Related partners and other invalid biological pairings are excluded.

When a qualifying intimate event in individual Reign chat reaches its conception roll, Reign shows a pregnancy-choice inquiry before insemination is resolved. **Proceed** allows exactly one idempotent conception roll. **Pull Out** prevents that roll, remembers the cancellation so it cannot be replayed, and returns the choice to the conversation as an ordinary extra dialogue turn. The popup is therefore a real reproductive decision, not a cosmetic notification. Its visual presentation is being modernized, but the underlying Proceed/Pull Out behavior is already part of the feature.

When pregnancy creates a commitment decision, Honor contributes a **+10** bonus and the final commitment chance is capped at **90%**. Marriage adds **+30** affinity; abandoning the relationship can apply **-70**.

## Pregnant NPC battle protection

With **Restrict Pregnant NPCs From Combat** enabled, every living pregnant NPC except the player is prevented from field and combat duties. This hidden protection is broader than merely stopping battle deployment. Reign blocks the NPC from:

- creating or leading a new mobile party;
- recruiting and recovering as a field commander;
- forming or joining army operations;
- attacking or capturing settlements;
- following, traveling, patrolling, or waiting on the map under a Reign order;
- raiding, besieging, attacking, surrendering, killing, or dueling through a Reign action.

If already in the field, the NPC is withdrawn to the nearest safe fortification, preferring a same-faction holding and then a nonhostile fallback. Reign avoids active battles and sieges and does not teleport someone out of a town while that town is actively besieged. Party leadership transfers to an eligible replacement when possible; otherwise the party is held or disbanded safely. A governorship can be removed if it conflicts with the safe relocation.

After birth, normal eligibility returns, but Reign does not magically reconstruct the former party. Likewise, disabling the setting does not recreate a party that was previously disbanded.

---

# Chapter 6: Rumors, reputations, and Public Standing

## Chapter summary

Rumors are temporary, uncertain reports. Reputations are durable public conclusions. Public Standing is the numerical social weight those active signals exert. Reign separates all three so that gossip can fade or be disproved, repeated evidence can harden into a lasting reputation, and a charming subject can soften—but not erase—the damage.

## Rumor lifecycle

A rumor normally lasts **45 days**. Each eligible underlying event gets a saved deterministic exposure roll, so reloading does not reroll history. The same thread and archetype cannot be exposed twice on the same day.

Repeated exposures inside the 45-day window have the following promotion chance:

| Exposure number | Chance to become a reputation |
|---:|---:|
| 1st | 0% |
| 2nd | 30% |
| 3rd | 60% |
| 4th and later | 90% |

Some decisive public outcomes create a reputation directly. Promotion creates the durable reputation and suppresses the matching active rumor without deleting its historical record.

A correction can disprove a rumor and reset its active exposure streak. It does not automatically erase a reputation that society already accepted as established fact.

## Public Standing and relationship projection

Positive and negative active contributions are summed separately. The subject's Charm mitigates only negative effects, scaling linearly to a maximum **60% reduction at Charm 300**. Observers in the subject's kingdom receive full strength; observers in other kingdoms receive half strength.

Projection is directional and affects only existing, socially connected pairs. An affair's co-participant is not treated as an observer condemning the other participant for that same affair.

Parameterized **Favors [person]** signals create person-specific jealousy, but Public Standing collapses any number of current favorites into a single global **-5 rumor / -10 reputation** penalty.

## Complete reputation catalog

Values below are `rumor / established reputation` Public Standing contributions. “Direct” means the signal normally begins as a reputation. “Derived” means Reign computes it from other state and it is not a normal manually removable tag.

### Personal and family conduct

| Reputation | Meaning | Value |
|---|---|---:|
| Disloyal | Betrayed a spouse's or partner's trust | -5 / -15 |
| Promiscuous | Intimacy outside accepted bonds | -5 / -15 |
| Marital Strife | A marriage is publicly troubled | -5 / -10 |
| Divorcee | A completed divorce | 0 / -5 for men; 0 / -20 for women |
| Flirt | A validated explicit flirt exchange | 0 / -5 |
| The Unchaste | Publicly condemned sexual conduct | 0 / -5 for men; 0 / -50 for women |
| Infertile | A season of opportunity without a child | -10 / -20 |
| Dynasty Secure | Living acknowledged children strengthen succession | +1 per child, maximum +50 |

Gendered values model the unequal social norms of the setting; they are not a statement of moral approval by the mod.

### Combat and command

| Reputation | Meaning | Value |
|---|---|---:|
| Strong Captain | Repeatedly performs well as a party commander | +5 / +10 |
| Weak Captain | Repeatedly performs poorly as a party commander | -5 / -10 |
| Tactician | Credited with sound tactical performance | +5 / +10 |
| Failed Tactician | Blamed for tactical failure | -5 / -10 |
| Siege Commander | Successfully directs a siege | +5 / +10 |
| Inept Besieger | Fails conspicuously in siege command | -10 / -20 |
| Siege Breaker | Relieves or breaks a siege | +5 / +10 |
| Champion of the Pit | Wins major tournament glory | +5 / +15 |
| Duelist | Wins a duel | +5 / +10 |
| Fallen Challenger | Loses a duel | -5 / -10 |
| Coward | Publicly fails a courage test or abandons danger | -10 / -20 |
| War Crowned | Ruler publicly wins a war | direct +15 |
| Defeated Crown | Ruler publicly loses a war | direct -15 |
| Battlemaster | Derived pattern of superior martial reputation | derived +10 |
| The Frail | Derived pattern of persistent martial weakness | derived -10 |

### Governance and realm stewardship

| Reputation | Meaning | Value |
|---|---|---:|
| Realm Builder | Holdings and prosperity expand | +5 / +10 |
| Realm in Decline | The realm visibly contracts or deteriorates | -5 / -15 |
| Provider of the Realm | Food security and recovery are credited to the ruler | +5 / +15 |
| Starving Crown | Hunger and failed provision are blamed on the ruler | -10 / -20 |
| Unifier | The ruler binds clans and realm together | +5 / +15 |
| Fractured Crown | The ruler presides over division | -10 / -20 |
| Keeper of Order | Security and law improve | +5 / +10 |
| Lawless Crown | Crime and insecurity spread | -5 / -15 |
| Guardian of Commons | Villages and common prosperity recover | +5 / +10 |
| Lord of Empty Fields | Rural decline and depopulation are blamed on the ruler | -5 / -15 |
| Consolidator | Holdings are governed with sustainable concentration | +5 / +15 |
| Overextended Crown | Expansion outruns control and administration | -10 / -20 |
| Fair Hand of Crown | Titles and fiefs are distributed fairly | +5 / +15 |
| Hoarder of Titles | The ruler monopolizes titles or holdings | -10 / -20 |
| Gatherer of Banners | Lords and armies rally to the crown | +5 / +15 |
| Scatterer of Banners | Lords and armies abandon the crown | -10 / -20 |
| Steward of the Realm | Derived pattern of sound rule | derived +10 |
| Ruinous Crown | Derived pattern of destructive rule | derived -15 |

### Court and social position

| Reputation | Meaning | Value |
|---|---|---:|
| Well Regarded | Mild positive court standing | derived +5 |
| Court Favorite | Strong positive court standing | derived +10 |
| Beloved of Court | Exceptional positive court standing | derived +15 |
| Ill Regarded | Mild negative court standing | derived -5 |
| Shunned at Court | Strong negative court standing | derived -10 |
| Court Pariah | Exceptional negative court standing | derived -15 |
| Favors [person] | Ruler visibly favors a specific courtier | -5 / -10 globally, plus person-specific jealousy |
| Attentive Lord | Ruler remains personally present for 5 consecutive days | +5 / +15 |
| Absent Lord | Ruler remains away for 15 consecutive days | -10 / -25 |

## Where reputations come from

Reign producers include battles, sieges, tournaments, duels, wars, realm statistics, food and security, court presence, favoritism, romance, affairs, marriage, divorce, children, arrest accusations, kingdom events, political choices, and validated conversation conduct. Ordinary producer exposure chances range roughly from 5% to 30%; decisive war outcomes are direct and certain.

Spymaster missions can reveal, mitigate, fabricate, promote, disprove, or sometimes remove these signals. That system is covered in Chapter 8.

---

# Chapter 7: Court, capitals, government, councils, family chambers, and ambassadors

## Chapter summary

Rule Mode turns rulership into a place-bound institution rather than a remote omniscient menu. The ruler designates a capital, receives concrete petitions, negotiates with a culture-specific representative government, appoints officers and councillors, meets family members, receives ambassadors, and opens military or intelligence work. Personal presence, office vacancies, resources, and explicit consent all matter.

## Designating a capital and entering Rule Mode

Only the current ruler of a kingdom can designate a capital. Use **Designate Capital** in a town belonging to the player's kingdom. A capital can later be moved or cleared through the supported capital workflow.

After a capital has existed, use **Enter Rule Mode** in a safe town or castle belonging to the player's kingdom. A captive cannot hold court. A siege interrupts the session.

The court scope is:

- **Royal Court** at the designated capital, with full royal authority.
- **Local Docket** at another owned kingdom town or castle, with local scope and reduced authority.

The feature must also be enabled in MCM. It is enabled by default for a new configuration; an existing saved configuration that explicitly turned it off remains authoritative.

## Ruler docket and petitions

The old placeholder court agenda has been retired. Unresolved placeholder matters are removed without inventing effects; completed entries remain available as history. The shipped ruler docket now creates petitions only from concrete native need, never merely to fill an agenda. Petitions do **not expire**, only one can remain pending for the same petitioner and settlement, and an unaffordable decision stays pending rather than pretending to succeed.

The currently certified petition families are:

| Petition | When it can appear | What direct aid means |
|---|---|---|
| Food | A village has fewer than 400 hearths | Deliver food stock |
| Town funds | Town prosperity is low or falling | Pay a gold grant |
| Village funds | A village with at least 400 hearths produces below its healthy output | Pay a gold grant |
| Soldiers | Settlement security is below 80 and it is not besieged | Detach a temporary troop expedition |

Need severity is deterministic:

| Severity | Food hearths | Town prosperity | Village output versus healthy output | Settlement security |
|---|---:|---:|---:|---:|
| Minor | below 400 | below 5,000 and falling | below 80% | below 80 |
| Serious | below 200 | below 3,500 and not improving | below 60% | below 50 |
| Severe | below 100 | below 1,500 | below 40% | below 25 |

Each severity uses fixed terms:

| Severity | Gold | Food | Soldiers | Term | Requester relation on grant/refusal | Associated relations on grant | Daily hearths | Daily prosperity | Village output | Daily security | Expedition casualty cap |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Minor | 1,000 | 20 | 10 | 3 days | +5 / -5 | +1 | +1 | +0.2 | +5% | +0.5 | 5% |
| Serious | 7,500 | 40 | 40 | 10 days | +10 / -10 | +2 | +2 | +0.5 | +10% | +1 | 20% |
| Severe | 20,000 | 60 | 100 | 15 days | +20 / -20 | +4 | +4 | +1 | +20% | +2 | 40% |

The ruler can grant the requested aid, refuse it, or fund a supported substitute. Refusal creates a **3-day** repeat cooldown for that settlement and petition kind. A food substitute is priced from the required stock and the capital's grain price, multiplied by **10 × 5**. A soldier substitute uses native replacement value plus full-term wages, then multiplies that total by **5**. Direct soldier aid cannot reduce a healthy garrison below the greater of **50 troops** or **25% of its healthy strength**. The expedition returns after its term with bounded casualties; if its original return route becomes invalid, Reign finds a safe fallback instead of losing the detachment.

Reign evaluates **1–5 deterministic petition opportunities per day**, but only real need produces a case. The audience uses natural dialogue; only the explicit Grant, Fund Instead, or Refuse decision can apply the coded result. The broader planned noble-dispute, scandal, family-conflict, demand, and judgment docket is not part of this certified petition slice yet.

## Chancellor

The Chancellor is a separate ruler-docket office, not one of the Royal Council's four seats. A normal candidate must explicitly agree in direct conversation and be an adult, free, active lord, clan hero, or lawful settlement notable of the ruler's kingdom. The player cannot appoint themself. A party leader in battle or an army cannot safely accept. Appointment makes the candidate partyless and resident at the capital, removes an incompatible governorship or party command, and begins **Inactive**.

The default salary when no other amount is agreed is **500 denars per Active day**, but a nonnegative whole-denar salary can be negotiated. Inactive days are unpaid. Activation or deactivation takes effect at the next **08:00** docket boundary, and activation requires the ruler to hold at least one full day's salary. If a completed Active day cannot be paid in full, the Chancellor becomes Inactive before new petitions are drawn. While Active, the Chancellor suppresses ordinary petitions. Delegating this way on at least **15 of the last 30 days** produces the normal **Absent Lord** social outcome.

Normal dismissal also requires direct conversation. If the ruler is captured, a valid emergency Chancellor can assume unpaid Active authority automatically. The emergency or existing office continues through captivity and a **3-day handoff** after release, then returns or ends according to its recorded lifecycle. Service, payment, emergency World History, and the Chancellor's long-term emergency memory persist. The former Regent system is retired and migrated to court history rather than continuing as hidden authority.

## Court screen

The main Royal Court tabs are **Court, Court History, Lands, Subjects, Diplomacy, Ambassadors, Spymaster, and Military**. A noncapital court exposes the reduced **Local Docket** and its history. Status cards expose relevant funds, influence, renown, supply, and strength. Depending on tab and authority, the player can:

- hear and decide current ruler petitions;
- inspect settlements, crises, and governors;
- review subjects, relationships, obligations, rumors, plots, and intelligence;
- assemble and answer diplomatic packages;
- appoint officers, councillors, and the Chancellor;
- post and direct ambassadors;
- start covert missions; and
- open the War Council and issue military orders.

The Royal Court also provides direct entrances to **Government**, **Royal Council**, **Family Chambers**, and **Castle Layout**.

## Court offices and Royal Council seats

| Office | Primary Bannerlord skill |
|---|---|
| Steward | Steward |
| Economic Advisor | Trade |
| Foreign Advisor | Charm |
| Marshal | Better of Leadership or Tactics |
| Spymaster | Roguery |

Office performance blends **58% skill, 22% loyalty, and 20% integrity**, then applies duty and absence penalties. Performance changes the speed, cost, or reliability of relevant work. Appointment and dismissal are real, persistent decisions.

The player-facing **Royal Council** is a four-seat body: **War Councilor, Spymaster, Economic Advisor, and Foreign Advisor**. One person may hold only one major seat, and the War Councilor must be distinct from all other councillors. The War Councilor is selected inside the War Council and is not a normal court-office record. If that seat is vacant, intelligence uses the ruler's own Tactics and Leadership.

Economic and Foreign Advisor candidates must be living, active, free adults physically present at the capital. Accepting a resident council seat can require them to relinquish incompatible governor or party-leader duty; active battles, sieges, armies, captivity, and other unsafe states block the move. Council residence follows the capital and has shelter/recovery handling when the capital changes or is lost. Appoint the Economic Advisor from the Royal Council; the Economic Report does not currently contain a separate appointment control.

Opening the Royal Council requests bounded reports only from occupied seats. Topics route to the relevant domain; an irrelevant topic makes **zero provider calls**. Normal advice is limited to **1–4 sentences**, with a requested expansion capped at two short paragraphs. Hearings can include at most four present advisors, and only present participants receive the resulting memory. Reports are advisory: they cannot execute an action or directly change relationship or reputation state. Private, attributed transcripts are retained for the latest 24 council records.

**Availability note:** the Royal Council interface and office behavior are deployed as an acceptance-stage preview. Deterministic and UI coverage exist, but provider-backed opening/routing, save/reload, capital-relocation, and final native acceptance remain open. Treat council advice as usable but not yet launch-certified.

## Representative Government

Every kingdom receives a culture-specific representative institution. It is a second political axis alongside noble **Political Pressure**: Government represents organized members and people-side interests; Political Pressure represents ruler and noble pressures. Government does not directly rewrite Public Standing or a rebellion probability.

The Royal Court's **Government** entrance opens the ruler view, including parties, coalition, speakers, resolutions, members, lobbying, authority changes, and action approval or override controls. In any keep, **Explore the keep → Castle Layout → Government** opens the current realm's read-only public view; it does not grant ruler controls to an observer.

| Culture | Institution | Who receives seats |
|---|---|---|
| Empire | Imperial Senate | Town and village notables |
| Vlandia | Great Council of Peers | Landholding clan leaders |
| Sturgia | Grand Veche | Landholding clan leaders plus town merchants and artisans |
| Battania | Oenach | Landholding clan leaders plus village headmen and landowners |
| Aserai | Majlis al-Shura | Landholding clan leaders plus town merchants |
| Khuzait | Great Kurultai | Landholding clan leaders |
| Nord | Great Thing | Landholding clan leaders plus town and village notables |
| Other/fallback | Council of Estates | Landholding clan leaders plus town notables |

Members are assigned to **2–4 persistent political parties**. A government has a 45% chance of starting with two parties, 45% with three, and 10% with four only when it has at least 12 occupied seats. Each party has 2–4 policy planks, a speaker, seat count, loyalty, and coalition role. Government trust, party loyalty, and each member's Government loyalty are bounded from 0 to 100. The complete party blueprint vocabulary is:

| Party | Planks |
|---|---|
| Crown and Order | Royal Authority, Security |
| Common Weal | Representative Authority, Popular Welfare, Agriculture |
| Open Markets | Trade, Infrastructure, Popular Welfare, Peace |
| Land and Hearth | Agriculture, Local Autonomy, Popular Welfare |
| Iron Standard | Military Strength, Expansion |
| Peace and Plenty | Peace, Trade, Popular Welfare |
| Lawful Estates | Justice, Noble Privilege, Representative Authority, Peace |
| Free Clans | Clan Privilege, Local Autonomy, Representative Authority |
| Temple Compact | Faith, Popular Welfare |
| Watchful Realm | Security, Espionage, Royal Authority |
| Civic Builders | Infrastructure, Trade, Representative Authority |
| Old Privileges | Noble Privilege, Royal Authority, Local Autonomy |
| Border Voice | Security, Local Autonomy, Agriculture |
| Just Peace | Peace, Justice, Representative Authority |
| Merchant Defence | Trade, Security, Infrastructure |
| War Clans | Military Strength, Clan Privilege, Expansion |

Seats are reconciled every **7 days**. A member must be alive, adult, free, and not disabled. Landholding seats exclude the ruling clan, mercenaries, eliminated clans, and clans without a fief. A notable seat goes to the highest-power eligible notable of the required occupation in each relevant settlement. New members choose the compatible active party from their native personality, skills, occupation, landed/ruling status, and the party's planks, with a stable identity tie-breaker. A party's speaker is the highest stable score from `2 × Charm + Leadership + Government loyalty + half ruler relation`.

Government authority begins from a weighted roll: **10% level 1, 25% level 2, 30% level 3, 25% level 4, and 10% level 5**. Its practical meaning is:

| Level | Authority over the ruler | Daily settlement benefit while stable |
|---:|---|---|
| 1 | Advisory | none |
| 2 | Formal political pressure | +0.10 loyalty, +0.25 prosperity, +0.05 village hearths |
| 3 | Formal pressure plus an exact 7-day reconsideration delay | +0.20 loyalty, +0.50 prosperity, +0.10 hearths |
| 4 | Major actions require approval or a conscious override | +0.35 loyalty, +0.75 prosperity, +0.15 hearths |
| 5 | Major actions are blocked pending approval or an authority reduction; minor actions require approval or override | +0.50 loyalty, +1.00 prosperity, +0.25 hearths |

Major actions include war, peace, treaties, policies, taxation, major spending, fief transfer, and major justice. Espionage and relief are not major actions, but the institution can still support or oppose them through its planks. When Government fails to achieve its aims, consequences scale by authority: ×0.5, ×0.75, ×1, ×1.5, and ×2 from levels 1 through 5.

Raising authority grants realmwide immediate public benefits and a 30-day daily bonus:

| Increase | Immediate loyalty / prosperity / hearths | 30-day daily loyalty / prosperity / hearths | Government trust | Supportive / royalist relation |
|---|---|---|---:|---:|
| 1→2 | +8 / +200 / +10 | +0.25 / +0.50 / +0.10 | +15 | +6 / -3 |
| 2→3 | +12 / +350 / +20 | +0.50 / +1.00 / +0.25 | +25 | +10 / -5 |
| 3→4 | +16 / +550 / +35 | +0.75 / +1.50 / +0.50 | +35 | +15 / -8 |
| 4→5 | +22 / +900 / +60 | +1.00 / +2.50 / +0.75 | +50 | +25 / -12 |

Reducing authority requires support from at least **two thirds of all occupied seats**. A successful negotiated reduction still creates modest dissent:

| Reduction from level | Settlement notable relation | Landholding-lord relation | Nonlandholder dissenter relation | Government trust |
|---:|---:|---:|---:|---:|
| 2 | -2 | 0 | -2 | -3 |
| 3 | -3 | 0 | -3 | -5 |
| 4 | -4 | 0 | -4 | -7 |
| 5 | -5 | 0 | -5 | -10 |

A forced reduction is much harsher:

| Forced reduction | Settlement notable relation | Landholding-lord relation | Nonlandholder dissenter relation | Government trust |
|---|---:|---:|---:|---:|
| 2→1 | -20 | -20 | -10 | -40 |
| 3→2 | -35 | -35 | -20 | -60 |
| 4→3 | -55 | -50 | -30 | -80 |
| 5→4 | -70 | -70 | -40 | reset to 0 |

Government consent is explicit, level-specific, and consumed by the reduction it authorizes. Individual consent protects only that character; a clan leader's consent covers their clan; the player's clan is always exempt from forced-reduction relationship punishment. The updated exact relation targeting and consent-persistence rules are present in current source but are undergoing a reopened final certification pass; this note should be removed only when that revised acceptance gate is green.

Parties propose resolutions and meet every **21 days**. The governing coalition is built to a majority where possible. The player may speak with occupied party speakers, lobby members, and then face a final vote by all members. Coalition alignment, party membership, personality, loyalty, existing relations, and Charm affect outcomes. NPC-only meetings use no provider calls; a player meeting is bounded to at most one generated turn per occupied speaker. A resolution passes only by strict majority.

### Resolution catalog and exact terms

The catalog contains **104 need-backed resolutions**, eight in each of thirteen categories: settlement relief, food/agriculture, trade, security, defense, war/peace, raids/retribution, Spymaster work, diplomacy, justice/nobles, construction, prisoners/households, and taxation/obligations. Every proposal offers two coded completion routes. Across the catalog those routes can require treasury payment; food or goods delivery; loyalty, prosperity, hearth, or security improvement; garrison minimum or maximum; troop recruitment; a field army; patrol or caravan-guard duty; defeating bandits, raiders, or enemy engagements; making peace or declaring war; ending raids; Spymaster investigation or counterintelligence; identifying or capturing a culprit; improving foreign or clan relations; trade or non-aggression agreements; prisoner exchange, release, or ransom; clan compensation; granting or returning a fief; governor replacement; policy enactment or repeal; construction completion, change, or pause; tax/tariff/obligation relief; army supply; a feast; militia creation; or resolution of notable issues.

Resolution scale controls the reusable targets:

| Target family | Minor | Standard | Major |
|---|---:|---:|---:|
| Treasury payment or clan compensation | 10,000 | 25,000 | 60,000 |
| Food, goods, or army supplies | 50 | 150 | 350 |
| Garrison/recruits/field army/militia | 25 | 75 | 150 |
| Loyalty or security improvement | 5 | 10 | 20 |
| Prosperity improvement | 150 | 400 | 800 |
| Hearth improvement | 25 | 75 | 150 |
| Foreign or clan relation improvement | 5 | 10 | 20 |
| Bandit parties, enemy engagements, or issues | 1 | 2 | 3 |
| Deadline | 7 days | 14 days | 21 days |

Completion rewards are respectively **+1/+2/+4 loyalty**, **+50/+125/+250 prosperity**, **+5/+10/+20 hearths or security**, **+2/+4/+8 speaker relation**, and **+3/+6/+10 Government trust** for minor, standard, and major resolutions.

### Lobbying

The ruler can try **persuasion, bribery, or a recorded deal** once per method, member, and debate. The base chances are 5%, 20%, and 10%, then Reign adds `Charm ÷ 8`, `relation ÷ 5`, method-relevant traits, and subtracts `party loyalty ÷ 4` and `Government loyalty ÷ 10`; the final chance is clamped to **3–80%**. Persuasion adds twice the member's Mercy trait level, bribery subtracts four times Generosity and adds three times Calculating, and a deal adds four times Calculating plus twice Honor. Success shifts that member's vote by **+15 persuasion, +20 bribery, or +25 deal**. Failure records a -5 outcome but does not contribute a vote shift, lowers the member's Government loyalty by 2, and cannot be rerolled with the same method in that debate.

A bribe costs a scale base of **5,000 / 12,500 / 30,000 denars** for minor, standard, or major business, multiplied by `1 + 0.25 × (Government level - 1)`. A deal requires explicit recorded terms. If an accepted deal is not fulfilled by the later of seven days from agreement or 21 days after the proposal date, the ruler loses **12 relation** with the member, the member loses **15 Government loyalty**, Government trust falls by **10**, and the promised vote shift is removed.

## Family Chambers

Open **Family Chambers** from Royal Court, or use **Castle Layout** after choosing **Explore the keep** in the native Keep menu. The screen has adult family cards on the left, a central 16:9 castle scene, and child cards on the right. It is designed to make spouses, parents, children, and household conversation discoverable without searching a whole keep scene.

Eligible adults are adult lords currently available in that castle. Adult cards retain ordinary noble information and portraits; child cards show only name, exact age, and parents. Selecting an adult selects their children; selecting a child selects any available parent; later deselection affects only the card the player clicked. A chamber conversation includes the player and **1–4 NPCs**. Castle Chat's image setting controls the 1536×864 scene image, and children play, study, babble, or otherwise behave according to their exact developmental age.

Children receive age-aware, safe conversation rather than adult political or romantic prompting. Child identity art is accepted only from a single-face crop with at least **0.70 confidence**, and it is refreshed when the child ages by at least two years. Childhood conversations retain all memories while the character is a minor. At adulthood, only memories created at exact age **5 or older** remain active; younger material is archived, then normal adult construction takes over. Client chamber sessions are retained for 30 days. Prompts exclude romance, sexuality, danger, politics, weapons, and combat.

## Retired Regent

Older saves may retain Regent history, but the office no longer delegates current court authority. The active remote-management replacement is the salaried Chancellor for petition suppression plus normal correspondence and the four specialized Royal Council seats. Historical Regent records are migrated instead of silently discarded.

## Court supply and economic transfers

Court food and supply shipments are atomic. Reign verifies royal authority, source ownership, vassal or owner consent where required, gold, inventory, route, and destination before moving anything. If any step fails, transferred gold and items are rolled back rather than leaving a half-completed shipment.

## Castle rooms and living household placement

Castle Chat assigns eligible residents to rooms deterministically by time, role, skill, and household context. Time blocks are morning (05:00–10:59), afternoon (11:00–16:59), evening (17:00–20:59), and night.

The complete room set is:

- Castle Gardens
- Noble Solar
- Library
- Training Yard
- Inner Courtyard
- Small Dining Chamber
- Stable Courtyard
- Portrait Gallery
- Main Hall
- Chapel
- Throne Room
- Baths
- Battlements
- Guest Bedrooms
- Royal Bedroom

Ordinary room capacity is **4**; Guest Bedrooms allow **20**. Baths choose a fair rotating group of 2–4 same-sex visitors. The Royal Bedroom can include a spouse, active lover, or a sufficiently bold and low-Honor intimate candidate. These assignments create conversation opportunities; they do not themselves prove an affair or sexual event.

Room imagery can be generated independently of the general AI Portraits switch through **Castle Chat Image Generation**. Each culture/room pair has separately editable image and dialogue prompts in the Control Center; edits apply only to new sessions because active sessions snapshot their revision.

## Outbound ambassadors

The ruler can maintain up to **3** persistent outbound ambassador postings. Available actions include:

- gather public information;
- improve relations;
- deliver a trade proposal; and
- recall the ambassador.

Postings and missions are archived and survive saving.

## Resident foreign ambassadors

A peaceful foreign ruler can select an envoy to reside at the player's capital. The candidate must be a living, active, free adult lord or lady, have no current party, governorship, or captivity, and have at least **50 Charm**. Selection uses the highest available Charm band: **200+**, then **150–199**, **100–149**, and **50–99**. Establishing relations requires a relation of at least **-30** with the foreign ruler; an envoy sent for an international court matter can arrive below that threshold, provided the kingdoms are at peace.

Travel takes **1–3 days** according to distance. On arrival, the envoy resides partyless in the Keep. Moving the capital relocates the posting. Loss of the capital makes the envoy shelter; war recalls them.

Official ambassador dialogue has an immutable authority charter: exact authorized subjects, terms, due dates, hashes, and prohibited actions are stored with the posting. An envoy can negotiate only within that charter. Unsupported matters—such as an alliance they were not empowered to grant—must be referred to the foreign ruler. Official turns are archived and inherited by a successor envoy so the state cannot be reset through personnel changes. Tone pressure is capped and cannot manufacture authority.

---

# Chapter 8: Spymaster, intelligence, sabotage, and covert politics

## Chapter summary

The Spymaster is Reign's bounded covert-action system. It sells information, counters foreign agents, manipulates rumors and reputations, sabotages settlements, and can attempt assassination. Missions cost real gold, occupy one of three slots, take campaign time, and use transparent difficulty and detection rules.

## Access, capacity, and skill

Appoint an eligible courtier to the **Spymaster** office, then use the Spymaster tab in Rule Mode. The office uses Roguery and has **3 concurrent mission slots**. An officeholder with active missions cannot simply be replaced or dismissed to erase those missions.

Success chance is:

`52 + 0.18 × Spymaster Roguery - target difficulty`, clamped to **5%–95%**.

Detection chance is:

`base notice × (1.5 - Roguery / 300)`, also clamped to **5%–95%**.

Displayed difficulty labels are Routine at 75% or higher, Challenging at 55%–74%, Difficult at 35%–54%, and Extreme below 35%.

## Mission catalog

| Mission | Base cost | Days | Difficulty | Base notice |
|---|---:|---:|---:|---:|
| Land intelligence | 1,500 | 4 | 10 | 20 |
| Learn skills | 1,500 | 4 | 14 | 24 |
| Learn relationships | 2,250 | 5 | 22 | 32 |
| Learn rumors/reputation | 3,000 | 6 | 30 | 40 |
| Counterintelligence | 7,500 | 8 | 25 | 12 |
| Disrupt food | 7,500 | 7 | 30 | 38 |
| Disrupt construction | 15,000 | 9 | 40 | 48 |
| Disrupt security | 22,500 | 10 | 50 | 58 |
| Disrupt loyalty | 30,000 | 12 | 60 | 68 |
| Assassinate governor | 37,500 | 14 | 78 | 100 |
| Assassinate person | 37,500 | 14 | 82 | 100 |
| Mitigate own rumor | 4,500 | 5 | 20 | 10 |
| Mitigate own reputation | 11,250 | 7 | 35 | 10 |
| Mitigate target rumor | 4,500 | 6 | 30 | 26 |
| Mitigate target reputation | 11,250 | 8 | 45 | 34 |
| Promote positive reputation | 4,500 | 6 | 28 | 24 |
| Fabricate moderate harmful rumor | 11,250 | 8 | 48 | 48 |
| Fabricate severe harmful reputation | 30,000 | 12 | 70 | 72 |

Clan tier and ruler status increase people-target difficulty by `5 × clan tier`, plus 8 for a ruler. Tier also increases cost and duration; assassination costs scale especially sharply.

## Mission effects

Successful settlement sabotage applies:

- **Food disruption:** -35% effective village food contribution for 14 days.
- **Construction disruption:** -50% construction power for 18 days.
- **Security disruption:** -2 security per day for 21 days.
- **Loyalty disruption:** -2 loyalty per day for 21 days.

If a mission becomes invalid or is cancelled, spent money is not refunded. Reign records the failed or invalidated operation rather than silently rewinding the campaign.

## Rumor and reputation control

Against a rumor, a successful mitigation has an **82%** chance to remove/disprove it; otherwise it greatly weakens its projection. Against an established reputation, only **15%** of successes remove it; otherwise it is mitigated. Mitigated social effects use a **0.35 multiplier**.

Fabrication creates a traceable social signal through the normal rumor/reputation system. It does not rewrite World History into making the allegation objectively true.

## Assassination, capture, and breakout

Assassination is always noticeable and can lead to the Spymaster's capture. If captured, the player may:

- pay ransom, which exposes the sponsor and operation; or
- attempt a personal breakout.

Breakout chance is `25 + 0.18 × player Roguery - 0.2 × settlement Security - 4 × target clan tier`, clamped to **5%–85%**. Failure captures both the player and Spymaster and exposes the operation. Assassination is a real destructive campaign action and may kill a unique character.

## Foreign agents and counterintelligence

Every **5 days**, foreign realms can recruit agents among candidates whose loyalty/relationship is at most 40. Recruitment chance is `90 - 2 × loyalty`, clamped to **10%–90%**. A foreign sponsor operates against the player only when its ruler's outlook toward the player is at or below **-30**.

A hidden foreign operation has a **25%** detection chance; a detected operation has a **35%** attribution chance. Exposed agents remain in historical records but leave the active hidden pool. One counterintelligence success exposes `1 + floor(Spymaster Roguery / 100)` candidates.

---

# Chapter 9: Diplomacy and political pressure

## Chapter summary

Reign adds negotiated, persistent diplomatic agreements and lets non-player rulers pursue policy from personality, relationships, war load, realm condition, and domestic political pressure. Dialogue can propose terms, but only a validated sovereign action changes the map or creates an agreement.

## Ruler-driven diplomacy

When enabled, non-player rulers can pursue expansion, prosperity, security, or peace. Their seven virtues, current wars, relative strength, relationships, treaties, domestic pressure, and recent diplomatic history shape what they attempt. The player's kingdom is excluded from autonomous ruler-driven diplomacy until the player-facing court integration can represent the decision safely.

Relationship breakthroughs and incidents can create additional diplomacy opportunities. A proposal, response, refusal, native execution, war origin, treaty breach, and defensive obligation are all recorded as separate correlated facts.

## Core agreements and default durations

| Agreement | Default duration |
|---|---:|
| Trade agreement | 120 days |
| Non-aggression pact | 90 days |
| Alliance | 180 days |
| Defensive pact | 120 days |
| Guarantee of independence | 180 days |
| Hostage guarantee | 60 days |
| Demilitarized border | 60 days |
| Caravan protection agreement | 60 days |
| Supply agreement | 60 days |
| Loan or subsidy | 120 days |
| Paid neutrality | 60 days |
| Paid entry into war | 30 days |
| Protectorate or vassalage | 180 days |

Terms can specify a different validated duration. Reign records whether an agreement is public, when it began, when it expires, whether it remains active, why it ended, who broke it, and which war triggered the end.

## Diplomatic action families

Reign supports:

- declaring war, making peace, and tribute peace;
- reparations, settlement surrender, full surrender, war indemnity, and recognition of conquest;
- returning an occupied settlement and establishing a demilitarized border;
- promises, trade, non-aggression, alliances, defensive pacts, and guarantees;
- prisoner exchanges, ransom packages, and hostage guarantees;
- caravan protection, supply agreements, loans, and subsidies;
- paid neutrality or paid entry into a war;
- protectorate or vassalage arrangements;
- multi-part diplomatic packages; and
- backing a rebellion.

**Temporary Truce** and **Trade Embargo** remain recognized legacy action names but are deliberately retired/refused by the production executor. Reign does not pretend they succeeded.

## Native and recorded effects

Some agreements map directly to native Bannerlord operations, such as war, peace, tribute, prisoner transfer, gold transfer, settlement transfer, or joining a war. Others are durable Reign agreements whose obligations are enforced when relevant events occur.

A defensive pact can require the ally to join a defensive war. Reign records the parent war so that defensive-pact cascades cannot recursively create endless war chains.

## Treaty breaches

Starting an incompatible war can end trade, non-aggression, defensive, alliance, guarantee, or demilitarized-border agreements. Relationship consequences are directional:

| Broken agreement | Betrayed ruler's change toward breaker | Breaker's change toward betrayed ruler |
|---|---:|---:|
| Trade agreement | -20 | -5 |
| Non-aggression pact | -40 | -10 |
| Defensive pact, alliance, or guarantee | -50 | -10 |

Public war declarations and breaches also affect wider Public Standing and recorded diplomatic history. Pre-existing wars are seeded into the history ledger with an explicit “unknown prior origin” rather than assigned a fabricated cause.

## Political pressure and diplomacy

The pressure system described in Chapter 2 feeds this director. Positive/peaceful pressure tends toward peace, trade, security, or aid; hostile pressure tends toward expansion, coercion, treaty strain, or punitive demands. A ruler can choose domestic lords or preserve foreign relations, so diplomacy reflects internal politics rather than only military arithmetic.

---

# Chapter 10: Military command, party guests, War Council, duels, arrests, and rebellion

## Chapter summary

Reign lets accepted conversation and sovereign court decisions become durable military orders. It also adds temporary noble party guests, a map-based War Council, real one-on-one duels, two-step arrest authority with evidence and custody, and a complete civil-war system. Every path preserves native battles, parties, prisoners, and political consequences.

## Campaign Command Engine

When enabled, the Campaign Command Engine turns a sufficiently clear, authorized order into a persistent plan for an NPC commander's personal party or army. It never commandeers the player's main party.

Authority comes from either:

- the player's proven sovereign/command authority; or
- the commander's explicit agreement in the conversation or correspondence.

An order is drafted, clarified if required facts are missing, shown or confirmed where necessary, hashed so a vague “yes” cannot confirm altered terms, and then executed as bounded native steps. A reply such as “yes, but use 120 men” is a revision, not confirmation of the old plan.

Commanders can continue, adapt, request guidance, withdraw, refuse, or deviate according to authority, personality, safety, changing war state, geography, and the order's terms. Urgent reports can arrive through Correspondence with deadlines. Duplicate reports and duplicate native execution are suppressed.

### Complete Campaign Command objectives

1. Establish the commander's personal party.
2. Move to a named settlement or valid point.
3. Hold position until further orders.
4. Hold for a specified time.
5. Patrol a settlement or region for a specified time.
6. Scout a region and report.
7. Escort an identified party.
8. Recruit and resupply to specified troop/food targets.
9. Form an army and wait for invited lords.
10. Join an identified army.
11. Leave an army with the required authority or commander agreement.
12. Disband an army the commander controls.
13. Raid an identified hostile village.
14. Besiege and capture an identified hostile fortification.
15. Defend a settlement for a specified period.
16. Relieve an active siege.
17. Hunt enemy parties in a bounded region and time.
18. Engage an identified enemy party.
19. Withdraw to the nearest safe friendly settlement.
20. Return to the commander's home settlement.

Orders can compose multiple steps, such as recruit, patrol, and hold, or form an army and then besiege. Settlement capitulation/surrender is deliberately not a Campaign Command objective; it belongs to diplomacy and cannot be smuggled in as a movement order.

## Temporary noble party guests

An eligible foreign-clan lord or lady can temporarily travel inside the player's main party without becoming a companion or changing clan. Ask in ordinary one-on-one dialogue. The noble must be a living, active, free adult; belong to another clan; not be a companion, governor, prisoner, child, dead, quest-bound, in battle, or in a siege; and must explicitly consent. Leaving an army is a separate decision that also requires acceptance.

The terms must state the purpose, risk, relevance, and duration, and must ask rather than command. A fixed visit can last **1–30 days**. An open-ended visit has a mandatory review every **5 days**. The guest keeps their original clan, kingdom, identity, and equipment; their equipment is locked while the agreement is live. Accepting, renewing, or ending the guest arrangement is relationship-neutral and does not itself create traits, rumors, or reputation.

If the guest led a party, Reign leaves **[Name]'s Waiting Camp** on the map. This is the real original party, visibly paused and protected from encounters, reinforcement use, finance/desertion processing, and ordinary autonomous orders until the guest returns. It is not destroyed or secretly reassigned.

At a review, campaign time pauses. **Speak Now** opens a targeted Party Chat; **Let Them Depart** begins the return. The player may also ask to end the visit through dialogue, but the noble can refuse an early request. Return travel lasts `clamp(1 + distance ÷ 12, 1, 12)` hours and ends at the original party when possible or at a safe friendly fortification. The lifecycle—Active, Review Due, Departing, Returning, Completed, or Hostile Banishment Pending—survives saving.

War against the guest's own faction forces a review. Until the player explicitly acknowledges the risk, Reign excludes the guest from battle and restores them afterward. If the player accepts the risk and the guest fights their original faction, the result is banishment: after battle, the guest transfers to the player's clan, but only when the old clan can retain a valid successor. Ordinary arrest remains a separate system and cannot be smuggled into a guest agreement.

## War Council

Open the **Military** tab in Royal Court and then the War Council. The screen provides:

- a fixed close-scale parchment map that can be panned by click or drag;
- markers for kingdom parties, numbered with Roman numerals;
- infantry, missile, or mounted token classification based on dominant party composition, with ship tokens for parties currently at sea and black variants for the player's realm;
- all settlements and their hostility state;
- available kingdom lords, clan, current party, and order state;
- kingdom strength, clan/party/settlement counts, and war/peace relation;
- recent battle reports; and
- drag-and-drop orders.

The authored world image is **16,384×16,384**, loaded as sixteen 4,096-pixel tiles, and is intentionally held at scale **44** so the parchment remains readable. Mouse-wheel and zoom-button input do not change scale; clicks and drags pan within the map boundary. The settlement directory can center the map on any of the **440** catalogued settlements.

Realm parties are always visible. Foreign parties appear only when an intelligence sweep detects them. Sweeps run when the screen opens and every **3 days**. Detection range rises linearly from **70 map units at Tactics 0** to **520 at Tactics 300**. Detection chance rises linearly from **25% at Leadership 0** to **90% at Leadership 250**. The selected War Councilor supplies those skills; if no one is selected, the ruler does.

Available War Council orders are **Free Roam, War Council Hold, Patrol, and Attack**. Hold and patrol markers can target valid map locations or settlements. Attack must be dropped on a hostile village, town, or castle; villages become raid objectives and fortifications become besiege-and-capture objectives. An in-person command takes precedence over a map order. Right-click **Remove Order** to clear an existing council order.

The council can mobilize an eligible player-clan or same-realm lord who is alive, active, free, and either partyless or traveling in the player's main party. Mobilization requires a safe spawn settlement and recruiting funds equal to the greater of **5,000 denars** or Bannerlord's party-gold lower threshold. The native safeguards are rechecked when the party is created.

A commander already following an order issued elsewhere cannot be silently overwritten from the map. The **Raven** control opens direct correspondence for clarification, negotiation, or command; a spoken or written accepted command still follows the Campaign Command authority rules above.

### Battle ledger

The council records worldwide completed battles with battle/naval type, attacker, defender, winner, initial strengths, losses, captured lords, and settlement. It retains at most the **100 newest reports** and only reports from the last **30 days**. Battles involving the player's realm receive a bold red heading so they can be separated from background intelligence quickly.

## Duels

An NPC can agree to a duel during a one-on-one conversation. Reign then shows a **Bannerlord Reign Duel** prompt. Starting it opens the nearest suitable native arena/mission context; cancelling leaves the campaign unchanged.

Modes are:

- **Training/nonlethal:** both combatants normally begin at 100 health and the duel stops when either reaches 8 health. Nearby agents can be paused inside an 18-unit radius. Original player health is restored afterward.
- **Lethal/duel of honor:** death risk is explicit. The target defaults to 100% death risk on defeat; player death is disabled unless the validated terms explicitly allow it.

The winner receives Duelist social evidence and the loser Fallen Challenger. Arrest duels use the same combat path but convert victory into custody and defeat into escape.

## Arrests

Reign arrests are sovereign/custodial actions, not an unvalidated dialogue shortcut. The player can prepare an arrest only when jurisdiction is real:

- in a player-owned town or castle;
- for a character already in the player party; or
- during an active encounter with the accused's party.

The process is intentionally two-step. The first accepted action prepares guards and records the accusation. A second explicit confirmation orders the arrest. One character cannot have two open arrest cases.

### Charges, evidence, and consequences

| Severity | Justified relationship change | Unjustified relationship change | Accusation reputation value |
|---|---:|---:|---:|
| Minor | -6 | -12 | -8 |
| Serious | -3 | -20 | -15 |
| Grave | 0 | -30 | -25 |
| Capital / treason | +8 | -45 | -40 |

Reign looks for cause and evidence in the objective/known record. Evidence can be hidden from the player while still establishing cause. Every accusation receives a unique reputation tag, preventing one rescinded case from deleting another case's public consequence.

In a settlement or the player party, confirmed custody moves the accused to the valid player destination. During a party encounter, the accused may surrender. Refusal offers a nonlethal duel or a native party battle. Winning the duel captures the accused; losing lets them escape. A battle counts only if native battle resolution actually places the accused in player custody.

Protected prisoners from the player's clan or own kingdom cannot be released by ordinary automatic escape/release paths. Death, explicit player release, and release after battle remain valid. **Release** frees the prisoner while retaining the accusation. **Rescind** clears the accusation, reverses its synchronized effect, and releases the accused.

## Rebellion and civil war

Each week, every eligible free, living, non-mercenary NPC vassal clan leader with relation below **-20** to the ruler receives an independent d100 roll. A result of **1–5** begins a rebellion: a 5% chance per eligible leader. Only one rebellion can exist in a kingdom. If several qualify, the lowest ruler relation wins, then clan ID breaks a tie. A resolved kingdom has a **63-day cooldown**.

Other vassals choose the rebel side when their relation to the rebel leader is greater than their relation to the ruler. Sides are then fixed; they do not oscillate every tick.

### Player participation

A non-ruling player vassal can declare rebellion through accepted chat or correspondence at any ruler relation. A mailed player declaration executes on dispatch; ordinary replies and physical summons still travel normally. The player begins alone as a real rebel kingdom and recruits a lord only after unambiguous acceptance. The accepted lord's whole clan moves.

NPC rebellions can involve pledges, refusals, named informers, delivered letters, a **7-day summons**, renunciation, defiance, and ruler verdicts such as pardon, imprisonment, exile, or execution.

### Victory and reunification

The war's objective is the throne:

- loyalists win when the rebel leader is captured by loyalists, killed, or surrenders;
- rebels win when the ruler is captured by rebels, killed, deposed, or surrenders;
- capture by an unrelated foreign power is not decisive;
- ordinary peace cannot bypass the civil-war resolution.

The winner reunifies the kingdom and returns clans from the temporary rebel realm. There is no separate rebel succession chain.

### Judgment of the defeated

Each defeated clan leader is judged. For an NPC victor, the score is:

`d100 + 15 × Mercy trait level + round(relation / 5)`

- 67 or higher: freedom and +30 relation;
- 34–66: imprisonment and +10 relation;
- 33 or lower: execution.

A victorious player judges leaders sequentially, and the queue survives saving. A losing player is judged by the same system and may die if campaign death is enabled.

---

# Chapter 11: Kingdom catastrophes, boons, and economic simulation

## Chapter summary

Reign changes settlement economics so soldiers, raids, food, recovery, patrols, security, and inter-settlement relief have persistent costs. It also adds rare kingdom-wide catastrophes and boons that modify real native economic and political values.

## Kingdom events

When enabled, Reign makes one saved deterministic **1% global roll per day** for a kingdom catastrophe or boon. Events are announced, recorded in World History, and normally last **30 days**. Only one eligible target is selected through production rules; forcing a debug event does not consume or alter the natural roll.

### Harmful events

| Event | Effect |
|---|---|
| Famine | -35% effective village food production |
| Pestilence | At least -1 village hearth per day, respecting the Reign hearth floor |
| Crime Wave | -0.5 security per day in towns and castles |
| Trade Collapse | -1 prosperity per day |
| Construction Stagnation | -25% construction power |
| Political Unrest | -0.5 loyalty per day |
| Border Crisis | Immediately creates one eligible new war |

### Beneficial events

| Event | Effect |
|---|---|
| Bountiful Harvest | +35% effective village food production |
| Population Boom | +1 village hearth per day |
| Law and Order | +0.5 security per day |
| Trade Boom | +1 prosperity per day |
| Golden Age | +25% construction power |
| National Unity | +0.5 loyalty per day |
| Grand Reconciliation | Immediately ends one eligible ordinary war |
| Orderly Succession | An eligible NPC ruler abdicates to a ruling-clan heir; the player ruler is excluded |

Reign keeps the newest **128 completed kingdom-event records**.

## Village devastation and recovery

A looted village begins at recovery step 0 of 10. Its effective contribution is `step / 10`. Each normal daily village tick has a 30% chance to advance one step, so recovery is gradual and uncertain rather than immediate.

Each damaged bound village contributes its own persistent, recovery-scaled **-2 security** penalty to the linked town. Reign removes Bannerlord's single non-stacking looted-village penalty before adding these per-village effects, so multiple devastated villages matter independently.

Siege aftermath applies an immediate security shock:

- Mercy: -5 security
- Pillage: -15 security
- Devastate: -30 security

## Recruitment consumes population

Ordinary volunteer recruitment consumes **0.5 village hearth per troop**. A village cannot be reduced below **10 hearth**, and a recovering village must be at least 30% recovered before it can supply recruits. The available recruit capacity shown to AI and player recruitment is derived from the total safe hearth reserve of eligible source villages.

Recruitment source is weighted by available hearth, spreading the burden among villages. Prisoners, mercenary/map recruitment, and other recruitment without a notable village source do not consume hearth.

Mobilization strain also reduces food contribution while it remains large relative to village hearth:

| Strain / hearth | Daily food penalty |
|---:|---:|
| below 10% | 0 |
| 10%–19.99% | -1 |
| 20%–34.99% | -2 |
| 35% or more | -4 |

Strain decays daily by multiplying it by 0.95 and then subtracting 0.5.

## Real household retinues and patrol manpower

When an NPC lord party is created, Reign removes magically spawned non-hero troops and gives it a **10–15 troop** culture-appropriate household retinue of tier 3 or lower. Duplicate initialization is blocked.

Settlement patrols are not free troops. A patrol can form only when its home garrison has at least **5 healthy troops**. Its roster is transferred from the garrison, favoring lower tiers, and replenishment returns surviving patrol troops before transferring a new roster. When a patrol is removed outside combat, its troops return to the garrison.

## Realm food relief network

Every day, towns and castles in the same kingdom—or the same independent clan—can move food to endangered holdings.

A donor must be outside siege, have nonnegative food change, and hold more than 70% of capacity. It keeps a 60% reserve and ships at most **10 food per day**. A recipient is below 30% capacity or is projected to fall below that threshold within 7 days. Reign tries to raise it toward 50% capacity, with at most **15 delivered food per recipient per day**.

Routes over 180 map units are invalid. Base efficiency is 90% within 60 units, 80% from 60–120, and 70% from 120–180. Low endpoint security can reduce efficiency, but it remains within 70%–90%. A successful receipt gives +0.05 daily loyalty; an unfed, empty, declining holding gets -0.1. Total relief loyalty is clamped to -0.2 through +0.2. Besieged recipients cannot receive relief.

Economic reports expose donor, recipient, shipment, delivery, loss, blocked request, mobilization, recovery, and security-shock evidence to court and diagnostics.

---

# Chapter 12: Portraits, event art, the Memory Book, and interface changes

## Chapter summary

Reign can replace supported native portraits with persistent AI portraits, illustrate memories and events, add contextual tavern and castle art, and present its major screens through a shared black-marble and antique-gold interface system. It also makes smaller interface changes that support hidden information and keyboard use.

## Modern Reign interface system

The Court, Government, Royal Council, Economic Report, War Council, Family Chambers, ruler-petition audience, Party Chat, Correspondence, social and wilderness events, Memory Book, Spymaster, Character Editor, preparation, and related Reign screens share one modern visual language: black marble, antique-gold framing and ornaments, Reign serif typography, fixed portrait apertures, and explicit hover/selected/disabled states.

This is more than a decorative overlay. Superseded legacy frames and rectangles are disabled so they cannot show through while scrolling or scaling, portraits are clipped inside declared apertures, and dynamic lists, names, numbers, maps, banners, and scenes remain inside their reserved bounds. A screen that looks materially older than the surrounding Reign interfaces may therefore indicate a stale asset or installation rather than an intentional alternate theme.

## AI portraits

When enabled, Reign captures the native hero appearance, combines it with verified identity, age, culture, gender, clothing, visible status, and authored portrait rules, and generates a persistent portrait. Shared portraits can be pregenerated for shipped characters; campaign-specific portraits cover dynamic identity and campaign changes.

The cache distinguishes:

- `source.png`: native source capture;
- `portrait.png`: active master image;
- `custom.png`: manual override when present;
- derived thumbnail, party, normal portrait, and zoom tiers.

Derived images are generated from the master so each UI surface gets an appropriate size. The same cache key is used across supported conversation, party, game-menu, and Reign screens. A portrait zoom button opens a larger preview.

Image providers are configured only in the local Control Center: NanoGPT, AtlasCloud, or the local **Reign Image Generator**. Provider API keys stay on the local server.

## Memory Book and memory images

Reign inserts a Memory Book control into the quest interface and can open it as an overlay. Illustrated memories combine stored event facts, participants, perspective, and portrait references. The book is a visual view over persistent memory; deleting or changing an illustration does not rewrite the underlying event.

## Castle, event, and tavern art

- Castle rooms can generate first-person room scenes from culture, time block, settlement, ordered participants, and a label-free contact sheet.
- Social and wilderness events can display generated event art.
- Ruler-petition audiences use one of six culture-specific throne-room references—Empire, Vlandia, Sturgia, Battania, Aserai, or Khuzait—plus the petitioner's portrait to compose a 2048×1152 scene; unknown cultures use the approved fallback.
- Tavern menus display a random culture-appropriate scene inside a Reign frame for the duration of that tavern entry.

Castle imagery has its own MCM switch and can remain on when general AI portrait replacement is off.

## Main menu and branding

Reign replaces the supported main-menu logo and video presentation with Reign assets. These are presentation changes only and do not alter campaign state.

## Hidden-information UI

Where exact NPC relations would normally appear, Reign removes or replaces the number while retaining layout, portraits, banners, quest markers, and legitimate non-relationship information. Party/game-menu portrait items can display a hidden-relation marker instead of leaking the number. The reveal cheat restores exact values.

## Popup keyboard handling

Reign-owned inquiries consistently accept:

- Enter or Numpad Enter for an enabled affirmative action;
- Escape for an enabled cancel action; or
- Escape as acknowledgement when the only affirmative label is Acknowledge, OK, Okay, Close, or Continue.

This applies only to inquiries opened by Reign and does not remap other mods' or native Bannerlord popups.

## Small safety and compatibility changes

Reign also:

- prevents an encyclopedia tick from crashing when Bannerlord has already removed its active UI layer;
- recovers a native delayed-teleport list race by deferring remaining teleports one hour;
- keeps portrait overlays from erasing banners and quest markers;
- sanitizes repeated or impossible generated stage directions; and
- preserves exact skill visibility even while relationship numbers are hidden.

---

# Chapter 13: Saving, timelines, backups, and the Control Center

## Chapter summary

Reign stores much more campaign state than fits naturally inside a Bannerlord save. Save Sync binds the native save to an immutable server snapshot and restores the matching timeline on load. The Control Center manages those campaigns, AI settings, characters, memory, logs, and advanced verification tools.

## Save Sync identity

Save Sync is enabled by default. Each native save point receives a unique Reign identity and immutable server snapshot. Copying or renaming a `.sav` file does **not** create a new Reign point; the identity travels inside the save.

Before a native save, Reign requests an urgent bounded flush and freezes the corresponding server generation. If the server is offline or registration fails, Bannerlord's native save can still succeed, but Reign warns that the point is unprotected. Start the server and save again to create a protected alignment point.

Loading a protected point restores the exact matching Reign generation and timeline. Loading an older save is a legitimate time branch: later server history does not bleed backward into it.

## Unique-state limit

Save Sync supports **15 unique protected states**. It warns at **10**. Identical save content can share a snapshot. Overwriting a native save retires its superseded unique point when safe. Deleting through Bannerlord's native save UI removes the matching Reign point only after native deletion succeeds.

A 16th unique save remains a valid native save but is unprotected until an older point is removed. This fail-open native behavior prevents Reign from destroying a player's ability to save.

## Campaign backups

The Control Center's **Campaign Backups** tab can:

- list authoritative campaigns and their latest native heartbeat;
- enable or disable Save Sync;
- preview alignment and last loaded point;
- export a `.reignbackup`/zip archive;
- import an archive, optionally replacing the same campaign ID;
- delete one confirmed campaign; or
- delete all confirmed Reign campaign data.

An archive contains the campaign's PostgreSQL world memory, character files, histories, relationships, identity knowledge, actions, diplomacy, events, audit data, generated portraits, source images, external portrait cache, and Save Sync metadata. It excludes global API keys. Keep the matching native Bannerlord save with the archive.

Deleting Reign campaign data does not delete native Bannerlord saves and preserves global settings, API keys, shared pregenerated portraits, and test definitions. Deletion is destructive and confirmation-gated.

## Control Center tabs

The dedicated Control Center contains:

| Tab | Purpose |
|---|---|
| General | Server address, lifetime explanation, health |
| Campaign Backups | Save Sync, campaign export/import/deletion |
| LLM Backend | OpenAI-compatible endpoint, key, temperature, tokens, prompt limits, cache probe |
| Models | Per-request dialogue, construction, diplomacy, events, memory, relationship, correspondence, strategy, and helper models; private reasoning policy |
| Action Router | Hidden action planner, legacy fallback router, preview and diagnostics |
| AI Image Generation | Portrait provider, keys, models, generation settings, shared portrait library |
| Characters | Complete Character Editor and revision history |
| Character Memory | Raw continuity, semantic retrieval, vector provider, reindex/retry, consolidation |
| Prompting | Editable general, context-pull, action, and relationship prompts |
| Castle Chat | Per-culture, per-room image and dialogue prompts |
| Diagnostics | Server/key/action/portrait checks and failed-character retry |
| Logging | Logging, telemetry, provider concurrency/timeouts/circuit breaker, replay capture |
| Log Viewer | Read, download, or clear selected operational logs |
| Audit Viewer | Correlated prompt → model → plan → validation → execution timeline |
| NPC Dialogue Audit | Long-form production dialogue quality audit |
| World History | Inspect objective campaign events and knowledge continuity |
| NPC Dialogue Lab | Controlled dialogue experiments |
| Relationship Director | Inspect and exercise relationship processing |
| World Test | Inspect background world simulation and rumors |
| Test Lab | Feature and contract test controls |
| Live Test Bridge | Explicitly armed native-game test bridge |
| Verification Lab | Offline-first quick, offline, live-model, and game verification |

The audit, test, live-bridge, and lab entries are primarily developer/acceptance tools. They can mutate test data or consume provider calls and should not be used casually on a valued campaign.

## LLM and memory privacy boundaries

The local server owns API keys and model routing. Bannerlord sends structured facts and prompts to the server; keys never enter the game client. Provider reasoning output is excluded and not stored. Reign stores visible replies and compact decision briefs.

Prompt limits record section sizes, warn on unexpectedly large context, deduplicate ordinary dialogue, and block a pathological request rather than silently deleting critical evidence. Semantic memory uses local vector storage by default. External Qdrant receives vectors and metadata, not the raw campaign prose.

---

# Chapter 14: Complete MCM settings reference

## Chapter summary

The Mod Configuration Menu controls which production systems are active in Bannerlord. Server model, memory, image-provider, logging, and backup settings live in the Control Center instead. Disabling a feature prevents new work; it does not necessarily erase already-persisted state.

## General

| Setting | What it controls |
|---|---|
| Enable Bannerlord Reign AI World | Master switch for Reign's AI-backed campaign integration |
| Enable Social Events | Scheduled town events and their player UI |
| Enable Tournament Celebrations | Tournament-linked feast/fair/dance opportunities |
| Enable Party Chat | Group conversation screen and hotkey |
| Party Chat Hotkey | Defaults to backslash (`\`) |
| Enable Correspondence | Letter screen, delivery, replies, and unread state |
| Correspondence Hotkey | Defaults to `Ctrl+M` |
| Enable Court System | Rule Mode, ruler petitions, Government, capital, offices, councils, Family Chambers, and court tabs; enabled by default for new configurations while an explicitly saved Off value remains honored |
| Castle Chat Image Generation | Room backgrounds, independent of the general AI Portraits switch |
| Enable Passive NPC Relationships | Background directional relationship director |
| Ambient Relationship Drift (no LLM) | Small deterministic personality/co-presence drift |
| Use Reign-Controlled NPC Marriage | Replaces native random NPC matchmaking; player courtship remains native |
| Restrict Pregnant NPCs From Combat | Withdraws pregnant NPCs from parties and blocks field/combat orders |
| Enable Generated Wilderness Events | Hourly open-map party encounters |
| Enable Conversation Relationship Changes | Applies validated conversational conduct to affinity |
| Enable Kingdom Catastrophes and Boons | Enables the daily 1% kingdom-event roll |

## Execution

| Setting | What it controls |
|---|---|
| Execute Diplomacy Actions | Allows validated diplomacy plans to mutate native state |
| Execute Strategy Plans | Allows validated recruit/army/settlement strategies |
| Execute Internal Politics Actions | Allows validated clan, claimant, rebellion, exile, and mediation politics |
| Execute Conversation Actions | Allows accepted individual dialogue to execute bounded regular actions |
| Enable Campaign Command Engine | Persistent multi-step NPC party and army orders |
| Enable Ruler-Driven NPC Diplomacy | Autonomous NPC-ruler diplomacy; player's realm remains excluded until represented safely through court |

## Local server

| Setting | What it controls |
|---|---|
| Use Local Server | Sends Reign work to the configured local companion server |
| Local Server URL | Normally `http://127.0.0.1:5101` |
| Receive Server Action Commands | Allows the client to pull, validate, execute, and receipt queued server actions |

## AI portraits

| Setting | What it controls |
|---|---|
| Enable AI Portraits | Replaces supported native portraits with cached Reign portraits |

## Cheats

| Setting | What it controls |
|---|---|
| Reveal All Character Relationships | Restores exact relationship numbers; skills are visible regardless |

## Debug controls

These are developer tools, not ordinary campaign features. Use them on disposable saves where they can change state.

- Show Debug Messages.
- Enable Debug Controls.
- Start a town social event.
- Start/force a wilderness event.
- Make the player know every living hero.
- Prepare a Court test realm.
- Trigger a random ruler-diplomacy roll.
- Force a selected kingdom event.
- Start, stop, or report the NPC Dialogue Audit.
- Diagnose, generate, reset, or otherwise inspect portrait identity/cache state.

## Utilities

- **Open Party Chat** opens the screen without using the hotkey.
- **Open Correspondence** opens the letter screen without using the hotkey.

---

# Chapter 15: Complete executable action reference

## Chapter summary

This is the complete production action vocabulary exposed by Reign's client contracts. The presence of an action does not mean any NPC may perform it at any time. Every action still requires resolution, authority, current-state validation, feature settings, and a successful native executor receipt.

## Diplomacy actions

| Action | Purpose |
|---|---|
| Declare War | Create a native war with recorded origin |
| Make Peace | End an eligible native war |
| Offer Tribute Peace | Peace with daily tribute and duration |
| Record Promise | Persist a public or private diplomatic commitment |
| Demand Reparations Peace | Peace package with payment |
| Demand Settlement Peace | Peace package requiring a settlement |
| Demand Surrender Peace | Full surrender terms |
| Sign Trade Agreement | Public trade agreement, default 120 days |
| Sign Non-Aggression Pact | Public restraint agreement, default 90 days |
| Sign Alliance | Public alliance, default 180 days |
| Sign Defensive Pact | Defensive war obligation, default 120 days |
| Sign Temporary Truce | Retired; production refuses it |
| Break Treaty | End a selected active agreement and record breaker |
| Exchange Prisoners | Transfer agreed eligible prisoners |
| Ransom Package | Transfer prisoners and payment as one package |
| Hostage Guarantee | Record/transfer a hostage-backed guarantee |
| War Indemnity | Payment consequence of war |
| Recognize Conquest | Record recognition of existing ownership |
| Return Occupied Settlement | Transfer a validated occupied settlement back |
| Demilitarized Border | Record bounded border-restraint terms |
| Trade Embargo | Retired; production refuses it |
| Caravan Protection | Record commercial route protection |
| Supply Agreement | Record and execute supported supply terms |
| Loan or Subsidy | Transfer/record financial support |
| Pay to Stay Neutral | Private or public neutrality arrangement |
| Pay to Join War | Payment plus validated entry into a named war |
| Guarantee Independence | Promise to defend a realm's independence |
| Protectorate or Vassalage | Establish a dependent political relationship |
| Diplomatic Package | Atomic multi-term package |
| Back Rebellion | Foreign support for an active rebellion |

## Strategy actions

| Action | Purpose |
|---|---|
| Recruit and Recover | Rebuild a party under bounded conditions |
| Form Army | Create a native army when eligible |
| Attack Settlement | Begin a validated hostile settlement operation |
| Capture Settlement | Complete/represent the capture objective through native state |

## Internal politics actions

| Action | Purpose |
|---|---|
| Start Ruling-Clan Rebellion | Begin the autonomous civil-war flow |
| Install Ruling Clan | Change the ruling clan after a validated political outcome |
| Marriage Alliance | Create a validated marriage-backed alliance |
| Support Claimant | Back an eligible claim |
| Encourage Clan Defection | Move an accepting eligible clan |
| Exile Clan | Remove a clan under validated authority |
| Restore Exiled Clan | Restore an eligible exiled clan |
| Mediate Clan Dispute | Resolve a recorded internal dispute |
| Resolve Civil War | Apply winner, reunification, and postwar state |
| Recruit Lord to Rebellion | Move an accepting lord's whole clan to the rebels |
| Join Rebellion | Player or clan joins an active rebellion |
| Surrender Rebellion | Accepted decisive surrender |
| Resolve Rebellion Pledge | Apply pledge/refusal/report outcome |
| Resolve Rebellion Summons | Apply renounce/defy/ruler verdict outcome |
| Consent to Government Reduction | Record an NPC's explicit, level-specific consent to the ruler's next one-level authority reduction; a clan leader may cover their clan |

## Court-bound executable decisions

These decisions use dedicated Court or feature contracts rather than the free-form world-action planner, but they still revalidate current state and produce a durable result:

| Decision | Purpose |
|---|---|
| Grant Petition Directly | Deduct the exact gold, food, or troops and begin the bounded commitment |
| Fund Petition Instead | Pay the validated food or soldier gold substitute |
| Refuse Petition | Record refusal, directional relation loss, and the repeat cooldown |
| Appoint / Dismiss Chancellor | Begin or end the explicitly agreed capital-resident office |
| Activate / Deactivate Chancellor | Schedule the paid service state for the next 08:00 boundary |
| Appoint / Dismiss Court Office | Change one persistent eligible officeholder |
| Select / Clear War Councilor | Change whose Tactics and Leadership drive War Council intelligence |
| Increase Government Authority | Apply the next authority level and its public benefits |
| Negotiate / Force Government Reduction | Resolve the two-thirds vote or knowingly apply forced-reduction fallout |
| Approve / Override Government Action | Resolve a pending action according to the current authority level |
| Lobby Government Member | Attempt persuasion, bribery, or a recorded deal once for that debate |
| Call Resolution Vote | Run the final individual vote across all occupied seats |
| Fulfill Lobbying Deal | Mark explicit promised terms complete before their deadline |
| Post / Direct / Recall Ambassador | Change a persistent posting or its bounded mission |
| Appoint Resident Advisor | Move an eligible Economic or Foreign Advisor into capital residence |

## Regular conversation and world actions

### Movement, parties, and combat

- Follow the player on the map.
- Follow the player in the current scene.
- Stop following.
- Go to a named settlement.
- Patrol around a named settlement.
- Wait near a named settlement.
- Raid a village.
- Besiege a settlement.
- Create a party.
- Show the way to a destination.
- Attack an identified party.
- Attack the player's party.
- Have the player attack an identified party.
- Surrender to the player.
- Leave the player alone/end hostility.
- Kill a character when lethal authority and safety allow it.
- Duel the player through the mission-backed duel system.

### Property and status

- Give gold to the player.
- Transfer gold between validated parties.
- Transfer an item.
- Transfer a workshop.
- Transfer a prisoner.
- Hire or dismiss the player as a mercenary.
- Offer or dismiss player vassalage.
- Join or leave a clan.
- Join or leave a kingdom.
- Hire a mercenary clan.
- Execute an atomic trade package.

### Arrest and command

- Prepare an arrest.
- Confirm a prepared arrest.
- Release an arrested character while keeping the accusation.
- Rescind an arrest accusation and clear/release the accused.
- Issue a Campaign Command order.
- Revise a Campaign Command order.
- Respond to an order report.
- Cancel a Campaign Command order.

### Temporary noble party agreements

- Accept a Temporary Party Guest agreement after explicit consent and complete terms.
- Renew or revise an active Temporary Party Guest agreement with fresh consent.
- End an active Temporary Party Guest agreement and begin the protected return journey after the guest accepts departure.
- Acknowledge Own-Faction Combat Risk during the mandatory hostility review.

## Why an accepted action can still fail

Common legitimate rejections include missing or ambiguous identity, no physical presence, no ownership, no sovereign authority, an NPC refusing, insufficient gold/items/troops, wrong war state, active siege/battle, pregnancy restriction, imprisonment, an invalid temporary-guest term or unsafe succession, a stale target, a changed save timeline, a disabled execution setting, or an unsupported native adapter. Reign records the rejection rather than converting generated intent into success.

---

# Chapter 16: Exact rules and formulas quick reference

## Chapter summary

This chapter collects Reign's most consequential numeric rules in one place for fast lookup. It is a companion to the explanations in earlier chapters: use those chapters for context, eligibility, and exceptions, and use these tables when you need the shipped interval, probability, threshold, cap, duration, or formula.

## Social and family

| Rule | Exact value |
|---|---:|
| Rumor lifetime | 45 days |
| Rumor promotion | 0%, 30%, 60%, 90% at exposures 1, 2, 3, 4+ |
| Negative Standing Charm mitigation | Linear to 60% at Charm 300 |
| Cross-kingdom Standing strength | 50% |
| Flirtation | enter 30, reset 25 |
| Lovers/growing | enter 50, reset 45 |
| Serious romance | enter 70, reset 60 |
| Lovers end | either direction below 30 |
| Estranged spouse | either direction at or below -30 |
| Divorce queue | either direction at or below -50 |
| Organic marriage opportunity | 10% daily when eligible |
| Lovers' conception opportunity | 5% |
| Player-parented conception source | Qualifying intimate event in individual Reign chat only; native pregnancy involving the player is disabled |
| Valid mother age | 18–45 |
| Fling Judgment ceiling | 40 |
| Fling initial spark / cap | 25% / 50% |
| Fling cooldown / discovery | 7 days / 20% |
| Pregnancy commitment Honor bonus / cap | +10 / 90% |
| Marriage / breakup affinity | +30 / -70 |
| Family Chambers conversation size | player plus 1–4 NPCs |
| Child identity crop confidence | one face, at least 0.70 |
| Child identity age refresh | after at least 2 years |
| Adult-retained childhood memories | created at exact age 5 or older |
| Family Chambers client-session retention | 30 days |
| Temporary guest fixed term | 1–30 days |
| Temporary guest open-ended review | every 5 days |
| Temporary guest return travel | `clamp(1 + distance ÷ 12, 1, 12)` hours |

## World and economy

| Rule | Exact value |
|---|---:|
| Reign year / season | 126 days / 31.5 days |
| Autonomous-world startup grace | 5 days |
| Ordinary social-event attempt | every 3 days, 25%, max 2 open |
| Wilderness attempt | 2% hourly after 3-day gap |
| Wilderness participants | 1–3 |
| Kingdom-event natural roll | 1% daily |
| Kingdom-event duration | 30 days |
| Recruit hearth cost / floor | 0.5 per troop / 10 hearth |
| Village recovery | 10 steps, 30% chance per normal day |
| NPC household retinue | 10–15 troops |
| Minimum patrol source | 5 healthy garrison troops |
| Relief route limit | 180 map units |
| Relief donor / recipient limits | 10 shipped / 15 delivered per day |
| Protected Save Sync states | 15; warning at 10 |
| Petition opportunities | 1–5 deterministic opportunities daily; only real need creates a petition |
| Refused-petition repeat cooldown | 3 days for that settlement and petition kind |
| Chancellor default salary / boundary | 500 denars daily / changes at next 08:00 |
| Chancellor delegation Absent Lord threshold | 15 Active days in a rolling 30-day window |
| Chancellor post-captivity handoff | 3 days |

## Politics, command, and covert work

| Rule | Exact value |
|---|---:|
| Political-pressure daily incident roll | 43% |
| Pressure action eligibility | magnitude at least 10 |
| Pressure action chance | 0.5 × magnitude, cap 50% |
| Rebellion trigger | weekly 5% per eligible leader below -20 relation |
| Post-rebellion cooldown | 63 days |
| Rebellion summons | 7 days |
| War Council battle history | newest 100, last 30 days |
| War Council map scale | fixed at 44; pan only |
| War Council foreign sweep | on open and every 3 days |
| Foreign-party detection range | linear 70 at Tactics 0 to 520 at Tactics 300 |
| Foreign-party detection chance | linear 25% at Leadership 0 to 90% at Leadership 250 |
| Government initial authority | L1 10%, L2 25%, L3 30%, L4 25%, L5 10% |
| Government party count | 45% two, 45% three, 10% four only with at least 12 seats |
| Government trust and loyalty bounds | 0–100 |
| Government seat reconciliation | every 7 days |
| Government meeting interval | 21 days |
| Government reduction approval | at least two thirds of all occupied seats |
| Government resolution passage | strict majority of all members |
| Government level-3 delay | exactly 7 days |
| Government resolution deadlines | minor 7, standard 14, major 21 days |
| Government lobbying chance | `clamp(base + Charm ÷ 8 + relation ÷ 5 + traits - party loyalty ÷ 4 - Government loyalty ÷ 10, 3, 80)` |
| Lobby success shift | persuasion +15, bribery +20, deal +25 |
| Campaign Command objectives | 20 |
| Spymaster capacity | 3 missions |
| Spymaster success | `clamp(52 + .18R - difficulty, 5, 95)` |
| Spymaster notice | `clamp(base × (1.5 - R/300), 5, 95)` |
| Foreign-agent cycle | 5 days |
| Hidden operation detection / attribution | 25% / 35% |

---

# Chapter 17: Feature locator and glossary

## Chapter summary

This chapter is the discovery index for subtle or easily missed behavior. The locator maps an observation back to the responsible system and detailed chapter; the glossary gives stable meanings to Reign-specific terms; the maintenance note defines this document's role as the canonical living player guide.

## “I did not know Reign did that” locator

| Observation or question | Reign system |
|---|---|
| A pregnant lord stopped leading a party | Pregnancy battle protection; Chapter 5 |
| My spouse and I never have a background pregnancy | Native pregnancy involving the player is disabled; a qualifying intimate event in individual Reign chat is the only player-parented conception path; Chapter 5 |
| A Proceed/Pull Out popup appeared before possible conception | Pregnancy-choice gate; Chapter 5 |
| Exact relations vanished but skills remain | Hidden-information UI; Chapters 3 and 12 |
| Years seem longer | 126-day calendar; Chapter 2 |
| A village stopped offering recruits | Manpower/hearth economy; Chapter 11 |
| An NPC dungeon holds prisoners longer | Equalized NPC dungeon hold rate; Chapter 2 |
| A stranger remembers a battle but another does not | World History versus knowledge receipts; Chapter 2 |
| A rumor disappeared but the reputation remained | Rumor promotion/correction; Chapter 6 |
| A ruler favors one courtier and others grow jealous | Favors [person] and directional projection; Chapter 6 |
| A foreign ruler changes policy without a random war popup | Political pressure and diplomacy; Chapters 2 and 9 |
| A town sends food to another town | Realm relief network; Chapter 11 |
| Patrol size reduced a garrison | Real patrol manpower; Chapter 11 |
| A new family of nobles lives in a castle | Authored court households; Chapter 3 |
| An NPC's “yes” did not transfer the item | Action validation and receipts; Chapters 1 and 15 |
| A commander asks for clarification or sends a report | Campaign Command Engine; Chapter 10 |
| A noble travels in the main party but remains in another clan | Temporary noble party guest; Chapter 10 |
| A named Waiting Camp appeared on the map | Protected original party of a temporary guest; Chapter 10 |
| A guest is withheld from battle against their own faction | Mandatory guest hostility review; Chapter 10 |
| Foreign parties appear and disappear from the War Council map | Tactics/Leadership intelligence sweeps; Chapter 10 |
| War Council zoom controls do not change scale | The current map uses fixed scale 44 and pan-only navigation; Chapter 10 |
| A petition remains after the ruler cannot afford it | Need-backed ruler docket; Chapter 7 |
| No petitions appear while the Chancellor is paid | Active Chancellor suppression; Chapter 7 |
| A council report did not execute its advice | Royal Council reports are advisory only; Chapter 7 |
| The Government delays, pressures, approves, or blocks a royal act | Representative Government authority; Chapter 7 |
| A party speaker asks to meet every three weeks | Government's 21-day meeting cycle; Chapter 7 |
| A family member or child can be selected from the Keep | Family Chambers; Chapter 7 |
| A foreign envoy cannot grant an alliance | Ambassador authority charter; Chapter 7 |
| The player must confirm an arrest twice | Arrest safety and jurisdiction; Chapter 10 |
| Loading an older save restores older social history | Save Sync timeline branch; Chapter 13 |

## Glossary

**Affinity:** Reign's directional interpersonal relationship value.

**Campaign Command:** A durable, bounded multi-step order for an NPC commander, party, or army.

**Chancellor:** The separately salaried ruler-docket office that suppresses ordinary petitions while active and paid.

**Court noble:** One of Reign's 720 authored nobles in 120 landless households.

**Derived reputation:** A computed label produced from a pattern of active social facts rather than a normal manually created tag.

**Foundation traits:** Reign's 43 private personality dimensions, represented at -2 through +2 and as stable percentages.

**Government authority:** The five-level power of a kingdom's representative institution to advise, pressure, delay, approve, or block ruler actions.

**Knowledge receipt:** Evidence that a particular character learned a particular event at a particular time and source.

**Native projection:** A controlled aggregate written back to Bannerlord when the base game requires a single value such as relation.

**Objective history:** Facts the campaign actually recorded, independent of what any character believes.

**Public Standing:** The summed positive and negative social contribution of active rumors and reputations.

**Reign point:** An immutable server snapshot bound to a native Bannerlord save through Save Sync identity.

**Reputation:** A durable public conclusion established through repetition or a decisive event.

**Rumor:** A temporary, uncertain, second-hand social report, normally lasting 45 days.

**Rule Mode:** The place-bound court interface available to the player ruler in an eligible kingdom town or castle after designating a capital.

**Royal Council:** The advisory body comprising the War Councilor, Spymaster, Economic Advisor, and Foreign Advisor.

**Temporary party guest:** A consenting noble who travels inside the player's party for a bounded or review-based term without changing clan or kingdom.

**Waiting Camp:** A temporary guest's protected original mobile party, paused on the map until return.

**Timeline:** The branch of World History, character state, memory, and relationships aligned to a particular save lineage.

**World History:** Reign's objective, campaign-scoped event ledger.

## Maintaining this guide

This file is intended to remain the single canonical player and feature guide. Future feature work should update the relevant chapter, exact-value table, MCM reference, executable-action reference, and feature locator in the same change whenever behavior, availability, naming, defaults, or player discovery changes. Retired behavior should be labeled as retired rather than silently removed, preserving the guide's usefulness across campaign versions.
