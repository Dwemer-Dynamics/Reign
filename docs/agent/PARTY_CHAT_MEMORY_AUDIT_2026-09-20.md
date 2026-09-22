# Party chat memory and Arena Overhaul audit — 2026-09-20

Status: investigation and repair design complete; implementation, historical-data repair and native/model acceptance remain open.

Task: `01a0c055-897d-7852-8acf-78aece8a5ac3`.
Campaign: `woERXl3xD23E`; timeline: `main_258fb5560008_recovered_64e99061c8`.
Reviewed: the six replies in the two current Chaikand party-chat exchanges (2026-09-20 19:18–19:29 UTC), their captured requests/prompts, authoritative tournament history, relevant current source, and the installed Arena Overhaul assembly.

## Findings

The observed problems span event capture, retrieval and generated dialogue. A prompt-only change would leave incorrect event records and incomplete counts underneath it.

### 1. Arena Overhaul's team result is reduced to a single champion

The user confirms Michael, Temurtai and Ulagara won the latest team tournament together. Reign's native history records Michael as the winner and the others only as participants. A separate reputation listener emits these records for the same completed event:

- `...058154_627c1c7d`: “Temurtai failed to win a tournament in which the player also competed.”
- `...058155_bd634573`: the equivalent Ulagara loss record.

This follows directly from `ReignBeta/src/Modules/Reputation/Campaign/ReignCombatReputationCampaignBehavior.cs:207–221`: every hero other than the callback's winner becomes a `CounterOnly` loss against `champion_of_the_pit`. The world-history listener at `ReignWorldHistoryCampaignBehavior.cs:394–399` likewise captures no team identifiers, teammates or team outcome.

Those erroneous reputation records are a separate confirmed data defect. They were **not** present in the inspected Ulagara prompt, so they are not established as the immediate cause of her wording.

Installed Arena Overhaul inspection explains the information loss:

- Module/file version: `1.5.9.4`.
- Assembly: `Modules/ArenaOverhaul/bin/Win64_Shipping_Client/ArenaOverhaul.1.4.6.dll`.
- Product version: `1.5.9-rebuilt.1.4.6-hotfix4-prize-variety`.
- SHA-256: `2D3AAEF774FEA843D7969D450A87B6D3109B010EF66AF63B03604A5F09C1B801`.

`ArenaOverhaul.TeamTournament.TeamTournamentBehavior.EndCurrentMatch` obtains the winning team from `LastMatch.Winners`, assigns its **leader** to `Winner`, and dispatches the native `OnTournamentFinished(Winner.Character, allParticipants, town, prize)`. The winning leader still exposes `Winner.Team.Members`, and every member exposes `Character`. Only after that synchronous callback does `CurrentInfo.Finish()` clear the selection/current tournament information.

The relevant distinction is a single champion in the native completion callback versus Arena Overhaul's persistent team-tournament result. Ordinary native round teams must not be mistaken for co-winners of the whole tournament.

