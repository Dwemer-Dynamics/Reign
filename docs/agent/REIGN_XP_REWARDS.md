# Reign participation XP

## Requirements and implementation — 2026-09-15

The user requested restoration of participant-based social-event skill XP, additional Charm for spoken conversations, Leadership for decided petitions, and server Options controls. Build, validation, deployment, a deferred manual task in Testing, and pushes to both canonical remotes are authorized. No native campaign baseline was supplied; in-game acceptance is deferred to the user.

| Trigger | Base XP before native learning modifiers |
|---|---:|
| Completed player exchange in individual, party/group or petition conversation | 5 Charm |
| First completed spoken exchange in a social-event phase | 5 Charm |
| End of a social-event phase with actual conversation | 2 in one selected skill |
| Successful explicit petition grant or refusal | 10 Leadership |

The 2 XP phase bonus is an implementation default for the user's “very small” amount. The other values, enabled default and multiplier bounds were selected by the user. Social events use their phase Charm award instead of also receiving per-turn Charm. The additional random bonus is drawn once at phase end so every distinct participant spoken with during that phase can contribute. A successful nonempty nonsilent reply is participation; automatic approach openings are excluded. Repeatedly talking to one person gives no extra draw weight. Participants with all four relevant skills at zero cannot supply a skill bonus.

Select a participant uniformly, then their highest **Steward, Trade, Leadership or Tactics** skill. Steward is Bannerlord's quartermaster skill. Highest-skill ties are resolved by the same stable random policy. SHA-256-derived selection tied to the event/phase identity prevents rerolling on request replay. It does not consult the model or generate provider work.

## Settings and boundaries

The Control Center Options tab's Save XP options button saves only the XP toggle and five multiplier values through the existing settings API. The global Save Settings button also includes correctly typed XP values. Defaults are On and 1x for every multiplier. The universal `reignXpMultiplier` accepts 0.25x through 5x in 0.25x steps. Each category accepts 0x through 5x in 0.25x steps; 0x disables that category. Disabled controls retain their values and the page displays combined base amounts. Invalid incoming values are rejected before settings writes. Existing server settings and secrets are preserved by the existing partial merge.

| XP reason | Additional setting | Default |
|---|---|---:|
| Individual, party/group and petition conversation Charm | `reignXpConversationMultiplier` | 1x |
| Once-per-phase social-event Charm | `reignXpSocialCharmMultiplier` | 1x |
| Social phase-end skill bonus | `reignXpSocialBonusMultiplier` | 1x |
| Explicit petition grant/refusal Leadership | `reignXpPetitionMultiplier` | 1x |

Awarded XP before Bannerlord's learning rate is **base XP × universal multiplier × category multiplier**. Missing category fields in old server settings or an older gameplay-options response default to 1x. Award sites pass an explicit reason so conversation Charm and social-phase Charm can be tuned independently. A zero-valued category still consumes its receipt; turning it back on cannot replay earlier rewards. Eligibility, native learning modifiers, skill selection and duplicate protection retain their existing behavior.

`GET /api/gameplay-options` exposes success, these XP gameplay values and the existing player-approach options. The client refreshes before relevant interactions and phase completion with a five-second transport timeout per candidate endpoint. It retains the last confirmed configuration on failure; before any confirmed configuration it grants nothing. Settings are global to the selected server and require no restart. An in-flight turn uses its captured settings. No missed rewards are queued for later when options are disabled or unavailable.

The native campaign behavior awards only to the player through `Hero.AddSkillXp`, on the native main thread. It serializes its claim ledger and unfinished phase participants in `_reign_participation_xp_v1` within the Bannerlord save. Loading an earlier save restores corresponding earlier XP and eligibility. Exact conversation receipts, social turn identities, phase reward identities and petition IDs cannot pay twice. A context captured for another campaign cannot apply rewards to the new campaign.

Letters, idle attendance, automatic greetings, technical failures, postponed/automatic/defaulted petitions, UI calibration and dialogue audit fixtures do not earn these participation rewards. Ordinary native skill gains and the existing suppression of XP from passive relationship corrections retain their established behavior. NPC skill values are read solely to select the social bonus and are not modified.

