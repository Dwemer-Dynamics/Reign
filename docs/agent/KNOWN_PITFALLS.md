# Reign Known Pitfalls

Record compact, recurring hazards that can cause data loss, invalid evidence, unsafe runtime behavior, or repeated engineering waste. Prefer a link to deeper evidence over duplicating a long investigation.

## Entry format

### YYYY-MM-DD — Area — Pitfall

- Context or symptoms:
- Decision or root cause:
- Alternatives/failed approaches:
- Consequences or successful fix:
- Verification/evidence:
- Follow-up:

### 2026-09-05 — Starting children — Native age registration and child state

- Hazard: creating a newborn and backdating it leaves `HeroCreated` observers initially seeing age zero; activating a minor can bypass the native coming-of-age skill/equipment initialization. Native parent setters also append to each parent's child list on every assignment.
- Implementation: use `HeroCreator.CreateChild` with the final integer age; normalize its randomized birthday only within that same age bucket; retain `NotSpawned` until normal adulthood; assign each parent only once. A native allocation exception with no returned identity fails closed rather than allocating another child on retry. Existing saves never seed or resume partial seeding.
- Evidence: installed-API client compilation and 11 provider-free planner/receipt checks passed in `.codex-build/reign-mcp/validation/20260905-221832-01bf00e5/validation-report.json`. Adapter: `ReignBeta/src/Modules/Characters/Campaign/ReignStartingChildrenCampaignBehavior.cs`.
- Follow-up: native family-tree, 17-to-adult transition and save/reload acceptance require a disposable new campaign; offline checks do not prove native runtime behavior.

### 2026-09-05 — Portraits — Native physique units and source provenance

- Context: portrait snapshots/rosters use native 0–1 weight and build, while legacy general character profiles express those fields as 0–100 percentages.
- Root cause to avoid: guessing units from whether a value exceeds one turns a legacy 1% value into native maximum weight. Convert only at the explicitly identified legacy-profile boundary; reject out-of-range native snapshot/roster values.
- Implementation: the native worker reports the actual resolved body properties; the generator returns source-hash-bound `reign-native-physique-v1` metadata. The server appends the editable body layer after source resolution to both initial portraits and clothing edits. Physique defaults retain explicit provenance. Shared source reuse requires matching physique metadata.
- Evidence: `.codex-build/reign-mcp/validation/20260905-191549-df3c47fb/validation-report.json` (Tier 3 Release, 95/95 quick checks); `.codex-build/portrait-physique-20260905/result.json` (installed native render and actual weight/build/hash verification).
- Follow-up: paid-provider and human body-shape acceptance remain separate from deterministic prompt/transport validation; this layer is not an automatic body-measurement gate.
