# Court Life release acceptance audit

Audit date: 2026-10-25 (America/Chicago). Preparation and historical review are complete. Current native readiness passed; feature acceptance is in progress.

The expanded goal is section 14 of [the autonomous plan](../COURT_LIFE_AUTONOMOUS_TEST_PLAN.md). [Scenario recipes](court-life-release-scenarios.json) cover the 84 named requirements, all 75 current international templates and all 40 patronage templates in 15 grouped execution packs. Recipes explicitly identify preparation controls that do not yet exist; they are not an executable runner or a claim of in-game passage.

## Evidence recovered

The previous task checkpoint reports 137 positive, 59 partial and four blocked rows out of 200, and a completed 180-day native soak with an exact reload in a new process. Those historical results remain recorded; they are not certified for the October release candidate.

The retirement archive `D:/ReignMigration/retired-bannerlord-events-20260930/historical-reports.zip` contains 1,949 Court Life files. All recovered bytes match the original `report-manifest.json` size/SHA256 entries. The generated archive inventory lists every retained path/hash. Seventy case summaries and embedded native phase reports are indexed separately, including original failures and later repairs. Phase-success counts are not scenario-pass totals and may contain wrapper copies of the same receipt.

The original `20260907/scenario-ledger.json` and `20260913-soak/progress.json` are absent from this report archive and the retired C: checkout. The archived soak checkpoint retains the final state, counts, save name, world day and reload claim. Referenced reports/receipts still require recovery or fresh bounded proof where their completeness matters. Absence from this archive is not a claim that no other backup exists. Preserve this limitation instead of recreating a pass ledger from memory.

## Which historical behavior can be reused

- Family: age-four/teen/spouse roleplay, two-turn attendance, unopened/zero/one-turn dismissals, threshold neglect, no future visits, decay to zero, and accepted reconciliation have historical evidence. Finish missing boundaries and recheck the current dialogue/memory and adulthood paths.
- International: most complaint templates, exact payments, refused extortion, real trade/ransom/gift/release paths and completed-action continuity have historical reports. Recheck changed dialogue, Government/native diplomacy, charter/counteroffer and pressure branches. Do not mistake an old `Success=true, Completed=false` receipt for treaty completion.
- Patronage: commissioning, several funding/reach tiers, praise, loyalty, durable work and delivery/reload have historical evidence. Three-destination regional reach and fact-dependent work still need valid prerequisites, and current dialogue must preserve the exact commission.
- Visitors: foreign solo courtesy, a parent/adult-child household, refusal to pursue a married ruler, immediate placement, resident availability, ordinary return and reload have prior evidence. Positive personality initiative, more household sizes and assignment/return failures need native coverage under current movement rules.
- Favor/jealousy: contract/boundary and partial native contact/expiry evidence exist. The eligible domestic jealousy complaint and response branches were blocked; prepare a legitimate domestic setup rather than repeat random favor rolls.
- Shared docket/UI: reopening, several retry/effect boundaries, scroll overflow and source screens have prior evidence. Finish exact daily scheduling/foreign-reply reservation, multi-speaker failure, authority loss and current rendered/native acceptance.

These are evidence-reuse candidates. Each final reused result must cite the actual sufficient transcript, effects and persistence report, not this prose summary.

## Changes requiring a fresh pass

Comparison is against the 2026-09-14 source recovery snapshot, with line-ending normalization and exact path relocation for server/contracts:

| Scope | Current files | Same text | Changed text | No old path match |
| --- | ---: | ---: | ---: | ---: |
| Client Court | 83 | 63 | 18 | 2 |
| Server Court | 22 | 18 | 4 | 0 |
| Court contracts | 11 | 11 | 0 | 0 |
| Client/server dialogue | 220 | 58 | 33 | 129 |
| Diplomacy | 26 | 15 | 11 | 0 |
| Relationships/reputation | 59 | 28 | 13 | 18 |

The larger scopes overlap multiple features; changed file counts do not prove defects and path misses may be additions/relocations. The full generated audit pins all current files.