## Verification and release

Use the current exact changed-path `changed / Release` plan and canonical validator. `ReignXpTests` exercises multiplier boundaries and fractional amounts, disabled/unconfirmed settings, highest-skill selection, ties, participation weighting, replay after phase advancement, ledger serialization and earlier-save eligibility. New speech in an already rewarded phase still advances its conversation without reopening the bonus pool. Browser evidence covers the complete new Options control states, saves, failed saves, disabled retention and supported desktop/narrow layouts without a second listening server.

Native acceptance remains required for actual XP accounting with normal learning modifiers; individual/group/petition exchanges; social phase end and distinct participants; grant/refusal versus postponement; on/off and minimum/maximum multiplier; and save/reload without duplicate awards. Use only a user-selected baseline and a guarded disposable copy for automated advancement. The later Testing task is for the user's manual acceptance.

### Verified implementation and deployment — 2026-09-15

| Scope | Profile / tier | Result | Duration | Source fingerprint SHA-256 |
|---|---|---|---:|---|
| Complete client, shared rules, server, tests and catalog | changed / Release / Tier 3 | 621 tests passed, including 17 XP cases; Save Sync and Court boundary checks passed | 467.917 s | `d19456983d52e0b866e2a4bb2e4e0705d9cdfa2bf86e2e049f118a3c5095d311` |
| Final server Options save integration and navigation contract | changed / Release / Tier 2 | Server build and architecture verification passed, including 327/327 architecture assertions | 26.226 s | `3632b2961dee5d33ddb29394c6a34ec80c8ae179ef1b5afa226f13f93854db20` |

Reports live beneath `.codex-build/reign-mcp/validation/`: full run `20260915-171452-16e419e8`, final server run `20260915-173025-58dca5a9`. Each contains `validation-report.json` and `repository-hygiene-report.json`; enforced paired-repository hygiene passed with no issues. The full client build has one existing unrelated `PortraitPatch._announcedNoMatch` unused-field warning. The final server build has no warnings.

Only four server UI/test files changed after the full run. The native client and shared rules remained unchanged; the shared contract DLL hashes from both reports match exactly. `.codex-build/xp-rewards-20260915/incremental-validation-provenance.json` records component reuse. Deployment consumed the full run's client and the final run's server, with that shared-assembly equality enforced.

Final artifact-bound quick `contracts` run `verify-1789493807331-7e7c46b8` passed **107/107** checks in 182.601 s (183.464 s process duration), including the social-event contracts. Its report is under the final server artifact's `server/out/data/tests`. The first extra contract run failed only because the packaged source fixture omitted the client DLL required by `contracts.build_artifacts`. The rerun supplied the exact validated client DLL to that isolated fixture, without changing assertions or production files. The fallback and SHA-256 are recorded in `contract-fixture-receipt.json` in the XP evidence directory. The harness still requires this explicit fixture dependency when its source-only package is used.

The tracked browser runner passed **30 scenarios** at 800, 1280 and 1920 pixels, with **21 rendered screenshots** covering default/min/max/disabled/pending/error/global-save states. It exercised both save paths, reload, keyboard limits and layout without provider calls or production settings writes. Evidence: `.codex-build/xp-rewards-20260915/ui/browser-report.json` and its adjacent PNGs. Screenshots were visually inspected; no new native artwork or portrait geometry is involved.

Eight installed runtime/debug files were replaced from validated artifacts after confirming the server and Bannerlord were stopped. Exact prior files are retained in `.codex-build/xp-rewards-20260915/deployment-backup-20260915-173025-58dca5a9`. Targets are the current installed `ReignBeta/bin/Win64_Shipping_Client` in Bannerlord's Steam Modules directory and `D:/Reign/ReignServer/versions/0.1.0-preview.1-7d604476a974/app`. No installer payload was rebuilt for this local update.

