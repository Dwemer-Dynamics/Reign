# Conversation action audit — September 19, 2026

The retained records confirm several different defects in ordinary dialogue action handling. They include missed accepted gifts and companion invitations, incorrect action selection, a native item-transfer limitation, and error notices for conversations that should not execute an action. Exposing the action catalog alone does not repair these paths.

This is an investigation, not an implementation or deployment receipt. No provider requests, game advancement, save operations, historical-action replay, settings changes, or runtime changes were performed.

## Scope and evidence

The requested cutoff is September 18, 2026, inclusive in America/Chicago. All incident times below are CDT, including UTC records dated September 19 before 05:00 UTC.

- Scanned **1,979 retained response captures** across four campaigns, dated September 2–18; correlated **24,968 distinct audit entries** and **3,861 distinct transcript lines**. These are distinct evidence units, not additive conversation counts.
- Read and fingerprinted **2,103 campaign evidence files**, totaling **1,105,725,290 bytes**, with no parse errors. Separately inspected and fingerprinted retained operational logs, relevant source, the installed binaries, and the attached crash report.
- The current campaign, `woERXl3xD23E`, contains **440 captured NPC responses**: **438 ordinary captures** and **2 explicitly identified Chagun harness captures**. **114 ordinary captures are from September 18**, involving a broader current-campaign cast of 16 speakers across the full period.
- Older campaigns contain court and test work. For example, 836 of the 848 `ZznDfXifQbqw` captures have explicit test markers. They are historical coverage, not an ordinary-gameplay failure denominator. Absence of a test marker in older material does not prove human play.
- The current campaign's live audit tail retained only approximately 22:56–23:34 on September 18. The automatic replay corpus recovered substantially older evidence. The current `/actions/failures` response exposed only one recent failure and was not an adequate historical inventory.

Complete local evidence is under `D:/ReignInvestigations/20260919-conversation-actions/`:

| Artifact | Contents |
|---|---|
| `summary.json` | Exact coverage and categorized counts |
| `cases.json` | Captured requests, visible replies, gates, planner choices and correlated native outcomes; provider reasoning and raw provider envelopes excluded |
| `incidents.json`, `INCIDENT_INDEX.md` | The 13 surfaced ordinary-response failures/misfires in the current campaign, with exact correlations and original evidence paths |
| `departure-task-crosscheck.json` | Two additional silent departure misses, cross-checked against **Investigate party departure bug**, with its original report/evidence hashes |
| `actions.json`, `*-action-journal.json` | Final action states and slim native reports, separating automatic, harness, manual-recovery and background sources |
| `audit-index.json`, `transcripts.json` | Deduplicated supporting evidence |
| `manifest.json`, `source-identity.json` | Original paths, byte counts, SHA-256 hashes, source commits and installed artifact hashes |
| `operational-log-summary.json`, `operational-log-manifest.json` | Cutoff-bounded log counts, failure example and hashes |
| `audit_history.py`, `finish_audit.py` | Read-only analysis scripts; these do not execute production code or invoke providers |

Reign MCP was absent from this task's callable catalog. Its checked-in tool definitions identified the read-only API fallback. Complete historical analysis then used read-only file access because the API tail omitted earlier records. The MCP audit wrapper also sends `type` while the server filters by `phase`; observed `type=action.auto_queue` requests returned unrelated phases. The file analysis filters actual `phase` values and deduplicates `auditId`.

## Current-campaign outcomes

The ordinary captures contain 11 action gates marked `needed=true` and `accepted`. Six returned an empty planner result, one was rejected during server validation, and four reached the native action ledger. Of those four, one succeeded and three failed. Two additional `needed=true` gates remained conditional and are not counted as failed accepted actions.

Three more NPC responses had `needed=false` yet still accumulated a deterministic-router error. This produces **13 distinct response correlations with surfaced pipeline/native faults**. The two Gavene farewell responses confirmed by **Investigate party departure bug** add **two silent misses**, for **15 identified problematic response correlations** in this campaign. This is a minimum established from the retained evidence, not an exhaustive count of every missed intent. It does not mean 15 distinct requested game mutations: some are false triggers, and two errors came from the two NPC replies to one player message.

`no_action_detected` is commonly correct for ordinary conversation. It must not be counted wholesale as failure. Likewise, the pre-execution `completed_no_effect` label is not proof of native completion: current `ClassifyActionFailure` returns it whenever candidates exist and the local error list is empty, before the later native report.