Concrete impact: CourtLifeDialogue now uses original-input/route-memory preparation, delivery replay and publication instead of the former isolated reply write. Player input is no longer truncated at the old 2,000-character admission. The Court Life ViewModel, family screen routing, docket behavior and native agreement infrastructure also changed. Therefore fresh original-input retention, exact turn retries, adult/thin speaker routes, cross-mode recall, pending Government and settlement acknowledgement are required. Current international refresh now starts a 60-second cooldown after completion to leave a real idle interval; test queue quiescence and explicit invalidation rather than carrying forward the former slow-drain diagnosis as current fact.

The runtime health probe now reports managed `ReignServer`, database `reign`, current schema and healthy background/provider queues. The old shared-Skyrim-WSL failure is historical. This probe establishes neither the exact installed release-candidate hashes nor current game/save readiness.

## Efficient execution packs

| Pack | Purpose |
| --- | --- |
| R00 | Evidence, candidate identity, current contracts and baseline readiness |
| R01 | Clock boundary, foreign-reply priority, suppression and docket overflow |
| R02 | Catalog audiences, role/knowledge isolation, reopen/refuse/apologize |
| R03 | Judgments, real money, acceptance mitigation and insufficient funds |
| R04 | Sovereign referrals, real diplomatic adapters and Government completion |
| R05 | NPC pressure, extortion/personality/power and ambassador selection |
| R06 | Family age-appropriate roleplay, attendance and reconciliation |
| R07 | Family relation offsets, patience rounding and identity lifecycle |
| R08 | Favor contact, exact expiry, grace and reacquisition |
| R09 | Known/unknown jealousy, two favorites and eligible court complaint |
| R10 | Actual public-history prerequisites for patronage |
| R11 | Funding, recipients, loyalty/praise, failure and delivery |
| R12 | Visitor groups, consent, instant movement, leases and safe return |
| R13 | Unique failure seams and bundled independent-process persistence |
| R14 | UI, existing petition integration, collisions and applicable longevity proof |

Use deliberately selected eligible actors and starting states. Group independent deliveries into one clock interval. Reuse proof for unchanged branches and choose incomplete cases to cover fresh integration regressions. No reroll-until-lucky loops, no repeated successful near-equivalent conversation, and no automatic second 180-day soak. If changed daily/persistence behavior or unavailable evidence requires another long run, record why and its exact unanswered stability assertion first.

## Current execution prerequisites

The user selected `test` as the protected baseline and authorized loading it and creating a separate disposable copy. Run `court-life-release-01a06f8a-20261025` owns only `ReignTest_CourtLifeRelease01a06f8a_ffb73fa3_Current` in campaign `BYcEHbnIrLdl`. The baseline SHA256 is `CDF09B7AF5E4314A032B3F0AAE1536B45BEB6C0CC7CA516CF6FAC0A2F814FFDF`. The NPC Lives task released the deployment window; its separate Current remains protected. Never restart Codex during these tests: if that becomes unavoidable, checkpoint and pause as the user instructed.

Native preflight `live-1792989708564-c69a1db2` passed at paused day `91077.37496207407`. Save Sync retained eight of fifteen unique states. All three image-generation settings were disabled and reread after a game-only reload. No generated-image acceptance is claimed.

The first household fixture, `live-1792989858232-6d402942`, failed before spouse/child creation: native `SetBirthDay` changed the birthday while the Reign calendar retained the ruler's previous age anchor. This also affects explicit age edits outside the fixture. The repair synchronizes that anchor after the native birthday setter while retaining ordinary 126-day aging. Changed Release Tier 2 validation `20261026-044913-a9a3caae` passed with green hygiene; native age/family and reload proof remain required. The failed partial fixture was discarded without saving, preserving the clean Current for retry.

