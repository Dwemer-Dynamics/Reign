# Reign Testing Tool Guide

War-economy/funding/replacement: [procedure](testing/WAR_ECONOMY.md).

Select a proof layer and read its procedure. `reign.testing.json` owns capability names, gates, evidence schemas and this document index. Query `reign_get_testing_catalog` with a relevant filter when the route is uncertain.

For current-turn source speaker normalization, use `dialogueMemoryAccuracy.sourceSpeakerAliases` and the [dialogue procedure](testing/DIALOGUE_AND_PROVIDERS.md). The `prompt_efficiency` suite checks event-scoped current aliases, canonical stored ownership and next-read continuity; passing citation checks does not establish semantic accuracy.

## Start with the affected behavior

| Requirement | Lowest useful proof |
|---|---|
| Deterministic logic | Focused unit or contract checks selected by the validation plan |
| Provider-free subsystem behavior | Verification Lab `quick` or `offline` from the exact successful Release artifact |
| Provider behavior | The applicable explicitly authorized `live-llm` suite and cost limits |
| Bannerlord integration, persistence, time or native UI | The guarded enrolled disposable-campaign harness |
| Subjective dialogue, visuals or play quality | Required human/native acceptance with retained evidence |

Offline results do not establish required native acceptance. A read-only explanation or audit does not require a product build. For source or policy changes, use [validation guidance](VALIDATION.md) and the current manifest-selected plan.

For pre-writer evidence accuracy, `memory_evidence_replay` can optionally capture the production writer envelope, publish a captured reply and its memory layers into its isolated fixture, and retain the next request's actual evidence. Follow [the writer/storage replay procedure](testing/DIALOGUE_AND_PROVIDERS.md#evidence-writer-and-next-read-replay-2026-09-29). Its optional finalCheck connects bounded local recovery and the durable checker/revision budget to publication and the next read, using captured responses only. Pending checker/rewrite stages cannot publish intermediate output. It does not invoke models or execute actions; supplied failures, pending stages and semantic review remain separate. Retain exact model-capacity metadata for large snapshots; normal source and size limits still apply, and a preparation failure is recorded rather than counted as a completed writer test.

## Coordinate shared work

At substantive task start, inspect active same-project task summaries, including worktrees. Read further only for likely overlap. Immediately before validation, call `reign_get_validation_status`; before deployment or campaign operations, recheck the relevant runtime/harness ownership. The status snapshot does not reserve resources.

Pass the current Codex task UUID as `requestingTaskId` to `reign_validate`. Busy status means continue independent work and wait for the owner or reuse a fingerprint-compatible successful report. Unknown ownership is not permission to remove the lock, interrupt another task or restart the runtime. [Coordination details](TASK_COORDINATION.md) define the scope of permitted messages.

## Procedure families

- [Proof selection and evidence](testing/PROOF_AND_EVIDENCE.md)
- [Campaign and runtime operations](testing/CAMPAIGN_AND_RUNTIME.md)
- [Court and diplomatic testing](testing/COURT_AND_DIPLOMACY.md)
- [Characters and portraits](testing/CHARACTERS_AND_PORTRAITS.md)
- [Dialogue and provider testing](testing/DIALOGUE_AND_PROVIDERS.md)
- [Interface proof and deployment](testing/UI_AND_DEPLOYMENT.md)
- [Feature harness procedures](testing/FEATURE_HARNESSES.md)
- [War economy, recruitment and recovery](testing/WAR_ECONOMY.md)
- [Personal gossip launch proof](testing/PERSONAL_GOSSIP_LAUNCH_PLAN_2026-10-04.md)
- [Recovery and continuity](testing/RECOVERY.md)
- [Launch packaging and clean-install proof](testing/RELEASE_PACKAGING.md)

The [interface design authority](REIGN_INTERFACE_DESIGN_RULES.md) retains the complete preview/fidelity matrix and required native acceptance. Scoped preview reports remain partial.

When a tool, command, scenario, gate or evidence schema changes, update the machine catalog, the owning procedure, applicable help/security documentation and catalog coverage together. Preserve dated requirements and failure evidence.

