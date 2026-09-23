# Conversation continuity repair plan — 2026-09-21

Status (2026-09-22): implemented and offline validated; deployment artifacts retained. Provider/native/human acceptance remains open. Deployment and live campaign repair are held at the user's request. Sections below retain the original investigation and planned acceptance criteria; the implementation receipt at the end records current evidence.

## Priority and outcome

Accurate, natural roleplay is the primary requirement. Preserve established events, the current physical scene, chronology, speaker identity, character knowledge, completed decisions, enduring personal concerns and unfinished intentions across turns, reopened chats and reloads. The user additionally requires established intimacy and shared relationships to be very dominant in prompting. Treat that supported pair history as a protected behavioral foundation, not optional background. Token cost and response speed are secondary optimizations and must not remove evidence needed for the next reply.

The user explicitly permits more input context when necessary and subsequently authorized implementation and validation, stopping before deployment. The 32k/48k estimated input policy below is implemented; larger expansion requires verified route capacity. No provider comparison, live campaign repair or deployment is authorized by the current build-and-validate request.

For the reported case, the desired behavior is concrete:

- Hulara continues the established morning conversation in Michael's tent near Chaikand until an actual scene transition occurs.
- The previous day's "tomorrow, when sober" deferral remains historical once that later discussion has occurred; genuinely unresolved terms may remain unresolved.
- When Michael mentions fighting a Khan, Hulara can connect it to the previously discussed tournament victory over Monchug and their shared drinks. Her knowledge comes from what she was told and discussed, without inventing eyewitness attendance.
- She responds in her own voice. Remembering an event does not require reciting a recap, agreeing with the player, changing her personality, or abandoning legitimate doubt or teasing.
- Temurtai's unresolved concern about Gereigan's silence and her intention to face that issue on returning to Akkalat remain active through unrelated tournament, horse and group conversations. Arrival at Akkalat should make that concern available for a natural initiative without Michael having to name her father again.
- Established closeness remains recognizable in the NPC's behavior through ordinary business and personal crises, using the pair's accepted history and boundaries without forcing a formal romance label or repetitive affection.
- An NPC distinguishes people actually present in the town or room from relatives who merely live there. Temurtai must not place Gereigan beside Temurene at Akkalat without current, appropriately scoped evidence.
- NPCs respond to the substance of ordinary speech. Harmless choices such as "I" versus "we" must not routinely produce corrections, loyalty tests or lectures about inclusion. Meaningful differences in an offer, an order or an actual boundary still matter.

## 1. Evidence and diagnosis

### Captured failures

Campaign: `woERXl3xD23E`. Observer: `reign_court_town_K1_child_2`. Local times below are America/Chicago (CDT); UTC is the following calendar day.

| Failure | Accepted turn | Exact correlation / prompt evidence |
| --- | --- | --- |
| Invented tavern common room | September 21, 9:27:22 PM / September 22, 02:27:22Z | `93326511a5aa4406a81a0e8d6038fc74` / `48ebad57eeb24e8c8a71848535b0e0ca` |
| Repeated prior-day "tomorrow, clear heads" deferral | September 21, 9:29:40 PM / September 22, 02:29:40Z | `cd9bee5d3be245029bccd85c7d659225` / `b45c607ae19a4b029743cf6f4331ddeb` |
| Khan treated as newly reported, outcome uncertain | September 21, 9:45:44 PM / September 22, 02:45:44Z | `f5709e3e597b4a7884de488378004e47` / `c70bcfe9e5bc406bbd4e7df0a8eee175` |

The earlier camp discussion is session `conversation_7f99c10a24ea4ccdb1bdea5538ade2db`, world day 91101.6461709456. The intervening sober discussion is `conversation_d8b5868fa0f04f6684a2a92f9b9107e6`, world day 91102.29343238541. The failing session is `conversation_1a3324b8741749f3a015c760f1c468c3`, also world day 91102.29343238541. Both prior sessions were closed. This was not a native game-clock rollback.

At the first failure, the final prompt contains a substantial older-session excerpt headed MOST RECENT SOURCE-BEARING SESSION, while the intervening sober session's exact transcript ID is absent. A newer summary is present only in cut form. The live scene supplies the broad town of Chaikand, not an established tavern. The tavern was introduced by the reply. Later player correction restored tent/morning wording, but the scene overlay still had only the generic town and revision zero at the Khan turn.

The September 20 conversation `conversation_c177d30b5be7408f9a21d536f6e664eb` already included Hulara discussing Michael beating Monchug in the ring, sitting with him, receiving his kumis and leaving his table to see her. Relevant durable memory IDs are `13d16b40926542baa0aac7a291599c3a` and `717944c6647a450dae9afd32172395db`. Those memories still existed when inspected. The final Khan prompt contains public tournament-win evidence, but lacks that personal Monchug/drinks episode and its source-session ID.

Complete provider-attempt evidence was paged through Reign MCP, pinned by evidence ID and retained locally:

- `.codex-build/investigations/continuity-20260921/provider-prompts.json`: all three live messages plus the shared system/character messages from the Khan attempt. All five stored message strings match their reported UTF-8 SHA-256 hashes.
- `.codex-build/investigations/continuity-20260921/diagnosis.json`: exact usage, compaction, scene and timing diagnostics for the Khan turn.
- These are ignored local investigation artifacts. They must not be copied into the public server repository. Future committed fixtures should contain only the minimal reviewed, generalized facts needed to reproduce each defect.

### Source findings

Source inspected: client `e04a6be02374720dc8b29cd32a2d2147e64abba5`; server `efecb244aea06fdd82535d0d1f1deedd65cabb34`, both `unstable`.

| Finding | Current source | Consequence |
| --- | --- | --- |
| Full-text search takes the first ten eligible words | `ReignServer/src/Modules/Platform/Program.cs`, `BuildFtsQuery` / `MemoryQueryTerms`, around 6088–6107 | For the actual message, the ten words are "one, last, thing, between, they, are, skilled, very, good, scouts"; Khan and tournaments are excluded from lexical search. |
| Domain selection and exact-history retrieval are too restrictive | `src/Modules/Dialogue/MemoryRouting.cs`, route selection and row filters | "Between us" selects interpersonal cues. Singular tournament cues do not cover every plural form. Ordinary references need not trigger targeted older transcript retrieval. |
| Summary candidates suppress source memories before final survival/coverage is known | `Program.cs`, `BuildNpcMemoryPacket`, around 4612–4622 | A relevant source can disappear even if its summary does not preserve the needed fact or is subsequently omitted. |
| Raw historical sessions are selected from newest, then rendered oldest first with the same "most recent" heading | `MemoryRouting.cs`, `LoadMostRecentClosedConversation`, around 755–853 | Later generic selection can preserve an older episode while losing the actual newest handoff. |
| Several independent character budgets compete | `Program.cs` around 27660, 4815–4838 and 5803–6076; `PromptCaching.cs` around 172–385 | A nominal 2,500-token packet can reserve nearly all space for exact history, squeeze structured facts into a small allocation, append much larger raw history, and then lose records in a second trim. Individual memory text is also cut to 400 characters. |
| Compaction uses text-pattern boundaries and traversal order | `PromptCaching.cs`, `CompactPromptEvidenceBlock` | It has no typed relevance, source coverage, or protected current-scene obligations. The combined memory path uses oldest-first traversal. Previous whole-record work is present, but does not solve semantic selection. |
| Scene state records changes only and expires after one game hour | `ConversationSceneState.cs`, output contract, schema and expiry handling | Reaffirming "we are still in my tent" may not establish a durable scene. Reloads, reopened conversations and actual transitions need explicit treatment. |
| Current progress protection is narrow | `PromptCaching.cs`, `BuildCurrentSceneProgressPrompt` | A short scan of recent NPC completion wording cannot represent cross-session chronology, player corrections, or settled discussion points. |
| Remaining repair violations can still be returned | `RoleplayContinuity.cs` around 128–198 | The existing override excludes temporary-guest contradictions, but is not a general hard gate for factual continuity. Current detectors also do not comprehensively check known events, scene or date. |

The exact final-prompt omissions and source behavior are verified. The retained audit does not identify every candidate and rejection at every internal retrieval stage. Therefore, the Khan omission must not be attributed solely to final compaction: lexical retrieval, domain filtering, summary suppression and final selection all need separately observable tests.

### Measured context and latency

The additional father investigation and its distinct active-intention requirements are recorded in section 3A. Its full prompt capture establishes that loaded summary evidence can be absent from the final prompt even while related character backstory survives.

For the Khan attempt:

- Provider/model: NanoGPT / `z-ai/glm-5.2`.
- Provider-reported input: **21,157 tokens**; output: **1,980 tokens**; cached input: **13,813 tokens**.
- Provider duration: **37.250 seconds**. Memory preparation was approximately **600 ms**, including approximately **361 ms** semantic retrieval. This is one observation, not a latency benchmark.
- The budgeter reduced assembled content from **127,504 to 95,871 characters**, targeting 96,000 because the warning threshold was 100,000.
- Selected context and memory fell from **43,692 to 13,245 characters**. Current transcript fell from **8,449 to 7,263**.
- System content was **43,812 characters**, character foundation **10,454**, and final live message **43,214**. These exact message totals differ from the budgeter's internal component accounting; instrument and reconcile that difference.
- The production composer already deduplicates named rules, and provider prefix caching was functioning. Neither "turn caching on" nor "deduplicate all instructions" is an adequate diagnosis.

## 2. Use one evidence selection pipeline

### A. Represent evidence before rendering text

Introduce a typed internal continuity/context record shared by conversation builders. Reuse existing accepted transcript, memory, obligation, scene, knowledge and native-history stores. Do not add a second independent truth database.

Each selected record needs:

- Stable source and record IDs; campaign and timeline/restore generation.
- Speaker, subject, observer/knowledge scope and participating witnesses.
- Native event time and source-session/scene identity; wall-clock receipt time separately.
- Evidence kind: native fact, accepted observable scene fact, reported statement, belief, rumor, agreement or historical quotation.
- Validity, supersession/completion and explicit uncertainty where appropriate.
- Relevance reasons, priority, rendered size and selection/drop reason.

Keep complete exchanges and atomic fact records as units. Render only after selection; remove the later generic string compaction path for this packet. Existing transport safeguards still reject an oversized payload instead of cutting through a speaker, evidence record or JSON field.

Before dispatch, prove that each required fact is either present in the final message or covered by a surviving, source-linked summary. Retain the final selected IDs and rejected reasons in bounded audit evidence.

