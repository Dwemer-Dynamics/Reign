# Dialogue marriage action fix

2026-09-09. Included with the Event Chat history/familiarity and petition culture bundle. Deployment remains separately authorized.

## Observed failure

The 06:54 and 06:56 UTC Michael/Zotyra turns explicitly accepted marriage, but their original and repaired hidden-planner actions all failed participant authority. Original candidates named clans/kingdoms without spouses. Repair arguments also mapped `source=hidden_action_planner` into `FromHero`, while broad clan/kingdom aliases could confuse actor and target. Nothing reached the native marriage eligibility check. The later 06:58 reply claimed wife/queen status with a `roleplay_only` gate; that prose did not establish a marriage.

Evidence: `.codex-build/marriage-action-diagnosis-20260909/evidence.json`. Correlations: `dialogue-reign_court_town_EW2_child_2-1788936807630-3013abf2`, `dialogue-reign_court_town_EW2_child_2-1788936925404-2a3c91fa`, and `dialogue-reign_court_town_EW2_child_2-1788937087998-ff8047eb`.

## Implemented contract

- The planner catalog identifies marriage as mechanical and requires both exact spouse IDs. Canonical arguments retain their original roles; metadata cannot supply a hero identity.
- Personal dialogue marriage requires the player and agreeing NPC in the live hero index. Missing, conflicting or third-party identities reject with a repairable reason. Native affiliations supply canonical actor/target fields. The existing consent receipt binds `personal_marriage_consent` and the exact spouse terms.
- The native executor resolves only those two living people and preserves native eligibility checks. It never substitutes an eligible relative. Reciprocal existing marriage is an idempotent success even after clan changes; it does not call `MarriageAction.Apply` again or rewrite hashed dialogue terms. Success still requires reciprocal native spouse links.
- The shared dialogue prompt makes native spouse state authoritative and tells the model that performing an agreed ceremony needs an action. Both individual and event/party replies surface queue failures without debug mode. A deliberately narrow guard replaces explicit unconfirmed first-person completion claims with pending/unconfirmed status and suppresses derived memory/state writes for that fabricated reply. Future, refused, conditional and quoted marriage discussion is retained; the guard does not claim general language understanding.

## Verification and limits

The existing Verification Lab quick/offline contracts run `contracts.dialogue_marriage`, including production normalization of the accepted pair and rejected legacy/conflicting/third-party shapes. Shared router tests preserve ordinary transfer direction; existing action and conversation suites remain required. `reign.testing.json` and `TESTING_TOOL_GUIDE.md` document the evidence and unchanged test gates.

Bundle validation also exposed identity-test fixture interference: the persisted day-50 court prompt adapter shared a campaign with the day-10 compact-roster checks. They now use separate unique test campaigns with the existing confined cleanup, and `court_audience_fixture_isolated` verifies that the court fixture leaves the roster campaign empty. The original same-clan/family and compact-storage assertions remain intact; production identity behavior is unchanged.

Final validation reports and bundle readiness are recorded under `.codex-build/marriage-dialogue-fix-20260909/`. Native marriage, eligibility, retry/save-reload and provider output are acceptance checks after authorized deployment using existing disposable-campaign/provider gates. This change does not replay old rejected actions, force a marriage in the normal campaign, or repair old narrative memories by inventing a completed wedding.