## Existing section links

These anchors preserve links from older task checkpoints. Each opens the relocated procedure.

<a id="court-audience-artwork-2026-09-09"></a>
- [Court audience artwork (2026-09-09)](testing/COURT_AND_DIPLOMACY.md#court-audience-artwork-2026-09-09)
<a id="control-center-navigation-2026-09-09"></a>
- [Control Center navigation (2026-09-09)](testing/UI_AND_DEPLOYMENT.md#control-center-navigation-2026-09-09)
<a id="international-settlement-acceptance-2026-09-09"></a>
- [International settlement acceptance (2026-09-09)](testing/COURT_AND_DIPLOMACY.md#international-settlement-acceptance-2026-09-09)
<a id="diplomatic-completion-evidence-2026-09-09"></a>
- [Diplomatic completion evidence (2026-09-09)](testing/COURT_AND_DIPLOMACY.md#diplomatic-completion-evidence-2026-09-09)
<a id="binding-an-existing-court-encounter-2026-09-09"></a>
- [Binding an existing court encounter (2026-09-09)](testing/COURT_AND_DIPLOMACY.md#binding-an-existing-court-encounter-2026-09-09)
<a id="testing-an-existing-patronage-encounter-2026-09-08"></a>
- [Testing an existing patronage encounter (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#testing-an-existing-patronage-encounter-2026-09-08)
<a id="prompt-composition-and-complete-evidence-2026-09-08"></a>
- [Prompt composition and complete evidence (2026-09-08)](testing/DIALOGUE_AND_PROVIDERS.md#prompt-composition-and-complete-evidence-2026-09-08)
<a id="patronage-eligibility-diagnostics-2026-09-08"></a>
- [Patronage eligibility diagnostics (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#patronage-eligibility-diagnostics-2026-09-08)
<a id="combined-alpha-deployment-packaging-2026-09-06"></a>
- [Combined alpha deployment packaging (2026-09-06)](testing/UI_AND_DEPLOYMENT.md#combined-alpha-deployment-packaging-2026-09-06)
<a id="dialogue-marriage-execution-2026-09-09"></a>
- [Dialogue marriage execution (2026-09-09)](testing/DIALOGUE_AND_PROVIDERS.md#dialogue-marriage-execution-2026-09-09)
<a id="conversation-history-and-earned-familiarity-2026-09-09"></a>
- [Conversation history and earned familiarity (2026-09-09)](testing/DIALOGUE_AND_PROVIDERS.md#conversation-history-and-earned-familiarity-2026-09-09)
<a id="political-pressure-observation-2026-09-08"></a>
- [Political pressure observation (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#political-pressure-observation-2026-09-08)
<a id="international-payment-identity-2026-09-08"></a>
- [International payment identity (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#international-payment-identity-2026-09-08)
<a id="exact-relationship-and-favor-observation-2026-09-08"></a>
- [Exact relationship and favor observation (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#exact-relationship-and-favor-observation-2026-09-08)
<a id="focused-reputation-reports-2026-09-08"></a>
- [Focused reputation reports (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#focused-reputation-reports-2026-09-08)
<a id="court-life-persistence-row-ordering-2026-09-08"></a>
- [Court Life persistence row ordering (2026-09-08)](testing/COURT_AND_DIPLOMACY.md#court-life-persistence-row-ordering-2026-09-08)
<a id="encountered-residents-and-homes-2026-09-06"></a>
- [Encountered residents and Homes (2026-09-06)](testing/CHARACTERS_AND_PORTRAITS.md#encountered-residents-and-homes-2026-09-06)
<a id="sovereign-recognition-and-castle-scene-prompting-2026-09-05"></a>
- [Sovereign recognition and castle scene prompting (2026-09-05)](testing/COURT_AND_DIPLOMACY.md#sovereign-recognition-and-castle-scene-prompting-2026-09-05)
<a id="starting-children-in-new-campaigns-2026-09-05"></a>
- [Starting children in new campaigns (2026-09-05)](testing/CHARACTERS_AND_PORTRAITS.md#starting-children-in-new-campaigns-2026-09-05)
<a id="court-life-acceptance-preparation-2026-09-05"></a>
- [Court-life acceptance preparation (2026-09-05)](testing/COURT_AND_DIPLOMACY.md#court-life-acceptance-preparation-2026-09-05)
<a id="background-character-portraits-2026-09-05"></a>
- [Background character portraits (2026-09-05)](testing/CHARACTERS_AND_PORTRAITS.md#background-character-portraits-2026-09-05)
<a id="campaign-storage-retirement"></a>
- [Campaign storage retirement](testing/CAMPAIGN_AND_RUNTIME.md#campaign-storage-retirement)
<a id="choose-the-lowest-proof-layer"></a>
- [Choose the lowest proof layer](testing/PROOF_AND_EVIDENCE.md#choose-the-lowest-proof-layer)
<a id="discover-before-operating"></a>
- [Discover before operating](testing/PROOF_AND_EVIDENCE.md#discover-before-operating)
<a id="repository-hygiene-evidence"></a>
- [Repository hygiene evidence](testing/PROOF_AND_EVIDENCE.md#repository-hygiene-evidence)
<a id="visible-lifetime-rules"></a>
- [Visible lifetime rules](testing/CAMPAIGN_AND_RUNTIME.md#visible-lifetime-rules)
<a id="autonomous-campaign-sequence"></a>
- [Autonomous campaign sequence](testing/CAMPAIGN_AND_RUNTIME.md#autonomous-campaign-sequence)
<a id="save-and-queue-safety"></a>
- [Save and queue safety](testing/CAMPAIGN_AND_RUNTIME.md#save-and-queue-safety)
<a id="feature-harnesses"></a>
- [Feature harnesses](testing/FEATURE_HARNESSES.md#feature-harnesses)
<a id="native-capture-presentation-contract"></a>
- [Native capture presentation contract](testing/UI_AND_DEPLOYMENT.md#native-capture-presentation-contract)
<a id="typography-parity-contract"></a>
- [Typography parity contract](testing/UI_AND_DEPLOYMENT.md#typography-parity-contract)
<a id="party-agency-autonomous-certification"></a>
- [Party Agency autonomous certification](testing/FEATURE_HARNESSES.md#party-agency-autonomous-certification)
<a id="recovery-and-overnight-work"></a>
- [Recovery and overnight work](testing/RECOVERY.md#recovery-and-overnight-work)
<a id="codex-restart-continuity"></a>
- [Codex restart continuity](testing/RECOVERY.md#codex-restart-continuity)
<a id="ui-open-readiness-deadlines"></a>
- [UI-open readiness deadlines](testing/RECOVERY.md#ui-open-readiness-deadlines)
<a id="preview-parity-visibility"></a>
- [Preview parity visibility](testing/UI_AND_DEPLOYMENT.md#preview-parity-visibility)
<a id="capability-maintenance"></a>
- [Capability maintenance](testing/PROOF_AND_EVIDENCE.md#capability-maintenance)
<a id="portrait-clothing-edit-profiles-2026-09-04"></a>
- [Portrait clothing edit profiles (2026-09-04)](testing/CHARACTERS_AND_PORTRAITS.md#portrait-clothing-edit-profiles-2026-09-04)
<a id="conversation-intoxication-and-italic-actions"></a>
- [Conversation intoxication, private drinking and italic actions](testing/DIALOGUE_AND_PROVIDERS.md#conversation-intoxication-and-italic-actions)
<a id="concurrent-scene-preparation-and-native-action-font-binding-2026-09-06"></a>
- [Concurrent scene preparation and native action font binding (2026-09-06)](testing/UI_AND_DEPLOYMENT.md#concurrent-scene-preparation-and-native-action-font-binding-2026-09-06)
<a id="immediate-noble-visitors-2026-09-07"></a>
- [Immediate noble visitors (2026-09-07)](testing/COURT_AND_DIPLOMACY.md#immediate-noble-visitors-2026-09-07)
<a id="wanderer-population-2026-09-08"></a>
- [Wanderer population (2026-09-08)](testing/CHARACTERS_AND_PORTRAITS.md#wanderer-population-2026-09-08)
<a id="clan-accords-2026-09-08"></a>
- [Clan Accords (2026-09-08)](testing/FEATURE_HARNESSES.md#clan-accords-2026-09-08)
<a id="scoped-provider-free-interface-previews-2026-09-08"></a>
- [Scoped provider-free interface previews (2026-09-08)](testing/UI_AND_DEPLOYMENT.md#scoped-provider-free-interface-previews-2026-09-08)
<a id="character-narratives-and-interests-2026-09-09"></a>
- [Character narratives and interests (2026-09-09)](testing/CHARACTERS_AND_PORTRAITS.md#character-narratives-and-interests-2026-09-09)
<a id="codex-sdk-image-provider-2026-09-09"></a>
- [Codex SDK image provider (2026-09-09)](testing/DIALOGUE_AND_PROVIDERS.md#codex-sdk-image-provider-2026-09-09)
<a id="openrouter-providers-2026-09-09"></a>
- [OpenRouter providers (2026-09-09)](testing/DIALOGUE_AND_PROVIDERS.md#openrouter-providers-2026-09-09)
<a id="codex-performance-controls-and-isolated-comparisons"></a>
- [Codex performance controls and isolated comparisons](testing/DIALOGUE_AND_PROVIDERS.md#codex-performance-controls-and-isolated-comparisons)


## Precision memory verification and maintenance (2026-09-23)

Run manifest-selected changed/Release validation, then `reign_run_offline_verification` with its exact `validationRunId`, `tier=offline`, `suite=memory_precision` and confirmation `run isolated offline verification`. Keep `prompt_efficiency`, `memory_relationships` and Save Sync coverage with the candidate. Fixtures are run-owned in ReignValidation, use no paid providers or listening runtime, and retain existing Lab cleanup. Inspect every `memory_precision_*` assertion and the packet's actual required content, not only source IDs.

The validated `ReignServer/scripts/Repair-ConversationContinuity.ps1` gains `-Mode Stage`: an exact campaign/timeline `reign-memory-stage-request-v1` produces a read-only serializable snapshot manifest with complete source records, source hashes, unresolved coverage and token estimates. The existing `Preview` rolls back; `Apply` requires the reviewed manifest/artifact hashes, unchanged sources and restore generation, stopped game/service and verified backup. New operations are `rebuild_scene_memory`, `set_memory_mode` (legacy/shadow/precision, projection-generation increment) and `quarantine_summary`; all are derived-state operations. Staging cannot run providers, replay actions, change scores or rewrite original transcripts. An uninitialized predeployment generation must be restaged after schema deployment. Private memory content stays in ignored evidence storage.

Shadow is the default and makes no additional helper/provider calls. A helper can choose only supplied eligible aliases, once, with one bounded source expansion. Cross-encoder ranking is optional and requires a local model plus a held-out accuracy/latency certificate; it stays disabled on failure. Sources and limits: `ReignServer/docs/server/MemoryPrecision.md`. Paid provider, native game and human roleplay acceptance remain separate gates; synthetic offline cases cannot prove their quality targets.

Memory replay transport evidence: [initial provider JSON capture](testing/DIALOGUE_AND_PROVIDERS.md#initial-provider-json-capture-2026-09-29) records shared-builder NanoGPT JSON/hash and bounded fixture generation settings. Live and replay generation share dialogue/event request defaults, including dialogue's 0.5 temperature cap and production output budget; party chat uses the event participation contract. Explicit quiet is distinguished from malformed empty speech, with quiet publication/next-read coverage excluded. Disabled checking sends explicit reasoning `none`. Reader packing protects current speech citations and retains independently valid findings after rejecting invented aliases. Use these contracts to distinguish production settings from private prompt/reasoning variants; capture does not dispatch HTTP, reproduce live queue/retry state or authorize provider spend.

`reign_test_structured_actions`: [procedure](testing/FEATURE_HARNESSES.md#guarded-structured-action-tests).