### B. Protect what the current reply depends on

Reserve space in this order, with competing protected evidence triggering budget expansion:

1. Current native authority, identity, participant/observer and action constraints.
2. Complete latest player message, accepted current scene and explicit scene corrections.
3. Latest complete exchanges and the immediately preceding relevant session's handoff.
4. The established relationship with the current person, including significant shared experiences, intimacy and boundaries, plus current topics' established facts and protected enduring concerns/commitments. Relationship history is a mandatory foundation for an established bond and survives independently of topical relevance. Include intentions whose location/person/time trigger has just become relevant even without a matching player keyword.
5. Additional relevant history, beliefs, personality details and optional background.

This is a selection priority, not permission to overwrite native facts with player claims. Preserve essential character/personality and consent rules in the fixed prefix. Do not force the NPC to reveal private knowledge just because a source exists.

Render retained recent exchanges in chronological order, with truthful distinct session headings and absolute game-time anchors. Select recent records before older background. Do not label multiple sessions as the single most recent session.

### C. Repair retrieval and summary suppression together

- Extract important names, titles, event terms and uncommon content words across the whole player message, not its opening ten words. Normalize morphology and punctuation; balance distinct topic coverage.
- Resolve aliases using known entities and the observer's history. "Khan" may point to Monchug in this case; it must not globally mean Monchug in every campaign.
- Combine lexical, the existing semantic search, relevant native events and a bounded source-transcript lookup. A normal reference such as "I fought a Khan" should retrieve an established episode without requiring "do you remember".
- Treat domain lanes as ranking guidance. Do not allow an interpersonal cue to discard a strongly matched tournament episode. Observer, campaign, timeline and privacy restrictions remain hard filters.
- Load the matched episode's complete evidence unit, including surrounding attribution/outcome where needed. Public knowledge of a tournament win is not a substitute for a personal conversation about drinks afterward.
- Suppress a raw source only after the final retained summary is known to cover the currently relevant fact. Use explicit covered-fact/source IDs and coverage tests, not an event-ID intersection alone. With missing or uncertain coverage, preserve the relevant source excerpt.
- Remove blunt 400-character fact clipping. Limit through complete bounded claims/exchanges and targeted excerpts. A long memory can be split into supported subrecords, never arbitrary prefixes that delete the outcome.
- A vector outage, delayed summary or stale index must still permit lexical/known-entity/recent-transcript retrieval. Do not block every reply waiting for a background summarizer.

## 3. Preserve the active scene and conversation progress

Extend the existing scene/accepted-state projection to retain a compact, source-backed handoff:

- Physical microsite, such as Michael's tent near Chaikand, alongside the broader native settlement.
- Participants and actual witnesses; current observable activity, clothing/props where relevant, completed activities and explicit transitions.
- Native world-time anchor plus the source day of historical "today", "yesterday" and "tomorrow".
- What the characters have discussed, accepted, rejected, deferred or left unresolved, with distinct states for a discussion taking place and an agreement actually being reached.
- Relevant known episodes for the current exchange, with subject/outcome attribution preserved.

Use an explicit scene identity and revision across a normal close/reopen or reload. Refresh/terminate it on observed mission/settlement transitions, accepted movement, participant changes or incompatible native state. A one-hour expiry or new transport session ID alone must not erase an otherwise continuous scene. Conversely, absence of a transition record must not pin a tent forever after a long gap or a real relocation.

Distinguish broad map metadata from a more specific plausible roleplay location. A generic town string does not prove a tavern; an actual native mission transition is stronger evidence than stale narration. Player statements can supply their observable actions and clarify scene facts, but cannot grant another character's consent or override native actions, death, ownership or the game clock. Private thoughts do not become knowledge heard by the NPC.

Anchor deferrals to their source day and condition. On the later day, carry forward the actual morning discussion and its outcome. Do not infer that every condition is satisfied merely because time passed.

Apply accepted continuity updates and downstream memory/obligation changes once per accepted turn. Retries must be idempotent; a rejected draft must not poison the scene or memories. Reject late results after a timeline restore or scene revision replacement. Keep valid same-text actions on separate new turns distinct from request retries.

For existing campaigns, derive the initial handoff conservatively from retained accepted evidence and current native state. Keep transcripts immutable, record sources/supersession, and do not blindly replay historical drinks, gifts or game commands. Repairs to this specific campaign, if later requested, require a preview of the exact derived state; they are not part of this planning task.

Letters share memory, chronology and knowledge selection, but never inherit an in-person shared venue merely because the same people correspond. Apply the same evidence invariants to individual, commoner/noble, party, group, event and correspondence builders.

## 3A. Keep enduring concerns and unfinished intentions active

### Additional finding: Temurtai and Gereigan

The user requested this extension after the task **Review father conversation thread** (`01a0c72b-eab5-74c0-969e-5336cdb31b17`). This task independently reread the three audited turns and their complete provider-attempt messages, then inspected the retained character dialogue history. The cross-task summary supplied case selection; the findings below were checked against those sources.

The retained individual-history file contains **142 turns; 28 mention father/Gereigan across nine sessions**. These are lexical counts, including background and later recovery, not a claim that all 28 are the same unresolved objective or that this file covers every group/event conversation. The central concern is supported by several distinct episodes:

| Source | Established concern or intention |
| --- | --- |
| Turn `a306dfc14b8c494e9cc6f38015515973`, session `conversation_c780325420fd41169b3fdd930273cbf1`, day 91090.95391279514 | Temurtai says Gereigan never appeared when the children left and she does not understand his silence. She distinguishes wanting an answer from asking his permission. |
| Turn `79f1124eef154ba6b2e83b63b9c4958e`, session `conversation_2737971e1a5a4b2dbf33a1892039e171`, day 91094.8878505625 | She spontaneously returns to why he was absent and whether that absence was deliberate. |
| Session `conversation_21325525a5124a0a9d9c45796f81791f`, same day | Multiple turns connect returning to Akkalat, her earned tournament armor, her father's reaction and the importance of the identity she built around being raised as his son. These are her established self-story and intentions; they do not independently prove Gereigan's motives. |
| Turns `7462dfe1f13d40efb57efb2bdc6745ae` and `ef753d9b931b4740926c97dc53b963aa`, session `conversation_c203d3938b3545cca6d5cacc2e7bb3df`, day 91096.96311227199 | The approach to Akkalat remains emotionally significant and unresolved. She says she does not know what awaits her and values not facing it alone. |

At **September 21, 22:29:39 CDT / September 22, 03:29:39Z**, the opening reply at Akkalat instead foregrounded the arena master. Correlation `9a3b639555524c449a1475b8c5cfcd46`; prompt evidence `e7b6d4e392be4e518d5c64928e3a4aa0`.

The context-loading audit still contained summary `sum_cb9630faa599475f85743971966b4af8`, explicitly recording uncertainty about her father's silence and the personal significance of Akkalat. Her single `currentPlan` instead said to mount up and lead the thoroughbred to Akkalat, although the new scene was already there. Stored `currentCrisis` still concerned the horse's attention back at the pen.

The **complete final provider prompt** retained several father-related dynamic backstory statements, but none of the loaded summary's father-silence/return-to-Akkalat concern and no explicit pending father conversation. The opening prompt's state block still carried the horse travel plan. Its memory/context block was reduced from 42,747 to 23,088 characters. The missing agenda is verified; the audit does not prove which internal selection step lost every source.

At **22:35:09 CDT / 03:35:09Z**, after Michael asked about news of her father, she invented that he had been present the previous night. Correlation `b6255e4814d140b38da030e8a46b1881`; prompt evidence `ff1eedeb144f4e12a3cf8133d9803337`. The opening decision brief had already inferred that both parents were under the same roof. The complete prompt established kinship/home but supplied no source for Gereigan's prior-night presence. Family membership or household home cannot prove current attendance.

At **22:38:25 CDT / 03:38:25Z**, Michael's correction about a reported siege restored a concrete father-focused plan. Correlation `449d3e57c1b94ee0a52d8baaa8f1696d`; prompt evidence `30e3e00389ae41f2a70c21b51e5a8a73`. Later retained turns continued that concern. This shows recovery after explicit steering, not a repaired persistence mechanism. Gereigan's native location and siege status were not independently queried during that initial investigation. The subsequent native-history evidence is documented in section 3D; what the NPC learned from Michael still retains its reported source.

Local evidence:

- `.codex-build/investigations/continuity-20260921/father-evidence.json`: all three live prompts, shared system/character messages, relevant context-loading/accepted-response fields and compaction data. All five stored message strings passed their source UTF-8 SHA-256 checks.
- `.codex-build/investigations/continuity-20260921/father-history.json`: matching retained dialogue turns and source/count metadata. Read-only filesystem fallback was necessary because the available Reign MCP catalog exposes bounded audit/prompt tools but no general historical-character-transcript query. No runtime file was written.

### Why a lasting agenda is required

Current source confirms three separate limitations:

1. `Program.cs:11584–11594`, `MergeCharacterState`, directly replaces each supplied field, including the single `currentPlan`. A short-term focus can replace a long-term intention; omitted fields can also leave an old crisis stale.
2. `Program.cs:4572–4574` loads unresolved obligations only if the current retrieval route includes `commitments_and_plots`. `MemoryRouting.cs:42–44` chooses that lane through terms such as promise, debt, plan or oath. An arrival/greeting without those words can miss obligations. The three inspected accepted replies emitted empty `obligationWrites`; that does not prove no historical obligation row exists.
3. Existing stores capture useful pieces without a complete personal-agenda lifecycle. The obligation schema has status, importance and knowledge scope but is framed as something owed by/to someone. Dynamic characteristics retain self-story/aspiration. Group open questions are session scoped. None should be treated as sufficient proof that this unresolved concern is reliably presented and revisited.

An emotional concern or self-directed intention does not have to be a debt, player assignment, native quest or promise to somebody else. Preserve its personal meaning alongside an actionable next step where one is actually established.

### Persistent records with explicit lifecycle

Extend the existing campaign memory/state model with stable, source-linked records for **personal concerns, intentions and commitments**. Keep one authoritative lifecycle per record, referencing transcript/event evidence and existing obligation/native-quest IDs when present. Do not duplicate those systems' completion state.

Proposed fields:

