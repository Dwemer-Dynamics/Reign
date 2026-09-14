# Clan Accords implementation and acceptance

## Approved behavior

Clan Accords are a player-ruler-to-NPC-clan system. NPC clans never initiate autonomous agreements with one another and consume no capacity. The player has one slot per clan tier **for each accord type**, with at most one accord of each type with a given partner. Lesser adult nobles can arrange benefits for their clan; clan-leader approval is not required. Formation requires explicit present agreement from both actual speakers. A proposal, discussion, condition or refusal does not create an accord.

| Type | Benefit to each clan |
| --- | --- |
| Trade cooperation | 50 denars per day |
| Mutual Watch | 0.1 security per day in every owned town and castle |
| Agricultural exchange | 0.2 hearth growth per day in every owned village |
| Artisan exchange | 0.1 prosperity per day in every owned town |
| Garrison cooperation | 2% lower garrison wages |

Benefits stack. Mutual Watch covers eyes-and-ears and information-sharing offers without reports or spymaster recruitment. Agreements do not grant Government votes, treaty approval or another feature's authority. An agreement with no applicable holdings remains valid and begins benefiting newly acquired applicable holdings.

At the seasonal tick, each living adult partner member receives up to +5 goodwill in each direction with the player, once per partner/person/season across all accord types. The effective relationship ceiling is +20: +18 receives +2; +20 or higher receives zero without lowering an existing higher value. Cancellation costs -10 per adult partner member for each cancelled accord. Duplicate event delivery does not repeat consequences. Actual relationship values remain hidden from the interface; fixed rules may be displayed.

War with the partner's faction ends opposing accords without cancellation penalties. Clan elimination likewise ends them. Joining an already-hostile kingdom is reconciled immediately; peaceful changes in allegiance, ruler succession or arranger death preserve the accord. Peace does not recreate terminated accords.

Both clans retain formation and termination knowledge, including the original named arrangers and the actual cancelling player separately. This gives lesser nobles narrative credit without adding a promotion or rank system.

## Implementation boundaries

- `ReignModules/Reign.Core.Contracts/ClanAccords/ClanAccordLedger.cs` owns deterministic capacity, stacking, history and idempotency rules.
- `ReignBeta/src/Modules/Diplomacy/ClanAccords/` owns native saved state, lifecycle, exact model arithmetic and the nonmutating native contract harness. `_reign_clan_accords_v1` contains the ledger, pending events and seasonal cursor.
- The real-time main-thread pump drains bounded saved batches even while native time is paused. Acknowledgments are bound to the sent save-state instance, request, campaign, timeline and event IDs. Failures retain work for retry.
- `ReignBetaServer/src/Modules/Diplomacy/ClanAccords.cs` owns the campaign/timeline projection, private memory and authoritative directional relationship effects. Durable receipts prevent repeated effects; Save Sync includes the new tables.
- `ReignBeta/src/Modules/Diplomacy/UI/` and `GUI/Prefabs/ReignClanAccordsScreen.xml` own the screen. The Economy entry sits to the right of the date. The screen presents capacities, stacked totals, partner banner/name/current kingdom, benefits, arrangers, date and explicit cancellation confirmation.

Native model benefits are applied to the completed native calculation so preexisting percentage modifiers do not scale the fixed accord amount. Native limits remain authoritative. Garrison reductions apply only to garrison parties.

## Validation and deployment hold

The user explicitly requested a pause and alert before deployment. Do not replace installed artifacts or restart the runtime as part of predeployment validation.

Use manifest-selected `changed` validation with exact source paths. Core ledger, visible-consent and isolated database replay/memory tests are included in `world_diplomacy`. Interface checks include the provider-free contract, typography/alpha/layout contracts, rendered matrix and approved-reference fidelity. Report unrelated failures separately without waiving required gates.

The existing armed live-test scenario operation `clan_accords_test`, profile `contracts`, reports `reign-clan-accord-native-contract-v1`. Its 38 assertions exercise actual native `ExplainedNumber` arithmetic and an isolated nonempty JSON saved-state roundtrip. It does not alter a campaign, use a provider, advance time or write a save. Compilation is not execution of this native harness.

After authorized deployment, native acceptance must use an exact enrolled disposable Current save and prove:

1. Natural dialogue formation by a lesser clan member, plus refusal, tentative and conditional safety.
2. All five installed model benefits, including native percentage modifiers and both partner clans.
3. Seasonal ceiling, multiple-accord deduplication, cancellation penalties and attributed memories.
4. War/allegiance/elimination termination and preserved peaceful succession/arranger history.
5. Paused transport failure/recovery and a real fresh-process save/reload with nonempty state.
6. The native screen, Economy entry, banner apertures, totals, filters, scrolling, cancellation and return flow.

The guarded campaign-test quiescence gate requires `runtime.clanAccords` schema `reign-clan-accords-runtime-v1`, availability and no pending work before saving. Missing diagnostics fail closed. No exact disposable baseline has been provided to this task; offline evidence cannot substitute for this native acceptance.

Working evidence is under `.codex-build/clan-accords/` and `.codex-build/clan-accords-art/`. Authoritative build and hygiene reports come from `.codex-build/reign-mcp/validation/`; record the successful run, duration and source fingerprint at handoff. Implementation remains unaccepted until the required evidence is complete.

## Predeployment evidence checkpoint — 2026-09-08

- Focused `changed` Tier 2 server validation `20260909-005420-fc7a9951` passed in45.156 seconds, including129 diplomacy assertions/tests with51 Clan Accords checks. Source fingerprint: `ac4634d601a3aac1092b812c05eb7f0885f4712ea6c2f34fdb0f7a1538b0e2db`. Report: `.codex-build/reign-mcp/validation/20260909-005420-fc7a9951/validation-report.json`.
- Targeted rendered preview passed14/14 cases, covering all six Clan Accords states and Economy at1920×1080 and3440×1440: `.codex-build/clan-accords-art/targeted-verified/rendered-preview-audit.json`. This report explicitly declares partial scope and does not claim global UI readiness.
- Approved-reference fidelity passed2/2 at each resolution: `.codex-build/clan-accords-art/fidelity-verified-1920/approved-reference-fidelity-audit.json` and `.codex-build/clan-accords-art/fidelity-verified-3440/approved-reference-fidelity-audit.json`.
- Combined validation and native acceptance remain pending. These successful focused checks do not authorize deployment or substitute for final shared-source validation.
Final shared visual checkpoint: after the shared font correction and verified production portrait fixture selection, the complete142-case matrix passed with zero errors at `.codex-build/ui-preview/combined-final-20260909/rendered-preview-audit.json`. All20 approved-reference checks passed at each resolution in `.codex-build/ui-preview/combined-fidelity-1920x1080/approved-reference-fidelity-audit.json` and `.codex-build/ui-preview/combined-fidelity-3440x1440/approved-reference-fidelity-audit.json`. Government's six compound aperture assets passed `.codex-build/clan-accords/government-compound-v4-audit.json`. These provider-free results leave deployment and native acceptance pending.