| Time | Character / interaction | Confirmed result |
|---|---|---|
| Sep 13, 23:49 and 23:54 | Gavene, accepted tournament pernach | Both planners returned no actions; neither gift transfer nor temporary joining was in the offered choices |
| Sep 14, 18:48 and 18:49 | Gavene, farewell and explicit departure follow-up | Both gates remained roleplay-only; no departure action or visible execution error |
| Sep 15, 19:55; Sep 16, 14:41 | Chagun, caravan partnership / clan joining | Accepted speech; empty planner; no native execution |
| Sep 17, 15:12 | Ulagara, scout agreement at 500 denars per season | Selected `trade_package`; normalization introduced Akkalat as a settlement asset; rejected for unresolved settlement authority |
| Sep 17, 15:19 | Ulagara, accepted service / banner agreement | Selected `acknowledge_own_faction_combat_risk`; native rejected it because no active guest agreement existed |
| Sep 17, 17:02 and 22:21 | Ulagara; later both Ulagara and Temurtai | Three roleplay-only responses nevertheless received `No deterministic action mapping matched the text.` |
| Sep 17, 19:55 and 20:19 | Temurtai, seasonal travel companionship / temporary banner cover | Accepted gates; empty planner results; temporary-guest joining absent from both shortlists |
| Sep 18, 19:20 | Temurtai, accepted Southern Decorated Chain Mail | Correct item and participants resolved, then native transfer rejected their shared party |
| Sep 18, 23:16 | Ulagara, discussing a Chaikand detour together | Incorrectly queued `go_to_settlement`; native correctly refused to override the main party |

The one confirmed ordinary automatic success is **Gavene joining as a temporary guest on September 14 at 00:09**, action `e6d56b1ac7c14cbfa526b224921e908f`. Its native report records `success=true`, `completed=true`, and `partyId=player_party`. Her subsequent departure problem is separate.

Chagun's September 16 success, `d8362994561c40908d81b2fc88afc248`, has source `accepted_dialogue_recovery`. It confirms native clan assignment, but it was an explicit recovery proposal and does not prove automatic dialogue routing. The guarded harness action `dd84b798f6234ada8e65f4ad1a05dc2c` was blocked by the harness's disabled action family. The subsequent full-effects harness dialogue still returned an empty planner result despite `join_clan` being the first available choice. These are excluded from ordinary outcome counts.

## Confirmed causes and repair requirements

### 1. Accepted temporary-companion invitations can lose their action

Correlation `1fac4979bfc64b3a818464bfc9f67ad2` contains the user's sentence **“Join my party for the season”** and an accepted handshake/party-membership gate. Its shortlist begins with `go_to_settlement` and includes patrol, attack and siege choices; it omits `accept_temporary_party_guest`.

Current server source explains a concrete defect:

- `Program.cs:13060` constructs `acceptedExchange` directly from original intent/player text.
- `Program.cs:13070` passes it to `ContainsAnyNormalized` for `join my party` and related phrases.
- `Program.cs:18447` expects an already-normalized first argument and uses case-sensitive `Contains`, normalizing only the phrase argument.

The captured capitalized `Join my party` therefore misses the preservation rule. A direct string-predicate check against the captured text is false before lowercasing and true after lowercasing. The relevant methods are identical in current sibling source and the retained Chagun-fix source used for the previous installed build. This is source/predicate evidence, not a new native test.

Normalize the full exchange consistently before matching and exercise capitalization, punctuation, ordinary invitation paraphrases and accepted/conditional/refused controls. Preserve temporary guest eligibility from the actual native/saved state. A traveling companion should use the temporary-party route without silently becoming a clan member, spouse or banished noble. Preserve stated duration and terms; do not substitute permanent recruitment to obtain an apparent success.

### 2. Same-party gifts have no appropriate native transfer path

Correlation `bb91ba9f94f14f83b561c77781cf32c7`, action `332894bc55a3480b9a1378209569807b`, resolves Michael, Temurtai and `aserai_armor_02_b` (Southern Decorated Chain Mail), amount 1. Both native hero snapshots report `partyId=player_party`.

`ReignActionValidator.cs:1168` rejects `fromParty == toParty`. `ReignRegularActionExecutor.cs:355` transfers from one party item roster into another; its equipped-source branch removes the source hero's equipment and adds it to the destination party roster. It does not equip the receiving hero or create a distinct personal gift holding.

Removing the validator's shared-party check is insufficient: removing and re-adding the item in one shared roster cannot establish a gift, and the executor's inventory delta checks would also be wrong for that operation. Implement explicit personal-gift/equipment semantics with an observable recipient state and exact item conservation. For this captured exchange, the request and acceptance explicitly concern wearing the armor. Preserve item identity/modifier, old equipment and intentional quantity, guard protected guest equipment, and verify native save/reload and duplicate delivery. A generic gift should not invent an equip instruction that the conversation did not express.