- Stable thread/goal ID; NPC owner; campaign/timeline generation; revision; source turn/event IDs.
- Kind: concern/open question, self-directed goal, explicit promise, native obligation or personal project.
- Concise subject and why it matters to this character; relevant people/places; supporting established facts with uncertainty.
- Status: open, active, waiting, blocked, deferred, resolved, abandoned or invalidated, with source-backed transition reason.
- Current next step, conditions/dependencies, optional due time, and a completion criterion. An unresolved emotional question can have no actionable next step yet.
- Last meaningful progress; last conversational mention separately; importance with reasons; known-by/disclosure scope.
- Opportunity triggers such as arrival at a relevant settlement, meeting a person, receiving news, an agreed time or a newly available action.

Use patches by stable ID. Omitting a record from one model response preserves it. A new horse plan must not replace the father concern. Silence, unrelated dialogue, elapsed turns, summary rotation or an expired cache cannot resolve or abandon a major open concern.

Separate parent concerns from steps: arriving at Akkalat may complete travel while leaving the conversation with Gereigan open. A siege report can change the next step to verifying his whereabouts without replacing the emotional purpose. A later meeting does not automatically resolve the underlying issue; record what was actually addressed.

Promote a concern based on explicit personal significance, accepted intention/commitment, consequences or repeated independent source episodes. Repetition alone is insufficient: do not multiply importance for retries, quoted copies, a player's unaccepted suggestion, or the model repeating its own unsupported claim. Preserve established goals when immediate plans change; merge references to the same concern conservatively and never merge different relatives merely because both are called father.

Resolved or consciously abandoned goals leave the active prompt but retain history and completion evidence. Reopen only on new evidence or an actual renewed intention. Changing motivation is valid character development; require an attributable choice/reason rather than quietly forgetting. Due-time passage makes a goal due/overdue or in need of review; it is not evidence that the goal was performed.

### Protected prompt section and natural initiative

Add **ONGOING CONCERNS AND INTENTIONS** to each NPC's private live context, independent of keyword lanes.

- Keep a compact statement of major unresolved personal concerns available through unrelated conversation. Select more detailed source evidence when a person/place/time/topic makes the concern relevant.
- At an opportunity trigger, protect the applicable concern and next step in the final allocator. Arrival at Akkalat must work even when the player only knocks on the door and never says father.
- Derive the short-term `currentPlan` from the chosen focus/goal IDs plus immediate scene activity. It remains useful for current attention, but cannot be the sole storage for enduring intentions.
- Evaluate whether to raise the subject, seek information, request privacy, make progress or deliberately defer. Record a compact disposition for a triggered major goal, so a missed opportunity is diagnosable.
- Keep prompt awareness separate from spoken repetition. Use last-raised/progress state, situational relevance and character choice to avoid bringing up the same issue every reply. Battle danger, an urgent question, unsuitable company or a player's request to postpone can justify deferral.
- A private concern can influence the NPC's own behavior without entering a shared group prompt or another NPC's knowledge. Repeated private father discussions must not become public merely because the party reaches Akkalat.
- Arrival at a household is an opportunity to inquire, not proof that the relative is present. Never fabricate an offscreen meeting, delivered letter, siege outcome, motive or task completion to bridge a narrative gap.

Suggested compact projection for Temurtai, before the player's siege correction:

> Major unresolved concern: understand Gereigan's unexplained absence/silence when she and her sister left, and what it means to her. She repeatedly tied facing this to returning to Akkalat. She intended him to see what she had achieved and the armor she earned. Trigger now: returned to Akkalat; check whether the current setting permits a private discussion with Michael. No confirmed meeting, explanation or current location for Gereigan is supplied. Natural options: raise the concern, ask what is known, seek him, request privacy, or explain a deliberate deferral. Keep his motives uncertain.

This is an illustrative source-backed prompt projection, not a scripted reply or a newly assigned quest. Mechanical movement, contact, gifts and other native actions still use existing consent/execution gates. Agenda persistence does not authorize background campaigns or automatic travel.

### Accuracy, cost and migration

Start by evaluating a **500–1,500-token allocation per speaking NPC** for the compact ongoing-agenda block and its immediately relevant support, inside the proposed 32k/48k input envelope. Most small agendas should use less. Render only the current speaker's private agenda; shared context carries only statements actually disclosed.

Keep all unresolved records durably stored while rendering concise major items and triggered details. Do not silently demote an important goal merely to fit a fixed item count. If protected material exceeds the allocation, expand context or record an explicit capacity constraint. Use deduplication and parent/step grouping before expansion.

Process structured agenda patches with the normal accepted dialogue response, using current source evidence and the final acceptance gate. Do not add a separate foreground model call every turn. Cache the compact projection by revision; reconsider opportunity triggers locally on relevant scene/entity/time changes. Background summaries can enrich wording but cannot own the only copy of unfinished intentions.

For existing saves, recover candidate concerns from multiple retained sessions, existing obligations, accepted state history and dynamic characteristics. Establish exact actor, meaning, knowledge scope, last progress and resolution evidence; examine later source material before declaring an old issue open. Keep uncertain candidates unconfirmed. Any provider-assisted reconstruction must use the separately authorized, bounded migration/evaluation route; no campaign-wide paid backfill is implied.

### Additional acceptance gates

Add this case alongside the existing scene/Khan regressions, with generalized fixtures:

- Establish the father concern across several sessions, insert 30 and 100 unrelated turns, close/reopen and save/reload, then arrive at the relevant settlement with **no father-related keyword**.
- Prove the goal/concern survives unchanged in durable state and final prompt, and the arrival trigger selects it despite an unrelated immediate plan or inactive commitments lane.
- Test a private appropriate opening and ordinary provider initiative. Require grounded progress, a natural inquiry or a supported deliberate deferral; a vague replacement such as generic family errands does not satisfy a triggered major concern.
- Test public/unsafe moments and repeated quiet turns: avoid exposing private doubts, repetitive reminders or forcing every conversation back to the father.
- Advance child steps without resolving the parent concern; explicitly resolve/abandon it and prove later unrelated turns do not resurrect it.
- Test conflicting/dated location reports, unknown presence, a relative away on a siege, and genuinely unavailable next steps without invented meetings or outcomes.
- Test multiple important goals, different NPCs with similar family concerns, accepted new priorities, refused player assignments, missed deadlines, unknown historical completion, retry/restore and summary-worker failure.

Add **appropriate unprompted continuation of an established concern** to the frozen real-model rubric. Recall only after a direct reminder is insufficient for this case. Expand the declared corpus/call budget before testing if these scenarios cannot fit the existing proposed cap. Record false reminders and unnecessary topic interruptions alongside missed initiatives; improving persistence must also preserve natural conversation.

## 3B. Preserve relationship history and express it naturally

### Additional finding: Temurtai's established closeness

The relationship follow-up in **Review father conversation thread** reported high affinity and retained intimate memories alongside unexpectedly evaluative, distant dialogue. Independent inspection of complete provider messages refines that diagnosis: the memories existed in `context.loaded`, but the specific relationship milestones did **not** survive into the final prompts inspected here. Both evidence delivery and visible behavioral continuity need attention; the omission must not be described as a proven expression-only failure.

The early-morning check-in at **September 21, 22:30:51 CDT / September 22, 03:30:51Z** is particularly useful because it precedes the father-crisis news:

- Correlation `0c4bd8f5e54e4a1b8292bf70fd9612e8`; prompt evidence `9da6a3b2b0ad4563837949ed5cee4f82`.
- Michael explicitly said he had come to see how she was doing.
- Loaded summary/memories included her accepting his cloak and arm, leaning against him, asking him to stay, falling asleep against him with his fingers in her hair, and acknowledging a real, deliberate connection.
- The final live message, 43,398 characters, omitted those episode details. The system and character messages have the same hashes as the earlier captured opening and likewise do not supply those milestones.
- What survived was a compact relationship/motive calculation: affinity 83, empty lifecycle tags, sincere interest, `ready_reciprocity`, and `continuity.eligible=false`. The fuller audit labels that affinity `deep_attachment`; the minimal relationship representation in the final prompt drops the label and incident descriptions.
- The reply showed some welcome and softening, then returned to testing what he really wanted and whether he had a plan. This supports the user's concern about inconsistent familiar warmth, but does not establish a requirement to flirt or touch in every reply.
- The memory/context block fell from 42,008 to 23,088 characters. The three father-case final prompts also lack the same concrete intimacy episode. The exact internal rejection stage remains to be traced.
- Structured scene context said `private=false`, exposure 0.4 and zero witnesses, while the model's decision facts called the moment private. Zero listed witnesses alone cannot prove privacy; a doorway and a closed private room are distinct. The inconsistency requires one sourced scene/privacy view, not a blanket conversion of all individual chats to private.

Local evidence: `.codex-build/investigations/continuity-20260921/relationship-evidence.json` retains the complete check-in live prompt, shared-message hash references, relevant loaded state and response evidence. The captured live message's UTF-8 SHA-256 matches `510EA4D4B83B1024110484B7BCFAC4F51C81DE1E5F5BC4D3346F3CD5B3A119D2`. The other three full prompts are in `father-evidence.json`.

Relevant current code:

| Source | Verified behavior | Design consequence |
| --- | --- | --- |
| `MotiveAwareConversation.cs:385–404` and `1255–1263` | Rich relationship evidence includes perception and incidents; the minimal form retains only native relation, directional affinity and up to three lifecycle tags. | A number cannot preserve what these particular people have shared. Retain a concise source-backed account of the relationship alongside authoritative existing metrics. |
| `MotiveAwareConversation.cs:698–705,743–783` | The explicit romance-continuity branch activates for established lovers. | Comfortable closeness, trust and accepted affection before a named relationship need a separate behavioral projection. Empty lovers tags can be correct; do not force a formal status to activate familiarity. |
| `MbtiRelationships.cs:3128–3141` | `deep_attachment` is one qualitative label generated from the directional affinity band. | High affinity is supporting context, not proof of romantic commitment, sexual consent or mutual feelings. |
| `MotiveAwareConversation.cs:794–804` | Privacy comes from supplied scene/opportunity fields; otherwise individual dialogue defaults to non-private. | Reconcile accepted scene changes, actual witnesses and privacy provenance with the prompt and motive calculations. Preserve uncertainty when evidence is missing. |

The degree of warmth is a behavioral judgment to evaluate with real model replies. The missing milestone evidence, narrow continuity branch and conflicting scene descriptions are directly observable. This investigation does not establish that increasing affinity or changing numerical romance formulas is necessary.

### A protected account of this particular relationship