The [upstream Arena Overhaul repository](https://github.com/Bannerhaul/Bannerlord.ArenaOverhaul) identifies the mod source. The installed custom build, inspected locally, is the evidence for the callback order and member properties above; upstream HEAD is not assumed to match it. The running process's managed assembly identity was not independently enumerated.

### 2. Seven recorded wins are being reduced to three examples

The authoritative tournament-finished ledger contains these player wins, in order:

| Town | Campaign day | Temurtai / Ulagara in recorded participant list |
| --- | ---: | --- |
| Saneopa | 91079.66 | Neither listed |
| Phycaon | 91081.55 | Neither listed |
| Odokh | 91084.51 | Neither listed |
| Akkalat | 91086.88 | Neither listed |
| Varcheg | 91094.89 | Both |
| Epicrotea | 91096.22 | Both |
| Chaikand | 91101.65 | Both |

This confirms **seven recorded victories** and their participation in the last three. The user separately confirms shared team victories and knowledge of three or four earlier wins when they met. The legacy participant lists alone do not prove team membership or exclude spectators.

`ReignServer/src/Modules/Dialogue/WorldHistoryDialogueMemory.cs:95–101` selects at most three recent wins for recognition. The final native-history packet contains at most six events (`Take(6)`, line 182). It supplies no aggregate count and no explicit “these are examples, not your total record” field.

The first round of replies follows the three supplied examples—Epicrotea, Varcheg, Akkalat—then turns them into “You won three.” This is consistent with confusing a retrieval window with the complete record. Simply increasing the window does not fix that logical error.

### 3. Firsthand evidence was supplied, then contradicted

Ulagara's exact provider-bound prompt contains:

- Varcheg: `firsthand participant`.
- Epicrotea: `firsthand participant`.
- Akkalat: `public tournament news; secondhand`.

It also contains a comprehension entry describing **her own Varcheg tournament victory**. Her reply nevertheless says:

> “Varcheg I heard about until I was sick of hearing about it.”

It then asserts “Akkalat I was there for.” That claim is not established by the supplied native participant record and conflicts with the packet's supplied secondhand framing; the record does not rule out spectator attendance.

Temurtai likewise describes “Three tournaments I've heard tell of.” This is not solely missing memory: existing authoritative participation labels are failing to constrain the generated reply. Preserve the distinction between tournament participation, spectating, traveling with the party, and hearing a report.

### 4. The just-finished result is missing from recall; inconsistent clock precision explains it

The Chaikand result was already ingested at 19:12:02 UTC, before the replies. Its event day and all participant/public knowledge-rule availability days are `91101.65`.

The native profile in each audited request reports `observedWorldDay = 91101.6461709456`. Client party-chat payload construction uses the same native double clock (`ReignServerClient.cs:3404`), while world-history event creation first casts it to `float` (`ReignWorldHistoryCampaignBehavior.cs:715,896`).

Arithmetic:

- Native double: `91101.6461709456`.
- Float representation: `91101.6484375`.
- Retained serialized event day: `91101.65`.
- Both event and knowledge predicates require their day to be less than or equal to the caller's day.

The result is therefore “in the future” relative to the observed native clock, despite already happening. Chaikand is absent from the exact prompt; its old tournament-start announcement remains present. This strongly explains why the prior Akkalat result occupies the third recognition slot.

Coverage boundary: the audit request omits the top-level `worldDay`; the exact observed native clock, current payload source, stored event/rules, missing prompt result and arithmetic all support this diagnosis. No production retrieval replay or live database modification was performed.

### 5. One invented defeat becomes a group fact

The first speaker, Monchug, invents the companions' collapse before the final. Temurtai reacts with an invented thinning bracket; Ulagara promises to outlast the bracket next time.

After the player explicitly introduces both as partners in three victories, Monchug still says Ulagara “didn't finish at all,” and Ulagara answers “I lost a bracket.”

The prompt tells later speakers to react to prior speakers and calls the group transcript authoritative. That must mean authoritative evidence of **what was said**, not proof that every claim within it happened. Here, later replies perpetuate unsupported claims over the player's correction and available historical evidence.

All four companion replies wrote zero `memoryWrites` and reported no recall-consistency repair. This does not prove the conversation was unrecorded—the audit/transcript and other memory lanes exist—but it shows that a remedy relying only on optional model-written memories will not reliably capture the shared achievement.

### 6. Large generic prompts compete with continuity

Ulagara's captured request contains 97,573 message characters, including a 40,821-character system message. Before final assembly, selected context/memory was reduced from 41,460 to 13,638 characters: **27,822 characters removed, about 67%**.

The inspected compactor (`PromptCaching.cs:332–364`) retains text prefixes and suffixes using character/line boundaries. It does not select complete event/turn records by factual importance. The retained historical tail begins with an unattributed text fragment after the compaction marker.

The critical Varcheg firsthand line survived, so compaction alone does not explain that contradiction. It is nevertheless a continuity and attribution risk. The memory worker reported zero pending, running or failed jobs in the status snapshot; there is no evidence here of an indexing backlog causing these replies.

## Recommended repair sequence

### A. Capture the actual completed tournament result once

Introduce a normalized tournament-result snapshot consumed by both history and reputation:

- Stable event/session ID, campaign, timeline, native double day and completed-event sequence.
- Source adapter and detected mod version.
- Tournament format: native individual champion, Arena Overhaul persistent team tournament, or unresolved.
- Official champion/leader separately from winning team and its complete member IDs.
- Per-participant team, outcome and knowledge basis; individual knockouts only when actually observed.

For the installed Arena Overhaul build, a narrowly scoped optional adapter can inspect the active `TeamTournamentBehavior` during the existing native completion callback, validate its town/winner/final-round identity, and snapshot `Winner.Team.Members` or `LastMatch.Winners`. Read public properties through a version-checked optional integration; do not require the mod or modify its binaries. Snapshot once for both Reign listeners, before Arena Overhaul clears its current state.

A teammate can be knocked out and still win the tournament with the team. Never infer a lost bracket from breathlessness, lack of the prize, not being the leader, clan membership, or absence from an individual-winner field.

A detected but unsupported team format must retain “team outcome unresolved” rather than recording every nonleader as a loser. Preserve native individual-tournament behavior and existing prizes, bets and leaderboard ownership.

### B. Make completed events immediately available using one clock

Use the same unrounded native double day throughout new history events, knowledge rules and conversation payloads. Include the just-completed event ID/sequence in immediate conversation context so asynchronous history delivery or rounding cannot hide it.

Legacy precision handling must be constrained to the same campaign/timeline and an already-observed event boundary. Do not broadly relax all future-event or news-delay filters. Preserve rollback and private-knowledge rules.

### C. Project a compact, protected shared-history block

Compute per-speaker facts from authoritative records before semantic retrieval. For this incident, after the confirmed historical repair, the intended content would be:

- Seven recorded player victories; three shared victories with this companion.
- Shared wins at Varcheg, Epicrotea and Chaikand.
- Earlier recorded wins at Saneopa, Phycaon, Odokh and Akkalat, with observer-specific knowledge provenance.
- Today: the player's team won; this speaker was a winning teammate; Monchug was on the opposing side only when the team snapshot or confirmed correction establishes it.

Store source IDs and provenance, not just prose. Keep counts separate from the bounded list of recent examples; mark incomplete historical coverage when applicable. Companions should remember shared experiences without requiring the player to ask a recall question.

Pin this small block through compaction. Retrieve older dialogue by current relevance and preserve whole attributed turns. Deduplicate general instructions to recover prompt space; do not indiscriminately remove personality, family history or established personal disclosures.

### D. Enforce fact authority across the whole group exchange

Rank verified completed events and explicit recorded roles above prior generated dialogue. An earlier speaker's invented defeat must remain a claim that another character can correct, not become new world truth.

Add a bounded factual-consistency path for event references: referenced event, speaker participation, team outcome, firsthand/hearsay basis, and count scope. Reject or repair contradictions before the reply becomes context for the next speaker and before durable facts are written. Reuse the existing bounded continuity infrastructure where appropriate; do not add an unconditional extra model call to every turn.

Personality should govern how a character feels about a shared win—pride, teasing, exhaustion, envy—not silently rewrite who participated or who won. Apply the same treatment to shared battles, travel, rescues, gifts and agreements when their authoritative adapters supply evidence.

### E. Repair established history without inventing old teams

The retained native tournament events have no team rosters, and Arena Overhaul clears its current selection after completion. Presence alone cannot reconstruct old winning teams because opponents appear in the same participant list.

For this campaign, the user's explicit confirmation identifies the three shared victories. A later authorized repair can attach a dated, user-attested correction to those exact events, supersede only the erroneous co-winner loss projections, and rebuild affected summaries/indices without deleting the original transcript or pretending the team roster was captured natively.

Do not replay tournament callbacks, relationship awards, provider responses or prize/XP effects. Do not relabel every historical participant or clan member as a teammate.

## Required acceptance evidence

Extend existing contracts and the testing catalog/procedure in the same implementation change:

| Case | Required result |
| --- | --- |
| Arena Overhaul team win; teammate knocked out | All actual winning teammates retain team victory; no co-winner loss counter |
| Team loss, player elimination and leave/simulated completion | Actual final result and team membership preserved; no invented player win |
| Native tournament; mod absent | One champion; no permanent-team inference from native round teams |
| Unsupported mod version or missing final data | Explicit unresolved coverage; no guessed roster or loser |
| Duplicate completion and save/reload | One durable event, one set of derived counters; consistent restored history |
| Day 91101.6461709456, immediate paused chat | Just-finished event is available; genuine future/other-timeline events remain excluded |
| Seven known wins, three recent examples | Counts remain seven recorded and three shared; no “only three” inference |
| Varcheg participant; other town report only | No downgrade to hearsay or invented firsthand attendance |
| First speaker falsely calls teammate a loser | Subsequent speakers use result facts and can correct the claim |
| Maximum prompt pressure | Protected facts, complete provenance and speaker labels survive |
| Switch party/individual/event conversation, reopen and reload | Shared history and knowledge provenance remain consistent |
| Historical targeted repair | Only exact confirmed events change projections; no rewards/actions replayed |

Existing useful routes: `tournamentKnowledge` / `baseline.world_history`, prompt-efficiency/continuity contracts and `memory-cross-mode.json`. Current world-history tests cover basic first/secondhand retrieval, same-day news and privacy; they do not cover this installed mod's winning roster, mixed clock precision, aggregate counts or actual generated contradictions.

Use isolated deterministic fixtures first, then the manifest-selected changed-path validation. A real Arena Overhaul native win and natural dialogue on an explicitly enrolled disposable save remain necessary for acceptance. No native advancement or paid provider run is authorized merely by this audit.

## Evidence and provenance

Local private evidence directory: `.codex-build/investigations/party-chat-memory-20260920/`.

- `evidence.json`: six replies, four captured player requests, seven wins, exact Chaikand event and knowledge rules, false companion loss records, prompt diagnostics and inspection scope.
- `prompt-evidence.json`: complete three-message provider prompt, pinned evidence ID `fed5068fe70045a1b0bea9e89c9dc6fa`. All three decoded UTF-8 content hashes match the capture manifest.
- `precision-proof.json`: exact arithmetic and its coverage limit.
- `arena/`: read-only decompilation of five installed mod types. Ignored reference material; not managed or distributable Reign source.
- `documentation-plan.json`: changed-path Tier 1 no-build plan.
- `checkpoint.json`: recoverable Project Memory checkpoint.

Correlation IDs for the initial replies:

- Monchug: `party_chat-lord_6_1-1789931856660-a317f56c`.
- Temurtai: `party_chat-reign_court_town_K2_child_2-1789931915331-77dc0a2e`.
- Ulagara: `party_chat-reign_court_town_K2_child_4-1789931935781-ee78226b`.

Client checkout: `unstable`, HEAD `2908e0d71514480364091deff06bdd4eb0463b59`.
Server checkout: `unstable`, HEAD `0c551f29b3d3418f2bd65ec98ef1d8a4be8863d1`.
Both have substantial pre-existing staged/unstaged work; these findings refer to the observed working source and captured runtime, not clean HEAD-only builds. No other active same-project task was listed in the 50-task coordination snapshot; both repositories' worktrees were inventoried.

Project Memory semantic MCP search did not return and was stopped. The documented CLI full-text fallback retrieved the previous tournament-recognition repair and saved the investigation checkpoint. The independent service and product runtime were not restarted.

Only this audit and the existing roadmap entry are changed by the task. No product source, installed files, campaign state or model settings were changed. Documentation validation receipts are recorded in the evidence directory after the final check.

## 2026-09-20 implementation follow-up

The later authorized integration task bundled the author's Arena Overhaul source as a separate assembly inside the Reign module and added a winning-team snapshot before Arena Overhaul dispatches Bannerlord's single-winner tournament event. Reign consumes that snapshot in world history, combat reputation and celebration text. Dialogue now receives named teammates, firsthand participation and an aggregate documented win count. New client events use Bannerlord's double-precision campaign day; server retrieval tolerates at most 0.005 day for legacy rounded native tournament completions in the same timeline.

The user-confirmed Varcheg, Epicrotea and Chaikand results were corrected in Michael's inactive campaign `woERXl3xD23E`, timeline `main_258fb5560008_recovered_64e99061c8`. Exactly three event summaries and six teammate roles were updated. Exactly six erroneous social-outcome summaries were marked as corrections and their counter-only payload arrays cleared; their original event IDs and the party-chat transcript remain. A full pre-repair PostgreSQL schema backup is `.codex-build/investigations/party-chat-memory-20260920/michael-campaign-before-team-repair.dump` (SHA-256 `B45E85A6D96CBC1C39B8B83B005AB333FE9E41E919418AC8152198CC4E5BB675`). The exact fail-closed transaction is `team-victory-repair.sql` alongside it. MCP read-back confirmed all three named winning teams and the three Ulagara corrections; the corresponding Temurtai records passed the same transaction assertions. Validation, deployment and native conversation acceptance are tracked by the implementing task.