| Installed runtime artifact | SHA-256 |
|---|---|
| ReignBeta.dll | `C0CDCE989C1D63D6778C53FF5FF04C5E73F831C4A0CB948E8458770AE76C7D61` |
| ReignBetaServer.exe | `5E05CFD3C5C7B87408FAE04E3460FFF64B2A6607AF2EF76ED616836C57AE7847` |
| Reign.Core.Contracts.dll, both components | `9A3B32E5212A6F8BCAF7CF089821BE4DBDF9C61FE622EE4B68C796D490CC955D` |

The supported installed visible shortcut started the server and its dedicated Control Center. Health confirms the unified lifetime group; the Control Center has a visible application window. The running executable path matches the installed target, all eight installed hashes match, and the real Options HTML and `/api/gameplay-options` return the expected controls and On/1x defaults. Four invalid settings submissions were rejected with the settings file unchanged. Existing settings were preserved during deployment. Exact evidence: `.codex-build/xp-rewards-20260915/deployment-receipt.json` and `runtime-proof.json`. No game or campaign was started.

After these checks, **Test Reign XP rewards** was created in the app's **Testing** section as a parked manual checklist (task `01a0a62a-10d3-7191-82d3-f805ad5f2404`). Native XP accounting, real conversations/petitions, server restart persistence and native save/reload acceptance remain for the user. Offline success does not close that requirement; the roadmap checkbox remains open. Runtime evidence, backups and rendered outputs remain local ignored artifacts; implementation, tests, catalog and durable notes are tracked source.

## Unstable integration — 2026-09-16