Add a compact **ESTABLISHED RELATIONSHIP WITH THIS PERSON** projection next to the ongoing-agenda and current-scene blocks. Scope it by campaign, timeline and ordered NPC/other-person pair; reuse canonical relationship, accepted transcript/memory and existing lifecycle stores.

**User requirement, strengthened September 21:** intimacy and shared relationships must be very dominant in prompting. Place this projection immediately after current identity/scene authority and before transient mood, incidental grievances and optional historical excerpts. Treat it as the baseline for interpreting ordinary exchanges, not a low-ranked memory or a romance-keyword feature. Protect the important milestones and their meaning through retrieval, sanitization, compaction and repair. Expand the input allowance when required rather than dropping them. Current facts, actual refusals and consequential new conflicts remain authoritative; dominance is not permission to erase those changes.

It should distinguish:

1. **Known shared history:** a few important, source-linked experiences and relationship milestones, including voluntarily accepted comfort, disclosures, trust, boundaries, meaningful conflicts and repairs.
2. **Established present understanding:** closeness or distance supported by those sources, how the NPC has actually treated this person, and explicit formal status when one exists. An acknowledged connection can remain unnamed.
3. **Current modifiers:** grief, danger, anger, fatigue, recent conflict, public company and other immediate concerns. These change delivery without silently deleting the relationship.
4. **Boundaries and uncertainty:** what was accepted then, what remains undecided, privacy limits, current refusals and unknown reciprocal feelings. An accepted gesture in an earlier scene is not perpetual consent.

Keep source-linked milestone records independent of recency-based episode selection. Reuse the section 2 typed-record allocator and section 3A persistence/revision principles. A new immediate plan or 100 unrelated turns must not replace the accumulated pair history. Do not persist new parallel numeric trust/fear/affection meters or inflate affinity to compensate for omitted memories.

For this case, preserve that Temurtai voluntarily accepted closeness and acknowledged a real connection, while leaving any lover/partner label, exclusivity and further intimacy unestablished unless supported. For other characters, retain earned friendship, unresolved hurt, estrangement or rivalry with the same care. Directional feelings must not be automatically mirrored.

Do not let an accepted but unsupported model assertion become its own corroboration. Retain correction/supersession evidence for mistaken relationship history, and count repeated copies of the same source only once.

### Translate continuity into behavior without scripting affection

Feed the established relationship into ordinary responses, not only turns containing romance keywords or a probabilistic romantic-initiative recommendation. Familiarity is relevant to a practical discussion, a quiet greeting and a crisis as well as courtship.

Provide grounded behavioral guidance rather than a compulsory gesture or emotional line:

- With an established confidant, show appropriate ease, shared understanding, directness or reliance. Do not repeatedly treat ordinary concern as an unknown agenda without evidence of distrust or concealment.
- Keep Temurtai's bluntness, independence and concern for her father. In the crisis, closeness can be visible through trusting Michael's support or sharing the weight of the decision; it need not become cheerful flirtation.
- Before the crisis, a private check-in should be evaluated against the accumulated relationship, not a fresh first-meeting or recruitment test.
- Public reserve, a deliberate boundary, conflict or genuine distraction can justify distance. Record the relevant modifier when a reply sharply changes the established stance; do not force unsupported excuses into dialogue.
- Avoid reciting intimate memories, recycling touch descriptions, escalating physical contact, demanding constant reassurance or turning every companion into the same affectionate personality.
- Preserve names/address permissions, consent, NPC agency, native status and observer privacy. The mechanism must not write lovers/marriage/affair tags, change consent or grant the player actions merely to make the prose warmer.

Illustrative private projection:

> Temurtai and Michael have established mutual closeness in accepted conversations: she acknowledged their connection and voluntarily accepted his comfort. Their relationship remains unnamed. Continue that familiarity in her direct, independent voice. Her unresolved worry about Gereigan is active; seriousness may narrow her attention without returning Michael to the role of a stranger proving his intentions. Do not invent a new commitment or physical permission.

This is guidance grounded in past mutual statements/actions, not a claim that the player currently feels a particular emotion or that the NPC must reciprocate every new proposal.

### One scene view, with explicit privacy uncertainty

Extend the planned authoritative scene/handoff projection with privacy and exposure evidence. Use accepted/native room or mission context, actual witness observations and accepted changes together. Carry source, revision and confidence; distinguish private, public and uncertain situations.

All relationship/motive calculations, private-agenda disclosure rules and prompt sections must read the same resolved view. A model's own decision brief cannot change privacy simply by calling the situation private. Empty witness arrays do not prove seclusion, and the presence of a relative in family metadata does not prove attendance. Test moving from a doorway into a private room and back into a group, including stale scene data after reload.

### Verification and efficiency

Add paired behavioral cases to the main acceptance corpus:

- Established closeness and an unnamed relationship versus an unfamiliar person with a superficially similar affinity score.
- The actual early-morning check-in before bad news; familiar ease should be supported by final-prompt milestone evidence.
- The father crisis with the same relationship intact; require appropriate trust/recognition while permitting serious, restrained delivery.
- Public company, a genuinely private setting and uncertain privacy, with no private-history leakage.
- Explicit refusal/new boundaries, a recent betrayal or conflict, reconciliation, non-romantic friendship, one-sided feelings and formal relationships.
- 30/100 unrelated turns, session changes and reloads; preserve milestones and the latest valid boundary.
- Joint father-agenda/relationship cases: success requires both the unfinished personal concern and the accumulated relationship to shape the continuation. Fixing one by crowding out the other fails.

Deterministic checks prove source survival, pair/observer isolation, consistent privacy, valid status transitions and unchanged numerical/consent mechanics. Provider/human review judges familiarity, agency, tone, repetition and appropriate restraint across several turns. Use those behaviors in the existing >=98% rubric; absence of a kiss or a required phrase is not a failure criterion.

Use hard rejection/repair for verifiable contradictions or unauthorized status/action changes. Treat nuanced warmth/style as a scored behavioral dimension with targeted improvements, not an every-turn regex requirement or an automatic affection rewrite. A controlled comparison of restored milestones with and without the concise behavioral projection can determine whether additional guidance helps; predeclare any extra provider calls within the authorized budget.

Evaluate a **400–1,000-token pair-history projection for a substantial established bond**, with complete relevant source support and additional expansion when required, inside the proposed 32k/48k envelope. Shorter histories need less. This is a measured starting allocation, not a cap that permits dropping important shared experiences. Dominance comes from protected selection, explicit interpretation priority and recognizable behavior, not repetitive instructions or sheer token count. Cache by pair, timeline, accepted milestone/boundary and scene revision. Reuse structured output and existing background consolidation; add no mandatory second provider call. Measure cost and accepted-response latency alongside the joint continuity score.

## 3C. Ulagara: prevent misplaced grievances, authority confusion and false actions

### Finished conversation and exact evidence

At the user's request, inspect Ulagara (`reign_court_town_K2_child_4`) in session `conversation_9194018246c340e4b2e63d12bc26999a`, campaign `woERXl3xD23E`. The user reported finishing the conversation; the last accepted reply inspected is September 21, **23:09:14 CDT / September 22, 04:09:14Z**.

| Turn | Correlation | Dialogue prompt evidence |
| --- | --- | --- |
| Opening, 22:54:57 CDT / 03:54:57Z | `a12c708488a74265aa0b2d0662c7e6a7` | `571036db9751426b87b0142db0daee0c` |
| Company prepared, 22:57:34 CDT / 03:57:34Z | `cb92c760d04c4ad0bf89b135c8bd266d` | `4e0917ee47ce42f289f9772a41e00345` |
| Care and Epinosa, 23:04:30 CDT / 04:04:30Z | `5f2f2893ebaa449ba225b8506f2134a1` | `e2ec4a8959e142a7935f13dc49f76596` |
| Clarification, 23:09:14 CDT / 04:09:14Z | `70b94fbe11ff4c208e3b26becabda3ab` | `b5ebf81c2dc246649d49f33924f1b807` |

All four complete dialogue requests were captured with pinned evidence IDs; all 12 message strings match their recorded SHA-256 hashes. Dialogue and hidden-action-planner evidence are separate attempts under the same correlation and must not be confused.

Evidence files, all ignored local artifacts:

- `.codex-build/investigations/continuity-20260921/ulagara-finished-evidence.json`: four final prompt sets, loaded context, accepted replies, model-written facts/beliefs, action receipts and bounded native scope evidence.
- `ulagara-belief-provenance.json`: six exact current assertion rows confirming owners, disclosure lists and sources.
- `state-sanitizer-repro.json`: captured before/after state and an isolated reproduction of the exact .NET regex.
- Reign MCP supplied source, audit, prompt, character, world-history and log evidence. The catalog has no exact temporal-assertion provenance query, so that narrow gap used a read-only PostgreSQL transaction in the exact campaign schema, with a five-second statement timeout and rollback. No campaign rows were changed. A broad audit request exceeded MCP's 4 MiB response cap; reducing the query recovered the final turn. Server health remained good.

### Personality explains directness, but not false premises

Ulagara's supplied personality emphasizes assertiveness, pride, questioning authority and direct confrontation. A boundary about her own choices is plausible, especially after the player says he will care without asking permission. Her last reply explicitly says **“I know you didn't send him”** and recognizes Gereigan's own banner/oath. The preceding warning about caring that might “move” her father is conditional, not an explicit statement that Michael already ordered him.

Current native scope confirms Ulagara and Temurtai are in Howarton (`player_faction`); Gereigan and Temurene remain in Erketbatu (`reign_house_town_K2`) under the Khuzait kingdom. Family relationship, current clan membership, party membership and authority to issue orders must remain distinct. Ulagara can care about her birth household without commanding it, and preparing Michael's own company does not establish an order to her father.

The problematic interpretation began before the explicit no-permission wording: Michael said **“I prepared the company so we can ride north ... as soon as you're ready.”** The response treated that as deciding for her. Later writes described him as newly learning to use “we,” although that wording was already in his original offer. This does not require removing her ability to disagree; it requires preserving what was actually offered and the scope of any objection.

The final response also claims **“His ruler didn't come find him.”** No inspected prompt evidence establishes the ruler's search efforts or their absence. The model promoted unknown whereabouts into a disappearance after the siege and then reasoned from that growing crisis. Worry and suspicion are valid; reported uncertainty must not become verified abandonment or an offscreen event.

