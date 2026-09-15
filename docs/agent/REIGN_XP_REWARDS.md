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

The Control Center Options tab's Save XP options button saves only `reignXpEnabled` and `reignXpMultiplier` through the existing settings API. The global Save Settings button also includes the correctly typed XP values. Defaults are On and 1x. The slider accepts 0.25x through 5x in 0.25x steps, retains its value when disabled, and displays scaled base amounts. Invalid incoming values are rejected before settings writes. Existing server settings and secrets are preserved by the existing partial merge.

`GET /api/gameplay-options` exposes only success and those two gameplay values. The client refreshes before relevant interactions and phase completion with a five-second transport timeout per candidate endpoint. It retains the last confirmed configuration on failure; before any confirmed configuration it grants nothing. Settings are global to the selected server and require no restart. An in-flight turn uses its captured settings. No missed rewards are queued for later when options are disabled or unavailable.

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