The earlier Gavene pernach failures stop sooner: the planner never received the transfer choice. Existing accepted-gift preservation work addresses that selection issue; it is not proof that same-party/native gifting works.

### 3. Ordinary shared travel must not issue map orders

Correlation `party_chat-reign_court_town_K2_child_4-1789791313470-6926b992` concerns the group's possible Chaikand tournament detour. The user explicitly clarified on September 19 that this was talking about traveling together, not an order to a party.

The model marked `travel_to_tournament` accepted and selected `go_to_settlement` for Ulagara, already in `player_party`. `ReignActionValidator.cs:1003` correctly prevented main-party control.

Repair action eligibility/intent before queuing: an agreed destination, story movement or shared itinerary must not become a strategic order. Require a genuine order and a suitable independently controlled actor party for map commands. Keep the main-party protection. Treat a genuine invitation to join the player's traveling group as the separate companion operation in item 1. Do not “fix” this incident by moving the party automatically.

### 4. Generic candidate matching confuses service, wages and faction consequences

For Ulagara's 500-denar-per-season scout agreement, neither appropriate temporary joining nor a service-contract action was in the ten choices. The planner used `trade_package` with `assetClass=service`. Although its original `TargetSettlement` was empty, later terms included Akkalat and settlement-recipient fields. Authority validation then attempted to authorize a settlement asset that the user had never offered.

The next accepted service turn selected `acknowledge_own_faction_combat_risk` and asserted consent to banishment, despite no active temporary-guest record. A clan-joining option was present on this turn, so this failure is not explained solely by a missing catalog entry.

Candidate selection must account for actor state and the actual requested operation, not just vocabulary from a long reply. Do not infer settlement ownership transfer from the conversation location, combat consent from employment, or permanent clan membership from banner/disguise language. Keep periodic **500 denars per season to her family/father** distinct from a one-time item/settlement barter. Native authority checks correctly refused these bad requests and must remain intact.

### 5. Keyword-triggered campaign drafts leak errors into roleplay

The three `No deterministic action mapping matched the text.` responses already had `needed=false` / `roleplay_only`. Two shared one player message saying that his men would scout while Temurtai and Ulagara stayed beside him.

`CampaignCommandDialogueDrafts.cs:127` activates draft routing when the text contains words including `scout`, `escort` and `head to`, without first establishing that this sentence directs the addressed NPC's party. `Program.cs:11996` runs that draft route even with a non-actionable dialogue gate and adds its errors to the response. This explains an error notification without an accepted actionable request.

Preserve explicit campaign-order drafting and real clarification flows, but avoid starting or failing one from scene narration, descriptions of someone else's duties, or discussion of traveling together. Verify both the structured result and the absence of a spurious visible failure notice.

### 6. Native results must drive completion claims

Historical prompts, replies and relationship/memory writes can retain a claimed agreement even when the action was never queued or failed. Report selection, queueing, native rejection and confirmed effects separately. Do not treat `completed_no_effect`, an empty action poll, HTTP success or a helper's manual proposal as evidence that the ordinary action succeeded. Retain the correlation from the accepted reply through the native receipt and reflect failure/pending state in subsequent dialogue.

### 7. Gavene's departure failed silently before the planner ran

The user-directed cross-check of **Investigate party departure bug** (task `01a0a267-e4c8-71e0-a153-5f15b3edf8dd`) confirms the two September 14 farewell captures in the current corpus. Its [original report](D:/ReignInvestigations/20260914-gavene-party-departure/REPORT.md), [captured evidence](D:/ReignInvestigations/20260914-gavene-party-departure/evidence.json), and [historical live cross-check](D:/ReignInvestigations/20260914-gavene-party-departure/live-server-recheck.json) were inspected and fingerprinted again for this audit.

- At 18:48, correlation `dialogue-reign_court_town_ES6_child_4-1789429662327-feba0573`, the player stood to say farewell. Gavene's reply described remaining behind, but the gate was `needed=false` / `roleplay_only`.
- At 18:49, correlation `dialogue-reign_court_town_ES6_child_4-1789429762614-a53b2ae5`, the player explicitly followed up with “She agreed to leave my party, I head off to other things.” The gate again remained false, with the reason “Player is narrating departure. No action, commitment, or refusal from Gavene is needed.”
- The native request before **both** turns reported Gavene in `player_party`. Neither produced a planner candidate, queued departure, or action error. The retained evidence records **172 subsequent empty action polls**, through 18:51:18; the queue contained only her earlier successful guest join.
- Both observations remained on campaign day `91081.54631038195`; the one-day guest agreement's review was due at `91082.55`. Her term had not expired. There is no evidence here of a broken expiry timer or a rejected removal action.