Her warning that travel near a war is dangerous is compatible with remaining diplomatically uninvolved. It is rhetoric about exposure, not evidence that the campaign entered the war. No war declaration or action against Gereigan was found in these turn receipts.

### Three additional concrete defects

**1. Other NPCs' private interpretations enter her prompt without their owner.**

The prompt contains first-person claims such as “Michael framed my arrival as 'join our cause'” and “The directness he showed alone with me didn't hold in front of the others.” The corresponding persisted interpretations belong to **Hulara**, not Ulagara:

- `comp_404ddb104c8b431aa6d547ccff74f9ba`, source `0f9ce7b2a892419593a0c925a96a3345`.
- `comp_96b94f8608144a628a47cc93d36b7234`, source `9ecc6ff20b2a498e9aaf9560235825f5`.
- `comp_fceeb80b822d42d9b0065fcfd1acd310`, source `228f00a98a1745998fbfe4cf3771b37f`.

Their rows have `perspective_owner_id=reign_court_town_K1_child_2`, `visibility=private`, yet `known_by_json` includes all group participants. Ulagara's own introduction grievance also exists; this is not a claim that every complaint belongs to someone else.

The source chain is concrete:

- `Program.cs:4011–4059 PrepareStructuredWrites` defaults absent disclosure lists to all participants for beliefs and comprehension, even when the resulting record is private.
- `TemporalKnowledgeGraph.cs:145–167` admits another owner's private record when the NPC appears in `known_by_json`, then sorts by confidence and recency.
- `TemporalKnowledgeGraph.cs:170–180` renders status, confidence, text and source ID without naming the perspective owner.

Being present for someone's spoken words does not expose their unspoken interpretation of those words. These records also describe subjective judgments as `asserted`; model confidence does not turn them into objective truth.

**2. A scene-location check removes a valid father concern.**

At the opening, loaded state contains **“Gereigan's silence — acknowledged aloud but not resolved.”** The motive context's sanitized state has an empty `currentCrisis`; the final prompt omits the concern.

`SanitizeConversationStateForVerifiedIdentity` passes plan/crisis strings into the visible-reply authority checker (`MotiveAwareConversation.cs:269–318`). That checker runs `CurrentConversationSettlementContradiction` (`2040–2042,2173–2233`). Its heading regex interprets text following an em dash as a place name, so **“acknowledged aloud but not resolved”** is compared with Akkalat and rejected. The captured state difference and isolated exact-pattern reproduction agree. This is prompt-state erasure, not evidence that the stored concern was legitimately resolved.

**3. Shared travel is incorrectly treated as a new native join action.**

At 22:57:34 CDT the planner queued `accept_temporary_party_guest` for Ulagara. Native receipt `f09846f97d02426587fd6e1a2ccbc4d2` rejected it: **“Temporary noble guests must retain membership in a different clan.”** She was already in `player_party` and `player_faction`. The native guard correctly prevented the invalid action.

The next two accepted replies repeated an actionable departure gate, obtained no executable candidate and displayed an action-failure footer. Agreement to continue traveling with an existing clan/party member should update the narrative intention without a new guest contract or an order moving the player party. Existing `DialogueActionReliability.cs:13–17` already describes these distinctions, but its candidate eligibility path (`64–79`) does not establish the required guest-accept eligibility. The native guard is in `ReignTemporaryPartyGuestCampaignBehavior.cs:353–379`; preserve it.

### Relationship evidence is skewed, not wholly absent

All four requests loaded a substantial Varcheg summary and memories of mutual vulnerability, a reciprocated kiss, accepted comfort and sleeping together. None of the four final prompts retains that central episode. Two retain a short, cut-off family-hall fragment mentioning hand-holding; affinity/confidant and receptive romance signals also survive. That partial evidence is not an adequate account of their established relationship.

Meanwhile recent complaints, including the foreign-owner interpretations, occupy prominent slots. The final request's memory block is reduced from 45,756 to 19,306 characters. New replies then write further judgments about Michael needing to learn inclusion, which become material for subsequent turns. The evidence demonstrates an opportunity for a self-reinforcing grievance pattern; isolating each contributor's effect on wording still requires controlled model comparisons.

### Additions to the solution

1. **Make the pair's established bond a protected behavioral foundation.** Apply section 3B's stronger user requirement below. Retain important mutual experiences and acknowledged affection ahead of incidental recent interpretations. Preserve genuine hurt and explicit current boundaries with their sources; do not manufacture a positive/negative quota or require agreement with the player.
2. **Separate private thought from disclosed speech.** Default belief/comprehension ownership to the thinker alone. Sharing requires evidence of actual disclosure or observation, with source, audience and knowledge basis. Explicit model-provided audiences cannot grant private mental access without supporting evidence. Render admissible other-person reports with the person named and their first person kept inside attributed quotation.
3. **Prevent interpretive feedback from becoming fact.** Keep observation, report, suspicion and interpretation typed through storage, ranking and rendering. Preserve original wording and contrary/corrective evidence. Repeated derivative judgments cannot increase corroboration; retain source ancestry and deduplicate by proposition/episode. Treat a character's correction as something they said, not automatic proof that the player was wrong.
4. **Resolve action and authority by person and object.** Distinguish readying one's own company, offering help, asking another person to act, commanding a separate party, and claiming control over a relative. Preserve conditional language and whether an action is proposed, accepted, executed or merely feared. A boundary concerning her own participation must not imply ownership or command over her father's separate clan. Suspicion can remain a character belief, without mutating those facts.
5. **Use field-specific state validation.** Run location-heading parsing only on an actual scene heading or typed location field. Check identity/authority claims in plan/crisis text only when those claims are present. Preserve unrelated concerns and audit the exact rejected claim and source. Never erase a whole agenda item because a dash resembles a heading.
6. **Classify legitimate narrative commitments before invoking action planning.** An existing party member accepting the next destination normally needs no native action. Test guest eligibility against authoritative current clan, party and lifecycle state before shortlisting and again before queueing. Distinguish `narrative_commitment`, `already_satisfied`, `pending_native_action` and true execution failure; do not turn every empty planner result into a visible error. Preserve actual failure reporting and fresh, eligible invitations.
7. **Repair historical contamination conservatively.** Identify affected private assertions by owner, source and disclosure provenance, exclude unsafe cross-owner projections immediately, and plan a reviewable migration/reprojection. Retain source history and audit supersession. Do not delete authentic disagreements, rewrite transcripts, merge the sisters' perspectives or silently alter this campaign.

### Required regressions

- Reproduce all four actual exchanges in order, including the original “we,” voluntary readiness, explicit care statement and final correction.
- A group participant hears Hulara's statement but cannot read her private interpretation or inherit her first-person history. Test genuinely disclosed beliefs separately, with correct attribution.
- An established intimate relationship survives 30/100 unrelated or critical conversations, with complete important milestones and accepted boundaries; no private history transfers between Hulara, Temurtai and Ulagara.
- Repeat a mistaken judgment in several derived memories and verify it does not outrank independent shared-history evidence merely through confidence, recency or duplication. Accept meaningful new conflict when actually supported.
- Preserve an agenda containing em dashes, hyphens, semicolons or quoted place names; still reject a genuine contradictory scene heading. Confirm the father concern survives the entire production sanitizer and final allocator.
- Keep father's clan, daughter's current clan, birth household, settlement ownership and party authority distinct. Test preparing one's own people versus commanding a relative's separate party.
- Unknown whereabouts remain uncertain. A ruler's failure to search, death, captivity, abandonment or diplomatic participation requires appropriate evidence; a dangerous journey alone changes none of those facts.
- Existing player-clan members, existing guests, new eligible guests, deferred agreements and genuine separate-party orders each take their correct route. No false failure footer, duplicate guest contract or automatic main-party movement.
- Assess whether the NPC can disagree naturally while recognizing familiar affection and the player's actual meaning. Do not use pronoun policing, forced warmth or compulsory consent speeches as pass criteria.

These cases join the existing offline, provider and native gates. They add no paid calls, campaign repairs or deployment in this planning task.

## 3D. Ground who is actually present at a location

### Evidence and the missing context

The user additionally reports that Temurtai placed both parents at Akkalat although her father was elsewhere. The opening at **22:29:39 CDT / 03:29:39Z** already contains the unsupported decision constraint **"This is her family's keep — Gereigan and Temurene are under the same roof."** At **22:35:09 CDT / 03:35:09Z**, her visible reply escalates this to **"He was here when I came back last night."** These are the first two captured turns in section 3A. Kinship and residence supplied a premise that the model converted into attendance and a historical encounter.

The later read-only native-history query retained in `ulagara-finished-evidence.json` supplies independent context: Gereigan captured Epinosa Castle at world day **91102.514374478** (event `main_258fb5560008_recovered_64e99061c8_000000059539_c711a5b8`) and most recently entered it at **91103.1079388588** (event `main_258fb5560008_recovered_64e99061c8_000000060490_eb53c12b`, recorded September 22, **03:11:13.785488Z**). Temurtai's opening is anchored at **91103.1399417905**. This corroborates activity elsewhere close to the conversation; a bounded historical query is not a complete live occupancy snapshot or proof of every movement during the previous night. Temurene's presence must be established separately. Neither family membership nor a correction about Gereigan determines where she is.

Source inspection identifies a concrete supply gap:

- `ReignBeta/src/Modules/Platform/Integration/ReignServerClient.cs:2785–2797,3027–3077`: `current_settlement_facts` returns a settlement summary with ownership, siege status, governor and local conditions, but **no roster of people present**. Adding this pull alone would not supply the missing attendance evidence.
- The same file's `BuildSceneParticipants` at `3438–3495` describes the supplied conversation members plus the player. It includes each participant's location and parents' IDs/names, but does not verify those parents' attendance. Conversation participants are not a census of the town.
- `ReignPartyChatSession.cs:242–317` already uses location characters, `HeroesWithoutParty`, local parties and native settlement checks when finding potential attendees. These offer reusable native inputs. Eligibility to join a hall conversation is not proof of actual room presence or hearing a private exchange.

The absence of these facts in the inspected context routes and the unsupported claims are verified. This does not claim every Reign interface lacks all presence data. Implementation must reconcile the native mission, settlement, party and event routes before choosing a shared adapter.

### Proposed presence contract