Paired changes are reviewed in [Reign #5](https://github.com/Dwemer-Dynamics/Reign/pull/5) and [ReignServer #5](https://github.com/Dwemer-Dynamics/ReignServer/pull/5), targeting `unstable`. The integration merges the retained client XP/portrait commits through `f4ee14d9` with client base `a71eb684`, and the retained server XP/drinking commits through `9519367b` with server base `78cdf2a0`. Both histories remain merge parents. The Options page and its browser harness now use the extracted `ReignServer/ui/index.html`; shared XP rules live under `ReignServer/shared/Reign.Core.Contracts`. The current Linux server layout and DwemerDistro behavior are preserved.

| Scope | Profile / tier | Result | Duration | Source fingerprint SHA-256 |
|---|---|---|---:|---|
| Combined client, server, contracts and tooling | changed / Release / Tier 3 | 634 MCP tests, 75 portrait-tool tests, 62 Save Sync and 170 Court assertions passed | 276.124 s | `b8242458878ad1547c482471d3bfc128b6dc80311e023b8a6a04894ab4021308` |
| Final server prompt-layering self-test correction | changed / Release / Tier 2 | Server build and 476 dialogue assertions passed | 50.444 s | `416b0356c1be60c3a17c7e224da4852863786c9c08ac8aeacc5c7b21d7e7f53d` |

Canonical reports are `.codex-build/reign-mcp/validation/20260916-164448-20b41629/validation-report.json` and `.codex-build/reign-mcp/validation/20260916-165800-3fe81f0d/validation-report.json`. Each adjacent `repository-hygiene-report.json` passes enforce-mode paired hygiene with zero issues. The only code change after the full run corrects a self-test to compare scoped, normalized prompt segments and assert World Tone for commoners as well as nobles; production prompt behavior is unchanged. The shared contract assembly is identical across both runs (SHA-256 `7156d48977675000f60ad48f4996615ae7a2d7fed8088d4a843492a6a2f60742`). Client and browser evidence are retained without rebuilding unchanged source.

Artifact-bound verification of the final server DLL (SHA-256 `747da237abc926bacbd1b97303b545ebe6a8225a2d316519fd96c5f79aec9e97`) passed:

- `conversation_intoxication`, quick, `verify-1789577976416-47a0f407`: 351/351 assertions, 25.623 s process duration.
- `prompt_efficiency`, quick, `verify-1789578003927-2265e292`: prompt-size check and 177/177 caching assertions, 47.673 s.
- `interaction_architecture`, offline, `verify-1789578124302-9d83786c`: 330/330 architecture assertions and the settlement-authority matrix, 87.735 s.
- XP Options browser matrix: 30/30 scenarios, three widths (800/1280/1920), 21 screenshots. Wide default and narrow failure states were visually inspected; controls, text and save feedback fit their bounds.

Local evidence is retained in `D:/ReignLaunch/unstable-integration-20260916`, including `component-validation-provenance.json`, `offline-summary.json`, per-suite MCP results, `xp-options-browser/browser-report.json`, screenshots and the redacted staged-path audit. No private runtime data, dependency downloads, reports or generated test files are publication inputs.

Recovery notes: three unchanged hash-bound UI text files retained Windows line endings after the branch switch; restoring their exact tracked LF bytes fixed the initial hash failures without changing asset definitions or expected hashes. The loaded MCP connector predated the Linux source layout, so validation used the canonical script and supplemental verification called the current MCP over stdio. One initial isolated PostgreSQL startup failure passed after a readiness check. The architecture suite requires the `offline` dispatcher; a `quick` request produced zero checks and was treated as a failure. The catalog's older quick/offline wording needs separate maintenance. Failed attempts remain in the local evidence directory.

This integration publishes source only. It does not deploy the Linux server or replace the installed client, run providers, alter player saves, establish a clean install on another machine, or complete native XP/portrait acceptance. The existing manual acceptance task and roadmap checkbox remain open.

## Independent XP category modifiers — 2026-10-07

Implemented the four settings listed above while retaining the universal multiplier. Native award sites pass explicit reasons; social Charm, phase-end skill bonuses and petition grants/refusals use their own categories. Conversation Charm covers individual, party/group and petition exchanges. Every category defaults to 1x, permits 0x–5x in quarter steps and combines with the universal setting. Old settings retain their reward amounts. Receipt eligibility, native learning modifiers, stable phase selection and the saved ledger/codec retain their existing behavior.

| Verification | Result | Duration | Fingerprint / run |
|---|---|---:|---|
| Canonical changed / Release / Tier 3 | 1,572 tests passed, including all 28 XP cases; persistence and court boundaries; paired hygiene passed | 517.256 s | `118de4c0ebbb50db6f98a9b7f7e045c8afed8ab0f562763084babb3291e29f03` / `20261007-171828-c479f273` |
| Artifact-bound offline `interaction_architecture` | 519/519 architecture assertions, including 16 XP settings assertions, plus settlement-authority check passed | 229.743 s process | `verify-1791394072319-24ab65a8`, bound to the preceding Release artifact |
| Canonical Linux component publish | Self-contained Linux artifact and paired hygiene passed | 69.108 s | `a747a96b99dea357d53b45b71f8d5b3e71780a10207d9e08c4de140d4e9f8cc8` / `20261007-173236-f5f5431d` |
| Isolated production Options browser matrix | 72 named scenario runs at 800/1280/1920 pixels; 42 screenshots; no script failures | — | Both save paths, independent category persistence, mixed rewards, failure and disabled retention, legacy defaults and keyboard bounds |

Canonical reports are `.codex-build/reign-mcp/validation/20261007-171828-c479f273/validation-report.json` and its `repository-hygiene-report.json`, plus `.codex-build/reign-mcp/linux-server/20261007-173236-f5f5431d/linux-build-report.json` (Linux hygiene is embedded). The first validation attempt `20261007-171709-878e5ae5` stopped at hygiene because the new authored self-test file was untracked; registering that exact file resolved it. No failed product assertion was suppressed.

Client output is retained under the successful validation run's `client/out`; Linux output is under the Linux run's `publish`, with `reign-linux-artifact.json`. The client, validation server and Linux shared-contract files have distinct hashes. A comparison of all 251 type/member declarations and 1,850 executable methods, constants, locals and exception-region offsets passed across the three assemblies. Deploy each component's own exact hash-bound contract file. This parity check does not replace native execution acceptance.

The task-owned release receipt, copied validation/hygiene reports, browser report/screenshots, offline verifier response and contract comparison are retained under `C:/Users/speed/Documents/Codex/2026-09-15/reign-xp-manual-testing/outputs`, headed by `XP_Category_Modifiers_Ready_2026-10-07.json`. Narrow combined-value and wide default screenshots were visually inspected. All verification used isolated state; no providers, live campaign changes or production settings writes were used.

At the initial build checkpoint, deployment awaited the user's stop of Bannerlord and the server. The user subsequently confirmed both were stopped and authorized deployment; the verified result follows. Native learning-rate and save/reload acceptance remain open.

### Deployment and production script-order correction — 2026-10-07

Deployed the two XP client DLLs from `20261007-171828-c479f273` over the verified full product baseline `20261007-162501-cccdacd8`. The full module content, portrait helper and Windows installation identity were preserved. Prior DLLs and the installation record are retained outside Bannerlord's `Modules` directory in `.reign-deployment/xp-before-20261007-171828-c479f273`.

The first server deployment's real Control Center page revealed `Cannot access 'floatIds' before initialization`, preventing navigation to Options. The isolated browser fixture had moved all field declarations before injected scripts and missed the production ordering. Moved category membership into the template's `floatIds` declaration, made the fixture preserve declaration/injection order, and added a named regression scenario. The final full served-page test opens Options, checks all five sliders and the enabled toggle, confirms the managed server reports on, and records no script errors. Read-only lifecycle status is allowed; provider requests and settings writes remain blocked.

| Final correction verification | Result | Duration | Fingerprint / run |
|---|---|---:|---|
| Canonical changed / Release / Tier 3 | 1,572 tests, including 28 XP cases; 113/113 quick verification checks; paired hygiene passed | 440.961 s | `cd7a15080296e80c9d63299377121a52d43c4946426d8578f7d6b16423479f9a` / `20261007-181216-5bee7061` |
| Canonical Linux component publish | Exact deployment artifact and paired hygiene passed | 30.804 s | `6fc7c8490705100bce92e1b87e8533323c1007fd33463a1f0a61dd8b6ad8c4f5` / `20261007-182004-b8b684fa` |
| Corrected isolated browser matrix | 75 named scenario runs at 800/1280/1920 pixels; 42 screenshots | — | Includes production field initialization order and both save paths |
| Deployed page and managed lifecycle | Options navigation and controls passed; server/owned worker stop and restart passed | — | `xp-deployed-options-ui.json`, `xp-corrected-runtime-stopped.json`, `xp-corrected-runtime-restarted.json` |

Final server activation uses `/var/www/html/ReignServer/runtime/current`, retained version `20261007182239-36777`. All 1,375 installed files and inventory match the final Linux manifest. Public port 8089 and internal server 5101 are healthy at version 0.1.0, database `reign`, schema 2/2. Worker 5102 has the loaded 384-dimensional model; a provider-free local inference probe passed with no current error and no persisted documents. Fresh startup logs contain no error/fatal entries. Private settings and Windows bridge hashes exactly match predeployment values and remain readable as the server's `reign` identity. XP remains enabled with universal/category multipliers all at 1x.

The final Linux shared-contract DLL retains the initial Linux hash, so the existing metadata/IL parity proof remains applicable to the retained client. Both components keep their own validated files. Source rollback copies are retained under `deployment-backups/xp-corrected-20261007-182004-b8b684fa-source`, and the earlier compatible server version remains available. A temporary rollback command was blocked by automatic approval review; it was not executed. The corrected activation completed through the canonical deployment script.

The authoritative task-owned deployment receipt is `C:/Users/speed/Documents/Codex/2026-09-15/reign-xp-manual-testing/outputs/XP_Category_Modifiers_Deployment_2026-10-07.json`, with copied corrected validation/hygiene reports, browser screenshots, exact installed hashes, runtime/worker/lifecycle/log probes and the first-deployment regression evidence. Narrow combined-value and actual served-page screenshots were visually inspected. No game was launched or campaign advanced. Native XP accounting and save/reload acceptance remain open; the roadmap checkbox stays unchecked.
