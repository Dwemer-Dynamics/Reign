# Reign Native World History

Reign records authoritative campaign facts separately from dialogue memories, rumors, and generated event prose. The game-side `ReignWorldHistoryCampaignBehavior` listens to Bannerlord campaign events, captures immutable entity and role snapshots, and sends them through an off-thread persistent outbox.

## Runtime Storage

- Offline outbox: `Modules/ReignBeta/HistoryOutbox/<campaign>/<timeline>/pending.jsonl`
- Canonical database: `server/app/data/campaigns/<campaign>/world_memory.sqlite`
- Append-only mirror: `server/app/data/campaigns/<campaign>/world/history.jsonl`
- Active-save branches are stored in `world_history_timelines`; loading a save whose sequence predates the server head creates a new active branch.

The database separates events, event entities and roles, knowledge-delivery rules, claim checks, and ingestion state. Event rows retain a lossless JSON payload while normalized columns and FTS5 indexes support bounded searches.

## Capture And Coverage

Battle outcomes include every participating party and named hero, leaders, sides, initial forces, casualties, prisoners, loot, contribution, location, and result. Captivity transitions are correlated with battles to distinguish direct rescuers, assisting participants, and rescued beneficiaries where native state supports that attribution.

The installed game currently exposes 276 public `CampaignEvents` endpoints. `ReignWorldHistoryCoverage` fingerprints that surface and classifies every endpoint as a dedicated capture, a correlated/reconciled outcome, or an intentional telemetry/query exclusion. A game update that changes the endpoint set produces a prominent runtime warning.

## Knowledge

- Participants are eligible immediately.
- Ordinary event news reaches involved kingdoms after three campaign days.
- Major event news reaches involved kingdoms after one day and becomes globally eligible after three days.

The objective ledger is not character knowledge. An NPC receives a knowledge receipt only when an eligible event is actually selected for that NPC's context.

## Claim Verification

`POST /world-history/verify` classifies the reason for a request and the claim family, generates at most 256 indexed candidates, sends at most 64 factual projections to Minime, and expands at most 12 candidates into canonical correlated evidence. Minime only reranks candidates. Deterministic role, identity, outcome, quantity, location, date, and coverage checks issue the verdict.

Verdicts are `verified`, `partially_verified`, `contradicted`, `not_found`, `history_incomplete`, `insufficient_evidence`, or `ambiguous_identity`. Missing evidence never becomes a contradiction when capture coverage is incomplete.

Every check has an objective verdict and a speaker-visible verdict. Dialogue receives only speaker-eligible evidence. The objective result remains internal to verification and lie detection.

## Historical Lie Detection

`POST /world-history/lie-check` reuses canonical claim verification and then classifies the target's strongest eligible evidence as firsthand, secondhand, or unavailable. A materially false claim is uncovered automatically when the target was directly involved. Secondhand evidence uses the live native skill averages `(Roguery + Charm) / 2` for claimant and target.

The deception chance is `clamp(10, 80, 50 + 0.2 * (claimantAverage - targetAverage))`. Its SHA-256-derived roll is stable for the same claimant, target, normalized claim, evidence set, and knowledge basis, so repeating a story or reloading cannot reroll it. Truthful, incomplete, ambiguous, and unsupported claims never become lies.

Detected falsehoods select a deterministic reaction from the target's personality, directional fear, captivity, relative clan tier, and party strength. The available strategies are confrontation, probing, quietly recording the falsehood, or feigning belief for possible leverage. The language model expresses that selected strategy but cannot change the result.

Accepted false claims remain separate unverified beliefs. When relevant eligible history is selected later, linked beliefs are reconciled and may become private discovery memories. Reign does not scan or inject the full ledger when news availability changes.

Dialogue receives only the target-safe `promptPacket`. Failed detection never exposes the objective verdict or hidden evidence. When the player character detects an NPC's historical falsehood, the game displays a private recognition line without revealing facts the player does not know.

## APIs

- `POST /world-history/timeline/open`
- `POST /world-history/ingest-batch`
- `GET /world-history/query`
- `GET /world-history/event/{eventId}`
- `GET /world-history/correlation/{correlationId}`
- `POST /world-history/verify`
- `POST /world-history/lie-check`
- `GET /world-history/lie-checks`
- `GET /world-history/export`
- `POST /world-history/tests`

The control center's **World History** tab provides search, evidence inspection, claim verification, lie-check inspection, and the focused self-test runner.