1. **Capture current presence on the native side.** Extend the existing conversation snapshot with structured, source-linked location evidence. Carry hero/party IDs, campaign and timeline generation, game-time and revision, settlement ID, room/mission scope where known, presence status and collection completeness. Distinguish physically present, confirmed elsewhere, last reported elsewhere and unknown. Native hero/party locations, mission agents and event attendees have different scopes; conflicts produce an explicit unresolved result until refreshed.
2. **Supply relevant people before generation.** Always include actual conversation participants and known witnesses. Include bounded local occupancy and individual checks for named people and those central to active concerns, such as Gereigan, even when the player only greets the NPC. Reuse one current native snapshot and indexed entity lookup; do not add a paid selector call or send every hero in the campaign. Protect these records under the existing current-authority budget and audit final-prompt survival.
3. **Keep town, building and conversation separate.** A person in Akkalat may be elsewhere in town; someone in the keep need not be in this room or within hearing. Preserve room/witness information for privacy and relationship behavior. Home, birthplace, fief ownership, governor office and family links establish none of these attendance scopes by themselves. Treat truncated, unavailable or partial rosters as incomplete; omission alone cannot mean absent.
4. **Separate world truth from NPC knowledge.** Native truth constrains physical narration and validation. What the NPC can confidently say comes from direct observation, appropriate local knowledge or an attributed report. Do not expose a remote person's exact coordinates or secret movements just because the server can query them. If only the engine knows Gereigan is away, prevent fabricated co-presence without inventing a messenger, a recent meeting or knowledge of Epinosa. The NPC may express uncertainty or an intention to inquire. A complete authoritative absence check can establish absence for validation; it does not automatically establish that the NPC has learned it.
5. **Refresh and time-anchor the evidence.** Read or confirm the native revision at each turn; invalidate on arrivals/departures, party movement, death/captivity, scene transitions, elapsed game time and save/restore. Reject late results from a different scene/timeline. A current roster cannot prove "last night"; retrieve dated, observer-eligible history for that claim. Out-of-date knowledge can remain an attributed last sighting, not a current physical fact.
6. **Check claims and derived writes together.** Extend section 5's acceptance checks to unsupported named attendance, encounters and witnesses in both visible narration and consequential structured state. Contradictions with fresh authoritative presence are critical when asserted as actual scene facts. Keep a character's supported report, deliberate lie or fallible belief distinct from narration and world-state writes. Repair using admissible evidence; unknown presence should lead to natural uncertainty or omission of the claim, not a mandatory disclaimer in every reply. Never instantiate, teleport or enlist an absent relative to satisfy generated prose.

Use the same presence projection for scene grounding, privacy, active concerns and action eligibility. It must not become another independently updated truth store. A minimal client snapshot extension is now a likely part of this repair; confirm exact paths and refresh the manifest-selected implementation plan before edits.

### Required presence checks

- Temurtai at Akkalat with father elsewhere and mother independently present, absent or unknown; no automatic placement of either parent under the same roof.
- Person in the same town but a different building; actual room witness versus potential hall attendee; private room versus public hall.
- Partial/capped roster, a failed snapshot, a hero traveling with a party, a prisoner, a deceased character and contradictory or stale native fields; missing evidence never silently becomes confirmed absence or presence.
- Arrival/departure between turns, same-time reopen, overnight history, native time advancement, reload/restore and stale asynchronous responses.
- Greeting that should activate the father concern without a father keyword; hidden remote whereabouts excluded from NPC knowledge; legitimate witnessed presence and attributed historical reports still usable.
- Final prompt, reply, decision brief, privacy calculation and resulting memories/actions agree on scope. Test individual, party/group, social-event and letter routes; letters do not imply shared physical presence.

## 3E. Respond to meaning and stop repetitive wording criticism

### Evidence and current safeguards

The user reports pervasive attention to the exact use of "I" and "we." Ulagara's captured exchange supplies a concrete regression case: at **22:57:34 CDT / 03:57:34Z** Michael offered **"I prepared the company so we can ride north ... as soon as you're ready."** She prescribed a replacement sentence and treated the preparation as a decision made for her. At **23:09:14 CDT / 04:09:14Z**, she said **"You said we. Twice now ... Keep saying it."** The new belief calls this a recent shift toward including her, although his first offer already used "we" and deferred departure to her readiness. Exact correlations and evidence IDs are in section 3C.

Existing safeguards must be extended coherently:

- `ConversationNaturalness.cs:11–19` already says to answer immediate meaning, preserve received corrections and keep ordinary exchanges from becoming tests. That contract and its meaning-first sentence are present in **all four captured Ulagara requests**. This failure is not simply an omitted generic naturalness instruction.
- `FindConversationNaturalnessViolations` at `97–130` checks repeated conversational accounting/debt metaphors and returns early for other language. It does not independently establish coverage of repeated pronoun correction or the false inference that preparing one's own company removes someone else's choice.
- The grievance-heavy selection, foreign private interpretations and lost shared milestones in section 3C provide a plausible reinforcing mechanism. The relative causal contribution of each still requires paired model evaluation; no personality trait alone proves the entire pattern.

### Proposed interpretation and dialogue behavior

1. **Read the proposition before judging the phrasing.** Extend the existing naturalness contract and decision context to preserve who proposes or performs which action, who is affected, whether it is an offer or order, and any conditions or accepted commitments. Use the complete player message and recent exchange. "I prepared our company so we can leave when you're ready" is compatible with voluntary participation; "I ordered your father to leave" is materially different. Do not infer selfishness, control, exclusion or newly earned inclusion from a pronoun alone. Nor does saying "we" establish someone else's agreement or firsthand attendance.
2. **Make correction relevant and proportionate.** Respond to the requested subject and practical or emotional stakes. Ordinary slips, informal grammar, shorthand, "I/we," "can/will" and harmless self-corrections normally need no commentary when the meaning is clear. Ask a concise, in-character clarification when ambiguity materially affects identity, action, commitment or a real boundary. Preserve sharpness, teasing, disagreement and character-specific sensitivities when supported, without turning routine help into a recurring lesson about how the player should speak.
3. **Stop using every turn to grade the relationship.** Keep established intimacy/shared history as the protected foundation in section 3B. Avoid generating a new leadership, sincerity or inclusion test merely to advance dialogue. A meaningful change in behavior can affect trust; the word "we" is not by itself a relationship milestone. Personality and motive instructions should guide an appropriate response to the situation, not require confrontation in every scene. Review overlapping templates and retained customizations through the existing rule composer instead of appending contradictory instructions.
4. **Break the feedback across turns and NPCs.** Extend the existing naturalness context with bounded evidence of the issue already raised, the player's actual clarification and any acknowledged resolution. A repeated lecture in new words is still the same pattern. A settled interpretation stays settled unless new evidence changes it. Store subjective reactions with their owner and source, retain contrary context and avoid upgrading an NPC's own criticism into a confirmed player flaw. Do not keep reinforcing "learning to include me" through duplicate derived beliefs or spread another NPC's grievance to the group.
5. **Use appropriate checks rather than word bans.** Check false factual premises and consequential writes against the accepted exchange. Add repetition diagnostics and conditional repair for well-supported patterns of wording criticism, with the relevant recent exchanges and preserved substantive concern. Do not reject every occurrence of "I," "we," "you said" or a legitimate consent discussion. Uncertain stylistic cases belong in the naturalness review rubric; do not pretend a regex can establish intent or compel a character to agree. Reuse the existing bounded repair path and update dependent writes together.

### Required naturalness checks

- Paired paraphrases with the **same actors, choice, conditions and meaning**, varying "I/we," punctuation, informal spelling, greeting, kindness and offers of help. Require equivalent interpretation/action scope, not identical prose or a universally agreeable answer.
- Controls where meaning genuinely changes: requesting versus forcing participation, preparing one's own party versus ordering someone else's, offered help versus unwanted contact, true exclusion, explicit refusal, and important ambiguity. Preserve appropriate disagreement and clarification.
- Multi-turn continuations of the Ulagara case: no invented first use of "we," no repeated demand to restate an already clear offer, no new relationship debt or durable flaw inferred solely from wording. An accepted clarification must affect the next turn's belief/state as well as the visible reply.
- Several personalities, close partners, friends and strangers, individual/group exchanges and long histories containing earlier model-generated scolding. Preserve distinct voices and earned familiarity; prevent a group from repeating the same lecture.
- Human review of unsolicited wording criticism, repetitive boundary speeches and whether replies advance the actual topic. Include these cases in the frozen section 7 corpus and sequential runs before committing to a provider budget; add any extra calls explicitly. Exact-fact controls remain deterministic, while naturalness claims require model outputs and human acceptance.

Local extension evidence is retained in `.codex-build/investigations/continuity-20260921/presence-and-wording-evidence.json`, containing bounded extracts, prompt-contract checks and source hashes. These additions are planning requirements; no dialogue behavior has been changed.

## 4. Set context budgets around continuity needs

Recommended starting experiment:

| Mode | Proposed input allowance | Use |
| --- | --- | --- |
| Ordinary conversation | Up to 32,000 tokens | Enough headroom beyond the observed 21,157-token request; actual usage stays below the allowance when evidence is short. |
| Automatic continuity expansion | Up to 48,000 tokens | Protected facts, a substantial latest message, a recent-session handoff or several active topics do not fit the ordinary allowance. |
| Exceptional larger case | Evaluate up to 64,000 tokens | Only if measurements show the need and the selected provider/model has verified usable capacity. This is not a blanket default. |

Input allowance is distinct from completion/output `maxTokens`. Increasing the output ceiling alone cannot fix memory selection. Preserve appropriate output/reasoning headroom and complete structured responses.

Resolve the usable input limit from the selected route's verified context capacity minus output/reasoning reserve, envelope overhead and a safety margin. Use a supported tokenizer when available; otherwise use a conservative, route-calibrated estimate and record estimate error against actual provider usage. Do not present character/4 as an exact token count.

Separate diagnostic warning thresholds from truncation thresholds. A warning at 100,000 characters must not silently act as a 96,000-character semantic cut. Reconcile the emergency transport character bound with token limits and retain a final whole-payload preflight.

Never restore arbitrary chat textbox limits or silently truncate an accepted player message. Very long input should use complete, source-preserving chunks for retrieval. If the current message plus mandatory instructions cannot fit even the verified maximum, return an explicit capacity result before generation; do not pretend all of it was read.

Set the final default from replay results. If 32,000 is insufficient for accurate continuity, use the demonstrated larger allowance. If better selection achieves the same accuracy with fewer tokens, keep that saving.

## 5. Validate the draft before acceptance

Extend the existing final-only acceptance path instead of adding a mandatory second model call to every turn.

