# Beta fix batch — 2026-11-14

Implementation is local in paired managed worktrees based on client `60e2915a` and server `117d817`. Deployment is held. No live campaign, save, native clock, provider setting or game installation is changed by this batch.

## Ruler captivity

Wait seven full fractional native campaign days from original capture before any verdict. Handover preserves the anchor. Execution then waits eight additional days from sentencing. Every entry point and asynchronous callback rechecks the clock and custody. A missing legacy capture timestamp begins a full waiting window; existing absolute execution deadlines are preserved. Defeated-lord judgments retain their queued intended verdict rather than rerolling it after the wait. Escape, rescue and loss of custody cancel normally during the waiting period. New history records include capture and sentencing anchors. Read-only runtime status exposes up to twenty active judgment clocks, with total/limit coverage, so future testing can match the exact capture, decision and execution anchors without a fixture. Queued orders remain tied to their issuing sovereign; a successor does not inherit them. An open or failing decision does not starve later judgments.

## Tournament prizes

This repairs Arena Overhaul's existing expanded item-pool feature; no items or category-weighted selection system are added. The expanded elite catalog remains available regardless of the prize-scaling toggle or clan tier. It includes the intended ordinary Tier5/Tier6 armor, weapons and mounts alongside the existing eligible unique rewards, with the original culture modes and excluded practice/magic items. The duplicate tier filter is corrected; original standard-item price caps and per-type/per-culture pool limits remain. The defect was the expanded pool being bypassed, not a new item-budget policy. Gold and renown scaling remain separate.

Native regular rewards, banner chance, hero-count value quarters/elite halves and configured reroll rules remain in charge of selection. Existing announced prizes are retained when no reroll is due. Both installed-compatible elite call sites reach the same catalog and recent-ID filter; regular call sites also apply history filtering. Candidate caches are invalidated without mutating model-owned lists or changing the retained prize; native rebuilds them only when a permitted roll needs them, so they cannot bypass current history. The existing minimum-eight-candidate anti-repeat safeguard is retained. Ten distinct recent IDs persist in native save data, and a new campaign starts empty.

## Diagnostics

- Relationship GET status uses campaign/timeline query values; POST keeps body scoping.
- Wedding receipts report scheduledStartDay/scheduledEndDay, including updated bookings. A pending receipt cannot acknowledge a terminal transition that occurred while its request was in flight. Pending weddings become overdue only after the booked end plus one day for a terminal receipt. Old receipts without dates are explicitly unknown and remain visible as partial timing coverage, rather than being labeled overdue after one day. Ordinary pending actions keep their one-day rule.
- Save Sync resolves lastLoadedSavePoint through the current ledger ID before checking storage availability. Removed registrations are reported distinctly from legacy snapshot loss.
- Memory status adds attentionRequired (failed plus incomplete) and a source-free reason breakdown from the latest 100 jobs, with explicit inspected/total coverage and nonretryable counts. Historical job state is preserved; no paid reprocessing is performed.

## Findings with no behavior change

The repeated wilderness event is consistent with the actual Alpha3 checkpoint predating its prior resolution. Zero gossip deliveries have not yet been tied to an eligible audience/source opportunity. Neither observation justifies an invented behavior fix. The fourteen existing incomplete memory jobs retain source fallbacks: twelve invalid selections and two whole-record budget mismatches; no fresh-session recurrence has been established.

## Verification and acceptance

Deterministic policy tests cover exact fractional ruler boundaries, save/load anchors and legacy deadlines, original reroll rules, recent-history exhaustion and normalization. Server self-tests cover scheduled versus ordinary overdue classification, read-only memory reason projection, GET/POST scope selection and current-versus-cached Save Sync metadata.

Changed/Release Tier3 `20261114-211632-eb2366c7` passed all twelve operations in 700.831 seconds with source fingerprint `954e315d49e8740150f838b29bcb12171f88233fcb654ae58b9cd376b4774900` and clean enforced hygiene. MCP tests passed 3,163/3,163 (including arena 16/16 and ruler policy 71/71), relationship unit tests 34/34, Save Sync boundary 66/66 and relationship boundary 442/442. Exact-artifact memory precision verification passed 834/834; baseline verification passed five groups, including World Test 173/173. Claude reviewed the applied changes; remaining review corrections were checked against surrounding source and tests. Durable reports, review results and artifact identity are indexed in `E:/CodexStorage/Projects/reign/beta-monitoring/2026-11-14-01a1d729/VALIDATION_SUMMARY.json`. Native RUL-TIME-001 through003 and in-game prize variety/save-load acceptance remain pending and cannot be inferred from offline tests. Use the existing guarded disposable campaign procedures only after separately authorized native testing; do not modify the ongoing player test campaign.
