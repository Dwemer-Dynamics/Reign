# Reign Semantic Memory

Reign combines semantic retrieval with the existing campaign-scoped SQLite and FTS5 memory system. SQLite remains authoritative: vector search returns source IDs, and Reign reloads those rows and applies its normal knowledge, visibility, timeline, and status checks before prompt construction.

## Runtime

- Managed worker: `server/app/vector-worker/ReignVectorWorker.exe`
- Model cache: `server/app/data/models/embeddings`
- Local vectors: `server/app/data/vectors/local`
- Per-campaign embedding ledger and queue: `server/app/data/campaigns/<campaign>/world_memory.sqlite`

The worker uses FastEmbed's ONNX build of `BAAI/bge-small-en-v1.5`. It starts without loading the model; the first indexing or semantic query downloads and loads it. Reign continues using FTS5 while the model is unavailable.

Local mode stores vectors persistently through Qdrant Client local mode. External Qdrant mode still performs embedding inference locally and sends only vectors plus campaign/source metadata to Qdrant. Raw memory prose is not included in Qdrant payloads.

## Retrieval

Reign materializes FTS matches by source ID from the full active archive, then unions them with semantic candidates, recent and important records, relationships, and obligations. This keeps exact names and quotations retrievable when the vector worker is unavailable, even when a match is older than the normal recency window. Deterministic relevance is scored first, semantic similarity adds a bounded bonus, and Minime may rerank the final candidate pool.

Rows whose status is no longer `active`, including episodic memories replaced by consolidation, are rejected both by the indexing pump and again when semantic hits are reloaded from SQLite. The final prompt packet labels campaign day, acquisition type, confidence, importance, and source where available, and applies the router's per-lane budget before global prompt truncation.

Conversation turns are semantic candidates only for exact-history or continuity routes. World-history vectors only locate possible evidence; deterministic event, entity, role, date, and coverage checks continue to issue factual verdicts.

Exact conversation recall uses FTS5 BM25 order before semantic similarity and recency. Named or game-resolved entities can open the world-affairs lane even when the player's wording omits explicit words such as `war`, `siege`, or `kingdom`.

## APIs

- `GET /memory/embeddings/status`
- `POST /memory/embeddings/reindex`
- `POST /memory/embeddings/retry-failed`

The worker provides `/health`, `/topic`, `/rerank`, `/vectors/upsert`, `/vectors/search`, `/vectors/delete`, and `/vectors/status` on `127.0.0.1:8082` by default.

Indexing is asynchronous and idempotent. Source-table triggers enqueue changed records, content hashes skip unchanged documents, interrupted jobs resume with bounded backoff, and backend/model changes retain prior index generations until replacement indexing succeeds.
