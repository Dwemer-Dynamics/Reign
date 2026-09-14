# Conversation prompt composition, layout 9

2026-09-08. Character accuracy and attribution take precedence over token savings. This change covers the production dialogue/event envelopes used by individual, Homes/castle, party/social and office conversations, plus correspondence and the hidden action planner. Independent court-specific generators keep their own contracts; no similarity-based deletion is applied to them.

## Standing rules and applicability

`PromptRuleComposition.cs` composes explicitly named requirements. A layer that needs a shared rule declares that dependency even when another layer also requires it. The rule renders once in a request. Missing rules, conflicting definitions, empty required templates and cycles fail before provider submission. Different observers, subjects, permissions, duties and exceptions retain separate content. This is not an automatic semantic deduplicator: overlapping prose in distinct character-type templates stays intact until an equivalence review and behavioral evidence justify extraction.

World tone, noble danger/formality, memory, identity, action receipt, scene-state and output contracts retain their wording. Noble rules are omitted only for explicitly false native `isLord`. A native encountered-resident commoner classification also proves non-noble applicability. Otherwise missing/invalid status retains conditional noble guidance with an explicit prohibition on inferring a title or office. Office rules come from the existing native posting/office resolver, and appear after the reusable policies. Room attire uses the existing native scene selector. The original commoner role and household rules remain in every applicable character foundation, including letters. No model-selected or player-keyword-only omission is introduced.

Correspondence and the planner compose their own contracts; they do not inherit a dialogue output schema. The standing rebellion decision contract in letters moves out of the live suffix into the system prefix, with unchanged wording. Live decision evidence stays in the suffix.

Every provider request remains self-contained. Standing rules cannot safely be sent once per conversation and then omitted from independent chat-completion requests. Stable prefixes permit the provider's existing implicit cache to reuse work. No new provider flags, model route, subscription change or unsupported claim of cached-token savings is introduced. Missing provider cache usage remains unreported.

## Retired templates and reviewed migration

The four inactive monolithic defaults, their dead standalone builders, whole-template compatibility extension and extension-compaction path are removed. `RetiredPromptMigration.cs` contains only reviewed hashes and an archive migration; none of the archived content is loaded as a prompt. Before activation, all old files are checked before any is moved. Unknown content stops activation with filename/hash and preserves the entire set. Known files move byte-for-byte to `prompts/retired-v9/`; rerunning migration is safe. Hash-equivalent files with different bytes receive distinct archives. The prompt editor cannot recreate retired filenames. No installed files are changed during source validation.

| Retired source | Active owner of its requirements/data |
| --- | --- |
| `dialogue_user_template.txt` | Dialogue engine, World Tone, Noble Prompt, authoritative fact boundary, visible reply/action receipt rules, identity contract, dynamic characteristics policy, live-turn template, character foundation, categorized knowledge packet and output schema |
| `event_user_template.txt` | Event engine and active phase, shared fact/action/memory requirements, event-specific speaker/witness/identity rules, live-turn template and output schema |
| `correspondence_user_template.txt` | Correspondence system and live-turn template, separate foundation, directional relationship and attributed memory |
| `action_planner_user_template.txt` | Planner system/rules/schema, allowed-action candidate message, resolver hints and live commitment template |

The reviewed installed dialogue hash is `B7B7CD540F3F74B0354988FC150197ACA1A5C03E1A13FA7ECB211CEAF28255CA`; event is `5E9664BED5655AD0A0850530F7F8E9F1CFDCAC18342DB22A6ED058ED73322CE5`. Their false-recall warning predates the current source's explicit distinction between attributed memories and newly disclosed low-impact soft canon. Existing current grounding/disclosure contracts remain authoritative; this cleanup does not revert that separate work. Their remaining restrictions map to the active owners above. Stock and previously migrated stock hashes are also recognized explicitly. Any subsequent custom edit requires a new review, never blanket acceptance.

