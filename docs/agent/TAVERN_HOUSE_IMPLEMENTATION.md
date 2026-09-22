# Visit the Madam implementation

Task: `01a0979f-f869-7b91-a739-eca98636a6eb`. Authorized 2026-09-12: build, validate and deploy the approved town tavern venue and Whoremonger plan. Status: system implemented, validated and deployed; native campaign acceptance remains pending.

## Accepted behavior

Town tavern submenu only. Separate madam portrait, complete horizontal worker cards, square scene left and transcript/composer right. Portrait click opens full body; eye requests missing art. Madam dialogue negotiates 1–4 participants (including herself at a higher character-decided valuation), followed by exact participant/payment confirmation. One confirmed agreement starts one visit, even at zero gold; only selected characters join private chat. Time stays paused until leaving. Scene toggle is independent of portraits. Arrival and Look Again depict non-explicit scenes with the player and complete chosen cast on a combined identity sheet. Arrival, full-conversation summary and Look Again image templates are editable in existing web prompts.

Ship distinct native initial casts and portraits for every supported town: 3–4 women including the madam and 1–2 men, aged 19–30. Worker Charm/Roguery 100–180, initial madam 200–250 and above each worker. Permanent succession ranks eligible women by Charm+Roguery, Charm, stable ID. Recruit voluntarily at exact agreed free/paid cost with native companion limits; replace the vacancy after 7 days. Active staff retire at 40 and are immediately replaced; recruited companions retain ordinary native lives. Initial and existing-save materialization is once-only; all identity, history, receipt and replacement state survives loading. Active staff are excluded from wanderer rotation/removal.

Whoremonger: confirmed player visits across all towns form one inactivity-reset streak. Visits 1–3 have no exposure roll; visit 4 is 10%, then +10 percentage points per visit up to 100%, including after tag acquisition. Only 30 consecutive days without a confirmed visit reset counters, promotion streak and active rumor; an established reputation remains. Existing active rumor deadline refreshes on every player visit. NPC actual town entries roll for free living adults with both Honor/Judgment<=40: percent=(50-H)*(50-J)/100; no player grace/ramp. NPC rumor/promotion resets 30 days after the last successful exposure, not failed entry rolls. Repeated exposures use existing promotion 0/30/60/90% and one active contribution. Base rumor -5, reputation -10; current spouse base -20 replaces either value. Normal current-Charm mitigation and rounding apply. No repeated subtraction from underlying relation values.

## Ownership and shared resources

- Root: interface/client integration, registration, testing catalog/docs, shared validation and deployment.
- `tavern_roster`: dedicated native roster/contracts/authored data and population guards.
- `tavern_reputation`: dedicated rumor adapters/rules and scoped social-standing integration.
- `tavern_server`: dedicated dialogue/quote/image server files and prompts; root merges Program.cs registrations.
- Preserve all preexisting staged and unstaged changes. Portrait rebuild parent `01a097f8-f66a-7a22-9a98-eee2bc3a2343` and shard `01a0981d-579d-70c3-8ad7-2cda25b52c88` confirmed staging-only work; their existing portrait inputs/candidates are outside this task. Use a distinct tavern character namespace.
- Coordinate again immediately before validation, installed module replacement, server-window closure/restart, portrait metadata changes, and native campaign operations. Do not resume idle/stopped Court or Homes tasks.

## Evidence and remaining work

Pre-edit MCP plan: Release / changed / Tier 3, complete coverage. Shared validation initially available; last observed report belonged to Court task `20260912-232328-21184ef3` and does not validate this feature. Visible unified installed server was healthy on 5101; no lifecycle mutation performed by this task.

Pending: guarded native campaign acceptance. The user deferred initial AI portrait preparation and will handle those images separately. Native advancement requires a user-named baseline and enrolled task-owned disposable copy. Record fingerprints, durations, reports, hygiene and material gaps here at each checkpoint.

### 2026-09-12 deployment checkpoint

Release `changed` Tier 3 validation `20260913-034614-387faad0` passed with complete 82-path coverage, source fingerprint `56578526f922273f074999b11988ecfd446367e5d611fb3c685b35c412578a98`, all ten project build/test operations and four persistence boundaries. Artifact-bound quick verification passed Tavern House 29/29 and Social Reputation 60/60. Deployment plan fingerprint `83ec3c6b4606903f84aac4cb95c89e617d231bccef162862e1e638fa6904834b` installed and verified 65 changed files while 1,075 already matched, covering 1,140 exact files total. The visible unified server restarted successfully on port 5101 and exposed all four editable Tavern House prompts.