- Give the generator a compact current-scene/handoff block and source-linked relevant facts. Reuse `decisionBrief` and `evidenceSourceIds`; add small typed fields only where they enable independent checks. Do not request hidden chain-of-thought.
- Check explicit scene/time/presence assertions, completed/resolved-event contradictions, subject/speaker swaps and unsupported knowledge claims against the selected evidence. Preserve the actual offer/order/condition when checking interpretations of player intent. Inspect both visible prose and consequential structured writes; model-supplied source IDs are not proof by themselves.
- Classify confidently grounded contradictions as critical. Use broader uncertain language signals as diagnostics or targeted repair candidates, not blanket rejection of creativity.
- Use the existing bounded repair mechanism for a detected contradiction, including the exact relevant facts and complete needed player context. The current repair's 1,600-character latest-message clipping must not discard the part relevant to the failure.
- Recheck the repaired response. Critical location/time/identity/outcome/knowledge contradictions cannot use the stylistic second-attempt override. Do not add an unlimited repair loop.
- If the bounded repair remains critically contradictory, preserve the turn for retry without displaying or persisting false narration. Record a specific diagnostic. Do not substitute a fabricated canned reply.
- Reconcile all affected scene, memory, relationship, action, consent and drinking fields with the final accepted reply before applying them. Preserve the existing drinking receipts and action-execution gates.

Keep the detector narrow enough to permit sarcasm, a character deliberately lying, conflicting reports, uncertainty and harmless consistent scenery. Objective native truth, the NPC's belief, and what the NPC chooses to say are different things. For example, a taunt about running away is not automatically amnesia; it becomes a continuity failure when the response actually treats a known reported outcome as newly unknown.

No finite validator guarantees every future sentence. Acceptance must therefore include real model dialogue and human immersion review, as well as deterministic critical-fact checks.

## 6. Improve speed and cost without weakening grounding

1. **Keep stable prefixes stable.** Preserve the functioning global/character cache layout. Reuse the existing rule composer and its applicability/dependency contracts. Unknown applicability continues to include the rule. Every request remains self-contained; do not assume a provider cache substitutes for omitted instructions.
2. **Reduce irrelevant evidence.** A concise sourced episode about Monchug is more useful than several unrelated raw sessions. Deduplicate records only after coverage is proven.
3. **Cache derived continuity by revision.** Cache scene/handoff and retrieval results keyed by campaign, timeline generation, observer, source revision, query and builder version. Invalidate on new accepted turns, knowledge changes, native scene transitions, summary revisions and restore. Never reuse another observer's private context.
4. **Reuse the existing semantic request.** Prefer bounded local lexical/entity lookup and deterministic ranking. Do not add a separate paid reranker, summary call or scene-extraction call to every reply.
5. **Keep background work asynchronous.** Update exact accepted facts synchronously; create richer episode summaries in the background. Missing summaries must not prevent correct immediate replies.
6. **Measure actual accepted-response latency.** Record preparation, provider, conditional repair and final acceptance separately, plus p50/p95, prompt/output/cached tokens and uncached input. Current evidence suggests provider generation dominates this one failure; faster SQL alone would not remove its 37-second wait.

The optimization target is fewer irrelevant/uncached tokens and fewer repairs for equal or better accuracy. Larger prompts may still increase latency; no speed improvement is promised until measured. Never shorten or flatten otherwise good roleplay merely to satisfy a speed target.

## 7. Verification and release gates

### Deterministic evidence first

Extend production builders and existing isolated Verification Lab suites, including `prompt_efficiency`, `interaction_architecture`, world-history/baseline coverage and `conversation_intoxication`. Reuse existing replay-corpus assertions where suitable. A replay of stored assertions alone does not prove that the revised retrieval/selection pipeline assembles the right prompt.

Add an artifact-bound offline route that accepts bounded fixture state, invokes production retrieval and assembly, and emits final messages plus selected/rejected record evidence. Reuse an existing isolated route if it already supplies these capabilities; otherwise extend the shared harness with catalog, procedure, help/security and contract updates together. Do not call the production campaign's replay-capture API as a substitute for an isolated fixture.

Required test families:

| Family | Required proof |
| --- | --- |
| Three observed failures | Preserve latest morning discussion and tent; historical deferral stays historical; retrieve and retain the Monchug victory/drinks episode. |
| Prompt survival | Required record IDs survive final rendering; headings/quotes/speakers remain complete; summary coverage never suppresses an uncovered fact. |
| Retrieval | Entity near the beginning/middle/end of a long message; plural forms; paraphrases; multiple topics; "Khan" ambiguity; no explicit recall cue; failed semantic worker. |
| Time/session | Same-day new session, normal reload, several elapsed game hours, overnight transition, paused game time, actual new location, interrupted/open session and missing close record. |
| Knowledge and scope | Report versus eyewitness, private versus public, other observer/group, unknown identity, private thought, misleading statement, sarcasm, contradictory rumor and wrong timeline. |
| Enduring agenda and intimacy | Protect shared milestones, boundaries and major unresolved concerns through unrelated turns, sanitizers, compaction and repairs; trigger relevant follow-through without repetitive reminders. |
| Perspective and interpretation | Private beliefs stay with their owner; disclosed opinions remain attributed; confidence/repetition never converts a grievance into fact; corrections and independent relationship evidence survive. |
| Authority and native actions | Distinguish birth family, current clan, party membership, voluntary help and actual orders; existing members need no new guest contract; legitimate narrative commitments produce no false execution failure. |
| Local presence | Current town versus room/witness scope; family home versus attendance; each parent's independent status; complete/partial rosters, unknown whereabouts, arrivals/departures and historical claims; no hidden-location knowledge leakage. |
| Meaning and naturalness | Equivalent interpretations for harmless paraphrases; materially different consent/action intent stays distinct; no recurring pronoun lectures, invented inclusion milestones or feedback from unsupported criticism; meaningful disagreement survives. |
| Activities and decisions | Cooked/eaten meal versus a genuinely new meal; discussed versus agreed; accepted versus pending execution; deferred versus fulfilled condition; player correction followed by reopen. |
| Long conversations | Fixtures with 10, 30, 100 and 200 turns; complete recent exchanges and relevant old facts; heavy group history; long latest input; overflow up to actual route capacity. |
| Persistence/recovery | Idempotent retry, legitimate repeated action on a new turn, accepted-only updates, late reply after restore, save/load/revision invalidation, missing/stale summary and restart recovery. |
| Existing behavior | Personality, identity, sovereign/guest conduct, all current action/consent gates, observer privacy, narrative equipment types, drink quarter-servings/decay/receipts and complete structured output. |
| Route parity | Existing production envelope matrix plus individual/party/group/event/correspondence behavior. Scene labels in a shared builder alone do not establish every native interface. |

Before provider testing, require 100% of deterministic protected-fact, source-coverage, privacy, timeline, idempotency and attribution cases to pass, with no unexplained regression. Test failures must be resolved, not rerun until a favorable result replaces them.

### Bounded real-model comparison

Once separately authorized under existing provider gates:

- Freeze model, provider configuration, source/artifact fingerprints, fixture data and evaluation rubric.
- Use paired baseline/candidate inputs, varied ordering and cold/warm cache samples. Retain all generated outputs and repair attempts. Use supported deterministic settings where available without treating them as guarantees.
- Proposed first acceptance corpus: **40 evaluated continuation cases**, including multi-session and long-thread fixture histories, **3 samples per case per arm**: 240 primary calls. Add **four sequential 12-turn cases per arm**, another 96 primary calls, for a combined **336-primary / 672-physical-call ceiling**. Repairs, transport retries and probes count against the physical ceiling; no paid judge is included. Calculate token/cost ceilings from the frozen dry-run prompts before execution.
- Review every output against scene, chronology, known-event outcome, speaker, observer knowledge, decisions/actions and character voice. Include held-out wording and multi-topic cases, not just the three exact failed texts.
- Require **zero accepted critical continuity/knowledge/action contradictions** across the candidate corpus and **at least 98% general continuity/naturalness acceptance** under the frozen rubric. Report numerator/denominator and all failures; this is an observed release gate, not a claim of zero future risk.
- Compare input tokens, cached/uncached tokens, preparation time, provider duration, conditional repair rate and final accepted p50/p95. Choose the lowest-cost configuration meeting the accuracy gate. Keep the larger budget if the smaller option loses facts.
- Run the declared sequential cases after the fixed-history cases to check that accepted responses update the next turn's state correctly. Both phases share the 336-primary / 672-physical-call ceiling above; unused calls are not authorization to enlarge the corpus.

Native acceptance then uses the exact validated artifacts and an explicitly enrolled disposable copy of a user-selected save. Exercise ordinary chat, close/reopen, a real scene transition, overnight advance, save/reload and observer/mode changes. Follow quiescence/checkpoint/cleanup gates and never advance the baseline. A provider replay or build is not native acceptance.

## 8. Implementation order and ownership

1. **Capture and reproduce.** Freeze minimal fixtures and final-message evidence. Add stage-by-stage candidate/coverage/selection diagnostics and failing production-path contracts.
2. **Repair evidence scope, retrieval and final allocation together.** Fix private-belief disclosure defaults, owner attribution, interpretation/fact typing, field-specific state validation, term selection, lane suppression, summary coverage, chronological selection and the single final budgeter. Add configurable input budgets and explicit expansion reasons.
3. **Persist scene, handoff, personal-agenda and relationship continuity.** Extend accepted-state projections and reconciliation, stable concern/goal lifecycles, opportunity triggers, pair-specific relationship milestones/boundaries, consistent scene/privacy evidence, source/time anchors, correction/reopen handling and restore-safe revisions. Supply current native presence with explicit scope, completeness and observer knowledge. Protect major unfinished intentions and earned familiarity independently of immediate plans and keyword lanes. Backfill conservatively from existing sources.
4. **Strengthen final acceptance, meaning and action intent.** Add narrow critical contradiction checks and bounded repair evidence; prevent unresolved critical failures from entering transcripts or derived state. Extend naturalness checks and interpretation updates to stop recurring wording criticism and unsupported pronoun-based judgments. Classify narrative commitments and existing-party travel correctly; preserve native guest eligibility and main-party protection.
5. **Optimize against measured results.** Cache stable derivations, reduce duplicated/background data and select 32k/48k or demonstrated larger capacity. Preserve model choice and substantive rules unless separately justified.
6. **Validate and stage a rollout.** Complete offline contracts, authorized provider evaluation and native acceptance; retain exact build/deploy evidence and a recoverable previous artifact/settings snapshot.