The inspected Asta request had 83,232 message characters. Its compacted legacy extension accounted for 7,466 characters (about 9%, roughly 1,867 estimated tokens at four characters/token). This is a historical component measurement, not a promise that the next request has the same size or a provider-billed token count. The original audit truncated long strings, so a complete exact replay cannot be recovered from it.

## Wealth evidence

Native wealth v3 computes personal wealth from `Hero.Gold` alone. Party cargo has an explicit `observed`, `unknown` or `not_applicable` state and a nullable value. Clan funds remain distinct, including the existing warning when Bannerlord exposes the same wallet. `visibleWealthTier` is deprecated and emitted as unknown; visible presentation comes from the separate appearance and actual scene-attire evidence.

Prompt projection leaves historical storage untouched, discards the misleading cargo-derived visible label, recomputes historical personal tiers only from supplied known cash, and treats old zero cargo as ambiguous. Unknown economic capacity cannot become zero cash. Rich dress, low personal cash and strong clan backing can coexist. This does not infer public knowledge of a private treasury or convert equipment value into spendable gold.

## Evidence and verification

`reign_get_prompt_inventory` includes dependency/source metadata. Every composed envelope includes rule IDs, source, version hash, size, dependencies, selection and omission reasons. `reign_get_prompt_evidence` retrieves outbound messages captured immediately before a provider attempt through read-only `GET /audit/prompt`. Use exact campaign/correlation, then pin `evidenceId` across message/offset pages (maximum 12000 characters). Reassemble the redacted content and compare its exact UTF-8 SHA-256. Different retries/repairs have different evidence IDs. Only outbound role/content and sanitized diagnostics are recorded; no returned model reasoning, API headers or credentials. Strings are redacted before storage and before pagination. The bounded per-campaign ledger uses the existing audit policy of 8 MiB maximum/4 MiB retained. Predating, expired or failed captures are explicitly unavailable; the old truncated audit is never presented as complete.

`pipeline.prompt_caching` in the existing `prompt_efficiency` suite exercises dependency A-only/B-only/A+B, missing/conflicting/cyclic/empty rules, 24 production event/rank/room/office combinations, fresh and continued self-contained requests, unchanged source tone, historical wealth, private unknown wealth, archive preflight/idempotence and long-message redaction/pagination. Existing contracts, encountered-resident envelopes, sovereign identity/conduct and office suites provide adjacent coverage. MCP tool tests verify read-only routing, exact attempt selection, invalid bounds and catalog/guide agreement.

Build/contract success proves composition and data semantics. Provider response quality, stylistic equivalence and native installed behavior require the existing gated provider/disposable-campaign acceptance routes. Those are distinct from predeployment validation; no claim of guaranteed probabilistic response behavior follows from an offline matrix.

Production Test Lab `POST /tests/run` in forced canned mode uses `BuildTestMessages` and the same dialogue/event builders as game requests. It now also captures complete evidence labeled `captureStage=test_lab_assembly`; that label never claims a provider call. The existing MCP does not wrap this older Test Lab endpoint, so the before/after deployment comparison uses that documented API with fully supplied snapshots, a task-owned `verify_prompt_comparison_20260908` campaign namespace, no snapshot paths, `live=false` and `canned=true`. The comparison is restricted to the successful prompt phase, not the later action-normalization result of a deliberately action-free canned reply. Full baseline messages are available through this test route; the original in-game audit remains truncated. The installed server is verified after deployment through the same route and through isolated Verification Lab checks.

The social-signal instruction previously present only in the retired dialogue default is now an explicit active dialogue rule, with its original wording. Validation caught that ownership gap. Full production tests cover six dialogue/event/Home/party envelopes and two correspondence envelopes in addition to the 24 global-rule combinations. Pagination also checks Unicode character boundaries, and unknown/null historic cash never becomes a zero-cash classification.

The focused quick/offline prompt_efficiency suite explicitly executes contracts.dialogue_prompt_budget and pipeline.prompt_caching. It measures assembled commoner/noble system prefixes (45000-character bound), not a sum of selected template files. A catalog-only result is insufficient proof. The existing pipeline suite still runs the same caching contracts as part of its wider coverage.

