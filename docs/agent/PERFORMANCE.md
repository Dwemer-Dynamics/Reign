# Reign Performance Ledger

Record measured performance behavior, budgets, regressions, and validated tradeoffs. Every claim needs reproducible evidence; impressions and unmeasured guesses do not belong here.

## Entry format

### YYYY-MM-DD — Area — Finding

- Context or symptoms:
- Decision or root cause:
- Alternatives/failed approaches:
- Consequences or successful fix:
- Verification/evidence:
- Follow-up:

### 2026-08-16 — Repository hygiene — Compile policy globs once per audit

- Context or symptoms: The first enforce-mode scan of the 1,900-file repository baseline ran for more than 25 minutes and returned no validation report.
- Decision or root cause: Git enumeration took only 42 ms for tracked paths and 146 ms for full status, while 806 authored files totaled 44,120,032 bytes. The audit was instead rebuilding every prohibited/secret-name/allowlist glob regex for every tracked path, producing roughly 100,000 regex constructions.
- Alternatives/failed approaches: Waiting through the original loaded MCP route confirmed the delay but produced no evidence directory; manual secret spot checks were not accepted as a replacement for the canonical enforce-mode gate.
- Consequences or successful fix: `RepositoryGlobSet` compiles each policy glob once and reuses it across paths. The repository-scale matcher regression covers 54 patterns across 1,900 paths.
- Verification/evidence: The regression passed in 96 ms. The repaired v4 documentation audit `20260816-050100-41753726` completed through the canonical CLI fallback with zero hygiene issues, and ecosystem run `20260816-050330-7bc8e7d8` passed all 22 operations plus offline Verification Lab with the same enforce-mode gate.
- Follow-up: Keep repository-scale matcher coverage when adding new path classifiers; investigate only if full hygiene latency grows materially beyond source-size growth.

### 2026-09-13 — Relationship worker — Batching bypassed by later features

- Context or symptoms: Retained campaign days required 18–20 seconds each and waited 292–340 seconds. A guarded disposable native day reproduced 16.562 seconds for 1,563 evaluations; historical August 4 worker samples recorded 1.192–1.537 seconds for 843–876 evaluations on different workloads.
- Decision or root cause: The archived August 13 Favoring patch replaced daily in-memory Public Standing lookups with per-direction database reads. Native projection also reloads player IDs per pair. Fling processing reloads/parses 46,030,156 bytes of personality JSON despite persisted judgments and wrote 889 individual daily rolls in the fresh test. Core COPY/merge remained fast at 90/59 ms; the expensive stages were pair processing (6,742 ms) and finalization (5,237 ms).
- Alternatives/failed approaches: Increasing dice-computation parallelism or the evaluation throttle would not address the dominant stages; measured parallel dice work was 12 ms. PostgreSQL outage isolation is a separate reliability requirement.
- Consequences or successful fix: Deployed scoped standing/roster/player reads, targeted unresolved fling documents and batched roll receipts, co-presence indexing, immediate prepared lifecycle writes, fair active-campaign priority, durable phase metrics and bulk identity-roster replacement. A comparable same-checkpoint day fell from 16.562 to 5.280 seconds on the first revision. A further ten native days averaged 6.200 seconds with a 0.773-second median pair loop. Those days exposed a mid-day invalidation fallback to 2,071 point reads; the final revision retains participating IDs and refreshes stale standing/roster state in a batch, including nested scopes and newly inserted formerly missing rows.
- Final native result: Four fresh days on the final artifact averaged **4.516 seconds** (4.180–5.197), at 1,593–1,616 pairs/day. A natural standing mutation required two batch reads. Relationship processing completed before native pause; input queue waits were 0–4 seconds. The broader guarded operation still took 201.580 seconds, including 97.497 seconds of native advancement. The game was finally stopped and drained at day 91280.406858; the baseline and run-owned rolling checkpoint were preserved.
- Verification/evidence: [Report, artifact hashes and native evidence](RELATIONSHIP_PROCESSING_REGRESSION_2026-09-13.md). Broad Release `changed` Tier 3 validation `20260913-235919-d990d4cd` passed 195 relationship, 62 persistence, 555 MCP and 11 kernel checks. Final focused Tier 2 `20260914-003909-52f7f7b2` passed 195/195, including the expanded immediate-mutation/bounded-read contract; hygiene had zero issues. Exact final server SHA-256 `2B24114CBB58DD4C96A9830793955FF94CE99EE1627D0FF07AE061369DD23982` was deployed. All native work used the verified run-owned disposable copy and preserved the baseline. Activity samples show lock occupancy, not exact CPU/query counts.
- Follow-up: Certify sustained headroom at the actual fastest arrival cadence; observed native days were about 25 seconds, which does not establish a few-second budget. Full identity synchronization, save/restart contention, derived-worker scheduling, ready-work metadata, active history lists and PostgreSQL outage isolation remain measured or proposed follow-ups. Separate relationship work, wider quiescence gates, autosaves and modal pauses. Keep query-count assertions after mutations as well as on quiet warmed days. Preserve probabilities, all eligible NPCs, historical knowledge, ordered effects and checkpoint gates. Historical and fresh samples are not identical-input A/B evidence.