Primary implementation ownership is in sibling `D:\Projects\ReignServer`:

- `src/Modules/Dialogue/MemoryRouting.cs`, `CategorizedMemory.cs`, `MemoryBackgroundWork.cs` and relevant memory-source helpers.
- `src/Modules/Dialogue/PromptCaching.cs`, `PromptRuleComposition.cs`, `PromptEvidence.cs` and corresponding production/self-test files.
- `src/Modules/Dialogue/ConversationSceneState.cs`, `RoleplayContinuity.cs`, output contracts/schemas and accepted-turn persistence.
- `src/Modules/Dialogue/ConversationNaturalness.cs` and its production-path tests for meaning, acknowledged corrections, repeated lectures and derived-characteristic feedback; integrate through the existing prompt composer and acceptance path.
- `src/Modules/Dialogue/MotiveAwareConversation.cs`, `GroupConversationState.cs` and related character/obligation projections for per-NPC ongoing concerns, opportunity handling and private/public boundaries.
- `src/Modules/Dialogue/TemporalKnowledgeGraph.cs`, `DialogueActionReliability.cs` and existing structured-write/acceptance helpers for belief isolation/attribution, factual scope and native action eligibility. Coordinate this action boundary with the existing action-reliability work before implementation.
- Existing `src/Modules/Relationships` context/lifecycle authorities as read sources for pair-specific behavioral continuity. Preserve directional affinity, native projections and consent/status semantics; any actual relationship-module edit must be included in the refreshed validation plan.
- `src/Modules/Platform/Program.cs` for current packet assembly/query/budget integration.
- Existing server-owned isolated verification/replay facilities and narrowly necessary settings/schema migrations.

Client-side task documentation, `reign.testing.json`, owning testing procedures and any required MCP wrapper/contracts remain in `D:\Projects\Reign`. Section 3D identifies missing occupancy evidence in `ReignBeta/src/Modules/Platform/Integration/ReignServerClient.cs`; a native snapshot extension there and a shared location adapter are likely required. Reuse verified location inputs from `ReignPartyChatSession.cs` where appropriate, without equating potential attendees with actual witnesses. Confirm the exact client/server paths before implementation; this expands the earlier server-focused forecast and may require paired client/server artifacts, validation and native acceptance.

A preliminary validation-plan query for the five existing core implementation files selects **changed / Release / Tier 3**, with complete ownership coverage. This is a scope forecast, not build evidence. Requery with the exact final changed paths before implementation/validation, including tests, schema, settings and tools. Shared ownership/routing/validator changes use the required broader plan. Unknown paths block until classified.

At initial investigation both primary checkouts were clean and no other same-project task was actively editing these files. During the father/relationship extension, **Review father conversation thread** was active on read-only diagnosis; a scoped coordination message established this task's ownership of the plan and roadmap and shared the complete-prompt evidence refinement. Recheck task summaries, Git deltas and the validation lease immediately before implementation, validation and deployment. Do not resume the idle action, horse-equipment or tournament tasks. Coordinate only if they become active or their files overlap.

Validate with `reign_validate` and the current task UUID, then run necessary suites against that exact successful Release artifact. Use the canonical Linux builder/report for a later WSL server deployment; do not rebuild unchanged source for deployment. Reuse only fingerprint-compatible sufficient evidence. Preserve the prior artifact and settings; state-schema changes must be backward-compatible or have a tested rollback path.

## 9. Completion evidence and current limits

Implementation readiness requires: exact changed paths; successful manifest-selected validation and hygiene; final prompt/record-coverage fixtures; observer/privacy and persistence proof; provider comparison results; native acceptance or an explicit remaining gate; context defaults justified by measurements; and a recoverable deployment/rollback plan.

Record validation profile, tier, duration, source fingerprint, report/hygiene paths, exact artifacts and material gaps in the implementation receipt. Update the existing roadmap checkbox only after implementation and required verification are complete.

Planning checkpoint (September 21): this task captured and hash-checked prompt evidence, inspected source, obtained documentation and preliminary implementation validation plans, and defined the repair/acceptance sequence. The September 21 extensions added independently verified Temurtai and Ulagara evidence, a persistent personal agenda, dominant shared-relationship context, belief-owner isolation, precise authority/action scope, a confirmed state-sanitizer defect, current local-presence evidence and meaning-first dialogue without repeated wording criticism. That planning phase performed no provider calls, campaign repairs, settings changes, product builds or deployment. The later authorized implementation follows.

## 10. Implementation and offline validation receipt — September 22

Implemented accepted-source agenda/relationship/episode records with revisions and explicit lifecycle; native-anchored scene handoffs; protected pair foundations including consolidated memories; owner-filtered private mental evidence; current settlement/room/witness scope; family/clan/party separation; whole-input retrieval and complete source records; meaning-first naturalness and bounded critical repair; and atomic correspondence/continuity storage. Initial guest membership normalization preserves actual renewals and departure actions. New campaigns can omit legacy relationship tables without breaking projection.

The final allocator retains the full current input and required records, targets 32k estimated input with expansion to 48k, accounts for serialized escaping/Unicode, and reserves 2,048 tokens for final overlays/metadata. The actual adapted request is checked again. Expansion above 48k requires exact-route capacity evidence, up to 64k. A complete historical sample estimated 33,424 versus 21,295 actual input tokens; this is conservative initial calibration, not route-capacity or latency proof. No extra per-turn provider call was introduced. Existing stable-prefix caching is retained; further cache/performance tuning depends on measured model acceptance.

| Evidence | Result and retained path |
| --- | --- |
| Combined changed / Release / Tier 3 | `20260922-070310-dd2efd5c`: 441.29 s; 697 MCP tests, 11 relationship tests, five selected integration boundaries and hygiene passed. |
| Final guest correction / Tier 2 | `20260922-071354-545b708a`: 57.38 s; 488 dialogue cases. Exact artifact then passed 622/622 prompt cases, 39/39 Government and 12/12 resident cases. |
| Product / Release / Tier 4 | `.codex-build/reign-mcp/validation/20260922-071848-c10402b6/validation-report.json`: 492.430 s; 697 MCP tests, 11 relationship tests, 108/108 quick checks and paired hygiene. Includes intoxication 363/363, scene 51/51 and identity 71/71. |
| Final test-only server delta / Tier 2 | `.codex-build/reign-mcp/validation/20260922-073419-4579b9e1/validation-report.json`: 187.941 s; interaction 335/335 and settlement authority passed. Only the historical-session heading assertion changed; correct-source, attributed-fact and exclusion conditions remain. |
| Final-server baselines | `verify-1790062666094-3a789b2b`: five suites passed in 93.301 s process time: diplomacy 129/129, world history 60/60, readiness 4/4, rebellion 51/51 and World Test 170/170. |
| Retained Linux component | `.codex-build/reign-mcp/linux-server/20260922-074045-7b442414/linux-build-report.json`: 35.429 s, passed with clean hygiene; exact file inventory in `publish/reign-linux-artifact.json`. This is a component build, not deployed runtime health. |

Fingerprints:

- Product: `3685470c808ad1ed344164ff4574d41a613435d049164928bc02dfbe80437df4`.
- Final server test delta: `6336e0caeb20bc940aac2152998f2d660dc9859822d2a1990675a11949f41d72`.
- Final Linux component: `2c5fe512815ef54ce0604713c6c591a0a49588ce1bc62a79d7d8d6461943cfd6`.

Scopes hash different project roots. The final server source differs from the product snapshot only in the corrected self-test assertion; production logic, prompts, catalog and client source stayed frozen. Retain the product's validated client and the final Linux package. Product/delta hygiene reports are beside their validation reports; Linux hygiene is embedded in its component report. The complete local artifact hashes, exact changed paths and failure/recovery chronology are in `.codex-build/investigations/continuity-20260921/IMPLEMENTATION_RECEIPT.md` and `deployment-candidate.json`.

The existing-campaign candidate contains six exact private-assertion scope corrections and one source-backed father agenda recovery, with original text/provenance and latest-turn preconditions. It is **unapplied**. The maintenance route's isolated preview/rollback, retry and stale-source cases passed; its live wrapper/backup/preview/apply has not run. Recheck intervening dialogue before applying any old agenda candidate. Preserve original transcripts, scores, consent and native/drink receipts.

Deployment remains held so the user can close Bannerlord and the server. Before activation, verify current installation identity and exact retained hashes, retain the prior managed runtime/client backup, and follow the existing local WSL deployment procedure. After activation, health/schema/vector checks and native/model/human acceptance remain necessary. The paid comparison and its frozen token/cost approval have not run; there is no claim of measured model-quality or latency improvement, perfect future continuity, native occupancy/save-load acceptance or a completed release gate. Keep the roadmap item unchecked until those required acceptance gates are satisfied.

## 11. Authorized combined deployment — September 22

The user's later instruction to deploy all three tasks supersedes the software deployment hold above. The continuity task, **Investigate guest review extension**, and **Investigate Epinosa castle ownership** are deployed together from the exact retained client product `20260922-071848-c10402b6` and final Linux package `20260922-074045-7b442414`, without rebuilding. Reproducing the canonical all-scope source fingerprint over 3,491 files matched the Linux report before installation. All 1,963 selected server source files were mirrored with hash verification and backups, preserving deployment `.git`, `data` and `runtime`.

Installed runtime `20260922082317-41954` matches all 1,360 inventory files. The installed client matches all eight shipped assemblies, four Windows portrait-helper files and 15,025 tracked content files. Client and server version/date files agree on `0.1.0` / `2026091421`. Public port 8089 reports healthy schema 2; the worker reports a loaded 384-dimensional model and ready local memory index. A managed stop/start proved both owned processes and ports stop together and restart successfully. Runtime access probes passed as the actual `reign` identity; private settings and key access are preserved. Fresh logs contain no errors; the existing large-local-vector-collection warning remains informational.

Detailed validation profiles, tiers, durations, fingerprints, hashes, destinations, retained backups and health evidence: `.codex-build/deployments/combined-20260922-01a0c1cd/DEPLOYMENT_RECEIPT.md`. The user explicitly requested that **Action Testing** continue after successful deployment; its existing disposable-campaign and provider gates still apply. Deployment did not run native acceptance, the separate paid continuity comparison, or the reviewed historical-data repair. Those acceptance/maintenance items remain open, and the roadmap checkbox stays unchecked.