2026-10-26 progress: product validation `20261026-045023-566b3d55` passed (2,449 MCP, 11 relationship tests and 116 quick checks), and its client was deployed with matching DLL SHA256 `5E8D45E45C984D69F0A9E20289FC84D43879728387F94AFE07FCF3834D72048F`. Household setup then passed for ruler34, wife34 and children4/14. The young child's native fractional age initially fell just below four; the production eligibility rejection was correct, and a guarded short native clock interval carried her past the birthday. The short probe stopped at day `91077.39650567014` against target `91077.39496207408`; retain that measured overshoot when choosing later clock-boundary margins.

The drawing visit `family_visit_90bb3514` passed its scoped current dialogue/attendance/conclusion checks: exact player wording, age-appropriate replies, one then two unique player turns, server status attended, zero dismissals and one conclusion receipt. This does not close the broader family matrix or persistence requirement. The teen arena visit completed one player turn and concluded; its later dismissal remains to be observed. A separate naturally rolled spouse visit remains unopened for the zero-turn branch.

Native inspection also confirmed that the calendar file retained ruler22 after the in-memory birthday edit to34. Immediate birthday-anchor persistence is implemented and product validation `20261026-052025-14b4f2ae` passed; deployment/reload proof is pending. The first patronage opening failed before its provider call because shared political context incorrectly required a native hero profile for a virtual creator. Gold stayed `5003434`, no commission existed and no player turn was submitted. The server repair derives a bounded creator profile while preserving native-speaker gates; targeted validation and native retry remain pending.

Scenario preparation gaps include bounded clock/trait/power/adulthood/public-history/favor-knowledge and fault-injection seams, plus Visitor/Family exact existing-matter binding if needed. Implement only necessary controls, behind current gates, with catalog/guide/security/contracts in the same change. Do not claim a declarative recipe has already created those native scenarios.

Image generation remains disabled. Existing portraits, scene routing, cast bounds, scroll and disabled/error/loading UI can be checked. Corrected newly generated full-body artwork remains a separate visual coverage gap if no retained image proves it.

## Durable artifacts

Generated recovery/audit data lives under `D:/Projects/Reign/.codex-build/court-life-release-20261025/`: `archive-inventory.json`, `historical-phase-index.json`, `case-manifests.json`, `audit.json`, `recipe-integrity.json`, and the local checkpoint. The JSON recipe is validated for unique IDs, complete named-requirement assignment and current catalog enumeration. These checks prove plan integrity only.

Readiness proof does not pass the feature matrix. No release-ready claim is made while mandatory acceptance remains unresolved.

2026-10-26 continuation: combined deployment retained client SHA256 `D37033A8EDFE93E6106698D849003D2DA312A4674A3F7F8DAD549BD4A699F97F` and installed server SHA256 `9F41DAA68BE152408A3D2ED1C8E994DE00595EADCB1256A33D86B8BDBD06D496`. Household ages and three exact matter fingerprints passed an independent game-process reload. The teen one-turn and naturally unopened spouse zero-turn visits each added exactly one dismissal after their deadlines; the attended child stayed at zero. These scoped results do not close the family matrix.

The same patronage encounter now creates its valid creator political profile, but still failed before a provider call because raw mixed-line-ending evidence differed from the normalized protected continuity text. The thin-route repair normalizes the rendered evidence with the existing normalizer while retaining original player input and content-tampering rejection. A distinct spouse walk conversation exposed a false role-integrity rejection of “an hour of my husband … a petition from my king.” The detector now distinguishes ordinary dual-role language from explicit different-person claims and contradictory assent/refusal. Regression cases retain rejection of the latter. Changed Release Tier 3 validation `20261026-064605-dba7d34d` passed all eight operations, including persistence boundaries and 362 Court assertions; native retries and deployment of these two follow-up fixes remain pending.

The border-ultimatum fixture `live-1792997333168-87373fde` correctly refused setup without actual eligible strategic/personality prerequisites. No international matter or diplomatic effects were injected. Inspect eligibility before choosing a legitimate setup; do not reroll it until lucky. Current save day remains `91078.39727004166`, paused; spouse-walk progress is not yet in the rolling disk checkpoint.