The authored `reign_tavern_cast.json` still ships all 284 native characters across 57 towns; campaigns materialize them with native faces, bodies, clothing and roles. At the user's direction, the initial AI portrait/source pack was not rendered, imported or deployed. Missing AI portraits use the native fallback and eye-generation control. Native campaign acceptance still requires the user to name an immutable baseline for a guarded disposable copy; no campaign was loaded, advanced or saved by this task.

### 2026-09-12 implementation checkpoint

Native roster/lifecycle, native payment/recruitment, saved visit receipts, town-entry rumor adapters, server negotiation/scene/session endpoints, editable prompts, dedicated screen, integrated card artwork and preview fixtures are implemented and undergoing integration checks. The authored catalog contains 284 unique characters across 57 towns with explicit native face, body and outfit data. Native source snapshots are prepared under `D:\ReignTaskStaging\tavern-house-01a0979f\native-snapshots`; sources and AI portraits are not yet rendered. Missing initial portrait packs do not silently trigger paid bulk generation. Replacement generation uses the ordinary native portrait request path.

C: exhausted during concurrent portrait work. All agents paused writes; the other task moved only its own staging/generated images to D: behind verified junctions. A truncated `SocialReputation.cs` was reconstructed from its original Git contents plus this task's scoped edits and byte-verified against a D: recovery checkpoint (193,738 bytes, SHA-256 `1fd97c00d25a879493631877b20123cebb778737c74188e273187a0928db19aa`). The incomplete new client and cast files were rewritten and verified nonzero. No validation or deployment ran during disk exhaustion. Subsequent task evidence and native source staging use D: where the supported tools permit.

Provider-free verification route: Release `reign_validate` with exact changed paths; then artifact-bound `reign_run_offline_verification`, suite `tavern_house` or aggregate `contracts`, plus existing rumor tests. UI route: `test-tavern-house-contract.mjs`, full preview/alpha/reference/typography contracts, complete rendered matrix, and provider-free native `ui-open --target tavern-house`. The native calibration constructor cannot create staff, call providers, take payment, recruit, or write conversation state. Live native behavior and generated-image quality remain separate acceptance requirements.

### 2026-09-12 validation and recovery checkpoint

The first Release / changed / Tier 3 run, `20260913-021330-ff9e0a63`, passed enforce-mode repository hygiene with zero issues, then failed the client build on two native API references (`InputUsageMask` import and native skill enumeration). Both references are corrected. The failed run took 47.2 seconds and has source fingerprint `ef2e1bea352dd372bf3dad9b4a42e6e3bc4124d61f6d1b8690fd22badf4923bc`; it does not authorize deployment. Its validation and hygiene reports are beneath `.codex-build/reign-mcp/validation/20260913-021330-ff9e0a63`.

Final provider-free interface evidence passes 166/166 cases, combining 142 unchanged cases from the complete shared-renderer run with 24 final tavern cases at both viewports. Composition and screenshot hashes are explicit; original reports remain intact. All 22 approved references pass at both viewports, both new portrait apertures pass, and the mixed/plain multiline text regression passes 8/8. The evidence index is `D:\ReignTaskStaging\tavern-house-01a0979f\ui-evidence\handoff.json`. Seven preexisting PartyChat typography differences and one Training Yard alpha-corner failure remain recorded without changing their frozen authorities. These results do not establish native acceptance.

Recovery review corrected unpaid-receipt reopening, partial recruitment transfer/payment, initial madam greetings, first-confirmation transcript restoration, stale scene delivery and leaving during a slow reply. Durable dialogue markers preserve exact turn/speaker identity and recover completed exchanges; known no-reply failures remain retryable, while uncertain provider outcomes are not repeated. Reload metadata and delayed cross-town payment confirmation are included in the next coherent validation pass. Native source/AI portrait preparation remains pending; a bounded, provider-free prepared-source import is being added to avoid restarting the renderer for every cast member. No provider call, campaign advancement or deployment has run.

C: capacity was restored to over 55 GiB by the active portrait owner moving intact inactive Codex session history to D: behind a verified junction. Neither this task's build nor the concurrent portrait work is storage-blocked at this checkpoint. Recheck capacity and shared task/runtime ownership immediately before validation and installation.
