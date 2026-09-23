# Persistent NPC refusals and credible negotiation

The private `proposalDecisions` contract separates interpretation of an offer from authority to erase a refusal. The actor considers ambitions, dreams, personality, duties, risks, shared history and the player's capacity relative to the NPC and the particular benefit. A lower-ranking player's major promise of power needs actual relative capacity, very high affinity with significant shared history, or poor judgment supporting a risky choice. Tournament prestige cannot prove political power. Bribery, deception and coerced compliance remain distinct from friendship and genuine consent.

An established noble's initial major, unpaid, indefinite travel agreement also requires exceptional established trust, poor judgment, credible patronage or relevant tournament prospects. The initial tournament calibration is five known native wins, retrieved through the existing observer/time/timeline knowledge filter. This permits consideration; personality, duties and the actual offer still decide willingness. It does not grant political credibility.

## State and transitions

Storage and verification are PostgreSQL only, through Npgsql. The offline suite explicitly asserts the connection type and database engine. No SQLite dependency, file or fallback is introduced.

`ConversationAgency.cs` stores `conversation_negotiations` and idempotent `conversation_negotiation_turns` in the campaign database. Identity includes timeline, NPC, player, topic and an optional supplied native object ID. Paraphrases, tactics, channel changes and invented identifiers cannot reset it. Separate people, objects and timelines remain isolated. Existing campaigns start without invented historical refusals. These temporal tables belong to normal whole-campaign PostgreSQL Save Sync snapshots/exports, not the non-temporal testing overlay.

Outcomes: inability, personal refusal, refusal on current terms, undecided, conditional, accepted, accepted under duress. Reopening must address every decisive saved objection; trivial or worse offers preserve the evaluated baseline. Numeric terms require quoted current player support. The code's initial material-change defaults unlock reconsideration, never guarantee acceptance:

- Price: a cash increase of at least 25% (minimum one denar), or securing a previously deferred payment.
- Ordinary duty/risk: an indefinite commitment becoming bounded, or duration reduced at least by half; actual native duty changes also matter.
- Credibility: relevant opportunity improvement of 15 points or clan advancement. Prestige does not reopen a political-power objection.
- Relationship: at least 15 affinity points AND a new significant shared event. An extraordinary personal trust exception requires affinity at least 85 and significant shared history.
- Personal limits: changed relevant traits or underlying duties; payment or waiting alone cannot erase them. Missing facts do not prove change.

Pressure begins after a clear recorded refusal. The first repeat warns; the next three pressure turns can use existing routine negative relationship reactions. A durable penalty count prevents cooling/apologies from resetting that cap. Patience sets the end-discussion threshold at three, four or five repeats. Clarification does not escalate; an accepted apology/withdrawal reduces irritation by one. Seven campaign days can reduce irritation by one, never clear the refusal. Ending discussion is an in-character posture, not a forced screen close. These are internal calibration values, not a player-facing acceptance formula.

## Production integration

Protected continuity carries every compact live boundary and required source ID across individual, party/social, court and correspondence envelopes. Codex output schemas and compact metadata preserve `proposalDecisions`; other providers receive the same prompt contract. The final response guard evaluates after existing repairs and before scene changes, social effects and actions. One bounded repair can reconcile a contradiction. Unresolved or stale replies fail without accepting their proposed effects. The separately guarded action-cooperation probe retains its explicitly artificial acceptance behavior; it cannot prove organic refusal quality.

Relevant compromising memories use existing observer knowledge access, time and timeline checks and retain confidence/source attribution. Threats require credible capacity; blackmail requires supplied evidence. Belief is not a native world fact. Native eligibility, authority, payment and ownership checks still apply. The existing authority receipt binds supported voluntary commands to accepted negotiation revisions and checks duration/payment agreement. Unilateral declarations and official/native authority retain their existing owners.

Letters commit refusal state atomically with dispatch before actions are queued. Letter retries cannot duplicate a reply or pressure penalty. Written pressure uses the existing relationship receipts; neutral boundary markers prevent the party reaction completion pass from reintroducing positive reactions. A repaired reply must also pass the existing identity, kinship, political, continuity and eligibility guards. Action binding checks supported voluntary commands, exact object identity, payment, duration and recurring wage period/payee. NPC counteroffers stay in reconsideration until the player confirms the exact new numeric terms.

No new provider, settings migration, endpoint, UI or historical replay is introduced. State is read from authoritative campaign storage; no separate relationship score is created. The user authorized a server-only deployment on 2026-09-23, coordinated ahead of the storage task.

## Verification

`RunConversationAgencySelfTests` runs in `prompt_efficiency` / `pipeline.prompt_caching` with injected responders and unique `agency_test_` campaigns in isolated Verification Lab storage. It covers 25 repeated attempts under channel labels, bribes versus rerolls, loyalty/duties/rank/judgment/history, coercion, conditional terms, invalid quotes/evidence, real database reopen/isolation, stale revisions, retry receipts, party consequences, atomic letters, exact asset/payment/duration binding and repair. An actual isolated PostgreSQL Save Sync snapshot/rollback verifies refusal revisions and receipt rewind. Existing Lab cleanup/retention owns fixtures.

Offline checks prove engine behavior, not arbitrary semantic interpretation, organic provider realism or native Bannerlord acceptance. Release acceptance additionally requires paired bounded provider scenarios and a guarded disposable native save/reload with exact validated artifacts. Retain transcripts and compare speech with actual effects. Keep the roadmap item unchecked until appropriate acceptance is recorded.

2026-09-23 verification: changed Release Tier 3 run `20260923-173318-07b33129` passed with clean enforced hygiene; artifact-bound `prompt_efficiency` passed all 696 assertions and its separate prompt-budget contract. Linux build `20260923-173951-6556ee2a` was deployed server-only. The installed DLL and full runtime inventory match the Linux artifact, PostgreSQL reports current schema 2, and the model is loaded at 384 dimensions. The canonical validator reused the successful report before activation, confirming unchanged tested source. See `.codex-build/agency-deployment/RESULT.md` for fingerprints, timings, runtime evidence and acceptance limits.