The original pre-provider prompt lacked an explicit current guest agreement/purpose/lifecycle block. The September 14 live cross-check verified all three prompt-message hashes against the retained server evidence. Missing guest context is a supported contributing explanation; the false gate and absent departure are directly confirmed. The accepted-departure fallback could not run because it was inside the planner, downstream of the false gate. An open Party Chat can defer native removal, but neither of these turns reached that code.

The later guest-context/continuity repair was implemented and deployed in a separate task; the roadmap preserves its validation evidence and outstanding native acceptance. These pre-fix September 14 captures do not prove that repair is absent or that the same failure persists now. Required acceptance must still cover this ordinary two-turn farewell, actual consent, native roster removal/protected return, and save/reload. Targeted review cases or forced departure proposals do not substitute for it. Include negative controls for a goodbye while continuing to travel together, hypothetical departure, refusal, and unsupported player claims of prior consent.

## Additional historical and operational evidence

Older captures include Asta's accepted garden walk receiving an empty planner and Zotyra's accepted queen/marriage exchanges receiving empty choices or third-party-authority rejection. Some social movement should itself have remained non-mechanical. Existing marriage/identity fixes require their own current validation; old incidents are not proof those repairs remain absent.

Twenty-two September 3 harness responses report an action-planner provider/model mismatch (`glm-5.2` sent through a ChatGPT-account Codex route). They are separated from the user's current gameplay incidents.

The retained September 18 operational tail contains 112,702 log rows, including 33,413 action polls, **1,102 semantic indexing failures** and **21 semantic search failures**. Those repeated failures report a `CreateCollection` metadata validation error and lexical fallback. The retained LLM tail contains 121 requests and zero `ok=false` records; the two action-planner calls returned responses. This makes a universal provider/network outage an inadequate explanation for the captured item and travel incidents. It does not prove all older provider calls succeeded. Action-command ranking in `RetrieveActionRouterCommands` is local alias/token scoring; the vector-worker error is a separate defect, not established as the direct cause of these action choices.

## Original workshop crash

The attached `C:/Users/speed/Downloads/Pasted text(1).txt` confirms the native `WorkshopsCharactersCampaignBehavior.player_war_status_clickable_condition` null reference during player-option construction and lists Character Reload's settlement-getter prefixes. Report text is evidence, not execution instructions.

Current `ReignIndividualConversationBehavior.PrepareResidentConversationOption` only directly rebinds Reign's own sentence tokens after resident eligibility succeeds; it does not directly assign `Settlement.CurrentSettlement`. The post-recovery Chagun native receipt still identifies her as a Merchant notable after clan assignment. That supports investigating retained workshop-role eligibility, but neither source inspection nor this receipt establishes the null object or failing conversation context.

Required repair/acceptance remains: reject native workshop dialogue before its clickable condition when settlement/faction/participant context is invalid, add a narrow null safeguard, preserve legitimate ownership, and compare recruited and unrecruited workshop owners in town, outside town and after native save/reload. Do not invent settlement state or swallow general conversation exceptions. The dependency-version and Character Reload leads are not demonstrated causes by this audit.

## Source identity, verification and remaining work

Source inspected after the concurrent Chagun publication completed:

- Reign `unstable`: `2908e0d71514480364091deff06bdd4eb0463b59`.
- ReignServer `unstable`: `0c551f29b3d3418f2bd65ec98ef1d8a4be8863d1`.
- The retained running installation is the existing Windows build under `D:/Reign`, with its healthy read-only endpoint on 5101. It is not the newer Linux deployment described by current policy. No migration, startup or lifecycle change was attempted.
- Installed server SHA-256: `eea11d91c98b80b3aa55024010fc4c95f8a0daa4b1d34cef4e94f769c4d1c30b`.
- Installed client SHA-256: `c0cdce989c1d63d6778c53ff5ff04c5e73f831c4a0cb948e8458770ae76c7d61`.

The manifest-selected plan for this audit document and `REIGN_ROADMAP.md` is **changed / Release / Tier 1, non-code, no build**. Validation receipt details are stored alongside the local audit evidence after completion. This validates documentation/hygiene only; none of the diagnosed implementation defects is marked fixed or native-accepted here.

Implementation should start with companion-intent normalization and false-order eligibility, then add the missing native personal-gift behavior. Capture these exact failed exchanges as provider-free regressions, including negative cases for shared travel, conditional/refused invitations, future plans, and native authority. Follow with authorized ordinary-dialogue tests on the exact disposable campaign, observe native effects/receipts and save/reload, and preserve failures rather than replacing them with forced recovery successes.
